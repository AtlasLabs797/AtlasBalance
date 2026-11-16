# Informe de revisión integral — Atlas Balance V-03.02

Fecha: 2026-11-16 · Rama: `V-03.02` (commit `4b3bf58`) · Solo informe: no se ha cambiado código.

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
| A-2 | **Bloqueo de cuenta indefinido.** `FailedLoginAttempts` solo vuelve a 0 con un login correcto. Pasados los 30 min el contador sigue en 5, así que un único fallo vuelve a bloquear. Durante el bloqueo se rechazan también las sesiones ya abiertas, y no existe desbloqueo para ADMIN. Quien conozca un email deja fuera a ese usuario con una petición cada 30 min. | `API/Services/AuthService.cs:182-214`, `:260`; `API/Middleware/UserStateMiddleware.cs:71` | Poner el contador a 0 cuando caduque el bloqueo; no invalidar sesiones activas por bloqueo; acción «Desbloquear» para ADMIN. | V |
| A-3 | **Un backup en `PENDING` detiene los automáticos para siempre.** Si el servicio se para a mitad de un backup, el `SaveChanges` del `catch` usa el token ya cancelado y la fila queda `PENDING`. El planificador omite el backup mientras exista alguna, y nada las recupera. No hay alerta de «sin backup desde hace N días». | `API/Services/BackupService.cs:117-123`; `API/Jobs/BackupSchedulerJob.cs:43-49` | `CancellationToken.None` en el `catch`; pasar a `FAILED` los `PENDING` con más de 2 h; alerta por antigüedad del último backup correcto. | V |
| A-4 | **Los backups automáticos del mismo día comparten fichero.** `AUTO` usa `backup_yyyy-MM-dd.dump` y la frecuencia admite cada 1-168 h. Un segundo `pg_dump` que falle a medias destruye el dump bueno mientras su fila sigue en `SUCCESS`. | `API/Services/BackupService.cs:50-53`; `API/Services/BackupConfigurationService.cs:293,341-344` | Incluir la hora en el nombre, o escribir a `.tmp` y renombrar solo si termina bien. | V |
| A-5 | **El planificador semántico de IA llama a OpenRouter sin las restricciones de privacidad.** La llamada principal añade `zdr: true` y `data_collection: "deny"`; la del planificador no. Contradice lo que afirma el inventario de datos personales. | `API/Services/IaPlanner/SemanticPlannerClient.cs:84-95` frente a `API/Services/AtlasAiService.cs:732-735` | Un único constructor de petición para ambas llamadas. | V |
| A-6 | **La IA ignora el filtro por concepto en los totales.** El planificador acepta `filtros.concepto`, pero totales, ranking, tendencia y último movimiento no lo aplican (tampoco importe mínimo o máximo). «¿Cuánto he gastado en seguros?» puede devolver el total de todos los gastos, sin aviso. | `API/Services/IaPlanner/FinancialToolsService.cs:294-384`, `:482-554`, `:636-680` (solo `Search`, `:779`, filtra) | Aplicar los filtros en todas las herramientas; hasta entonces, rechazar el plan cuando traiga concepto y la operación no sea búsqueda. | V |
| A-7 | **Validar una importación muestra un error falso.** `invalidate('catalogo' as never)`: la clave no existe en `mutationInvalidation`, se lanza `TypeError` y el `catch` muestra «No se pudo validar la importación» cuando la validación fue bien. | `FE/pages/ImportacionPage.tsx:529`; `FE/queries/invalidation.ts` | Usar una clave real y quitar el `as never`. | V - **CORREGIDO** (LOTE 1, 2026-11-16) |
| A-8 | **Configuración pierde lo escrito sin guardar.** Cualquier acción menor (tipo de cambio, sincronizar, divisas, tokens) llama a `load()`, que pone la página en skeleton y recarga todo el estado. | `FE/pages/ConfiguracionPage.tsx:554,569,623,772` | Recargar solo la sección afectada. | V - **CORREGIDO** (LOTE 1, 2026-11-16) |
| A-9 | **El job de vencimientos de plazo fijo falla en PostgreSQL** (ya conocido, sigue abierto). `Contains` sobre una columna `jsonb` genera `jsonb ~~ jsonb` (42883). Como el cambio de estado ya se guardó antes, al reintentar el aviso de «próximo a vencer» se pierde para siempre. | `API/Services/PlazoFijoService.cs:196-203`, `:48-62` | `EF.Functions.JsonContains` con test contra PostgreSQL real; no usar el cambio de estado como disparador. | V |

