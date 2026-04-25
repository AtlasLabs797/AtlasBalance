# Documentacion tecnica

## 2026-04-25 - V-01.04 - Paquete final y publicacion

### Que cambio

- Se regenero el paquete `AtlasBalance-V-01.04-win-x64.zip` con `scripts/Build-Release.ps1`.
- El build frontend del paquete quedo sincronizado en `backend/src/GestionCaja.API/wwwroot`.
- El ZIP final queda fuera de Git y se publica como asset de GitHub Release.

### Verificacion

- `scripts\Build-Release.ps1 -Version V-01.04`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance\backend\tests\GestionCaja.API.Tests\GestionCaja.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list "Atlas Balance\backend\src\GestionCaja.API\GestionCaja.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- Paquete verificado sin `appsettings.Development.json`, `.env`, `node_modules`, `obj`, `bin\Debug` ni `.bak-iframe-fix`.
- SHA256 final del ZIP `AtlasBalance-V-01.04-win-x64.zip`: `B5ABC5525CBD49F2BD0A5ADC5B930A2113AF323F99C1337087B8E0D7875E6A10`.

## 2026-04-25 - V-01.04 - Auditoria de bugs y seguridad

### Que cambio

- Se reviso la superficie tecnica de seguridad activa: autenticacion JWT en cookies httpOnly, CSRF por header `X-CSRF-Token`, validacion de `SecurityStamp`, permisos backend, integracion OpenClaw, rutas de backup/exportacion, cabeceras HTTP, CI y secretos versionables.
- Se actualizaron los minimos declarados del frontend para cerrar deuda de supply chain: `axios ^1.15.2` y `react-router-dom ^6.30.3`.
- El bundle de produccion se recompilo y se sincronizo con `backend/src/GestionCaja.API/wwwroot`.
- No se cambiaron contratos de API ni modelo de datos.

### Por que

El lockfile ya resolvia versiones seguras, pero dejar rangos minimos vulnerables en `package.json` es pedir que una reinstalacion sin lockfile fiable abra otra vez el agujero. Eso no es "flexibilidad", es pereza con consecuencias.

### Verificacion

- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ".\Atlas Balance\backend\GestionCaja.sln" -c Release --no-build`: 107/107 OK.
- `dotnet list ".\Atlas Balance\backend\GestionCaja.sln" package --vulnerable --include-transitive`: sin vulnerabilidades.
- `wwwroot`: sincronizado y sin sourcemaps, plantillas Development ni `.env`.

## 2026-04-25 - V-01.04 - Importacion simple de plazo fijo y resumen dashboard

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
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK.
- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" --filter "ImportacionServiceTests|DashboardServiceTests"`: 28/28 OK.
- `dotnet build "Atlas Balance/backend/src/GestionCaja.API/GestionCaja.API.csproj" -c Release`: OK, 0 warnings.

## 2026-04-25 - V-01.04 - Actualizaciones post-instalacion

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
- `scripts\Build-Release.ps1 -Version V-01.04`: OK; ZIP regenerado.
- Scripts empaquetados parsean correctamente.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.04-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.
- Pendiente de entorno real: probar update desde `V-01.03` instalada a `V-01.04` en Windows Server 2019.

## 2026-04-25 - V-01.04 - Cierre de incidencias instalacion Windows Server 2019

### Que cambio

- `scripts\install.ps1` valida que la carpeta sea un paquete release antes de autoelevar.
- `scripts\Instalar-AtlasBalance.ps1` valida `api\GestionCaja.API.exe` y `watchdog\GestionCaja.Watchdog.exe` antes de instalar.
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
- `scripts\Build-Release.ps1 -Version V-01.04`: OK; ZIP generado.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Scripts empaquetados parsean correctamente.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.04-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.

## 2026-04-25 - V-01.04 - Apertura de version

### Que cambio

- `V-01.04` pasa a ser la version activa del sistema.
- Backend: `Directory.Build.props` sube a `1.4.0` y `InformationalVersion` a `V-01.04`.
- Frontend: `package.json` y `package-lock.json` suben a `1.4.0`; `appVersion` pasa a `V-01.04`.
- `Atlas Balance/VERSION`, `SeedData`, `Build-Release.ps1` e `Instalar-AtlasBalance.ps1` quedan alineados con `V-01.04`.
- `Documentacion/Versiones/v-01.03.md` queda cerrada como version publicada.
- `Documentacion/Versiones/v-01.04.md` queda como archivo activo de trabajo.

