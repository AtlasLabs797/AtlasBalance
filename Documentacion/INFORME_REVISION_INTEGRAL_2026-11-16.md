# Informe de revisión integral — Atlas Balance V-03.02

Fecha: 2026-11-16 · Rama: `V-03.02` (commit `4b3bf58`) · Informe de revisión: las secciones 12 a 14 documentan las correcciones que ya se han aplicado sobre este mismo árbol (LOTE 1, LOTE 4 y LOTE 3).

## 1. Resumen

La base es sólida en lo que más se ha auditado: SQL parametrizado, cookies `__Host-` con `SameSite=Strict`, refresh tokens rotados con detección de reutilización, cadena de actualización firmada, alcance de permisos aplicado en el chat de IA y en la integración OpenClaw. No se encontró ninguna escalada de rol, IDOR directo ni XSS.

Lo que sí hay son fallos funcionales y de coherencia que las auditorías anteriores, centradas en seguridad y accesibilidad, no cubrían:

1. **Guardar un usuario le borra cuatro permisos** (revisar, aprobar importaciones, conciliar, cerrar conciliación) y la interfaz no permite concederlos.
2. **Una cuenta puede quedar bloqueada de forma indefinida** con un intento fallido cada 30 minutos, incluido el administrador, sin desbloqueo posible desde la app.
3. **Los backups automáticos pueden dejar de hacerse sin avisar** si uno queda en `PENDING`, y con frecuencia horaria se pisan el fichero del día.
4. **El asistente de IA puede dar cifras erróneas con aspecto de dato**: ignora el filtro por concepto y mezcla divisas.
5. **Validar una importación muestra siempre un error falso.**
6. **Los totales en divisa omiten en silencio las cuentas sin tipo de cambio**, y la sincronización pisa las tasas puestas a mano.

Recuento de hallazgos: 9 altos, 44 medios, 38 bajos, informativos o sospechas, y 29 de código muerto, mantenibilidad, CI y tests.

De los nueve altos, solo A-9 constaba ya en `REGISTRO_BUGS.md`. Los otros ocho no aparecen en ninguna auditoría anterior de `Documentacion/`. Sobre A-2: la auditoría del checklist de hoy da por hecho que el desbloqueo es «automático a los 30 min o por admin»; en el código no existe el desbloqueo por admin y el automático no reinicia el contador.

## 2. Cómo se hizo y qué valor tiene cada hallazgo

Siete subagentes en solo lectura (seis en Sonnet, uno en Haiku), uno por área. El orquestador abrió después el código citado de todos los hallazgos altos y de buena parte de los medios.

| Marca | Significado |
|---|---|
| **V** | Verificado por el orquestador leyendo el código citado |
| **S** | Reportado por un subagente como confirmado; no releído por el orquestador |
| **?** | Sospecha: requiere ejecutar, medir o consultar a negocio |

**Verificado:** lectura estática del código. **No hecho:** no se ejecutó build, tests, la app ni ninguna consulta contra PostgreSQL; no hubo validación visual en navegador. Ningún hallazgo está comprobado en ejecución.

Rutas abreviadas: `API/` = `Atlas Balance/backend/src/AtlasBalance.API/`, `FE/` = `Atlas Balance/frontend/src/`.

## 3. Prioridad alta

| ID | Qué pasa | Dónde | Arreglo | Estado |
|---|---|---|---|---|
| A-1 | **Editar un usuario revoca cuatro permisos.** El formulario solo maneja seis flags; `puede_revisar_lineas`, `puede_aprobar_importaciones`, `puede_conciliar` y `puede_cerrar_conciliacion` no se cargan ni se envían. El backend reemplaza la matriz entera, así que quedan a `false` en cada guardado y no se pueden conceder desde la interfaz. Conciliación exige `PuedeConciliar` estricto, de modo que para no-ADMIN esas funciones son inalcanzables. | `FE/components/usuarios/UsuarioModal.tsx:329-360`; `API/DTOs/UsuariosDtos.cs:51-54`; `API/Controllers/UsuariosController.cs:317-320` | Añadir los cuatro flags a la fila del formulario, a la carga y al payload. Test de ida y vuelta que falle si el payload pierde un flag del DTO. | V - **CORREGIDO** (LOTE 1, 2026-11-16) |
| A-2 | **Bloqueo de cuenta indefinido.** `FailedLoginAttempts` solo vuelve a 0 con un login correcto. Pasados los 30 min el contador sigue en 5, así que un único fallo vuelve a bloquear. Durante el bloqueo se rechazan también las sesiones ya abiertas, y no existe desbloqueo para ADMIN. Quien conozca un email deja fuera a ese usuario con una petición cada 30 min. | `API/Services/AuthService.cs:182-214`, `:260`; `API/Middleware/UserStateMiddleware.cs:71` | Poner el contador a 0 cuando caduque el bloqueo; no invalidar sesiones activas por bloqueo; acción «Desbloquear» para ADMIN. | V - **CORREGIDO** (LOTE 2, 2026-11-16) |
| A-3 | **Un backup en `PENDING` detiene los automáticos para siempre.** Si el servicio se para a mitad de un backup, el `SaveChanges` del `catch` usa el token ya cancelado y la fila queda `PENDING`. El planificador omite el backup mientras exista alguna, y nada las recupera. No hay alerta de «sin backup desde hace N días». | `API/Services/BackupService.cs:117-123`; `API/Jobs/BackupSchedulerJob.cs:43-49` | `CancellationToken.None` en el `catch`; pasar a `FAILED` los `PENDING` con más de 2 h; alerta por antigüedad del último backup correcto. | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| A-4 | **Los backups automáticos del mismo día comparten fichero.** `AUTO` usa `backup_yyyy-MM-dd.dump` y la frecuencia admite cada 1-168 h. Un segundo `pg_dump` que falle a medias destruye el dump bueno mientras su fila sigue en `SUCCESS`. | `API/Services/BackupService.cs:50-53`; `API/Services/BackupConfigurationService.cs:293,341-344` | Incluir la hora en el nombre, o escribir a `.tmp` y renombrar solo si termina bien. | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| A-5 | **El planificador semántico de IA llama a OpenRouter sin las restricciones de privacidad.** La llamada principal añade `zdr: true` y `data_collection: "deny"`; la del planificador no. Contradice lo que afirma el inventario de datos personales. | `API/Services/IaPlanner/SemanticPlannerClient.cs:84-95` frente a `API/Services/AtlasAiService.cs:732-735` | Un único constructor de petición para ambas llamadas. | V - **CORREGIDO** (LOTE 4, 2026-11-16) |
| A-6 | **La IA ignora el filtro por concepto en los totales.** El planificador acepta `filtros.concepto`, pero totales, ranking, tendencia y último movimiento no lo aplican (tampoco importe mínimo o máximo). «¿Cuánto he gastado en seguros?» puede devolver el total de todos los gastos, sin aviso. | `API/Services/IaPlanner/FinancialToolsService.cs:294-384`, `:482-554`, `:636-680` (solo `Search`, `:779`, filtra) | Aplicar los filtros en todas las herramientas; hasta entonces, rechazar el plan cuando traiga concepto y la operación no sea búsqueda. | V - **CORREGIDO** (LOTE 4, 2026-11-16) |
| A-7 | **Validar una importación muestra un error falso.** `invalidate('catalogo' as never)`: la clave no existe en `mutationInvalidation`, se lanza `TypeError` y el `catch` muestra «No se pudo validar la importación» cuando la validación fue bien. | `FE/pages/ImportacionPage.tsx:529`; `FE/queries/invalidation.ts` | Usar una clave real y quitar el `as never`. | V - **CORREGIDO** (LOTE 1, 2026-11-16) |
| A-8 | **Configuración pierde lo escrito sin guardar.** Cualquier acción menor (tipo de cambio, sincronizar, divisas, tokens) llama a `load()`, que pone la página en skeleton y recarga todo el estado. | `FE/pages/ConfiguracionPage.tsx:554,569,623,772` | Recargar solo la sección afectada. | V - **CORREGIDO** (LOTE 1, 2026-11-16) |
| A-9 | **El job de vencimientos de plazo fijo falla en PostgreSQL** (ya conocido, sigue abierto). `Contains` sobre una columna `jsonb` genera `jsonb ~~ jsonb` (42883). Como el cambio de estado ya se guardó antes, al reintentar el aviso de «próximo a vencer» se pierde para siempre. | `API/Services/PlazoFijoService.cs:196-203`, `:48-62` | `EF.Functions.JsonContains` con test contra PostgreSQL real; no usar el cambio de estado como disparador. | V - **CORREGIDO** (LOTE 5, 2026-11-17) |

## 4. Seguridad

### 4.1 Permisos y fuga de datos

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| S-1 | Media | El dashboard da saldos a quien solo tiene permisos de escritura (`PuedeImportar`, `PuedeAgregarLineas`) sin `PuedeVerCuentas`; extractos y cuentas sí lo exigen. Para GERENTE se ignora `PuedeVerDashboard`. | `API/Services/DashboardService.cs:887-917` frente a `API/Services/UserAccessService.cs:200-206` | V - **CORREGIDO** (LOTE 6, 2026-11-18): el dashboard exige `PuedeVerDashboard` **y** `PuedeVerCuentas` en la misma fila para todo rol no ADMIN (la excepción de GERENTE, que ignoraba `PuedeVerDashboard`, desaparece) y se elimina el helper `GrantsAccountDataAccess`, que concedía lectura de saldos con cualquier flag de escritura. |
| S-2 | Media | Las columnas que el administrador oculta a un usuario no se filtran en el servidor (`GET /api/extractos` devuelve todas) y el propio usuario puede sobrescribir esa preferencia con `PUT /api/extractos/columnas-visibles`. | `API/Controllers/ExtractosController.cs:766-795`; `API/Controllers/UsuariosController.cs:1228-1244` | V - **CORREGIDO** (LOTE 6, 2026-11-18): la regla del administrador y la preferencia del usuario se separan (columna nueva `columnas_visibles_admin`); el servidor filtra las columnas en `GET /api/extractos` y el `PUT` del usuario guarda la **intersección** con la regla del admin, de modo que ya no puede ampliarla. Nota: las columnas base no-nullable de la fila (`fecha`, `monto`, `saldo`...) no se pueden ocultar sin cambiar el contrato; ver la seccion 19. |
| S-3 | Media | Revertir un lote es un borrado masivo que no exige `PuedeEliminarLineas`: basta `PuedeImportar`. | `API/Services/ImportacionService.cs:643`, `:1135` | V - **CORREGIDO** (LOTE 6, 2026-11-18): revertir un lote exige `PuedeEliminarLineas` (borrado masivo); antes bastaba `PuedeAprobarImportaciones` o `PuedeImportar`. 404 (lote inexistente) ≠ 403 (sin permiso) y el bypass de ADMIN se mantiene. |
| S-4 | Media | `GET /api/ia/config` devuelve presupuesto y gasto global de IA a cualquier usuario autenticado; `/modelos` sí comprueba el permiso. | `API/Controllers/IaController.cs:35-45`; `API/Services/AtlasAiService.cs:95-106` | V - **CORREGIDO** (LOTE 4, 2026-11-16) |
| S-5 | Media | `UpdateUsuarioRequest` tiene valores por defecto destructivos: un `PUT` con cuerpo parcial desactiva al usuario, lo baja a EMPLEADO y le borra permisos y emails. | `API/DTOs/UsuariosDtos.cs:104-115` | V - **CORREGIDO** (LOTE 6, 2026-11-18): el PUT pasa a ser **estricto**. Los campos que deben distinguir "enviado" de "no enviado" son anulables en el DTO y el controller responde **400 con la lista de los campos ausentes**; nada se aplica a medias. `CreateUsuarioRequest` no se toca (sus valores por defecto son sensatos). |
| S-6 | Media | Las respuestas locales del planificador de IA no se auditan ni cuentan contra los límites: un usuario puede extraer saldos por el chat sin rastro. | `API/Services/AtlasAiService.cs:1014-1070` | S - **CORREGIDO** (LOTE 4, 2026-11-16) |
| S-7 | Baja | La exportación CSV de auditoría (hasta 50.000 filas) no deja rastro de quién la extrajo. | `API/Controllers/AuditoriaController.cs:201-282` | ? - **CONFIRMADO Y CORREGIDO** (LOTE 6, 2026-11-18): `ExportarCsv` no dejaba ninguna fila de auditoría (ni el interceptor de entidades ni `SecurityAuditMiddleware` cubren una lectura). Ahora escribe `AUDITORIA_EXPORTACION_CSV` con el actor, el número de filas y los filtros, y la acción queda también en el registro de seguridad. |
| S-8 | Baja | Respuestas 404 antes que 403 en varios endpoints (oráculo de existencia; ids GUID). | `ExtractosController`, `CuentasController.cs:535-544` | S - **PARCIAL** (LOTE 6, 2026-11-18): en `CuentasController.ActualizarNotas` **no hay defecto**: la cuenta existente sin permiso ya respondía 403 y el 404 queda para "no existe" (confirmado con tests, no solo leyendo). Se revisa `ExtractosController` en el mismo lote. Queda abierto el oráculo de existencia: con la regla 403/404 pedida, un actor sin permiso sigue distinguiendo "existe" de "no existe". |
| S-9 | Info | Tres métodos de permisos sin uso en `UserAccessService` cuyas reglas difieren de las que aplican de verdad Conciliación e Importación. Un cambio de política toca tres sitios. | `API/Services/UserAccessService.cs:311-336` | V - **CORREGIDO** (LOTE 6, 2026-11-18): los tres métodos han dejado de estar muertos y son la **fuente única** de las reglas de permiso por acción (`UserAccessAction` + un único mapa de predicados en `UserAccessService`). `ConciliacionService` e `ImportacionService` los consultan en vez de reimplementarlos, y `CanConciliarCuentaAsync`/`CanCerrarConciliacionAsync` pasan a ser estrictos, que es la regla que de verdad se aplicaba. Sin cambio observable de permisos. |

### 4.2 Autenticación y sesión

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| S-10 | Media | **El alta de MFA se puede sobrescribir.** La rama `SetupRequired` guarda el secreto sin comprobar que el usuario siga sin MFA, y completar un alta no rota el `SecurityStamp`. Quien conozca la contraseña inicial y tenga un desafío abierto (5 min) puede fijar su propio autenticador después de la víctima. | V - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-11 | Media | Carrera en el límite de login: el throttle se consulta antes de bcrypt y el contador se incrementa después, sin concurrencia. Una ráfaga paralela evalúa más contraseñas de las permitidas y el contador no llega al bloqueo. | S - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-12 | Media | La API guarda en su configuración la credencial de propietario de la BD (`MigrationConnection`). El `REVOKE` sobre auditoría solo protege frente a SQLi, no frente a un compromiso del proceso; el comentario de `Program.cs` lo sobreestima. | `API/Program.cs:1125-1149`, `:1282-1292` | S |
| S-13 | Baja | Sin vida máxima de sesión: cada rotación reinicia 7 días y hereda la verificación MFA original. | S - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-14 | Baja | Cookies legacy sin `__Host-` aceptadas en producción (ya conocido). | S - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-15 | Baja | Dos pestañas que refrescan a la vez: la segunda se trata como reutilización y cierra todas las sesiones (ya conocido). | S - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-16 | Baja | Un cliente anónimo llena `AUDITORIAS` con 401 sobre rutas únicas (la deduplicación incluye la ruta). | S - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-17 | Baja | SMTP sin credenciales usa `StartTlsWhenAvailable`: las alertas con saldos pueden ir en claro. | S - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-18 | Baja | Respuestas de `/api/*` sin `Cache-Control: no-store`. | S - **CORREGIDO** (LOTE 2, 2026-11-16) |
| S-19 | Baja | El cifrado de backups autentica cada bloque pero no el número de bloques: un `.enc` truncado descifra sin error. | `API/Services/BackupEncryptionService.cs:146-171` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| S-20 | ? | Rutas comparadas por cadena exacta: `/api/auth/login/` podría caer en el cubo anónimo de 60/min en vez del de 10/min. | `API/RateLimiting/RateLimitingSetup.cs` | ? |
| S-21 | ? | La firma de actualización cubre el ZIP, no la versión: quien controle la release podría servir una versión firmada anterior. | `API/Services/ActualizacionService.cs:87` | ? |

### 4.3 Integración OpenClaw y tokens

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| S-22 | Media | El límite de 100 req/min se aplica después del chequeo de scope, y cada petición rechazada escribe una notificación de admin y una fila de auditoría. | `API/Middleware/IntegrationAuthMiddleware.cs:142-172` | S - **CORREGIDO** (LOTE 4, 2026-11-16) |
| S-23 | Baja | Un `PUT` que solo cambia el nombre de un token renueva su caducidad a 90 días; «Rotar» resucita un token revocado; la fecha de expiración no tiene tope. | `API/Controllers/IntegracionesController.cs:246-252`, `:306-341` | S - **CORREGIDO** (LOTE 4, 2026-11-16) |
| S-24 | Info | Scopes por defecto de más: `vencimientos` no tiene endpoint, `auditoria` devuelve valores anterior y nuevo, y el permiso de escritura no hace nada. | `API/Controllers/IntegracionesController.cs:21-30` | S - **CORREGIDO** (LOTE 4, 2026-11-16) |
| S-25 | Baja | Telemetría anónima sin `MaxLength`, escribe a nivel `Error`. | `API/Controllers/TelemetriaController.cs:14` | S - **CORREGIDO** (LOTE 4, 2026-11-16) |

## 5. Bugs funcionales

### 5.1 Backend

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| B-1 | Media | Totales en divisa: una cuenta sin tipo de cambio aporta 0 y la respuesta no lo marca. Igual en `total_convertido` de OpenClaw. | `API/Services/DashboardService.cs:560-564`; `API/Controllers/IntegrationOpenClawController.cs:190-193` | V - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-2 | Media | La sincronización de tipos de cambio pisa las tasas manuales cada 12 h. Un fallo de sincronización solo queda en un `Warning`. | `API/Services/TiposCambioService.cs:443-468` | V - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-3 | Media | Tras revertir un lote no se puede reimportar: la deduplicación cuenta las filas borradas y el índice único de huella no filtra `deleted_at`. | `API/Services/ImportacionService.cs:787-794`; `API/Data/AppDbContext.cs:221-224` | V - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-4 | Media | `RevertirLote` no es atómico: borra extractos fuera de transacción y después guarda el lote. No valida el estado ni invalida conciliaciones. | `API/Services/ImportacionService.cs:637-688` | S - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-5 | Media | Conciliación: el score no impone el tope de importe por movimiento. Un esperado de 50 € puede sugerirse contra un extracto de 900 € si otro movimiento del lote es grande. | `API/Services/ConciliacionService.cs:446-456` | V - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-6 | Media | Conciliación: un mismo extracto puede conciliarse dos veces y `SetEstado` acepta cualquier transición. `FirstAsync` da 500 si el movimiento ya no existe. | `API/Services/ConciliacionService.cs:212-280`, `:325-349` | S - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-7 | Media | `PUT /api/extractos/{id}` devuelve 500 si falla la evaluación de alertas, con el cambio ya guardado. `Crear` sí lo protege. | `API/Controllers/ExtractosController.cs:383-385` | V - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-8 | Media | Alerta de saldo: `pg_advisory_xact_lock` sin transacción explícita se libera al instante; dos importaciones simultáneas envían el email doble. | `API/Services/AlertaService.cs:98-105` | V - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-9 | Media | El saldo anterior de un movimiento de plazo fijo se calcula incluyendo filas borradas. | `API/Services/ImportacionService.cs:988-994` | S - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-10 | Media | IA: agregados que suman o comparan divisas distintas; «duplicado probable» marca pagos recurrentes legítimos; los seguimientos («¿y el mes anterior?») pierden los filtros. | `API/Services/IaPlanner/FinancialToolsService.cs:948-973`, `:1100-1112` | S - **CORREGIDO** (LOTE 4, 2026-11-16) |
| B-11 | Media | IA: el timeout del planificador escapa como 500 y la llamada facturada no se contabiliza. Los contadores de coste se actualizan sin concurrencia. | `API/Services/IaPlanner/IntentPlanner.cs:293`; `API/Services/AtlasAiService.cs:2147-2158` | V - **CORREGIDO** (LOTE 4, 2026-11-16) |
| B-12 | Baja | La primera prueba SMTP de cada usuario devuelve siempre 429. | `API/Services/SmtpTestRateLimit.cs:25-29` | V - **CORREGIDO** (LOTE 1, 2026-11-16) |
| B-13 | Baja | El importe «0,500» se interpreta como 500 (backend y frontend). | `API/Services/ImportacionService.cs:2239-2253`; `FE/utils/formatters.ts:30-100` | S - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-14 | Baja | Cambiar la divisa de una cuenta con extractos no se bloquea. | `API/Controllers/CuentasController.cs:454` | S - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-15 | Baja | Paginación sin desempate (`ThenBy Id`): filas duplicadas u omitidas entre páginas al ordenar por importe o concepto. | `API/Controllers/ExtractosController.cs:104-120`; `AuditoriaController.cs:119` | S - **CORREGIDO** (LOTE 5, 2026-11-17) |
| B-16 | Baja | «Hoy» en UTC: entre las 00:00 y las 02:00 de España el mes o el día actual es el anterior. El año `2026` está escrito a mano en el detector de resumen anual de la IA. | `API/Services/AtlasAiService.cs:1811`; `FE/components/extractos/ExtractoTable.tsx:230` | S - **CORREGIDO** (LOTE 4, 2026-11-16; parte frontend en LOTE 7, 2026-11-17) |
| B-17 | ? | El saldo actual es el de la fila con mayor `FilaNumero`. Si un banco exporta en orden ascendente, el saldo «actual» sería el más antiguo. No hay validación de continuidad de saldos. | `API/Services/ImportacionService.cs:771-772`; `API/Services/DashboardService.cs:512-521` | ? - **NO CORREGIDO** (LOTE 5, 2026-11-17): el defecto es real pero su alcance depende del orden en que exportan los bancos, y esa pregunta se respondio por auto-seleccion de tiempo, sin confirmacion del usuario. Ver seccion 17. |

