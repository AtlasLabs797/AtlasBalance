# Auditoria del checklist "Securituy app" contra Atlas Balance - 2026-11-16

Version: V-03.02. Alcance: contrastar cada punto del archivo `Securituy app.txt` (seguridad, privacidad, configuracion, dependencias, logging, permisos, validacion, RLS, secretos y 5 puntos de rendimiento) con el codigo real. **Solo informe: no se ha tocado codigo ni dependencias.**

> Documento interno. Describe vulnerabilidades sin corregir: no publicar ni subir a un repositorio publico hasta cerrar P0/P1. No contiene valores secretos.

## 0. Estado de las correcciones (actualizado 2026-11-16)

| Punto | Estado | Nota |
|---|---|---|
| P0-1 npm | Cerrado | axios 1.21.1; 0 high/critical (7 moderate sin arreglo no mayor). `follow-redirects` no entra en el bundle (comprobado). |
| P0-2 NuGet | Cerrado | MailKit/MimeKit 4.18.2 -> Pkcs 10.0.13; 0 vulnerables en 4 proyectos. Falta un envio SMTP real. |
| M-1 RLS | Cerrado | Test que enumera tablas; mutacion de prueba detectada. 8 excepciones documentadas siguen siendo deuda de V-02.07. |
| M-2 aviso de bloqueo | Cerrado en codigo | Probado con email falso; sin SMTP/RLS reales. |
| M-3 contrasenas | Cerrado | ~29.700 entradas 12+ (SecLists). |
| M-4 CSP | Cerrado | `style-src 'self'` en cabecera y `<meta>`; 0 violaciones con mocks; login real OK. Smoke autenticado pendiente. |
| M-5 log HttpClient | Cerrado (parcial) | Test de configuracion. La clave de exchangerate sigue en la ruta de la URL (no verificable sin clave real). |
| M-6 PII | Cerrado | `INVENTARIO_DATOS_PERSONALES_V-03.02.md`; contiene hallazgos nuevos (ver abajo). |
| M-7 escaner | Cerrado | Repo real: 0 hallazgos. |
| M-8 auditoria semanal | Hecho, sin verificar | Solo dispara desde `main`. |

Hallazgos nuevos del inventario, **no corregidos** (fuera del alcance acordado, para decidir): `GET /api/cuentas/{id}` devuelve IBAN completo a quien tenga permiso sobre la cuenta; la auditoria inmutable guarda IBAN/nombre/concepto en `detalles_json` 365 dias; la clausula de no-retencion del proveedor de IA solo aplica a OpenRouter de pago; OpenClaw recibe `concepto` sin seudonimizar (decision documentada); el dump local `pg_dump` y los `.xlsx` van sin cifrar; no hay purga de `MFA_TRUSTED_DEVICES` caducados; `Purge-DeliveryData` no sirve para peticiones individuales.

Actualizacion 2026-11-16: estado de estos hallazgos en la seccion 9.

## 1. Dictamen

La base es solida y ya paso varias auditorias (`Archivo/AUDITORIA_SEGURIDAD_PRE_LAUNCH_2026-08-04.md`, `Archivo/SEGURIDAD_CHECKLIST_APP_V-01.05_2026-05-01.md`). La mayoria del checklist ya esta cubierto, a menudo mejor que lo que pide el texto (RLS con FORCE, auditoria firmada y append-only, rotacion de refresh con deteccion de reuso, MFA, CSRF doble capa).

Lo que cambia respecto a agosto, y es lo urgente:

1. **El 2026-08-04 dependencias estaba en 0 vulnerabilidades. Hoy no.** `npm audit` da 2 *high* y NuGet 1 transitiva *high*. Los dos gates de CI (`ci.yml`) fallarian con el arbol actual.
2. Quedan mejoras de endurecimiento menores (aviso de bloqueo de cuenta, lista de contrasenas, CSP `unsafe-inline`, prueba que enumere RLS por tabla).
3. Varios puntos del checklist **no aplican** a una app on-premise de mismo origen sin registro publico ni subidas multipart; se justifican abajo.

## 2. Que significa cada estado

- **Cumple**: verificado en codigo en esta sesion.
- **Mejorable**: funciona, pero hay un hueco concreto.
- **No aplica**: el escenario no existe en esta arquitectura (motivo indicado).
- **No verificado**: no abierto en esta sesion o depende del servidor real.

## 3. Verificado, bloqueado y pendiente

