# Auditoria de seguridad - V-02-04

- **Fecha:** 2026-07-02
- **Alcance:** todo el codigo versionado — backend `AtlasBalance.API` (22 controladores, 4 middleware, servicios), `AtlasBalance.Watchdog`, frontend React, scripts y configuracion.
- **Metodologia:** cyber-neo (OWASP Top 10 2025 / CWE Top 25) con revision manual + `npm audit` + `dotnet list package --vulnerable` + barrido de secretos sobre `git ls-files`.
- **Resultado global:** 1 hallazgo MEDIUM-HIGH corregido, 1 LOW de robustez corregido, 0 criticos. Risk score residual: **0-5 (Low Risk)**. La base venia muy endurecida por las pasadas V-02-02/03/04.

---

## Hallazgos

### AB-SEC-001 — Logout no eliminaba las cookies de sesion en produccion (CORREGIDO)

- **Severidad:** MEDIUM-HIGH
- **CWE:** CWE-613 (Insufficient Session Expiration)
- **OWASP:** A07:2021/2025 (Identification and Authentication Failures)
- **Archivo:** `backend/src/AtlasBalance.API/Controllers/AuthController.cs` (`DeleteCookie`)
- **Descripcion:** desde V-02-03 las cookies de produccion llevan prefijo `__Host-atlas-*`, pero `AuthController.DeleteCookie` seguia borrando solo los nombres legacy (`access_token`, `refresh_token`, `csrf_token`, `mfa_trusted`). En produccion, `POST /api/auth/logout` revocaba el refresh token en servidor pero el navegador conservaba `__Host-atlas-access-token` (JWT valido hasta ~1h) y `__Host-atlas-csrf-token`. En una oficina con equipos compartidos (4-8 usuarios), otra persona podia seguir operando con la sesion "cerrada" hasta que caducara el access token. El mismo bug se habia corregido en `UserStateMiddleware.DeleteAuthCookies` pero se omitio `AuthController`.
- **Remediacion aplicada:** `DeleteCookie` borra el nombre real segun entorno (via `CookieName`) mas la variante legacy, con `Path=/` y `Secure` (requisitos del prefijo `__Host-`). Se conserva la politica V-01.07 de no borrar `mfa_trusted` en logout.
- **Regresion:** `AuthControllerTests.Logout_Should_Delete_HostPrefixed_Cookies_In_Production` (nuevo) + `Logout_Should_Keep_Trusted_Mfa_Cookie` (existente, sigue verde).

### AB-SEC-002 — NRE en `UserStateMiddleware.DeleteAuthCookies` sin `RequestServices` (CORREGIDO)

- **Severidad:** LOW (robustez; en el pipeline real `RequestServices` nunca es null)
- **CWE:** CWE-476 (NULL Pointer Dereference)
- **Archivo:** `backend/src/AtlasBalance.API/Middleware/UserStateMiddleware.cs`
- **Descripcion:** `context.RequestServices.GetService(...)` lanzaba `NullReferenceException` con `DefaultHttpContext` (tests unitarios). Rompia `InvokeAsync_Should_Reject_Token_When_SecurityStamp_Is_Stale`.
- **Remediacion aplicada:** acceso null-conditional (`?.`); con `RequestServices` null se asume produccion (borra ambas variantes de cookie), el comportamiento mas seguro.

---

## Revisado sin hallazgos (evidencia de cobertura)

### Autenticacion y sesion
- **Login/MFA/refresh (`AuthService`):** bcrypt work factor 12; rate limiting por email+IP (5/15min) y por IP (20/15min) con claves hasheadas; bloqueo de cuenta 30min tras 5 fallos; TOTP con anti-replay (`MfaLastAcceptedStep`), challenge en cache 5min ligado a IP, max 5 fallos por challenge/usuario; rotacion de refresh token con deteccion de reuso (revoca toda la familia y audita `RefreshTokenReuseDetected`); `pg_advisory_xact_lock` contra carreras de refresh; security stamp validado en cada request (`UserStateMiddleware`, comparacion tiempo constante); refresh exige MFA previa si la politica lo requiere; cambio de password revoca sesiones y rota stamp.
- **JWT:** HMAC-SHA256 con secreto validado en arranque (rechaza <32 chars, placeholders y `AllowedHosts` con `*` fuera de dev); issuer/audience/lifetime validados, `ClockSkew` cero; token solo en cookie httpOnly `SameSite=Strict` (+`__Host-` en prod); JWT suprimido para rutas de integracion.
- **CSRF:** doble token (cookie + header `X-CSRF-Token`) con `FixedTimeEquals`; exclusiones minimas (login/mfa/refresh/health) protegidas por `SameSite=Strict`.

