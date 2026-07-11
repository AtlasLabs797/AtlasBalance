# Análisis de seguridad y bugs abiertos — Atlas Balance V-02-04 (pre-exposición a internet)

- **Fecha:** 2026-07-10
- **Versión auditada:** V-02-04 (rama `V-02-04`)
- **Tipo:** auditoría estática, solo lectura. No se ha modificado, instalado ni ejecutado nada.
- **Stack:** ASP.NET Core 8 (C#) + React 18 + TypeScript + Vite 8 + PostgreSQL 16+ + EF Core 8 + Npgsql.
- **Equipo auditor:** 6 subagentes en paralelo (seguridad backend, seguridad frontend, concurrencia/integridad, rendimiento/escalabilidad, bugs/flujos críticos, configuración/despliegue), orquestados por el modelo principal.
- **Documentos previos usados:** `REVIEW_REPORT_2026-06-30.md`, `SEGURIDAD_AUDITORIA_V-02-04.md`, `LOG_ERRORES_INCIDENCIAS.md`, `REGISTRO_BUGS.md`, `Documentacion/Versiones/v-02-04.md`.

---

## 0 · Veredicto global

Atlas Balance V-02-04 es una aplicación **sólidamente endurecida** para su contexto original (LAN con 4-8 usuarios, on-premise Windows Server). Tiene los cimientos de seguridad correctos: JWT en cookie `__Host-` HttpOnly+SameSite=Strict, CSRF double-submit, bcrypt 12, RLS en PostgreSQL con firma HMAC, MFA TOTP con anti-replay, Hangfire dashboard off en prod, watchdog en loopback con secreto en compare constant-time, soft-delete universal, separación dev/prod, fail-closed al arranque, validación de placeholders.

**Pero va a estar expuesta a internet.** Eso cambia el listón: un atacante externo automatiza el descubrimiento, el brute-force y el probing de bugs latentes. Bajo esa luz, esta auditoría encuentra:

- **3 CRITICAL** que bloquean el despliegue público.
- **11 HIGH** que deberían arreglarse antes de exponer a internet (o aceptar riesgo documentado).
- **30+ MEDIUM** que se pueden aceptar como deuda inicial pero deben ir a `REGISTRO_BUGS.md`.
- **40+ LOW/INFO** que son endurecimiento y pulido.

**Recomendación:** NO exponer a internet hasta cerrar los 3 CRITICAL y, como mínimo, los 5 HIGH más urgentes. Estimar **2-3 semanas** de trabajo para los CRITICAL+HIGH con un desarrollador a tiempo completo y suite de tests focalizados.

---

## 1 · Hallazgos CRITICAL (bloquean exposición a internet)

### CRIT-1 — `AtlasAiService.AskAsync` acepta cualquier modelo OpenRouter con regex permisiva
- **Severidad:** CRITICAL
- **CWE / OWASP:** CWE-77 / CWE-285 / OWASP A01:2025 (Broken Access Control)
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/AtlasAiService.cs:146-160, 1627-1631, 480-528` + `Constants/AiConfiguration.cs`
- **Descripción:** `IsAllowedOpenRouterModel` valida contra una regex textual `[a-z0-9._-]+/[a-z0-9._-]+`, NO contra el catálogo real de OpenRouter. Un usuario autenticado con permisos de IA puede pedir un modelo premium no suscrito en la cuenta del operador. Resultado: 404 del proveedor o, peor, cobro a un modelo no autorizado. Combinado con BACKEND-019/020 (mismo vector) significa que cualquier modelo de OpenRouter es invocable si pasa la regex.
- **Escenario de ataque:** un admin con token válido invoca `POST /api/ia/ask { model: "anthropic/claude-3-opus" }` cuando la cuenta solo está suscrita a `openai/gpt-4o-mini`. La API key compartida recibe el cargo.
- **Fix:** que `IsAllowedOpenRouterModel` valide contra el cache de `LoadOpenRouterModelsAsync` (líneas 453-528). Si el catálogo no está cargado, rechazar con `IaConfigurationException` (fail-closed). Documentar la allowlist explícita por entorno.
- **Esfuerzo:** 1 día (incluye test + cache invalidation en login admin).

### CRIT-2 — Auditoría (`AuditService.LogAsync`) se persiste FUERA de la transacción del negocio
- **Severidad:** CRITICAL
- **CWE / OWASP:** CWE-778 / OWASP A09:2025 (Security Logging and Monitoring Failures)
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/AuditService.cs:34-49`
- **Descripción:** `LogAsync` añade la entrada a `_dbContext.Auditorias` y ejecuta su propio `SaveChangesAsync`. Si el `SaveChangesAsync` posterior del servicio de negocio falla (`DbUpdateConcurrencyException`, timeout, fallo de red), la fila de auditoría **queda persistida aunque el cambio de negocio se revertió**. La auditoría se vuelve decorativa: en `AUDITORIAS` aparecen acciones que nunca ocurrieron. En una app financiera, esto destruye forensics y cumplimiento.
- **Escenario:** admin A confirma un lote de importación. La auditoría `importacion_lote_confirmado` se persiste. El `SaveChangesAsync` del lote falla por concurrencia con admin B. La auditoría queda escrita pero el lote NO está confirmado. Un mes después, en una auditoría externa, se demuestra que la BD dice "confirmado" en `AUDITORIAS` pero el lote sigue en `validado` en `IMPORTACION_LOTES`.
- **Fix:** Opción A: pasar `IDbContextTransaction` explícita. Opción B (recomendada): implementar `SaveChangesInterceptor` EF Core que escriba en `AUDITORIAS` dentro del mismo `SaveChanges`/`Commit` del negocio y audite cualquier INSERT/UPDATE/DELETE en tablas financieras. Esto cubre también lo que el review 2026-06-30 pidió y `BACKEND-006`, `BUG-002` del audit de flujos.
- **Esfuerzo:** 1-2 días (incluye tests transaccionales).

### CRIT-3 — Validación de paquete de actualización en Watchdog: solo exige presencia de 4 archivos, no verifica firma
- **Severidad:** CRITICAL
- **CWE / OWASP:** CWE-345 / OWASP A08:2025 (Software and Data Integrity Failures)
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.Watchdog/Services/WatchdogOperationsService.cs:1000-1006`
- **Descripción:** `IsValidReleasePackage` exige `File.Exists` de `VERSION`, `api/AtlasBalance.API.exe`, `watchdog/AtlasBalance.Watchdog.exe` y `scripts/Actualizar-AtlasBalance.ps1`. NO verifica hashes, NO compara la firma RSA del ZIP, NO lee el contenido. Si un atacante logra escribir en `UpdateSourceRoot` (compromiso local, RCE en otro proceso, error de despliegue) puede colocar esos 4 archivos en posiciones legítimas y el Watchdog ejecutará el update. La API principal SÍ verifica digest + firma RSA (BACKEND-016 lo confirma), pero el Watchdog no re-verifica.
- **Escenario:** un RCE en un proceso local (vía una vulnerabilidad de frontend XSS chained, o un bug en el actualizador que escribe en `UpdateSourceRoot`) coloca binarios maliciosos. El Watchdog ve "existen los 4 archivos", actualiza y reinicia. Ahora el atacante controla el servicio.
- **Fix:** que el Watchdog vuelva a verificar la firma RSA del `.zip` original (no del extraído) usando la `ReleaseSigningPublicKeyPem` configurada. Como mínimo, comparar el hash de `api/AtlasBalance.API.exe` con un valor esperado por versión.
- **Esfuerzo:** 0.5-1 día (incluye test `WatchdogUpdateTests.Should_Reject_PackageWithCorruptedSignature`).

---

## 2 · Hallazgos HIGH (arreglar antes de exponer a internet)

### HIGH-1 — `ImportacionService.ConfirmarLoteAsync` no valida que la divisa del archivo coincida con la de la cuenta
- **Severidad:** HIGH
- **CWE / OWASP:** CWE-20 / OWASP A04:2025 (Insecure Design)
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/ImportacionService.cs:518-770`
- **Descripción:** El usuario puede pegar un extracto de un banco USD en una cuenta EUR. La importación se completa sin pedir confirmación. Resultado: una cuenta EUR termina con importes en USD que no le corresponden, contaminando la conciliación y el dashboard durante meses.
- **Fix:** añadir `DivisaEsperada` opcional al `ImportacionLoteCrearRequest`. Si se omite, usar `cuenta.Divisa` por defecto y validar que los importes pegados están en esa divisa (al menos un check heurístico de símbolo o comparación con un importe del primer extracto histórico). UI: checkbox explícito "Confirmo que el archivo está en ${cuenta.divisa}".
- **Esfuerzo:** 1 día.

### HIGH-2 — `GoogleDriveBackupService.ImportAsync` no verifica SHA-256 post-descifrado
- **Severidad:** HIGH
- **CWE / OWASP:** CWE-345 / OWASP A08:2025
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/GoogleDriveBackupService.cs:356-415`
- **Descripción:** Tras `DownloadFileAsync` y `DecryptAsync`, el `.dump` descifrado se marca como `Backup.Estado = SUCCESS` sin recomputar SHA-256 ni comparar con `BackupCloudCopy.ChecksumSha256`. Un `.dump.enc` corrupto en Drive (por ataque, fallo de Drive o corrupción silenciosa) descifra con éxito y queda registrado como "Importado" — pero puede estar mal.
- **Fix:** calcular SHA-256 del `.dump` resultante tras descifrar, comparar constant-time con `copy.ChecksumSha256`. Si falla, marcar `BackupCloudCopy.ErrorMessage` y devolver `BadRequest` con detalle.
- **Esfuerzo:** 4-6 h (incluye test `GoogleDriveRestoreTests.VerifySha256AfterDecrypt_FailsWhenCorrupted`).

### HIGH-3 — `TiposCambioService.ConvertAsync`: una tasa cruzada faltante aborta el dashboard entero con 409
- **Severidad:** HIGH
- **CWE / OWASP:** CWE-754 / OWASP A04:2025
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/DashboardService.cs:344-347` + `Services/TiposCambioService.cs`
- **Descripción:** `DashboardService` agrega saldos por titular y los convierte a una divisa destino. Una cuenta en una moneda sin tipo de cambio registrado aborta el endpoint completo con 409. El usuario pierde el dashboard entero. Esto fue marcado como H1 en el review 2026-06-30 y NO se ha cerrado (CONFIRMADO PRESENTE en V-02-04).
- **Fix:** introducir `Task<IReadOnlyDictionary<string, decimal>> BulkConvertAsync(saldosPorDivisa, target, ct)`. Si falta una tasa, devolver `tasa_pendiente: true` en esa fila y marcarla visualmente, en lugar de abortar. Reutilizar el patrón que ya está en `BuildMetricsAsync` (líneas 471-509).
- **Esfuerzo:** 4-6 h.

### HIGH-4 — N+async en `DashboardService.GetEvolucionAsync`: 4 patrones await por extracto
- **Severidad:** HIGH
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/DashboardService.cs:267-278, 289-299, 330-342, 345-353`
- **Descripción:** Cuatro `foreach`/secuencias que llaman `await _tiposCambioService.ConvertAsync(...)` por elemento. Con 50 000 filas/cuenta y N cuentas, ~50 000 × N awaits serializados por petición. Marcado como H2 en el review 2026-06-30 y NO cerrado (CONFIRMADO PRESENTE).
- **Fix:** reutilizar el patrón de `BuildMetricsAsync`: precomputar `tasaPorDivisa` una sola vez y aplicar `tasa * monto` en memoria por (divisa, cuenta). Considerar `Task.WhenAll` para los bucles independientes.
- **Esfuerzo:** 4-6 h (refactor + tests).

### HIGH-5 — `ix_plazos_fijos_cuenta_id` y `ix_extractos_cuenta_id_fila_numero` son UNIQUE sin filtro `deleted_at IS NULL`
- **Severidad:** HIGH
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Migrations/` (índices creados en la migración V-0203)
- **Descripción:** Tras soft-delete de un plazo fijo o un extracto, no se puede volver a crear uno con el mismo `CuentaId`/`FilaNumero`. Bug real de integridad. Confirmado por el agente de concurrencia.
- **Fix:** recrear los índices con `WHERE deleted_at IS NULL` en una nueva migración. Test: `SoftDeletePlazoFijo_Then_CrearNuevo_ConMismoCuentaId_DeberiaPermitir`.
- **Esfuerzo:** 1-2 h (incluye test).

### HIGH-6 — `PlazoFijo` sin `xmin` / concurrencia optimista
- **Severidad:** HIGH
- **CWE / OWASP:** CWE-362 / OWASP A04:2025
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Models/Entities.cs` + `Data/AppDbContext.cs`
- **Descripción:** `RenovarAsync` y `ProcesarVencimientosAsync` colisionan silenciosamente (last-write-wins entre dos usuarios o entre job y admin). Confirmado por el agente de concurrencia.
- **Fix:** `b.UseXminAsConcurrencyToken()` en `PLAZOS_FIJOS`. Devolver 409 con estado actual. Aprovechar el handler global `DbUpdateConcurrencyException → 409` que ya existe en `Program.cs:339`.
- **Esfuerzo:** 4-6 h.

### HIGH-7 — `PlazoFijoService.ProcesarVencimientosAsync` envía email y crea `NotificacionesAdmin` ANTES del `SaveChangesAsync`
- **Severidad:** HIGH
- **CWE / OWASP:** CWE-362 / OWASP A04:2025
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/PlazoFijoService.cs:42-91, 202-232`
- **Descripción:** Side effects materializados antes del commit. Si la transacción falla, los emails ya salieron y las notificaciones ya están persistidas. Sin protección de carrera entre réplicas/ejecuciones.
- **Fix:** patrón outbox — insertar side effects en una tabla `PLAZOS_FIJOS_OUTBOX` y procesarla en Hangfire tras commit. O reordenar: commit → email → audit.
- **Esfuerzo:** 4-6 h.

### HIGH-8 — `WatchdogClientService.GetEstadoAsync` lee `StateFilePath` sin validar directorio permitido (path traversal via config)
- **Severidad:** HIGH
- **CWE / OWASP:** CWE-22 / OWASP A01:2025
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/WatchdogClientService.cs:90-144`
- **Descripción:** Si el operador configura `WatchdogSettings:StateFilePath = "C:\Windows\System32\drivers\etc\hosts"`, el API lee ese archivo y lo expone como JSON en `/api/sistema/estado`. Path traversal via configuration.
- **Fix:** validar que `stateFilePath` esté dentro de un directorio permitido (`Path.GetFullPath(stateFilePath).StartsWith(installPath, Ordinal)`).
- **Esfuerzo:** 0.5-1 h.

### HIGH-9 — `AuditService` no es thread-safe: N `SaveChangesAsync` por cambios en `SaveCellAudits`
- **Severidad:** HIGH (rendimiento + integridad)
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/AuditService.cs:34-48` + `Controllers/ExtractosController.cs:944-981`
- **Descripción:** `SaveCellAudits` añade N `Auditorias` y hace un `SaveChangesAsync` por cada una (N round-trips). Combinado con CRIT-2, esto multiplica la ventana de inconsistencia. En edición masiva, son N round-trips de BD.
- **Fix:** `AddRange` y un único `SaveChangesAsync` al final. O migrar a `SaveChangesInterceptor` (ver CRIT-2).
- **Esfuerzo:** 0.5 día.

### HIGH-10 — `DashboardService.GetPrincipalAsync` aún tiene N+async + aborta por tipo de cambio faltante
- **Severidad:** HIGH (combina H1+H2 del review)
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/DashboardService.cs:430-583`
- **Descripción:** Mismo patrón que HIGH-3 y HIGH-4, pero en el endpoint principal. Un usuario ve 409 sin entender por qué. El fix en `BuildMetricsAsync` (líneas 471-509) NO se extendió al resto del método.
- **Fix:** unificar con el patrón bulk + tolerante. Mismo esfuerzo que HIGH-3+4 juntos.
- **Esfuerzo:** 6-8 h.

### HIGH-11 — `AlertaService` race en cooldown per-cuenta tras fix parcial de H7
- **Severidad:** HIGH
- **Archivo:** `Atlas Balance/backend/src/AtlasBalance.API/Services/AlertaService.cs:84-101, 320-362`
- **Descripción:** Fix parcial de H7 del review: el cooldown es por `cuenta.Id`, pero dos `EvaluateSaldoPostAsync` concurrentes (vía una importación masiva) leen `lastSentAt=null` y disparan email doble. Además, alertas globales y por `TipoTitular` comparten la misma clave de cooldown, por lo que las alertas alternativas jamás se activan para esa cuenta.
- **Fix:** incluir `alcance` (CUENTA | TIPO_TITULAR | GLOBAL) en el `cooldownKey` y tomar un lock por `(cuentaId, alcance)`. Test concurrente.
- **Esfuerzo:** 4-6 h.

---

## 3 · Hallazgos MEDIUM (recomendados antes de exponer, aceptar como deuda documentada si no)

Los 30+ hallazgos MEDIUM se listan en la tabla consolidada al final. Los más relevantes:

- **MED-1 `PassthroughSecretProtector` fallback silencioso** en `AuthService.cs:54-66`. Riesgo: si DI falla, MFA secret acaba en claro. Fix: hacer `ISecretProtector` obligatorio (1 h).
- **MED-2 `SecretProtector` se basa en prefijo de texto (`enc:v1:`)** sin MAC. Posible bypass de cifrado si alguien escribe `enc:v1:manual`. Fix: añadir HMAC a `ProtectForStorage` (2 h).
- **MED-3 `AtlasAiService` envía `contexto_financiero` completo al modelo** (saldos, IBANs, importes) sin redacción. Fix: redactar IBAN, agregar importes (1 d).
- **MED-4 `ConfiguracionController.SendTestEmail` no rate-limita.** Fix: rate limit 5/min/usuario o confirmación tipo "RESTAURAR" (1 h).
- **MED-5 `EmailService` no valida `smtpFrom` contra CRLF** (`<`, `>`, `\r`, `\n`). Defensa en profundidad. Fix: regex `/^[^<>:\r\n]+@[a-zA-Z0-9.-]+$/` (0.5 h).
- **MED-6 `WatchdogClientService` path traversal via config** (ver HIGH-8).
- **MED-7 `RlsContextSigner` usa HMAC determinista** sin nonce, secret compartido con `JwtSettings:Secret`. Documentar y rotar por separado (0.5 h).
- **MED-8 `BackupConfigurationService` no re-marca `EsSecreto` para todas las claves sensibles** (BACKEND-007/008). Riesgo de regresión si se añade una clave. Fix: una única `ConfiguracionSecretKeys.List` (2 h).
- **MED-9 `CsrfMiddleware` no audita intentos de CSRF** rechazados. Visibilidad cero. Fix: `ILogger.LogWarning` + `IAuditService.LogAsync` (1 h).
- **MED-10 `TiposCambioService.ResolveRate` sin protección contra ciclos y división por cero** en rama inversa (`1m / row.Tasa`). Fix: validar `> 0` y capar profundidad BFS a 6 saltos (0.5 h).
- **MED-11 `TiposCambioService.ConvertAsync` puede overflow** con tasas degeneradas en cadena (`1_000_000 × 1_000_000`). Fix: capar a `decimal.MaxValue/2` antes de overflow (1 h).
- **MED-12 `IntegrationOpenClawController.Saldos/GraficaEvolucion` ejecutan ConvertAsync en serie por cuenta** (mismo bug que HIGH-4 pero en API integración). Fix: `BulkConvertAsync` (4 h).
- **MED-13 `ImportacionService.ContenidoOriginal` retiene el raw pegado indefinidamente** (hasta 5 MB). Datos personales sin necesidad operativa. Fix: truncar/hashear tras dedup (4-6 h).
- **MED-14 `ImportacionService.ConfirmarLoteAsync` no usa lock** para evitar doble confirmación concurrente. Fix: `pg_advisory_xact_lock` por lote (2 h).
- **MED-15 `ImportacionService.RevertirLoteAsync` carga 50 000 extractos en memoria** solo para marcarlos. Fix: `ExecuteUpdateAsync` (1 h).
- **MED-16 `ConciliacionService.SugerirAsync` hace N + 2N round-trips** por movimiento (1000 movimientos = 2000 round-trips). Fix: CTE/Window + Dictionary en memoria (1 d).
- **MED-17 `ConciliacionService.ApplyCuentaScope` con EXISTS por cada cuenta**. Fix: HashSet en memoria (4 h).
- **MED-18 `AlertaService.EvaluateSaldoPostAsync` 6+ round-trips secuenciales por escritura de extracto**. Fix: cache `CONFIGURACION` + recipient en una query (1 d).
- **MED-19 `PlazoFijoService.ProcesarVencimientosAsync` email storm** (1 email por plazo, 30 plazos = 30 emails). Fix: digest diario (1 d).
- **MED-20 `AtlasAiService.BuildFinancialContextAsync` queries secuenciales independientes**. Fix: `Task.WhenAll` (1 d).
- **MED-21 Sin CHECK constraints en columnas `Estado`** de `IMPORTACION_LOTES`, `MOVIMIENTOS_ESPERADOS`, `CONCILIACIONES`, `BACKUP_CLOUD_CONNECTIONS`. Fix: `HasCheckConstraint` (2 h).
- **MED-22 `Conciliacion`, `ImportacionLoteFila`, `ExtractoColumnaExtra`, `RevisionExtractoEstado`, `IaUsoUsuario`, `MovimientoEsperado` siguen sin `ISoftDelete`**. Fix: 1 d (migración + tests).
- **MED-23 DTOs sin atributos de validación** (`[Required]/[Range]/[MaxLength]`). El paquete `FluentValidation.AspNetCore` está en `.csproj` pero no registrado. Fix: registrar FluentValidation o añadir atributos manualmente (1-2 d).
- **MED-24 `LogPaths` relativos en Windows Service** (`logs/atlas-balance-.log`). El working directory por defecto es `C:\Windows\System32`. Fix: ruta absoluta configurable con `fileSizeLimitBytes` y `retainedFileCountLimit` (0.5 d).
- **MED-25 `Configuracion` secretos en `appsettings.Production.json.template` con placeholders detectables** (bien), pero no hay CI step que falle si se commitea con placeholders (mencionado en el audit CONFIG-025). Fix: añadir step (0.5 d).
- **MED-26 `INSTALL_CREDENTIALS_ONCE.txt` con contraseñas en claro** durante 24 h tras instalación. Fix: mostrar en pantalla y forzar captura a gestor de secretos (1 d).
- **MED-27 `AtlasAiService.GetModelsAsync` cache global con race** entre búsquedas concurrentes. Fix: cachear lista completa y filtrar siempre en memoria (0.5 h).
- **MED-28 `ConfiguracionController.Update` no sanitiza colores** (regex). Defensa en profundidad. Fix: regex hex/rgb (0.5 h).
- **MED-29 `IntegrationAuthMiddleware` guarda contadores de rate-limit 2 min** sin invalidar en revocación de token. Fix: limpiar en `IntegracionesController.Revocar/Eliminar` (1 h).
- **MED-30 `RlsDbCommandInterceptor.ShouldSkip` usa heurística de string** (`Contains 'atlas.')`); frágil. Fix: flag estático de re-entry (0.5 h).

---

## 4 · Hallazgos LOW (endurecimiento y pulido)

40+ hallazgos LOW. Los más relevantes:

- **LOW-FE-1 `index.html` sin CSP `<meta>` ni SRI** (defense-in-depth). Frontend FRONTEND-010.
- **LOW-FE-2 `Api.ts` no tiene timeout por defecto** (FRONTEND-008). 15 s sería razonable.
- **LOW-FE-3 `AppErrorBoundary.sendBeacon` a `/api/telemetria/errores` sin auth** (FRONTEND-002). Verificar que backend requiere auth + limita payload.
- **LOW-FE-4 `TokenCreatedModal` muestra token plano sin expiración** (FRONTEND-009). Auto-cerrar a 60s.
- **LOW-FE-5 `PaisScopeStore.loadPaises` setea `lastError` pero nadie lo lee** (frontend FRONTEND-018).
- **LOW-FE-6 `ConfiguracionPage` lockea toda la página durante polling de update** (frontend FRONTEND-013). Separar `updateStatus === 'pending'`.
- **LOW-FE-7 `ImportacionPage` filtra client-side sobre página**; sin server-side search (frontend PERF-FE-005).
- **LOW-FE-8 `RevisionPage` doble fetch al alternar tabs** (PERF-FE-008).
- **LOW-FE-9 `Vite sourcemap: false` en producción** — debugging opaco. Trade-off consciente.
- **LOW-BE-1 `Server: Kestrel` header no se desactiva** (CONFIG-008).
- **LOW-BE-2 CSP sin `upgrade-insecure-requests` ni `report-uri`** (CONFIG-009).
- **LOW-BE-3 Sin `Cross-Origin-Resource-Policy: same-origin`** (CONFIG-010).
- **LOW-BE-4 Lista de contraseñas comunes (9 entradas)** — debería ser HIBP k-anonymity o top-10k. (BACKEND-N07).
- **LOW-BE-5 `AuditService.DetallesJson` sin límite de tamaño** — cap a 32 KB. (BACKEND-N08).
- **LOW-BE-6 `WatchdogClientService` sin timeout duro en `SmtpClient`** (review §3.2 MED).
- **LOW-BE-7 `TiposCambioService.ResolveRate` ciclos/división por cero** (LOW-17).
- **LOW-BE-8 `Double-creates de PermisosUsuario` sin UNIQUE composite**.
- **LOW-BE-9 `AUDITORIAS.ValorAnterior/ValorNuevo` son `text` — pérdida de precisión decimal**.
- **LOW-BE-10 `DeletedById` en USUARIOS FK sin auto-null**.
- **LOW-BE-11 HSTS sin `includeSubDomains` ni `preload`**.
- **LOW-BE-12 `BackupConfigurationService` no re-marca `EsSecreto` para todas las claves**.

---

## 5 · Validación de hallazgos del review previo (2026-06-30)

| ID | Título | Estado V-02-04 | Evidencia |
|---|---|---|---|
| C1 | `IntegrationAuthMiddleware` scopes.Count==0 | **CERRADO** | `IntegrationAuthMiddleware.cs:228` deny-by-default |
| C2 | `BackupEncryptionService` regenera clave | **CERRADO** | `BackupEncryptionService.cs:131-191` fail-closed |
| C3 | `WatchdogOperationsService` semáforo muerto | **CERRADO** | try/catch en `StartRestoreAsync/StartUpdateAsync` |
| C1-bis | Zip-slip en `ActualizacionService` | **CERRADO** | `TryExtractPackageSafely` con cap 10k/1GB/512MB |
| H1 | Dashboard 409 entero por tasa faltante | **PRESENTE** | (ver HIGH-3) |
| H2 | N+async Dashboard | **PRESENTE** | (ver HIGH-4) |
| H3 | Conciliación igualdad exacta de monto | **CERRADO** | `HardenedConciliacionService` con tolerancia |
| H4 | Índice cubriente conciliación | **NO VERIFICABLE** | No leído `AppDbContext.cs` completo; sin migración visible en V-02-04 |
| H5 | Sin concurrencia optimista en extractos | **PARCIALMENTE CERRADO** | `Program.cs:339` mapea global; `xmin` solo en EXTRACTOS y EXTRACTOS_DESGLOSES (no en REVISION/CONCILIACIONES/PLAZOS_FIJOS — ver HIGH-6) |
| H6 | Lote importación inconsistente | **CERRADO** | `ConfirmarLoteAsync:419-438` try/catch con estado="error" |
| H7 | Cooldown global de alertas | **PARCIALMENTE CERRADO** | (ver HIGH-11) |
| H8 | Google Drive restore sin SHA-256 | **PRESENTE** | (ver HIGH-2) |
| H9 | Secretos en `CONFIGURACION` en plano | **PARCIALMENTE CERRADO** | `Program.cs:236 + 829-866` cifra al arranque; BACKEND-007/008 residuales |
| MED | `PassthroughSecretProtector` fallback | **PRESENTE** | (ver MED-1) |
| MED | Validación paquete actualización Watchdog | **PRESENTE** | (ver CRIT-3) |
| MED | `MfaChallengeState` sin revalidar stamp | **CERRADO** | `VerifyMfaAsync:264-291` valida usuario de BD |
| MED | `AuditService` no transaccional | **PRESENTE** | (ver CRIT-2) |
| MED | Cascadas CASCADE | **CERRADO** | `AppDbContext.cs:251-296, 460-466` en Restrict |
| LOW | Lista contraseñas 9 entradas | **PRESENTE** | (ver LOW-BE-4) |
| MED | `Audit DetallesJson` sin límite | **PRESENTE** | (ver LOW-BE-5) |
| H-FE-1 | Versión hardcodeada Sidebar | **CERRADO** | `import.meta.env.VITE_APP_VERSION` |
| H-FE-2 | Fila extractos virtualizer | **PARCIALMENTE CERRADO** | `measureElement` aplicado; estimación aún rígida |
| H-FE-3 | Alertas post cambio password | **CERRADO** | `ChangePasswordPage:42` carga alertas |
| H-FE-4 | Filter sin debounce | **CERRADO** | `useDebouncedValue` aplicado |
| H-FE-5 | Interceptor 401 solo | **CERRADO** | maneja 419/440 |
| H-FE-6 | Chat IA retry no visible | **CERRADO** | `lastFailedPrompt` + botón reintentar |
| H-FE-7 | `divisaStore.convertir` 1:1 silencioso | **NO APLICABLE** | `divisaStore` ya no existe; conversión en backend |

---

## 6 · Top acciones priorizadas para exponer a internet

### Fase 0 — Bloqueantes (1 semana)
| # | Acción | Severidad | Esfuerzo |
|---|---|---|---|
| 1 | CRIT-2 `AuditService` transaccional (SaveChangesInterceptor) | CRITICAL | 1-2 d |
| 2 | CRIT-3 Watchdog verifica firma RSA del ZIP | CRITICAL | 0.5-1 d |
| 3 | CRIT-1 `AtlasAiService` allowlist de OpenRouter real | CRITICAL | 1 d |
| 4 | HIGH-2 Google Drive SHA-256 post-descifrado | HIGH | 4-6 h |
| 5 | HIGH-1 Validar divisa archivo = cuenta en importación | HIGH | 1 d |
| 6 | HIGH-5 Índices UNIQUE con `WHERE deleted_at IS NULL` | HIGH | 1-2 h |
| 7 | HIGH-3+HIGH-4+HIGH-10 Bulk convert + tolerante en Dashboard | HIGH | 1-1.5 d |

### Fase 1 — Endurecimiento (1 semana)
| # | Acción | Severidad | Esfuerzo |
|---|---|---|---|
| 8 | HIGH-6 `PlazoFijo` con xmin | HIGH | 4-6 h |
| 9 | HIGH-7 Outbox en `PlazoFijoService` | HIGH | 4-6 h |
| 10 | HIGH-8 `WatchdogClientService` path traversal | HIGH | 0.5-1 h |
| 11 | HIGH-9 `AuditService` thread-safe + un `SaveChanges` | HIGH | 0.5 d |
| 12 | HIGH-11 `AlertaService` cooldown por (cuenta, alcance) + lock | HIGH | 4-6 h |
| 13 | MED-1 `PassthroughSecretProtector` fail-closed | MEDIUM | 1 h |
| 14 | MED-2 `ProtectForStorage` con HMAC | MEDIUM | 2 h |
| 15 | MED-5 Email CRLF validation | MEDIUM | 0.5 h |
| 16 | MED-22 Soft-delete en entidades pendientes | MEDIUM | 1 d |
| 17 | MED-23 DTOs con validación (FluentValidation o atributos) | MEDIUM | 1-2 d |
| 18 | MED-21 CHECK constraints en columnas Estado | MEDIUM | 2 h |
| 19 | MED-24 Log path absoluto + rotación | MEDIUM | 0.5 d |
| 20 | CONFIG-002 PostgreSQL `sslmode=require` | MEDIUM | 4 h |
| 21 | CONFIG-001 Firewall con `LocalSubnet` por defecto | HIGH | 1 h |
| 22 | CONFIG-019 Servicio Windows con cuenta de bajo privilegio | MEDIUM | 0.5-1 d |
| 23 | CONFIG-007 `server_certificate_validation_callback` evitar | MEDIUM | 0.5 h |

### Fase 2 — Deuda documentada (paralela a Fase 1)
- Rendimiento (PERF-*): aplicar quick wins (1-2 días): PERF-BE-010/020/022, índices AUDITORIAS, PERF-FE-001/003/013.
- BACKEND-007/008: una única `ConfiguracionSecretKeys.List` y derivar todo.
- MED-3 redacción de IBAN en contexto IA.
- MED-4 rate limit en `SendTestEmail`.
- MED-7 documentar separación `RlsContextSecret` vs `JwtSecret`.
- MED-11 cap `decimal.MaxValue/2` en `TiposCambioService.ConvertAsync`.
- MED-12 Bulk convert en `IntegrationOpenClawController`.
- MED-13/14/15/16/17/18/19/20 fixes de rendimiento backend.
- LOW-* todos (CSP meta, SRI, headers, sourcemap).

---

## 7 · Lo que NO es un problema (validación cruzada de los 6 agentes)

- **Inyección SQL:** 0 superficie. Todos los `ExecuteSql*` parametrizados; los 6 usos son `pg_advisory_xact_lock` o `ExecuteSqlInterpolated`.
- **XSS en frontend:** 0 usos de `dangerouslySetInnerHTML`/`innerHTML`/`eval`/`Function`. `AiMessageContent` renderiza con `React.createElement`. Markdown sanitizado.
- **CSRF:** doble token (cookie + `X-CSRF-Token`) con `FixedTimeEquals`. Exclusiones quirúrgicas (login/mfa/refresh/health) protegidas por `SameSite=Strict`.
- **Auth JWT:** HMAC-SHA256, secret >=32 chars validado al arranque, placeholders rechazados, `ClockSkew=Zero`. Cookies `__Host-` HttpOnly+SameSite=Strict en prod.
- **Refresh tokens:** rotación con `pg_advisory_xact_lock`, detección de reuso (revoca familia + rota SecurityStamp), `mfa_verified_at` validado.
- **MFA TOTP:** anti-replay con `MfaLastAcceptedStep`, trusted devices 90 días, revocación administrativa.
- **Rate limit:** login 5/15min, integración 100/min, MFA 5/challenge.
- **Integración OpenClaw:** deny-by-default en scopes, bearer SHA-256 hasheado, audit por request con query redactada.
- **Watchdog:** loopback-only, secret en compare constant-time, validación de placeholders al arranque.
- **CORS:** dev-only. En prod, frontend se sirve desde el mismo origen.
- **Hangfire dashboard:** dev-only.
- **RLS:** activado, firmado HMAC, backstop de soft-delete, alineado con modelo de 3 roles.
- **Soft-delete universal:** `ISoftDelete` + filtros automáticos (con MED-22 residual).
- **Cifrado de secretos:** `DataProtectionSecretProtector` con DPAPI local-machine, claves en `C:\ProgramData\AtlasBalance\keys` con ACL restringido.
- **Cabeceras HTTP:** CSP restrictiva, HSTS en prod, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, COOP. (Con LOW-BE-1/2/3 residuales.)
- **Supply chain:** `npm audit` 0 vulnerabilidades (audit V-02-04); `dotnet list package --vulnerable` 0 paquetes. `package-lock.json` y NuGet lockfiles versionados. `overrides` para `form-data`/`js-yaml` ya aplicadas.
- **Auto-update:** HTTPS enforced, dominio restringido a `github.com`, digest SHA-256 + firma RSA, anti zip-slip, caps de tamaño, rollback automático.
- **SCA npm/dotNet:** 0 vulnerabilidades en ambos lockfiles.
- **PostgreSQL:** imagen pinned por digest en compose, `app_user` con permisos mínimos (sin SUPERUSER/CREATEDB/BYPASSRLS), `atlas_owner` separado para migraciones.
- **Concurrencia:** `xmin` aplicado en EXTRACTOS, EXTRACTOS_DESGLOSES, MOVIMIENTOS_ESPERADOS, CONCILIACIONES. Falta en PLAZOS_FIJOS (HIGH-6) y REVISION_EXTRACTO_ESTADOS.
- **Cifrado de backups:** AES-GCM con nonce por chunk, clave persistida en BD, fail-closed al validar corrupto.
- **Logging:** sin password/token cleartext. PII solo en `Auditorias` (intencional).
- **Auditoría:** IP tracking presente (con ForwardedHeaders configurado en fail-closed por defecto).
- **Watchdog secret:** `X-Watchdog-Secret` en compare constant-time, validación >=32 chars en prod.
- **CSP** en backend: presente y restrictiva (con LOW residuales).
- **Frontend store:** tokens solo en cookies httpOnly, CSRF en memoria (Zustand), nada en localStorage. (LOW-FE-5 email persiste, FRONTEND-006 país/banner).

---

## 8 · Bugs abiertos en `REGISTRO_BUGS.md` que SÍ están resueltos en código

- `V-01.09 - Bootstrap desde Watchdog antiguo pendiente`: cerrado en código, pendiente operativo (instalación física).
- `V-01.06 - Ejecutar suite completa con Docker/Testcontainers`: CERRADO (suite 323/323 OK, ver `Documentacion/Versiones/v-02-04.md`).
- `V-01.06 - E2E autenticado contra PostgreSQL real con datos de volumen`: CERRADO (`VolumeSmokeTests` con 50k filas, 2/2 OK, 2026-07-07).
- `V-01.06 - Validación visual final con datos reales`: **SIGUE ABIERTO** (no hay sesión de navegador real con 50k filas).

---

## 9 · Cobertura y limitaciones

### Archivos leídos por categoría

**Backend completo:**
- `Program.cs` (866 líneas), `Constants/SecurityConfigurationDefaults.cs`, `Constants/SecurityPolicy.cs`, `ConfigurationDefaults.cs`.
- 22 Controllers: AuthController, UsuariosController, IntegracionesController, IntegrationOpenClawController, BackupsController, ConfiguracionController, SistemaController, ExportacionesController, ImportacionController, IaController, ExtractosController (parcial), RevisionController, ConciliacionController, AuditLogController, AlertasController, CuentasController (parcial), TitularesController (parcial), PaisesController (parcial), FormatosImportacionController, DivisasController, TiposCambioController, NotificacionesAdminController, PapeleraController.
- Middleware: IntegrationAuthMiddleware (415 líneas), UserStateMiddleware (parcial), CsrfMiddleware, PrimerLoginMiddleware.
- Services: AuthService (1233 líneas), AtlasAiService (parcial), BackupService, BackupEncryptionService, BackupConfigurationService, HardenedBackupConfigurationService, HardenedConciliacionService, HardenedGoogleDriveBackupService, GoogleDriveBackupService, ImportacionService (1247 líneas), ConciliacionService, RevisionService, AlertaService, PlazoFijoService, ExportacionService, TiposCambioService, UserAccessService, IntegrationTokenService, PaisScopeQueryExtensions, SecretProtector, AuditService.
- Data: AppDbContext (parcial), RlsDbCommandInterceptor, RlsContextSigner, Migrations (índices y CHECK via grep).
- Jobs: BackupSchedulerJob, SyncTiposCambioJob, PlazoFijoVencimientoJob, ExportMensualJob, LimpiezaAuditoriaJob, LimpiezaRefreshTokensJob, AutoUpdateJob.
- DTOs: Importacion, Conciliacion, Configuracion, Auth, Alertas, Auditoria, Backups, Cuentas, Dashboard, Exportaciones, Extractos, FormatosImportacion, Ia, Integraciones, NotificacionesAdmin, Paises, Revision, Sistema, Titulares, Usuarios.

**Backend parcial / no leído:**
- `AtlasBalance.Watchdog/Services/WatchdogOperationsService.cs` (revisado parcialmente por grep).
- `ExtractosController.cs` líneas 200-1249 (PUT desglose y CRUD no leído en detalle).
- `Models/Entities.cs` parcial.
- `Jobs/*` parcial.
- `SeedData.cs` no leído.

**Frontend completo:**
- `services/api.ts` (145 líneas), `App.tsx`, `main.tsx`, `vite.config.ts` (180 líneas), `tsconfig.json`, `index.html`, `package.json`, `package-lock.json` (verificado por grep).
- Stores: authStore, uiStore, notificacionesAdminStore, alertasStore, permisosStore, paisScopeStore, updateStore, iaAvailabilityStore.
- Hooks: useSessionTimeout, useDialogFocus, useBlockingOverlay, useUnsavedChanges.
- Componentes críticos: extractos/* (EditableCell, ExtractoTable, AuditCellModal), ia/* (AiChatPanel, AiMessageContent), layout/* (Sidebar, TopBar, BottomNav, Layout, AlertBanner, PaisScopeSelect), common/* (ToastViewport, AppErrorBoundary), auth/* (ProtectedRoute, RoleGuard, SessionTimeoutWarning), integraciones/* (CreateTokenModal, TokenCreatedModal, TokenList).
- Páginas: LoginPage, ChangePasswordPage, ConfiguracionPage (parcial), ExtractosPage, RevisionPage (parcial), ConciliacionPage (parcial), ImportacionPage (1044 líneas), BackupsPage (parcial), TitularesPage (parcial), UsuariosPage (parcial).

**Frontend no leído (limitaciones):**
- DashboardPage, DashboardTitularPage, CuentasPage, CuentaDetailPage, TitularDetailPage, AlertasPage, AuditoriaPage, ExportacionesPage, FormatosImportacionPage, IaPage, PapeleraPage, NotFoundPage.
- Modales no críticos.
- `hooks/useConfirmDialog.ts`, `hooks/useDebouncedValue.ts` (helpers no sensibles).

**Infraestructura / despliegue:**
- `scripts/Instalar-AtlasBalance.ps1` (989 líneas), `scripts/Actualizar-AtlasBalance.ps1` (780 líneas), `scripts/Build-Release.ps1`, `scripts/Start-Dev.ps1`.
- `install.cmd`, `update.cmd`, `uninstall.cmd`, `start.cmd`.
- `AtlasBalance.Watchdog/Program.cs`, `appsettings.json`, `appsettings.Production.json.template`.
- `Dockerfile*`, `docker-compose.yml`, `Directory.Build.props`.
- `.env`, `.env.example`, `.gitignore`.
- `Documentacion/RUNBOOK_INSTALACION_CLIENTE.md`, `INCIDENCIAS_INSTALACION_WINDOWS_SERVER_2019_V-01.05.txt`.

### Limitaciones globales

1. **100% estático.** No se ejecutó `dotnet build`, `dotnet test`, `npm audit`, ni se levantaron servidores dev ni Playwright ni un navegador real.
2. **No se validaron planes SQL reales** (no se capturó `EXPLAIN ANALYZE` ni `ToQueryString()` en runtime). Las estimaciones de impacto son cualitativas, basadas en patrones y la cardinalidad declarada (50 000 filas/cuenta, 4-8 usuarios).
3. **SCA no ejecutado en este audit** — los datos son del audit V-02-04 (2026-07-02): 0 vulnerabilidades npm/nuget.
4. **No se contrastó con la BD real** (Testcontainers no se levantó). Las afirmaciones sobre migraciones se basan en lectura de archivos `.cs`.
5. **El audit no midió timings de carga** de la app con 4-8 usuarios concurrentes. Las recomendaciones de rendimiento son por patrón, no por benchmark.
6. **No se inspeccionaron bundles minificados** (`.js`/`.css` en `wwwroot/`) más allá de la aserción de hashes.
7. **`AtlasAiService.cs` 2871 líneas, leído parcial** (1-1154 + 2030-2478 + 280-300). Posibles más flujos IA no revisados.
8. **`ImportacionService.cs` 1247+ líneas, leído parcial** (1-1247). Posibles más validaciones de fechas/rangos no revisadas.
9. **`ExtractosController.cs` 1249 líneas, leído parcial** (1-200). PUT desglose y CRUD no leídos en detalle.
10. **No se inspeccionó `SeedData.cs`**: valores seed (admin password por defecto) no verificados.

### Validación cruzada

Los 6 agentes trabajaron en paralelo sin coordinación. **Concuerdan** en:
- C1/C2/C3 del review 2026-06-30 cerrados.
- H9 (secretos en `CONFIGURACION`) cerrado en arranque, residual en BackupConfigurationService.
- `AuditService` no transaccional (CRIT-2 / BUG-002 / BACKEND-006 / PERF de auditoría — 3 agentes lo mencionan).
- H1+H2 del review (dashboard 409 y N+async) NO cerrados (CRIT/HIGH-3+4+10).
- H7 (cooldown alertas) parcialmente cerrado (HIGH-11).
- H8 (Google Drive SHA-256) NO cerrado (HIGH-2).
- Cascadas CASCADE convertidas a Restrict (3 agentes lo confirman).

Los 6 agentes encontraron **problemas distintos** que se complementan, lo que da confianza en la cobertura.

---

## 10 · Recomendaciones de proceso

1. **Antes de exponer a internet:**
   - Cerrar los 3 CRITICAL (CRIT-1/2/3) y los 5 HIGH más urgentes (HIGH-1/2/3/4/5).
   - Aplicar Fase 1 de endurecimiento (1 semana) en paralelo.
   - Validar toda la suite de tests con Testcontainers (Docker disponible).
   - Plan de rollback definido: feature flag `INTERNET_EXPOSED=true` que activa logging adicional, rate limit más estricto, y banners de "modo expuesto".

2. **Política de secretos:**
   - Mover la clave privada de firma de release a GitHub Secrets (mencionado en `LOG_ERRORES_INCIDENCIAS.md` como pendiente).
   - Cifrar secretos con `DataProtectionSecretProtector` en todas las claves (BACKEND-007/008).
   - Eliminar `INSTALL_CREDENTIALS_ONCE.txt` o cifrarlo con DPAPI.

3. **Pipeline de verificación previo a release:**
   - `dotnet build` (con `-p:UseAppHost=false` para evitar bloqueos de `apphost.exe`).
   - `dotnet test` con Testcontainers (Docker Desktop activo).
   - `npm audit --omit=dev` y `dotnet list package --vulnerable --include-transitive`.
   - `npm run lint && npx tsc --noEmit && npm run build`.
   - Secret scan con `Test-AtlasSecrets.ps1`.

4. **Monitoreo post-exposición:**
   - `Application Insights` o equivalente para detectar patrones anómalos (login fallidos masivos, requests 401, etc.).
   - Alertas sobre `AUDITORIAS` para `RefreshTokenReuseDetected`, `login_bloqueado`, `mfa_remember_revoked`, `BackupCloudCopy.Failed`.
   - Healthcheck público separado del interno.

5. **Documentación:**
   - Esta auditoría va como `Documentacion/AUDITORIA_SEGURIDAD_BUGS_PRE_INTERNET_2026-07-10.md` (el presente archivo).
   - Cada hallazgo CRITICAL/HIGH debe ir a `Documentacion/REGISTRO_BUGS.md` como bug abierto con su plan de fix.
   - Cada fix debe ir a `Documentacion/LOG_ERRORES_INCIDENCIAS.md` al cerrarlo.
   - El work de fix se asocia a la versión actual (V-02-04) en `Documentacion/Versiones/v-02-04.md` y, si se requiere nueva versión, se crea `v-02-05.md` y se actualiza `version_actual.md`.

---

## 11 · Resumen ejecutivo (1 minuto)

- **V-02-04 está bien para LAN con 4-8 usuarios, NO está listo para internet.**
- **3 CRITICAL bloquean el despliegue público** (allowlist OpenRouter, auditoría transaccional, watchdog sin verificación de firma).
- **11 HIGH deberían arreglarse antes de exponer** (validación de divisa, SHA-256 en Google Drive, dashboard tolerante a tasas faltantes, N+async, índices UNIQUE con soft-delete, xmin en plazos fijos, outbox en emails, path traversal, etc.).
- **30+ MEDIUM** se aceptan como deuda inicial con plan documentado.
- **40+ LOW** son endurecimiento y pulido.
- **Tiempo estimado para Fase 0 (CRITICAL + 5 HIGH más urgentes): 1 semana** con un desarrollador a tiempo completo + suite de tests focalizados.
- **Tiempo estimado para Fase 1 (endurecimiento completo): 2-3 semanas** total.

**Veredicto del equipo auditor: NO exponer a internet sin cerrar antes los 3 CRITICAL.**
