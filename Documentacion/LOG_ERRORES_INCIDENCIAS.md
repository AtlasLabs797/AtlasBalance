# Log de errores e incidencias

## 2026-04-25 - V-01.04 - Hallazgos de auditoria corregidos antes de release

- Contexto: la auditoria de uso, bugs y seguridad detecto tres problemas que no eran aceptables para cerrar version: Tailwind/shadcn reintroducidos contra el stack canonico, contrato duplicado de resumen de cuenta sin metadatos de plazo fijo y controles propios con soporte de teclado incompleto.
- Causa: se mezclo una capa UI externa con el sistema de CSS variables propio, el endpoint historico de cuentas quedo por detras del resumen rico usado por el dashboard, y los controles custom no cerraron todo el contrato de accesibilidad al reemplazar controles nativos.
- Solucion aplicada: se eliminaron dependencias/configuracion/imports Tailwind/shadcn y `components.json`; `CuentasController.Resumen` ahora devuelve titular, tipo de cuenta, notas, ultima actualizacion y `plazo_fijo`; `DatePickerField`, `ConfirmDialog` y `AppSelect` mejoran etiquetas, navegacion de teclado y focus trap.
- Verificacion: busqueda sin restos directos de Tailwind/shadcn, `npm.cmd run lint` OK, `npm.cmd run build` OK, `wwwroot` sincronizado, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, NuGet vulnerable sin hallazgos y `dotnet test ...GestionCaja.API.Tests.csproj -c Release` 108/108 OK.

## 2026-04-25 - V-01.04 - Gradientes decorativos marcados como deuda visual

- Contexto: la auditoria marco fondos con `radial-gradient` y degradados suaves en login, layout y tarjetas como residuos de UI generica.
- Causa: la capa de coherencia visual habia introducido decoracion de fondo que no aporta informacion y contradice el criterio de superficies sobrias del proyecto.
- Solucion aplicada: se sustituyeron esos fondos por tokens planos (`var(--bg-app)`, `var(--bg-surface-soft)`, `var(--bg-surface)` y mezclas solidas). Se dejaron intactos los degradados funcionales de flecha de `select` y shimmer de skeleton.
- Verificacion: busqueda posterior solo encontro degradados funcionales, `npm.cmd run lint` OK y `npm.cmd run build` OK.

## 2026-04-25 - V-01.04 - Endpoints nuevos respondian 500 ante body o listas null

- Contexto: en una pasada extra de auditoria sobre los endpoints añadidos en V-01.04 (`POST /api/alertas`, `PUT /api/alertas/{id}`, `POST /api/cuentas/{id}/plazo-fijo/renovar` y `POST /api/importacion/plazo-fijo/movimiento`), se detecto que ninguno comprobaba que el cuerpo deserializado no fuera null y que `SaveAlertaSaldoRequest.DestinatarioUsuarioIds` se accedia directamente con `.Count` aunque deserializar `"destinatario_usuario_ids": null` deja la propiedad en null.
- Causa: los DTOs nuevos solo definian valor por defecto `= []`, pero el inicializador no se aplica cuando el JSON envia explicitamente `null`. Ningun controlador validaba previamente el cuerpo.
- Solucion aplicada: `if (request is null) return BadRequest(new { error = "Request invalido" });` al inicio de los endpoints afectados y `request.DestinatarioUsuarioIds ?? []` antes de validar/procesar destinatarios.
- Verificacion: `dotnet build -c Release` OK, `dotnet test --no-build` 107/107 OK, `dotnet list package --vulnerable --include-transitive` sin hallazgos, `npm audit` 0 vulnerabilidades.

## 2026-04-25 - V-01.04 - Manifiesto frontend mantenia minimos vulnerables pese a lockfile seguro

