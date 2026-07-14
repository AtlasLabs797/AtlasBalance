# Atlas Balance — Auditoría general (read-only) · V-02-02

**Fecha:** 2026-06-30
**Alcance:** revisión estática del código actual (`V-02-02`) — frontend (`Atlas Balance/frontend/src`), backend (`Atlas Balance/backend/src/AtlasBalance.API`), tests, esquema y docs. **No se modificó ningún archivo** durante la auditoría.
**Método:** lectura de código + cruce con `Documentacion/SPEC.md`, `Diseno/AUDITORIA_UI_UX_GLOBAL_V-02-02.md`, `SEGURIDAD_AUDITORIA_V-01.06.md` y bitácoras existentes. Sin ejecutar la app, sin docker, sin tests dinámicos.
**Construye sobre:** las auditorías internas previas. Esta auditoría **añade hallazgos nuevos** y consolida los vivos.

## Resumen ejecutivo

Atlas Balance está en un estado decente para una herramienta de tesorería interna: el modelo de seguridad multicapa (JWT + cookies httpOnly + CSRF + RLS + bearer tokens hasheados), el sistema de permisos por intersección de dimensiones y el grid virtualizado de extractos son sólidos. La capa de auditoría está bien pensada y los recientes passes del equipo ya cerraron los huecos gordos (refresh tokens ligados a security stamp, MFA obligatorio, conversión de divisas con 409, RLS alineado con roles).

**Pero quedan riesgos reales que conviene cerrar antes de llamar al sistema release-ready:**

1. **Dos bugs confirmados de corrupción de datos**: `fila_numero` se renumera al insertar (rompe auditoría) y la restauración de extractos puede chocar con el `UNIQUE(cuenta_id, fila_numero)` y devolver 500 silencioso.
2. **Datos financieros silenciosamente incorrectos**: la conversión de divisas puede usar tasas con más de un año de antigüedad sin avisar; las monedas sin tasa se reportan como 0 y desaparecen del total sin alerta.
3. **Varios workflows anunciados en el SPEC están a medias**: cancelación de plazo fijo, aprobación maker-checker para edits grandes, notificación de SMTP caído, revertir último lote desde UI, score de confianza en conciliaciones.
4. **Rendimiento crece mal con datos reales**: N+1 sobre `ConvertAsync` en dashboard y OpenClaw, sin `AbortController` en ningún fetch → storms de peticiones al cambiar filtros rápido en 50 k filas.
5. **Cobertura de UX incompleta** (ya conocido) sobre los flujos críticos donde se mueve dinero: import, revisión, conciliaciones, deletes/restores, festivos de plazo fijo. El audit previo lo dejó dicho; sigue vigente.
6. **Defensas de seguridad con gaps aislados** pero corregibles: token sin expiración permitido por un solo admin, JWT secret reaprovechado como clave de firma RLS, restore de BD sin doble confirmación, sin histórico de contraseñas.

El reporte entra en detalle abajo, ordenado por bloque y severidad.

---

# 1. Bugs confirmados (alta prioridad, todos reproducibles desde código)

## B-01 · `fila_numero` deja de ser inmutable al insertar con `InsertBeforeFilaNumero` — **CRÍTICO**
- **Dónde:** `Atlas Balance/backend/src/AtlasBalance.API/Controllers/ExtractosController.cs:167-240` (Crear) y `249-275` (shift).
- **Qué pasa:** el SPEC §3.3 dice que `fila_numero` es **INMUTABLE** ("asignado al insertar, MAX+1; si se borra, el número queda hueco"). El controlador, sin embargo, acepta `InsertBeforeFilaNumero` y re-numera todas las filas existentes vía `UPDATE ... SET fila_numero = fila_numero + offset` + `... fila_numero - max`.
- **Por qué importa:** las referencias de celda estilo Excel (`A1`, `B5`, etc.) que se guardan en `AUDITORIAS.celda_referencia` se vuelven obsoletas al primer usuario con `puede_agregar_lineas`. La trazabilidad celda-a-celda se pierde silenciosamente.
- **Fix:** borrar `InsertBeforeFilaNumero` de `CreateExtractoRequest` (`DTOs/ExtractosDtos.cs:36`) y siempre insertar en `MAX + 1`. Si se quiere huecos，真正的 Excel, añadir una columna `orden` separada para manipular el orden visible.

## B-02 · Restaurar un extracto soft-deleted puede chocar con el `UNIQUE(cuenta_id, fila_numero)` — **ALTO · BUG CONFIRMADO**
- **Dónde:** `Controllers/ExtractosController.cs:411-429` (`Restaurar`).
- **Reproducir:** soft-delete fila 5 → añadir 2 filas nuevas (101, 102) → restaurar fila 5 → `DbUpdateException: duplicate key value violates unique constraint "ix_extractos_cuenta_id_fila_numero"`.
- **Por qué importa:** 500 sin mensaje accionable. La papelera deja de cumplir su promesa.
- **Fix (elegir uno):** (a) al soft-delete, mover `fila_numero` a un namespace negativo (p. ej. `-id.GetHashCode()`); (b) al restaurar, re-numerar filas `≥ fila_numero_restaurada`; (c) reemplazar el UNIQUE por `UNIQUE WHERE deleted_at IS NULL` (índice parcial de Postgres).