## 4. Seguridad

### 4.1 Permisos y fuga de datos

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| S-1 | Media | El dashboard da saldos a quien solo tiene permisos de escritura (`PuedeImportar`, `PuedeAgregarLineas`) sin `PuedeVerCuentas`; extractos y cuentas sí lo exigen. Para GERENTE se ignora `PuedeVerDashboard`. | `API/Services/DashboardService.cs:887-917` frente a `API/Services/UserAccessService.cs:200-206` | V |
| S-2 | Media | Las columnas que el administrador oculta a un usuario no se filtran en el servidor (`GET /api/extractos` devuelve todas) y el propio usuario puede sobrescribir esa preferencia con `PUT /api/extractos/columnas-visibles`. | `API/Controllers/ExtractosController.cs:766-795`; `API/Controllers/UsuariosController.cs:1228-1244` | V |
| S-3 | Media | Revertir un lote es un borrado masivo que no exige `PuedeEliminarLineas`: basta `PuedeImportar`. | `API/Services/ImportacionService.cs:643`, `:1135` | V |
| S-4 | Media | `GET /api/ia/config` devuelve presupuesto y gasto global de IA a cualquier usuario autenticado; `/modelos` sí comprueba el permiso. | `API/Controllers/IaController.cs:35-45`; `API/Services/AtlasAiService.cs:95-106` | V |
| S-5 | Media | `UpdateUsuarioRequest` tiene valores por defecto destructivos: un `PUT` con cuerpo parcial desactiva al usuario, lo baja a EMPLEADO y le borra permisos y emails. | `API/DTOs/UsuariosDtos.cs:104-115` | V |
| S-6 | Media | Las respuestas locales del planificador de IA no se auditan ni cuentan contra los límites: un usuario puede extraer saldos por el chat sin rastro. | `API/Services/AtlasAiService.cs:1014-1070` | S |
| S-7 | Baja | La exportación CSV de auditoría (hasta 50.000 filas) no deja rastro de quién la extrajo. | `API/Controllers/AuditoriaController.cs:201-282` | ? |
| S-8 | Baja | Respuestas 404 antes que 403 en varios endpoints (oráculo de existencia; ids GUID). | `ExtractosController`, `CuentasController.cs:535-544` | S |
| S-9 | Info | Tres métodos de permisos sin uso en `UserAccessService` cuyas reglas difieren de las que aplican de verdad Conciliación e Importación. Un cambio de política toca tres sitios. | `API/Services/UserAccessService.cs:311-336` | V |