**Verificado (ejecutado o leido):** `npm audit --omit=dev`; `dotnet list package --vulnerable --include-transitive`; `dotnet nuget why`; `Test-AtlasSecrets.ps1`; historial git de `.env`/`appsettings.*`/`.pfx`/`.pem`/`.key` (sin resultados); lectura de `Program.cs` (lineas 1-800), `AuthController`, `AuthService` (cookies, refresh, logout, cambio de contrasena), `SecurityPolicy`, `CsrfMiddleware`, `CsrfService`, `RateLimitingSetup/Options`, `ConfiguracionController.Get`, `ExportacionesController.Descargar`, `CuentasController.Obtener`, `TelemetriaController`, `PackageExtraction`, `BackupService.RunProcessAsync`, `BackupEncryptionService` (cabecera), `ci.yml`, frontend (`localStorage`, HTML crudo, `console`, redirecciones).

**Bloqueado / no ejecutado:** suite de tests y Testcontainers (no se ejecuto; AGENTS.md seccion 8), pruebas de comportamiento HTTP reales (no se levanto la API), inspeccion del ZIP de release y del Windows Server real (`ASPNETCORE_ENVIRONMENT`, ACL, BitLocker, certificado).

**Pendiente / no verificado:** lectura completa de los 24 controladores (solo se muestrearon Cuentas y Exportaciones por id; **no** se revisaron rutas de escritura con ids en el cuerpo: Extractos editar/borrar, Importacion `cuenta_id`, Alertas PUT/DELETE; ahi se confia en `ControllerAuthorizationCoverageTests`, `UserAccessScopeMatrixTests` y RLS), cuerpo de `SecurityAlertService` (M-2 se basa en una busqueda en tres ficheros), `npm audit fix --dry-run` (bloqueado por `EPERM`), `IntegrationAuthMiddleware` (solo cabecera), Watchdog completo, `GoogleDriveBackupService`, `IaController`/`AtlasAiService`, `Purge-DeliveryData`, medicion real de rendimiento.

## 4. Seguridad: punto por punto

### 4.1 Revision maestra y pre-launch

| Punto | Estado | Evidencia / comentario |
|---|---|---|
| Autenticacion + autorizacion en cada endpoint | Cumple | `FallbackPolicy = RequireAuthenticatedUser` (`AuthorizationConfiguration.cs`); anonimos explicitos y acotados (`AuthController`, `/api/health*`, `TelemetriaController`). Hay `ControllerAuthorizationCoverageTests`. |
| Secretos fuera del codigo | Cumple (adaptado) | Sin hallazgos en git ni en historial. Secretos de runtime en configuracion externa con ACL, cifrados con DataProtection+DPAPI (`SecretProtector`). Variables de entorno no aportan mas en un Windows Service. |
| Validacion en backend | Cumple | `[ApiController]` + DataAnnotations; `InvalidModelStateResponseFactory` generico (`Program.cs:346`). |
| Errores sin detalles internos | Cumple | `UseExceptionHandler` generico (`Program.cs:566`); `IncludeErrorDetails` solo en Development. |
| Queries parametrizadas | Cumple | EF Core; el SQL crudo hallado usa parametros (`ExtractosController.cs:298-309`, `AuditService.cs:115`, advisory locks con `{0}`). |
| Sanitizacion de contenido de usuario | Cumple | Sin `dangerouslySetInnerHTML`, `innerHTML`, `eval`, `document.write` en `frontend/src`. |
| CSRF | Cumple | Doble envio (`CsrfService`, comparacion en tiempo constante) + validacion de Origin/Referer independiente (`CsrfMiddleware.cs:44`) + `SameSite=Strict` + prefijo `__Host-`. |
| Datos sensibles en respuestas API | Cumple en lo visto | `ConfiguracionController` es solo ADMIN (`:19`). `Get` devuelve `ApiKeyConfigurada` (bool) para el tipo de cambio y solo booleanos de clave para IA (`:442-444`); secretos cifrados en BD. Sin revisar el resto de DTOs. |
| Rate limiting | Cumple | Global particionado por usuario/IP, auth 10/min/IP, escritura 60/min, "costosas" 5/min, IA con concurrencia 1, integracion propia (`RateLimitingSetup.cs`). |

### 4.2 Autenticacion y sesion