## B-03 · Filas con `monto = 0` se importan como movimientos válidos — **ALTO · BUG CONFIRMADO**
- **Dónde:** `Services/ImportacionService.cs:574-682`; tests en `ImportacionServiceTests.cs:908-960` que **fijan el bug** ("ValidarAsync_Should_Allow_Concept_Rows_With_Missing_Amount").
- **Qué pasa:** una fila vacía con solo concepto (`"\tEGARARECYCLING\t\t"`) se inserta como extracto con `monto = 0`. Cuenta en `total_registros`, suma 0 a ingresos/egresos, **pero ocupa un índice en la gráfica de evolución** y ensucia la detección de duplicados y out-of-order.
- **Por qué importa:** los bancos españoles，经常分拆 líneas (cabecera sin importe + detalle). Si pegas sin querer una sola línea "concepto sin importe" sale como si fuera un movimiento válido.
- **Fix:** exigir `monto != 0` (rechazo claro) **o** introducir `Extractos.es_descriptivo` y excluirlo de agregaciones. Decidir y aplicarlo consistente con `Conciliacion` (que sí rechaza `monto == 0`, ver `ConciliacionService.cs:84-87`).

## B-04 · `TiposCambioService.ConvertAsync` puede usar tasas con > 365 días de antigüedad sin avisar — **ALTO · BUG CONFIRMADO**
- **Dónde:** `Services/TiposCambioService.cs:453-514` (`ResolveRate`).
- **Qué pasa:** el catálogo en BD no tiene control de "stale". Si la API externa lleva un año caída y nadie ha editado la tasa manual, `EUR → USD` se resuelve con la tasa original. La conversión es await por movimiento, sin warning. Los `SaldosPorDivisa` del dashboard redondean a 2 decimales, así que un 10× en la tasa puede no notarse.
- **Por qué importa:** cifras financieras falsas en pantalla y export. Caso típico: API caída, nadie se entera, dashboard reporta números congelados como si fueran reales.
- **Fix:** añadir `MaxStalenessDays` configurable. Si la tasa supera el umbral, lanzar `TipoCambioMissingException` (ya existe `tipocambio_missing`, devolver 409) o devolver `null` y dejar que el caller la trate como "no conversión"; añadir `staleCurrencies: string[]` al DTO del dashboard para que el front pueda avisar.

## B-05 · `KpiCard` `Inmovilizado` miente con un helper inadecuado — **MEDIO** (UX, no dinero)
- **Dónde:** `frontend/src/pages/DashboardPage.tsx:322-331`.
- **Qué pasa:** el helper del KPI "Inmovilizado" muestra el **conteo de plazos fijos** cuando la variación es ≥0.1 %; los otros tres KPIs muestran `±X.X% vs. anterior`. El usuario espera la misma forma. El condicional invierte: oculta el dato cuando es pequeño (justo cuando sería seguro mostrar "0%").
- **Fix:** mostrar siempre `±X.X% vs. anterior` y mover el conteo de plazos fijos a un sub-texto pequeño.

## B-06 · `AppErrorBoundary` se traga el error en producción — **MEDIO** (UX/operación)
- **Dónde:** `frontend/src/components/common/AppErrorBoundary.tsx:22-46`.
- **Qué pasa:** cuando una vista peta, sale el card "Sección no disponible". Sin `role="alert"`, sin toast, sin `console.error` en producción, sin botón "Copiar diagnóstico". El admin nunca recibe la traza.
- **Fix:** `role="alert"`, emitir toast, botón que copia `{ timestamp, pathname, errorName, errorMessage, stack }` al portapapeles, envío opcional a `/api/diagnostics`.

## B-07 · `DashboardRoute` redirige sin avisar — **MEDIO** (UX)
- **Dónde:** `frontend/src/App.tsx:37-44` y mismo patrón en `DashboardPage.tsx:197-199` y `DashboardTitularPage.tsx:140-142`.
- **Qué pasa:** un usuario sin permiso de dashboard aterriza en `/dashboard` (deep link o stale sidebar) y se ejecuta `<Navigate to="/extractos" replace />` sin toast ni mensaje.
- **Fix:** toast explicativo + `EmptyState` "permission" antes de navegar (mismo UX ya existente en `EmptyState` variante `permission`).

## B-08 · El helper de "Egresos" usa signo algebraico que confunde — **MEDIO**
- **Dónde:** `frontend/src/pages/DashboardPage.tsx:301-306`.
- **Qué pasa:** cuando egresos suben (malo), el helper muestra `+15.0%` con la clase `--negative`. El usuario lee `+` y asume "más ingresos". En una app financiera eso es lo peor que puede pasar.
- **Fix:** para egresos, mostrar siempre `−15.0%` (signo real precedido de `−`) y "good" cuando caen.

