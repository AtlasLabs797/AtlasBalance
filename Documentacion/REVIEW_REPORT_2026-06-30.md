# Atlas Balance — Informe de revisión técnica

**Fecha:** 2026-06-30
**Ámbito:** revisión de solo lectura de `Atlas Balance/backend` y `Atlas Balance/frontend`.
**Naturaleza:** auditoría técnica. No se ha modificado ningún archivo del proyecto.
**Severidades:** CRITICAL (pérdida de datos / brecha / caída) · HIGH (bug serio en flujo normal) · MEDIUM (caso molesto / diseño débil) · LOW (pulido) · INFO (observación neutra).

---

## 0 · Veredicto global

Atlas Balance tiene una arquitectura **sólida en sus cimientos**: autenticación con JWT en cookies HttpOnly+Strict, bcrypt 12, CSRF double-submit, refresh tokens con `pg_advisory_xact_lock` y detección de reuso, MFA con replay protection, integración OpenClaw separada por bearer + scopes, watchdog en loopback con secreto compartido en compare constant-time, soft-delete universal vía `ISoftDelete`, RLS en Postgres, Hangfire y Serilog. La estructura de carpetas, las migraciones reversibles, el uso de `System.Text.Json`, la convención de nombres en BD y el chequeo de placeholders al arrancar son las cosas que más se cuidan.

Donde más riesgo hay acumulado es en tres puntos:

1. **Huecos de seguridad pequeños pero explotables** en middleware de integración y rotación de claves (ver §3).
2. **Falta de concurrencia optimista** en estados financieros colaborativos (revisión, conciliación) — riesgo de *lost-write* entre dos revisores.
3. **N+async en paths críticos** (Dashboard, Conciliación, Conversión) — a 50k+ filas de extractos el sistema empieza a sufrir.

No hay vulnerabilidades CRÍTICAS del estilo "auth roto" o "SQL injection". El nivel de defensa es alto; lo que falta es **endurecer el extremo trasero** y **optimizar el rendimiento de lectura** antes de que los datos crezcan.

---

## 1 · Hallazgos CRITICAL (arreglar antes del próximo deploy)

### C1 · Token sin scopes = acceso total al API de integración
**Archivo:** `backend/src/AtlasBalance.API/Middleware/IntegrationAuthMiddleware.cs:219-228`
**Qué pasa:** Cuando un token de integración se crea con `EndpointScopes` vacío (o el JSON falla), el middleware hace `if (scopes.Count == 0) return true;` y concede acceso a **todos** los endpoints `/api/integration/openclaw/*`. Combinado con un token sin expiración es catastrófico.
**Fix:** invertir la lógica. Si `scopes.Count == 0`, denegar todo. Si falla el parseo del JSON, denegar con `403 forbidden` y auditar.

### C2 · Cifrado de backups puede hacer irrecuperables todas las copias en la nube
**Archivo:** `backend/src/AtlasBalance.API/Services/BackupEncryptionService.cs:128-172`
**Qué pasa:** Si la clave AES guardada en BD se corrompe (Base64 inválido, ruido en disco), el helper **regenera una nueva clave y la sobreescribe silenciosamente**. A partir de ese momento, todos los `.dump.enc` ya subidos a Google Drive se vuelven irrecuperables para siempre. Sólo te das cuenta cuando intentas restaurar meses después.
**Fix:** NUNCA sobreescribir. Si la fila está corrupta, fallar ruidosamente con una excepción y bloquear el inicio del servicio hasta que un admin intervenga. La rotación de claves debe ser manual y con copia de seguridad previa.

### C3 · Watchdog puede quedar muerto si una BD-falla ocurre durante la toma del semáforo
**Archivo:** `backend/src/AtlasBalance.Watchdog/Services/WatchdogOperationsService.cs:91-125` y `:145-219`
**Qué pasa:** En `StartRestoreAsync` / `StartUpdateAsync`, tras `WaitAsync(0)` se escribe en `WatchdogStateStore` ANTES de soltar el lock. Si el disco falla al guardar el estado, el `SemaphoreSlim` ya tomado **no se libera porque sólo hay `finally` dentro del `Task.Run` que nunca arrancó**. Watchdog queda inerte hasta reinicio manual.
**Fix:** envolver la sección post-`WaitAsync` en `try { ... } catch { _operationLock.Release(); throw; }` ANTES del `Task.Run`.

---

## 2 · Hallazgos HIGH

### H1 · Dashboard lanza 409 entero si UNA sola conversión cruzada de divisa no tiene tipo de cambio
**Archivo:** `backend/src/AtlasBalance.API/Services/DashboardService.cs:344-347` + `backend/src/AtlasBalance.API/Services/TiposCambioService.cs` (lanzador de `TipoCambioMissingException`)
**Qué pasa:** El dashboard agrega saldos por titular y los convierte a una divisa destino. Una cuenta en una moneda sin tipo de cambio registrado aborta el endpoint completo con 409. El usuario pierde el dashboard entero.
**Fix:** introducir `Task<IReadOnlyDictionary<string, decimal>> BulkConvertAsync(saldosPorDivisa, target, ct)`; si falta una tasa, devolver `tasa_pendiente: true` en esa fila y marcarla visualmente, en lugar de abortar.

