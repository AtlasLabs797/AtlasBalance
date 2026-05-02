# Auditoria de seguridad V-01.05

Fecha: 2026-04-25

## Addendum 2026-05-01

Se aplico el checklist general de seguridad de app contra la superficie real de Atlas Balance.

Cambios cerrados:

- MFA TOTP obligatorio para usuarios web antes de emitir JWT/cookies.
- Revocacion de sesiones al cambiar permisos, email o perfil de usuario.
- Verificacion SHA-256 del digest de assets descargados desde GitHub Releases.
- Escaneo CI de secretos de alta confianza.
- Documento de respuesta a incidentes.

Detalle: `Documentacion/SEGURIDAD_CHECKLIST_APP_V-01.05_2026-05-01.md`.

## Addendum 2026-05-02

Se cerro la segunda tanda de hallazgos residuales del escaneo repo-wide.

Cambios cerrados:

- Credenciales one-shot de instalacion/reset: directorio `C:\AtlasBalance\config` protegido antes de escribir secretos y fallo cerrado si ACL falla.
- `Reset-AdminPassword.ps1`: ejecucion obligatoria como Administrador.
- `ExtractosController.ToggleFlag`: permisos por campo real cambiado (`flagged` y `flagged_nota`).
- `DashboardService`: `PuedeVerDashboard` global sin flags de datos ya no concede scope global.
- `IntegrationOpenClawController.Auditoria`: no expone valores de auditoria de extractos soft-deleted.
- `ImportacionPage`: `returnTo` solo acepta rutas internas.
- RLS `EXPORTACIONES`: politica de escritura usa `can_write_cuenta_by_id`.
- CI/compose: PostgreSQL fijado por digest OCI.

Verificacion: tests focalizados backend 20/20 OK, frontend lint/build OK, parser PowerShell OK.

## Resumen

Riesgo residual tras esta pasada: bajo.

Se revisaron las incidencias documentadas de bugs y seguridad, dependencias npm/NuGet, cabeceras, cookies, CSRF, auth, permisos, integracion OpenClaw, rutas de exportacion/backup, CI/CD, secretos versionables y artefactos servidos. El unico cambio aplicado fue de supply chain: el frontend declaraba minimos antiguos aunque el lockfile ya resolvia versiones seguras.

## Alcance revisado

- Backend ASP.NET Core: auth, JWT/cookies, CSRF, permisos, auditoria, integracion OpenClaw, exportaciones, backups y configuracion de seguridad.
- Frontend React/Vite: storage, interceptores Axios, rutas, build servido y dependencias.
- Scripts/CI: instalacion, actualizacion, `.gitignore`, workflows y empaquetado servido.
- Documentacion: `LOG_ERRORES_INCIDENCIAS.md`, `REGISTRO_BUGS.md`, auditorias V-01.02/V-01.03 e incidencias Windows Server 2019.

## Hallazgos

### Manifiesto frontend con minimos vulnerables

- Severidad: media.
- Contexto: `package-lock.json` resolvia `axios@1.15.0` y `react-router-dom@6.30.3`, pero `package.json` seguia permitiendo minimos antiguos.
- Riesgo: una regeneracion de dependencias sin lockfile podia caer en rangos afectados por advisories recientes.
- Correccion: actualizado a `axios ^1.15.2` y `react-router-dom ^6.30.3`.
- Fuentes: GitHub Advisory `GHSA-4hjh-wcwx-xvwj` para Axios y advisory de React Router `CVE-2025-68470`.

## Incidencias previas revisadas

- Sesiones tras reset/cambio de password: cubierto por `SecurityStamp`, revocacion de refresh tokens y tests.
- Dashboard-only con fuga de datos: cubierto en backend y frontend; tests de permisos pasan.
- Rutas `backup_path`/`export_path`: validacion de ruta absoluta y extension revisada.
- Exportaciones: descarga limitada a `.xlsx` bajo `export_path`.
- Integracion OpenClaw: bearer hasheado, throttle previo a BD, auditoria con parametros sensibles redactados.
- Cabeceras anti-frame: `SAMEORIGIN`/`frame-ancestors 'self'` mantiene iframe interno y bloquea embedding externo.
- Secretos: `.env`, certificados, appsettings Development y logs quedan ignorados por Git; no hay secretos versionables detectados.
- CI: actions fijadas por SHA y permisos `contents: read`.

## Verificacion

- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\backend\src\GestionCaja.API\wwwroot /MIR`: OK, codigo 1 por archivos copiados.
- `dotnet test ".\Atlas Balance\backend\GestionCaja.sln" -c Release --no-build`: 107/107 OK.
- `dotnet list ".\Atlas Balance\backend\GestionCaja.sln" package --vulnerable --include-transitive`: sin vulnerabilidades.
- `git diff --check` sobre archivos tocados: OK.
- `wwwroot`: sin sourcemaps, plantillas Development ni `.env`.

## Riesgos residuales

- `semgrep`, `trivy` y `gitleaks` no estan instalados en este entorno; la auditoria fue manual + npm/NuGet + tests.
- Existe un `.env` local ignorado por Git. Correcto para desarrollo, pero no debe copiarse a paquetes ni compartirse.
- El bug abierto de estado Git local sigue sin tocarse; arreglar `.git` sin pedir permiso seria una forma bastante eficiente de romperlo todo.