## B-09 · AI chat no rechaza `aria-modal="false"` + `role="dialog"` — **MEDIO** (a11y)
- **Dónde:** `frontend/src/components/layout/TopBar.tsx:130` y `AiChatPanel.tsx:216-226`.
- **Qué pasa:** `<div role="dialog" aria-modal="false">` es una contradicción de WAI-ARIA. El widget se comporta como popover no modal pero la semántica dice modal. Sin focus trap, sin `Escape` para cerrar en uso de escritorio.
- **Fix:** `role="region" aria-label="Chat IA financiero"`, documentar no-modal, añadir `Escape` que invoque `onClose` cuando exista.

## B-10 · `useDialogFocus` no restaura foco cuando el trigger se desmonta — **ALTO** (a11y, regresión reciente)
- **Dónde:** `frontend/src/hooks/useDialogFocus.ts:71-77`.
- **Qué pasa:** la limpieza intenta `.focus()` al `triggerRef.current`, pero ese ref se captura en `useEffect` de mount. Si el componente que abrió el diálogo se desmonta primero (caso típico: `Navigate` reemplaza la ruta padre), el ref apunta a un nodo ya fuera del DOM y el `.focus()` no hace nada. El usuario pierde el contexto del teclado.
- **Fix:** capturar `document.activeElement` síncronamente al abrir (en una `ref` paralela), no en un `useEffect`. Si se prefiere API declarativa, permitir `useDialogFocus(open, { triggerRef })`.

---

# 2. Workflows que el SPEC promete y el código no entrega

## W-01 · Sin cancelación de plazo fijo — **ALTO**
- **Dónde:** `Services/PlazoFijoService.cs` y `Enums.cs:30`.
- **Qué pasa:** `EstadoPlazoFijo.CANCELADO` existe en el enum y `ProcesarVencimientosAsync` lo salta explícitamente. **Ningún código pone nunca ese estado**. No hay `CancelarAsync`. Un plazo fijo no se puede cancelar desde el sistema.
- **Plan:** método `CancelarAsync(plazoId, motivo)` que (a) valida permisos, (b) marca `estado = CANCELADO`, `fecha_cancelacion = now`, (c) crea un extracto inverso en `CuentaReferenciaId` con el capital devuelto, (d) audit, (e) notificación admin.
- **Bonus:** añadir cálculo de **intereses devengados** con day-count (ACT/360 vs 30/360), tratamiento de vencimiento en sábado/domingo y un mini-calendario de festivos (ES/DO/MX/US/IT) configurable.

## W-02 · Sin aprobación maker-checker para edits de extracto grandes — **ALTO**
- **Hoy:** un usuario con `puede_editar_lineas` puede cambiar el `monto` de cualquier fila sin segunda opinión. La SPEC §3.6 menciona el patrón maker-checker para conciliaciones pero **no** para extractos.
- **Propuesta:** `EXTRACTOS.requiere_aprobacion` (boolean) + `EXTRACTOS.aprobado_por_id` + `EXTRACTOS.aprobado_en`. Disparador: delta `(monto_nuevo − monto_anterior) / saldo > 1%` o importe equivalente > 1 000 USD/EUR. La edición se persiste como `BORRADOR`; un usuario con `puede_revisar_lineas` la aprueba. Diff visible en UI. Audit completo.

## W-03 · Sin "Revertir último lote" desde la UI — **MEDIO**
- **Hoy:** `ImportacionController.RevertirLote` requiere `loteId`. No hay acción "Deshacer última importación de esta cuenta" en la pantalla de import.
- **Fix:** endpoint nuevo `POST /api/importacion/cuentas/{cuentaId}/revertir-ultimo` + botón en `ImportacionPage` con confirmación doble. Mantener la revocación por `loteId` para casos avanzados.

## W-04 · Conciliaciones: score visible pero sin etiqueta de confianza — **MEDIO**
- **Hoy:** `ConciliacionService.SugerirAsync` rellena `Score` (60–100) y lo devuelve, pero la UI no lo muestra y se puede aprobar cualquier match. Un 70 ("5 días de la fecha esperada, sin referencia") se aprueba tan fácil como un 100.
- **Plan:** mostrar `Score`, regla y etiqueta `ALTA/MEDIA/BAJA`. Confirmación explícita cuando `Score < 80`. En la tabla de conciliación, hacer el score una columna ordenable y filtrable.

## W-05 · SMTP caído → alerta silenciosamente perdida — **MEDIO**
- **Dónde:** `Services/AlertaService.cs:120-140` y `PlazoFijoService.TrySendEmailAsync` (sin manejo).
- **Hoy:** un catch traga el error, no se actualiza `FechaUltimaAlerta`, no hay `NotificacionAdmin`.
- **Fix:** emitir `NotificacionesAdmin.tipo = "ALERTA_SALDO_SMTP_FAIL"` y `"PLAZO_FIJO_SMTP_FAIL"` con `Origen = "SMTP"`, `Detalle = motivo del error sin secretos`. Mostrar en badge admin.

