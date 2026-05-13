# Documentacion tecnica

## 2026-05-13 - V-01.06 - CI locked restore y release firmado

### Que cambio

- `AtlasBalance.API.csproj` y `AtlasBalance.Watchdog.csproj` declaran `RuntimeIdentifiers=win-x64`.
- `.github/workflows/ci.yml` restaura backend por proyectos concretos, ejecuta tests sobre `AtlasBalance.API.Tests.csproj` y audita paquetes por proyecto.
- `Build-Release.ps1` deja de depender del restore de solucion para publicar runtime-specific; ahora restaura API y Watchdog por proyecto con `--locked-mode -r win-x64` y publica con `--no-restore`.
- El script de release genera `.zip.sig` con RSA/SHA-256 mediante un firmador temporal .NET 8 cuando recibe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`.
- `Instalar-AtlasBalance.ps1` y `appsettings.Production.json.template` incluyen una clave publica de firma por defecto; `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM` sigue pudiendo sobrescribirla.
- Tests backend de IA y tipos de cambio se ajustan a los mensajes saneados vigentes.
- `ConfiguracionController` sanea el destinatario del test SMTP antes de escribirlo en logs, cerrando el finding CodeQL de log injection.

### Por que

GitHub Actions fallo en `dotnet restore --locked-mode` porque los lockfiles ya contenian dependencias para `win-x64`, pero los proyectos no declaraban ese RID. Eso no era un fallo de GitHub; era el repo contradiciendose a si mismo. De paso, publicar un ZIP sin `.sig` era inutil para el actualizador online: la app lo rechazaria y con razon.

El restore de solucion tambien falla localmente sin error MSBuild concreto. Mantenerlo como gate principal seria mala ingenieria: CI ahora valida los tres proyectos reales y el script de release valida los dos publicables con RID.

### Verificacion

- `dotnet restore` por proyecto API/Watchdog/Test: OK.
- Suite backend sin Docker/Testcontainers sobre `AtlasBalance.API.Tests.csproj`: OK, 223/223.
- `Build-Release.ps1 -Version V-01.06`: OK con build frontend, restore locked, publish API/Watchdog y firma.
- ZIP: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.06-win-x64.zip`.
- Firma: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.06-win-x64.zip.sig`.
- SHA256 ZIP: `A901F53B2431C3A987C2C9F73B7C1B7C5553A3D070F2BDB2630ABFA4116CAD31`.
- Verificacion local de firma RSA/SHA-256: `SIGNATURE_OK`.

### Pendiente real

- La clave privada de firma debe vivir en un almacen seguro operativo o secreto de CI si se automatiza el release; no se versiona.
- El E2E autenticado con PostgreSQL real/datos de volumen sigue siendo el gate para quitar la etiqueta RC/pre-release. Llamarlo final sin esa prueba seria maquillaje.

## 2026-05-12 - V-01.06 - Saneado de datos para entrega

### Que cambio

- Se retiran los scripts locales de datos demo `scripts/seed-demo-data*.sql`.
- Se elimina el seed anidado `Atlas Balance/Atlas Balance/scripts/seed-development-data.sql`, que era un artefacto local fuera del paquete real.
- Se anade `scripts/purge-delivery-data.sql` para dejar una base sin datos operativos antes de publicar o entregar.
- Se anade `scripts/Purge-DeliveryData.ps1` con confirmacion obligatoria (`-ConfirmDeliveryPurge` o `ATLAS_CONFIRM_DELIVERY_PURGE=BORRAR_DATOS`) y ejecucion contra el contenedor `atlas_balance_db`.
- La purga borra usuarios, emails, refresh tokens, titulares, cuentas, plazos fijos, extractos, columnas extra, estados de revision, permisos, preferencias, alertas, backups, exportaciones, notificaciones, tokens de integracion, auditorias y uso IA.
- La purga conserva tablas maestras necesarias para arrancar (`CONFIGURACION`, `FORMATOS_IMPORTACION`, `DIVISAS_ACTIVAS`, `TIPOS_CAMBIO`, migraciones), pero pone a `NULL` las referencias a usuarios borrados.
- Se resetean valores sensibles de `CONFIGURACION`: SMTP, claves API, proveedor/modelo IA operativo y contadores de consumo IA.
- `.gitignore` ignora seeds demo y la carpeta local anidada `Atlas Balance/Atlas Balance/`.

### Por que

Entregar una app financiera con extractos, titulares, cuentas, hashes demo o tokens locales seria una cagada basica. La limpieza correcta no es borrar tablas al azar: hay FKs, RLS forzado y configuracion que debe sobrevivir para que la app arranque limpia.

### Detalle tecnico

- El primer diseno con `TRUNCATE` fallo porque PostgreSQL bloquea truncar `USUARIOS` si `CONFIGURACION` mantiene una FK hacia esa tabla, aunque los valores esten a `NULL`.
- La version final usa `DELETE` en orden de dependencias dentro de una transaccion.
- Durante la purga se desactiva RLS en las tablas protegidas y se vuelve a activar con `FORCE ROW LEVEL SECURITY` antes del `COMMIT`.
- Hangfire se limpia dentro de un bloque condicional si existe el schema `hangfire`.
- El seed admin no queda en base tras la purga; en el siguiente primer arranque se creara de nuevo solo si `SeedAdmin:Password` esta configurado correctamente.

### Verificacion

- Conteo inicial local: `USUARIOS=3`, `TITULARES=13`, `CUENTAS=29`, `EXTRACTOS=404`, `REFRESH_TOKENS=12`, `AUDITORIAS=103`, `IA_USO_USUARIOS=1`.
- `Purge-DeliveryData.ps1 -ConfirmDeliveryPurge`: OK en segundo intento tras corregir la estrategia.
- Conteo final de 21 tablas sensibles: todas `0`.
- Verificacion de configuracion sensible sin exponer valores: SMTP/API/IA vacio o reseteado.
- `rg` sobre nombres de demo y hashes de usuarios demo: sin resultados en rutas publicables.
- Parser PowerShell de `Purge-DeliveryData.ps1`: OK.

### Pendiente real

- La purga no sustituye el E2E autenticado con PostgreSQL real. Sirve para no publicar datos; no prueba el flujo funcional completo del release.

## 2026-05-12 - V-01.06 - Microinteracciones emil prepublicacion

### Que cambio

- `AppSelect` guarda si el popover se abre por teclado o por puntero; la apertura por teclado marca `data-open-source="keyboard"` y no anima.
- `ToastViewport` pasa a un `ToastItem` con temporizador propio por notificacion, pausa por hover/foco y pausa automatica con `document.visibilityState`.
- Los toasts reemplazan keyframes por transicion con `@starting-style`, manteniendo entrada corta sin bloquear interacciones.
- Se retiran la animacion global de entrada de pagina y el pop del item activo de navegacion.
- El shell deja de animar propiedades de layout en el colapso de sidebar; se eliminan transiciones sobre `grid-template-columns`, padding y max-width/max-height.
- El check de los checkbox ya no nace en `scale(0)`; usa opacidad y escala parcial para evitar aparicion brusca.
- Los hover con transform/sombra de KPIs, tarjetas de balance, navegacion y boton IA se limitan a `(hover: hover) and (pointer: fine)`.
- `revision-ai.css` anade `prefers-reduced-motion` local para el chat flotante y los puntos de carga.
- `frontend/dist` se copia a `backend/src/AtlasBalance.API/wwwroot` y se podan assets obsoletos solo dentro de `wwwroot/assets`.

### Por que

El polish bueno no es mas movimiento; es quitar movimiento donde estorba. Navegacion por teclado, cambios de ruta y superficies repetidas deben responder ya. Hover pegajoso en touch y toasts que desaparecen con la pestana oculta son detalles pequenos, pero huelen a producto sin rematar.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: falla dentro del sandbox por `spawn EPERM` conocido de Vite/Rolldown; OK fuera del sandbox con aprobacion.
- `wwwroot/index.html` coincide con `frontend/dist/index.html`.
- `dist_files=65`, `wwwroot_files=65`, `stale_assets=0`.
- Barrido estatico sin `page-surface-in`, `nav-link-pop`, `toast-slide-in` ni `scale(0)` en `frontend/src`.

### Pendiente real

- Sigue sin cerrarse el E2E autenticado con PostgreSQL real/datos de volumen ni el ZIP firmado `V-01.06`; esto deja el release final bloqueado, aunque el polish frontend queda validado.

## 2026-05-12 - V-01.06 - Clarify de copy y errores UI/API

### Que cambio

- `frontend/src/utils/errorMessage.ts` centraliza mensajes saneados para Axios, red y codigos HTTP; evita que el usuario vea `Network Error` o `Request failed with status code`.
- Tablas y pantallas de sistema traducen estados tecnicos: copias/exportaciones muestran `Pendiente`, `Lista`, `Fallida`, `Manual`, `Automatica`; tokens muestran `Activo`/`Revocado`.
- `BackupsPage` pasa a `Copias de seguridad` y endurece la doble confirmacion de restauracion.
- `ExportacionesPage`, `TokenList`, `AlertasPage`, `UsuariosPage`, `CuentasPage`, `TitularesPage`, `FormatosImportacionPage`, dashboards y extractos sustituyen `N/A` y empty states muertos por textos accionables.
- `ConfirmDialog` acepta `loadingLabel` para que las acciones destructivas no caigan en el generico `Procesando...`.
- Controllers y servicios API dejan de devolver `Request invalido`, referencias a logs del servidor y errores de IA demasiado tecnicos.
- `.gitignore` ignora `backend/.codex-build/`, usado para builds aislados cuando `bin/Debug` esta bloqueado por una API local viva.

### Por que

Una app financiera no puede publicar una interfaz que le habla al usuario como si estuviera leyendo logs. `N/A`, `Request invalido`, `Flag` y `SUCCESS` son deuda de producto, no detalles esteticos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `dotnet build src/AtlasBalance.API/AtlasBalance.API.csproj --no-restore -v minimal -o .\.codex-build\api`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro aplica el bloqueo conocido `spawn EPERM` de Vite/Rolldown.
- Barridos `rg` sin restos utiles de copy tecnico en codigo vivo, excluyendo nombres internos/migraciones.

### Incidencias

- `dotnet build` normal queda bloqueado por `.NET Host (9632)` usando `bin/Debug/net8.0/AtlasBalance.API.dll`; la compilacion se valido con salida aislada.
- `Remove-Item backend/.codex-build -Recurse` fallo por `Access denied` sobre DLLs generadas. No se insistio; la carpeta queda ignorada.

## 2026-05-12 - V-01.06 - Polish final UI

### Que cambio

- `api.ts` y `divisaStore.ts` dejan de emitir errores/warnings en consola de produccion; las trazas quedan limitadas a `import.meta.env.DEV`.
- `DatePickerField` restaura foco al trigger al cerrar con Escape, Hoy, Limpiar o seleccion de fecha.
- Selects, botones de accion, toasts, sidebar colapsada y navegacion reciben labels/estados de foco mas claros.
- El chat IA mejora contraste del mensaje de usuario y conserva wrapping robusto en textos largos.
- Overlays comunes eliminan `backdrop-filter` caro; el QR MFA mantiene blanco real documentado para lectura fiable.
- Copy visible y metadatos HTML quedan saneados: acentos, mensajes de confirmacion, labels de botones y nombres de secciones.
- `Documentacion/Diseno/design-tokens.css` se sustituye por snapshot alineado con `frontend/src/styles/variables.css`; `DESIGN.md` actualiza radios reales.
- `wwwroot` se sincroniza sin borrar la carpeta completa: copia del build, verificacion de `index.html` y poda solo de chunks JS obsoletos.

### Por que

Un release financiero no puede salir con consola ruidosa, labels genericos tipo "Editar", foco perdido al cerrar popovers o copy medio roto. Son detalles pequenos, pero juntos huelen a producto sin revisar.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro aplica el bloqueo conocido de Vite/Rolldown `spawn EPERM`.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: `dist_files=65 wwwroot_files=65`.
- Barrido estatico en `wwwroot` sin `console.error`, `console.warn`, `debugger`, trazas `[API]`, copy sin acentos detectado ni referencias a chunks JS retirados.

### Pendiente real

- Falta E2E autenticado con PostgreSQL real y datos de volumen. Sin eso, llamar "final" al release sigue siendo demasiado optimista.

## 2026-05-12 - V-01.06 - Documentacion de publicacion y copy final

### Que cambio

- `README_RELEASE.md`, `Documentacion/documentacion.md` y `DOCUMENTACION_USUARIO.md` dejan de apuntar a paquetes `V-01.05` como si fueran el release actual.
- `SECURITY.md` y `CONTRIBUTING.md` se reescriben en UTF-8 limpio, sin emojis ni tono de plantilla.
- `v-01.06.md` declara el estado real de publicacion: Docker/Testcontainers cerrado 225/225, E2E autenticado con datos reales pendiente.
- Se corrige copy visible en IA, auditoria, configuracion, exportaciones XLSX, emails de plazo fijo y scripts de instalacion/reset.
- Se limpia mojibake residual en documentacion tecnica viva.

### Por que

Publicar una version `V-01.06` con README y guia instalable de `V-01.05` seria una metedura de pata basica. Peor aun: `Build-Release.ps1` copia `Documentacion/documentacion.md` dentro del paquete, asi que el error viajaria con el ZIP.

### Verificacion

- Barridos `rg` sobre referencias `AtlasBalance-V-01.05`, `Build-Release.ps1 -Version V-01.05`, mojibake y copy reportado por subagentes.
- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `dotnet test ... --filter "ActualizacionServiceTests|PlazoFijoServiceTests|ExportacionServiceTests"`: 17/17 OK.
- `npm.cmd run build`: bloqueado dentro del sandbox por `spawn EPERM`; OK fuera del sandbox con aprobacion.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: 65/65 archivos tras repetir fuera del sandbox por `Access denied`.

### Pendiente real

- No se cierra el E2E autenticado con PostgreSQL real y datos de volumen; sigue bloqueando llamar final al release.

## 2026-05-12 - V-01.06 - Optimize prepublicacion

### Que cambio

- `App.tsx` incorpora `DashboardRoute` para bloquear la ruta de dashboard antes de cargar la pagina y sus graficas cuando el usuario no tiene permiso.
- `CuentasPage` y `TitularesPage` cargan `EvolucionChart` y `TitularSaldoBarChart` bajo demanda con `React.lazy` y `Suspense`.
- `TitularSaldoBarChart` encapsula el grafico de barras compartido para no duplicar Recharts en dos paginas.
- `useDebouncedValue` evita disparar busquedas remotas por cada pulsacion en cuentas/titulares.
- `ImportacionPage` analiza solo unas pocas lineas no vacias para previsualizacion/separador, pagina la tabla de validacion en bloques de 200 filas y usa `Set` para seleccion.
- `IntegrationOpenClawController.Auditoria` filtra auditoria con subquery de extractos y carga el mapa de cuentas solo para los extractos de la pagina devuelta.
- `GetMonthTotalsByCuentaAsync` agrupa ingresos/egresos por cuenta en SQL.
- La migracion `20260512143000_AddRevisionConceptTrigramIndex` crea `pg_trgm` e indice GIN parcial sobre `lower(concepto)` para las busquedas textuales de revision.

### Por que

Las pantallas con tablas grandes y graficas no pueden tratar todos los datos como si fueran una demo de 20 filas. Partir CSVs completos en cada tecla, renderizar miles de filas de validacion o importar Recharts en rutas que no lo necesitan es coste absurdo. No mata la app en pequeno; en volumen la vuelve torpe.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` conocido de Vite/Rolldown.
- `dotnet build "Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" --no-restore -p:UseAppHost=false` con `OutDir` aislado: OK fuera del sandbox.
- `dotnet test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" --no-restore --filter "FullyQualifiedName~IntegrationOpenClawControllerTests|FullyQualifiedName~RevisionServiceTests"`: 8/8 OK fuera del sandbox.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: `dist_files=65 wwwroot_files=65`.

### Pendiente real

- Falta E2E autenticado con PostgreSQL real y datos de volumen.
- Falta generar el ZIP firmado de `V-01.06`; en esta sesion `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` no esta presente. Dejar builds verdes no equivale a publicar paquete.

## 2026-05-12 - V-01.06 - Adapt responsive desktop/tablet/mobile

### Que cambio

- `Layout` usa `matchMedia('(min-width: 768px) and (max-width: 1023.98px)')` para alinear JS con CSS: mobile `<768px`, tablet `768-1023.98px`, desktop `>=1024px`.
- `shell.css`, `system-coherence.css`, `entities.css`, `users.css`, `admin.css` y `revision-ai.css` ajustan los cortes `768/1024` al mismo contrato.
- `DatePickerField` calcula colision horizontal del popover y alterna alineacion izquierda/derecha. En puntero tactil, el calendario pasa a superficie tipo bottom sheet con dias y flechas de 44px.
- `global.css` evita scroll horizontal global accidental y eleva targets tactiles de selects, date picker y labels con checkbox.
- `TitularDetailPage` y `FormatosImportacionPage` envuelven tablas en contenedores de scroll local.
- `dashboard.css` reduce anchos de la hoja de cuenta por breakpoint, mantiene scroll interno y da 44px a checkboxes de tabla en touch.
- `extractos.css` eleva filas/checkboxes/acciones compactas en touch y deja `Hist.` visible como accion tactil.
- `revision-ai.css` eleva selector de modelo, prompts, envio de chat y botones de estado de revision en touch.
- `auth.css`, `users.css`, `importacion.css` y `system-coherence.css` corrigen targets compactos de login, filas, permisos, importacion y acciones de tarjetas.

### Por que

La app se usa sobre todo en escritorio, pero publicar una app de tesoreria que se rompe en iPad o en un movil de 320px es una chapuza cara. Las tablas financieras pueden tener scroll horizontal interno; la pagina completa no.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro del sandbox sigue bloqueado por `spawn EPERM` conocido de Vite/Rolldown.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: `dist_files=61 wwwroot_files=61`.
- Playwright `setContent` fuera del sandbox:
  - `1366x768`: `overflow=0`.
  - `1024x768 touch`: `overflow=0`, sin targets tactiles menores de 44px.
  - `768x1024 touch`: `overflow=0`, sin targets tactiles menores de 44px.
  - `390x844 touch`: `overflow=0`, sin targets tactiles menores de 44px.
  - `320x568 touch`: `overflow=0`, sin targets tactiles menores de 44px.

### Pendiente real

- No sustituye el E2E autenticado con datos reales/volumen. Ese pendiente sigue abierto en `REGISTRO_BUGS.md`.

## 2026-05-12 - V-01.06 - Hardening de estados borde y errores API

### Que cambio

- `frontend/src/utils/errorMessage.ts` centraliza mensajes de error para Axios/API:
  - errores sin respuesta;
  - payloads con `error`, `detail`, `title`, `message`, `mensaje`;
  - validaciones ASP.NET en `errors`;
  - fallos 400/401/403/404/409/413/429/5xx;
  - truncado defensivo de mensajes largos.
- `api.ts` usa ese extractor para logs saneados y toasts, evitando payloads enteros en consola.
- `ImportacionPage` diferencia "fallo cargando contexto" de "sin cuentas", muestra CTA de reintento y bloquea doble confirmacion por ref interna.
- `ExtractosPage` captura errores de resumen/filas/preferencias/auditoria, revierte preferencias de columnas si el PATCH falla y no permite ocultar la ultima columna visible.
- `CuentaDetailPage` elimina el limite silencioso de 500 movimientos y usa paginacion real con `PageSizeSelect`.
- `AiChatPanel` muestra estado de permiso/configuracion cuando IA esta bloqueada; ya no queda un panel vacio.
- `RoleGuard` devuelve un `EmptyState` de permisos en vez de redirigir silenciosamente.
- `RevisionPage` calcula si el usuario puede editar cada fila con `cuenta_id`/`titular_id`; si no puede, muestra `Solo lectura` y no renderiza botones de escritura.
- `RevisionDtos` y `RevisionService` exponen `TitularId` en comisiones y seguros para que el frontend pueda aplicar permisos por titular.
- `AppSelect`, tablas de importacion/extractos, modales de auditoria, errores de formulario y columnas monetarias reciben reglas de overflow para textos largos, importes grandes y datasets anchos.
- `Program.cs` registra un manejador global de excepciones: log interno con path, respuesta JSON saneada y sin stack trace al cliente.
- `20260512110000_HardenReleaseSecurityPermissions.Designer.cs` registra la migracion de hardening RLS. Sin ese descriptor EF compilaba la clase, pero no la aplicaba.
- `ImportacionServiceTests` se actualiza para esperar `Clave de columna extra duplicada`, que es el contrato vigente.

### Por que

Los casos feos no son periferia: son donde una app financiera demuestra si esta lista o solo bonita. Un 403 silencioso, 500 opaco, tabla que se desborda o limite oculto de 500 movimientos acaba en datos mal interpretados. Eso es el tipo de bug que parece pequeno hasta que alguien toma una decision con una pantalla incompleta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `dotnet build "Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" --no-restore -p:UseAppHost=false -m:1` con `OutDir` aislado: OK, 1 warning conocido de Hangfire obsoleto.
- `RevisionServiceTests`: 5/5 OK.
- Backend no Docker: primero 222/223 por test obsoleto de importacion; tras corregirlo, 223/223 OK.
- Docker fuera del sandbox: `29.4.2`.
- Backend completo con Testcontainers/PostgreSQL: primero 224/225 por migracion RLS no descubierta; tras anadir `.Designer.cs`, 225/225 OK.
- `frontend/dist` sincronizado a `backend/src/AtlasBalance.API/wwwroot`: `dist_files=61 wwwroot_files=61`.

### Pendiente real

- E2E autenticado/visual con datos reales sigue pendiente en `REGISTRO_BUGS.md`. Los gates de build, TypeScript, lint, RLS y suite backend completa ya estan cerrados.

## 2026-05-12 - V-01.06 - Auditoria UI prepublicacion

### Que cambio

- Nuevo `frontend/src/hooks/useDialogFocus.ts` para dialogs y sheets: foco inicial, trap de Tab/Shift+Tab, Escape opcional y restauracion del foco anterior.
- `UsuarioModal`, `CreateTokenModal`, `TokenCreatedModal`, `AuditCellModal`, `SessionTimeoutWarning`, modales inline de entidades y bottom nav mobile adoptan semantica `role="dialog"`/`aria-modal` y foco controlado.
- `DatePickerField` acepta `label`; `AddRowForm` deja de depender de placeholders para cuenta, fecha, concepto, monto, saldo y columnas extra.
- Login y cambio de password exponen errores con `role="alert"`, `aria-invalid` y `aria-describedby`.
- Importacion etiqueta los checkboxes de fila valida con `aria-label`.
- `iaAvailabilityStore` centraliza `/ia/config` con TTL y polling unico desde `Layout`; topbar, sidebar y bottom nav consumen el store.
- `AiChatPanel` se carga con `React.lazy`; `qrcode` se importa dinamicamente solo en MFA.
- `useSessionTimeout` calcula inactividad con refs y solo actualiza estado cuando cambia el aviso o el modal esta visible.
- `DashboardPrincipalResponse` incorpora `SaldosPorCuenta`; `DashboardService` reutiliza `BuildSaldosPorCuenta` para principal y titular.
- `CuentasPage` consume `principal.saldos_por_cuenta` y elimina el fan-out por titular que multiplicaba llamadas HTTP.
- `EvolucionChart` traduce colores legacy de configuracion a tokens CSS; `ConcentracionDonutCharts` usa `--chart-series-*` y la leyenda no colorea texto con el color del segmento.
- `variables.css`, `shell.css`, `dashboard.css` y `revision-ai.css` reducen blur/radios/sombras, corrigen tokens inexistentes y aseguran targets tactiles minimos en controles criticos.
- `Build-Release.ps1` limpia `wwwroot` con error estricto y comprueba que queda vacio antes de copiar assets nuevos.

