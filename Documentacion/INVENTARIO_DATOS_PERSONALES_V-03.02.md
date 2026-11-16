# Inventario de datos personales (PII) - Atlas Balance V-03.02

Fecha: 2026-11-16. Alcance: despliegue on-premise, 4-8 usuarios internos, tesoreria multi-banco.
Destinatario: responsable del tratamiento (quien decide fines y medios).

## Como leer este documento

- Regla "cita o omite": cada fila cita la entidad/campo en codigo. Si no pude verificar algo leyendo el codigo, no esta en las tablas: esta en la seccion 7.
- Rutas de codigo relativas a `Atlas Balance/backend/src/AtlasBalance.API/` (abreviado `API/`). Entidades: `API/Models/Entities.cs`. Tablas: `API/Data/AppDbContext.cs`.
- Roles: ADMIN, GERENTE, EMPLEADO (`API/Models/Enums.cs`). El acceso a cuentas/extractos se acota por `PERMISOS_USUARIO` (scope por cuenta, titular o pais).
- "Cifrado en reposo" se refiere solo a lo que hace la aplicacion. El cifrado de disco o de la base de datos no esta en el codigo (ver seccion 7).
- Documento escrito leyendo codigo; no se ha ejecutado nada ni consultado una base real.

---

## 1. Usuarios y credenciales

| Dato | Donde se recoge | Donde se guarda | Quien accede | Cifrado en reposo | Transmision | Retencion / borrado |
|---|---|---|---|---|---|---|
| Email de acceso | `POST /api/usuarios` y `PUT /api/usuarios/{id}` (`UsuariosController`); login `POST /api/auth/login` | `USUARIOS.email` (`Usuario.Email`) | ADMIN (`[Authorize(Roles="ADMIN")]` en `UsuariosController`); el propio usuario via `GET /api/auth/me` | No (texto plano) | HTTPS (ver nota T) | Soft delete: `DELETE /api/usuarios/{id}` pone `deleted_at`, desactiva y revoca sesiones; no borra la fila. Restaurable con `POST /api/usuarios/{id}/restaurar` |
| Nombre completo | Igual que email | `USUARIOS.nombre_completo` (`Usuario.NombreCompleto`) | ADMIN; el propio usuario | No | HTTPS | Igual que email |
| Emails adicionales (destino de alertas) | `POST /api/usuarios/{id}/emails` | `USUARIO_EMAILS.email` (`UsuarioEmail.Email`) | ADMIN | No | HTTPS; despues se usan como destinatarios SMTP (seccion 4) | `DELETE /api/usuarios/{id}/emails/{emailId}`. No he verificado si es borrado fisico (seccion 7) |
| Hash de contrasena | Alta/edicion de usuario, `PUT /api/auth/cambiar-password` | `USUARIOS.password_hash` (`Usuario.PasswordHash`) | Solo el backend. Se redacta como `[REDACTED]` en auditoria (`AuditSaveChangesInterceptor.ColumnasSecretas`) | Hash BCrypt, work factor 12 (`AuthService`, `PasswordWorkFactor = 12`). No es cifrado reversible | HTTPS (la contrasena viaja en el login) | Con la fila de usuario |
| Secreto TOTP (MFA) | Alta MFA en `POST /api/auth/mfa/verify` (`AuthService`, rama `SetupRequired`) | `USUARIOS.mfa_secret` (`Usuario.MfaSecret`) | Solo el backend. Redactado en auditoria | Si: ASP.NET Data Protection con HMAC, formato `enc:v2:` (`API/Services/SecretProtector.cs`, `AuthService.cs:434`). Claves en `%ProgramData%\AtlasBalance` protegidas con DPAPI LocalMachine en Windows (`Program.cs`, `ProtectKeysWithDpapi`) | HTTPS | Con la fila de usuario. `POST /api/usuarios/{id}/mfa/revocar` lo revoca |
| Intentos fallidos, bloqueo, fechas de login/cambio de contrasena | Login | `USUARIOS.failed_login_attempts`, `locked_until`, `fecha_ultima_login`, `password_changed_at`, `mfa_last_accepted_step` | ADMIN | No | n/a | Con la fila de usuario |
| Hash del refresh token | Login / refresh | `REFRESH_TOKENS.token_hash` (`RefreshToken.TokenHash`) | Solo backend | SHA-256 del token (`AuthService.ComputeSha256`); no se guarda el token en claro | HTTPS; cookie | `LimpiezaRefreshTokensJob` (diario) borra filas caducadas (`expira_en <= now`) o revocadas hace mas de 1 dia: hard delete (`ExecuteDeleteAsync`). Vigencia 7 dias (`JwtSettings:RefreshTokenExpDays`) |
| IP de la sesion | Login / refresh (`AuthController`) | `REFRESH_TOKENS.ip_address` (`RefreshToken.IpAddress`, tipo `inet`) | Solo backend (se usa para detectar cambio de IP) | No | n/a | Igual que el refresh token |
| Id de sesion | Login | `REFRESH_TOKENS.session_id` y `AUDITORIAS.session_id` | Backend; ADMIN lo ve en auditoria | No | n/a | Segun cada tabla |
| Dispositivo MFA de confianza: hash del token | `POST /api/auth/mfa/verify` (si el recuerdo de dispositivo esta activo) | `MFA_TRUSTED_DEVICES.token_hash` | Backend | SHA-256 | Cookie `mfa_trusted` | Vigencia 90 dias (`SecurityConfigurationDefaults.MfaRememberDeviceDays`). Revocacion: `DELETE /api/auth/mfa/trusted-devices[/{id}]` solo pone `revoked_at` (`AuthService.cs:725-760`). No encontre job que borre filas caducadas (seccion 7). **Corregido 2026-11-16:** `LimpiezaMfaTrustedDevicesJob` (diario) borra filas caducadas o revocadas hace mas de 1 dia (seccion 7, punto 2) |
| Dispositivo MFA de confianza: User-Agent e IP | Mismo flujo | `MFA_TRUSTED_DEVICES.user_agent_summary` (max 256) y `ip_address_summary` (max 128) (`AuthService.SummarizeUserAgent/SummarizeIp`) | El propio usuario via `GET /api/auth/mfa/trusted-devices`; ADMIN por BD | No | HTTPS | Sin purga verificada. **Corregido 2026-11-16:** el mismo job borra la fila completa (caducada o revocada hace mas de 1 dia) |
| Permisos y preferencias | `PUT /api/usuarios/{id}/permisos`, `PUT /api/extractos/columnas-visibles` | `PERMISOS_USUARIO.*`, `PREFERENCIAS_USUARIO_CUENTA.*`, `USUARIOS.puede_usar_ia` | ADMIN (permisos); el usuario (preferencias) | No | HTTPS | Con el usuario; `PREFERENCIAS_USUARIO_CUENTA` tiene FK en cascada al usuario (`AppDbContext.cs:374`), pero el usuario no se borra fisicamente |