## W-06 · Soft-delete de `Titular`/`Cuenta` no se propaga — **MEDIO**
- **Hoy:** `EnsureCuentaPermitidaAsync` exige `cuenta.Activa` pero no `titular.Activo` en algunas rutas (lo cubre `Importacion`/`Exportacion`; ver `ImportacionService.cs:863-912`, pero el resto no). Un titular soft-deleted deja a sus cuentas en `DeletedAt = null` con extractos legibles si conoces el id.
- **Fix:** centralizar en `UserAccessService.ApplyCuentaScope(cuentaQuery, currentUser)` y aplicar en TODOS los controllers; tests de regresión por controller.

## W-07 · Visibilidad de "Recharts" en un chart y manejo de 0 datos heterogéneo — **MEDIO**
- **Dónde:** `frontend/src/components/dashboard/TitularSaldoBarChart.tsx:50-53` y `SaldoPorDivisaCard.tsx:23-25`.
- **Hoy:** `TitularSaldoBarChart` aún usa el tooltip default de Recharts (inconsistente con el resto). `SaldoPorDivisaCard` cuando `items.length === 0` sigue mostrando la cabecera `Saldos por divisa` con un `<p>No hay saldos disponibles.</p>` dentro.
- **Fix:** tooltip propio compartido (mismo componente que `EvolucionChart`), ocultar la card cuando no hay datos.

## W-08 · Notificación admin para export mensual fallido — **BAJO**
- **Hoy:** `ExportacionService.ExportMensual` (línea 215-238) hace `try/catch` que se traga errores. Sin fila en `NotificacionesAdmin`.
- **Fix:** emitir notificación por cuenta fallida con motivo; agrupar al final del job para no spam.

## W-09 · 28 días de retención de auditoría hard-coded — **MEDIO** (compliance)
- **Dónde:** `Jobs/LimpiezaAuditoriaJob.cs:9` (`public const int RetentionDays = 28;`).
- **Hoy:** SPEC §3.7 vende "registro completo" pero el job borra >28 días. Ningún ajuste.
- **Fix:** mover a `CONFIGURACION.auditoria_retention_days`, default 365. Sin cambio retroactivo: respetar el primer valor en el momento del primer arranque.

## W-10 · Sin panel de conciliación de cuenta (estado "X cuentas conciliadas este mes") — **BAJO**
- Falta KPI simple en dashboard: `Conciliado este mes: 18/24 cuentas (75%)`. Endpoint `GET /api/dashboard/conciliacion-resumen` con `cuenta_id`, `estado`, `ultimaConciliacionEn`.

---

# 3. Seguridad

Todos los hallazgos siguientes son de severidad media-baja pero **fáciles de cerrar**. Marco los que considero especialmente importantes.

## S-01 · Token de integración sin expiración sin segundo paso — **MEDIO**
- **Dónde:** `Controllers/IntegracionesController.cs:104-175, 256-339`.
- **Qué pasa:** un admin puede crear un token `SinExpiracion = true` con un solo click. El audit registra `sin_expiracion = true` (bien) pero no hay cap (¿90 días? ¿1 año?), ni motivo obligatorio, ni confirmación con segundo admin.
- **Riesgo:** admin comprometido = exfiltración perpetua.
- **Fix:** cap máximo 365 días, motivo obligatorio ≥ 32 chars, alerta al resto de admins cuando se crea.

## S-02 · Restablecer BD requiere solo `confirmacion = "RESTAURAR"` y un único admin — **MEDIO**
- **Dónde:** `Controllers/BackupsController.cs:255-298`.
- **Qué pasa:** un admin con acceso al endpoint puede tirar la BD abajo con un string. No hay doble confirmación ni rate-limit.
- **Fix:** requerir `security_step_up` (re-MFA), rate-limit 1/24h, log de "destructive operation" como alerta.

## S-03 · `JWT signing key` y `RlsContextSecret` son la misma clave por defecto — **ALTO** (defense-in-depth roto)
- **Dónde:** `Program.cs:455-459` y `Data/RlsDbCommandInterceptor.cs:14-20`.
- **Qué pasa:** si no se configura `Security:RlsContextSecret` por separado, se firma RLS con la misma `JwtSettings:Secret`. Un leak de JWT = capacidad de forjar contextos RLS (bypass efectivo de `FORCE RLS`).
- **Fix:** exigir claves distintas en `Production` (fail startup si son iguales). Documentar rotación: paso 1 generar nueva, paso 2 escribir en DB, paso 3 `ClearAllPools()`.

## S-04 · `MfaRememberDeviceDays = 90` en código (la doc dice 30) — **MEDIO**
- **Dónde:** `Constants/SecurityConfigurationDefaults.cs:5-7`.
- **Qué pasa:** el usuario puede recordar dispositivo 90 días (cookie firmada, 512 bits de entropía, bind a `security_stamp`). Un leak de la cookie = ventana de 90 días sin segundo factor.
- **Fix:** alinear con el SPEC (30 días), bind a `User-Agent` resumido (`{family}/{version}`), invalidar además al cambiar IP/CIDR known.