### Por que

El informe UI no era cosmetica: habia modales que atrapaban mal el teclado, formularios que dependian de placeholder, polling duplicado, bundle inicial innecesariamente pesado y colores de graficas que se saltaban el tema. Eso en una app financiera no es "detalle"; es friccion diaria.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- API build normal: bloqueado por `AtlasBalance.API.dll` en uso en `bin\Debug`.
- API build con `OutDir` aislado en `.codex-verify`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `npm.cmd run build` dentro del sandbox: bloqueado por `spawn EPERM` conocido de Vite/Rolldown.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`; `dist_files=62 wwwroot_files=62`.
- Busqueda estatica en `wwwroot`: sin `AiChatPanel-B-aUHQbU`, `surface-raised`, `transition-base`, colores legacy de grafica ni `dashboard/titular` en el bundle servido.

## 2026-05-12 - V-01.06 - Hardening de seguridad prepublicacion

### Que cambio

- `vite.config.ts` usa un logger con redaccion de `Cookie`, `Set-Cookie`, `Authorization`, CSRF, JWT y tokens comunes antes de escribir errores de proxy.
- `api.ts` deja de volcar cuerpos completos de error en consola y registra solo mensajes saneados.
- MFA recordado requiere `remember_device=true` explicito y dura 30 dias.
- `AuthService` mantiene throttle por IP+email y anade contador por IP para password spraying.
- `/api/health` queda reducido a `{ status = healthy }`.
- `UserAccessService` separa lectura real (`PuedeVerCuentas`) de permisos operativos; `ExtractosController` aplica la misma regla en lecturas.
- Exportacion manual/descarga exige lectura de cuenta; revision de estados exige `PuedeEditarLineas`.
- Nueva migracion `20260512110000_HardenReleaseSecurityPermissions`:
  - scopes RLS firmados `data`, `write`, `export`, `revision`;
  - lectura normal sin `PuedeImportar`/write;
  - politica de exportacion basada en `PuedeVerCuentas`;
  - politica de revision basada en `PuedeEditarLineas`.
- `Build-Release.ps1` siempre ejecuta `npm ci`, falla sin `package-lock.json`, valida `dotnet restore --locked-mode` y exige firma RSA salvo `-AllowUnsignedLocal`.
- NuGet usa `RestorePackagesWithLockFile` y lockfiles por proyecto.
- CI usa `ubuntu-24.04`, `global.json`, `.node-version`, `--locked-mode` y patrones high-confidence adicionales.
- El instalador valida identificadores PostgreSQL con regex estricta y aplica ACL restrictiva a `appsettings.Production.json`, PFX, DataProtection y credenciales one-shot.
- Backup/Watchdog ya no exponen stderr/rutas internas como estado visible; el detalle queda en logs locales protegidos.

### Por que

Habia tres fallos publicables: tokens de sesion en logs de desarrollo, permisos operativos abriendo lectura financiera y release dependiente de `node_modules` local. Eso no es deuda estetica; es superficie de ataque real.

### Verificacion

- `cyber-neo` secret scan: 0 findings.
- CI-style tracked secret scan: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` conocido de Vite/Rolldown.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list "Atlas Balance/backend/AtlasBalance.sln" package --vulnerable --include-transitive`: 0 vulnerabilidades.
- `dotnet restore "Atlas Balance/backend/AtlasBalance.sln" --locked-mode`: OK fuera del sandbox.
- `dotnet build "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore -p:UseAppHost=false -m:1`: OK con warnings conocidos por `apphost.exe`/cache bloqueados.
- Tests filtrados no Docker: 34/34 OK.
- `RowLevelSecurityTests`: bloqueo superado en el hardening posterior de la misma fecha; suite backend completa 225/225 OK con Docker/Testcontainers fuera del sandbox.

## 2026-05-12 - V-01.06 - Revision permite descartar falsos positivos

### Ajuste posterior

- La vista `Todas/Todos` deja de incluir `DESCARTADA`.
- En comisiones, `Todas` muestra solo `PENDIENTE` y `DEVUELTA`.
- En seguros, `Todos` muestra solo `PENDIENTE` y `CORRECTO`.
- El descarte pasa a ser un boton cuadrado con solo icono de cruz visible; `aria-label` y `title` conservan el significado accesible.
- Las descartadas quedan disponibles solo desde el filtro explicito `Descartadas/Descartados`.

### Verificacion del ajuste

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `RevisionServiceTests`: 5/5 OK fuera del sandbox con `-p:OutDir=C:\tmp\atlas-revision-discard-test-out\`.
- API local saludable en `localhost:5000`, PID `9632`.
- `index.html` sirve `assets/index-Pl0LJUu6.js`, que referencia `RevisionPage-DoqGux5L.js`.
- Navegador interno abre `/revision` sin overlay, pero redirige a login por no tener sesion autenticada.

### Que cambio

- `RevisionService` anade el estado persistido `DESCARTADA` para `COMISION` y `SEGURO`.
- `NormalizeEstadoFilter` acepta `DESCARTADA`, plurales, `IGNORADA` y alias `NO_ES_COMISION`/`NO_ES_SEGURO`.
- `RevisionPage` muestra el filtro `Descartadas/Descartados`.
- En comisiones se puede pulsar `No es comision`; en seguros, `No es seguro`.
- Las filas descartadas se pueden restaurar a `PENDIENTE`.
- El bundle frontend se recompila y se copia a `backend/src/AtlasBalance.API/wwwroot`.

### Por que

La deteccion automatica es un detector, no un veredicto. Si un movimiento contiene una palabra que parece comision o seguro pero no lo es, obligar al usuario a dejarlo como pendiente o marcarlo como revisado falsea el control. `DESCARTADA` conserva trazabilidad y permite filtrar esos falsos positivos sin borrarlos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro fallo por el `spawn EPERM` conocido de Vite/Rolldown.
- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter RevisionServiceTests -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-revision-discard-test-out\\ --no-restore` fuera del sandbox: 5/5 OK.
- `wwwroot/assets/RevisionPage-*.js` contiene `No es comision`, `No es seguro`, `Descartada` y `Descartado`.
- API local saludable en `localhost:5000`, PID `32520`.

## 2026-05-12 - V-01.06 - KPIs del dashboard con variacion compacta

### Que cambio

- `frontend/src/pages/DashboardPage.tsx` mantiene el calculo de variacion de `Saldo total`, `Ingresos periodo`, `Egresos periodo`, `Disponible` e `Inmovilizado`.
- Los helpers visibles ya no anaden `vs inicio` ni `vs anterior`; ahora muestran solo el porcentaje con signo.
- No cambia la semantica de color ni la fuente de datos.

### Por que

El dashboard ya comunica el contexto por posicion y periodo seleccionado. Repetir `vs inicio` y `vs anterior` en cinco tarjetas mete ruido en una zona que debe leerse de un vistazo. El numero importa; la coletilla no.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- Busqueda estatica sin restos de `vs inicio` ni `vs anterior` en `DashboardPage.tsx`.

## 2026-05-12 - V-01.06 - Revision sin 500 por traduccion Npgsql

### Que cambio

- `RevisionService` deja de proyectar la query base a un record posicional `RevisionRawRow(...)`.
- La proyeccion interna pasa a una clase con propiedades `init` y `new RevisionRawRow { ... }`.
- El filtro de comisiones por importe (`Monto > umbral || Monto < -umbral`) sigue ejecutandose en SQL, pero ahora EF/Npgsql puede inlinear la propiedad proyectada.
- Se anade una regresion en `RevisionServiceTests` que construye la query con proveedor Npgsql y llama a `ToQueryString()` sobre el filtro de `Monto`, sin levantar PostgreSQL ni Docker.

### Por que

La pantalla `Revision` devolvia HTTP 500 al cargar comisiones porque EF Core no podia traducir una condicion sobre `RevisionRawRow.Monto` cuando `RevisionRawRow` era un record construido por constructor posicional. El test existente usaba `InMemoryDatabase`, que no traduce a SQL y por tanto no detectaba el fallo. Esa prueba era demasiado comoda para un bug de base de datos.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter RevisionServiceTests -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-revision-test-out\\ --no-restore` fuera del sandbox: 5/5 OK.
- Intentos descartados:
  - test directo bloqueado por `AtlasBalance.API.dll` en uso;
  - salida aislada con `BaseOutputPath/BaseIntermediateOutputPath` bloqueada por permisos en sandbox y despues por AssemblyInfo duplicados al cambiar `BaseIntermediateOutputPath`;
  - se cambio a `OutDir` aislado, manteniendo `obj` en su ruta normal.
- API local reiniciada por `Start-BackendDev.ps1`; el comando fue interrumpido por timeout de conversacion, pero el healthcheck posterior confirmo API saludable en `localhost:5000`, PID `42848`.

## 2026-05-12 - V-01.06 - EvolucionChart reserva eje Y para importes compactos

### Que cambio

- `EvolucionChart.tsx` cambia la reserva del eje Y a un rango adaptativo de 52-116 px.
- `getEvolutionAxisWidth` ahora recibe el dominio calculado y estima la etiqueta mas larga usando valores de serie, extremos del dominio y cero.
- Los ticks de X/Y declaran estilo estable: color secundario, fuente monoespaciada, `fontSize: 12` y numeros tabulares.
- El ancho se estima por anchura aproximada de cada caracter, no por longitud bruta de string.
- El margen izquierdo del `LineChart` baja a 0, el margen derecho a 18, el padding del eje X a 8 px y el `tickMargin` del eje Y a 8 px.

### Por que

Los importes laterales se cortaban porque el ancho maximo de 72 px era insuficiente para etiquetas como `15,6 M EUR`, especialmente cuando el tick lo generaba Recharts desde el dominio con padding y no desde un punto exacto de datos. Encoger texto habria sido una chapuza: en una pantalla financiera, el numero debe leerse entero.

La primera correccion era demasiado conservadora: evitaba el recorte, pero dejaba un hueco lateral visible. La estimacion por caracteres corrige ese exceso porque no trata igual un espacio, una coma, un digito y una letra de divisa.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- No se ejecuto build Vite ni validacion con servidor temporal por la incidencia conocida de `spawn EPERM`/servidores colgados; el cambio queda cubierto por lint y TypeScript.

## 2026-05-11 - V-01.06 - Parser IA sin mensaje generico `respuesta malformada`

### Que cambio

- `AtlasAiService.BuildProviderResponseErrorMessage` ya no devuelve el literal generico `El proveedor de IA devolvio una respuesta malformada`.
- Los errores de shape no compatible se expresan como `respuesta de chat compatible (kind)`, manteniendo la categoria tecnica (`invalid_json`, `missing_choices`, `unsupported_content`, etc.).
- El `catch (JsonException)` global se reclasifica como `provider_response_processing_error` y registra `provider_response_error_kind=json_processing_error`.
- `ParseProviderResponse` incorpora tolerancia adicional:
  - payloads `data:`/SSE con chunks JSON aunque la request sea no streaming;
  - `choices[0].delta.content`;
  - `output_text` top-level;
  - contenido anidado como `text.value`, `output_text`, `content` o arrays de partes.

### Por que

El mensaje generico estaba funcionando como una caja negra: ocultaba si el fallo era JSON invalido, shape no compatible, streaming accidental o procesamiento interno. Eso hace que cada incidencia parezca la misma y empuja a repetir parches. La app ahora intenta recuperar las variantes razonables y, si no puede, da una categoria concreta.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter "AtlasAiServiceTests|ConfiguracionControllerTests" -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-ai-test-bin-provider-parser-loop\\ --no-restore --logger "console;verbosity=normal"` fuera del sandbox: 68/68 OK.
- Busqueda estatica: el literal viejo solo queda en test de regresion, no en codigo productivo.

## 2026-05-11 - V-01.06 - IA financiera con rankings deterministas por cuenta

### Que cambio

- `AtlasAiService.AskAsync` intenta resolver antes del proveedor las intenciones financieras soportadas de ranking por cuenta/titular/divisa.
- La V1 cubre metricas `gastos`, `ingresos` y `neto`; periodos `mes actual`, `ultimos 30 dias`, `mes anterior`, `trimestre actual` y `ano actual`.
- La consulta determinista aplica `_userAccessService.ApplyCuentaScope`, agrupa por titular, cuenta y divisa, y calcula:
  - `Ingresos = SUM(monto > 0)`
  - `Gastos = -SUM(monto < 0)`
  - `Neto = SUM(monto)`
  - contador de movimientos segun metrica.
- La salida se secciona por divisa para no mezclar monedas y limita el ranking a 10 por defecto.
- Esta ruta no construye contexto LLM, no exige desencriptar API key y no crea llamada HTTP al proveedor. Mantiene el contrato de `IaChatResponse` con `TokensEntradaEstimados=0`, `TokensSalidaEstimados=0` y `CosteEstimadoEur=0`.
- La auditoria `IA_CONSULTA` marca `deterministic=true`, `deterministic_kind=account_ranking`, metrica, fechas de periodo, filas devueltas y movimientos analizados. No guarda prompt, respuesta completa ni datos financieros crudos.
- La ruta LLM queda mas estricta: el prompt indica que una seccion agregada/ranking ya calculado es fuente primaria y `CleanProviderAnswer`/`ContainsInternalAnalysisLeak` eliminan o rechazan metacomentarios tipo `It seems`, `maybe`, `Actually` cuando llegan visibles.

### Por que

Pedirle a un LLM que sume y ordene movimientos financieros desde texto parcial es una mala arquitectura. El modelo puede sonar convincente y aun asi inventar cuentas, mezclar divisas o copiar razonamiento interno. Para datos contables, el backend debe calcular con SQL/EF y dejar el proveedor para redaccion o preguntas no deterministas.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter "AtlasAiServiceTests|ConfiguracionControllerTests" -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-ai-test-bin-financial-ranking\\ --no-restore --logger "console;verbosity=normal"` fuera del sandbox: 66/66 OK.
- Intentos dentro del sandbox: bloqueados por `AtlasBalance.API.dll` en uso y `Access denied` al escribir en `C:\tmp`.

## 2026-05-11 - V-01.06 - Parser IA tolerante a respuestas OpenRouter no triviales

### Que cambio

- `AtlasAiService.ParseProviderResponse` clasifica respuestas del proveedor en categorias tecnicas:
  - `provider_error` para errores embebidos en HTTP 200.
  - `provider_empty_response` para `choices` vacio, `content=null` o ausencia de texto util.
  - `provider_unusable_response` para `refusal`, `content_filter`, `length` y tool calls sin contenido.
  - `provider_malformed_response` para JSON invalido, shape no-chat o contenido de tipo no soportado.
- El parser acepta tres formas utiles: `choices[0].message.content` como string, `content` como array de partes de texto y `choices[0].text`.
- Las peticiones al proveedor incluyen `stream=false`, `Accept: application/json` y cabecera `X-OpenRouter-Title`.
- Los errores HTTP 429/503 leen `Retry-After` y lo trasladan al mensaje visible y auditoria sin dormir la request.
- La auditoria IA incorpora `provider_response_error_kind`, `finish_reason`, cliente HTTP, uso de fallback y detalle saneado; no guarda prompt, respuesta completa, datos financieros ni secretos.

### Por que

OpenRouter normaliza hacia el contrato Chat Completions, pero aun asi documenta casos de no contenido, errores de proveedor y formas distintas para streaming/no streaming. El parser anterior era demasiado fragil: confundia refusals, truncados, filtros de contenido, `content` por partes y errores 200 con JSON roto. Eso daba al usuario un mensaje opaco y dejaba poca informacion operativa.

No se fuerza `response_format=json_schema` porque los modelos gratis permitidos actuales no soportan de forma uniforme `response_format`/`structured_outputs`; activarlo globalmente romperia parte de la allowlist.

### Verificacion

- `dotnet test .\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj --filter "AtlasAiServiceTests|ConfiguracionControllerTests" -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-ai-test-bin-openrouter-parser\\ --no-restore --logger "console;verbosity=normal"` fuera del sandbox: 61/61 OK.
- Intentos dentro del sandbox: bloqueados por `AtlasBalance.API.dll` en uso y `Access denied` al escribir salida aislada en `C:\tmp`.

## 2026-05-11 - V-01.06 - IA OpenRouter sin proxy heredado y errores TLS claros

### Que cambio

- Los `HttpClient` de IA ya no usan proxy automatico como fallback por defecto. Si `Ia:UseSystemProxy=false` y `Ia:ProxyUrl` esta vacio, `openrouter`, `openrouter-fallback`, `openai` y `openai-fallback` salen directo.
- Si hace falta proxy real, se configura de forma explicita con `Ia:UseSystemProxy=true` o `Ia:ProxyUrl`.
- `AtlasAiService.ShortTransportMessage` recorre toda la cadena de excepciones y clasifica errores de TLS/certificado, proxy local roto, DNS y conexion rechazada.
- El mensaje de red muestra detalle principal/fallback cuando difieren, y la auditoria usa el mismo saneado sin prompt ni API key.
- Nueva regresion cubre el caso `.NET` `Authentication failed, see inner exception` para que no vuelva a llegar crudo al chat.

### Por que

El fallback a proxy automatico era un pie metido en una trampa: en Windows/.NET el proxy por defecto puede venir de variables de entorno como `HTTP_PROXY`, `HTTPS_PROXY` o `ALL_PROXY`. Esta maquina ya habia heredado `127.0.0.1:9`; repetir ese camino era dejar abierta la misma averia. El comportamiento seguro en una app on-prem es salida directa por defecto y proxy solo si se configura.

### Verificacion

- `dotnet test "tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests -p:UseAppHost=false -p:OutputPath=C:\tmp\atlas-ai-test-bin --no-restore --verbosity minimal`: 42/42 OK fuera del sandbox.

## 2026-05-11 - V-01.06 - Login sin API absoluta y arranque backend verificable

### Que cambio

- `frontend/src/services/api.ts` fija `baseURL: '/api'`. Se elimina `VITE_API_URL` del contrato TypeScript del frontend.
- `frontend/.env.local` queda como aviso local: no se debe apuntar el cliente a `localhost:5000`.
- `frontend/dist` se recompila y se copia a `backend/src/AtlasBalance.API/wwwroot`.
- `scripts/Start-BackendDev.ps1` compila con `UseAppHost=false`, arranca el DLL, limpia proxies de entorno, escribe logs/PID y espera `http://localhost:5000/api/health`.
- `scripts/Start-Dev.ps1` deja de matar todos los procesos `dotnet` y no declara listo el entorno sin healthcheck.
- `scripts/Launch-AtlasBalance.ps1`, `start-backend.bat` y `start-frontend.bat` usan los arranques endurecidos.
- `/api/health` anade `started_at`, `version`, `pid` y `environment`.

### Por que

Atlas Balance sirve el frontend desde el backend en produccion y usa proxy Vite en desarrollo. Compilar `http://localhost:5000/api` en el cliente era la causa perfecta del `Network Error`: en LAN `localhost` es el equipo del usuario, no el servidor. Ademas, los scripts antiguos abrian ventanas y seguian como si todo hubiese ido bien aunque la API no escuchara. Eso no era un problema de login; era un arranque sin contrato.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox por `spawn EPERM` conocido de Vite/Rolldown.
- `dotnet build AtlasBalance.API.csproj -p:UseAppHost=false --no-restore`: OK con warnings no bloqueantes.
- `http://localhost:5000/api/health`: 200 con version/PID.
- `http://localhost:5173/api/health`: 200 via proxy.
- Busqueda en `dist` y `wwwroot`: sin `VITE_API_URL` ni `http://localhost:5000/api`.

## 2026-05-11 - V-01.06 - Chat IA: salida final sin razonamiento interno

### Que cambio

- `AtlasAiService.BuildProviderRequest` envia `reasoning: { exclude: true }` en todos los payloads de OpenRouter.
- El prompt de sistema ordena devolver solo la respuesta final visible para el usuario, en espanol, sin analisis interno, borradores, pasos ni frases como `we need to answer`, `analysis`, `reasoning`, `thinking` o `final answer`.
- El prompt tambien prohibe placeholders tipo `[PERSON_NAME]`, `[ACCOUNT_NAME]` o `<name>`; si falta un dato, debe decir `no consta en el contexto`.
- `CleanProviderAnswer` limpia defensivamente la respuesta del proveedor antes de construir `IaChatResponse`:
  - elimina bloques `<think>`, `<thinking>`, `<reasoning>` y `<analysis>`;
  - corta prefacios iniciales de razonamiento hasta la respuesta;
  - quita etiquetas de salida como `Final:` o `Respuesta final:`;
  - sustituye placeholders anonimizados por `no consta en el contexto`.
- `AtlasAiServiceTests` cubre el saneado de respuesta y que OpenRouter incluya `reasoning.exclude`.

### Por que

OpenRouter puede devolver tokens de razonamiento en `message.reasoning` si el modelo los genera. La documentacion oficial indica que `reasoning.exclude: true` evita devolverlos. Pero el ejemplo visto en la UI (`We need to answer...`) venia dentro de `message.content`, no como campo `reasoning`; ahi el proveedor ya ha metido su razonamiento en el texto final. Por eso hacen falta dos barreras: contrato de OpenRouter y limpieza backend. Dejarlo al frontend seria tarde y fragil.

### Verificacion

- Documentacion oficial revisada: OpenRouter `Reasoning Tokens`, seccion `Excluding Reasoning Tokens from Response`.
- Primer `dotnet test` bloqueado por `AtlasBalance.API.dll` en uso por PID `25776`; se paro ese PID exacto y se repitio.
- `dotnet test ... --filter FullyQualifiedName~AtlasAiServiceTests -p:UseAppHost=false --no-restore --verbosity minimal`: 41/41 OK.
- `dotnet test ... --filter "FullyQualifiedName~AtlasAiServiceTests|FullyQualifiedName~ConfiguracionControllerTests" -p:UseAppHost=false --no-restore --verbosity minimal`: 47/47 OK.
- Warning residual: MSB3101 al escribir cache `obj`, no bloqueante.

## 2026-05-11 - V-01.06 - OpenRouter Auto limitado a 3 modelos en `models`

### Que cambio

- `AiConfiguration.OpenRouterMaxFallbackModels` fija el limite local en 3.
- `OpenRouterAutoFallbackModels` deja de derivarse de toda la allowlist y pasa por una terna explicita: `nvidia/nemotron-3-super-120b-a12b:free`, `google/gemma-4-31b-it:free` y `minimax/minimax-m2.5:free`.
- `AtlasAiService` mantiene la ruta `models` para `openrouter/auto`, pero ahora el payload cumple el limite efectivo de OpenRouter.
- `BuildProviderHttpErrorMessage` reconoce el 400 `'models' array must have 3 items or fewer` y lo explica como fallo de limite de fallback.
- `AtlasAiServiceTests` parsea el JSON enviado y comprueba que `models` contiene exactamente 3 modelos permitidos.

### Por que

OpenRouter documenta `models` como fallback ordenado entre modelos y `openrouter/auto + plugins.auto-router.allowed_models` como Auto Router sobre un pool curado. En Atlas Balance no se puede volver al Auto Router abierto porque ya fallo por interseccion vacia con la allowlist gratis. El error real nuevo marco la otra frontera: `models` no puede llevar seis candidatos; el maximo operativo es 3.

### Verificacion

- `dotnet test ... --filter "AtlasAiServiceTests|ConfiguracionControllerTests"` dentro del sandbox: bloqueado por `Access denied` al escribir en `C:\tmp`.
- El mismo test fuera del sandbox: 46/46 OK tras corregir una asercion textual del test nuevo.