### 5.2 Jobs

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| J-1 | Media | Ningún job tiene `DisableConcurrentExecution` ni `AutomaticRetry`, con 2 workers. Un backup largo más una exportación retrasan las alertas de seguridad, que además evalúan una ventana fija y dejan huecos. | `API/Program.cs:333`; `API/Services/SecurityAlertService.cs:85-90` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| J-2 | Media | La exportación mensual traga todos los errores con un `catch` vacío y sin log. | `API/Services/ExportacionService.cs:228-240` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| J-3 | Media | Un aviso de «próximo a vencer» cuyo email falla no se reintenta; solo un plazo por grupo recibe notificación de admin. | `API/Services/PlazoFijoService.cs:58-90` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| J-4 | Baja | Limpieza de exportaciones: si falla el borrado del fichero, la fila se marca borrada igualmente y el fichero queda en disco. | `API/Jobs/LimpiezaExportacionesJob.cs:59-72` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| J-5 | Baja | La limpieza de refresh tokens no ve los de usuarios borrados. | `API/Jobs/LimpiezaRefreshTokensJob.cs:22-24` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| J-6 | Baja | La auto-actualización puede lanzarse en horario laboral si el servidor estaba caído a la hora configurada. | `API/Jobs/AutoUpdateJob.cs:47-57` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |

### 5.3 Frontend

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| F-1 | Media | Cualquier error al renovar el token (red, 5xx, 429) cierra la sesión y lo propaga a las demás pestañas. | V - **CORREGIDO** (LOTE 2, 2026-11-16) |
| F-2 | Media | La papelera pide 500 filas y filtra en cliente: los eliminados más antiguos no aparecen ni se pueden restaurar. Muestra todos los importes en EUR. | `FE/pages/PapeleraPage.tsx:93-141`, `:228` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| F-3 | Media | El backend omite los `null` (`WhenWritingNull`) y el frontend compara con `!== null`: en auditoría se pinta un importe vacío. Mismo tipo de fallo que el de permisos de octubre. | `FE/pages/AuditoriaPage.tsx:375,383`; `FE/components/extractos/AuditCellModal.tsx:50,57` | S - **CORREGIDO** (LOTE 1, 2026-11-16) |
| F-4 | Media | El interceptor lanza un toast por cada error, también en los sondeos con la API caída a propósito (actualización, backup): un toast cada 2,5 s. Se suma a los 82 errores que ya pintan las páginas. | `FE/services/api.ts:172-186` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| F-5 | Media | El banner de alertas reaparece al cambiar de país; dos escritores sobre el mismo store. | `FE/hooks/queries/useAlertasActivasQuery.ts:37` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| F-6 | Media | Salir de la nota de alerta lanza un PATCH aunque no cambie nada. | `FE/components/extractos/ExtractoTable.tsx:828` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| F-7 | Baja | Borrado y marcado en lote con una petición por fila, sin transacción. | `FE/pages/CuentaDetailPage.tsx:458,601` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| F-8 | Baja | Los campos numéricos de Configuración no se pueden vaciar (`Number(x) \|\| 0`). | `FE/pages/ConfiguracionPage.tsx:908,1023` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| F-9 | Baja | Si falla el guardado de una celda, se descarta lo escrito. | `FE/components/extractos/EditableCell.tsx:53-54` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| F-10 | ? | La clave de idempotencia de «Confirmar importación» no se regenera al abrir otro lote desde el historial. | `FE/pages/ImportacionPage.tsx:202,517,856` | ? - **SOSPECHA DESCARTADA, DEFECTO REAL CORREGIDO** (LOTE 7, 2026-11-17) |

## 6. Rendimiento

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| R-1 | Media | Exportación mensual: carga todos los extractos sin `AsNoTracking` y reutiliza el `DbContext` entre cuentas. `AdjustToContents` sobre 50.000 filas. | `API/Services/ExportacionService.cs:93-96`, `:163` | V - **CORREGIDO** (LOTE 3, 2026-11-16) |
| R-2 | Media | Chat de IA: entre 12 y 20 consultas y dos llamadas facturables por pregunta; el mapa de seudónimos se construye dos veces. | `API/Services/AtlasAiService.cs:1685-1713`, `:2106` | S - **CORREGIDO** (LOTE 4, 2026-11-16) |
| R-3 | Media | `GET lotes/{id}/filas` devuelve hasta 50.000 filas sin paginar. | `API/Controllers/ImportacionController.cs:113-130` | S |
| R-4 | Media | `CuentaDetailPage` invalida las mismas consultas que tiene activas: petición duplicada en cada cambio de página o periodo. | `FE/pages/CuentaDetailPage.tsx:337` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| R-5 | Media | `ExtractoTable`: unas 1.600 evaluaciones de permisos por render; cada foco re-renderiza la tabla. | `FE/components/extractos/ExtractoTable.tsx:602` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| R-6 | Media | Búsquedas sin debounce (una petición por tecla) en Formatos, Países, Cuentas, Usuarios y en el campo «Modelo» de IA. | `FE/pages/FormatosImportacionPage.tsx:164`; `FE/pages/ConfiguracionPage.tsx:361` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| R-7 | Baja | Importación de 50.000 filas en un único `SaveChanges`; N+1 en resúmenes de titulares y en la evolución de OpenClaw. | `API/Services/ImportacionService.cs:332-344`; `ExtractosController.cs:722-744` | S - **CORREGIDO (parcial)** (LOTE 7, 2026-11-17): el N+1 de los resumenes de titulares y la insercion masiva por bloques estan corregidos; el N+1 asincrono de la evolucion de OpenClaw sigue pendiente. Ver seccion 18. |
| R-8 | Baja | `/ia/config` se consulta cada 30 s en todas las pestañas, con tres fuentes de verdad. | `FE/hooks/queries/useIaConfigQuery.ts:20` | S - **CORREGIDO** (LOTE 7, 2026-11-17) |
| R-9 | Baja | Todo el CSS (13.010 líneas) se carga en el arranque aunque las rutas sean `lazy`. | `FE/main.tsx:10-13` | S - **CORREGIDO, CON MATICES** (LOTE 8, 2026-11-18): el CSS de cada ruta se carga desde su modulo lazy (19 hojas en `src/styles/layout/routes/` y un `import` por pagina). El CSS inicial baja de **228.149 a 163.965 B** (-28,1 % raw, -23,3 % gzip). **Pero el total del bundle sube 9 KB gzip**: 20 ficheros comprimen peor que 1, de modo que R-9 reparte el CSS en vez de reducirlo: gana quien no visite todas las rutas y pierde quien las recorra. Ademas, al dejar de ser global el orden de la cascada, **41 reglas se quedaron congeladas** en las hojas globales (las que al moverse invertian el orden con otra regla y cambiaban el valor final). Verificado con un resolutor de cascada propio (25 unidades x 32 contextos, 78.869.024 comprobaciones de propiedad con las combinaciones reales de clases del JSX) que da **0 diferencias**, y con una comparacion A/B de capturas sobre el mismo arbol en la que solo difieren las marcas de tiempo del mock. Pendiente: el reparto no es reproducible con una herramienta en el repo (el generador era temporal) y el caso acumulado (varias rutas visitadas en la misma sesion) solo lo cubre el arnes visual, no una comprobacion estatica. |
| R-10 | ? | El índice trigram sobre `lower(concepto)` no casa con `lower(COALESCE(concepto,''))`, que es lo que consulta la IA: posible scan secuencial. Revisión de comisiones y seguros hace `LIKE '%…%'` con 12-14 términos. | `API/Services/AtlasAiService.cs:2046-2053`; `API/Services/RevisionService.cs:595-626` | ? |
| R-11 | ? | `backdrop-filter` permanente en barra superior y sidebar sobre tablas con scroll. | `FE/styles/layout/shell.css:45,417` | ? |

## 7. Diseño y experiencia de uso

Sin validación visual: todo sale de leer CSS y TSX. Según el subagente de diseño, lo aplicado hoy por los informes Modern Web Guidance e Impeccable está en el código y no vio regresiones; el orquestador no lo comprobó.

| Métrica | Valor |
|---|---|
| CSS total | 13.010 líneas en 29 archivos |
| Tokens definidos / sin uso | 543 / 239 |
| Breakpoints distintos | 16 |
| `!important` reales | 22 (de 39) |
| Clases CSS sin uso | unas 35 |
| `style={{}}` en TSX | 21 |

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| D-1 | Media | Porcentajes con `toFixed`: «12.3%» con punto junto a «1.234,56 €», «+0.0%» para el cero y guion ASCII en negativos. | `FE/pages/DashboardPage.tsx:281-398`; `FE/pages/DashboardTitularPage.tsx:213-250` | V - **CORREGIDO** (LOTE 8, 2026-11-18): `formatPercent` en `FE/utils/formatters.ts` con `Intl.NumberFormat('es-ES')` (coma decimal), signo menos real U+2212 reutilizando `withRealMinusSign`, separador del locale antes del `%` y cero escrito «0,0 %» (nunca «+0,0 %»). Valor ausente o no numérico: «—». Los 9 `toFixed` de porcentajes del dashboard (11 llamadas a `formatPercent` en total, contando las dos del donut) ya no usan `toFixed`; los `toFixed` de importes (ConfiguracionPage, aiChatStore) se dejaron intactos porque no pintan porcentajes. |
| D-2 | Media | Textos sin tilde y desiguales: «Nuevo pais», «Cargando paises...», «podran iniciar sesion», «Anadir dinero». Errores genéricos sin causa ni siguiente paso. | `FE/pages/configuracion/PaisesPage.tsx:241,289`; `FE/pages/ConfiguracionPage.tsx:461`; `FE/pages/ImportacionPage.tsx:906` | V - **CORREGIDO** (LOTE 8, 2026-11-18): barrido de tildes y unificación de fórmulas en PaisesPage, ConfiguracionPage, AlertasPage e ImportacionPage, y 31 errores genéricos convertidos en accionables («No se pudo <qué>. <qué hacer ahora>.»). Los comentarios del código se dejan sin tilde, por costumbre del repo. |
| D-3 | Media | Estados de carga dispares: 8 pantallas con skeleton y unas 14 con un `<p>Cargando…</p>` sin `role="status"`. | Alertas, Auditoría, Backups, Cuentas, Titulares, Exportaciones, Formatos, Países, Papelera | S - **CORREGIDO** (LOTE 8, 2026-11-18): `PageSkeleton` pasa a ser el componente único de carga, con `role="status"` y `aria-live="polite"`, esqueleto de tabla real (`variant="table"`, cabecera + filas y columnas) y modo `inline` para los estados dentro de una sección. Se quitan **los 28 `<p>Cargando…</p>`** de `src/` (14 en el primer pase y 14 en el cierre, tras D-9). Se retira `aria-busy` del contenedor: en una region viva lo silencia, que es justo lo contrario de lo que pide el hallazgo. |
| D-4 | Media | Breakpoints fuera de la regla 768 / 1200: entre 721 y 900 px el shell y las páginas están en modos distintos. | `FE/styles` (720, 760, 860, 900, 1180, 1280…) | V - **CORREGIDO** (LOTE 8, 2026-11-18): **todas las media queries de anchura de `FE/styles` usan ya los tres umbrales del sistema** (767.98 / 1023.98 / 1199.98, mas las dos formas inversas de la banda tablet, `min-width: 768px`..`1199.98px` y `min-width: 1024px`..`1199.98px`). Se mapearon 23 umbrales en las hojas de pagina y se corrigieron los que quedaron fuera al repartirse el CSS: `components/tabs.css` (560px -> 767.98px y `768-1200` -> `768-1199.98px`), `layout/routes/auditoria.css` (1200px -> 1199.98px) y `layout/shell.css` (560px -> 767.98px). El bloque del movil estrecho (360px), que ajustaba la barra inferior, **se integro tambien en 767.98px**: es el unico mapeo que cambia el aspecto en una anchura frecuente (390 px), asi que se valido con capturas. Consecuencias del mapeo, documentadas: la banda 901-1023.98 px pasa a una columna donde antes habia dos (dashboard, configuracion, conciliacion, backups, extractos y login), la cabecera de Extractos colapsa a columna a partir de 1181 px y Configuracion recupera 3 columnas entre 1200 y 1280 px. El arnes visual se amplia con la anchura de tablet (1024 x 768) para poder validar esa banda. |
| D-5 | Media | `system-coherence.css` es una capa de parches que gana por orden de carga: 15 prefijos de página repiten el mismo patrón de cabecera. | `FE/styles/layout/system-coherence.css:1-440` | S - **CORREGIDO** (LOTE 8, 2026-11-18): la capa de parches **deja de existir** (`system-coherence.css` borrado, sus 428 lineas repartidas). Nace `src/styles/layout/page.css` con las clases compartidas `.page` (+ `.page--wide`), `.page-header` (+ `--plain` para Conciliacion, `--static` para Papelera y Alertas, `--stacked` para Importacion) y `__title`/`__lead`/`__subtitle`, que se anaden a las 19 pantallas y 21 cabeceras **sin quitar las clases propias**. Las 6 reglas duplicadas de la cabecera (5 copias de la base + el patron de 15 prefijos) desaparecen: 2.062 reglas CSS quedan **byte a byte identicas** y las 77+14 que cambian son todas renombres de selector, uno a uno. Un resolutor de cascada por elemento (141 comprobaciones: 47 elementos x 3 estados de media) da **cero diferencias de valor**. La validacion visual destapo un defecto real que ni el comparador de reglas ni el resolutor veian: un `<div class="auditoria-header auditoria-actions-header">` reutilizaba la clase de cabecera y perdia 12 px de padding y su borde, lo que subia la tabla de Auditoria 13 px; corregido anadiendole `page-header`, y la recaptura lo deja identico. |
| D-6 | Media | Estilos globales dentro de hojas de página: el modal vive en `extractos.css`, la paginación en `users.css`, `.import-muted` se usa en 10 páginas ajenas, quedan clases `phase2-*`. | `FE/styles/layout/extractos.css:789-836` | V (modal) / S - **CORREGIDO** (LOTE 8, 2026-11-18): tres hojas nuevas de componentes (`src/styles/components/modal.css`, `pagination.css` y `tabs.css`) con el modal (139 lineas de `extractos.css`, 136 de `users.css`, 45 de `importacion.css`, 22 de `admin.css`, 27 de `entities.css` y 55 de `system-coherence.css`), la paginacion (69 lineas de 4 hojas) y las pestanas `config-tabs` (107 lineas). `.import-muted` pasa a `global.css` (la usan 10 paginas y 4 componentes, incluido el esqueleto de carga). Las 11 clases `phase2-*` se renombran a `entity-*` y `modal-form*`: **0 coincidencias de `phase2` quedan en el repo**. El orden de fuente interno de cada hoja movida se conserva (`users -> extractos -> entities -> importacion -> admin -> system-coherence`), que es lo que mantiene los tres casos que compiten (`.users-modal`, `.import-modal` y `.users-modal-body` vs `.modal-form-body`); las hojas nuevas se cargan al final, donde antes ganaba la capa de parches. Verificado: 332/332 pruebas, build correcto y comparacion de capturas sin ninguna diferencia atribuible al CSS (el resto es ruido del arnes: el mock genera marcas de tiempo con `Date.now()` y dos ejecuciones del mismo codigo difieren en ~40 imagenes). Pendiente: renombrar `.users-pagination` a `.pagination` (14 TSX), que es un paso aparte y reversible. |
| D-7 | Media | 239 tokens sin uso (toda la familia `--color-*`) y el tema oscuro definido en dos sitios. | `FE/styles/variables.css:93-134`, `:253-262` | V (muestra) / S - **CORREGIDO** (LOTE 8, 2026-11-18): **265 tokens sin uso borrados** (493 nombres definidos -> 228; 296 sitios de declaracion eliminados) en `variables.css` y `design-system/tokens/*`, con un inventario propio que comprueba `var(--nombre)` y el literal desnudo en `src/`, `tests/`, `e2e/`, `public/` e `index.html` (comentarios excluidos) y que ademas sigue las cadenas de alias: un token reexportado no se da por muerto si su destino vive. **El bloque `[data-theme="dark"]` de `variables.css` NO era un duplicado**: `variables.css` importa `themes.css` ANTES de su propio `:root`, asi que su `:root` (misma especificidad) gana por orden de fuente y pisaba los valores oscuros. Probado en Chromium: sin ese bloque, `--text-primary` pasa de `#f5f5f7` a `#1d1d1f` (texto oscuro sobre fondo oscuro) y `--amount-positive` de `#78c79a` a `#176a3a`. El bloque se mantiene con 6 tokens y solo se borra `--text-link`, que si era redundante. Comprobado que 31 tokens x 2 temas (62 valores computados) dan el MISMO resultado antes y despues. |
| D-8 | Media | Los colores del gráfico son texto libre sin validar y no se adaptan al tema. | `FE/pages/ConfiguracionPage.tsx:950-965` | S - **CORREGIDO** (LOTE 8, 2026-11-18): el campo de color valida el hexadecimal (`#RGB` o `#RRGGBB` o vacío), no se guarda nada inválido, y el valor por defecto sale de los tokens `--chart-*` del tema activo, con paleta (`datalist`) y mensaje de error junto al campo. Para que el color siga al tema, `FE/utils/chartColors.ts` (nuevo) resuelve el hexadecimal de un token a su variable `var(--chart-N)`, tanto en tema claro como oscuro; `EvolucionChart` lo consume. |
| D-9 | Baja | El «Modal único» está a medias: 5 usos de `<Modal>` y 8 modales montados a mano. | `UsuarioModal`, `CuentasPage`, `TitularesPage`, `PaisesPage`… | S - **CORREGIDO** (LOTE 8, 2026-11-18): los **10 modales montados a mano de 8 ficheros** pasan al componente comun `<Modal>`: `UsuarioModal`, los 3 de confirmacion de `UsuariosPage`, `TitularesPage`, `CuentasPage`, `PaisesPage`, el de importacion de `CuentaDetailPage`, el overlay de restauracion de `BackupsPage` y el aviso de sesion de `SessionTimeoutWarning`. `<Modal>` gana las props `variant` ('default' \| 'fullscreen-mobile' \| 'stack-top'), `panelClassName`, `headerClassName`, `subtitleClassName`, `subtitleId`, `bareBody` y `closeButtonClassName`, para no perder el aspecto legacy (el CSS no hizo falta tocarlo). No queda ningun `role="dialog"` manual fuera del componente comun (quedan los cuatro que no son modales: `DatePickerField`, `TopBar`, `CommandPalette` y `BottomNav`), ni manejadores de Escape duplicados, y los 16 `titleId` son unicos. La validacion visual destapo un defecto real: `<Modal>` pintaba su boton de cierre en cuatro modales que antes no lo tenian, y se corrigio con `showCloseButton={false}`. |
| D-10 | Baja | Encabezados que saltan de `h1` a `h3`; 23 `<th>` sin `scope`; 24 tablas y 2 `<caption>`. | `FE/pages/ImportacionPage.tsx:808,1011,1042` | S - **CORREGIDO** (LOTE 8, 2026-11-18): **la premisa de los 23 `<th>` estaba obsoleta**: los 146 `<th>` del repo ya tienen `scope="col"` (cerrado en un lote anterior) y no existe el patron de cabecera de fila, asi que no hay nada que anadir ahi. Si queda trabajo real, esta corregido: **22 `<caption className="sr-only">`** anadidos (de 2 a 24 tablas con nombre accesible, sin impacto visual), los **3 saltos de encabezado** de `ImportacionPage` corregidos (`h3` -> `h2`) y los **19 componentes compartidos que colgaban directamente del `h1`** (`EmptyState` y `KpiCard`, que pintaban un `h3` fijo): ambos aceptan ahora `headingLevel="h2"` y el CSS mantiene el tamano (21 px) en los dos niveles, de modo que **la jerarquia se corrige sin cambiar el aspecto**. Los `h3` que cuelgan del `<h2>` de un `<Modal>` no son saltos y se han dejado. |
| D-11 | Baja | Los importes positivos se distinguen solo por el color. | `SignedAmount` | S - **CORREGIDO** (LOTE 8, 2026-11-18): 29 usos de `showSign` (26 sitios de importes firmados mas 3 con la guarda `sinSignoPrevio`) pintan ahora el «+» del positivo con `showSign` de `SignedAmount`, de modo que el color deja de ser el unico portador. No se tocan las magnitudes con etiqueta propia («Ingresos», «Egresos», umbral minimo) ni los negativos. La guarda `sinSignoPrevio` (para no duplicar un «+» que ya traiga el dato crudo) se aplica en Importacion y en AuditCellModal. |
| D-12 | Baja | Tokens con nombre engañoso: `--text-16` vale 17 px, `--weight-medium` y `--weight-semibold` valen 600. | `FE/styles/tokens` | S - **CORREGIDO** (LOTE 8, 2026-11-18): `--text-16`, `--text-20` y `--text-32` eran alias **muertos** de `--text-17`, `--text-21` y `--text-34` (que ya existen con ese valor): se borran en vez de renombrarse, para no crear una segunda definicion del mismo token. En los pesos queda **un solo nombre por peso**: `--weight-medium` pasa a ser alias explicito de `--weight-semibold` (mismo 600), y `--font-weight-normal`, `--font-weight-medium`, `--font-weight-semibold` y `--font-weight-bold` (alias muertos) se borran; `--font-weight-heavy` deja de ser un literal `700` duplicado y apunta a `var(--weight-bold)`. |
| D-13 | Baja | Acciones de conciliación y revisión visibles para quien no tiene permiso: el backend responde 403 con toast. | `FE/stores/permisosStore.ts:111` | S - **CORREGIDO** (LOTE 6, 2026-11-18): `canConciliarCuenta` deja de estar sin consumidores y las pantallas de Conciliación y Revisión ocultan las acciones que el backend deniega. Se añaden `canCerrarConciliacionCuenta` (estricto) y `canRevisarCuenta`, con los predicados exactos del backend. |