### 4.2 Autenticación y sesión

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| S-10 | Media | **El alta de MFA se puede sobrescribir.** La rama `SetupRequired` guarda el secreto sin comprobar que el usuario siga sin MFA, y completar un alta no rota el `SecurityStamp`. Quien conozca la contraseña inicial y tenga un desafío abierto (5 min) puede fijar su propio autenticador después de la víctima. | `API/Services/AuthService.cs:437-450` | V |
| S-11 | Media | Carrera en el límite de login: el throttle se consulta antes de bcrypt y el contador se incrementa después, sin concurrencia. Una ráfaga paralela evalúa más contraseñas de las permitidas y el contador no llega al bloqueo. | `API/Services/AuthService.cs:131`, `:205-216` | S |
| S-12 | Media | La API guarda en su configuración la credencial de propietario de la BD (`MigrationConnection`). El `REVOKE` sobre auditoría solo protege frente a SQLi, no frente a un compromiso del proceso; el comentario de `Program.cs` lo sobreestima. | `API/Program.cs:1125-1149`, `:1282-1292` | S |
| S-13 | Baja | Sin vida máxima de sesión: cada rotación reinicia 7 días y hereda la verificación MFA original. | `API/Services/AuthService.cs:608-619` | S |
| S-14 | Baja | Cookies legacy sin `__Host-` aceptadas en producción (ya conocido). | `API/Program.cs:202-203`; `API/Middleware/CsrfMiddleware.cs:67-69` | S |
| S-15 | Baja | Dos pestañas que refrescan a la vez: la segunda se trata como reutilización y cierra todas las sesiones (ya conocido). | `API/Services/AuthService.cs:524-535` | S |
| S-16 | Baja | Un cliente anónimo llena `AUDITORIAS` con 401 sobre rutas únicas (la deduplicación incluye la ruta). | `API/Middleware/SecurityAuditMiddleware.cs:136-138` | S |
| S-17 | Baja | SMTP sin credenciales usa `StartTlsWhenAvailable`: las alertas con saldos pueden ir en claro. | `API/Services/EmailService.cs:315-319` | S |
| S-18 | Baja | Respuestas de `/api/*` sin `Cache-Control: no-store`. | `API/Program.cs:640-687` | S |
| S-19 | Baja | El cifrado de backups autentica cada bloque pero no el número de bloques: un `.enc` truncado descifra sin error. | `API/Services/BackupEncryptionService.cs:146-171` | S |
| S-20 | ? | Rutas comparadas por cadena exacta: `/api/auth/login/` podría caer en el cubo anónimo de 60/min en vez del de 10/min. | `API/RateLimiting/RateLimitingSetup.cs` | ? |
| S-21 | ? | La firma de actualización cubre el ZIP, no la versión: quien controle la release podría servir una versión firmada anterior. | `API/Services/ActualizacionService.cs:87` | ? |

### 4.3 Integración OpenClaw y tokens

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| S-22 | Media | El límite de 100 req/min se aplica después del chequeo de scope, y cada petición rechazada escribe una notificación de admin y una fila de auditoría. | `API/Middleware/IntegrationAuthMiddleware.cs:142-172` | S |
| S-23 | Baja | Un `PUT` que solo cambia el nombre de un token renueva su caducidad a 90 días; «Rotar» resucita un token revocado; la fecha de expiración no tiene tope. | `API/Controllers/IntegracionesController.cs:246-252`, `:306-341` | S |
| S-24 | Info | Scopes por defecto de más: `vencimientos` no tiene endpoint, `auditoria` devuelve valores anterior y nuevo, y el permiso de escritura no hace nada. | `API/Controllers/IntegracionesController.cs:21-30` | S |
| S-25 | Baja | Telemetría anónima sin `MaxLength`, escribe a nivel `Error`. | `API/Controllers/TelemetriaController.cs:14` | S |

## 5. Bugs funcionales

