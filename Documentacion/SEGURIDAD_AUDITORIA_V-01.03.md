# Auditoria de seguridad V-01.03

Fecha: 2026-04-25

## Resumen

Riesgo residual despues de esta pasada: bajo-medio.

Se corrigieron los hallazgos explotables que encontre en esta sesion: sesiones no invalidadas tras reset/cambio de password, reuse de refresh token sin escalado, login enumerable/rate limit flojo, bearer invalido de integracion sin throttle previo, URL de actualizaciones usable como SSRF basico, rutas configurables validadas tarde, password minimo flojo, credenciales one-shot persistentes y dependencia frontend vulnerable.

## Alcance revisado

- Backend ASP.NET Core: auth, JWT/cookies, CSRF, permisos, auditoria, integracion OpenClaw, backups, exportaciones, actualizaciones y Watchdog.
- Frontend React/Vite: validaciones de password, permisos de cuenta, dependencias npm.
- Scripts y packaging: instalador, credenciales iniciales y rutas de produccion.
- Dependencias: npm audit y NuGet vulnerable.
- Documentacion activa bajo `Documentacion` y version `V-01.03`.

## Hallazgos corregidos

### 1. Sesiones sobreviven a reset/cambio de password

- Severidad: alta.
- Correccion: `SecurityStamp` y `PasswordChangedAt` en `USUARIOS`, claim en access token, validacion en `UserStateMiddleware`, rotacion de stamp y revocacion de refresh tokens en cambio/reset/delete.
- Tests: `AuthServiceTests`, `UsuariosControllerTests`.

### 2. Refresh token reuse no escalaba a incidente

- Severidad: media-alta.
- Correccion: si reaparece un refresh token revocado por rotacion, se revocan sesiones activas, se rota `SecurityStamp` y se audita `REFRESH_TOKEN_REUSE_DETECTED`.
- Tests: `RefreshToken_Should_Revoke_Active_Sessions_When_Rotated_Token_Is_Reused`.

### 3. Login enumerable y bloqueo abusivo

- Severidad: media.
- Correccion: cuentas bloqueadas devuelven la misma respuesta externa que credenciales invalidas; se agrega throttle por cliente/email antes de insistir y se sube el umbral de bloqueo global para reducir DoS barato.
- Tests: `Login_Should_Throttle_Client_Before_Global_Account_Lock` y bloqueo existente.

### 4. Bearer invalido de integracion consultaba BD sin throttle previo

- Severidad: media.
- Correccion: `IntegrationAuthMiddleware` limita intentos invalidos por IP/minuto antes de validar tokens activos.
- Tests: suite de `IntegrationAuthMiddlewareTests`.

### 5. `app_update_check_url` permitia GET arbitrario

- Severidad: media-baja.
- Correccion: allowlist HTTPS estricta a `github.com/AtlasLabs797/AtlasBalance` o `api.github.com/repos/AtlasLabs797/AtlasBalance/...`; configuraciones inseguras se rechazan o caen al endpoint oficial.
- Tests: `ConfiguracionControllerTests` y `ActualizacionServiceTests`.

### 6. Rutas configurables se normalizaban antes de validar

- Severidad: media.
- Correccion: backup/export/download/Watchdog validan entrada cruda como ruta explicitamente absoluta antes de `Path.GetFullPath`, bloqueando relativas y traversal.
- Tests: `ExportacionServiceTests`, `WatchdogOperationsServiceTests`.

### 7. Password minimo de 8 caracteres

- Severidad: baja-media.
- Correccion: minimo 12 caracteres y bloqueo de passwords comunes en backend; frontend actualizado para crear/reset/cambio.

### 8. `INSTALL_CREDENTIALS_ONCE.txt` quedaba persistente

- Severidad: media-baja.
- Correccion: ACL restringida se mantiene y el instalador registra tarea programada SYSTEM para borrarlo a las 24 horas.

### 9. `postcss` vulnerable

- Severidad: moderada.
- Correccion: `postcss` actualizado de `8.5.9` a `8.5.10` en `package-lock.json`.
- Fuente: GitHub Advisory `GHSA-qx2v-qp2m-jg93`.

## Verificacion

- `dotnet build '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `dotnet test '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-build`: 94/94 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list '.\Atlas Balance\backend\GestionCaja.sln' package --vulnerable --include-transitive`: sin vulnerabilidades.
- Parser PowerShell de `Instalar-AtlasBalance.ps1`: OK.

## Riesgos residuales

- Rate limiting en memoria: suficiente para esta app local/Windows, pero si algun dia se escala a multiples instancias, debe ir a Redis/Postgres.
- `INSTALL_CREDENTIALS_ONCE.txt`: el borrado a 24 horas reduce el riesgo, pero el primer dia sigue siendo material sensible. Guardarlo en un password manager y borrarlo manualmente sigue siendo lo correcto.
- No hice pentest dinamico con trafico real ni fuzzing largo. Esto fue auditoria estatica + pruebas automatizadas + hardening focalizado.
