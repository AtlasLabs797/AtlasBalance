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
- `E2E_API_HEALTH_URL`: defaults to `https://localhost:5000/api/health`.
- `E2E_SKIP_WEBSERVER=1`: use when Vite is already running and you do not want Playwright to manage it.

Do not run this with guessed credentials. The backend rate limit will lock the account after repeated failed logins.
