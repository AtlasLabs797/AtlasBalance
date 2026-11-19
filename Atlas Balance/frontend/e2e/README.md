# E2E smoke tests

These tests cover the admin login, the main navigation buttons, the extract account filter, logout, API 5xx errors, console errors, and the configuration endpoint secret boundary.

Run them against a disposable/local database:

```powershell
$env:E2E_ADMIN_PASSWORD = Read-Host "E2E admin password"
npm run test:e2e
```

Optional variables:

- `E2E_ADMIN_EMAIL`: defaults to `admin@atlasbalance.local`.
- `E2E_BASE_URL`: defaults to `http://localhost:5173`.
- `E2E_API_HEALTH_URL`: defaults to `http://localhost:5002/api/health`.
- `E2E_SKIP_WEBSERVER=1`: use when Vite is already running and you do not want Playwright to manage it.

Do not run this with guessed credentials. The backend rate limit will lock the account after repeated failed logins.

## What CI runs (and what it does not)

CI runs **only the visual specs**, with:

```powershell
npx playwright test e2e/visual --reporter=list --output pw-test-results
```

- `.github/workflows/ci.yml` (job `e2e`): only on `push` to `main`.
- `.github/workflows/release.yml` (job `verify`): on every release.

`e2e/visual/*.spec.ts` uses the simulated API (`e2e/visual/mockApi.ts`) and the Vite server that Playwright starts itself (`webServer` in `playwright.config.ts`), so it needs no backend, no database and no credentials.

`e2e/admin-smoke.spec.ts` is **excluded on purpose**: it needs a real backend and `E2E_ADMIN_PASSWORD`, CI has neither, and the spec aborts when the variable is missing. It must be run locally against a disposable database.

## Visual capture harness (`e2e/visual/capture.spec.ts`)

One screenshot per route, for 2 themes (light/dark) x 2 viewports (1440x900 and 390x844), plus a
session of modals/dialogs (new holder form, import history and its revert dialog, MFA device
revocation). The API is simulated, so the harness needs no backend.

Route coverage (21 routes = the 20 app screens plus the `/cambiar-password` first-login screen):

| Group | Routes |
| --- | --- |
| Access | `/login`, `/cambiar-password` |
| Dashboards | `/dashboard`, `/dashboard/titular/:id`, `/dashboard/cuenta/:id` |
| Portfolio | `/titulares`, `/titulares/:id`, `/cuentas`, `/extractos`, `/conciliacion`, `/revision` |
| Operations | `/importacion`, `/formatos-importacion` |
| Administration | `/configuracion`, `/configuracion/paises`, `/backups`, `/usuarios`, `/auditoria`, `/papelera`, `/alertas`, `/seguridad/dispositivos-mfa` |

The ids in the route list are the ones from the fixture (`TIT[0] = 00000000-0000-4000-8000-000000000021`,
`CTA[0] = 00000000-0000-4000-8000-000000000031`), so the detail screens have data to render.

Notes for future changes:

- `/cambiar-password` is only reachable with `primer_login=true` (`ProtectedRoute` with
  `allowPrimerLogin`), which would redirect every other screen. The mock exposes
  `setPrimerLoginMock(true/false)` in `mockApi.ts` and the harness turns it on just for that
  navigation.
- Any new screen needs its endpoints in `mockApi.ts`; unknown paths answer `{}` (and are listed
  in the run output), which usually renders an empty screen or an error boundary instead of the
  real page.
- `VISUAL_OUT` sets the folder for the screenshots (default `test-results/visual`).
  `VISUAL_FAIL` forces a 500 on one or more endpoints (comma separated) to capture error states.

```powershell
$env:VISUAL_OUT = "pw-visual"
npx playwright test e2e/visual/capture.spec.ts --reporter=list --output pw-visual-results
```