### H2 · N+async en construcción de métricas del dashboard
**Archivo:** `backend/src/AtlasBalance.API/Services/DashboardService.cs:233-307, 430-583`
**Qué pasa:** Por cada extracto leído se invoca `await _tiposCambioService.ConvertAsync(...)` en serie dentro de un `foreach`. Importar 1.000 filas dispara miles de awaits en serie.
**Fix:** agregar primero por `(cuenta, divisa)` (un único valor por cuenta), convertir una sola vez por divisa, luego proyectar.

### H3 · Matching de conciliación exige igualdad exacta de monto
**Archivo:** `backend/src/AtlasBalance.API/Services/ConciliacionService.cs:317-368`
**Qué pasa:** `x.Monto == movimiento.Monto` (línea 328). Una transferencia esperada de 1.000,00 € con comisión bancaria de 1,50 € figura como extracto a 998,50 € → nunca matchea. Es el caso más común de “comisión de banco”.
**Fix:** aceptar tolerancia configurable (`conciliacion_tolerance_amount`, `conciliacion_tolerance_percent`). Score negativo por diferencia proporcional; los casos con diff < tolerancia deberían entrar como sugerencias con score 70-85.

### H4 · No hay índice para la búsqueda de conciliación
**Archivo:** `backend/src/AtlasBalance.API/Data/AppDbContext.cs:186-198`
**Qué pasa:** El query `Where(CuentaId == X && Monto == M && Fecha >= a && Fecha <= b)` no tiene índice cubriente. En cuentas con muchos extractos, cada `SugerirAsync` provoca un sequential scan dentro de la ventana de fechas.
**Fix:** `b.HasIndex(e => new { e.CuentaId, e.Fecha, e.Monto })` (o variante parcial `WHERE monto <> 0`).

### H5 · Sin concurrencia optimista en extracto / revisión / conciliación
**Archivos:** `backend/src/AtlasBalance.API/Models/Entities.cs:156-243`, `Services/RevisionService.cs:234-270`, `Services/ConciliacionService.cs`
**Qué pasa:** Cero matches para `[Timestamp]`, `xmin`, `[ConcurrencyCheck]`. Dos usuarios editando el mismo extracto o cambiando el mismo estado de revisión tienen last-write-wins silencioso. El test `ExtractosConcurrencyTests` sólo cubre el *create*.
**Fix:** usar `b.UseXminAsConcurrencyToken()` (Npgsql 8) en `EXTRACTOS`, `REVISION_EXTRACTO_ESTADOS`, `CONCILIACIONES`, `MOVIMIENTOS_ESPERADOS`. Devolver 409 con el estado actual al cliente.

### H6 · Lote de importación puede quedar en estado inconsistente si un extracto intermedio falla
**Archivo:** `backend/src/AtlasBalance.API/Services/ImportacionService.cs:362-456`
**Qué pasa:** En `ConfirmarLoteAsync` se invoca `ConfirmarAsync` sin catch. Si lanza, los extractos ya están escritos (commit interno), pero el lote queda en `validado` con `ConfirmadoPorId = null` y el usuario ve error genérico.
**Fix:** envolver en try/catch; en catch → `lote.Estado = "error"; lote.Notas = ex.Message; persistir; rethrow`.

### H7 · Cooldown global de alertas — una sola notificación para N cuentas
**Archivo:** `backend/src/AtlasBalance.API/Services/AlertaService.cs:83-94`
**Qué pasa:** `FechaUltimaAlerta` vive en `AlertaSaldo`. Si dos cuentas comparten la misma alerta (global o por tipo de titular) y ambas caen a saldo bajo, **sólo se notifica una vez**. Las demás quedan sin avisar.
**Fix:** mover el cooldown a una tabla hija `alerta_saldo_ultima_notificacion(cuenta_id, alerta_id, fecha_utc)` y consultarla al evaluar.

### H8 · Google Drive restore sin verificar SHA-256 post-descifrado
**Archivo:** `backend/src/AtlasBalance.API/Services/GoogleDriveBackupService.cs:356-415`
**Qué pasa:** Tras descargar y descifrar, marca el backup como `SUCCESS` sin recomputar el checksum y compararlo con `copy.ChecksumSha256`.
**Fix:** verificación post-descifrado `expected_sha == computed_sha`; si falla → `FAILED` con detalle en audit.

### H9 · Tabla `CONFIGURACION` guarda secretos en texto plano
**Archivo:** `backend/src/AtlasBalance.API/Migrations/20260622120000_AddBackupSchedulingAndGoogleDrive.cs:116-148`
**Qué pasa:** OAuth client_secret, claves de cifrado y API keys se guardan como `text` sin cifrar. Cualquier backup `pg_dump` o export los expone en claro.
**Fix:** introducir `SecretProtector` (ya existe `DataProtectionSecretProtector` y `ISecretProtector`) en `CONFIGURACION.Valor` con columna dedicada a secretos; cifrar/descrifrar en el repositorio.

---

## 3 · Seguridad y autorización

