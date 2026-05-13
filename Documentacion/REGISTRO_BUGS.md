# Registro de bugs

## Abiertos

### 2026-05-10 - V-01.06 - Pendientes altos tras auditoria final

- Contexto: la auditoria general detecto problemas no criticos pero demasiado relevantes para llamar a la app "lista".
- Pendientes:
  - Hacer E2E autenticado contra PostgreSQL real con datos de volumen antes de release.
  - Validacion visual/E2E final de tablas tipo grid con datos reales. Los modales y formularios criticos ya recibieron foco controlado, labels y ARIA en la auditoria UI del 2026-05-12; los casos de overflow y estados feos recibieron hardening el 2026-05-12. El pase `adapt` del 2026-05-12 cubrio responsive/touch/overflow estatico en desktop/tablet/mobile, pero falta sesion real con datos de volumen.
- Estado: abierto. Bloquea recomendar release final.

### 2026-04-20 - V-01.02 - Estado Git local no fiable

- Contexto: `git status --short` funciona, pero lista practicamente todo el arbol como `untracked`.
- Causa probable: copia local/repo recreado sin historial o indice util para esta carpeta.
- Impacto: no se puede obtener diff fino ni preparar commit fiable desde esta copia sin reparar el estado Git.
- Estado: abierto. No se ha tocado `.git` para evitar empeorar el repositorio local.

## Cerrados

### 2026-05-13 - V-01.06 - Cerrado - GitHub Actions fallaba en restore locked

- Contexto: PR #6 fallaba en `dotnet restore "./Atlas Balance/backend/AtlasBalance.sln" --locked-mode`.
- Causa: lockfiles con target `win-x64` y proyectos API/Watchdog sin `RuntimeIdentifiers`; ademas, el restore de solucion es un gate opaco en este repo.
- Solucion: declarar `RuntimeIdentifiers=win-x64` en API/Watchdog y cambiar CI a restore/test/audit por proyectos concretos.
- Verificacion: restores por proyecto API/Watchdog/Test OK; suite backend sin Docker/Testcontainers 223/223 OK.
- Estado: cerrado. La suite completa Docker/Testcontainers se valida en GitHub Actions tras el push.

### 2026-05-13 - V-01.06 - Cerrado - Publicacion bloqueada por paquete sin firma

- Contexto: `Atlas Balance/Atlas Balance Release` solo tenia un ZIP local sin `.zip.sig`; el actualizador online rechaza bien cualquier paquete sin firma detached.
- Impacto: no se podia publicar un release online verificable. Subir ZIP sin firma habria roto el flujo de actualizacion de la propia app.
- Solucion: generar el paquete con `Build-Release.ps1 -Version V-01.06` usando clave privada de firma en el entorno, producir `AtlasBalance-V-01.06-win-x64.zip` y `AtlasBalance-V-01.06-win-x64.zip.sig`, y dejar la clave publica por defecto en instalador/plantilla productiva.
- Verificacion: SHA256 del ZIP firmado `95DCA977E145DE07BF41E5B6478AD856BF803E4938A0A98480ABB043F51781E1`; firma RSA/SHA-256 verificada localmente como `SIGNATURE_OK`.
- Estado: cerrado para el bloqueo de firma. Sigue abierto el E2E autenticado con PostgreSQL real/datos de volumen si se quiere llamar release final.

### 2026-05-12 - V-01.06 - Cerrado - Documentacion de publicacion apuntaba a V-01.05

- Contexto: las guias de instalacion/release y el README que se copia al paquete seguian nombrando `AtlasBalance-V-01.05-win-x64.zip`.
- Impacto: un paquete `V-01.06` podia salir con instrucciones del release anterior. Eso confunde al operador y deja fatal la publicacion.
- Solucion: actualizar documentacion viva a `V-01.06`, quitar SHA viejo, documentar que el SHA se calcula tras generar el ZIP firmado y anadir estado actual de gates en `v-01.06.md`.
- Verificacion: barridos `rg` sobre referencias operativas a `AtlasBalance-V-01.05` y comandos de release `V-01.05`.
- Estado: cerrado. Sigue abierto el E2E autenticado con datos reales.

### 2026-05-12 - V-01.06 - Cerrado - Mojibake y copy publico roto

- Contexto: `SECURITY.md`, `CONTRIBUTING.md` y varias docs vivas contenian mojibake y texto de plantilla.
- Impacto: publicar con caracteres rotos en GitHub comunica poca seriedad. En una app financiera, eso no es cosmetica.
- Solucion: reescritura de textos publicos y correccion de copy visible en UI, emails, exportacion y scripts.
- Verificacion: barridos de patrones de mojibake y textos concretos detectados por subagentes.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Pantallas pesadas con coste innecesario antes de publicar

- Contexto: el pase `optimize` encontro coste evitable en graficas cargadas de forma ansiosa, busquedas por cada pulsacion, validacion de importacion renderizando demasiadas filas y auditoria OpenClaw materializando todo el scope antes de paginar.
- Causa: patrones validos para datasets pequenos usados en pantallas que pueden recibir CSVs grandes, historicos amplios y graficas pesadas.
- Solucion: lazy loading de graficas/ruta dashboard, debounce de busquedas, validacion paginada de importacion, seleccion con `Set`, paginacion real antes de mapear extractos en OpenClaw, agregacion SQL de totales mensuales e indice trigram para revision.
- Verificacion: frontend lint OK, TypeScript OK, build frontend OK, build API OK y tests focalizados OpenClaw/Revision 8/8 OK.
- Estado: cerrado como optimizacion tecnica; el release final sigue bloqueado por E2E autenticado con datos reales/volumen.

### 2026-05-12 - V-01.06 - Cerrado - Hardening de estados feos, permisos y overflow UI

- Contexto: subagentes detectaron estados vacios falsos, errores API escondidos, panel IA vacio sin permisos, acciones de revision ofrecidas a usuarios de solo lectura, limite silencioso de 500 movimientos y riesgo de overflow en tablas/selectores/importes.
- Causa: la UI asumía datos felices y permisos uniformes; eso en finanzas es pedir que alguien lea una pantalla incompleta como si fuera verdad.
- Solucion: extractor unico de errores API, estados de error/reintento, rollback de preferencias fallidas, paginacion real en cuenta, estados de permiso visibles, acciones de revision condicionadas por cuenta/titular y CSS defensivo para textos/importes/tablas grandes.
- Verificacion: frontend lint OK, TypeScript OK, build frontend OK y `wwwroot` sincronizado 61/61 archivos.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Migracion RLS de hardening no era descubierta por EF

- Contexto: la suite backend completa con Docker fallo 224/225 en `RowLevelSecurityTests`: `EXPORTACIONES` violaba RLS al insertar con scope `export`.
- Causa: `20260512110000_HardenReleaseSecurityPermissions.cs` existia, pero faltaba `20260512110000_HardenReleaseSecurityPermissions.Designer.cs`; EF la compilaba pero no la registraba como migracion aplicable.
- Solucion: anadir descriptor con `[DbContext]` y `[Migration("20260512110000_HardenReleaseSecurityPermissions")]`.
- Verificacion: suite backend completa con Docker/Testcontainers 225/225 OK.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Test de importacion esperaba mensaje antiguo de duplicado

- Contexto: la suite no Docker fallo 222/223 en `ImportacionServiceTests.ValidarAsync_Should_Reject_Duplicate_Mapping_Indexes_And_Extra_Names`.
- Causa: el codigo correcto devuelve `Clave de columna extra duplicada (...)`; el test seguia esperando el texto antiguo `Nombre de columna extra duplicado`.
- Solucion: actualizar la asercion al contrato vigente.
- Verificacion: backend no Docker 223/223 OK y suite completa 225/225 OK.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Docker/Testcontainers pendiente para RLS y concurrencia

