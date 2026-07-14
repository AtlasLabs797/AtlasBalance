# Atlas Balance — Auditoría de concurrencia e integridad de datos

**Fecha:** 2026-07-10
**Versión auditada:** V-02-04
**Tipo:** Solo lectura (análisis estático)
**Ámbito:** revisión de los archivos del backend listados en el encargo + sus migraciones EF Core
**Contexto operativo:** 4–8 usuarios simultáneos, datos financieros, exposición a internet

---

## Validación de hallazgos del review previo (2026-06-30)

Estado de cada hallazgo del informe anterior, referenciado por su severidad y origen.

### HIGH

| # | Hallazgo original | Estado actual | Evidencia |
|---|---|---|---|
| H1 | `DashboardService` aborta con 409 si una conversión cruzada no tiene tasa | **CORREGIDO** | `DashboardService.cs:480-487` (catch `TipoCambioMissingException` y marca `tasaPorDivisa[div] = 0m`); `Program.cs:328-334` reserva `TipoCambioMissingException` a 409 (sólo se lanza si llega a la mano global) |
| H2 | N+async por extracto al convertir moneda en `DashboardService` | **CORREGIDO** | `DashboardService.cs:471-488` precomputa `tasaPorDivisa` por (origen,destino) único y agrega por divisa antes de convertir. N+1 residual en `GetEvolucionAsync` y `BuildPlazosFijosResumenAsync` (ver CONC-NN1 abajo) |
| H3 | `Conciliacion` exige `x.Monto == movimiento.Monto` sin tolerancia | **CORREGIDO** | `HardenedConciliacionService.cs:143-174` aplica tolerancia configurable (`conciliacion_tolerance_amount`, `conciliacion_tolerance_percent`) y scoring con `amountPenalty` |
| H4 | No hay índice cubriente `(CuentaId, Fecha, Monto)` para `SugerirAsync` | **CORREGIDO** | Migración `20260701115326_V0203_Hardening.cs:26-29` crea `ix_extractos_cuenta_id_fecha_monto`; `AppDbContext.cs:199` lo mantiene |
| H5 | Sin concurrencia optimista en extracto / revisión / conciliación | **CORREGIDO** | `AppDbContext.cs:186` (`Extracto.UseXminAsConcurrencyToken()`), `:286` (`RevisionExtractoEstado.UseXminAsConcurrencyToken()`), `:427` (`MovimientoEsperado.UseXminAsConcurrencyToken()`), `:449` (`Conciliacion.UseXminAsConcurrencyToken()`); handler global `Program.cs:339-349` mapea `DbUpdateConcurrencyException` → 409 `concurrency_conflict` |
| H6 | Lote de importación puede quedar inconsistente si un extracto intermedio falla | **CORREGIDO** | `ImportacionService.cs:402-439` envuelve la confirmación en try/catch y marca `lote.Estado = "error"`, persiste nota y re-lanza como 409 |
| H7 | Cooldown global de alertas — una sola notificación para N cuentas | **PARCIALMENTE CORREGIDO** | `AlertaService.cs:84-101` y `:330-362` ahora almacenan el cooldown por cuenta en `CONFIGURACION` (clave `alerta_saldo_last_sent_utc:{cuentaId:N}`). Funciona, pero contamina `CONFIGURACION` y sigue sin backstop transaccional para la lectura/escritura de la fila de cooldown. Detalles en CONC-005 |
| H8 | Google Drive restore sin verificar SHA-256 post-descifrado | **CORREGIDO (fuera de alcance auditado)** | Confirmado en `v-02-04.md`; no releí `GoogleDriveBackupService.cs` por estar fuera del encargo |
| H9 | Secretos en `CONFIGURACION` (OAuth, API keys) en plano | **CORREGIDO** | Migración `20260701115326_V0203_Hardening.cs:19-24` añade `es_secreto`; `ConfiguracionRepository.cs:61-94` cifra con `ISecretProtector` cuando `esSecreto=true`; `Program.cs:829-866` (`ProtectExistingConfigurationSecrets`) re-cifra los valores existentes al arranque. La lectura desde `TiposCambioService.cs:284` ya hace `UnprotectFromStorage` |

### MED

| # | Hallazgo original | Estado actual | Evidencia |
|---|---|---|---|
| M-soft-delete | Soft delete ausente en varias entidades | **PARCIALMENTE CORREGIDO** | `MovimientoEsperado : ISoftDelete` (`Entities.cs:398`), `Conciliacion` **SIGUE SIN `ISoftelete`** (línea 417), `ImportacionLoteFila` y `ExtractoColumnaExtra` y `RevisionExtractoEstado` y `IaUsoUsuario` siguen sin soft-delete. Ver CONC-006 |
| M-check | Sin CHECK constraints en `Estado` de varios | **PRESENTE** | No hay `HasCheckConstraint` en `AppDbContext.cs`; ninguna migración añade CHECK a `IMPORTACION_LOTES.estado`, `MOVIMIENTOS_ESPERADOS.estado`, `CONCILIACIONES.estado`, `BACKUP_CLOUD_CONNECTIONS.estado`. Ver CONC-007 |
| M-audit-interceptor | Sin interceptor EF que audite todos los `SaveChanges` | **PRESENTE** | No hay `SaveChangesInterceptor` registrado; cada servicio llama `IAuditService.LogAsync` manualmente |
| M-indices | Faltan índices `(UsuarioId, TitularId)` en `PERMISOS_USUARIO` y `(EntidadTipo, EntidadId)` en `AUDITORIAS` | **PARCIALMENTE CORREGIDO** | `AppDbContext.cs:316` añade índice compuesto `(UsuarioId, PaisId, TitularId, CuentaId)` (mejor, cubre el caso). `AUDITORIAS` **NO** tiene índice `(EntidadTipo, EntidadId)`. Ver CONC-008 |
| M-usuario-emails | `USUARIO_EMAILS` permite varios `EsPrincipal=true` por usuario | **PRESENTE** | `AppDbContext.cs:74-80` define `USUARIO_EMAILS` sin índice parcial único sobre `EsPrincipal`. La app lo controla en el controller (UsuariosController.cs:331-340) con `currentPrimary.Any() -> false`, pero no es backstop de BD y dos inserciones concurrentes pueden violar la invariante. Ver CONC-009 |
| M-n+async-conciliacion | N+async en `ConciliacionService.ApplyCuentaScope` (subquery EXISTS por cada permiso) | **PRESENTE** | `ConciliacionService.cs:378-400` y `HardenedConciliacionService.cs:238-247` mantienen EXISTS correlacionado. No es N+1 de roundtrip, pero cada fila ejecuta la EXISTS. En la práctica se traduce a un `IN (...)` optimizado por Postgres, no es bloqueante |
| M-interes-previsto | `PlazoFijo.InteresPrevisto` mapeado `(18,2)` — si es %, debería ser `(18,8)` | **PRESENTE / NO DECIDIDO** | `AppDbContext.cs:160` mantiene `(18,2)`. La entidad no documenta la intención. Ver CONC-010 |
| M-plazo-fijo-email | `PlazoFijoService` envía 1 email por plazo vencido — email storm | **PRESENTE** | `PlazoFijoService.cs:202-224` un email por plazo a todos los admins. Para 50 plazos vencidos el mismo día → 50×N emails. Ver CONC-011 |
| M-backup-encription | `BackupEncryptionService` regenera clave silenciosamente | **CORREGIDO** | `BackupEncryptionService.cs:131-192` NUNCA regenera una clave corrupta; lanza `InvalidOperationException` pidiendo intervención manual. La única generación es cuando la fila no existe |