Mejoras de diseño de mayor efecto, en orden:

1. Componente único de carga, vacío y error, más un barrido de tildes y puntuación.
2. `formatPercent` en `utils/formatters.ts` con `Intl.NumberFormat('es-ES')`.
3. Clases `.page` y `.page-header` compartidas, y sacar modal, paginación y utilidades a hojas propias. Con eso se cierra `system-coherence.css` y se puede cargar el CSS por ruta.
4. Tres breakpoints (767.98 / 1023.98 / 1199.98).
5. Borrar tokens y clases sin uso.

## 8. Código muerto y mantenibilidad

| ID | Qué | Dónde | Estado |
|---|---|---|---|
| M-1 | `TendenciasAnomaliasService` (237 líneas): solo lo usan sus tests; no está registrado en DI. | `API/Services/IaPlanner/TendenciasAnomaliasService.cs` | V - **CORREGIDO** (LOTE 8, 2026-11-18): servicio y su fichero de tests borrados. No había ninguna referencia fuera de sus propios tests (grep de nombre de tipo, de método, de fichero y variantes de capitalización) ni registro en DI: `Program.cs` no lo declara en ningún `AddScoped`/`AddSingleton`/`AddTransient`. Con el servicio caen también sus tipos públicos (`TrendVerdict`, `TrendVeredicto`, `TendenciasAnomaliasResultado`) y el `internal static class TrendPointExtensions`, que solo usaba él. Decisión del usuario: borrar, no registrar en DI. |
| M-2 | `AppendPeriodSummary_ObsoleteKeptForBuild` y una sobrecarga de `AppendPeriodSummaryAsync` sin llamadores. | `API/Services/AtlasAiService.cs:1957-1992` | V - **CORREGIDO** (LOTE 8, 2026-11-18): las dos piezas están borradas. `AppendPeriodSummary_ObsoleteKeptForBuild` (con su `#pragma warning disable IDE0051`) solo aparecía en su propia definición. La sobrecarga de 6 parámetros `(StringBuilder builder, string title, IQueryable<Models.Cuenta>, DateOnly, DateOnly, CancellationToken)` no tenía ningún llamador: los cinco `await AppendPeriodSummaryAsync(...)` del servicio invocan todos la sobrecarga de 4 parámetros, que se conserva. La sospecha (`S`) del overload queda cerrada: era código muerto confirmado. |
| M-3 | `CanApproveImportacionAsync`, `CanConciliarCuentaAsync`, `CanCerrarConciliacionAsync` sin uso. | `API/Services/UserAccessService.cs:311-336` | V - **CORREGIDO** (LOTE 6, 2026-11-18): los tres métodos se usan; las reglas viven en un único mapa (`UserAccessAction`) y tanto Conciliación como Importación las consultan. |
| M-4 | `AtlasAiService.cs` (3.744 líneas) mezcla orquestación, cliente HTTP, límites, formato, contexto y parseo. El ranking existe tres veces con semánticas distintas. Corte propuesto: cliente de proveedor, gobernanza, constructor de contexto, posprocesado y formateador; el servicio queda como orquestador. Arregla de paso A-5. | `API/Services/AtlasAiService.cs` | S |
| M-5 | `ExtractosController` reimplementa la lógica de alcance de `UserAccessService` (ya conocido). | `API/Controllers/ExtractosController.cs:1045-1118` | S - **CORREGIDO** (LOTE 6, 2026-11-18): la condición del informe se cumple (los tests de autorización existentes cubren admin, acceso global, permiso solo-dashboard, país, titular sin acceso, borrados, permisos de edición y de desglose), asi que el controller inyecta `IUserAccessService` y deja de reimplementar el alcance. |
| M-6 | Unas 35 clases CSS y 239 tokens sin uso (ver sección 7). `canConciliarCuenta` sin consumidores. | `FE/styles`, `FE/stores/permisosStore.ts:111` | V (muestra) - **CORREGIDO** (LOTE 8, 2026-11-18): **50 clases CSS sin uso borradas** (la cifra del informe era una estimación) de `global.css`, `revision-ai.css`, `dashboard.css`, `entities.css`, `importacion.css` y `admin.css`: restos de una rejilla tipo Excel que ya no existe (`.account-excel-*`, `.account-col-*`, `.account-cell-*`), la familia `.ab-*` de un sistema de badges que no se usa, y varias clases del chat de IA (`.ai-chat-composer-*`, `.ai-chat-links`, `.ai-chat-loading-dots`). Cada clase se comprobo con busqueda de token exacto en `src/`, `tests/`, `e2e/` y en el resto de hojas CSS; las que se construyen por concatenacion (`.date-picker-popover--*`, `.alertas-notif-item--*`, `.backup-cloud-badge--*`, `.signed-amount--*`) y las `phase2-*` (reservadas al bloque de sistema de diseño) se conservaron. Los tokens van en D-7 y `canConciliarCuenta` ya lo consumia D-13. |
| M-7 | Reglas del proyecto que no se cumplen: React Hook Form solo en 2 formularios (el resto usa `useState`); varios archivos con más de un componente; 14 `eslint-disable exhaustive-deps`; 105 errores de las reglas nuevas de react-hooks desactivados. | `FE/pages`, `FE/eslint.config.js` | S |
| M-8 | Duplicados: `parseDecimalInput`, `InsertRowDraft` y `getTodayInputValue` en dos archivos cada uno. Páginas de más de 1.200 líneas: Configuración, CuentaDetail, Importación. | `FE/pages` | S |
| M-9 | El snapshot de EF no tiene `xmin` en `PLAZOS_FIJOS` y `PendingModelChangesWarning` está silenciado (ya conocido). El test dependiente de la fecha ya está corregido en el código (fechas ancladas al día 1 del mes, `FinancialToolsServiceTests.cs:403-411`): falta cerrar la entrada en `REGISTRO_BUGS.md`. | `API/Data/AppDbContext.cs:19` | V (test) / S - **PARCIAL** (LOTE 5, 2026-11-17): el `xmin` de `PLAZOS_FIJOS` ya esta en el snapshot y el test por fecha queda cerrado en `REGISTRO_BUGS.md`, pero la deriva restante no es solo del snapshot (27 diferencias y divergencias de esquema en la base real), asi que el `Ignore` se mantiene. Ver seccion 17. |
| M-10 | `frontend/.test-dist-v2/` aparece sin seguimiento: `.gitignore` solo cubre `.test-dist/`. Tampoco está ignorada `frontend/playwright-report/`, creada hoy a las 14:42 (origen no identificado: pudo ser un subagente o una sesión anterior). | `.gitignore:33` | V - **CORREGIDO** (LOTE 8, 2026-11-18): la premisa estaba a medias: `.test-dist-v2/` **ya estaba** cubierto por el patrón `Atlas Balance/frontend/.test-dist-*/` (comprobado con `git check-ignore -v`), así que no se ha duplicado ninguna regla. Lo que faltaba era `Atlas Balance/frontend/playwright-report/`, que ahora está en `.gitignore` (sección Frontend) y en los `ignores` de `frontend/eslint.config.js` (junto con `test-results`). No se ha borrado ninguna carpeta. |
| M-11 | `Documentacion/Diseno/design-tokens.css` está marcado como obsoleto pero sigue en el árbol; hay una carpeta `Documentacion/Diseno/Diseño` anidada. | `Documentacion/Diseno` | V - **CORREGIDO** (LOTE 8, 2026-11-18): `design-tokens.css` (sin ninguna referencia en el repo, solo menciones en prosa histórica) y la carpeta anidada `Documentacion/Diseno/Diseño/` se han **archivado** (movido, no borrado) en `Documentacion/Diseno/Archivo/`, con un `README.md` que explica el motivo y la fecha. `DESIGN.md` y los dos informes de diseño de V-03.02 siguen donde estaban. |

### 8.1 Endpoints sin llamada desde el frontend

Comprobado con `grep` de la ruta en `FE/` y en `scripts/`. Antes de retirarlos hay que confirmar que no los usa un cliente externo.

| Endpoint | Observación | Estado |
|---|---|---|
| `GET` / `DELETE /api/auth/mfa/trusted-devices` | Sin interfaz: el usuario no puede ver ni revocar sus dispositivos MFA de confianza. Aquí falta la pantalla, no sobra el endpoint. | V - **CORREGIDO** (LOTE 6, 2026-11-18): pantalla nueva de dispositivos MFA de confianza (`/seguridad/dispositivos-mfa`), con listado y revocacion. |
| `POST /api/importacion/lotes/{id}/revertir` | Sin interfaz. Reduce la exposición práctica de S-3, B-3 y B-4 a quien llame a la API directamente, pero la función no está disponible para el usuario. | V - **CORREGIDO** (LOTE 6, 2026-11-18): boton "Revertir" en el historial de lotes de la pantalla de Importacion, con confirmacion y solo para quien puede eliminar lineas. |
| `POST /api/importacion/validar`, `POST /api/importacion/confirmar`, `GET /api/importacion/lotes/{id}/filas` | Flujo anterior a los lotes; el frontend usa `lotes` y `lotes/{id}/confirmar`. | V |
| `GET /api/cuentas/{id}/resumen` | El frontend usa `/extractos/cuentas/{id}/resumen`. | V |
| `GET`/`PUT /api/usuarios/{id}/permisos`, `/permisos/cuenta/{cuentaId}`, `/emails` | El frontend solo llama a `/usuarios/catalogos-permisos` y al `PUT` del usuario completo. | V |
| `GET /api/sistema/version-actual`, `/sistema/metricas`, `GET /api/revision/settings` | Sin referencias en frontend ni scripts. | V |
| `GET`/`PUT /api/integraciones/{id}`, `GET /api/integraciones/{id}/auditoria` | Sin llamada en el frontend. | S |

### 8.2 Otros restos

| ID | Qué | Estado |
|---|---|---|
| M-12 | `ICacheMetricsSink`: interfaz sin implementación ni consumidor (`API/Caching/CacheMetrics.cs:47`). | V - **CORREGIDO** (LOTE 8, 2026-11-18): interfaz borrada. No tenía ni implementaciones ni inyecciones: `grep` de `ICacheMetricsSink` en todo el repositorio solo la encuentra en su propia declaración. `CacheMetrics` y `CacheMetricsSnapshot` se conservan porque `CacheService` las usa de verdad (`GetMetricsSnapshot`, contadores por espacio de caché) y los tests del proyecto `Caching` los consultan. |
| M-13 | Unos 20 tipos exportados en `FE/types/index.ts` que nadie importa (`CreateIntegrationTokenRequest`, `PlazoFijo`, `EstadoBackup`…) y exports usados solo dentro de su archivo en `utils/`. Basta quitar el `export` o borrarlos. | S - **CORREGIDO** (LOTE 8, 2026-11-18): inventario con grep de cada simbolo (tipos, constantes y funciones de `FE/types/index.ts` y `FE/utils/*.ts`), clasificado en consumido / solo interno / muerto. Los "solo interno" pierden el `export` (siguen siendo privados de su modulo, sin cambio de comportamiento) y los "muertos" se borran del fichero. Verificado con `tsc --noEmit`, `eslint` y la suite completa del frontend: **326/326**. Un aviso: al quitar el `export` de `FE/utils/errorToast.ts` desaparecia el `import type { AxiosRequestConfig } from 'axios'` que sostenia la ampliacion `declare module 'axios'`, y el programa aislado de tests fallaba con TS2664 ("Invalid module name in augmentation"): se ha resuelto con un `import type {} from 'axios'` (sin dependencia en tiempo de ejecucion), que es lo que exige el propio comentario del fichero. |
| M-14 | `README_RELEASE.md` sigue hablando de `V-02-03` (líneas 1, 5 y 100) y `Check-VersionAlignment.ps1` no lo comprueba. `scripts/install.ps1:34` lleva `V-03.02` escrito a mano. | V - **CORREGIDO** (LOTE 8, 2026-11-18): `README_RELEASE.md` pasa a la versión real `V-03.02` y `Check-VersionAlignment.ps1` lo verifica con el mismo patrón que las demás fuentes (todas sus apariciones de versión se normalizan y se comparan). `scripts/install.ps1` ya no escribe la versión a mano: la lee del fichero `VERSION` de la raíz del paquete y falla con mensaje claro si no existe o no tiene el formato `V-XX.XX`. `Check-VersionAlignment.ps1` pasa con exit 0. |
| M-15 | Lanzadores por pares en español e inglés (`Actualizar Atlas Balance.cmd` y `update.cmd`, `Instalar Atlas Balance.cmd` e `install.cmd`, `Atlas Balance.cmd` y `start.cmd`) y parejas de `.ps1` con función parecida (`install.ps1` e `Instalar-AtlasBalance.ps1`, `update.ps1` y `Actualizar-AtlasBalance.ps1`, tres `Start-*Dev.ps1`). No se comparó su contenido. `start-local-dev.cmd` no se referencia desde ningún sitio. | V (referencias) / ? (duplicación) - **CORREGIDO** (LOTE 8, 2026-11-18): la comparación del contenido cambia la premisa: los tres pares de `.ps1` inglés/español **ya eran alias finos** (los ingleses delegan en los canónicos españoles), y los tres `Start-*Dev.ps1` hacen cosas distintas (Docker / solo API / PostgreSQL portátil), así que no se unifican. Lo que sí era duplicado era la capa `.cmd`: `install.cmd` y `update.cmd` pasan a ser alias finos que delegan en `Instalar Atlas Balance.cmd` y `Actualizar Atlas Balance.cmd`, que son los lanzadores canónicos. `start.cmd` y `Atlas Balance.cmd` **no** se fusionan: resuelven el `InstallPath` de forma distinta y unirlos degradaría uno de los dos casos. Nada se borra. |
| M-16 | Dos `.gitignore` (raíz y `Atlas Balance/`) con reglas repetidas. | S - **PROPUESTA, NO APLICADO** (LOTE 8, 2026-11-18): el encargo pedía solo proponer. Los dos ficheros se solapan en `.env*`, `backend/**/bin/`, `obj/`, `node_modules`, `dist`, `wwwroot/assets`, `logs`, `*.log`, `pgdata`, `seed-demo-data*.sql` y certificados; la raíz es la que se usa de verdad (la subcarpeta `Atlas Balance/` es redundante). Un detalle técnico: `Atlas Balance/.gitignore` tiene una línea con **solo CR** (byte 13) que `git check-ignore` interpreta como patrón vacío y produce falsos positivos. |
| M-17 | `Documentacion/` acumula unos 17 informes de auditoría antiguos y `DOCUMENTACION_CAMBIOS.md` pasa de 28.000 líneas. Conviene archivar lo anterior a V-03. | V (tamaño) - **CORREGIDO** (LOTE 8, 2026-11-18): 18 informes anteriores a V-03 archivados en `Documentacion/Archivo/` (movidos con `git mv`, nada borrado) con un índice `README.md`. `DOCUMENTACION_CAMBIOS.md` pasa de **23.565 a 614 líneas** (solo entradas de V-03.02) y el histórico de V-03.01 y anteriores queda en `DOCUMENTACION_CAMBIOS_ARCHIVO.md` (**23.030 líneas**). 18 referencias de ruta actualizadas. |

No hay `TODO`/`FIXME`, `console.log`, `debugger` ni `Console.Write` olvidados, ningún archivo del frontend sin importar, ninguna dependencia de `package.json` sin uso y ningún binario, log, `.env` o certificado en Git. Las versiones están alineadas en `V-03.02` / `3.2.0`.

Descartado al verificar: el subagente dio por muertos `UpdateExchangeRateConfigRequest` y `UpdateRevisionConfigRequest`; se usan como propiedades de otro DTO (`ConfiguracionDtos.cs:63,65`).

### 8.3 CI y cobertura de tests

| ID | Qué | Estado |
|---|---|---|
| T-1 | Los e2e de Playwright no se ejecutan en ningún workflow: `ci.yml` solo corre `lint`, `test:unit` y `build`. | V - **CORREGIDO** (LOTE 8, 2026-11-18): job `e2e` nuevo en `ci.yml` (ubuntu-24.04) que corre `npx playwright test e2e/visual` **solo en push a `main`** (`if: github.event_name == 'push' && github.ref == 'refs/heads/main'`), y los mismos e2e tambien en el job `verify` de `release.yml`, de modo que la release queda gateada por ellos. `e2e/admin-smoke.spec.ts` queda excluido de forma explicita: exige backend real y credenciales. Antes de esto, los tres specs visuales fallaban (5 tests) por **fixtures obsoletos en `e2e/visual/mockApi.ts`** (`columnas_restringidas: []` en vez de `null`, que hacia que la fila se pintara sin celdas; y `/conciliacion` devolviendo un array en vez de `PaginatedResponse`): con el mock corregido, 29 pasan y 1 queda saltado. No se ha tocado `frontend/src/**`. |
| T-2 | De los seis `*.Tests.ps1` de `scripts/`, CI solo ejecuta `Test-AtlasSecrets.Tests.ps1`. Quedan fuera los de MFA/TOTP, seguridad del servicio, sincronización de directorio, conservación de secretos del instalador y smoke test. | V - **CORREGIDO** (LOTE 8, 2026-11-18): los cinco restantes se ejecutan en el job `policy-gates` de `ci.yml`. `Mfa-Totp.Tests.ps1`, `ServiceSecurity.Tests.ps1` e `Installer-SecretPreservation.Tests.ps1` corren en la matriz completa (ubuntu-24.04 y windows-2022); `Sync-AtlasDirectory.Tests.ps1` y `Smoke-Test-AtlasBalance.Tests.ps1` solo en `windows-2022` (en Linux fallan por `$env:TEMP` nulo y por ACLs de Windows), con el motivo anotado en la cabecera de cada script. Ninguno queda fuera de CI. |
| T-3 | Sin test por nombre en backend: 8 controladores (Alertas, Conciliación, Dashboard, Divisas, IA, Importación, Telemetría, TiposCambio), 9 jobs (entre ellos `BackupSchedulerJob`, `PlazoFijoVencimientoJob` y `ExportMensualJob`, los tres con hallazgos en este informe) y servicios como `BackupConfigurationService`, `SecretProtector` y `PackageExtraction`. Puede haber cobertura indirecta. | S |
| T-4 | Frontend: solo `permisosStore` tiene test entre los stores; `services/api.ts` (interceptores de CSRF y refresh) no tiene ninguno. | S |
| T-5 | Sin análisis estático de seguridad (CodeQL o similar) ni lint de PowerShell en CI. | S |

