# Documentacion tecnica

## 2026-04-25 - V-01.03 - Paquete release Windows x64 generado

### Que cambio

- Se genero el paquete `AtlasBalance-V-01.03-win-x64` en `Atlas Balance/Atlas Balance Release`.
- Se genero el ZIP `AtlasBalance-V-01.03-win-x64.zip` para distribucion.
- `scripts/Build-Release.ps1` recompilo el frontend y reemplazo `GestionCaja.API/wwwroot` con el bundle de produccion actual.
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
- `INSTALL_CREDENTIALS_ONCE.txt` se borra automaticamente con tarea programada SYSTEM a las 24 horas.
- `postcss` queda resuelto a `8.5.10`.

### Impacto operativo

- Tras desplegar esta version, los access tokens antiguos sin `security_stamp` dejan de ser validos. Eso es correcto: los usuarios tendran que autenticarse otra vez.
- La URL de actualizaciones ya no acepta endpoints arbitrarios; si se necesita otro canal de releases, primero hay que ampliar la allowlist de forma explicita.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.

### Verificacion

- `dotnet build '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `dotnet test '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-build`: 94/94 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list '.\Atlas Balance\backend\GestionCaja.sln' package --vulnerable --include-transitive`: sin vulnerabilidades.
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
- `dotnet build '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
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

## 2026-04-23 - V-01.03 - Cierre de fuga de alcance global en extractos

### Que cambio

- `ExtractosController.GetAllowedAccountIds` y `CanViewTitular` dejaron de tratar `PuedeVerDashboard` global como permiso global de datos.
- El alcance global en extractos queda restringido a permisos de datos reales: `PuedeAgregarLineas`, `PuedeEditarLineas`, `PuedeEliminarLineas` o `PuedeImportar`.
- Se agrego regresion automatizada en `ExtractosControllerTests` para impedir que `/api/extractos` devuelva datos cross-account a perfiles dashboard-only globales.

### Por que

La logica local de `ExtractosController` estaba mas permisiva que `UserAccessService`. Esa divergencia abria una fuga de datos financieros entre cuentas.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~GestionCaja.API.Tests.ExtractosControllerTests|FullyQualifiedName~GestionCaja.API.Tests.UserAccessServiceTests"`: 8/8 OK.

## 2026-04-24 - V-01.03 - Frontend alineado con permisos reales de cuenta

### Que cambio

- `frontend/src/stores/permisosStore.ts` diferencia entre alcance de cuenta y permiso global solo de dashboard.
- Una fila global `cuenta_id = null`, `titular_id = null` ya no habilita `canViewCuenta` ni contamina `getColumnasVisibles/getColumnasEditables` salvo que conceda acceso global de datos (`agregar`, `editar`, `eliminar`, `importar`).
- `frontend/src/pages/CuentasPage.tsx` ya no ofrece enlaces o botones a `/dashboard/cuenta/:id` para cuentas sin acceso real; muestra `Sin acceso`.
- `frontend/src/pages/CuentaDetailPage.tsx` intercepta `403` del backend y redirige a `/dashboard` en vez de dejar al usuario atrapado en un error de carga.

### Por que

El backend ya estaba bien. El frontend seguia mintiendo: enseñaba rutas de cuenta a perfiles `dashboard-only` globales, como si pudieran abrirlas. Eso no filtraba datos, pero era UX rota y semantica de permisos incoherente.

### Reglas tecnicas

- En frontend, el acceso a cuenta no debe inferirse de cualquier permiso coincidente. Una fila global solo vale como acceso de cuenta si equivale a acceso global de datos.
- Los estados visuales de apertura de cuenta tienen que apoyarse en la misma semantica que backend. Si backend va a responder `403`, frontend no debe mostrar un CTA operativo.
- Cuando una ruta depende de datos protegidos y el backend responde `403`, la pantalla debe redirigir o cerrar el paso de forma limpia, no quedarse en un error generico.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.