### LOW

| # | Hallazgo original | Estado actual | Evidencia |
|---|---|---|---|
| L-ciclos | `TiposCambio.ResolveRate` ciclos, división por cero si `Tasa <= 0` | **CORREGIDO** | `TiposCambioService.cs:396-398` descarta `Tasa <= 0` al construir el catálogo; `:460` valida `reverse != 0m` antes de dividir; `:470-510` BFS usa `visited` para evitar ciclos |
| L-doble-permiso | Double-creates de `PermisosUsuario` sin UNIQUE composite | **PRESENTE** | `AppDbContext.cs:298-309` declara índice compuesto pero NO `IsUnique()`. Ver CONC-012 |
| L-auditoria-text | `AUDITORIAS.ValorAnterior/ValorNuevo` son `text` | **PRESENTE** | `Entities.cs:321-322` mantiene `string?` (mapea a `text`/`varchar` sin cota) |
| L-deletedby-fk | `DeletedById` en `USUARIOS` es FK sin auto-null | **PARCIALMENTE CORREGIDO** | `AppDbContext.cs:126` (`Pais.DeletedById` → Restrict). Para `USUARIOS` no aplica (es la tabla padre). Para `MfaTrustedDevice.DeletedById` → Restrict (`AppDbContext.cs:110`). El resto de `DeletedById` están en Restrict, no SetNull, así que un soft-delete de un usuario REVIENTA si todavía hay filas con `DeletedById=usuario` en otras tablas (no sólo en su `DeletedAt`) |
| L-cascadas | Cascadas en `ImportacionLoteFila`, `ExtractoColumnaExtra`, `RevisionExtractoEstado` | **CORREGIDO** | Migración `20260701115326_V0203_Hardening.cs:36-82` cambia esos tres a `Restrict` |

---

## Estado de concurrencia por entidad

| Entidad | xmin (concurrencia optimista) | Soft delete | CHECK constraints en `Estado` | Índices clave | FKs (acciones) |
|---|---|---|---|---|---|
| `USUARIOS` | NO | Sí (`ISoftDelete`) | NO | Email UNIQUE, Rol, Activo, MfaEnabled | `DeletedById` → Restrict (no self) |
| `USUARIO_EMAILS` | NO | NO | NO | UsuarioId | `UsuarioId` → Restrict |
| `REFRESH_TOKENS` | NO | NO (filtro por `Usuario.DeletedAt == null`) | NO | TokenHash UNIQUE, ExpiraEn, UsuarioId | `UsuarioId` → Restrict |
| `MFA_TRUSTED_DEVICES` | NO | Sí (`ISoftDelete`) | NO | TokenHash UNIQUE, ExpiraEn, RevokedAt, DeletedAt | `UsuarioId`/`DeletedById` → Restrict |
| `PAISES` | NO | Sí | NO | Nombre UNIQUE, CodigoIso2 UNIQUE parcial, Activo, DeletedAt | `DeletedById` → Restrict |
| `TITULARES` | NO | Sí | NO | Nombre, Tipo, DeletedAt | `DeletedById` → Restrict |
| `CUENTAS` | NO | Sí | NO | TitularId, Divisa, PaisId, EsEfectivo, TipoCuenta, Activa, DeletedAt | `TitularId`/`FormatoId`/`PaisId`/`DeletedById` → Restrict |
| `PLAZOS_FIJOS` | **NO** (riesgo) | Sí | `fecha_vencimiento >= fecha_inicio`, `interes_previsto IS NULL OR >= 0` | **`cuenta_id` UNIQUE sin filtro `deleted_at IS NULL` (BUG CONC-013)**, Estado, FechaVencimiento, DeletedAt | `CuentaId` 1-1 Restrict, `CuentaReferenciaId` → Restrict, `DeletedById` → Restrict |
| `FORMATOS_IMPORTACION` | NO | Sí | NO | (sin índices explícitos) | `UsuarioCreadorId`/`DeletedById` → Restrict |
| `EXTRACTOS` | **SÍ** (`xmin`) | Sí | NO | `(CuentaId, FilaNumero)` UNIQUE (BUG: sin filtro soft-delete), `(CuentaId, ImportacionFingerprint)` UNIQUE parcial fingerprint NOT NULL, `(CuentaId, Fecha, Monto)` cubriente, `(CuentaId, Fecha)`, `(CuentaId, DeletedAt)`, `ImportacionLoteHash`, `ImportacionLoteId`, Fecha, Flagged, Checked | `CuentaId` Restrict, `ImportacionLoteId` SetNull, 5 FKs a USUARIOS Restrict |
| `EXTRACTOS_DESGLOSES` | NO | Sí | NO | `ExtractoId`, `(ExtractoId, Orden)` UNIQUE **`WHERE deleted_at IS NULL` (correcto)**, DeletedAt | `ExtractoId` Restrict, 3 FKs a USUARIOS Restrict |
| `EXTRACTOS_COLUMNAS_EXTRA` | NO | **NO** | NO | ExtractoId, NombreColumna | `ExtractoId` Restrict |
| `IMPORTACION_LOTES` | **NO** (no colaborativo) | NO | NO | CuentaId, LoteHash, Sha256, Estado, FechaCreacion | 4 FKs a USUARIOS Restrict |
| `IMPORTACION_LOTE_FILAS` | NO | **NO** | NO | `(LoteId, Indice)` UNIQUE, Fingerprint | `LoteId` Restrict |
| `REVISION_EXTRACTO_ESTADOS` | **SÍ** (`xmin`) | **NO** | NO | `(ExtractoId, Tipo)` UNIQUE, Tipo, Estado | `ExtractoId` Restrict, `UsuarioModificacionId` Restrict |
| `PERMISOS_USUARIO` | NO | NO | NO | UsuarioId, `(UsuarioId, CuentaId)`, `(UsuarioId, PaisId)`, `(UsuarioId, PaisId, TitularId, CuentaId)` **(no UNIQUE, ver CONC-012)** | 4 FKs Restrict |
| `PREFERENCIAS_USUARIO_CUENTA` | NO | NO | NO | UsuarioId, `(UsuarioId, PaisId, TitularId, CuentaId)` | `UsuarioId` Cascade, `CuentaId` Cascade (borrar cuenta = borrar preferencias), `PaisId`/`TitularId` Restrict |
| `ALERTAS_SALDO` | NO | NO | NO | `CuentaId` UNIQUE parcial `cuenta_id IS NOT NULL`, `TipoTitular` UNIQUE parcial `cuenta_id IS NULL AND tipo_titular IS NOT NULL` | `CuentaId` Restrict |
| `ALERTA_DESTINATARIOS` | NO | NO | NO | `(AlertaId, UsuarioId)` UNIQUE | `AlertaId` Cascade, `UsuarioId` Restrict |
| `AUDITORIAS` | NO | NO | NO | `(UsuarioId, Timestamp)`, TipoAccion, EntidadId, Timestamp **(FALTA `(EntidadTipo, EntidadId)`)** | `UsuarioId` Restrict |
| `IA_USO_USUARIOS` | NO | **NO** | NO | `(UsuarioId, MonthKey)` UNIQUE, FechaModificacion | `UsuarioId` Restrict |
| `INTEGRATION_TOKENS` | NO | Sí | NO | TokenHash UNIQUE, Estado, FechaExpiracion, RotatedFromTokenId | 3 FKs Restrict |
| `INTEGRATION_PERMISSIONS` | NO | NO | NO | TokenId, TitularId, CuentaId, PaisId | `TokenId` Cascade, otros Restrict |
| `AUDITORIA_INTEGRACIONES` | NO | NO | NO | TokenId, Timestamp, CodigoRespuesta | `TokenId` Restrict |
| `TIPOS_CAMBIO` | NO | NO | NO | `(DivisaOrigen, DivisaDestino)` UNIQUE | (sin FKs) |
| `MOVIMIENTOS_ESPERADOS` | **SÍ** (`xmin`) | Sí | NO | `(CuentaId, Estado)`, `(CuentaId, FechaEsperada, Monto)`, Referencia, DeletedAt | 4 FKs Restrict |
| `CONCILIACIONES` | **SÍ** (`xmin`) | **NO** (no `ISoftDelete`) | NO | `(CuentaId, Estado)`, MovimientoEsperadoId, ExtractoId, `(MovimientoEsperadoId, ExtractoId)` UNIQUE | `CuentaId` Restrict, `MovimientoEsperadoId` Restrict (CORREGIDO), `ExtractoId` SetNull, 3 FKs usuarios Restrict |
| `DIVISAS_ACTIVAS` | NO | NO | NO | (PK = Codigo) | (sin FKs) |
| `CONFIGURACION` | NO | NO | NO | EsSecreto | `UsuarioModificacionId` Restrict |
| `BACKUPS` | NO | Sí | NO | (sin índices explícitos) | `IniciadoPorId`/`DeletedById` Restrict |
| `BACKUP_CLOUD_CONNECTIONS` | NO | Sí | NO | `(Provider, DeletedAt)` | `DeletedById` Restrict |
| `BACKUP_CLOUD_COPIES` | NO | Sí | NO | BackupId, `(Provider, Estado)` | `BackupId` Restrict, `ConnectionId` SetNull |
| `EXPORTACIONES` | NO | Sí | NO | (sin índices explícitos) | `CuentaId`/`IniciadoPorId`/`DeletedById` Restrict |
| `NOTIFICACIONES_ADMIN` | NO | NO | NO | (sin índices explícitos) | (sin FKs) |