- Contexto: habia un gate abierto porque Docker/Testcontainers no estaba disponible en ejecuciones anteriores.
- Solucion: comprobar Docker fuera del sandbox con aprobacion (`29.4.2`) y ejecutar la suite completa.
- Verificacion: `dotnet test AtlasBalance.API.Tests.csproj` completo con Testcontainers/PostgreSQL: 225/225 OK.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Revision no permitia descartar falsos positivos

- Contexto: una deteccion automatica de comision o seguro podia ser falsa, pero la UI solo permitia dejarla pendiente o marcarla como devuelta/correcta.
- Causa: faltaba un estado persistido de descarte para diferenciar "revisado y no aplica" de "pendiente".
- Solucion: anadir `DESCARTADA` en backend/frontend, filtro de descartadas y acciones `No es comision`, `No es seguro` y `Restaurar`.
- Verificacion: `RevisionServiceTests` 5/5 OK; frontend lint, TypeScript y build OK; bundle servido contiene los nuevos textos; API local saludable PID `32520`.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Revision devolvia 500 al cargar comisiones

- Contexto: la pantalla `Revision` mostraba `Request failed with status code 500` al cargar comisiones.
- Causa: el filtro relacional sobre `RevisionRawRow.Monto` se aplicaba despues de proyectar a un record posicional, forma que EF/Npgsql no traducian a SQL.
- Solucion: cambiar `RevisionRawRow` a clase con propiedades `init` y proyeccion por inicializador, manteniendo filtros y paginacion en base de datos.
- Verificacion: `RevisionServiceTests` 5/5 OK con salida `OutDir` aislada y regresion Npgsql por `ToQueryString()`.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Etiquetas laterales del grafico Evolucion cortadas

- Contexto: en el dashboard principal, los numeros laterales del grafico `Evolucion` se veian cortados con importes compactos de millones.
- Causa: el eje Y tenia una reserva maxima de 72 px y el calculo no contemplaba ticks generados desde el dominio.
- Solucion: calcular la reserva con extremos del dominio y anchura estimada por etiqueta, fijar ticks monoespaciados/tabulares y acotar el eje a 52-116 px para evitar tanto recorte como aire lateral excesivo.
- Verificacion: `npm.cmd run lint` OK y `npm.cmd exec tsc -- --noEmit` OK.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter devolvia `Authentication failed, see inner exception`

- Contexto: el chat IA mostraba un error de red de OpenRouter con detalle tecnico opaco `Authentication failed, see inner exception`.
- Causa: fallo de transporte HTTPS/proxy, no autenticacion de API key. El backend solo leia un nivel de `InnerException` y el fallback a proxy automatico podia reciclar proxies heredados rotos.
- Solucion: salida directa por defecto para clientes IA, proxy solo explicito (`Ia:UseSystemProxy`/`Ia:ProxyUrl`), fallback directo y saneado profundo de errores TLS/proxy/DNS/conexion.
- Verificacion: `AtlasAiServiceTests` 42/42 OK; test nuevo impide que `Authentication failed, see inner exception` vuelva al usuario o a auditoria.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Login devolvia `Network Error`

- Contexto: el frontend de desarrollo podia seguir vivo mientras la API no escuchaba en `localhost:5000`; ademas el bundle podia quedar compilado con API absoluta `http://localhost:5000`.
- Causa: uso de `VITE_API_URL` para una app que debe llamar a `/api` same-origin y scripts de arranque sin healthcheck real.
- Solucion: `api.ts` queda en `baseURL: '/api'`, bundle reconstruido y copiado a `wwwroot`, nuevo launcher backend con PID/logs/healthcheck, `Start-Dev.ps1` valida la API antes de declarar entorno listo y `/api/health` expone version/PID/arranque.
- Verificacion: frontend lint/build OK, backend build OK, health directo y via Vite proxy OK, busqueda sin URL absoluta en bundles.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Chat IA mostraba razonamiento interno del modelo

- Contexto: al preguntar por las cuentas con mas gastos del trimestre, el chat devolvia texto en ingles tipo `We need to answer...`, mezclaba datos y mostraba placeholders como `[PERSON_NAME]`.
- Causa: el backend devolvia `message.content` casi en bruto. OpenRouter puede excluir `message.reasoning`, pero algunos modelos pueden meter razonamiento directamente en `content`.
- Solucion: payload OpenRouter con `reasoning.exclude=true`, prompt de salida final sin analisis interno y saneado backend de `<think>`, prefacios de razonamiento, etiquetas `Final:` y placeholders.
- Verificacion: `AtlasAiServiceTests` 41/41 OK; `AtlasAiServiceTests|ConfiguracionControllerTests` 47/47 OK.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter Auto enviaba demasiados modelos en `models`

- Contexto: tras corregir Auto para usar `models`, OpenRouter devolvia 400 con `'models' array must have 3 items or fewer`.
- Causa: `OpenRouterAutoFallbackModels` se derivaba de toda la allowlist gratis, quitando `openrouter/auto`, y enviaba seis modelos. OpenRouter permite como maximo 3 elementos en `models`.
- Solucion: fallback Auto limitado a tres candidatos explicitos con `OpenRouterMaxFallbackModels = 3`; los otros modelos gratis siguen disponibles como seleccion manual.
- Verificacion: `AtlasAiServiceTests|ConfiguracionControllerTests` 46/46 OK; test nuevo parsea el payload y cubre el 400 exacto.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter Auto fallaba por interseccion vacia de restricciones

- Contexto: seleccionar `Auto` en IA devolvia 404 con `No models match your request and model restrictions`.
- Causa: se intento usar Auto Router de OpenRouter con `allowed_models` formado por modelos `:free`; Auto Router solo filtra su bolsa curada, no cualquier modelo valido de OpenRouter.
- Solucion: `Auto` conserva `openrouter/auto` como valor de configuracion, pero la llamada usa `models` con fallback acotado a modelos gratis permitidos. Ajuste posterior: OpenRouter limita `models` a 3 elementos. El texto visible pasa a `Auto (gratis permitido)`.
- Verificacion: `AtlasAiServiceTests|ConfiguracionControllerTests` 45/45 OK, frontend lint OK, TypeScript OK, build frontend OK fuera del sandbox y `wwwroot` sincronizado.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter Auto no podia elegir sin riesgo de salir de la allowlist

- Contexto: el usuario queria que OpenRouter eligiera automaticamente el mejor modelo para cada pregunta, pero dentro de la lista permitida de la cuenta.
- Causa: `openrouter/auto` abierto puede enrutar fuera de la lista o a modelos de pago; convertir Auto a un modelo fijo eliminaba ese riesgo, pero tambien eliminaba la funcion de Auto Router.
- Solucion historica: `Auto` quedo como default y se envio con `plugins.auto-router.allowed_models` limitado a los seis modelos gratis permitidos. Esta via fue sustituida el mismo dia por `models` con maximo 3 candidatos porque Auto Router no cubria bien la allowlist gratis y `models` tiene limite efectivo de 3. Los modelos exactos siguen seleccionables manualmente.
- Verificacion: frontend lint OK; TypeScript OK; `AtlasAiServiceTests|ConfiguracionControllerTests` 44/44 OK; build frontend OK; `wwwroot` sincronizado.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Chat IA no enviaba con Enter ni permitia elegir modelo desde el chat

- Contexto: el usuario esperaba que `Enter` enviara el mensaje y que el modelo pudiera seleccionarse desde el propio chat.
- Causa: el textarea solo enviaba mediante submit del boton y `/api/ia/chat` no aceptaba un modelo por consulta.
- Solucion: `Enter` envia, `Shift+Enter` inserta linea, el chat muestra selector de modelo y el backend valida el `model` solicitado sin tocar la configuracion global.
- Verificacion: frontend lint OK, TypeScript OK, build OK fuera del sandbox, `AtlasAiServiceTests` 35/35 OK, Playwright estatico con selector visible y sin overflow.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - IA demasiado restrictiva con consultas financieras administrativas