- Contexto: durante la auditoria de seguridad V-01.04, `npm ls` confirmo que el lockfile resolvia `axios@1.15.0` y `react-router-dom@6.30.3`, pero `package.json` seguia declarando `axios ^1.7.9` y `react-router-dom ^6.28.0`.
- Causa: actualizaciones previas habian dejado el lockfile en versiones seguras, pero no elevaron los rangos minimos declarados en el manifiesto.
- Solucion aplicada: se actualizo el manifiesto a `axios ^1.15.2` y `react-router-dom ^6.30.3`; el lockfile queda regenerado con `axios@1.15.2`.
- Verificacion: `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, `npm.cmd run lint` OK, `npm.cmd run build` OK, `dotnet test ... --no-build` 107/107 OK, NuGet vulnerable sin hallazgos y `wwwroot` sincronizado.

## 2026-04-25 - V-01.04 - Popup nativo de fecha no podia igualarse al diseno Atlas

- Contexto: aunque el campo cerrado de fecha ya tenia mejor estilo, al abrir el calendario seguia apareciendo el selector nativo del navegador, fuera del sistema visual de Atlas.
- Causa: el popup interno de `input type="date"` no es estilizables de forma consistente entre navegadores/OS; CSS solo alcanza el campo cerrado y parte del indicador WebKit.
- Solucion aplicada: se reemplazaron los `input type="date"` del frontend por `DatePickerField`, un selector propio con popover, dias, mes, navegacion, estado seleccionado/hoy, acciones `Hoy`/`Limpiar` y posicionamiento hacia arriba cuando no cabe debajo.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK y comprobacion visual en navegador de `/cuentas` sin errores de consola.

## 2026-04-25 - V-01.04 - Dashboard de cuenta no mostraba vencimiento de plazo fijo

- Contexto: en el detalle de una cuenta `PLAZO_FIJO`, el usuario veia saldo, periodo, notas y desglose, pero no la fecha en la que vence el plazo fijo.
- Causa: el endpoint `/api/extractos/cuentas/{id}/resumen` no devolvia `tipo_cuenta` ni el bloque `plazo_fijo`; la UI de detalle no tenia dato que pintar.
- Solucion aplicada: el resumen de cuenta devuelve `TipoCuenta` y `PlazoFijoResponse` para cuentas de plazo fijo; `CuentaDetailPage` muestra vencimiento, dias restantes/vencido y estado bajo el titulo de la cuenta.
- Verificacion: backend Release build OK, `npm.cmd run lint` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.

## 2026-04-25 - V-01.04 - Date picker de plazo fijo no seguia el sistema visual

- Contexto: en el formulario de cuentas de tipo `PLAZO_FIJO`, los campos de fecha de inicio/vencimiento usaban `input type="date"` nativo y el selector de calendario no se veia como el resto de campos.
- Causa: los estilos globales cubrian inputs/selects, pero no ajustaban `color-scheme`, partes internas WebKit ni el indicador `::-webkit-calendar-picker-indicator` de los controles de fecha.
- Solucion aplicada: se agregaron reglas globales para `input[type='date']`, `::-webkit-datetime-edit`, `::-webkit-calendar-picker-indicator` y modo oscuro, manteniendo el popup nativo del navegador.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.

## 2026-04-25 - V-01.04 - Tests backend bloqueados por API Debug en ejecucion

- Contexto: al ejecutar `dotnet test` tras modificar importacion/dashboard, MSBuild no pudo copiar `GestionCaja.API.exe` ni `GestionCaja.API.dll` en `bin\Debug\net8.0`.
- Causa: habia un proceso local `GestionCaja.API` ejecutandose desde `backend/src/GestionCaja.API/bin/Debug/net8.0`, bloqueando los artefactos.
- Solucion aplicada: se identifico el PID con `Get-Process`, se detuvo el proceso local y se repitieron los tests.
- Verificacion: `dotnet test ... --filter "ImportacionServiceTests|DashboardServiceTests"` paso 28/28 y `dotnet build ... -c Release` paso sin warnings.

## 2026-04-25 - V-01.04 - Implementacion plazo fijo detecto rotura TypeScript y lint estricto

- Contexto: al compilar frontend tras agregar campos de plazo fijo, `tsc` fallo en `CuentasPage.tsx` por un cierre JSX sobrante. Despues, `npm.cmd run lint` fallo por `react-refresh/only-export-components` en `components/ui/button.tsx` porque el proyecto usa `--max-warnings 0`.
- Causa: el bloque condicional de plazo fijo dejo un `)}` duplicado; el warning de lint era una regla estricta sobre un componente UI que exporta tambien `buttonVariants`.
- Solucion aplicada: se elimino el cierre sobrante y se agrego una excepcion local de ESLint en `button.tsx` para mantener el contrato del componente sin mover archivos ahora.
- Verificacion: `npm.cmd run lint` OK y `npm.cmd run build` OK.

## 2026-04-25 - V-01.04 - Actualizador post-instalacion incompleto

- Contexto: una vez instalada la aplicacion, el flujo de actualizacion manual desde paquete no dejaba la instalacion preparada para futuras actualizaciones y no validaba salud real de la API tras reemplazar binarios.
- Causa: `update.cmd`/`update.ps1` seguian el patron inicial de wrapper minimo; `Actualizar-AtlasBalance.ps1` actualizaba API/Watchdog, pero no refrescaba scripts instalados ni `atlas-balance.runtime.json`, y no hacia health check con `curl.exe -k`.
- Solucion aplicada: `update.ps1` valida paquete antes de autoelevar y soporta `-PackagePath`; el actualizador copia scripts/wrappers operativos a la instalacion, actualiza `VERSION`/runtime, conserva configuracion, mantiene backup/rollback y falla si `/api/health` no responde tras arrancar.
- Mitigacion operativa: para actualizar desde un paquete nuevo, ejecutar `.\update.cmd -InstallPath C:\AtlasBalance` en la carpeta descomprimida; en instalaciones ya actualizadas se puede usar `C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-XX-win-x64 -InstallPath C:\AtlasBalance`.

## 2026-04-25 - V-01.04 - Incidencias de instalacion Windows Server 2019 cerradas en scripts

- Contexto: la instalacion real en Windows Server 2019 detecto confusion entre repo fuente y paquete release, wrappers fragiles, dependencia poco fiable de `winget`, falsos negativos de `Invoke-WebRequest`, credenciales iniciales falsas al reinstalar sobre BD existente y necesidad de reset admin soportado.
- Causa: el flujo operativo mezclaba documentacion de desarrollo con instalacion de servidor; el instalador asumia demasiadas cosas felices: carpeta correcta, PostgreSQL automatico, BD nueva y health check PowerShell fiable.
- Solucion aplicada: `install.ps1` e `Instalar-AtlasBalance.ps1` validan paquete release antes de instalar; `install.cmd`/`Instalar Atlas Balance.cmd` devuelven codigo de salida; el instalador detecta usuarios existentes y no genera password admin falsa; se agrega `Reset-AdminPassword.ps1`; `Build-Release.ps1` incluye scripts operativos nuevos; el health check usa `curl.exe -k` como prueba primaria.
- Mitigacion operativa: si la BD ya existe y no se conoce el admin, ejecutar `scripts\Reset-AdminPassword.ps1` desde la instalacion; si `curl.exe -k` responde pero el navegador no, instalar `atlas-balance.cer` como raiz confiable en el cliente.

## 2026-04-25 - V-01.04 - Reinstalacion falla por password HTTPS desalineada

- Contexto: en Windows Server 2019, tras reinstalar `V-01.03`, `AtlasBalance.API` quedaba detenido y el visor de eventos mostraba `System.Security.Cryptography.CryptographicException: La contraseña de red especificada no es válida` al cargar `atlas-balance.pfx`.
- Causa: `Instalar-AtlasBalance.ps1` reutilizaba `C:\AtlasBalance\certs\atlas-balance.pfx` si ya existia, pero generaba una password HTTPS nueva y la escribia en `appsettings.Production.json`. Eso dejaba certificado viejo con password nueva.
- Solucion aplicada: el instalador `V-01.04` elimina `atlas-balance.pfx` y `atlas-balance.cer` existentes antes de generar el certificado nuevo, garantizando que la password configurada y el PFX coincidan.
- Mitigacion operativa para instalaciones afectadas: detener `AtlasBalance.API`, borrar `C:\AtlasBalance\certs\atlas-balance.pfx` y `C:\AtlasBalance\certs\atlas-balance.cer`, y relanzar `scripts\Instalar-AtlasBalance.ps1` directamente desde el paquete.

## 2026-04-25 - V-01.04 - Modal de importacion rechazado por cabeceras anti-frame

- Contexto: en produccion, desde el dashboard de cuenta, el modal `Importar movimientos` mostraba un panel gris con icono de documento roto/rechazo de conexion.
- Causa: el frontend cargaba `/importacion` dentro de un `iframe`, pero la API aplicaba `X-Frame-Options: DENY` y `Content-Security-Policy: frame-ancestors 'none'` a todas las rutas, bloqueando incluso iframes same-origin.
- Solucion aplicada: las cabeceras pasan a `X-Frame-Options: SAMEORIGIN` y `frame-ancestors 'self'`, permitiendo solo embebidos del mismo origen y manteniendo bloqueado el clickjacking externo.
- Mitigacion operativa para `V-01.03` ya instalado: parchear el bundle servido para que el boton de importacion navegue a `/importacion` en pagina completa o publicar un paquete nuevo con la correccion.

## 2026-04-25 - V-01.03 - Auditoria profunda de seguridad y hardening aplicado

### Sesiones no revocadas tras reset/cambio de password

- Contexto: el reset admin y algunos cambios de estado cambiaban credenciales o usuario sin invalidar sesiones ya emitidas.
- Causa: JWT sin estado de sesion y refresh tokens activos aunque el password cambiara.
- Solucion aplicada: `SecurityStamp` en usuario, claim en access token, validacion en `UserStateMiddleware`, migracion `UserSessionHardening`, rotacion de stamp y revocacion de refresh tokens en cambio/reset/delete y reuse.

### Login y bearer de integracion con rate limit incompleto

- Contexto: login exponia diferencias utiles para enumeracion/bloqueo y la integracion OpenClaw consultaba BD antes de limitar bearer invalido repetido.
- Causa: bloqueo de cuenta demasiado distinguible y rate limit aplicado tarde.
- Solucion aplicada: respuesta generica para bloqueos, throttle por cliente/email antes de insistir, umbral de bloqueo global mas alto, y rate limit por IP/minuto para bearer invalido antes de consultar tokens.

### URL de actualizaciones y rutas configurables demasiado permisivas

- Contexto: `app_update_check_url`, `backup_path`, `export_path`, rutas de descarga/exportacion y rutas Watchdog pasaban por normalizacion que podia ocultar entradas relativas o destinos no oficiales.
- Causa: confianza excesiva en configuracion admin y `Path.GetFullPath` usado antes de validar si la ruta era explicitamente absoluta.
- Solucion aplicada: allowlist HTTPS estricta a `github.com/AtlasLabs797/AtlasBalance` o `api.github.com/repos/AtlasLabs797/AtlasBalance/...`; validacion de rutas crudas antes de normalizar; bloqueo de traversal y rutas relativas.

### Dependencia frontend vulnerable

- Contexto: `npm audit` marco `postcss <8.5.10` como vulnerabilidad moderada de XSS en serializacion CSS.
- Causa: dependencia transitiva resuelta a `postcss@8.5.9`.
- Solucion aplicada: `npm.cmd update postcss`, lockfile resuelto a `postcss@8.5.10`.

### Credenciales iniciales one-shot persistentes

- Contexto: `INSTALL_CREDENTIALS_ONCE.txt` quedaba con ACL restringida, pero podia sobrevivir si nadie lo borraba.
- Causa: flujo operativo manual.
- Solucion aplicada: el instalador registra una tarea programada SYSTEM para borrar el archivo automaticamente a las 24 horas.

## 2026-04-20 - V-01.02 - Release sin scripts one-click completos

- Contexto: la carpeta de release solo conservaba `.gitkeep` hasta generar paquete, y el empaquetado no incluia scripts `install/update/uninstall/start` con esos nombres.
- Causa: existian wrappers historicos en espanol y scripts parciales, pero faltaba el contrato operativo pedido para release autonoma.
- Solucion aplicada: creados wrappers `install.cmd`, `update.cmd`, `uninstall.cmd`, `start.cmd` y sus scripts PowerShell; `Build-Release.ps1` los copia al paquete.

## 2026-04-20 - V-01.02 - Arranque no levantaba PostgreSQL gestionado

- Contexto: `Launch-AtlasBalance.ps1` arrancaba Watchdog y API, pero no la base de datos.
- Causa: el script asumio que PostgreSQL ya estaba activo como dependencia externa.
- Solucion aplicada: el runtime registra `ManagedPostgres` y `PostgresServiceName`; `start` y `update` arrancan PostgreSQL gestionado antes de tocar backend/API.

## 2026-04-20 - V-01.02 - `setup-https.ps1` no parseaba en PowerShell

- Contexto: la validacion sintactica de scripts detecto error de cadena sin terminar en `scripts/setup-https.ps1`.
- Causa: archivo con texto mojibake/codificacion rota.
- Solucion aplicada: reescritura ASCII del script, manteniendo su funcion de desarrollo con `mkcert` y mensajes claros.

## 2026-04-20 - V-01.02 - Auditoria tecnica profunda

### Secretos de configuracion persistidos en claro

- Contexto: `smtp_password` y `exchange_rate_api_key` se guardaban como texto plano en la tabla `CONFIGURACION`.
- Causa: la pantalla de configuracion persistia valores sensibles igual que parametros normales.
- Solucion aplicada: `ISecretProtector` con Data Protection, prefijo `enc:v1:`, migracion automatica de valores legacy en arranque y lectura descifrada solo en SMTP/tipos de cambio.

### Permiso global de dashboard ampliaba indebidamente el alcance de datos

- Contexto: un permiso global con `PuedeVerDashboard` podia activar `HasGlobalAccess` y abrir consultas de cuentas/titulares/exportaciones.
- Causa: `UserAccessService` mezclaba permiso de visualizacion de dashboard con permiso global de datos.
- Solucion aplicada: `HasGlobalAccess` solo se concede por permisos globales de datos (`agregar`, `editar`, `eliminar`, `importar`). Se anadio test de regresion.

### Descarga de exportaciones confiaba en ruta guardada en BD

- Contexto: `ExportacionesController.Descargar` abria la ruta persistida si el usuario tenia acceso a la cuenta.
- Causa: faltaba comprobar raiz permitida y extension.
- Solucion aplicada: se bloquea cualquier descarga fuera de `export_path` y cualquier fichero que no sea `.xlsx`.

### Watchdog podia quedar expuesto por configuracion de URLs

- Contexto: Watchdog anadia `http://localhost:5001` con `app.Urls`, pero Kestrel podia recibir overrides externos.
- Causa: binding menos estricto del necesario para un servicio administrativo.
- Solucion aplicada: `ConfigureKestrel` fuerza `ListenLocalhost(5001)`.