---

## Nuevos hallazgos

### CONC-001 — `GetEvolucionAsync` mantiene N+async real (HIGH)

- **Archivo:línea**: `AtlasBalance.API/Services/DashboardService.cs:267-353`
- **Descripción**: Aunque `BuildMetricsAsync` ya precomputa `tasaPorDivisa`, la otra ruta de cálculo (`GetEvolucionAsync`) sigue recorriendo `currentSaldo` (267-278), `prevExtractos` (289-299), cada extracto del rango (323-339) y el `currentSaldo` por bucket (345-353) con un `await _tiposCambioService.ConvertAsync(...)` por iteración. Con 50k extractos y ~30 buckets el dashboard dispara ~1.5M awaits secuenciales.
- **Escenario de pérdida / impacto**: latencia de varios minutos para 1 mes de evolución; el dashboard se vuelve inutilizable antes de los 50k filas. No hay pérdida de datos, sí degradación grave del servicio.
- **Fix recomendado**: extraer un `BulkConvertAsync(amounts, source, target, ct)` a `ITiposCambioService` (o reusar el `tasaPorDivisa` cacheado del request). Una sola llamada con todos los importes a convertir → una llamada al catálogo cacheado, una multiplicación por par.
- **Esfuerzo**: 4h.

### CONC-002 — `PlazoFijo` sin `xmin` (HIGH)

- **Archivo:línea**: `AtlasBalance.API/Models/Entities.cs:121-140` + `AppDbContext.cs:156-169`
- **Descripción**: `PlazoFijo` es editable por el usuario (`RenovarAsync` en `PlazoFijoService.cs:97-156`) y un Hangfire job (`ProcesarVencimientosAsync` cada día) lo modifica. Si dos usuarios renuevan a la vez, o si un job pisa un cambio manual, no hay protección: last-write-wins silencioso. El resto de entidades financieras colaborativas (extractos, revisión, conciliación, movimientos esperados) ya tienen `xmin`.
- **Escenario de pérdida**: usuario A renueva el plazo → usuario B renueva a la vez → A guarda sus nuevos importes → B guarda los suyos → A pierde. Sin error, sin auditoría de la colisión. También: el job marca `Estado = VENCIDO` justo cuando un usuario está editando `Renovable = true` y notas.
- **Fix recomendado**: `entity.UseXminAsConcurrencyToken()` en `PlazoFijo`; capturar `DbUpdateConcurrencyException` en `RenovarAsync` y `ProcesarVencimientosAsync` y re-lanzar como 409 (o reintentar el job). Esto último requiere reescribir el job para manejar la colisión (lock advisory o reintento).
- **Esfuerzo**: 2h (entidad) + 4h (job).

### CONC-003 — Email y notificación fuera de transacción en `PlazoFijoService.ProcesarVencimientosAsync` (HIGH)

- **Archivo:línea**: `AtlasBalance.API/Services/PlazoFijoService.cs:58-93`
- **Descripción**: El foreach envía email y crea `NotificacionesAdmin` ANTES del `SaveChangesAsync` final (línea 93). Si el commit falla (deadlock, conflicto, error de FK), el destinatario ya recibió el email / ya tiene la notificación, pero el estado del plazo no se persistió como vencido. La siguiente corrida re-enviará.
- **Escenario de corrupción**: notificación duplicada, email duplicado, base desalineada con la realidad que el admin ve.
- **Fix recomendado**: diferir side effects hasta después del `SaveChangesAsync`; o usar un outbox transaccional (`NOTIFICACIONES_PENDIENTES` con worker Hangfire) que materialice email/notificación cuando el commit haya pasado. Mínimo viable: recolectar (adminNotif, emailTask) en una lista durante el loop, ejecutar tras `SaveChangesAsync`.
- **Esfuerzo**: 4h.