- Contexto: preguntas como `cual ha sido los gastos globales del ultimo mes` y consultas sobre Seguridad Social, impuestos, recibos o facturas deben responderse si los datos estan en Atlas Balance.
- Causa: la barrera tematica y el prompt separaban mal asuntos externos de vocabulario financiero/fiscal legitimo.
- Solucion: se amplia la allowlist financiera, se aclara el prompt y se anaden periodos/categorias de contexto para ultimo mes, mes pasado, impuestos/Seguridad Social y recibos/facturas.
- Verificacion: `AtlasAiServiceTests` 33/33 OK.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Chat IA mostraba respuesta cortada y Markdown crudo

- Contexto: una respuesta de analisis financiero aparecia con `**trimestre actual**`, una tabla Markdown visible y contenido cortado lateralmente dentro de la burbuja.
- Causa: el frontend pintaba el contenido del proveedor como texto plano y el layout grid del panel no daba la fila flexible a `.ai-chat-messages` en el estado configurado normal.
- Solucion: renderer local `AiMessageContent`, tablas Markdown convertidas a pares dato/valor, metadatos plegados en `Detalles de IA`, layout flex del panel y prompt backend para evitar tablas/pipes/asteriscos.
- Verificacion: frontend lint OK, TypeScript OK, build OK fuera del sandbox, `AtlasAiServiceTests` 33/33 OK, Playwright estatico sin Markdown crudo ni overflow.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter 404 por allowlist de modelos gratis

- Contexto: OpenRouter devolvia 404 con `No endpoints available matching your guardrail restrictions and data policy` aunque el usuario habia permitido modelos gratis en su cuenta.
- Causa: Atlas Balance intentaba usar modelos fuera de esa allowlist o forzaba ZDR sobre endpoints gratis que OpenRouter no publica como ZDR.
- Solucion: OpenRouter queda limitado a modelos gratis permitidos; `Auto` usa `auto-router.allowed_models` acotado a la allowlist y los modelos exactos gratis no fuerzan ZDR.
- Verificacion: API publica de OpenRouter revisada; build API OK; `AtlasAiServiceTests` 29/29 OK; frontend lint/build OK; `wwwroot` sincronizado; API reiniciada y `/api/health` healthy.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - OpenRouter 404 por slug de modelo obsoleto

- Contexto: el chat IA devolvia `OpenRouter no ha respondido correctamente (404)`.
- Causa: `openrouter/auto` se enviaba con `allowed_models` incluyendo `anthropic/claude-3.5-sonnet`, que ya no existe en OpenRouter.
- Solucion: Auto Router usa patrones actuales, la allowlist directa y el selector frontend se actualizan, y el runtime normaliza solo el slug obsoleto conocido a `openrouter/auto`.
- Verificacion: OpenRouter `/api/v1/models` confirma los nuevos slugs; build API OK; `AtlasAiServiceTests` 25/25 OK; frontend lint/build OK; `wwwroot` sincronizado; API reiniciada y `/api/health` 200 `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Chat IA mostraba error generico de red

- Contexto: al usar IA aparecia `Error de red al consultar el proveedor de IA`.
- Causa: el cliente de IA no tenia fallback de salida HTTP y la auditoria no guardaba suficiente detalle tecnico sanitizado para distinguir red directa, proxy, sandbox, DNS o TLS.
- Solucion historica: `AtlasAiService` reintenta con cliente HTTP fallback y registra `http_client`, `used_http_fallback` y errores de transporte recortados sin prompt ni clave. Ajuste posterior: el fallback ya no usa proxy automatico por defecto; proxy solo explicito.
- Verificacion: OpenRouter 200 y OpenAI 401 esperado fuera del sandbox; build API OK; `AtlasAiServiceTests` 24/24 OK; API reiniciada y `/api/health` 200 `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Chat IA seguia fallando en resumenes y categorias

- Contexto: tras arreglar saldos actuales, el chat IA aun podia devolver HTTP 500 al pedir resumenes mensuales, ingresos/gastos, comisiones, seguros o movimientos relevantes.
- Causa: varias consultas seguian filtrando y agrupando sobre `AiExtractoRow`, un record proyectado que Npgsql no podia traducir a SQL.
- Solucion: los agregados y filtros se hacen sobre `Extractos`/`Cuentas`; los records de prompt se proyectan solo al final.
- Verificacion: `AtlasAiServiceTests` 22/22 OK; build API OK; verificador temporal contra PostgreSQL real OK con rollback; `/api/health` responde `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Primer mensaje del chat IA devolvia HTTP 500

- Contexto: al enviar el primer mensaje desde IA, Axios mostraba `Request failed with status code 500`.
- Causa: el contexto de saldos actuales agrupaba y enlazaba sobre `AiExtractoRow`, un record proyectado dentro de LINQ. PostgreSQL/Npgsql no podia traducir esa expresion.
- Solucion: el agregado de ultimo saldo por cuenta se hace sobre columnas escalares de `EXTRACTOS` y la proyeccion al record ocurre al final.
- Verificacion: `AtlasAiServiceTests` 20/20 OK; API dev reiniciada; `/api/health` responde `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Chat flotante IA tapado por filtros del dashboard

- Contexto: al abrir IA desde la topbar en el dashboard principal, los selectores de periodo/divisa quedaban por encima del panel.
- Causa: `.app-topbar` no tenia z-index propio pese a alojar el overlay flotante de IA; el contenido principal se pintaba despues en el shell.
- Solucion: `.app-topbar` crea un plano de apilado con `position: relative` y `z-index: var(--z-sticky)`.
- Verificacion: frontend lint OK; build OK fuera del sandbox; `wwwroot` sincronizado; Playwright headless con CSS compilado confirma que el solape queda dentro del chat (`insideChat=true`).
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Guardar API key de OpenRouter fallaba por modelo invalido

- Contexto: guardar la configuracion IA podia fallar al pegar un token de OpenRouter si el modelo estaba vacio o arrastraba un slug no permitido.
- Solucion: `openrouter/auto` pasa a ser default seguro; frontend normaliza el modelo antes de enviar y backend convierte modelos vacios o no permitidos del proveedor a default seguro antes de guardar.
- Verificacion: tests focalizados IA/configuracion 25/25 OK, frontend lint/build OK, `wwwroot` sincronizado y backend dev saludable en `/api/health`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Mezcla incorrecta de sesion ChatGPT con consumo OpenAI

- Contexto: se mezclo la sesion web de ChatGPT con el consumo de OpenAI desde Atlas Balance.
- Solucion: OpenAI queda como proveedor IA por API key de servidor. El flujo externo de ChatGPT se retira por completo para no dejar una mitad de producto que nadie va a usar.
- Estado: cerrado por retirada completa del flujo externo.

### 2026-05-10 - V-01.06 - Cuentas desalineada respecto a Titulares

- Contexto: `Cuentas` usaba el ancho completo del contenido mientras `Titulares` estaba centrada en el contenedor visual comun.
- Solucion: aplicar el max-width compartido a `.phase2-page` en `system-coherence.css`, cubriendo `Cuentas` y cualquier vista phase2 equivalente.
- Verificacion: Playwright desktop confirma `deltaLeft=0` y `deltaWidth=0` entre `Titulares` y `Cuentas`; lint/build frontend OK.

### 2026-05-10 - V-01.06 - Importacion sin idempotencia/fingerprint

- Contexto: reimportar el mismo extracto podia duplicar movimientos.
- Solucion: fingerprint SHA-256 persistido por cuenta/fila/contenido normalizado, hash de lote, fila origen, fecha de importacion e indice unico filtrado.
- Verificacion: tests de reimportacion exacta, parcial y filas repetidas OK.

### 2026-05-10 - V-01.06 - Revision sin paginacion real

- Contexto: `RevisionService` cargaba todos los movimientos y filtraba en memoria.
- Solucion: filtros, ordenacion y paginacion pasan a consulta EF; endpoints devuelven `PaginatedResponse`.
- Verificacion: test de paginacion con total OK.