## S-05 · Race window en detección de reuse de refresh token — **MEDIO**
- **Dónde:** `Services/AuthService.cs:411-518`.
- **Qué pasa:** el advisory lock es por `SHA-256(token)` y se mantiene hasta `CommitAsync`. Si dos peticiones concurrentes con el mismo token llegan casi-a-la-vez, ambas pueden leer antes de que la primera confirme y solo la segunda verá `RevocadoEn` seteado. La cancelación en cadena que sigue revoca los tokens del usuario, pero no escribe audit para la **segunda** petición (la del atacante potencial).
- **Fix:** lock por `UsuarioId` (serializa TODA la actividad de refresh del usuario) **o** key por token+generation. SIEMPRE escribir audit en cualquier rama que toque "reuse".

## S-06 · `LoginAsync` filtra existencia de usuario por timing — **BAJO**
- **Dónde:** `Services/AuthService.cs:90-196`.
- **Qué pasa:** con email inexistente, devuelve 401 sin llamar a `BCrypt.Verify`. El no-user branch es <5 ms; con user existe + bad password son ~120 ms. Enumeración local barata.
- **Fix:** ejecutar siempre un `BCrypt.Verify` contra hash dummy en la rama no-user; redactar `motivo = "usuario_no_encontrado"` en el audit a `motivo = "login_fallido"`.

## S-07 · `BCrypt` policy no chequea breach ni historial — **BAJO**
- **Dónde:** `Constants/SecurityPolicy.cs:20-43`; usado en `Services/AuthService.cs:636`.
- **Fix:** añadir chequeo contra HIBP k-anonymity (opcional, con cache local), y mantener un historial `últimas 5 password_hash` en `USUARIOS_PASSWORD_HISTORIAL` para que `ChangePassword` no permita reciclar.

## S-08 · Redaction de secrets en `/api/configuracion` usa substring match — **BAJO**
- **Dónde:** `Controllers/ConfiguracionController.cs:601-621` (`IsSensitiveConfigKey`).
- **Riesgo:** una clave futura `smtp_auth` o `oauth_refresh` no se redacta porque solo busca `password`, `secret`, `api_key`, `token`, `credential`, `clave`.
- **Fix:** allowlist explícito de claves sensibles.

## S-09 · `Pagina` del OpenClaw endpoint sin upper bound → overflow `Skip` → 500 — **BAJO**
- **Dónde:** `Controllers/IntegrationOpenClawController.cs:210-211`.
- **Fix:** `pagina = Math.Clamp(pagina, 1, 100_000)`.

## S-10 · `IntegracionesController.Eliminar` (soft-delete) no marca `Estado = Revocado` — **BAJO**
- **Dónde:** `Controllers/IntegracionesController.cs:341-364`.
- **Riesgo:** si alguien elimina la cláusula `DeletedAt == null` del middleware (regresión silenciosa), el soft-delete no protege.
- **Fix:** también poner `Estado = Revocado, FechaRevocacion = now`. Belt and suspenders.

## S-11 · `MaxRows = 50_000` definido pero nunca aplicado — **BAJO**
- **Dónde:** `Services/ImportacionService.cs:30-34` (`MaxRawDataLength = 5*1024*1024`, `MaxRows = 50_000`). `MaxRows` no aparece referenciado.
- **Fix:** chequeo tras `ParseRows`. Devolver 413 con mensaje claro.

## S-12 · `DataProtection` keys ACL se delega al instalador — **BAJO**
- **Dónde:** `Program.cs:96-115`.
- **Fix:** aplicar ACL explícito en startup a `C:\ProgramData\AtlasBalance\keys` (`SYSTEM` + cuenta de servicio). Fallar arranque si no se puede.

## S-13 · `IntegrationAuthorizationService` permite titular-sin-país = "este titular en cualquier país" — **BAJO** (diseño)
- **Dónde:** `Services/IntegrationAuthorizationService.cs:35-54` y la RLS policy correspondiente.
- **Qué pasa:** un token con `IntegrationPermission(pais=NULL, titular=A, cuenta=NULL)` puede leer cuentas del titular A en **cualquier** país. La SPEC habla de scopes pero la realidad es por fila, no por intersección.
- **Fix:** documentar y/o forzar que toda fila de permission comparta `PaisId` cuando hay `TitularId` no nulo.

---

# 4. UI / UX

Resumiendo lo nuevo **encima** del `AUDITORIA_UI_UX_GLOBAL_V-02-02.md`:

1. **Dashboard hierarchy & data honesty** (B-05, B-08, F-WF-10): los KPIs necesitan tono correcto + un KPI "Conciliación del mes" para sustituir el conteo de plazos fijos inventado. El bloque hero debe seguir siendo saldo consolidado único, no cards repetidas.
2. **Mobile cohesión** (F-UI-004): la IA flotante debería desaparecer también en tablet (768–1199 px) donde hoy persiste sin bottom-nav que la ancle; coordinar z-index con bottom-nav y modal-backdrop.
3. **N+1 + storms** (F-PERF-001/002): dashboard y openclaw iteran `ConvertAsync` por movimiento. Refactor para agrupar por divisa y pre-agregar antes de convertir.
4. **Sin `AbortController`** en ningún fetch: esto explica por qué cambiar filtros rápido en extractos provoca parpadeos visibles. Patrón a aplicar globalmente (un solo helper compartido `useFetchWithAbort`).
5. **ErrorBoundary mudo** (B-06): crítico que el admin pueda copiar el diagnóstico en producción. Es un cambio pequeño con retorno alto.
6. **A11y concretadas**: B-09 (ai chat role/aria-modal), B-10 (useDialogFocus), `aria-live` en `SessionTimeoutWarning`, `ToastViewport` con `aria-atomic="true"`, `EditableCell` "saved" linked via `aria-describedby`.
7. **Theme tokens**: revisar `dashboard.css` para asegurar que el bloque rojo aparece en light *y* dark sin "saltos" en badges y los gráficos. Hoy hay un par de lugares con valor literal en vez de token.
8. **Chart 0-data**: `SaldoPorDivisaCard` y `ConcentracionDonutCharts` deberían devolver `null` u `EmptyState` cuando no hay datos, no un card vacío.
9. **Microcopy**: mensajes en castellano coherentes — "No se pudo conectar", "Sesión expirada", "Sin permiso para…" deben usar las mismas palabras que la pantalla de login y el toast. Hoy hay variaciones pequeñas.
10. **Resumen de IA**: el chat podría mostrar un ribbon "Última respuesta hace 4 min" para evitar que el usuario crea que está viendo tiempo real cuando es cache. Pequeño pero mejora percepción.

---

# 5. Frontend — rendimiento y robustez

Los hallazgos más importantes para producción:

- **F-PERF-001/002/005/009/012** — storms + N+1 de fetches. Patrón único: helper `useFetcher()` que monta `AbortController`, aborta en cleanup y expone `data/error/loading`.
- **F-STATE-001/002** — `paisScopeStore.clear()` no limpia (re-lee localStorage) y logout no resetea `divisaStore`/`updateStore`/`iaAvailabilityStore`. Siguiente login recibe datos del usuario anterior hasta expirar TTL.
- **F-A11Y-003/012** — `useDialogFocus` con regresión ya explicada (B-10).
- **F-PERF-007** — `iaAvailabilityStore` polling de 60 s con `force=true`; debería ser `force=false` salvo la primera llamada.
- **F-PERF-015** — el `<input>` del nombre de modelo en IA dispara `GET /ia/modelos` por cada pulsación. Debounce 300 ms.
- **F-PERF-013** — `DashboardPage` puede caer en loop si el servidor devuelve `divisa_principal` distinta a la del URL.
- **F-UI-014** — `BackupsPage` no limpia sesión de Drive si el usuario abandona la pestaña durante el OAuth device flow.

---

# 6. Backend — mantenibilidad y deuda técnica

- **`ImportacionService.cs` es un monstruo de 1700+ líneas** con 11 métodos públicos. Romper en clases: `ImportacionFingerprintBuilder`, `ImportacionRowParser`, `ImportacionValidator`, `ImportacionAuditWriter`. Probablemente la mayor oportunidad de bajar bugs futuros.
- **DTOs ad-hoc en `IntegrationOpenClawController`** — no hay `OpenClawDtos.cs`. Crear ahora que se añade `?pais_id` y `?divisa=` como filtros.
- **`AuditActions` constantes dispersas** — algunas acciones se referencian vía constante, otras como string literal (`"importacion_lote_creado"`). Mover todo a `Constants/AuditActions.cs`.
- **`IClock` no se usa** en muchos servicios —>`DateTime.UtcNow` directo. Inyectar `IClock` y mockear en los tests sensibles a fecha (ej. el `AtlasAiServiceTests.AskAsync_Should_Respect_Cuenta_Scope_In_Deterministic_Ranking` ya fallaba por esto en 2026-06-21).
- **`WatchdogSettings` tipado en una tabla `CONFIGURACION`**: muchos valores críticos (`auditoria_retention_days`, `tipos_cambio_max_staleness_days`, `mfa_remember_device_days`, `importacion_max_bytes`, `importacion_max_rows`) viven como string key/value. Wrapper tipado en `Services/ConfiguracionService.cs` con safe-parse y logs cuando falte la clave.
- **`Extractor`** de strings user-facing en castellano — algunos mensajes están en inglés en controllers; revisar y centralizar en `Resources/`.
- **`IntegrationToken.EndpointScopesJson` existe pero no se lee** en ningún sitio. Si está reservado para Fase 2, comentarlo; si no, quitarlo.
- **`LimpiezaAuditoriaJob` debería avisar** antes del borrado, no después.

---

# 7. Gaps en tests (F-TEST-*)

Por severidad:

| Severidad | Brecha | Acción |
|---|---|---|
| Alta | `PlazoFijoService` sin test de cálculo de intereses (no hay código). | Implementar primero el código (W-01) y luego los tests. |
| Alta | `ExportacionService` sin test de inyección de fórmulas (`=HYPERLINK(...)`, `+1+1`, `\t=cmd|...`). | Inyectar payloads y asertar que el cell sale saneado. |
| Alta | `ExtractosController` sin cobertura de `InsertBeforeFilaNumero` / `Restaurar`. | Añadir `ExtractosLifecycleTests` con Testcontainers. |
| Alta | `IntegrationOpenClawController` cobertura insuficiente; agregar test cross-cuenta por filtro. | Foco en `IntegrationAuthorizationService.GetScopeAsync`. |
| Media | `TiposCambioService` sin test de tasa stale. | Una vez aplicado B-04. |
| Media | `ConciliacionService` solo cubre `Confirmar`. | Añadir `Resolver`, `MarcarExcepcion`, `Score < 80`. |
| Media | `UserAccessService` sin tests negativos (`puede_editar != puede_eliminar`). | Tests unitarios de jerarquía. |
| Media | Sin tests para `LimpiezaAuditoriaJob`. | Verificar cutoff + idempotencia. |
| Media | Sin tests para `DashboardService` multi-divisa / sin tasa. | Ver B-04 y F-LOGIC-09. |
| Media | `ImportacionService` happy-path sin verificar partial-ok (`acepta_advertencias`). | Cobertura del flag nuevo. |

---

# 8. Lo bueno (no hace falta tocar)

- **Row-Level Security**: la migración `20260501120000_EnableRowLevelSecurity.cs` y la capa `FORCE RLS` están blindadas. El interceptor `RlsDbCommandInterceptor.cs` distingue correctamente `auth_mode ∈ { user, integration, auth, system, anonymous }` y firma los contextos. La migración `20260626193000_AlignRlsDashboardAccessWithRoles` corrigió el backstop.
- **Auth flow**: `ChangePasswordAsync` exige `mfa_verified_at` vigente del refresh token (no del usuario). `REFRESH_TOKENS.mfa_verified_at` y `security_stamp` enlazados. `Logout` borra `mfa_trusted` (corregido en V-01.09). 
- **CSRF / cookies**: el header `X-CSRF-Token` se inyecta en axios y el estado vive en cookie `csrf_token`. Frontend lo parsea con `getCsrfTokenFromCookie()`.
- **Tokens de integración**: hasheados SHA-256 en BD, `last_used_at` actualizado en cada validación, revocación inmediata vía `Estado = Revocado`.
- **Update flow**: ZIP traversal bloqueado con `Path.GetFullPath` + prefijo con separador, `MaxArchiveEntries` y `MaxExtractedPackageBytes` enforced durante extracción, `MaxUpdatePackageBytes` aplicado durante el stream (no después). Firma RSA/SHA-256 obligatoria excepto `-AllowUnsignedLocal` explícito.
- **Backups**: `Path.GetFullPath` + `StartsWith` para evitar path traversal.
- **IA prompt-injection**: `AtlasAiService.ExtractProviderErrorSummary` redacta; `AtlasAiService.AskAsync` audit NO incluye prompt body (correcto).
- **Frontend**: shell dark-mode permanente, login redesign limpio, focus tokens, `prefers-reduced-motion`, charts con `role="img"`, virtualización real con `@tanstack/react-virtual`.

---

# 9. Top 12 quick wins (ordenados por impacto / esfuerzo)

1. **Quitar `InsertBeforeFilaNumero`** y arreglar `Restaurar` → 1 PR de BD + 1 controller. Cierra **B-01 + B-02**.
2. **Aplicar `MaxStalenessDays` a `ConvertAsync`** + `staleCurrencies` en respuesta del dashboard → 2 cambios. Cierra **B-04** y baja ruido en **F-LOGIC-09**.
3. **`AppErrorBoundary` que avisa y copia diagnóstico** en producción → 1 componente + 1 endpoint opcional. Cierra **B-06**.
4. **Helper "Egresos" + "Inmovilizado" corregidos** en `DashboardPage.tsx` → cambio de UI/JSX. Cierra **B-05 + B-08**.
5. **Cap de 365 días + motivo obligatorio en `SinExpiracion`** para tokens de integración → controller. Cierra **S-01**.
6. **Doble confirmación (security step-up) en `BackupsController.Restaurar`** + rate-limit 1/24h → controller + audit. Cierra **S-02**.
7. **Forzar claves distintas `JwtSettings:Secret` vs `Security:RlsContextSecret` en `Production`** → 1 check en `Program.cs`. Cierra **S-03**.
8. **Toast "Sin permiso para abrir el dashboard" antes de `Navigate`** en `DashboardRoute` → 5 líneas. Cierra **B-07**.
9. **`useDialogFocus` captura `activeElement` síncrono** → 1 hook. Cierra **B-10** y la regresión a11y más molesta.
10. **`AbortController` global** (`useFetcher()`) para todos los listados → 1 hook + propagación a ~10 archivos. Cierra la mayor parte de **F-PERF-***.
11. **`tipo_accion` filtrable en OpenClaw por `pais_id`** + cap de `pagina` → 2 líneas. Cierra **S-09** + añade seguridad defensiva.
12. **Mover `auditoria_retention_days` a `CONFIGURACION`** con default 365 → 1 migración + 1 wrapper. Cierra **W-09**.

---

# 10. Hoja de ruta sugerida (no es orden de implementación, es agrupación)

**Sprint 1 — Higiene crítica (bugs confirmados)**
- B-01, B-02, B-03, B-04 (raíz: BD + 1 endpoint)
- S-03 (1 check en startup)