### `AllowedHosts` permisivo en produccion

- Contexto: configuracion base/plantilla permitia `AllowedHosts="*"`.
- Causa: default comodo heredado de desarrollo.
- Solucion aplicada: fuera de Development la API rechaza `AllowedHosts` vacio, placeholder o wildcard; instalador escribe `$ServerName;localhost`.

### Artefactos locales con cookies/cabeceras/payloads de login

- Contexto: quedaban ficheros auxiliares de smoke/login y logs temporales fuera de Git.
- Causa: ejecuciones manuales dejaron outputs con informacion sensible o de sesion.
- Solucion aplicada: eliminados logs API temporales y artefactos de login/cookies/cabeceras en `Otros/Auxiliares/artifacts`.

## 2026-04-20 - V-01.02

### Typos activos en email, rutas y evento interno

- Contexto: la revision V-01.02 marcaba `atlasbalnace` y `atlas-blance`; el codigo principal ya estaba corregido, pero quedaban restos activos en `appsettings`, plantillas, scripts, placeholders, tests y evento de importacion.
- Causa: se corrigieron algunos literals del backend, pero no se hizo barrido completo sobre archivos versionables ni bundle servido.
- Solucion aplicada: normalizacion a `atlasbalance`/`atlas-balance`, rutas `C:/AtlasBalance`, constante compartida `IMPORTACION_COMPLETADA_EVENT` y rebuild/copias de `wwwroot`.