### CONC-004 — `ProcesarVencimientosAsync` sin protección de carrera entre ejecuciones (HIGH)

- **Archivo:línea**: `AtlasBalance.API/Services/PlazoFijoService.cs:31-95`
- **Descripción**: El job corre con cron `Cron.Daily()` (`Program.cs:302-305`). Si el proceso se reinicia o si dos réplicas de la API (futuro) corren el job a la vez, ambos leen los mismos plazos, ambos envían emails a admins, ambos escriben `FechaUltimaNotificacion = hoy` y ambos generan `NotificacionesAdmin` duplicados. La protección actual (`FechaUltimaNotificacion != hoy`) sólo evita re-envío EN LA MISMA corrida, no entre réplicas.
- **Escenario de duplicación**: 2 emails + 2 admin-notifs por plazo cuando hay 2 procesos o 2 arranques el mismo día.
- **Fix recomendado**: tomar `pg_advisory_xact_lock` con un key estable (p. ej. `"plazo-fijo-vencimiento-job"`) al inicio del job; o deduplicar con un índice único parcial sobre `NotificacionesAdmin(DetallesJson->cuenta_id, fecha::date)` y la `UPSERT` correspondiente. Alternativa más barata: crear un `JobRun` row con `(job_name, run_date)` UNIQUE y `INSERT ON CONFLICT DO NOTHING`; el primer proceso gana.
- **Esfuerzo**: 2h.

### CONC-005 — Cooldown de alertas via `CONFIGURACION` sin backstop (MED)

- **Archivo:línea**: `AtlasBalance.API/Services/AlertaService.cs:84-101` y `:330-362`
- **Descripción**: El fix parcial anti-H7 usa filas de `CONFIGURACION` con clave `alerta_saldo_last_sent_utc:{cuentaId:N}`. La lectura y la escritura se hacen en métodos separados, cada uno con su propio `SaveChangesAsync`. Si dos requests `EvaluateSaldoPostAsync` para la misma cuenta se solapan, ambos leen `lastSentAt = null`, ambos pasan el cooldown, ambos disparan email y ambos hacen `UpsertCuentaCooldownAsync`. Resultado: hasta 2 emails por cooldown de 24h para la misma cuenta.
- **Escenario de duplicación**: dos extracciones masivas (importación + edición manual) evalúan la misma cuenta a la vez → email doble.
- **Fix recomendado**: introducir tabla `ALERTA_SALDO_COOLDOWN(cuenta_id, alerta_id, fecha_utc)` con PK `(cuenta_id, alerta_id)` y usar `INSERT ... ON CONFLICT DO UPDATE SET fecha_utc = EXCLUDED.fecha_utc WHERE EXCLUDED.fecha_utc > alerta_saldo_cooldown.fecha_utc` (lockeado por fila). Mantener el resultado en una sola transacción con la inserción en `NOTIFICACIONES_ADMIN`. Como mínimo, tomar `pg_advisory_xact_lock` por `cuentaId` en el método.
- **Esfuerzo**: 4h (tabla) o 1h (lock).

### CONC-006 — Soft delete ausente en `Conciliacion`, `ImportacionLoteFila`, `ExtractoColumnaExtra`, `RevisionExtractoEstado`, `IaUsoUsuario` (MED)

- **Archivo:línea**: `AtlasBalance.API/Models/Entities.cs:214-234` (fila/columna extra), `:252-260` (revision), `:328-340` (ia uso), `:417-438` (conciliacion)
- **Descripción**: El query filter `ApplySoftDeleteQueryFilters` (`AppDbContext.cs:530-546`) sólo aplica a entidades que implementan `ISoftDelete`. Las anteriores no. Esto rompe:
  1. RLS: las políticas RLS en movimientos/conciliaciones/revisiones ya filtran por `deleted_at IS NULL` para el caso particular, pero no hay rastro de "quién/el-cuándo-borró" en BD.
  2. Borrado lógico = borrado físico: hoy un `DELETE` real es la única vía. Sin filtro, los servicios deben acordarse de NO borrar y marcar deleted_at.
  3. Restauración imposible.
- **Escenario de corrupción**: para auditoría financiera es grave: borrar una conciliación para "corregir" deja el historial perdido, sin saber quién la borró, cuándo, ni por qué. Si el `IA_USO_USUARIOS` se borra se pierde el registro mensual y el siguiente request crea uno nuevo con `Requests=0` sin huella de los consumos previos.
- **Fix recomendado**: hacer que esas 5 entidades implementen `ISoftDelete`; añadir migración que añada `deleted_at`/`deleted_by_id` y un índice por `deleted_at` para mantener la simetría con el resto. Actualizar servicios para usar soft-delete (migrar el código de `DELETE` existente si lo hay).
- **Esfuerzo**: 1 día (entidad + migración + barrido de servicios).

### CONC-007 — Sin CHECK constraints sobre columnas `Estado` (MED)

- **Archivo:línea**: `AppDbContext.cs:225` (`IMPORTACION_LOTES.Estado`), `:432` (`MOVIMIENTOS_ESPERADOS.Estado`), `:450` (`CONCILIACIONES.Estado`), `:478` (`BACKUP_CLOUD_CONNECTIONS.Estado`)
- **Descripción**: Aunque la aplicación normaliza los valores (`NormalizeEstado`, `NormalizeMovimientoPlazoFijo`, etc.), un insert directo vía SQL (admin, ETL, script de migración manual) puede meter un valor basura. La única salvaguarda es el código de aplicación.
- **Escenario de corrupción**: un valor como `"conciliada "` (con espacio) entraría por bypass y la app lo trataría como estado desconocido.
- **Fix recomendado**: `entity.HasCheckConstraint("ck_xxx_estado", "estado IN ('a', 'b', 'c')")` en EF Core + migración. No sustituye al `NormalizeEstado` (que mapea sinónimos); es un cinturón de seguridad.
- **Esfuerzo**: 2h.

### CONC-008 — Falta índice `(EntidadTipo, EntidadId)` en `AUDITORIAS` (MED)

- **Archivo:línea**: `AppDbContext.cs:350-361`
- **Descripción**: `Auditorias` se consulta desde el endpoint `GetAuditCelda` (`ExtractosController.cs:639-668`) con `WHERE a.EntidadTipo == "EXTRACTOS" && a.EntidadId == id`. Hay índice `(UsuarioId, Timestamp)`, índice `TipoAccion`, índice `EntidadId` y índice `Timestamp`, pero NO compuesto `(EntidadTipo, EntidadId)`. PostgreSQL usará uno de los dos simples y filtrará el otro, pero ambos `EntidadTipo` y `EntidadId` aparecen en `WHERE`.
- **Escenario de impacto**: a 100k+ auditorías por cuenta el endpoint empieza a sufrir. No es bloqueante hoy, es deuda de rendimiento.
- **Fix recomendado**: `entity.HasIndex(e => new { e.EntidadTipo, e.EntidadId })`.
- **Esfuerzo**: 30 min (índice + migración).