### 2026-05-10 - V-01.06 - Exportaciones grandes sin control

- Contexto: ClosedXML genera XLSX en memoria dentro de la request.
- Solucion: limite `export_max_rows`, auditoria de bloqueo, estado `FAILED` y HTTP 413 en exportacion manual.
- Verificacion: tests de exportacion normal, grande y usuario sin permiso OK.

### 2026-05-10 - V-01.06 - Plazos fijos marcaban notificacion aunque fallara email

- Contexto: `FechaUltimaNotificacion` se actualizaba antes del envio SMTP.
- Solucion: solo se marca tras email correcto; sin destinatarios o SMTP fallido queda reintento disponible sin duplicar notificacion interna.
- Verificacion: tests SMTP OK, SMTP falla, sin destinatarios y reintento OK.

### 2026-05-10 - V-01.06 - Suite backend bloqueada por MSBuild tras renombrado

- Contexto: los targets NuGet de tests quedaron obsoletos despues del renombrado y el build paralelo amplificaba el fallo.
- Solucion: restore regenerado y `BuildInParallel=false` en `AtlasBalance.API.Tests.csproj`.
- Verificacion: suite sin tests Docker/PostgreSQL 163/163 OK.

### 2026-05-10 - V-01.06 - Parser europeo manual incompleto

- Contexto: altas/ediciones manuales frontend usaban `Number(...)`, que rechaza `1.234,56`.
- Solucion: `parseEuropeanNumber` compartido y campos `inputMode="decimal"` en extractos, desglose de cuenta e importacion de plazo fijo.
- Verificacion: frontend lint/build OK; parser backend cubierto por tests parametrizados de importacion.

### 2026-05-10 - V-01.06 - IA demasiado abierta y con exceso de contexto financiero

- Contexto: `/api/ia/chat` estaba disponible para cualquier usuario autenticado y podia enviar demasiado contexto financiero a OpenRouter.
- Causa: faltaban permiso fuerte, cuota, longitud maxima y minimizacion del prompt.
- Solucion: chat IA con permiso persistente por usuario, interruptor global, limites configurables, auditoria de metadatos, allowlist backend de modelos y contexto minimizado con conceptos tratados como datos no confiables.
- Verificacion: API build OK, frontend lint/build OK.

### 2026-05-10 - V-01.06 - Revision y exportacion manual aceptaban permiso de lectura para escribir

- Contexto: usuarios con acceso solo lectura podian intentar marcar estados de revision o lanzar exportaciones manuales.
- Causa: los endpoints validaban `CanAccessCuentaAsync`, que concede lectura.
- Solucion: nuevo `CanWriteCuentaAsync`; `RevisionService` y `ExportacionesController.Manual` exigen escritura antes de mutar.
- Verificacion: API build OK; test de regresion anadido, ejecucion bloqueada por el bug abierto del proyecto de tests.

### 2026-05-10 - V-01.06 - Saldo bajo entraba en cooldown aunque el email no saliera

- Contexto: con SMTP roto o sin destinatarios, `FechaUltimaAlerta` podia actualizarse y bloquear reintentos.
- Causa: el servicio marcaba la alerta tras intentar enviar, sin distinguir exito real.
- Solucion: si no hay destinatarios o falla SMTP, no se actualiza `FechaUltimaAlerta` ni se registra auditoria de disparo; `EmailService` lanza error si falta SMTP para alertas.
- Verificacion: API build OK; test de regresion anadido, ejecucion bloqueada por el bug abierto del proyecto de tests.

### 2026-05-10 - V-01.06 - Exportacion perdia el orden original del extracto

- Contexto: las exportaciones de cuenta salian ordenadas por fecha, no por el orden persistido de importacion.
- Causa: `ExportacionService` usaba `OrderBy(e => e.Fecha).ThenBy(e => e.FilaNumero)`.
- Solucion: exportar por `fila_numero desc`, con fecha Excel `dd/mm/yyyy` y formato numerico `#,##0.00`.
- Verificacion: backend API build OK; test de regresion anadido, pero la ejecucion del proyecto de tests queda bloqueada por `AtlasBalance.Watchdog`.

### 2026-05-10 - V-01.06 - Barras de formula cortaban contenido

- Contexto: al seleccionar celdas largas, la barra superior tipo Excel aplicaba ellipsis.
- Causa: estilos de una sola linea heredados para valores cortos.
- Solucion: wrapping y sin truncado en las barras de formula de extractos y cuenta.
- Verificacion: frontend lint/build OK.

### 2026-05-10 - V-01.06 - Tabla de estados de revision sin RLS

- Contexto: `REVISION_EXTRACTO_ESTADOS` es una tabla nueva de datos financieros derivados.
- Causa: la migracion creaba tabla e indices pero no politicas RLS.
- Solucion: RLS forzado y politicas de lectura/escritura vinculadas al extracto.
- Verificacion: backend API build OK; `RowLevelSecurityTests` actualizado para exigir la tabla.

### 2026-05-02 - V-01.06 - Extractos no tenia reticula real de celdas

- Contexto: la tabla de `Extractos` seguia viendose con margenes torcidos y casillas desplazadas pese al fix previo de anchos fijos.
- Causa: el viewport dibujaba una cuadricula decorativa de `120px` que no coincidia con los anchos reales; el ancho total tampoco se heredaba desde un contenedor comun.
- Solucion: mover `--extracto-sheet-width` al viewport, quitar la cuadricula falsa y construir la reticula con bordes reales de celda y altura fija por fila.
- Verificacion: frontend lint/build OK, Playwright headless confirma alineacion exacta en 13 columnas y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - Desglose de cuenta sin insercion intermedia

- Contexto: al revisar una cuenta, no habia forma de anadir una linea entre dos filas del extracto y conservar ese orden.
- Causa: el alta manual de extractos siempre se guardaba al final de la cuenta con `fila_numero = max + 1`.
- Solucion: `POST /api/extractos` soporta `insert_before_fila_numero`, desplaza filas posteriores de forma transaccional, la UI de cuenta expone `Insertar debajo` y el desglose se ordena por `fila_numero desc`.
- Verificacion: `ExtractosControllerTests` 11/11 OK, frontend lint/build OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - Graficas de evolucion recortadas por arriba

- Contexto: la linea de saldo en `Evolucion` podia verse cortada en la parte superior del chart.
- Causa: dominio Y automatico sin colchon superior; el trazo quedaba pegado al limite del area de dibujo.
- Solucion: `EvolucionChart` define un dominio vertical con padding del 4%, manteniendo el cero cuando todo es positivo y ampliando el rango cuando hay valores negativos.
- Verificacion: frontend lint/build OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - Tabla de extractos parecia desplazada entre filas y columnas

- Contexto: la vista de extractos no podia permitirse celdas visualmente movidas; debe leerse como hoja tipo Excel.
- Causa: tracks flexibles `fr` dentro de filas absolutas virtualizadas, sin ancho total compartido entre cabecera y cuerpo, mas un offset vertical negativo innecesario.
- Solucion: anchos fijos por columna, `--extracto-sheet-width` compartido por cabecera/cuerpo/filas y transform vertical desde `virtualRow.start`.
- Verificacion: frontend lint/build OK, Playwright headless confirma alineacion de cabecera y celdas, y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - KPIs de ingresos y egresos cortados en dashboard principal

- Contexto: con importes largos, las tarjetas `Ingresos periodo` y `Egresos periodo` no reservaban ancho suficiente y el texto se cortaba visualmente.
- Causa: fuente mono fija demasiado grande para tarjetas laterales estrechas con `white-space: nowrap`.
- Solucion: los KPIs se convierten en contenedores CSS, los importes escalan con unidades `cqw` y el layout superior da mas ancho al bloque principal compactando `Saldos por divisa`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma que los KPIs superiores y las divisas no desbordan.

### 2026-05-02 - V-01.05 - CI GitHub no podia instalar dependencias frontend

