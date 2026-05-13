using AtlasBalance.API.Constants;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasBalance.API.Data;

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
        var seedActorId = EnsureSeedAdmin(context, now, configuration, isDevelopment);

        EnsureDefaultDivisas(context);
        EnsureDefaultTiposCambio(context, now);
        EnsureDefaultConfiguraciones(context, seedActorId, now);

        return seedActorId;
    }

    private static Guid? EnsureSeedAdmin(AppDbContext context, DateTime now, IConfiguration? configuration, bool isDevelopment)
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
            PuedeUsarIa = true,
            FechaCreacion = now,
            SecurityStamp = UserSessionState.CreateSecurityStamp(),
            PasswordChangedAt = now
        });

        return adminId;
    }

    private static void EnsureDefaultDivisas(AppDbContext context)
    {
        var divisas = new[]
        {
            new DivisaActiva { Codigo = "EUR", Nombre = "Euro", Simbolo = "\u20AC", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Nombre = "Dolar Estadounidense", Simbolo = "$", Activa = true, EsBase = false },
            new DivisaActiva { Codigo = "MXN", Nombre = "Peso Mexicano", Simbolo = "MX$", Activa = true, EsBase = false },
            new DivisaActiva { Codigo = "DOP", Nombre = "Peso Dominicano", Simbolo = "RD$", Activa = true, EsBase = false }
        };

        foreach (var divisa in divisas)
        {
            if (context.DivisasActivas.Any(d => d.Codigo == divisa.Codigo))
            {
                continue;
            }

            context.DivisasActivas.Add(divisa);
        }
    }

    private static void EnsureDefaultTiposCambio(AppDbContext context, DateTime now)
    {
        var tiposCambio = new[]
        {
            new TipoCambio { Id = Guid.NewGuid(), DivisaOrigen = "EUR", DivisaDestino = "USD", Tasa = 1.08m, FechaActualizacion = now, Fuente = FuenteTipoCambio.MANUAL },
            new TipoCambio { Id = Guid.NewGuid(), DivisaOrigen = "EUR", DivisaDestino = "MXN", Tasa = 18.25m, FechaActualizacion = now, Fuente = FuenteTipoCambio.MANUAL },
            new TipoCambio { Id = Guid.NewGuid(), DivisaOrigen = "EUR", DivisaDestino = "DOP", Tasa = 69.15m, FechaActualizacion = now, Fuente = FuenteTipoCambio.MANUAL }
        };

        foreach (var tipoCambio in tiposCambio)
        {
            if (context.TiposCambio.Any(t =>
                    t.DivisaOrigen == tipoCambio.DivisaOrigen &&
                    t.DivisaDestino == tipoCambio.DivisaDestino))
            {
                continue;
            }

            context.TiposCambio.Add(tipoCambio);
        }
    }

    private static void EnsureDefaultConfiguraciones(AppDbContext context, Guid? seedActorId, DateTime now)
    {
        var configuraciones = new Dictionary<string, (string Valor, string Tipo, string Descripcion)>
        {
            ["app_base_url"] = ("https://caja.empresa.local", "string", "URL base de la aplicacion"),
            ["saldo_minimo_global"] = ("0", "decimal", "Saldo minimo global para alertas"),
            ["exchange_rate_sync_hours"] = ("12", "int", "Horas entre sincronizaciones de tipos de cambio"),
            ["backup_retention_weeks"] = ("6", "int", "Semanas de retencion de backups"),
            ["backup_path"] = ("C:/AtlasBalance/backups", "string", "Ruta de almacenamiento de backups"),
            ["export_path"] = ("C:/AtlasBalance/exports", "string", "Ruta de exportaciones"),
            ["app_version"] = ("V-01.06", "string", "Version instalada"),
            ["app_update_check_url"] = (ConfigurationDefaults.UpdateCheckUrl, "string", "Repositorio oficial de GitHub para actualizaciones"),
            ["smtp_host"] = ("", "string", "Host SMTP"),
            ["smtp_port"] = ("587", "int", "Puerto SMTP"),
            ["smtp_user"] = ("", "string", "Usuario SMTP"),
            ["smtp_password"] = ("", "string", "Password SMTP cifrado"),
            ["smtp_from"] = ("noreply@empresa.com", "string", "Remitente SMTP"),
            ["exchange_rate_api_key"] = ("", "string", "Clave API para sincronizacion de tipos de cambio"),
            ["divisa_principal_default"] = ("EUR", "string", "Divisa principal para dashboards"),
            ["dashboard_color_ingresos"] = ("#43B430", "string", "Color linea ingresos dashboard"),
            ["dashboard_color_egresos"] = ("#FF4757", "string", "Color linea egresos dashboard"),
            ["dashboard_color_saldo"] = ("#7B7B7B", "string", "Color linea saldo dashboard"),
            ["revision_comisiones_importe_minimo"] = ("1", "decimal", "Importe minimo para mostrar comisiones en revision"),
            ["alerta_saldo_cooldown_horas"] = ("24", "int", "Horas minimas entre emails duplicados de saldo bajo"),
            ["ai_enabled"] = ("false", "bool", "Interruptor global de IA financiera"),
            ["ai_provider"] = ("OPENROUTER", "string", "Proveedor de IA financiera"),
            ["ai_model"] = ("", "string", "Modelo de IA seleccionado"),
            ["openrouter_api_key"] = ("", "string", "Clave API de OpenRouter protegida"),
            ["openai_api_key"] = ("", "string", "Clave API de OpenAI protegida"),
            ["ai_requests_per_minute"] = ("6", "int", "Consultas maximas de IA por usuario y minuto"),
            ["ai_requests_per_hour"] = ("30", "int", "Consultas maximas de IA por usuario y hora"),
            ["ai_requests_per_day"] = ("60", "int", "Consultas maximas de IA por usuario y dia"),
            ["ai_global_requests_per_day"] = ("300", "int", "Consultas maximas globales de IA por dia"),
            ["ai_monthly_budget_eur"] = ("0", "decimal", "Presupuesto mensual estimado de IA en EUR; 0 desactiva bloqueo por coste"),
            ["ai_user_monthly_budget_eur"] = ("0", "decimal", "Presupuesto mensual estimado de IA por usuario en EUR; 0 desactiva bloqueo por coste individual"),
            ["ai_total_budget_eur"] = ("0", "decimal", "Presupuesto total estimado de IA en EUR; 0 desactiva bloqueo por coste"),
            ["ai_budget_warning_percent"] = ("80", "int", "Porcentaje de presupuesto para mostrar aviso de IA"),
            ["ai_input_cost_per_1m_tokens_eur"] = ("0", "decimal", "Coste estimado de entrada por millon de tokens"),
            ["ai_output_cost_per_1m_tokens_eur"] = ("0", "decimal", "Coste estimado de salida por millon de tokens"),
            ["ai_max_input_tokens"] = ("6000", "int", "Tokens maximos aproximados de contexto por consulta IA"),
            ["ai_max_output_tokens"] = ("700", "int", "Tokens maximos de respuesta por consulta IA"),
            ["ai_max_context_rows"] = ("80", "int", "Movimientos relevantes maximos enviados a IA"),
            ["ai_usage_month_key"] = ("", "string", "Mes contable actual de uso IA"),
            ["ai_usage_month_cost_eur"] = ("0", "decimal", "Coste estimado de IA acumulado en el mes actual"),
            ["ai_usage_total_cost_eur"] = ("0", "decimal", "Coste estimado total acumulado de IA"),
            ["ai_usage_total_requests"] = ("0", "int", "Consultas totales de IA registradas"),
            ["ai_usage_last_user_id"] = ("", "string", "Ultimo usuario que uso IA"),
            ["ai_usage_last_at_utc"] = ("", "datetime", "Ultimo uso de IA en UTC"),
            ["integration_rate_limit_per_minute"] = ("100", "int", "Rate limit por token de integracion")
        };

        foreach (var (clave, item) in configuraciones)
        {
            if (context.Configuraciones.Any(c => c.Clave == clave))
            {
                continue;
            }

            context.Configuraciones.Add(new Configuracion
            {
                Clave = clave,
                Valor = item.Valor,
                Tipo = item.Tipo,
                Descripcion = item.Descripcion,
                FechaModificacion = now,
                UsuarioModificacionId = seedActorId
            });
        }
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

        if (!isDevelopment && LooksLikePlaceholder(configuredPassword))
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
            var defaultId = Guid.Parse(formato.Id);
            var existsById = context.FormatosImportacion
                .IgnoreQueryFilters()
                .Any(f => f.Id == defaultId);

            if (existsById)
            {
                continue;
            }

            var existsByBankAndCurrency = context.FormatosImportacion
                .IgnoreQueryFilters()
                .Any(f =>
                    f.BancoNombre != null &&
                    f.BancoNombre.ToLower() == formato.BancoNombre.ToLower() &&
                    (f.Divisa ?? string.Empty).ToUpper() == formato.Divisa);

            if (existsByBankAndCurrency)
            {
                continue;
            }

            context.FormatosImportacion.Add(new FormatoImportacion
            {
                Id = defaultId,
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
