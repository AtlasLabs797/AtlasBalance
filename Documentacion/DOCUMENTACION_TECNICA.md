# Documentacion tecnica

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
- `dotnet test .\backend\GestionCaja.sln -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 82/82 OK.
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
- `GestionCaja.Watchdog` escucha explicitamente en localhost mediante Kestrel.
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

- `dotnet build "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore`: OK, 0 warnings.
- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-build`: 83/83 OK.
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
- El build frontend generado se copio a `backend/src/GestionCaja.API/wwwroot` para que la API local sirva el bundle corregido.

### Por que

La revision previa no estaba equivocada, pero estaba incompleta: el codigo principal ya tenia varios fixes, mientras que configuracion, scripts y artefactos servidos seguian arrastrando restos. Eso es peor que un bug obvio, porque parece arreglado hasta que instalas o pruebas desde el backend.

### Verificacion

- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore`: 82/82 OK.
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
- Se añadieron plantillas de configuracion para API y Watchdog, y un `.env.example` sin secretos.
- `SeedData` usa `V-01.02` y el check de actualizacion usa la version runtime en el User-Agent.
- Se corrigieron mensajes mojibake en importacion y asunto SMTP.
- GitHub Actions queda fijado a SHAs concretos para reducir riesgo de supply chain.
- Se añadio `.gitignore` dentro de `Atlas Balance` para proteger la app si se trabaja desde esa carpeta como raiz.

### Por que

Los secretos "solo de desarrollo" en archivos base son una bomba lenta: se copian, se reutilizan y un dia llegan a produccion. La configuracion base debe ser segura por defecto y obligar a crear secretos locales/produccion fuera de Git.

### Reglas tecnicas

- No commitear `appsettings.Development.json`, `appsettings.Production.json`, `.env`, certificados, logs ni paquetes generados.
- Para desarrollo local, copiar las plantillas y rellenar secretos reales en archivos ignorados.
- Para produccion, generar secretos fuertes distintos para JWT, Watchdog, PostgreSQL, certificado y admin inicial.
- No ejecutar restauraciones Watchdog si `WatchdogSettings:DbPassword` no esta configurado.

### Verificacion

- `python Skills/Seguridad/cyber-neo-main/skills/cyber-neo/scripts/scan_secrets.py "Atlas Balance" --json`: 0 hallazgos.
- `dotnet list "Atlas Balance/backend/GestionCaja.sln" package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm.cmd audit --json`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
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
- `dotnet build "Atlas Balance/backend/GestionCaja.sln" --no-restore` debe resolver rutas relativas dentro de la app.

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
