using GestionCaja.API.Models;
using GestionCaja.API.Constants;
using GestionCaja.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Data;

public static class SeedData
{
    private const string DefaultAdminEmail = "admin@atlasbalance.local";

    public static void Initialize(AppDbContext context, IConfiguration? configuration = null, IHostEnvironment? environment = null)
    {
        var now = DateTime.UtcNow;
        var seedActorId = EnsureInitialData(context, now, configuration, environment?.IsDevelopment() ?? true);

        EnsureDefaultFormatosImportacion(context, seedActorId, now);

        context.SaveChanges();
    }

    private static Guid? EnsureInitialData(AppDbContext context, DateTime now, IConfiguration? configuration, bool isDevelopment)
    {
        if (context.Usuarios.Any())
        {
            return context.Usuarios
                .Where(u => u.Rol == RolUsuario.ADMIN)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefault()
                ?? context.Usuarios.Select(u => (Guid?)u.Id).FirstOrDefault();
        }

        var adminId = Guid.NewGuid();
        var adminEmail = ResolveSeedAdminEmail(configuration);
        var adminPassword = ResolveSeedAdminPassword(configuration, isDevelopment);

        context.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = adminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, workFactor: 12),
            NombreCompleto = "Administrador",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = true,
            FechaCreacion = now,
            SecurityStamp = UserSessionState.CreateSecurityStamp(),
            PasswordChangedAt = now
        });

        context.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Nombre = "Euro", Simbolo = "€", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Nombre = "Dólar Estadounidense", Simbolo = "$", Activa = true, EsBase = false },
            new DivisaActiva { Codigo = "MXN", Nombre = "Peso Mexicano", Simbolo = "MX$", Activa = true, EsBase = false },
            new DivisaActiva { Codigo = "DOP", Nombre = "Peso Dominicano", Simbolo = "RD$", Activa = true, EsBase = false }
        );

        context.TiposCambio.AddRange(
            new TipoCambio { Id = Guid.NewGuid(), DivisaOrigen = "EUR", DivisaDestino = "USD", Tasa = 1.08m, FechaActualizacion = now, Fuente = FuenteTipoCambio.MANUAL },
            new TipoCambio { Id = Guid.NewGuid(), DivisaOrigen = "EUR", DivisaDestino = "MXN", Tasa = 18.25m, FechaActualizacion = now, Fuente = FuenteTipoCambio.MANUAL },
            new TipoCambio { Id = Guid.NewGuid(), DivisaOrigen = "EUR", DivisaDestino = "DOP", Tasa = 69.15m, FechaActualizacion = now, Fuente = FuenteTipoCambio.MANUAL }
        );

        var configuraciones = new Dictionary<string, (string Valor, string Tipo, string Descripcion)>
        {
            ["app_base_url"] = ("https://caja.empresa.local", "string", "URL base de la aplicación"),
            ["saldo_minimo_global"] = ("0", "decimal", "Saldo mínimo global para alertas"),
            ["exchange_rate_sync_hours"] = ("12", "int", "Horas entre sincronizaciones de tipos de cambio"),
            ["backup_retention_weeks"] = ("6", "int", "Semanas de retención de backups"),
            ["backup_path"] = ("C:/AtlasBalance/backups", "string", "Ruta de almacenamiento de backups"),
            ["export_path"] = ("C:/AtlasBalance/exports", "string", "Ruta de exportaciones"),
            ["app_version"] = ("V-01.03", "string", "Versión instalada"),
            ["app_update_check_url"] = (ConfigurationDefaults.UpdateCheckUrl, "string", "URL del servidor de actualizaciones"),
            ["smtp_host"] = ("", "string", "Host SMTP"),
            ["smtp_port"] = ("587", "int", "Puerto SMTP"),
            ["smtp_user"] = ("", "string", "Usuario SMTP"),
            ["smtp_password"] = ("", "string", "Password SMTP (cifrado en fases siguientes)"),
            ["smtp_from"] = ("noreply@empresa.com", "string", "Remitente SMTP"),
            ["exchange_rate_api_key"] = ("", "string", "API key para sincronizacion de tipos de cambio"),
            ["divisa_principal_default"] = ("EUR", "string", "Divisa principal para dashboards"),
            ["dashboard_color_ingresos"] = ("#43B430", "string", "Color línea ingresos dashboard"),
            ["dashboard_color_egresos"] = ("#FF4757", "string", "Color línea egresos dashboard"),
            ["dashboard_color_saldo"] = ("#7B7B7B", "string", "Color línea saldo dashboard"),
            ["integration_rate_limit_per_minute"] = ("100", "int", "Rate limit por token de integración")
        };

        foreach (var (clave, item) in configuraciones)
        {
            context.Configuraciones.Add(new Configuracion
            {
                Clave = clave,
                Valor = item.Valor,
                Tipo = item.Tipo,
                Descripcion = item.Descripcion,
                FechaModificacion = now,
                UsuarioModificacionId = adminId
            });
        }

        return adminId;
    }

    private static string ResolveSeedAdminEmail(IConfiguration? configuration)
    {
        var configuredEmail = configuration?["SeedAdmin:Email"]?.Trim();
        return string.IsNullOrWhiteSpace(configuredEmail)
            ? DefaultAdminEmail
            : configuredEmail;
    }

    private static string ResolveSeedAdminPassword(IConfiguration? configuration, bool isDevelopment)
    {
        var configuredPassword = configuration?["SeedAdmin:Password"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            throw new InvalidOperationException("SeedAdmin:Password must be configured before first startup.");
        }

        if (!SecurityPolicy.TryValidatePassword(configuredPassword, out var passwordError))
        {
            throw new InvalidOperationException($"SeedAdmin:Password is not valid: {passwordError}.");
        }

        if (!isDevelopment &&
            LooksLikePlaceholder(configuredPassword))
        {
            throw new InvalidOperationException("SeedAdmin:Password must be a real non-default production password.");
        }

        return configuredPassword;
    }

    private static bool LooksLikePlaceholder(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("change", StringComparison.Ordinal) ||
               normalized.Contains("cambiar", StringComparison.Ordinal) ||
               normalized.Contains("generar", StringComparison.Ordinal) ||
               normalized.Contains("placeholder", StringComparison.Ordinal) ||
               normalized.Contains("aqui", StringComparison.Ordinal);
    }

    private static void EnsureDefaultFormatosImportacion(AppDbContext context, Guid? seedActorId, DateTime now)
    {
        foreach (var formato in DefaultFormatosImportacion)
        {
            var exists = context.FormatosImportacion
                .IgnoreQueryFilters()
                .Any(f =>
                    f.BancoNombre != null &&
                    f.BancoNombre.ToLower() == formato.BancoNombre.ToLower() &&
                    (f.Divisa ?? string.Empty).ToUpper() == formato.Divisa);

            if (exists)
            {
                continue;
            }

            context.FormatosImportacion.Add(new FormatoImportacion
            {
                Id = Guid.Parse(formato.Id),
                Nombre = formato.Nombre,
                BancoNombre = formato.BancoNombre,
                Divisa = formato.Divisa,
                MapeoJson = formato.MapeoJson,
                UsuarioCreadorId = seedActorId,
                FechaCreacion = now,
                Activo = true
            });
        }
    }

    private static readonly IReadOnlyList<DefaultFormatoImportacion> DefaultFormatosImportacion =
    [
        new(
            "e1b2cba0-60bd-4854-9b24-d2e88763fa5d",
            "Sabadell",
            "Sabadell",
            "EUR",
            """
            {"tipo_monto":"una_columna","fecha":0,"concepto":1,"monto":3,"saldo":4,"columnas_extra":[{"nombre":"Fecha Valor","indice":2},{"nombre":"Desglose","indice":5},{"nombre":"Documento","indice":6},{"nombre":"Cuenta","indice":7},{"nombre":"Comentario","indice":8},{"nombre":"Columna","indice":9}]}
            """),
        new(
            "b93a72f5-f2b1-4f7d-b1a6-661dac305696",
            "BBVA",
            "BBVA",
            "EUR",
            """
            {"tipo_monto":"una_columna","fecha":0,"concepto":3,"monto":6,"saldo":7,"columnas_extra":[{"nombre":"Fecha Valor","indice":1},{"nombre":"Codigo","indice":2},{"nombre":"Observaciones 1","indice":4},{"nombre":"Observaciones 2","indice":5},{"nombre":"Desglose","indice":8},{"nombre":"Documento","indice":9},{"nombre":"Cuenta","indice":10}]}
            """),
        new(
            "8d7bd2be-834b-4222-845b-94f12bd450a5",
            "Banquinter",
            "Banquinter",
            "EUR",
            """
            {"tipo_monto":"una_columna","fecha":0,"concepto":4,"monto":8,"saldo":9,"columnas_extra":[{"nombre":"Fecha Valor","indice":1},{"nombre":"Clave","indice":2},{"nombre":"Referencia","indice":3},{"nombre":"Descripcion","indice":5}]}
            """),
        new(
            "4d0bbbf2-03a0-4f22-887e-3eb6d1a5730a",
            "BBVA",
            "BBVA",
            "MXN",
            """
            {"tipo_monto":"dos_columnas","fecha":0,"concepto":1,"ingreso":3,"egreso":2,"saldo":4}
            """),
        new(
            "e1789b1e-aa3a-40a3-b0e4-a1060eb208a0",
            "Banco Caribe",
            "Banco Caribe",
            "DOP",
            """
            {"tipo_monto":"dos_columnas","fecha":0,"concepto":1,"ingreso":4,"egreso":3,"saldo":5,"columnas_extra":[{"nombre":"Cheque","indice":2}]}
            """),
        new(
            "2f4f4189-ab4c-4ee6-bc02-08ff2229660f",
            "Banco Caribe",
            "Banco Caribe",
            "USD",
            """
            {"tipo_monto":"dos_columnas","fecha":0,"concepto":1,"ingreso":4,"egreso":3,"saldo":5,"columnas_extra":[{"nombre":"Cheque","indice":2}]}
            """),
        new(
            "841fd198-fb75-4a75-8773-d139c4f3d095",
            "Banco Popular",
            "Banco Popular",
            "DOP",
            """
            {"tipo_monto":"una_columna","fecha":0,"concepto":4,"monto":5,"saldo":6,"columnas_extra":[{"nombre":"Fecha Efectiva","indice":1},{"nombre":"Nro. cheque","indice":2},{"nombre":"Nro Referencia","indice":3}]}
            """),
        new(
            "5b4ba06c-a56e-44c0-9422-352117394a96",
            "Banco Popular",
            "Banco Popular",
            "USD",
            """
            {"tipo_monto":"una_columna","fecha":0,"concepto":4,"monto":5,"saldo":6,"columnas_extra":[{"nombre":"Fecha efectiva","indice":1},{"nombre":"Nro. cheque","indice":2},{"nombre":"Nro. referencia","indice":3}]}
            """)
    ];

    private sealed record DefaultFormatoImportacion(
        string Id,
        string Nombre,
        string BancoNombre,
        string Divisa,
        string MapeoJson);
}