| Punto | Estado | Evidencia / comentario |
|---|---|---|
| Hash de contrasena | Cumple | bcrypt (`BCrypt.Net`), regla de AGENTS.md. |
| Tokens en cookies httpOnly, no en localStorage | Cumple | `AuthController.cs:269-306`: httpOnly + Secure + `SameSite=Strict`; solo la cookie CSRF es legible por JS (diseno double-submit). `localStorage` guarda tema, densidad, email recordado (opt-in, `LoginPage.tsx:147`) y el pais seleccionado (`paisScopeStore`, borrado al cerrar sesion); `sessionStorage` guarda un aviso de actualizacion y el cierre del banner de alertas. Ningun token ni dato financiero. |
| Expiracion del access token (15-30 min) | **Desviacion justificada** | 60 min (`appsettings.json`). Mitigado: `UserStateMiddleware` compara el `SecurityStamp` en cada peticion, asi que logout/cambio de contrasena invalidan el JWT al instante. Bajar a 30 min es posible pero el beneficio es pequeno. |
| Refresh 7-30 dias | Cumple | 7 dias, rotacion, **deteccion de reuso que revoca todas las sesiones** (`AuthService.cs:519-523`). |
| Logout invalida en servidor | Cumple | Rota `SecurityStamp` y revoca todos los refresh tokens (`AuthService.cs:640-700`); equivale a "cerrar sesion en todas partes". |
| Cambio de contrasena invalida sesiones | Cumple | Rota stamp, revoca refresh (`ChangePasswordAsync`, lineas ~790-900). |
| Fuerza bruta en login | Cumple | Por (IP,email) 3, por IP 7, por cuenta 5 -> bloqueo 30 min; senuelo bcrypt para igualar latencia; mensaje unico "Credenciales invalidas" (`AuthService.cs:120-253`). |
| Longitud minima 8 / lista de contrasenas filtradas | Mejorable | Minimo 12 (mejor que lo pedido), sin reglas de complejidad. Pero la blocklist es una lista local de ~150 entradas + patron palabra+sufijo (`SecurityPolicy.cs`), no top-10.000 ni HIBP. Ver M-3. |
| Reset de contrasena por email | **No aplica** | No existe flujo de "olvide mi contrasena": lo restablece un admin (`UsuariosController.cs:685`) y revoca sesiones. Sin superficie de enumeracion por email. |
| Nuevo ID de sesion tras login / fijacion de sesion | Cumple | Tokens nuevos por login y rotacion en cada refresh; id de sesion de auditoria que sobrevive a la rotacion. |
| Sesion ligada a IP/UA | Cumple (decision documentada) | No se invalida por cambio de IP (VPN/DHCP); se audita `SessionIpChanged` (`AuthService.cs:570-590`). Aceptable. |
| Aviso por email de bloqueo de cuenta | **Mejorable** | No se encontro envio de email al titular al bloquearse (`AccountLocked` solo audita). Ver M-2. |
| MFA | Cumple (extra) | TOTP, dispositivos de confianza ligados al stamp. No pedido por el checklist. |
| Registro publico / honeypot / verificacion de email | **No aplica** | No hay signup: las cuentas las crea un admin. |

### 4.3 Errores, logs y fuga de datos

| Punto | Estado | Evidencia |
|---|---|---|
| Handler global backend | Cumple | `Program.cs:566-618`; detalle solo en log. |
| Error boundary y telemetria frontend | Cumple | `TelemetriaController` acotado (20/min/IP, topes de longitud, `LogScrubber`). Anonimo a proposito (sendBeacon). |
| `console.*` con datos sensibles | Cumple | Una sola llamada, `api.ts:127`, solo `import.meta.env.DEV` y con detalle saneado. |
| Secretos en logs | Cumple con una condicion | La clave de exchangerate viaja en la **ruta** de la URL (`TiposCambioService.cs:415`) y el webhook de Slack es una URL secreta: lo protege el override `System.Net.Http.HttpClient: Warning` (presente en `appsettings.json` y plantillas). Si alguien lo sube a Information, la clave acaba en el log. Ver M-5. |
| Sondas `/api/health*` | Cumple | Devuelven solo `{status}` (`HealthProbeDtos.cs`); el detalle esta en `/api/sistema/salud`, solo ADMIN. |

### 4.4 Inyeccion, redirecciones, DOM

| Punto | Estado | Evidencia |
|---|---|---|
| SQL injection | Cumple | Ver 4.1. |
| XSS almacenado / DOM | Cumple | Sin sumideros HTML en el frontend. CSP `script-src 'self'`. |
| Open redirect | Cumple / no aplica | No hay `?redirect=`; el unico `returnTo` (`CuentaDetailPage.tsx:412`) es una ruta interna y existe `safeRoute` con test. |
| Inyeccion de comandos | Cumple | `ProcessStartInfo` con `ArgumentList` y sin shell (`BackupService.cs:322`): no hay concatenacion de comandos. Los binarios vienen de `WatchdogSettings`. `backup_path` y `export_path` **si** los edita un ADMIN desde la API, pero `Update` los valida con `IsSafeAbsoluteDirectory` (`ConfiguracionController.cs:139-144`, rechaza UNC y relativas); aceptable (admin autenticado, sin shell). No se auditaron las llamadas del Watchdog (`WatchdogOperationsService.cs:1265`, `ElevatedUpdateRunner.cs:126`). |
| SSRF por URL configurable | Cumple | La URL de actualizacion se restringe al host `github.com` (`ActualizacionService.cs:323, 860`). |

