# Registro de bugs

## Abiertos

### 2026-04-20 - V-01.02 - Estado Git local no fiable

- Contexto: `git status --short` funciona, pero lista practicamente todo el arbol como `untracked`.
- Causa probable: copia local/repo recreado sin historial o indice util para esta carpeta.
- Impacto: no se puede obtener diff fino ni preparar commit fiable desde esta copia sin reparar el estado Git.
- Estado: abierto. No se ha tocado `.git` para evitar empeorar el repositorio local.

## Cerrados

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

- Contexto: `frontend/src` tenia fixes ya aplicados, pero `backend/src/GestionCaja.API/wwwroot` seguia sirviendo bundles antiguos.
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
