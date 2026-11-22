# Verificación de las correcciones del informe de revisión integral

Fecha: 2026-11-19 · Rama: `V-03.02` · Commit revisado: `b00a3de` (LOTE 8; árbol limpio, sin cambios durante la revisión).
Objeto: comprobar, ID por ID, si cada hallazgo de `INFORME_REVISION_INTEGRAL_2026-11-16.md` quedó resuelto de verdad. Revisión en solo lectura: no se ha tocado código, ni el informe original, ni se ha hecho commit.

## 1. Resumen

- **De 113 IDs, 78 están resueltos de verdad.** Además, 18 están parciales, 3 tienen una regresión, 13 siguen abiertos (S-20 y R-10, además, necesitan ejecución) y 1 no se puede verificar sin ejecutar (R-9).
- **Tres regresiones reales**, confirmadas leyendo el código:
  - **B-10:** la IA deja de marcar como «duplicado probable» dos movimientos idénticos del mismo día.
  - **S-17:** con usuario y contraseña SMTP ya no se fuerza STARTTLS.
  - **S-25:** los informes de error del navegador con stack largo se pierden con un 400.
- **Siete correcciones dadas por «CORREGIDO» se quedan cortas:**
  - **J-1:** cinco jobs siguen sin protección.
  - **B-2:** el antispam de avisos no funciona en PostgreSQL.
  - **S-2:** la restricción de columnas del admin se ignora con ciertos alcances.
  - **D-1:** sigue saliendo «+0,0 %».
  - **F-7:** el lote sigue siendo una petición por fila.
  - **M-3:** `CanApproveImportacionAsync` sigue sin uso.
  - **A-6:** la detección de anomalías no aplica los filtros.
- **El estado del informe es incorrecto o está desactualizado en 7 IDs:**
  - R-3 figura sin estado, pero está corregido.
  - T-3 está desactualizado.
  - En M-17 las cifras no cuadran.
  - En M-16 la premisa de la línea con solo CR es falsa.
  - En D-12 los alias se borraron, no se conservaron.
  - S-9 y M-3 afirman que «los tres métodos se usan», y no es cierto.
  - La sección 8.1 cita mal las rutas de integraciones.
- **Tests:**
  - Backend: 1282/1282 en verde, con PostgreSQL real vía Testcontainers.
  - Caching: 15/15.
  - Frontend: `tsc` y `lint` limpios, y 332/332 tests unitarios.
  - Muchos tests de frontend comprueban el fuente con regex o AST, no el comportamiento. Que pasen no prueba que la pantalla funcione.

## 2. Método

- El orquestador repartió los IDs entre 9 subagentes de solo lectura: 7 en Sonnet (A, S-1..12, S-13..25, B, J+F, R+T, D) y 2 en Haiku (M; 8.1 y 9). Cada uno tenía instrucciones de desconfiar del informe y de la bitácora, seguir el flujo real y buscar sitios hermanos y regresiones.
- El orquestador releyó en el código todos los veredictos de regresión, todos los «CORREGIDO» que un subagente rebajó y las cifras contradictorias. Columna **Fuente**:
  - **O**: releído por el orquestador.
  - **S**: solo subagente.
  - **E**: cubierto por tests ejecutados.
- **Regla de veredicto:** cada ID se juzga contra su hallazgo y su arreglo propuesto. Los defectos del mismo tipo en sitios que el hallazgo no citaba van aparte, en la sección 4 («nuevos hallazgos»), y no rebajan el veredicto.
- **Veredictos:**
  - RESUELTO
  - PARCIAL
  - REGRESIÓN: el arreglo rompe algo que antes funcionaba.
  - ABIERTO: sigue abierto, como declara el informe.
  - ESTADO INCORRECTO: el informe no refleja la realidad.
  - NO VERIFICABLE: hace falta ejecutar algo.

Rutas: `API/` = `Atlas Balance/backend/src/AtlasBalance.API/`, `FE/` = `Atlas Balance/frontend/src/`.

## 3. Veredicto por ID

### 3.1 Prioridad alta (A)