### Version `V-01.01` residual en instalador y documentacion de paquete

- Contexto: `Instalar-AtlasBalance.ps1` seguia escribiendo `V-01.01` en runtime y `Documentacion/documentacion.md` describia el paquete `V-01.01`.
- Causa: el cambio de version runtime no alcanzo scripts de instalacion ni documentacion de usuario.
- Solucion aplicada: instalador, comandos y documentacion de paquete actualizados a `V-01.02`.

### Bundle frontend servido desactualizado

- Contexto: `frontend/src` ya tenia fixes para CSRF, refresh concurrente y contador de alertas, pero `backend/src/GestionCaja.API/wwwroot` conservaba bundles antiguos ignorados por Git.
- Causa: se compilo frontend sin sincronizar siempre el resultado con el `wwwroot` que sirve la API local.
- Solucion aplicada: `npm.cmd run build`, limpieza segura de `wwwroot` y copia de `frontend/dist`; barrido final sin restos de typos/version antigua.

### Secretos de desarrollo en configuracion versionable

- Contexto: la auditoria con `cyber-neo` y revision manual detecto credenciales/defaults de desarrollo en configuracion versionable.
- Causa: valores comodos para bootstrap quedaron en archivos base.
- Solucion aplicada: `appsettings.json`, Watchdog y `docker-compose.yml` ya no incluyen secretos reales; se añadieron plantillas y `.env.example`; `SeedAdmin:Password` debe configurarse antes del primer arranque.

