using System.Data.Common;
using System.Security.Claims;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtlasBalance.API.Data;

// V-02-06 (RLS-SEC-01): el secreto RLS ahora se inyecta por DI desde
// Program.cs despues de validarlo. Esto elimina la lectura independiente de
// IConfiguration y la inconsistencia con JWT que provocaba que un secreto
// en blanco ("" o solo espacios) generara firmas invalidas en runtime.
public sealed class RlsDbCommandInterceptor : DbCommandInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _contextSecret;

    // El tipo de parametro RlsContextSecret es internal, asi que el ctor
    // tambien debe serlo para no disparar CS0051. La clase sigue siendo
    // public sealed para que EF Core la descubra como interceptor y la DI
    // interna la construya por reflection (las llamadas AddScoped son en
    // este ensamblado).
    internal RlsDbCommandInterceptor(IHttpContextAccessor httpContextAccessor, RlsContextSecret contextSecret)
    {
        _httpContextAccessor = httpContextAccessor;
        _contextSecret = contextSecret.Secret;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ApplyRlsContext(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyRlsContextAsync(command, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyRlsContext(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyRlsContextAsync(command, cancellationToken);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        ApplyRlsContext(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyRlsContextAsync(command, cancellationToken);
        return result;
    }

    private void ApplyRlsContext(DbCommand command)
    {
        if (ShouldSkip(command))
        {
            return;
        }

        ReentryGuard.Enter();
        try
        {
            var context = BuildContext();
            using var contextCommand = CreateContextCommand(command, context, _contextSecret);
            contextCommand.ExecuteNonQuery();
        }
        finally
        {
            ReentryGuard.Exit();
        }
    }

    private async Task ApplyRlsContextAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (ShouldSkip(command))
        {
            return;
        }

        ReentryGuard.Enter();
        try
        {
            var context = BuildContext();
            await using var contextCommand = CreateContextCommand(command, context, _contextSecret);
            await contextCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            ReentryGuard.Exit();
        }
    }

    private static bool ShouldSkip(DbCommand command) =>
        command.Connection is null ||
        command.Connection.State != System.Data.ConnectionState.Open ||
        // V-02-06 (MED-30): si el comando se origina dentro del propio
        // interceptor (publicacion de set_config del contexto RLS),
        // saltamos. Antes se hacia por busqueda textual en CommandText.
        ReentryGuard.IsActive;

    /// <summary>
    /// V-02-06 (MED-30 + PR F1): marcador de reentrada que fluye por
    /// `AsyncLocal` para sobrevivir a continuaciones de `await`. La version
    /// previa usaba `[ThreadStatic]`, que no viaja con la continuacion y
    /// deja el contador incoherente entre hilos (puede mantenerse positivo
    /// en un hilo y decrementarse en otro). `AsyncLocal&lt;int&gt;` mantiene
    /// el contador anidado por contexto logico.
    /// </summary>
    private static class ReentryGuard
    {
        private static readonly AsyncLocal<int> _depth = new();

        public static bool IsActive => _depth.Value > 0;

        public static void Enter() => _depth.Value++;

        public static void Exit() => _depth.Value--;
    }

    // V-02-06 (RLS-UNIT-01): exponemos BuildContext como internal para que los
    // tests unitarios puedan verificar el scope derivado del path sin tener
    // que instanciar un DbCommand real (lo que exigiria un AppDbContext
    // conectado a PostgreSQL).
    internal RlsSessionContext BuildContext()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return RlsSessionContext.System();
        }

        if (httpContext.Items.TryGetValue(IntegrationHttpContextItemKeys.CurrentIntegrationToken, out var tokenValue) &&
            tokenValue is IntegrationToken integrationToken)
        {
            return RlsSessionContext.Integration(integrationToken.Id);
        }

        if (httpContext.User.Identity?.IsAuthenticated == true &&
            TryGetUserId(httpContext.User, out var userId))
        {
            var isAdmin = httpContext.User.IsInRole(nameof(RolUsuario.ADMIN));
            var path = httpContext.Request.Path;
            var isReadMethod =
                string.Equals(httpContext.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(httpContext.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(httpContext.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase);
            var scope = path.StartsWithSegments("/api/dashboard", StringComparison.OrdinalIgnoreCase)
                ? "dashboard"
                : path.StartsWithSegments("/api/exportaciones", StringComparison.OrdinalIgnoreCase)
                    ? "export"
                    : path.StartsWithSegments("/api/revision", StringComparison.OrdinalIgnoreCase)
                        ? "revision"
                        : isReadMethod
                            ? "data"
                            : "write";

            return RlsSessionContext.User(userId, isAdmin, scope);
        }

        if (httpContext.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            return RlsSessionContext.AuthFlow();
        }

        return RlsSessionContext.Anonymous();
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

    private static DbCommand CreateContextCommand(DbCommand sourceCommand, RlsSessionContext context, string contextSecret)
    {
        var contextCommand = sourceCommand.Connection!.CreateCommand();
        contextCommand.Transaction = sourceCommand.Transaction;
        var isAdmin = context.IsAdmin ? "true" : "false";
        var system = context.IsSystem ? "true" : "false";
        var signature = RlsContextSigner.Sign(
            contextSecret,
            context.AuthMode,
            context.UserId,
            context.IntegrationTokenId,
            isAdmin,
            system,
            context.RequestScope);

        contextCommand.CommandText = """
            SELECT
                set_config('atlas.auth_mode', @auth_mode, false),
                set_config('atlas.user_id', @user_id, false),
                set_config('atlas.integration_token_id', @integration_token_id, false),
                set_config('atlas.is_admin', @is_admin, false),
                set_config('atlas.system', @system, false),
                set_config('atlas.request_scope', @request_scope, false),
                set_config('atlas.context_signature', @context_signature, false)
            """;

        AddParameter(contextCommand, "@auth_mode", context.AuthMode);
        AddParameter(contextCommand, "@user_id", context.UserId);
        AddParameter(contextCommand, "@integration_token_id", context.IntegrationTokenId);
        AddParameter(contextCommand, "@is_admin", isAdmin);
        AddParameter(contextCommand, "@system", system);
        AddParameter(contextCommand, "@request_scope", context.RequestScope);
        AddParameter(contextCommand, "@context_signature", signature);
        return contextCommand;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    internal readonly record struct RlsSessionContext(
        string AuthMode,
        string UserId,
        string IntegrationTokenId,
        bool IsAdmin,
        bool IsSystem,
        string RequestScope)
    {
        public static RlsSessionContext System() => new("system", string.Empty, string.Empty, true, true, "system");
        public static RlsSessionContext AuthFlow() => new("auth", string.Empty, string.Empty, false, false, "auth");
        public static RlsSessionContext Anonymous() => new("anonymous", string.Empty, string.Empty, false, false, "anonymous");
        public static RlsSessionContext User(Guid userId, bool isAdmin, string requestScope) => new("user", userId.ToString(), string.Empty, isAdmin, false, requestScope);
        public static RlsSessionContext Integration(Guid tokenId) => new("integration", string.Empty, tokenId.ToString(), false, false, "integration");
    }
}