### Por que

`V-01.03` ya fue publicada. Seguir metiendo cambios ahi seria una forma bastante tonta de romper la trazabilidad.

### Reglas tecnicas

- Todo cambio nuevo debe documentarse bajo `V-01.04`.
- El siguiente paquete debe generarse con `scripts/Build-Release.ps1 -Version V-01.04`.
- No reutilizar assets ni notas de release de `V-01.03` para publicar `V-01.04`.

### Verificacion

- `git diff --check`: OK.
- `dotnet build '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `npm.cmd run build`: OK con `atlas-balance-frontend@1.4.0`.

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

## 2026-04-25 - V-01.04 - Importacion con advertencias para filas solo concepto

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

- `dotnet test "Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" --filter ImportacionServiceTests`: 21/21 OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.

## 2026-04-25 - V-01.04 - Permiso global explicito para ver cuentas

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

- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" --filter "UserAccessServiceTests|UsuariosControllerTests|ExtractosControllerTests"`: 12/12 OK.
- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 97/97 OK.
- `dotnet build "Atlas Balance/backend/src/GestionCaja.API/GestionCaja.API.csproj" -c Release`: OK, 0 warnings.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; `robocopy` devolvio codigo `1`, copia correcta con archivos actualizados.

## 2026-04-25 - V-01.04 - Plazo fijo, autonomos, alertas por tipo y dashboard inmovilizado

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

- `dotnet build ...GestionCaja.API.csproj -c Release`: OK.
- Tests focalizados de cuentas/dashboard/alertas/plazos: 12/12 OK.
- Tests backend sin Testcontainers: 103/103 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK.

## 2026-04-25 - V-01.04 - Coherencia visual del frontend

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
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Screenshots Playwright de `/login`: `output/playwright/ui-login-desktop.png` y `output/playwright/ui-login-mobile.png`.

## 2026-04-25 - V-01.04 - CSS de layout separado por dominios

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
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.04 - Calendario nativo alineado con inputs

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
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.04 - Vencimiento visible en detalle de plazo fijo

### Que cambio

- `ExtractosDtos.CuentaResumenKpiResponse` incluye `TipoCuenta` y `PlazoFijo`.
- `ExtractosController.GetCuentaResumen`, `GetCuentasTitular` y `GetTitularesResumen` pasan `TipoCuenta` a `BuildSummary`.
- `BuildSummary` adjunta `PlazoFijoResponse` solo para cuentas `PLAZO_FIJO`.
- `CuentaDetailPage` muestra una banda compacta bajo el titulo con fecha de vencimiento, dias restantes/vencido y estado.
- `entities.css` agrega estilos de `.cuenta-plazo-summary`.

### Por que

El dato de vencimiento existia al crear/editar la cuenta y en la lista de cuentas, pero no aparecia en el dashboard de cuenta. Eso obligaba al usuario a salir de la pantalla donde esta mirando saldo y movimientos, justo donde el vencimiento importa.

### Verificacion

- `dotnet build "Atlas Balance\\backend\\src\\GestionCaja.API\\GestionCaja.API.csproj" -c Release`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.04 - Date picker propio

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
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Navegador in-app en `http://localhost:5173/cuentas`: se abre el modal de editar plazo fijo, el calendario se muestra con el sistema visual Atlas y no hay errores de consola.

## 2026-04-25 - V-01.04 - Correccion de hallazgos de auditoria

### Que cambio

- El frontend deja de depender de Tailwind/shadcn: se eliminan dependencias, plugin Vite, imports CSS, `components.json`, `components/ui/button.tsx` y `lib/utils.ts`.
- `global.css` queda como entrada de tokens/estilos propios, sin `@theme`, `@apply`, imports Tailwind ni compatibilidad shadcn.
- `backend/src/GestionCaja.API/wwwroot` se sincroniza desde `frontend/dist` para que la API sirva los bundles corregidos.
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
- `dotnet test ".\\Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" -c Release --filter CuentasControllerTests`: 4/4 OK.
- `dotnet test ".\\Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list ".\\Atlas Balance\\backend\\src\\GestionCaja.API\\GestionCaja.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