### Textos mojibake en importacion y correo SMTP

- Contexto: errores como "Indice", "Fecha vacia" y el asunto SMTP aparecian con caracteres rotos.
- Causa: cadenas arrastradas con codificacion incorrecta.
- Solucion aplicada: se corrigieron las cadenas y los tests que esperaban texto roto.

### Version `V-01.01` residual

- Contexto: `SeedData` insertaba `app_version = V-01.01` y el check de actualizacion enviaba User-Agent viejo.
- Causa: valores literales no actualizados al pasar a `V-01.02`.
- Solucion aplicada: seed inicial usa `V-01.02`; User-Agent usa la version runtime resuelta desde assembly.

### `npm.ps1` bloqueado por ExecutionPolicy

- Contexto: `npm audit`, `npm run lint` y `npm run build` fallan si se invoca `npm` desde PowerShell.
- Causa: PowerShell bloquea `C:\Program Files\nodejs\npm.ps1`.
- Solucion aplicada: usar `npm.cmd` en este entorno.

### Tests con Testcontainers bloqueados por Docker no disponible

- Contexto: la suite completa falla en `ExtractosConcurrencyTests` si Docker no esta arrancado/configurado.
- Causa: `PostgresFixture` necesita Docker/Testcontainers.
- Solucion aplicada: para verificacion local sin Docker se ejecuto `dotnet test ... --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`. En auditoria posterior con Docker disponible, la suite completa quedo en 83/83 OK.

