# Auditoria de uso, bugs y seguridad - V-01.04

Fecha: 2026-04-25

## Veredicto

No he encontrado un P0 ni una vulnerabilidad critica explotable con la evidencia disponible. La base esta bastante mejor que una app interna tipica: auth por cookies httpOnly, CSRF, security stamp, revocacion de refresh tokens, headers, rutas de exportacion/backup validadas y auditoria de dependencias limpia.

Estado posterior a la correccion: los hallazgos que no debian pasar a release ya estan corregidos.

1. Tailwind/shadcn se eliminaron del frontend.
2. `CuentasController.Resumen` se alineo con metadatos de cuenta/titular/plazo fijo.
3. `DatePickerField`, `ConfirmDialog` y `AppSelect` recibieron mejoras de teclado/accesibilidad.
4. Los gradientes decorativos detectados se redujeron a superficies planas.
5. `backend/src/GestionCaja.API/wwwroot` se sincronizo con el build frontend corregido.

Lo que sigue sin estar perfecto: falta ejecutar Playwright E2E con `E2E_ADMIN_PASSWORD` en una base disposable y el estado Git local sigue siendo mala base para revisar diffs finos.

## Comprobaciones ejecutadas

- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd ls axios react-router-dom postcss vite`: axios 1.15.2, react-router-dom 6.30.3, postcss 8.5.10, vite 8.0.8.
- `dotnet list ... package --vulnerable --include-transitive`: sin paquetes vulnerables en NuGet.
- `dotnet test ...GestionCaja.API.Tests.csproj -c Release`: 107/107 OK.
- Parser PowerShell sobre `Atlas Balance/scripts/*.ps1`: sin errores.
- `git diff --check`: sin errores de whitespace; solo avisos de normalizacion LF/CRLF.
- Revision manual de auth, cookies, CSRF, middleware de integracion, headers, permisos, exportaciones, backups, updates, CI/CD, Docker y frontend critico.

No se ejecuto Playwright E2E porque `E2E_ADMIN_PASSWORD` no esta definido y el propio test se niega a adivinar credenciales para no bloquear el admin. Buena decision del test; mala idea seria forzarlo.

## Hallazgos

### P1 - Stack frontend violado por Tailwind/shadcn - Corregido

Ubicacion:

- `Atlas Balance/frontend/package.json`
- `Atlas Balance/frontend/vite.config.ts`
- `Atlas Balance/frontend/src/styles/global.css`
- `Atlas Balance/frontend/components.json`

Impacto:

El proyecto prohibe Tailwind y shadcn, pero el build usa `@tailwindcss/vite`, importa `tailwindcss`, `tw-animate-css` y `shadcn/tailwind.css`, y mantiene dependencias shadcn. Esto no rompe el build hoy, pero aumenta superficie de supply chain, mete otro sistema de estilos y contradice la arquitectura documentada. En una app financiera interna, meter dependencias UI por inercia es mala higiene.

Recomendacion:

Ejecutada: se quitaron Tailwind/shadcn, sus dependencias, plugin Vite, imports CSS, `components.json`, boton shadcn y utilidades asociadas.

### P2 - Contrato duplicado/inconsistente de resumen de cuenta - Corregido

Ubicacion:

- `Atlas Balance/backend/src/GestionCaja.API/Controllers/CuentasController.cs`
- `Atlas Balance/backend/src/GestionCaja.API/Controllers/ExtractosController.cs`
- `Atlas Balance/backend/src/GestionCaja.API/DTOs/CuentasDtos.cs`

Impacto:

La UI usa `/api/extractos/cuentas/{id}/resumen`, que si devuelve `tipo_cuenta`, `plazo_fijo`, `titular_id` y notas. Pero `CuentasController.Resumen` tambien existe y devuelve `CuentaResumenResponse`, sin esos campos. Es un contrato paralelo y mas pobre. Esto es una trampa para el siguiente cambio: alguien llamara al endpoint "obvio" de cuentas y no vera vencimiento de plazo fijo.

Recomendacion:

Ejecutada: `CuentasController.Resumen` ahora devuelve cuenta, titular, tipo, notas, ultima actualizacion y `plazo_fijo`; se agrego test de regresion para cuenta `PLAZO_FIJO`.

### P2 - Gaps de accesibilidad en controles propios - Corregido parcialmente

Ubicacion:

- `Atlas Balance/frontend/src/components/common/DatePickerField.tsx`
- `Atlas Balance/frontend/src/components/common/ConfirmDialog.tsx`
- `Atlas Balance/frontend/src/components/common/AppSelect.tsx`

Impacto:

El `DatePickerField` tiene `role="dialog"` y botones, pero los dias se anuncian como numeros sin etiqueta de fecha completa y no hay navegacion tipo calendario con flechas dentro del grid. `ConfirmDialog` enfoca el boton cancelar y restaura foco, pero no atrapa tabulacion dentro del modal. `AppSelect` va bastante mejor, aunque sigue siendo un combobox custom que merece prueba con teclado real.

Recomendacion:

Ejecutada: dias con etiqueta completa, navegacion con flechas/Home/End, focus trap en dialogo y Enter/Espacio en select. Pendiente razonable: prueba manual con lector de pantalla real.

### P3 - Estetica: quedan huellas de "AI admin UI" - Corregido

Ubicacion:

- `Atlas Balance/frontend/src/styles/global.css`
- `Atlas Balance/frontend/src/styles/auth.css`
- `Atlas Balance/frontend/src/styles/layout/dashboard.css`
- `Atlas Balance/frontend/src/styles/layout/entities.css`

Impacto:

Hay fondos con radial gradients y tarjetas/listas con degradados suaves. No rompe uso ni seguridad, pero choca con la regla visual de evitar orbes/decoracion generica. La app ya es suficientemente sobria; estos efectos no compran mucho.

Recomendacion:

Ejecutada: se sustituyeron fondos decorativos por superficies planas. Se conservaron solo degradados funcionales de select/skeleton.

### P3 - Git local sigue siendo mala base de revision

Ubicacion:

- Estado del repositorio local.

Impacto:

`git status --short` lista una cantidad enorme de cambios y archivos untracked. La auditoria puede validar build/test/codigo actual, pero no puede dar una lectura fina de "que cambio exactamente" frente a una base limpia. Para publicar, esto es jugar con niebla.

Recomendacion:

Antes de push/release: limpiar staging mentalmente, revisar un diff intencional por version y excluir generados/ruido.

## Seguridad

### Lo que esta bien

- Cookies de auth httpOnly, SameSite Strict y Secure fuera de Development.
- CSRF en mutaciones API, con header `X-CSRF-Token`.
- Refresh token rotado y hasheado; reuse revoca sesiones.
- `SecurityStamp` invalida access tokens tras cambios sensibles.
- Produccion rechaza secretos vacios/placeholders y `AllowedHosts=*`.
- Headers presentes: CSP, HSTS fuera de Development, X-Content-Type-Options, X-Frame-Options SAMEORIGIN, Referrer-Policy y Permissions-Policy.
- OpenClaw va por middleware bearer separado, token hasheado, rate limit y redaccion de query params sensibles.
- Exportaciones limitadas a `.xlsx` dentro de `export_path`.
- Backup/export paths rechazan rutas relativas y traversal.
- GitHub Actions tiene permisos minimos y actions fijadas por SHA.
- `.env`, appsettings locales, certificados y release packages estan ignorados.

### Riesgo residual

- SCA limpia no significa "seguro"; solo significa "sin CVEs conocidas en las fuentes consultadas".
- No hay Semgrep/Trivy/Gitleaks disponibles en este entorno; la SAST ha sido manual y por patrones.
- E2E no ejecutado por falta de password de admin en entorno.

## Score

### Seguridad

Risk score: 4/100, bajo.

- Critical: 0
- High: 0
- Medium: 0
- Low/Info: 4

### UI tecnica

| Dimension | Score | Nota |
|---|---:|---|
| Accesibilidad | 3/4 | Buena base, controles custom necesitan teclado/screen reader real. |
| Rendimiento | 3/4 | Build correcto; charts chunk pesado pero aislado. |
| Theming | 3/4 | Tokens propios fuertes, pero Tailwind/shadcn meten doble sistema. |
| Responsive | 3/4 | Hay breakpoints y layouts moviles; falta prueba visual E2E esta sesion. |
| Anti-patrones | 2/4 | Gradientes decorativos y dependencia shadcn/Tailwind huelen a desviacion de criterio. |
| Total | 14/20 | Bueno, pero no excelente. |

## Siguiente accion recomendada

1. Ejecutar Playwright E2E con `E2E_ADMIN_PASSWORD` en una base disposable.
2. Revisar el estado Git local antes de publicar: ahora mismo hay demasiados cambios/untracked para una revision fina.
3. Hacer una pasada manual con teclado y lector de pantalla real sobre fecha, dialogo y select.