### 4.5 Subida de archivos

**No aplica el escenario clasico**: no hay `multipart`/`IFormFile`; la importacion es JSON con CSV pegado (5 MiB, 50.000 filas, 64 columnas extra, 4.096 chars/celda; `ImportacionService.cs:31`). Kestrel limita el cuerpo a 10 MB (`Program.cs:40`).

Los equivalentes reales si estan cubiertos:
- Importacion de backups de Drive (segun auditoria 2026-08-04, no reverificado): tamano en tres capas, 10 GiB configurable, borrado de parciales.
- Paquetes de actualizacion: ZIP con proteccion zip-slip, tope de entradas y tamano (`PackageExtraction.cs`, leido). Firma RSA reverificada sobre copia aislada antes de ejecutar como SYSTEM: segun `REGISTRO_BUGS.md` (V-03.01), no reverificado.
- Descarga de exportaciones: comprueba ambito de cuenta, ruta bajo `export_path`, extension `.xlsx` y responde `NotFound` (no revela existencia) (`ExportacionesController.cs:173-260`).
- Backups cifrados AES-GCM por bloques (`BackupEncryptionService.cs`).

Puntos 1-2 de "Magic bytes / re-codificar imagenes / metadata / cuota por usuario": **no aplican** (sin imagenes ni documentos subidos por usuarios).

### 4.6 HTTPS, cabeceras y cookies

| Punto | Estado | Evidencia |
|---|---|---|
| Redireccion HTTP->HTTPS | Cumple en codigo | `HttpsRedirectionMiddleware` (308, consciente de proxy). Falta validar en el servidor real. |
| HSTS | Cumple | 1 ano + subdominios; sin `preload` a proposito (`Program.cs:229`). |
| `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP/CORP | Cumple | `Program.cs:627-640`. |
| `X-Frame-Options` DENY | **Desviacion justificada (verificada)** | `SAMEORIGIN` + `frame-ancestors 'self'` porque `CuentaDetailPage.tsx:1365` incrusta un `<iframe>` de la propia app (`?embedded=1`, `Layout.tsx:62`). |
| CSP | Mejorable | `script-src 'self'` correcto; `style-src 'self' 'unsafe-inline'` debilita la defensa. Ver M-4. |
| Contenido mixto / WebSockets | Cumple | Sin `http://` en `src`/`index.html`/`public` (salvo dev). |
| Cookies | Cumple | httpOnly/Secure/`SameSite=Strict` y prefijo `__Host-`. `ShouldUseSecureCookie() => !IsDevelopment || Request.IsHttps` (`AuthController.cs:312`): Secure siempre fuera de Development. |

### 4.7 Privacidad y datos personales

| Punto | Estado | Comentario |
|---|---|---|
| Inventario de PII | Mejorable | No hay inventario formal. Hay `PiiMasking`, `IntegrationPseudonyms` y `AiPseudonymizer` (nombres reversibles a placeholders antes de enviar a la IA externa). Falta el documento. Ver M-6. |
| Minimizacion | No verificado | No se revisaron los formularios uno a uno. |
| Cifrado en reposo | Mejorable | Secretos de configuracion cifrados y backups cifrados. Los **dumps locales** y la BD dependen de BitLocker (ya anotado como gate en agosto); no se pudo verificar. |
| Borrado de cuenta (RGPD) | **Tension de diseno** | AGENTS.md exige borrado logico universal; el derecho de supresion pide borrado real. Es una app interna (4-8 usuarios): se resuelve con el procedimiento `Purge-DeliveryData` bajo peticion, no con un boton de autoservicio. Conviene documentarlo. |
| Exportacion de datos del usuario | No aplica / bajo valor | Usuarios internos de la empresa; no hay clientes finales. |

### 4.8 Configuracion insegura