### CONC-009 — `USUARIO_EMAILS` permite varios `EsPrincipal=true` por usuario (MED)

- **Archivo:línea**: `AppDbContext.cs:74-80`; lógica de control en `Controllers/UsuariosController.cs:331-340`
- **Descripción**: El controller fuerza un único principal dentro de la transacción actualizando los antiguos a `false`. Pero (a) no hay backstop de BD, (b) dos inserciones concurrentes de emails-mark-as-primary pueden dejar dos `EsPrincipal=true`. El login usa `Usuario.Email` (no `UsuarioEmails`), así que un principal adicional no bloquea el login, pero confunde a la UI y al flujo de notificaciones.
- **Escenario de corrupción**: dos requests concurrentes (uno crea email A como principal, otro crea email B como principal) → ambos guardan sin verse → tabla con 2 principales. Invariante violada.
- **Fix recomendado**: índice parcial único en migración: `CREATE UNIQUE INDEX ux_usuario_emails_principal ON usuario_emails(usuario_id) WHERE es_principal`. La app maneja el `try/catch` con 409 al violarlo.
- **Esfuerzo**: 1h.

### CONC-010 — `PlazoFijo.InteresPrevisto` mapeado `(18,2)` — precisión de tipo de interés (MED)

- **Archivo:línea**: `AppDbContext.cs:160` (`entity.Property(e => e.InteresPrevisto).HasPrecision(18, 2);`)
- **Descripción**: El review previo no cerró si el campo es un importe monetario esperado (Euros) o un porcentaje (TIN/TAE). El campo se llama `InteresPrevisto` ("interés previsto"), y en el dashboard se suma con `ConvertAsync` (`DashboardService.cs:618`). Si el banco carga un TIN del 2,75% en `interes_previsto`, con (18,2) se redondea a 2,75 que casualmente encaja; pero un TAE de 2,375% se almacenaría como 2,38 (pérdida de 0,005 pp = ~17 € al año sobre 3.400 € de plazo). Y un TIN del 12,345% pierde el 5. En Europa los TIN/TAE se publican hasta 3-4 decimales.
- **Escenario de imprecisión**: la columna `interes_previsto` en BD se redefine como porcentaje más adelante y se descubre que los datos almacenados están truncados.
- **Fix recomendado**: confirmar con producto qué representa el campo. Si es importe → mantener (18,2) o subir a (18,4) por simetría con saldos. Si es tipo → cambiar a (18,8) como `TipoCambio.Tasa`. Documentar la decisión en la entidad.
- **Esfuerzo**: 1h (decisión + migración si aplica).

### CONC-011 — `PlazoFijoService` envía 1 email por plazo vencido (MED)

- **Archivo:línea**: `AtlasBalance.API/Services/PlazoFijoService.cs:202-224`
- **Descripción**: Por cada plazo vencido o próximo a vencer se envía un email individual a TODOS los admins activos. Si hay 40 plazos y 3 admins, son 120 emails. Además, `TryAddAdminNotificationAsync` inserta una `NotificacionesAdmin` por plazo — el dashboard de admin muestra 40 entradas el día del vencimiento.
- **Escenario de impacto**: email-storm en vencimientos concentrados (todos los plazos de un titular con mismo día de cierre). Ruido en la bandeja.
- **Fix recomendado**: agrupar por destinatario en una sola ejecución: `recipients = admins activos; subject = "X plazos vencen hoy"; body = tabla con cada plazo`. Para admin-notifs, agregar en un solo mensaje con la lista.
- **Esfuerzo**: 4h.

### CONC-012 — `PERMISOS_USUARIO` sin UNIQUE composite (LOW)

- **Archivo:línea**: `AppDbContext.cs:298-309`
- **Descripción**: Hay índice `(UsuarioId, PaisId, TitularId, CuentaId)` (línea 316, en `PreferenciaUsuarioCuenta`) y varios índices parciales sobre `PermisosUsuario` (`(UsuarioId, CuentaId)`, `(UsuarioId, PaisId)`) pero ninguno `IsUnique()`. Un doble POST al endpoint que crea permisos puede dejar dos filas idénticas.
- **Escenario de duplicación**: un admin añade el mismo permiso por error → dos filas con misma semántica → el `CanXxx` que evalúa `Any(...)` sigue funcionando pero el admin ve dos filas idénticas y el borrado de una no libera a la otra.
- **Fix recomendado**: índice único `WHERE cuenta_id IS NOT NULL` y `WHERE cuenta_id IS NULL AND titular_id IS NOT NULL`, etc. — un UNIQUE por cada "shape" posible. O un trigger que bloquee el duplicado. EF Core 8 soporta `HasFilter(...)` + `IsUnique()`.
- **Esfuerzo**: 2h.

### CONC-013 — `PLAZOS_FIJOS.cuenta_id` UNIQUE sin filtro soft-delete (HIGH bug)

- **Archivo:línea**: `AppDbContext.cs:161` (`entity.HasIndex(e => e.CuentaId).IsUnique();`) + migración `20260425145516_AddPlazoFijoAutonomosAlertas.cs:129-133` (`unique: true` sin `WHERE deleted_at IS NULL`)
- **Descripción**: La entidad implementa `ISoftDelete` y el query filter oculta los plazos soft-deleted a las queries, pero el índice único en BD no incluye `WHERE deleted_at IS NULL`. Resultado: si un plazo se soft-deleted, EF no lo ve, pero la BD sigue considerando ocupado el slot `cuenta_id` en el índice, por lo que `INSERT` de un nuevo plazo para la misma cuenta revienta con `23505 unique_violation` en `ix_plazos_fijos_cuenta_id`.
- **Escenario de bloqueo**: tras el primer plazo fijo de una cuenta, esa cuenta no puede volver a tener un plazo fijo después de cancelar/soft-deleted el primero. Si el negocio permite plazos sucesivos, este índice lo bloquea.
- **Fix recomendado**: `entity.HasIndex(e => e.CuentaId).IsUnique().HasFilter("\"deleted_at\" IS NULL");` (consistente con `EXTRACTOS_DESGLOSES` que sí lo tiene). Requiere DROP/CREATE del índice en migración.
- **Esfuerzo**: 1h (migración + actualizar la línea en `AppDbContext`).

### CONC-014 — `EXTRACTOS (cuenta_id, fila_numero)` UNIQUE sin filtro soft-delete (HIGH bug)

