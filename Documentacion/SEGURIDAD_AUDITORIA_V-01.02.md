# Auditoria de seguridad V-01.02

Fecha: 2026-04-20

## Resumen

Riesgo final tras parches: bajo.

La revision cubrio backend ASP.NET Core, Watchdog, frontend React/Vite, configuracion, base de datos, scripts, dependencias npm/NuGet, CI/CD, archivos publicos, logs, temporales y auxiliares. No se detectaron SQL injection, XSS directo, tokens en localStorage, cookies inseguras en produccion, endpoints API sin auth accidental ni dependencias vulnerables conocidas.

## Hallazgos corregidos

### Secretos/defaults de desarrollo en archivos versionables

- Severidad inicial: alta.
- Archivos afectados: configuracion API, Watchdog y Docker Compose.
- Solucion: configuracion base sin secretos, `.env.example`, plantillas locales/produccion y password admin inicial obligatoria.

### Password admin inicial hardcodeada

- Severidad inicial: alta.
- Archivo afectado: `SeedData`.
- Solucion: `SeedAdmin:Password` debe venir de configuracion. Si la BD esta vacia y falta, el arranque falla.

### Watchdog con fallback de password de BD

- Severidad inicial: media.
- Archivo afectado: `WatchdogOperationsService`.
- Solucion: restauraciones fallan si `WatchdogSettings:DbPassword` no esta configurado.

### CI con actions por tag mutable

- Severidad inicial: media.
- Archivo afectado: `.github/workflows/ci.yml`.
- Solucion: actions oficiales fijadas por SHA.

### Version vieja y mojibake

- Severidad inicial: baja.
- Archivos afectados: seed, actualizaciones, importacion, SMTP.
- Solucion: `V-01.02` aplicada y cadenas rotas corregidas.

### Secretos SMTP/API key guardados en claro en BD

- Severidad inicial: alta.
- Archivos afectados: configuracion backend, servicios SMTP y tipos de cambio.
- Solucion: `ISecretProtector` con Data Protection, prefijo `enc:v1:`, migracion automatica de valores legacy y redaccion en respuestas/auditoria.

### Permiso global de dashboard abria alcance de datos

- Severidad inicial: alta.
- Archivo afectado: `UserAccessService`.
- Solucion: `PuedeVerDashboard` ya no cuenta como permiso global de datos; test de regresion agregado.

### Descarga de exportaciones basada solo en ruta de BD

- Severidad inicial: media.
- Archivo afectado: `ExportacionesController`.
- Solucion: validacion de extension `.xlsx` y raiz `export_path`.

### Watchdog con binding demasiado laxo

- Severidad inicial: media.
- Archivo afectado: `GestionCaja.Watchdog/Program.cs`.
- Solucion: Kestrel escucha solo en `localhost:5001`.

### `AllowedHosts` wildcard en produccion

- Severidad inicial: media.
- Archivos afectados: `Program.cs`, plantilla de produccion e instalador.
- Solucion: la API rechaza wildcards, placeholders o valor vacio fuera de Development.

### Artefactos locales con informacion de sesion

- Severidad inicial: media.
- Archivos afectados: logs temporales API y `Otros/Auxiliares/artifacts`.
- Solucion: eliminados payloads de login, cookies, cabeceras y captura de login rellenado.

## Comprobaciones

- Barrido local de patrones sensibles: sin secretos reales versionables tras excluir falsos positivos y archivos ignorados.
- NuGet audit: sin paquetes vulnerables.
- npm audit: 0 vulnerabilidades.
- Backend build Release: 0 warnings, 0 errores.
- Backend tests Release completos: 83/83 OK.
- Frontend lint/build: OK.

## Riesgos pendientes

- El estado Git local no permite diff fino porque la copia aparece practicamente entera como `untracked`; no afecta runtime, pero bloquea revision/commit fiable.
- La seguridad productiva depende de configurar secretos reales, conservar el keyring de Data Protection protegido por DPAPI y fijar `AllowedHosts` real antes de release. No improvises esto en produccion.