| Punto | Estado | Evidencia |
|---|---|---|
| Debug/Swagger/Hangfire en prod | Cumple | Hangfire solo en Development (`Program.cs:720`); sin Swagger. |
| CORS | Cumple | No se registra en produccion (mismo origen); solo `localhost:5173` en Development. |
| Secretos debiles en arranque | Cumple | `RejectUnsafeProductionSecret` y `RejectUnsafeAllowedHosts` fail-closed (`Program.cs:127-171`). |
| TLS a PostgreSQL | Cumple | `DatabaseTlsPolicy` fail-closed para hosts remotos; loopback exento. La plantilla de produccion usa `SslMode=Disable` contra `localhost`, aceptable mientras la BD este en la misma maquina. |
| BD accesible y RLS | Cumple en lo financiero; 8 tablas fuera a proposito | 27 tablas con `ENABLE`/`FORCE` y politicas. Sin RLS, documentado como limite deliberado y deuda diferida (`DOCUMENTACION_TECNICA.md` ~4674 y ~4696): `USUARIOS`, `USUARIO_EMAILS`, `REFRESH_TOKENS`, `INTEGRATION_TOKENS`, `CONFIGURACION`, `TIPOS_CAMBIO`, `DIVISAS_ACTIVAS`, `FORMATOS_IMPORTACION`. Rol runtime sin UPDATE/DELETE sobre `AUDITORIAS`. Ver M-1. |
| Credenciales de test en el repo | Mejorable | El scanner de secretos **falla localmente**: detecta una cadena de conexion con contrasena en `appsettings.Development.json`. Esta en `.gitignore`, no esta en git ni en historial, y en CI no existe, asi que CI no falla. Es un valor de desarrollo local, pero evita ruido: ver M-7. |
| Servicios de terceros | Cumple | Clientes HTTP con timeout, hosts fijos. Webhooks entrantes: no hay (Slack/Drive son salientes). |

### 4.9 Dependencias

| Punto | Estado | Evidencia |
|---|---|---|
| Auditoria npm | **Falla** | `npm audit --omit=dev` y `npm audit` completo: 2 *high*, `axios` (rango afectado 1.0.0-1.19.0, 12 advisories) y `follow-redirects` (1.16.0-1.16.1, 3 advisories). npm indica arreglo con `npm audit fix`, pero la version exacta corregida **no se pudo confirmar**: `npm audit fix --dry-run` fallo con `EPERM` al borrar `node_modules\.package-lock.json` (bloqueo del SO, no reintentado; `package.json` y el lock no cambiaron). Por los rangos hay que quedar por encima de 1.19.0 y 1.16.1 (inferencia). Ver P0-1. |
| Auditoria NuGet | **Falla** | `System.Security.Cryptography.Pkcs 10.0.0` (High GHSA-846f-f764-76mx, Moderate GHSA-2fmv-rv2c-59x7), transitiva de **MailKit 4.17.0 -> MimeKit 4.17.0**, en API, API.Tests y Caching.Tests. Watchdog limpio. Ver P0-2. |
| Lock file y versiones | Cumple (con matiz) | `package-lock.json` y `packages.lock.json` versionados; CI usa `npm ci` y `--locked-mode`. `package.json` usa rangos `^`, que el lock fija de facto (el checklist prefiere exactas: opcional). `overrides` en npm para transitivas. |
| Gate de CI | Cumple; **probable rojo** | `ci.yml` falla ante high/critical npm y ante cualquier vulnerabilidad NuGet. Con estos resultados deberia fallar en la rama; **no se consulto `gh run list`**, asi que es inferencia. |
| Paquetes abandonados | No verificado | Sin revision de actividad de repositorios. |

### 4.10 Logging, monitorizacion y auditoria

| Punto | Estado | Evidencia |
|---|---|---|
| Eventos de seguridad y login | Cumple (por el codigo citado; segun auditorias previas) | `SecurityAuditMiddleware`, `AuditService`, `SecurityEventLog` a fichero propio con 400 dias de retencion. |
| Auditoria inmutable y firmada | Cumple (por encima del checklist) | Trigger append-only, privilegios revocados, firma (`AuditSigner`), verificacion nocturna (`VerificacionIntegridadAuditoriaJob`). |
| Alertas (10 fallos/5 min, 401/403, etc.) | No reverificado | Existen `SecurityAlertJob`/`SecurityAlertService`/`HealthAlertJob` (registrados en `Program.cs:535-551`); no se leyeron las reglas concretas. Segun V-02.07. |
| Redaccion de logs | Cumple en lo visto | `LogScrubber` evita log forging (quita CR/LF y trunca); no redacta secretos. La proteccion se apoya en no loguearlos (regla de AGENTS.md) y en `IntegrationAuthMiddleware`, que marca como `[REDACTED]` las claves sensibles de query. Segun auditoria 2026-08-04, no reverificado en todos los servicios. |
| Alertas por pais/IP nueva | No verificado | Hay `SessionIpChanged`; no se confirmo regla de "login desde nuevo pais". Poco valor en red local. |