- Contexto: GitHub Actions fallaba en `npm ci` con `404 Not Found` para tarballs `1.5.0` inexistentes.
- Causa: el lockfile apuntaba a `once`, `graphemer`, `loose-envify` y `natural-compare` en `1.5.0`, versiones inexistentes en npm.
- Solucion: se fijan overrides a `1.4.0` y se actualiza `package-lock.json` para resolver los cuatro paquetes a tarballs publicados.
- Verificacion: `npm.cmd ci` OK, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, `npm.cmd run lint` OK y `npm.cmd run build` OK.

### 2026-05-02 - V-01.05 - Hallazgos residuales del escaneo repo-wide

- Contexto: tras el cierre post-hardening quedaban ocho hallazgos en instalador/reset, permisos de extractos, dashboard, OpenClaw, importacion, RLS y CI.
- Solucion:
  - Credenciales one-shot de instalacion y reset se escriben solo despues de restringir `C:\AtlasBalance\config`; los scripts fallan cerrado si ACL falla.
  - `Reset-AdminPassword.ps1` requiere Administrador.
  - `ToggleFlag` comprueba `flagged` y `flagged_nota` segun el campo que realmente cambia.
  - Dashboard global para gerente requiere tambien permiso global de datos; `PuedeVerDashboard` global solo ya no abre cuentas.
  - Auditoria OpenClaw respeta soft-delete de extractos.
  - `returnTo` de importacion solo acepta rutas internas.
  - RLS de `EXPORTACIONES` usa permiso de escritura para escribir.
  - PostgreSQL queda digest-pinned en CI y compose.
- Verificacion: tests focalizados 20/20 OK, frontend lint/build OK, parser PowerShell OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.05 - Revision repo-wide de bugs y seguridad post-hardening

- Contexto: pasada nueva tras cerrar el escaneo previo. Subagente revisor encontro filtraciones residuales hacia integracion OpenClaw, password admin reseteada que iba a stdout, y falta de defensa en profundidad ante ZIP slip en update online.
- Hallazgos cerrados:
  - `IntegrationOpenClawController` enviaba el email del usuario creador de cada extracto al cliente OpenClaw. Ahora devuelve `nombre_completo` (mantiene el `usuario-eliminado` para usuarios borrados).
  - El endpoint `/api/integration/openclaw/auditoria` exponia `ip_address` de operadores internos al socio externo. Se elimino del payload.
  - `scripts/Reset-AdminPassword.ps1` imprimia la password temporal en pantalla; ahora la vuelca a `C:\AtlasBalance\config\RESET_ADMIN_CREDENTIALS_ONCE.txt` con ACL restringida a Administrators.
  - `ActualizacionService` extraia el ZIP descargado con `ZipFile.ExtractToDirectory`. Aunque ya hay digest SHA-256 + firma RSA del paquete, ahora se valida cada entrada contra el `packageRoot` antes de escribir, abortando si una entrada apuntaria fuera.
- Verificacion: `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --no-build` 129/129 OK; parser PowerShell de `Reset-AdminPassword.ps1` OK; frontend lint/build OK; npm audit 0 vulnerabilidades; NuGet sin vulnerabilidades.

### 2026-05-02 - V-01.05 - Harness RLS local sin permiso sobre __EFMigrationsHistory

- Contexto: la suite completa quedaba 127/128 porque `RowLevelSecurityTests.CoreFinancialTables_Should_Enforce_Rls_By_User_And_IntegrationScope` fallaba con `permission denied for table __EFMigrationsHistory` cuando otros tests creaban antes el esquema con el rol bootstrap del fixture.
- Causa: tras crear el rol `rls_owner_*`, el test cambiaba ownership solo de la BD y del esquema `public`, pero las tablas `__EFMigrationsHistory`, las del dominio y el esquema `atlas_security` seguian perteneciendo al rol bootstrap. La nueva conexion del owner no podia leer/migrar.
- Solucion: en `CreateRoleConnectionStringsAsync` el bootstrap ahora reasigna ownership de todas las tablas, secuencias, vistas, materializadas y funciones de los esquemas `public` y `atlas_security` al nuevo owner. Asi `MigrateAsync` y `ConfigureRlsRuntimeAsync` ven todo el catalogo bajo el mismo owner.
- Verificacion: `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --no-build` 129/129 OK.

### 2026-05-02 - V-01.05 - Vulnerabilidades detectadas en escaneo repo-wide

- Contexto: escaneo completo con subagentes sobre backend, frontend, scripts, CI, dependencias y update path.
- Hallazgos: lockout password/MFA debil, redaccion insuficiente de auditoria de integraciones, amplificacion de columnas extra en importacion, permisos dashboard-only usados como acceso a datos, restore de extractos con permiso insuficiente, fuga de referencia de plazo fijo y actualizacion online sin firma independiente.
- Solucion: controles corregidos en servicios/controladores/middleware/scripts y tests de regresion en auth, integraciones, importacion, permisos, extractos, cuentas y actualizaciones.
- Verificacion: suite focalizada 72/72 OK; NuGet y npm audit sin vulnerabilidades; parser PowerShell OK. Suite completa 127/128 por bug abierto de harness RLS.

### 2026-05-02 - V-01.05 - Graficas de evolucion con eje Y fijo

- Contexto: todas las graficas `Evolucion` compartian un `YAxis` fijo de `72px`, suficiente para importes grandes pero excesivo para importes pequenos.
- Solucion: calcular dinamicamente el ancho del eje Y en `EvolucionChart`, con limite inferior `44px` y superior `72px`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma `gridStartX=45px` en las cuatro rutas con evolucion.

### 2026-05-02 - V-01.05 - Cuentas de efectivo sin selector de formato

- Contexto: una cuenta de tipo `EFECTIVO` no dejaba seleccionar `Formato de importacion` como las demas cuentas importables.
- Solucion: permitir formato para `NORMAL` y `EFECTIVO`, limpiar solo datos bancarios en efectivo y mantener `PLAZO_FIJO` como unico tipo sin formato.
- Verificacion: `CuentasControllerTests` 5/5 OK, frontend lint/build OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.05 - Graficas de barras desalineadas en cuentas y titulares

- Contexto: las graficas de barras de los dashboards embebidos en `Cuentas` y `Titulares` arrancaban visualmente demasiado a la derecha.
- Solucion: reducir el ancho reservado por `YAxis` de `120` a `72`, usar ticks compactos, margenes explicitos y ejes simplificados en ambos `BarChart`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma `gridStartX=72px` en `/titulares` y `/cuentas`.

### 2026-05-01 - V-01.05 - Grafica Evolucion desalineada en dashboard principal

- Contexto: la grafica `Evolucion` del dashboard principal quedaba visualmente corrida a la derecha dentro de la tarjeta.
- Solucion: reducir el ancho reservado por `YAxis`, declarar margenes explicitos en `LineChart` y separar ticks con `tickMargin`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma `plotInsetFromLegend=72px`.

### 2026-05-01 - V-01.05 - Logo del login fuera de columna

- Contexto: el logo superior del login quedaba alineado al margen izquierdo de la pagina, no con el formulario centrado.
- Solucion: `.auth-logo-container` pasa a usar el ancho de la columna de login y centra el bloque de marca completo.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Edge headless confirma `brandDeltaCard=0px`.

### 2026-05-01 - V-01.05 - KPI del dashboard se solapaba tras reorden UI

- Contexto: al aplicar la nueva guia UI/UX, el `Saldo total` quedo en una columna demasiado estrecha y podia invadir visualmente `Saldos por divisa`.
- Solucion: ajuste de `dashboard-command-grid` y escala del KPI destacado dentro del grid principal.
- Verificacion: Playwright desktop/mobile sin overflow horizontal y sin solapamiento; frontend lint/build OK.

### 2026-05-01 - V-01.05 - PostgreSQL Row Level Security no configurado