### 3.1 · Lo que está bien hecho
- JWT cookies: HttpOnly + Secure + SameSite=Strict, validados con HMAC SHA-256, `ClockSkew=Zero`. (`Program.cs:67-75`.)
- Refresh tokens: rotación, detección de reuso (revoca familia completa + rota SecurityStamp), serialización con `pg_advisory_xact_lock` por hash. (`AuthService.cs:414-518`, `:964-1003`.)
- CSRF: double-submit cookie + header, `FixedTimeEquals` en la comparación, exclusiones quirúrgicas. (`CsrfMiddleware.cs:10-39`, `CsrfService.cs:20-35`.)
- TOTP MFA: replay protection con `matchedStep <= MfaLastAcceptedStep`, trusted-device cookie atado a SecurityStamp. (`AuthService.cs:294-295`, `:1079-1082`.)
- Login throttle: doble contador (por (email, IP) y por IP) + `LockedUntil` en BD; genérico 401 sin enumeración. (`AuthService.cs:911-944`.)
- Integración: bearer SHA-256 hasheado en BD, scopes por endpoint, rate limit por token. (`IntegrationTokenService.cs:40-74`, `Middleware/IntegrationAuthMiddleware.cs`.)
- Watchdog: loopback-only, secret en header con compare constant-time, validación de path contra root anclado. (`Watchdog/Program.cs:10-79`, `Services/WatchdogOperationsService.cs:545-573`.)
- CORS sólo en Development, HSTS fuera de Development, Hangfire dashboard sólo en Development. (`Program.cs:194-411`.)
- Logs sin password / token cleartext. PII sólo en `Auditorias` (intencional).
- bcrypt cost 12 en todos los puntos de escritura de password.

### 3.2 · Lo que conviene endurecer

| Sever | Hallazgo | Archivo:línea | Fix |
|---|---|---|---|
| MED | `PassthroughSecretProtector` queda como fallback silencioso si DI no inyecta `DataProtectionSecretProtector` (MFA secret acabaría en claro) | `Services/AuthService.cs:65, 1222-1233` | Hacer `ISecretProtector` obligatorio en el constructor |
| MED | `MfaChallengeState` cacheado en memoria no se invalida si cambia el SecurityStamp durante el challenge | `Services/AuthService.cs:257-402` | Re-verificar stamp al consumir el challenge |
| MED | `AuditService.LogAsync` no está acoplado a la transacción del negocio: si el negocio falla, el log persiste | `Services/AuditService.cs:34-48` | Audit debe compartir `DbContext`/transacción |
| MED | `MfaChallengeState` se reinicia en cada reinicio del Windows Service | `Services/AuthService.cs:46, 822-832` | Documentar o mover a BD |
| MED | Existe referencia a `FluentValidation.AspNetCore` pero nunca se registra `AddFluentValidation` | `AtlasBalance.API.csproj:29`, `Program.cs` | Cablear FluentValidation o quitar el paquete |
| MED | Validación de paquete de actualización (`ActualizarApp`) sólo verifica presencia de 4 ficheros, no firma | `backend/src/AtlasBalance.Watchdog/Services/WatchdogOperationsService.cs:978-984`, config en `UpdateSecurity:ReleaseSigningPublicKeyPem` | Usar la clave pública PEM cargada para verificar la firma del paquete |
| MED | Zip-slip en extracción de paquete: `entry.FullName` puede contener `..` | `backend/src/AtlasBalance.API/Services/ActualizacionService.cs:767, 796` | Validar `Path.GetFullPath(destino).StartsWith(packageRoot)` y rechazar segmentos `..` |
| MED | `Conciliacion -> MovimientoEsperado` FK es CASCADE: borrar un movimiento destruye su conciliación (y por tanto su historial) | `Data/AppDbContext.cs:425` | Cambiar a `Restrict` |
| MED | Cascadas en `ImportacionLoteFila`, `ExtractoColumnaExtra`, `RevisionExtractoEstado` queman datos colaterales si algo bypasea soft-delete | `Data/AppDbContext.cs:242, 251, 263` | Cambiar a `Restrict` y dejar al servicio decidir |
| LOW | Lista de contraseñas comunes es sólo 9 entradas | `Constants/SecurityPolicy.cs:5-43` | Integrar HIBP k-anonymity o top-10k |
| LOW | Watchdog acepta HTTP plano; el secreto compartido protege, pero documentar la decisión | `Watchdog/Program.cs:10-13` | OK porque es loopback-only |
| LOW | `EmailService.SendAsync` no tiene timeout duro si SMTP no responde | `Services/EmailService.cs:62-103` | `CancelAfter(15s)` en el SmtpClient |
| LOW | `Security:RequireMfaForWebUsers` se puede desactivar y deja MFA bypassable | `Services/AuthService.cs:791-795` | Validar contra `true` en arranque (como ya se hace con `WatchdogSettings:SharedSecret`) |
| MED | Audit `DetallesJson` no tiene límite: un caller puede meter megabytes | `Services/AuditService.cs:34-48` | Cap a 32 KB en el servicio |

