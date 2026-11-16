using AtlasBalance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Services;

public interface IAccountLockoutNotifier
{
    /// <summary>
    /// Avisa por correo al titular de que su cuenta acaba de bloquearse. Se llama
    /// solo en la transicion a bloqueado. Nunca lanza ni espera al envio.
    /// </summary>
    void NotifyLocked(Guid userId, DateTime lockedAtUtc, TimeSpan lockDuration, string? ipAddress);
}

/// <summary>
/// Envio en segundo plano del aviso de bloqueo. Va fuera de la peticion porque el
/// login iguala su latencia con un BCrypt senuelo (V-02.07) y un SMTP inline la
/// delataria; y con su propio scope porque el del request se libera al terminar.
/// Cualquier fallo (SMTP, destinatarios) se registra y se descarta: avisar es
/// secundario frente a responder el login.
/// </summary>
public sealed class AccountLockoutNotifier : IAccountLockoutNotifier
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountLockoutNotifier> _logger;
    private readonly Func<Func<Task>, Task> _dispatch;

    public AccountLockoutNotifier(IServiceScopeFactory scopeFactory, ILogger<AccountLockoutNotifier> logger)
        : this(scopeFactory, logger, work => Task.Run(work))
    {
    }

    // Los tests inyectan un dispatch sincrono (work => work()) para poder esperar el envio.
    internal AccountLockoutNotifier(
        IServiceScopeFactory scopeFactory,
        ILogger<AccountLockoutNotifier> logger,
        Func<Func<Task>, Task> dispatch)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _dispatch = dispatch;
    }

    public void NotifyLocked(Guid userId, DateTime lockedAtUtc, TimeSpan lockDuration, string? ipAddress)
    {
        var ipSummary = AuthService.SummarizeIp(ipAddress);
        var lockMinutes = Math.Max(1, (int)Math.Round(lockDuration.TotalMinutes));
        try
        {
            _ = _dispatch(() => SendAsync(userId, lockedAtUtc, lockMinutes, ipSummary));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("No se pudo encolar el aviso de bloqueo de cuenta ({ErrorType}).", ex.GetType().Name);
        }
    }

    private async Task SendAsync(Guid userId, DateTime lockedAtUtc, int lockMinutes, string? ipSummary)
    {
        try
        {
            // Sin HttpContext el interceptor RLS deniega: contexto de sistema explicito.
            using var _rlsScope = RlsDbCommandInterceptor.SystemContextScope.Enter();
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ct = cts.Token;

            var smtpHost = await db.Configuraciones
                .AsNoTracking()
                .Where(x => x.Clave == "smtp_host")
                .Select(x => x.Valor)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(smtpHost))
            {
                _logger.LogDebug("SMTP no configurado: se omite el aviso de bloqueo de cuenta.");
                return;
            }

            var principal = await db.Usuarios
                .AsNoTracking()
                .Where(u => u.Id == userId && u.Activo)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
            if (principal is null)
            {
                return;
            }

            var adicionales = await db.UsuarioEmails
                .AsNoTracking()
                .Where(e => e.UsuarioId == userId)
                .Select(e => e.Email)
                .ToListAsync(ct);

            var recipients = adicionales
                .Append(principal)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (recipients.Count == 0)
            {
                return;
            }

            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            await email.SendAccountLockedAsync(recipients, lockedAtUtc, lockMinutes, ipSummary, ct);
        }
        catch (Exception ex)
        {
            // Solo el tipo: el mensaje de MailKit/SMTP puede incluir direcciones.
            _logger.LogWarning(
                "No se pudo enviar el aviso de bloqueo de cuenta del usuario {UserId} ({ErrorType}).",
                userId,
                ex.GetType().Name);
        }
    }
}