## 2026-05-11 - V-01.06 - Auto OpenRouter corregido para cuentas con modelos gratis restringidos

### Que cambio

- `Auto` sigue guardandose como `openrouter/auto`, pero `AtlasAiService` ya no llama al Auto Router de OpenRouter con `plugins.auto-router.allowed_models`.
- Para `Auto`, el backend envia `models` con maximo tres slugs gratis permitidos en orden de fallback: Nemotron, Gemma y MiniMax. gpt-oss, GLM y Qwen Coder siguen disponibles como seleccion manual.
- `ProviderRuntimeModel` resuelve `openrouter/auto` al primer modelo gratis permitido para auditoria y metadatos cuando OpenRouter no devuelve `model`.
- El mensaje 404 `No models match your request and model restrictions` ahora se explica como incompatibilidad de restricciones, no como un slug obsoleto que el usuario deba volver a guardar.
- El selector frontend cambia la etiqueta a `Auto (gratis permitido)`.

### Por que

La suposicion anterior era incorrecta. La documentacion oficial de OpenRouter dice que Auto Router elige dentro de una bolsa curada propia; `allowed_models` solo restringe esa bolsa. Los modelos `:free` permitidos por esta app no tienen por que estar en esa bolsa, asi que la interseccion puede quedar vacia y OpenRouter devuelve `No models match your request and model restrictions`.

La opcion robusta para una cuenta restringida a modelos gratis es mandar la lista exacta de modelos gratis permitidos mediante `models`, dejando que OpenRouter haga fallback entre ellos sin salir de la allowlist.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests|ConfiguracionControllerTests`: 45/45 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Bundle sincronizado: `Auto (gratis permitido)` aparece en `aiModels-BjnwCRyE.js`.

## 2026-05-11 - V-01.06 - Selector IA discreto en cabecera

### Que cambio

- `AiChatPanel` mueve el proveedor y selector de modelo a la cabecera del panel.
- El selector conserva `aria-label` y etiqueta `sr-only`, pero ya no muestra la etiqueta visual `Modelo`.
- `getCompactModelLabel` acorta las etiquetas solo en el chat: `Auto (gratis permitido)` pasa a `Auto` y se retira `(free)` del texto visible.
- `revision-ai.css` cambia el control a un estilo secundario: texto tenue, fondo transparente, 32px de alto, borde en hover/focus y foco visible.
- El build final queda copiado a `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El selector de modelo es util, pero hacerlo tan grande era mala jerarquia: competia con la pregunta, que es la accion principal. En una herramienta financiera interna, las opciones avanzadas deben estar disponibles sin robar atencion.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- Playwright headless con `setContent`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Chromium.
- Validacion visual: toolbar dentro de cabecera, selector de 32px, selector menor que el 55% del textarea y sin overflow horizontal.
- `wwwroot` sincronizado con `Copy-Item`.

## 2026-05-11 - V-01.06 - OpenRouter Auto acotado a seis modelos gratis

### Que cambio

- Estado actual: esta estrategia quedo sustituida el mismo dia por `models` con maximo 3 candidatos. Se conserva como historico del intento anterior.
- OpenRouter conserva `openrouter/auto` como opcion por defecto visible y guardada.
- `AiConfiguration.OpenRouterModels` permite `openrouter/auto` mas estos seis slugs exactos: `nvidia/nemotron-3-super-120b-a12b:free`, `google/gemma-4-31b-it:free`, `minimax/minimax-m2.5:free`, `openai/gpt-oss-120b:free`, `z-ai/glm-4.5-air:free` y `qwen/qwen3-coder:free`.
- En ese intento, cuando el usuario usaba `Auto`, `AtlasAiService` enviaba `model=openrouter/auto` con `plugins.auto-router.allowed_models` limitado a esos seis modelos.
- Los modelos exactos gratis siguen sin `provider.zdr=true`. Gemma, MiniMax y gpt-oss se pinchan a proveedores verificados; Nemotron, GLM y Qwen se envian sin pin artificial porque no hay proveedor exacto verificado en codigo.
- La respuesta del proveedor se parsea tambien para leer `model`; la auditoria y `IaChatResponse.Model` reflejan el modelo real usado cuando OpenRouter lo devuelve.
- El selector frontend muestra `Auto (elige el mejor)` por defecto y los seis modelos manuales. `Detalles de IA` convierte el slug devuelto a etiqueta legible cuando esta en la lista.

### Por que

OpenRouter Auto Router si elige el mejor modelo para cada prompt, pero dejarlo abierto es mala operativa: puede escoger modelos fuera de la allowlist de la cuenta o modelos de pago. La comprobacion posterior demostro que para esta cuenta restringida la via robusta no es `allowed_models`, sino `models` con un fallback de maximo 3 candidatos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests|ConfiguracionControllerTests`: 44/44 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Bundle sincronizado: `Auto (elige el mejor)` y los seis modelos OpenRouter aparecen en `aiModels-BJ_TwRsn.js`.

## 2026-05-11 - V-01.06 - Enter y modelo por consulta en chat IA

### Que cambio

- `AiChatPanel` intercepta `Enter` en el textarea para enviar la consulta. `Shift+Enter` mantiene el salto de linea.
- El chat incorpora un selector compacto de modelo, limitado al proveedor activo (`OpenRouter` u `OpenAI`).
- `frontend/src/utils/aiModels.ts` centraliza proveedores, modelos permitidos, normalizacion y modelo por defecto. `ConfiguracionPage` y `AiChatPanel` usan la misma fuente.
- `IaChatRequest` acepta `Model`.
- `IaController` pasa el modelo solicitado a `AtlasAiService.AskAsync`.
- `AtlasAiService` aplica el modelo solicitado solo a la llamada actual, lo valida contra `AiConfiguration.IsAllowedModel` y bloquea cualquier valor fuera de allowlist antes de construir la peticion al proveedor.
- `AtlasAiServiceTests` cubre dos regresiones: modelo solicitado permitido sin cambiar `ai_model` global, y modelo solicitado no permitido bloqueado sin llamada HTTP ni prompt en auditoria.

### Por que

Enviar con `Enter` es el comportamiento esperado en un chat. Lo raro era obligar al boton.

El selector se implementa por consulta, no como guardado global. Cambiar el modelo global desde un chat normal seria mala seguridad y mala operativa: una persona podria cambiar coste/comportamiento para todos sin pasar por `Configuracion > Revision e IA`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests`: 35/35 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Playwright headless con `setContent` y CSS compilado: selector visible, dentro del panel, formulario visible y sin overflow horizontal.

## 2026-05-11 - V-01.06 - IA permite consultas financieras administrativas

### Que cambio

- `AtlasAiService.IsQuestionWithinAllowedDomain` amplia el vocabulario financiero permitido: gastos, ingresos, importes, montos, totales/globales, impuestos, Seguridad Social, retenciones, cuotas de autonomos, recibos, facturas, cargos, cobros, comisiones, seguros y nominas.
- El prompt de sistema declara esas consultas como financieras permitidas aunque usen vocabulario fiscal o administrativo.
- La restriccion externa ya no dice `temas legales` de forma bruta; ahora rechaza asesoramiento legal externo, sin bloquear preguntas sobre impuestos o Seguridad Social presentes en los extractos.
- `BuildFinancialContextAsync` anade resumenes especificos para `ultimo mes`/`ultimos 30 dias` y `mes pasado`/`mes anterior`.
- Se agregan categorias de contexto `IMPUESTOS/SEGURIDAD SOCIAL DETECTADOS` y `RECIBOS/FACTURAS DETECTADOS`.
- `AtlasAiServiceTests` cubre la pregunta exacta `cual ha sido los gastos globales del ultimo mes` y variantes de Seguridad Social, impuestos, recibos, facturas, comisiones, seguros e ingresos.

### Por que

La barrera anterior era demasiado estrecha para una app de tesoreria. Bloquear recetas esta bien. Bloquear `Seguridad Social`, `impuestos` o `recibos` dentro de una aplicacion financiera es pegarse un tiro en el pie. La regla correcta es permitir todo lo que sea dato financiero propio y seguir cortando temas externos o asesoramiento fuera del producto.

### Verificacion

- `AtlasAiServiceTests`: 33/33 OK con `UseAppHost=false`.
- Hubo bloqueos previos por binarios `.dll/.exe` en uso y permisos en rutas temporales; se resolvio parando los procesos dotnet locales que bloqueaban el build.
- Persisten warnings conocidos de `Access denied` al intentar borrar `.exe` y cache de referencias, no bloqueantes.

## 2026-05-11 - V-01.06 - Render legible de respuestas IA

### Que cambio

- `AiChatPanel` separa la respuesta del proveedor de los metadatos tecnicos. La respuesta visible ya no concatena `Movimientos analizados`, `Modelo`, `Tokens` y `Coste` como texto plano al final.
- Se agrega `AiMessageContent`, un renderer React local para respuestas IA. Convierte parrafos/listas/negritas simples a JSX seguro y transforma tablas Markdown en bloques dato/valor.
- `revision-ai.css` cambia el panel de grid fijo a flex column. `.ai-chat-messages` pasa a ocupar el espacio flexible real con `min-height: 0`, evitando la fila vacia que dejaba la respuesta arriba y el formulario abajo.
- Las burbujas IA aceptan `overflow-wrap: anywhere`, `min-width: 0` y ancho completo para que cadenas largas de tablas/modelos no se corten.
- `AtlasAiService.BuildSystemMessage` instruye al proveedor a responder sin tablas Markdown, pipes ni asteriscos de negrita.

### Por que

El fallo era doble: el layout asignaba la zona flexible a una fila que no contenia los mensajes cuando no habia aviso de configuracion, y el frontend pintaba Markdown crudo dentro de un `<p>`. Las tablas Markdown generan cadenas largas con pipes y guiones; con `overflow-x: hidden` eso se ve como texto cortado. Arreglar solo CSS habria dejado asteriscos y tablas feas. Arreglar solo el prompt habria sido confiar demasiado en el modelo. Ambas cosas juntas son la solucion correcta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests`: 33/33 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Playwright headless con `setContent` y CSS compilado: sin Markdown crudo, sin overflow horizontal, mensaje dentro del panel y area de mensajes usando la altura disponible.

## 2026-05-10 - V-01.06 - Protocolo anti-encallamiento de agentes

### Que cambio

- Se agrega una seccion `Protocolo anti-encallamiento` en las instrucciones canonicas y copias operativas del proyecto.
- El protocolo fija un maximo de dos intentos por la misma via cuando una herramienta falla o se queda colgada.
- Se documentan los atascos conocidos de esta maquina: `spawn EPERM` en Vite/Rolldown/Chromium, servidores temporales sin cerrar, `robocopy /MIR`, `wwwroot` bloqueado, `apphost.exe` en uso, Docker/Testcontainers no disponible y limpiezas con `Access denied`.
- Se exige separar en la respuesta final lo verificado, lo bloqueado y lo pendiente, sin vender validacion visual cuando solo hubo checks estaticos.

### Por que

La regla anterior decia "corta si se encalla", pero era demasiado vaga. En la practica, el agente repetia la misma via esperando un resultado distinto. Eso no es perseverancia: es quemar tiempo. La nueva regla convierte los bloqueos conocidos en decisiones cerradas.

### Verificacion

- Revisadas `CLAUDE.md`, `AGENTS.md`, `Atlas Balance/CLAUDE.md` y `Atlas Balance/AGENTS.md`.
- Confirmada la presencia de `Protocolo anti-encallamiento` en las tres instrucciones largas y las reglas resumidas en `AGENTS.md`.
- No aplica build ni tests de runtime porque no cambia codigo.

## 2026-05-10 - V-01.06 - Fix definitivo 500 chat IA en resumenes

### Que cambio

- `AppendPeriodSummaryAsync` ya no recibe ni filtra un `IQueryable<AiExtractoRow>`.
- Los totales de mes actual, mes anterior, periodo anual y totales por mes se calculan desde `Extractos` enlazado con `Cuentas`.
- `AppendCategoryAsync` usa un predicado `Expression<Func<Extracto, bool>>` para conceptos y agrupa por divisa desde entidades EF.
- La busqueda de movimientos relevantes aplica el filtro de concepto sobre `Extracto.Concepto` y proyecta a `AiExtractoRow` solo al final.
- La prueba `AskAsync_Should_Build_Period_And_Category_Context` cubre ingresos/gastos, comisiones, seguros y totales mensuales en el prompt enviado al proveedor.

### Por que

El arreglo anterior solo corto el fallo del agregado de saldos actuales. El mismo patron malo seguia en otras ramas del contexto IA: construir `AiExtractoRow` dentro de la expresion LINQ y luego filtrar/ordenar/agrupar por sus propiedades. InMemory traga eso; Npgsql no. PostgreSQL no traduce magia de records C# inventados a mitad de consulta.

La regla correcta para este servicio queda clara: todo lo que deba ejecutarse en SQL se expresa con entidades y columnas escalares; los records de prompt se construyen despues.

### Verificacion

- `AtlasAiServiceTests` 22/22 OK con `UseAppHost=false`.
- Build del API OK con salida temporal y `NuGetAudit=false`; solo queda el warning existente de Hangfire PostgreSQL obsoleto.
- Verificador temporal contra PostgreSQL real OK dentro de transaccion revertida, con proveedor HTTP mockeado y sin coste externo.
- `GET http://localhost:5000/api/health`: `healthy`.

## 2026-05-10 - V-01.06 - Alineacion simetrica de Extractos

### Que cambio

- `system-coherence.css` mantiene el max-width generico de paginas en `1280px`, pero agrega una excepcion especifica para `.extractos-page` con `max-width: 1600px`.
- `extractos.css` ajusta `.extractos-header` a `align-items: center` para equilibrar el titulo con el bloque de filtros.
- `.extractos-page`, `.add-row-form` y `.extracto-table-section` reciben `min-width: 0` para que el contenido interno no fuerce overflow del contenedor centrado.

### Por que

`Extractos` es una hoja financiera, no una pantalla de lectura. El limite generico de `1280px` dejaba la tabla de 8 columnas empujando hacia la derecha y rompia la simetria: mucho aire a la izquierda, casi nada a la derecha. Darle ancho propio al contenedor corrige el eje visual sin inventar una tabla nueva.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` dentro del sandbox: bloqueado por `spawn EPERM`, incidencia ya conocida.
- `npm.cmd run build` fuera del sandbox: OK.
- Copia de `frontend/dist` a `backend/src/AtlasBalance.API/wwwroot`: OK fuera del sandbox.
- Playwright headless con CSS compilado: margenes laterales `98px/98px`, bordes de header/form/tabla con delta `0`, titulo centrado respecto al bloque de filtros y sin overflow horizontal.

## 2026-05-10 - V-01.06 - Header de cuenta alineado por grilla

### Que cambio

- `.cuenta-detail-page .dashboard-toolbar` usa una grilla desktop de dos columnas: identidad a la izquierda y acciones a la derecha.
- `.dashboard-toolbar-main` se aplana con `display: contents` solo en detalle de cuenta para que titulo y ficha participen directamente en la grilla.
- `.cuenta-heading-block` ocupa la fila superior izquierda; `.account-identity-strip` queda en la fila inferior izquierda.
- `.dashboard-toolbar-actions` ocupa la columna derecha desde la primera fila hasta la segunda, con `align-self: stretch` y contenido alineado arriba.
- En `max-width: 900px` todos los bloques vuelven a una sola columna y filas automaticas.

### Por que

Subir el panel `Periodo` no bastaba si la caja derecha quedaba visualmente corta. El objetivo correcto era alinear el bloque derecho con el conjunto real de la izquierda: empezar con el titulo y terminar con la ficha de datos de cuenta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` fuera del sandbox: OK.
- Playwright headless con CSS compilado y fixture del shell: `topDelta=0`, `bottomDelta=0.01`, `startsAboveIdentityBy=75.44`, pagina `1280px`.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`.

## 2026-05-10 - V-01.06 - Restriccion tematica del chat IA

### Que cambio

- `AtlasAiService.AskAsync` valida el ambito de la pregunta despues de comprobar IA global y permiso de usuario, y antes de validar proveedor/API key o llamar al modelo.
- Las preguntas fuera de ambito lanzan `IaOutOfScopeException` y quedan auditadas como `IA_CONSULTA_BLOQUEADA` con motivo `out_of_scope`.
- `IaController` devuelve `400 Bad Request` con el mensaje: `Solo puedo responder sobre Atlas Balance, su funcionamiento o los datos financieros disponibles.`
- El prompt de sistema declara el ambito permitido: Atlas Balance, funcionamiento de sus modulos y datos financieros del contexto.
- El modelo debe rechazar recetas, cocina, programacion, noticias, ocio, salud, temas legales y cualquier asunto externo.
- `AtlasAiServiceTests` cubre una receta de cocina y verifica que no se llama al proveedor ni se guarda el prompt en auditoria.

### Por que

Solo ponerlo en la interfaz seria una defensa de cartulina. La restriccion tiene que vivir en backend, donde esta el coste, el proveedor externo y la auditoria. La barrera local corta lo obvio sin gastar tokens; el prompt endurecido cubre lo ambiguo cuando la pregunta si parece relacionada con la app o los datos.

No se implementa como "IA general con una nota amable". Esa seria la forma rapida de acabar respondiendo recetas dentro de una app financiera.

### Verificacion

- Primer intento de `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests --no-restore`: bloqueado por `AtlasBalance.API.exe` en uso.
- Se paro el proceso local `AtlasBalance.API` que bloqueaba el binario.
- La verificacion final usa `-p:UseAppHost=false` para no depender del apphost `.exe` bloqueado en Windows.
- Repeticion del test focalizado: `AtlasAiServiceTests` 21/21 OK.
- Quedan warnings no bloqueantes de apphost y cache de referencias de tests con acceso denegado, ya vistos en este entorno.

## 2026-05-10 - V-01.06 - Cierres de UI con icono X

### Que cambio

- Se crea `frontend/src/components/common/CloseIconButton.tsx`, un boton comun solo-icono con `lucide-react/X`, `aria-label` obligatorio y `title` derivado.
- Se reemplazan los botones visibles `Cerrar` por `CloseIconButton` en toast, auditoria de celda, chat IA, token generado, sheet movil, usuarios, titulares, cuentas e importacion desde detalle de cuenta.
- `global.css` define la base `.close-icon-button` con target de control, foco heredado, hover sobrio y sin colores nuevos.
- Las cabeceras de modales relacionadas pasan a `grid-template-columns: minmax(0, 1fr) auto` para reservar sitio estable al cierre.
- `TokenCreatedModal` mueve el cierre al header y conserva `Copiar` como accion de contenido.

### Por que

Los botones de cierre con texto repetian una accion universal y ocupaban demasiado peso visual en modales. La X es el patron correcto para cerrar superficies, pero solo si conserva accesibilidad y tamano tactil. Hacer un reemplazo textual sin ajustar CSS habria dejado botones gigantes con una X dentro, especialmente en mobile.

No se cambio `Cerrar sesion` porque no es un cierre de superficie: es una accion de cuenta. Ocultarlo tras una X seria peor UX.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` dentro del sandbox: falla por `spawn EPERM` de Vite/Rolldown, incidencia conocida.
- `npm.cmd run build` fuera del sandbox: OK.
- Copia de `frontend/dist` a `backend/src/AtlasBalance.API/wwwroot`: OK fuera del sandbox.
- Playwright headless con CSS compilado: los cierres probados no tienen texto visible y miden `43x43` en viewport movil.
- Busqueda `rg` confirma que no quedan botones con texto visible `Cerrar` en `frontend/src` ni en `wwwroot`.

## 2026-05-10 - V-01.06 - Fix 500 al enviar primer mensaje IA

### Que cambio

- `AtlasAiService.BuildFinancialContextAsync` deja de calcular `SALDOS ACTUALES POR CUENTA` agrupando sobre el record proyectado `AiExtractoRow`.
- El agregado de ultimo saldo por cuenta ahora se hace sobre columnas escalares de `EXTRACTOS` filtradas por scope de cuenta y rango defensivo.
- La proyeccion a `AiExtractoRow` se mantiene solo al final, cuando la consulta ya tiene identificada la fila con `fila_numero` maximo por cuenta.
- Se agrega una prueba de regresion para confirmar que el contexto IA usa el saldo de la fila con mayor `fila_numero`.

### Por que

El endpoint `/api/ia/chat` fallaba antes de llamar al proveedor. El log mostraba que Npgsql no podia traducir el join entre `baseRows` y `latestKeys` porque ambos dependian de propiedades de un record construido dentro de la expresion LINQ. InMemory no lo detectaba, asi que el test anterior era demasiado comodo y se comio el bug.

La solucion correcta es mantener las partes agregadas en SQL como columnas simples y proyectar a objetos de dominio solo despues. Eso conserva minimizacion de contexto, scope por usuario y evita cargar extractos en memoria.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests --no-restore`: 20/20 OK.
- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests --no-build --no-restore`: 20/20 OK.
- API dev reiniciada con `dotnet run --no-build`.
- `GET http://localhost:5000/api/health`: `healthy`.

## 2026-05-10 - V-01.06 - Chat IA por encima del contenido

### Que cambio

- `frontend/src/styles/layout/shell.css` define `.app-topbar` como plano de apilado propio con `position: relative` y `z-index: var(--z-sticky)`.
- El chat flotante de IA, montado dentro de `TopBar`, deja de quedar en el mismo plano que el contenido principal.
- No se modifica `AiChatPanel`, el endpoint `/api/ia/*`, permisos ni configuracion de proveedor.

### Por que

El panel de IA se renderizaba dentro de la topbar, pero el contenido de la pagina se pintaba despues en el grid del shell. En el dashboard principal eso dejaba los selectores de periodo/divisa visualmente por encima del chat. Era un bug de stacking context, no de IA.

La solucion correcta es elevar la topbar como contenedor de overlays ligeros. Subir z-indexes sueltos en los selects o mover el panel a ojo habria sido maquillaje fragil.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` dentro del sandbox: falla por `spawn EPERM` de Vite/Rolldown, incidencia ya conocida.
- `npm.cmd run build` fuera del sandbox: OK.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`; `index.html` apunta a `index-B8Ww_DgG.js` y `index-iV1XYHkN.css`.
- Playwright headless con CSS compilado confirma que un punto de solape entre filtros y panel cae dentro de `.ai-floating-chat` (`insideChat=true`, `topbarZ=200`, `chatZ=400`).

## 2026-05-10 - V-01.06 - Ajuste visual de identidad de cuenta

### Que cambio

- `frontend/src/styles/layout/dashboard.css` rediseña `.account-identity-strip` como panel flexible con gaps, superficie sunken y bloques internos para `Titular`, `Banco` e `IBAN`.
- El primer bloque de la ficha recibe mayor peso visual para que el titular sea la ancla de lectura.
- `.dashboard-toolbar-main` pasa a `flex: 1 1 42rem` para que la zona izquierda del toolbar no se contraiga a min-content en desktop.
- La regla responsive conserva una sola columna en movil sin separadores extra ni overflow.

### Por que

La ficha anterior era mala: parecia una tabla incompleta, dejaba una zona muerta a la derecha y usaba una linea vertical que no comunicaba jerarquia. Para una pantalla financiera, esos datos tienen que poder escanearse en menos de un segundo y seguir el mismo lenguaje de superficies, bordes y espaciado que los KPI y tarjetas del dashboard.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- Playwright con fixture HTML y CSS reales:
  - Desktop 2048x900: ficha `832px`, tres bloques en una fila, sin overflow horizontal.
  - Movil 390x844: ficha `350px`, tres bloques apilados, sin overflow horizontal.