### Autorizacion
- Los 22 controladores tienen `[Authorize]` a nivel de clase; ADMIN-only en usuarios, backups, configuracion, auditoria, sistema, divisas, tipos de cambio, integraciones, notificaciones y formatos de importacion. `IntegrationOpenClawController` protegido por `IntegrationAuthMiddleware` (bearer SHA-256 en BD, deny-by-default sin scopes, rate limit 100/min + 30/min para tokens invalidos por IP, auditoria por request con query redactada).
- `UsuariosController`: no permite quitarse el propio acceso admin, exige al menos un admin activo, valida coherencia cuenta/titular/pais de permisos y revoca sesiones al cambiar permisos/emails/password.
- Exportaciones/importacion validan scope por cuenta (`IUserAccessService`) en servidor.

### Ficheros y procesos
- **Descargas (backups/exportaciones):** ruta debe estar rooteada, con extension esperada (`.dump`/`.xlsx`) y dentro del root configurado (`GetFullPath` + prefijo con separador). Config de rutas (`ConfiguracionController`) rechaza `..` y rutas relativas.
- **Actualizaciones (`ActualizacionService`):** solo assets del repo oficial por HTTPS, digest SHA-256 obligatorio, firma RSA `.zip.sig` verificada contra clave publica configurada, limites de tamano (300MB zip / 1GB extraido / 10k entradas), extraccion anti zip-slip, `sourcePath` manual rechazado, paquete confinado a `UpdateSourceRoot`.
- **Procesos (`BackupService`/watchdog):** `ProcessStartInfo.ArgumentList` (sin shell, sin inyeccion), password via `PGPASSWORD` en entorno, timeouts y kill del arbol de procesos.
- **Watchdog:** Kestrel solo loopback (5001), header `X-Watchdog-Secret` en tiempo constante, rechazo de placeholders/secretos cortos en produccion.

### Inyeccion y salida
- **SQLi:** EF Core parametrizado; los unicos `ExecuteSqlRaw` son `pg_advisory_xact_lock({0})` parametrizados. Identificadores Postgres citados con `QuotePostgresIdentifier`.
- **XSS:** sin `dangerouslySetInnerHTML`/`innerHTML`/`eval` en el frontend; emails HTML con `EscapeHtml`; destinatarios via `MailboxAddress.Parse` (sin header injection).
- **SSRF:** clientes HTTP con `BaseAddress` fija (GitHub, exchangerate, OpenRouter/OpenAI/MiniMax, Google); URL de update check normalizada al repo oficial; watchdog forzado a loopback.

### Configuracion y secretos
- Secretos de BD cifrados con DataProtection (`enc:v1:`, DPAPI a maquina en prod, claves persistidas); API keys nunca devueltas al frontend (solo flag "configurada"); auditoria con `RedactSensitiveConfig`.
- Sin secretos versionados: `git ls-files` no contiene `.env`, `*.pem`, `*.key`, `*.pfx` ni `appsettings.Development.json` (solo `.env.example` y templates). Secretos de desarrollo fuera del repo (`%APPDATA%\AtlasBalance\dev-secrets`).
- Cabeceras: CSP restrictiva (`default-src 'self'`, `object-src 'none'`), HSTS, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, COOP. CORS solo en desarrollo. Hangfire dashboard solo en desarrollo. `ForwardedHeaders` con proxies/redes explicitos validados.
- Frontend: tokens solo en cookies httpOnly; CSRF token solo en memoria (Zustand, sin localStorage); axios `withCredentials` con refresh en cola acotada.

### Dependencias (SCA)
- `npm audit`: **0 vulnerabilidades** (292 dependencias, lockfile versionado).
- `dotnet list package --vulnerable --include-transitive`: **0 paquetes vulnerables** en API, Watchdog y Tests (lockfiles NuGet versionados).

---

## Observaciones menores (aceptadas, sin accion)

1. **Fallback de cookie legacy en produccion** (`Program.cs` OnMessageReceived y `ReadCookie`): se acepta `access_token` ademas de `__Host-atlas-access-token` como migracion. No es explotable (exige JWT firmado valido); puede retirarse en una version futura.
2. **CSP `style-src 'unsafe-inline'`:** requerido por los estilos inline de React/Recharts. Riesgo bajo con `script-src 'self'`.
3. **`GET /mfa/trusted-devices` lista tambien dispositivos revocados** (expone `RevokedAt` al propio usuario): comportamiento informativo, no un fallo.

## Verificacion

- Build backend OK (API + Watchdog, `OutDir` redirigido por ACL de `bin`).
- Tests (2026-07-02, tras arrancar Docker Desktop): **suite completa 323/323 OK**, incluidos
  `ExtractosConcurrencyTests` (409 de concurrencia) y `RowLevelSecurityTests` con Testcontainers.