| ID | Veredicto | Fuente | Evidencia | Notas |
|---|---|---|---|---|
| A-1 | RESUELTO | S+E | `FE/utils/permisosFormUtils.ts:90-93,211-214,239-273`; `UsuarioModal.tsx:836-878`; `API/DTOs/UsuariosDtos.cs:57-60` | El test de ida y vuelta compara contra una lista escrita a mano, no contra el DTO: no avisará si el DTO gana un flag nuevo. |
| A-2 | RESUELTO | S+E | `API/Services/AuthService.cs:1427-1467`; `UserStateMiddleware.cs:61-89`; `UsuariosController.cs:885-914` (desbloquear, ADMIN y auditoría) | Tests `AuthLot2Tests` y `AuthLot2PostgresTests`. |
| A-3 | RESUELTO | S+E | `BackupService.cs:100-146` (`CancellationToken.None`); `BackupSchedulerJob.cs:104-114` (PENDING > 2 h pasa a FAILED), `:150-212` (aviso por antigüedad) | |
| A-4 | RESUELTO | S+E | `BackupService.cs:75-77,115` (`.tmp` + `File.Move`), `:163-164` (fecha y hora en el nombre), `:253-285` (limpieza de `.tmp`) | |
| A-5 | RESUELTO | S+E | `Constants/AiConfiguration.cs:252-264` (constructor único); usado en `AtlasAiService.cs:755` y `SemanticPlannerClient.cs:119` | Son las dos únicas llamadas POST a OpenRouter. |
| A-6 | PARCIAL | S | `FinancialToolsService.cs`: el filtro se aplica en 7 herramientas y saldos y pendientes rechazan con motivo | `DetectAnomaliesAsync` (`:1057`) ni aplica ni rechaza concepto ni importes, y el arreglo pedía cubrir «todas las herramientas». Impacto bajo. |
| A-7 | RESUELTO | S+E | `FE/queries/invalidation.ts:56`; `ImportacionPage.tsx:550`; 0 `as never` en `FE/` | |
| A-8 | RESUELTO | S+E | `ConfiguracionPage.tsx:461-564` (recargas por sección); `load()` solo al montar | `loadDivisas` sigue pisando ediciones sin guardar de la misma sección (menor). |
| A-9 | RESUELTO | S+E | `PlazoFijoService.cs:240-293` (`JsonContains`), `:64-70`, `:113-119` | Test contra PostgreSQL real en verde. |

### 3.2 Seguridad (S)

| ID | Veredicto | Fuente | Evidencia | Notas |
|---|---|---|---|---|
| S-1 | RESUELTO | S+E | `DashboardService.cs:896-927`; `FE/stores/permisosStore.ts:156-162` | El backend y el front coinciden. |
| S-2 | **PARCIAL** | O | `ExtractosController.cs:1505-1525` (`ScopesChain`); `UsuariosController.cs:1336-1348` | Detalle en 4.1. |
| S-3 | RESUELTO | S+E | `ImportacionService.cs:795-806` (`EliminarLineas`, 404 antes de 403) | El front puede ocultar «Revertir» a quien sí puede revertir (ver 4.2). |
| S-4 | RESUELTO | S+E | `AtlasAiService.cs:2456-2477`; `IaDtos.cs:12-46` | |
| S-5 | RESUELTO | S+E | `UsuariosController.cs:619-637,973-983` | El único cliente (`UsuarioModal.tsx:377-392`) envía el cuerpo completo. |
| S-6 | RESUELTO | S+E | `AtlasAiService.cs:979-1015` | Deja dos filas de auditoría en la rama semántica (menor). |
| S-7 | RESUELTO | S+E | `AuditoriaController.cs:287-306` | La descarga del xlsx de exportaciones no audita (ver 4.2). |
| S-8 | PARCIAL (coherente) | S | `CuentasController.cs:553-570`; `ExtractosController.cs:382-387` | Coincide con lo declarado. Además, con RLS real el usuario sin lectura puede recibir 404 donde los tests en memoria dan 403. **NO VERIFICABLE** sin un test PostgreSQL con RLS. |
| S-9 | RESUELTO (texto inexacto) | O | `UserAccessService.cs:106-120` (mapa único) | El mapa único existe, pero la frase «los tres métodos se usan» es falsa: `CanApproveImportacionAsync` no tiene ningún llamador (ver M-3). |
| S-10 | RESUELTO | S+E | `AuthService.cs:487-499` | Queda una carrera menor entre la comprobación y la escritura. |
| S-11 | PARCIAL | S | `AuthService.cs:1427-1455` (incremento atómico) | Una ráfaga paralela sigue evaluando más contraseñas que el límite: el chequeo va antes de bcrypt y el registro después. Un login correcto borra el contador por IP. |
| S-12 | ABIERTO (coherente) | S | `Program.cs:1139-1148,1298-1308` | Sin cambios. |
| S-13 | RESUELTO | S+E | `AuthService.cs:641-663,723,1019,1141` | |
| S-14 | RESUELTO | S+E | `AuthController.cs:232-247`; `Program.cs:208-213` | `AuditRequestContext.cs:26,50-56` aún clasifica el origen con la cookie legacy (solo afecta a la auditoría). |
| S-15 | RESUELTO | S+E | `AuthService.cs:584-609,1494-1589` | |
| S-16 | RESUELTO | S+E | `SecurityAuditMiddleware.cs:134-157` | |
| S-17 | **REGRESIÓN** | O | `EmailService.cs:317-335` (default `auto` y modo desconocido igual a `auto`), `:355-362`; `SeedData.cs:168`; antes, `hasCredentials ? StartTls : StartTlsWhenAvailable` | Detalle en 4.1. |
| S-18 | RESUELTO | S+E | `SecurityResponseHeaders.cs:17-31` | |
| S-19 | RESUELTO | O+E | `BackupEncryptionService.cs:125-133,256-322,327-331` | V3 autentica el número de bloques en el AAD del último bloque. No se puede degradar a V2 porque el AAD cambia. Los `.enc` V1 y V2 antiguos siguen siendo truncables (compatibilidad asumida). |
| S-20 | ABIERTO / NO VERIFICABLE | S | `RateLimiting/RateLimitingSetup.cs:201-212` (`path.Equals` exacto) | Hay indicios de que es real: `/api/auth/login/` caería en el cubo anónimo. Hay que ejecutar un POST con barra final. |
| S-21 | ABIERTO (coherente) | S | `ActualizacionService.cs:87,408-423` | La versión sale del JSON, no del paquete firmado: es posible un downgrade. |
| S-22 | PARCIAL | S | `IntegrationAuthMiddleware.cs:155-173,280-311` | El orden y la notificación están arreglados. Sigue escribiendo una fila de auditoría por cada 429, en una ruta exenta del limitador global. |
| S-23 | RESUELTO | S+E | `IntegracionesController.cs:280-289,359-367,404-409` | |
| S-24 | RESUELTO | O+E | `IntegracionesController.cs:31-38,57-72` | Los defaults ya son correctos. Un PUT sin `scopes` vuelve a ponerlos (ver 4.2). |
| S-25 | **REGRESIÓN** (efecto colateral) | O+E | `DTOs/TelemetriaDtos.cs:18-34`; `FE/utils/reportClientError.ts:34-40` | Detalle en 4.1. |