Lo que sí está bien en CI: acciones fijadas a SHA, imagen de PostgreSQL fijada por digest, permisos `contents: read` salvo en el job de publicación, y versiones de Node y .NET tomadas de `.node-version` y `global.json`.

## 9. Contrato de la API

Endpoints que no cumplen `{ data, total, page, pageSize, totalPages }` con `sortBy`/`sortDir`:

| Endpoint | Incumplimiento |
|---|---|
| `GET /api/importacion/lotes/{id}/filas` | Array completo, hasta 50.000 filas |
| `GET /api/conciliacion`, `/movimientos-esperados` | Array plano con `Take(500)`, sin aviso de corte |
| `GET /api/paises`, `/auditoria`, `/importacion/lotes`, `/revision/comisiones`, `/revision/seguros` | Sin `sortBy`/`sortDir` |
| `GET /api/usuarios`, `/cuentas`, `/titulares`, `/formatos-importacion` | `sortDir=desc` con `sortBy` desconocido cae en ascendente |
| `GET /api/extractos` | Sin desempate determinista |

## 10. Verificado, bloqueado y pendiente

**Verificado.** Lectura del código citado por el orquestador para los 9 hallazgos altos y unos 25 medios y bajos (marca V).

**Bloqueado.**
- Sin ejecución: no se corrió build, tests, la app ni PostgreSQL. Nada está reproducido.
- Sin validación visual, axe ni Lighthouse.
- Tres subagentes se cortaron por el límite de uso y se reanudaron hasta entregar. El de código muerto (Haiku) no llegó a revisar métodos del backend, paquetes NuGet, el Watchdog, el contenido de los scripts ni el código comentado en bloque.