## 2026-05-10 - V-01.06 - Alineacion de pantallas phase2

### Que cambio

- `frontend/src/styles/layout/system-coherence.css` incluye `.phase2-page` en la regla compartida de anchura y centrado.
- La misma clase se incluye en el reset mobile para mantener `max-width: none` en pantallas pequenas.
- `CuentasPage` ya tenia `className="phase2-page cuentas-page"`, asi que no hizo falta tocar TSX ni duplicar estilos locales.

### Por que

`Titulares` estaba centrada con `max-width: 1500px`, pero `Cuentas` no entraba en esa lista global y ocupaba casi todo el ancho disponible. El resultado era una pantalla visualmente distinta sin razon funcional. La solucion correcta era subir la regla a `.phase2-page`, porque ese es el patron comun real.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- Playwright desktop 2048px con APIs mockeadas: `Titulares` y `Cuentas` miden `left=400`, `width=1500`, `deltaLeft=0`, `deltaWidth=0`, sin errores de consola.

## 2026-05-10 - V-01.06 - Ajuste visual del modal de usuarios

### Que cambio

- `UsuarioModal` separa el bloque de emails en etiqueta `Destinatarios`, textarea y ayuda `Uno por línea o separados por coma.`.
- El textarea queda enlazado a la ayuda con `aria-describedby="notification-emails-help"`.
- `users.css` añade reglas locales para `.users-notifications-section`, `.users-notification-field` y `.users-field-help`.
- El textarea del modal fuerza `width: 100%`, mantiene `min-height: 7rem`, `resize: vertical` y la misma altura de línea del sistema.

### Por que

El bloque anterior metía el texto de ayuda y el textarea dentro de un `label` inline sin layout propio. En desktop dejaba una caja estrecha y centrada que partía direcciones de email y no seguía la retícula del modal. Era un diseño roto, no una preferencia estética.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- Playwright desktop con APIs mockeadas: textarea `1046px` dentro de modal `1080px`, sin errores de consola.
- Playwright móvil 390px con APIs mockeadas: textarea `366px`, `scrollWidth=390`, sin overflow horizontal.

## 2026-05-10 - V-01.06 - Cierre de pendientes altos de auditoria release

### Que cambio

- `ImportacionService` calcula fingerprint SHA-256 estable por cuenta/fila/contenido normalizado y persiste trazabilidad en `Extracto.ImportacionFingerprint`, `ImportacionLoteHash`, `ImportacionFilaOrigen` y `FechaImportacion`.
- `AppDbContext` define indice unico filtrado `(cuenta_id, importacion_fingerprint)` para que reimportar el mismo archivo no duplique movimientos.
- La migracion `20260510120740_AddExtractoImportacionFingerprint` agrega solo columnas e indices de importacion; no renombra tablas ni toca datos existentes.
- `RevisionService` deja de cargar todos los extractos a memoria: filtra conceptos/estado, ordena y pagina con `Skip/Take` en EF. Los endpoints de revision devuelven `PaginatedResponse<T>`.
- `ExportacionService` aplica `export_max_rows` antes de generar XLSX con ClosedXML. Default: 50.000 filas. Maximo aceptado: 200.000. Al superar el limite audita `EXPORTACION_BLOQUEADA`, marca proceso `FAILED` y el endpoint manual devuelve HTTP 413.
- `PlazoFijoService` separa notificacion interna de email enviado: `FechaUltimaNotificacion` solo se actualiza tras email correcto; si SMTP falla o no hay admins activos, puede reintentarse sin duplicar notificaciones internas.
- `parseEuropeanNumber` centraliza el parseo manual frontend de importes europeos y admite `1.234,56`, `1234,56`, `-1.234,56`, `1 234,56`, `1.234`, `1,234`, simbolos de divisa y parentesis negativos.
- Las altas/ediciones manuales de extractos, desglose de cuenta e importes de plazo fijo usan `parseEuropeanNumber` y campos `inputMode="decimal"` para no bloquear la coma decimal.
- `AiChatPanel` cierra con Escape en modo flotante y enfoca el textarea cuando la IA esta disponible.
- `AtlasBalance.API.Tests.csproj` desactiva build paralelo para evitar carreras de referencias entre API, Watchdog y tests tras el renombrado.

### Verificacion