### Estado Git local no fiable

- Contexto: `git status --short` ya responde, pero lista practicamente todo el arbol como `untracked`.
- Causa probable: copia local/repo recreado sin historial o indice util para esta carpeta.
- Solucion aplicada: no se modifico `.git`; reparar el estado Git requiere decision explicita para recrear o relinkar correctamente la copia.

### Frontend aparentemente caido por API sin cadena de conexion

- Contexto: la app parecia "no funcionar", pero el frontend compilaba/renderizaba; al arrancar API se rompia en startup.
- Causa: `ConnectionStrings:DefaultConnection` vacia en [appsettings.json](C:/Proyectos/Atlas%20Balance%20Dev/Atlas%20Balance/backend/src/GestionCaja.API/appsettings.json:3), provocando `Host can't be null` al ejecutar migraciones en [Program.cs](C:/Proyectos/Atlas%20Balance%20Dev/Atlas%20Balance/backend/src/GestionCaja.API/Program.cs:152).
- Solucion aplicada: diagnostico confirmado ejecutando API con y sin `ConnectionStrings__DefaultConnection`; sin valor falla por host nulo, con valor pasa a autenticar contra PostgreSQL (fallo esperado si password no coincide). Verificar que el entorno de ejecucion tenga cadena de conexion valida antes de levantar backend.

### Password de PostgreSQL desalineada con el contenedor activo

- Contexto: tras configurar cadena de conexion local, la API seguia fallando con `28P01: password authentication failed for user "app_user"`.
- Causa: el contenedor `atlas_balance_db` ya existente estaba inicializado con una password distinta a la configuracion local nueva.
- Solucion aplicada: se sincronizo la configuracion local de desarrollo (`.env` y `appsettings.Development.json`, ambos fuera de Git) con las credenciales reales del contenedor activo y la API quedo operativa (`/api/health` HTTP 200).

## 2026-04-20 - V-01.01

### `rg.exe` bloqueado por acceso denegado

- Contexto: al listar archivos del proyecto, `rg --files` fallo con `Acceso denegado`.
- Causa probable: binario `rg.exe` no ejecutable desde este entorno.
- Solucion aplicada: usar PowerShell (`Get-ChildItem` y `Select-String`) para inspeccion y busqueda.

### Raiz inicial sin repositorio Git

- Contexto: `git status` en `C:\Proyectos\Atlas Balance` devolvio que no era repositorio.
- Causa: el repositorio real estaba anidado en `atlas-blance-scaffolding/atlas-blance`.
- Solucion aplicada: mover la app a `Atlas Balance` y dejar `.git` en la raiz para que tambien se versionen `Documentacion` y la configuracion de GitHub.

### `DOCUMENTACION_CAMBIOS.md` no era UTF-8 valido

- Contexto: `apply_patch` no pudo modificar el archivo por una secuencia UTF-8 invalida.
- Causa probable: mezcla historica de codificaciones.
- Solucion aplicada: tratar ese archivo con PowerShell usando lectura de codificacion del sistema y reescritura UTF-8 cuando hizo falta actualizarlo.

### Regex invalida al filtrar `git status`

- Contexto: un `Select-String -Pattern` con barras invertidas sin escapar fallo al buscar `bin/obj/dist` en el estado Git.
- Causa: PowerShell interpreto `\o` como secuencia regex invalida.
- Solucion aplicada: repetir la busqueda con `Select-String -SimpleMatch`.

### Carpeta `Skills` con duplicados por agente