### 4.11 Permisos, IDOR, RLS

| Punto | Estado | Evidencia |
|---|---|---|
| Propiedad por recurso | Cumple en lo muestreado | `Obtener` cuenta -> `CanAccessCuentaAsync` (`CuentasController.cs:176`); exportacion -> ambito de la cuenta y `NotFound` si no. Hay `UserAccessScopeMatrixTests`, `UserAccessServiceTests`. |
| Roles en backend | Cumple | `[Authorize(Roles = "ADMIN")]` a nivel de clase en Usuarios, Configuracion, Backups, Auditoria, Sistema, Integraciones, Divisas, TiposCambio, FormatosImportacion y NotificacionesAdmin; por accion en Cuentas, Titulares, Paises y Alertas; `ADMIN,GERENTE` en exportar y en una accion de Extractos. Resto: autenticado + ambito (`IUserAccessService`). |
| UUID en lugar de enteros | Cumple | PK `Guid` en entidades (`Entities.cs`). |
| Middleware reutilizable de autorizacion | Cumple en esencia | `IUserAccessService` centraliza el ambito. |
| RLS en todas las tablas | **Mejorable** | Verificado cruzando los `ToTable(...)` de `AppDbContext` con las migraciones: 35 tablas, 27 con RLS, 8 sin (lista arriba). `RowLevelSecurityTests.cs:503-552` comprueba una **lista fija de 26 nombres**, no enumera `pg_class`: una tabla nueva sin RLS pasaria desapercibida. Ver M-1. |
| Bearer de integracion (OpenClaw) | Cumple | Prefijo `sk_atlas_balance_` y redaccion de claves sensibles de query (`IntegrationAuthMiddleware`, leido de forma parcial). Hash SHA-256 en BD y 100 req/min: segun AGENTS.md y tests existentes, no reverificado. |

### 4.12 Claves de API solo en backend

Cumple. No hay claves en el frontend (sin `VITE_*` sensibles, sin llamadas directas a proveedores desde el navegador). La IA se llama desde el backend (clientes HTTP `openrouter`/`openai`/`minimax`, `Program.cs:287-322`) y la API solo expone si hay clave (booleanos). `AtlasAiService`/`IaController` no se leyeron: el cifrado de esas claves en BD se asume por `ProtectExistingConfigurationSecrets` (`Program.cs:480`), no verificado fila a fila. Frontend: la unica variable `VITE_*` es `VITE_APP_VERSION`.

## 5. Rendimiento (los 5 ultimos puntos)

| Punto | Estado | Evidencia / recomendacion |
|---|---|---|
| Inline de SVG criticos | **No aplica / ya hecho** | Los iconos son `lucide-react` con imports nombrados (14 modulos) y se compilan al bundle como JSX: no generan peticiones. El favicon ya es un `data:` URI inline (`index.html:19`). `public/logos` solo contiene dos PNG (`Atlas Balance.png`, `Atlas Labs.png`): son los unicos recursos de imagen por red; no se midio si estan sobre el pliegue. |
| Pool de conexiones | Cumple | Npgsql agrupa por defecto; `Maximum Pool Size=20` en runtime y 4 en migracion (`appsettings.Production.json.template`). Hangfire con 2 workers (`Database:HangfireWorkerCount`). Existe `Rls/RlsPoolResetProbe.cs` en los tests (su contenido no se leyo). Sin PgBouncer porque no hace falta con 4-8 usuarios. |
| Cache en memoria | Cumple | `CacheService` con single-flight, generaciones de invalidacion y limite de entradas; invalida via `DashboardCacheInvalidationInterceptor`. Single-node, documentado (CONC-027). Riesgo conocido: no escala a varias instancias, irrelevante on-premise. |
| Cache de peticiones en cliente | Cumple | `@tanstack/react-query` con `staleTime`/`gcTime` y tests de `queryKeys`/`invalidation`/`queryClient`. |
| Cache de lecturas repetidas | Cumple | TTL por tipo (config 120 s, ambito de usuario 45 s, dashboard 15-300 s, tipos de cambio 300 s). **No medido**: no hay cifras reales de caida de consultas. |

Sin medir en esta sesion: tiempos reales de respuesta ni volumen de consultas. Cualquier afirmacion de "mejora" necesita medicion con `RequestMetrics`/`pg_stat_statements`.