Nota T (transmision): la app fuerza HTTPS fuera de desarrollo con `HttpsRedirectionMiddleware` (`Security:HttpsRedirect`, por defecto activo). En modo reverse-proxy solo redirige si el proxy envia `X-Forwarded-Proto`; exigir TLS en el proxy es requisito del operador (comentario del propio middleware). La conexion a PostgreSQL remota sin TLS se bloquea al arrancar salvo `Security:AllowInsecureDatabaseTransport` (`DatabaseTlsPolicy.cs`); en loopback no hace falta.

---

## 2. Titulares, cuentas y extractos

| Dato | Donde se recoge | Donde se guarda | Quien accede | Cifrado en reposo | Transmision | Retencion / borrado |
|---|---|---|---|---|---|---|
| Nombre del titular (empresa, particular o autonomo) | `POST/PUT /api/titulares` (`TitularesController`, solo ADMIN; `SaveTitularRequest`) | `TITULARES.nombre` (`Titular.Nombre`), tipo en `Titular.Tipo` | Lectura: cualquier usuario con scope sobre alguna cuenta del titular (`TitularesController` con `[Authorize]`); escritura: ADMIN | No | HTTPS | Soft delete (`DELETE /api/titulares/{id}`, `deleted_at`); restaurable |
| Notas del titular (texto libre) | Mismo endpoint, `Notas` (max 4000) | `TITULARES.notas` | Igual | No | HTTPS | Igual |
| Identificacion, email y telefono del titular | **Ningun endpoint los recoge**: `SaveTitularRequest` y las respuestas de `TitularesDtos.cs` no los incluyen | Columnas `TITULARES.identificacion`, `contacto_email`, `contacto_telefono` (`Titular.Identificacion`, `ContactoEmail`, `ContactoTelefono`) | Nadie via API. Solo por acceso directo a BD | No | n/a | Comentario del codigo (V-02-07): "sin uso funcional; pendiente decidir su borrado definitivo". Pueden existir valores heredados en BD; ver seccion 7 |
| Nombre de la cuenta, banco, pais, notas | `POST/PUT /api/cuentas` (`CuentasController`, ADMIN) | `CUENTAS.nombre`, `banco_nombre`, `pais_id`, `notas` | Lectura por scope (`UserAccessService.CanAccessCuentaAsync`); escritura ADMIN | No | HTTPS | Soft delete (`DELETE /api/cuentas/{id}`) |
| IBAN y numero de cuenta | Mismo endpoint; IBAN validado por `API/Validation/IbanValidator.cs` | `CUENTAS.iban`, `CUENTAS.numero_cuenta` (`Cuenta.Iban`, `Cuenta.NumeroCuenta`) | Listados y resumen: enmascarados (`PiiMasking.MaskIban/MaskNumeroCuenta`, deja 4 visibles; `CuentasController.cs:149-150,305`). **`GET /api/cuentas/{id}` devuelve IBAN y numero completos** a cualquier usuario con scope sobre esa cuenta (`CuentasController.cs:214-215`), no solo ADMIN. **Corregido 2026-11-16 (IBAN):** solo ADMIN recibe el IBAN completo; el resto lo ve enmascarado. `numero_cuenta` sigue completo para no-ADMIN (pendiente) | No | HTTPS | Soft delete con la cuenta |
| Fecha, concepto, comentarios, importe, saldo del movimiento | `POST/PUT /api/extractos`, importacion `POST /api/importacion/validar|lotes|confirmar` | `EXTRACTOS.fecha`, `concepto`, `comentarios`, `monto`, `saldo` (`Extracto.*`) | Usuarios con scope y permiso (`PermisoUsuario.PuedeVerCuentas`, etc.) | No | HTTPS | Soft delete (`DELETE /api/extractos/{id}`). El concepto es texto libre y puede contener nombres de terceros, IBAN o referencias (limite `ExtractoLimits.ConceptoMaxLength`) |
| Nota de alerta de la linea | Endpoint exacto no verificado (campo `Extracto.FlaggedNota`; ver `ExtractosController`/`RevisionController`) | `EXTRACTOS.flagged_nota` | Usuarios con scope | No | HTTPS | Soft delete con la fila |
| Columnas extra importadas (cualquier columna del banco) | Importacion | `EXTRACTOS_COLUMNAS_EXTRA.nombre_columna`, `valor` | Usuarios con scope | No | HTTPS | Soft delete |
| Desglose por tercero (nombre del tercero, importe, notas) | `PUT /api/extractos/{id}/desglose` | `EXTRACTOS_DESGLOSES.tercero_nombre`, `importe`, `notas` (`ExtractoDesglose`) | Usuarios con scope y permiso de edicion | No | HTTPS | Soft delete |
| Movimientos esperados y conciliaciones (referencia, concepto, observacion) | `ConciliacionController` | `MOVIMIENTOS_ESPERADOS.referencia/concepto`, `CONCILIACIONES.observacion` | Usuarios con permiso `PuedeConciliar` | No | HTTPS | Soft delete |
| Pegado bruto del extracto importado | `POST /api/importacion/lotes` | `IMPORTACION_LOTES.contenido_original` (truncado a 2 KB, `ImportacionService.cs:1522`) y `nombre_archivo` | Usuarios con permiso `PuedeImportar` | No | HTTPS | `LimpiezaExportacionesJob` vacia `contenido_original` a los 180 dias (clave `importacion_contenido_retention_days`, rango 1-3650). `nombre_archivo` no se limpia |
| Filas parseadas del lote de importacion | Mismo flujo | `IMPORTACION_LOTE_FILAS.datos_json` (`ImportacionLoteFila.DatosJson`, fila completa en JSON) | Usuarios con permiso de importar | No | HTTPS | No encontre purga por retencion para esta tabla (seccion 7). **Corregido 2026-11-16 (parcial):** `LimpiezaImportacionLotesJob` vacia `datos_json` de lotes cerrados con mas de 180 dias; lotes abiertos y filas soft-deleted no se tocan (seccion 7, punto 3) |