- Contexto: revision solicitada para comprobar si la base de datos tenia Row Level Security configurado.
- Hallazgo: no habia migraciones ni scripts con `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY` o `CREATE POLICY`; la base local tenia todas las tablas `public` sin RLS y el rol `app_user` era superusuario con capacidad de saltarse RLS.
- Solucion: migraciones `EnableRowLevelSecurity` y `SignRowLevelSecurityContext` con funciones auxiliares, RLS forzado, politicas por usuario/integracion/admin/sistema y firma HMAC del contexto; interceptor EF Core para fijar contexto `atlas.*`; Docker e instalador separados en rol owner/migracion y rol runtime sin `BYPASSRLS`.
- Verificacion: `RowLevelSecurityTests` OK; tests focalizados RLS/permisos/integraciones 15/15 OK; `dotnet ef database update` aplicado en `atlas_balance_db`; catalogo local con 11 tablas objetivo bajo RLS/FORCE RLS, 20 politicas, dos migraciones RLS aplicadas, `app_user` sin `rolsuper` ni `rolbypassrls`, secreto RLS sembrado, contexto falsificado rechazado y contexto firmado aceptado.

### 2026-04-26 - V-01.05 - Importacion cambiaba el orden de lineas del extracto

- Contexto: `ConfirmarAsync` reordenaba filas por fecha antes de crear extractos, aunque el usuario necesita que la secuencia pegada desde el banco se conserve.
- Solucion: eliminado el ordenamiento por fecha; la numeracion se asigna desde abajo hacia arriba para que la linea superior quede con el `fila_numero` mas alto del lote.
- Verificacion: `ImportacionServiceTests` 26/26 OK y backend Release build OK.

### 2026-04-26 - V-01.05 - Actualizacion desde paquete podia dejar la API parada

- Contexto: `update.cmd -InstallPath C:\AtlasBalance` en un paquete `V-01.04` podia no reenviar bien `-InstallPath`; al ejecutar el actualizador directo, la API podia caer en arranque por `23505 pk_formatos_importacion`.
- Solucion: `scripts/update.ps1` ahora declara `InstallPath`/`SkipBackup` explicitamente y los reenvia al actualizador sin `ValueFromRemainingArguments`; `SeedData` evita insertar formatos por ID fijo si ese ID ya existe aunque banco/divisa no coincidan.
- Verificacion: parser PowerShell OK, `SeedDataTests` 5/5 OK y paquete `V-01.05` regenerado.

### 2026-04-25 - V-01.05 - Hallazgos de auditoria de uso, bugs y seguridad corregidos

- Contexto: la auditoria V-01.05 dejo abiertos tres puntos malos para release: Tailwind/shadcn contra el stack canonico, `CuentasController.Resumen` con contrato mas pobre que el resumen de extractos, y accesibilidad incompleta en controles propios.
- Solucion: eliminados Tailwind/shadcn y sus imports/configuracion; `CuentaResumenResponse` ahora expone titular, tipo de cuenta, notas, ultima actualizacion y `plazo_fijo`; `DatePickerField` tiene etiquetas completas y navegacion por flechas/Home/End; `ConfirmDialog` atrapa Tab dentro del modal; `AppSelect` abre/cierra con Enter/Espacio.
- Verificacion: busqueda sin restos directos de Tailwind/shadcn, `npm.cmd run lint` OK, `npm.cmd run build` OK, `wwwroot` sincronizado, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, NuGet sin vulnerabilidades y backend tests 108/108 OK.

### 2026-04-25 - V-01.05 - Gradientes decorativos de UI reducidos

- Contexto: la auditoria marco fondos `radial-gradient` y degradados suaves como huella de UI generica y contraria al criterio visual del proyecto.
- Solucion: reemplazados fondos decorativos de `body`, login, panels, KPIs, listas y empty states por superficies planas basadas en tokens. Se mantienen solo degradados funcionales de `select` y skeleton.
- Verificacion: busqueda de degradados deja solo usos funcionales, `npm.cmd run lint` OK y `npm.cmd run build` OK.

### 2026-04-25 - V-01.05 - Endpoints nuevos NPE-able si el cuerpo o las listas llegaban null

- Contexto: `POST /api/alertas`, `PUT /api/alertas/{id}`, `POST /api/cuentas/{id}/plazo-fijo/renovar` y `POST /api/importacion/plazo-fijo/movimiento` accedian a `request.SaldoMinimo`, `request.DestinatarioUsuarioIds.Count` o `request.CuentaId` sin antes validar que el body no fuera null. Un cliente autorizado mandando `null` o JSON sin la propiedad colapsaba la peticion en 500.
- Riesgo: ruido en logs, falta de respuesta clara al consumidor y degradacion gratuita ante input malformado. Solo afecta a admins (todas son rutas con `[Authorize(Roles = "ADMIN")]` o `[Authorize]`), pero el contrato seguro debe ser 400 con mensaje, no 500.
- Solucion: validacion temprana del body (`if (request is null) return BadRequest(...)`) y normalizacion de `DestinatarioUsuarioIds` con `?? []` antes de consumirla.
- Verificacion: backend Release build OK, `dotnet test` 107/107 OK, NuGet sin vulnerabilidades.

### 2026-04-25 - V-01.05 - Manifiesto frontend mantenia minimos vulnerables de dependencias

- Contexto: el `package-lock.json` resolvia versiones seguras, pero `package.json` seguia declarando minimos antiguos: `axios ^1.7.9` y `react-router-dom ^6.28.0`.
- Riesgo: instalaciones regeneradas sin lockfile fiable podian resolver rangos afectados por advisories recientes de Axios y React Router. Confiar solo en el lockfile aqui era una trampa tonta.
- Solucion: actualizado el manifiesto a `axios ^1.15.2` y `react-router-dom ^6.30.3`, dejando el lockfile en versiones verificadas.
- Verificacion: `npm.cmd audit --audit-level=moderate` OK, frontend lint/build OK, backend tests 107/107 OK y NuGet sin vulnerabilidades.

### 2026-04-25 - V-01.05 - Selector de fecha nativo no seguia el diseno Atlas al abrirse

- Contexto: el campo cerrado de fecha se veia integrado, pero el calendario desplegado seguia siendo el popup nativo del navegador.
- Solucion: creado `DatePickerField` propio y reemplazados los `input type="date"` del frontend.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y prueba visual en navegador de `/cuentas` sin errores de consola.

### 2026-04-25 - V-01.05 - Detalle de plazo fijo ocultaba vencimiento

- Contexto: el dashboard de una cuenta de plazo fijo no mostraba cuando se acababa el plazo, aunque el dato existia en la ficha de cuenta.
- Solucion: el resumen de cuenta expone `tipo_cuenta` y `plazo_fijo`; `CuentaDetailPage` muestra vencimiento, dias restantes/vencido y estado bajo el titulo.
- Verificacion: backend Release build OK, frontend lint/build OK y `wwwroot` sincronizado.

### 2026-04-25 - V-01.05 - Actualizaciones post-instalacion no dejaban flujo operativo completo

- Contexto: tras tener Atlas Balance instalado, el update manual no refrescaba scripts instalados, no actualizaba runtime y no comprobaba salud HTTP real al final.
- Solucion: `update.ps1` soporta `-PackagePath`, validacion temprana de paquete, copia de scripts/wrappers operativos a `C:\AtlasBalance`, actualizacion de `VERSION`/`atlas-balance.runtime.json` y health check con `curl.exe -k`.
- Verificacion: parser PowerShell OK y documentacion actualizada con ambos modos de ejecucion.

### 2026-04-25 - V-01.05 - Instalacion Windows Server 2019 con flujo operativo fragil

- Contexto: el operador intento instalar desde carpeta fuente, habia paquete V-01.03 mientras se validaba V-01.05, `winget` no era fiable, `install.cmd` podia desordenar parametros, `Invoke-WebRequest` daba falso negativo y una reinstalacion sobre BD existente generaba credenciales iniciales falsas.
- Solucion: validacion temprana de paquete release, mensajes duros para carpeta equivocada, fallback documentado a PostgreSQL manual 16+/17, wrappers con codigo de salida, health check con `curl.exe -k`, deteccion de usuarios existentes y script `Reset-AdminPassword.ps1` para reset controlado.
- Verificacion: parser PowerShell OK y ejecucion de instalador/wrapper desde carpeta fuente falla con mensaje claro de paquete invalido.