**Sprint 2 — UX dashboard**
- B-05, B-06, B-07, B-08 + W-10 (dashboard summary + conciliación resumen)
- F-PERF-001/002 (N+1 dashboard/openclaw)

**Sprint 3 — Acceso y seguridad**
- S-01, S-02, S-04, S-08
- B-09, B-10 (a11y críticos)

**Sprint 4 — Workflows anunciados**
- W-01 (cancelación plazo fijo + día-count + festivos)
- W-02 (maker-checker edits grandes)
- W-03 (revertir último lote)
- W-05 (notificación SMTP caído)
- W-07, W-08 (charts/UI)

**Sprint 5 — Tests + mantenibilidad**
- Cobertura de todas las brechas de F-TEST-*
- Romper `ImportacionService.cs` en 3–4 clases
- Wrapper tipado de `Configuracion`
- IClock en servicios date-sensitive

**Sprint 6 — Performance global**
- `AbortController` global + `useFetcher()`
- Cache de Dashboard a 30 s
- Batch conversion en `TiposCambioService`
- IA `force=false` después de primer load

---

# 11. Lo que NO encontré / no pude verificar

- Cobertura E2E autenticada real (regla anti-encallamiento: no levanto Playwright).
- Tests de PostgreSQL real con Testcontainers (Docker no disponible en la máquina).
- Comportamiento real del `OpenClaw` con prompts largos y políticas mixed-read.
- Política real de festivos para `PlazoFijoService` (código no la tiene).

Estos quedan como **gates de release** abiertos, ya conocidos y documentados en `REGISTRO_BUGS.md`.

---

# 12. Anexo — mapa rápido por archivo (no exhaustivo)

Solo los más críticos.

| Archivo | Hallazgos clave |
|---|---|
| `Controllers/ExtractosController.cs` | B-01, B-02, F-LOGIC-20 |
| `Controllers/IntegrationOpenClawController.cs` | F-INPUT-004/005, F-AUTHZ-007, F-LOGIC-06, F-PERF-02 |
| `Controllers/IntegracionesController.cs` | S-01, S-10 |
| `Controllers/BackupsController.cs` | S-02, F-FILE-001 |
| `Controllers/ImportacionController.cs` | F-AUTHZ-002, F-WF-02, F-INPUT-001 |
| `Services/ImportacionService.cs` | F-LOGIC-03/04, F-DX-04 (1700 líneas), F-INPUT-001/003 |
| `Services/PlazoFijoService.cs` | F-LOGIC-07, W-01 |
| `Services/TiposCambioService.cs` | F-LOGIC-05, B-04 |
| `Services/AuthService.cs` | S-04, S-05, S-06 |
| `Services/BackupService.cs` | W-08, F-PERF-07 |
| `Services/ExportacionService.cs` | F-LOGIC-12, W-08 |
| `Services/AlertaService.cs` | F-LOGIC-08 |
| `Services/DashboardService.cs` | F-PERF-01, F-LOGIC-09 |
| `Data/RlsDbCommandInterceptor.cs` | F-RLS-001/002/004 |
| `Program.cs` | S-03, F-SECRET-008 |
| `Jobs/LimpiezaAuditoriaJob.cs` | W-09 |
| `frontend/src/App.tsx` | B-07 |
| `frontend/src/hooks/useDialogFocus.ts` | B-10 |
| `frontend/src/services/api.ts` | patrón sin AbortController, refresh queue OK |
| `frontend/src/pages/DashboardPage.tsx` | B-05, B-08, F-PERF-002 |
| `frontend/src/pages/ExtractosPage.tsx` | F-PERF-001, F-UI-018/019 |
| `frontend/src/components/ia/AiChatPanel.tsx` | F-UI-005, F-A11Y-002 |
| `frontend/src/components/common/AppErrorBoundary.tsx` | B-06 |
| `frontend/src/components/dashboard/SaldoPorDivisaCard.tsx` | W-07 |
| `frontend/src/components/dashboard/TitularSaldoBarChart.tsx` | W-07 (tooltip default Recharts) |

---

# 13. Cierre

Atlas Balance tiene buenos cimientos (RLS, intersección de permisos, auditoría a nivel de celda, paquete firmado, updater hardened, MFA + CSRF). Los huecos que quedan son **concretos, limitados y corregibles en sprints pequeños**. La mayor ganancia para el usuario final viene de cerrar los **cuatro bugs de datos confirmados** (B-01/B-02/B-03/B-04) y los **dos de UX diarios** (B-05/B-07). La mayor ganancia para la seguridad viene de **forzar claves distintas JWT/RLS** (S-03) y **cap+ motivo para tokens sin expiración** (S-01). La mayor ganancia de confianza interna viene de **tests de ciclo de vida del extracto** (F-TEST-07) y del **cálculo de intereses de plazo fijo** (F-TEST-01).

Si quieres, lo próximo natural es atacar el **Sprint 1** (raíces de bugs) y dejar noteado el roadmap en `Documentacion/DOCUMENTACION_CAMBIOS.md` cuando arranquemos.
