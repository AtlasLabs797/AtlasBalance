# Log de errores e incidencias

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

### 2026-04-20 - V-01.02 - Scripts smoke y docs historicas con credenciales

- Contexto: `phase2-smoke.ps1`, `phase2-smoke-curl.ps1`, `Otros/Raiz anterior/SPEC.md` y `CORRECCIONES.md` contenian passwords/usuarios concretos.
- Causa: artefactos antiguos de pruebas y planificacion quedaron con datos reales aunque viven en `Otros/` (fuera del repo principal, pero presentes en la maquina de trabajo).
- Solucion aplicada: los scripts leen las passwords de `ATLAS_SMOKE_ADMIN_PASSWORD`/`ATLAS_SMOKE_TEST_PASSWORD` (fallan si no existen). Los documentos historicos sustituyen los valores por placeholders.
