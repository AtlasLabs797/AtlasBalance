# Auditoria de seguridad V-01.06

Fecha: 2026-05-12.

## Alcance

- Secretos y logs.
- Auth, MFA, cookies y CSRF.
- Permisos, BOLA/IDOR y RLS.
- Dependencias npm/NuGet.
- Docker y PostgreSQL local.
- CI/CD, release y firma.
- Instalador Windows, ACL y scripts operativos.
- Sinks de SQL, comandos, rutas, SSRF, XSS, TLS y errores.

## Hallazgos corregidos

| Severidad | Area | Hallazgo | Correccion |
| --- | --- | --- | --- |
| Alta | Logs/secretos | JWT y cookies en `logs/dev/atlas-frontend-dev.err.log`. | Logger redactor en Vite, consola Axios saneada y log local limpiado. |
| Alta | Permisos | `PuedeImportar`/write abria lectura financiera. | Lectura normal solo con `PuedeVerCuentas`; RLS con scopes firmados. |
| Alta | Supply chain | Release reutilizaba `node_modules` local y permitia `npm install`. | `npm ci` obligatorio y `package-lock.json` requerido. |
| Alta | Instalador | Secretos productivos en JSON sin ACL especifica fuerte. | ACL restrictiva en appsettings, PFX, DataProtection y credenciales one-shot. |
| Media | Auth | Rate limit solo IP+email permitia password spraying. | Contador adicional por IP. |
| Media | MFA | Trusted device automatico durante 90 dias. | Opt-in explicito y TTL 30 dias. |
| Media | Release | Firma opcional. | Firma obligatoria salvo `-AllowUnsignedLocal`. |
| Media | NuGet | Sin lockfiles. | `RestorePackagesWithLockFile`, lockfiles y CI `--locked-mode`. |
| Baja | Health | `/api/health` exponia PID, entorno, version y timestamps. | Respuesta publica reducida a estado. |
| Baja | CI | Secret scan incompleto y runtimes flotantes. | Runtimes fijados y patrones high-confidence ampliados. |
| Baja | Instalador SQL | Identificadores PostgreSQL interpolados. | Validacion estricta y quoting de identificadores. |
| Baja | Errores | stderr/rutas internas visibles a admins. | Mensajes operativos genericos; detalle solo en logs locales. |

## Verificacion

- `cyber-neo` secret scan: 0 findings.
- CI-style tracked secret scan: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list "Atlas Balance/backend/AtlasBalance.sln" package --vulnerable --include-transitive`: 0 vulnerabilidades.
- `dotnet restore "Atlas Balance/backend/AtlasBalance.sln" --locked-mode`: OK fuera del sandbox.
- `dotnet build "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore -p:UseAppHost=false -m:1`: OK.
- Tests no Docker: 34/34 OK.
- Verificacion posterior de hardening: suite backend completa con Docker/Testcontainers 225/225 OK.

## Bloqueos cerrados

- El bloqueo inicial de `RowLevelSecurityTests` se cerro el 2026-05-12: Docker fuera del sandbox responde `29.4.2`.
- La primera corrida completa fallo por una migracion RLS sin `.Designer.cs`; corregido el descriptor, la suite completa paso 225/225.

## Fuentes de criterio

- OWASP Top 10 2025 A01 Broken Access Control.
- OWASP Top 10 2025 A07 Authentication Failures.
- OWASP Top 10 2025 A08 Software or Data Integrity Failures.
- CWE-532 Insertion of Sensitive Information into Log File.
- CWE-798 Use of Hard-coded Credentials.