## 6. Mejoras propuestas, priorizadas

### P0 - antes de la siguiente version/release

**P0-1. Frontend: 2 vulnerabilidades *high* (axios, follow-redirects).**
- Impacto real (estimacion, sin confirmar contra el bundle): en el navegador axios usa el adaptador XHR; `follow-redirects` pertenece al adaptador Node y no deberia estar en `dist/`; varios advisories de axios exigen *prototype pollution* previa o el adaptador `fetch`/Node. La explotabilidad aqui parece baja, pero el gate de CI queda rojo y hay que cerrarlo. Confirmar con un `grep follow-redirects` sobre los assets de un `npm run build`.
- Como: en una rama, `npm audit fix` (o subir `axios` a la version corregida), regenerar `package-lock.json`, correr `npm run lint`, `npm run test:unit` y `npm run build`, y probar login/refresh porque el interceptor de refresh es el consumidor critico. Plan de un paquete cada vez, con `git revert` del commit como rollback.

**P0-2. Backend: `System.Security.Cryptography.Pkcs 10.0.0` vulnerable (via MailKit/MimeKit).**
- Impacto: se usa para S/MIME en correo; Atlas Balance envia SMTP simple, asi que la ruta vulnerable probablemente no se ejecuta. El gate NuGet falla igualmente.
- Como: actualizar `MailKit`/`MimeKit` a la version que arrastre un Pkcs corregido o, si no existe aun, fijar `System.Security.Cryptography.Pkcs` a la version parcheada como referencia directa en `AtlasBalance.API.csproj`. Regenerar `packages.lock.json` en API, API.Tests y Caching.Tests (CI usa `--locked-mode`). Verificar con `dotnet list package --vulnerable --include-transitive`, tests de `EmailService` y un envio SMTP de prueba (`Test-AtlasSmtp.ps1`).

### P1 - proxima version

**M-1. Prueba de cobertura RLS por tabla.** El test actual usa una lista fija. Sustituirla por una consulta que enumere todas las tablas de `public` y falle si alguna no tiene `relrowsecurity` y `relforcerowsecurity` y no figura en una lista de excepciones explicita: las 8 de identidad/configuracion ya documentadas, `__EFMigrationsHistory` y el esquema de Hangfire. Asi una tabla financiera nueva sin RLS rompe CI. Esfuerzo bajo. La deuda diferida a V-02.07 (RLS en `USUARIOS`, `REFRESH_TOKENS`, `INTEGRATION_TOKENS`, `CONFIGURACION`) sigue abierta; su diseno exige limitar columnas, no solo filas, asi que no se propone como cambio rapido.

**M-2. Aviso por email al bloquear una cuenta.** El checklist lo pide y hoy solo se audita (`AuditActions.AccountLocked`). Reutilizar `IEmailService`/`IAlertDispatcher`: al activarse `LockedUntil`, enviar al usuario hora, IP resumida y aviso, **sin enlace de cambio de contrasena** (no hay flujo de reset; el desbloqueo es automatico a los 30 min o por admin). Limitar a un aviso por bloqueo. Esfuerzo bajo-medio.

**M-3. Lista de contrasenas.** Opciones: (a) lista local ampliada de 10.000 entradas (cargada como recurso embebido, sin red), o (b) HIBP k-anonymity. Recomendacion: **(a)**; (b) envia un prefijo de hash a un tercero desde un servidor on-premise y rompe el uso offline. Mantener minimo 12 y sin reglas de complejidad. Prueba: ampliar `SecurityPolicyTests`.

**M-4. CSP sin `unsafe-inline` en estilos.** Requiere localizar `style=` inline en React/Recharts. Es la unica directiva floja. Riesgo bajo (inyeccion CSS, sin script), esfuerzo medio-alto: dejarlo anotado como deuda aceptada salvo que se haga un barrido de estilos.

### P2 - higiene

**M-5. Test de que `System.Net.Http.HttpClient` no baja de Warning** en la configuracion efectiva, y mover la clave de exchangerate fuera de la ruta si el proveedor lo permite (cabecera). Evita que subir el nivel de log filtre la clave.

**M-6. Documento de inventario de PII** (`Documentacion/`): que datos guarda la app (usuarios, titulares, cuentas, extractos, IPs de auditoria), donde, quien accede, cifrado y que se envia a proveedores de IA (ya seudonimizado), mas el procedimiento de supresion bajo peticion (tension con soft delete). Sin codigo.