---

## Auditoria adicional 2026-04-20 (tarde)

Segunda pasada sobre el arbol completo tras el hardening inicial. Enfoque: secretos residuales, PII en respuestas de integracion, path traversal en configuracion editable por admins, packaging de release.

### Hallazgos corregidos en esta pasada

#### Query params sensibles en auditoria de integracion
- Severidad inicial: media.
- Archivo afectado: `Middleware/IntegrationAuthMiddleware.cs`.
- Solucion: redaccion por lista de claves sensibles antes de serializar al registro de auditoria.

#### Path traversal via `backup_path`/`export_path`
- Severidad inicial: media.
- Archivos afectados: `Services/BackupService.cs`, `Services/ExportacionService.cs`.
- Solucion: `ResolveSafeDirectory` rechaza rutas no absolutas, con caracteres invalidos o con segmentos `..`.

#### Fuga de email de usuarios borrados via integracion
- Severidad inicial: media.
- Archivo afectado: `Controllers/IntegrationOpenClawController.cs`.
- Solucion: `IgnoreQueryFilters()` sigue cargando metadatos historicos, pero los usuarios con `deleted_at` no nulo aparecen como `usuario-eliminado` en la respuesta.

#### Kestrel de desarrollo en `0.0.0.0` y `AllowedHosts=*`
- Severidad inicial: baja.
- Archivos afectados: `appsettings.Development.json`, `appsettings.Development.json.template`.
- Solucion: binding a `localhost` y `AllowedHosts=localhost`.

#### `dotnet publish` empaquetando dev secrets
- Severidad inicial: alta. No lo habian detectado los agentes en la primera pasada.
- Archivos afectados: `GestionCaja.API.csproj`, `GestionCaja.Watchdog.csproj`.
- Solucion: `Content Update="appsettings.Development.json" CopyToPublishDirectory="Never" ExcludeFromSingleFile="true"` y equivalentes para las plantillas.

#### Passwords hardcodeadas en scripts y docs historicas en `Otros/`
- Severidad inicial: media (solo local, `Otros/` nunca se sube).
- Archivos afectados: `Otros/Auxiliares/phase2-smoke.ps1`, `Otros/Auxiliares/phase2-smoke-curl.ps1`, `Otros/Raiz anterior/SPEC.md`, `Otros/Raiz anterior/CORRECCIONES.md`.
- Solucion: env vars obligatorias en los scripts; placeholders en documentos.

### Verificaciones

- Barrido `Grep` en `Atlas Balance/` con patrones (`password|pwd|secret|token|api[_-]?key|bearer`) y defaults (`Admin1234|dev_password|changeme`): 0 hallazgos reales.
- `wwwroot/assets/`: solo bundles Vite minificados, sin `.map`.
- `docker-compose.yml`: usa `${ATLAS_BALANCE_POSTGRES_PASSWORD:?...}` (falla si falta).
- Wrappers `.cmd` y `.bat` en la raiz: no contienen secretos.
- Watchdog `appsettings.json`: placeholders vacios (secreto real fuera de git).

### Riesgos pendientes

- Paquetes de release anteriores a este cambio pueden contener `appsettings.Development.json`. Si se distribuyeron, los secretos embebidos deben rotarse: `JwtSettings:SecretKey`, `WatchdogSettings:SharedSecret`, `SeedAdmin:Password`, password de `app_user` en `ConnectionStrings:DefaultConnection`.
- Falta volver a correr `dotnet build -c Release` y `dotnet test` tras estos cambios antes del siguiente paquete.
- `Otros/Auxiliares/` sigue viviendo fuera del repo oficial pero dentro del workspace; cualquier dump futuro (logs, cookies, curls con Authorization) debe seguir evitandose.