### 3.3 · Cosas que faltan ver con más profundidad
- `IntegrationTokenService.ResolveExpiration` confía en la validación del controlador padre. Hay que confirmar que `Controllers/IntegracionesController.cs` rechaza `SinExpiracionConfirmada = true` sin el segundo check de string mágico (`"NO_EXPIRAR"`). Si no lo hace, añadirlo.
- `Program.cs:411` desactiva Hangfire dashboard en producción — correcto. Confirmar que el webhook `/hangfire` también se cae.

---

## 4 · Frontend — UI / UX / accesibilidad

### 4.1 · Lo que está bien hecho
- Auth flow: `App.tsx:62-108` corre `/auth/me` con flag `mounted`, limpia todas las stores en logout. (`App.tsx`, `services/api.ts:62-78`.)
- Refresh token queue: `failedQueue` con cap de 50, `originalRequest._retry` previene loops. (`services/api.ts:107-117`.)
- Axios interceptor: omite refresh en `/auth/login` y `/auth/refresh-token`. (`api.ts:80-95`.)
- Sesión: idle timer de 20 min con toast a 18 min y modal a 19 min, debounce de 2s en reset. (`hooks/useSessionTimeout.ts:1-127`.)
- Dialogs: focus trap genérico en `useDialogFocus.ts:33-77`, restauración del focus al cerrar.
- Charts accesibilidad: tabla sr-only paralela al gráfico en `EvolucionChart`, `TitularSaldoBarChart`. (`EvolucionChart.tsx:222-271`.)
- Vite tiene `customLogger` que redacta en consola `Cookie`, `Authorization`, `Set-Cookie`, `X-CSRF-Token`, `Bearer`, JWTs y muchos prefijos de secrets. (`vite.config.ts:1-38`.)
- Code splitting por página vía `lazy(...)` ya configurado en `App.tsx:15-35`.
- `AiMessageContent.tsx:121-185` renderiza vía `React.createElement`, sin `dangerouslySetInnerHTML` ni `eval`. Resistente a XSS.
- Grid de extractos: ARIA roles correctos (`role="grid"`, `aria-rowindex`, `aria-sort`). (`ExtractoTable.tsx:309-336`.)
- Layout responsivo: sidebar colapsa en tablet, bottom-nav en móvil, `BottomNav.tsx:122-132` con `role="dialog"` + Escape.

### 4.2 · Hallazgos clave