### 3.3 Bugs funcionales (B, J, F)

| ID | Veredicto | Fuente | Evidencia | Notas |
|---|---|---|---|---|
| B-1 | PARCIAL | S | `DashboardService.cs:549-555`; aviso en Dashboard, DashboardTitular y Cuentas | La evolución del dashboard (`:257-272`) y la de OpenClaw siguen omitiendo cuentas sin tasa, sin aviso. |
| B-2 | **PARCIAL** | O | `TiposCambioService.cs:465-492` (MANUAL respetado), `:535-550` | Detalle en 4.1. |
| B-3 | RESUELTO | S+E | `ImportacionService.cs:1037-1049`; índice parcial en `AppDbContext.cs:221-224` | Test con PostgreSQL real. |
| B-4 | RESUELTO | S+E | `ImportacionService.cs:788-955` | Una conciliación en `excepcion` puede volver a confirmarse contra un extracto borrado (ver 4.2). |
| B-5 | RESUELTO | S+E | `ConciliacionService.cs:~610-625` | |
| B-6 | RESUELTO | S+E | `ConciliacionService.cs:40-47,436-460` | Una carrera real devolvería 500 por el 23505 sin capturar (raro). |
| B-7 | RESUELTO | S+E | `ExtractosController.cs:437-452` | |
| B-8 | RESUELTO | S+E | `AlertaService.cs:101-186` | |
| B-9 | RESUELTO | S+E | `ImportacionService.cs:~1265-1280` | |
| B-10 | **REGRESIÓN** | O | `FinancialToolsService.cs:1104` y `:1358-1369` (`SonProximos` con `Distinct()`); antes (`2732448^`) era un `GroupBy` con `Count() > 1` | Detalle en 4.1. Las partes de divisa, ranking y seguimiento están bien. |
| B-11 | RESUELTO | S+E | `IntentPlanner.cs:293-348`; `AtlasAiService.cs:2205-2225` | El lock es de proceso; vale para una sola instancia, como declara el informe. |
| B-12 | RESUELTO | S+E | `SmtpTestRateLimit.cs:25-29` | |
| B-13 | RESUELTO | S+E | `ImportacionService.cs:2592-2605`; `FE/utils/formatters.ts:30-100` | Queda la inconsistencia ya documentada con «1,500». |
| B-14 | RESUELTO | S+E | `CuentasController.cs:454-465` | Huecos: restaurar desde la papelera y plazos o movimientos con divisa propia (ver 4.2). |
| B-15 | RESUELTO | S+E | `ExtractosController.cs:146`; `AuditoriaController.cs:337` | Los dos listados citados tienen desempate. Los hermanos van en 4.2. |
| B-16 | RESUELTO | S+E | `FechasOperativas.cs`; `FE/utils/hoy.ts` | Lo citado está corregido. Quedan sitios de negocio con «hoy» en UTC (4.2). 3 de los 6 tests leen el fuente con regex. |
| B-17 | ABIERTO (coherente) | S | `ImportacionService.cs:172,1426,2248-2310` (detector y aviso) | La fila de la tabla no menciona que se añadió el detector. |
| J-1 | **PARCIAL** | O | Atributos en 9 jobs; **0** en `SyncTiposCambioJob`, `PlazoFijoVencimientoJob`, `AutoUpdateJob`, `VerificacionIntegridadAuditoriaJob` y `HealthAlertJob`; sin `GlobalJobFilters` en `Program.cs` | El hallazgo decía «ningún job». El LOTE 3 acotó su alcance con honestidad, pero la tabla dice CORREGIDO sin matiz. Siguen 2 workers con timeouts de hasta 3600 s. |
| J-2 | RESUELTO | S+E | `ExportacionService.cs:243-289` | Si fallan todas las cuentas, el job termina «bien» sin avisar (4.2). |
| J-3 | RESUELTO | S+E | `PlazoFijoService.cs:58-65,95-113` | |
| J-4 | RESUELTO | S+E | `LimpiezaExportacionesJob.cs:69-122` | Las filas que quedan fuera del `export_path` actual no se purgan nunca. |
| J-5 | RESUELTO | S+E | `LimpiezaRefreshTokensJob.cs:37-69` | Test con RLS real. |
| J-6 | RESUELTO | S+E | `AutoUpdateJob.cs:52-71,210-235` | El campo «Hora UTC» de Configuración ya no tiene efecto. |
| F-1 | RESUELTO | S+E | `FE/services/api.ts:212-226`; `sessionRefreshCoordinator.ts` | |
| F-2 | RESUELTO | S+E | Los 4 `Listar` con `soloEliminados`; `PapeleraPage.tsx` | |
| F-3 | RESUELTO | S+E | `AuditoriaPage.tsx:379-390`; `AuditCellModal.tsx:55-65` | |
| F-4 | RESUELTO | S+E | `api.ts:168-191`; `errorToast.ts`; `pollLifecycle.ts` | Doble toast si falla el refresh (cosmético). |
| F-5 | RESUELTO | S+E | `alertasStore.ts:48-97` | |
| F-6 | RESUELTO | S+E | `ExtractoTable.tsx:837-841` | |
| F-7 | **PARCIAL** | S | `CuentaDetailPage.tsx:493-523`; `utils/concurrencia.ts` | Sigue habiendo una petición por fila y sin transacción, que es justo lo que decía el hallazgo. Ahora se paraleliza (4 a la vez) y se informa de los fallos. |
| F-8 | RESUELTO | S+E | `ConfiguracionPage.tsx:142-181` (`NumberField`) | Quedan hermanos en Backups y Conciliación (4.2). |
| F-9 | RESUELTO | S+E | `EditableCell.tsx:35-61` | Si falla la recarga posterior a un guardado correcto, la celda se da por no guardada. |
| F-10 | RESUELTO | S+E | `ImportacionService.cs:282-295`; `ImportacionPage.tsx:245` | Las dos afirmaciones del informe (sospecha descartada y defecto real corregido) son ciertas. |