### 5.1 Backend

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| B-1 | Media | Totales en divisa: una cuenta sin tipo de cambio aporta 0 y la respuesta no lo marca. Igual en `total_convertido` de OpenClaw. | `API/Services/DashboardService.cs:560-564`; `API/Controllers/IntegrationOpenClawController.cs:190-193` | V |
| B-2 | Media | La sincronización de tipos de cambio pisa las tasas manuales cada 12 h. Un fallo de sincronización solo queda en un `Warning`. | `API/Services/TiposCambioService.cs:443-468` | V |
| B-3 | Media | Tras revertir un lote no se puede reimportar: la deduplicación cuenta las filas borradas y el índice único de huella no filtra `deleted_at`. | `API/Services/ImportacionService.cs:787-794`; `API/Data/AppDbContext.cs:221-224` | V |
| B-4 | Media | `RevertirLote` no es atómico: borra extractos fuera de transacción y después guarda el lote. No valida el estado ni invalida conciliaciones. | `API/Services/ImportacionService.cs:637-688` | S |
| B-5 | Media | Conciliación: el score no impone el tope de importe por movimiento. Un esperado de 50 € puede sugerirse contra un extracto de 900 € si otro movimiento del lote es grande. | `API/Services/ConciliacionService.cs:446-456` | V |
| B-6 | Media | Conciliación: un mismo extracto puede conciliarse dos veces y `SetEstado` acepta cualquier transición. `FirstAsync` da 500 si el movimiento ya no existe. | `API/Services/ConciliacionService.cs:212-280`, `:325-349` | S |
| B-7 | Media | `PUT /api/extractos/{id}` devuelve 500 si falla la evaluación de alertas, con el cambio ya guardado. `Crear` sí lo protege. | `API/Controllers/ExtractosController.cs:383-385` | V |
| B-8 | Media | Alerta de saldo: `pg_advisory_xact_lock` sin transacción explícita se libera al instante; dos importaciones simultáneas envían el email doble. | `API/Services/AlertaService.cs:98-105` | V |
| B-9 | Media | El saldo anterior de un movimiento de plazo fijo se calcula incluyendo filas borradas. | `API/Services/ImportacionService.cs:988-994` | S |
| B-10 | Media | IA: agregados que suman o comparan divisas distintas; «duplicado probable» marca pagos recurrentes legítimos; los seguimientos («¿y el mes anterior?») pierden los filtros. | `API/Services/IaPlanner/FinancialToolsService.cs:948-973`, `:1100-1112` | S |
| B-11 | Media | IA: el timeout del planificador escapa como 500 y la llamada facturada no se contabiliza. Los contadores de coste se actualizan sin concurrencia. | `API/Services/IaPlanner/IntentPlanner.cs:293`; `API/Services/AtlasAiService.cs:2147-2158` | V (catch) / S |
| B-12 | Baja | La primera prueba SMTP de cada usuario devuelve siempre 429. | `API/Services/SmtpTestRateLimit.cs:25-29` | V - **CORREGIDO** (LOTE 1, 2026-11-16) |
| B-13 | Baja | El importe «0,500» se interpreta como 500 (backend y frontend). | `API/Services/ImportacionService.cs:2239-2253`; `FE/utils/formatters.ts:30-100` | S |
| B-14 | Baja | Cambiar la divisa de una cuenta con extractos no se bloquea. | `API/Controllers/CuentasController.cs:454` | S |
| B-15 | Baja | Paginación sin desempate (`ThenBy Id`): filas duplicadas u omitidas entre páginas al ordenar por importe o concepto. | `API/Controllers/ExtractosController.cs:104-120`; `AuditoriaController.cs:119` | S |
| B-16 | Baja | «Hoy» en UTC: entre las 00:00 y las 02:00 de España el mes o el día actual es el anterior. El año `2026` está escrito a mano en el detector de resumen anual de la IA. | `API/Services/AtlasAiService.cs:1811`; `FE/components/extractos/ExtractoTable.tsx:230` | S |
| B-17 | ? | El saldo actual es el de la fila con mayor `FilaNumero`. Si un banco exporta en orden ascendente, el saldo «actual» sería el más antiguo. No hay validación de continuidad de saldos. | `API/Services/ImportacionService.cs:771-772`; `API/Services/DashboardService.cs:512-521` | ? |

### 5.2 Jobs

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| J-1 | Media | Ningún job tiene `DisableConcurrentExecution` ni `AutomaticRetry`, con 2 workers. Un backup largo más una exportación retrasan las alertas de seguridad, que además evalúan una ventana fija y dejan huecos. | `API/Program.cs:333`; `API/Services/SecurityAlertService.cs:85-90` | V (atributos) / S |
| J-2 | Media | La exportación mensual traga todos los errores con un `catch` vacío y sin log. | `API/Services/ExportacionService.cs:228-240` | V |
| J-3 | Media | Un aviso de «próximo a vencer» cuyo email falla no se reintenta; solo un plazo por grupo recibe notificación de admin. | `API/Services/PlazoFijoService.cs:58-90` | S |
| J-4 | Baja | Limpieza de exportaciones: si falla el borrado del fichero, la fila se marca borrada igualmente y el fichero queda en disco. | `API/Jobs/LimpiezaExportacionesJob.cs:59-72` | S |
| J-5 | Baja | La limpieza de refresh tokens no ve los de usuarios borrados. | `API/Jobs/LimpiezaRefreshTokensJob.cs:22-24` | S |
| J-6 | Baja | La auto-actualización puede lanzarse en horario laboral si el servidor estaba caído a la hora configurada. | `API/Jobs/AutoUpdateJob.cs:47-57` | S |