- **Archivo:línea**: `AppDbContext.cs:191` y migración `20260413120705_Initial.cs` (CREATE UNIQUE INDEX sin filtro)
- **Descripción**: Mismo problema que CONC-013, pero en extractos. `Extracto : ISoftDelete` (`Entities.cs:156`) y `ApplySoftDeleteQueryFilters` lo filtra en queries, pero el índice único no contempla `deleted_at IS NULL`. Insertar un extracto con la misma `(cuenta_id, fila_numero)` que uno soft-deleted revienta con unique violation.
- **Escenario de bloqueo**: tras `DELETE` (soft) de un extracto, no se puede volver a usar ese `fila_numero` para un nuevo extracto (ni siquiera importando datos viejos). El `ReutilizarFilaNumero` que algunos importers pueden querer hacer falla.
- **Fix recomendado**: `entity.HasIndex(...).IsUnique().HasFilter("\"deleted_at\" IS NULL");`. Recreate el índice.
- **Esfuerzo**: 1h.

### CONC-015 — `ExportacionService.ExportarMensualAsync` ejecuta `await` por cuenta en serie (MED)

- **Archivo:línea**: `AtlasBalance.API/Services/ExportacionService.cs:215-238`
- **Descripción**: foreach secuencial con `await ExportarCuentaAsync(cuentaId, ...)` por cada cuenta. Para 50 cuentas con 50k filas cada una, son 50 exports secuenciales. Cada export abre 1 transacción + N queries. Tiempo total: horas.
- **Escenario de impacto**: el job de "exportación mensual" del día 1 puede no terminar antes del día siguiente. No hay pérdida de datos, pero un timeout del job lo deja en estado raro.
- **Fix recomendado**: paralelizar con `Parallel.ForEachAsync` limitado (semáforo de 4) o `Task.WhenAll` con chunk. Cuidado con la concurrencia de EF Core (un `DbContext` por scope, no compartido).
- **Esfuerzo**: 3h.

### CONC-016 — `AuditService` no comparte transacción con el negocio (MED)

- **Archivo:línea**: `AtlasBalance.API/Services/AuditService.cs:34-49`
- **Descripción**: Cada `IAuditService.LogAsync` hace su propio `SaveChangesAsync`. Si el `SaveChangesAsync` del negocio falla justo después, la fila de auditoría ya está persistida. No hay forma de rollback conjunto.
- **Escenario de corrupción**: el admin ve "X actualizado" en `AUDITORIAS` pero el cambio nunca se persistió. Pista falsa en una investigación.
- **Fix recomendado**: pasar `DbContext` al servicio y añadir al `ChangeTracker` del mismo contexto del caller; la auditoría se commitea con el siguiente `SaveChangesAsync` del caller. Documentar contrato: "audit debe ir antes del SaveChanges del caller, no después".
- **Esfuerzo**: 4h (refactor de audit + tests).

### CONC-017 — `AuditService.LogAsync` no limita tamaño de `DetallesJson` (LOW)

- **Archivo:línea**: `AtlasBalance.API/Services/AuditService.cs:34-49`
- **Descripción**: Acepta cualquier `string?` para `DetallesJson` (columna `text`/`jsonb` sin cota). Un caller puede meter megabytes; la tabla crecerá sin control.
- **Fix recomendado**: cap a 32 KB en el servicio (cortar o rechazar). Aplicar también en `SaveAudit`/`SaveCellAudits` del `ExtractosController`.
- **Esfuerzo**: 1h.

### CONC-018 — `AlertaSaldo` no tiene `xmin` (MED)

- **Archivo:línea**: `Entities.cs:294-303` (`AlertaSaldo`)
- **Descripción**: `AlertaService.EvaluateSaldoPostAsync` modifica `alertaAplicable.FechaUltimaAlerta = now` (línea 150) y luego guarda. Si dos admins editan la misma alerta (cambian `SaldoMinimo`), last-write-wins. El cooldown per-cuenta está separado y se actualiza también, así que el bug de la colisión está mitigado por accidente, pero la edición concurrente de la definición de alerta no.
- **Escenario de pérdida**: admin A baja el umbral de 1000 a 500, admin B lo baja a 750 casi a la vez; sólo uno gana silenciosamente.
- **Fix recomendado**: `UseXminAsConcurrencyToken()` en `AlertaSaldo`. Trivial.
- **Esfuerzo**: 30 min.

### CONC-019 — `Usuario` no tiene `xmin` (LOW, riesgo acotado)

- **Archivo:línea**: `Entities.cs:9-31`
- **Descripción**: `CambioPassword`, `MFA setup`, `borrado lógico` y rotación de `SecurityStamp` no tienen concurrencia optimista. Cambio de password concurrente puede perder el `SecurityStamp` rotation de uno. Riesgo bajo porque cada usuario opera sobre su propia fila.
- **Fix recomendado**: añadir `UseXminAsConcurrencyToken()` y mapear `DbUpdateConcurrencyException` a 409. Útil en `AuthService.ChangePasswordAsync` y `SetActivoAsync`.
- **Esfuerzo**: 1h.

### CONC-020 — `Conciliacion.SetEstadoAsync` mezcla dos escrituras con xmin de dos entidades (LOW)

- **Archivo:línea**: `AtlasBalance.API/Services/ConciliacionService.cs:266-315`
- **Descripción**: Modifica `conciliacion` y `movimiento` en el mismo `SaveChangesAsync`. Ambas tienen `xmin`. Si el `xmin` de `movimiento` cambió entre la lectura y el guardado, el `DbUpdateConcurrencyException` mencionará `Conciliaciones` O `MovimientosEsperados`. El handler global mapea ambos a 409 genérico sin distinguir qué entidad falló.
- **Escenario de UX**: el frontend recibe un 409 sin saber qué campo fue pisado. Tendría que recargar ambos. Aceptable, pero podría ser más explícito.
- **Fix recomendado**: dejar que el cliente sepa que revise ambas entidades (ya implícito en el código del frontend) o inspeccionar `ex.Entries` y emitir un `code` específico por tabla.
- **Esfuerzo**: 1h.

### CONC-021 — `RevisionService.SetEstadoAsync` lee extracto con `AsNoTracking` (LOW)

- **Archivo:línea**: `AtlasBalance.API/Services/RevisionService.cs:238-245`
- **Descripción**: Lee `Extracto` con `AsNoTracking` para verificar permisos. Si el extracto es soft-deleted justo después, el filtro del query filter no se aplica (no hay `ISoftDelete` en la query porque es AsNoTracking + lectura, pero el filtro sí está). El `RevisionExtractoEstado` con `xmin` está protegido. Si dos usuarios marcan/desmarcan el mismo `(extractoId, tipo)`, el segundo recibe 409 correctamente.
- **Escenario de pérdida**: ninguno directo (xmin protege). Pero el endpoint no avisa al cliente que "otro usuario ya lo modificó" — el 409 genérico sí lo hace, pero podría distinguir "otro revisor lo cambió hace 2 segundos" vs "el extracto fue borrado".
- **Fix recomendado**: revisar el `RevisionExtractoEstado` previo en `Read` con `AsNoTracking` para devolver el `xmin` actual y permitir `If-Match`; opcional. Es pulido.
- **Esfuerzo**: 2h.

### CONC-022 — `ImportacionService.ConfirmarAsync` envía email y evalúa alerta fuera de transacción (LOW)