**Pendiente de revisar** (nadie lo cubrió):
- Scripts PowerShell de instalación y actualización, y las ACL reales de `updates\` y `config\update-runner`.
- Políticas RLS y las 70 migraciones (solo se miró el snapshot).
- `GoogleDriveBackupService` a fondo, `AlertDispatcher`, `HealthCheckService`.
- `TitularesPage`, `RevisionPage`, `ExportacionesPage` y los componentes de dashboard, leídos solo por patrón.
- Las sospechas (marca ?) necesitan ejecución: `EXPLAIN` para R-10, routing con barra final para S-20, orden de pegado para B-17, backend de idempotencia para F-10.

## 11. Orden de trabajo propuesto

1. **A-1** (permisos borrados al guardar usuario) y **A-7** (error falso en importación): bugs visibles a diario, arreglo pequeño.
2. **A-2** y **S-10**: bloqueo de cuenta y alta de MFA.
3. **A-3**, **A-4** y **J-2**: backups y exportación que fallan en silencio.
4. **A-5**, **A-6** y **B-10**: privacidad y corrección de la IA.
5. **A-9**, **B-1**, **B-2**, **B-3**: plazo fijo, tipos de cambio y reimportación.
6. **S-1** a **S-5**: coherencia de permisos.
7. Diseño: carga y textos, `formatPercent`, breakpoints.
8. Limpieza: código muerto, tokens, corte de `AtlasAiService`.

## 12. LOTE 1 - Correccion aplicada (2026-11-16)

Alcance: solo los cinco IDs del LOTE 1 (A-1, A-7, A-8, B-12, F-3). Ningun otro
ID tocado. Rama `V-03.02`. Sin commit ni push.

| ID | Estado | Que se cambio | Evidencia |
|---|---|---|---|
| A-1 | **Corregido** | `UsuarioModal` carga, envia y ofrece los cuatro flags que faltaban. El payload se construye en `utils/permisosFormUtils.ts` (codigo puro) y el componente lo usa sin duplicarlo. | `tests/permisosFormUtils.test.ts`: 4 fallos antes (payload sin los 4 flags, fila con solo `puede_conciliar` descartada, `mapPermisoApiRow` sin leerlos, `isPermisoRowBlank` incorrecta) -> 25/25 despues. |
| A-7 | **Corregido** | Clave real `importacionLoteCrear` en `mutationInvalidation` (invalida el prefijo `catalogo`) y `invalidate('importacionLoteCrear')` sin `as never`. El `TypeError` que hacia fallar la validacion correcta desaparece. | `tests/invalidation.test.ts`: 3 fallos antes (clave inexistente y `as never` en la pagina) -> 5/5 despues. |
| A-8 | **Corregido** | `ConfiguracionPage` tiene un recargador por seccion (configuracion, tipos de cambio, divisas, tokens, catalogos). Las acciones menores recargan solo su seccion y no activan el esqueleto de pagina completa. | `tests/configuracionSecciones.test.ts`: 5 fallos antes -> 7/7 despues. |
| B-12 | **Corregido** | La rama de alta de `SmtpTestRateLimit.TryAcquireAsync` marca `allowed = true`; el primer intento de cada usuario deja de devolver 429. Limite (5/min por usuario) sin cambios. | `SmtpTestRateLimitTests`: 3 fallos antes (`expected true, got false`) -> 4/4 despues. |
| F-3 | **Corregido** | `!= null` en `AuditoriaPage` y `AuditCellModal`, y `valor_anterior`/`valor_nuevo` opcionales en `types/index.ts` (`WhenWritingNull` los omite y llegan como `undefined`). | `tests/auditoriaValores.test.ts`: 3 fallos antes (tipos obligatorios y comparacion con `!== null`) -> 4/4 despues. |

Verificacion global: backend `AtlasBalance.API.Tests` 1002/1002 y
`AtlasBalance.Caching.Tests` 15/15; frontend `eslint src tests` 0 errores,
`tsc --noEmit` 0 errores, 123/123 pruebas unitarias y build de Vite correcto
(2.671 modulos). Sin validacion visual en navegador.

Observaciones que quedan abiertas y **no** se han tocado (fuera de LOTE 1):

- `SmtpTestRateLimit` usa `ConcurrentDictionary.AddOrUpdate`, que puede invocar
  el delegado mas de una vez: con concurrencia extrema un mismo usuario podria
  esquivar el limite en la ventana. Correccion posible con `TryGetValue` +
  `TryUpdate` en bucle.
- `npm run lint`, `npm run test:unit` y `npm run build` canonicos siguen
  fallando en esta maquina por permisos heredados de carpetas de otra cuenta de
  sandbox (`.test-dist/`, `.test-dist-v2/`, `dist/`); es el problema ya
  anotado en `LOG_ERRORES_INCIDENCIAS.md` y como M-10 de este informe. Las
  mismas comprobaciones pasaron usando carpetas de salida nuevas.

## 13. LOTE 4 - Correccion aplicada (2026-11-16)

Alcance: solo los doce IDs del LOTE 4 (A-5, A-6, B-10, B-11, B-16, S-4, S-6,
S-22, S-23, S-24, S-25 y R-2). Ningun otro ID tocado. Rama `V-03.02`. Sin
commit ni push.

Decisiones tomadas por el usuario antes de implementar (el cuestionario se
respondio por tiempo de espera con las opciones propuestas, y siguen siendo
ajustables):

- **S-23:** vigencia maxima de un token de integracion: **365 dias**.
- **S-24:** se quitan `vencimientos` y `auditoria` de los scopes **por defecto**.
  Afecta solo a los tokens que se creen a partir de ahora.
- **A-6:** cuando el plan trae un filtro que la herramienta no puede aplicar
  (por ejemplo `concepto` en saldos), la IA **rechaza la pregunta con un
  motivo claro** en vez de devolver una cifra sin filtrar.

Metodo: dos subagentes en solo lectura verificaron primero los hallazgos
marcados S (que el informe no habia releido) y corrigieron dos premisas del
propio informe: el rechazo por scope **no** escribe notificacion (solo
auditoria), y el defecto real de `Rotar` con scopes era la **cadena vacia**,
no `[]`. Tres subagentes implementadores, con ficheros disjuntos,
escribieron el test que falla, el arreglo y el test en verde; el orquestador
cerro los dos huecos que quedaron entre ellos (el concepto no se guardaba en
la memoria conversacional y la instruccion del planificador no mencionaba los
importes).

| ID | Estado | Que se cambio | Evidencia |
|---|---|---|---|
| A-5 | **Corregido** | `AiConfiguration.OpenRouterPrivacyProvider(provider, runtimeModel)` es ahora el unico constructor del bloque `provider { zdr, data_collection }`. Lo usan la llamada principal y la del planificador semantico. Los modelos gratuitos siguen sin bloque (incompatibles con ZDR). | `IaPrivacidadYContadoresTests`: el planificador no mandaba el bloque; el resto de casos (modelo de pago, modelo gratuito, OpenAI) ya pasaban. |
| A-6 | **Corregido** | `Concepto`, `ImporteMinimo` e `ImporteMaximo` se aplican en ultimo movimiento, totales, ranking, tendencia, revision, busqueda y comparacion de periodos. Los importes se comparan contra la **magnitud** (`Math.Abs`). El planificador semantico ya acepta `importe_minimo` / `importe_maximo`. El filtro no aplicable (saldos, pendientes) **rechaza con motivo** visible en el chat. | `FinancialToolsFiltrosTests`: 8/8 fallos antes -> 8/8 despues. |
| B-10 | **Corregido** | (i) Los snapshots agrupan por **divisa**: no se suman EUR y USD ni se comparan importes de distinta divisa; si los periodos abarcan varias y el plan no fija una, se rechaza con motivo. (ii) El ranking aplica el top N **por divisa**. (iii) `DUPLICADO_PROBABLE` exige proximidad en el tiempo (`AnomalyDuplicateMaxDays = 3`): una prima mensual ya no se marca. (iv) El seguimiento restaura concepto, cuentas y titulares. (v) La busqueda semantica usa `filtros.concepto` como termino. | `FinancialToolsDivisaTests` (6 tests) y `ConversationContextFiltrosTests` (5): 11 tests de B-10, 8 fallos antes -> 0. Las cuatro clases del area del planificador (`FinancialToolsFiltrosTests` 8, `FinancialToolsDivisaTests` 6, `ConversationContextFiltrosTests` 5, `IntentPlannerTimeoutTests` 3) suman 22 tests con 18 fallos antes -> 0. |
| B-11 | **Corregido** | (a) El timeout del planificador se captura (`TimeoutException` y `TaskCanceledException` que no viene del llamante) y devuelve un rechazo controlado con `SemanticCallAttempted = true`; la cancelacion del llamante se propaga. (b) Los contadores de coste se actualizan bajo un lock: no se pierden incrementos con peticiones concurrentes. | `IntentPlannerTimeoutTests` y `IaContadoresConcurrenciaTests` (PostgreSQL real, 8 `AskAsync` en paralelo). |
| B-16 | **Corregido** | Se elimina el literal `2026` del detector de resumen anual (usa el anio en curso) y "hoy"/"ahora" pasan a la zona operativa `Europe/Madrid` con el helper `FechasOperativas`: en el servicio (contexto, limites y corte de mes de los contadores) y tambien en las herramientas del planificador (saldos, tendencia, anomalias) y en el ancla del plan. El corte de dia de los limites es local; el corte de mes de los contadores tambien. | `IaFechasOperativasTests`: 3 fallos antes -> 6/6 (la ultima comprobacion cubre tambien las herramientas del planificador y el ejecutor). |
| S-4 | **Corregido** | `GET /api/ia/config` solo expone a ADMIN el presupuesto, el coste y los limites globales. Para el resto de usuarios esas 17 propiedades se **omiten** del JSON (no se ponen a 0) y quedan su propio uso y `usuario_puede_usar`. | `IaConfigPermisosTests`: 1 fallo de 4 antes -> 4/4. |
| S-6 | **Corregido** | Las respuestas locales del planificador (ayuda, aclaracion, fallo de ejecucion y respuesta del plan) pasan por el control de limites, incrementan el contador del usuario y escriben fila de auditoria con `origen = "local"`. La ruta determinista no se toca (ya contaba y auditaba). | `IaRespuestasLocalesAuditoriaTests`: 4/4 fallos antes -> 4/4. |
| S-22 | **Corregido** | El rate limit se aplica **antes** del chequeo de scope (el sondeo de rutas consume cuota) y la notificacion de admin se emite **una vez por token y ventana** en vez de una por rechazo. La escritura de auditoria va en try/catch: un fallo de BD no convierte un 403/429 en 500. | `IntegrationRateLimitScopeTests`: 3/3 fallos antes -> 3/3. |
| S-23 | **Corregido** | El PUT sin fecha conserva la caducidad (no renueva a 90 dias), no se admiten caducidades pasadas ni a mas de **365 dias**, `Rotar` rechaza tokens no activos o caducados sin emitir nada, y hereda los scopes **tal cual** (una cadena vacia ya no se convierte en "todos los scopes"). | `IntegrationTokenExpiryTests`: 9/9 fallos antes -> 9/9. |
| S-24 | **Corregido** | `DefaultOpenClawScopes` pasa a ser `titulares, saldos, extractos, evolucion, alertas`. `vencimientos` deja de ser concedible (no tiene endpoint) y `auditoria` sigue siendo concedible de forma explicita, ya no por defecto. | `IntegrationDefaultScopesTests`: 2/4 fallos antes -> 4/4. |
| S-25 | **Corregido** | El DTO de telemetria lleva `[MaxLength]` (500 / 4000 / 4000 / 200 / 40), asi que el binding rechaza los cuerpos sobredimensionados antes de procesarlos, y el log pasa de `Error` a `Warning`. | `TelemetriaControllerTests`: 2/2 fallos antes -> 2/2. |
| R-2 | **Corregido** | La configuracion de IA se carga una sola vez por peticion (snapshot por `DbContext` scoped) y el mapa de seudonimos se construye una sola vez (`Lazy<Task<AiPseudonymMap>>` por peticion). | `IaReutilizacionContextoTests` (PostgreSQL real, interceptor que cuenta SELECT): 2 cargas de config y 2 mapas antes -> 1 y 1. |

Verificacion global: backend `AtlasBalance.API.Tests`, suite completa con
PostgreSQL real via Testcontainers, **1171 tests, 0 fallos** (medicion tras el
cierre de este lote; el total sube conforme las otras sesiones anaden sus
pruebas). El area del LOTE 4
en verde: **60/60** en las catorce clases nuevas de este lote (los trece
ficheros nuevos; `IaPrivacidadYContadoresTests.cs` contiene ademas
`IaContadoresConcurrenciaTests`), **217/217** en las suites previas de IA y
planificador, **131/131** en las previas de integraciones, telemetria y DTOs.
Frontend: `tsc --noEmit` sin errores, `eslint src tests` sin errores, 129/129
pruebas unitarias y build de Vite correcto. Sin validacion visual en navegador
y sin llamadas reales a OpenAI, OpenRouter ni MiniMax (los tests usan el
handler HTTP de prueba).

Nota: mientras se aplicaba este lote, la suite completa daba 7 fallos que
eran de trabajo en vuelo de otra sesion sobre este mismo arbol (cifrado de
backups, limpieza de exportaciones, purga de refresh tokens y las plantillas
`appsettings*.json.template`, que tenian un JSON invalido por una coma que
faltaba). No se tocaron esos ficheros; cuando la otra sesion termino su
trabajo, la suite quedo en verde.

Observaciones que quedan abiertas y **no** se han tocado (fuera de LOTE 4):

- `PermisoEscritura` de los tokens de integracion sigue sin efecto: no hay
  endpoints de escritura bajo `/api/integration/openclaw` y el flag no se
  consulta en runtime. Queda pendiente de decision.
- El endpoint de `auditoria` de OpenClaw sigue devolviendo `valor_anterior` y
  `valor_nuevo`; solo deja de concederse por defecto.
- El lock de los contadores de coste (B-11) es de proceso: valido para la
  instalacion on-premise de una sola instancia; con varias habria que pasar a
  `ExecuteUpdate` o `SELECT ... FOR UPDATE`.
- El corte de dia de los limites de IA usa el desfase local/UTC actual: en los
  dos dias del cambio de hora puede desviarse una hora.
- El `Limite` del ranking es ahora "por divisa": con varias divisas el top N
  puede devolver mas filas que antes.
- `M-4` (corte de `AtlasAiService.cs`) sigue fuera de este lote.

## 14. LOTE 3 - Correccion aplicada (2026-11-16)

Alcance: solo los diez IDs del LOTE 3 (A-3, A-4, J-1, J-2, J-3, J-4, J-5, J-6,
R-1 y S-19). Ningun otro ID tocado. Rama `V-03.02`. Sin commit ni push.

Decisiones tomadas por el usuario antes de implementar:

- **A-3:** el aviso por antiguedad del ultimo backup correcto no usa un umbral
  fijo sino uno **derivado de la frecuencia** configurada: HOURLY 1 dia, DAILY 2,
  WEEKLY 8 y MONTHLY 32. Motivo: la frecuencia por defecto es semanal y un
  umbral fijo de 2 dias habria avisado siempre.
- **J-6:** la auto-actualizacion solo actua dentro de una ventana de madrugada
  **en hora local del servidor** (01:00-05:00 por defecto). Si el servidor no
  estaba encendido de madrugada, la actualizacion se aplaza al dia siguiente.

Metodo: el orquestador confirmo las diez premisas leyendo el codigo citado (los
diez hallazgos existen tal como los describe el informe) y repartio el trabajo en
siete subagentes implementadores con ficheros disjuntos. Cada subagente escribio
el test que falla con el defecto presente, el arreglo minimo y el test en verde.

El orquestador reviso cada diff linea a linea, corrigio por su cuenta un defecto
que un subagente no vio (el recupero del backup tras una ejecucion interrumpida
abandonaba tambien los backups MANUALES en curso) y termino el trabajo de un
subagente que se quedo atascado (J-4 y J-5).

| ID | Estado | Que se cambio | Evidencia |
|---|---|---|---|
| A-3 | **Corregido** | El estado del backup se guarda con `CancellationToken.None` (con el token de la peticion, ya cancelado al apagar el servicio, la fila se quedaba en `PENDING` para siempre). El planificador abandona los `PENDING` de mas de 2 h y avisa (notificacion de admin de tipo `BACKUP`) cuando el ultimo volcado correcto supera el umbral derivado de la frecuencia, con anti-spam y sin aviso si el backup automatico esta desactivado. | `BackupSchedulerJobTests` (nuevo) y 5 casos de `BackupServiceTests`: 11 fallos antes -> 23/23 en verde. |
| A-4 | **Corregido** | El volcado va a `<ruta final>.tmp` y solo se publica con `File.Move` si termina bien; `RutaArchivo` guarda siempre la ruta final (retencion y subida a Drive) y el nombre lleva fecha y hora tambien en automatico. Sin costura el `pg_dump` no se puede probar: constructor publico de 5 argumentos intacto (lo ve el DI) y constructor `internal` de 6 con delegate simulador. Ademas la retencion borra los `*.tmp` de mas de 2 h: sin eso, un volcado interrumpido dejaria datos personales en disco para siempre (la retencion solo borra `.dump`). | `BackupDumpNamingTests` (nuevo) y 4 casos de `BackupServiceTests`: 6 fallos antes -> verde. |
| J-1 | **Corregido** | `DisableConcurrentExecution` + `AutomaticRetry(3; 60/300/900 s)` en backups (3600 s), exportacion (1800 s), limpiezas (900 s) y alertas (600 s). El reintento del backup deja de ser un no-op (si quedo en "STARTED", se abandonan sus `PENDING` automaticos y se relanza). `SecurityAlertService` evalua desde la marca de la ultima evaluacion correcta (`security_alerts_last_evaluated_utc`) en vez de una ventana fija, y la linea base de la regla 6 se mide sobre el rango real. | `JobConcurrencyAttributesTests` (nuevo) y 3 casos de `SecurityAlertServiceTests`: 13 fallos antes -> 25/25. |
| J-2 | **Corregido** | `ExportacionService` recibe `ILogger`: cada cuenta fallida de la exportacion mensual se registra con id de cuenta, tipo de excepcion y mensaje saneado, y al final un recuento de éxitos y fallos. La cancelacion se propaga. | 2 fallos antes -> 13/13 en `ExportacionServiceTests`. |
| J-3 | **Corregido** | El aviso de "proximo a vencer" se reintenta mientras el plazo siga en ese estado y no se haya entregado; y el admin recibe una notificacion por cada plazo del grupo (el digest de email sigue siendo uno). La consulta jsonb no se toco (A-9, otro lote). | 2 fallos antes ("Expected date to be <2026-04-26>, but found <null>" y "Expected notificaciones to contain 2 item(s), but found 1") -> 7/7. |
| J-4 | **Corregido** | La fila solo se marca si el fichero se borro (o no existia), y no se toca ninguna ruta fuera de la raiz `export_path` (mismo criterio que la retencion de backups). | 3 fallos antes -> verde en `LimpiezaExportacionesJobTests`. |
| J-5 | **Corregido** | El job ignora el filtro global que ocultaba los tokens de los usuarios con soft delete y purga tambien los de esos usuarios, con independencia de su caducidad. | `LimpiezaRefreshTokensJobTests` (nuevo) en verde y, sobre todo, un test contra **PostgreSQL real con RLS FORCE** (`RowLevelSecurityRetentionJobsTests`) que comprueba que el borrado en bloque se lleva por delante el token del usuario borrado: sin el arreglo ese token sobrevive. |
| J-6 | **Corregido** | Ventana local configurable `app_update_auto_window_start_local` / `..._end_local` (01:00-05:00 por defecto). Fuera de la ventana el job no escribe nada: la actualizacion se aplaza al dia siguiente. "Ya comprobado hoy" se compara en dia local. | 5 fallos antes (a las 11:00 local el job SI comprobaba la version) -> 7/7. |
| R-1 | **Corregido** | `AsNoTracking` en extractos y columnas extra, `ChangeTracker.Clear()` entre cuentas (solo en el camino mensual) y `AdjustToContents(1, min(filas, 200))` en vez de medir 50.000 filas. | 2 fallos antes -> 13/13 en `ExportacionServiceTests`. |
| S-19 | **Corregido** | Formato V3 (`ATLASBKP3`): la cabecera declara el numero de bloques y el AAD del ULTIMO bloque lo incluye, asi que un truncado cambia que bloque es el ultimo y su tag deja de validar. V1 y V2 quedan byte a byte identicos: los `.enc` existentes se siguen restaurando. | 8/8 en `BackupEncryptionServiceTests` (truncamiento, compatibilidad V1 y V2, ida y vuelta, manipulacion de la cabecera) y 35/35 en las clases `*Backup*`. |

Verificacion global: las doce clases de test del LOTE 3 dan **105/105** en una
ejecucion propia del orquestador (incluye un test contra **PostgreSQL real con
RLS FORCE** para J-5). La suite completa del backend da **1170/1170**, sin
fallos. Tras la revision posterior del lote (2026-11-17, seccion 16) el arbol
queda limpio: los dos fallos que habia por el JSON invalido de las plantillas
`appsettings*.json.template` eran de la otra sesion sobre este mismo arbol y ya
estan resueltos.

Observaciones que quedan abiertas y **no** se han tocado (fuera de LOTE 3):

- El campo "Hora UTC" de la pantalla de Configuracion sigue escribiendo
  `app_update_auto_hour_utc`, que J-6 deja sin efecto; la ventana local todavia
  no es editable y las claves nuevas no estan en el seed.
- Los `.enc` V1/V2 siguen sin autenticar el numero de bloques: es un limite del
  formato legacy, no se puede corregir sin dejar de descifrar los backups ya
  existentes.
- La purga en bloque de refresh tokens (`ExecuteDeleteAsync`) no queda cubierta
  por test automatico: el proveedor InMemory de EF no la soporta.
- `DisableConcurrentExecution` serializa instancias del mismo job, no jobs
  distintos: un backup y una exportacion pueden seguir ocupando los dos
  workers a la vez.



## 15. LOTE 2 - Correccion aplicada (2026-11-16)

Alcance: los diez IDs del LOTE 2 (A-2, S-10, S-11, S-13, S-14, S-15, S-16, S-17, S-18 y F-1). Ningun otro ID tocado, sin commit ni push, sin cambio de version.

Decisiones tomadas antes de implementar (confirmadas por el usuario):

- **A-2:** el bloqueo por intentos fallidos solo impide logins nuevos; las sesiones ya abiertas y su refresh siguen vivas.
- **S-13:** vida maxima de sesion de 30 dias (`JwtSettings:MaxSessionDays`).
- **S-17:** el modo TLS del SMTP es explicito y configurable, y **admite envios sin cifrar** (hay relays que no admiten TLS).
- **S-15:** ventana de gracia con IP coincidente para el reintento concurrente de refresh, conservando intacta la revocacion por reuso fuera de la ventana.

| ID | Estado | Que se cambio | Evidencia |
|---|---|---|---|
| A-2 | **Corregido** | El contador de fallos vuelve a 0 cuando el bloqueo ya caduco (login, verificacion MFA y cambio de contrasena) y el incremento es atomico en BD. El bloqueo deja de cortar las sesiones abiertas: se retiran el 423 del refresh y el rechazo por `LockedUntil` del middleware. Nuevo `POST /api/usuarios/{id}/desbloquear` (solo ADMIN) con auditoria `ACCOUNT_UNLOCKED`, y el listado expone `bloqueado_hasta` e `intentos_fallidos`. La interfaz muestra el estado de bloqueo y el boton "Desbloquear" con confirmacion. | `AuthLot2Tests`: 15/15 (10 fallos en rojo con el defecto presente: "Expected value to be 1 ... but found 6"). `AuthLot2PostgresTests`: 2/2 (rojo: "Expected value to be 8 ... but found 1"). |
| S-10 | **Corregido** | Antes de guardar el secreto MFA se comprueba que el usuario sigue sin MFA; si ya lo tiene, se invalida el desafio y se responde 401 sin tocar el secreto. | `VerifyMfa_Should_Not_Overwrite_An_Existing_Mfa_Secret`: rojo ("Expected a <AuthException> to be thrown, but no exception was thrown") -> verde. |
| S-11 | **Corregido** | Incremento atomico del contador en BD (`ExecuteUpdate` con reinicio a 0 si el bloqueo caduco, mas recarga de la entidad trackeada) y limite por IP tambien para cuentas existentes, con umbral propio configurable (`LoginMaxFailuresPerIpExistingUser` = 20) y contador de cache separado. | `Login_Should_Return_429_When_The_Ip_Exceeds_The_Existing_User_Limit`: rojo ("expected ... 429, but found 401"). Test de concurrencia contra PostgreSQL real: 8 incrementos paralelos, sin perdidas. |
| S-13 | **Corregido** | Nueva columna `sesion_ini_en` en `REFRESH_TOKENS` (migracion `20261116210000_AddSesionIniEnRefreshToken`) que ancla la sesion y se hereda en cada rotacion. A los 30 dias la sesion se revoca y se responde 401: el usuario vuelve a iniciar sesion con MFA. | `RefreshToken_Should_Fail_When_The_Session_Expired` y `RefreshToken_Should_Keep_The_Session_Anchor_And_Id_When_Rotating`: rojo -> verde. |
| S-14 | **Corregido** | En produccion solo se lee la cookie con prefijo `__Host-` (access token en `Program.cs`, `AuthController.ReadCookie` y cookie CSRF en `CsrfMiddleware`); en Development se aceptan las dos. El frontend pasa a leer **siempre** la cookie `__Host-` cuando existe, para que una cookie legacy que sobreviva en el navegador no provoque 403 al guardar. | `CsrfMiddlewareTests` y `AuthControllerTests`: rojo ("Expected context.Response.StatusCode to be 403, but found 200") -> verde. |
| S-15 | **Corregido** | Ventana de gracia de 30 s (`RefreshReuseGraceSeconds`): si el token reutilizado se acaba de rotar, su reemplazo sigue vivo y la IP coincide, se emite un token nuevo de la misma sesion sin rotar el `SecurityStamp` ni revocar otras sesiones, con auditoria propia `REFRESH_TOKEN_REUSE_BENIGN`. Fuera de la ventana, la revocacion por reuso queda intacta. | `RefreshToken_Should_Issue_A_New_Token_When_The_Rotated_Token_Is_Reused_Inside_The_Grace_Window`: rojo -> verde. Test de concurrencia contra PostgreSQL real: el refresh paralelo ya no mata la sesion. |
| S-16 | **Corregido** | La deduplicacion de `SecurityAuditMiddleware` ya no incluye la ruta: se deduplica por tipo de accion, IP y usuario dentro de la ventana. | `Should_Deduplicate_Across_Different_Resources_For_The_Same_User_And_Ip`: rojo ("Expected (db.Auditorias.CountAsync()) to be 1, but found 3") -> verde. |
| S-17 | **Corregido** | Modo TLS explicito y configurable (`smtp_tls_mode`: `required`, `auto`, `none`) con semilla, DTO, controlador y selector en Configuracion. La heuristica por "hay credenciales" desaparece. Cuando el mensaje sale sin cifrar se escribe un warning en el log del servidor (host, puerto y modo; nunca contrasena ni cuerpo). | `SmtpTlsModeTests`: 17 casos de la tabla puerto/modo -> `SecureSocketOptions`. |
| S-18 | **Corregido** | Las respuestas de `/api/*` llevan `Cache-Control: no-store` mediante `SecurityResponseHeaders.ApplyCacheControl`, sin pisar un `Cache-Control` ya puesto. Los estaticos de `wwwroot` no se tocan. | `SecurityLot2Tests`: 3 casos (rojo por clase inexistente) -> verde. |
| F-1 | **Corregido** | La sesion solo se cierra cuando la renovacion responde **401/403**. Un error de red, un 5xx, un 429, un timeout o un error local rechazan la peticion y avisan sin limpiar estado ni redirigir a `/login`. El coordinador distingue el fallo invalidante del transitorio, de modo que las demas pestanas tampoco se expulsan. | `tests/sessionErrorHandling.test.ts`: 3 tests (rojo por modulo inexistente y por el defecto reproducido) -> verde. Suite del frontend 129/129. |

Verificacion global: backend `AtlasBalance.API.Tests` 1170/1170 y `AtlasBalance.Caching.Tests` 15/15, sin fallos (durante el trabajo hubo fallos de otro lote en curso en este mismo arbol y tres de `SerilogHttpClientLoggingTests`, que no localizaban la carpeta de fuentes al compilar la suite con `--artifacts-path`: el test usa ahora `TestSourceLocator` y localiza bien los ficheros). Frontend: `npm run lint` 0 errores, `tsc --noEmit` 0 errores, 129/129 pruebas unitarias y build de Vite correcto. Sin validacion visual en navegador y sin login autenticado real (MFA activo sin credenciales de desarrollo).

Desviaciones y limites conocidos:

- **El umbral por IP para cuentas existentes (20) es un compromiso.** Por debajo el spray no se acota; por encima, detras de un proxy inverso los usuarios legitimos de la oficina caen. El limite por (IP,email) = 3 sigue siendo la red que protege cada cuenta. El contrato previo ("un login valido funciona aunque la IP haya llegado al limite") se conserva con un contador separado.
- **La migracion `20261116210000_AddSesionIniEnRefreshToken` se escribio a mano.** `dotnet ef migrations add` genera una migracion sucia con ~20 cambios ajenos por la deriva previa entre modelo y snapshot. Esa deriva sigue pendiente: el siguiente `migrations add` seguira generando basura hasta que se arregle.
- **El desbloqueo administrativo limpia el estado en BD, no los contadores de rate limit en memoria.** Un usuario recien desbloqueado puede seguir recibiendo 429 hasta que expire la ventana de 15 minutos.
- **S-16 reduce el detalle forense:** dos rechazos legitimos del mismo usuario e IP en rutas distintas dentro de la ventana se registran una sola vez.
- **S-17 no se ha verificado contra un servidor SMTP real:** el warning de "envio sin cifrar" depende de `client.Capabilities`, que solo se puede comprobar conectando a un servidor.

Observaciones que quedan abiertas y **no** se han tocado (fuera de LOTE 2):

- El frontend puede seguir leyendo la cookie CSRF legacy si el navegador la conserva de una version anterior: el arreglo aplicado (leer siempre la `__Host-`) evita el 403, pero la cookie legacy sigue en el navegador hasta que caduque.
- La rama de cuenta bloqueada de `LoginAsync` sigue invocando `RecordLoginFailure`, de modo que los contadores de "cuenta inexistente" se incrementan tambien al bloquearse una cuenta real.
- `SecurityAlertService` tiene su propia logica de "cuentas por IP" que solapa con el limite de S-11.
- `ChangePasswordAsync` sigue devolviendo 423 con la cuenta bloqueada (contrato fijado por un test existente) mientras que el refresh ya no corta la sesion: no es incoherencia funcional, pero no es uniforme.
- El error local "La respuesta de refresh pertenece a una sesion anterior" cae ahora en la rama transitoria y muestra un mensaje de conexion aunque el problema no sea la conexion.

## 16. Revision posterior del LOTE 3 (2026-11-17)

Revision del LOTE 3 a peticion del usuario ("revisa que todo este bien"). No
cambia el alcance: solo los IDs del LOTE 3, sin commit ni push.

**Dos huecos reales encontrados y cerrados:**

| ID | Que faltaba | Que se hizo | Evidencia |
|---|---|---|---|
| J-5 | El borrado en bloque (`ExecuteDeleteAsync`) solo estaba probado en su parte de **seleccion**: el proveedor InMemory de EF no lo soporta, asi que nadie sabia si EF Core 10 respeta de verdad el `IgnoreQueryFilters` y el criterio de usuario borrado en el DELETE. | Test contra PostgreSQL 16 real con RLS FORCE y el rol de runtime (`RowLevelSecurityRetentionJobsTests`): el token de un usuario con soft delete tiene que desaparecer DE VERDAD, el caducado tambien, y el vivo de un usuario activo se respeta. | Rojo sin el arreglo: "Expected ... to be 0L because el usuario tiene soft delete: su token no puede seguir vivo, but found 1L". Verde con el: 11/11 en la clase. Conclusion: el `ExecuteDelete` si respeta el `IgnoreQueryFilters`. |
| A-4 | El propio arreglo introducia una fuga: al escribir el volcado en un temporal, un proceso que muere a mitad deja un `<nombre>.dump.tmp` con datos personales que la retencion no puede tocar (solo borra `.dump`). Antes de A-4 ese volcado a medias acababa en un `.dump` que si se eliminaba; con A-4 se quedaria en disco para siempre. | `ApplyRetentionAsync` borra ahora los `*.tmp` con mas de 2 h (el timeout de `pg_dump` son 30 minutos, asi que uno mas viejo no puede ser de un volcado en curso). | `ApplyRetentionAsync_Should_Delete_Orphan_Temporary_Dumps_But_Keep_Recent_Ones`: rojo sin la llamada, verde con ella. |

**Comprobaciones hechas sin tocar codigo:**

- Las escrituras de los jobs funcionan bajo RLS: `BACKUPS` y `NOTIFICACIONES_ADMIN`
  tienen policy `FOR ALL USING (atlas_security.is_admin_or_system())` y los jobs
  corren en `SystemContextScope`, asi que el UPDATE de A-3 (abandonar `PENDING`)
  y el INSERT de su alerta pasan; `CONFIGURACION` no tiene RLS, asi que la marca
  de evaluacion de J-1 se puede escribir.
- `GET /api/configuracion` devuelve un DTO curado, no el mapa de configuraciones:
  la clave nueva de J-1 no aparece en la pantalla ni la puede pisar nadie.
- No hay cache de `CONFIGURACIONES`: la marca de J-1, que se actualiza cada 5
  minutos, no invalida nada en la aplicacion.
- El `.gitignore` ya ignora `.artifacts/`, asi que los directorios de compilacion
  aislados de esta revision no ensucian el estado del arbol.

**Limite anadido a la documentacion (no es un defecto):** en `BackupOperationJob`
(backup manual e importacion desde Drive) el `AutomaticRetry` no llega a
activarse, porque el job captura su propia excepcion y marca la operacion como
`FAILED` sin relanzarla; y una reejecucion no haria nada, porque el metodo vuelve
si la operacion ya esta en `SUCCESS` o `FAILED`. El bloqueo concurrente si es
efectivo. En el resto de jobs (planificador de backups, exportacion mensual,
limpiezas y alertas de seguridad) la excepcion se propaga y el reintento si
funciona. `BackupWeeklyJob` esta ademas fuera de la programacion actual
(`Program.cs` hace `RemoveIfExists("backup-weekly")`): el calendario efectivo lo
marca `BackupSchedulerJob`, que corre cada 15 minutos y decide con `IsDue`.

Verificacion tras la revision: las doce clases del LOTE 3 dan **105/105** y la
suite completa del backend **1170/1170**, sin fallos.

## 17. LOTE 5 - Correccion aplicada (2026-11-17)

Alcance: los quince IDs del LOTE 5 (A-9, B-1, B-2, B-3, B-4, B-5, B-6, B-7,
B-8, B-9, B-13, B-14, B-15 y M-9) mas la investigacion de B-17. Ningun otro
ID tocado. Rama `V-03.02`. Sin commit ni push, sin cambio de version.

**Las cuatro preguntas previas se respondieron por auto-seleccion de tiempo,
no por el usuario.** El formulario se lanzo con 60 s por pregunta y, al no
llegar respuesta, el runtime eligio la opcion recomendada de cada una. Esas
opciones se aplicaron como supuesto de trabajo y quedan abiertas a cambio:

| Pregunta | Respuesta aplicada | Consecuencia |
|---|---|---|
| B-1: cuenta sin tipo de cambio | Se excluye del total y se marca | El total no cambia; la respuesta lista las divisas sin tasa y la interfaz avisa. |
| B-2: tasas MANUAL | No se pisan y se avisa | La sincronizacion omite las manuales y genera aviso de administrador. |
| B-14: cambio de divisa | Se prohibe con extractos | 409 mientras la cuenta tenga extractos vivos. |
| B-17: orden de exportacion de los bancos | Del mas reciente al mas antiguo | Coincide con la convencion de la app (la linea mas reciente arriba del todo): **el criterio del saldo actual no se cambia** y el detector queda como red de seguridad. Ver "B-17" mas abajo. |

Las cuatro respuestas se obtuvieron por **auto-seleccion de tiempo** (el runtime
elige la opcion recomendada al agotarse los 60 s de cada pregunta), asi que
**ninguna esta confirmada por el usuario**. Se preguntaron dos veces: en la
primera ronda la de B-17 salio con la variante contraria ("del mas antiguo al
mas reciente"), incompatible con la convencion de la app, y por eso no se
aplico; en la segunda ronda las cuatro coinciden con lo ya implementado, de
modo que **el codigo es coherente con las cuatro**. Si el usuario quiere otra
variante, el punto exacto a tocar esta en el bloque siguiente.

Metodo: el orquestador confirmo las premisas leyendo el codigo citado y
repartio el trabajo en once subagentes con ficheros disjuntos. Cada subagente
escribio el test que falla con el defecto presente, el arreglo minimo y el
test en verde. El orquestador reviso cada diff linea a linea y corrigio por su
cuenta dos cosas que los subagentes dejaron mal (la matriz de transiciones de
conciliacion y el desempate de la paginacion de lotes), corrigio una
asercion que no encajaba con el indice nuevo y ajusto un comentario
desactualizado.

**Estado de esas decisiones y donde revertirlas.** Las tres primeras se
 aplicaron como SUPUESTO de trabajo (la respuesta automatica coincide con lo
 que ya estaba implementado, de modo que el codigo es coherente con lo
 pedido), pero **ninguna esta confirmada por el usuario**. Si alguna se
 cambia, el punto exacto a tocar es:

- **B-1** (excluir y marcar): `DashboardService` (calculo de las divisas sin
  tasa) y `IntegrationOpenClawController` (mismo calculo para la integracion),
  mas el aviso del frontend (`frontend/src/utils/dashboardDivisasSinTipoCambio.ts`).
  La alternativa descartada (bloquear el total) seria no devolver el total en
  cuanto falte una tasa.
- **B-2** (no pisar y avisar): `TiposCambioService.SincronizarTiposCambioAsync`
  (bucle que salta las tasas `MANUAL`) y el helper `NotificarAdminAsync` (el
  aviso). La alternativa descartada (bloquear las manuales hasta que alguien
  las libere) seria no dejar que la sincronizacion las toque nunca, con su
  aviso propio.
- **B-14** (prohibir con extractos): `CuentasController.Actualizar` (la guarda
  que devuelve 409) y la ayuda del frontend en `CuentasPage.tsx`. La
  alternativa descartada (permitir con confirmacion) seria cambiar el 409 por
  una confirmacion explicita con aviso.
- **B-17**: el criterio del saldo actual **no se ha cambiado**; lo unico
  anadido es la deteccion y el aviso, que funcionan igual con cualquier orden
  de exportacion.

**Trazabilidad del reparto entre subagentes.** Cada bloque se asigno a un
subagente con ficheros disjuntos, y cada subagente dejo su test en el
proyecto de pruebas (todos en `Atlas Balance/backend/tests/AtlasBalance.API.Tests/`
o en `Atlas Balance/frontend/tests/`):

| Subagente (alcance) | Ficheros de codigo | Tests que dejo |
|---|---|---|
| A-9 | `Services/PlazoFijoService.cs` | `PlazoFijoJsonbPostgresTests.cs` |
| B-3, B-4, B-9, B-13 (backend) | `Services/ImportacionService.cs` | `ImportacionLote5Tests.cs`, `ImportacionLote5PostgresTests.cs` |
| B-5, B-6 | `Services/ConciliacionService.cs` | `ConciliacionLote5Tests.cs`, `ConciliacionExtractoUnicoTests.cs` |
| B-8 | `Services/AlertaService.cs` | `AlertaAdvisoryLockPostgresTests.cs` |
| M-9 + indices de B-3 y B-6 (base de datos) | `Data/AppDbContext.cs`, `Migrations/AppDbContextModelSnapshot.cs`, `Migrations/20261117120000_IndicesParcialesHuellaYConciliacion.cs` | `IndicesParcialesLote5Tests.cs`, `SnapshotModelDriftTests.cs` |
| B-1 | `Services/DashboardService.cs`, `Controllers/IntegrationOpenClawController.cs`, `DTOs/DashboardDtos.cs`, frontend | `DashboardDivisasSinTipoCambioTests.cs`, `tests/dashboardDivisasSinTipoCambio.test.ts` |
| B-2 | `Services/TiposCambioService.cs`, `Controllers/TiposCambioController.cs` | `TiposCambioManualTests.cs` |
| B-7, B-15 | `Controllers/ExtractosController.cs`, `Controllers/AuditoriaController.cs`, `Services/RevisionService.cs` | `ExtractosAlertaPostTests.cs`, `PaginacionDesempateTests.cs` |
| B-13 (frontend) | `frontend/src/utils/formatters.ts` | `tests/parseEuropeanNumber.test.ts` |
| B-14 | `Controllers/CuentasController.cs` | `CuentasDivisaConExtractosTests.cs` |

El orquestador toco ademas, por su cuenta: la matriz de transiciones de
conciliacion y `frontend/src/pages/ConciliacionPage.tsx`; el desempate de la
paginacion de lotes (`Services/ImportacionService.cs`); el arreglo del advisory
lock (`Services/ImportacionService.cs`); el detector del orden de las filas
pegadas (`Services/ImportacionService.cs`, `DTOs/ImportacionDtos.cs`,
`frontend/src/pages/ImportacionPage.tsx`, `frontend/src/types/index.ts`); el
alta del test de B-1 en la lista explicita de `npm run test:unit`
(`frontend/package.json`); y una asercion que no encajaba con el indice nuevo
(`tests/ConciliacionExtractoUnicoTests.cs`).
| ID | Estado | Que se cambio | Evidencia |

|---|---|---|---|
| A-9 | **Corregido** | La comprobacion de "ya existe la notificacion" pasa de `Contains` (que EF traducía a `jsonb ~~ jsonb`) a `EF.Functions.JsonContains`, que genera `detalles_json @> @duplicadoJson`. El provider InMemory de EF no traduce `JsonContains`, asi que hay una rama equivalente sobre el JSON materializado para los tests unitarios. El aviso no depende del cambio de estado (eso ya lo garantizo J-3): el test comprueba el caso "estado ya guardado y aviso nunca entregado". | `PlazoFijoJsonbPostgresTests` (nuevo) contra PostgreSQL real: en rojo `Npgsql.PostgresException : 42883: operator does not exist: jsonb ~~ jsonb`, en verde. SQL generado comprobado sin ninguna aparicion de `~~`. `PlazoFijoServiceTests` + test nuevo: 9/9. |
| B-1 | **Corregido** | El total NO cambia. Lo que se anade: `divisas_sin_tipo_cambio` en las tres respuestas del dashboard y en la integracion OpenClaw (la integracion solo lista las divisas de las cuentas visibles para su token). En la interfaz, un aviso: "El total no incluye GBP y USD. No hay tipo de cambio para esas divisas." | `DashboardDivisasSinTipoCambioTests` (nuevo) 5/5 (rojo: la lista venia vacia), `DashboardServiceTests` 9/9, `IntegrationOpenClawControllerTests` 12/12, `IntegrationOpenClawScopeCoverageTests` 3/3, `IntegrationDefaultScopesTests` 4/4. Frontend: helper puro con test propio y suite 134/134. |
| B-2 | **Corregido** | La sincronizacion no pisa las tasas con `Fuente = MANUAL`: las omite (no tocan tasa, fuente ni fecha) y deja aviso al administrador con las divisas afectadas. Un fallo de la sincronizacion (HTTP no exitoso, payload sin tasas o excepcion) tambien genera aviso en `NOTIFICACIONES_ADMIN`, no solo un `LogWarning`. Anti-spam por clave: si ya existe un aviso con la misma clave no se repite. La respuesta dice cuantas tasas manuales se omitieron (`manual_omitidas`) y la pantalla de Configuracion lo indica. | `TiposCambioManualTests` (nuevo): rojo con 5 fallos (la tasa manual se pisaba y no habia aviso) y verde 5/5. El subagente que lo started se quedo atascado a mitad y lo termino el orquestador reutilizando el test ya escrito en rojo. `TiposCambioServiceTests` sin regresiones. |
| B-3 | **Corregido** | La deduplicacion por huella ya no ignora el filtro de soft delete, y el indice unico de huella pasa a ser parcial (`WHERE deleted_at IS NULL AND importacion_fingerprint IS NOT NULL`), con migracion escrita a mano e idempotente. Un lote revertido ya se puede reimportar. | `ImportacionLote5Tests` 9/9 y `ImportacionLote5PostgresTests` 2/2 contra PostgreSQL real (reimport tras revertir). El indice lo verifica `IndicesParcialesLote5Tests`. |
| B-4 | **Corregido** | `RevertirLoteAsync`: borrado y guardado del lote en una transaccion con `pg_advisory_xact_lock` por cuenta; el estado del lote se relee con el lock tomado y solo se revierte un lote `confirmado` (si no, 409 de dominio); las conciliaciones de los extractos borrados pasan a `excepcion` y su movimiento esperado vuelve a `pendiente`. Las conciliaciones ya resueltas no se tocan. | `ImportacionLote5Tests` 9/9 (rojo: no habia 409 y la conciliacion se quedaba en `sugerida`) y `ImportacionLote5PostgresTests` 2/2 con atomicidad comprobada (rojo: el extracto quedaba borrado sin rollback). |
| B-5 | **Corregido** | Si la diferencia de importe entre el movimiento y el extracto supera la tolerancia del movimiento, el score se pone a 0 y el extracto no se propone. La tolerancia sigue viniendo de `CONFIGURACION` y no se toca. | `ConciliacionLote5Tests` 7/7 (rojo: el movimiento de 50 € recibia el extracto de 900 €). `ConciliacionServiceToleranciaTests` sin regresiones. |
| B-6 | **Corregido** | Al pasar a `conciliada` se rechaza (409) si el extracto ya esta conciliado; las transiciones de estado se validan contra un conjunto minimo coherente con el CHECK de la tabla; si el movimiento esperado no existe se devuelve 404 en vez de 500. En la base de datos, indice unico parcial sobre `CONCILIACIONES(extracto_id)`, con purga idempotente y no destructiva de duplicados preexistentes. | `ConciliacionLote5Tests` 7/7 (rojo: 6 fallos, incluido el `InvalidOperationException` que se traducía en 500) y `ConciliacionExtractoUnicoTests` 2/2 contra PostgreSQL real: el indice salta con 23505. |
| B-7 | **Corregido** | `PUT /api/extractos/{id}` protege la evaluacion de alertas con el mismo try/catch que `Crear`: si falla, el cambio ya guardado no se convierte en un 500. | `ExtractosAlertaPostTests` (nuevo) en rojo (`InvalidOperationException` propagada) y en verde. |
| B-8 | **Corregido** | La zona critica de la alerta de saldo (lock, lectura del cooldown, escritura del cooldown, commit) va dentro de una transaccion explicita; si el llamante ya tiene una transaccion (camino de importacion) se reutiliza la suya. El envio del email queda FUERA de la transaccion para que un SMTP lento no retenga el lock. | `AlertaAdvisoryLockPostgresTests` (nuevo) contra PostgreSQL real: rojo con 2 envios de email, verde con 1, tres ejecuciones seguidas sin intermitencia. `AlertaServiceTests` + test nuevo: 8/8. |
| B-9 | **Corregido** | El saldo anterior del movimiento de plazo fijo se calcula solo sobre extractos vivos. El `maxFila` contiguo si incluye las filas borradas (para no reutilizar su numero de fila) y no se toco. | `ImportacionLote5Tests` (rojo: saldo 999 en vez de 100). |
| B-13 | **Corregido** | Backend: si la parte entera vale 0, el numero no se trata como millar ("0,500" es 0,5). Frontend: **el defecto real era con punto** ("0.500" se leia como 500), porque en `parseEuropeanNumber` la coma siempre es decimal y la heuristica de millar esta en la rama del punto. Se aplico la misma regla en esa rama. | `ImportacionLote5Tests` (teoria de 5 casos) y `parseEuropeanNumber.test.ts` (nuevo, 2 casos) en rojo y en verde. Suite del frontend: 131/131 con la lista canonica y 221/221 con todos los ficheros compilados. |
| B-14 | **Corregido** | Cambiar la divisa de una cuenta con extractos vivos devuelve 409 con un mensaje accionable: "La cuenta tiene extractos: no se puede cambiar su divisa. Crea una cuenta nueva o elimina los extractos de esta cuenta antes de cambiarla." Los extractos en papelera no bloquean (el filtro global de soft delete se respeta). La interfaz avisa de la restriccion al editar. | `CuentasDivisaConExtractosTests` (nuevo) 4/4 (rojo: devolvia 200 y cambiaba la divisa). `CuentasControllerTests` 20/20. Frontend: `tsc` y `eslint` limpios, build correcto. |
| B-15 | **Corregido** | Desempate estable por clave primaria despues del campo pedido en los cuatro listados: extractos (`ThenBy(x => x.Id)`), auditoria (`ThenBy(x => x.Id)`), revision (`ThenBy(x => x.ExtractoId)`, que es la PK de la proyeccion) y lotes (`ThenBy(x => x.Id)` en `ImportacionService.ListarLotesAsync`, que esta fuera del controller). El contrato de la API no cambia. | `PaginacionDesempateTests` (nuevo) 4/4 en rojo y en verde. Regresion de las cuatro clases de controller: 54/54. |
| M-9 | **Parcial** | El bloque de `xmin` de `PLAZOS_FIJOS` se escribio a mano en el snapshot, con el mismo estilo que las otras cuatro tablas. **El `Ignore` de `PendingModelChangesWarning` NO se quito**: la deriva restante no es solo del snapshot. | `SnapshotModelDriftTests` (nuevo) 1/1 con la comparacion modelo/snapshot que hace EF por dentro: **27 diferencias** pendientes, y la base real difiere del modelo en el esquema (`deleted_at` como `timestamp without time zone` en tres tablas donde el modelo pide `with time zone`, 3 indices `*_deleted_by_id` y 4 FK `deleted_by_id` que faltan, filtros de tres indices unicos y el nombre de una FK). |
| B-17 | **Parcial: detectado y avisado; el criterio NO se cambia** | Se ha implementado la deteccion del orden real de las filas pegadas: la validacion comprueba las fechas del bloque y, si suben al bajar por el bloque (es decir, la linea mas antigua va ARRIBA del todo), avisa de que el saldo actual se calculara con la fila mas antigua. **El criterio del saldo actual no se toca**: sigue siendo la fila con mayor `FilaNumero`, porque cambiarlo es una decision de producto que depende del orden en que exportan los bancos. | `ImportacionLote5Tests`: rojo con el detector desactivado (1 de 12 fallos) y 12/12 en verde. El aviso sale por la respuesta de validacion (`advertencias`) y se ve en la pantalla de importacion antes de confirmar. |

**B-17: el defecto es real, pero la pregunta de fondo no esta respondida.**
Lo que se comprobo leyendo el codigo y la documentacion:

- `ImportacionService` numera las filas **de abajo arriba** al confirmar, para que
  "la linea superior quede como la ultima del lote" (`ImportacionService.cs:771-774`).
  Es decir: la fila mas pegada arriba del fichero recibe el `fila_numero` mas alto.
- El "saldo actual" es la fila con mayor `FilaNumero` (`DashboardService.cs:508-521`),
  por diseño explicito (V-02-04) y distinto del snapshot a fecha de corte.
- La documentacion de usuario dice lo mismo: "Al confirmar, Atlas Balance respeta
  el orden del extracto pegado. La linea superior queda como la ultima del lote"
  (`DOCUMENTACION_USUARIO.md:477`). La pantalla de importacion **no** dice nada
  del orden de las filas.
- Por tanto, la app **espera que la linea mas reciente del banco sea la linea
  superior** del bloque pegado. Un banco que exporta de mas antiguo a mas
  reciente haria que el sistema tomara la fila mas antigua como saldo actual.
- No hay ninguna validacion de continuidad de saldos que delate el error.
- **Evidencia del producto (2026-11-17):** los datos de demostracion del propio
  frontend siguen la misma convencion. `frontend/e2e/visual/mockApi.ts:102-114`
  genera los 60 extractos de la cuenta de ejemplo "Operativa BBVA" con
  `fila_numero: 60 - i` y `fecha` que **baja** segun `i` (`14 - i/2`): el
  `fila_numero` mas alto es la fecha mas reciente y el saldo es el acumulado en
  ese sentido. Es decir, `fila_numero` ascendente = chronological, y la linea
  superior del bloque pegado es la mas reciente.
- **Evidencia externa (2026-11-17):** los formatos predefinidos del producto son
  de BBVA, Sabadell (BS) y Banquinter (`SeedData.cs:824-874`), y ninguno de
  ellos describe el orden de las filas. Las paginas publicas de estos bancos
  describen COMO descargar los movimientos (BBVA: "Buscar movimientos" >
  "Descargar movimientos", en Excel; Sabadell: "Saldos y extractos" >
  descargar en Excel, PDF o Norma 43) pero **ninguna fija en que orden vienen
  las filas del fichero**. No se ha encontrado ninguna fuente que lo declare, de
  modo que la pregunta al usuario sigue siendo la unica fuente valida.

La pregunta al usuario (en que orden exportan los bancos) se respondio por
auto-seleccion, sin confirmacion. En la primera ronda salio la variante
("del mas antiguo al mas reciente"), que **contradice** la convencion
documentada de la aplicacion y los datos de demostracion del producto, y no se
aplico. En la segunda ronda salio la variante que **coincide** con la
convencion ("del mas reciente al mas antiguo"), con lo que el criterio del
saldo actual **no se cambia**: la app ya hace lo correcto para ese orden de
exportacion. Sigue siendo una respuesta no confirmada por el usuario, asi que
B-17 queda **pendiente de una respuesta confirmada** (y supondria cambiar el
significado de `fila_numero` en todos los datos existentes y en todas las
instalaciones).

Lo que si se ha hecho (y no cambia ninguna regla): la validacion **detecta** el
orden real del fichero y **avisa** cuando no coincide con el que espera la app.
La deteccion se apoya en las fechas, no en heuristicas de saldo, asi que no
tiene falsos positivos: solo avisa cuando hay al menos dos fechas distintas y
suben de forma estricta al bajar por el bloque (la linea mas antigua arriba del
todo). Con una sola fecha, con fechas repetidas o con fechas mezcladas no se
dice nada. El aviso sale en la pantalla de importacion, antes de confirmar, y
dice exactamente que va a pasar y por que. Hay tres caminos posibles:

1. Confirmar que los bancos exportan de mas reciente a mas antiguo (lo que la
   app ya espera y lo que hacen sus datos de demostracion): B-17 se cierra como
   "no aplica" y el aviso se queda como red de seguridad.
2. Confirmar que exportan de mas antiguo a mas reciente: hay que invertir la
   numeracion (migracion de datos incluida) o deducir el saldo actual por fecha
   en lugar de por `FilaNumero`.
3. Dejar el comportamiento actual y dejar solo el aviso, que en ambos casos
   detecta el error de pegado.

**Verificacion global del lote:** backend **1231/1231** en `AtlasBalance.API.Tests`
(incluida la suite de PostgreSQL real con Testcontainers) y **15/15** en
`AtlasBalance.Caching.Tests`, sin fallos. Frontend: `npx tsc --noEmit` sin
errores, `npx eslint src tests --max-warnings 0` sin errores, **227/227** pruebas
unitarias y build de Vite correcto. Los comandos canonicos `npm run test:unit`
y `npm run build` siguen rotos por permisos heredados de `.test-dist/` y `dist/`
(otra cuenta de sandbox): las mismas comprobaciones pasaron con carpetas de
salida nuevas. Sin validacion visual en navegador ni sesion autenticada real.

Observaciones que quedan abiertas y **no** se han tocado (fuera de LOTE 5):

- **`ConfirmarLoteAsync` respondia siempre 409 contra PostgreSQL real. YA
  CORREGIDO en este lote** (no es un ID del informe, pero bloqueaba B-3).
  `TryAcquireAdvisoryXactLockAsync` leia con `SqlQueryRaw<int>` el boolean que
  devuelve `pg_try_advisory_xact_lock`: Npgsql lanzaba `InvalidCastException` y
  el fail-closed devolvia `false`, con lo que confirmar un lote (y por tanto
  reimportar un lote revertido) era imposible. Se lee como `SqlQueryRaw<bool>`.
  Verificado con una sonda contra PostgreSQL real y con un test de extremo a
  extremo por el camino real de la API (`ImportacionLote5PostgresTests`): en
  rojo sin el arreglo (1 de 3) y en verde con el (3/3).
- El frontend y el backend no coinciden en como interpretan "1,500": el backend
  lo trata como millar (1500) y el frontend como decimal (1,5). Es una
  incoherencia anterior a B-13 y unificarla cambiaria el comportamiento de
  casos que el encargo excluia expresamente.
- La transicion `excepcion -> conciliada` se mantiene porque la pantalla ofrece
  el boton "Conciliar" en toda fila que no este conciliada (el subagente la
  habia bloqueado con un 409; el orquestador lo corrigio). En cambio, las filas
  `resuelta` ya no ofrecen "Conciliar" ni "Excepcion": ese estado es terminal.
- La purga de conciliaciones duplicadas que hace la migracion marca las
  sobrantes con `deleted_at` sin reescribir su `estado`: su movimiento esperado
  sigue en `conciliada` aunque su conciliacion este borrada.
- `npm run lint`, `npm run test:unit` y `npm run build` siguen rotos en esta
  maquina por permisos heredados de `.test-dist/`, `.test-dist-v2/` y `dist/`
  (otra cuenta de sandbox). Las mismas comprobaciones pasan con carpetas de
  salida nuevas.
- En este mismo arbol trabaja en paralelo otra sesion (LOTE 7). **Incidente de
  proceso:** durante el LOTE 5, un subagente capturo su fase roja con
  `git checkout --` sobre `ImportacionService.cs`, lo que devolvio el fichero a
  su estado en `HEAD` y se llevo por delante el trabajo en vuelo de la otra
  sesion (R-7, escritura por bloques). **Comprobado el estado actual: ese
  trabajo esta intacto** (marcas `TamanoBloqueInsercion`, `ChangeTracker.Clear`,
  `BuildSummariesAsync` y `soloEliminados` presentes en el arbol), de modo que
  no queda ninguna perdida pendiente de rehacer. El incidente queda aqui porque
  es un aviso para los lotes siguientes: para capturar el rojo hay que usar una
  copia del fichero o un `edit` puntual y su restauracion, **nunca** un
  `git checkout --` sobre un arbol compartido. Todos los rojos de este lote se
  capturaron ya asi.

## 18. LOTE 7 - Correccion aplicada (2026-11-17)

Alcance: solo los catorce IDs del LOTE 7 (F-2, F-4, F-5, F-6, F-7, F-8, F-9,
F-10, R-4, R-5, R-6, R-7, R-8 y B-16). Ningun otro ID tocado. Rama `V-03.02`.
Sin commit ni push.

Decisiones de producto fijadas antes de implementar (el cuestionario se
respondio por tiempo de espera con la opcion propuesta, y sigue siendo
ajustable):

- **F-7:** borrado y marcado en lote con **concurrencia limitada en el cliente**
  (pool de 4), no con un endpoint de lote en el backend.
- **F-8:** vaciar un campo numerico conserva el ultimo valor valido, porque los
  DTO del backend usan `int` no nullable y "vacio" no es representable en la API.

Metodo: diez subagentes con ficheros disjuntos (grupo por fichero), cada uno
con la obligacion de abrir el codigo citado antes de arreglarlo, escribir un
test que falla antes, aplicar el arreglo y dejar la suite en verde. Revision
del orquestador sobre cada diff y verificacion final unificada.

| ID | Estado | Que se cambio | Evidencia |
|---|---|---|---|
| F-2 | **Corregido** | (a) Los cuatro listados paginados aceptan `soloEliminados`, aplicado ANTES de contar y paginar (reutiliza el `IgnoreQueryFilters()` de `incluirEliminados` y la proteccion de ADMIN). La papelera pide 50 por pagina, pinta paginacion real, deja de filtrar en cliente y usa la divisa de la fila (`ExtractoListItemResponse.Divisa`, que ya venia). (b) **Respuestas fuera de orden**: la pagina descarta la respuesta de una peticion que ya no es la vigente (`utils/peticionSecuencial.ts`): si el usuario cambia de pestana o de pagina mientras la peticion esta en vuelo, la respuesta vieja no pisa la nueva, no muestra su error y no apaga el estado de carga de la peticion vigente. | `PapeleraSoloEliminadosTests` (backend, 5 tests): 5/5 en rojo antes -> 5/5. `papeleraSoloEliminados.test.ts` (frontend, 5 tests): 5/5 en rojo antes -> 5/5. `papeleraRespuestasFueraDeOrden.test.ts` (frontend, 6 tests): 3 de 6 en rojo antes (filas, error y estado de carga sin guarda de vigencia) -> 6/6. |
| F-4 | **Corregido** | `utils/errorToast.ts` decide el toast de una peticion y admite `skipErrorToast` por peticion; `utils/pollLifecycle.ts` da a los bucles `active`/`wait`/`stop`, de modo que los sondeos de Configuracion y Backups no pintan toasts y se detienen al desmontar. Los toasts de 419/440, 429 y de red de una peticion normal no cambian. | `errorToast.test.ts` (9) + `sondeosSinToast.test.ts` (3): 9 de 21 tests nuevos en rojo antes -> 21/21. |
| F-5 | **Corregido** | `alertasStore` pasa a ser el unico escritor del estado del banner: `syncAlertas` es la unica entrada del contenido y ancla el descarte a la huella del contenido (`alerta_id:cuenta_id`) guardada en `sessionStorage`. El hook solo proyecta la consulta: mientras no hay datos no se toca el descarte. | `alertasBannerDismissal.test.ts` (6, sobre el hook y el store reales): 4 de 6 en rojo antes -> 6/6. |
| F-6 | **Corregido** | `utils/notaAlerta.ts` compara la nota editada con `row.flagged_nota`: si no cambia, no hay PATCH. La comparacion es contra el valor del servidor, asi que un cambio ajeno no se pierde. | `notaAlerta.test.ts` (4): 1 fallo antes -> 4/4. |
| F-7 | **Corregido** | `utils/concurrencia.ts` (`ejecutarEnLote`, limite 4) ejecuta todas las operaciones, no corta el lote ante un fallo y devuelve el resultado por operacion; la pagina actualiza el estado solo con las filas que si se movieron y avisa de las que fallaron. | `loteConcurrencia.test.ts` (8): 11 de 15 en rojo antes -> 13/13 con el area. |
| F-8 | **Corregido** | `utils/numberInput.ts` (`''` -> `null`, nunca 0; `'0'` -> 0 valido) y componente `NumberField`: once campos numericos se pueden vaciar sin convertir el valor en 0. Con el campo vacio se conserva el ultimo valor valido. | `configuracionCamposNumericos.test.ts` (5): 2 de 21 en rojo antes -> verde. |
| F-9 | **Corregido** | `utils/editableCellSave.ts` resuelve el resultado del guardado: en `'failed'` NUNCA escribe el borrador, de modo que lo escrito se conserva, la celda sigue en edicion y el aviso de error se ve (antes nunca se renderizaba). Reintento con Enter o blur, descarte con Escape. | `editableCellSave.test.ts` (6): 4 de 6 en rojo antes -> 6/6. |
| F-10 | **Sospecha descartada; defecto real corregido** | El backend acota el replay idempotente al propio lote (`lote.ConfirmacionIdempotencyKey`), asi que confirmar un lote distinto con la clave anterior NO daba 409: la sospecha, tal como estaba redactada, no se reproduce. Si habia un 409 real: en la CREACION, al recargar el contexto (cambio de pais o cuenta preseleccionada) se fijaba otra cuenta objetivo sin renovar la clave. Arreglo: la clave se regenera por lote (validar y abrir del historial) y al cambiar la cuenta objetivo. | `importacionIdempotencia.test.ts` (6): 5 de 6 en rojo antes -> 6/6. |
| R-4 | **Corregido** | El efecto que llamaba a `loadCuentaData` (que invalidaba `cuentaResumen` y `extractos.list`, las dos consultas activas) se sustituye por uno que solo limpia los avisos. Las recargas explicitas (celda guardada, fila insertada, evento de importacion) siguen refrescando. | `cuentaDetailInvalidacion.test.ts` (3): en rojo antes -> 13/13 con el area. |
| R-5 | **Corregido** | `utils/canEditCellCache.ts` cachea por fila y columna en un `useMemo` con dependencia `[canEditCell]`: tres evaluaciones por celda pasan a una, y la cache se invalida sola si cambian los permisos o la fila. | `canEditCellCache.test.ts` (5): 2 fallos antes -> 5/5. |
| R-6 | **Corregido** | Se reutiliza el hook existente `useDebouncedValue` (300 ms) en Formatos, Usuarios y el campo "Modelo" de Configuracion. **Paises y Cuentas ya estaban corregidos** (commit `882f721`, anterior al informe): el defecto real era la mitad del hallazgo. | `busquedaDebounce.test.ts` (6, 5 en rojo antes) y `configuracionDebounceModelo.test.ts` (4, 4 en rojo antes) -> verde. |
| R-7 | **Corregido (parcial)** | (a) `ExtractosController.BuildSummariesAsync` sustituye al N+1: un titular con 4 cuentas pasa de **23 a 9** SELECT (medido con PostgreSQL real e interceptor de comandos). (b) `ImportacionService` inserta por bloques de 1.000 (`TamanoBloqueInsercion`) las filas del lote, los extractos y sus columnas extra, y el estado de las filas, todo dentro de la misma transaccion: sin lote a medias y sin romper la idempotencia (el indice unico sigue decidiendo la carrera). | `ExtractosResumenConsultasTests` (2, PostgreSQL real): 1 fallo antes ("Expected consultasConCincoCuentas to be less than or equal to 11 ... but found 23") -> 2/2. `ImportacionR7Tests` (3, con contador de filas por `SaveChanges`): 2 fallos antes ("...but found 1: {5100}") -> 3/3. |
| R-8 | **Corregido** | La cache de TanStack Query pasa a ser la fuente de verdad de la disponibilidad de la IA: `staleTime` de 5 min en vez de `refetchInterval` de 30 s, `refetchOnWindowFocus` y refresco INMEDIATO al guardar la configuracion global (un interceptor detecta el `PUT /configuracion` correcto). De 2 peticiones/min por pestana a 1 al montar y como mucho 1 cada 5 min. | `iaConfigSondeo.test.ts` (8): 4 de 8 en rojo antes -> 8/8. |
| B-16 | **Corregido (parte frontend)** | El helper compartido **no existia**: "hoy" estaba duplicado como funcion local en `ConciliacionPage` e `ImportacionPage`, y `ExtractoTable` usaba `toISOString().slice(0, 10)` (UTC). Ahora hay un helper unico (`utils/hoy.ts`) usado por los cuatro sitios, y las dos copias locales se eliminan. | `hoy.test.ts` (5, con `TZ=Europe/Madrid`), `cuentaDetailHoy.test.ts` (2) y la parte de B-16 de `importacionIdempotencia.test.ts`. |

Verificacion global: frontend con `tsc --noEmit` sin errores, `eslint . --max-warnings 0` sin errores, **227/227 pruebas unitarias** (37 ficheros de test) y build de Vite correcto (5,5 s). Backend con la suite completa: **1230/1231**, con un unico fallo que NO es de este lote (`AuthServiceTests.Login_Should_Not_Reveal_When_User_Is_Already_Locked`, introducido por el commit ajeno `01ffeb3` del LOTE 2: espera 401 y recibe 429 por el limite por IP; reproducido en aislamiento con 43 correctos y 1 fallo en esa clase). Sin validacion visual ni sesion autenticada.

Pendientes que quedan abiertos y **no** se han tocado:

- Los extractos de una cuenta **ya borrada** siguen sin salir en la papelera:
  `ExtractosController.Listar` filtra por `GetAllowedAccountIds`, que excluye las
  cuentas eliminadas. Es un filtro de alcance preexistente.
- El N+1 de la evolucion de OpenClaw (`IntegrationOpenClawController.cs:520`)
  queda pendiente: no es N+1 de SQL (el catalogo de tipos de cambio esta
  cacheado) sino N llamadas async por extracto. Ademas `DashboardService`
  arrastra un N+async residual ya anotado en `AUDITORIA_CONCURRENCIA_2026-07-10.md`.
- `iaAvailabilityStore.load()` (sin llamante) y `aiChatStore.ensureConfig()`
  siguen siendo copias de la configuracion de IA: no se han tocado por no meter
  cambios de comportamiento ajenos al ID.
- La atomicidad del troceado (rollback sin dejar rastro) no esta probada contra
  PostgreSQL: el test usa InMemory y comprueba la estructura del troceado, no
  el rollback. Docker si esta disponible en la maquina.
- `npm run test:unit` y `npm run build` canonicos siguen rotos por permisos
  heredados de `.test-dist/` y `dist/` (M-10 de este informe): las mismas
  comprobaciones pasan con carpetas de salida nuevas. **Causa comprobada con
  ACL**, no es codigo: `icacls .test-dist` y `icacls dist` conceden `(M)` solo
  a dos SID y a `TRAKERIA\CodexSandboxUsers`, mientras que a
  `BUILTIN\Usuarios` solo `(RX)`; el usuario que ejecuta (`trakeria\usuario`)
  no esta en la ACL de escritura. Los comandos canonicos **si se ejecutaron** y
  fallaron por `TS5033 EPERM` (22 ficheros de `.test-dist/`) y por
  `EPERM ... copyfile public/fonts/Geist-Bold.woff2 -> dist/fonts/...`.
- **El HEAD actual no compila por si solo** (queda fuera del alcance de este
  lote, que no hace commit): el commit ajeno `01ffeb3` ("LOTE 2") se llevo por
  delante codigo en vuelo de este LOTE 7 (`ConfiguracionPage.tsx` con F-8, R-6
  y F-4, y `services/api.ts` con F-4), que importan `@/utils/numberInput`,
  `@/utils/pollLifecycle` y `@/utils/errorToast`: esos tres ficheros son
  **nuevos y no estan en HEAD** (`git cat-file -e HEAD:<fichero>` falla). Un
  clon limpio de HEAD no compila hasta que se commitee el LOTE 7. La accion
  pendiente es un commit propio del LOTE 7 (`git commit --only` con sus
  ficheros), que no se ha hecho porque este lote no commitea sin que lo pidas.
- El `BOM` de `DOCUMENTACION_CAMBIOS.md` no se ha perdido: el blob de HEAD y el
  fichero de trabajo empiezan los dos por `23 23 20` (`## `), sin marca UTF-8.
- Al compilar la suite de backend **fuera** del arbol (`--artifacts-path` en
  `%TEMP%`) aparecen 14 fallos con `FileNotFoundException` y **ningun** de los
  fallos reales: son exactamente los tests que leen ficheros de codigo o de
  configuracion (`AppsettingsValidezTests` 6, `SerilogHttpClientLoggingTests`
  3, `IaFechasOperativasTests` 3, `ControllerAuthorizationCoverageTests` 1 y
  `WatchdogLogConfigurationTests` 1). El mecanismo es `TestSourceLocator.Find`
  (`tests/AtlasBalance.API.Tests/TestSourceLocator.cs:5-28`): sube desde
  `AppContext.BaseDirectory` buscando `src/...` o
  `Atlas Balance/backend/src/...`, y con la carpeta de compilacion fuera del
  repositorio el recorrido no encuentra nada y lanza `FileNotFoundException`.
  Con `--artifacts-path` **dentro** del repo (`backend/artifacts-l7-orchestrator`)
  el mismo recorrido encuentra la raiz y esos tests pasan. Es una limitacion
  del localizador de fuentes, no una regresion del LOTE 7, y no se ha tocado
  porque es infraestructura compartida de tests.

## 19. LOTE 6 - Correccion aplicada (2026-11-18)

Alcance: los IDs del LOTE 6 (S-1, S-2, S-3, S-5, S-7, S-8, S-9, M-3, M-5,
R-3 y D-13) mas las dos pantallas que faltaban de la seccion 8.1. Ningun otro ID
tocado. Rama `V-03.02`. Sin commit ni push y sin cambio de version.

**Comprobacion previa del LOTE 1.** El encargo da por hecho que A-1 (los cuatro
permisos que el formulario de usuario perdia) ya esta aplicado. **Comprobado de
punta a punta**: los cuatro flags estan en el formulario (`UsuarioModal.tsx`),
en el payload (`utils/permisosFormUtils.ts`), en el DTO
(`SavePermisoUsuarioRequest`) y en la persistencia (`UsuariosController.cs`).
No hizo falta tocar nada de A-1.

**Decisiones de producto.** El cuestionario se respondio por **auto-seleccion de
tiempo** (60 s por pregunta), de modo que **ninguna esta confirmada por el
usuario** y todas siguen siendo ajustables:

- **S-1:** quien solo tiene permisos de escritura **no** ve saldos en el
  dashboard: hacen falta "Ver cuentas" y "Ver dashboard".
- **S-2:** las columnas que fija el administrador son una **restriccion**: el
  servidor filtra y el usuario solo puede estrechar su vista.
- **S-3:** **revertir un lote exige `puede_eliminar_lineas`**.
- **S-5:** el PUT de usuario pasa a ser **estricto**: cuerpo completo o 400 con
  la lista de campos que faltan (no se anade un PATCH).
- **8.1 (endpoints sin llamada):** se busco cliente externo en todo el
  repositorio (frontend, scripts, OpenClaw y documentacion) y **no hay
  ninguno**, pero **no se retiran**: la respuesta fue automatica y borrarlos es
  irreversible.

**Dos premisas del informe corregidas al releer el codigo.** (a) En S-2 **no
existe el concepto de "columnas ocultas"**: hay dos *whitelists*
(`columnas_visibles` y `columnas_editables`) y el administrador y el usuario
escribian **en la misma columna de la misma fila** de
`PREFERENCIAS_USUARIO_CUENTA`. (b) En S-8, la parte de `CuentasController`
**no tiene defecto**: `ActualizarNotas` ya respondia 403 cuando la cuenta
existe y falta el permiso; el defecto real estaba en `ExtractosController` (7
endpoints).

Metodo: el orquestador confirmo las premisas leyendo el codigo citado,
aplico el cambio de esquema (columna nueva + migracion) y repartio el trabajo
en siete subagentes con ficheros disjuntos. Cada subagente escribio el test
que falla antes del arreglo, el arreglo minimo y el test en verde. El
orquestador corrigio por su cuenta cuatro cosas que quedaron fuera del alcance
de los subagentes: la columna y su migracion, el espejo en el registro de
seguridad de la exportacion de auditoria, el `sortDir=desc` de
`/api/usuarios`, y la ordenacion de la revision (que el subagente de R-3
resolvia trayendo **todas** las filas al controller para ordenarlas en
memoria: una regresion de rendimiento que se ha trasladado al servicio, donde
se resuelve en SQL).

| ID | Estado | Que se cambio | Evidencia |
|---|---|---|---|
| S-1 | **Corregido** | El dashboard exige `PuedeVerDashboard` **y** `PuedeVerCuentas` en la misma fila para todo rol no ADMIN (la excepcion de GERENTE desaparece) y se elimina `GrantsAccountDataAccess`, que concedia lectura con cualquier flag de escritura. ADMIN conserva el bypass. | `DashboardServicePermisosLecturaTests` (nuevo, 6 casos): rojo con 2 fallos -> 6/6; las tres clases de dashboard, 20/20. Dos tests existentes ajustados porque codificaban el defecto. |
| S-2 | **Corregido** | Columna nueva `columnas_visibles_admin` en `PREFERENCIAS_USUARIO_CUENTA` (migracion `20261118100000_AddColumnasVisiblesAdminPreferencias`): la regla del administrador deja de compartir celda con la preferencia del usuario. El conjunto efectivo es `admin ∩ usuario`; lo aplica el filtro en servidor de `GET /api/extractos` y el `PUT` del usuario guarda la interseccion. `GET /api/extractos/columnas-visibles` devuelve el conjunto efectivo y `columnas_restringidas`. El guardado de permisos del admin ya no borra la preferencia del usuario. | `ExtractosColumnasServidorTests` (nuevo, 9 casos): rojo con 7 fallos -> 9/9; `*Extractos*` 46/46; `UsuariosPermisosYCamposObligatoriosTests` (nuevo, 12 casos) -> 12/12. |
| S-3 | **Corregido** | `RevertirLoteAsync` usa la accion `EliminarLineas` (`puede_eliminar_lineas`); antes bastaba `puede_importar` o `puede_aprobar_importaciones`. 404 (lote inexistente) != 403 (sin permiso), bypass de ADMIN intacto. | `PermisosLote6Tests` (nuevo, 12 casos): rojo con 3 fallos -> 12/12. Tres seeds de test que reponen lotes dan ahora `puede_eliminar_lineas`. |
| S-5 | **Corregido** | `UpdateUsuarioRequest` deja de tener valores por defecto destructivos: los campos que deben distinguir "enviado" de "no enviado" son anulables en el DTO y el controller responde **400 con la lista de los campos ausentes**. `CreateUsuarioRequest` no se toca. | `UsuariosPermisosYCamposObligatoriosTests` (nuevo, 12 casos): 11 de 12 en rojo antes -> en verde; `*Usuarios*` 37/37. Tres peticiones de test existentes ajustadas. |
| S-7 | **Confirmado y corregido** | `ExportarCsv` no dejaba ninguna fila de auditoria (ni el interceptor de entidades ni `SecurityAuditMiddleware` cubren una lectura). Ahora escribe `AUDITORIA_EXPORTACION_CSV` con el actor, el numero de filas y los filtros, despues de materializar las filas; la accion queda tambien en el registro de seguridad. | `AuditoriaControllerExportacionCsvAuditoriaTests` (nuevo): rojo con "Sequence contains no elements" -> en verde; los tests previos de `AuditoriaController` siguen verdes. |
| S-8 | **Parcial** | En `CuentasController` **no habia defecto** (confirmado con tests, sin tocar el codigo de produccion). En `ExtractosController` si: 7 endpoints (`Actualizar`, `GuardarDesglose`, `ToggleCheck`, `ToggleFlag`, `Eliminar`, `Restaurar`, `GetAuditCelda`) devolvian 404 donde el recurso existe y faltaba el permiso. | `ExtractosControllerTests` 28/28; un test existente cambia de `NotFound` a `Forbid` porque codificaba el defecto. Tres tests nuevos fijan el comportamiento correcto de `ActualizarNotas`. |
| S-9 / M-3 | **Corregido** | Las reglas de permiso por accion tienen **una sola fuente**: `UserAccessAction` + un unico mapa de predicados en `UserAccessService`, con `CanCuentaAsync` (chequeo por cuenta) y `GetCuentasAccesiblesAsync` (conjunto de cuentas en una query, sin N+1). `ConciliacionService` e `ImportacionService` dejan de reimplementarlas. `CanConciliarCuentaAsync` y `CanCerrarConciliacionAsync` pasan a ser estrictos (la regla que de verdad se aplicaba). Sin cambio observable salvo S-3. | `PermisosLote6Tests` (12 casos, dos de delegacion con espia): rojo con 3 fallos -> 12/12; `*Conciliacion*` 13/13, `*Importacion*` 79/79, `*UserAccess*` 19/19. |
| M-5 | **Corregido** | La condicion del encargo se cumple (los tests de autorizacion existentes cubren admin, acceso global, permiso solo-dashboard, pais, titular sin acceso, borrados, permisos de edicion y de desglose): `ExtractosController` inyecta `IUserAccessService` y deja de reimplementar el alcance. | `ExtractosControllerTests` 28/28 y `*Extractos*` 46/46. |
| R-3 / seccion 9 | **Corregido** | `GET /api/importacion/lotes/{id}/filas`, `GET /api/conciliacion` y `GET /api/conciliacion/movimientos-esperados` devuelven `{ data, total, page, page_size, total_pages }` y admiten `?sortBy`/`?sortDir` (los dos listados de conciliacion ya no se recortan a 500 filas). Se anade ordenacion a paises, auditoria, lotes, comisiones y seguros, y el caso por defecto de cuentas, titulares, formatos y **usuarios** deja de ignorar `sortDir`. | `ContratoApiPaginacionTests` (nuevo, 18 casos): rojo con 5 fallos -> en verde; `*Conciliacion*` + `*Importacion*` 92/92 y 216/216 en las areas tocadas. |
| D-13 | **Corregido** | `canConciliarCuenta` deja de estar sin consumidores; el store replica los predicados exactos del backend y anade `canCerrarConciliacionCuenta` y `canRevisarCuenta`. Conciliacion y Revision ocultan las acciones que el backend deniega. En Revision la puerta de acciones pasa de `canEditCuenta` a `canRevisarCuenta` (antes se ocultaban a quien solo tiene `puede_revisar_lineas`, que el backend si le deja revisar). | `permisosConciliacionRevision.test.ts` (nuevo, 11 casos): rojo con 6 fallos -> 11/11; suite completa del frontend 265/265. |
| 8.1 (pantallas) | **Corregido** | Pantalla de **dispositivos MFA de confianza** (`/seguridad/dispositivos-mfa`, entrada de menu "Dispositivos MFA") para ver y revocar los equipos con MFA recordado, y boton **Revertir** en el historial de lotes de la pantalla de Importacion, con confirmacion y solo para quien puede eliminar lineas. | `dispositivosMfaYRevertirLote.test.ts` (nuevo, 17 casos) en verde; capturas Playwright de las dos pantallas y de los dos dialogos de confirmacion (54 PNG en `.artifacts/l6-visual`). |

**Lo que queda abierto de este lote (y no se ha tocado):**

- **Las columnas base no-nullable de la fila** (`Fecha`, `Monto`, `Saldo`,
  `FilaNumero`, `CuentaNombre`, `TitularNombre`, `Divisa`, `Checked`, `Flagged`,
  `Desglose*`) **no se pueden ocultar** con el contrato actual: las que son
  nullable (`Concepto`, `Comentarios`, `BancoNombre`) llegan a `null` y las
  columnas extra dinamicas se filtran enteras, pero ocultar las demas exigiria
  un contrato de fila nuevo. Con una whitelist del administrador
  `["fecha","monto"]` el usuario sigue viendo saldo, cuenta, titular, divisa,
  marcado y desglose.
- **El oraculo de existencia sigue abierto** con la regla 403/404 que pidio el
  encargo: un actor sin permiso sigue distinguiendo "existe" de "no existe".
  `Obtener` y `Resumen` de `CuentasController` hacen lo contrario (403
  uniforme): unificar ambos estilos es una decision de politica.
- **No hay migracion de datos** que rellene `puede_ver_dashboard` en los
  permisos ya dados de alta: un gerente con `puede_ver_cuentas` y sin
  `puede_ver_dashboard` pierde el panel (403). Lo decide el administrador
  revisando la matriz de permisos.
- **`GetCuentasAccesiblesAsync`** no se ha probado contra PostgreSQL real (no
  hay test de PostgreSQL de `ConciliacionService`); la forma de la SQL es la
  que ya tenia `ResolveAccessibleCuentas`.
- **El alcance de extractos pasa a salir de la cache** de `UserAccessService`
  (TTL 45 s en produccion): un cambio de permisos tarda hasta ese TTL en
  reflejarse en los extractos.
- **Para ADMIN**, `CanAccessCuentaAsync` no comprueba que la cuenta exista, de
  modo que el administrador puede auditar y ver el desglose de extractos de
  cuentas en la papelera.
- **`ImportacionService.GetContextoAsync`** mantiene su propia query de
  "cuentas importables", que es la unica duplicacion que queda de las reglas
  de alcance.
- **El desplegable de cuentas de la pantalla de Conciliacion** se alimenta de
  `/importacion/contexto`, que exige `puede_importar`: quien solo puede
  conciliar lo ve vacio (desajuste preexistente del backend, fuera del lote).
- **`/importacion/contexto` no devuelve `titular_id`**, asi que el boton de
  revertir y el de conciliar resuelven el alcance con la cuenta y el pais
  solamente; una fila de permisos acotada a un titular (y no a la cuenta) no
  habilita la accion aunque el backend si lo permitiria.
- **Los endpoints sin llamada de la seccion 8.1 no se han retirado** (no hay
  cliente externo en el repositorio, pero la decision no esta confirmada).

**Verificacion global del lote:** backend `AtlasBalance.API.Tests` **1292/1292**
(suite completa, incluida la de PostgreSQL real con Testcontainers, 4 m 51 s) y
`AtlasBalance.Caching.Tests` **15/15**. Frontend: `npx tsc --noEmit` sin
errores, `eslint src tests --max-warnings 0` sin errores, `npm run lint`
(canonico) en verde, **265/265** pruebas unitarias (los 40 ficheros de la lista
canonica de `package.json`) y build de Vite correcto (4,12 s). Capturas
Playwright de las pantallas nuevas: **54 PNG** en `.artifacts/l6-visual` (4
tests pasados). Los comandos canonicos `npm run test:unit` y `npm run build`
siguen rotos por los permisos heredados de `.test-dist/` y `dist/` (otra cuenta
de sandbox, M-10): las mismas comprobaciones pasaron con carpetas de salida
nuevas. Detalle de comandos y numeros en `DOCUMENTACION_CAMBIOS.md` y
`LOG_ERRORES_INCIDENCIAS.md`.

## 20. LOTE 8 - Correccion aplicada (2026-11-18)

Alcance: los IDs del LOTE 8 que se indican en la tabla de esta seccion. Ningun
otro ID tocado. Rama `V-03.02`. Sin commit ni push y sin cambio de version.

Decisiones del usuario antes de implementar (cuestionario respondido de forma
explicita, no por auto-seleccion de tiempo):

- **M-1:** `TendenciasAnomaliasService` **se borra** con sus tests. No estaba
  registrado en DI, asi que la aplicacion no podia alcanzarlo; la pregunta era si
  se borraba o se conectaba mas adelante y la respuesta fue borrarlo.
- **M-15:** los lanzadores se unifican **en espanol** (`Instalar Atlas Balance.cmd`
  y `Actualizar Atlas Balance.cmd` pasan a ser los canonicos y los ingleses
  quedan como alias finos que delegan). **No se borra ningun lanzador ni script.**
  `start.cmd` y `Atlas Balance.cmd` no se fusionan: resuelven la ruta de
  instalacion de forma distinta y unirlos degradaria uno de los dos casos.
- **M-17:** los informes anteriores a V-03 se **archivan** (se mueven a
  `Documentacion/Archivo/`, no se borran) y `DOCUMENTACION_CAMBIOS.md` se parte
  en dos ficheros. **M-16 (los dos `.gitignore`) no se aplica**: el encargo pedia
  solo proponerlo, y su propuesta queda escrita en la seccion 8.2.
- **T-1:** los e2e de Playwright se ejecutan **solo en push a `main` y en el
  workflow de release**, no en cada push ni en cada PR: instalan navegadores en
  CI y anaden minutos.

| ID | Estado | Que se cambio | Evidencia |
|---|---|---|---|
| D-1 | **Corregido** | `formatPercent` con `Intl.NumberFormat('es-ES')`: coma decimal, signo menos real, separador del locale y cero sin «+». 9 `toFixed` de porcentajes fuera. | `tests/formatPercent.test.ts` (6 tests) + suite completa 332/332. |
| D-2 | **Corregido** | Tildes y formulas unificadas en Paises, Configuracion, Alertas e Importacion; 31 errores genericos convertidos en accionables. | `tests/copyYColoresConfiguracion.test.ts` (9 tests). |
| D-3 | **Corregido** | `PageSkeleton` como componente unico de carga, con `role="status"`, esqueleto de tabla real y modo inline. 28 `<p>Cargando…</p>` fuera. | `tests/estadoCargaUnico.test.ts` (4 tests). |
| D-4 | **Corregido** | Todas las media queries de anchura de `FE/styles` quedan en 767.98 / 1023.98 / 1199.98 (incluidos los que se quedaron fuera al repartirse el CSS: `tabs.css`, `routes/auditoria.css` y `shell.css`, con el bloque de 360px integrado en 767.98px). | Arnes visual ampliado con la anchura de tablet (1024 x 768) + comparacion pixel a pixel. |
| D-5 | **Corregido** | `system-coherence.css` **borrado** (428 lineas repartidas). Nacen `.page`/`.page-header` y se anaden a 19 pantallas y 21 cabeceras sin quitar las clases propias. 2.062 reglas CSS byte a byte identicas; resolutor de cascada con 0 diferencias de valor. Un `<div>` que reutilizaba `.auditoria-header` perdia 12 px de padding: detectado en la validacion visual y corregido. | Comparador de triplets (media, selector, declaraciones) + resolutor por elemento (141 comprobaciones) + A/B de capturas. |
| D-6 | **Corregido** | Tres hojas de componentes (`modal`, `pagination`, `tabs`), `.import-muted` a la capa global y las 11 clases `phase2-*` renombradas (0 quedan). | Comparacion de reglas CSS sin diferencias atribuibles + capturas. |
| D-6 | **Corregido** | Tres hojas nuevas de componentes (`modal.css`, `pagination.css`, `tabs.css`), `.import-muted` a la capa global y las 11 clases `phase2-*` renombradas a `entity-*` y `modal-form*` (0 coincidencias de `phase2`). | Comparacion de reglas CSS (mismas declaraciones) + capturas: ninguna diferencia atribuible al CSS. |
| D-7 | **Corregido** | 265 tokens sin uso borrados (493 -> 228 nombres). El bloque `[data-theme="dark"]` de `variables.css` **resulta ser imprescindible** (gana por orden de fuente) y solo se retira `--text-link`, que si era redundante. | 62 valores computados (31 tokens x 2 temas) identicos antes y despues, medidos en Chromium. |
| D-8 | **Corregido** | El color del grafico se valida, no se guarda nada invalido y vuelve a seguir al tema: `utils/chartColors.ts` resuelve el hexadecimal de un token a `var(--chart-N)`. | `tests/chartColors.test.ts` (6 tests). |
| D-9 | **Corregido** | Los 10 modales montados a mano de 8 ficheros pasan a `<Modal>`, que gana variantes para no perder el aspecto legacy. | `tests/modalesUnicos.test.ts` (7 tests) + capturas de los modales. |
| D-10 | **Corregido** | 22 `<caption className="sr-only">` (de 2 a 24 tablas con nombre accesible), los 3 saltos `h3` -> `h2` de ImportacionPage y los 19 componentes compartidos que colgaban del `h1` (`EmptyState` y `KpiCard` aceptan `headingLevel="h2"`, con el CSS conservando el tamano). La premisa de los «23 `<th>` sin `scope`» estaba obsoleta. | `tests/semanticaTablas.test.ts` (4 tests) y `tests/nivelesEncabezado.test.ts` (2 tests). |
| D-11 | **Corregido** | 29 usos de `showSign` hacen explicito el «+» del positivo; las magnitudes con etiqueta propia y los negativos no se tocan. La guarda `sinSignoPrevio` pasa a `utils/formatters.ts` y se aplica tambien en Auditoria. | `tests/importesConSigno.test.ts` (30 tests). |
| D-12 | **Corregido** | `--text-16/20/32` (alias muertos) fuera; el 600 y el 700 tienen ya un solo nombre (`--weight-semibold` y `--weight-bold`). | Verificacion por grep de los 54 usos renombrados. |
| M-1 | **Corregido** | `TendenciasAnomaliasService` (237 lineas) y su fichero de tests borrados: sin referencias ni registro en DI. | Suite del backend: **1282/1282** (linea base previa: 1292/1292; la diferencia son los 10 tests borrados). |
| M-2 | **Corregido** | `AppendPeriodSummary_ObsoleteKeptForBuild` y la sobrecarga de 6 parametros de `AppendPeriodSummaryAsync` borrados; los 5 `await` restantes usan la de 4. | `dotnet test` 1282/1282. |
| M-6 | **Corregido** | 50 clases CSS sin uso borradas (la cifra del informe era una estimacion), con las de construccion dinamica conservadas. | Busqueda de token exacto en `src/`, `tests/`, `e2e/` y el resto de hojas. |
| M-10 | **Corregido** | La premisa estaba a medias (`.test-dist-v2/` ya estaba cubierto); faltaba `frontend/playwright-report/`, anadido al `.gitignore` y a los `ignores` de eslint, mas `pw-*/` y `.l8-*` para las capturas. | `git check-ignore -v`. |
| M-11 | **Corregido** | `design-tokens.css` y la carpeta anidada `Documentacion/Diseno/Diseño/` **archivados** (movidos, no borrados) en `Documentacion/Diseno/Archivo/`. | Indice `README.md` del archivo. |
| M-12 | **Corregido** | `ICacheMetricsSink` borrada; `CacheMetrics` y `CacheMetricsSnapshot` se conservan porque `CacheService` las usa. | `dotnet test` 1282/1282. |
| M-13 | **Corregido** | Exports sin uso revisados uno a uno: los «solo interno» pierden el `export` y los «muertos» se borran. Aviso: al quitar el `export` de `errorToast.ts` se caia el `import type` que sostenia la ampliacion `declare module 'axios'`; se resuelve con `import type {} from 'axios'`. | `tsc --noEmit` y `tsc -p tsconfig.test.v2.json --noEmit` en verde; suite 332/332. |
| M-14 | **Corregido** | `README_RELEASE.md` pasa a `V-03.02` y `Check-VersionAlignment.ps1` lo verifica; `scripts/install.ps1` lee la version del fichero `VERSION`. | `Check-VersionAlignment.ps1` exit 0 y prueba negativa con `-ExpectedVersion V-03.03` (exit 1). |
| M-15 | **Corregido** | `install.cmd` y `update.cmd` pasan a ser alias finos de los lanzadores en espanol. Los tres pares de `.ps1` ya eran alias y los tres `Start-*Dev.ps1` no son duplicados. | Rutas de los lanzadores comprobadas y cadena de empaquetado en `Build-Release.ps1` verificada. |
| M-17 | **Corregido** | 18 informes anteriores a V-03 archivados con indice; `DOCUMENTACION_CAMBIOS.md` de **23.565 a 614 lineas** (solo V-03.02) y el historico de V-03.01 y anteriores en `DOCUMENTACION_CAMBIOS_ARCHIVO.md` (23.030 lineas). | 18 referencias de ruta actualizadas; ninguna entrada de bitacora perdida. |
| T-1 | **Corregido** | Job `e2e` en `ci.yml` **solo en push a `main`** y e2e visuales tambien en `release.yml`; `admin-smoke.spec.ts` excluido de forma explicita. Ademas se corrigieron los **fixtures obsoletos** del mock que hacian fallar los tres specs (5 tests). | 29 pasan y 1 queda saltado; YAML validado con `yaml.safe_load`. |
| T-2 | **Corregido** | Los cinco `*.Tests.ps1` que no corrian se anaden a `policy-gates`: tres en la matriz completa y dos solo en `windows-2022` (requisitos de ACLs y `$env:TEMP`), con el motivo en la cabecera de cada script. | Ejecucion local de cada script y validacion del YAML. |
| R-9 | **Corregido, con matices** | El CSS de cada ruta se carga desde su modulo lazy (19 hojas en `src/styles/layout/routes/`). El CSS inicial baja de **228.149 a 163.965 B** (-28,1 % raw, -23,3 % gzip) pero el total del bundle **sube 9 KB gzip** (20 ficheros comprimen peor que 1): R-9 reparte el CSS, no lo reduce. 41 reglas se quedaron globales porque al moverse invertian el orden de la cascada. | Resolutor de cascada propio (25 unidades x 32 contextos, 78.869.024 comprobaciones) con **0 diferencias**, mas comparacion A/B de capturas sobre el mismo arbol: solo difieren las marcas de tiempo del mock. |

**Verificacion global del lote:** backend `AtlasBalance.API.Tests` **1282/1282**
(incluida la suite de PostgreSQL real con Testcontainers, 6 m 45 s) y
`AtlasBalance.Caching.Tests` **15/15**. Frontend: `npx tsc --noEmit` sin errores,
`eslint src tests --max-warnings 0` sin errores, **332/332** pruebas unitarias y
build de Vite correcto. El arnes visual de Playwright se amplio de 9 a **21 rutas**
y de 2 a **3 anchuras**: 1440 (escritorio), **1024 (tablet)** y 390 (movil), en
claro y oscuro, con la API simulada (6/6 pruebas del arnes en verde, 134
capturas por pasada) y sirve de comparacion pixel a pixel: el diff entre dos
ejecuciones del **mismo** codigo es de ~40 imagenes (el mock genera marcas de
tiempo con `Date.now()`), de modo que el criterio valido es «ninguna diferencia
atribuible al CSS», no «cero diferencias». Sin sesion autenticada real ni backend
en contra.

Los comandos canonicos `npm run test:unit` y `npm run build` **siguen en rojo por
el bloqueo de permisos de `frontend/.test-dist/` y `frontend/dist/`**, que es un
problema **historico y recurrente de este entorno**, no de este lote: el log de
incidencias lo recoge desde el 2026-06-23 y lo repiten los lotes 1, 2, 3, 5, 6 y
7 con la misma salida. La causa esta investigada al detalle (la carpeta admite
ficheros nuevos, pero los preexistentes no se pueden sobrescribir, renombrar ni
borrar; `icacls /grant` y `Move-Item` responden «Acceso denegado»; y
`Build-Release.ps1` no depende de `frontend/dist`, solo la sincronizacion local
manual documentada en `AGENTS.md` §8). No se cambia la carpeta de salida
canonica del repositorio por un problema de permisos de esta maquina: la
verificacion equivalente se hace con `VITE_BUILD_OUT_DIR` y con `--outDir` +
`--tsBuildInfoFile`, que es lo que recomienda el propio `AGENTS.md` §8. Detalle en
`DOCUMENTACION_CAMBIOS.md`, `DOCUMENTACION_TECNICA.md` y
`LOG_ERRORES_INCIDENCIAS.md`. Detalle en `DOCUMENTACION_CAMBIOS.md`,
`DOCUMENTACION_TECNICA.md` y `LOG_ERRORES_INCIDENCIAS.md`.

**Observaciones que quedan abiertas y no se han tocado:**

- **D-5** (clases `.page`/`.page-header` y cierre de `system-coherence.css`) y
  **R-9** (cargar el CSS por ruta) estan **corregidos**, con dos matices que
  conviene no pasar por alto: el reparto de R-9 **no es reproducible con una
  herramienta en el repo** (el generador era temporal) y **41 reglas se
  quedaron globales** porque al moverse invertian el orden de la cascada. El
  caso acumulado (varias rutas visitadas en la misma sesion, donde los chunks
  ya visitados se aplican despues de las hojas globales) solo lo cubre el
  arnes visual, no una comprobacion estatica. Ademas, R-9 **reduce el CSS
  inicial pero no el total**: 20 ficheros comprimen peor que 1.
- El overlay de restauracion de `BackupsPage` paso de `role="alertdialog"` a
  `role="dialog"` (lo impone el componente comun) y el `aria-busy` salio de la
  region viva: sin el, el progreso de la restauracion se anuncia; con el, el
  anuncio se diferiria indefinidamente. Recuperar el `alertdialog` exigiria un
  modal propio.
- El esqueleto de tabla usa 6 columnas por defecto y no cuadra con el numero real
  de columnas en `ExtractoTable` y `RevisionPage`.
- El nombre `.users-pagination` sigue mintiendo (lo usan 14 paginas): su
  renombrado a `.pagination` es un paso aparte y reversible, no aplicado.
- `DESIGN.md` se ha puesto al dia con los tokens que existen de verdad y lleva
  una nota de vigencia que remite a `src/styles/` como fuente de verdad.