### 2026-04-25 - V-01.05 - Reinstalacion reutilizaba PFX con password nueva

- Contexto: reinstalar sobre `C:\AtlasBalance` existente podia dejar `AtlasBalance.API` parado con `CryptographicException: La contraseña de red especificada no es válida`.
- Solucion: `Instalar-AtlasBalance.ps1` elimina `atlas-balance.pfx` y `atlas-balance.cer` existentes antes de generar un certificado HTTPS nuevo, evitando que el PFX viejo quede asociado a una password nueva en `appsettings.Production.json`.
- Verificacion: diagnostico reproducido por traza de Windows Event Log; correccion revisada en el flujo `New-AtlasCertificate`.

### 2026-04-25 - V-01.05 - Importacion embebida bloqueada por anti-frame

- Contexto: el modal de `Importar movimientos` del dashboard de cuenta cargaba `/importacion` en un `iframe`, pero el navegador mostraba rechazo de conexion/documento roto.
- Solucion: cabeceras de seguridad ajustadas de `DENY`/`frame-ancestors 'none'` a `SAMEORIGIN`/`frame-ancestors 'self'`, permitiendo solo el frame interno de la propia app.
- Verificacion: diagnostico por cabeceras HTTP del health check y revision del componente `CuentaDetailPage`.

### 2026-04-25 - V-01.03 - Hardening de sesiones, passwords, updates y rutas

- Contexto: la auditoria V-01.03 detecto resets de password sin revocar sesiones, reuse de refresh token sin escalado, login enumerable/rate limit flojo, bearer invalido de integracion sin throttle previo, `app_update_check_url` sin allowlist, rutas relativas aceptadas tras `GetFullPath`, politica de password de 8 caracteres, credenciales one-shot persistentes y `postcss` vulnerable.
- Solucion: `SecurityStamp` + `PasswordChangedAt`, migracion `UserSessionHardening`, revocacion de refresh tokens en reset/cambio/delete/reuse, rate limit por email/IP, respuesta generica para cuentas bloqueadas, throttle previo para bearer invalido, allowlist HTTPS al repo oficial, validacion de rutas crudas antes de normalizar, minimo 12 caracteres con bloqueo de passwords comunes, borrado programado de `INSTALL_CREDENTIALS_ONCE.txt` y `postcss` actualizado a `8.5.10`.
- Verificacion: `dotnet test -c Release --no-build` 94/94 OK, `npm.cmd run lint` OK, `npm.cmd run build` OK, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades y NuGet sin vulnerabilidades.

### 2026-04-24 - V-01.03 - Frontend muestra detalle de cuenta a perfiles dashboard-only

- Contexto: tras cerrar la fuga de datos en `ExtractosController`, el frontend seguia usando `usePermisosStore.canViewCuenta` para pintar enlaces a `/dashboard/cuenta/:id` y otras affordances de cuenta.
- Solucion: `canViewCuenta` y helpers de cuenta ya no consideran validas las filas globales `dashboard-only`; `CuentasPage` deja de mostrar CTAs operativos sin acceso y `CuentaDetailPage` redirige al dashboard ante `403`.

### 2026-04-20 - V-01.02 - Release sin scripts one-click completos

- Contexto: el paquete no exponia `install/update/uninstall/start` como scripts principales.
- Solucion: wrappers `.cmd` y `.ps1` nuevos, copiados por `Build-Release.ps1`.

### 2026-04-20 - V-01.02 - `start` no arrancaba la base de datos gestionada

- Contexto: el lanzador arrancaba Watchdog/API pero dejaba PostgreSQL fuera del orden de arranque.
- Solucion: runtime con `ManagedPostgres` y arranque PostgreSQL -> Watchdog -> API.

### 2026-04-20 - V-01.02 - `setup-https.ps1` tenia sintaxis invalida

- Contexto: parser PowerShell detecto cadena sin terminar.
- Solucion: reescritura ASCII del script.

### 2026-04-20 - V-01.02 - Secretos de configuracion guardados en claro

- Contexto: SMTP password y Exchange Rate API key quedaban persistidos como valores normales en BD.
- Solucion: Data Protection con `ISecretProtector`, migracion automatica en arranque y redaccion en respuestas/auditoria.

### 2026-04-20 - V-01.02 - Permiso global de dashboard concedia alcance global de datos

- Contexto: `PuedeVerDashboard` global podia hacer que un usuario no admin listara datos fuera de su alcance esperado.
- Solucion: `HasGlobalAccess` solo se activa con permisos globales de datos y se cubrio con test.

### 2026-04-20 - V-01.02 - Descarga de exportaciones sin validacion de ruta segura

- Contexto: la ruta de fichero venia de BD y se abria si existia.
- Solucion: descarga limitada a `.xlsx` dentro de `export_path`.

### 2026-04-20 - V-01.02 - Watchdog con binding administrativo demasiado laxo

- Contexto: servicio administrativo con secreto compartido, pero binding configurable por URLs externas.
- Solucion: Kestrel escucha solo en `localhost:5001`.

### 2026-04-20 - V-01.02 - `AllowedHosts` abierto en produccion

- Contexto: plantilla/base permitia `AllowedHosts="*"`.
- Solucion: la API rechaza wildcards fuera de Development y el instalador escribe hosts explicitos.

### 2026-04-20 - V-01.02 - Artefactos auxiliares con informacion de sesion/login

- Contexto: cookies, cabeceras, payloads y captura de login quedaron en `Otros`.
- Solucion: eliminados los artefactos sensibles y confirmado barrido final sin restos.

### 2026-04-20 - V-01.02 - Typos `atlasbalnace` / `atlas-blance` en archivos activos

- Contexto: quedaban restos en configuracion, plantillas, scripts, placeholders, tests y evento interno de importacion.
- Solucion: normalizados email/rutas/namespaces, creada constante compartida para el evento de importacion y verificado barrido final sin coincidencias en codigo activo.

### 2026-04-20 - V-01.02 - Instalador escribia version runtime antigua

- Contexto: `Instalar-AtlasBalance.ps1` seguia usando `V-01.01` al generar runtime/credenciales de instalacion.
- Solucion: actualizado a `V-01.02` y sincronizada la documentacion de instalacion.

### 2026-04-20 - V-01.02 - Bundle servido por la API desactualizado

- Contexto: `frontend/src` tenia fixes ya aplicados, pero `backend/src/AtlasBalance.API/wwwroot` seguia sirviendo bundles antiguos.
- Solucion: recompilado frontend, limpiado `wwwroot` de forma controlada y copiado `frontend/dist`.

### 2026-04-20 - V-01.02 - Secretos de desarrollo en archivos versionables

- Contexto: `appsettings.json`, `appsettings` de Watchdog y `docker-compose.yml` contenian credenciales/defaults de desarrollo.
- Solucion: se dejaron sin secretos versionables, se añadieron plantillas y `.env.example`, y el seed admin exige password configurada.

### 2026-04-20 - V-01.02 - Version runtime antigua en seed y actualizaciones

- Contexto: `SeedData` y el User-Agent de checks de actualizacion seguian usando `V-01.01`.
- Solucion: `app_version` inicial y User-Agent pasan a `V-01.02`/version runtime actual.

### 2026-04-20 - V-01.02 - Textos mojibake en importacion y SMTP

- Contexto: algunos errores de importacion y asunto SMTP mostraban texto roto.
- Solucion: se corrigieron cadenas user-facing y sus tests.

### 2026-04-20 - V-01.02 - Acciones CI sin SHA fijo

- Contexto: GitHub Actions usaba tags mutables para actions oficiales.
- Solucion: se fijaron los actions a SHAs concretos.