### 3.4 Rendimiento (R) y CI/tests (T)

| ID | Veredicto | Fuente | Evidencia | Notas |
|---|---|---|---|---|
| R-1 | RESUELTO | S+E | `ExportacionService.cs:101-112,178-179,272-279` | |
| R-2 | RESUELTO | S+E | `AiConfiguration.cs:304-340`; `AtlasAiService.cs:295-344` | |
| R-3 | RESUELTO / **ESTADO INCORRECTO** | S+E | `ImportacionController.cs:114-133`; `ImportacionService.cs:474-516` | Corregido en el LOTE 6, pero la tabla de la sección 6 lo deja sin estado. |
| R-4 | RESUELTO | S+E | `CuentaDetailPage.tsx:345-357` | Posible doble refetch tras guardar una celda. |
| R-5 | RESUELTO | S+E | `canEditCellCache.ts`; `ExtractoTable.tsx:170,611` | Los padres pasan `canEditCell` inestable: la caché solo aprovecha dentro de cada render y en los cambios de foco. |
| R-6 | RESUELTO | S+E | `useDebouncedValue` en Formatos, Usuarios y Configuración | |
| R-7 | PARCIAL (coherente) | S+E | `ExtractosController.cs:923+`; `ImportacionService.cs:1088-1154` | Pendiente real: la evolución de OpenClaw sigue convirtiendo por cada extracto. |
| R-8 | RESUELTO | S+E | `useIaConfigQuery.ts` | `aiChatStore.ts:96` sigue pidiendo `/ia/config` por su cuenta y `iaAvailabilityStore.load()` está muerto. |
| R-9 | NO VERIFICABLE | S | `main.tsx:10-13`; 19 hojas `routes/*.css` | No se ejecutó `vite build` ni el arnés visual. |
| R-10 | ABIERTO / NO VERIFICABLE | S | `AtlasAiService.cs:2005` (`COALESCE`) frente al índice `lower(concepto)` | Hace falta `EXPLAIN`. |
| R-11 | ABIERTO (coherente) | S | `shell.css:45,417` (`backdrop-filter`) | |
| T-1 | RESUELTO | S | `ci.yml:201-242`; `release.yml:157-172` | Solo corre en push a `main`: los PR no lo pasan. No se ejecutó Playwright. |
| T-2 | RESUELTO | S | `ci.yml:20-50` (`policy-gates`) | No se ejecutaron los `*.Tests.ps1`. |
| T-3 | ABIERTO / **ESTADO DESACTUALIZADO** | S | Ya existen `BackupSchedulerJobTests`, `TelemetriaControllerTests` y `DataProtectionSecretProtectorTests` | Siguen sin test Conciliación, Divisas, IA, Importación, Dashboard (controladores), `PlazoFijoVencimientoJob` y `ExportMensualJob`. |
| T-4 | ABIERTO (coherente) | S | Sin tests de `authStore`, `uiStore` ni de los interceptores | |
| T-5 | ABIERTO (coherente) | S | Ningún workflow ejecuta CodeQL ni PSScriptAnalyzer | |