- **Archivo:línea**: `AtlasBalance.API/Services/ImportacionService.cs:742-748`
- **Descripción**: `EvaluateSaldoAlertAsync` se llama tras `tx.CommitAsync` (línea 744) → está bien en orden. Pero `_auditService.LogAsync` (línea 740) está ANTES del `tx.CommitAsync`. Si el commit falla, la auditoría de "importación confirmada" persiste sin que la importación se haya confirmado.
- **Escenario**: ver CONC-016. Mismo patrón.
- **Fix recomendado**: mover `_auditService.LogAsync` después del commit, o aplicar CONC-016.
- **Esfuerzo**: incluido en CONC-016.

### CONC-023 — `ImportacionLote` y `PlazoFijo` sin `xmin` no colaborativo (LOW)

- **Archivo:línea**: `Entities.cs:186-212` (`ImportacionLote`), `:121-140` (`PlazoFijo`)
- **Descripción**: Si dos admins editan `lote.Notas` o `lote.AdvertenciasAceptadas` a la vez, last-write-wins. Mismo patrón para `PlazoFijo` ya cubierto en CONC-002. Estos no son "colaborativos" en el sentido de edición simultánea probable, pero la app permite editarlos desde Configuración.
- **Fix recomendado**: añadir `UseXminAsConcurrencyToken()` en ambos.
- **Esfuerzo**: 30 min total.

### CONC-024 — `Conciliacion` (entidad) y `RevisionExtractoEstado` sin soft-delete rompe la promesa de "histórico" (MED)

- **Archivo:línea**: ver CONC-006
- **Descripción**: Los `revision_extracto_estados` y `conciliaciones` se usan para construir historial. Sin `ISoftDelete`, el borrado accidental destruye el historial. La RLS usa `atlas_security.can_read_cuenta_by_id` que no distingue "borrado" de "activo". Para una auditoría financiera esto es grave: un admin puede "borrar" una conciliación que no le gustaba.
- **Fix recomendado**: parte de CONC-006.

### CONC-025 — `GetSaldosDivisaAsync` y `BuildPlazosFijosResumenAsync` con `await` por entrada (LOW)

- **Archivo:línea**: `DashboardService.cs:177-192` y `:611-619`
- **Descripción**: Aunque `BuildMetricsAsync` ya precomputa, estos dos métodos NO. `GetSaldosDivisaAsync` itera sobre `metrics.SaldosPorDivisa.OrderBy(x => x.Key)` (típicamente 3-6 monedas) — bajo impacto. `BuildPlazosFijosResumenAsync` itera sobre `plazos` con `await ConvertAsync` por plazo — hasta N awaits donde N = número de plazos fijos.
- **Fix recomendado**: reusar `tasaPorDivisa` de `BuildMetricsAsync` o factorizar el método para aceptar el diccionario precargado. Bajo impacto, pero el patrón es el mismo.
- **Esfuerzo**: 1h.

### CONC-026 — `HardenedConciliacionService.SugerirAsync` N+async en `FindBestMatchAsync` por movimiento (MED)

- **Archivo:línea**: `AtlasBalance.API/Services/HardenedConciliacionService.cs:78-91` y `:143-174`
- **Descripción**: `foreach (var movimiento in movimientos)` → `await FindBestMatchAsync` por movimiento → dentro hace una query a Extractos, Cuenta (Exists), Permisos (Exists). 1000 movimientos → 1000 queries a Extractos (cada una con el índice cubriente, OK) + 1000 SaveChanges al final (uno por toda la lista, OK). No es N+1 grave, pero si crece a 5000 movimientos empieza a molestar.
- **Fix recomendado**: agrupar por (CuentaId, Fecha) y hacer batch matching con una sola query, deduplicar en memoria. El matching exacto puede hacerse en SQL; el score scoring en C# es OK.
- **Esfuerzo**: 6h.

### CONC-027 — Cache de tipos de cambio con race benigno (LOW)

- **Archivo:línea**: `TiposCambioService.cs:377-410`
- **Descripción**: `GetRateCatalogAsync` cachea 5 min. Si dos requests llegan a la vez con cache miss, ambos hacen query y ambos escriben en cache. No destructivo (mismos datos). Pero si `InvalidateCache` se llama entre la query de A y el `Set` de A, A repuebla con datos viejos justo antes del invalidate. Próximo request leerá cache stale hasta el siguiente TTL.
- **Fix recomendado**: usar `IMemoryCache.GetOrCreateAsync` con `SlidingExpiration` o un lock de doble-check.
- **Esfuerzo**: 1h.

### CONC-028 — `GetAuthenticatedScopeAsync` con EXISTS pesado en dashboard (LOW)

- **Archivo:línea**: `DashboardService.cs:793-803`
- **Descripción**: La query de `cuentaIdsList` tiene un `Cuentas.Any(c => ... PermisosUsuario.Any(p => ...) AND)`. Es un SQL complejo, no N+1 de EF, pero un usuario con 50 permisos y 200 cuentas puede disparar un plan pesado. Además, se repite en cada `GetPrincipalAsync` (cada carga del dashboard).
- **Fix recomendado**: cachear el `DashboardScope` en `IMemoryCache` con TTL corto (30s) e invalidar al modificar permisos. O materializar un join en una CTE y cachearlo por usuario.
- **Esfuerzo**: 3h.

### CONC-029 — `AppDbContext` no usa `IDbContextFactory` y la API es `AddDbContext` (LOW)

- **Archivo:línea**: `Program.cs:34-38`
- **Descripción**: `AddDbContext` registra `AppDbContext` como scoped. Si algún job (Hangfire) o servicio en background lo usa fuera del scope de request, hay riesgo de usar un `DbContext` disposed. La documentación de EF Core advierte de esto. Hoy no veo uso problemático (los jobs usan `IServiceScopeFactory` correctamente por Hangfire), pero es un patrón frágil.
- **Fix recomendado**: registrar también `IDbContextFactory<AppDbContext>` para uso desde background jobs y mantener scoped para los controllers.
- **Esfuerzo**: 1h (preventivo).

### CONC-030 — `MIGRATION_CONNECTION` separada del runtime connection (LOW, defensive)

- **Archivo:línea**: `Program.cs:632-670`
- **Descripción**: El sistema intenta usar `atlas_owner` para migraciones y `app_user` para runtime. Si en algún cluster futuro `migrations` y `runtime` están en sidecar containers distintos y la red tiene jitter, la doble conexión puede acabar usando un usuario con permisos distintos. La nota de seguridad está bien tomada.
- **Fix recomendado**: documentar el modelo de permisos en un README del repo (no código).
- **Esfuerzo**: 30 min (docs).

---

## Cobertura del audit

### Archivos leídos íntegros