## Regla de uso

- Si se detecta un bug, anadirlo en `Abiertos` con descripcion, contexto, version y pasos de reproduccion.
- Cuando se resuelva, moverlo a `Cerrados` y documentar la solucion tambien en `Documentacion/LOG_ERRORES_INCIDENCIAS.md`.

### 2026-05-01 - V-01.05 - Cerrado - Test de dashboard dependia del dia del mes

- Contexto: la verificacion amplia del hardening de seguridad fallo en `DashboardServiceTests.GetPrincipalAsync_Should_Aggregate_CurrentBalances_And_PeriodFlows_In_TargetCurrency`.
- Causa: el test generaba un movimiento en `monthStart.AddDays(2)` y lo esperaba como contabilizado aunque al ejecutarse el dia 1 o 2 del mes era una fecha futura.
- Solucion: sustituido por `today` para los movimientos que deben entrar en el mes actual.
- Verificacion: backend sin Testcontainers 115/115 OK.

### 2026-04-20 - V-01.02 - Query params sensibles guardados en auditoria de integracion

- Contexto: `IntegrationAuthMiddleware` metia todo `Request.Query` en la tabla de auditoria sin redactar claves sensibles.
- Solucion: whitelist-by-deny con `HashSet` de claves sensibles y marcador `REDACTED`.

### 2026-04-20 - V-01.02 - `backup_path`/`export_path` sin validacion de ruta segura

- Contexto: valores editables por admin en tabla `CONFIGURACION`; se usaban directos en `Directory.CreateDirectory` y `Path.Combine`.
- Solucion: helper `ResolveSafeDirectory` que rechaza rutas relativas, traversal `..` y caracteres invalidos.

### 2026-04-20 - V-01.02 - Fuga de email de usuarios borrados via integracion OpenClaw

- Contexto: `IntegrationOpenClawController` exponia emails reales de usuarios con `deleted_at != null` al devolver extractos.
- Solucion: el email se sustituye por `usuario-eliminado` cuando el usuario esta borrado.

### 2026-04-20 - V-01.02 - Kestrel de desarrollo escuchando en 0.0.0.0

- Contexto: `appsettings.Development.json` bindeaba `https://0.0.0.0:5000` con `AllowedHosts=*`.
- Solucion: binding a `localhost` y `AllowedHosts=localhost` tambien en Development.

### 2026-04-20 - V-01.02 - Release zip incluia `appsettings.Development.json`

- Contexto: `dotnet publish` no excluia los ficheros Development ni las plantillas; los zips generados por `Build-Release.ps1` incluian secretos de desarrollo.
- Solucion: `ItemGroup` con `CopyToPublishDirectory="Never"` en los csproj de API y Watchdog. Cualquier paquete distribuido antes de hoy debe considerarse comprometido y los secretos rotarse.

### 2026-04-20 - V-01.02 - Passwords hardcodeadas en scripts smoke y docs historicas

- Contexto: `phase2-smoke.ps1`, `phase2-smoke-curl.ps1`, `Otros/Raiz anterior/SPEC.md` y `CORRECCIONES.md` contenian credenciales concretas.
- Solucion: scripts ahora leen `ATLAS_SMOKE_ADMIN_PASSWORD`/`ATLAS_SMOKE_TEST_PASSWORD`; documentos sustituyen los valores por placeholders.

### 2026-04-23 - V-01.03 - Exposicion cross-account en /api/extractos para dashboard-only global

- Contexto: un permiso global con `PuedeVerDashboard=true` podia terminar devolviendo extractos de todas las cuentas desde `GET /api/extractos`.
- Solucion: `ExtractosController` alinea su criterio de acceso global con `UserAccessService` y excluye `PuedeVerDashboard` de acceso global de datos; se anadio test de regresion para dashboard-only global.

### 2026-04-25 - V-01.05 - Importacion bloqueaba filas informativas de banco

- Contexto: filas con solo concepto y sin fecha/monto/saldo se marcaban como error fatal en la validacion de importacion.
- Solucion: esas filas pasan a advertencias importables; se heredan fecha y saldo de la ultima fila valida anterior y se importa monto `0`. Las filas parcialmente rotas o ambiguas siguen bloqueadas.

### 2026-04-26 - V-01.05 - Importacion bloqueaba filas informativas con saldo

- Contexto: filas con concepto y saldo, pero sin fecha ni importe, aparecian como errores y no podian seleccionarse para importar.
- Solucion: esas filas pasan a advertencias importables; se hereda la fecha anterior, se importa monto `0` y se conserva el saldo informado si es numerico.

### 2026-04-25 - V-01.05 - JSX sobrante y lint estricto durante implementacion de plazo fijo

- Contexto: `npm.cmd run build` fallo en `CuentasPage.tsx` por un cierre JSX duplicado; despues `npm.cmd run lint` fallo por warning de Fast Refresh en `components/ui/button.tsx`.
- Solucion: corregido el JSX y documentada una excepcion local de ESLint para el componente UI que exporta `Button` y `buttonVariants`.

### 2026-05-10 - V-01.06 - Cerrado - IA sin gobierno suficiente de permisos, coste y privacidad

- Contexto: la auditoria especifica de IA exigia que no pudiera usarse sin autorizacion, sin limites o exponiendo datos/coste sin control.
- Impacto: riesgo alto de consumo no autorizado, coste inesperado y envio innecesario de datos financieros a proveedor externo.
- Solucion:
  - Interruptor global `ai_enabled`.
  - Permiso persistente `USUARIOS.puede_usar_ia`.
  - Validacion backend obligatoria en `/api/ia/chat`.
  - Limites configurables por minuto/hora/dia, limite global diario, presupuesto mensual/total y caps de tokens/contexto.
  - Contadores de coste `ai_usage_*` persistidos fuera de auditoria para que la limpieza de logs no reinicie el presupuesto total.
  - Auditoria sin prompt ni respuesta completos.
  - Frontend oculta IA si no hay permiso o esta desactivada.
- Verificacion: API build OK, frontend lint/build OK. Tests backend siguen bloqueados por fallo de runner/MSBuild sin salida.

### 2026-05-10 - V-01.06 - Cerrado - IA sin presupuesto mensual por usuario ni pruebas de proveedor

- Contexto: quedaba coste gobernado solo globalmente y faltaban tests reproducibles para fallos de proveedor.
- Impacto: un usuario autorizado podia consumir presupuesto comun sin barrera individual; errores de proveedor podian quedar poco cubiertos.
- Solucion:
  - Nueva tabla `IA_USO_USUARIOS` y clave `ai_user_monthly_budget_eur`.
  - Bloqueo `user_monthly_budget_exceeded` antes de llamada externa.
  - Parser defensivo de payload del proveedor.
  - Tests de 401, 404, timeout, respuesta malformada, presupuesto por usuario y persistencia de contadores.
- Verificacion: `AtlasAiServiceTests` 18/18 OK.

### 2026-05-10 - V-01.06 - Cerrado - Tests Testcontainers bloqueados por Docker no disponible

- Contexto: `dotnet test AtlasBalance.API.Tests.csproj` compila y ejecuta 173 tests, pero fallan dos tests que requieren PostgreSQL en Testcontainers.
- Impacto: no se puede declarar release apto porque no se ha validado RLS real ni concurrencia de `fila_numero` sobre PostgreSQL.
- Evidencia: Docker client existe, pero no conecta a `npipe:////./pipe/dockerDesktopLinuxEngine`; `Start-Service com.docker.service` falla por permiso/servicio no abrible.
- Solucion: el 2026-05-12 se comprobo Docker fuera del sandbox con aprobacion y se ejecuto la suite completa con Testcontainers/PostgreSQL.
- Verificacion: `dotnet test AtlasBalance.API.Tests.csproj`: 225/225 OK.
- Estado: cerrado. El gate que sigue abierto para release final es el E2E autenticado con datos reales, no Docker/Testcontainers.