### 3.5 Diseño (D)

| ID | Veredicto | Fuente | Evidencia | Notas |
|---|---|---|---|---|
| D-1 | **PARCIAL** | O+E | `DashboardPage.tsx:284` (`variacionPct > 0 ? '+' : ''` sobre el valor crudo, también en `:307,317,391,405` y `DashboardTitularPage.tsx:216,241,255`) | 0,04 sale «+0,0 %», el defecto citado. `formatPercent` en sí está bien. |
| D-2 | RESUELTO | S+E | Las 4 pantallas citadas están limpias | Quedan textos sin tilde en otras pantallas (4.2). |
| D-3 | RESUELTO | S+E | `PageSkeleton.tsx`; 37 usos | |
| D-4 | RESUELTO | S | 55 media queries, todas en los tres umbrales | `@container (max-width: 760px)` en `routes/dashboard.css:218`. Sin test. |
| D-5 | RESUELTO | S | `system-coherence.css` ya no existe; `layout/page.css` | La equivalencia de cascada no se puede verificar sin el arnés visual. |
| D-6 | PARCIAL (coherente) | S | `styles/components/{modal,pagination,tabs}.css` | Lo pendiente que se declara (`.users-pagination`) es exacto. |
| D-7 | RESUELTO | S | Ningún `var(--x)` queda sin definir (268 definidos, 253 usados) | Siguen tokens duplicados en dos hojas con valores distintos (`--space-1..8`, `--amount-*`…). |
| D-8 | RESUELTO | S+E | `ConfiguracionPage.tsx:187,273,631`; `chartColors.ts` | |
| D-9 | RESUELTO | S+E | `role="dialog"` solo en `Modal.tsx` y en 4 no modales | |
| D-10 | RESUELTO | S+E | 24 `<table>` con 24 `<caption>`; 0 `<th>` sin `scope` | |
| D-11 | PARCIAL | S | 29 `showSign` | `DashboardPage.tsx:276,301,474` (saldo consolidado, disponible e intereses) sin `showSign`, cuando `DashboardTitularPage` sí lo lleva. |
| D-12 | RESUELTO / texto inexacto | S | 0 usos de `--text-16/20/32` | El informe dice que `--weight-medium` y `--font-weight-heavy` se conservan como alias, pero se borraron. |
| D-13 | RESUELTO | S+E | `permisosStore.ts:133-155`; coincide con `UserAccessService.cs:109-117` | |

### 3.6 Mantenibilidad (M)