- Contexto: el inventario de `Skills` mostro muchas copias de la misma skill en `.agents`, `.codex`, `.claude`, `.cursor`, `.gemini`, etc.
- Causa: varios paquetes instalan la misma skill para multiples agentes.
- Solucion aplicada: documentar rutas canonicas en `Documentacion/SKILLS_LOCALES.md` y ordenar a los agentes que no traten cada copia como una skill distinta.

### Whitespace en release y documento de paleta

- Contexto: `git diff --cached --check` detecto trailing whitespace en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.01-win-x64/api/wwwroot/index.html` y `Documentacion/Diseno/Diseño/Palesta Y tipografia.txt`.
- Causa probable: archivos generados o movidos con espacios finales heredados.
- Solucion aplicada: limpieza mecanica de espacios finales en ambos archivos, re-stage y repeticion de `git diff --cached --check` sin errores.

### Advertencia GitHub por ZIP grande

- Contexto: `git push -u origin V-01.01` subio correctamente, pero GitHub aviso que `AtlasBalance-V-01.01-win-x64.zip` pesa 97.49 MiB y supera el maximo recomendado de 50 MiB.
- Causa: se incluyo el paquete de release completo en Git porque la instruccion fue subir todo el proyecto salvo `Otros/` y `Skills/`.
- Solucion aplicada: se cambio la politica para mantener `Atlas Balance/Atlas Balance Release` fuera de Git salvo `.gitkeep` y publicar paquetes como assets de GitHub Releases.

### Release GitHub inmutable antes de subir asset

- Contexto: el primer intento de publicar `V-01.01` creo un release publicado antes de adjuntar el ZIP y la API devolvio `Cannot upload assets to an immutable release`.
- Causa: en este repositorio, un release publicado queda inmutable para subida posterior de assets.
- Solucion aplicada: publicar el paquete Windows x64 en un tag especifico `V-01.01-win-x64`, creando primero el release como draft, subiendo el asset y publicandolo despues. El draft untagged generado por el intento fallido se elimino. Tambien se elimino el tag remoto accidental `V-01.01` para evitar ambiguedad con la rama del mismo nombre.

### PR rechazado por historia Git sin ancestro comun

- Contexto: la API de GitHub rechazo el PR desde `V-01.01` a `main` con `The V-01.01 branch has no history in common with main`.
- Causa: `origin/main` habia sido forzado al commit inicial con `LICENSE`, dejando la rama de version sin ancestro comun con la rama base remota.
- Solucion aplicada: fusionar `origin/main` en `V-01.01` con `git merge --allow-unrelated-histories --no-edit origin/main`, incorporando `LICENSE` en raiz.

### 2026-04-20 - V-01.02 - Query params sensibles en auditoria de integracion

- Contexto: `IntegrationAuthMiddleware` serializaba todo `context.Request.Query` al registro de auditoria de integracion.
- Causa: la serializacion no distinguia claves sensibles; un cliente mal configurado podia meter `?token=...` o `?api_key=...` en la URL y quedar guardado en claro.
- Solucion aplicada: `HashSet` de claves sensibles (`token`, `api_key`, `apikey`, `secret`, `password`, `authorization`, `access_token`, `refresh_token`, `bearer`) y reemplazo por marcador `REDACTED` en la serializacion del registro de auditoria.

### 2026-04-20 - V-01.02 - `backup_path` y `export_path` sin validacion de traversal

- Contexto: ambos valores vienen de la tabla `CONFIGURACION` editable por admins. Se usaban directo en `Path.Combine`/`Directory.CreateDirectory`.
- Causa: faltaba validar ruta absoluta, caracteres invalidos y segmentos `..`. Un admin podia elegir una ruta relativa o apuntar a una carpeta fuera de la raiz prevista.
- Solucion aplicada: helper `ResolveSafeDirectory` que rechaza rutas no rooted, con caracteres invalidos o con segmentos `..`, aplicado en `BackupService.CreateBackupAsync` y `ExportacionService`.

### 2026-04-20 - V-01.02 - Email de usuarios borrados expuesto via integracion

- Contexto: `IntegrationOpenClawController` hacia `IgnoreQueryFilters()` al resolver `creado_por_id -> email` y devolvia emails de usuarios con `deleted_at != null`.
- Causa: necesidad de rellenar el historico incluso si el usuario ya no existe; se cargaba el email real del usuario borrado.
- Solucion aplicada: se sigue cargando la fila, pero si `deleted_at` no es nulo el email devuelto es el literal `usuario-eliminado`. Asi se mantiene el historico sin filtrar PII.

### 2026-04-20 - V-01.02 - Kestrel de desarrollo escuchando en todas las interfaces

- Contexto: `appsettings.Development.json` y su plantilla bindeaban `https://0.0.0.0:5000` con `AllowedHosts="*"`.
- Causa: comodin de desarrollo para acceso desde otros equipos de la LAN.
- Solucion aplicada: binding a `localhost` y `AllowedHosts=localhost` tambien en Development. Si hace falta LAN, hay que pedirlo explicito.