### 5.3 Frontend

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| F-1 | Media | Cualquier error al renovar el token (red, 5xx, 429) cierra la sesión y lo propaga a las demás pestañas. | `FE/services/api.ts:209-214` | V |
| F-2 | Media | La papelera pide 500 filas y filtra en cliente: los eliminados más antiguos no aparecen ni se pueden restaurar. Muestra todos los importes en EUR. | `FE/pages/PapeleraPage.tsx:93-141`, `:228` | S |
| F-3 | Media | El backend omite los `null` (`WhenWritingNull`) y el frontend compara con `!== null`: en auditoría se pinta un importe vacío. Mismo tipo de fallo que el de permisos de octubre. | `FE/pages/AuditoriaPage.tsx:375,383`; `FE/components/extractos/AuditCellModal.tsx:50,57` | S - **CORREGIDO** (LOTE 1, 2026-11-16) |
| F-4 | Media | El interceptor lanza un toast por cada error, también en los sondeos con la API caída a propósito (actualización, backup): un toast cada 2,5 s. Se suma a los 82 errores que ya pintan las páginas. | `FE/services/api.ts:172-186` | S |
| F-5 | Media | El banner de alertas reaparece al cambiar de país; dos escritores sobre el mismo store. | `FE/hooks/queries/useAlertasActivasQuery.ts:37` | S |
| F-6 | Media | Salir de la nota de alerta lanza un PATCH aunque no cambie nada. | `FE/components/extractos/ExtractoTable.tsx:828` | S |
| F-7 | Baja | Borrado y marcado en lote con una petición por fila, sin transacción. | `FE/pages/CuentaDetailPage.tsx:458,601` | S |
| F-8 | Baja | Los campos numéricos de Configuración no se pueden vaciar (`Number(x) \|\| 0`). | `FE/pages/ConfiguracionPage.tsx:908,1023` | S |
| F-9 | Baja | Si falla el guardado de una celda, se descarta lo escrito. | `FE/components/extractos/EditableCell.tsx:53-54` | S |
| F-10 | ? | La clave de idempotencia de «Confirmar importación» no se regenera al abrir otro lote desde el historial. | `FE/pages/ImportacionPage.tsx:202,517,856` | ? |

## 6. Rendimiento