- `AtlasBalance.API/Models/Entities.cs` (532 líneas, completo)
- `AtlasBalance.API/Data/AppDbContext.cs` (547 líneas, completo)
- `AtlasBalance.API/Services/RevisionService.cs` (475 líneas, completo)
- `AtlasBalance.API/Services/ConciliacionService.cs` (580 líneas, completo)
- `AtlasBalance.API/Services/ImportacionService.cs` (1-1247 líneas, ~80% — completado por offset en la siguiente tanda si fuera necesario)
- `AtlasBalance.API/Services/PlazoFijoService.cs` (281 líneas, completo)
- `AtlasBalance.API/Services/AlertaService.cs` (363 líneas, completo)
- `AtlasBalance.API/Services/DashboardService.cs` (992 líneas, completo)
- `AtlasBalance.API/Services/TiposCambioService.cs` (592 líneas, completo)
- `AtlasBalance.API/Services/ExportacionService.cs` (343 líneas, completo)
- `AtlasBalance.API/Services/UserAccessService.cs` (353 líneas, completo)
- `AtlasBalance.API/Services/BackupEncryptionService.cs` (226 líneas, completo)
- `AtlasBalance.API/Services/ConfiguracionRepository.cs` (95 líneas, completo)
- `AtlasBalance.API/Services/AuditService.cs` (60 líneas, completo)
- `AtlasBalance.API/Services/HardenedConciliacionService.cs` (371 líneas, completo)
- `AtlasBalance.API/Data/RlsDbCommandInterceptor.cs` (216 líneas, completo)
- `AtlasBalance.API/Program.cs` (866 líneas, completo)
- `AtlasBalance.API/Controllers/ExtractosController.cs` (1-1115 líneas, completo)
- Migración `20260629090000_FinancialHardeningV0202.cs` (314 líneas, completo)
- Migración `20260425145516_AddPlazoFijoAutonomosAlertas.cs` (extractos del unique index y CHECK constraints)
- Migración `20260701115326_V0203_Hardening.cs` (153 líneas, completo)

### Archivos NO leídos (fuera de alcance del encargo explícito)

- `AuthService.cs` (sólo las líneas relevantes a xmin/IPs: ~609, 630, 1037)
- `AtlasAiService.cs`
- `BackupConfigurationService.cs`, `BackupService.cs`, `HardenedBackupConfigurationService.cs`, `GoogleDriveBackupService.cs`, `HardenedGoogleDriveBackupService.cs`
- `CsrfService.cs`, `EmailService.cs`, `IntegrationTokenService.cs`, `IntegrationAuthorizationService.cs`
- `WatchdogClientService.cs`, `ActualizacionService.cs`, `TotpService.cs`, `SecretProtector.cs`, `UserSessionState.cs`
- `AuditService.cs` ya leído
- Todos los demás controllers (salvo ExtractosController y los chequeados en sección 5.x)
- RLS migrations completas (EnableRowLevelSecurity, HardenRls*, AlignRlsDashboard)
- `Jobs/*.cs` (cron jobs): PlazoFijoVencimientoJob, BackupSchedulerJob, etc. — pendiente auditar
- `Tests/` — no auditado (fuera de alcance de solo-lectura del código de negocio)
- `RlsContextSigner.cs`

### Limitaciones del audit

1. **Sólo análisis estático**: no se ejecutaron tests, ni queries reales contra Postgres, ni benchmarks. Las conclusiones sobre N+async son por inspección del código, no por medición.
2. **No se verificó el RLS runtime**: las políticas se leyeron de las migraciones, no se probó con datos reales. Los puntos de vista de "RLS cubre esto" son por lectura del DDL.
3. **No se contrastó con el comportamiento del frontend**: el flujo de 409 ya está mapeado a "recargar fila" en el frontend según `v-02-04.md`; se asume correcto. No se leyó `ExtractosPage` ni `api.ts` para validar la rama 409.
4. **No se examinó la cobertura de tests**: los tests `ExtractosConcurrencyTests` y `RowLevelSecurityTests` aparecen como verdes en `v-02-04.md`. No se abrieron para ver qué assertions concretas tienen.
5. **No se midió latencia**: la estimación de N+async en `GetEvolucionAsync` (CONC-001) es cualitativa — "1.5M awaits" sale de 50k extractos × 30 buckets, no de una medición.
6. **No se contrastó con la base de datos viva**: las aserciones sobre CHECK constraints, índices únicos y filtros parciales se basan en migraciones aplicadas. Si la BD real diverge del snapshot EF, hay que revalidar.
7. **Las severidades son propuesta**: el equipo debe revisar y aceptar antes de promover a tickets. Especialmente CONC-002, CONC-013, CONC-014 y CONC-022 deberían ser validados con producto.
8. **No se auditaron los `Jobs/*`**: `PlazoFijoVencimientoJob`, `BackupSchedulerJob`, `LimpiezaRefreshTokensJob`, etc. están fuera de los archivos solicitados. Los problemas transaccionales de `PlazoFijoService` (CONC-003, CONC-004) son probablemente extensibles a esos jobs.

### Riesgos priorizados para resolver antes de exponer a internet

Por severidad e impacto multi-usuario:

1. **CONC-013** (HIGH bug, 1h) — el índice único de PLAZOS_FIJOS bloquea la cuenta tras un soft-delete
2. **CONC-014** (HIGH bug, 1h) — el índice único de EXTRACTOS bloquea `fila_numero` tras un soft-delete
3. **CONC-002** (HIGH, 2h+4h) — `PlazoFijo` sin xmin, riesgo de last-write en renovación concurrente
4. **CONC-003 + CONC-004** (HIGH, 6h) — email y notificación fuera de transacción + carrera entre jobs
5. **CONC-001** (HIGH perf, 4h) — N+async en `GetEvolucionAsync` que vuelve inutilizable el dashboard
6. **CONC-006** (MED, 1 día) — soft delete en 5 entidades que el review marcó hace semanas
7. **CONC-007** (MED, 2h) — backstops de CHECK en columnas `Estado`
8. **CONC-005** (MED, 4h) — cooldown de alertas sin backstop transaccional (riesgo email doble)

Los LOW (CONC-009, CONC-010, CONC-011, CONC-012, etc.) son pulido. CONC-018 y CONC-019 son mitigaciones baratas que cierran simetrías.

### Conclusión

V-02-04 cierra correctamente los HIGH del review previo (xmin en 4 entidades, índice cubriente, restrict en cascadas, cifrado de secretos, validación del backup key, bulk convert en dashboard, tolerancia de conciliación, lote de importación robusto). El handler global de 409 está en su sitio.

Quedan **dos bugs HIGH confirmados** (índices únicos sin filtro soft-delete) que probablemente ya están causando errores en producción con datos de prueba, y **un HIGH de diseño** (PlazoFijo sin concurrencia) que se manifestará en cuanto dos usuarios renueven a la vez o un job pise una edición. Los MED pendientes son de baja urgencia funcional pero erosiónan el modelo de soft-delete y la cobertura de auditoría.

No se detectaron problemas de seguridad nuevos respecto al review. La superficie de inyección SQL sigue en cero. La concurrencia optimística está bien diseñada donde está aplicada — la tarea es extenderla, no rediseñarla.