---

## 3. Auditoria y logs

| Dato | Donde se recoge | Donde se guarda | Quien accede | Cifrado en reposo | Transmision | Retencion / borrado |
|---|---|---|---|---|---|---|
| IP de origen de cada accion auditada | `AuditService.LogAsync` (todos los controladores) y `AuditSaveChangesInterceptor` | `AUDITORIAS.ip_address` (`Auditoria.IpAddress`, `inet`) | Solo ADMIN: `AuditoriaController` (`GET /api/auditoria`, `/exportar-csv`) | No. Las filas llevan firma HMAC-SHA256 (`AUDITORIAS.firma`, `AuditSigner`) que detecta manipulacion, no cifra | n/a | 365 dias por defecto (`LimpiezaAuditoriaJob.RetentionDays`, `Auditoria:RetentionDays`). Hard delete via `atlas_security.purgar_auditorias()`. El trigger append-only impide UPDATE y DELETE de filas de menos de 90 dias (`Migrations/20260730090000_V0207AuditoriaAppendOnly.cs`, `MinRetentionDays = 90`) |
| User-Agent (max 256) y session_id | Igual | `AUDITORIAS.user_agent`, `session_id` (`AuditRequestContext`) | Solo ADMIN | No | n/a | Igual |
| Usuario que actua y tipo de accion | Igual | `AUDITORIAS.usuario_id`, `tipo_accion`, `entidad_tipo`, `entidad_id` | Solo ADMIN | No | n/a | Igual. FK a `USUARIOS` con `Restrict` (`AppDbContext.cs:432`): impide borrar fisicamente un usuario con auditoria |
| Valores anteriores y nuevos de entidades (email y nombre de usuario, nombre de titular, IBAN, concepto, comentarios, importes...) | `AuditSaveChangesInterceptor`: audita altas, cambios y bajas de `Usuario`, `Titular`, `Cuenta`, `Extracto`, `ExtractoDesglose`, etc. | `AUDITORIAS.detalles_json` (tipo `json`, truncado a 32 KB) con `old` y `new` de **todas las propiedades** salvo `PasswordHash`, `MfaSecret`, `TokenHash`, `RefreshToken`, `EndpointScopesJson` y `Configuracion.Valor` de secretos, que salen como `[REDACTED]` | Solo ADMIN | No | n/a | Igual que arriba. Consecuencia: el IBAN completo, el nombre y los conceptos quedan copiados en auditoria durante 365 dias y no se pueden editar (append-only). **Corregido 2026-11-16 (IBAN):** las filas nuevas guardan el IBAN enmascarado antes de firmar, y un cambio de IBAN anade `IbanCambiado: true`. Nombre y concepto siguen completos (decision del usuario). Filas anteriores sin tocar: caducan a los 365 dias |
| Email en eventos de login | `AuthService.LoginAsync` | `AUDITORIAS.detalles_json` (`{ "email": ... }`) incluso con email inexistente | Solo ADMIN | No | Los avisos de seguridad pueden reenviar el email por SMTP/Slack (seccion 4) | Igual |
| Peticiones de la integracion externa | `IntegrationAuthMiddleware` | `AUDITORIA_INTEGRACIONES.endpoint`, `parametros`, `ip_address` (`AuditoriaIntegracion`); `INTEGRATION_TOKENS.last_used_ip_address` | ADMIN | No. Los parametros pasan por `RedactQueryValue` | HTTPS | 28 dias (`LimpiezaAuditoriaJob.IntegrationRetentionDays`), suelo de BD 7 dias. `INTEGRATION_TOKENS` se conserva (soft delete) |
| Espejo de eventos de seguridad: fichero | `SecurityEventLog` (`API/Logging/SecurityEventLog.cs`) | Fichero Serilog `atlas-security-.log` (por defecto `%ProgramData%\AtlasBalance\logs\security\`) con `usuario`, `entidad`, `ip`, `origen`, `sesion`, `ua`. No incluye `detalles_json` | Quien tenga acceso al sistema de ficheros del servidor | No | n/a | Rotacion diaria, 400 ficheros (`Program.cs`, `retainedFileCountLimit: 400`), mas que la auditoria a proposito |
| Espejo de eventos de seguridad: Windows Event Log | `SecurityEventLog.EscribirEnEventLog` | Log "Application", origen `AtlasBalance` | Administradores del servidor Windows | No | n/a | Segun la politica del Event Log de Windows (no configurada por la app, verificado solo que existe `Security:MirrorToWindowsEventLog`, por defecto true) |
| Log de aplicacion | Serilog (`Program.cs`) | `atlas-balance-.log` (por defecto `%ProgramData%\AtlasBalance\logs\`) | Acceso al sistema de ficheros | No | n/a | Rotacion diaria, 30 ficheros, 50 MB por fichero. Los logs con datos de usuario pasan por `LogScrubber.Scrub` (quita saltos de linea y corta a 256) y, en algunos sitios, `LogScrubber.RedactPii` (`ConfiguracionController.cs:349`, UA del espejo) |
| Errores del frontend | `POST /api/telemetria/errores` (anonimo, con cuota por IP) | Log de aplicacion: mensaje, ruta, stack e IP (`TelemetriaController`) | Acceso al sistema de ficheros | No | HTTPS | 30 dias (log de aplicacion). La IP va con `Scrub`, sin `RedactPii` |

---

## 4. Integraciones y terceros (datos que salen del servidor)

El servidor llama a hosts externos configurados en `Program.cs` (`AddHttpClient`, lineas 262-320) y en los servicios indicados. Salvo SMTP (host definido por el operador), todos son hosts fijos.

### 4.1 Proveedores de IA (OpenRouter, OpenAI, MiniMax)

- Activacion: solo si ADMIN configura clave y modelo y el usuario tiene `USUARIOS.puede_usar_ia`. Endpoint de uso: `POST /api/ia/chat` (`IaController`, `[Authorize]`).
- Hosts: `openrouter.ai`, `api.openai.com`, `api.minimax.io` (`Program.cs:289-319`, `AtlasAiService.ProviderHostName`).
- Que sale: la pregunta del usuario y un contexto financiero (resumen de periodo, saldos, y hasta N movimientos relevantes con fecha, titular, cuenta, divisa, importe, saldo y concepto; cada texto recortado a 180 caracteres) (`AtlasAiService.BuildFinancialContextAsync`, `SanitizeContextText`).
- Como se seudonimiza, antes de salir (`AtlasAiService.cs:324-355`, fail-closed):
  - `AiPseudonymMap` (`API/Services/AiPseudonymizer.cs`) sustituye nombres de titulares, cuentas y terceros del scope del usuario por `[TITULAR_n]`, `[CUENTA_n]`, `[TERCERO_n]`. Reversible: la respuesta se des-seudonimiza en el servidor (`pseudonyms.Reverse`). Ignora nombres de menos de 3 caracteres.
  - `DlpScrubber` (`API/Services/IaPlanner/DlpScrubber.cs`) redacta por patron: IBAN, email, telefono espanol, DNI/NIF/NIE, CIF, tarjeta, BIC. `RedactIbanLike` redacta IBAN en el contexto.
  - Si el regex agota el tiempo o el DLP falla, no se envia nada (`dlp_fail_closed`, `pseudonymization_timeout`).
- Que NO se seudonimiza: los importes, saldos, fechas y divisas salen en claro. El concepto libre solo se protege si contiene un nombre del mapa o coincide con algun patron del DLP; un nombre de persona que no sea titular, cuenta ni tercero registrado pasa tal cual. El DLP no cubre nombres propios arbitrarios (los patrones son los listados arriba).
- OpenRouter: para modelos de pago se envia `provider: { zdr: true, data_collection: "deny" }` (`AtlasAiService.OpenRouterPrivacyProvider`). Para modelos gratuitos y `openrouter/free` esa restriccion se omite por incompatibilidad de endpoints (comentario en `BuildProviderRequest`); ahi solo queda la seudonimizacion. No hay clausula equivalente en el codigo para OpenAI ni MiniMax.
- Que se guarda en local: la auditoria `ia_consulta` guarda solo longitudes, tokens, coste y numero de entidades seudonimizadas, no la pregunta ni la respuesta (`AtlasAiService.cs:458-486`; `IaAuditSchema.FragmentosProhibidos` lo protege con tests). La memoria conversacional es solo en RAM con TTL de 30 min (`InMemoryConversationMemory`).
- Clave API del proveedor: `CONFIGURACION` con `es_secreto`, cifrada con `SecretProtector` (`ConfiguracionRepository.cs`).

### 4.2 Google Drive (copias de backup)

- Activacion: ADMIN vincula la cuenta via OAuth (`POST /api/backups/google-drive/link/start`, `BackupsController`, solo ADMIN). Alcance pedido: `drive.file openid email` (`GoogleDriveBackupService.cs:76`).
- Que sale: el volcado de PostgreSQL completo, cifrado antes de subir con AES-256-GCM por bloques de 1 MB (`BackupEncryptionService`, formato `ATLASBKP2`). La clave (32 bytes) esta en `CONFIGURACION.backup_cloud_encryption_key`, protegida con `SecretProtector`; nunca se regenera si ya existe. El nombre del fichero remoto es `<nombre del backup>.enc` (`GoogleDriveBackupService.cs:285`).
- Datos personales que se guardan en local por esta integracion: `BACKUP_CLOUD_CONNECTIONS.account_email` (email de la cuenta Google) y `refresh_token` (cifrado con `SecretProtector`, `GoogleDriveBackupService.cs:571`).
- Retencion remota: `BackupService.ApplyRetentionAsync` borra tambien la copia remota al caducar el backup local (6 semanas por defecto).
- Dato que Google recibe en claro: el tamano, el nombre del fichero y los metadatos de la cuenta; el contenido va cifrado.

### 4.3 SMTP

- Servidor definido por el operador en `CONFIGURACION` (`smtp_host`, `smtp_user`, `smtp_password` secreto, `smtp_from`). TLS: `SslOnConnect`, `StartTls` o `StartTlsWhenAvailable` segun puerto/ajuste (`EmailService.cs:257-262`). El ultimo modo permite caer a texto plano si el servidor no ofrece STARTTLS (comportamiento del enum de MailKit; revisar la configuracion real).
- Que sale (`EmailService.cs`): alerta de saldo bajo con nombre de titular, nombre de cuenta, saldo, saldo minimo y **ultimo concepto del movimiento** (texto libre); aviso de plazo fijo; alerta de seguridad con regla, severidad, resumen y detalles, que incluyen el email de la cuenta atacada e IPs implicadas (`SecurityAlertService.cs:148-166`); correo de prueba.
- Destinatarios: `USUARIOS.email` y `USUARIO_EMAILS.email` de los usuarios marcados como destinatarios (`AlertaService.cs:320-340`).

### 4.4 Slack (webhook de alertas)

- Opt-in: solo si `Security:Alertas:SlackWebhookUrl` esta configurado y es `https://hooks.slack.com/...` (`SlackAlertNotifier.cs`). Sin configurar, no sale nada. La URL es secreto y vive en appsettings de produccion.
- Que sale: titulo de la regla, severidad, resumen y detalles de la alerta de seguridad. Segun las reglas de `SecurityAlertService`, eso incluye email del usuario afectado, ids de usuario e IPs.

### 4.5 Tipo de cambio

- Host `v6.exchangerate-api.com` (`Program.cs:264`). La clave API va en la ruta de la URL (`TiposCambioService.cs:415`) con la divisa base. No sale ningun dato personal ni de cuentas; si la IP publica del servidor.
- Clave API: `CONFIGURACION.exchange_rate_api_key`, secreta.

### 4.6 Actualizaciones (GitHub)

- Hosts `api.github.com` y `github.com` (`ActualizacionService.cs`, validacion de host en lineas 323, 463). Se envia `User-Agent: AtlasBalance/<version>` y, si esta configurado, `github_update_token` como Bearer. No salen datos de usuarios ni financieros; GitHub ve la IP publica del servidor y la version instalada.

### 4.7 Integracion externa OpenClaw (cliente de confianza, no es un servidor de terceros hosteado por Atlas)

- Endpoints `api/integration/openclaw/*` (`IntegrationOpenClawController.cs`), autenticados con bearer token (`INTEGRATION_TOKENS`, guardado como hash).
- Nombres de titular y cuenta se devuelven como seudonimos opacos `TITULAR-xxxxxxxx` y `CUENTA-xxxxxxxx` derivados del GUID (`API/Services/IntegrationPseudonyms.cs`); el GUID completo viaja igualmente en `id`, y existe `POST /api/integration/openclaw/resolver-nombres` para resolverlos bajo demanda con el scope del token.
- Salen sin seudonimizar: `concepto` y `comentarios` de cada extracto (`IntegrationOpenClawController.cs:332-345`, con comentario explicito del propio codigo). Los IBAN no aparecen en esos endpoints.
- Quien es el destino real de esa integracion (que sistema consume) no se deduce del codigo; ver seccion 7.

---

## 5. Backups y exportaciones

| Dato | Donde se recoge | Donde se guarda | Quien accede | Cifrado en reposo | Transmision | Retencion / borrado |
|---|---|---|---|---|---|---|
| Volcado completo de la base (todas las tablas anteriores) | `POST /api/backups/manual`, `BackupSchedulerJob` | Fichero `pg_dump -F c` en la ruta `backup_path` (por defecto `C:\atlas-balance\backups`) y fila en `BACKUPS` (`BackupService.RunPgDumpAsync`) | Solo ADMIN (`BackupsController`) y quien acceda al disco | **No hay cifrado a nivel de aplicacion en la copia local**: `EncryptAsync` solo se invoca en la subida a Drive (`GoogleDriveBackupService.cs:283`) | n/a | `backup_retention_weeks`, 6 por defecto (rango 1-52). Borra el fichero fisico, pone `deleted_at` en `BACKUPS` y borra la copia remota (`BackupService.ApplyRetentionAsync`). Rutas fuera de la raiz permitida se omiten |
| Copia cifrada en Google Drive | Subida automatica o `POST /api/backups/{id}/google-drive/retry` | Google Drive; metadatos en `BACKUP_CLOUD_COPIES` | ADMIN | AES-256-GCM (ver 4.2) | HTTPS a Google | Igual que el backup local |
| Exportacion mensual/manual a Excel | `POST /api/exportaciones/manual` (ADMIN, GERENTE), `ExportMensualJob` | `.xlsx` en `export_path` (por defecto `C:\atlas-balance\exports`); fila en `EXPORTACIONES` (`ExportacionService`) | Descarga `GET /api/exportaciones/{id}/descargar` con scope de cuenta | **No**: xlsx sin contrasena (no hay codigo de cifrado en `ExportacionService`). El nombre del fichero incluye nombre de titular y de cuenta (`ExportacionService.cs:46-51`). Contiene nro. fila, fecha, concepto, monto, saldo, revisada, alerta y nota, y columnas extra | HTTPS en la descarga | `LimpiezaExportacionesJob`: 90 dias por defecto (`exportacion_retention_days`, 1-3650). Borra el fichero fisico y pone `deleted_at` |
| Exportacion CSV de auditoria | `GET /api/auditoria/exportar-csv` (ADMIN) | Se genera en la respuesta; no se guarda en servidor segun lo leido | ADMIN | n/a | HTTPS | Lo que haga el ADMIN con el fichero descargado |

---

## 6. Supresion: tension entre soft delete universal y derecho de supresion

### 6.1 La tension, citada

`AGENTS.md` (secciones de backend, lineas 208 y 245) exige "Soft delete universal: `deleted_at` + `deleted_by_id` en todas las entidades" y filtrar `deleted_at IS NULL`. Eso significa que los endpoints `DELETE` (`UsuariosController.Eliminar`, `TitularesController.Eliminar`, `CuentasController.Eliminar`, `ExtractosController`) **ocultan** los datos pero no los suprimen: la fila, el nombre, el email o el concepto siguen en la base, en los backups y en la auditoria.

Excepciones reales de borrado fisico verificadas en codigo:
- `AUDITORIAS` y `AUDITORIA_INTEGRACIONES`: por retencion (365 / 28 dias), via funciones `atlas_security.purgar_*` (excepcion declarada en el comentario de `LimpiezaAuditoriaJob`).
- `REFRESH_TOKENS`: por caducidad (`LimpiezaRefreshTokensJob`).
- Ficheros `.xlsx` y backups locales y remotos: borrado fisico a los 90 dias y 6 semanas por defecto, pero la fila se queda con `deleted_at`.
- `IMPORTACION_LOTES.contenido_original`: se vacia a 180 dias.

No existe en el codigo ninguna funcion de supresion o anonimizacion por persona (busque "anonimiz", "supresion", "erasure" en `backend/src` y `scripts`; sin resultados utiles).

### 6.2 Lo que existe hoy: `Purge-DeliveryData`

`Atlas Balance/scripts/Purge-DeliveryData.ps1` + `purge-delivery-data.sql`. No es un procedimiento de supresion por persona. Es una purga total de una base antes de entregar o publicar:
- Exige `-ConfirmDeliveryPurge` o `ATLAS_CONFIRM_DELIVERY_PURGE=BORRAR_DATOS`.
- Copia el SQL al contenedor Docker `atlas_balance_db` y lo ejecuta con `psql` como `postgres` (parametros por defecto; no sirve tal cual para una instalacion nativa en Windows).
- Desactiva RLS, vacia claves de configuracion (SMTP, API de IA, Drive, clave de cifrado de backups), hace `DELETE` de usuarios, titulares, cuentas, extractos, desgloses, tokens, auditorias, backups y copias cloud, trunca Hangfire y reactiva RLS.
- Limites leidos en el script, sin ejecutarlo:
  - No toca ficheros en disco (backups `.dump`, exports `.xlsx`, logs) ni borra en Google Drive; solo borra las filas.
  - La lista de tablas no incluye `MFA_TRUSTED_DEVICES`, `IMPORTACION_LOTES`, `IMPORTACION_LOTE_FILAS`, `MOVIMIENTOS_ESPERADOS`, `CONCILIACIONES` (el script no las nombra). Con FKs `Restrict` hacia `USUARIOS` (`AppDbContext.cs:107,124`), el `DELETE FROM "USUARIOS"` podria fallar si hay filas en esas tablas. No lo he probado.
  - El `DELETE FROM "AUDITORIAS"` choca con el trigger append-only (bloquea borrar filas de menos de 90 dias y sin la marca `atlas.audit_purge`). El script no desactiva el trigger. Puede fallar con auditoria reciente; no lo he probado.
- Uso apropiado: solo para vaciar un entorno completo (entrega, fin de contrato). No usar para atender una peticion individual.

### 6.3 Procedimiento propuesto (solo texto; no implementado, no ejecutado)

Esto es una propuesta para que el responsable de tratamiento la valide. No es un hecho del sistema.

1. Registrar la peticion: quien, fecha, interesado, que datos, base legal. Verificar la identidad del solicitante.
2. Valoracion previa: hay conservacion obligatoria (contable, fiscal, auditoria) que justifique negar o limitar la supresion. En tesoreria, los extractos y movimientos suelen tener obligaciones de conservacion. Decision documentada por el responsable, no por el sistema.
3. Localizar los datos del interesado con consultas de solo lectura sobre la base (`SELECT`), por tabla y campo:
   - Si es usuario interno: `USUARIOS` (email, nombre), `USUARIO_EMAILS`, `MFA_TRUSTED_DEVICES`, `REFRESH_TOKENS`, `PERMISOS_USUARIO`, `PREFERENCIAS_USUARIO_CUENTA`, `IA_USO_USUARIOS`, y sus ids en `AUDITORIAS.usuario_id` y en `detalles_json`.
   - Si es titular particular o autonomo: `TITULARES.nombre`, `notas`, columnas heredadas `identificacion/contacto_*`; `CUENTAS.nombre/iban/numero_cuenta`; `EXTRACTOS.concepto/comentarios/flagged_nota`; `EXTRACTOS_DESGLOSES.tercero_nombre`; `EXTRACTOS_COLUMNAS_EXTRA.valor`; `IMPORTACION_LOTES.nombre_archivo`; `IMPORTACION_LOTE_FILAS.datos_json`; `MOVIMIENTOS_ESPERADOS`.
4. Elegir por cada tabla entre anonimizar (sustituir por valor neutro, conservando importes y fechas para la contabilidad) o borrar (solo si no hay obligacion de conservar). Para usuarios internos, anonimizar `email` y `nombre_completo` en lugar de borrar la fila, porque `AUDITORIAS` tiene FK `Restrict`.
5. Ejecutar los cambios con un DBA, en transaccion, con copia de seguridad previa, usando una conexion owner (el script de purga ya documenta que RLS exige owner). Revisar RLS: en cuentas con scope, el `UPDATE` hay que hacerlo con rol que lo atraviese.
6. Datos que no se pueden alcanzar con `UPDATE` normal y requieren decision explicita:
   - `AUDITORIAS`: append-only; `ip_address`, `user_agent` y el `detalles_json` con email/nombre/IBAN/concepto caducan solos a los 365 dias. Alternativas: esperar al vencimiento (documentarlo al interesado) o reducir `Auditoria:RetentionDays` (nunca por debajo de 90, suelo de BD). No manipular filas firmadas: rompe la verificacion de integridad (`AuditIntegrityService`).
   - Backups locales y copias en Drive: los volcados antiguos conservan el dato hasta que caduquen (6 semanas por defecto). Si se pide supresion efectiva antes, borrar manualmente los backups afectados y sus copias remotas, y dejarlo anotado. Al restaurar un backup anterior hay que volver a aplicar la supresion.
   - Exportaciones `.xlsx` ya generadas: borrar a mano los ficheros afectados de `export_path` (si no, caducan a 90 dias).
   - Logs: ficheros de aplicacion (30 dias), de seguridad (hasta 400 dias) y Windows Event Log. La IP y el id de usuario estan en el log de seguridad; no hay mecanismo de edicion, solo caducidad.
7. Verificar con las mismas consultas del paso 3 que no queda el dato donde se decidio suprimirlo, y revisar el resultado de la siguiente retencion de cada job.
8. Responder al interesado en plazo y archivar la evidencia (sin conservar el dato suprimido).
9. Mejora recomendada al producto, a decidir: una operacion de administracion "anonimizar usuario" y "anonimizar titular" que cubra tablas, y un job que purgue `MFA_TRUSTED_DEVICES` caducados. Hecho 2026-11-16: `LimpiezaMfaTrustedDevicesJob` (seccion 7, punto 2).

---

## 7. No verificado / pendientes

Cosas que no incluyo como hecho porque no pude confirmarlas leyendo el codigo, o que el codigo deja sin resolver:

1. Cifrado de disco/BD: el codigo no cifra la base de datos ni el disco de `backup_path`, `export_path` ni los logs. Si hay BitLocker o cifrado de PostgreSQL, esta fuera del repositorio.
2. Purga de `MFA_TRUSTED_DEVICES`: no encontre job que borre filas caducadas ni revocadas (solo `LimpiezaRefreshTokensJob` toca `REFRESH_TOKENS`). **Corregido 2026-11-16:** `LimpiezaMfaTrustedDevicesJob`.
3. Purga de `IMPORTACION_LOTE_FILAS.datos_json` y de `IMPORTACION_LOTES.nombre_archivo`: no encontre retencion. `LimpiezaExportacionesJob` solo vacia `contenido_original`. **Corregido parcialmente 2026-11-16:** `datos_json` de lotes cerrados con mas de 180 dias (`LimpiezaImportacionLotesJob`). Pendiente: lotes abiertos (confirmar reconstruye desde `datos_json`), filas soft-deleted (la policy RLS de update exige `deleted_at IS NULL`; requiere migracion), `errores_json`/`advertencias_json` y `nombre_archivo`.
4. `DELETE /api/usuarios/{id}/emails/{emailId}`: no revise si es borrado fisico o soft delete (la entidad `UsuarioEmail` no implementa `ISoftDelete` en `Entities.cs`, lo que sugiere fisico; no confirmado en ejecucion).
5. Valores en las columnas heredadas `TITULARES.identificacion/contacto_email/contacto_telefono` en instalaciones existentes: el codigo actual no las rellena, pero datos antiguos pueden seguir ahi. Hay que consultar la base real.
6. Si el recuerdo de dispositivo MFA esta activo en la instalacion (ajuste en `CONFIGURACION`); depende de la configuracion real.
7. Destino real y condiciones contractuales de OpenClaw, y si el proveedor de IA elegido (OpenAI, MiniMax, OpenRouter) tiene contrato de encargado de tratamiento y politica de retencion. No hay clausula ZDR equivalente para OpenAI ni MiniMax en el codigo.
8. Retencion de logs del Watchdog (`AtlasBalance.Watchdog`) y de Caddy (`scripts/Caddyfile.example`): no los revise. Pueden contener IP y rutas.
9. Contenido del log de peticiones de ASP.NET (`UseSerilogRequestLogging`, `Program.cs:620`): no verifique que campos emite (ruta, metodo, tiempo, estado por defecto), ni si la cadena de consulta puede llevar datos personales.
10. Datos personales en el frontend (almacenamiento local del navegador, cookies distintas de `mfa_trusted` y los tokens): no revise `frontend/`.
11. Ejecucion real de `Purge-DeliveryData` sobre el esquema actual (FK a `USUARIOS`, trigger de `AUDITORIAS`): solo lectura estatica, ver 6.2.
12. Modo TLS de SMTP efectivo en la instalacion (`StartTlsWhenAvailable` permite texto plano si el servidor no ofrece STARTTLS).
13. `GET /api/cuentas/{id}` devuelve IBAN completo a cualquier usuario con scope. Es un hecho del codigo, pero decidir si es aceptable es una decision de negocio. **Corregido 2026-11-16 (IBAN):** solo ADMIN ve el IBAN completo. Pendiente: `numero_cuenta` sigue completo para no-ADMIN y buscable.
14. Base legal, finalidades, categorias de interesados, plazos legales de conservacion y registro de actividades de tratamiento: este inventario es tecnico; esos elementos los define el responsable.
15. `IA_USO_USUARIOS`, `ALERTA_DESTINATARIOS` y `NOTIFICACIONES_ADMIN`: no contienen PII propia salvo ids de usuario y texto de mensaje; no analice el texto de `NOTIFICACIONES_ADMIN.mensaje`/`detalles_json`.

---

## Resumen (max 10 lineas)

1. Datos personales directos: email, nombre, IP, User-Agent y secreto TOTP de los 4-8 usuarios; nombre/IBAN/conceptos de titulares, sobre todo particulares y autonomos.
2. Protegidos en reposo por la app: contrasena (BCrypt 12), tokens (SHA-256), TOTP, claves API y token de Drive (Data Protection `enc:v2`), y backups subidos a Drive (AES-256-GCM). El resto, incluidos IBAN, nombres, backup local y xlsx, esta en claro.
3. Auditoria guarda IP y copias de valores (incluido IBAN y nombre) 365 dias, y es inmutable; las integraciones, 28 dias. IBAN: las filas nuevas ya van enmascaradas desde 2026-11-16; las anteriores no se tocan.
4. A terceros salen: IA (seudonimizada y con DLP, pero importes y conceptos libres no se enmascaran del todo), Drive (cifrado), SMTP y Slack (incluyen email atacado, IP y ultimo concepto) y OpenClaw (concepto y comentarios sin seudonimizar).
5. `GET /api/cuentas/{id}` devuelve el IBAN completo a cualquier usuario con acceso a esa cuenta. Corregido 2026-11-16: solo ADMIN.
6. Soft delete oculta pero no suprime; no existe herramienta de supresion por persona. `Purge-DeliveryData` es un vaciado total (Docker, sin tocar ficheros) y no se probo.
7. Retencion por defecto: auditoria 365 d, integracion 28 d, exports 90 d, backups 6 semanas, logs 30 d (seguridad 400 d), importacion bruta 180 d.
8. Huecos: sin purga de dispositivos MFA caducados ni de filas de importacion; sin cifrado de backup local ni de xlsx. Corregidos 2026-11-16 los dos primeros, con los limites de los puntos 7.2 y 7.3; cifrado de backup local y xlsx pendiente (decision: BitLocker).
9. Procedimiento de supresion propuesto en 6.3 (anonimizar, esperar o gestionar caducidad de auditoria/backups/logs).
10. Pendientes en seccion 7 (15 puntos), entre ellos Watchdog, Caddy, frontend y contratos con proveedores de IA.