- `dotnet restore "Atlas Balance\\backend\\AtlasBalance.sln" --disable-parallel`: OK fuera del sandbox.
- `dotnet build "Atlas Balance\\backend\\AtlasBalance.sln" --no-restore -m:1 --disable-build-servers`: OK con warning MSB3101 de cache en `obj`.
- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter "FullyQualifiedName!~RowLevelSecurityTests&FullyQualifiedName!~ExtractosConcurrencyTests"`: 163/163 OK.
- `dotnet test` completo: 163/165 OK; los 2 fallos restantes requieren Docker/Testcontainers para PostgreSQL.
- `npm.cmd install`: OK, 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- `npm.cmd audit`: 0 vulnerabilidades.
- `dotnet list "Atlas Balance\\backend\\AtlasBalance.sln" package --vulnerable --include-transitive`: 0 paquetes vulnerables.
- Secret scan con `rg` sobre codigo versionable activo: sin coincidencias de claves/API tokens.
- `git diff --check`: OK; solo warnings de normalizacion LF/CRLF.

### Pendiente no maquillable

- Para release final hay que levantar Docker y ejecutar los tests PostgreSQL reales: RLS y concurrencia de `fila_numero`. Sin eso, la recomendacion sigue siendo no apta.

## 2026-05-10 - V-01.06 - Renombrado tecnico a AtlasBalance

### Que cambio

- La solucion backend pasa a `AtlasBalance.sln`.
- Los proyectos .NET quedan como `AtlasBalance.API`, `AtlasBalance.Watchdog` y `AtlasBalance.API.Tests`.
- Los namespaces C# pasan a `AtlasBalance.*`.
- Scripts, CI, referencias de release, rutas de build y `ProjectReference` apuntan a los nuevos nombres.
- El frontend mantiene el nombre visible `Atlas Balance`.
- `Actualizar-AtlasBalance.ps1` actualiza el `binPath` de los servicios existentes despues de sincronizar los nuevos ejecutables.

### Compatibilidad conservada

- Se mantiene `SetApplicationName("AtlasBalance")` en Data Protection.
- Se mantiene la base de datos `atlas_balance`.
- Se mantienen rutas publicas `/api/*` y `/watchdog/*`.
- No se renombran tablas, columnas ni migraciones aplicadas a nivel de BD.
- No se modifican secretos ni recursos externos productivos.

### Verificacion

- Build directo API: OK.
- Build directo Watchdog: OK.
- Frontend lint/build: OK.
- Busqueda final de variantes antiguas en codigo activo: sin resultados.
- Build de solucion y tests backend: bloqueados por el fallo MSBuild ya registrado en el proyecto de tests.

## 2026-05-10 - V-01.06 - Revision bancaria e IA

### Que cambio

- `RevisionService` calcula comisiones y seguros desde todos los extractos visibles por `UserAccessScope`; las escrituras de estado exigen `CanWriteCuentaAsync`.
- La deteccion normaliza tildes y compara conceptos con listas de terminos bancarios.
- El umbral `revision_comisiones_importe_minimo` se aplica sobre `Math.Abs(monto)` y solo muestra importes estrictamente superiores.
- Los estados se guardan en `REVISION_EXTRACTO_ESTADOS` con clave unica `(extracto_id, tipo)`.
- La migracion `20260509160722_AddRevisionEstadosAiConfig` habilita y fuerza RLS; las politicas delegan en `atlas_security.can_read_extracto` y `atlas_security.can_write_extracto`.
- `AtlasAiService` arma contexto financiero minimizado desde saldos, totales agregados y movimientos relevantes limitados. Conceptos y pregunta se tratan como datos no confiables para reducir prompt injection.
- La IA soportada en esta version es OpenRouter via backend y OpenAI via backend con API key de servidor. En OpenRouter, las rutas no gratuitas pueden exigir `provider.zdr=true`; los modelos gratis permitidos no lo fuerzan porque OpenRouter no los publica como endpoints ZDR.
- `/api/ia/chat` exige autenticacion, interruptor global activo, permiso `puede_usar_ia`, allowlist de modelo, limites configurables, presupuesto/tokens y auditoria de metadatos sin guardar prompts completos.
- `ConfiguracionController` valida el modelo IA con allowlist tambien en backend.
- `AlertaService` evita duplicados de saldo bajo usando `alerta_saldo_cooldown_horas` con rango efectivo 1-720 horas y no marca cooldown si el email no se envia.
- El ultimo saldo operativo se toma por `fila_numero` para respetar el orden real del extracto importado.
- `ExportacionService` exporta extractos por `fila_numero desc`, no por fecha, y aplica formato Excel `dd/mm/yyyy` y `#,##0.00`.
- `formatters.ts` centraliza formato europeo y fuerza separador de miles con `Intl.NumberFormat('es-ES')`.

### Frontend

- `navigation.ts` registra `Revision` e `IA` en el menu lateral.
- `RevisionPage` expone `Comisiones` y `Seguros`, filtro de estado y acciones de marcado.
- `IaPage` y `AiChatPanel` usan `/api/ia/chat`.
- `TopBar` monta el chat flotante con el boton de IA sin abandonar la pantalla actual.
- `ConfiguracionPage` incluye ajustes de revision/IA y no revela la API key guardada.
- Las barras tipo formula de extractos y cuenta muestran el contenido completo de la celda seleccionada con wrapping.

### Verificacion

- `dotnet build "Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" --no-restore --disable-build-servers`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox por limitacion `spawn EPERM` de Vite dentro del sandbox.
- `npm.cmd audit --audit-level=critical --json`: 0 vulnerabilidades.
- `dotnet list AtlasBalance.API.csproj package --vulnerable --include-transitive`: 0 paquetes vulnerables.
- `scan_secrets.py "Atlas Balance" --json`: 1 falso positivo bajo en secreto literal de test RLS.
- `dotnet test ... AtlasBalance.API.Tests.csproj ...`: bloqueado por resolucion de `AtlasBalance.Watchdog` con error MSBuild sin diagnostico (`0 Errores`).

## 2026-05-02 - V-01.06 - Reticula real en tabla de Extractos

### Que cambio

- `ExtractoTable.tsx` mueve `--extracto-sheet-width` al viewport para que cabecera, cuerpo, espaciador y filas lo hereden desde un contenedor comun.
- `extractos.css` elimina el fondo con gradientes que dibujaba una cuadricula falsa de `120px`.
- `.extracto-row` usa `height: var(--sheet-row-height)` y `align-items: stretch`.
- `.cell` usa `box-sizing: border-box`, altura fija y `border-bottom` propio para construir celdas completas.
- Los textos directos dentro de celda se recortan con ellipsis para no empujar el ancho visual.
- Se sincroniza `frontend/dist` con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El intento anterior alineaba los tracks, pero dejaba una trampa visual: el viewport seguia pintando una cuadricula de fondo con columnas de `120px`, mientras las columnas reales miden distinto. Eso hace que una tabla financiera parezca torcida aunque el grid tecnico este cerca de alinearse. La correccion correcta es que las lineas sean los bordes de las celdas reales, punto.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `/extractos` mockeado: OK; 13 columnas visibles, `maxLeftDelta=0`, `maxWidthDelta=0`, `maxBottomDelta=0`, altura de fila `42px` y `backgroundImage=none`.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Insercion ordenada de lineas en desglose de cuenta

### Que cambio

- `CreateExtractoRequest` agrega `InsertBeforeFilaNumero`.
- `ExtractosController.Crear` calcula la fila destino con `Math.Clamp`, bloquea por cuenta con `pg_advisory_xact_lock` en PostgreSQL y desplaza las filas `>= destino`.
- Para PostgreSQL, el desplazamiento usa dos `UPDATE` con offset temporal para no chocar con el indice unico `(cuenta_id, fila_numero)`.
- Para tests/in-memory, el desplazamiento se hace ordenando las filas descendentes y sumando `1`.
- `CuentaDetailPage` agrega accion `Insertar debajo` y formulario inline que envia `insert_before_fila_numero`.
- `CuentaDetailPage` carga el desglose con `sortBy=fila_numero&sortDir=desc` para que la vista respete el orden persistido.
- `dashboard.css` define estilos para acciones de fila y formulario intermedio.

### Por que

El alta manual existente solo agregaba al final (`max(fila_numero) + 1`). Eso no sirve para extractos bancarios con lineas informativas o desglose partido: si el usuario necesita meter una linea entre dos movimientos, el orden persistido debe moverse de verdad en backend. Hacerlo solo en React seria humo caro.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ExtractosControllerTests -c Release`: 11/11 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Filtro de periodo en Extractos

### Que cambio

- `frontend/src/pages/ExtractosPage.tsx` agrega filtros `Desde` y `Hasta` con `DatePickerField`.
- Los filtros se sincronizan con la URL mediante `fechaDesde` y `fechaHasta`.
- `loadRows` envia esos parametros a `GET /api/extractos`, que ya soportaba `DateOnly? fechaDesde/fechaHasta`.
- Se valida en frontend que `fechaDesde` no sea posterior a `fechaHasta`.
- `frontend/src/styles/layout/extractos.css` adapta el header de filtros para titulares, cuentas y fechas sin romper mobile.
- El bundle frontend se recompila y se sincroniza con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

La API ya sabia filtrar por fechas, pero la pantalla no daba forma de usarlo. Eso es funcionalidad medio hecha: existe en el contrato, pero el usuario sigue obligado a tragar todo el historico o filtrar celda a celda. Ahora el rango vive donde debe vivir: arriba, junto a titular y cuenta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Dominio vertical de graficas de evolucion

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` calcula un dominio Y explicito con `getEvolutionDomain`.
- El dominio incluye las series `saldo`, `ingresos` y `egresos`.
- Cuando todos los valores son positivos, se mantiene `0` como base visual y se suma padding al maximo.
- Cuando hay valores negativos, se resta padding al minimo para no recortar trazos bajo cero.
- El padding usa el 4% del rango o de la magnitud maxima, con minimo de `1`.
- Se sincroniza `frontend/dist` con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El dominio automatico de Recharts podia colocar el valor maximo justo contra el borde superior del area de trazado. Con un `strokeWidth` de 2.6, eso recortaba visualmente la parte alta de la linea de saldo. El fix correcto es dar aire al dominio de datos, no mover la grafica a ojo con CSS.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Tabla de extractos con reticula estable

### Que cambio

- `ExtractoTable.tsx` calcula el ancho total de la hoja desde las columnas visibles.
- `getColumnTrack` pasa a devolver anchos fijos en pixeles, respaldados por `getColumnWidth`.
- Cabecera, espaciador virtualizado y filas comparten `--extracto-sheet-width`.
- `extractos.css` fija `width: max(100%, var(--extracto-sheet-width))` y `min-width: var(--extracto-sheet-width)` en cabecera, cuerpo, espaciador y filas.
- Las filas virtualizadas ya no aplican `translateY(virtualRow.start - headerOffset)`; ahora arrancan en `virtualRow.start` porque el cuerpo ya esta debajo de la cabecera.
- El bundle frontend se recompila y se sincroniza con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

La combinacion de columnas `fr`, filas absolutas y un cuerpo sin ancho intrinseco estable permitia que algunas filas recalcularan la cuadricula contra el viewport en vez de contra el ancho real de columnas. En una tabla de extractos eso es un bug serio: si una celda parece moverse de columna, el usuario deja de confiar en el dato.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `/extractos` mockeado: OK; 9 columnas visibles, filas renderizadas y sin diferencias de posicion/ancho entre cabecera y celdas.
- `robocopy dist ..\backend\src\AtlasBalance.API\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - KPIs del dashboard principal sin overflow

### Que cambio

- `frontend/src/styles/layout/dashboard.css` declara `container-type: inline-size` y `min-width: 0` en `.dashboard-kpi`.
- `.dashboard-overview-grid` pasa a `minmax(46rem, 1.32fr) minmax(20rem, 0.68fr)` para dar prioridad al bloque principal frente al desglose por divisa.
- Los importes de `.dashboard-kpi p` usan `font-size: clamp(1rem, 8cqw, 1.55rem)`.
- El KPI destacado usa `font-size: clamp(1.35rem, 6cqw, var(--font-size-kpi))`, manteniendo el override especifico de overview.
- El bundle frontend se recompila y se sincroniza con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El ajuste anterior de V-01.05 resolvio el `Saldo total`, pero dejaba los KPIs laterales con importes grandes, fuente mono y `white-space: nowrap` dentro de columnas estrechas. Resultado: los ingresos y egresos invadian la tarjeta de al lado. Truncar dinero seria mala decision; el numero debe caber. Ademas, el reparto antiguo daba demasiado protagonismo horizontal a `Saldos por divisa`, que es informacion secundaria en esta vista.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `/dashboard` y APIs mockeadas: OK; sin overflow horizontal, bloque principal `979px`, divisas `505px` y sin desbordamiento en KPIs ni divisas.
- `robocopy dist ..\backend\src\AtlasBalance.API\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Apertura de version

### Que cambio

- `Atlas Balance/VERSION` pasa de `V-01.05` a `V-01.06`.
- `Atlas Balance/Directory.Build.props` pasa de `1.5.0` a `1.6.0` y declara `InformationalVersion` como `V-01.06`.
- `Atlas Balance/frontend/package.json` y `package-lock.json` pasan la version del paquete frontend a `1.6.0`.
- `Documentacion/Versiones/version_actual.md` apunta a `Documentacion/Versiones/v-01.06.md`.
- `Documentacion/Versiones/v-01.06.md` queda creado como registro activo de la nueva version.

### Por que

La nueva linea de trabajo debe quedar trazada desde el primer cambio. Mantener `V-01.05` como activa mientras se trabaja en `V-01.06` mezclaria release cerrado con trabajo nuevo.

### Verificacion

- `git switch -c V-01.06`: OK.
- `git status --short --branch`: rama activa `V-01.06`.
- `Select-String` confirma `V-01.06` / `1.6.0` en las fuentes runtime y documentacion de version.

## 2026-05-02 - V-01.05 - Fix de lockfile npm para CI GitHub

### Que cambio

- `Atlas Balance/frontend/package.json` declara overrides para `once`, `graphemer`, `loose-envify` y `natural-compare` en `1.4.0`.
- `Atlas Balance/frontend/package-lock.json` actualiza esas entradas desde `1.5.0` inexistente a `1.4.0`.
- No cambia codigo runtime ni bundle servido; es una correccion de reproducibilidad de instalacion.

### Por que

GitHub Actions ejecuta `npm ci` en entorno limpio. El lockfile versionado apuntaba a tarballs que npm no publica (`once-1.5.0.tgz`, `graphemer-1.5.0.tgz`, `loose-envify-1.5.0.tgz` y `natural-compare-1.5.0.tgz`), por lo que CI fallaba antes de auditar, lintar o compilar.

### Verificacion

- `npm.cmd ci`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

## 2026-05-02 - V-01.05 - Paquete release Windows x64

### Que cambio

- Ejecutado `scripts/Build-Release.ps1 -Version V-01.05`.
- El script recompila frontend, sincroniza `frontend/dist` hacia `backend/src/AtlasBalance.API/wwwroot`, publica API y Watchdog self-contained para `win-x64` y crea el paquete en `Atlas Balance/Atlas Balance Release`.
- Artefactos generados:
  - `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64`
  - `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64.zip`
- El ZIP queda fuera de Git por `.gitignore`; debe subirse como asset de GitHub Release.

### Verificacion

- `npm.cmd run build`: OK.
- `dotnet publish` API Release win-x64: OK.
- `dotnet publish` Watchdog Release win-x64: OK.
- SHA256 ZIP: `3E7A3ED22EFC4D18A161EA9D8D15CD9C12B3D51BDEF9AE38863767EC5CEAE299`.
- Tamano ZIP: `102350978` bytes.

### Pendiente operativo

- No se genero `AtlasBalance-V-01.05-win-x64.zip.sig` porque falta `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` en el entorno. Sin ese asset, el actualizador online falla cerrado.

## 2026-05-02 - V-01.05 - Cierre de hallazgos residuales del escaneo repo-wide

### Que cambio

- `Instalar-AtlasBalance.ps1` guarda credenciales iniciales en `C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt`.
- `Instalar-AtlasBalance.ps1` y `Reset-AdminPassword.ps1` protegen el directorio `config` con ACL `Administrators/SYSTEM` antes de escribir secretos; si `icacls` falla, no queda archivo de credenciales expuesto.
- `Reset-AdminPassword.ps1` exige ejecucion como Administrador.
- `ExtractosController.ToggleFlag` valida permisos por campo cambiado (`flagged` y `flagged_nota`).
- `DashboardService` ignora filas globales `PuedeVerDashboard` que no tengan permisos de datos; los dashboards de gerente quedan globales solo con alcance global real de datos o scopeados por titular/cuenta.
- `IntegrationOpenClawController.Auditoria` deja de usar `IgnoreQueryFilters()` al resolver extractos y no devuelve valores de auditoria de extractos soft-deleted.
- La politica RLS `exportaciones_write` pasa de `can_read_cuenta_by_id` a `can_write_cuenta_by_id`.
- `ImportacionPage` normaliza `returnTo` y solo acepta rutas internas que empiecen por `/`.
- CI y `docker-compose.yml` fijan `postgres:16-alpine` por digest OCI.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Verificacion

- `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --filter "ExtractosControllerTests|DashboardServiceTests|IntegrationOpenClawControllerTests|RowLevelSecurityTests" --no-restore`: 20/20 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Parser PowerShell de scripts de instalacion/reset/update: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Hardening de seguridad repo-wide

### Que cambio

- `AuthService` usa `MaxFailedLoginAttempts = 5` y bloquea la cuenta aunque el throttle por cliente tambien se active.
- MFA acumula fallos por usuario durante una ventana de 15 minutos; crear un challenge nuevo ya no reinicia el contador efectivo.
- `IntegrationAuthMiddleware` redacta query params con normalizacion de clave y tambien valores con pinta de bearer/integration token.
- `ImportacionService` limita `ColumnasExtra` a 64, nombres a 80 caracteres, rechaza indices extra fuera de los datos y no persiste extras vacios.
- `UserAccessService` y `ExtractosController` solo derivan scope de datos desde flags de datos (`PuedeVerCuentas`, agregar, editar, eliminar, importar), no desde `PuedeVerDashboard`.
- `ExtractosController.Restaurar` requiere `CanDelete`, alineado con la accion de eliminar/restaurar.
- `CuentasController` y `ExtractosController` ocultan `CuentaReferenciaId/Nombre` de plazo fijo cuando la cuenta referencia no pasa scope o filtros de borrado para el usuario.
- `ActualizacionService` exige firma detached `.zip.sig` RSA/SHA-256 para updates online; `Build-Release.ps1` genera esa firma si existe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`.

### Configuracion nueva

```json
{
  "UpdateSecurity": {
    "ReleaseSigningPublicKeyPem": "-----BEGIN PUBLIC KEY-----..."
  }
}
```

Tambien se acepta `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM`. Para CI/release, `Build-Release.ps1` firma el ZIP si recibe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`. La clave privada no se documenta ni se guarda en repo. Si no hay clave publica o no existe el asset `.zip.sig`, el update online falla cerrado.

### Por que

El digest SHA-256 de GitHub Releases detecta corrupcion, no compromiso del canal de release. Si el atacante puede cambiar asset y metadata, puede cambiar ambos. La firma detached ancla el paquete a una clave fuera del canal de descarga. Lo demas son controles de autorizacion y brute-force que tenian que vivir en el backend, no solo en RLS o en UI.

### Verificacion

- Tests focalizados seguridad: 72/72 OK.
- Suite backend completa: 127/128; falla el harness RLS local por `permission denied for table __EFMigrationsHistory`.
- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- Parser PowerShell de scripts tocados: OK.

## 2026-05-02 - V-01.05 - Alineacion dinamica de EvolucionChart

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` introduce un calculo de ancho para el `YAxis`.
- El calculo revisa las etiquetas compactas de `ingresos`, `egresos` y `saldo` en todos los puntos.
- El eje queda limitado entre `44px` y `72px`.
- Todas las pantallas que renderizan evolucion heredan el ajuste porque usan el mismo componente: `/dashboard`, `/dashboard/titular/:id`, `/titulares` y `/cuentas`.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

Un ancho fijo de `72px` era aceptable para importes grandes, pero torpe para series pequenas como `4 EUR`: la grafica seguia arrancando demasiado a la derecha aunque las etiquetas no necesitaran ese espacio. La solucion correcta es adaptar el eje al contenido, con limites para no romper etiquetas largas.

### Reglas tecnicas

- No cambia contratos de API, permisos ni calculos financieros.
- No se introduce dependencia nueva.
- El tooltip conserva importes completos con `formatCurrency`.
- El eje sigue usando `formatCompactCurrency`; solo cambia su ancho reservado.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Playwright headless con APIs mockeadas sobre `/dashboard`, `/dashboard/titular/titular-1`, `/titulares` y `/cuentas`: OK; `gridStartX=45px`, `yAxisWidth=39px` y sin errores de pagina en las cuatro rutas.

## 2026-05-02 - V-01.05 - Saldo total del dashboard sin salto de linea

### Que cambio

- `dashboard.css` ajusta `dashboard-kpi-grid--overview` para dar mas ancho relativo al KPI destacado.
- Los KPIs superiores reducen padding dentro de esa grilla.
- Los importes de `.dashboard-kpi p` usan `white-space: nowrap`.
- El saldo destacado en `dashboard-kpi-grid--overview .dashboard-kpi--featured p` baja a `clamp(1.35rem, 1.5vw, 1.65rem)`.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Por que

El saldo total tenia una escala demasiado grande para una tarjeta de una tercera parte del resumen. Con `1.000.000,00 €` se partia o desbordaba. Eso no es un detalle: en una app de tesoreria, los numeros grandes son el caso normal, no una sorpresa.

### Reglas tecnicas

- No cambia formato monetario ni calculos.
- No se oculta el importe con ellipsis.
- No se toca el contrato del componente `KpiCard`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `total_convertido=1000000`: `1.000.000,00 €` queda en una linea y no desborda (`wraps=false`, `overflows=false`).
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Listado de cuentas en tres columnas

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` agrega la clase `cuentas-page` al contenedor raiz.
- `frontend/src/styles/layout/entities.css` define una grilla especifica para `.cuentas-page .phase2-cards`.
- El listado inferior de cuentas usa tres columnas en desktop, dos en tablet y una en mobile.
- Las tarjetas de cuenta ajustan el header para permitir badges en una segunda linea, limitan titulo/notas a dos lineas y reorganizan metadatos en dos columnas internas.
- El saldo queda destacado en la columna derecha en desktop/tablet y vuelve a apilarse en mobile.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El listado de cuentas heredaba dos columnas de `.phase2-cards`. Pasarlo a tres columnas sin tocar la estructura interna dejaba demasiada informacion financiera comprimida: banco, divisa, estado, vencimiento y saldo compiten por espacio. La solucion correcta es acotar la grilla a `CuentasPage` y ajustar la tarjeta para esa nueva densidad.

### Reglas tecnicas

- No cambia contratos de API, permisos, filtros, paginacion ni calculos.
- No se introduce dependencia nueva.
- La regla mobile especifica evita que la mayor especificidad de `cuentas-page` mantenga dos columnas por debajo de `900px`.
- Se mantiene CSS variables propias y el sistema responsive existente.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/cuentas`: desktop `3` columnas, tablet `2`, mobile `1`, sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Divisa base primero en saldos por divisa

### Que cambio

- `SaldoPorDivisaCard.tsx` calcula `orderedItems` antes de renderizar.
- La lista se parte en dos bloques: primero los items cuya `divisa` coincide con `divisaPrincipal`, despues el resto.
- El resto de divisas conserva el orden recibido de la API.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Por que

La divisa base es la referencia de comparacion del dashboard. Si aparece segunda o tercera, el usuario tiene que reconstruir mentalmente la pantalla. Eso es mala jerarquia, no una preferencia estetica.

### Reglas tecnicas

- No cambia ningun endpoint ni calculo.
- No se ordenan alfabeticamente las divisas secundarias para evitar cambiar mas de lo pedido.
- No se introduce dependencia nueva.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con API mockeada: la API devuelve `USD` antes que `EUR`, pero `EUR` se renderiza primero porque es `divisaPrincipal`.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Reorden de plazos fijos y saldos por titular en dashboard principal

### Que cambio

- `DashboardPage.tsx` agrupa los KPIs superiores y la tarjeta `Plazos fijos` dentro de `dashboard-overview-primary`.
- `Plazos fijos` se renderiza debajo de `Saldo total`, `Ingresos periodo` y `Egresos periodo`, manteniendo `Saldos por divisa` en la columna derecha del resumen.
- `Saldos por titular` deja de formar parte de una grilla secundaria y pasa a ser una tarjeta de ancho completo en la parte inferior.
- `saldosPorTipo` ya no elimina tipos vacios: siempre prepara Empresa, Autonomo y Particular para mantener tres columnas previsibles.
- `dashboard.css` cambia `dashboard-titular-groups` a tres columnas en desktop y conserva una columna en mobile.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Por que

Los plazos fijos explican saldo inmovilizado, asi que deben leerse junto a los KPIs de saldo/movimiento. Ponerlos abajo junto a titulares era una mezcla floja. Los titulares, en cambio, son comparacion por categoria; si hay tres tipos, el layout debe tener tres columnas, no dos y luego apaños.

### Reglas tecnicas

- No cambia ningun endpoint ni contrato de API.
- No cambia calculo de saldos, permisos ni filtros.
- No se introduce dependencia nueva.
- La adaptacion responsive se limita a CSS del dashboard.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK; `Plazos fijos` debajo de KPIs, `Saldos por titular` a ancho completo, columnas `Empresa|Autonomo|Particular` en la misma fila y sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Listado de titulares en tres columnas

### Que cambio

- `frontend/src/pages/TitularesPage.tsx` agrega la clase `titulares-page` al contenedor raiz.
- `frontend/src/styles/layout/entities.css` define una grilla especifica para `.titulares-page .phase2-cards`.
- El listado inferior de titulares usa tres columnas en desktop, dos en tablet y una en mobile.
- Las tarjetas de titular limitan titulo y notas a dos lineas, reorganizan metadatos en dos columnas internas y mantienen las acciones al pie.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

La regla global `.phase2-cards` estaba en dos columnas y tambien la usa `CuentasPage`. Cambiarla globalmente habria sido una metedura de pata: el ajuste pedido pertenece solo a Titulares. La clase de pagina permite ampliar densidad en esa vista sin efectos colaterales.

### Reglas tecnicas

- No cambia contratos de API, permisos, paginacion ni estado.
- No se introduce dependencia nueva.
- La composicion conserva CSS variables propias y los breakpoints existentes.
- La regla mobile explicita evita que la mayor especificidad de `titulares-page` mantenga dos columnas por debajo de `900px`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/titulares`: desktop `3` columnas, tablet `2`, mobile `1`, sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-02 - V-01.05 - Formato de importacion en cuentas de efectivo

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` muestra el selector `Formato de importacion` para `NORMAL` y `EFECTIVO`.
- Al cambiar una cuenta a `EFECTIVO`, la UI limpia `banco_nombre`, `numero_cuenta` e `iban`, pero conserva `formato_id` si es compatible con la divisa.
- Al cambiar a `PLAZO_FIJO`, la UI sigue limpiando datos bancarios y `formato_id`.
- `frontend/src/pages/ImportacionPage.tsx` aclara que las cuentas normales y de efectivo usan formato de importacion.
- `CuentasController` usa `SupportsFormatoImportacion(tipoCuenta)` para aceptar formato en `NORMAL` y `EFECTIVO`, y rechazarlo implicitamente en `PLAZO_FIJO`.
- Se agrega `Crear_Should_Keep_Formato_For_Efectivo` en `CuentasControllerTests`.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El codigo anterior mezclaba dos conceptos distintos: `EFECTIVO` no tiene datos bancarios, pero si puede necesitar un formato para importar movimientos pegados/CSV. `PLAZO_FIJO` si tiene un flujo especial sin formato bancario. Meter ambos en el mismo saco era el bug.

### Reglas tecnicas

- El formato sigue filtrado por divisa.
- Las cuentas de efectivo no persisten banco, numero de cuenta ni IBAN.
- Las cuentas de plazo fijo siguen sin `formato_id` y usan el endpoint especifico de movimiento simple.
- No cambia el contrato de importacion; `ImportacionService` ya leia `FormatoId` desde la cuenta.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release --filter CuentasControllerTests`: 5/5 OK.
- `npm.cmd run lint`: OK tras corregir dependencia faltante del `useEffect`.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-02 - V-01.05 - Alineacion de graficas en Cuentas y Titulares

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` importa `formatCompactCurrency` y lo usa en el `YAxis` de la grafica de barras del dashboard de cuentas.
- `frontend/src/pages/TitularesPage.tsx` aplica el mismo ajuste en la grafica de barras del dashboard de titulares.
- En ambas graficas, `BarChart` usa margenes explicitos `top: 12`, `right: 8`, `bottom: 12`, `left: 0`.
- `YAxis` baja de `120` a `72`, oculta `axisLine`/`tickLine` y usa `tickMargin={10}`.
- `CartesianGrid` usa `var(--chart-grid)` y desactiva lineas verticales para mantener consistencia con el resto de dashboards.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El carril del eje Y estaba sobredimensionado y empujaba el area real de barras hacia la derecha. Ya se habia corregido el mismo patron en la grafica de evolucion del dashboard principal; dejarlo repetido en cuentas/titulares era inconsistente y visualmente torpe.

### Reglas tecnicas

- No cambia contratos de API, permisos, calculos ni stores.
- No se introduce dependencia nueva.
- El tooltip conserva `formatCurrency` para mostrar importes completos; el formato compacto queda limitado al eje.
- Se mantiene Recharts 2 y CSS variables propias.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Playwright headless con APIs mockeadas sobre `/titulares` y `/cuentas`: OK; `gridStartX=72px`, `yAxisWidth=69px` y sin errores de pagina en ambas rutas.

## 2026-05-01 - V-01.05 - Dashboard principal con grafica a ancho completo

### Que cambio

- `DashboardPage.tsx` separa el dashboard en tres ritmos: resumen superior (`dashboard-overview-grid`), grafica principal (`dashboard-evolution-card`) y bloques secundarios.
- `EvolucionChart.tsx` acepta `height?: number`; el dashboard principal lo usa con `height={420}`.
- `dashboard.css` agrega `dashboard-overview-grid`, refuerza la tarjeta de evolucion con mas padding y adapta divisas/KPIs en desktop y mobile.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.
- `Documentacion/Diseno/DESIGN.md` se actualiza para que la guia ya no contradiga la nueva jerarquia.

### Por que

La pantalla anterior intentaba meter KPIs, divisas y grafica en una sola fila. Eso hacia que la grafica quedara demasiado estrecha para leer tendencias. En tesoreria, la evolucion temporal necesita area util real; si el usuario tiene que acercarse a la pantalla, el diseño falló.

### Reglas tecnicas

- No cambia contratos de API, permisos, filtros ni calculos.
- No se introduce dependencia nueva.
- La altura configurable queda encapsulada en `EvolucionChart` para no duplicar componentes.
- Se mantiene CSS variables propias y Recharts 2.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK, `chartWidthRatio=0.960`, `svgHeight=420`, sin errores de pagina, sin respuestas API 500 y sin overflow horizontal. Dos fallos previos fueron del script mock de verificacion, no del producto.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-01 - V-01.05 - Alineacion de la grafica Evolucion

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` define margenes explicitos en `LineChart`: `top: 4`, `right: 8`, `bottom: 0`, `left: 0`.
- El `YAxis` reduce su anchura de `116` a `72`.
- `XAxis` y `YAxis` usan `tickMargin={10}` para separar etiquetas sin agrandar artificialmente el eje.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

La tarjeta estaba bien; la grafica no. Recharts estaba reservando demasiado espacio horizontal para el eje Y, asi que el area real de trazado arrancaba tarde y la grafica parecia desalineada dentro del dashboard. Corregirlo en el componente mantiene el layout limpio y evita parches de padding alrededor.

### Reglas tecnicas

- No cambia contratos de API, filtros, permisos ni estructura de datos.
- No se introduce dependencia nueva.
- Se mantiene Recharts 2 y CSS variables propias.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK; `plotInsetFromLegend=72px`, frente al carril anterior de `116px`.

## 2026-05-01 - V-01.05 - MFA recordado 90 dias y QR de enrolamiento

### Que cambio

- `AuthService.LoginAsync` acepta la cookie `mfa_trusted` y omite el reto MFA solo si el token firmado coincide con el usuario, su `security_stamp` y una expiracion futura.
- `AuthService.VerifyMfaAsync` emite un token MFA recordado durante 90 dias tras verificar correctamente el codigo TOTP.
- `AuthController` lee/escribe `mfa_trusted` como cookie `HttpOnly`, `SameSite=Strict`, `Secure` cuando aplica, y la elimina en logout.
- El enrolamiento inicial sigue generando secreto TOTP por usuario y ahora el frontend pinta un QR real desde `mfa_otp_auth_uri`.
- Se agrega `qrcode` al frontend para generar el QR localmente sin servicios externos.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

Pedir Google Authenticator en cada login es seguridad teatral y mala UX: fuerza friccion constante y acaba empujando a la gente a atajos peores. El criterio correcto aqui es MFA obligatorio en primer enrolamiento y revalidacion periodica. Tres meses es una ventana razonable para una app on-premise de pocos usuarios si el recordatorio queda atado al usuario y se invalida al rotar `security_stamp`.

### Reglas tecnicas

- La cookie recordada no contiene secretos TOTP ni tokens JWT.
- La firma usa HMAC SHA-256 con `JwtSettings:Secret`.
- El token queda ligado a `user_id`, `security_stamp` y expiracion. Cambios de password, permisos, email o perfil que roten `security_stamp` invalidan tambien el recuerdo MFA.
- El QR se genera desde el `otpauth://` emitido por backend; la clave manual queda visible como fallback.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --filter AuthServiceTests`: 11/11 OK.
- `dotnet test ... --filter AuthServiceTests` en Debug quedo bloqueado por `AtlasBalance.API.exe` en uso, PID `35456`; se verifico en Release para no detener un proceso local activo.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-01 - V-01.05 - Alineacion del logo en login

### Que cambio

- `frontend/src/styles/auth.css` cambia `.auth-logo-container` de `width: min(100%, 1120px)` a la misma columna visual del formulario: `width: min(calc(100% - 2rem), 430px)`.
- `.auth-logo-container` usa `justify-content: center` para centrar el bloque de marca completo sobre la tarjeta.
- En mobile se usa `width: min(calc(100% - 1.5rem), 430px)` y se conserva el centrado.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El header del login estaba usando un ancho de pagina completa pensado para layouts generales, no para una pantalla de autenticacion centrada. Resultado: primero el logo quedaba flotando a la izquierda; despues quedo alineado al borde de la tarjeta, pero no centrado como bloque. En login, la marca tiene que caer sobre el eje central de la tarjeta.

### Reglas tecnicas

- No cambia JSX, rutas, autenticacion, MFA ni contratos de API.
- No se introduce dependencia nueva.
- Se conserva CSS variables propias y el comportamiento responsive existente.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Edge headless/CDP en `/login`: centro del bloque de marca y centro de la tarjeta coinciden; `brandDeltaCard=0px`.

## 2026-05-01 - V-01.05 - Aplicacion UI/UX en shell y dashboard

### Que cambio

- `frontend/src/utils/navigation.ts` incorpora grupos semanticos de navegacion: `operacion`, `control` y `sistema`.
- La navegacion usa iconos de `lucide-react` con stroke consistente, en linea con `Documentacion/Diseno/DESIGN.md`.
- `Sidebar.tsx` renderiza secciones agrupadas con labels y separadores discretos.
- `BottomNav.tsx` reduce el menu movil principal a Inicio, Titulares, Cuentas, Importar y Mas; el sheet `Mas` agrupa los accesos secundarios por las mismas secciones.
- `DashboardPage.tsx` reorganiza la primera lectura: KPIs, saldos por divisa y evolucion quedan en `dashboard-command-grid`.
- `SaldoPorDivisaCard.tsx` pasa a una estructura mas semantica con total dominante y desglose `Disponible` / `Inmovilizado`.
- `dashboard.css` ajusta el grid del dashboard para evitar solapamientos, conservar densidad y mantener una columna unica en breakpoints medios/moviles.
- `global.css` deja de importar Geist, porque la guia define `National Park`, `Hind Madurai` y `Atlas Mono` como sistema tipografico activo.
- `auth.css` corrige `--font-mono` por `--font-family-mono` en el bloque MFA.
- `backend/src/AtlasBalance.API/wwwroot` se sincroniza con el build frontend actualizado.

### Por que

La guia UI/UX ya estaba escrita, pero no aplicada. El menu plano de muchas entradas era arquitectura visual floja: obligaba a leer todo al mismo nivel. El dashboard tambien repartia demasiado pronto la atencion; para tesoreria, el orden correcto es saldo total, liquidez por divisa y evolucion.

El solapamiento detectado en la verificacion inicial del KPI principal confirmo el punto: si un numero financiero importante no cabe, el diseno esta fallando aunque compile.

### Reglas tecnicas

- No se cambia ningun contrato de API.
- No se introduce dependencia nueva.
- Se mantiene CSS variables propias, dark/light mode y componentes existentes.
- Los grupos de navegacion viven en `navigation.ts` para que desktop y mobile compartan arquitectura.
- `wwwroot` debe actualizarse despues de cada build frontend que cambie UI servida por la API.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright con APIs mockeadas en `/dashboard`: desktop y mobile sin overflow horizontal; sidebar con grupos `Operacion`, `Control`, `Sistema`; bottom nav con `Inicio`, `Titulares`, `Cuentas`, `Importar`, `Mas`; KPI principal sin solapamiento tras correccion.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-01 - V-01.05 - Row Level Security activo en PostgreSQL

### Que cambio

- Se agrega la migracion `20260501120000_EnableRowLevelSecurity`.
- Se agrega la migracion `20260501133000_SignRowLevelSecurityContext`.
- La migracion crea el schema auxiliar `atlas_security`, funciones de contexto y politicas RLS.
- RLS queda activado con `FORCE ROW LEVEL SECURITY` en:
  - `TITULARES`
  - `CUENTAS`
  - `PLAZOS_FIJOS`
  - `EXTRACTOS`
  - `EXTRACTOS_COLUMNAS_EXTRA`
  - `EXPORTACIONES`
  - `PREFERENCIAS_USUARIO_CUENTA`
  - `AUDITORIAS`
  - `AUDITORIA_INTEGRACIONES`
  - `BACKUPS`
  - `NOTIFICACIONES_ADMIN`
- `RlsDbCommandInterceptor` fija contexto PostgreSQL antes de cada comando EF Core mediante variables `atlas.*` y una firma HMAC.
- `RlsContextSigner` firma el payload de contexto. PostgreSQL valida la firma con `atlas_security.context_is_valid()`.
- `Program.cs` aplica migraciones con `ConnectionStrings:MigrationConnection` si existe, configura el secreto de firma en `atlas_security.rls_context_secret`, concede permisos al runtime y limpia pools antes de usar `DefaultConnection`.
- `IntegrationAuthMiddleware` publica el token de integracion validado antes de escribir auditoria/rate limit.
- `docker-compose.yml` deja de usar `app_user` como `POSTGRES_USER`; las bases nuevas crean `atlas_owner` para ownership/migraciones y `app_user` como runtime sin `BYPASSRLS`.
- `Instalar-AtlasBalance.ps1` crea/separa `atlas_balance_owner` y `atlas_balance_app`; ambos sin superusuario ni `BYPASSRLS`, pero solo el owner queda en `MigrationConnection`.

### Como funciona

El backend sigue siendo la primera capa de permisos. RLS es la segunda capa: si una consulta directa o un bug de backend intenta leer/escribir fuera del alcance, PostgreSQL tambien filtra.

El interceptor fija estas variables de sesion:

- `atlas.auth_mode`: `anonymous`, `auth`, `user`, `integration` o `system`.
- `atlas.user_id`: usuario autenticado.
- `atlas.integration_token_id`: token de integracion autenticado.
- `atlas.is_admin`: admin de aplicacion.
- `atlas.system`: operaciones internas sin `HttpContext`, como migraciones/seed.
- `atlas.request_scope`: alcance especial, por ejemplo `dashboard`.
- `atlas.context_signature`: HMAC SHA-256 del payload anterior.

Las politicas consultan `PERMISOS_USUARIO` e `INTEGRATION_PERMISSIONS`. Admin y operaciones internas tienen paso amplio solo si `atlas.context_signature` valida contra el secreto DB. Usuarios normales e integraciones quedan limitados a sus cuentas permitidas.

El detalle importante: un cliente SQL con credenciales runtime puede ejecutar `SET atlas.system=true`, pero eso no le concede nada si no puede firmar el contexto. Sin esta firma, RLS seria teatro.

### Limites deliberados

- Las tablas de identidad/configuracion no quedan bajo estas politicas. Muchas se leen durante login, seed, proteccion de secretos o administracion y meterlas en RLS sin un diseno especifico romperia arranque/autenticacion.
- RLS no reemplaza permisos de controlador. Si alguien elimina checks en C#, sigue siendo un bug aunque PostgreSQL bloquee parte del dano.
- En contenedores dev antiguos puede no existir rol `postgres` porque se crearon con `app_user` como superusuario. La migracion activa RLS y firma de contexto, pero la separacion fuerte owner/runtime exige migrar ownership con un rol administrador o recrear la base con el Docker/instalador nuevo.

### Verificacion

- `dotnet build '.\Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj' -c Release --no-restore`: OK.
- `dotnet test '.\Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj' -c Release --no-restore --filter RowLevelSecurityTests`: OK.
- Tests focalizados `RowLevelSecurityTests|UserAccessServiceTests|IntegrationAuthorizationServiceTests|IntegrationAuthMiddlewareTests|IntegrationTokenServiceTests`: 15/15 OK.
- `dotnet ef database update`: OK sobre `atlas_balance_db`.
- Catalogo local: 11 tablas objetivo con RLS y FORCE RLS activos, 20 politicas publicas, dos migraciones RLS aplicadas, `app_user` sin superusuario ni `BYPASSRLS`, secreto RLS sembrado, contexto falsificado rechazado y contexto firmado aceptado.

## 2026-04-26 - V-01.05 - Dashboard de titulares: evolucion antes del listado

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` reordena el render del bloque `titulares-dashboard-card`.
- La tarjeta `Evolucion` (`titulares-evolucion-card`) pasa a mostrarse antes de `cuentas-balance-list`.
- No hay cambios en servicios, tipos, stores ni contratos de API.

### Por que

El orden anterior forzaba leer primero el detalle y despues la tendencia. Para analisis rapido de titulares, eso es al reves de lo util.

### Reglas tecnicas

- Cambio solo de orden de JSX.
- Se conserva la misma fuente de datos (`evolucion`, `principal`, `saldosCuentaRows`) y la misma logica de permisos.
- Sin cambios CSS.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

## 2026-04-26 - V-01.05 - Reorden de dashboard principal (grafica antes de saldos)

### Que cambio

- `frontend/src/pages/DashboardPage.tsx` reordena el render para mostrar primero la tarjeta `Evolucion`.
- El bloque `dashboard-grid` (Saldo por divisa + Saldos por titular) queda debajo de la grafica.
- No se tocan servicios, tipos, stores ni endpoints.
- `backend/src/AtlasBalance.API/wwwroot` se sincroniza con el build frontend actualizado.

### Por que

El dashboard principal quedaba menos util para lectura rapida: primero se veian desgloses y despues la tendencia. Con la grafica arriba se prioriza el contexto temporal antes del detalle por divisa/titular.

### Reglas tecnicas

- Cambio solo de orden de componentes en JSX; sin impacto en contratos de API.
- Se conserva la misma carga paralela de `principal`, `evolucion` y `saldosDivisa`.
- Sin cambios CSS: la disposicion se apoya en estilos existentes.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK (codigo `1` esperado).

## 2026-04-26 - V-01.05 - Importacion preserva el orden de lineas pegadas

### Que cambio

- `ImportacionService.ConfirmarAsync` deja de ordenar las filas validadas por fecha antes de guardar.
- La asignacion de `fila_numero` se hace recorriendo las filas seleccionadas desde la ultima linea pegada hacia la primera.
- La linea superior del extracto pegado recibe el `fila_numero` mas alto del lote, por lo que sigue arriba cuando la vista ordena por fecha/fila descendente.
- El detalle de auditoria `primeras_filas` se calcula con el orden original de indices, no con el orden interno de insercion.

### Por que

Ordenar por fecha durante la importacion era una decision demasiado lista para su propio bien. En extractos bancarios, especialmente con lineas informativas, movimientos del mismo dia o saldos de detalle, el orden del fichero es parte del dato. Cambiarlo en backend rompe la lectura del banco y descoloca lineas auxiliares.

### Reglas tecnicas

- La validacion puede normalizar fecha, monto y saldo, pero no debe reordenar filas.
- `fila_numero` es el mecanismo de estabilidad visual: mayor numero significa linea mas reciente/superior en la vista descendente.
- Las filas no seleccionadas o invalidas no consumen `fila_numero`.
- No se cambia el ordenamiento general de `GET /api/extractos`; se corrige solo la numeracion creada por la importacion.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ImportacionServiceTests --no-restore`: 26/26 OK.
- `dotnet build ".\\Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" -c Release --no-restore`: OK, 0 warnings, 0 errores.

## 2026-04-26 - V-01.05 - Borrado multiple de extractos por cuenta

### Que cambio

- `CuentaDetailPage` incorpora seleccion multiple de filas en el desglose de cuenta.
- Se anade checkbox por fila, checkbox global para seleccionar todo y contador de seleccion.
- Se anade confirmacion unica para borrar en lote desde el mismo dashboard de cuenta.
- El borrado multiple llama en bucle al endpoint existente `DELETE /api/extractos/{id}`.

### Por que

Eliminar linea por linea era lento y propenso a errores cuando hay limpieza masiva. El cambio reduce clics sin abrir otra superficie de permisos.

### Reglas tecnicas

- No se crea endpoint nuevo: se reaprovecha la ruta actual para conservar validaciones y auditoria.
- Si falla un borrado durante el lote, se muestra error con progreso parcial y se recarga para dejar el estado real.
- El flujo de borrado multiple solo aparece si el usuario ya tiene permiso `puede_eliminar_lineas`.

### Verificacion

- `npm.cmd run build`: OK.
- `npm.cmd run lint`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK (codigo `1` esperado).

## 2026-04-26 - V-01.05 - Actualizacion automatica desde GitHub Release oficial

### Que cambio

- `ActualizacionService` mantiene `app_update_check_url` como repo oficial de GitHub (`https://github.com/AtlasLabs797/AtlasBalance`) y consulta `releases/latest` via API de GitHub.
- Si el release no trae `source_path`, el backend busca el asset `AtlasBalance-*-win-x64.zip`, valida que la URL pertenezca al repo oficial, descarga el ZIP y lo extrae dentro de `WatchdogSettings:UpdateSourceRoot`.
- Antes de entregar la ruta al Watchdog, el paquete debe contener `VERSION`, `api/AtlasBalance.API.exe` y `watchdog/AtlasBalance.Watchdog.exe`.
- La comparacion de versiones ahora normaliza etiquetas tipo `V-01.05-win-x64`, evitando comparaciones lexicas rotas con el formato real de releases.
- `WatchdogOperationsService` crea backup PostgreSQL previo con `pg_dump` antes de actualizar binarios. Si no puede crear backup y `RequireDatabaseBackupBeforeUpdate` esta activo, no actualiza.
- El Watchdog crea copia rollback de binarios antes de sincronizar y la restaura si falla la copia.
- Si `RequireHealthCheckAfterUpdate` esta activo, Watchdog exige que `ApiHealthUrl` responda OK tras arrancar la API; si falla, revierte binarios.
- La pantalla `Configuracion > Sistema` muestra el campo como repositorio GitHub de actualizaciones, no como endpoint JSON manual.

### Por que

El boton `Actualizar ahora` ya existia, pero era medio humo: con el repo de GitHub configurado podia detectar releases, pero no descargar el asset ni preparar una ruta local segura para el Watchdog. Ahora el flujo real es repo oficial -> ultimo release -> ZIP win-x64 validado -> carpeta segura de updates -> Watchdog.

### Reglas tecnicas

- No se aceptan assets fuera de `https://github.com/AtlasLabs797/AtlasBalance/releases/download/...`.
- No se extrae nada fuera de `UpdateSourceRoot`.
- No se actualiza si el paquete no parece un release Windows x64 completo.
- En produccion, `RequireDatabaseBackupBeforeUpdate` queda activo por defecto. Desactivarlo es una mala idea salvo tests controlados.
- En produccion, `RequireHealthCheckAfterUpdate` queda activo por defecto y usa `https://localhost/api/health`.

### Verificacion

- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" -c Release --filter "ActualizacionServiceTests|WatchdogOperationsServiceTests|ConfiguracionControllerTests"`: 14/14 OK.
- Parser PowerShell de `scripts/Instalar-AtlasBalance.ps1`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por copia con cambios.

## 2026-04-26 - V-01.05 - Actualizacion post-instalacion endurecida

### Que cambio

- `scripts/update.ps1` declara `PackagePath`, `InstallPath` y `SkipBackup` de forma explicita.
- El wrapper ya no usa `ValueFromRemainingArguments` para reenviar `-InstallPath` a `Actualizar-AtlasBalance.ps1`.
- `SeedData.EnsureDefaultFormatosImportacion` comprueba primero si el ID fijo del formato por defecto ya existe usando `IgnoreQueryFilters()`.
- Si el ID ya existe, el seeder no intenta insertar otra fila con la misma PK aunque banco/divisa esten incompletos, cambiados o heredados de una version anterior.
- Se agrego una regresion en `SeedDataTests` para una fila legacy con el ID de Sabadell ya existente y `BancoNombre`/`Divisa` nulos.

### Por que

La actualizacion real desde `V-01.04` demostro dos fallos operativos: el wrapper podia pasar mal `-InstallPath`, y el arranque de API podia morir antes de servir `/api/health` por `23505 pk_formatos_importacion`. Esa combinacion es mala: actualiza binarios, crea backup, pero deja el servicio parado. Arreglado en el flujo de release, no con parches manuales en servidor.

### Verificacion

- Parser PowerShell sobre `scripts/update.ps1` y `scripts/Actualizar-AtlasBalance.ps1`: OK.
- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter SeedDataTests`: 5/5 OK.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.05`: OK.
- ZIP corregido: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64.zip`, SHA256 `482189BB4B6F731CEB02ECA214A550B1CE9DB33C71F0DBF4E057761E8FD002C3`.

## 2026-04-26 - V-01.05 - Limpieza de artefactos locales

### Que cambio

- Se eliminaron artefactos locales no versionables: `.codex-runlogs/`, `output/`, logs de API y paquetes generados antiguos dentro de `Atlas Balance/Atlas Balance Release/`.
- `Atlas Balance/Atlas Balance Release/` queda solo con `.gitkeep`; los ZIP y carpetas de paquete se regeneran con `scripts/Build-Release.ps1` y se publican como assets de GitHub Releases.
- Se eliminaron directorios frontend vacios heredados de la limpieza de shadcn: `frontend/src/lib/` y `frontend/src/components/ui/`.
- `.gitignore` ahora ignora `.codex-runlogs/` y `output/`.

### Por que

Mantener paquetes release, logs, capturas y backups SQL temporales dentro del workspace ensucia el estado local y aumenta el riesgo de arrastrar datos privados. El codigo fuente y la documentacion quedan; los artefactos se regeneran cuando hacen falta.

### Verificacion

- `git check-ignore -v .codex-runlogs/foo output/foo`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ".\AtlasBalance.sln" -c Release --no-restore`: 107/108 OK; `ExtractosConcurrencyTests` falla porque Docker/Testcontainers no esta disponible.
- `dotnet test ".\AtlasBalance.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 107/107 OK.

## 2026-04-25 - V-01.05 - Paquete final y publicacion

### Que cambio

- Se regenero el paquete `AtlasBalance-V-01.05-win-x64.zip` con `scripts/Build-Release.ps1`.
- El build frontend del paquete quedo sincronizado en `backend/src/AtlasBalance.API/wwwroot`.
- El ZIP final queda fuera de Git y se publica como asset de GitHub Release.

### Verificacion

- `scripts\Build-Release.ps1 -Version V-01.05`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list "Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- Paquete verificado sin `appsettings.Development.json`, `.env`, `node_modules`, `obj`, `bin\Debug` ni `.bak-iframe-fix`.
- SHA256 final del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `B5ABC5525CBD49F2BD0A5ADC5B930A2113AF323F99C1337087B8E0D7875E6A10`.

## 2026-04-25 - V-01.05 - Auditoria de bugs y seguridad

### Que cambio

- Se reviso la superficie tecnica de seguridad activa: autenticacion JWT en cookies httpOnly, CSRF por header `X-CSRF-Token`, validacion de `SecurityStamp`, permisos backend, integracion OpenClaw, rutas de backup/exportacion, cabeceras HTTP, CI y secretos versionables.
- Se actualizaron los minimos declarados del frontend para cerrar deuda de supply chain: `axios ^1.15.2` y `react-router-dom ^6.30.3`.
- El bundle de produccion se recompilo y se sincronizo con `backend/src/AtlasBalance.API/wwwroot`.
- No se cambiaron contratos de API ni modelo de datos.

### Por que

El lockfile ya resolvia versiones seguras, pero dejar rangos minimos vulnerables en `package.json` es pedir que una reinstalacion sin lockfile fiable abra otra vez el agujero. Eso no es "flexibilidad", es pereza con consecuencias.

### Verificacion

- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ".\Atlas Balance\backend\AtlasBalance.sln" -c Release --no-build`: 107/107 OK.
- `dotnet list ".\Atlas Balance\backend\AtlasBalance.sln" package --vulnerable --include-transitive`: sin vulnerabilidades.
- `wwwroot`: sincronizado y sin sourcemaps, plantillas Development ni `.env`.

## 2026-04-25 - V-01.05 - Importacion simple de plazo fijo y resumen dashboard

### Que cambio

- `CuentaImportacionContextoResponse` expone `TipoCuenta` para que el frontend distinga cuentas normales, efectivo y plazo fijo.
- `ImportacionService.ValidarAsync` y `ConfirmarAsync` rechazan importaciones con formato para `PLAZO_FIJO`.
- Nuevo contrato `ImportacionPlazoFijoMovimientoRequest/Response`.
- Nuevo endpoint `POST /api/importacion/plazo-fijo/movimiento`.
- `RegistrarMovimientoPlazoFijoAsync` exige permiso de importacion, cuenta activa de plazo fijo, monto positivo y fecha.
- El movimiento usa `INGRESO` como monto positivo y `EGRESO` como monto negativo, calcula `saldo_actual = ultimo_saldo + monto_firmado`, asigna `fila_numero` con bloqueo transaccional cuando la BD es relacional y registra auditoria.
- `DashboardPrincipalResponse` incluye `PlazosFijos` con monto total convertido, intereses previstos convertidos, fecha/dias del proximo vencimiento y numero de cuentas.
- `DashboardService` calcula ese resumen con las cuentas visibles para el usuario y excluye plazos `RENOVADO`/`CANCELADO` del calculo de intereses/vencimiento.
- El frontend cambia automaticamente a un formulario simple cuando la cuenta seleccionada es `PLAZO_FIJO`.

### Por que

Un plazo fijo no tiene extracto bancario normal que mapear. Forzar CSV/Excel aqui era burocracia tecnica: lo correcto es registrar entrada o salida y que el sistema calcule el saldo.

### Reglas tecnicas

- Las cuentas de plazo fijo no deben depender de `formatos_importacion`.
- No permitir monto negativo en request; el signo lo decide `tipo_movimiento`.
- Los intereses previstos siguen siendo importe absoluto aproximado, no porcentaje.
- El resumen de dashboard respeta el alcance de cuentas visible para el usuario.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.
- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter "ImportacionServiceTests|DashboardServiceTests"`: 28/28 OK.
- `dotnet build "Atlas Balance/backend/src/AtlasBalance.API/AtlasBalance.API.csproj" -c Release`: OK, 0 warnings.

## 2026-04-25 - V-01.05 - Actualizaciones post-instalacion

### Que cambio

- `update.cmd` y `Actualizar Atlas Balance.cmd` devuelven el codigo de salida de PowerShell.
- `scripts\update.ps1` valida que el origen sea un paquete release antes de autoelevar.
- `scripts\update.ps1` soporta `-PackagePath` para que una instalacion ya actualizada pueda aplicar paquetes futuros desde otra carpeta.
- `scripts\Actualizar-AtlasBalance.ps1` conserva configuracion, crea backup DB previo, crea rollback de binarios, reemplaza API y Watchdog, copia scripts/wrappers operativos a la instalacion, actualiza `VERSION`, actualiza `atlas-balance.runtime.json` y valida `/api/health`.

### Por que

Instalar una vez no basta. Si el update no actualiza tambien su propia maquinaria, la siguiente actualizacion vuelve a depender de scripts viejos. Eso es deuda operativa disfrazada de "ya lo vemos luego".

### Verificacion

- Parser PowerShell OK para `update.ps1` y `Actualizar-AtlasBalance.ps1`.
- Ejecutar update desde carpeta fuente falla con mensaje de paquete invalido.
- `scripts\Build-Release.ps1 -Version V-01.05`: OK; ZIP regenerado.
- Scripts empaquetados parsean correctamente.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.
- Pendiente de entorno real: probar update desde `V-01.03` instalada a `V-01.05` en Windows Server 2019.

## 2026-04-25 - V-01.05 - Cierre de incidencias instalacion Windows Server 2019

### Que cambio

- `scripts\install.ps1` valida que la carpeta sea un paquete release antes de autoelevar.
- `scripts\Instalar-AtlasBalance.ps1` valida `api\AtlasBalance.API.exe` y `watchdog\AtlasBalance.Watchdog.exe` antes de instalar.
- El instalador mantiene autodeteccion `PostgreSQL\17\bin` antes que `16\bin` y muestra instrucciones concretas para instalacion manual si `winget` falla.
- El instalador detecta usuarios existentes en `"USUARIOS"` y, si los hay, no escribe `SeedAdmin:Password` ni un `Password admin inicial` falso.
- `scripts\Reset-AdminPassword.ps1` resetea una cuenta admin usando la conexion de produccion local: genera hash bcrypt 12, marca `primer_login`, activa usuario, limpia bloqueo, rota `security_stamp` y revoca refresh tokens.
- `scripts\Build-Release.ps1` empaqueta `Reset-AdminPassword.ps1` e `install-cert-client.ps1`.
- El health check post-instalacion usa `curl.exe -k` si esta disponible y deja `Invoke-WebRequest` como fallback.

### Por que

La instalacion estaba demasiado optimista. En Windows Server 2019 eso es pedir problemas: `winget` puede no existir, PowerShell puede fallar con TLS autofirmado y una BD existente no significa admin nuevo. El cambio elimina mentiras operativas.

### Reglas tecnicas

- Nunca ejecutar instalacion de servidor desde ZIP `main`/carpeta fuente.
- No regenerar credenciales iniciales si la BD ya tiene usuarios.
- No pedir SQL manual largo para reset admin; usar `Reset-AdminPassword.ps1`.
- Para health check operativo en Server 2019, preferir `curl.exe -k`.

### Verificacion

- Parser PowerShell OK para `Instalar-AtlasBalance.ps1`, `install.ps1`, `Reset-AdminPassword.ps1` y `Build-Release.ps1`.
- `Instalar-AtlasBalance.ps1` desde carpeta fuente falla con mensaje de paquete invalido.
- `install.ps1` desde carpeta fuente falla con mensaje de paquete invalido antes de autoelevar.
- `scripts\Build-Release.ps1 -Version V-01.05`: OK; ZIP generado.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Scripts empaquetados parsean correctamente.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.

## 2026-04-25 - V-01.05 - Apertura de version

### Que cambio

- `V-01.05` pasa a ser la version activa del sistema.
- Backend: `Directory.Build.props` sube a `1.5.0` y `InformationalVersion` a `V-01.05`.
- Frontend: `package.json` y `package-lock.json` suben a `1.5.0`; `appVersion` pasa a `V-01.05`.
- `Atlas Balance/VERSION`, `SeedData`, `Build-Release.ps1` e `Instalar-AtlasBalance.ps1` quedan alineados con `V-01.05`.
- `Documentacion/Versiones/v-01.03.md` queda cerrada como version publicada.
- `Documentacion/Versiones/v-01.05.md` queda como archivo activo de trabajo.

### Por que

`V-01.03` ya fue publicada. Seguir metiendo cambios ahi seria una forma bastante tonta de romper la trazabilidad.

### Reglas tecnicas

- Todo cambio nuevo debe documentarse bajo `V-01.05`.
- El siguiente paquete debe generarse con `scripts/Build-Release.ps1 -Version V-01.05`.
- No reutilizar assets ni notas de release de `V-01.03` para publicar `V-01.05`.

### Verificacion

- `git diff --check`: OK.
- `dotnet build '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `npm.cmd run build`: OK con `atlas-balance-frontend@1.5.0`.

## 2026-04-25 - V-01.03 - Paquete release Windows x64 generado

### Que cambio

- Se genero el paquete `AtlasBalance-V-01.03-win-x64` en `Atlas Balance/Atlas Balance Release`.
- Se genero el ZIP `AtlasBalance-V-01.03-win-x64.zip` para distribucion.
- `scripts/Build-Release.ps1` recompilo el frontend y reemplazo `AtlasBalance.API/wwwroot` con el bundle de produccion actual.
- API y Watchdog quedaron publicados como self-contained `win-x64`.
- El paquete incluye scripts operativos, `VERSION`, `README.md`, `documentacion.md`, `.gitignore` y `version.json`.

### Reglas tecnicas

- Los artefactos de `Atlas Balance/Atlas Balance Release` no deben entrar en commits normales; van como assets de GitHub Releases.
- Si se cambia documentacion incluida en el paquete despues de generar el ZIP, hay que regenerar el release. No hacerlo seria publicar un paquete con instrucciones atrasadas.
- `version.json` debe conservar `source_path = C:\AtlasBalance\updates\V-01.03\api` para actualizaciones de esta version.

### Verificacion

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.03`: OK.
- Carpeta generada: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.03-win-x64`.
- ZIP generado: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.03-win-x64.zip`.
- `version.json` y `VERSION` empaquetados: `V-01.03`.
- Barrido de `api` empaquetada: sin `*Development*`, `*.template` ni `.env`.

## 2026-04-25 - V-01.03 - Hardening de seguridad post-auditoria

### Que cambio

- Se agregaron `SecurityStamp` y `PasswordChangedAt` a `USUARIOS` mediante la migracion `UserSessionHardening`.
- Los access tokens incluyen `security_stamp`; `UserStateMiddleware` lo valida contra BD en cada request API autenticado.
- Cambios/reset de password, borrado de usuario y reuse de refresh token rotan el stamp y revocan refresh tokens activos.
- Login usa throttle por cliente/email y deja de distinguir externamente usuario bloqueado de credenciales invalidas.
- Reuse de refresh token revocado escala a incidente: revoca sesiones activas, rota stamp y registra `REFRESH_TOKEN_REUSE_DETECTED`.
- Passwords de usuarios y seed admin pasan a minimo 12 caracteres y bloqueo de passwords comunes.
- `IntegrationAuthMiddleware` corta bearer invalido repetido por IP/minuto antes de consultar tokens activos.
- `app_update_check_url` queda limitado a HTTPS del repo oficial `AtlasLabs797/AtlasBalance`.
- Backups, exportaciones, descargas y rutas Watchdog validan la ruta cruda antes de `Path.GetFullPath`.
- `config\INSTALL_CREDENTIALS_ONCE.txt` se borra automaticamente con tarea programada SYSTEM a las 24 horas.
- `postcss` queda resuelto a `8.5.10`.

### Impacto operativo

- Tras desplegar esta version, los access tokens antiguos sin `security_stamp` dejan de ser validos. Eso es correcto: los usuarios tendran que autenticarse otra vez.
- La URL de actualizaciones ya no acepta endpoints arbitrarios; si se necesita otro canal de releases, primero hay que ampliar la allowlist de forma explicita.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.

### Verificacion

- `dotnet build '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `dotnet test '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-build`: 94/94 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list '.\Atlas Balance\backend\AtlasBalance.sln' package --vulnerable --include-transitive`: sin vulnerabilidades.
- Parser PowerShell sobre `scripts/Instalar-AtlasBalance.ps1`: OK.

## 2026-04-20 - V-01.03 - Apertura de version

### Que cambio

- `V-01.03` pasa a ser la version activa del sistema.
- Backend: `Directory.Build.props` sube a `1.3.0` y `InformationalVersion` a `V-01.03`.
- Frontend: `package.json` y `package-lock.json` suben a `1.3.0`; `appVersion` pasa a `V-01.03`.
- `Atlas Balance/VERSION`, `SeedData`, `Build-Release.ps1` e `Instalar-AtlasBalance.ps1` quedan alineados con `V-01.03`.
- `Documentacion/Versiones/v-01.02.md` queda cerrada como version publicada.
- `Documentacion/Versiones/v-01.03.md` queda como archivo activo de trabajo.

### Por que

`V-01.02` ya fue publicada. Seguir metiendo cambios ahi seria versionado barro: funciona hasta que alguien necesita saber que demonios se desplego.

### Reglas tecnicas

- Todo cambio nuevo debe documentarse bajo `V-01.03`.
- El siguiente paquete debe generarse con `scripts/Build-Release.ps1 -Version V-01.03`.
- No reutilizar assets ni notas de release de `V-01.02` para publicar `V-01.03`.

### Verificacion

- `git diff --check`: OK; solo avisos esperados de normalizacion LF/CRLF.
- `dotnet build '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `npm.cmd run build`: OK con `atlas-balance-frontend@1.3.0`.

## 2026-04-20 - V-01.02 - Release autonoma con scripts one-click

### Que cambio

- El paquete de release ahora incluye `install.cmd`, `update.cmd`, `uninstall.cmd` y `start.cmd`.
- Los `.cmd` llaman wrappers PowerShell en `scripts/install.ps1`, `scripts/update.ps1`, `scripts/uninstall.ps1` y `scripts/start.ps1`.
- `install.cmd` se autoeleva y llama al instalador real con `-InstallDependencies` por defecto.
- `Instalar-AtlasBalance.ps1` puede preparar PostgreSQL 16 gestionado con `winget`, usando servicio `AtlasBalance.PostgreSQL`, password generada y puerto libre si `5432` esta ocupado.
- `atlas-balance.runtime.json` registra si PostgreSQL es gestionado por Atlas, su servicio y la configuracion DB usada.
- `Launch-AtlasBalance.ps1` arranca en orden: PostgreSQL gestionado, Watchdog y API.
- `Actualizar-AtlasBalance.ps1` arranca PostgreSQL gestionado antes de crear backup y reemplazar binarios.
- `uninstall.ps1` elimina servicios, firewall, atajos, `%ProgramData%\AtlasBalance`, carpeta instalada y PostgreSQL gestionado si fue creado por el instalador.
- `Build-Release.ps1` copia los nuevos scripts y `README_RELEASE.md` dentro del paquete generado.

### Por que

La release anterior tenia piezas utiles, pero no cumplia literalmente el contrato de "install/update/uninstall/start" ni arrancaba la base de datos desde `start`. Eso es una grieta operativa: si PostgreSQL queda parado, el backend no arranca y el usuario culpa al frontend. Mal diagnostico, mala noche.

### Reglas tecnicas

- El frontend no se instala en produccion: se compila con Vite y se sirve desde `wwwroot` en la API.
- El backend publicado es self-contained; el servidor no necesita .NET Runtime.
- La API aplica migraciones EF Core en startup.
- Si se usa PostgreSQL externo, el instalador exige password admin o binarios `psql`; no intenta adivinar credenciales.
- `uninstall.cmd` solo borra la base gestionada por Atlas. Una base externa no se elimina sin una decision explicita.

### Verificacion

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.02`: OK.
- Paquete generado en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.02-win-x64`.
- ZIP generado en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.02-win-x64.zip`.
- Parser PowerShell sobre scripts fuente y scripts empaquetados: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK dentro del build de release.
- `dotnet test .\backend\AtlasBalance.sln -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 82/82 OK.
- Suite backend completa: 82/83 OK; `ExtractosConcurrencyTests` falla por Docker/Testcontainers no disponible en este entorno, incidencia ya conocida.
- Scanner local de secretos sobre el paquete generado: 0 hallazgos.
- Paquete verificado sin `appsettings.Development.json`, plantillas, source maps, `node_modules` ni `frontend/dist` suelto.
- `winget search PostgreSQL.PostgreSQL --source winget`: confirma existencia de `PostgreSQL.PostgreSQL.16` en este entorno.

## 2026-04-20 - V-01.02 - Auditoria tecnica profunda y hardening

### Que cambio

- `smtp_password` y `exchange_rate_api_key` en `CONFIGURACION` se almacenan protegidos con ASP.NET Core Data Protection y prefijo `enc:v1:`.
- En cada arranque, la API migra automaticamente esos valores si aun estan en claro.
- En produccion, las claves de Data Protection se guardan fuera del directorio servido, por defecto en `%ProgramData%/AtlasBalance/keys`; puede sobrescribirse con `DataProtection:KeysPath`. En Windows se protegen con DPAPI de maquina.
- `ConfiguracionController` no devuelve secretos al frontend y redacta esos valores en auditoria.
- `EmailService` y `TiposCambioService` descifran secretos solo justo antes de usarlos.
- `UserAccessService` ya no interpreta `PuedeVerDashboard` global como permiso global de datos.
- `ExportacionesController.Descargar` valida que el fichero sea `.xlsx` y este dentro de `export_path`.
- `AtlasBalance.Watchdog` escucha explicitamente en localhost mediante Kestrel.
- La API rechaza `AllowedHosts` vacio, placeholder o wildcard fuera de Development.
- Scripts de backup/restore/manual/service install usan nombres y usuarios actuales, restauran `PGPASSWORD` y validan extension `.dump`.
- Se eliminaron logs y artefactos de smoke/login con cookies, cabeceras o payloads sensibles.

### Por que

Guardar secretos en claro dentro de la tabla de configuracion era el riesgo mas serio que quedaba. Y el permiso global de dashboard era peor de lo que parecia: podia abrir datos fuera del alcance esperado. Eso no era "deuda tecnica"; era una fuga esperando su turno.

### Reglas tecnicas

- No leer `smtp_password` ni `exchange_rate_api_key` directamente salvo a traves de `ISecretProtector`.
- No cambiar la cuenta de servicio, mover de maquina o borrar el keyring de Data Protection sin plan de rotacion; los secretos cifrados quedarian ilegibles.
- Las exportaciones descargables deben seguir saliendo solo de `export_path`.
- Watchdog debe permanecer en loopback y autenticado con `X-Watchdog-Secret`.
- Produccion debe declarar hosts explicitos en `AllowedHosts`; wildcards ya no son aceptables.

### Verificacion

- `dotnet build "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore`: OK, 0 warnings.
- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-build`: 83/83 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet list ... package --vulnerable --include-transitive`: sin vulnerabilidades.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.

### Pendientes

- Rotar secretos locales si `.env` o `appsettings.Development.json` se compartieron fuera del equipo.
- Reparar el estado Git local si se necesita diff/commit/push fiable desde esta copia.

## 2026-04-20 - V-01.02 - Cierre de bugs reportados

### Que cambio

- `SeedAdmin:Email` queda normalizado como `admin@atlasbalance.local` en configuracion base y plantillas.
- Se corrigieron ejemplos, placeholders, rutas por defecto y tests que arrastraban `atlasbalnace` o `atlas-blance`.
- El evento interno de importacion ahora usa la constante compartida `IMPORTACION_COMPLETADA_EVENT` con namespace `atlas-balance`.
- `Instalar-AtlasBalance.ps1` escribe runtime `V-01.02`, no `V-01.01`.
- La documentacion de instalacion y `SPEC.md` apuntan a `V-01.02` y rutas `C:/AtlasBalance`.
- El build frontend generado se copio a `backend/src/AtlasBalance.API/wwwroot` para que la API local sirva el bundle corregido.

### Por que

La revision previa no estaba equivocada, pero estaba incompleta: el codigo principal ya tenia varios fixes, mientras que configuracion, scripts y artefactos servidos seguian arrastrando restos. Eso es peor que un bug obvio, porque parece arreglado hasta que instalas o pruebas desde el backend.

### Verificacion

- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore`: 82/82 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `docker ps --filter "name=atlas_balance_db"`: contenedor activo en `5433->5432`.
- Barrido `Select-String` en codigo activo y `wwwroot`: 0 restos de `atlasbalnace`, `atlas-blance` o `V-01.01`.

### Pendientes

- Ninguno de estos bugs queda abierto.

## 2026-04-20 - V-01.02 - Auditoria de seguridad y bugs

### Que cambio

- Se eliminaron secretos y passwords de desarrollo de configuracion versionable.
- `SeedAdmin:Password` pasa a ser obligatorio antes del primer arranque con BD vacia.
- Si `JwtSettings:Secret` falta en Development, la API genera una clave efimera de proceso; fuera de Development sigue siendo obligatorio.
- Watchdog ya no usa password de BD por defecto para restauraciones.
- `docker-compose.yml` exige `ATLAS_BALANCE_POSTGRES_PASSWORD` desde `.env` local o variable de entorno.
- Se anadieron plantillas de configuracion para API y Watchdog, y un `.env.example` sin secretos.
- `SeedData` usa `V-01.02` y el check de actualizacion usa la version runtime en el User-Agent.
- Se corrigieron mensajes mojibake en importacion y asunto SMTP.
- GitHub Actions queda fijado a SHAs concretos para reducir riesgo de supply chain.
- Se anadio `.gitignore` dentro de `Atlas Balance` para proteger la app si se trabaja desde esa carpeta como raiz.

### Por que

Los secretos "solo de desarrollo" en archivos base son una bomba lenta: se copian, se reutilizan y un dia llegan a produccion. La configuracion base debe ser segura por defecto y obligar a crear secretos locales/produccion fuera de Git.

### Reglas tecnicas

- No commitear `appsettings.Development.json`, `appsettings.Production.json`, `.env`, certificados, logs ni paquetes generados.
- Para desarrollo local, copiar las plantillas y rellenar secretos reales en archivos ignorados.
- Para produccion, generar secretos fuertes distintos para JWT, Watchdog, PostgreSQL, certificado y admin inicial.
- No ejecutar restauraciones Watchdog si `WatchdogSettings:DbPassword` no esta configurado.

### Verificacion

- `python Skills/Seguridad/cyber-neo-main/skills/cyber-neo/scripts/scan_secrets.py "Atlas Balance" --json`: 0 hallazgos.
- `dotnet list "Atlas Balance/backend/AtlasBalance.sln" package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm.cmd audit --json`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

### Pendientes

- Reparar el estado Git local si se necesita commit/push fiable desde esta carpeta.

## 2026-04-20 - V-01.01 - Reorganizacion de estructura

### Que cambio

- La aplicacion quedo centralizada en `Atlas Balance`.
- Los paquetes existentes quedaron en `Atlas Balance/Atlas Balance Release`.
- La documentacion quedo centralizada en `Documentacion`.
- Material auxiliar, duplicados y artefactos temporales quedaron en `Otros`.
- `CLAUDE.md` y `AGENTS.md` fueron actualizados sin planificacion por bloques temporales.
- `Atlas Balance/scripts/Build-Release.ps1` ahora genera paquetes en `Atlas Balance/Atlas Balance Release`.
- `Build-Release.ps1` copia la documentacion de usuario desde `Documentacion/documentacion.md`.
- El repositorio Git quedo en la raiz para versionar juntos `Atlas Balance` y `Documentacion`.

### Por que

La estructura anterior mezclaba app real, scaffolding, duplicados, documentacion, repos auxiliares de diseno y artefactos generados. Eso aumenta el riesgo de tocar lo equivocado y hace mas dificil empaquetar o revisar cambios.

### Como queda

- Runtime y codigo fuente: `Atlas Balance`
- Releases: `Atlas Balance/Atlas Balance Release`
- Documentacion: `Documentacion`
- Auxiliares no runtime: `Otros`

### Verificacion esperada

- `git status --short` debe funcionar desde la raiz del proyecto.
- `powershell -File "Atlas Balance/scripts/Build-Release.ps1" -Version V-01.01` debe publicar en `Atlas Balance/Atlas Balance Release`.
- `dotnet build "Atlas Balance/backend/AtlasBalance.sln" --no-restore` debe resolver rutas relativas dentro de la app.

## 2026-04-20 - V-01.01 - Catalogo de skills locales

### Que cambio

- Se analizo `Skills` y se separaron skills reales de copias repetidas por agente.
- Se creo `Documentacion/SKILLS_LOCALES.md` como catalogo canonico.
- Se actualizaron `CLAUDE.md`, `AGENTS.md`, `Atlas Balance/CLAUDE.md` y `Atlas Balance/AGENTS.md` para indicar como y cuando usar skills locales.

### Por que

La carpeta `Skills` contiene repos completos y varias carpetas repetidas para diferentes agentes. Sin una guia, un agente puede cargar duplicados, ejecutar scripts innecesarios o aplicar reglas de stack equivocadas. Eso seria ruido, no mejora.

### Reglas tecnicas

- La documentacion canonica de uso vive en `Documentacion/SKILLS_LOCALES.md`.
- Para cada tarea se debe cargar solo la skill relevante.
- Las recomendaciones de las skills se subordinan al stack real de Atlas Balance.
- No se deben ejecutar CLIs o scripts dentro de `Skills` sin necesidad clara.

## 2026-04-20 - V-01.01 - Politica de subida a GitHub

### Que cambio

- `.gitignore` ahora excluye explicitamente `Otros/` y `Skills/`.
- `Atlas Balance/Atlas Balance Release/` queda como carpeta local de salida, mantenida en Git solo con `.gitkeep`.
- Los paquetes generados de release se publican como assets de GitHub Releases, no como archivos en la historia Git.
- Las instrucciones de agentes indican que GitHub debe recibir todo lo versionable excepto `Otros/`, `Skills/` y paquetes generados de release.

### Por que

El repositorio oficial debe contener el proyecto util para desarrollo, documentacion y configuracion, pero no repos auxiliares, duplicados de trabajo, skills locales pesadas ni binarios generados. Los ZIP de release pesan demasiado para vivir comodamente en Git; GitHub Releases es el sitio correcto para distribuirlos.

### Reglas tecnicas

- Subir a GitHub como Git: codigo, documentacion, configuracion y scripts.
- Subir a GitHub Releases: ZIP, carpetas empaquetadas y binarios generados de release.
- No subir nunca: `Otros/`, `Skills/`, secretos, `.env`, logs, cookies, tokens, certificados privados, `node_modules`, `bin/obj` ni artefactos locales sensibles.

## 2026-04-23 - V-01.03 - Cierre de fuga de alcance global en extractos

### Que cambio

- `ExtractosController.GetAllowedAccountIds` y `CanViewTitular` dejaron de tratar `PuedeVerDashboard` global como permiso global de datos.
- El alcance global en extractos queda restringido a permisos de datos reales: `PuedeAgregarLineas`, `PuedeEditarLineas`, `PuedeEliminarLineas` o `PuedeImportar`.
- Se agrego regresion automatizada en `ExtractosControllerTests` para impedir que `/api/extractos` devuelva datos cross-account a perfiles dashboard-only globales.

### Por que

La logica local de `ExtractosController` estaba mas permisiva que `UserAccessService`. Esa divergencia abria una fuga de datos financieros entre cuentas.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~AtlasBalance.API.Tests.ExtractosControllerTests|FullyQualifiedName~AtlasBalance.API.Tests.UserAccessServiceTests"`: 8/8 OK.

## 2026-04-24 - V-01.03 - Frontend alineado con permisos reales de cuenta

### Que cambio

- `frontend/src/stores/permisosStore.ts` diferencia entre alcance de cuenta y permiso global solo de dashboard.
- Una fila global `cuenta_id = null`, `titular_id = null` ya no habilita `canViewCuenta` ni contamina `getColumnasVisibles/getColumnasEditables` salvo que conceda acceso global de datos (`agregar`, `editar`, `eliminar`, `importar`).
- `frontend/src/pages/CuentasPage.tsx` ya no ofrece enlaces o botones a `/dashboard/cuenta/:id` para cuentas sin acceso real; muestra `Sin acceso`.
- `frontend/src/pages/CuentaDetailPage.tsx` intercepta `403` del backend y redirige a `/dashboard` en vez de dejar al usuario atrapado en un error de carga.

### Por que

El backend ya estaba bien. El frontend seguia mintiendo: ensenaba rutas de cuenta a perfiles `dashboard-only` globales, como si pudieran abrirlas. Eso no filtraba datos, pero era UX rota y semantica de permisos incoherente.

### Reglas tecnicas

- En frontend, el acceso a cuenta no debe inferirse de cualquier permiso coincidente. Una fila global solo vale como acceso de cuenta si equivale a acceso global de datos.
- Los estados visuales de apertura de cuenta tienen que apoyarse en la misma semantica que backend. Si backend va a responder `403`, frontend no debe mostrar un CTA operativo.
- Cuando una ruta depende de datos protegidos y el backend responde `403`, la pantalla debe redirigir o cerrar el paso de forma limpia, no quedarse en un error generico.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.

## 2026-04-25 - V-01.05 - Importacion con advertencias para filas solo concepto

### Que cambio

- `ImportacionService.ValidateRows` diferencia errores fatales de advertencias importables.
- Las filas con concepto y fecha/monto/saldo vacios pasan a ser validas con advertencias.
- Para poder persistirlas en `EXTRACTOS`, la fecha y el saldo se heredan de la ultima fila valida anterior y el monto se normaliza a `0`.
- `FilaValidacionResponse` expone `Advertencias` y el frontend las muestra en la tabla de validacion con estado visual de aviso.
- Se agregaron regresiones para validar e importar filas informativas sin romper las reglas existentes de filas ambiguas.

### Por que

Algunos bancos exportan lineas informativas o de detalle como filas separadas con solo concepto. Bloquearlas como error obligaba al usuario a descartarlas aunque quisiera conservar esa informacion en el extracto.

### Reglas tecnicas

- Solo se relajan filas claramente informativas: concepto presente y fecha, importe y saldo vacios.
- Una fila con fecha/saldo pero importe vacio sigue siendo error; eso ya no es una descripcion, es un movimiento incompleto.
- Una fila sin referencia previa de fecha o saldo sigue siendo error, porque inventar datos financieros desde cero seria una mala idea.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ImportacionServiceTests`: 21/21 OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.

## 2026-04-25 - V-01.05 - Permiso global explicito para ver cuentas

### Que cambio

- `PERMISOS_USUARIO` incorpora `puede_ver_cuentas`.
- `UserAccessService`, `ExtractosController`, `AuthService` y las respuestas de permisos exponen y respetan ese permiso.
- El alcance global sobre todas las cuentas se concede si existe una fila global (`cuenta_id = null`, `titular_id = null`) con `puede_ver_cuentas` o con permisos de datos heredados (`agregar`, `editar`, `eliminar`, `importar`).
- El modal de usuarios agrega el boton `Acceso a todas las cuentas` y el checkbox `Ver cuentas`.
- La migracion `AddPuedeVerCuentasPermiso` rellena `puede_ver_cuentas = true` para permisos existentes que ya daban acceso por scope o por acciones de datos, sin convertir permisos globales dashboard-only.

### Por que

Hasta ahora se podia conseguir acceso global solo dejando scope vacio y marcando una accion de datos. Eso era poco claro y empujaba a conceder importacion o edicion solo para que el usuario pudiera ver cuentas. Mala idea: visibilidad y escritura deben ser permisos distintos.

### Reglas tecnicas

- `puede_ver_dashboard` no concede acceso a extractos ni a todas las cuentas.
- `puede_ver_cuentas` concede visibilidad/lectura de cuentas dentro de su scope.
- Los permisos de escritura/importacion siguen implicando visibilidad para compatibilidad, pero no al reves.

### Verificacion

- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter "UserAccessServiceTests|UsuariosControllerTests|ExtractosControllerTests"`: 12/12 OK.
- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 97/97 OK.
- `dotnet build "Atlas Balance/backend/src/AtlasBalance.API/AtlasBalance.API.csproj" -c Release`: OK, 0 warnings.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; `robocopy` devolvio codigo `1`, copia correcta con archivos actualizados.

## 2026-04-25 - V-01.05 - Plazo fijo, autonomos, alertas por tipo y dashboard inmovilizado

### Que cambio

- `TipoTitular` incorpora `AUTONOMO` sin alterar los valores enteros existentes de `EMPRESA` y `PARTICULAR`.
- `Cuenta` incorpora `TipoCuenta`; `es_efectivo` se mantiene por compatibilidad, pero la logica nueva usa `tipo_cuenta`.
- Nueva tabla `PLAZOS_FIJOS` con relacion 1:1 a cuenta, cuenta de referencia opcional, fechas, interes previsto, renovable, estado, notificacion y soft delete.
- Nueva migracion `AddPlazoFijoAutonomosAlertas`: rellena `tipo_cuenta = EFECTIVO` desde `es_efectivo`, crea indices y constraints de fechas/interes.
- `GET /api/titulares` acepta `tipoTitular`.
- `GET /api/cuentas` acepta `tipoTitular` y `tipoCuenta`; las respuestas exponen `titular_tipo`, `tipo_cuenta` y `plazo_fijo`.
- `POST/PUT /api/cuentas` crean y editan cuentas de plazo fijo.
- `POST /api/cuentas/{id}/plazo-fijo/renovar` renueva manualmente, audita y no crea movimientos.
- `PlazoFijoVencimientoJob` corre diario con Hangfire y usa `IPlazoFijoService`.
- `ALERTAS_SALDO` admite `tipo_titular`; `AlertaService` aplica prioridad cuenta > tipo titular > global.
- Dashboard separa saldos disponibles e inmovilizados y agrupa saldos por titular por tipo.

### Por que

Un plazo fijo es patrimonio, pero no liquidez. Meterlo como saldo normal mentia en el dashboard. La app ahora diferencia dinero disponible de dinero inmovilizado sin inventar transferencias ni liquidaciones automaticas.

### Reglas tecnicas

- No cambiar una cuenta `PLAZO_FIJO` a otro tipo: se bloquea y se debe crear otra cuenta.
- `fecha_vencimiento >= fecha_inicio`.
- `interes_previsto` es importe absoluto y no puede ser negativo.
- El job marca `VENCIDO` el mismo dia de vencimiento.
- Las alertas globales, por tipo y por cuenta son mutuamente excluyentes por alcance.
- `puede_ver_dashboard` sigue sin abrir datos fuera del alcance autorizado.

### Verificacion

- `dotnet build ...AtlasBalance.API.csproj -c Release`: OK.
- Tests focalizados de cuentas/dashboard/alertas/plazos: 12/12 OK.
- Tests backend sin Testcontainers: 103/103 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.

## 2026-04-25 - V-01.05 - Coherencia visual del frontend

### Que cambio

- `frontend/src/styles/variables.css` incorpora tokens semanticos para controles, superficies, sombras, foco y estados de interaccion.
- `frontend/src/styles/global.css` alinea inputs, selects, botones base y tokens shadcn/Tailwind con las variables propias de Atlas Balance.
- `frontend/src/components/ui/button.tsx` deja de usar medidas y colores genericos de shadcn y pasa a respetar radios, alturas, foco y variantes del sistema visual de la app.
- `frontend/src/styles/layout.css` agrega una capa comun para paginas, headers, cards, tablas, tabs, navegacion, modales y estados hover/focus.
- `frontend/src/styles/auth.css` ajusta login para usar las mismas superficies, foco, sombras y boton primario del resto del producto.

### Por que

La app tenia buena base, pero habia dos sistemas visuales compitiendo: CSS variables propias y tokens shadcn/Tailwind genericos. Eso acababa creando diferencias sutiles entre botones, tabs, campos, cards y estados de foco. Sutil en una pantalla; feo cuando recorres toda la app.

### Reglas tecnicas

- No se agrega ninguna dependencia.
- Tailwind/shadcn solo se usan donde ya existian; sus tokens se subordinan al sistema propio.
- Las alturas minimas de controles se mantienen cerca de 44px para touch y teclado.
- Las animaciones siguen limitadas a color, sombra, transform y opacity.
- Los cambios son sistemicos; no se reescribe funcionalidad de paginas.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Screenshots Playwright de `/login`: `output/playwright/ui-login-desktop.png` y `output/playwright/ui-login-mobile.png`.

## 2026-04-25 - V-01.05 - CSS de layout separado por dominios

### Que cambio

- `frontend/src/styles/layout.css` queda como archivo indice con imports.
- Los estilos se reparten en:
  - `frontend/src/styles/layout/shell.css`
  - `frontend/src/styles/layout/users.css`
  - `frontend/src/styles/layout/extractos.css`
  - `frontend/src/styles/layout/entities.css`
  - `frontend/src/styles/layout/dashboard.css`
  - `frontend/src/styles/layout/importacion.css`
  - `frontend/src/styles/layout/admin.css`
  - `frontend/src/styles/layout/system-coherence.css`

### Por que

`layout.css` habia pasado de ser hoja de layout a cajon de todo: shell, usuarios, extractos, titulares, dashboard, importacion, configuracion, auditoria y capa visual comun. Eso escala fatal. Separarlo reduce el coste de tocar una pantalla sin romper otra por accidente.

### Reglas tecnicas

- Se mantiene el orden original de cascada mediante imports en `layout.css`.
- No se cambia ningun selector ni comportamiento visual intencionadamente.
- `system-coherence.css` queda al final porque actua como capa comun de overrides visuales.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `git diff --check` en los CSS tocados: OK, con aviso esperado de normalizacion CRLF/LF.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Calendario nativo alineado con inputs

### Que cambio

- `frontend/src/styles/global.css` agrega reglas para `input[type='date']`.
- Se fuerza `color-scheme` claro/oscuro en `html` y en los inputs de fecha para que el picker nativo del navegador respete el tema activo.
- Se estiliza `::-webkit-calendar-picker-indicator` con fondo, radio, hover, active y filtro en dark mode.
- Se normalizan las partes internas `::-webkit-datetime-edit` y `::-webkit-datetime-edit-fields-wrapper`.

### Por que

Los campos de fecha del plazo fijo eran inputs nativos y el icono/picker del calendario quedaban fuera del sistema visual. Feo y evitable.

### Limitacion

El calendario desplegable es nativo del navegador/OS. CSS puede mejorar tema e indicador, pero no convertirlo en un componente totalmente propio sin reemplazar `input type="date"` por un date picker custom.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Vencimiento visible en detalle de plazo fijo

### Que cambio

- `ExtractosDtos.CuentaResumenKpiResponse` incluye `TipoCuenta` y `PlazoFijo`.
- `ExtractosController.GetCuentaResumen`, `GetCuentasTitular` y `GetTitularesResumen` pasan `TipoCuenta` a `BuildSummary`.
- `BuildSummary` adjunta `PlazoFijoResponse` solo para cuentas `PLAZO_FIJO`.
- `CuentaDetailPage` muestra una banda compacta bajo el titulo con fecha de vencimiento, dias restantes/vencido y estado.
- `entities.css` agrega estilos de `.cuenta-plazo-summary`.

### Por que

El dato de vencimiento existia al crear/editar la cuenta y en la lista de cuentas, pero no aparecia en el dashboard de cuenta. Eso obligaba al usuario a salir de la pantalla donde esta mirando saldo y movimientos, justo donde el vencimiento importa.

### Verificacion

- `dotnet build "Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" -c Release`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Date picker propio

### Que cambio

- Se crea `frontend/src/components/common/DatePickerField.tsx`.
- Se reemplazan los `input type="date"` en:
  - `components/extractos/AddRowForm.tsx`
  - `pages/AuditoriaPage.tsx`
  - `pages/CuentasPage.tsx`
  - `pages/ImportacionPage.tsx`
- `global.css` incorpora los estilos `.date-picker-*` y `.date-field`.
- El popover calcula si debe abrir hacia abajo o hacia arriba segun el espacio disponible.

### Por que

El calendario nativo del navegador no puede ajustarse al diseno Atlas de forma fiable. El intento anterior estilaba el campo cerrado, pero al abrir el selector volvia a aparecer una UI ajena al producto.

### Decisiones de diseno

- Mantener una superficie blanca, borde suave y sombra contenida, siguiendo `Documentacion/Diseno/DESIGN.md`.
- Usar `lucide-react` para iconos porque ya esta instalado en el proyecto.
- No meter una libreria de date picker: seria dependencia nueva para un componente pequeno y controlable.
- Incluir `Hoy` y `Limpiar` como acciones compactas para filtros y formularios.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Navegador in-app en `http://localhost:5173/cuentas`: se abre el modal de editar plazo fijo, el calendario se muestra con el sistema visual Atlas y no hay errores de consola.

## 2026-05-01 - V-01.05 - Hardening por checklist general de seguridad

### Que cambio

- `USUARIOS` incorpora `mfa_enabled`, `mfa_secret`, `mfa_enabled_at` y `mfa_last_accepted_step`.
- `AuthService` exige MFA TOTP cuando `Security:RequireMfaForWebUsers=true`.
- El login correcto con password crea un challenge temporal MFA y no emite JWT hasta validar el codigo.
- Si el usuario aun no tenia MFA, el challenge entrega una clave TOTP para enrolamiento y la guarda protegida al verificar el primer codigo.
- `TotpService` implementa RFC 6238 con HMAC-SHA1, periodo de 30 segundos, 6 digitos y tolerancia de un intervalo.
- `AuthController` agrega `POST /api/auth/mfa/verify`.
- `CsrfMiddleware` excluye el verify MFA porque ocurre antes de tener sesion/cookie autenticada.
- `UsuariosController` rota `security_stamp` y revoca refresh tokens al cambiar permisos, permiso de cuenta, email, perfil o restaurar usuario.
- `ActualizacionService` verifica el `digest` SHA-256 del asset descargado desde GitHub Release antes de extraerlo.
- CI agrega escaneo de secretos de alta confianza sobre archivos versionados.
- `LoginPage` soporta el segundo paso MFA y el setup inicial.
- `wwwroot` se sincroniza con el build frontend nuevo.

### Por que

El checklist general marcaba puntos P0 que si aplican a Atlas Balance: MFA, sesiones regeneradas ante cambio de permisos, verificacion de updates, secret scanning e incident response. Lo demas que habla de movil, IA, RAG, pagos, cloud o Kubernetes no pertenece al producto actual.

### Reglas tecnicas

- No se emiten cookies `access_token`/`refresh_token` hasta completar MFA.
- Los challenges MFA viven en memoria 5 minutos y aceptan maximo 5 fallos.
- `mfa_last_accepted_step` evita reutilizar el mismo codigo TOTP.
- Los secretos MFA nunca deben aparecer en logs ni documentacion.
- El digest de GitHub no sustituye la firma de codigo, pero bloquea ZIPs manipulados entre la API de releases y el extractor local.
- Todo cambio de permisos o identidad revoca sesiones del usuario afectado aunque el backend ya lea permisos desde BD; el frontend no debe seguir con permisos cacheados viejos.

### Verificacion

- `dotnet build ".\Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" -c Release --no-restore`: OK.
- Tests focalizados auth/usuarios/update/CSRF/sesion: 24/24 OK.
- Tests backend sin Testcontainers: 115/115 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- NuGet vulnerable audit: sin hallazgos.

## 2026-04-25 - V-01.05 - Correccion de hallazgos de auditoria

### Que cambio

- El frontend deja de depender de Tailwind/shadcn: se eliminan dependencias, plugin Vite, imports CSS, `components.json`, `components/ui/button.tsx` y `lib/utils.ts`.
- `global.css` queda como entrada de tokens/estilos propios, sin `@theme`, `@apply`, imports Tailwind ni compatibilidad shadcn.
- `backend/src/AtlasBalance.API/wwwroot` se sincroniza desde `frontend/dist` para que la API sirva los bundles corregidos.
- Se reemplazan fondos decorativos por superficies planas con tokens propios en `global.css`, `auth.css` y estilos de layout.
- `CuentaResumenResponse` se amplia con `CuentaNombre`, `Divisa`, `TitularId`, `TitularNombre`, `EsEfectivo`, `TipoCuenta`, `PlazoFijo`, `Notas` y `UltimaActualizacion`.
- `CuentasController.Resumen` resuelve el resumen mensual y adjunta metadatos de plazo fijo cuando corresponde.
- `DatePickerField` gana semantica de grid, etiquetas de fecha completas y navegacion con flechas/Home/End.
- `ConfirmDialog` implementa focus trap basico con Tab/Shift+Tab.
- `AppSelect` abre y cierra con Enter/Espacio ademas de raton/flechas.

### Por que

La auditoria encontro deuda real, no cosmetica: un segundo sistema de estilos contradiciendo la arquitectura, un endpoint de resumen con contrato inferior al endpoint usado por la UI y controles custom que no cerraban el contrato minimo de teclado.

### Reglas tecnicas

- No se acepta Tailwind/shadcn como dependencia implicita del producto. Si algun dia se quiere usar, debe cambiar primero la documentacion canonica.
- Los resumentes de cuenta no deben divergir en campos criticos: tipo de cuenta, titular y plazo fijo son parte del contrato de lectura.
- Todo control propio que sustituya a un nativo debe cubrir teclado basico antes de release.
- Los fondos de app deben priorizar tokens, bordes, spacing y tipografia sobre degradados decorativos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release --filter CuentasControllerTests`: 4/4 OK.
- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list ".\\Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-05-11 - V-01.06 - OpenRouter ajustado a allowlist de modelos gratis

### Que cambio

- `AiConfiguration.OpenRouterModels` queda limitado a `openrouter/auto` y modelos gratis permitidos.
- Esta entrada fue ampliada el mismo dia: la allowlist actual contiene seis modelos gratis, pero `Auto` usa `models` con maximo 3 candidatos por request. Ver la seccion superior `OpenRouter Auto limitado a 3 modelos en models`.
- Las llamadas a modelos gratis se pinchan al proveedor exacto con `provider.only` y `allow_fallbacks=false`:
  - `openai/gpt-oss-120b:free` -> `open-inference/int8`.
  - `minimax/minimax-m2.5:free` -> `open-inference/int8`.
  - `google/gemma-4-31b-it:free` -> `google-ai-studio`.
- Para los modelos gratis no se envia `provider.zdr=true`, porque la API publica `/api/v1/endpoints/zdr` de OpenRouter no lista esos endpoints gratis como ZDR. Forzarlo era la causa practica del 404 por `guardrail restrictions and data policy`.
- La auditoria IA incluye `runtime_model`; para estos modelos registra `zero_data_retention=false`.
- El mensaje de 404 por politica/guardrail explica que Atlas ya esta enviando los modelos de la allowlist y que, si persiste, hay que revisar `OpenRouter > Settings > Privacy` o anadir un modelo ZDR permitido.

### Por que

La cuenta de OpenRouter del usuario restringe modelos. Usar un default externo a esa allowlist era una mala idea: aunque el modelo exista, OpenRouter lo descarta por guardrails de cuenta. La solucion final actual es obedecer los slugs exactos permitidos y usar `models` con un fallback de maximo 3 candidatos.

### Verificacion

- API publica de OpenRouter revisada para slugs y endpoints.
- `dotnet build '.\Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj' -p:UseAppHost=false --no-restore`: OK.
- `AtlasAiServiceTests`: 29/29 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro queda bloqueado por `spawn EPERM` conocido.
- `wwwroot` sincronizado.
- API reiniciada y `/api/health`: `healthy`.

## 2026-05-10 - V-01.06 - Gobierno seguro de IA

### Contrato de seguridad

- Las claves de OpenRouter/OpenAI solo viven en `CONFIGURACION.openrouter_api_key` y `CONFIGURACION.openai_api_key`, protegidas con `ISecretProtector`; `GET /api/ia/config` devuelve solo flags `*_api_key_configurada`.
- El frontend nunca llama a OpenRouter/OpenAI. Todas las llamadas salen desde `AtlasAiService` mediante los `HttpClient` `openrouter` u `openai`.
- Cada proveedor tiene un cliente fallback (`openrouter-fallback` / `openai-fallback`). El comportamiento actual es salida directa por defecto; si se configura `Ia:UseSystemProxy=true` o `Ia:ProxyUrl`, el cliente principal usa proxy y el fallback queda directo. Si el primer envio lanza `HttpRequestException`, `AtlasAiService` reconstruye la request y reintenta por el fallback antes de devolver error de red.
- `POST /api/ia/chat` exige usuario autenticado y delega en `AtlasAiService`, que valida:
  - `ai_enabled=true`.
  - `USUARIOS.puede_usar_ia=true`.
  - proveedor/modelo permitido.
  - API key presente en backend.
  - limites de requests por usuario y globales.
  - presupuesto mensual/total si hay coste estimado configurado.
  - maximo aproximado de tokens de entrada y salida.
- Los permisos se validan en base de datos en cada request, no solo por claim ni por React.
- Los cambios de usuario siguen rotando `SecurityStamp` y revocando refresh tokens.
- OpenRouter queda restringido por allowlist backend. En la configuracion actual se usan modelos gratis permitidos por la cuenta del usuario; no se marcan como ZDR y quedan auditados con `zero_data_retention=false`.
- `openrouter/auto` se conserva como valor guardado, pero la llamada Auto se materializa como `models` con maximo 3 candidatos gratis permitidos.
- Las llamadas a OpenAI usan API key de servidor contra `https://api.openai.com/v1/chat/completions`.

### Configuracion

Nuevas claves en `CONFIGURACION`:

- `ai_enabled`
- `ai_provider`
- `ai_model`
- `openrouter_api_key`
- `openai_api_key`
- `ai_requests_per_minute`
- `ai_requests_per_hour`
- `ai_requests_per_day`
- `ai_global_requests_per_day`
- `ai_monthly_budget_eur`
- `ai_total_budget_eur`
- `ai_budget_warning_percent`
- `ai_input_cost_per_1m_tokens_eur`
- `ai_output_cost_per_1m_tokens_eur`
- `ai_max_input_tokens`
- `ai_max_output_tokens`
- `ai_max_context_rows`
- `ai_usage_month_key`
- `ai_usage_month_cost_eur`
- `ai_usage_total_cost_eur`
- `ai_usage_total_requests`
- `ai_usage_last_user_id`
- `ai_usage_last_at_utc`

La migracion `20260510123000_HardenAiGovernance` agrega `USUARIOS.puede_usar_ia`, indice `ix_usuarios_puede_usar_ia` e inserta defaults de configuracion si faltan.

Los presupuestos mensual/total se comparan contra `ai_usage_month_cost_eur` y `ai_usage_total_cost_eur`. No se recalculan desde `AUDITORIAS`, porque `LimpiezaAuditoriaJob` borra auditorias antiguas a los 28 dias y eso habria permitido perder gasto historico.

### Auditoria IA

Nuevas acciones:

- `IA_CONSULTA`: uso correcto. Guarda usuario, proveedor, modelo, cliente HTTP usado, si hubo fallback, movimientos analizados, longitud de pregunta, longitud de contexto, tokens aproximados y coste estimado.
- `IA_CONSULTA_BLOQUEADA`: bloqueo por permiso, IA global, limites, presupuesto, tokens o configuracion.
- `IA_CONSULTA_ERROR`: fallo de proveedor, red, timeout o respuesta malformada. En errores de transporte guarda cliente principal/fallback y mensajes tecnicos recortados; nunca prompt, respuesta completa ni API key.
- `IA_PRESUPUESTO_AVISO`: aviso al superar el porcentaje configurado.

Regla: no guardar prompts completos, respuestas completas, claves, extractos completos ni payloads del proveedor.

### Privacidad y prompt injection

El contexto IA incluye agregados, saldos y movimientos relevantes limitados por `ai_max_context_rows`. Los conceptos bancarios se truncan, se serializan como datos y el prompt de sistema declara que conceptos, nombres de cuentas, extractos importados y pregunta del usuario son datos no confiables. Las instrucciones dentro de datos bancarios no deben obedecerse.

### Verificacion

- API build: OK.
- Frontend lint/build: OK.
- Tests unitarios nuevos para IA desactivada y usuario sin permiso quedan en `AtlasAiServiceTests`.
- La suite backend no pudo ejecutarse por fallo MSBuild preexistente del proyecto de tests: devuelve codigo 1 con `0 Errores` o sin salida util.

### Presupuesto IA por usuario y proveedor

Desde V-01.06 la gobernanza de IA combina dos barreras de coste:

- Global: `ai_usage_month_cost_eur`, `ai_usage_total_cost_eur`, `ai_monthly_budget_eur`, `ai_total_budget_eur`.
- Por usuario: tabla `IA_USO_USUARIOS` con `usuario_id`, `month_key`, `requests`, `input_tokens`, `output_tokens` y `coste_estimado_eur`.

`AtlasAiService.EnsureBudgetAsync` evalua primero el presupuesto global mensual, despues el presupuesto mensual por usuario (`ai_user_monthly_budget_eur`) y finalmente el presupuesto total. Si se supera el limite individual, registra `IA_CONSULTA_BLOQUEADA` con motivo `user_monthly_budget_exceeded` y no llama al proveedor.

El contexto financiero se construye desde consultas SQL scopeadas por usuario:

- rango maximo defensivo de `AiConfigurationDefaults.MaxContextYears`,
- saldos actuales por cuenta por `fila_numero`,
- agregados mensuales/periodo/categoria en SQL,
- movimientos relevantes limitados por `ai_max_context_rows`,
- truncado final por `AiConfigurationDefaults.MaxContextCharacters`.

El proveedor queda cubierto por tests de error controlado: 401/API key invalida, 404/modelo no encontrado, timeout/red y JSON/campos malformados. La auditoria no guarda prompts, respuestas completas ni payloads del proveedor.

### Proveedor OpenAI

Desde V-01.06 `AiConfiguration` permite dos proveedores:

- `OPENROUTER`: modelos permitidos `openrouter/auto`, `nvidia/nemotron-3-super-120b-a12b:free`, `google/gemma-4-31b-it:free`, `minimax/minimax-m2.5:free`, `openai/gpt-oss-120b:free`, `z-ai/glm-4.5-air:free`, `qwen/qwen3-coder:free`.
- `OPENAI`: modelos permitidos `gpt-4.1-mini`, `gpt-4o-mini`, `gpt-4o`.

`ConfiguracionController` guarda la API key correspondiente sin devolverla al cliente y redacta claves en auditoria. Si llega un modelo vacio o no permitido para un proveedor soportado, normaliza a default seguro (`openrouter/auto` en OpenRouter, `gpt-4o-mini` en OpenAI) para permitir guardar la API key sin depender de valores antiguos del formulario. En runtime, `AtlasAiService` auto-repara slugs obsoletos conocidos hacia `openrouter/auto`; los modelos desconocidos siguen bloqueados.

La migracion `20260510180000_AddOpenAiProviderConfig` inserta `openai_api_key` si falta. El seeding tambien la crea en instalaciones nuevas.

## 2026-05-10 - V-01.06 - Desglose de cuenta: seleccion, insercion y flag

### Que cambio

- `CuentaDetailPage.tsx` reordena la tabla del desglose para que la seleccion sea la primera columna visible.
- La columna `Flag` desaparece del render de la tabla. El marcado se ejecuta con `flagSelectedRows`, que recorre solo las filas seleccionadas y llama a `PATCH /api/extractos/{id}/flag`.
- El check de revision usa actualizacion local optimista y ya no llama a `loadCuentaData()` despues de cada click.
- La insercion intermedia usa el endpoint existente `POST /api/extractos` con `insert_before_fila_numero`, pero actualiza `rows` localmente con la fila devuelta y desplaza `fila_numero` en memoria.
- La eliminacion por fila se retira del cuerpo de la tabla. El borrado queda en la accion superior de papelera sobre seleccion, manteniendo el `ConfirmDialog` ya existente para borrado multiple.
- `dashboard.css` anade el trigger flotante `account-row-insert-trigger`, icon buttons para flag/papelera y una columna de seleccion compacta.
- `AGENTS.md` y `CLAUDE.md` incorporan una regla para cortar validaciones visuales o servidores dev que se encallen y continuar con validaciones utiles.
- Ajuste posterior: el trigger `account-row-insert-trigger` se mueve fuera de `account-selection-cell` y se renderiza en `account-row-anchor-cell`, desplazado al borde derecho de la columna `Nº Fila`. El objetivo es que el `+` no tape el checkbox de seleccion ni reduzca su zona clicable.

### Por que

La tabla mezclaba tres patrones distintos: check operativo, flag por columna y seleccion de borrado al final. Eso obligaba a recorrer visualmente demasiadas columnas y, peor, cada check/flag recargaba datos. Para una tabla financiera densa, ese patron es torpe: la seleccion debe estar al inicio y las acciones masivas fuera del grid.

### Recarga y scroll

Los checkboxes de seleccion solo modifican `selectedRowIds`, sin formulario ni navegacion. El check de revision y el flag aplican cambios al estado local (`setRows`) y hacen la llamada API sin disparar `loadCuentaData()`. Al no remedir ni remontar toda la pantalla, se conserva el scroll actual del usuario.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `npm.cmd run build` dentro del sandbox: bloqueado por `spawn EPERM` de Vite, incidencia conocida.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot` mediante copia no destructiva fuera del sandbox por permisos locales.
- Ajuste del `+` validado con `npm.cmd run lint` OK y build OK fuera del sandbox; `wwwroot` resincronizado.
- Se limpio el servidor temporal `127.0.0.1:5176` que quedo vivo durante la validacion abortada.

## 2026-04-26 - V-01.05 - Fix de altura del AlertBanner en el shell

### Que cambio

- `frontend/src/styles/layout/shell.css` ajusta la grilla de `app-main` para soportar tres filas estables: topbar, banner y contenido.
- Se define placement explicito para evitar auto-placement ambiguo cuando el banner existe:
  - `.app-main > .app-topbar { grid-row: 1; }`
  - `.app-main > .alert-banner { grid-row: 2; align-self: start; min-height: 0; height: auto; }`
  - `.app-main > .app-content { grid-row: 3; min-height: 0; }`
- Se replica la misma estructura en el breakpoint mobile (`max-width: 768px`).
- Se agrega `align-self: start` en `.alert-banner` para evitar estirado vertical residual en dashboards.
- Barrido de codigo frontend confirma que `AlertBanner` solo se monta en `components/layout/Layout.tsx`, por lo que el fix aplica a todas las rutas no embebidas.

### Por que

Con `grid-template-rows: var(--topbar-height) 1fr`, al aparecer el banner la fila flexible `1fr` se la quedaba el propio banner y quedaba sobredimensionado. El contenido pasaba a una fila implicita posterior, rompiendo proporciones en Configuracion/Backups/Papelera. En dashboards, ademas, se apreciaba estirado residual por comportamiento por defecto de grid (`align-self: stretch`), corregido con `align-self: start`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por copia con cambios.

## 2026-04-26 - V-01.05 - Importacion permite avisos con saldo presente

### Que cambio

- `ImportacionService.ValidateRows` amplia la regla de filas informativas: si una fila tiene concepto, fecha vacia e importe vacio, pasa a ser importable con advertencias aunque traiga saldo.
- El importe se normaliza a `0`.
- La fecha se hereda de la ultima fila valida anterior.
- El saldo se conserva si viene parseable; solo se hereda el saldo anterior cuando tambien esta vacio.
- Se agregan regresiones de validacion y confirmacion para filas tipo `concepto + saldo` sin fecha ni importe.

### Por que

Algunos bancos exportan lineas informativas de beneficiario/desglose con concepto y saldo, pero sin fecha ni importe. Tratarlas como error fatal bloqueaba importaciones correctas. La app debe avisar y dejar continuar, no ponerse exquisita con basura bancaria previsible.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ImportacionServiceTests`: 26/26 OK.
- `dotnet build "Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" -c Release`: OK, 0 warnings.

## 2026-04-26 - V-01.05 - Vista tabular de extractos tipo hoja de calculo

### Que cambio

- `ExtractoTable.tsx` agrupa cabecera y filas dentro de `extracto-table-viewport`, de forma que el scroll horizontal es comun.
- La tabla declara semantica `role="grid"`, con conteo de filas/columnas y encabezados de columna.
- La estimacion del virtualizador cambia segun densidad: `42px` en modo comodo y `34px` en modo compacto.
- Se agrega `getColumnLabel` para mostrar nombres legibles sin cambiar los campos reales usados por sort, filtros o guardado.
- `extractos.css` define variables locales de hoja (`--sheet-grid`, `--sheet-head-bg`, `--sheet-row-height`, etc.) y refuerza bordes, foco, hover, cabecera sticky y primera columna sticky.
- Se sincroniza `backend/src/AtlasBalance.API/wwwroot` desde `frontend/dist`.

### Por que

La vista anterior era una tabla editable, pero no una hoja de calculo convincente: cabecera y cuerpo tenian scroll separado, las celdas tenian poco borde y el foco no parecia una seleccion de celda. Para extractos bancarios densos, esa blandura visual estorba. La lectura debe ser de matriz, no de lista bonita.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.