**M-7. Scanner de secretos local ruidoso.** `Test-AtlasSecrets.ps1` marca el `appsettings.Development.json` ignorado por git. Excluir archivos ignorados (`git ls-files` como universo) o la ruta, para que el scanner local coincida con CI y no se normalice ignorar sus fallos. Rotar de todos modos esa contrasena si alguna vez se reutilizo fuera del Docker local.

**M-8. Avisar de dependencias antes del push.** El gate de CI esta bien, pero las alertas nuevas aparecen sin cambios de codigo. Programar un `npm audit`/`dotnet list package --vulnerable` semanal (workflow `schedule` o Dependabot) para enterarse antes de que rompa una release.

### Gates operativos aun abiertos (de agosto)

Siguen sin evidencia en este informe: `ASPNETCORE_ENVIRONMENT=Production` efectivo, ACL de `backups`/`exports`, BitLocker, certificado y HSTS reales, 17 tests Postgres en CI, `pg_dump` + restauracion con rol owner. Ver `Archivo/AUDITORIA_SEGURIDAD_PRE_LAUNCH_2026-08-04.md`.

## 7. Que NO recomiendo hacer

- **No** bajar `X-Frame-Options` a DENY: rompe el `<iframe>` de `CuentaDetailPage`.
- **No** migrar a variables de entorno por el checklist: no mejora la seguridad en un Windows Service con ACL.
- **No** anadir registro, reset por email, honeypot, verificacion de email, subida de imagenes ni S3/Supabase: no hay esa superficie y cada cosa la crearia.
- **No** forzar `SameSite`/`CORS` adicionales: ya estan al maximo (Strict, mismo origen).
- **No** montar Redis ni PgBouncer: single-node con 4-8 usuarios.

## 8. Orden de ejecucion sugerido

1. P0-1 y P0-2 en una rama, uno por commit (AGENTS.md: un cambio verificable cada vez).
2. M-1 (test RLS), por ser barato y proteger lo mas critico.
3. M-2 y M-3.
4. M-5 a M-8 como lote de higiene.
5. M-4 solo si se decide invertir en limpiar estilos inline.

Cada cambio de codigo debe ir con su version (V-03.02), bitacora y, si procede, entrada en `LOG_ERRORES_INCIDENCIAS.md`.

## 9. Nuevos hallazgos del inventario: estado (2026-11-16)

Esta seccion actualiza la lista "Hallazgos nuevos del inventario, no corregidos" de la seccion 0, que se mantiene como registro del momento de la auditoria.

| Hallazgo | Estado | Nota |
|---|---|---|
| `GET /api/cuentas/{id}` devuelve IBAN completo | Cerrado (IBAN) | Solo ADMIN ve el IBAN completo; busqueda por IBAN solo ADMIN. `numero_cuenta` sigue completo para no-ADMIN: pendiente. |
| Auditoria inmutable guarda IBAN, nombre y concepto 365 dias | Parcial | Filas nuevas: IBAN enmascarado antes de firmar, `IbanCambiado` en cambios de IBAN. Nombre y concepto siguen completos (decision del usuario). Filas anteriores no se tocan por la firma; caducan a los 365 dias. |
| Clausula de no-retencion del proveedor de IA solo en OpenRouter | Pendiente contractual | OpenRouter ya envia `zdr=true` y `data_collection=deny`. OpenAI y MiniMax no tienen clausula en codigo: falta DPA y politica de retencion. No es un fix de codigo. |
| OpenClaw recibe `concepto` sin seudonimizar | Sin cambios | Decision documentada en el inventario. |
| Backup local `pg_dump` y `.xlsx` sin cifrar | Pendiente operativo | Decision del usuario: dejarlo a BitLocker. Gate operativo abierto desde la auditoria de 2026-08-04. |
| Sin purga de `MFA_TRUSTED_DEVICES` caducados | Cerrado | `LimpiezaMfaTrustedDevicesJob` (diario): caducados o revocados hace mas de 1 dia. Test en Postgres real con RLS FORCE. |
| Sin purga de filas de importacion | Parcial | `LimpiezaImportacionLotesJob` (diario) vacia `datos_json` de lotes cerrados con mas de 180 dias. No cubre lotes abiertos, filas soft-deleted (la policy RLS de update exige `deleted_at IS NULL`; requiere migracion), `errores_json`, `advertencias_json` ni `nombre_archivo`. |
| `Purge-DeliveryData` no sirve para peticiones individuales | Pendiente | Sin cambios. Solo vacia la base entera. |

Verificacion del lote: AtlasBalance.API.Tests 994/994 y AtlasBalance.Caching.Tests 15/15; `tsc` y lint del frontend. Sin prueba en la app autenticada (MFA obligatorio).