### 2026-04-20 - V-01.02 - `dotnet publish` empaquetaba secretos de desarrollo

- Contexto: `scripts/Build-Release.ps1` ejecuta `dotnet publish` sin exclusiones explicitas. Cualquier paquete generado por el script incluia `appsettings.Development.json` y las plantillas dentro de la carpeta `api` del release.
- Causa: los csproj de API y Watchdog no marcaban esos archivos con `CopyToPublishDirectory="Never"`.
- Solucion aplicada: `ItemGroup` con `Content Update="..." CopyToPublishDirectory="Never" ExcludeFromSingleFile="true"` para los tres ficheros en ambos csproj. Cualquier release futuro queda limpio de secretos de desarrollo.

## 2026-04-25 - V-01.04 - Importacion bloqueaba filas informativas con concepto pero sin fecha/monto/saldo

- Contexto: al validar extractos pegados desde banco, algunas filas con solo concepto aparecian como error (`Monto vacio`, `Fecha vacia`, `Saldo vacio`) y no podian importarse.
- Causa: la validacion trataba cualquier campo obligatorio vacio como error fatal, aunque esas filas fueran descripciones/informacion adicional exportada por el banco.
- Solucion aplicada: si una fila tiene concepto y deja vacios fecha, importe y saldo, se convierte en fila importable con advertencias: fecha y saldo se heredan de la ultima fila valida anterior y el monto se importa como `0`. Las filas ambiguas o parcialmente rotas siguen siendo errores.
- Verificacion: `dotnet test Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj --filter ImportacionServiceTests` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.

### 2026-04-20 - V-01.02 - Scripts smoke y docs historicas con credenciales

- Contexto: `phase2-smoke.ps1`, `phase2-smoke-curl.ps1`, `Otros/Raiz anterior/SPEC.md` y `CORRECCIONES.md` contenian passwords/usuarios concretos.
- Causa: artefactos antiguos de pruebas y planificacion quedaron con datos reales aunque viven en `Otros/` (fuera del repo principal, pero presentes en la maquina de trabajo).
- Solucion aplicada: los scripts leen las passwords de `ATLAS_SMOKE_ADMIN_PASSWORD`/`ATLAS_SMOKE_TEST_PASSWORD` (fallan si no existen). Los documentos historicos sustituyen los valores por placeholders.

## 2026-04-23 - V-01.03 - ExtractosController concedia alcance global con dashboard-only

- Contexto: `GET /api/extractos` usaba `GetAllowedAccountIds`, y ese helper concedia acceso a todas las cuentas si existia permiso global con `PuedeVerDashboard=true`.
- Causa: logica de autorizacion local mas permisiva que `UserAccessService`.
- Solucion aplicada: `GetAllowedAccountIds` y `CanViewTitular` ahora solo conceden alcance global con permisos de datos (`agregar`, `editar`, `eliminar`, `importar`), excluyendo `PuedeVerDashboard`.
- Verificacion: test de regresion en `ExtractosControllerTests` + ejecucion de `ExtractosControllerTests` y `UserAccessServiceTests` (8/8 OK).

## 2026-04-24 - V-01.03 - Frontend mostraba dashboards de cuenta a perfiles dashboard-only globales

- Contexto: tras cerrar la fuga de datos en extractos, el frontend seguia ofreciendo enlaces y botones a `/dashboard/cuenta/:id` desde `CuentasPage` y otras vistas, aunque el backend ya bloqueaba ese detalle para perfiles con permiso global solo de dashboard.
- Causa: `permisosStore.canViewCuenta` trataba cualquier fila global (`cuenta_id/titular_id null`) como acceso de cuenta, sin distinguir si era solo `PuedeVerDashboard`.
- Solucion aplicada: `canViewCuenta`, `canAddInCuenta`, `canEditCuenta`, `canDeleteInCuenta`, `canImportInCuenta`, `getColumnasVisibles` y `getColumnasEditables` pasan a ignorar filas globales `dashboard-only`; solo cuentan filas scopeadas de cuenta/titular o filas globales con acceso global de datos. `CuentasPage` muestra `Sin acceso` en vez de CTA operativos y `CuentaDetailPage` redirige al dashboard si recibe `403`.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.