| ID | Veredicto | Fuente | Evidencia | Notas |
|---|---|---|---|---|
| M-1 | RESUELTO | S+E | Servicio y tests borrados, sin referencias | El build compila. |
| M-2 | RESUELTO | S+E | `AtlasAiService.cs:1892-1914,2024` | |
| M-3 | **PARCIAL** | O | `grep CanApproveImportacionAsync` en `API/`: solo la interfaz y la definición | Sigue sin uso. Solo lo prueba un test aislado. |
| M-4 | ABIERTO (coherente) | O | `AtlasAiService.cs` mide 3.865 líneas (el informe dice 3.744) | |
| M-5 | PARCIAL | S | `ExtractosController.cs:1280-1288` resuelto | `GetPermission` (`:1299-1322`) sigue reimplementando el cruce país, titular y cuenta. |
| M-6 | PARCIAL | S | 50 clases borradas | Quedan clases sin uso: `.account-excel-wrap`, `atl-table`, `atl-kbd`, `dashboard-insert-*`… Antes de borrarlas hay que descartar que se construyan dinámicamente. |
| M-7 | ABIERTO (coherente) | O | 12 `eslint-disable exhaustive-deps` (el informe dice 14) | |
| M-8 | ABIERTO (parcial) | S | `parseDecimalInput` duplicado en `CuentaDetailPage.tsx:62` y `ExtractosPage.tsx:53` | Dos de los cuatro puntos están resueltos sin documentar. |
| M-9 | PARCIAL (coherente) | S+E | `AppDbContext.cs:27`; snapshot `:2151` | `SnapshotModelDriftTests` no detectaría un `AlterColumn` sobre `PLAZOS_FIJOS`. |
| M-10 | RESUELTO | S | `.gitignore:36-39,68` | |
| M-11 | RESUELTO | S | Movido a `Documentacion/Diseno/Archivo/` | |
| M-12 | RESUELTO | S+E | 0 referencias a `ICacheMetricsSink` | |
| M-13 | PARCIAL (residuo menor) | S | `AmountTone` (`formatters.ts:105`) exportado sin uso externo | |
| M-14 | RESUELTO | S | Las 4 fuentes de versión coinciden en V-03.02 / 3.2.0 | No se ejecutó `Check-VersionAlignment.ps1`. |
| M-15 | RESUELTO | S | `install.cmd` y `update.cmd` llaman a los canónicos | |
| M-16 | ABIERTO (coherente) / **premisa falsa** | O | 0 CR sueltos en los dos `.gitignore` (fichero de trabajo y blob de HEAD) | La «línea con solo CR» del informe no existe. |
| M-17 | RESUELTO / **cifras incorrectas** | O | `DOCUMENTACION_CAMBIOS.md` tenía 28.325 líneas en `4ec9488`; ahora 859 + 27.558 en el archivo | El informe dice «23.565 → 614» y «23.030». El `README` del archivo dice 17 informes y se archivaron 18. No se perdió ninguna línea. |

### 3.7 Secciones 8.1 y 9

| Punto | Veredicto | Fuente | Notas |
|---|---|---|---|
| 8.1: dispositivos MFA y Revertir lote | RESUELTO | S+E | `DispositivosMfaPage.tsx:41,68,97`; `ImportacionPage.tsx:644`. |
| 8.1: el resto de endpoints huérfanos | ABIERTO (coherente) | S | `importacion/validar`, `importacion/confirmar` (raíz), `lotes/{id}/filas`, `cuentas/{id}/resumen`, `usuarios/{id}/permisos*` y `/emails` (también POST y DELETE, que no se citan), `sistema/version-actual`, `sistema/metricas` y `revision/settings`. |
| 8.1: rutas de integraciones | **ESTADO INCORRECTO** | S | No existen `/api/integraciones/{id}` ni `.../{id}/auditoria`. Las rutas reales son `/api/integraciones/tokens/{id}[/auditoria]`, y siguen huérfanas. |
| 9: paginación en `lotes/{id}/filas` y conciliación | RESUELTO | S+E | `ContratoApiPaginacionTests`. |
| 9: `sortBy`/`sortDir` en países, auditoría, lotes, comisiones y seguros | RESUELTO | S+E | |
| 9: `sortDir` por defecto en usuarios, cuentas, titulares y formatos | RESUELTO | S+E | Usuarios no tiene test ni desempate `ThenBy(Id)`. |
| 9: desempate en `/api/extractos` | RESUELTO | S+E | Sitio hermano: con `sortBy` desconocido se ignora `sortDir` (`ExtractosController.cs:139`). |
| 9: texto del contrato | **ESTADO INCORRECTO** | S | El encabezado dice `pageSize` y `totalPages`; el JSON real es `page_size` y `total_pages` (snake_case). |

## 4. Problemas que hay que corregir

### 4.1 Regresiones y correcciones incompletas (prioridad)

1. **B-10, la IA deja de detectar duplicados del mismo día.**
   - Dónde: `FinancialToolsService.cs:1358-1369`.
   - Qué pasa: `SonProximos` hace `fechas.Distinct()`. Dos movimientos idénticos con la misma fecha se reducen a una sola fecha, la función devuelve `false` y no se marca `DUPLICADO_PROBABLE`.
   - Antes del LOTE 4 el `GroupBy(...).Count() > 1` sí los detectaba.
   - Ningún test usa fechas iguales.
   - Arreglo: contar elementos y no fechas distintas, más un test con fecha idéntica.
2. **S-17, STARTTLS deja de ser obligatorio con credenciales.**
   - Antes: `hasCredentials ? StartTls : StartTlsWhenAvailable`.
   - Ahora: el modo por defecto es `auto` (seed y fallback de modo desconocido), es decir `StartTlsWhenAvailable` con o sin credenciales.
   - Consecuencia: en una instalación existente con usuario SMTP, si el servidor (o alguien en medio) no anuncia STARTTLS, la app se autentica y envía en claro, con solo un warning en el log (`EmailService.cs:355-372`).
   - El defecto original (sin credenciales en claro) sigue igual por defecto.
   - El comentario de `:314-315` («un valor mal escrito no puede abrir el canal en claro») es falso.
   - Arreglo: default `required`, o `required` siempre que haya credenciales, y un test del envío.
