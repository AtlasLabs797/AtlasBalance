using AtlasBalance.API.Constants;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq.Expressions;

namespace AtlasBalance.API.Data;

public static class SeedData
{
    private const string DefaultAdminEmail = "admin@atlasbalance.local";

    public static void Initialize(AppDbContext context, IConfiguration? configuration = null, IHostEnvironment? environment = null)
    {
        var now = DateTime.UtcNow;
        var isDevelopment = environment?.IsDevelopment() ?? true;
        var seedActorId = EnsureInitialData(context, now, configuration, isDevelopment);

        EnsureDefaultFormatosImportacion(context, seedActorId, now);
        EnsureDemoData(context, seedActorId, now, configuration, isDevelopment);

        context.SaveChanges();
    }

    private static Guid? EnsureInitialData(AppDbContext context, DateTime now, IConfiguration? configuration, bool isDevelopment)
    {
        var seedActorId = EnsureSeedAdmin(context, now, configuration, isDevelopment);

        EnsureDefaultDivisas(context);
        EnsureDefaultTiposCambio(context, now);
        EnsureDefaultConfiguraciones(context, seedActorId, now, configuration);

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

    private static void EnsureDefaultConfiguraciones(AppDbContext context, Guid? seedActorId, DateTime now, IConfiguration? configuration)
    {
        var appBaseUrl = ResolveConfiguredAppBaseUrl(configuration) ?? "https://caja.empresa.local";
        var configuraciones = new Dictionary<string, (string Valor, string Tipo, string Descripcion)>
        {
            ["app_base_url"] = (appBaseUrl, "string", "URL base de la aplicacion"),
            ["saldo_minimo_global"] = ("0", "decimal", "Saldo minimo global para alertas"),
            ["exchange_rate_sync_hours"] = ("12", "int", "Horas entre sincronizaciones de tipos de cambio"),
            ["backup_retention_weeks"] = ("6", "int", "Semanas de retencion de backups"),
            ["backup_path"] = ("C:/AtlasBalance/backups", "string", "Ruta de almacenamiento de backups"),
            ["backup_auto_enabled"] = ("true", "bool", "Activa copias de seguridad automaticas"),
            ["backup_auto_frequency"] = ("WEEKLY", "string", "Frecuencia de copias automaticas"),
            ["backup_auto_time_utc"] = ("02:00", "string", "Hora UTC de copia automatica"),
            ["backup_auto_day_of_week"] = ("0", "int", "Dia de semana UTC de copia automatica; 0 domingo"),
            ["backup_auto_day_of_month"] = ("1", "int", "Dia de mes UTC de copia automatica"),
            ["backup_auto_interval_hours"] = ("24", "int", "Intervalo horario para copias automaticas"),
            ["backup_auto_last_started_utc"] = ("", "datetime", "Ultima copia automatica iniciada"),
            ["backup_auto_last_result"] = ("", "string", "Ultimo resultado de copia automatica"),
            ["backup_destination"] = ("LOCAL", "string", "Destino de backups: LOCAL o LOCAL_Y_GOOGLE_DRIVE"),
            ["google_drive_oauth_client_id"] = ("", "string", "OAuth Client ID para Google Drive"),
            ["google_drive_oauth_client_secret"] = ("", "string", "OAuth Client Secret para Google Drive protegido"),
            ["google_drive_folder_id"] = ("", "string", "Carpeta de Google Drive para backups"),
            ["backup_cloud_encryption_key"] = ("", "string", "Clave protegida de cifrado para backups en nube"),
            ["export_path"] = ("C:/AtlasBalance/exports", "string", "Ruta de exportaciones"),
            ["app_version"] = ("V-02-04", "string", "Version instalada"),
            ["app_update_check_url"] = (ConfigurationDefaults.UpdateCheckUrl, "string", "Repositorio oficial de GitHub para actualizaciones"),
            ["app_update_auto_enabled"] = ("false", "bool", "Aplicar automaticamente releases firmados de GitHub"),
            ["app_update_auto_hour_utc"] = ("3", "int", "Hora UTC minima para la comprobacion automatica diaria"),
            ["app_update_auto_last_checked_utc"] = ("", "datetime", "Ultima comprobacion automatica de actualizaciones en UTC"),
            ["app_update_auto_last_started_utc"] = ("", "datetime", "Ultima actualizacion automatica iniciada en UTC"),
            ["app_update_auto_last_result"] = ("", "string", "Ultimo resultado de actualizacion automatica"),
            [SecurityConfigurationDefaults.MfaRememberDeviceEnabledKey] = ("true", "bool", "Permite recordar dispositivos MFA durante 90 dias"),
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
            ["minimax_api_key"] = ("", "string", "Clave API de MiniMax protegida"),
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

    private static string? ResolveConfiguredAppBaseUrl(IConfiguration? configuration)
    {
        var value = configuration?["App:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return value.TrimEnd('/');
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

    private static void EnsureDemoData(
        AppDbContext context,
        Guid? seedActorId,
        DateTime now,
        IConfiguration? configuration,
        bool isDevelopment)
    {
        if (!ShouldSeedDemoData(configuration, isDevelopment))
        {
            return;
        }

        if (context.Cuentas.IgnoreQueryFilters().Any(c => c.Nombre.StartsWith("Demo ")))
        {
            EnsureDemoAdminPermissions(context, seedActorId);
            return;
        }

        var espanaId = ResolvePaisId(context, "Espana", Guid.Parse("70000000-0000-0000-0000-000000000001"));
        var mexicoId = ResolvePaisId(context, "Mexico", Guid.Parse("70000000-0000-0000-0000-000000000002"));
        var dominicanaId = ResolvePaisId(context, "Republica Dominicana", Guid.Parse("70000000-0000-0000-0000-000000000003"));

        var espana = new Pais
        {
            Id = espanaId,
            Nombre = "Espana",
            CodigoIso2 = "ES",
            Activo = true,
            FechaCreacion = now
        };
        var mexico = new Pais
        {
            Id = mexicoId,
            Nombre = "Mexico",
            CodigoIso2 = "MX",
            Activo = true,
            FechaCreacion = now
        };
        var dominicana = new Pais
        {
            Id = dominicanaId,
            Nombre = "Republica Dominicana",
            CodigoIso2 = "DO",
            Activo = true,
            FechaCreacion = now
        };

        AddIfMissing(context.Paises, espana, p => p.Id == espana.Id || p.Nombre == espana.Nombre);
        AddIfMissing(context.Paises, mexico, p => p.Id == mexico.Id || p.Nombre == mexico.Nombre);
        AddIfMissing(context.Paises, dominicana, p => p.Id == dominicana.Id || p.Nombre == dominicana.Nombre);

        var holdingId = ResolveTitularId(context, "Demo Atlas Labs Holding", Guid.Parse("71000000-0000-0000-0000-000000000001"));
        var operacionesId = ResolveTitularId(context, "Demo Operaciones Norte", Guid.Parse("71000000-0000-0000-0000-000000000002"));
        var autonomoId = ResolveTitularId(context, "Demo Laura Martin", Guid.Parse("71000000-0000-0000-0000-000000000003"));

        var holding = new Titular
        {
            Id = holdingId,
            Nombre = "Demo Atlas Labs Holding",
            Tipo = TipoTitular.EMPRESA,
            Identificacion = "DEMO-B0001",
            ContactoEmail = "finanzas.demo@atlas.local",
            Notas = "Titular demo para validar dashboards y permisos.",
            FechaCreacion = now
        };
        var operaciones = new Titular
        {
            Id = operacionesId,
            Nombre = "Demo Operaciones Norte",
            Tipo = TipoTitular.EMPRESA,
            Identificacion = "DEMO-B0002",
            ContactoEmail = "ops.demo@atlas.local",
            Notas = "Titular demo con cuentas multi-divisa.",
            FechaCreacion = now
        };
        var autonomo = new Titular
        {
            Id = autonomoId,
            Nombre = "Demo Laura Martin",
            Tipo = TipoTitular.AUTONOMO,
            Identificacion = "DEMO-A0003",
            ContactoEmail = "laura.demo@atlas.local",
            Notas = "Titular demo persona/autonomo.",
            FechaCreacion = now
        };

        AddIfMissing(context.Titulares, holding, t => t.Id == holding.Id || t.Nombre == holding.Nombre);
        AddIfMissing(context.Titulares, operaciones, t => t.Id == operaciones.Id || t.Nombre == operaciones.Nombre);
        AddIfMissing(context.Titulares, autonomo, t => t.Id == autonomo.Id || t.Nombre == autonomo.Nombre);

        var sabadellFormatId = ResolveFormatoId(context, Guid.Parse("e1b2cba0-60bd-4854-9b24-d2e88763fa5d"), "Sabadell", "EUR");
        var bbvaMxnFormatId = ResolveFormatoId(context, Guid.Parse("4d0bbbf2-03a0-4f22-887e-3eb6d1a5730a"), "BBVA", "MXN");
        var popularUsdFormatId = ResolveFormatoId(context, Guid.Parse("5b4ba06c-a56e-44c0-9422-352117394a96"), "Banco Popular", "USD");

        var cuentas = new[]
        {
            new Cuenta
            {
                Id = Guid.Parse("72000000-0000-0000-0000-000000000001"),
                TitularId = holdingId,
                PaisId = espanaId,
                Nombre = "Demo Sabadell Operativa EUR",
                NumeroCuenta = "DEMO-ES-001",
                Iban = "ES00 0000 0000 0000 0000 0001",
                BancoNombre = "Sabadell",
                Divisa = "EUR",
                FormatoId = sabadellFormatId,
                TipoCuenta = TipoCuenta.NORMAL,
                Activa = true,
                FechaCreacion = now,
                Notas = "Cuenta demo principal con nominas, cobros y proveedores."
            },
            new Cuenta
            {
                Id = Guid.Parse("72000000-0000-0000-0000-000000000002"),
                TitularId = holdingId,
                PaisId = espanaId,
                Nombre = "Demo Caja Oficina EUR",
                NumeroCuenta = "DEMO-CASH-ES",
                BancoNombre = "Caja interna",
                Divisa = "EUR",
                TipoCuenta = TipoCuenta.EFECTIVO,
                EsEfectivo = true,
                Activa = true,
                FechaCreacion = now,
                Notas = "Caja demo para ver efectivo separado de bancos."
            },
            new Cuenta
            {
                Id = Guid.Parse("72000000-0000-0000-0000-000000000003"),
                TitularId = operacionesId,
                PaisId = mexicoId,
                Nombre = "Demo BBVA Nomina MXN",
                NumeroCuenta = "DEMO-MX-001",
                BancoNombre = "BBVA",
                Divisa = "MXN",
                FormatoId = bbvaMxnFormatId,
                TipoCuenta = TipoCuenta.NORMAL,
                Activa = true,
                FechaCreacion = now,
                Notas = "Cuenta demo con movimientos en pesos mexicanos."
            },
            new Cuenta
            {
                Id = Guid.Parse("72000000-0000-0000-0000-000000000004"),
                TitularId = operacionesId,
                PaisId = dominicanaId,
                Nombre = "Demo Popular USD Reserva",
                NumeroCuenta = "DEMO-DO-USD",
                BancoNombre = "Banco Popular",
                Divisa = "USD",
                FormatoId = popularUsdFormatId,
                TipoCuenta = TipoCuenta.NORMAL,
                Activa = true,
                FechaCreacion = now,
                Notas = "Reserva demo para concentracion por pais/divisa."
            },
            new Cuenta
            {
                Id = Guid.Parse("72000000-0000-0000-0000-000000000005"),
                TitularId = autonomoId,
                PaisId = espanaId,
                Nombre = "Demo Plazo Fijo EUR",
                NumeroCuenta = "DEMO-PF-001",
                BancoNombre = "Sabadell",
                Divisa = "EUR",
                TipoCuenta = TipoCuenta.PLAZO_FIJO,
                Activa = true,
                FechaCreacion = now,
                Notas = "Plazo fijo demo proximo a vencer."
            }
        };

        foreach (var cuenta in cuentas)
        {
            AddIfMissing(context.Cuentas, cuenta, c => c.Id == cuenta.Id || c.Nombre == cuenta.Nombre);
        }

        EnsureDemoExtractos(context, seedActorId, now);
        EnsureDemoPlazoFijo(context, now);
        EnsureDemoAlertas(context, seedActorId, now);
        EnsureDemoAdminPermissions(context, seedActorId);
        EnsureDemoAuditoria(context, seedActorId, now);
    }

    private static bool ShouldSeedDemoData(IConfiguration? configuration, bool isDevelopment)
    {
        var configured = configuration?["DemoData:Enabled"];
        if (bool.TryParse(configured, out var enabled))
        {
            return enabled && isDevelopment;
        }

        return isDevelopment;
    }

    private static void EnsureDemoExtractos(AppDbContext context, Guid? seedActorId, DateTime now)
    {
        AddExtractos(
            context,
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            seedActorId,
            now,
            [
                new("2026-05-02", "Saldo inicial demo", 0m, 85420.12m, "Apertura visual"),
                new("2026-05-05", "Cobro cliente Polaris", 18200m, 103620.12m, "Factura F-2026-041"),
                new("2026-05-07", "Pago proveedor infraestructura", -7340.8m, 96279.32m, "Servidores y licencias"),
                new("2026-05-12", "Nominas mayo", -28450.55m, 67828.77m, "Lote SEPA"),
                new("2026-05-16", "Cobro cliente Bruma", 12600m, 80428.77m, "Transferencia"),
                new("2026-05-20", "Alquiler oficina", -3850m, 76578.77m, "Madrid"),
                new("2026-05-26", "Abono TPV semanal", 6210.44m, 82789.21m, "Ventas online"),
                new("2026-06-03", "Pago impuestos", -14800m, 67989.21m, "Modelo demo"),
                new("2026-06-06", "Cobro consultoria", 22400m, 90389.21m, "Proyecto Atlas")
            ]);

        AddExtractos(
            context,
            Guid.Parse("72000000-0000-0000-0000-000000000002"),
            seedActorId,
            now,
            [
                new("2026-05-01", "Saldo caja inicial", 0m, 4200m, "Conteo mensual"),
                new("2026-05-09", "Gastos mensajeria", -180.35m, 4019.65m, "Caja"),
                new("2026-05-19", "Reposicion caja", 1500m, 5519.65m, "Transferencia interna"),
                new("2026-06-04", "Material oficina", -420.5m, 5099.15m, "Compra demo")
            ]);

        AddExtractos(
            context,
            Guid.Parse("72000000-0000-0000-0000-000000000003"),
            seedActorId,
            now,
            [
                new("2026-05-03", "Saldo inicial demo", 0m, 1250000m, "MXN"),
                new("2026-05-08", "Cobro marketplace", 245000m, 1495000m, "Ventas MX"),
                new("2026-05-14", "Pago logistica", -186500m, 1308500m, "Operador local"),
                new("2026-05-22", "Pago nomina MX", -420000m, 888500m, "Lote nomina"),
                new("2026-06-01", "Cobro distribuidor", 315000m, 1203500m, "Distribucion")
            ]);

        AddExtractos(
            context,
            Guid.Parse("72000000-0000-0000-0000-000000000004"),
            seedActorId,
            now,
            [
                new("2026-05-01", "Saldo reserva", 0m, 54000m, "USD"),
                new("2026-05-13", "Intereses cuenta", 92.4m, 54092.4m, "Banco Popular"),
                new("2026-05-29", "Transferencia a operativa", -8000m, 46092.4m, "Liquidez"),
                new("2026-06-05", "Ingreso partner Caribe", 12500m, 58592.4m, "Contrato demo")
            ]);

        AddExtractos(
            context,
            Guid.Parse("72000000-0000-0000-0000-000000000005"),
            seedActorId,
            now,
            [
                new("2026-04-15", "Constitucion plazo fijo", 25000m, 25000m, "Capital inicial"),
                new("2026-05-15", "Devengo intereses", 142.5m, 25142.5m, "Interes estimado"),
                new("2026-06-15", "Devengo intereses previsto", 142.5m, 25285m, "Proyeccion demo")
            ]);
    }

    private static void AddExtractos(
        AppDbContext context,
        Guid cuentaId,
        Guid? seedActorId,
        DateTime now,
        IReadOnlyList<DemoExtracto> rows)
    {
        var existingRows = context.Extractos
            .IgnoreQueryFilters()
            .Where(e => e.CuentaId == cuentaId)
            .Select(e => e.FilaNumero)
            .ToHashSet();
        var cuentaSuffix = cuentaId.ToString("N")[^4..];

        for (var i = 0; i < rows.Count; i++)
        {
            var filaNumero = i + 1;
            if (existingRows.Contains(filaNumero))
            {
                continue;
            }

            var row = rows[i];
            context.Extractos.Add(new Extracto
            {
                Id = Guid.Parse($"73000000-0000-0000-{cuentaSuffix}-{filaNumero:000000000000}"),
                CuentaId = cuentaId,
                Fecha = DateOnly.Parse(row.Fecha, CultureInfo.InvariantCulture),
                Concepto = row.Concepto,
                Comentarios = row.Comentario,
                Monto = row.Monto,
                Saldo = row.Saldo,
                FilaNumero = filaNumero,
                FechaImportacion = now,
                ImportacionFilaOrigen = filaNumero,
                UsuarioCreacionId = seedActorId,
                FechaCreacion = now,
                Checked = filaNumero % 3 == 0,
                CheckedAt = filaNumero % 3 == 0 ? now : null,
                CheckedById = filaNumero % 3 == 0 ? seedActorId : null,
                Flagged = filaNumero == rows.Count - 1,
                FlaggedNota = filaNumero == rows.Count - 1 ? "Revisar en demo" : null,
                FlaggedAt = filaNumero == rows.Count - 1 ? now : null,
                FlaggedById = filaNumero == rows.Count - 1 ? seedActorId : null
            });
        }
    }

    private static void EnsureDemoPlazoFijo(AppDbContext context, DateTime now)
    {
        var cuentaId = Guid.Parse("72000000-0000-0000-0000-000000000005");
        if (context.PlazosFijos.IgnoreQueryFilters().Any(p => p.CuentaId == cuentaId))
        {
            return;
        }

        context.PlazosFijos.Add(new PlazoFijo
        {
            Id = Guid.Parse("74000000-0000-0000-0000-000000000001"),
            CuentaId = cuentaId,
            CuentaReferenciaId = Guid.Parse("72000000-0000-0000-0000-000000000001"),
            FechaInicio = new DateOnly(2026, 4, 15),
            FechaVencimiento = new DateOnly(2026, 7, 15),
            InteresPrevisto = 427.5m,
            Renovable = true,
            Estado = EstadoPlazoFijo.PROXIMO_VENCER,
            FechaCreacion = now,
            Notas = "Plazo fijo demo para validar vencimientos."
        });
    }

    private static void EnsureDemoAlertas(AppDbContext context, Guid? seedActorId, DateTime now)
    {
        var cuentaCajaId = Guid.Parse("72000000-0000-0000-0000-000000000002");
        if (!context.AlertasSaldo.Any(a => a.CuentaId == cuentaCajaId))
        {
            var alertaId = Guid.Parse("75000000-0000-0000-0000-000000000001");
            context.AlertasSaldo.Add(new AlertaSaldo
            {
                Id = alertaId,
                CuentaId = cuentaCajaId,
                SaldoMinimo = 6000m,
                Activa = true,
                FechaCreacion = now
            });

            if (seedActorId.HasValue)
            {
                context.AlertaDestinatarios.Add(new AlertaDestinatario
                {
                    Id = Guid.Parse("75000000-0000-0000-0000-000000000101"),
                    AlertaId = alertaId,
                    UsuarioId = seedActorId.Value
                });
            }
        }

        if (!context.AlertasSaldo.Any(a => a.TipoTitular == TipoTitular.EMPRESA && a.CuentaId == null))
        {
            context.AlertasSaldo.Add(new AlertaSaldo
            {
                Id = Guid.Parse("75000000-0000-0000-0000-000000000002"),
                TipoTitular = TipoTitular.EMPRESA,
                SaldoMinimo = 50000m,
                Activa = true,
                FechaCreacion = now
            });
        }
    }

    private static void EnsureDemoAdminPermissions(AppDbContext context, Guid? seedActorId)
    {
        if (!seedActorId.HasValue)
        {
            return;
        }

        if (context.PermisosUsuario.Any(p =>
                p.UsuarioId == seedActorId.Value &&
                p.PaisId == null &&
                p.TitularId == null &&
                p.CuentaId == null))
        {
            return;
        }

        context.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.Parse("76000000-0000-0000-0000-000000000001"),
            UsuarioId = seedActorId.Value,
            PuedeVerCuentas = true,
            PuedeAgregarLineas = true,
            PuedeEditarLineas = true,
            PuedeEliminarLineas = true,
            PuedeImportar = true,
            PuedeVerDashboard = true,
            PuedeRevisarLineas = true,
            PuedeAprobarImportaciones = true,
            PuedeConciliar = true,
            PuedeCerrarConciliacion = true
        });
    }

    private static void EnsureDemoAuditoria(AppDbContext context, Guid? seedActorId, DateTime now)
    {
        var auditId = Guid.Parse("77000000-0000-0000-0000-000000000001");
        if (context.Auditorias.Any(a => a.Id == auditId))
        {
            return;
        }

        context.Auditorias.Add(new Auditoria
        {
            Id = auditId,
            UsuarioId = seedActorId,
            TipoAccion = "DEMO_SEED",
            EntidadTipo = "DemoData",
            Timestamp = now,
            DetallesJson = """{"mensaje":"Datos demo sinteticos cargados para validar la interfaz"}"""
        });
    }

    private static Guid ResolvePaisId(AppDbContext context, string nombre, Guid fallbackId)
    {
        var local = context.Paises.Local.FirstOrDefault(p => p.Nombre == nombre);
        if (local is not null)
        {
            return local.Id;
        }

        var existingId = context.Paises
            .IgnoreQueryFilters()
            .Where(p => p.Nombre == nombre)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefault();

        return existingId ?? fallbackId;
    }

    private static Guid ResolveTitularId(AppDbContext context, string nombre, Guid fallbackId)
    {
        var local = context.Titulares.Local.FirstOrDefault(t => t.Nombre == nombre);
        if (local is not null)
        {
            return local.Id;
        }

        var existingId = context.Titulares
            .IgnoreQueryFilters()
            .Where(t => t.Nombre == nombre)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefault();

        return existingId ?? fallbackId;
    }

    private static Guid? ResolveFormatoId(AppDbContext context, Guid fallbackId, string bancoNombre, string divisa)
    {
        var local = context.FormatosImportacion.Local.FirstOrDefault(f =>
            f.Id == fallbackId ||
            (f.BancoNombre == bancoNombre && f.Divisa == divisa));
        if (local is not null)
        {
            return local.Id;
        }

        return context.FormatosImportacion
            .IgnoreQueryFilters()
            .Where(f =>
                f.Id == fallbackId ||
                (f.BancoNombre == bancoNombre && f.Divisa == divisa))
            .Select(f => (Guid?)f.Id)
            .FirstOrDefault();
    }

    private static void AddIfMissing<TEntity>(
        DbSet<TEntity> set,
        TEntity entity,
        Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
    {
        if (set.IgnoreQueryFilters().Any(predicate))
        {
            return;
        }

        set.Add(entity);
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

    private sealed record DemoExtracto(
        string Fecha,
        string Concepto,
        decimal Monto,
        decimal Saldo,
        string Comentario);
}