| ID | Sev. | Qué pasa | Dónde | Estado |
|---|---|---|---|---|
| R-1 | Media | Exportación mensual: carga todos los extractos sin `AsNoTracking` y reutiliza el `DbContext` entre cuentas. `AdjustToContents` sobre 50.000 filas. | `API/Services/ExportacionService.cs:93-96`, `:163` | S |
| R-2 | Media | Chat de IA: entre 12 y 20 consultas y dos llamadas facturables por pregunta; el mapa de seudónimos se construye dos veces. | `API/Services/AtlasAiService.cs:1685-1713`, `:2106` | S |
| R-3 | Media | `GET lotes/{id}/filas` devuelve hasta 50.000 filas sin paginar. | `API/Controllers/ImportacionController.cs:113-130` | S |
| R-4 | Media | `CuentaDetailPage` invalida las mismas consultas que tiene activas: petición duplicada en cada cambio de página o periodo. | `FE/pages/CuentaDetailPage.tsx:337` | S |
| R-5 | Media | `ExtractoTable`: unas 1.600 evaluaciones de permisos por render; cada foco re-renderiza la tabla. | `FE/components/extractos/ExtractoTable.tsx:602` | S |
| R-6 | Media | Búsquedas sin debounce (una petición por tecla) en Formatos, Países, Cuentas, Usuarios y en el campo «Modelo» de IA. | `FE/pages/FormatosImportacionPage.tsx:164`; `FE/pages/ConfiguracionPage.tsx:361` | S |
| R-7 | Baja | Importación de 50.000 filas en un único `SaveChanges`; N+1 en resúmenes de titulares y en la evolución de OpenClaw. | `API/Services/ImportacionService.cs:332-344`; `ExtractosController.cs:722-744` | S |
| R-8 | Baja | `/ia/config` se consulta cada 30 s en todas las pestañas, con tres fuentes de verdad. | `FE/hooks/queries/useIaConfigQuery.ts:20` | S |
| R-9 | Baja | Todo el CSS (13.010 líneas) se carga en el arranque aunque las rutas sean `lazy`. | `FE/main.tsx:10-13` | S |
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
| D-1 | Media | Porcentajes con `toFixed`: «12.3%» con punto junto a «1.234,56 €», «+0.0%» para el cero y guion ASCII en negativos. | `FE/pages/DashboardPage.tsx:281-398`; `FE/pages/DashboardTitularPage.tsx:213-250` | V |
| D-2 | Media | Textos sin tilde y desiguales: «Nuevo pais», «Cargando paises...», «podran iniciar sesion», «Anadir dinero». Errores genéricos sin causa ni siguiente paso. | `FE/pages/configuracion/PaisesPage.tsx:241,289`; `FE/pages/ConfiguracionPage.tsx:461`; `FE/pages/ImportacionPage.tsx:906` | V |
| D-3 | Media | Estados de carga dispares: 8 pantallas con skeleton y unas 14 con un `<p>Cargando…</p>` sin `role="status"`. | Alertas, Auditoría, Backups, Cuentas, Titulares, Exportaciones, Formatos, Países, Papelera | S |
| D-4 | Media | Breakpoints fuera de la regla 768 / 1200: entre 721 y 900 px el shell y las páginas están en modos distintos. | `FE/styles` (720, 760, 860, 900, 1180, 1280…) | V |
| D-5 | Media | `system-coherence.css` es una capa de parches que gana por orden de carga: 15 prefijos de página repiten el mismo patrón de cabecera. | `FE/styles/layout/system-coherence.css:1-440` | S |
| D-6 | Media | Estilos globales dentro de hojas de página: el modal vive en `extractos.css`, la paginación en `users.css`, `.import-muted` se usa en 10 páginas ajenas, quedan clases `phase2-*`. | `FE/styles/layout/extractos.css:789-836` | V (modal) / S |
| D-7 | Media | 239 tokens sin uso (toda la familia `--color-*`) y el tema oscuro definido en dos sitios. | `FE/styles/variables.css:93-134`, `:253-262` | V (muestra) / S |
| D-8 | Media | Los colores del gráfico son texto libre sin validar y no se adaptan al tema. | `FE/pages/ConfiguracionPage.tsx:950-965` | S |
| D-9 | Baja | El «Modal único» está a medias: 5 usos de `<Modal>` y 8 modales montados a mano. | `UsuarioModal`, `CuentasPage`, `TitularesPage`, `PaisesPage`… | S |
| D-10 | Baja | Encabezados que saltan de `h1` a `h3`; 23 `<th>` sin `scope`; 24 tablas y 2 `<caption>`. | `FE/pages/ImportacionPage.tsx:808,1011,1042` | S |
| D-11 | Baja | Los importes positivos se distinguen solo por el color. | `SignedAmount` | S |
| D-12 | Baja | Tokens con nombre engañoso: `--text-16` vale 17 px, `--weight-medium` y `--weight-semibold` valen 600. | `FE/styles/tokens` | S |
| D-13 | Baja | Acciones de conciliación y revisión visibles para quien no tiene permiso: el backend responde 403 con toast. | `FE/stores/permisosStore.ts:111` | S |

Mejoras de diseño de mayor efecto, en orden:

1. Componente único de carga, vacío y error, más un barrido de tildes y puntuación.
2. `formatPercent` en `utils/formatters.ts` con `Intl.NumberFormat('es-ES')`.
3. Clases `.page` y `.page-header` compartidas, y sacar modal, paginación y utilidades a hojas propias. Con eso se cierra `system-coherence.css` y se puede cargar el CSS por ruta.
4. Tres breakpoints (767.98 / 1023.98 / 1199.98).
5. Borrar tokens y clases sin uso.

## 8. Código muerto y mantenibilidad

| ID | Qué | Dónde | Estado |
|---|---|---|---|
| M-1 | `TendenciasAnomaliasService` (237 líneas): solo lo usan sus tests; no está registrado en DI. | `API/Services/IaPlanner/TendenciasAnomaliasService.cs` | V |
| M-2 | `AppendPeriodSummary_ObsoleteKeptForBuild` y una sobrecarga de `AppendPeriodSummaryAsync` sin llamadores. | `API/Services/AtlasAiService.cs:1957-1992` | V (el primero) / S |
| M-3 | `CanApproveImportacionAsync`, `CanConciliarCuentaAsync`, `CanCerrarConciliacionAsync` sin uso. | `API/Services/UserAccessService.cs:311-336` | V |
| M-4 | `AtlasAiService.cs` (3.744 líneas) mezcla orquestación, cliente HTTP, límites, formato, contexto y parseo. El ranking existe tres veces con semánticas distintas. Corte propuesto: cliente de proveedor, gobernanza, constructor de contexto, posprocesado y formateador; el servicio queda como orquestador. Arregla de paso A-5. | `API/Services/AtlasAiService.cs` | S |
| M-5 | `ExtractosController` reimplementa la lógica de alcance de `UserAccessService` (ya conocido). | `API/Controllers/ExtractosController.cs:1045-1118` | S |
| M-6 | Unas 35 clases CSS y 239 tokens sin uso (ver sección 7). `canConciliarCuenta` sin consumidores. | `FE/styles`, `FE/stores/permisosStore.ts:111` | V (muestra) |
| M-7 | Reglas del proyecto que no se cumplen: React Hook Form solo en 2 formularios (el resto usa `useState`); varios archivos con más de un componente; 14 `eslint-disable exhaustive-deps`; 105 errores de las reglas nuevas de react-hooks desactivados. | `FE/pages`, `FE/eslint.config.js` | S |
| M-8 | Duplicados: `parseDecimalInput`, `InsertRowDraft` y `getTodayInputValue` en dos archivos cada uno. Páginas de más de 1.200 líneas: Configuración, CuentaDetail, Importación. | `FE/pages` | S |
| M-9 | El snapshot de EF no tiene `xmin` en `PLAZOS_FIJOS` y `PendingModelChangesWarning` está silenciado (ya conocido). El test dependiente de la fecha ya está corregido en el código (fechas ancladas al día 1 del mes, `FinancialToolsServiceTests.cs:403-411`): falta cerrar la entrada en `REGISTRO_BUGS.md`. | `API/Data/AppDbContext.cs:19` | V (test) / S |
| M-10 | `frontend/.test-dist-v2/` aparece sin seguimiento: `.gitignore` solo cubre `.test-dist/`. Tampoco está ignorada `frontend/playwright-report/`, creada hoy a las 14:42 (origen no identificado: pudo ser un subagente o una sesión anterior). | `.gitignore:33` | V |
| M-11 | `Documentacion/Diseno/design-tokens.css` está marcado como obsoleto pero sigue en el árbol; hay una carpeta `Documentacion/Diseno/Diseño` anidada. | `Documentacion/Diseno` | V |

### 8.1 Endpoints sin llamada desde el frontend

Comprobado con `grep` de la ruta en `FE/` y en `scripts/`. Antes de retirarlos hay que confirmar que no los usa un cliente externo.