3. **S-25, los informes de error del navegador con stack largo se pierden.**
   - `reportClientError.ts:34-40` envía `stack`, `componentStack` y `mensaje` sin recortar.
   - El DTO rechaza con 400 lo que supere 4000 o 500 caracteres, que son justo los errores más útiles.
   - Arreglo: recortar en el cliente o en el servidor (ya existe `Recortar`), no rechazar.
4. **B-2, el antispam de avisos de tipos de cambio no funciona en PostgreSQL.**
   - Dónde: `TiposCambioService.cs:545-547`.
   - Qué pasa: busca el texto `"clave":"x"` dentro de `DetallesJson`, que es `jsonb`. PostgreSQL devuelve el JSON normalizado con espacio (`"clave": "x"`); comprobado con `select '{"clave":"x"}'::jsonb::text`.
   - En producción el aviso se repetirá en cada sincronización fallida. En los tests (InMemory) ocurre lo contrario: el aviso se suprime para siempre, porque no filtra por fecha ni por leído.
   - Además siguen sin aviso, solo con `LogWarning`, «menos de 2 divisas», «sin clave API» y «ninguna tasa aplicable».
   - Arreglo: comparar sobre el JSON deserializado o con `JsonContains`, con una ventana temporal.
5. **S-2, la restricción de columnas del admin se ignora con ciertos alcances.**
   - Dónde: `ScopesChain` (`ExtractosController.cs:1505-1525`).
   - Qué pasa: para una cuenta solo prueba (P,T,C), (P,T,∅), (∅,T,∅), (P,∅,∅) y (∅,∅,∅). Una preferencia guardada con alcance (∅,T,C) o (P,∅,C) no coincide con ninguno.
   - Esas filas son legales: `ValidatePermisosAsync` solo exige titular **o** país junto a la cuenta, y el payload del modal envía `pais_id: permiso.pais_id || null`. Su whitelist de columnas no se aplica.
   - Además, `search` y `sortBy` sobre `concepto` y `comentarios` y `GET {id}/audit-celda` exponen columnas ocultas.
   - Verificado por lectura; **no ejecutado**.
6. **J-1, cinco jobs sin `DisableConcurrentExecution` ni `AutomaticRetry`:** `SyncTiposCambioJob`, `PlazoFijoVencimientoJob` (envía emails), `AutoUpdateJob` (cada hora), `VerificacionIntegridadAuditoriaJob` y `HealthAlertJob`. No hay filtro global.
7. **D-1, «+0,0 %».** El `+` se antepone según el valor crudo (`> 0`), no según el valor redondeado. Afecta a 8 sitios de los dos dashboards.
8. **M-3 / S-9.** `CanApproveImportacionAsync` sigue sin llamadores. Hay que usarlo o borrarlo, y corregir el texto de las dos filas.
9. **F-7.** Sigue habiendo una petición por fila y sin transacción. Si se acepta como decisión de producto, la fila debería decir eso y no CORREGIDO.
10. **A-6.** `DetectAnomaliesAsync` no aplica ni rechaza los filtros de concepto e importe.

### 4.2 Nuevos hallazgos (sitios hermanos fuera del alcance de cada ID)

- **Seguridad**
  - `IntegracionesController.Actualizar`: un PUT sin `scopes` repone los 5 por defecto, lo que amplía en silencio un token restringido. Además no rechaza tokens revocados (`:291-297`, `:774-777`).
  - `ImportacionPage.tsx:352-356`: `canDeleteInCuenta(cuentaId, undefined, …)` excluye los permisos de eliminar acotados a un titular, así que el botón Revertir se oculta a quien el backend sí deja revertir.
  - `ExportacionesController.Descargar` (`:173-214`) no audita la descarga y responde 404 sin acceso, cuando extractos responde 403.
  - `IntegrationAuthMiddleware.cs:148`: un token válido reinicia el contador de intentos inválidos de su IP.
- **Datos y negocio**
  - Desempate de paginación ausente en Usuarios (`UsuariosController.cs:1481-1500`), Backups, Exportaciones, Integraciones (`:107`, `:582`) y OpenClaw (`:764`).
  - «Hoy» en UTC en `DashboardService.cs:251,579,704`, `CuentasController.cs:290,504`, `IntegrationOpenClawController.cs:465,1040`, `PlazoFijoService.cs:183` y `PlazoFijoVencimientoJob.cs:20`.
  - `FinancialToolsService.cs:1114-1131` (`IMPORTE_ATIPICO`) agrupa por titular y mezcla divisas.
  - B-14: restaurar extractos desde la papelera tras cambiar la divisa reintroduce el problema.
  - B-4: una conciliación en `excepcion` puede volver a `conciliada` contra un extracto borrado.
  - J-2: si fallan todas las cuentas, la exportación mensual termina «bien» sin avisar al admin.
  - J-4: las exportaciones de un `export_path` anterior no se purgan nunca.
