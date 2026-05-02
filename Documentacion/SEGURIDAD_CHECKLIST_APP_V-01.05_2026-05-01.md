# Checklist de seguridad aplicado a Atlas Balance - V-01.05

Fecha: 2026-05-01

## Veredicto

El checklist general si aplica a Atlas Balance en la parte web/backend, base de datos, Windows Service, actualizaciones, backups, secretos, CI/CD, dependencias, logging, permisos y respuesta a incidentes.

No aplican en esta version: app movil, IA integrada, RAG/vector database, pagos, cloud buckets, Kubernetes, trials/free tier y tool calls de IA.

## Modelo de amenazas

Datos tratados:

- Usuarios, roles, permisos y sesiones.
- Titulares, cuentas, IBAN/numeros de cuenta, saldos, extractos, alertas y exportaciones.
- Backups PostgreSQL y exports Excel.
- Tokens de integracion OpenClaw.
- Configuracion SMTP, API key de tipos de cambio y token opcional de GitHub para updates.

Atacantes relevantes:

- Usuario interno con permisos insuficientes intentando ver cuentas ajenas.
- Cuenta de admin comprometida.
- Cliente en LAN intentando abusar del login o de endpoints autenticados.
- Token de integracion filtrado.
- Paquete de update sustituido o manipulado.
- Persona con acceso al servidor intentando leer backups, logs o configuraciones locales.

Impacto de fuga o abuso:

- Exposicion de datos financieros internos.
- Manipulacion de extractos, permisos, alertas o usuarios.
- Restauracion o update malicioso.
- Perdida de trazabilidad de auditoria.

## Cambios aplicados

### Autenticacion y sesiones

- MFA TOTP obligatorio para usuarios web cuando `Security:RequireMfaForWebUsers=true`.
- El login ya no emite cookies JWT hasta validar MFA.
- Alta MFA por challenge temporal de 5 minutos y maximo 5 intentos.
- Secretos MFA guardados protegidos mediante `ISecretProtector`.
- Auditoria de `LOGIN_MFA_REQUIRED`, `MFA_ENABLED` y `MFA_VERIFIED`.
- Cambio de permisos, email o perfil de usuario rota `security_stamp` y revoca refresh tokens del usuario afectado.

### Actualizaciones

- Las actualizaciones descargadas desde GitHub Release ahora verifican `digest` SHA-256 del asset antes de extraer el ZIP.
- Si el digest falta o no coincide, el update se rechaza.
- Fuente usada: GitHub REST API expone `digest` en release assets: https://docs.github.com/en/rest/releases/assets?apiVersion=2022-11-28

### CI/CD y secretos

- CI mantiene npm audit y NuGet vulnerable audit.
- CI suma escaneo de patrones de secretos de alta confianza: private keys, AWS access keys, GitHub tokens, Slack tokens y Stripe live keys.
- Sigue recomendado usar GitHub secret scanning/Gitleaks como capa adicional si el repo lo permite.

### Tests y fiabilidad

- Tests de MFA, revocacion por cambio de permisos y digest de updates.
- Corregido test fragil de dashboard que generaba un movimiento futuro al ejecutarse en los primeros dias del mes.

## Controles ya existentes revisados

- Cookies `HttpOnly`, `Secure` fuera de desarrollo y `SameSite=Strict`.
- JWT valida firma, issuer, audience, expiracion y sin clock skew.
- CSRF en acciones autenticadas por header `X-CSRF-Token`.
- Cabeceras: CSP, HSTS fuera de desarrollo, `nosniff`, `SAMEORIGIN`, `Referrer-Policy`, `Permissions-Policy`.
- CORS solo en Development y restringido a `localhost:5173`.
- Rate limiting de login y de integracion OpenClaw.
- bcrypt con work factor 12.
- Refresh tokens hasheados, rotados y revocables.
- RLS PostgreSQL activo y forzado en tablas sensibles.
- Permisos server-side en endpoints de datos.
- Secretos SMTP/API key protegidos con Data Protection.
- `AllowedHosts=*` bloqueado fuera de Development.
- Rutas `backup_path` y `export_path` validadas como absolutas y sin traversal.
- Exportaciones descargables solo como `.xlsx` dentro de `export_path`.
- Hangfire dashboard solo en Development.
- `.env`, certificados, logs, appsettings locales y paquetes release fuera de Git.

## Pendientes que no se pueden cerrar solo con codigo

- Firmar binarios e instaladores Windows con certificado de firma de codigo.
- Firmar releases o publicar firma detached ademas del digest de GitHub.
- Cifrado en reposo de backups mediante BitLocker/EFS o herramienta aprobada en el servidor.
- Pentest antes de produccion con datos reales.
- Activar GitHub branch protection, reviews obligatorias, MFA de organizacion y secret scanning nativo.
- Revisar politica de privacidad, terminos y DPA solo si hay proveedor externo procesando datos personales.

## Estado P0 minimo antes de produccion

- Cubierto en codigo: HTTPS/HSTS, cookies seguras, JWT, CSRF, cabeceras, CORS, hashing password, rate limiting login/integracion, permisos backend, RLS, dependencias, secreto fuera de codigo, revocacion de sesiones, MFA, update digest.
- Cubierto por documentacion/proceso: modelo de amenazas, flujo de incidentes, manejo de secretos, alcance de datos.
- Pendiente operativo: firma de binarios, cifrado real de backups en disco, branch protection/secret scanning nativo y pentest.