| Sever | Hallazgo | Archivo:línea | Fix |
|---|---|---|---|
| HIGH | **Versión hardcodeada en sidebar** — `APP_VERSION_LABEL = 'V-02-02'` literal en `Sidebar.tsx:140-142`. Existe además en `package.json` y `VERSION`. Drift garantizado | `frontend/src/components/layout/Sidebar.tsx:140` | Inyectar `import.meta.env.VITE_APP_VERSION` desde `package.json` en build |
| HIGH | **Fila de extractos no respeta la altura estimada del virtualizer**: notas largas en `flagged_nota` desbordan la altura fija, se solapan filas | `ExtractoTable.tsx:99-107, 485-498, 510` | Cambiar a `useVirtualizer({ measureElement: true, ... })` o limitar `<input>` a una línea con ellipsis |
| HIGH | **No se cargan alertas tras primer login + cambio de contraseña** — `LoginPage.completeLogin` sólo carga alertas si `!primer_login`; el path post cambio no lo hace | `LoginPage.tsx:106-125`, `ChangePasswordPage.tsx:36-43` | Mover la carga de alertas a `Layout.tsx` o re-cargar tras el cambio |
| HIGH | **Filter input en extractos sin debounce**: cada keystroke re-renderiza virtualización completa | `ExtractoTable.tsx:329-333, 92-94` | Usar el `useDebouncedValue` existente (ya está en `hooks/useDebouncedValue.ts`) |
| HIGH | **Interceptor Axios sólo trata 401** — 419 / 440 (CSRF expirado) caen al toast genérico sin recuperación | `services/api.ts:91-95` | Manejar 419/440 igual que 401, o mapear explícitamente |
| HIGH | **Mensaje optimista en chat IA sin estado "no enviado"** en error | `AiChatPanel.tsx:165-193` | Marcar el prompt con retry visual si el POST falla |
| HIGH | **`divisaStore.convertir` con tasa faltante devuelve el monto original sin avisar** — el dashboard suma cifras no convertidas en silencio | `stores/divisaStore.ts:33-47` | Añadir warning visible (banner) o nuevo store "tasas pendientes" |
| MED | **Estado de tema y `data-theme` se mantienen en 2 sitios** (main.tsx y uiStore) | `main.tsx:10-12` + `stores/uiStore.ts:36, 45, 51` | Centralizar en una función `initTheme()` |
| MED | **audit-fetch race**: al abrir modal de auditoría de una celda y cambiar de celda antes del primer fetch, la respuesta puede llegar al modal equivocado | `ExtractosPage.tsx:321-336` | Cancelación con `AbortController` o `cancelled` flag |
| MED | **EditableCell revierte el valor al fallar `onSave`** sin mostrar el error | `components/extractos/EditableCell.tsx:33-53` | Mantener `draft` mostrado + inline error, permitir reintentar |
| MED | **LoginPage ignora `location.state.from`** que `ProtectedRoute` sí pasa | `LoginPage.tsx:43`, `ProtectedRoute.tsx:18-28` | Leer `location.state.from?.pathname` o quitar el state si no se usa |
| MED | **Logout paths divergen**: `useSessionTimeout.performLogout` borra `clearPermisos`/`clearAlertas` manualmente; `api.ts:122-134` usa `clearSessionState` central | `useSessionTimeout.ts:42-50` + `api.ts:122-134` | Consolidar en `clearSessionState` único |
| MED | **`ConfiguracionPage` lockea toda la página durante actualización** (10 min polling); debería desactivar sólo el botón | `ConfiguracionPage.tsx:339-392` | Refactorizar a `updateStatus === 'pending'` y deshabilitar acciones de update |
| MED | **`Sesión timeout toast aparece demasiado pronto** — la constante `TOAST_WARNING_MINUTES = 2` está mal nombrada: significa "minutos restantes", no "minutos desde inicio". Igual `WARNING_MINUTES = 1` | `hooks/useSessionTimeout.ts:9-10` | Renombrar a `TOAST_REMAINING_MINUTES` / `MODAL_REMAINING_MINUTES` y mover fuera del hook |
| MED | **Versión visible top-bar puede divergir de la del sidebar** si editas uno | `components/layout/Sidebar.tsx:140-142` vs `TopBar.tsx` | Una sola fuente `import.meta.env.VITE_APP_VERSION` |
| MED | **`Api.ts` fuerza `Content-Type: application/json`** incluso para `FormData` | `services/api.ts:13` | Skip default cuando `config.data instanceof FormData` |
| MED | **Audit store notifications** marca leído optimista sin rollback en fallo | `stores/notificacionesAdminStore.ts:36-46` | Marcar toast suave si el server call falla |
| LOW | **Toast** no tiene hard max-age — si la tab está oculta toda la vida del toast, éste persiste | `ToastViewport.tsx:42-52` | Hard max 30s |
| LOW | **`PaisScopeStore.loadPaises`** silencia errores sin UI surfacing | `stores/paisScopeStore.ts:55-57` | Mostrar banner visible si `lastError` |
| LOW | **CuentasPage** ejecuta effect de limpieza de form en cada render cuando cambian muchos deps | `CuentasPage.tsx:393-416` | Extraer a un handler explícito |
| LOW | **Vite `sourcemap: false` en producción** — debugging opaco si hay crash | `vite.config.ts:148-149` | Habilitar sourcemaps separados subidos aparte |
| LOW | **Focus global** sin offset — se solapa con el elemento | `styles/global.css:21-23` | Añadir `outline-offset: 2px` |
| LOW | **Detección de separador de CSV** sólo sobre primeras 5 líneas no vacías | `ImportacionPage.tsx:74-93` | Confirmar visualmente con el backend / permitir override |
| INFO | **Versiones duplicadas** (`Sidebar.tsx`, `package.json`, `Atlas Balance/VERSION`, `Directory.Build.props`) | (varios) | Inyectar desde env de Vite; backend expone `/sistema/version` |
| INFO | **Sidebars admin chevrones** faltan en secciones agrupadas | `Sidebar.tsx:96-145` | OK con aria-label, opcional visual |
| INFO | **BottomNav sheet `onClick` no es accesible por teclado** (sólo pointer) | `BottomNav.tsx:122-132` | Añadir botón close explícito o role="button" en backdrop |
| INFO | **`AppErrorBoundary`** sólo loggea en DEV | `AppErrorBoundary.tsx:23-26` | Loggear también en producción a `console.error` |

---

## 5 · Base de datos y rendimiento

### 5.1 · Lo que está bien hecho
- Soft-delete universal con `ISoftDelete` + `ApplySoftDeleteQueryFilters` automático (`Data/AppDbContext.cs:491-507`). Ejemplo de manual de EF Core.
- UUIDs como PK en todas las entidades transaccionales. Las lookup tables (`DIVISAS_ACTIVAS`, `CONFIGURACION`) usan PKs legítimos.
- ENUMs Postgres tipados (`AppDbContext.cs:50-57`) — las conversiones del `Down()` de las migraciones son correctas y verificadas con `UPDATE` antes del `DROP TYPE`.
- Índices en todos los FKs y en cada `DeletedAt` (lo crítico para el query filter). Ya hay `(CuentaId, Fecha)` para extractos y `(CuentaId, FilaNumero)` único.
- Raw SQL está parametrizado al 100 %: las 6 apariciones son `pg_advisory_xact_lock` o `ExecuteSqlInterpolated` con parámetros `{...}`. Cero inyección.
- Convención de nombres UPPER_SNAKE tablas / lower_snake columnas consistente en todo `Entities.cs`.
- `Initial.cs` es 100 % reversible (cada `CreateTable` tiene su `DropTable`).
- Numerario siempre en `decimal(18,4)`. FX en `decimal(18,8)`. Sin doubles.

### 5.2 · Lo que conviene endurecer

| Sever | Hallazgo | Archivo:línea | Fix |
|---|---|---|---|
| HIGH | Sin concurrencia optimista (no hay `xmin`/`RowVersion` en extractos, revisión, conciliación). `RevisionService.SetEstadoAsync` lost-write en dos revisores simultáneos | `Services/RevisionService.cs:234-270`, `Models/Entities.cs:156-243` | Añadir `b.UseXminAsConcurrencyToken()` + devolver 409 con `DbUpdateConcurrencyException` |
| HIGH | Índice cubriente `(CuentaId, Fecha, Monto)` no existe → sequential scan en conciliación | `Data/AppDbContext.cs:186-198` | Nuevo índice |
| HIGH | `Conciliacion -> MovimientoEsperado` FK es CASCADE (borra historial al borrar movimiento) | `Data/AppDbContext.cs:425` | Cambiar a `Restrict` |
| HIGH | `BackupEncryptionService` regenera clave silenciosamente si está corrupta → irrecuperable | (ver C2) | NUNCA sobreescribir |
| HIGH | Secretos en `CONFIGURACION` (OAuth, API keys) en plano | `Migrations/20260622120000_AddBackupSchedulingAndGoogleDrive.cs:116-148` | `ISecretProtector` para cifrar `Valor` cuando `EsSecreto=true` |
| HIGH | `DashboardService` N+async por extracto al convertir moneda | `Services/DashboardService.cs:430-583` | Bulk convert: agregar por `(cuenta,divisa)`, convertir una vez por divisa |
| HIGH | Una conversión cruzada faltante aborta el dashboard entero con 409 | `Program.cs:323-329` + `DashboardService.cs:344-347` | Permitir respuesta con `tasa_pendiente=true` por fila |
| MED | Soft delete ausente en `ImportacionLoteFila`, `ExtractoColumnaExtra`, `RevisionExtractoEstado`, `IaUsoUsuario`, `MovimientoEsperado`, `Conciliacion` | `Models/Entities.cs:213-243, 311-323, 381-421` | Implementar `ISoftDelete` o documentar exclusión |
| MED | Tabla `IMPORTACION_LOTES`, `MOVIMIENTOS_ESPERADOS`, `CONCILIACIONES`, `BACKUP_CLOUD_CONNECTIONS` usan VARCHAR para `Estado` sin CHECK | `Models/Entities.cs:200, 220, 390, 406, 461, 479` | `HasCheckConstraint` con los valores válidos |
| MED | Sin interceptor EF Core que audite todos los SaveChanges; cobertura depende de cada servicio de acordarse de llamar `IAuditService.LogAsync` | `Services/AuditService.cs:34-49` | `SaveChangesInterceptor` que escribe `AUDITORIAS` en cualquier INSERT/UPDATE/DELETE de tablas financieras |
| MED | `Migrations/20260626180000_ReduceUserRolesToThreeTypes.cs:20-22` hace `DROP TYPE rol_usuario; CREATE TYPE … AS ENUM`. Funciona pero si RLS function ya referencia el viejo tipo, el estado intermedio puede romperse | migración | Documentar en `LOG_ERRORES_INCIDENCIAS.md`. Idealmente recrear con `ALTER TYPE` en lugar de drop/create |
| MED | Faltan índices `(UsuarioId, TitularId)` en `PERMISOS_USUARIO` y `(EntidadTipo, EntidadId)` en `AUDITORIAS` | `Data/AppDbContext.cs:271-273, 325-330` | Crear índices |
| MED | `USUARIO_EMAILS` permite varios `EsPrincipal=true` por usuario | `Models/Entities.cs:33-39` | Índice parcial único `WHERE es_principal = true` |
| MED | N+async también en `ConciliacionService.ApplyCuentaScope` (subquery EXISTS por cada permiso) | `Services/ConciliacionService.cs:370-400` | HashSet en memoria + un solo IN-list |
| MED | `PlazoFijo.InteresPrevisto` mapeado `(18,2)` — si es %, debería ser `(18,8)` como `TipoCambio.Tasa` | `Data/AppDbContext.cs:159` | Confirmar intención y ajustar |
| MED | `PlazoFijoService` envía 1 email por plazo vencido — “email storm” con muchos plazos en el mismo día | `Services/PlazoFijoService.cs:42-91, 168-207` | Agrupar por destinatario, 1 email resumen diario |
| LOW | `TiposCambio.ResolveRate` permite ciclos, división por cero si Tasa <= 0 | `Services/TiposCambioService.cs:392-403` | Descarte explícito en rama inversa |
| LOW | Double-creates de `PermisosUsuario` posibles sin UNIQUE composite sobre `(UsuarioId, CuentaId IS NULL AND …)` | `Data/AppDbContext.cs:271-273` | UNIQUE parcial por ámbito |
| LOW | `AUDITORIAS.ValorAnterior/ValorNuevo` son `text` — pérdida de precisión decimal | `Models/Entities.cs:295-309` | `numeric(18,8)` o JSON tipado |
| LOW | `DeletedById` en `USUARIOS` es FK sin auto-null; borrado del referenciado bloquea | `Models/Entities.cs:30` | `OnDelete(SetNull)` |

---

## 6 · Workflows críticos

### 6.1 Importación (Excel/CSV paste)
- 5 MB de cap, 50.000 filas de cap — bien.
- Dedupe por fingerprint `(cuenta, fecha, monto, saldo, concepto_norm)` idempotente.
- Detección de separador con scoring aceptable, podría confundir cuando se mezclan tabs/comas en una sola tabla. (`ImportacionService.cs:1378-1395`.)
- **Bug:** no valida que la `Divisa` del archivo coincida con la `Divisa` de la cuenta; un formato mal seleccionado contamina la cuenta con moneda incorrecta. (`ImportacionService.cs:518-747`.) — **MED**.

### 6.2 Conciliación
- Matching por igualdad exacta de monto — no tolera comisiones. **HIGH** (H3).
- Score base 60 + ajuste por fecha (hasta +20) → llega a 80 sin match textual si fecha coincide, riesgo de falsos positivos en cuentas con muchos movimientos pequeños. **MED**.
- Maker-checker débil: cerrar tu propia conciliación sólo dispara notificación admin, no impide. **MED**.
- `ListarConciliaciones` con top 500 sin paginación real ni filtros de fecha. Funciona hoy, escala mal. **LOW**.

### 6.3 Tipos de cambio
- Caché en memoria 5 min TTL — bien. Se invalida tras escritura manual o sync.
- `SincronizarTiposCambioAsync` distingue poco entre API vacía (rate-limit soft) y 429. **LOW**.
- `ResolveRate` BFS — correcto, pero falta control de división por 0 si Tasa degenera. **LOW**.
- Conversión `double -> decimal` puede arrastrar imprecisión IEEE-754; considerar parseo directo a string si el API lo entrega. **MED**.

### 6.4 Alertas / notificaciones
- Cooldown global (no por cuenta) — **HIGH** (H7).
- Evaluación cada escritura de extracto → N+1 con import masivo. Mover a batch al final del lote. **MED**.
- Sin “snooze” / “resolver” para alertas de comisión y seguro. **INFO**.
- `NotificacionesAdmin` se crean desde 4 sitios sin service compartido — refactor. **LOW**.

### 6.5 Backups + cifrado + Google Drive
- Cifrado AES-GCM con nonce por chunk (bien hecho).
- Pérdida de clave = backups irrecuperables — **CRITICAL** (C2).
- Retención borra local pero deja `BackupCloudCopies` huérfanas en Drive. **MED**.
- Restore desde Drive no verifica SHA. **HIGH** (H8).
- Re-import tras revertir deja fingerprint activo y el archivo re-sube se ignora. **MED** (UX confuso).

### 6.6 Auditoría
- Sólo `AuthService`, `ImportacionService`, `ConciliacionService` escriben en `AUDITORIAS`. **Falta coverage** para extracto, plazos fijos, alertas, configuración. **MED**.
- `AuditService.LogAsync` no está acoplado a la transacción del negocio — log sobrevive a rollback. **MED**.
- Sin IP proxy-aware: detrás de un proxy real, todas las IPs son 127.0.0.1. **LOW**.

### 6.7 Hangfire / jobs
- `BackupSchedulerJob` (*/15 cron) + write `last_started_utc` antes de empezar — si muere, siguiente occurrence se pierde. **MED**.
- `AutoUpdateJob` cron `17 * * * *` se desperdicia porque la guarda ejecuta una vez al día. **LOW** — cambiar a `"17 3 * * *"`.
- `BackupWeeklyJob` existe pero `Program.cs:286` lo elimina (`RemoveIfExists`) — archivo huérfano, considerar borrar. **INFO**.

### 6.8 Watchdog
- Ver C3 (semáforo) y §3.2 (firma de paquete).
- `StopApiServiceSafeAsync` espera 2s tras `service.Stop()` — insuficiente para requests activos. Hacer polling con `service.Status == Stopped` y timeout. **MED**.
- `SolicitarActualizacionAsync` acepta 2xx genérico, debería aceptar específicamente `202 Accepted` para update y `200` para restore. **LOW**.

---

## 7 · DX / calidad de vida

### 7.1 Desarrollador
- **FluentValidation no está cableado** a pesar de estar en `csproj:29`. O se usa o se quita.
- **`PassthroughSecretProtector` silent fallback** — un fallo de DI baja a protección cero sin error. Marcar como `required` en DI.
- **Permission check duplicado inline** en `ExtractosController.cs:699-887` mientras existe `IUserAccessService`. Consolidar.
- **`theme` y `data-theme` mantenidos en dos sitios** (main.tsx y uiStore). Centralizar.
- **Roles string literals** `'ADMIN' | 'GERENTE' | 'EMPLEADO'` repetidos en TS en muchos sitios. Exportar un enum.
- **Versión del app** duplicada en `Sidebar`, `TopBar`, `package.json`, `VERSION`, `Directory.Build.props`. Una sola fuente vía `import.meta.env.VITE_APP_VERSION`.
- **Sin `appsettings.Schemas.md` automático** — `Documentacion/Versiones/` documenta cada cambio pero no hay un ERD vivo. Considerar `dotnet ef dbcontext info` o similar.

### 7.2 Internacionalización
- Strings hardcoded mezclan ES/EN en `ImportacionPage`, `DashboardTitularPage`, mensajes de error.
- Selectores `pais_nombre || 'Sin pais'` con espacio en `pais` en lugar de `país`. ESLint rule para evitar.

### 7.3 Accesibilidad añadida
- Foco con `outline-offset: 2px` global.
- Focus visible en todos los `<button>` primarios y secundarios (puede faltar hoy).
- Toast: aria-live correcto (`ToastViewport.tsx:84-89`); añadir `role="status"` en los no críticos.
- El sidebar versión sin etiqueta visible — añadir `<span class="visually-hidden">` alternativa.
- Faltan descripciones en diálogos de revocación MFA y eliminación de usuario (`UsuariosPage.tsx:380-381`).

---

## 8 · Top 10 acciones (orden de prioridad)

| # | Sever | Acción | Esfuerzo | Impacto |
|---|---|---|---|---|
| 1 | CRITICAL | `IntegrationAuthMiddleware`: si scopes.Count == 0 → denegar | 30 min | Cierra el peor agujero |
| 2 | CRITICAL | `BackupEncryptionService`: nunca regenerar clave silenciosamente | 2 h | Evita pérdida total de backups |
| 3 | CRITICAL | `WatchdogOperationsService`: try/catch que suelte el lock si el state-store falla | 30 min | Evita watchdog muerto |
| 4 | HIGH | `DashboardService`: bulk convert + tolerar tasa faltante sin abortar | 4 h | Rendimiento + UX |
| 5 | HIGH | `ConciliacionService`: tolerancia configurable de monto + subir índice cubriente | 6 h | Conciliación útil con comisiones |
| 6 | HIGH | Concurrencia optimista `xmin` en extractos + revisión + conciliación | 1 día | Cero lost-writes en revisión |
| 7 | HIGH | `CONFIGURACION` secretos cifrados con `ISecretProtector` | 1 día | Backups no exponen API keys |
| 8 | MED | Audit interceptor que cubra todos los SaveChanges | 1 día | Cumplimiento / forensics |
| 9 | MED | Soft-delete en `ImportacionLoteFila`, `ExtractoColumnaExtra`, `RevisionExtractoEstado`, `IaUsoUsuario`, `MovimientoEsperado`, `Conciliacion` | 4 h | Coherencia con el patrón |
| 10 | MED | Frontend: versión única via `import.meta.env`, alertas tras cambio de password, chat IA con retry visible, modal auditoría con cancel | 4 h | UX |

---

## 9 · Recomendaciones de proceso

1. **Habilitar FluentValidation** o eliminar la dependencia — hoy se ignora.
2. **Pipeline de verificación**:
   - Backend: `dotnet build`, `dotnet ef migrations script` para revisar SQL generado, `dotnet test` (incluyendo `tests/AtlasBalance.API.Tests/` que ya cubre extractos, conciliación, exportación, auditoría CSRF).
   - Frontend: `npm run lint`, `npm run build`, `npm run test:e2e` contra la `playwright.config.ts` ya definida.
3. **Inspección visual**: hoy el linter pasa pero no hay screenshot diff ni Playwright con assertions visuales. Añadir `@playwright/test` visuales para `DashboardPage`, `ExtractosPage`, `ConciliacionPage`, `BackupsPage`.
4. **Documentación**: el log de errores de incidencias (mencionado en `CLAUDE.md`) debería poblarse con los 3 CRITICAL y los HIGH; así no se vuelven a repetir.
5. **Política de secretos**: mover toda la lectura de secrets a un solo servicio (`SecretProtector` + tabla propia), y centralizar la lista de claves encriptadas al arrancar.

---

## 10 · Lo que NO es un problema

- Inyección SQL: **cero superficie**. `ExecuteSql*` parametrizado al 100 % en paths revisados.
- Auth JWT: hardening correcto (issuer/audience/lifetime/clock-skew).
- CSRF: cobertura buena, exclusiones quirúrgicas, compare constant-time.
- Rate limit: presente en login, MFA y bearer de integración.
- Soft-delete filter: implementación limpia.
- Naming BD: UPPER_SNAKE / lower_snake consistente, las migraciones no rompen convención.
- Hardening de cabeceras: CSP, COOP, XFO, Referrer-Policy, Permissions-Policy, HSTS — fuerte.
- CORS dev-only: correcto.
- Hangfire dashboard dev-only: correcto.
- `AiMessageContent`: render seguro por `React.createElement`, sin `dangerouslySetInnerHTML`.
- Auditoría de IP y forward headers: presente en API; pendiente de cablear proxy real.
- `Documentacion/DOCUMENTACION_CAMBIOS.md` workflow: riguroso; cada cambio lleva entrada.

---

**Verificación**: este informe se elaboró leyendo los archivos sin modificarlos. Las rutas citadas (formato `directorio/archivo:línea`) apuntan a lugares efectivamente abiertos durante el review. Las severidades son propuesta inicial — el equipo debe revisarlas antes de promover a tickets.