| Endpoint | Observación | Estado |
|---|---|---|
| `GET` / `DELETE /api/auth/mfa/trusted-devices` | Sin interfaz: el usuario no puede ver ni revocar sus dispositivos MFA de confianza. Aquí falta la pantalla, no sobra el endpoint. | V |
| `POST /api/importacion/lotes/{id}/revertir` | Sin interfaz. Reduce la exposición práctica de S-3, B-3 y B-4 a quien llame a la API directamente, pero la función no está disponible para el usuario. | V |
| `POST /api/importacion/validar`, `POST /api/importacion/confirmar`, `GET /api/importacion/lotes/{id}/filas` | Flujo anterior a los lotes; el frontend usa `lotes` y `lotes/{id}/confirmar`. | V |
| `GET /api/cuentas/{id}/resumen` | El frontend usa `/extractos/cuentas/{id}/resumen`. | V |
| `GET`/`PUT /api/usuarios/{id}/permisos`, `/permisos/cuenta/{cuentaId}`, `/emails` | El frontend solo llama a `/usuarios/catalogos-permisos` y al `PUT` del usuario completo. | V |
| `GET /api/sistema/version-actual`, `/sistema/metricas`, `GET /api/revision/settings` | Sin referencias en frontend ni scripts. | V |
| `GET`/`PUT /api/integraciones/{id}`, `GET /api/integraciones/{id}/auditoria` | Sin llamada en el frontend. | S |

### 8.2 Otros restos

| ID | Qué | Estado |
|---|---|---|
| M-12 | `ICacheMetricsSink`: interfaz sin implementación ni consumidor (`API/Caching/CacheMetrics.cs:47`). | V |
| M-13 | Unos 20 tipos exportados en `FE/types/index.ts` que nadie importa (`CreateIntegrationTokenRequest`, `PlazoFijo`, `EstadoBackup`…) y exports usados solo dentro de su archivo en `utils/`. Basta quitar el `export` o borrarlos. | S |
| M-14 | `README_RELEASE.md` sigue hablando de `V-02-03` (líneas 1, 5 y 100) y `Check-VersionAlignment.ps1` no lo comprueba. `scripts/install.ps1:34` lleva `V-03.02` escrito a mano. | V |
| M-15 | Lanzadores por pares en español e inglés (`Actualizar Atlas Balance.cmd` y `update.cmd`, `Instalar Atlas Balance.cmd` e `install.cmd`, `Atlas Balance.cmd` y `start.cmd`) y parejas de `.ps1` con función parecida (`install.ps1` e `Instalar-AtlasBalance.ps1`, `update.ps1` y `Actualizar-AtlasBalance.ps1`, tres `Start-*Dev.ps1`). No se comparó su contenido. `start-local-dev.cmd` no se referencia desde ningún sitio. | V (referencias) / ? (duplicación) |
| M-16 | Dos `.gitignore` (raíz y `Atlas Balance/`) con reglas repetidas. | S |
| M-17 | `Documentacion/` acumula unos 17 informes de auditoría antiguos y `DOCUMENTACION_CAMBIOS.md` pasa de 28.000 líneas. Conviene archivar lo anterior a V-03. | V (tamaño) |

No hay `TODO`/`FIXME`, `console.log`, `debugger` ni `Console.Write` olvidados, ningún archivo del frontend sin importar, ninguna dependencia de `package.json` sin uso y ningún binario, log, `.env` o certificado en Git. Las versiones están alineadas en `V-03.02` / `3.2.0`.

Descartado al verificar: el subagente dio por muertos `UpdateExchangeRateConfigRequest` y `UpdateRevisionConfigRequest`; se usan como propiedades de otro DTO (`ConfiguracionDtos.cs:63,65`).

### 8.3 CI y cobertura de tests

| ID | Qué | Estado |
|---|---|---|
| T-1 | Los e2e de Playwright no se ejecutan en ningún workflow: `ci.yml` solo corre `lint`, `test:unit` y `build`. | V |
| T-2 | De los seis `*.Tests.ps1` de `scripts/`, CI solo ejecuta `Test-AtlasSecrets.Tests.ps1`. Quedan fuera los de MFA/TOTP, seguridad del servicio, sincronización de directorio, conservación de secretos del instalador y smoke test. | V |
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