- **Frontend**
  - Siguen con `Number(e.target.value)` (vaciar el campo da 0): `BackupsPage.tsx:518,550` y `ConciliacionPage.tsx:450`.
  - `TokenList.tsx:113` formatea un porcentaje a mano.
  - Textos sin tilde en `UsuarioModal.tsx:699,724`, `BackupsPage.tsx:652`, `ConciliacionPage.tsx:525`, `CuentaDetailPage.tsx:944`, `DashboardTitularPage.tsx:318`, `TitularDetailPage.tsx:70` y `FormatosImportacionPage.tsx:341,353`.
  - `PapeleraPage.tsx:179,204-210`: el mensaje «restaurado correctamente» se borra al instante (preexistente).

### 4.3 Correcciones al texto del informe

- R-3: añadir «CORREGIDO (LOTE 6)» en la tabla de la sección 6.
- T-3: actualizar, porque ya existen `BackupSchedulerJobTests`, `TelemetriaControllerTests` y `DataProtectionSecretProtectorTests`.
- J-1, B-2, F-7 y D-1: rebajar a PARCIAL.
- B-10, S-17 y S-25: marcar la regresión.
- S-9 y M-3: quitar «los tres métodos se usan».
- D-12: los alias se borraron, no se conservaron.
- M-16: la premisa del CR es falsa.
- M-17: las cifras reales son 28.325 → 859 + 27.558, y el archivo tiene 18 informes, no 17.
- M-4: el fichero mide 3.865 líneas.
- M-7: son 12 disables, no 14.
- Sección 8.1: las rutas reales son `/api/integraciones/tokens/{id}`.
- Sección 9: `page_size` y `total_pages`.
- Hay filas y párrafos duplicados en la sección 20 (D-6, observaciones abiertas).

## 5. Verificado, bloqueado y pendiente

**Verificado**
- Lectura estática de los 113 IDs y de las secciones 8.1 y 9, por subagentes. El orquestador releyó todos los veredictos de regresión y de PARCIAL que contradicen al informe.
- Backend: `dotnet test AtlasBalance.sln --artifacts-path <scratchpad>`.
  - API.Tests: 1268/1282 en la pasada completa, con PostgreSQL real vía Testcontainers.
  - Los 14 restantes fallaron porque `TestSourceLocator` no encontraba el fuente desde la carpeta de artefactos del scratchpad. Se repitieron con la ruta resuelta y pasan: 27/27 de esas 5 clases.
  - Caching.Tests: 15/15.
- Frontend:
  - `tsc --noEmit` y `npm run lint`: limpios.
  - Tests unitarios: 330/332 en la pasada, compilados en el scratchpad porque `.test-dist/` da EPERM. Los 2 restantes (`nivelesEncabezado`) dependen del directorio de trabajo y pasan 2/2 desde `frontend/`.
- `jsonb::text` comprobado contra el PostgreSQL de desarrollo (B-2).

**Bloqueado**
- `npm run test:unit` y `dotnet test` en sus rutas canónicas: EPERM y acceso denegado en `frontend/.test-dist/` y `backend/**/obj`. Es el mismo bloqueo de permisos que registra la bitácora del LOTE 8. Se rodeó con salidas en el scratchpad.

**Pendiente (no ejecutado)**
- `vite build` y Playwright (`e2e/visual`): R-9, D-4, D-5, D-6, D-7 y T-1.
- Scripts `*.Tests.ps1` y `Check-VersionAlignment.ps1`: T-2 y M-14.
- `EXPLAIN` en PostgreSQL: R-10.
- Medir el render: R-11.
- POST con barra final: S-20.
- Test con RLS real: S-8.
- Reproducir S-2, B-2 y S-17 en ejecución.
- Ninguna validación visual en navegador.

## 6. Corrección posterior (2026-11-19)

Los diez puntos de la sección 4.1 se han corregido en el código: las regresiones B-10, S-17 y S-25, y las correcciones incompletas B-2, J-1, S-2, F-7, D-1, M-3 y A-6. El detalle está en la sección 21 de `INFORME_REVISION_INTEGRAL_2026-11-16.md`.

Los nuevos hallazgos de la sección 4.2 (sitios hermanos) y las correcciones de texto de la sección 4.3 (salvo el estado de esos diez IDs) siguen pendientes.

Durante la corrección apareció un detalle de los tests:
- El fixture HTTP de `TelemetriaControllerTests` recibía 400 del filtro de host (`AllowedHosts=localhost` frente a `127.0.0.1`), no de la validación.
- El test antiguo «400 con stack de 5000» pasaba por ese motivo, no por el `[MaxLength]`.
- Se ha corregido en ese fixture. Otros fixtures que llamen por `127.0.0.1` pueden tener el mismo problema: no se han revisado.
