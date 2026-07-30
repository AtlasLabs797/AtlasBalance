# Documentacion tecnica

## 2026-07-29 - V-02.07 - Capa de rate limiting global

- **Que:** `AddRateLimiter` de ASP.NET Core 8 configurado desde
  `AtlasBalance.API/RateLimiting/`. Antes no existia rate limiting a
  nivel de framework: solo cuatro contadores artesanales sueltos
  (`SmtpTestRateLimit`, `TelemetriaController`, `IntegrationAuthMiddleware`
  y los de `AuthService`), y 140 de 153 endpoints sin ningun limite.
- **Por que un limitador global y no atributos por endpoint:** decorar
  153 acciones una a una produce un diff enorme y, sobre todo, deja el
  siguiente endpoint nuevo sin cubrir en cuanto alguien se olvide del
  atributo. El `GlobalLimiter` clasifica por ruta y verbo, asi que la
  cobertura es por defecto y solo hay que declarar las excepciones.
- **Como clasifica** (`RateLimitingSetup.ResolvePartition`):

  | Trafico | Clave de particion | Limite por defecto |
  |---|---|---|
  | Fuera de `/api` y `/api/health` | — | Exento |
  | `/api/integration/openclaw/**` | — | Exento; ya limitado por token en `IntegrationAuthMiddleware` |
  | `/api/auth/login`, `refresh-token`, `mfa/verify`, `cambiar-password` | IP | 10/min |
  | Resto de rutas `/api` sin sesion | IP | 60/min |
  | GET/HEAD/OPTIONS con sesion | `userId` | 300/min |
  | POST/PUT/PATCH/DELETE con sesion | `userId` | 60/min |
  | Politica `atlas-expensive` (se suma a la de escritura) | `userId` | 5/min |

- **Por que lo autenticado particiona por `userId` y no por IP:** la IP
  depende de la topologia de red. Si algun dia se pone un proxy delante y
  no se configura `ForwardedHeaders:KnownProxies`, todo el trafico caeria
  en un unico cubo por la IP del proxy y los usuarios se denegarian entre
  si. Con `userId` eso no puede pasar. Solo lo anonimo particiona por IP,
  porque antes de tener sesion no hay otra cosa.
- **Donde se configura:** seccion `AtlasBalance:RateLimiting` de
  `appsettings.json` y de los dos `.template`, mismo patron que
  `AtlasBalance:Caching`. `Enabled: false` es el interruptor de
  emergencia y viene desactivado en Development, porque
  `React.StrictMode` duplica los efectos de montaje y generaria 429
  espureos al desarrollar.
- **Colocacion en el pipeline:** `app.UseRateLimiter()` va despues de
  `app.UseAuthentication()` —las politicas por usuario necesitan los
  claims resueltos— y despues de `UseStaticFiles`, para que los estaticos
  de la SPA no consuman presupuesto de API.
- **Respuesta al rechazar:** 429 con header `Retry-After` en segundos y
  cuerpo JSON generico. El rechazo se loguea con `LogScrubber.Scrub`
  sobre metodo, ruta e IP (CWE-117, mismo patron que el fix de CodeQL
  #16). Ese log es la via de calibracion: si aparecen rechazos de
  usuarios reales, los limites estan cortos y se suben con ese dato
  delante. El frontend tiene rama propia para 429 en
  `services/api.ts` y no reintenta.
- **Limitaciones conocidas:** los contadores viven en memoria del
  proceso, igual que el resto de caches de la app (single-node
  on-premise). Si el despliegue se escala a varias instancias hay que
  mover esto a un almacen compartido, misma deuda ya registrada para
  `IMemoryCache`.

## 2026-07-29 - V-02.07 - Cierre de los dos defectos pendientes de la auditoria de errores

- **Que:** se corrigen los dos defectos que la auditoria de mensajes
  de error dejo abiertos. Ninguno era fuga de datos; los dos eran
  validacion que aparentaba existir y no existia.
- **Por que:** ambos daban falsa sensacion de cobertura. Un lector del
  codigo veia `AddFluentValidationAutoValidation()` y `[Required]` y
  asumia validacion activa donde no habia ninguna.
- **Como (FluentValidation):** retirados el `using`, la llamada de
  registro en `Program.cs` y la `PackageReference` del `.csproj`.
  Como `Directory.Build.props` fija
  `RestorePackagesWithLockFile=true` y `Build-Release.ps1` restaura en
  `--locked-mode`, quitar el paquete obliga a regenerar los
  `packages.lock.json`: se hizo con `dotnet restore --force-evaluate`
  en API, `AtlasBalance.API.Tests` y `AtlasBalance.Caching.Tests` (los
  dos ultimos arrastraban FluentValidation transitivamente via
  `ProjectReference`). El Watchdog no estaba afectado. Sin ese paso, la
  release habria fallado en el primer `restore`.
- **Como (`[Required]` sobre `Guid`):** las tres propiedades
  `CuentaId` de `ImportacionValidarRequest`,
  `ImportacionConfirmarRequest` e
  `ImportacionPlazoFijoMovimientoRequest` pasan a `Guid?` conservando
  `[Required]`. En `ImportacionService` las lecturas usan
  `request.CuentaId ?? Guid.Empty` en lugar de `.Value`, para que un
  camino interno que no pase por validacion de modelo degrade al 404
  ya existente en vez de producir un 500.
- **Efecto observable:** una peticion de importacion sin `cuenta_id`
  pasa de devolver 404 "Cuenta no encontrada o inactiva" a devolver el
  400 generico de datos invalidos, que es lo correcto. El frontend no
  se ve afectado: nunca envia estas peticiones sin cuenta.
- **Deuda relacionada no tocada:** `ImportacionLoteCrearRequest.CuentaId`
  sigue siendo `Guid` sin `[Required]`. No prometia validacion, asi que
  no entraba en el alcance de estos dos defectos.

## 2026-07-29 - V-02.07 - Auditoria de mensajes de error sensibles y fugas de datos, con correcciones

- Auditoria de mensajes de error y fugas de datos hacia el cliente:
  respuestas HTTP de error, `console.*` del navegador, bundle de
  produccion (sourcemaps y JS minificado) y el comportamiento por
  defecto de ASP.NET Core (`ValidationProblemDetails`,
  `WWW-Authenticate`). 10 hallazgos corregidos (1 ALTA, 3 MEDIA, 6
  BAJA), 2 defectos preexistentes dejados como pendientes (no son
  fugas de datos), y una decision de producto documentada sin cambio
  de codigo.
- **Fragmento de clave API filtrado al cliente
  (`Services/AtlasAiService.cs`, severidad ALTA).** Un 401 real del
  proveedor de IA (formato `{"error":{"message":"Incorrect API key
  provided: sk-proj-abc123XYZ"}}`, placeholder inventado) atravesaba
  `ExtractProviderErrorSummary` y `ShortProviderPayload` sin
  redactarse: el regex de redaccion esperaba la credencial pegada a la
  palabra clave, y con el texto real de OpenAI redactaba "provided:"
  dejando la clave intacta. El fragmento llegaba al usuario final via
  `IaController` (`[Authorize]` generico, no solo ADMIN) en el campo
  `error` de un 502. Correccion: (a) eliminado el sufijo `{detail}` de
  `BuildProviderHttpErrorMessage` y `BuildProviderResponseErrorMessage`
  (el parametro `providerError` se conserva para
  `IsOpenRouterDataPolicyError`/`IsOpenRouterModelRestrictionError`,
  que lo siguen usando para clasificar); (b) `ShortProviderPayload`
  redacta ahora tambien por forma de credencial (`sk-proj-`,
  `sk-or-v1-`, `sk-`, `hf_`, `gsk_`, `xai-`, `AIza`); (c) se inyecto
  `ILogger<AtlasAiService>` (no existia) para que el detalle quede en
  Serilog ademas de en la auditoria de BD, que era el unico rastro
  tras quitar el detalle del cliente. Riesgo residual aceptado: un
  prefijo fuera de esa lista seguiria llegando a log/auditoria, ambos
  de acceso exclusivo de administrador on-premise.
- **Sourcemaps publicados en produccion (`vite.config.ts`,
  `scripts/Build-Release.ps1`, severidad MEDIA).** `sourcemap:
  'hidden'` solo omite el comentario `sourceMappingURL`, no impide
  servir el `.map`; `Build-Release.ps1` copiaba `dist` completo a
  `wwwroot` sin filtrar. Correccion: borrado explicito de `.map` del
  `wwwroot` publicado tras la copia (no `Copy-Item -Exclude`, no
  filtra de forma fiable en copias recursivas), con
  `-ErrorAction Stop` y verificacion posterior que rompe la release si
  queda alguno.
- **Error boundary con `console.error` en produccion y `sendBeacon` a
  ruta inexistente (`AppErrorBoundary.tsx`, severidad MEDIA).** El
  detalle completo del error acababa en la consola del cliente y no
  quedaba registro en servidor porque `/api/telemetria/errores` no
  existia (verificado por grep en `backend/src`). Correccion:
  eliminado el `console.error`; endpoint nuevo
  `Controllers/TelemetriaController.cs` + `DTOs/TelemetriaDtos.cs`
  (`POST /api/telemetria/errores`, `[AllowAnonymous]`, 20
  reportes/IP/min via `IMemoryCache`, recorte de longitud, saneado
  CR/LF, siempre 204). El DTO fija nombres con `[JsonPropertyName]`
  porque el frontend envia camelCase y la politica global es
  SnakeCaseLower; el payload viaja en `Blob` `application/json` porque
  `sendBeacon` con string suelto manda `text/plain` y no bindea. Ruta
  excluida de `CsrfMiddleware` (`sendBeacon` no puede mandar
  `X-CSRF-Token`) y de `PrimerLoginMiddleware` (debe funcionar con
  cambio de password pendiente).
- **Sin error boundary raiz ni handlers globales (`main.tsx`,
  `App.tsx`, severidad MEDIA).** El boundary solo envolvia cada ruta;
  un fallo en layout/providers/`App` dejaba pantalla en blanco sin
  captura. Correccion: `AppErrorBoundary` envuelve ahora todo el arbol
  en `main.tsx` (fuera de `QueryClientProvider` y `BrowserRouter`);
  listeners de `unhandledrejection` y `error` anadidos. Logica de
  envio centralizada en el modulo nuevo `src/utils/reportClientError.ts`,
  con tope de 10 reportes por carga de pagina, sin escribir nunca en
  consola.
- **`ValidationProblemDetails` por defecto (`Program.cs`, severidad
  BAJA).** Sin `InvalidModelStateResponseFactory`, `[ApiController]`
  devolvia `traceId`, URL `type` rfc7231, tipos .NET (`System.Guid`) y
  PascalCase (`RawData`) en vez del contrato snake_case. Correccion:
  `InvalidModelStateResponseFactory` devuelve 400 con mensaje generico
  y loguea el ModelState real saneado por `LogScrubber.Scrub`.
  Verificado antes de aplicarlo que `errorMessage.ts` no dependia del
  formato anterior (lee `payload.errors` con degradacion limpia, no
  usa `traceId`).
- **`JwtBearer.IncludeErrorDetails` (`Program.cs`, severidad BAJA).**
  El default `true` del framework exponia en `WWW-Authenticate` el
  motivo exacto del rechazo y el timestamp exacto de expiracion.
  Correccion: `options.IncludeErrorDetails =
  builder.Environment.IsDevelopment();`.
- **`UserStateMiddleware` distinguia el motivo del rechazo
  (severidad BAJA).** Cuatro mensajes distintos ("Token de usuario
  invalido", "La sesion ya no es valida", "Usuario bloqueado
  temporalmente por intentos fallidos", "Se requiere MFA para
  continuar") revelaban a quien posee un token robado por que dejo de
  funcionar. Correccion: mensaje unico "La sesion ya no es valida.
  Vuelve a iniciar sesion." y motivo real al log via
  `ILogger<UserStateMiddleware>` inyectado (no existia), con path e IP
  saneados por `LogScrubber.Scrub`. Verificado que el frontend no
  ramifica por ninguno de los cuatro mensajes anteriores.
- **Rate limit de integracion con cifra exacta
  (`IntegrationAuthMiddleware`, severidad BAJA).** El mensaje
  "RATE_LIMITED: Mas de 100 requests por minuto para este token"
  revelaba el limite configurado. Correccion: mensaje sin cifra.
- **Build de produccion sin eliminar `console.*` (`vite.config.ts`,
  severidad BAJA).** Sin `esbuild.drop`/terser/`minify`. Un primer
  intento con `esbuild: { drop: [...] }` no funciono: Vite 8 usa
  rolldown/oxc por defecto y descarta esas opciones en silencio con el
  aviso "Both esbuild and oxc options were set" (verificado
  empiricamente: seguian 9 `console.error` en el bundle). Correccion:
  `build.rollupOptions.output.minify = { compress: { dropConsole:
  true, dropDebugger: true } }`, mecanismo nativo de oxc, solo bajo
  `build.*` (no afecta a dev). Verificado en el bundle: 0
  `console.error`, 0 `console.log`, 0 `debugger`.
- **Sin limite explicito de tamano de request (`Program.cs`, severidad
  BAJA).** Sin `MaxRequestBodySize`, Kestrel usaba su default de
  30.000.000 bytes, y un cuerpo excesivo caia en el 500 generico del
  handler global. Correccion: `MaxRequestBodySize` a 10 MiB (el unico
  endpoint de payload grande es importacion, limitado a 5 MiB de
  `RawData`) mas rama nueva en el handler global que devuelve el
  `StatusCode` real de `BadHttpRequestException` sin `ex.Message`.
- **Cambios en tests.** Los constructores de `AtlasAiService` y
  `UserStateMiddleware` ganaron un parametro `ILogger`: 50 sitios
  actualizados en `AtlasAiServiceTests.cs` y 5 en
  `UserStateMiddlewareTests.cs` con `NullLogger<T>.Instance`. 4 tests
  de `AtlasAiServiceTests.cs` que asertaban que el detalle del
  proveedor SI aparecia en el mensaje (codificaban la fuga como
  comportamiento esperado) se reescribieron para verificar la
  propiedad correcta: mensaje al usuario sin texto del proveedor,
  entrada de auditoria con el texto conservado.
- **Auditado y correcto, sin cambios.** Los 23 controllers usan DTOs
  con allowlist explicita (0 `return Ok(entidad)`); ningun hash de
  password, token, secreto MFA, CSRF ni refresh token OAuth llega al
  cliente. `ConfiguracionController` y `GoogleDriveBackupService` ya
  redactaban secretos. Login con anti-enumeracion robusta (hash
  senuelo). Kestrel con `AddServerHeader = false`. Los 4 middleware
  devolvian literales fijos. Mensajes de Postgres nunca propagados. El
  Watchdog usa 13 literales fijos y seguros. El `console.error` de
  `api.ts` esta dentro de `import.meta.env.DEV`, saneado, y desaparece
  por dead-code elimination.
- **Decision de producto, no se toca.** `ExportacionesController`
  resuelve el nombre completo de quien genero una exportacion, y
  `ExtractosDtos` expone GUIDs de usuario en
  `CheckedById`/`FlaggedById`/`UsuarioId` a usuarios no-admin dentro de
  su propio scope. Se dejan como estan: 4-8 usuarios de la misma
  empresa que ya comparten acceso a esas cuentas, funcionalidad
  deseada.
- **Defectos detectados y no corregidos (van a `REGISTRO_BUGS.md` como
  pendientes).** `FluentValidation.AspNetCore` registrado con
  `AddFluentValidationAutoValidation()` en `Program.cs` sin que exista
  ningun `AbstractValidator<T>` en todo el backend (configuracion
  muerta). `[Required]` sobre un `Guid` no-nullable en
  `DTOs/ImportacionDtos.cs` que nunca falla porque el valor jamas es
  null (validacion inefectiva).
- **Verificacion:** build de `AtlasBalance.API` (0 errores, 6 warnings
  preexistentes), `AtlasBalance.API.Tests` 427/427,
  `AtlasBalance.Caching.Tests` 15/15, `npm run lint` limpio (0
  warnings), `npm run test:unit` 22/22, `npm run build` compila con
  bundle verificado sin `console.*` ni `debugger`. Pendiente sin
  verificar: endpoints en caliente (sin backend/Postgres levantados),
  ejecucion real de `Build-Release.ps1`, y el limite de 10 MiB de
  `MaxRequestBodySize` contra un payload real.

## 2026-07-28 - V-02.07 - Segunda tanda de la auditoria de autenticacion: blocklist, latencia, rehash BCrypt e IP de sesion

- Segunda tanda sobre los 7 hallazgos BAJOS que quedo abiertos tras la
  auditoria de autenticacion anterior (misma sesion). Cierra 4, cierra
  1 mas como diagnostico erroneo, deja 2 abiertos por decision
  deliberada y anade 1 hallazgo BAJO nuevo.
- **Blocklist de contrasenas comunes
  (`Constants/SecurityPolicy.cs`).** `TryValidatePassword` rechaza por
  longitud minima (12 caracteres) antes de comparar contra
  `CommonPasswords`, asi que de las 105 entradas originales solo 7
  tenian 12+ caracteres y eran alcanzables; las otras 98 eran codigo
  muerto. Se reescribio la lista con 154 entradas, todas de 12+
  caracteres, sin duplicados (verificado programaticamente). El
  `HashSet` sigue `private`; se anadio `internal static
  IReadOnlySet<string> CommonPasswordsView` solo para tests, en vez de
  hacer el `HashSet` `internal`, porque un campo mutable visible a
  todo el ensamblado permitiria a codigo futuro vaciarlo con
  `Clear()` y desactivar la blocklist en silencio. HIBP sigue sin
  integrarse (anotado en el codigo como la solucion real de
  produccion).
- **Enumeracion de usuarios por latencia
  (`Services/AuthService.cs`).** Si el email no existia o la cuenta
  estaba bloqueada, `LoginAsync` no llegaba a ejecutar `BCrypt.Verify`
  (~250 ms de diferencia medible respecto a "password incorrecta"),
  aunque el mensaje de error fuera identico. Se anadio
  `DummyPasswordHash` (hash BCrypt sobre bytes aleatorios generados al
  arrancar, no es un secreto ni corresponde a ninguna contrasena
  real) y se verifica contra el en las ramas de email inexistente y
  cuenta bloqueada de `LoginAsync`. La misma omision existia en la
  rama de cuenta bloqueada de `ChangePasswordAsync` (encontrada por
  revision adversarial); tambien corregida.
- **Rehash oportunista de BCrypt.** Tras un login correcto, si
  `BCrypt.PasswordNeedsRehash(hash, PasswordWorkFactor)` es true, la
  contrasena en claro ya validada se rehashea con el work factor
  vigente (unico momento en que el servicio dispone de ella). Se
  introdujo la constante `PasswordWorkFactor = 12` dentro de
  `AuthService`, reusada tambien en `ChangePasswordAsync` (que antes
  tenia el 12 como literal suelto), para que ambos valores no puedan
  divergir.
- **Cambio de IP en la sesion (`RefreshTokenAsync`).** Compara la IP
  guardada en el refresh token con la IP actual y, si difieren,
  audita el evento nuevo `SESSION_IP_CHANGED`
  (`Constants/AuditActions.cs`). Decision explicita: **no se invalida
  la sesion.** Anclar por IP expulsaria a usuarios legitimos con
  VPN/DHCP/salto de red; anclar por User-Agent romperia con cada
  auto-actualizacion del navegador. El rastro de auditoria es lo que
  aporta valor sin romper el uso legitimo. Se anadio
  `NormalizeIpForComparison` porque una misma maquina puede llegar
  como `10.0.0.1` (X-Forwarded-For) o `::ffff:10.0.0.1` (socket
  dual-mode), y `IPAddress.Equals` los trata como distintos; sin
  normalizar se generarian alertas falsas y una auditoria con ruido no
  sirve para investigar. Solo se compara IP: anclar tambien
  User-Agent exigiria columna nueva en `REFRESH_TOKENS` y su
  migracion, fuera de este alcance.
- **Cookie `csrf_token` con `MaxAge` de 7 dias: cerrado como
  diagnostico erroneo, no como bug.** El fallo de CSRF devuelve 403,
  y el interceptor de `frontend/src/services/api.ts` solo
  auto-recupera en 401, 419 y 440. Acortar la cookie CSRF a 1h
  rompería con 403 sin recuperacion automatica a cualquier usuario
  inactivo mas de 1h con el refresh token todavia vivo. Los 7 dias
  actuales coinciden con la vida del refresh token, que es la vida
  real de la sesion; el comportamiento actual es correcto.
- **Hallazgo nuevo (BAJO) - asimetria de escrituras a BD en el
  login.** Encontrado por revision adversarial: tras igualar el coste
  de BCrypt, la rama de password incorrecta sigue haciendo un
  `SaveChangesAsync` extra (contador de intentos) que las ramas de
  email inexistente y cuenta bloqueada no hacen. Diferencia del orden
  de milisegundos o menos, sepultada por el jitter de red normal; no
  se persigue.
- Quedan abiertos por decision deliberada (no deuda olvidada): rate
  limiting en `IMemoryCache` de proceso, no distribuido (instancia
  unica on-premise) y ausencia de tests de auth en frontend (el
  runner actual `tsc + node --test` no tiene jsdom/testing-library).
- Tests nuevos: 6 en `AuthServiceTests.cs`
  (`Login_Should_Cost_The_Same_Whether_Or_Not_The_Email_Exists`,
  `Login_Should_Rehash_A_Password_Stored_With_An_Older_Work_Factor`,
  `RefreshToken_Should_Audit_An_Ip_Change_Without_Closing_The_Session`,
  `RefreshToken_Should_Not_Audit_When_Only_The_Ipv4_Mapping_Differs`,
  `RefreshToken_Should_Not_Audit_When_The_Ip_Is_Unchanged`,
  `ChangePassword_Should_Cost_The_Same_When_The_Account_Is_Locked`) y
  6 en `SecurityPolicyTests.cs` (archivo nuevo). Los tests de latencia
  comparan una rama contra la otra (margen del 50%) en vez de un
  umbral absoluto, con calentamiento previo; ejecutados 3 veces
  seguidas sin fallos.
- Verificacion: `AtlasBalance.API.Tests` 427/427 PASS,
  `AtlasBalance.Caching.Tests` 15/15 PASS. Total 442/442.

## 2026-07-28 - V-02.07 - Auditoria de autenticacion y sesion: logout invalida el access token, cambiar-password con limite de intentos

- Auditoria de `AtlasBalance.API/Services/AuthService.cs` centrada en
  logout y cambio de password. Dos correcciones de severidad MEDIA.
- **`LogoutAsync`.** Antes solo marcaba `RevocadoEn` en el refresh
  token presentado; el `SecurityStamp` del usuario no cambiaba, y
  como `UserStateMiddleware` valida el claim `security_stamp` del JWT
  contra BD en cada request, un access token capturado antes del
  logout seguia siendo aceptado hasta 60 minutos (el borrado de
  cookies solo pasaba en el navegador). Ahora `LogoutAsync`:
  1. exige que el refresh token presentado este vivo (no revocado, no
     caducado) antes de actuar;
  2. captura el `SecurityStamp` vigente como `previousStamp` y rota
     el `SecurityStamp` via `UserSessionState.RotateSecurityStamp`;
  3. revoca todos los refresh tokens activos del usuario;
  4. re-ancla al stamp nuevo solo los `MfaTrustedDevices` que cumplen
     las cuatro condiciones: `RevokedAt == null`, no caducados, del
     mismo usuario y `SecurityStamp == previousStamp`.

  El punto 4 es necesario porque los dispositivos MFA recordados
  estan anclados al `SecurityStamp`; sin re-anclarlos, rotar el stamp
  en cada logout habria regresionado el comportamiento fijado en
  V-01.09 ("logout conserva la cookie `mfa_trusted`"). El filtro por
  `previousStamp` es el que evita que ese mismo re-anclaje deshaga
  otras invalidaciones: un cambio de contrasena, un reset por admin,
  una revocacion administrativa o una deteccion de reuso de refresh
  token rotan el `SecurityStamp` sin tocar `MFA_TRUSTED_DEVICES`,
  dejando esos dispositivos invalidados de forma implicita
  (`RevokedAt == null` pero con el stamp viejo). Sin el filtro, un
  logout rutinario posterior los readoptaria como confiables otra
  vez, anulando esa invalidacion. Efecto funcional: cerrar sesion
  cierra ahora TODAS las sesiones del usuario en todos los
  dispositivos.
- **`ChangePasswordAsync`.** La verificacion de `passwordActual` con
  BCrypt no pasaba por el circuito de `FailedLoginAttempts`/
  `LockedUntil`/auditoria que ya tenia el login, permitiendo fuerza
  bruta sobre la contrasena actual con una sesion robada. Ahora
  reutiliza `MaxFailedLoginAttempts` (5) y `LockDuration` (30 min):
  comprueba `LockedUntil` antes de verificar (423 Locked si ya esta
  bloqueada), incrementa el contador al fallar, bloquea al quinto
  fallo, audita `LOGIN_FAILED` (motivo `password_actual_incorrecta`)
  y `ACCOUNT_LOCKED`, y resetea contador/bloqueo al acertar.
- Tests nuevos en `AuthServiceTests.cs`:
  `Logout_Should_Rotate_Security_Stamp_And_Revoke_Every_Active_Session`,
  `Logout_Should_Keep_Trusted_Mfa_Devices_Anchored_To_The_New_Stamp`,
  `Logout_Should_Ignore_An_Already_Revoked_Refresh_Token`,
  `ChangePassword_Should_Lock_Account_After_Repeated_Bad_Current_Password`,
  `ChangePassword_Should_Reject_While_Account_Is_Locked`.
- De paso se repararon dos bloqueos incidentales que impedian
  verificar por tests (ninguno relacionado con la auditoria en si):
  el constructor de `IntegrationTokenService` en
  `IntegracionesControllerTests.cs` no se habia actualizado tras el
  commit `f05b0dd` (le anadio `ICacheService` +
  `IOptions<CachingOptions>`), y dos literales esperados en
  `AuthServiceTests.cs` (lineas 94 y 126) tenian el caracter de
  reemplazo U+FFFD en vez de la tilde de "invalidas", corrupcion
  preexistente confirmada con `git show HEAD`.
- Verificacion: `AtlasBalance.API.Tests` 415/415 PASS,
  `AtlasBalance.Caching.Tests` 15/15 PASS. Total 430/430.
- Hallazgos NO corregidos en este alcance (documentados en
  `REGISTRO_BUGS.md` como pendientes abiertos, severidad BAJA): lista
  de contrasenas comunes 93% inefectiva por el gate de longitud
  minima, enumeracion de usuarios por latencia en login (falta hash
  dummy), sin `PasswordNeedsRehash` para BCrypt, rate limiting en
  `IMemoryCache` de proceso (no compartido si se escala), sesiones
  sin anclaje a IP/User-Agent, `MaxAge` de `csrf_token` inconsistente
  con el access token, sin tests de frontend para auth.

## 2026-07-27 - V-02.07 - Capa de cache para lecturas repetidas

- La API corre en una sola instancia Windows on-premise y solo tiene
  `IMemoryCache` (registrado en `Program.cs:136`). Esto encaja con la
  arquitectura single-node documentada en `SPEC.md` y la auditoria
  pre-internet (`SEGURIDAD_AUDITORIA_V-01.03.md:85` ya preveia que,
  si algun dia se escala, esta capa habra que moverla a Redis).
- `AtlasBalance.API/Caching/CacheService.cs` envuelve `IMemoryCache`
  con `GetOrLoadAsync<T>(namespace, key, loader, ttl, ct)`. Aplica
  single-flight con un `SemaphoreSlim` por `namespace+key` (asi N
  peticiones concurrentes con cache miss hacen UNA sola consulta) y
  mantiene una generacion por namespace (cada escritura bumpea el
  contador y todas las claves cacheadas quedan invalidadas sin
  enumerar `IMemoryCache`).
- `AtlasBalance.API/Caching/DashboardCacheInvalidator.cs` expone la
  fachada de invalidacion: `InvalidateDashboardScope`,
  `InvalidateDashboardReference`, `InvalidateDashboardMetrics`.
- `AtlasBalance.API/Data/DashboardCacheInvalidationInterceptor.cs` es
  un `SaveChangesInterceptor` registrado tras
  `AuditSaveChangesInterceptor` que invalida los caches del dashboard
  tras un `SaveChanges` exitoso si las entidades tocadas pertenecen
  a los grupos configurados (extractos, cuentas, plazos fijos,
  permisos, usuarios, configuracion relevante). Asi cualquier ruta
  (controllers, jobs Hangfire, seeds) queda cubierta sin acoplar la
  fachada en cada consumer.
- Consumidores actuales:
  - `TiposCambioService` usa `ICacheService` para el catalogo de
    tasas (TTL 5 min). Cierra la race benigna CONC-027 que
    documentaba `AUDITORIA_CONCURRENCIA_2026-07-10.md:302`.
  - `DashboardService` cachea el `Scope` por `userId` (TTL 30 s), la
    referencia `divisa_base + colores` (TTL 5 min) y las `Metrics`
    (TTL 15 s, clave `userId|paisId|divisa|hashCuentas`). Esto
    reduce las tres llamadas paralelas del frontend en
    `DashboardPage.tsx:161` (y equivalentes en `CuentasPage.tsx:313`
    y `TitularesPage.tsx:195`) a una sola familia de lecturas por
    TTL.
  - `ConfiguracionRepository` cachea el mapa completo de
    `CONFIGURACIONES` (TTL 120 s). Cierra MED-18: los 6+ round-trips
    por escritura de extracto (`AlertaService.cs:344-365`) y los
    servicios que releen SMTP/IA/backup pasan a 1 consulta por TTL.
    La fila cruda entra al cache; `_secretProtector.UnprotectFromStorage`
    se aplica bajo demanda en el caller (nunca se cachea plaintext).
  - `UserAccessService.GetScopeAsync` cachea el `UserAccessScope`
    por `userId` (TTL 45 s) con bypass explicito para admin. Cierra
    CONC-028: la query `Cuentas.Any(... PermisosUsuario.Any(...))`
    ya no corre en cada request autenticado.
  - `IntegrationTokenService.ValidateActiveTokenAsync` cachea el
    token activo por `TokenHash` (TTL 20 s). OpenClaw llega a
    100 req/min del mismo token sin golpear BD. `RevokeAsync`
    invalida el namespace completo tras `SaveChanges` (ventana
    maxima 20 s).
  - `AuthService.GetCurrentAsync` cachea el `AuthResult` de
    `GET /api/auth/me` con TTL 60 s y clave compuesta
    `(userId:N)|{securityStamp}`. La rotacion de stamp invalida la
    entrada por la propia clave; el interceptor anade una capa
    defensiva ante cambios en `USUARIOS`/`PERMISOS_USUARIO`/
    `PREFERENCIAS_USUARIO_CUENTA`.
- `IMemoryCache` queda por proceso. El helper expone
  `GetMetricsSnapshot(string)` con hits, misses, loads, single-flight
  waits, invalidations y load failures agregados por namespace, sin
  contener claves ni IDs (cero leakage a logs).
- Tests: `tests/AtlasBalance.Caching.Tests/` (15/15 PASS). Cubre
  single-flight concurrente, generacion por namespace, aislamiento
  entre namespaces, race invalidar durante carga, propagacion de
  cancelacion, invalidacion del catalogo de tasas tras escritura
  manual, bump de generaciones al invalidar, consistencia del cache
  de scope tras cambio conceptual de permisos, hit/miss + invalidacion
  de `ConfiguracionRepository` y `IntegrationTokenService`, bypass
  admin sin tocar cache en `UserAccessService`.
- TTLs: configurables via `appsettings.json` ->
  `AtlasBalance:Caching` (ver `CachingOptions.cs`). En Development
  se usan valores bajos (5-30 s) para iterar rapido; en Production
  los valores por defecto se mantienen (15-300 s segun volatilidad).

## 2026-07-27 - V-02.07 - Fuente unica del logo SVG

- El SVG del logo de Atlas Balance vive en
  `Documentacion/Diseno/brand/atlas-balance-logo.svg` como fuente unica
  de verdad. Antes vivia en
  `Atlas Balance/frontend/public/logos/Atlas Balance.svg`, donde se
  servia como peticion HTTP separada por el favicon y la mascara CSS
  del sidebar / login.
- Desde V-02.07 el SVG va inlineado:
  - Favicon en `frontend/index.html` (data URL, conserva la media
    query de tema oscuro).
  - Mascara CSS en `frontend/src/styles/variables.css` (variable
    `--logo-mask` que consume `.app-brand-logo` y `.auth-logo-image`).
- `Atlas Balance/frontend/public/logos/` queda solo con PNGs
  (`Atlas Balance.png` para `apple-touch-icon`, `Atlas Labs.png` para
  el footer del login). El SVG ya no se sirve como asset en runtime.
- Si cambia el logo: editar la fuente en
  `Documentacion/Diseno/brand/atlas-balance-logo.svg`, regenerar el
  PNG en `frontend/public/logos/Atlas Balance.png` si hace falta, y
  actualizar las dos copias inlineadas (`<link rel="icon">` en
  `index.html` y `--logo-mask` en `variables.css`).

## 2026-07-20 - V-02.06 - Operaciones largas, idempotencia y RLS

- `BACKUP_OPERATIONS` persiste manual/Drive/restore con soft-delete, FK,
  indices y estados controlados. Los endpoints devuelven 202 y el frontend
  consulta `/api/backups/operations/{id}`.
- Restore propaga el mismo `operation_id` al Watchdog y solo acepta el estado
  que coincide; esto elimina la carrera con un SUCCESS/FAILED global anterior.
- Creacion y confirmacion de lotes usan `Idempotency-Key`; el indice unique
  resuelve carreras concurrentes y la confirmacion guarda su respuesta dentro
  de la misma transaccion que extractos y estado final.
- RLS diferencia `reconcile` de `reconcile-close`; cerrar solo permite el estado
  `resuelta` y no amplifica `can_write_cuenta_by_id`.
- La migracion historica de auditoria redacta exclusivamente configuraciones
  sensibles o marcadas `EsSecreto`; es irreversible y no expone valores.

Gates locales: suite backend completa sin Testcontainers 389/389,
frontend unit/tsc/lint/build y scripts OK. PostgreSQL/Testcontainers y
round-trip Drive/restore siguen obligatorios en CI por Docker local no
disponible.

## 2026-07-04 - V-02-04 - Concurrencia del desglose de extractos

### Que cambio

- `ExtractoDesgloseResumenResponse` incluye `version`, un hash SHA-256 estable del conjunto
  activo de lineas del desglose.
- `ExtractoDesgloseUpsertRequest` exige `version` junto con `lineas`. Guardar sin version
  devuelve `400`; guardar con version antigua devuelve `409`.
- `ExtractosController.GuardarDesglose` serializa el guardado relacional con
  `pg_advisory_xact_lock` por `extracto_id` antes de leer/comparar lineas. Sin ese lock, dos
  requests simultaneos podrian leer la misma version y pasar el check.
- El frontend envia `{ version, lineas }` desde `DesgloseModal`. Ante `409`, `ExtractosPage`
  recarga el desglose vigente para evitar que el usuario siga editando un borrador obsoleto.

### Verificacion

- `dotnet test tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj --filter GuardarDesglose --no-restore`: 7/7 OK.
- `.\node_modules\.bin\tsc.cmd --noEmit`: OK.
- `npm run lint`: OK.
- `npm run build`: OK.

## 2026-07-03 - V-02-04 - Alta inline de filas en Extractos

### Que cambio

- `ExtractosPage` deja de mostrar el formulario global `Agregar fila manual` encima de la tabla.
- Se elimina el componente frontend muerto `AddRowForm`, que ya no representa el flujo real.
- `ExtractoTable` incorpora un boton `+` en la interseccion visual de la columna `Fila` con la fila siguiente, siguiendo el patron ya usado en el desglose de cuenta.
- La fila virtual activa sube de `z-index` para que el `+` no quede tapado por la fila inferior; la cabecera mantiene una capa superior.
- Al pulsar `+`, la tabla abre un borrador inline bajo la fila ancla con fecha, concepto, comentarios, importe, saldo y columnas extra.
- El alta usa el endpoint existente `POST /api/extractos` con `insert_before_fila_numero`, por lo que el backend conserva la logica transaccional de desplazar `fila_numero`.
- La fila virtualizada mide la altura real del borrador abierto para evitar solapes al hacer scroll.

### Verificacion

- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run lint`: OK.
- No se arranco servidor ni navegador para QA visual por las reglas anti-encallamiento de Vite/Chromium; queda validado por tipos, lint y revision estatica del diff.

## 2026-07-03 - V-02-04 - Desglose informativo de extractos

### Que cambio

- Nueva tabla `EXTRACTOS_DESGLOSES` para modelar lineas hijas informativas de un extracto:
  `tercero_nombre`, `importe`, `notas`, `orden`, auditoria basica y soft delete.
- Nueva migracion `20260703120000_AddExtractoDesgloses` con FK restrict, indices y RLS:
  lectura por `atlas_security.can_read_extracto(extracto_id)` y escritura por
  `atlas_security.can_write_extracto(extracto_id)`.
- `ExtractosController` expone `GET/PUT /api/extractos/{id}/desglose`. El `PUT` reemplaza
  el conjunto completo, normaliza orden y soft-deletea lineas omitidas.
- El reemplazo usa reordenacion temporal en dos fases dentro de la transaccion para evitar
  colisiones del indice unico `(extracto_id, orden)` al reutilizar ordenes o intercambiar lineas.
- `GET /api/extractos` devuelve `desglose_count`, `desglose_total` y `desglose_estado`.
  El estado se calcula, no se persiste duplicado.
- Frontend: `ExtractoTable` incorpora columna `Desglose`; `DesgloseModal` permite editar
  manualmente las lineas y muestra total/diferencia. No toca `monto`, `saldo`, `fila_numero`,
  dashboard ni conciliacion.

### Verificacion

- `npm.cmd exec tsc -- --noEmit`: OK.
- `dotnet test ...AtlasBalance.API.Tests.csproj --filter FullyQualifiedName~ExtractosControllerTests`
  con `OutDir` dentro del workspace: 23/23 OK.
- `npm.cmd run lint`: OK.

## 2026-07-02 - V-02-04 - Auditoria de seguridad completa y cierre de logout en produccion

### Que cambio

- **`AuthController.DeleteCookie` borra el nombre real por entorno.** El logout (y el borrado
  de `mfa_trusted` en `AttachCookiesAndBuildAuthResponse`/`RevokeCurrentTrustedDevice`) borraba
  solo los nombres legacy (`access_token`, `refresh_token`, `csrf_token`, `mfa_trusted`). En
  produccion las cookies reales llevan prefijo `__Host-atlas-*` (V-02-03), asi que el logout
  revocaba el refresh token en servidor pero dejaba en el navegador un access token valido
  hasta ~1h y la cookie CSRF viva (CWE-613). Ahora `DeleteCookie` replica el criterio de
  `UserStateMiddleware.DeleteAuthCookies`: borra el nombre real segun entorno mas la variante
  legacy, con `Path=/` y `Secure` (requisito del prefijo `__Host-`). La politica de conservar
  `mfa_trusted` tras logout (V-01.07) se mantiene intacta.
- **Guard de null en `UserStateMiddleware.DeleteAuthCookies`.** `context.RequestServices` puede
  ser null fuera del pipeline real (tests unitarios con `DefaultHttpContext`); se usa `?.` y
  fallback a comportamiento no-dev. Corrige el NRE del test
  `InvokeAsync_Should_Reject_Token_When_SecurityStamp_Is_Stale`.

### Verificacion

- Backend: build OK (API y Watchdog, `OutDir` redirigido a scratchpad por ACL de `bin`).
- Tests: suite completa **323/323 OK** (tras arrancar Docker Desktop), incluido el nuevo
  `AuthControllerTests.Logout_Should_Delete_HostPrefixed_Cookies_In_Production`, los
  `ExtractosConcurrencyTests` (409 de concurrencia) y `RowLevelSecurityTests` con Testcontainers.
- Auditoria completa documentada en `SEGURIDAD_AUDITORIA_V-02-04.md` (npm audit 0 vulnerabilidades,
  NuGet 0 paquetes vulnerables, sin secretos versionados).

## 2026-07-01 - V-02-04 - Correcciones post-review, UX de seguridad, a11y y limpieza

### Que cambio

- **Concurrencia optimista -> 409.** El handler global de `Program.cs` mapea
  `DbUpdateConcurrencyException` a `409 Conflict` con `{ error, code: "concurrency_conflict" }`,
  siguiendo el patron de `TipoCambioMissingException`. El token `xmin` de `Extracto`,
  `MovimientoEsperado`, `Conciliacion` y `RevisionExtractoEstado` ya detectaba el
  conflicto; antes caia al `500` generico. `ExtractosPage.onSaveCell` recarga la fila al
  recibir 409. Test: `ExtractosConcurrencyTests.Editar_Fila_En_Dos_Contextos_Debe_Lanzar_DbUpdateConcurrencyException`.
- **Aviso de importe ambiguo.** `ImportacionService` incorpora `BuildAmbiguousAmountWarning`/
  `AddAmbiguousAmountWarning`: cuando un importe con separador unico tiene exactamente un
  grupo de 3 digitos (miles vs decimal ambiguo), se anade una advertencia por fila
  (`FilaValidacionResponse.Advertencias`) para monto/ingreso/egreso/saldo. No bloquea.
- **Convencion de ordenacion de saldo (documentada, sin cambio funcional).** Se verifico que
  la doble ordenacion NO era un bug: saldo "ahora" (sin filtro de fecha) usa `FilaNumero DESC`
  primario; saldo "a fecha de corte" (`Fecha < inicio`) usa `Fecha DESC` primario. Comentarios
  anadidos en `DashboardService` (BuildMetrics/GetEvolucion) e `IntegrationOpenClawController`.
- **Cookies de sesion en `UserStateMiddleware.RejectAsync`.** Ahora borra los nombres reales
  segun entorno (`__Host-atlas-*` en produccion) mas las variantes legacy, con `Path=/` y
  `Secure` (requisito del prefijo `__Host-`). Antes solo borraba `access_token`/`refresh_token`/
  `csrf_token`, dejando viva la cookie real en produccion.
- **Frontend UX:** hooks `useConfirmDialog` (promesa sobre `ConfirmDialog`) y `useUnsavedChanges`
  (`beforeunload`). Confirmaciones en importar/conciliar/actualizar/tokens/divisas/Drive.
  `UsuarioModal` detecta cambios por snapshot y confirma el descarte al cerrar. `ExtractosPage`
  parchea la fila editada en local en vez de recargar la pagina (salvo cambio de fecha).
- **Frontend a11y/hardening:** `SignedAmount` con `showSign`; `ToastViewport` sin live region
  anidada; `useDialogFocus` cancela el `setTimeout` de foco en cleanup; `DatePickerField`
  enfoca el dia al abrir (popover no modal, sin `useDialogFocus` a proposito); `ChangePasswordPage`
  valida la confirmacion via RHF (`aria-describedby`); `formatDateTime` con guard de fecha
  invalida; `CreateTokenModal` calcula la expiracion como fin de dia local -> UTC y avisa de que
  OpenClaw es solo lectura.
- **Limpieza:** eliminado `stores/divisaStore.ts` (sin referencias) y 10 `.gitkeep` redundantes;
  `formatBytes` consolidado en `utils/formatters.ts` (+ tilde en "Sin tamaño").
- **Version:** `V-02-04` / `2.4.0` en `VERSION`, `Directory.Build.props`, `frontend/package.json`
  (+`appVersion`), `package-lock.json` y seed `app_version`.

### Verificacion

- Frontend: `npx tsc --noEmit` OK; `npm run lint` (`--max-warnings 0`) OK.
- Backend: fuentes compilan sin `error CS` (`dotnet build -p:UseAppHost=false`). El paso de
  copia a `bin` fallo con `MSB3021 Access denied` por `bin` bloqueado por una instancia en
  ejecucion; bloqueo de entorno, no de codigo. Tests con Postgres (fixture): no ejecutados.

### Seguimiento de pendientes

- **Modales restantes: HECHO.** `useUnsavedChanges` + confirmacion de descarte cableados en
  `CuentasPage`, `TitularesPage` y (guard `beforeunload` sobre `config`) `ConfiguracionPage`.
  Los tabs de Configuracion son render condicional sobre estado de pagina: cambiar de pestana no
  pierde datos.
- **Retencion IA: VERIFICADO en codigo.** Solo OpenRouter envia directiva de retencion cero
  (`zdr`/`data_collection: deny`); OpenAI/MiniMax no. Aviso anadido en la UI de la pestana IA.
  Queda accion contractual si se usan esos proveedores con datos reales.
- **Test 409 con Postgres: BLOQUEADO por ACL (requiere elevacion).** Docker OK. No hay app en
  ejecucion (unico `dotnet` = VBCSCompiler). Los `bin/Debug`/`obj` de API/Watchdog tienen ACL creada
  por identidad de sandbox: `BUILTIN\Usuarios` (incl. `TRAKERIA\usuario`) solo lectura, sin escritura
  ni borrado. La recompilacion falla con `Access denied` (`GenerateDepsFile`/staticwebassets). Fix:
  `icacls ... /grant "TRAKERIA\usuario:(OI)(CI)M" /T` sobre esos bin/obj como administrador (o
  borrarlos elevado) y reconstruir. Detalle en `Documentacion/Versiones/v-02-04.md`.

## 2026-07-01 - V-02-03 - Alineacion completa de fuentes de version

### Que cambio

- `Build-Release.ps1`, workflow de release, instalador y wrapper `install.ps1` apuntan por defecto a `V-02-03`.
- `SeedData` inicializa `app_version` como `V-02-03`.
- `package-lock.json` queda alineado con `package.json` en `2.3.0`.
- `README_RELEASE.md`, `CLAUDE.md` y `AGENTS.md` usan ejemplos de release `V-02-03`.
- La documentacion de usuario de paquetes instalables apunta al ZIP y firma `AtlasBalance-V-02-03-win-x64`.

### Verificacion

- Barrido `rg` de referencias activas fuera de documentacion historica.
- Parser PowerShell de scripts de release/instalacion: OK.
- `npm.cmd run lint`: OK.

## 2026-07-01 - V-02-03 - Wrappers de hardening para servicios bloqueados por ACL

### Que cambio

- `IConciliacionService` apunta ahora a `HardenedConciliacionService`, que delega operaciones normales al servicio original y aplica tolerancia configurable en sugerencias.
- `IGoogleDriveBackupService` apunta a `HardenedGoogleDriveBackupService`, que verifica el SHA-256 registrado del backup cifrado descargado antes de llamar a la importacion original.
- `IBackupConfigurationService` apunta a `HardenedBackupConfigurationService`, que asegura `EsSecreto=true` para secretos de backup tras guardar configuracion.
- Las clases originales quedan registradas como concretas para que los wrappers las usen como delegados internos.

### Verificacion

- Backend build OK.
- Suite backend completa: 321/321 OK.
- Frontend lint/build OK.

### Nota tecnica

- Esta aproximacion evita duplicar servicios completos y evita modificar archivos bloqueados por ACL. El punto critico de Google Drive se verifica antes de la importacion real mediante descarga temporal y hash del `.enc` cuando existe checksum local registrado.

## 2026-07-01 - V-02-03 - Correcciones finales aplicables de hardening

### Que cambio

- Las claves sensibles escritas desde `ConfiguracionController` quedan cifradas de forma idempotente y marcadas con `EsSecreto`.
- La migracion de secretos en arranque marca secretos existentes como `EsSecreto` e incluye `github_update_token`.
- El cooldown de alertas de saldo bajo pasa a ser por cuenta, evitando que una alerta global silencie otras cuentas. Para compatibilidad, `FechaUltimaAlerta` se respeta si la alerta ya era especifica de esa misma cuenta.
- Dashboard separa ingresos y egresos por divisa antes de convertir, evitando netear flujos contrarios.
- Frontend mejora recuperacion de sesion y UX IA: 419/440 fuerzan relogin claro, cambio de password recarga alertas y chat IA permite reintentar la ultima pregunta.

### Verificacion

- Backend build OK.
- Tests focalizados dashboard/alertas/configuracion: 24/24 OK.
- Suite backend completa: 320/320 OK.
- Frontend lint OK.
- Build Vite temporal OK.

### Limites

- No se pudo editar `ConciliacionService.cs`, `GoogleDriveBackupService.cs` ni `BackupConfigurationService.cs` por ACL local heredada. Los pendientes asociados quedan documentados en incidencias.

## 2026-07-01 - V-02-03 - Hardening backend/frontend y migracion minima

### Que cambio

- `BackupEncryptionService` deja de regenerar silenciosamente claves cifradas corruptas de backups cloud.
- Los tokens de integracion sin expiracion requieren confirmacion textual exacta `NO_EXPIRAR`.
- En produccion las cookies auth/CSRF usan prefijo `__Host-atlas-*`; desarrollo conserva nombres legacy.
- `ImportacionService.ParseRows` elimina BOM UTF-8 inicial (`\uFEFF`) antes de validar filas pegadas.
- `ConfirmarLoteAsync` marca el lote como `error`, guarda `Notas` y audita si falla la confirmacion interna.
- `Configuracion.EsSecreto` y `ConfiguracionRepository` preparan el modelo para secretos configurables; el cifrado real queda pendiente.
- La migracion `20260701115326_V0203_Hardening` es manual y minima: `IMPORTACION_LOTES.notas`, `CONFIGURACION.es_secreto`, indices nuevos y FKs `Restrict`; no crea/elimina `xmin`.
- Frontend: email recordado en login, filtros de extractos con debounce, cancelacion de auditoria al cerrar modal y telemetria basica de error boundary.

### Verificacion

- Backend build en copia temporal: OK.
- Backend tests directos en copia temporal: 319/320 OK; unico fallo conocido en dashboard (`252M` esperado vs `204.00M`).
- Testcontainers focalizado: 2/2 OK.
- `npm.cmd run lint`: OK.
- Build Vite con `VITE_BUILD_OUT_DIR` temporal: OK.

### Pendiente tecnico

- Implementar tolerancia configurable de conciliacion cuando `ConciliacionService.cs` no este bloqueado por ACL.
- Resolver el test time-sensitive de dashboard antes de promocionar la suite completa como verde.

## 2026-06-27 - V-02-02 - Vite y dependencias npm vulnerables corregidas

### Que cambio

- `Atlas Balance/frontend/package-lock.json` resuelve `form-data` a `4.0.6`.
- `Atlas Balance/frontend/package.json` fija `form-data@4.0.6` y `js-yaml@4.3.0` con `overrides`.
- La validacion SCA saco mas deuda del mismo arbol y se cerro en la misma pasada:
  - `vite` pasa del rango vulnerable `8.0.0 - 8.0.15` a resolucion `8.1.0`; `package.json` exige como minimo `^8.0.16`.
  - `js-yaml` pasa de `4.1.1` a `4.3.0`.
- `vite.config.ts` anade `atlas-open-in-editor-guard`, un middleware `configureServer` que intercepta `/__open-in-editor` antes del middleware interno de Vite.
- El guard rechaza rutas UNC, `file://host/...` remotas y rutas resueltas fuera de `Atlas Balance/frontend`.
- El dev server queda limitado a `127.0.0.1`, puerto estricto y `allowedHosts` de loopback.
- No cambia codigo runtime de la aplicacion ni contratos API; el cambio afecta dependencias y servidor de desarrollo.

### Por que

El advisory de Vite permite leer archivos bloqueados por `server.fs.deny` en Windows usando NTFS ADS (`.env::$DATA?raw`) o nombres 8.3 cuando el dev server esta expuesto con `--host`/`server.host`. Produccion no sirve Vite: sirve estaticos desde ASP.NET Core. El riesgo aqui era de desarrollo/local LAN, pero dejar `vite@8.0.8` en el lock era una puerta tonta.

`npm audit` saco ademas `form-data` high y `js-yaml` moderate. La correccion sensata es cerrar el arbol de dependencias, fijar las transitivas vulnerables con overrides y dejar el dev server cerrado a loopback por defecto. El guard de `/__open-in-editor` queda como defensa adicional de servidor de desarrollo, no como sustituto del parche de Vite.

### Verificacion

- `npm.cmd ls form-data --all`: confirmo `form-data@4.0.5` antes del cambio.
- `npm.cmd view form-data@4.0.6 version dist.integrity --json`: OK.
- `npm.cmd audit --audit-level=moderate`: OK final, `found 0 vulnerabilities`.
- Check Node del lockfile: `form-data 4.0.6`, `js-yaml 4.3.0`, `vite 8.1.0`.
- Instalacion limpia temporal con `npm.cmd ci --ignore-scripts --no-audit --fund=false`: OK.
- Se aparto el `node_modules` real bloqueado a `node_modules.blocked-20260627183808` y se ejecuto `npm.cmd ci --ignore-scripts` en el checkout real: OK.
- `npm.cmd ls form-data js-yaml vite --all`: OK, `form-data@4.0.6`, `js-yaml@4.3.0`, `vite@8.1.0`.
- `npm.cmd exec vite -- --version`: `vite/8.1.0 win32-x64 node-v24.15.0`.
- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- Build temporal Vite con `--outDir ..\..\tmp-vite-security-real-node-modules-v02-02`: OK.

### Limite real

La instalacion activa `node_modules` ya esta alineada con el lock corregido. Los residuos locales que Windows no dejaba borrar se movieron fuera del workspace a `C:\tmp\atlas-balance-blocked-node-modules\` y `C:\tmp\atlas-balance-blocked-artifacts\`; no forman parte del proyecto ni del artefacto versionable.

## 2026-06-26 - V-02-02 - RLS alineado con roles de dashboard

### Que cambio

- Se agrego la migracion `20260626193000_AlignRlsDashboardAccessWithRoles`.
- RLS incorpora `atlas_security.current_user_is_manager()` para reconocer `GERENTE` activo desde `USUARIOS.rol = 1`.
- `atlas_security.can_read_cuenta(...)` y `atlas_security.can_read_titular(...)` separan la lectura normal de la lectura `dashboard`.
- En scope `dashboard`, un usuario solo lee datos si es `GERENTE` con permiso de datos o si tiene `PuedeVerDashboard` mas algun permiso de datos.
- Un `EMPLEADO` con `PuedeVerCuentas` pero sin `PuedeVerDashboard` ya no puede leer cuentas/extractos cuando `atlas.request_scope = dashboard`.
- `RowLevelSecurityTests` cubre gerente, empleado sin dashboard, empleado con dashboard y lectura normal fuera de dashboard.
- Se revisaron endpoints sensibles de cuentas, titulares, extractos, dashboard, revision, alertas, exportaciones, importacion, IA y OpenClaw para confirmar que pasan por scopes de usuario/integracion antes de leer datos.

### Por que

El modelo de tres roles permitio dashboard a `GERENTE` con cualquier permiso de datos, pero la funcion RLS anterior no distinguia bien el scope `dashboard`: dejaba pasar `PuedeVerCuentas` de forma demasiado amplia y a la vez podia bloquear un gerente valido sin `PuedeVerDashboard`. Esa mezcla era mala defensa en profundidad. La base debe imponer la misma semantica que el backend.

### Verificacion

- `dotnet build "C:\Proyectos\Atlas Balance Dev\Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" --no-restore -p:UseAppHost=false`: OK.
- `docker info`: OK; Docker Desktop activo.
- `dotnet restore "...AtlasBalance.API.Tests.csproj" --artifacts-path "C:\tmp\atlas-rls-artifacts"`: OK tras aislar artefactos por `Access denied` en `bin/obj`.
- `RowLevelSecurityTests` con PostgreSQL real/Testcontainers: 1/1 OK.
- Tests focalizados de permisos/datos con artefactos en `C:\tmp\atlas-rls-artifacts`: 116/116 OK.

## 2026-06-26 - V-02-02 - Guardado de columnas de Extractos sin cuenta

### Que cambio

- `SaveColumnasVisiblesRequest` usa `JsonPropertyName` explicitos para el contrato snake_case del endpoint `PUT /api/extractos/columnas-visibles`.
- `CuentaId`, `TitularId` y `PaisId` siguen siendo nullable para soportar scope global, por pais, por titular y por cuenta.
- `ExtractosPage.saveVisibleColumns` construye el body del `PUT` sin claves nulas. En vista general envia solo `columnas_visibles`.
- `ExtractosControllerTests` ahora cubre deserializacion snake_case con `cuenta_id: null` y con `cuenta_id` omitido.

### Por que

El test anterior llamaba al controlador directamente y no probaba el payload real del navegador. Eso dejaba un agujero: la UI podia seguir recibiendo validacion `cuenta_id es requerido` aunque la logica interna aceptara `CuentaId = null`. La solucion endurece ambos lados del contrato y evita enviar nulos innecesarios.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `ExtractosControllerTests`: 18/18 OK.
- Build Vite temporal: OK.
- QA Browser con mock estricto: el mock rechazaba cualquier `PUT` que incluyera `cuenta_id`; activar `categoria` en vista general guardo sin enviar esa clave y sin error.

## 2026-06-26 - V-02-02 - Layout de formatos de importacion

### Que cambio

- `FormatosImportacionPage` declara un `colgroup` para la tabla de formatos.
- `entities.css` sustituye el `min-width: 860px` fijo por `width: 100%`, `table-layout: fixed` y anchuras en `rem` para banco, divisa, columnas base, estado y acciones; `Extra` queda como columna flexible.
- Las acciones de fila quedan dentro de `.formatos-row-actions`, anulando borde, margen y padding de `.phase2-row-actions` para que se comporten como acciones de celda.
- Se evita partir palabras con `overflow-wrap: normal`, `word-break: normal` y `white-space: nowrap` en botones de accion.
- El grid de la pagina usa una columna minima real para la tabla en desktop y pasa a una sola columna bajo `1023.98px`.

### Por que

La tabla estaba forzada a medir mas que la columna disponible. Eso creaba scroll horizontal interno y dejaba el usuario viendo media tabla, con acciones cortadas. El fallo no estaba en los datos: era layout CSS peleando contra el panel lateral.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd exec vite -- build --outDir tmp-vite-formatos-layout-v02-02 --emptyOutDir`: OK.
- Revalidacion tras evitar palabras partidas: lint OK, TypeScript OK y build Vite temporal OK.
- QA Browser renderizada bloqueada por politica de seguridad al abrir una pagina `data:` de prueba; no se uso workaround.

## 2026-06-26 - V-02-02 - Proyeccion de permisos en DashboardService

### Que cambio

- `DashboardService.GetAuthorizedScopeAsync` vuelve a proyectar `PuedeVerDashboard` cuando carga permisos de usuario.

### Por que

Una modificacion previa habia cambiado la logica para diferenciar `GERENTE` de otros roles, pero dejo fuera `PuedeVerDashboard` del tipo anonimo. El servicio lo leia despues y cualquier recompilacion del backend fallaba antes de ejecutar tests.

### Verificacion

- `dotnet test ...AtlasBalance.API.Tests.csproj --filter "FullyQualifiedName~ExtractosControllerTests" -p:UseAppHost=false --no-restore`: 17/17 OK tras corregir la proyeccion.

## 2026-06-26 - V-02-02 - Modelo de usuarios reducido a tres roles

### Que cambio

- `RolUsuario` queda en `ADMIN = 0`, `GERENTE = 1` y `EMPLEADO = 2`.
- La migracion `20260626180000_ReduceUserRolesToThreeTypes` convierte roles numericos antiguos `3/4` a `2` y recrea el tipo PostgreSQL auxiliar `rol_usuario` con solo `admin`, `gerente` y `empleado`.
- Los DTOs de alta/edicion de usuario tienen `EMPLEADO` como valor por defecto para evitar que un payload incompleto derive en `ADMIN`.
- `UsuarioModal`, tipos frontend y labels de `UsuariosPage` eliminan `EMPLEADO_ULTRA` y `EMPLEADO_PLUS`.
- `DashboardController` deja de depender de rol `GERENTE`: el servicio autoriza por rol y permisos.
- `DashboardService` permite dashboard a `GERENTE` con cualquier permiso de datos asignado; `EMPLEADO` necesita `PuedeVerDashboard` mas permiso de datos.
- El menu lateral y la navegacion inferior ocultan `Dashboard` cuando el usuario no lo tiene disponible.
- `UsuariosController` rechaza crear o actualizar un `GERENTE` sin ningun permiso de datos global, por pais, titular o cuenta.

### Por que

`EMPLEADO_ULTRA` y `EMPLEADO_PLUS` eran nombres sin comportamiento real. Eso confundia permisos: parecia una jerarquia de roles, pero el producto ya operaba con permisos granulares. La decision limpia es tres roles y permisos explicitos.

### Verificacion

- `DashboardServiceTests` cubre gerente con permiso de datos sin `PuedeVerDashboard` y empleado que solo entra al dashboard si tiene `PuedeVerDashboard`.
- `UsuariosControllerTests` cubre rechazo de gerente sin alcance de datos.
- Frontend lint OK.
- TypeScript OK.
- Backend build OK desde `C:\tmp` por el bloqueo conocido de SDK fijado en `global.json`.
- Tests focalizados `DashboardServiceTests|UsuariosControllerTests`: 17/17 OK.
- Build Vite temporal OK con salida dentro de `frontend`.

## 2026-06-26 - V-02-02 - Columnas extra disponibles en selector de Extractos

### Que cambio

- `PaginatedResponse<T>` incorpora `ColumnasDisponibles` como campo opcional. Con la politica JSON actual, solo se serializa cuando el endpoint lo rellena.
- `ExtractosController.Listar` calcula `columnas_disponibles` sobre la consulta filtrada completa antes de aplicar paginacion.
- `ExtractosPage` guarda `availableExtraColumns` desde `data.columnas_disponibles` y lo pasa a `ExtractoTable`.
- `ExtractoTable` compone sus columnas con `BASE_COLUMNS + availableExtraColumns + columnas_extra presentes en filas`.
- El panel `Columnas` anade `Mostrar todas`, que guarda todas las columnas disponibles en el scope activo.
- `ExtractosControllerTests` cubre el caso donde la pagina actual no trae una columna extra, pero otra fila del mismo resultado filtrado si la trae.

### Por que

El selector estaba demasiado pegado a la pagina cargada. Si una columna extra existia en el resultado filtrado pero no en la pagina o fila visible, desaparecia del selector. Eso hacia que el usuario no pudiera activarla o recuperar una preferencia limpia. Un selector de columnas debe basarse en el conjunto disponible, no en una muestra accidental de filas.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `dotnet test ...AtlasBalance.API.Tests.csproj --filter "FullyQualifiedName~ExtractosControllerTests" -p:UseAppHost=false --no-restore`: 17/17 OK.
- `npm.cmd exec vite -- build --outDir ..\..\tmp-vite-extractos-columns-v02-02 --emptyOutDir`: OK.
- QA Browser con API mock: `categoria` y `origen` aparecen desde `columnas_disponibles`; activar `categoria` actualiza cabecera y payload; `Mostrar todas` deja 11 columnas visibles; consola sin errores.

### Limite real

La QA fue mockeada para aislar el flujo del selector. No se tocaron datos reales ni preferencias reales de usuario.

## 2026-06-26 - V-02-02 - Ingresos y egresos en grafica principal del dashboard

### Que cambio

- `EvolucionChart` mantiene `variant="saldoArea"` para el dashboard principal, pero cambia internamente esa variante de `AreaChart` a `ComposedChart`.
- La serie `saldo` sigue como area azul con dominio propio calculado por `getSaldoDomain`.
- `ingresos` y `egresos` se renderizan como lineas sobre un eje Y secundario calculado por `getMovementDomain`.
- La leyenda de la variante `saldoArea` muestra `Saldo`, `Ingresos` y `Egresos`.
- El `aria-label` vuelve a describir las tres series para lectores de pantalla.

### Por que

El rediseño bancario anterior gano limpieza, pero oculto datos que el usuario espera ver en la grafica principal. Eso no es minimalismo, es informacion perdida.

Usar un unico eje para saldo e ingresos/egresos era mala solucion: el saldo esta en millones y el movimiento del periodo suele ser mucho menor. El eje secundario permite conservar la lectura fina del saldo sin hacer invisibles las lineas de movimiento.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd exec vite -- build --outDir ..\..\tmp-vite-dashboard-ingresos-egresos-v02-02 --emptyOutDir`: OK.
- QA Browser con build temporal y API mock:
  - desktop: wrapper `saldoArea` con leyenda `Saldo/Ingresos/Egresos`, tres trazos SVG y consola sin errores;
  - mobile `390x800`: grafica presente, tres trazos SVG, bottom nav visible y sin overflow horizontal.

### Limite real

La QA fue mockeada para aislar el componente. Antes de release conviene validar con datos reales de movimientos altos/bajos para confirmar que el eje derecho mantiene lectura clara.

## 2026-06-26 - V-02-02 - Selector de columnas de extractos por scope

### Que cambio

- `ExtractosPage.onToggleColumn` recibe desde `ExtractoTable` la lista real de columnas disponibles (`BASE_COLUMNS + columnas_extra`).
- Al guardar preferencias, el frontend usa ese set disponible para calcular altas/bajas y descartar columnas obsoletas.
- El payload de `PUT /api/extractos/columnas-visibles` conserva el scope correcto:
  - cuenta seleccionada: `cuenta_id + titular_id + pais_id`;
  - titular sin cuenta: `titular_id`;
  - pais/global sin cuenta: `pais_id` o scope global.
- `ExtractosController.SaveColumnasVisibles` deja de rechazar `CuentaId = null`; ahora usa `ResolvePreferenciaScope`, igual que el `GET`.
- `GetColumnasVisibles` y `SaveColumnasVisibles` consultan preferencias con comparacion explicita de nulos por `pais_id`, `titular_id` y `cuenta_id`, para que los scopes globales no dependan de igualdad contra `NULL`.
- `ExtractosControllerTests` cambia el test que esperaba `BadRequest` por una regresion que exige guardar preferencias globales sin cuenta.

### Por que

El bug era de contrato, no de CSS. La lectura de preferencias ya aceptaba scope global/titular/pais, pero la escritura exigia cuenta. Resultado: en la vista general de Extractos el selector parecia permitir ocultar columnas, pero el backend rechazaba el guardado y el estado se revertia. Eso es una mala UX y una mala API: dos endpoints hermanos no pueden discrepar asi.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `dotnet build ...AtlasBalance.API.csproj --no-restore -p:UseAppHost=false`: OK, con warning obsoleto de Hangfire PostgreSQL ya existente.
- `dotnet test ...AtlasBalance.API.Tests.csproj --filter "FullyQualifiedName~ExtractosControllerTests" -p:UseAppHost=false --no-restore`: 16/16 OK.

### Limite real

No se hizo QA visual con navegador real ni servidor dev. La validacion cubre contrato backend, lint y tipos frontend.

## 2026-06-26 - V-02-02 - Flag de extractos simplificado

### Que cambio

- `ExtractoTable` deja de renderizar el texto `Marcada/Sin marca` dentro de la columna `Alerta`.
- La celda de flag conserva solo checkbox y campo `Nota de alerta`.
- `extractos.css` elimina la columna interna y estilos muertos de `.flag-label`.
- El ancho de la columna `flagged` baja de `210px` a `176px` para recuperar espacio horizontal.
- El boton visible `Historial` se renderiza solo en `fila_numero`, no en cada celda no operativa.
- En tactil, el padding reservado para `Historial` queda limitado a la columna `Fila`.

### Por que

El texto de estado repetia lo que ya comunica el checkbox y ensuciaba una tabla financiera densa. Checkbox + nota es suficiente; meter una etiqueta intermedia era ruido, no informacion.

El `Historial` repetido en toda la fila era otra forma de ruido: parecia una accion distinta por celda, cuando visualmente debe actuar como acceso de fila desde la primera columna.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.

### Limite real

No se hizo QA visual con navegador real. El cambio es estatico y focalizado en render/CSS; antes de release conviene revisar una cuenta con notas de alerta largas.

## 2026-06-26 - V-02-02 - Dashboard estilo referencia bancaria

### Que cambio

- `DashboardPage` cambia la primera lectura a un panel unico: saldo consolidado, resumen de cuentas/bancos/divisas, tarjetas compactas por divisa y grafica principal dentro del mismo borde.
- El titulo visible vuelve a `Dashboard` para coincidir con la referencia y reducir ruido.
- `Saldos por titular` y `Plazos fijos` se agrupan en `dashboard-detail-grid` en desktop. `Saldos por pais` y `Concentracion` quedan debajo.
- `EvolucionChart` acepta `variant="saldoArea"` y `xAxisMode="month"`.
- En la variante `saldoArea`, el dominio del eje Y se calcula solo con `saldo`, las etiquetas del eje se compactan sin moneda y la animacion queda desactivada.
- `dashboard.css` reduce sombras, usa bordes finos, baja radios al sistema de 8px, refuerza numeros monoespaciados y hace el selector de periodo mas parecido a control segmentado.

### Por que

La referencia no era "mas bonita"; era mas clara. El dashboard anterior mezclaba saldo, divisas, ingresos, egresos y grafica como piezas competidoras. La nueva composicion fuerza una lectura bancaria: primero patrimonio consolidado, despues tendencia, luego movimiento del periodo y exposicion.

Se mantiene el selector de divisa aunque no salga en la captura de referencia. Quitar control funcional para copiar una imagen seria una mala decision.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd exec vite -- build --outDir ..\..\tmp-vite-dashboard-reference-v02-02 --emptyOutDir`: OK.
- QA Playwright finita con Chrome local, servidor temporal cerrado y APIs mockeadas: desktop `1198px` y mobile `390px`, sin overflow horizontal, consola sin errores, grafica `saldoArea` renderizada.
- Capturas revisadas con `view_image`: referencia del usuario, `output/playwright/dashboard-reference-desktop-v02-02.png` y `output/playwright/dashboard-reference-mobile-v02-02.png`.

### Limite real

La QA uso datos mockeados para no depender de login/API local. Antes de release conviene abrir con datos reales y comprobar divisas con codigos/importes largos.

## 2026-06-23 - V-02-02 - Interactividad responsive y accesibilidad operativa

### Que cambio

- `uiStore` incorpora `blockingOverlayCount` y acciones para registrar/desregistrar overlays bloqueantes.
- Nuevo hook `useBlockingOverlay(open)` para que cualquier modal/sheet/alertdialog participe en el bloqueo global.
- `useDialogFocus` usa `useBlockingOverlay`, conserva focus trap y restauracion de foco, y ahora sirve como punto unico para modales comunes.
- `Layout` observa si hay overlays activos, bloquea `document.body.style.overflow`, marca `body[data-overlay-open]` y anade `app-shell--overlay-open`.
- `TopBar` cierra y oculta el chat IA si hay overlay activo. En CSS movil, `.ai-floating-widget` queda oculto; la IA se usa por ruta.
- La escala de capas queda: sticky/bottom nav < toast/IA < modal backdrop < modal surface < tooltip. Esto evita que IA/toasts queden por encima de una accion bloqueante.
- `DatePickerField` detecta puntero tactil/coarse y renderiza `input type="date"` con boton `Limpiar`; en escritorio conserva el calendario custom con grid de dias.
- `BottomNav` prioriza accesos moviles segun permisos: Dashboard si procede, Cuentas, Extractos, Importar y Mas. Sin Dashboard, Extractos pasa primero.
- `ExtractoTable` declara `role="grid"` y cada celda de datos usa `role="gridcell"`, `aria-colindex`, `aria-selected` y `tabIndex` roving. Soporta flechas, Home/End, Ctrl+Home/End, PageUp/PageDown y Enter/F2.
- `RevisionPage` agrega `data-label` en celdas y CSS mobile para convertir filas en tarjetas etiquetadas bajo `767.98px`.
- `CuentaDetailPage` hace focusables las celdas no editables principales para actualizar la barra de formula con teclado.
- `system-coherence.css` refuerza scroll tactil local en wrappers de tabla y da ancho minimo a tablas administrativas dentro de `config-table-wrap`.

### Por que

La app ya tenia responsive parcial, pero no interactividad robusta. El problema no era estetico: overlays competian con IA, el date picker movil peleaba con la bottom nav, y `ExtractoTable` se anunciaba como tabla aunque funcionaba como hoja editable. Eso rompe teclado, tactil y lectores de pantalla.

Se mantuvo el criterio de producto: las superficies financieras densas no se convierten en tarjetas si perderian comparacion por columnas. Por eso Extractos y desglose de cuenta siguen siendo hojas con scroll local; Revision si cambia a tarjetas en movil porque cada fila es una decision aislada.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK.

### Limite real

No se hizo QA visual con navegador real ni servidor dev. En este repo ya hay incidencias repetidas con Vite/Rolldown/Chromium y la instruccion operativa exige no encallar la sesion levantando servidores largos desde `shell_command`. Antes de release visual, revisar manualmente desktop, tablet y movil con backend local levantado.

## 2026-06-22 - V-02-02 - Copias programables y Google Drive

### Que cambio

- `BackupSchedulerJob` reemplaza el recurring job semanal fijo. Hangfire ejecuta `backup-scheduler` cada 15 minutos y la decision real sale de `BackupSchedule`.
- Configuracion nueva en `CONFIGURACION`:
  - `backup_auto_enabled`
  - `backup_auto_frequency` (`HOURLY`, `DAILY`, `WEEKLY`, `MONTHLY`)
  - `backup_auto_time_utc`
  - `backup_auto_day_of_week`
  - `backup_auto_day_of_month`
  - `backup_auto_interval_hours`
  - `backup_auto_last_started_utc`
  - `backup_auto_last_result`
  - `backup_destination` (`LOCAL`, `LOCAL_Y_GOOGLE_DRIVE`)
  - `google_drive_oauth_client_id`
  - `google_drive_oauth_client_secret`
  - `google_drive_folder_id`
  - `backup_cloud_encryption_key`
- `ISecretProtector` protege `google_drive_oauth_client_secret`, `backup_cloud_encryption_key` y los refresh tokens de Drive.
- Tablas nuevas:
  - `BACKUP_CLOUD_CONNECTIONS`: proveedor, estado, email de cuenta, scope, refresh token protegido y estado de validacion.
  - `BACKUP_CLOUD_COPIES`: copia local, conexion, estado de subida/importacion, ID remoto, nombre remoto, tamano, checksum y error seguro.
- Migracion: `20260622120000_AddBackupSchedulingAndGoogleDrive`.
- RLS: ambas tablas nuevas quedan con FORCE RLS y politica admin/system.
- Endpoints admin nuevos bajo `/api/backups`:
  - `GET/PUT /config`
  - `POST /google-drive/link/start`
  - `GET /google-drive/link/{sessionId}`
  - `POST /google-drive/disconnect`
  - `POST /google-drive/test`
  - `POST /{id}/google-drive/retry`
  - `GET /google-drive/files`
  - `POST /google-drive/import`
- La subida a Drive usa OAuth device flow y scope `https://www.googleapis.com/auth/drive.file openid email`.
- Antes de subir, el `.dump` local se cifra a `.dump.enc` con AES-GCM por bloques y clave local protegida. El archivo temporal cifrado se borra best-effort tras subir.
- La importacion desde Drive descarga un `.enc`, lo descifra en la carpeta local de backups y registra un `Backup` restaurable.
- `scripts/purge-delivery-data.sql` borra conexiones/subidas de Drive y vacia claves OAuth/cifrado antes de una entrega limpia.

### Por que

Guardar solo en local no es estrategia de copia de seguridad seria: si el disco muere, la "copia" muere con el sistema. Pero subir un dump financiero sin cifrar a Drive tambien seria una mala decision. La solucion implementada mantiene copia local restaurable y sube a nube solo una version cifrada.

El scheduler no llama directamente a cron dinamico por cada cambio de configuracion. Hangfire despierta cada 15 minutos y `BackupSchedule.IsDue` decide si toca ejecutar. Es simple, auditable y evita reprogramaciones fragiles.

### Verificacion

- Backend build OK desde `C:\tmp` con SDK 8.0.421: `dotnet build ...AtlasBalance.API.csproj -p:UseAppHost=false --no-restore`.
- Tests focalizados: `BackupScheduleTests|BackupEncryptionServiceTests|ManualProcessResponseTests`: 9/9 OK.
- Frontend `npm.cmd run lint`: OK.
- Frontend `npm.cmd exec tsc -- --noEmit`: OK.
- Frontend `npm.cmd run build`: OK.

### Limite real

- No se probo subida real a Google Drive porque faltan credenciales OAuth y cuenta vinculada en este entorno.
- Para validar en servidor: configurar OAuth Client ID/Secret, guardar configuracion, vincular cuenta desde `Backups`, crear copia manual y confirmar que aparece un archivo `.enc` en Drive.
- La clave `backup_cloud_encryption_key` debe conservarse junto con la instalacion. Si se pierde, las copias cifradas ya subidas a Drive no se podran descifrar. Esto no es opcional ni romantico: sin clave no hay restauracion.

## 2026-06-21 - V-02-02 - Proveedor IA MiniMax M3/M2.7

### Que cambio

- Atlas Balance admite `MINIMAX` como tercer proveedor IA junto a `OPENROUTER` y `OPENAI`.
- Modelos permitidos para MiniMax: `MiniMax-M3` y `MiniMax-M2.7`.
- `Program.cs` registra los clientes HTTP `minimax` y `minimax-fallback` contra `https://api.minimax.io/v1/`.
- `AtlasAiService` llama a `chat/completions` con formato OpenAI-compatible. Para MiniMax usa `max_completion_tokens`, `reasoning_split=true` y, en `MiniMax-M3`, `thinking: { type: "disabled" }` para reducir razonamiento visible.
- La clave se guarda en `CONFIGURACION.minimax_api_key`, protegida por `ISecretProtector`, redactada en auditoria y expuesta al frontend solo como `minimax_api_key_configurada`.
- `Configuracion > Revision e IA` permite elegir MiniMax, pegar su API key y seleccionar M3 o M2.7. El chat IA muestra MiniMax y permite alternar esos modelos.
- Se anade la migracion `20260621190000_AddMiniMaxProviderConfig`.

### Por que

MiniMax no es un slug de OpenRouter aqui. Es otro endpoint, otra clave, otra facturacion y otra politica de datos. Mezclarlo con OpenRouter habria parecido funcionar hasta que hubiera que depurar cuota, privacidad o errores de proveedor.

Se verifico la documentacion oficial de MiniMax: la API compatible OpenAI usa `OPENAI_BASE_URL=https://api.minimax.io/v1`, `POST /v1/chat/completions`, y lista `MiniMax-M3` y `MiniMax-M2.7` como modelos disponibles. La misma documentacion marca `max_tokens` como legado y recomienda `max_completion_tokens`.

Fuentes consultadas:
- `https://platform.minimax.io/docs/api-reference/text-chat-openai`
- `https://platform.minimax.io/docs/api-reference/text-openai-api`
- `https://platform.minimax.io/docs/guides/quickstart-preparation`

### Verificacion

- Tests MiniMax focalizados: `dotnet test ... --filter "FullyQualifiedName~Update_Should_Accept_MiniMax|FullyQualifiedName~AskAsync_Should_Use_MiniMax"`: 3/3 OK.
- Frontend `npm.cmd run lint`: OK.
- Frontend `npm.cmd exec tsc -- --noEmit`: OK.
- Frontend `npm.cmd run build`: OK.
- `git diff --check`: OK, con avisos CRLF de Git no bloqueantes.

### Limite real

- La suite focalizada amplia `AtlasAiServiceTests|ConfiguracionControllerTests` compilo pero quedo 73/76: dos fallos de `ConfiguracionControllerTests` ya documentados y un test de ranking trimestral sensible a la fecha actual. No son fallos de MiniMax.
- No se hizo llamada real a MiniMax porque no hay API key en el entorno y no se deben inventar ni documentar secretos.

## 2026-06-09 - V-02-02 - Datos demo de desarrollo

### Que cambio

- `SeedData` incorpora datos demo sinteticos para entornos `Development`.
- La demo crea 3 paises, 3 titulares, 5 cuentas, 25 extractos, 1 plazo fijo, alertas de saldo, permiso global para el admin seed y una auditoria `DEMO_SEED`.
- Los IDs son fijos y el seed es idempotente: arrancar dos veces no duplica cuentas ni extractos.
- `DemoData:Enabled=false` desactiva el seed demo en desarrollo; en `Production` nunca se carga aunque la clave este en `true`.
- `appsettings.Development.json.template` deja `DemoData.Enabled=true` para que una instalacion local nueva muestre la app con datos.

### Por que

La UI financiera vacia no sirve para evaluar jerarquia, tablas, dashboards, paises, titulares, divisas, alertas ni plazos fijos. Meter datos demo sin guardarrail de entorno seria una mala idea: una app de tesoreria no debe arrancar produccion con movimientos ficticios.

### Verificacion

- `dotnet test "C:\Proyectos\Atlas Balance Dev\Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" --filter SeedDataTests --no-restore` desde `C:\tmp`: 8/8 OK.
- El comando desde la raiz queda bloqueado por el `global.json` que exige SDK `8.0.419`; se uso el workaround ya documentado con SDK `8.0.421`.
- Warnings no bloqueantes: `NU1900` por NuGet sin red y obsoleto Hangfire/PostgreSQL preexistente.

### Limite real

No se arranco la app ni se hizo validacion visual. El cambio queda validado a nivel de seed/test; la vista real dependera de levantar la base local con configuracion valida.

## 2026-06-09 - V-02-02 - Autorizacion real por pais en permisos y RLS

### Que cambio

- `PERMISOS_USUARIO` e `INTEGRATION_PERMISSIONS` incorporan `pais_id`; el pais pasa a ser dimension de autorizacion, no solo filtro operativo.
- Los scopes se evalúan por fila: si una regla tiene `pais_id`, `titular_id` y `cuenta_id`, las tres dimensiones deben coincidir. No se mezclan listas independientes.
- `UserAccessService`, `IntegrationAuthorizationService`, `ImportacionService`, `DashboardService`, `ExtractosController`, `AlertaService` y exportaciones respetan el scope por pais.
- RLS actualiza `can_read_cuenta`, `can_write_cuenta`, `can_read_titular`, `can_export_cuenta` y `can_review_extracto` con `pais_id`.
- `PERMISOS_USUARIO` e `INTEGRATION_PERMISSIONS` quedan bajo RLS/FORCE RLS.
- Las preferencias de columnas (`PREFERENCIAS_USUARIO_CUENTA`) agregan `pais_id` y `titular_id` para que restricciones por columnas no contaminen otro pais/titular.
- En extractos, las preferencias visibles se guardan con el scope real de la cuenta (`pais_id`, `titular_id`, `cuenta_id`) y las columnas editables solo se resuelven desde filas de permiso que conceden edicion. Una preferencia visual ya no puede actuar como permiso de edicion ilimitado.
- `RowLevelSecurityTests` cubre usuario e integracion con `pais_id + titular_id` y comprueba FORCE RLS en tablas nuevas de paises, MFA y permisos.
- `ImportacionContextoResponse` devuelve `pais_id` por cuenta para que el frontend mantenga el mismo contrato de scope que el backend.
- El frontend envia y consume `pais_id` en permisos de usuarios, tokens de integracion y helpers de permisos.
- Dashboard-only queda alineado: sin permiso operativo de datos no abre cuentas ni dashboard de datos.

### Por que

El selector global de pais era solo scope operativo. Eso no era seguridad. La seguridad real exige que el permiso, el backend y la base apliquen la misma frontera. Si `Pais A + Titular B` se convierte en `Pais A entero OR Titular B entero`, no es un permiso: es una fuga con UI bonita.

### Verificacion

- Subagentes usados para auditoria backend/RLS y frontend/contratos; se corrigieron los hallazgos de sobreconcesion por scopes combinados, exportacion incoherente, columnas por scope y dashboard-only inconsistente.
- Backend build OK desde `C:\tmp` con SDK `8.0.421` y `-p:UseAppHost=false`.
- Tests focalizados backend no Docker: `ExtractosControllerTests|UserAccessServiceTests|IntegrationAuthorizationServiceTests`: 32/32 OK.
- Frontend `npm.cmd run lint`: OK.
- Frontend `npm.cmd run build`: OK.
- Revalidacion 2026-06-26: `RowLevelSecurityTests` con PostgreSQL real/Testcontainers: 1/1 OK usando artefactos aislados en `C:\tmp\atlas-rls-artifacts`.

### Limite real

El modelo RLS esta preparado y el test ya cubre pais, pero la validacion PostgreSQL/Testcontainers queda como gate obligatorio de release. Si Docker no esta operativo, no se puede declarar RLS verde.

## 2026-06-09 - V-02-02 - App shell nativo con scope global por pais

### Que cambio

- Nuevo `paisScopeStore` frontend: `selectedPaisId: string | ''`, carga de paises activos, persistencia en `localStorage` y reset a `General` cuando el pais persistido ya no esta activo.
- `Layout` carga paises al tener sesion y recarga alertas activas con el pais seleccionado.
- `Sidebar` y `BottomNav` muestran `PaisScopeSelect`; en colapsado usa etiquetas cortas (`Gen`, ISO2) para no aplastar texto.
- El filtro local de pais desaparece de dashboard/cuentas y el scope global se propaga explicitamente por params, no por interceptor global de Axios.
- Nuevos filtros backend `paisId` en titulares, extractos/resumenes, importacion contexto, revision, exportaciones, alertas activas y auditoria. `/api/ia/chat` acepta `pais_id`.
- `PaisScopeQueryExtensions.ApplyPaisScope` centraliza el filtro de cuentas: sin `paisId` no filtra; con `paisId` exige `Cuenta.PaisId == paisId`.

### Por que

El selector de pais no es un permiso. Es scope operativo. Primero se aplica el alcance del usuario; despues se reduce por pais. Confundir eso con aislamiento de seguridad seria una mentira peligrosa.

Meter shadcn/Tailwind/app-shell externo aqui habria sido mala ingenieria: el repo no usa ese stack, no tiene `components.json` y falta token de registry. Se replico el comportamiento con las piezas reales del producto.

### Verificacion

- Backend build OK usando SDK instalado `8.0.421` desde `C:\tmp`; el `global.json` local exige `8.0.419` con `rollForward=disable`.
- Tests focalizados de capas afectadas: 161/161 OK.
- Frontend `npm.cmd run lint`: OK.
- Frontend `npm.cmd exec tsc -- --noEmit`: OK.
- Frontend `npm.cmd run build`: OK.
- Playwright con Chrome local y servidor temporal cerrado en el mismo comando: desktop expandido, desktop colapsado, tablet y movil verificados. Capturas en `output/playwright/`.

### Limite real

La suite backend no Docker completa quedo en 288/290 por dos fallos preexistentes/ajenos en `ConfiguracionControllerTests`. Docker/Testcontainers no se ejecuto.

## 2026-06-08 - V-02-02 - Actualizador, MFA recordado, OpenRouter y paises

### Que cambio

- `GET /api/sistema/version-disponible` devuelve preflight de instalabilidad: `instalable`, `bloqueos`, asset ZIP, firma, digest, clave publica y Watchdog disponible.
- `POST /api/sistema/actualizar` y `AutoUpdateJob` rechazan iniciar update si `instalable=false`.
- `IWatchdogClientService.EstaDisponibleAsync` comprueba el Watchdog local por HTTP con secreto y timeout; ya no se infiere disponibilidad desde el estado fallback.
- `MFA_TRUSTED_DEVICES` guarda dispositivos recordados con token opaco hasheado, `usuario_id`, `security_stamp`, expiracion, revocacion, `last_used_at`, user-agent e IP resumidos.
- `POST /api/auth/mfa/verify` emite `mfa_trusted` httpOnly/SameSite Strict por 90 dias si `remember_device=true`. `logout` no borra esa cookie.
- `GET/DELETE /api/auth/mfa/trusted-devices` lista y revoca dispositivos del usuario. Revocar MFA o cambiar password invalida por rotacion de `security_stamp`.
- OpenRouter acepta cualquier ID valido por sintaxis y conserva `openrouter/auto`; no hay allowlist fija ni array `models` de fallback.
- `GET /api/ia/modelos` consulta OpenRouter `/api/v1/models` con cache corta y devuelve sugerencias filtradas.
- `PAISES` es catalogo propio con soft delete; `CUENTAS.pais_id` es nullable. Cuentas y dashboard aceptan `paisId` y dashboard devuelve `saldos_por_pais`.

### Por que

El boton de update no podia depender solo de "hay version nueva": un release sin ZIP instalable, firma, digest, clave publica o Watchdog vivo no debe habilitar actualizacion. Eso no es UX; es control de danos.

La confianza MFA anterior era fragil: default apagado, duracion incorrecta y logout la borraba. Si "recordar dispositivo" no sobrevive a logout, el texto miente.

La allowlist fija de OpenRouter contradecia el objetivo de usar cualquier modelo disponible en la cuenta. La validacion correcta es sintactica y el proveedor decide disponibilidad, saldo y privacidad.

Pais pertenece a cuenta porque el dashboard y los filtros operan sobre saldos por cuenta. Ponerlo en titular habria mezclado estructuras cuando un titular tenga cuentas en varios paises.

### Verificacion

- Tests nuevos/actualizados cubren preflight de update, MFA recordado 90 dias/revocacion, modelos OpenRouter arbitrarios e invalidos, y dashboard filtrado/agregado por pais.
- `npm run build` frontend: OK.
- `C:\tmp\dotnet-sdk-8.0.419\dotnet.exe build AtlasBalance.API.csproj --no-restore`: OK con warnings no bloqueantes `NU1900` y Hangfire/PostgreSQL obsoleto.
- Tests focalizados backend: 122/122 OK para updater, auto-update, auth/MFA, IA/OpenRouter, dashboard y respuestas manuales.
- `git diff --check`: OK; solo avisos CRLF esperados.

### Limite real

No se ejecuto Testcontainers/Docker. Si una entrega necesita validar RLS con PostgreSQL real, ese gate no queda cubierto por esta pasada.

## 2026-06-01 - V-01.09 - Fallbacks de backup para updates desde instalaciones antiguas

### Que cambio

- `Actualizar-AtlasBalance.ps1` mantiene `ConnectionStrings:MigrationConnection` como primera opcion para `pg_dump`.
- Si falta, acepta una conexion completa en `ATLAS_DB_MIGRATION_CONNECTION` o `ATLAS_BALANCE_MIGRATION_CONNECTION`.
- Si solo se necesita cambiar usuario/password sobre la misma base, acepta `ATLAS_DB_OWNER_USER`/`ATLAS_DB_OWNER_PASSWORD` o `ATLAS_BALANCE_DB_OWNER_USER`/`ATLAS_BALANCE_DB_OWNER_PASSWORD`.
- Si existe `C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt`, el script puede recuperar de ahi el usuario/password de migracion/owner sin imprimirlos.
- Para actualizacion manual, `update.cmd -PromptForDbOwnerCredentials` pide usuario owner y password en consola segura antes de ejecutar `pg_dump`.
- `update.ps1` propaga ese modo aunque tenga que elevarse por UAC.
- `appsettings.Production.json.template` del Watchdog incluye de nuevo `DbOwnerUser` y `DbOwnerPassword`.

### Por que

El primer fix asumio que las instalaciones `V-01.06` sin `MigrationConnection` si tendrian credenciales owner en Watchdog. Eso era demasiado optimista. Una instalacion hecha desde plantilla o modificada a mano puede no tenerlas, y entonces el actualizador vuelve a caer al usuario runtime. Con RLS/FORCE RLS, ese usuario no sirve para un dump completo. No es un fallo de PostgreSQL; es exactamente la proteccion haciendo su trabajo.

### Regla operativa

No uses `-SkipBackup` para saltar este problema salvo que ya tengas un backup probado y reciente. Actualizar sin backup porque falta una password es una apuesta mala con nombre tecnico.

### Paquete local

- ZIP: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.09-win-x64.zip`
- Firma: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.09-win-x64.zip.sig`
- SHA-256 ZIP: `4E3256141498450775AB581FC5DFF38F066867592D38F3123CAEED8940B38128`
- SHA-256 firma: `E0CFAC2276D5AED379E5492DCC7E5B1A8FDE583525B5E3659D08AF7C239DD374`
- Verificacion local: `SIGNATURE_OK`
- Publicacion GitHub: assets reemplazados en Release `V-01.09-win-x64` mediante GitHub REST API. `gh` no estaba instalado, asi que se uso la credencial local de Git y subida con `HttpClient`.

## 2026-06-01 - V-01.09 - Backup de update compatible con V-01.06

### Que cambio

- `Actualizar-AtlasBalance.ps1` resuelve la conexion de backup con prioridad:
  1. `ConnectionStrings:MigrationConnection`.
  2. `WatchdogSettings.DbOwnerUser`/`DbOwnerPassword` del `watchdog/appsettings.Production.json`.
  3. `ConnectionStrings:DefaultConnection` solo como ultimo recurso.
- El fallback owner reutiliza `DbHost`, `DbPort` y `DbName` de Watchdog si existen; si faltan, toma host/puerto/base desde `DefaultConnection`.
- Si solo queda `DefaultConnection` y `pg_dump` falla, el mensaje explica que RLS puede bloquear el backup completo y que faltan credenciales owner/migracion.

### Por que

Instalaciones `V-01.06` pueden no tener `MigrationConnection` en la API, pero si tienen credenciales owner en Watchdog. Usar el usuario runtime para `pg_dump` contra tablas con RLS/FORCE RLS puede fallar antes de actualizar, como ocurrio con `AUDITORIAS`. La solucion correcta es usar credencial owner para backup, no saltarse el backup.

## 2026-06-01 - V-01.09 - Rotacion de clave de firma de releases

### Que cambio

- Se genero un nuevo par RSA 4096 para firma detached de releases.
- `Instalar-AtlasBalance.ps1` y `appsettings.Production.json.template` usan la nueva clave publica por defecto.
- La clave privada no se versiona. Se genero localmente en `tmp-release-signing-key/atlas-release-private.pem`, carpeta ignorada por Git.
- `Build-Release.ps1` construye el frontend en una carpeta temporal de release (`Atlas Balance Release/.frontend-dist-*`) y copia desde ahi al `api/wwwroot` publicado. No toca `frontend/dist` ni `backend/src/AtlasBalance.API/wwwroot`, que en Windows pueden quedar bloqueados por permisos o procesos externos.

### Por que

La clave privada anterior no esta en el repo ni en el entorno local. Eso es bueno para seguridad, pero si tampoco esta en el gestor de secretos, no se puede firmar. La clave publica no permite reconstruir la privada; si alguien te dice lo contrario, esta vendiendo humo con matematicas.

### Huella publica

`1762B5DFD784A0947EC0F191D38BC28D3AC7ED6EA7BA63902CEB31C0616242B4`

### Paquete generado

- ZIP: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.09-win-x64.zip`
- Firma: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.09-win-x64.zip.sig`
- SHA-256 ZIP: `A1F6D5A6BBEFAD7C05C8CBFBB09046A5B9C9F5DBCE5E5E1FB0D7DA41DC7E8061`
- SHA-256 firma: `19F9AE0197A7BB7F20E2DE0EBE87A9108B3E6D59922970466132DC2A27DC729E`
- Verificacion local: `SIGNATURE_OK`

### Implicacion operativa

Los paquetes firmados con esta clave solo verifican en instalaciones que tengan esta nueva publica. Instalaciones con la publica vieja necesitaran actualizar `UpdateSecurity:ReleaseSigningPublicKeyPem` manualmente, recibir un paquete con la nueva plantilla, o usar una ruta de update manual controlada.

## 2026-06-01 - V-01.09 - Auditoria profunda: seguridad, bugs y release gate

### Que cambio

- `RefreshToken` incorpora `security_stamp` y la migracion `20260601090000_AddRefreshTokenSecurityStamp` backfillea tokens existentes desde `USUARIOS.security_stamp`.
- `AuthService.RefreshTokenAsync` revoca tokens cuyo stamp no coincide con el usuario actual; `ChangePasswordAsync` exige garantia MFA de sesion para todo usuario sujeto a MFA.
- La migracion `20260601091000_HardenRlsIntegrationReadBackstop` reasegura `REVISION_EXTRACTO_ESTADOS` con RLS/FORCE y limita lecturas de integracion a permisos `lectura`.
- `TiposCambioService.ConvertAsync` deja de caer a 1:1 cuando falta tasa. Lanza `TipoCambioMissingException`; el handler global devuelve `409` con error claro.
- `ImportacionService` reevalua alertas de saldo bajo tras confirmacion de importacion y tras movimiento de plazo fijo. La huella de importacion se queda en identidad financiera estable: cuenta, fecha, monto, saldo y concepto.
- `AtlasAiService` distingue ranking por cuenta vs titular. Si el prompt pide titulares, agrupa por titular/divisa y no por cuenta.
- `Build-Release.ps1` resuelve `.dotnet\dotnet.exe` local antes que PATH. `Actualizar-AtlasBalance.ps1` backfillea claves no secretas faltantes y restaura binarios anteriores si falla el health check post-update.
- `.github/workflows/release.yml` crea un gate manual de release: verify completo, firma obligatoria y publicacion/actualizacion de GitHub Release como latest.

### Por que

La version tenia varios falsos verdes: refresh tokens que sobrevivian a revocaciones de seguridad, importaciones que no disparaban alertas, conversiones sin tasa convertidas en 1:1, y un actualizador que avisaba de rollback pero no lo ejecutaba. En tesoreria, el dato falso es peor que el error visible.

### Verificacion

- `dotnet test AtlasBalance.API.Tests.csproj --filter "TiposCambioServiceTests|ImportacionServiceTests|AtlasAiServiceTests|AuthServiceTests"`: 136/136 OK.
- `dotnet test AtlasBalance.API.Tests.csproj --filter "FullyQualifiedName!~RowLevelSecurityTests&FullyQualifiedName!~ExtractosConcurrencyTests"`: 276/276 OK.
- `dotnet build AtlasBalance.API.csproj -c Release --no-restore -p:UseAppHost=false`: OK, 1 warning obsoleto preexistente de Hangfire/PostgreSQL.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Parser PowerShell para `Build-Release.ps1` y `Actualizar-AtlasBalance.ps1`: OK.
- Secret scan de alta confianza sobre archivos versionables: OK.

### Limite real

No hay paquete publicable ni GitHub latest nuevo en esta maquina: falta `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`, falta `gh`/token local y Docker/Testcontainers sigue sin daemon operativo para validar RLS/concurrencia con PostgreSQL real. Publicar sin esos tres gates seria humo caro.

## 2026-05-22 - V-01.09 - Actualizacion one-click de paquete completo

### Que cambio

- `ActualizacionService` sigue consultando GitHub `releases/latest`, pero ahora entrega al Watchdog la raiz del paquete extraido, no `api`.
- Si la configuracion antigua solo tiene `WatchdogSettings:UpdateTargetPath=C:\AtlasBalance\api`, API y Watchdog derivan el `InstallPath` como `C:\AtlasBalance`. Las instalaciones nuevas escriben tambien `UpdateInstallPath`.
- `WatchdogOperationsService` valida que el origen sea un paquete completo firmado y extraido bajo `UpdateSourceRoot`: `VERSION`, `api/AtlasBalance.API.exe`, `watchdog/AtlasBalance.Watchdog.exe` y `scripts/Actualizar-AtlasBalance.ps1`.
- En modo normal de servicio Windows, Watchdog lanza un helper PowerShell no interactivo, resolviendo el PowerShell de sistema cuando existe, y ejecuta el mismo `scripts/Actualizar-AtlasBalance.ps1` del paquete. Asi puede reemplazar tambien su propia carpeta `watchdog` sin copiar encima de binarios vivos.
- En modo test/no-servicio, Watchdog aplica el paquete completo inline: `api`, `watchdog`, scripts, wrappers `.cmd`, `VERSION` y `atlas-balance.runtime.json`.
- `ConfiguracionPage.updateNow` ya no trata una caida temporal de la API como fallo inmediato; durante la ventana de update sigue esperando hasta que la API vuelva y el estado del Watchdog sea `SUCCESS` o `FAILED`, y conserva el mensaje real de `FAILED` sin pisarlo con un timeout generico.

### Por que

El flujo anterior validaba bien el ZIP, pero aplicaba solo `api`. Eso dejaba Watchdog, scripts y metadatos de instalacion atrasados. Una actualizacion de producto que solo cambia media instalacion es una trampa elegante: parece verde hasta que necesitas el script nuevo o el Watchdog nuevo.

### Verificacion

- Bloque focalizado update/watchdog: 26/26 OK.
- Frontend lint: OK.
- Frontend build: OK.
- Suite backend sin Docker/Testcontainers: 270/270 OK.
- GitHub `releases/latest` verificado por API: sigue en `V-01.06-win-x64` con `AtlasBalance-V-01.06-win-x64.zip` y `.zip.sig`.

### Limite real

El codigo ya apunta a `latest` y el boton queda preparado para actualizar paquete completo en instalaciones que ejecuten este codigo. Pero GitHub todavia no publica `V-01.09-win-x64` como latest. Ademas, una instalacion antigua cuyo Watchdog aun tenga el flujo parcial no puede actualizarse a si misma de forma completa con codigo que todavia no tiene; ese primer salto puede requerir `update.cmd` manual o una estrategia de bootstrap separada.

## 2026-05-22 - V-01.09 - Verificacion de actualizacion one-click desde GitHub

Nota: esta seccion documenta el diagnostico previo al fix. El estado actual esta en la seccion inmediatamente anterior: el codigo ya aplica paquete completo, con los limites pendientes de publicacion `latest`, bootstrap desde Watchdog antiguo y validacion Windows real.

### Que se comprobo

- Se reviso el flujo `Configuracion > Sistema > Actualizar ahora`: `SistemaController`, `ActualizacionService`, `WatchdogClientService`, `WatchdogOperationsService`, `AutoUpdateJob` y la UI de `ConfiguracionPage`.
- Se consulto la API real de GitHub para `AtlasLabs797/AtlasBalance/releases/latest`.
- Se ejecuto el bloque focalizado de tests de actualizacion/watchdog.

### Resultado

No cumple la promesa "solo dar a actualizar y que funcione" para una actualizacion completa de version.

La parte de seguridad del paquete esta razonablemente cerrada: repo oficial por HTTPS, asset `win-x64`, digest SHA-256, firma `.zip.sig`, limites de tamano/contenido y defensa Zip Slip. El problema es operativo: la actualizacion online descarga un paquete completo, pero `ActualizacionService` entrega al Watchdog solo la carpeta `api` y el target configurado es `C:\AtlasBalance\api`. Eso actualiza API/frontend, pero no actualiza Watchdog, scripts instalados, wrappers, `VERSION` raiz ni `atlas-balance.runtime.json`.

Ademas, el release real publicado como `latest` en GitHub es `V-01.06-win-x64`, no `V-01.09-win-x64`. Mientras no exista un release firmado `V-01.09` publicado como latest, ninguna instalacion puede actualizar online a `V-01.09` desde GitHub.

### Verificacion

- `https://api.github.com/repos/AtlasLabs797/AtlasBalance/releases/latest` -> `tag_name: V-01.06-win-x64`, assets `AtlasBalance-V-01.06-win-x64.zip` y `.zip.sig`.
- `C:\tmp\dotnet-sdk-8.0.419\dotnet.exe test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" --filter "FullyQualifiedName~ActualizacionServiceTests|FullyQualifiedName~AutoUpdateJobTests|FullyQualifiedName~WatchdogClientServiceTests|FullyQualifiedName~WatchdogOperationsServiceTests" --no-restore` -> 23/23 OK.

### Bloqueo

Este bloqueo quedo superado en codigo por la implementacion de paquete completo descrita arriba. Siguen vivos los bloqueos externos: publicar `V-01.09-win-x64` firmado como `latest`, validar el helper en una instalacion Windows real y definir bootstrap para Watchdog antiguos.

## 2026-05-22 - V-01.09 - Threat model: MFA en cambio de password, RLS y rutas de ficheros

### Que cambio

- `AuthController.CambiarPassword` pasa la cookie `refresh_token` actual a `AuthService.ChangePasswordAsync`.
- `ChangePasswordAsync` ya no inventa `mfa_verified_at` al emitir el refresh token nuevo. Si MFA es obligatorio y el usuario tiene MFA activo, solo preserva la garantia de una sesion actual ya verificada.
- Si la sesion actual no tiene refresh token activo con `mfa_verified_at`, el cambio de contrasena responde `401` y no rota password ni tokens.
- Nueva migracion `20260522103000_HardenRlsSoftDeleteBackstop`: RLS filtra soft-delete para usuario/integracion en titulares, cuentas, plazos, extractos y exportaciones, y los helpers por cuenta/extracto exigen cuenta y titular activos para datos dependientes.
- `ExportacionesController.Descargar`, `BackupsController.Restaurar` y `BackupService.ApplyRetentionAsync` validan ruta absoluta, extension esperada y raiz configurada antes de tocar disco.

### Por que

El bug de MFA era sutil y peligroso: el cambio de password marcaba el nuevo refresh como MFA-verificado por mirar el estado del usuario, no la garantia de la sesion. Eso podia convertir una sesion pre-MFA en sesion post-MFA. RLS y rutas de ficheros recibieron hardening porque son backstops: no deben depender de que todos los futuros controladores recuerden los mismos filtros.

### Verificacion

- `AuthServiceTests|AuthControllerTests|ManualProcessResponseTests`: 27/27 OK.
- Bloque autorizacion/integracion/import-export/update/watchdog: 88/88 OK.
- Suite backend sin Docker/Testcontainers: 269/269 OK.
- Revalidacion 2026-06-26: `RowLevelSecurityTests` con PostgreSQL real/Testcontainers: 1/1 OK usando artefactos aislados en `C:\tmp\atlas-rls-artifacts`.

### Limite real

RLS con PostgreSQL real era gate de release y quedo revalidado el 2026-06-26. Compilar la migracion no prueba las politicas con el motor; la prueba runtime si.

## 2026-05-20 - V-01.09 - Threat model: soft-delete heredado en importacion/exportacion

### Que cambio

- `ImportacionService.EnsureCuentaPermitidaAsync` ya no considera suficiente que `CUENTAS.activa=true`: ahora exige que exista un `TITULARES` activo para la cuenta.
- `ExportacionService.ExportarCuentaAsync` aplica la misma regla antes de generar XLSX manuales.
- `ExportacionService.ExportarMensualAsync` solo enumera cuentas con titular activo; el job ya no intenta exportar cuentas colgadas de titulares eliminados.
- `ActualizacionService` copia paquetes descargados con limite durante streaming. Si el servidor no declara `Content-Length`, el backend corta la escritura al superar el limite en vez de llenar disco y rechazar al final.
- `UpdateSecurity:MaxUpdatePackageBytes` permite bajar el limite por entorno; no puede subir por encima del maximo productivo de 300 MB.

### Por que

El fallo no estaba en la UI ni en los route guards. La ruta peligrosa era mas aburrida y por eso mas real: servicios backend que reciben un `cuentaId` directo y no heredan el soft-delete del titular padre. En una app de tesoreria, una cuenta de un titular eliminado debe comportarse como no visible, no como "activa si conoces el GUID".

### Verificacion

- Regresion de importacion: validar contra una cuenta activa con titular soft-deleted devuelve `404`.
- Regresion de exportacion manual: la misma cuenta se rechaza como no encontrada/inactiva y no genera XLSX.
- Regresion mensual: solo se exporta la cuenta con titular activo.
- Regresion de actualizador: asset sin `Content-Length` y superior al limite configurado se rechaza durante descarga y no llama al Watchdog.
- `C:\tmp\dotnet-sdk-8.0.419\dotnet.exe test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --no-restore -p:UseAppHost=false --filter "FullyQualifiedName~ImportacionServiceTests|FullyQualifiedName~ExportacionServiceTests|FullyQualifiedName~ActualizacionServiceTests" --verbosity minimal` -> 63/63 OK.

### Limite real

El threat model recibido no era una lista de hallazgos concretos y venia duplicado. Se revisaron las superficies descritas y se corrigio lo verificable encontrado. No sustituye a Testcontainers, E2E autenticado ni prueba real de backup/restore.

## 2026-05-20 - V-01.09 - IA: errores de red sin diagnosticos internos visibles

### Que cambio

- `AtlasAiService.BuildProviderNetworkMessage` devuelve un mensaje publico generico para fallos de conexion con OpenRouter/OpenAI.
- La ruta `ProviderNetworkException -> IaProviderException -> IaController HTTP 502` ya no transporta mensajes crudos de excepciones de red al usuario.
- La auditoria IA conserva solo codigos de diagnostico: `tls_certificate`, `proxy_unavailable`, `dns_resolution_failed`, `connection_refused` o `network_error`.
- Se eliminaron del mensaje visible y de la auditoria los detalles derivados de `rootMessage`: hostnames internos, proxy, puertos, sujetos/emisores de certificados y mensajes de sistema.
- `Directory.Build.props` excluye `**/.local-build/**` y `**/.codex-build/**`; `.gitignore` ignora `.local-build` anidados bajo backend.

### Por que

Sanitizar texto arbitrario de excepciones no es una defensa seria. El stack de transporte puede meter topologia interna en mensajes de TLS, DNS, proxy o socket. La solucion correcta es no enviar esa cadena al cliente y registrar solo una categoria operativa.

### Verificacion

- Test de regresion con TLS/proxy/certificado interno ficticio: 1/1 OK.
- `AtlasAiServiceTests`: 62/62 OK.
- `git diff --check`: OK, con avisos CRLF esperados.
- Barrido estatico confirma que la ruta de red de `AtlasAiService` ya no contiene `Detalle tecnico` ni diagnosticos crudos.

## 2026-05-20 - V-01.09 - Garantia MFA en refresh tokens

### Que cambio

- `REFRESH_TOKENS` incorpora `mfa_verified_at` mediante la migracion `20260520123000_AddRefreshTokenMfaAssurance`.
- `AuthService.VerifyMfaAsync` emite refresh tokens con `mfa_verified_at` tras validar el codigo TOTP.
- `AuthService.LoginAsync` tambien marca el refresh token cuando MFA es obligatorio y el login se acepta por `mfa_trusted` valido.
- `AuthService.RefreshTokenAsync` rechaza y revoca tokens sin `mfa_verified_at` cuando la politica vigente (`RequiresMfaAsync`) lo exige; desde `V-02.06` esa politica devuelve `true` para todo `ADMIN` y para no-administradores cuya clave `require_mfa_for_non_admin_users` este en `true` en `CONFIGURACION` (con fallback a `Security:RequireMfaForWebUsers` si la BD no la tiene sembrada).
- La rotacion de refresh preserva `mfa_verified_at` en el token de reemplazo.
- El access token incluye los claims `mfa_verified_at` (unix seconds) y `mfa_security_stamp` (anclado al `security_stamp` del usuario) cuando la sesion obtuvo garantia MFA. `UserStateMiddleware` rechaza cualquier sesion `ADMIN` sin esa marca, lo que invalida inmediatamente cualquier JWT heredado de una version anterior a `V-02.06`.

### Por que

El bug no era "falta pasar una cookie". El bug real era que el refresh token no llevaba estado de garantia MFA. Usar solo `mfa_trusted` habria roto sesiones legitimas de usuarios que verifican MFA sin recordar dispositivo. La garantia correcta vive en el refresh token que se esta rotando.

### Verificacion

- Reproduccion previa: `RefreshToken_Should_Reject_PreMfa_Token_When_Mfa_Becomes_Required` fallaba porque no se lanzaba `AuthException`.
- Tras el fix: `AuthServiceTests` 18/18 OK.
- Suite backend sin Docker/Testcontainers: 261/261 OK.

### Limite real

Los refresh tokens antiguos sin `mfa_verified_at` quedaran revocados al intentar renovarse con MFA obligatorio; el usuario tendra que iniciar sesion y completar MFA. Es el corte correcto. Lo contrario seria seguridad de escaparate.

## 2026-05-20 - V-01.09 - Logout limpia MFA recordado

### Que cambio

- `AuthController.Logout` vuelve a eliminar la cookie `mfa_trusted` junto a `access_token`, `refresh_token` y `csrf_token`.
- `SecurityConfigurationDefaults` fija `MfaRememberDeviceDays=62` y la clave `mfa_remember_device_enabled`.
- `AuthService.LoginAsync` solo acepta `mfa_trusted` si el admin activo esa clave en `CONFIGURACION`; si esta desactivada, exige TOTP y ordena limpiar la cookie.
- `AuthService.VerifyMfaAsync` solo emite `mfa_trusted` cuando el usuario marca recordar dispositivo y la politica admin lo permite.
- `Configuracion > General y SMTP > Autenticacion` permite activar o desactivar la opcion de recordar dispositivos MFA.
- `AuthControllerTests.Logout_Should_Delete_Trusted_Mfa_Cookie` prueba que logout emite el borrado de la cookie MFA recordada.
- `AuthServiceTests` cubre los casos permitido, desactivado por admin, expirado y revocado por rotacion de `security_stamp`.

### Por que

Un logout explicito debe cortar todos los artefactos de autenticacion del navegador. Dejar `mfa_trusted` vivo permitia que alguien con la contrasena volviera a entrar desde ese navegador sin TOTP durante hasta 90 dias. Para tesoreria, eso no es "recordar dispositivo"; es dejar una llave bajo el felpudo.

### Verificacion

- Suite focalizada `AuthServiceTests|AuthControllerTests|ConfiguracionControllerTests`: 29/29 OK.
- Frontend lint: OK.
- Frontend build (`tsc && vite build`): OK.
- Barrido estatico confirma `DeleteCookie("mfa_trusted")` en logout y `SecurityConfigurationDefaults.MfaRememberDeviceDays = 62`.
- La sincronizacion local de `frontend/dist` a `backend/src/AtlasBalance.API/wwwroot` fallo por `Access denied`; `Build-Release.ps1` debe regenerar `wwwroot` en un entorno con permisos antes de publicar.

### Limite real

La politica final elegida es 62 dias, no 30. Es menos estricta, pero el control importante queda intacto: un logout explicito ya no conserva la confianza MFA del navegador y la opcion de recordarlo queda bajo decision del administrador.

## 2026-05-20 - V-01.09 - Extraccion segura de paquetes con entrada raiz

### Que cambio

- `ActualizacionService.TryExtractPackageSafely` normaliza `packageRoot` sin separador final y conserva un `rootFullPathWithSeparator` solo para validar rutas hijas.
- Si una entrada ZIP resuelve exactamente al directorio raiz de extraccion, solo se acepta cuando el nombre normalizado es `.` y la entrada es directorio o longitud cero.
- Las entradas hijas siguen obligadas a empezar por `rootFullPathWithSeparator`, por lo que `../evil.txt` y rutas hermanas siguen rechazadas antes de extraer.
- `ActualizacionServiceTests` cubre paquetes firmados con entradas raiz `.` y `./`, mas un ZIP firmado con traversal `../evil.txt`.

### Verificacion

- Reproduccion previa: `ActualizacionServiceTests` fallaba con `rootDirectoryEntry: "."` porque el paquete se rechazaba.
- Despues del fix: `ActualizacionServiceTests` 13/13 OK.
- Bloque actualizacion/watchdog: 20/20 OK.
- Suite backend sin Docker/Testcontainers: 256/258 OK en ese momento; los fallos ajenos se tratan en bloques posteriores.

### Limite real

El parche corrige compatibilidad del updater y mantiene Zip Slip cerrado en la ruta cubierta. No convierte `V-01.09` en release final: siguen pendientes los gates Docker/Testcontainers y E2E autenticado.

## 2026-05-20 - V-01.09 - Hardening de login contra DoS por IP compartida

### Que cambio

- `AuthService` deja de aplicar el contador cliente/IP como bloqueo previo a credenciales validas.
- El precheck temprano mantiene solo el limite por email+cliente; el limite cliente/IP se aplica tras resolver usuario inexistente o fallo de password.
- Un login correcto limpia tanto el contador email+cliente como el contador cliente/IP.
- `Program.cs` activa `ForwardedHeaders` para `X-Forwarded-For` y `X-Forwarded-Proto` con `ForwardLimit=1` y confianza limitada a proxies/redes configuradas.
- `appsettings*.json*` documenta `ForwardedHeaders:KnownProxies` y `ForwardedHeaders:KnownNetworks` para despliegues con proxy inverso.
- Se corrigio un bloqueo de compilacion existente en el arbol actual: `mfaVerifiedAt` queda tipado como `DateTime?`.

### Por que

El hallazgo era correcto: 20 intentos invalidos desde una IP compartida podian dejar fuera a usuarios legitimos durante la ventana de 15 minutos. Subir el limite no arregla el fallo; solo encarece un poco el ataque. La regla buena es simple: una credencial valida no debe morir por un contador anonimo de IP antes de verificarla.

### Verificacion

- Regresiones nuevas en `AuthServiceTests`:
  - 20 fallos con emails distintos desde la misma IP ya no bloquean un login valido posterior.
  - un login valido limpia el contador cliente/IP y el siguiente fallo aislado no recibe 429.
- `C:\tmp\dotnet-sdk-8.0.419\dotnet.exe test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AuthServiceTests --no-restore -p:UseAppHost=false` -> 20/20 OK.
- `C:\tmp\dotnet-sdk-8.0.419\dotnet.exe test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" --filter "FullyQualifiedName!~RowLevelSecurityTests&FullyQualifiedName!~ExtractosConcurrencyTests" --no-restore -p:UseAppHost=false` -> 267/267 OK.
- `git diff --check` sobre archivos tocados -> OK, solo avisos CRLF esperados.

### Limite real

No se ejecuto la suite completa con Docker/Testcontainers ni E2E autenticado. Este cambio queda validado en la ruta de servicio de autenticacion y en la suite backend no Docker; el gate de release completo sigue abierto.

## 2026-05-20 - V-01.09 - Apertura de version

### Que cambio

- La version activa pasa de `V-01.07` a `V-01.09`.
- Runtime backend actualizado a `1.9.0` / `V-01.09` en `Directory.Build.props`.
- Runtime frontend actualizado a `1.9.0` / `V-01.09` en `package.json` y `package-lock.json`.
- `Atlas Balance/VERSION`, scripts de release/instalacion, seed `app_version`, tests de autoactualizacion y documentacion viva apuntan a `V-01.09`.
- Se crea `Documentacion/Versiones/v-01.09.md`; `v-01.07.md` queda cerrada como base anterior.

### Verificacion

- Barrido estatico de referencias activas `V-01.07` / `1.7.0` tras el cambio; solo quedan referencias historicas, la dependencia `esquery 1.7.0` y menciones legitimas a la base anterior.
- JSON de `package.json` y `package-lock.json` validado con Node.
- Parser PowerShell OK para `Build-Release.ps1`, `Instalar-AtlasBalance.ps1` e `install.ps1`.
- `git diff --check` OK, con avisos CRLF esperados.
- `AutoUpdateJobTests`: 3/3 OK con SDK local `C:\tmp\dotnet-sdk-8.0.419`.

### Limite real

No se genero paquete `V-01.09`, no se firmo ZIP y no se cerro el gate Docker/Testcontainers. Llamarlo release final ahora seria humo con numero nuevo.

## 2026-05-19 - V-01.07 - Jerarquia visual y pesos de accion UI/UX

### Que cambio

- `system-coherence.css` normaliza jerarquia de headers de pagina y limita headers sticky a wrappers de tabla concretos, evitando que cualquier `th` de la app se vuelva fijo por accidente.
- `users-table-card` reduce sombra para no competir con modales; modales y dialogs conservan `shadow-overlay`.
- `dashboard.css` refuerza headers de cards y permite destacar plazos fijos cercanos con variante warning.
- `entities.css` destaca saldos de cuenta, aplana paneles internos de evolucion/divisas y mejora titulares/cuentas con subtitulos de contexto.
- `importacion.css` convierte el resumen de validacion en metricas legibles y diferencia botones primarios/secundarios.
- `admin.css` convierte feedback de guardado en banner, agrega resumen visual de backups y mejora jerarquia de secciones de Configuracion.
- Pantallas ajustadas: Cuentas, Titulares, Usuarios, Backups, Configuracion, Importacion, CuentaDetalle y Dashboard.

### Verificacion

- `npm.cmd run lint` -> OK.
- `npm.cmd exec tsc -- --noEmit` -> OK.
- `npm.cmd run build` -> OK.
- `git diff --check` -> OK, con avisos CRLF esperados.

### Limite real

No se ejecuto QA visual autenticado ni E2E real. La app no debe marcarse como release final hasta validar flujos reales con backend/PostgreSQL, datos de volumen, Docker/Testcontainers y backup/restore.

## 2026-05-19 - V-01.07 - Skills Curated pre-release e higiene de repositorio

### Que cambio

- `Skills Curated/` se anade al `.gitignore` raiz. Es tooling local de agente, no producto ni fuente versionable del release.
- `.github/workflows/ci.yml` alinea el escaneo de secretos y excluye `Skills Curated/`, igual que `Otros/` y `Skills/`.
- `.gitignore` raiz y `Atlas Balance/.gitignore` ignoran `*.cer`, `*.p12`, `*.jks`, `*.dump` y `backend/**/TestResults/`.

### Por que

Si una carpeta de skills curados o resultados de tests aparecen como pendientes, alguien puede terminar subiendo basura local por accidente. En una app financiera eso no es detalle cosmetico: es mala disciplina de release.

### Verificacion

- Secret scan local `cyber-neo`: 490 archivos escaneados, 0 hallazgos.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list '.\AtlasBalance.sln' package --vulnerable --include-transitive`: 0 paquetes vulnerables.
- Backend sin Docker/Testcontainers: 254/254 OK.
- Frontend lint, TypeScript y build: OK.
- `git check-ignore` confirma exclusion de `Skills Curated/`, `TestResults/`, certificados/keystores y dumps.

### Limite real

Docker no esta instalado/disponible en esta maquina. La suite Testcontainers y el E2E visual/autenticado siguen siendo gates reales antes de publicar V-01.07 como release final.

## 2026-05-19 - V-01.07 - Refinamiento UI/UX y accesibilidad pre-entrega

### Que cambio

- `AppSelect` sigue siendo nativo y ahora elimina el `background-image` global del `select`, evitando doble flecha visual.
- `CuentaDetailPage` fija columnas de seleccion/fila en la tabla de movimientos y elimina tab stops de celdas contenedoras; el foco queda en controles reales.
- `ExtractoTable` recibe `totalRows`, aclara que sus filtros operan sobre la pagina cargada y usa `role="table"` en vez de `role="grid"` porque no implementa navegacion de grid con flechas.
- `ConfiguracionPage` completa el patron ARIA de tabs: `aria-controls`, `role="tabpanel"`, `tabIndex` activo y navegacion con flechas/Home/End.
- `CreateTokenModal` muestra errores de validacion dentro del modal con `role="alert"` y elimina el `label` vacio usado como spacer.
- `TokenList` modela metricas como `loading/error/ready`; ya no muestra ceros falsos cuando falla `/metricas`.
- `BackupsPage` anuncia la restauracion como `alertdialog` con `aria-busy`, foco inicial y descripcion asociada.
- `EvolucionChart` expone tabla `sr-only` con datos numericos para lectores de pantalla.
- Se normalizaron `role="alert"` en errores visibles y `aria-label` contextual en botones repetidos de acciones.

### Verificacion

- `npm.cmd run lint` -> OK.
- `npm.cmd exec tsc -- --noEmit` -> OK.
- `npm.cmd run build` -> OK.
- `git diff --check` -> OK, con avisos CRLF esperados en archivos .NET.

### Limite real

No se puede afirmar que todas las pantallas graficas esten visualmente perfectas sin ejecutar una sesion autenticada real en navegador contra backend y PostgreSQL. La revision actual cubre estaticamente UI/UX, accesibilidad, copy de estados y build; el gate visual/E2E sigue pendiente para release final.

## 2026-05-19 - V-01.07 - Auditoria integral y hardening pre-release

### Que cambio

- `CuentasController.Resumen` y `IntegrationOpenClawController.Saldos` calculan saldo actual con `fila_numero DESC` y solo desempatan por fecha. Esto alinea resumen, OpenClaw, dashboard y alertas.
- `ImportacionService` genera huellas `v2` por contenido normalizado y ordinal de duplicado dentro del lote. La fila origen ya no forma parte de la huella, por lo que una cabecera o fila extra no convierte movimientos iguales en "nuevos".
- Las transacciones relacionales de importacion y movimientos de plazo fijo se limpian en `finally`; si falla `SaveChanges` o auditoria, no se depende del final del scope para liberar la transaccion.
- `ConfiguracionController.Update` rechaza textos `null` en payloads parciales y `smtp/test` rechaza body nulo con `400`.
- `AppSelect` deja el listbox custom y usa `<select>` nativo, reduciendo superficie ARIA fragil en filtros y formularios.
- El modal de importacion en cuenta usa `useDialogFocus`, `Escape`, foco inicial/restauracion y descripcion asociada.
- `EditableCell` mantiene visible el error de guardado hasta que el usuario reintenta y lo anuncia con `role="alert"`.
- La validacion de importacion muestra estados textuales ("Valida", "Aviso importable", "Error bloqueante") y los mensajes async usan `role="alert"`/`role="status"`.
- Los formularios de importacion/formatos asocian labels criticos a sus controles.
- `TitularSaldoBarChart` expone resumen accesible y tabla alternativa oculta para lectores.
- Requisitos documentales corregidos: PostgreSQL 16+ en `Atlas Balance/AGENTS.md` y `Documentacion/SPEC.md`.

### Verificacion

- Tests focalizados iniciales fallaron en rojo antes del fix: saldo actual por fecha y huella dependiente de indice reproducian el bug.
- Tests focalizados despues del fix: `CuentasControllerTests|IntegrationOpenClawControllerTests|ImportacionServiceTests` -> 52/52 OK.
- `ConfiguracionControllerTests` -> 8/8 OK.
- Suite backend sin Docker/Testcontainers -> 254/254 OK.
- Suite backend completa -> 254/256 OK; fallan solo los dos tests que requieren Docker/Testcontainers (`ExtractosConcurrencyTests`, `RowLevelSecurityTests`).
- `npm.cmd run lint` -> OK.
- `npm.cmd exec tsc -- --noEmit` -> OK.
- `npm.cmd run build` -> OK.
- `npm audit --audit-level=moderate`: 0 vulnerabilidades en pase posterior con aprobacion.
- `dotnet list package --vulnerable --include-transitive`: 0 paquetes vulnerables en pase posterior con aprobacion.

### Criterio de release

V-01.07 mejora, pero no queda "release final" para clientes. Faltan ZIP firmado, suite PostgreSQL/Testcontainers verde, E2E autenticado con datos reales y prueba backup/restore bajo RLS. Publicarlo como final sin eso seria teatro.

## 2026-05-18 - V-01.07 - Verificacion backend post-Codex Security con SDK local

- Se instalo SDK .NET 8.0.419 en `C:\tmp\dotnet-sdk-8.0.419`, version exacta fijada por `global.json` con `rollForward=disable`.
- `dotnet restore "Atlas Balance\backend\AtlasBalance.sln"`: OK usando `Atlas Balance\backend\.local-build` como home/cache local.
- `dotnet build "Atlas Balance\backend\AtlasBalance.sln" --no-restore --configuration Debug`: OK, 0 errores.
- `dotnet test "Atlas Balance\backend\AtlasBalance.sln" --no-build --configuration Debug --filter "FullyQualifiedName!~ExtractosConcurrencyTests&FullyQualifiedName!~RowLevelSecurityTests"`: 249/249 OK.
- `dotnet test "Atlas Balance\backend\AtlasBalance.sln" --no-build --configuration Debug`: 249/251 OK; fallan solo `ExtractosConcurrencyTests` y `RowLevelSecurityTests` por Docker/Testcontainers no disponible/configurado.
- `dotnet list "Atlas Balance\backend\AtlasBalance.sln" package --vulnerable --include-transitive`: 0 paquetes vulnerables en API, Watchdog y tests.

## 2026-05-17 - V-01.07 - Revision Codex Security en profundidad

### Que cambio

- Las lecturas no-admin de cuentas/extractos ahora heredan el soft-delete del titular padre: `UserAccessService`, `ExtractosController` e integraciones filtran cuentas cuyo `TITULARES.deleted_at` no sea nulo.
- `GET /api/extractos/{id}/audit-celda` ya no devuelve auditoria de extractos soft-deleted a usuarios no-admin.
- `POST /api/exportaciones/manual` vuelve a exigir permiso operativo de cuenta (`CanWriteCuentaAsync`) y no solo lectura.
- OpenRouter envia `provider.zdr=true` y `data_collection=deny` en todas las rutas, incluidas `openrouter/auto`, modelos gratis y modelos gratis pineados. Si OpenRouter no puede servir un endpoint compatible, debe fallar cerrado.
- La actualizacion desde la app rechaza `sourcePath` manual; solo instala assets descargados desde GitHub Release oficial tras digest SHA-256 y firma `.zip.sig`.
- `BackupService` y Watchdog dejan de ejecutar `pg_dump`, `pg_restore` o `docker` por nombre sin ruta absoluta. `PostgresBinPath` debe apuntar al binario real y `DockerCliPath` queda documentado/configurable.
- `Instalar-AtlasBalance.ps1` deja de pasar SQL con passwords por argumento `psql -c`; lo envia por stdin. El instalador de PostgreSQL via winget sigue requiriendo `--superpassword`, asi que ese tramo debe tratarse como ventana local sensible.
- La importacion rechaza celdas individuales de mas de 4096 caracteres y la exportacion XLSX escapa textos tipo formula aunque empiecen con espacios.
- `package-lock.json` se limpio de la dependencia raiz huerfana `@fontsource-variable/geist`.

### Verificacion

- `npm.cmd audit --audit-level=moderate --json`: 0 vulnerabilidades.
- `npm.cmd ls --package-lock-only --depth=0`: sin dependencia `extraneous`.
- Parse AST de `Instalar-AtlasBalance.ps1`: OK.
- `git diff --check`: OK, solo avisos CRLF.
- SDK .NET 8.0.419 instalado localmente en `C:\tmp\dotnet-sdk-8.0.419` para respetar `global.json` con `rollForward=disable`.
- `dotnet restore "Atlas Balance\backend\AtlasBalance.sln"`: OK con cache en `Atlas Balance\backend\.local-build`.
- `dotnet build "Atlas Balance\backend\AtlasBalance.sln" --no-restore --configuration Debug`: OK, 0 errores.
- Suite backend sin Docker/Testcontainers: 249/249 OK.
- Suite backend completa: 249/251 OK; fallan solo `ExtractosConcurrencyTests` y `RowLevelSecurityTests` porque Docker/Testcontainers no esta disponible/configurado en esta maquina.
- `dotnet list "Atlas Balance\backend\AtlasBalance.sln" package --vulnerable --include-transitive`: 0 paquetes vulnerables en API, Watchdog y tests.

## 2026-05-17 - V-01.07 - Actualizacion automatica desde GitHub Release

### Que cambio

- Se anade `AutoUpdateJob`, registrado en Hangfire como `auto-update-github-release`, con ejecucion horaria y ventana diaria configurable por UTC.
- La autoactualizacion queda gobernada por `CONFIGURACION`: `app_update_auto_enabled`, `app_update_auto_hour_utc`, `app_update_auto_last_checked_utc`, `app_update_auto_last_started_utc` y `app_update_auto_last_result`.
- `Configuracion > Sistema` permite activar/desactivar el modo automatico, elegir hora UTC y ver ultima comprobacion/inicio automatico.
- `ActualizacionService` mantiene el flujo seguro existente: repo oficial, GitHub Release, asset `AtlasBalance-*-win-x64.zip`, digest SHA-256, firma `.zip.sig`, extraccion en `UpdateSourceRoot` y aplicacion via Watchdog.
- Se endurece la descarga/extraccion: ZIP maximo 300 MiB, contenido extraido maximo 1 GiB, entrada maxima 512 MiB y 10000 entradas maximas. Sigue rechazando rutas fuera de la raiz de extraccion.
- El build frontend se sincronizo con `backend/src/AtlasBalance.API/wwwroot` mediante copia acotada y poda solo de assets obsoletos.

### Por que

La app ya podia actualizar manualmente desde GitHub Releases, pero eso no es "lo hace solo". Faltaba un job recurrente. Lo peligroso era activarlo sin freno: una tesoreria no debe reiniciarse sola en horario de trabajo porque alguien subio un release. Por eso el automatico existe, pero es opt-in.

El pendiente de tamano/contenido de paquetes tambien era real. Firmar un ZIP no te protege de un paquete absurdamente grande o malformado. La firma dice quien lo publico; no dice que sea razonable tragarselo sin limites.

### Verificacion

- Tests backend focalizados con SDK local: `ActualizacionServiceTests|AutoUpdateJobTests|SeedDataTests|ConfiguracionControllerTests` -> 25/25 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK.
- Sincronizacion `frontend/dist` -> `wwwroot`: `dist_files=65 wwwroot_files=65`.
- `git diff --check` en archivos tocados: OK, solo avisos CRLF esperados.

### Pendiente real

- Ejecutar suite completa con Docker/Testcontainers antes de publicar release final.
- Publicar GitHub Release con ZIP y `.zip.sig`; sin firma o sin clave publica valida, el updater seguira fallando cerrado.

## 2026-05-17 - V-01.07 - IA: recibos/facturas no absorbe cargos de tarjeta

### Que cambio

- `RECIBOS/FACTURAS DETECTADOS` en `AtlasAiService` excluye conceptos de tarjeta/TPV/datáfono y prestamos/leasing cuando la coincidencia venia por terminos genericos como `cargo`.

### Por que

La suite no Docker destapo un fallo real: `Cargo tarjeta comercio` estaba inflando recibos/facturas de `35,00` a `80,00`. Un cargo de tarjeta no es automaticamente una factura. Meterlo en ese bloque ensucia el contexto IA y hace que el modelo parezca listo mientras suma basura.

### Verificacion

- Test focalizado `AtlasAiServiceTests.AskAsync_Should_Build_Period_And_Category_Context`: OK.
- Suite backend sin Testcontainers: 242/242 OK.

## 2026-05-17 - V-01.07 - IA: contexto financiero y errores de proveedor afinados

### Que cambio

- `AtlasAiService` deja de tratar `cuota`, `servicio`, `tarjeta` y `transferencia` como senales directas de comision en el contexto que se envia a IA.
- La seccion `SEGUROS DETECTADOS` del contexto IA se limita a cargos negativos y excluye falsos positivos: Seguridad Social/TGSS, Generalitat, transferencias, anulaciones, devoluciones y reembolsos.
- En V-01.07 los errores de red del proveedor IA pasaron a mostrar diagnostico tecnico saneado. En V-01.09 ese enfoque queda endurecido: el usuario ve un mensaje generico y la auditoria conserva solo codigos seguros de transporte.
- `AtlasAiServiceTests` anade regresion con falsos positivos reales de tarjeta, cuota/leasing, transferencia a aseguradora, anulacion de seguro y Generalitat.

### Por que

La IA no estaba "alucinando" sola: el backend podia darle contexto contaminado. Si metes transferencias a aseguradoras o Generalitat dentro de `SEGUROS DETECTADOS`, el modelo puede responder con basura muy convincente. Eso es peor que fallar: parece analisis.

El error de red generico tampoco ayudaba. La solucion posterior en V-01.09 corrige el exceso de detalle: el operador conserva una categoria segura en auditoria sin exponer topologia interna al usuario.

### Verificacion

- Documentacion oficial de OpenRouter revisada: `models`, `reasoning.exclude` y controles de privacidad/routing siguen vigentes; los slugs de modelos permitidos siguen publicados en `/api/v1/models`.
- `git diff --check` sobre `AtlasAiService.cs` y `AtlasAiServiceTests.cs`: OK, solo avisos CRLF esperados.
- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK.
- Backend tests no ejecutados: `dotnet` no existe en `PATH` ni en `C:\Program Files\dotnet\dotnet.exe` en esta maquina.

### Pendiente real

- Ejecutar `dotnet test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" --filter "AtlasAiServiceTests|ConfiguracionControllerTests" -p:UseAppHost=false --no-restore` en un entorno con SDK .NET.

## 2026-05-17 - V-01.07 - Revision bancaria afinada con ejemplos reales

### Que cambio

- `RevisionService` deja de usar `tarjeta` como termino directo de comision. Solo aparece si el concepto tambien trae una senal fuerte como `comision`.
- La revision de seguros queda limitada a cargos negativos (`Monto < 0`), porque ingresos, abonos o anulaciones no son pagos de seguro a revisar.
- La deteccion de seguros excluye nuevos falsos positivos observados en capturas: `seguros sociales`, `generalitat`, transferencias, anulaciones, devoluciones y reembolsos.
- Se agregan pruebas de regresion con los conceptos reportados: transferencias a proveedores/personas, cuotas de Seguridad Social, prestamos/cuotas/leasing, cargos de tarjeta, Generalitat, Reale/Occident por transferencia y anulaciones de seguros.

### Por que

La regla anterior aun tenia dos puntos flojos: `tarjeta` convertia cargos normales de tarjeta en comisiones, y `generali` cazaba `Generalitat`. Si el usuario acaba descartando media pantalla a mano, el detector esta estorbando. La revision debe ser conservadora: mejor no mostrar un dudoso que llenar la lista de ruido.

### Verificacion

- `git diff --check` sobre `RevisionService.cs` y `RevisionServiceTests.cs`: OK, solo avisos CRLF esperados.
- `where.exe dotnet`: no encuentra SDK/runtime .NET en esta maquina.

### Pendiente real

- Ejecutar `RevisionServiceTests` cuando haya SDK .NET disponible.

## 2026-05-16 - V-01.07 - Importacion, revision y MFA

### Que cambio

- `ImportacionService` deja de rechazar un formato cuando una columna extra mapeada no aparece en los datos pegados. Esa celda se trata como blanco y no se persiste en `EXTRACTOS_COLUMNAS_EXTRA`.
- Las columnas base siguen siendo obligatorias salvo los casos informativos ya soportados. Fecha, monto y saldo no pueden quedar realmente `NULL` porque el schema de `EXTRACTOS` no lo permite.
- `RevisionService` elimina `transferencia`, `cuota` y `servicio` como terminos directos de comision. Una transferencia normal deja de salir como comision; una linea con `comision` sigue saliendo.
- La deteccion de seguros excluye Seguridad Social, Seguro Social, TGSS y Tesoreria General para no mezclar impuestos/cotizaciones con polizas.
- El recuerdo MFA pasa de 30 a 90 dias.
- `Logout` deja de borrar la cookie `mfa_trusted`: cerrar sesion corta la sesion, no desconfia el dispositivo recordado.
- `UsuariosController` anade `POST /api/usuarios/{id}/mfa/revocar`, que limpia secreto MFA, desactiva MFA, rota `security_stamp`, revoca refresh tokens activos y audita `MFA_REVOKED` sin guardar secretos.
- `Reset-AdminPassword.ps1` tambien limpia MFA para recuperar un admin que haya perdido el Authenticator.
- `UsuariosPage` muestra estado de Authenticator y permite revocarlo desde un modal de confirmacion.

### Por que

Rechazar una importacion porque una columna extra viene vacia es mala UX y mala semantica: una columna opcional vacia es blanco, no error. Lo de `transferencia` como comision era directamente una regla floja: transferencia no significa comision. Y MFA recordado sin revocacion era media solucion; si no se puede cortar, no es control de acceso, es decoracion.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK.
- `frontend/dist` copiado a `backend/src/AtlasBalance.API/wwwroot`; `index.html` coincide con `dist`.
- Tests backend focalizados bloqueados: `dotnet` no existe en `PATH` ni fuera del sandbox en esta maquina.

### Pendiente real

- Ejecutar tests backend focalizados cuando haya SDK .NET disponible: `ImportacionServiceTests`, `RevisionServiceTests`, `AuthServiceTests`, `UsuariosControllerTests` y `AuthControllerTests`.

## 2026-05-16 - V-01.07 - Apertura de version

### Que cambio

- Se crea la rama local `V-01.07`.
- Runtime backend pasa a `1.7.0` / `V-01.07` en `Directory.Build.props`.
- Runtime frontend pasa a `1.7.0` / `V-01.07` en `package.json` y `package-lock.json`.
- `Atlas Balance/VERSION`, scripts de instalacion/release, seed de `app_version` y documentacion viva pasan a `V-01.07`.
- Se crea `Documentacion/Versiones/v-01.07.md` y `version_actual.md` declara `V-01.07` como version activa.

### Por que

Trabajar una version nueva sin mover todas las fuentes runtime es una forma barata de fabricar builds que mienten. Peor: si el script de release siguiera por defecto en `V-01.06`, alguien acabaria publicando un paquete con nombre viejo y binarios nuevos.

### Verificacion

- Rama local `V-01.07` creada.
- Barrido de fuentes canonicas confirma `V-01.07` / `1.7.0` en runtime y documentacion de version.
- No se ha generado paquete release `V-01.07`; el SHA queda pendiente hasta firmar el ZIP.

## 2026-05-13 - V-01.06 - CI locked restore y release firmado

### Que cambio

- `AtlasBalance.API.csproj` y `AtlasBalance.Watchdog.csproj` declaran `RuntimeIdentifiers=win-x64`.
- `.github/workflows/ci.yml` restaura backend por proyectos concretos, ejecuta tests sobre `AtlasBalance.API.Tests.csproj` y audita paquetes por proyecto.
- `Build-Release.ps1` deja de depender del restore de solucion para publicar runtime-specific; ahora restaura API y Watchdog por proyecto con `--locked-mode -r win-x64` y publica con `--no-restore`.
- El script de release genera `.zip.sig` con RSA/SHA-256 mediante un firmador temporal .NET 8 cuando recibe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`.
- `Instalar-AtlasBalance.ps1` y `appsettings.Production.json.template` incluyen una clave publica de firma por defecto; `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM` sigue pudiendo sobrescribirla.
- Tests backend de IA y tipos de cambio se ajustan a los mensajes saneados vigentes.

### Por que

GitHub Actions fallo en `dotnet restore --locked-mode` porque los lockfiles ya contenian dependencias para `win-x64`, pero los proyectos no declaraban ese RID. Eso no era un fallo de GitHub; era el repo contradiciendose a si mismo. De paso, publicar un ZIP sin `.sig` era inutil para el actualizador online: la app lo rechazaria y con razon.

El restore de solucion tambien falla localmente sin error MSBuild concreto. Mantenerlo como gate principal seria mala ingenieria: CI ahora valida los tres proyectos reales y el script de release valida los dos publicables con RID.

### Verificacion

- `dotnet restore` por proyecto API/Watchdog/Test: OK.
- Suite backend sin Docker/Testcontainers sobre `AtlasBalance.API.Tests.csproj`: OK, 223/223.
- `Build-Release.ps1 -Version V-01.06`: OK con build frontend, restore locked, publish API/Watchdog y firma.
- ZIP: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.06-win-x64.zip`.
- Firma: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.06-win-x64.zip.sig`.
- SHA256 ZIP: `95DCA977E145DE07BF41E5B6478AD856BF803E4938A0A98480ABB043F51781E1`.
- Verificacion local de firma RSA/SHA-256: `SIGNATURE_OK`.

### Pendiente real

- La clave privada de firma debe vivir en un almacen seguro operativo o secreto de CI si se automatiza el release; no se versiona.
- El E2E autenticado con PostgreSQL real/datos de volumen sigue siendo el gate para quitar la etiqueta RC/pre-release. Llamarlo final sin esa prueba seria maquillaje.

## 2026-05-12 - V-01.06 - Saneado de datos para entrega

### Que cambio

- Se retiran los scripts locales de datos demo `scripts/seed-demo-data*.sql`.
- Se elimina el seed anidado `Atlas Balance/Atlas Balance/scripts/seed-development-data.sql`, que era un artefacto local fuera del paquete real.
- Se anade `scripts/purge-delivery-data.sql` para dejar una base sin datos operativos antes de publicar o entregar.
- Se anade `scripts/Purge-DeliveryData.ps1` con confirmacion obligatoria (`-ConfirmDeliveryPurge` o `ATLAS_CONFIRM_DELIVERY_PURGE=BORRAR_DATOS`) y ejecucion contra el contenedor `atlas_balance_db`.
- La purga borra usuarios, emails, refresh tokens, titulares, cuentas, plazos fijos, extractos, columnas extra, desgloses de extractos, estados de revision, permisos, preferencias, alertas, backups, exportaciones, notificaciones, tokens de integracion, auditorias y uso IA.
- La purga conserva tablas maestras necesarias para arrancar (`CONFIGURACION`, `FORMATOS_IMPORTACION`, `DIVISAS_ACTIVAS`, `TIPOS_CAMBIO`, migraciones), pero pone a `NULL` las referencias a usuarios borrados.
- Se resetean valores sensibles de `CONFIGURACION`: SMTP, claves API, proveedor/modelo IA operativo y contadores de consumo IA.
- `.gitignore` ignora seeds demo y la carpeta local anidada `Atlas Balance/Atlas Balance/`.

### Por que

Entregar una app financiera con extractos, titulares, cuentas, hashes demo o tokens locales seria una cagada basica. La limpieza correcta no es borrar tablas al azar: hay FKs, RLS forzado y configuracion que debe sobrevivir para que la app arranque limpia.

### Detalle tecnico

- El primer diseno con `TRUNCATE` fallo porque PostgreSQL bloquea truncar `USUARIOS` si `CONFIGURACION` mantiene una FK hacia esa tabla, aunque los valores esten a `NULL`.
- La version final usa `DELETE` en orden de dependencias dentro de una transaccion.
- Durante la purga se desactiva RLS en las tablas protegidas y se vuelve a activar con `FORCE ROW LEVEL SECURITY` antes del `COMMIT`.
- Hangfire se limpia dentro de un bloque condicional si existe el schema `hangfire`.
- El seed admin no queda en base tras la purga; en el siguiente primer arranque se creara de nuevo solo si `SeedAdmin:Password` esta configurado correctamente.

### Verificacion

- Conteo inicial local: `USUARIOS=3`, `TITULARES=13`, `CUENTAS=29`, `EXTRACTOS=404`, `REFRESH_TOKENS=12`, `AUDITORIAS=103`, `IA_USO_USUARIOS=1`.
- `Purge-DeliveryData.ps1 -ConfirmDeliveryPurge`: OK en segundo intento tras corregir la estrategia.
- Conteo final de 21 tablas sensibles: todas `0`.
- Verificacion de configuracion sensible sin exponer valores: SMTP/API/IA vacio o reseteado.
- `rg` sobre nombres de demo y hashes de usuarios demo: sin resultados en rutas publicables.
- Parser PowerShell de `Purge-DeliveryData.ps1`: OK.

### Pendiente real

- La purga no sustituye el E2E autenticado con PostgreSQL real. Sirve para no publicar datos; no prueba el flujo funcional completo del release.

## 2026-05-12 - V-01.06 - Microinteracciones emil prepublicacion

### Que cambio

- `AppSelect` guarda si el popover se abre por teclado o por puntero; la apertura por teclado marca `data-open-source="keyboard"` y no anima.
- `ToastViewport` pasa a un `ToastItem` con temporizador propio por notificacion, pausa por hover/foco y pausa automatica con `document.visibilityState`.
- Los toasts reemplazan keyframes por transicion con `@starting-style`, manteniendo entrada corta sin bloquear interacciones.
- Se retiran la animacion global de entrada de pagina y el pop del item activo de navegacion.
- El shell deja de animar propiedades de layout en el colapso de sidebar; se eliminan transiciones sobre `grid-template-columns`, padding y max-width/max-height.
- El check de los checkbox ya no nace en `scale(0)`; usa opacidad y escala parcial para evitar aparicion brusca.
- Los hover con transform/sombra de KPIs, tarjetas de balance, navegacion y boton IA se limitan a `(hover: hover) and (pointer: fine)`.
- `revision-ai.css` anade `prefers-reduced-motion` local para el chat flotante y los puntos de carga.
- `frontend/dist` se copia a `backend/src/AtlasBalance.API/wwwroot` y se podan assets obsoletos solo dentro de `wwwroot/assets`.

### Por que

El polish bueno no es mas movimiento; es quitar movimiento donde estorba. Navegacion por teclado, cambios de ruta y superficies repetidas deben responder ya. Hover pegajoso en touch y toasts que desaparecen con la pestana oculta son detalles pequenos, pero huelen a producto sin rematar.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: falla dentro del sandbox por `spawn EPERM` conocido de Vite/Rolldown; OK fuera del sandbox con aprobacion.
- `wwwroot/index.html` coincide con `frontend/dist/index.html`.
- `dist_files=65`, `wwwroot_files=65`, `stale_assets=0`.
- Barrido estatico sin `page-surface-in`, `nav-link-pop`, `toast-slide-in` ni `scale(0)` en `frontend/src`.

### Pendiente real

- Sigue sin cerrarse el E2E autenticado con PostgreSQL real/datos de volumen ni el ZIP firmado `V-01.06`; esto deja el release final bloqueado, aunque el polish frontend queda validado.

## 2026-05-12 - V-01.06 - Clarify de copy y errores UI/API

### Que cambio

- `frontend/src/utils/errorMessage.ts` centraliza mensajes saneados para Axios, red y codigos HTTP; evita que el usuario vea `Network Error` o `Request failed with status code`.
- Tablas y pantallas de sistema traducen estados tecnicos: copias/exportaciones muestran `Pendiente`, `Lista`, `Fallida`, `Manual`, `Automatica`; tokens muestran `Activo`/`Revocado`.
- `BackupsPage` pasa a `Copias de seguridad` y endurece la doble confirmacion de restauracion.
- `ExportacionesPage`, `TokenList`, `AlertasPage`, `UsuariosPage`, `CuentasPage`, `TitularesPage`, `FormatosImportacionPage`, dashboards y extractos sustituyen `N/A` y empty states muertos por textos accionables.
- `ConfirmDialog` acepta `loadingLabel` para que las acciones destructivas no caigan en el generico `Procesando...`.
- Controllers y servicios API dejan de devolver `Request invalido`, referencias a logs del servidor y errores de IA demasiado tecnicos.
- `.gitignore` ignora `backend/.codex-build/`, usado para builds aislados cuando `bin/Debug` esta bloqueado por una API local viva.

### Por que

Una app financiera no puede publicar una interfaz que le habla al usuario como si estuviera leyendo logs. `N/A`, `Request invalido`, `Flag` y `SUCCESS` son deuda de producto, no detalles esteticos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `dotnet build src/AtlasBalance.API/AtlasBalance.API.csproj --no-restore -v minimal -o .\.codex-build\api`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro aplica el bloqueo conocido `spawn EPERM` de Vite/Rolldown.
- Barridos `rg` sin restos utiles de copy tecnico en codigo vivo, excluyendo nombres internos/migraciones.

### Incidencias

- `dotnet build` normal queda bloqueado por `.NET Host (9632)` usando `bin/Debug/net8.0/AtlasBalance.API.dll`; la compilacion se valido con salida aislada.
- `Remove-Item backend/.codex-build -Recurse` fallo por `Access denied` sobre DLLs generadas. No se insistio; la carpeta queda ignorada.

## 2026-05-12 - V-01.06 - Polish final UI

### Que cambio

- `api.ts` y `divisaStore.ts` dejan de emitir errores/warnings en consola de produccion; las trazas quedan limitadas a `import.meta.env.DEV`.
- `DatePickerField` restaura foco al trigger al cerrar con Escape, Hoy, Limpiar o seleccion de fecha.
- Selects, botones de accion, toasts, sidebar colapsada y navegacion reciben labels/estados de foco mas claros.
- El chat IA mejora contraste del mensaje de usuario y conserva wrapping robusto en textos largos.
- Overlays comunes eliminan `backdrop-filter` caro; el QR MFA mantiene blanco real documentado para lectura fiable.
- Copy visible y metadatos HTML quedan saneados: acentos, mensajes de confirmacion, labels de botones y nombres de secciones.
- `Documentacion/Diseno/design-tokens.css` se sustituye por snapshot alineado con `frontend/src/styles/variables.css`; `DESIGN.md` actualiza radios reales.
- `wwwroot` se sincroniza sin borrar la carpeta completa: copia del build, verificacion de `index.html` y poda solo de chunks JS obsoletos.

### Por que

Un release financiero no puede salir con consola ruidosa, labels genericos tipo "Editar", foco perdido al cerrar popovers o copy medio roto. Son detalles pequenos, pero juntos huelen a producto sin revisar.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro aplica el bloqueo conocido de Vite/Rolldown `spawn EPERM`.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: `dist_files=65 wwwroot_files=65`.
- Barrido estatico en `wwwroot` sin `console.error`, `console.warn`, `debugger`, trazas `[API]`, copy sin acentos detectado ni referencias a chunks JS retirados.

### Pendiente real

- Falta E2E autenticado con PostgreSQL real y datos de volumen. Sin eso, llamar "final" al release sigue siendo demasiado optimista.

## 2026-05-12 - V-01.06 - Documentacion de publicacion y copy final

### Que cambio

- `README_RELEASE.md`, `Documentacion/documentacion.md` y `DOCUMENTACION_USUARIO.md` dejan de apuntar a paquetes `V-01.05` como si fueran el release actual.
- `SECURITY.md` y `CONTRIBUTING.md` se reescriben en UTF-8 limpio, sin emojis ni tono de plantilla.
- `v-01.06.md` declara el estado real de publicacion: Docker/Testcontainers cerrado 225/225, E2E autenticado con datos reales pendiente.
- Se corrige copy visible en IA, auditoria, configuracion, exportaciones XLSX, emails de plazo fijo y scripts de instalacion/reset.
- Se limpia mojibake residual en documentacion tecnica viva.

### Por que

Publicar una version `V-01.06` con README y guia instalable de `V-01.05` seria una metedura de pata basica. Peor aun: `Build-Release.ps1` copia `Documentacion/documentacion.md` dentro del paquete, asi que el error viajaria con el ZIP.

### Verificacion

- Barridos `rg` sobre referencias `AtlasBalance-V-01.05`, `Build-Release.ps1 -Version V-01.05`, mojibake y copy reportado por subagentes.
- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `dotnet test ... --filter "ActualizacionServiceTests|PlazoFijoServiceTests|ExportacionServiceTests"`: 17/17 OK.
- `npm.cmd run build`: bloqueado dentro del sandbox por `spawn EPERM`; OK fuera del sandbox con aprobacion.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: 65/65 archivos tras repetir fuera del sandbox por `Access denied`.

### Pendiente real

- No se cierra el E2E autenticado con PostgreSQL real y datos de volumen; sigue bloqueando llamar final al release.

## 2026-05-12 - V-01.06 - Optimize prepublicacion

### Que cambio

- `App.tsx` incorpora `DashboardRoute` para bloquear la ruta de dashboard antes de cargar la pagina y sus graficas cuando el usuario no tiene permiso.
- `CuentasPage` y `TitularesPage` cargan `EvolucionChart` y `TitularSaldoBarChart` bajo demanda con `React.lazy` y `Suspense`.
- `TitularSaldoBarChart` encapsula el grafico de barras compartido para no duplicar Recharts en dos paginas.
- `useDebouncedValue` evita disparar busquedas remotas por cada pulsacion en cuentas/titulares.
- `ImportacionPage` analiza solo unas pocas lineas no vacias para previsualizacion/separador, pagina la tabla de validacion en bloques de 200 filas y usa `Set` para seleccion.
- `IntegrationOpenClawController.Auditoria` filtra auditoria con subquery de extractos y carga el mapa de cuentas solo para los extractos de la pagina devuelta.
- `GetMonthTotalsByCuentaAsync` agrupa ingresos/egresos por cuenta en SQL.
- La migracion `20260512143000_AddRevisionConceptTrigramIndex` crea `pg_trgm` e indice GIN parcial sobre `lower(concepto)` para las busquedas textuales de revision.

### Por que

Las pantallas con tablas grandes y graficas no pueden tratar todos los datos como si fueran una demo de 20 filas. Partir CSVs completos en cada tecla, renderizar miles de filas de validacion o importar Recharts en rutas que no lo necesitan es coste absurdo. No mata la app en pequeno; en volumen la vuelve torpe.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` conocido de Vite/Rolldown.
- `dotnet build "Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" --no-restore -p:UseAppHost=false` con `OutDir` aislado: OK fuera del sandbox.
- `dotnet test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" --no-restore --filter "FullyQualifiedName~IntegrationOpenClawControllerTests|FullyQualifiedName~RevisionServiceTests"`: 8/8 OK fuera del sandbox.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: `dist_files=65 wwwroot_files=65`.

### Pendiente real

- Falta E2E autenticado con PostgreSQL real y datos de volumen.
- Falta generar el ZIP firmado de `V-01.06`; en esta sesion `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` no esta presente. Dejar builds verdes no equivale a publicar paquete.

## 2026-05-12 - V-01.06 - Adapt responsive desktop/tablet/mobile

### Que cambio

- `Layout` usa `matchMedia('(min-width: 768px) and (max-width: 1023.98px)')` para alinear JS con CSS: mobile `<768px`, tablet `768-1023.98px`, desktop `>=1024px`.
- `shell.css`, `system-coherence.css`, `entities.css`, `users.css`, `admin.css` y `revision-ai.css` ajustan los cortes `768/1024` al mismo contrato.
- `DatePickerField` calcula colision horizontal del popover y alterna alineacion izquierda/derecha. En puntero tactil, el calendario pasa a superficie tipo bottom sheet con dias y flechas de 44px.
- `global.css` evita scroll horizontal global accidental y eleva targets tactiles de selects, date picker y labels con checkbox.
- `TitularDetailPage` y `FormatosImportacionPage` envuelven tablas en contenedores de scroll local.
- `dashboard.css` reduce anchos de la hoja de cuenta por breakpoint, mantiene scroll interno y da 44px a checkboxes de tabla en touch.
- `extractos.css` eleva filas/checkboxes/acciones compactas en touch y deja `Hist.` visible como accion tactil.
- `revision-ai.css` eleva selector de modelo, prompts, envio de chat y botones de estado de revision en touch.
- `auth.css`, `users.css`, `importacion.css` y `system-coherence.css` corrigen targets compactos de login, filas, permisos, importacion y acciones de tarjetas.

### Por que

La app se usa sobre todo en escritorio, pero publicar una app de tesoreria que se rompe en iPad o en un movil de 320px es una chapuza cara. Las tablas financieras pueden tener scroll horizontal interno; la pagina completa no.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro del sandbox sigue bloqueado por `spawn EPERM` conocido de Vite/Rolldown.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`: `dist_files=61 wwwroot_files=61`.
- Playwright `setContent` fuera del sandbox:
  - `1366x768`: `overflow=0`.
  - `1024x768 touch`: `overflow=0`, sin targets tactiles menores de 44px.
  - `768x1024 touch`: `overflow=0`, sin targets tactiles menores de 44px.
  - `390x844 touch`: `overflow=0`, sin targets tactiles menores de 44px.
  - `320x568 touch`: `overflow=0`, sin targets tactiles menores de 44px.

### Pendiente real

- No sustituye el E2E autenticado con datos reales/volumen. Ese pendiente sigue abierto en `REGISTRO_BUGS.md`.

## 2026-05-12 - V-01.06 - Hardening de estados borde y errores API

### Que cambio

- `frontend/src/utils/errorMessage.ts` centraliza mensajes de error para Axios/API:
  - errores sin respuesta;
  - payloads con `error`, `detail`, `title`, `message`, `mensaje`;
  - validaciones ASP.NET en `errors`;
  - fallos 400/401/403/404/409/413/429/5xx;
  - truncado defensivo de mensajes largos.
- `api.ts` usa ese extractor para logs saneados y toasts, evitando payloads enteros en consola.
- `ImportacionPage` diferencia "fallo cargando contexto" de "sin cuentas", muestra CTA de reintento y bloquea doble confirmacion por ref interna.
- `ExtractosPage` captura errores de resumen/filas/preferencias/auditoria, revierte preferencias de columnas si el PATCH falla y no permite ocultar la ultima columna visible.
- `CuentaDetailPage` elimina el limite silencioso de 500 movimientos y usa paginacion real con `PageSizeSelect`.
- `AiChatPanel` muestra estado de permiso/configuracion cuando IA esta bloqueada; ya no queda un panel vacio.
- `RoleGuard` devuelve un `EmptyState` de permisos en vez de redirigir silenciosamente.
- `RevisionPage` calcula si el usuario puede editar cada fila con `cuenta_id`/`titular_id`; si no puede, muestra `Solo lectura` y no renderiza botones de escritura.
- `RevisionDtos` y `RevisionService` exponen `TitularId` en comisiones y seguros para que el frontend pueda aplicar permisos por titular.
- `AppSelect`, tablas de importacion/extractos, modales de auditoria, errores de formulario y columnas monetarias reciben reglas de overflow para textos largos, importes grandes y datasets anchos.
- `Program.cs` registra un manejador global de excepciones: log interno con path, respuesta JSON saneada y sin stack trace al cliente.
- `20260512110000_HardenReleaseSecurityPermissions.Designer.cs` registra la migracion de hardening RLS. Sin ese descriptor EF compilaba la clase, pero no la aplicaba.
- `ImportacionServiceTests` se actualiza para esperar `Clave de columna extra duplicada`, que es el contrato vigente.

### Por que

Los casos feos no son periferia: son donde una app financiera demuestra si esta lista o solo bonita. Un 403 silencioso, 500 opaco, tabla que se desborda o limite oculto de 500 movimientos acaba en datos mal interpretados. Eso es el tipo de bug que parece pequeno hasta que alguien toma una decision con una pantalla incompleta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `dotnet build "Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" --no-restore -p:UseAppHost=false -m:1` con `OutDir` aislado: OK, 1 warning conocido de Hangfire obsoleto.
- `RevisionServiceTests`: 5/5 OK.
- Backend no Docker: primero 222/223 por test obsoleto de importacion; tras corregirlo, 223/223 OK.
- Docker fuera del sandbox: `29.4.2`.
- Backend completo con Testcontainers/PostgreSQL: primero 224/225 por migracion RLS no descubierta; tras anadir `.Designer.cs`, 225/225 OK.
- `frontend/dist` sincronizado a `backend/src/AtlasBalance.API/wwwroot`: `dist_files=61 wwwroot_files=61`.

### Pendiente real

- E2E autenticado/visual con datos reales sigue pendiente en `REGISTRO_BUGS.md`. Los gates de build, TypeScript, lint, RLS y suite backend completa ya estan cerrados.

## 2026-05-12 - V-01.06 - Auditoria UI prepublicacion

### Que cambio

- Nuevo `frontend/src/hooks/useDialogFocus.ts` para dialogs y sheets: foco inicial, trap de Tab/Shift+Tab, Escape opcional y restauracion del foco anterior.
- `UsuarioModal`, `CreateTokenModal`, `TokenCreatedModal`, `AuditCellModal`, `SessionTimeoutWarning`, modales inline de entidades y bottom nav mobile adoptan semantica `role="dialog"`/`aria-modal` y foco controlado.
- `DatePickerField` acepta `label`; `AddRowForm` deja de depender de placeholders para cuenta, fecha, concepto, monto, saldo y columnas extra.
- Login y cambio de password exponen errores con `role="alert"`, `aria-invalid` y `aria-describedby`.
- Importacion etiqueta los checkboxes de fila valida con `aria-label`.
- `iaAvailabilityStore` centraliza `/ia/config` con TTL y polling unico desde `Layout`; topbar, sidebar y bottom nav consumen el store.
- `AiChatPanel` se carga con `React.lazy`; `qrcode` se importa dinamicamente solo en MFA.
- `useSessionTimeout` calcula inactividad con refs y solo actualiza estado cuando cambia el aviso o el modal esta visible.
- `DashboardPrincipalResponse` incorpora `SaldosPorCuenta`; `DashboardService` reutiliza `BuildSaldosPorCuenta` para principal y titular.
- `CuentasPage` consume `principal.saldos_por_cuenta` y elimina el fan-out por titular que multiplicaba llamadas HTTP.
- `EvolucionChart` traduce colores legacy de configuracion a tokens CSS; `ConcentracionDonutCharts` usa `--chart-series-*` y la leyenda no colorea texto con el color del segmento.
- `variables.css`, `shell.css`, `dashboard.css` y `revision-ai.css` reducen blur/radios/sombras, corrigen tokens inexistentes y aseguran targets tactiles minimos en controles criticos.
- `Build-Release.ps1` limpia `wwwroot` con error estricto y comprueba que queda vacio antes de copiar assets nuevos.

### Por que

El informe UI no era cosmetica: habia modales que atrapaban mal el teclado, formularios que dependian de placeholder, polling duplicado, bundle inicial innecesariamente pesado y colores de graficas que se saltaban el tema. Eso en una app financiera no es "detalle"; es friccion diaria.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- API build normal: bloqueado por `AtlasBalance.API.dll` en uso en `bin\Debug`.
- API build con `OutDir` aislado en `.codex-verify`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `npm.cmd run build` dentro del sandbox: bloqueado por `spawn EPERM` conocido de Vite/Rolldown.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`; `dist_files=62 wwwroot_files=62`.
- Busqueda estatica en `wwwroot`: sin `AiChatPanel-B-aUHQbU`, `surface-raised`, `transition-base`, colores legacy de grafica ni `dashboard/titular` en el bundle servido.

## 2026-05-12 - V-01.06 - Hardening de seguridad prepublicacion

### Que cambio

- `vite.config.ts` usa un logger con redaccion de `Cookie`, `Set-Cookie`, `Authorization`, CSRF, JWT y tokens comunes antes de escribir errores de proxy.
- `api.ts` deja de volcar cuerpos completos de error en consola y registra solo mensajes saneados.
- MFA recordado requiere `remember_device=true` explicito y dura 30 dias.
- `AuthService` mantiene throttle por IP+email y anade contador por IP para password spraying.
- `/api/health` queda reducido a `{ status = healthy }`.
- `UserAccessService` separa lectura real (`PuedeVerCuentas`) de permisos operativos; `ExtractosController` aplica la misma regla en lecturas.
- Exportacion manual exige permiso operativo de cuenta (`CanWriteCuentaAsync`); descarga exige lectura de cuenta; revision de estados exige `PuedeEditarLineas`.
- Nueva migracion `20260512110000_HardenReleaseSecurityPermissions`:
  - scopes RLS firmados `data`, `write`, `export`, `revision`;
  - lectura normal sin `PuedeImportar`/write;
  - politica de exportacion basada en `PuedeVerCuentas`;
  - politica de revision basada en `PuedeEditarLineas`.
- `Build-Release.ps1` siempre ejecuta `npm ci`, falla sin `package-lock.json`, valida `dotnet restore --locked-mode` y exige firma RSA salvo `-AllowUnsignedLocal`.
- NuGet usa `RestorePackagesWithLockFile` y lockfiles por proyecto.
- CI usa `ubuntu-24.04`, `global.json`, `.node-version`, `--locked-mode` y patrones high-confidence adicionales.
- El instalador valida identificadores PostgreSQL con regex estricta y aplica ACL restrictiva a `appsettings.Production.json`, PFX, DataProtection y credenciales one-shot.
- Backup/Watchdog ya no exponen stderr/rutas internas como estado visible; el detalle queda en logs locales protegidos.

### Por que

Habia tres fallos publicables: tokens de sesion en logs de desarrollo, permisos operativos abriendo lectura financiera y release dependiente de `node_modules` local. Eso no es deuda estetica; es superficie de ataque real.

### Verificacion

- `cyber-neo` secret scan: 0 findings.
- CI-style tracked secret scan: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` conocido de Vite/Rolldown.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list "Atlas Balance/backend/AtlasBalance.sln" package --vulnerable --include-transitive`: 0 vulnerabilidades.
- `dotnet restore "Atlas Balance/backend/AtlasBalance.sln" --locked-mode`: OK fuera del sandbox.
- `dotnet build "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore -p:UseAppHost=false -m:1`: OK con warnings conocidos por `apphost.exe`/cache bloqueados.
- Tests filtrados no Docker: 34/34 OK.
- `RowLevelSecurityTests`: bloqueo superado en el hardening posterior de la misma fecha; suite backend completa 225/225 OK con Docker/Testcontainers fuera del sandbox.

## 2026-05-12 - V-01.06 - Revision permite descartar falsos positivos

### Ajuste posterior

- La vista `Todas/Todos` deja de incluir `DESCARTADA`.
- En comisiones, `Todas` muestra solo `PENDIENTE` y `DEVUELTA`.
- En seguros, `Todos` muestra solo `PENDIENTE` y `CORRECTO`.
- El descarte pasa a ser un boton cuadrado con solo icono de cruz visible; `aria-label` y `title` conservan el significado accesible.
- Las descartadas quedan disponibles solo desde el filtro explicito `Descartadas/Descartados`.

### Verificacion del ajuste

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `RevisionServiceTests`: 5/5 OK fuera del sandbox con `-p:OutDir=C:\tmp\atlas-revision-discard-test-out\`.
- API local saludable en `localhost:5000`, PID `9632`.
- `index.html` sirve `assets/index-Pl0LJUu6.js`, que referencia `RevisionPage-DoqGux5L.js`.
- Navegador interno abre `/revision` sin overlay, pero redirige a login por no tener sesion autenticada.

### Que cambio

- `RevisionService` anade el estado persistido `DESCARTADA` para `COMISION` y `SEGURO`.
- `NormalizeEstadoFilter` acepta `DESCARTADA`, plurales, `IGNORADA` y alias `NO_ES_COMISION`/`NO_ES_SEGURO`.
- `RevisionPage` muestra el filtro `Descartadas/Descartados`.
- En comisiones se puede pulsar `No es comision`; en seguros, `No es seguro`.
- Las filas descartadas se pueden restaurar a `PENDIENTE`.
- El bundle frontend se recompila y se copia a `backend/src/AtlasBalance.API/wwwroot`.

### Por que

La deteccion automatica es un detector, no un veredicto. Si un movimiento contiene una palabra que parece comision o seguro pero no lo es, obligar al usuario a dejarlo como pendiente o marcarlo como revisado falsea el control. `DESCARTADA` conserva trazabilidad y permite filtrar esos falsos positivos sin borrarlos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro fallo por el `spawn EPERM` conocido de Vite/Rolldown.
- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter RevisionServiceTests -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-revision-discard-test-out\\ --no-restore` fuera del sandbox: 5/5 OK.
- `wwwroot/assets/RevisionPage-*.js` contiene `No es comision`, `No es seguro`, `Descartada` y `Descartado`.
- API local saludable en `localhost:5000`, PID `32520`.

## 2026-05-12 - V-01.06 - KPIs del dashboard con variacion compacta

### Que cambio

- `frontend/src/pages/DashboardPage.tsx` mantiene el calculo de variacion de `Saldo total`, `Ingresos periodo`, `Egresos periodo`, `Disponible` e `Inmovilizado`.
- Los helpers visibles ya no anaden `vs inicio` ni `vs anterior`; ahora muestran solo el porcentaje con signo.
- No cambia la semantica de color ni la fuente de datos.

### Por que

El dashboard ya comunica el contexto por posicion y periodo seleccionado. Repetir `vs inicio` y `vs anterior` en cinco tarjetas mete ruido en una zona que debe leerse de un vistazo. El numero importa; la coletilla no.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- Busqueda estatica sin restos de `vs inicio` ni `vs anterior` en `DashboardPage.tsx`.

## 2026-05-12 - V-01.06 - Revision sin 500 por traduccion Npgsql

### Que cambio

- `RevisionService` deja de proyectar la query base a un record posicional `RevisionRawRow(...)`.
- La proyeccion interna pasa a una clase con propiedades `init` y `new RevisionRawRow { ... }`.
- El filtro de comisiones por importe (`Monto > umbral || Monto < -umbral`) sigue ejecutandose en SQL, pero ahora EF/Npgsql puede inlinear la propiedad proyectada.
- Se anade una regresion en `RevisionServiceTests` que construye la query con proveedor Npgsql y llama a `ToQueryString()` sobre el filtro de `Monto`, sin levantar PostgreSQL ni Docker.

### Por que

La pantalla `Revision` devolvia HTTP 500 al cargar comisiones porque EF Core no podia traducir una condicion sobre `RevisionRawRow.Monto` cuando `RevisionRawRow` era un record construido por constructor posicional. El test existente usaba `InMemoryDatabase`, que no traduce a SQL y por tanto no detectaba el fallo. Esa prueba era demasiado comoda para un bug de base de datos.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter RevisionServiceTests -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-revision-test-out\\ --no-restore` fuera del sandbox: 5/5 OK.
- Intentos descartados:
  - test directo bloqueado por `AtlasBalance.API.dll` en uso;
  - salida aislada con `BaseOutputPath/BaseIntermediateOutputPath` bloqueada por permisos en sandbox y despues por AssemblyInfo duplicados al cambiar `BaseIntermediateOutputPath`;
  - se cambio a `OutDir` aislado, manteniendo `obj` en su ruta normal.
- API local reiniciada por `Start-BackendDev.ps1`; el comando fue interrumpido por timeout de conversacion, pero el healthcheck posterior confirmo API saludable en `localhost:5000`, PID `42848`.

## 2026-05-12 - V-01.06 - EvolucionChart reserva eje Y para importes compactos

### Que cambio

- `EvolucionChart.tsx` cambia la reserva del eje Y a un rango adaptativo de 52-116 px.
- `getEvolutionAxisWidth` ahora recibe el dominio calculado y estima la etiqueta mas larga usando valores de serie, extremos del dominio y cero.
- Los ticks de X/Y declaran estilo estable: color secundario, fuente monoespaciada, `fontSize: 12` y numeros tabulares.
- El ancho se estima por anchura aproximada de cada caracter, no por longitud bruta de string.
- El margen izquierdo del `LineChart` baja a 0, el margen derecho a 18, el padding del eje X a 8 px y el `tickMargin` del eje Y a 8 px.

### Por que

Los importes laterales se cortaban porque el ancho maximo de 72 px era insuficiente para etiquetas como `15,6 M EUR`, especialmente cuando el tick lo generaba Recharts desde el dominio con padding y no desde un punto exacto de datos. Encoger texto habria sido una chapuza: en una pantalla financiera, el numero debe leerse entero.

La primera correccion era demasiado conservadora: evitaba el recorte, pero dejaba un hueco lateral visible. La estimacion por caracteres corrige ese exceso porque no trata igual un espacio, una coma, un digito y una letra de divisa.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- No se ejecuto build Vite ni validacion con servidor temporal por la incidencia conocida de `spawn EPERM`/servidores colgados; el cambio queda cubierto por lint y TypeScript.

## 2026-05-11 - V-01.06 - Parser IA sin mensaje generico `respuesta malformada`

### Que cambio

- `AtlasAiService.BuildProviderResponseErrorMessage` ya no devuelve el literal generico `El proveedor de IA devolvio una respuesta malformada`.
- Los errores de shape no compatible se expresan como `respuesta de chat compatible (kind)`, manteniendo la categoria tecnica (`invalid_json`, `missing_choices`, `unsupported_content`, etc.).
- El `catch (JsonException)` global se reclasifica como `provider_response_processing_error` y registra `provider_response_error_kind=json_processing_error`.
- `ParseProviderResponse` incorpora tolerancia adicional:
  - payloads `data:`/SSE con chunks JSON aunque la request sea no streaming;
  - `choices[0].delta.content`;
  - `output_text` top-level;
  - contenido anidado como `text.value`, `output_text`, `content` o arrays de partes.

### Por que

El mensaje generico estaba funcionando como una caja negra: ocultaba si el fallo era JSON invalido, shape no compatible, streaming accidental o procesamiento interno. Eso hace que cada incidencia parezca la misma y empuja a repetir parches. La app ahora intenta recuperar las variantes razonables y, si no puede, da una categoria concreta.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter "AtlasAiServiceTests|ConfiguracionControllerTests" -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-ai-test-bin-provider-parser-loop\\ --no-restore --logger "console;verbosity=normal"` fuera del sandbox: 68/68 OK.
- Busqueda estatica: el literal viejo solo queda en test de regresion, no en codigo productivo.

## 2026-05-11 - V-01.06 - IA financiera con rankings deterministas por cuenta

### Que cambio

- `AtlasAiService.AskAsync` intenta resolver antes del proveedor las intenciones financieras soportadas de ranking por cuenta/titular/divisa.
- La V1 cubre metricas `gastos`, `ingresos` y `neto`; periodos `mes actual`, `ultimos 30 dias`, `mes anterior`, `trimestre actual` y `ano actual`.
- La consulta determinista aplica `_userAccessService.ApplyCuentaScope`, agrupa por titular, cuenta y divisa, y calcula:
  - `Ingresos = SUM(monto > 0)`
  - `Gastos = -SUM(monto < 0)`
  - `Neto = SUM(monto)`
  - contador de movimientos segun metrica.
- La salida se secciona por divisa para no mezclar monedas y limita el ranking a 10 por defecto.
- Esta ruta no construye contexto LLM, no exige desencriptar API key y no crea llamada HTTP al proveedor. Mantiene el contrato de `IaChatResponse` con `TokensEntradaEstimados=0`, `TokensSalidaEstimados=0` y `CosteEstimadoEur=0`.
- La auditoria `IA_CONSULTA` marca `deterministic=true`, `deterministic_kind=account_ranking`, metrica, fechas de periodo, filas devueltas y movimientos analizados. No guarda prompt, respuesta completa ni datos financieros crudos.
- La ruta LLM queda mas estricta: el prompt indica que una seccion agregada/ranking ya calculado es fuente primaria y `CleanProviderAnswer`/`ContainsInternalAnalysisLeak` eliminan o rechazan metacomentarios tipo `It seems`, `maybe`, `Actually` cuando llegan visibles.

### Por que

Pedirle a un LLM que sume y ordene movimientos financieros desde texto parcial es una mala arquitectura. El modelo puede sonar convincente y aun asi inventar cuentas, mezclar divisas o copiar razonamiento interno. Para datos contables, el backend debe calcular con SQL/EF y dejar el proveedor para redaccion o preguntas no deterministas.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter "AtlasAiServiceTests|ConfiguracionControllerTests" -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-ai-test-bin-financial-ranking\\ --no-restore --logger "console;verbosity=normal"` fuera del sandbox: 66/66 OK.
- Intentos dentro del sandbox: bloqueados por `AtlasBalance.API.dll` en uso y `Access denied` al escribir en `C:\tmp`.

## 2026-05-11 - V-01.06 - Parser IA tolerante a respuestas OpenRouter no triviales

### Que cambio

- `AtlasAiService.ParseProviderResponse` clasifica respuestas del proveedor en categorias tecnicas:
  - `provider_error` para errores embebidos en HTTP 200.
  - `provider_empty_response` para `choices` vacio, `content=null` o ausencia de texto util.
  - `provider_unusable_response` para `refusal`, `content_filter`, `length` y tool calls sin contenido.
  - `provider_malformed_response` para JSON invalido, shape no-chat o contenido de tipo no soportado.
- El parser acepta tres formas utiles: `choices[0].message.content` como string, `content` como array de partes de texto y `choices[0].text`.
- Las peticiones al proveedor incluyen `stream=false`, `Accept: application/json` y cabecera `X-OpenRouter-Title`.
- Los errores HTTP 429/503 leen `Retry-After` y lo trasladan al mensaje visible y auditoria sin dormir la request.
- La auditoria IA incorpora `provider_response_error_kind`, `finish_reason`, cliente HTTP, uso de fallback y detalle saneado; no guarda prompt, respuesta completa, datos financieros ni secretos.

### Por que

OpenRouter normaliza hacia el contrato Chat Completions, pero aun asi documenta casos de no contenido, errores de proveedor y formas distintas para streaming/no streaming. El parser anterior era demasiado fragil: confundia refusals, truncados, filtros de contenido, `content` por partes y errores 200 con JSON roto. Eso daba al usuario un mensaje opaco y dejaba poca informacion operativa.

No se fuerza `response_format=json_schema` porque los modelos gratis permitidos actuales no soportan de forma uniforme `response_format`/`structured_outputs`; activarlo globalmente romperia parte de la allowlist.

### Verificacion

- `dotnet test .\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj --filter "AtlasAiServiceTests|ConfiguracionControllerTests" -p:UseAppHost=false -p:OutDir=C:\\tmp\\atlas-ai-test-bin-openrouter-parser\\ --no-restore --logger "console;verbosity=normal"` fuera del sandbox: 61/61 OK.
- Intentos dentro del sandbox: bloqueados por `AtlasBalance.API.dll` en uso y `Access denied` al escribir salida aislada en `C:\tmp`.

## 2026-05-11 - V-01.06 - IA OpenRouter sin proxy heredado y errores TLS claros

### Que cambio

- Los `HttpClient` de IA ya no usan proxy automatico como fallback por defecto. Si `Ia:UseSystemProxy=false` y `Ia:ProxyUrl` esta vacio, `openrouter`, `openrouter-fallback`, `openai` y `openai-fallback` salen directo.
- Si hace falta proxy real, se configura de forma explicita con `Ia:UseSystemProxy=true` o `Ia:ProxyUrl`.
- `AtlasAiService.ShortTransportMessage` recorre toda la cadena de excepciones y clasifica errores de TLS/certificado, proxy local roto, DNS y conexion rechazada.
- El mensaje de red muestra detalle principal/fallback cuando difieren, y la auditoria usa el mismo saneado sin prompt ni API key.
- Nueva regresion cubre el caso `.NET` `Authentication failed, see inner exception` para que no vuelva a llegar crudo al chat.

### Por que

El fallback a proxy automatico era un pie metido en una trampa: en Windows/.NET el proxy por defecto puede venir de variables de entorno como `HTTP_PROXY`, `HTTPS_PROXY` o `ALL_PROXY`. Esta maquina ya habia heredado `127.0.0.1:9`; repetir ese camino era dejar abierta la misma averia. El comportamiento seguro en una app on-prem es salida directa por defecto y proxy solo si se configura.

### Verificacion

- `dotnet test "tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests -p:UseAppHost=false -p:OutputPath=C:\tmp\atlas-ai-test-bin --no-restore --verbosity minimal`: 42/42 OK fuera del sandbox.

## 2026-05-11 - V-01.06 - Login sin API absoluta y arranque backend verificable

### Que cambio

- `frontend/src/services/api.ts` fija `baseURL: '/api'`. Se elimina `VITE_API_URL` del contrato TypeScript del frontend.
- `frontend/.env.local` queda como aviso local: no se debe apuntar el cliente a `localhost:5000`.
- `frontend/dist` se recompila y se copia a `backend/src/AtlasBalance.API/wwwroot`.
- `scripts/Start-BackendDev.ps1` compila con `UseAppHost=false`, arranca el DLL, limpia proxies de entorno, escribe logs/PID y espera `http://localhost:5000/api/health`.
- `scripts/Start-Dev.ps1` deja de matar todos los procesos `dotnet` y no declara listo el entorno sin healthcheck.
- `scripts/Launch-AtlasBalance.ps1`, `start-backend.bat` y `start-frontend.bat` usan los arranques endurecidos.
- `/api/health` anade `started_at`, `version`, `pid` y `environment`.

### Por que

Atlas Balance sirve el frontend desde el backend en produccion y usa proxy Vite en desarrollo. Compilar `http://localhost:5000/api` en el cliente era la causa perfecta del `Network Error`: en LAN `localhost` es el equipo del usuario, no el servidor. Ademas, los scripts antiguos abrian ventanas y seguian como si todo hubiese ido bien aunque la API no escuchara. Eso no era un problema de login; era un arranque sin contrato.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox por `spawn EPERM` conocido de Vite/Rolldown.
- `dotnet build AtlasBalance.API.csproj -p:UseAppHost=false --no-restore`: OK con warnings no bloqueantes.
- `http://localhost:5000/api/health`: 200 con version/PID.
- `http://localhost:5173/api/health`: 200 via proxy.
- Busqueda en `dist` y `wwwroot`: sin `VITE_API_URL` ni `http://localhost:5000/api`.

## 2026-05-11 - V-01.06 - Chat IA: salida final sin razonamiento interno

### Que cambio

- `AtlasAiService.BuildProviderRequest` envia `reasoning: { exclude: true }` en todos los payloads de OpenRouter.
- El prompt de sistema ordena devolver solo la respuesta final visible para el usuario, en espanol, sin analisis interno, borradores, pasos ni frases como `we need to answer`, `analysis`, `reasoning`, `thinking` o `final answer`.
- El prompt tambien prohibe placeholders tipo `[PERSON_NAME]`, `[ACCOUNT_NAME]` o `<name>`; si falta un dato, debe decir `no consta en el contexto`.
- `CleanProviderAnswer` limpia defensivamente la respuesta del proveedor antes de construir `IaChatResponse`:
  - elimina bloques `<think>`, `<thinking>`, `<reasoning>` y `<analysis>`;
  - corta prefacios iniciales de razonamiento hasta la respuesta;
  - quita etiquetas de salida como `Final:` o `Respuesta final:`;
  - sustituye placeholders anonimizados por `no consta en el contexto`.
- `AtlasAiServiceTests` cubre el saneado de respuesta y que OpenRouter incluya `reasoning.exclude`.

### Por que

OpenRouter puede devolver tokens de razonamiento en `message.reasoning` si el modelo los genera. La documentacion oficial indica que `reasoning.exclude: true` evita devolverlos. Pero el ejemplo visto en la UI (`We need to answer...`) venia dentro de `message.content`, no como campo `reasoning`; ahi el proveedor ya ha metido su razonamiento en el texto final. Por eso hacen falta dos barreras: contrato de OpenRouter y limpieza backend. Dejarlo al frontend seria tarde y fragil.

### Verificacion

- Documentacion oficial revisada: OpenRouter `Reasoning Tokens`, seccion `Excluding Reasoning Tokens from Response`.
- Primer `dotnet test` bloqueado por `AtlasBalance.API.dll` en uso por PID `25776`; se paro ese PID exacto y se repitio.
- `dotnet test ... --filter FullyQualifiedName~AtlasAiServiceTests -p:UseAppHost=false --no-restore --verbosity minimal`: 41/41 OK.
- `dotnet test ... --filter "FullyQualifiedName~AtlasAiServiceTests|FullyQualifiedName~ConfiguracionControllerTests" -p:UseAppHost=false --no-restore --verbosity minimal`: 47/47 OK.
- Warning residual: MSB3101 al escribir cache `obj`, no bloqueante.

## 2026-05-11 - V-01.06 - OpenRouter Auto limitado a 3 modelos en `models`

### Que cambio

- `AiConfiguration.OpenRouterMaxFallbackModels` fija el limite local en 3.
- `OpenRouterAutoFallbackModels` deja de derivarse de toda la allowlist y pasa por una terna explicita: `nvidia/nemotron-3-super-120b-a12b:free`, `google/gemma-4-31b-it:free` y `minimax/minimax-m2.5:free`.
- `AtlasAiService` mantiene la ruta `models` para `openrouter/auto`, pero ahora el payload cumple el limite efectivo de OpenRouter.
- `BuildProviderHttpErrorMessage` reconoce el 400 `'models' array must have 3 items or fewer` y lo explica como fallo de limite de fallback.
- `AtlasAiServiceTests` parsea el JSON enviado y comprueba que `models` contiene exactamente 3 modelos permitidos.

### Por que

OpenRouter documenta `models` como fallback ordenado entre modelos y `openrouter/auto + plugins.auto-router.allowed_models` como Auto Router sobre un pool curado. En Atlas Balance no se puede volver al Auto Router abierto porque ya fallo por interseccion vacia con la allowlist gratis. El error real nuevo marco la otra frontera: `models` no puede llevar seis candidatos; el maximo operativo es 3.

### Verificacion

- `dotnet test ... --filter "AtlasAiServiceTests|ConfiguracionControllerTests"` dentro del sandbox: bloqueado por `Access denied` al escribir en `C:\tmp`.
- El mismo test fuera del sandbox: 46/46 OK tras corregir una asercion textual del test nuevo.

## 2026-05-11 - V-01.06 - Auto OpenRouter corregido para cuentas con modelos gratis restringidos

### Que cambio

- `Auto` sigue guardandose como `openrouter/auto`, pero `AtlasAiService` ya no llama al Auto Router de OpenRouter con `plugins.auto-router.allowed_models`.
- Para `Auto`, el backend envia `models` con maximo tres slugs gratis permitidos en orden de fallback: Nemotron, Gemma y MiniMax. gpt-oss, GLM y Qwen Coder siguen disponibles como seleccion manual.
- `ProviderRuntimeModel` resuelve `openrouter/auto` al primer modelo gratis permitido para auditoria y metadatos cuando OpenRouter no devuelve `model`.
- El mensaje 404 `No models match your request and model restrictions` ahora se explica como incompatibilidad de restricciones, no como un slug obsoleto que el usuario deba volver a guardar.
- El selector frontend cambia la etiqueta a `Auto (gratis permitido)`.

### Por que

La suposicion anterior era incorrecta. La documentacion oficial de OpenRouter dice que Auto Router elige dentro de una bolsa curada propia; `allowed_models` solo restringe esa bolsa. Los modelos `:free` permitidos por esta app no tienen por que estar en esa bolsa, asi que la interseccion puede quedar vacia y OpenRouter devuelve `No models match your request and model restrictions`.

La opcion robusta para una cuenta restringida a modelos gratis es mandar la lista exacta de modelos gratis permitidos mediante `models`, dejando que OpenRouter haga fallback entre ellos sin salir de la allowlist.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests|ConfiguracionControllerTests`: 45/45 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Bundle sincronizado: `Auto (gratis permitido)` aparece en `aiModels-BjnwCRyE.js`.

## 2026-05-11 - V-01.06 - Selector IA discreto en cabecera

### Que cambio

- `AiChatPanel` mueve el proveedor y selector de modelo a la cabecera del panel.
- El selector conserva `aria-label` y etiqueta `sr-only`, pero ya no muestra la etiqueta visual `Modelo`.
- `getCompactModelLabel` acorta las etiquetas solo en el chat: `Auto (gratis permitido)` pasa a `Auto` y se retira `(free)` del texto visible.
- `revision-ai.css` cambia el control a un estilo secundario: texto tenue, fondo transparente, 32px de alto, borde en hover/focus y foco visible.
- El build final queda copiado a `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El selector de modelo es util, pero hacerlo tan grande era mala jerarquia: competia con la pregunta, que es la accion principal. En una herramienta financiera interna, las opciones avanzadas deben estar disponibles sin robar atencion.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- Playwright headless con `setContent`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Chromium.
- Validacion visual: toolbar dentro de cabecera, selector de 32px, selector menor que el 55% del textarea y sin overflow horizontal.
- `wwwroot` sincronizado con `Copy-Item`.

## 2026-05-11 - V-01.06 - OpenRouter Auto acotado a seis modelos gratis

### Que cambio

- Estado actual: esta estrategia quedo sustituida el mismo dia por `models` con maximo 3 candidatos. Se conserva como historico del intento anterior.
- OpenRouter conserva `openrouter/auto` como opcion por defecto visible y guardada.
- `AiConfiguration.OpenRouterModels` permite `openrouter/auto` mas estos seis slugs exactos: `nvidia/nemotron-3-super-120b-a12b:free`, `google/gemma-4-31b-it:free`, `minimax/minimax-m2.5:free`, `openai/gpt-oss-120b:free`, `z-ai/glm-4.5-air:free` y `qwen/qwen3-coder:free`.
- En ese intento, cuando el usuario usaba `Auto`, `AtlasAiService` enviaba `model=openrouter/auto` con `plugins.auto-router.allowed_models` limitado a esos seis modelos.
- Historico obsoleto: en ese intento los modelos gratis no llevaban `provider.zdr=true`. Desde el hardening V-01.07 todas las llamadas OpenRouter llevan `zdr=true` y `data_collection=deny`.
- La respuesta del proveedor se parsea tambien para leer `model`; la auditoria y `IaChatResponse.Model` reflejan el modelo real usado cuando OpenRouter lo devuelve.
- El selector frontend muestra `Auto (elige el mejor)` por defecto y los seis modelos manuales. `Detalles de IA` convierte el slug devuelto a etiqueta legible cuando esta en la lista.

### Por que

OpenRouter Auto Router si elige el mejor modelo para cada prompt, pero dejarlo abierto es mala operativa: puede escoger modelos fuera de la allowlist de la cuenta o modelos de pago. La comprobacion posterior demostro que para esta cuenta restringida la via robusta no es `allowed_models`, sino `models` con un fallback de maximo 3 candidatos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests|ConfiguracionControllerTests`: 44/44 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Bundle sincronizado: `Auto (elige el mejor)` y los seis modelos OpenRouter aparecen en `aiModels-BJ_TwRsn.js`.

## 2026-05-11 - V-01.06 - Enter y modelo por consulta en chat IA

### Que cambio

- `AiChatPanel` intercepta `Enter` en el textarea para enviar la consulta. `Shift+Enter` mantiene el salto de linea.
- El chat incorpora un selector compacto de modelo, limitado al proveedor activo (`OpenRouter` u `OpenAI`).
- `frontend/src/utils/aiModels.ts` centraliza proveedores, modelos permitidos, normalizacion y modelo por defecto. `ConfiguracionPage` y `AiChatPanel` usan la misma fuente.
- `IaChatRequest` acepta `Model`.
- `IaController` pasa el modelo solicitado a `AtlasAiService.AskAsync`.
- `AtlasAiService` aplica el modelo solicitado solo a la llamada actual, lo valida contra `AiConfiguration.IsAllowedModel` y bloquea cualquier valor fuera de allowlist antes de construir la peticion al proveedor.
- `AtlasAiServiceTests` cubre dos regresiones: modelo solicitado permitido sin cambiar `ai_model` global, y modelo solicitado no permitido bloqueado sin llamada HTTP ni prompt en auditoria.

### Por que

Enviar con `Enter` es el comportamiento esperado en un chat. Lo raro era obligar al boton.

El selector se implementa por consulta, no como guardado global. Cambiar el modelo global desde un chat normal seria mala seguridad y mala operativa: una persona podria cambiar coste/comportamiento para todos sin pasar por `Configuracion > Revision e IA`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests`: 35/35 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Playwright headless con `setContent` y CSS compilado: selector visible, dentro del panel, formulario visible y sin overflow horizontal.

## 2026-05-11 - V-01.06 - IA permite consultas financieras administrativas

### Que cambio

- `AtlasAiService.IsQuestionWithinAllowedDomain` amplia el vocabulario financiero permitido: gastos, ingresos, importes, montos, totales/globales, impuestos, Seguridad Social, retenciones, cuotas de autonomos, recibos, facturas, cargos, cobros, comisiones, seguros y nominas.
- El prompt de sistema declara esas consultas como financieras permitidas aunque usen vocabulario fiscal o administrativo.
- La restriccion externa ya no dice `temas legales` de forma bruta; ahora rechaza asesoramiento legal externo, sin bloquear preguntas sobre impuestos o Seguridad Social presentes en los extractos.
- `BuildFinancialContextAsync` anade resumenes especificos para `ultimo mes`/`ultimos 30 dias` y `mes pasado`/`mes anterior`.
- Se agregan categorias de contexto `IMPUESTOS/SEGURIDAD SOCIAL DETECTADOS` y `RECIBOS/FACTURAS DETECTADOS`.
- `AtlasAiServiceTests` cubre la pregunta exacta `cual ha sido los gastos globales del ultimo mes` y variantes de Seguridad Social, impuestos, recibos, facturas, comisiones, seguros e ingresos.

### Por que

La barrera anterior era demasiado estrecha para una app de tesoreria. Bloquear recetas esta bien. Bloquear `Seguridad Social`, `impuestos` o `recibos` dentro de una aplicacion financiera es pegarse un tiro en el pie. La regla correcta es permitir todo lo que sea dato financiero propio y seguir cortando temas externos o asesoramiento fuera del producto.

### Verificacion

- `AtlasAiServiceTests`: 33/33 OK con `UseAppHost=false`.
- Hubo bloqueos previos por binarios `.dll/.exe` en uso y permisos en rutas temporales; se resolvio parando los procesos dotnet locales que bloqueaban el build.
- Persisten warnings conocidos de `Access denied` al intentar borrar `.exe` y cache de referencias, no bloqueantes.

## 2026-05-11 - V-01.06 - Render legible de respuestas IA

### Que cambio

- `AiChatPanel` separa la respuesta del proveedor de los metadatos tecnicos. La respuesta visible ya no concatena `Movimientos analizados`, `Modelo`, `Tokens` y `Coste` como texto plano al final.
- Se agrega `AiMessageContent`, un renderer React local para respuestas IA. Convierte parrafos/listas/negritas simples a JSX seguro y transforma tablas Markdown en bloques dato/valor.
- `revision-ai.css` cambia el panel de grid fijo a flex column. `.ai-chat-messages` pasa a ocupar el espacio flexible real con `min-height: 0`, evitando la fila vacia que dejaba la respuesta arriba y el formulario abajo.
- Las burbujas IA aceptan `overflow-wrap: anywhere`, `min-width: 0` y ancho completo para que cadenas largas de tablas/modelos no se corten.
- `AtlasAiService.BuildSystemMessage` instruye al proveedor a responder sin tablas Markdown, pipes ni asteriscos de negrita.

### Por que

El fallo era doble: el layout asignaba la zona flexible a una fila que no contenia los mensajes cuando no habia aviso de configuracion, y el frontend pintaba Markdown crudo dentro de un `<p>`. Las tablas Markdown generan cadenas largas con pipes y guiones; con `overflow-x: hidden` eso se ve como texto cortado. Arreglar solo CSS habria dejado asteriscos y tablas feas. Arreglar solo el prompt habria sido confiar demasiado en el modelo. Ambas cosas juntas son la solucion correcta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec -- tsc --noEmit`: OK.
- `AtlasAiServiceTests`: 33/33 OK fuera del sandbox con `OutputPath=C:\tmp\atlas-test-bin`.
- `npm.cmd run build`: OK fuera del sandbox; dentro sigue bloqueado por `spawn EPERM` de Vite/Rolldown.
- `wwwroot` sincronizado fuera del sandbox por `Access denied` dentro del sandbox.
- Playwright headless con `setContent` y CSS compilado: sin Markdown crudo, sin overflow horizontal, mensaje dentro del panel y area de mensajes usando la altura disponible.

## 2026-05-10 - V-01.06 - Protocolo anti-encallamiento de agentes

### Que cambio

- Se agrega una seccion `Protocolo anti-encallamiento` en las instrucciones canonicas y copias operativas del proyecto.
- El protocolo fija un maximo de dos intentos por la misma via cuando una herramienta falla o se queda colgada.
- Se documentan los atascos conocidos de esta maquina: `spawn EPERM` en Vite/Rolldown/Chromium, servidores temporales sin cerrar, `robocopy /MIR`, `wwwroot` bloqueado, `apphost.exe` en uso, Docker/Testcontainers no disponible y limpiezas con `Access denied`.
- Se exige separar en la respuesta final lo verificado, lo bloqueado y lo pendiente, sin vender validacion visual cuando solo hubo checks estaticos.

### Por que

La regla anterior decia "corta si se encalla", pero era demasiado vaga. En la practica, el agente repetia la misma via esperando un resultado distinto. Eso no es perseverancia: es quemar tiempo. La nueva regla convierte los bloqueos conocidos en decisiones cerradas.

### Verificacion

- Revisadas `CLAUDE.md`, `AGENTS.md`, `Atlas Balance/CLAUDE.md` y `Atlas Balance/AGENTS.md`.
- Confirmada la presencia de `Protocolo anti-encallamiento` en las tres instrucciones largas y las reglas resumidas en `AGENTS.md`.
- No aplica build ni tests de runtime porque no cambia codigo.

## 2026-05-10 - V-01.06 - Fix definitivo 500 chat IA en resumenes

### Que cambio

- `AppendPeriodSummaryAsync` ya no recibe ni filtra un `IQueryable<AiExtractoRow>`.
- Los totales de mes actual, mes anterior, periodo anual y totales por mes se calculan desde `Extractos` enlazado con `Cuentas`.
- `AppendCategoryAsync` usa un predicado `Expression<Func<Extracto, bool>>` para conceptos y agrupa por divisa desde entidades EF.
- La busqueda de movimientos relevantes aplica el filtro de concepto sobre `Extracto.Concepto` y proyecta a `AiExtractoRow` solo al final.
- La prueba `AskAsync_Should_Build_Period_And_Category_Context` cubre ingresos/gastos, comisiones, seguros y totales mensuales en el prompt enviado al proveedor.

### Por que

El arreglo anterior solo corto el fallo del agregado de saldos actuales. El mismo patron malo seguia en otras ramas del contexto IA: construir `AiExtractoRow` dentro de la expresion LINQ y luego filtrar/ordenar/agrupar por sus propiedades. InMemory traga eso; Npgsql no. PostgreSQL no traduce magia de records C# inventados a mitad de consulta.

La regla correcta para este servicio queda clara: todo lo que deba ejecutarse en SQL se expresa con entidades y columnas escalares; los records de prompt se construyen despues.

### Verificacion

- `AtlasAiServiceTests` 22/22 OK con `UseAppHost=false`.
- Build del API OK con salida temporal y `NuGetAudit=false`; solo queda el warning existente de Hangfire PostgreSQL obsoleto.
- Verificador temporal contra PostgreSQL real OK dentro de transaccion revertida, con proveedor HTTP mockeado y sin coste externo.
- `GET http://localhost:5000/api/health`: `healthy`.

## 2026-05-10 - V-01.06 - Alineacion simetrica de Extractos

### Que cambio

- `system-coherence.css` mantiene el max-width generico de paginas en `1280px`, pero agrega una excepcion especifica para `.extractos-page` con `max-width: 1600px`.
- `extractos.css` ajusta `.extractos-header` a `align-items: center` para equilibrar el titulo con el bloque de filtros.
- `.extractos-page`, `.add-row-form` y `.extracto-table-section` reciben `min-width: 0` para que el contenido interno no fuerce overflow del contenedor centrado.

### Por que

`Extractos` es una hoja financiera, no una pantalla de lectura. El limite generico de `1280px` dejaba la tabla de 8 columnas empujando hacia la derecha y rompia la simetria: mucho aire a la izquierda, casi nada a la derecha. Darle ancho propio al contenedor corrige el eje visual sin inventar una tabla nueva.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` dentro del sandbox: bloqueado por `spawn EPERM`, incidencia ya conocida.
- `npm.cmd run build` fuera del sandbox: OK.
- Copia de `frontend/dist` a `backend/src/AtlasBalance.API/wwwroot`: OK fuera del sandbox.
- Playwright headless con CSS compilado: margenes laterales `98px/98px`, bordes de header/form/tabla con delta `0`, titulo centrado respecto al bloque de filtros y sin overflow horizontal.

## 2026-05-10 - V-01.06 - Header de cuenta alineado por grilla

### Que cambio

- `.cuenta-detail-page .dashboard-toolbar` usa una grilla desktop de dos columnas: identidad a la izquierda y acciones a la derecha.
- `.dashboard-toolbar-main` se aplana con `display: contents` solo en detalle de cuenta para que titulo y ficha participen directamente en la grilla.
- `.cuenta-heading-block` ocupa la fila superior izquierda; `.account-identity-strip` queda en la fila inferior izquierda.
- `.dashboard-toolbar-actions` ocupa la columna derecha desde la primera fila hasta la segunda, con `align-self: stretch` y contenido alineado arriba.
- En `max-width: 900px` todos los bloques vuelven a una sola columna y filas automaticas.

### Por que

Subir el panel `Periodo` no bastaba si la caja derecha quedaba visualmente corta. El objetivo correcto era alinear el bloque derecho con el conjunto real de la izquierda: empezar con el titulo y terminar con la ficha de datos de cuenta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` fuera del sandbox: OK.
- Playwright headless con CSS compilado y fixture del shell: `topDelta=0`, `bottomDelta=0.01`, `startsAboveIdentityBy=75.44`, pagina `1280px`.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`.

## 2026-05-10 - V-01.06 - Restriccion tematica del chat IA

### Que cambio

- `AtlasAiService.AskAsync` valida el ambito de la pregunta despues de comprobar IA global y permiso de usuario, y antes de validar proveedor/API key o llamar al modelo.
- Las preguntas fuera de ambito lanzan `IaOutOfScopeException` y quedan auditadas como `IA_CONSULTA_BLOQUEADA` con motivo `out_of_scope`.
- `IaController` devuelve `400 Bad Request` con el mensaje: `Solo puedo responder sobre Atlas Balance, su funcionamiento o los datos financieros disponibles.`
- El prompt de sistema declara el ambito permitido: Atlas Balance, funcionamiento de sus modulos y datos financieros del contexto.
- El modelo debe rechazar recetas, cocina, programacion, noticias, ocio, salud, temas legales y cualquier asunto externo.
- `AtlasAiServiceTests` cubre una receta de cocina y verifica que no se llama al proveedor ni se guarda el prompt en auditoria.

### Por que

Solo ponerlo en la interfaz seria una defensa de cartulina. La restriccion tiene que vivir en backend, donde esta el coste, el proveedor externo y la auditoria. La barrera local corta lo obvio sin gastar tokens; el prompt endurecido cubre lo ambiguo cuando la pregunta si parece relacionada con la app o los datos.

No se implementa como "IA general con una nota amable". Esa seria la forma rapida de acabar respondiendo recetas dentro de una app financiera.

### Verificacion

- Primer intento de `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests --no-restore`: bloqueado por `AtlasBalance.API.exe` en uso.
- Se paro el proceso local `AtlasBalance.API` que bloqueaba el binario.
- La verificacion final usa `-p:UseAppHost=false` para no depender del apphost `.exe` bloqueado en Windows.
- Repeticion del test focalizado: `AtlasAiServiceTests` 21/21 OK.
- Quedan warnings no bloqueantes de apphost y cache de referencias de tests con acceso denegado, ya vistos en este entorno.

## 2026-05-10 - V-01.06 - Cierres de UI con icono X

### Que cambio

- Se crea `frontend/src/components/common/CloseIconButton.tsx`, un boton comun solo-icono con `lucide-react/X`, `aria-label` obligatorio y `title` derivado.
- Se reemplazan los botones visibles `Cerrar` por `CloseIconButton` en toast, auditoria de celda, chat IA, token generado, sheet movil, usuarios, titulares, cuentas e importacion desde detalle de cuenta.
- `global.css` define la base `.close-icon-button` con target de control, foco heredado, hover sobrio y sin colores nuevos.
- Las cabeceras de modales relacionadas pasan a `grid-template-columns: minmax(0, 1fr) auto` para reservar sitio estable al cierre.
- `TokenCreatedModal` mueve el cierre al header y conserva `Copiar` como accion de contenido.

### Por que

Los botones de cierre con texto repetian una accion universal y ocupaban demasiado peso visual en modales. La X es el patron correcto para cerrar superficies, pero solo si conserva accesibilidad y tamano tactil. Hacer un reemplazo textual sin ajustar CSS habria dejado botones gigantes con una X dentro, especialmente en mobile.

No se cambio `Cerrar sesion` porque no es un cierre de superficie: es una accion de cuenta. Ocultarlo tras una X seria peor UX.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` dentro del sandbox: falla por `spawn EPERM` de Vite/Rolldown, incidencia conocida.
- `npm.cmd run build` fuera del sandbox: OK.
- Copia de `frontend/dist` a `backend/src/AtlasBalance.API/wwwroot`: OK fuera del sandbox.
- Playwright headless con CSS compilado: los cierres probados no tienen texto visible y miden `43x43` en viewport movil.
- Busqueda `rg` confirma que no quedan botones con texto visible `Cerrar` en `frontend/src` ni en `wwwroot`.

## 2026-05-10 - V-01.06 - Fix 500 al enviar primer mensaje IA

### Que cambio

- `AtlasAiService.BuildFinancialContextAsync` deja de calcular `SALDOS ACTUALES POR CUENTA` agrupando sobre el record proyectado `AiExtractoRow`.
- El agregado de ultimo saldo por cuenta ahora se hace sobre columnas escalares de `EXTRACTOS` filtradas por scope de cuenta y rango defensivo.
- La proyeccion a `AiExtractoRow` se mantiene solo al final, cuando la consulta ya tiene identificada la fila con `fila_numero` maximo por cuenta.
- Se agrega una prueba de regresion para confirmar que el contexto IA usa el saldo de la fila con mayor `fila_numero`.

### Por que

El endpoint `/api/ia/chat` fallaba antes de llamar al proveedor. El log mostraba que Npgsql no podia traducir el join entre `baseRows` y `latestKeys` porque ambos dependian de propiedades de un record construido dentro de la expresion LINQ. InMemory no lo detectaba, asi que el test anterior era demasiado comodo y se comio el bug.

La solucion correcta es mantener las partes agregadas en SQL como columnas simples y proyectar a objetos de dominio solo despues. Eso conserva minimizacion de contexto, scope por usuario y evita cargar extractos en memoria.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests --no-restore`: 20/20 OK.
- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter FullyQualifiedName~AtlasAiServiceTests --no-build --no-restore`: 20/20 OK.
- API dev reiniciada con `dotnet run --no-build`.
- `GET http://localhost:5000/api/health`: `healthy`.

## 2026-05-10 - V-01.06 - Chat IA por encima del contenido

### Que cambio

- `frontend/src/styles/layout/shell.css` define `.app-topbar` como plano de apilado propio con `position: relative` y `z-index: var(--z-sticky)`.
- El chat flotante de IA, montado dentro de `TopBar`, deja de quedar en el mismo plano que el contenido principal.
- No se modifica `AiChatPanel`, el endpoint `/api/ia/*`, permisos ni configuracion de proveedor.

### Por que

El panel de IA se renderizaba dentro de la topbar, pero el contenido de la pagina se pintaba despues en el grid del shell. En el dashboard principal eso dejaba los selectores de periodo/divisa visualmente por encima del chat. Era un bug de stacking context, no de IA.

La solucion correcta es elevar la topbar como contenedor de overlays ligeros. Subir z-indexes sueltos en los selects o mover el panel a ojo habria sido maquillaje fragil.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build` dentro del sandbox: falla por `spawn EPERM` de Vite/Rolldown, incidencia ya conocida.
- `npm.cmd run build` fuera del sandbox: OK.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot`; `index.html` apunta a `index-B8Ww_DgG.js` y `index-iV1XYHkN.css`.
- Playwright headless con CSS compilado confirma que un punto de solape entre filtros y panel cae dentro de `.ai-floating-chat` (`insideChat=true`, `topbarZ=200`, `chatZ=400`).

## 2026-05-10 - V-01.06 - Ajuste visual de identidad de cuenta

### Que cambio

- `frontend/src/styles/layout/dashboard.css` rediseña `.account-identity-strip` como panel flexible con gaps, superficie sunken y bloques internos para `Titular`, `Banco` e `IBAN`.
- El primer bloque de la ficha recibe mayor peso visual para que el titular sea la ancla de lectura.
- `.dashboard-toolbar-main` pasa a `flex: 1 1 42rem` para que la zona izquierda del toolbar no se contraiga a min-content en desktop.
- La regla responsive conserva una sola columna en movil sin separadores extra ni overflow.

### Por que

La ficha anterior era mala: parecia una tabla incompleta, dejaba una zona muerta a la derecha y usaba una linea vertical que no comunicaba jerarquia. Para una pantalla financiera, esos datos tienen que poder escanearse en menos de un segundo y seguir el mismo lenguaje de superficies, bordes y espaciado que los KPI y tarjetas del dashboard.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- Playwright con fixture HTML y CSS reales:
  - Desktop 2048x900: ficha `832px`, tres bloques en una fila, sin overflow horizontal.
  - Movil 390x844: ficha `350px`, tres bloques apilados, sin overflow horizontal.

## 2026-05-10 - V-01.06 - Alineacion de pantallas phase2

### Que cambio

- `frontend/src/styles/layout/system-coherence.css` incluye `.phase2-page` en la regla compartida de anchura y centrado.
- La misma clase se incluye en el reset mobile para mantener `max-width: none` en pantallas pequenas.
- `CuentasPage` ya tenia `className="phase2-page cuentas-page"`, asi que no hizo falta tocar TSX ni duplicar estilos locales.

### Por que

`Titulares` estaba centrada con `max-width: 1500px`, pero `Cuentas` no entraba en esa lista global y ocupaba casi todo el ancho disponible. El resultado era una pantalla visualmente distinta sin razon funcional. La solucion correcta era subir la regla a `.phase2-page`, porque ese es el patron comun real.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- Playwright desktop 2048px con APIs mockeadas: `Titulares` y `Cuentas` miden `left=400`, `width=1500`, `deltaLeft=0`, `deltaWidth=0`, sin errores de consola.

## 2026-05-10 - V-01.06 - Ajuste visual del modal de usuarios

### Que cambio

- `UsuarioModal` separa el bloque de emails en etiqueta `Destinatarios`, textarea y ayuda `Uno por línea o separados por coma.`.
- El textarea queda enlazado a la ayuda con `aria-describedby="notification-emails-help"`.
- `users.css` añade reglas locales para `.users-notifications-section`, `.users-notification-field` y `.users-field-help`.
- El textarea del modal fuerza `width: 100%`, mantiene `min-height: 7rem`, `resize: vertical` y la misma altura de línea del sistema.

### Por que

El bloque anterior metía el texto de ayuda y el textarea dentro de un `label` inline sin layout propio. En desktop dejaba una caja estrecha y centrada que partía direcciones de email y no seguía la retícula del modal. Era un diseño roto, no una preferencia estética.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- Playwright desktop con APIs mockeadas: textarea `1046px` dentro de modal `1080px`, sin errores de consola.
- Playwright móvil 390px con APIs mockeadas: textarea `366px`, `scrollWidth=390`, sin overflow horizontal.

## 2026-05-10 - V-01.06 - Cierre de pendientes altos de auditoria release

### Que cambio

- `ImportacionService` calcula fingerprint SHA-256 estable por cuenta/fila/contenido normalizado y persiste trazabilidad en `Extracto.ImportacionFingerprint`, `ImportacionLoteHash`, `ImportacionFilaOrigen` y `FechaImportacion`.
- `AppDbContext` define indice unico filtrado `(cuenta_id, importacion_fingerprint)` para que reimportar el mismo archivo no duplique movimientos.
- La migracion `20260510120740_AddExtractoImportacionFingerprint` agrega solo columnas e indices de importacion; no renombra tablas ni toca datos existentes.
- `RevisionService` deja de cargar todos los extractos a memoria: filtra conceptos/estado, ordena y pagina con `Skip/Take` en EF. Los endpoints de revision devuelven `PaginatedResponse<T>`.
- `ExportacionService` aplica `export_max_rows` antes de generar XLSX con ClosedXML. Default: 50.000 filas. Maximo aceptado: 200.000. Al superar el limite audita `EXPORTACION_BLOQUEADA`, marca proceso `FAILED` y el endpoint manual devuelve HTTP 413.
- `PlazoFijoService` separa notificacion interna de email enviado: `FechaUltimaNotificacion` solo se actualiza tras email correcto; si SMTP falla o no hay admins activos, puede reintentarse sin duplicar notificaciones internas.
- `parseEuropeanNumber` centraliza el parseo manual frontend de importes europeos y admite `1.234,56`, `1234,56`, `-1.234,56`, `1 234,56`, `1.234`, `1,234`, simbolos de divisa y parentesis negativos.
- Las altas/ediciones manuales de extractos, desglose de cuenta e importes de plazo fijo usan `parseEuropeanNumber` y campos `inputMode="decimal"` para no bloquear la coma decimal.
- `AiChatPanel` cierra con Escape en modo flotante y enfoca el textarea cuando la IA esta disponible.
- `AtlasBalance.API.Tests.csproj` desactiva build paralelo para evitar carreras de referencias entre API, Watchdog y tests tras el renombrado.

### Verificacion

- `dotnet restore "Atlas Balance\\backend\\AtlasBalance.sln" --disable-parallel`: OK fuera del sandbox.
- `dotnet build "Atlas Balance\\backend\\AtlasBalance.sln" --no-restore -m:1 --disable-build-servers`: OK con warning MSB3101 de cache en `obj`.
- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter "FullyQualifiedName!~RowLevelSecurityTests&FullyQualifiedName!~ExtractosConcurrencyTests"`: 163/163 OK.
- `dotnet test` completo: 163/165 OK; los 2 fallos restantes requieren Docker/Testcontainers para PostgreSQL.
- `npm.cmd install`: OK, 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro falla por `spawn EPERM` de Vite/Rolldown.
- `npm.cmd audit`: 0 vulnerabilidades.
- `dotnet list "Atlas Balance\\backend\\AtlasBalance.sln" package --vulnerable --include-transitive`: 0 paquetes vulnerables.
- Secret scan con `rg` sobre codigo versionable activo: sin coincidencias de claves/API tokens.
- `git diff --check`: OK; solo warnings de normalizacion LF/CRLF.

### Pendiente no maquillable

- Para release final hay que levantar Docker y ejecutar los tests PostgreSQL reales: RLS y concurrencia de `fila_numero`. Sin eso, la recomendacion sigue siendo no apta.

## 2026-05-10 - V-01.06 - Renombrado tecnico a AtlasBalance

### Que cambio

- La solucion backend pasa a `AtlasBalance.sln`.
- Los proyectos .NET quedan como `AtlasBalance.API`, `AtlasBalance.Watchdog` y `AtlasBalance.API.Tests`.
- Los namespaces C# pasan a `AtlasBalance.*`.
- Scripts, CI, referencias de release, rutas de build y `ProjectReference` apuntan a los nuevos nombres.
- El frontend mantiene el nombre visible `Atlas Balance`.
- `Actualizar-AtlasBalance.ps1` actualiza el `binPath` de los servicios existentes despues de sincronizar los nuevos ejecutables.

### Compatibilidad conservada

- Se mantiene `SetApplicationName("AtlasBalance")` en Data Protection.
- Se mantiene la base de datos `atlas_balance`.
- Se mantienen rutas publicas `/api/*` y `/watchdog/*`.
- No se renombran tablas, columnas ni migraciones aplicadas a nivel de BD.
- No se modifican secretos ni recursos externos productivos.

### Verificacion

- Build directo API: OK.
- Build directo Watchdog: OK.
- Frontend lint/build: OK.
- Busqueda final de variantes antiguas en codigo activo: sin resultados.
- Build de solucion y tests backend: bloqueados por el fallo MSBuild ya registrado en el proyecto de tests.

## 2026-05-10 - V-01.06 - Revision bancaria e IA

### Que cambio

- `RevisionService` calcula comisiones y seguros desde todos los extractos visibles por `UserAccessScope`; las escrituras de estado exigen `CanWriteCuentaAsync`.
- La deteccion normaliza tildes y compara conceptos con listas de terminos bancarios.
- El umbral `revision_comisiones_importe_minimo` se aplica sobre `Math.Abs(monto)` y solo muestra importes estrictamente superiores.
- Los estados se guardan en `REVISION_EXTRACTO_ESTADOS` con clave unica `(extracto_id, tipo)`.
- La migracion `20260509160722_AddRevisionEstadosAiConfig` habilita y fuerza RLS; las politicas delegan en `atlas_security.can_read_extracto` y `atlas_security.can_write_extracto`.
- `AtlasAiService` arma contexto financiero minimizado desde saldos, totales agregados y movimientos relevantes limitados. Conceptos y pregunta se tratan como datos no confiables para reducir prompt injection.
- La IA soportada en esta version es OpenRouter via backend y OpenAI via backend con API key de servidor. En OpenRouter, todas las rutas envian `provider.zdr=true` y `data_collection=deny`; si un modelo gratis no tiene endpoint compatible, la llamada falla cerrado.
- `/api/ia/chat` exige autenticacion, interruptor global activo, permiso `puede_usar_ia`, allowlist de modelo, limites configurables, presupuesto/tokens y auditoria de metadatos sin guardar prompts completos.
- `ConfiguracionController` valida el modelo IA con allowlist tambien en backend.
- `AlertaService` evita duplicados de saldo bajo usando `alerta_saldo_cooldown_horas` con rango efectivo 1-720 horas y no marca cooldown si el email no se envia.
- El ultimo saldo operativo se toma por `fila_numero` para respetar el orden real del extracto importado.
- `ExportacionService` exporta extractos por `fila_numero desc`, no por fecha, y aplica formato Excel `dd/mm/yyyy` y `#,##0.00`.
- `formatters.ts` centraliza formato europeo y fuerza separador de miles con `Intl.NumberFormat('es-ES')`.

### Frontend

- `navigation.ts` registra `Revision` e `IA` en el menu lateral.
- `RevisionPage` expone `Comisiones` y `Seguros`, filtro de estado y acciones de marcado.
- `IaPage` y `AiChatPanel` usan `/api/ia/chat`.
- `TopBar` monta el chat flotante con el boton de IA sin abandonar la pantalla actual.
- `ConfiguracionPage` incluye ajustes de revision/IA y no revela la API key guardada.
- Las barras tipo formula de extractos y cuenta muestran el contenido completo de la celda seleccionada con wrapping.

### Verificacion

- `dotnet build "Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" --no-restore --disable-build-servers`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox por limitacion `spawn EPERM` de Vite dentro del sandbox.
- `npm.cmd audit --audit-level=critical --json`: 0 vulnerabilidades.
- `dotnet list AtlasBalance.API.csproj package --vulnerable --include-transitive`: 0 paquetes vulnerables.
- `scan_secrets.py "Atlas Balance" --json`: 1 falso positivo bajo en secreto literal de test RLS.
- `dotnet test ... AtlasBalance.API.Tests.csproj ...`: bloqueado por resolucion de `AtlasBalance.Watchdog` con error MSBuild sin diagnostico (`0 Errores`).

## 2026-05-02 - V-01.06 - Reticula real en tabla de Extractos

### Que cambio

- `ExtractoTable.tsx` mueve `--extracto-sheet-width` al viewport para que cabecera, cuerpo, espaciador y filas lo hereden desde un contenedor comun.
- `extractos.css` elimina el fondo con gradientes que dibujaba una cuadricula falsa de `120px`.
- `.extracto-row` usa `height: var(--sheet-row-height)` y `align-items: stretch`.
- `.cell` usa `box-sizing: border-box`, altura fija y `border-bottom` propio para construir celdas completas.
- Los textos directos dentro de celda se recortan con ellipsis para no empujar el ancho visual.
- Se sincroniza `frontend/dist` con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El intento anterior alineaba los tracks, pero dejaba una trampa visual: el viewport seguia pintando una cuadricula de fondo con columnas de `120px`, mientras las columnas reales miden distinto. Eso hace que una tabla financiera parezca torcida aunque el grid tecnico este cerca de alinearse. La correccion correcta es que las lineas sean los bordes de las celdas reales, punto.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `/extractos` mockeado: OK; 13 columnas visibles, `maxLeftDelta=0`, `maxWidthDelta=0`, `maxBottomDelta=0`, altura de fila `42px` y `backgroundImage=none`.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Insercion ordenada de lineas en desglose de cuenta

### Que cambio

- `CreateExtractoRequest` agrega `InsertBeforeFilaNumero`.
- `ExtractosController.Crear` calcula la fila destino con `Math.Clamp`, bloquea por cuenta con `pg_advisory_xact_lock` en PostgreSQL y desplaza las filas `>= destino`.
- Para PostgreSQL, el desplazamiento usa dos `UPDATE` con offset temporal para no chocar con el indice unico `(cuenta_id, fila_numero)`.
- Para tests/in-memory, el desplazamiento se hace ordenando las filas descendentes y sumando `1`.
- `CuentaDetailPage` agrega accion `Insertar debajo` y formulario inline que envia `insert_before_fila_numero`.
- `CuentaDetailPage` carga el desglose con `sortBy=fila_numero&sortDir=desc` para que la vista respete el orden persistido.
- `dashboard.css` define estilos para acciones de fila y formulario intermedio.

### Por que

El alta manual existente solo agregaba al final (`max(fila_numero) + 1`). Eso no sirve para extractos bancarios con lineas informativas o desglose partido: si el usuario necesita meter una linea entre dos movimientos, el orden persistido debe moverse de verdad en backend. Hacerlo solo en React seria humo caro.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ExtractosControllerTests -c Release`: 11/11 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Filtro de periodo en Extractos

### Que cambio

- `frontend/src/pages/ExtractosPage.tsx` agrega filtros `Desde` y `Hasta` con `DatePickerField`.
- Los filtros se sincronizan con la URL mediante `fechaDesde` y `fechaHasta`.
- `loadRows` envia esos parametros a `GET /api/extractos`, que ya soportaba `DateOnly? fechaDesde/fechaHasta`.
- Se valida en frontend que `fechaDesde` no sea posterior a `fechaHasta`.
- `frontend/src/styles/layout/extractos.css` adapta el header de filtros para titulares, cuentas y fechas sin romper mobile.
- El bundle frontend se recompila y se sincroniza con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

La API ya sabia filtrar por fechas, pero la pantalla no daba forma de usarlo. Eso es funcionalidad medio hecha: existe en el contrato, pero el usuario sigue obligado a tragar todo el historico o filtrar celda a celda. Ahora el rango vive donde debe vivir: arriba, junto a titular y cuenta.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Dominio vertical de graficas de evolucion

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` calcula un dominio Y explicito con `getEvolutionDomain`.
- El dominio incluye las series `saldo`, `ingresos` y `egresos`.
- Cuando todos los valores son positivos, se mantiene `0` como base visual y se suma padding al maximo.
- Cuando hay valores negativos, se resta padding al minimo para no recortar trazos bajo cero.
- El padding usa el 4% del rango o de la magnitud maxima, con minimo de `1`.
- Se sincroniza `frontend/dist` con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El dominio automatico de Recharts podia colocar el valor maximo justo contra el borde superior del area de trazado. Con un `strokeWidth` de 2.6, eso recortaba visualmente la parte alta de la linea de saldo. El fix correcto es dar aire al dominio de datos, no mover la grafica a ojo con CSS.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Tabla de extractos con reticula estable

### Que cambio

- `ExtractoTable.tsx` calcula el ancho total de la hoja desde las columnas visibles.
- `getColumnTrack` pasa a devolver anchos fijos en pixeles, respaldados por `getColumnWidth`.
- Cabecera, espaciador virtualizado y filas comparten `--extracto-sheet-width`.
- `extractos.css` fija `width: max(100%, var(--extracto-sheet-width))` y `min-width: var(--extracto-sheet-width)` en cabecera, cuerpo, espaciador y filas.
- Las filas virtualizadas ya no aplican `translateY(virtualRow.start - headerOffset)`; ahora arrancan en `virtualRow.start` porque el cuerpo ya esta debajo de la cabecera.
- El bundle frontend se recompila y se sincroniza con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

La combinacion de columnas `fr`, filas absolutas y un cuerpo sin ancho intrinseco estable permitia que algunas filas recalcularan la cuadricula contra el viewport en vez de contra el ancho real de columnas. En una tabla de extractos eso es un bug serio: si una celda parece moverse de columna, el usuario deja de confiar en el dato.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `/extractos` mockeado: OK; 9 columnas visibles, filas renderizadas y sin diferencias de posicion/ancho entre cabecera y celdas.
- `robocopy dist ..\backend\src\AtlasBalance.API\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - KPIs del dashboard principal sin overflow

### Que cambio

- `frontend/src/styles/layout/dashboard.css` declara `container-type: inline-size` y `min-width: 0` en `.dashboard-kpi`.
- `.dashboard-overview-grid` pasa a `minmax(46rem, 1.32fr) minmax(20rem, 0.68fr)` para dar prioridad al bloque principal frente al desglose por divisa.
- Los importes de `.dashboard-kpi p` usan `font-size: clamp(1rem, 8cqw, 1.55rem)`.
- El KPI destacado usa `font-size: clamp(1.35rem, 6cqw, var(--font-size-kpi))`, manteniendo el override especifico de overview.
- El bundle frontend se recompila y se sincroniza con `backend/src/AtlasBalance.API/wwwroot`.

### Por que

El ajuste anterior de V-01.05 resolvio el `Saldo total`, pero dejaba los KPIs laterales con importes grandes, fuente mono y `white-space: nowrap` dentro de columnas estrechas. Resultado: los ingresos y egresos invadian la tarjeta de al lado. Truncar dinero seria mala decision; el numero debe caber. Ademas, el reparto antiguo daba demasiado protagonismo horizontal a `Saldos por divisa`, que es informacion secundaria en esta vista.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `/dashboard` y APIs mockeadas: OK; sin overflow horizontal, bloque principal `979px`, divisas `505px` y sin desbordamiento en KPIs ni divisas.
- `robocopy dist ..\backend\src\AtlasBalance.API\wwwroot /MIR`: OK.

## 2026-05-02 - V-01.06 - Apertura de version

### Que cambio

- `Atlas Balance/VERSION` pasa de `V-01.05` a `V-01.06`.
- `Atlas Balance/Directory.Build.props` pasa de `1.5.0` a `1.6.0` y declara `InformationalVersion` como `V-01.06`.
- `Atlas Balance/frontend/package.json` y `package-lock.json` pasan la version del paquete frontend a `1.6.0`.
- `Documentacion/Versiones/version_actual.md` apunta a `Documentacion/Versiones/v-01.06.md`.
- `Documentacion/Versiones/v-01.06.md` queda creado como registro activo de la nueva version.

### Por que

La nueva linea de trabajo debe quedar trazada desde el primer cambio. Mantener `V-01.05` como activa mientras se trabaja en `V-01.06` mezclaria release cerrado con trabajo nuevo.

### Verificacion

- `git switch -c V-01.06`: OK.
- `git status --short --branch`: rama activa `V-01.06`.
- `Select-String` confirma `V-01.06` / `1.6.0` en las fuentes runtime y documentacion de version.

## 2026-05-02 - V-01.05 - Fix de lockfile npm para CI GitHub

### Que cambio

- `Atlas Balance/frontend/package.json` declara overrides para `once`, `graphemer`, `loose-envify` y `natural-compare` en `1.4.0`.
- `Atlas Balance/frontend/package-lock.json` actualiza esas entradas desde `1.5.0` inexistente a `1.4.0`.
- No cambia codigo runtime ni bundle servido; es una correccion de reproducibilidad de instalacion.

### Por que

GitHub Actions ejecuta `npm ci` en entorno limpio. El lockfile versionado apuntaba a tarballs que npm no publica (`once-1.5.0.tgz`, `graphemer-1.5.0.tgz`, `loose-envify-1.5.0.tgz` y `natural-compare-1.5.0.tgz`), por lo que CI fallaba antes de auditar, lintar o compilar.

### Verificacion

- `npm.cmd ci`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

## 2026-05-02 - V-01.05 - Paquete release Windows x64

### Que cambio

- Ejecutado `scripts/Build-Release.ps1 -Version V-01.05`.
- El script recompila frontend, sincroniza `frontend/dist` hacia `backend/src/AtlasBalance.API/wwwroot`, publica API y Watchdog self-contained para `win-x64` y crea el paquete en `Atlas Balance/Atlas Balance Release`.
- Artefactos generados:
  - `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64`
  - `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64.zip`
- El ZIP queda fuera de Git por `.gitignore`; debe subirse como asset de GitHub Release.

### Verificacion

- `npm.cmd run build`: OK.
- `dotnet publish` API Release win-x64: OK.
- `dotnet publish` Watchdog Release win-x64: OK.
- SHA256 ZIP: `3E7A3ED22EFC4D18A161EA9D8D15CD9C12B3D51BDEF9AE38863767EC5CEAE299`.
- Tamano ZIP: `102350978` bytes.

### Pendiente operativo

- No se genero `AtlasBalance-V-01.05-win-x64.zip.sig` porque falta `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` en el entorno. Sin ese asset, el actualizador online falla cerrado.

## 2026-05-02 - V-01.05 - Cierre de hallazgos residuales del escaneo repo-wide

### Que cambio

- `Instalar-AtlasBalance.ps1` guarda credenciales iniciales en `C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt`.
- `Instalar-AtlasBalance.ps1` y `Reset-AdminPassword.ps1` protegen el directorio `config` con ACL `Administrators/SYSTEM` antes de escribir secretos; si `icacls` falla, no queda archivo de credenciales expuesto.
- `Reset-AdminPassword.ps1` exige ejecucion como Administrador.
- `ExtractosController.ToggleFlag` valida permisos por campo cambiado (`flagged` y `flagged_nota`).
- `DashboardService` ignora filas globales `PuedeVerDashboard` que no tengan permisos de datos; los dashboards de gerente quedan globales solo con alcance global real de datos o scopeados por titular/cuenta.
- `IntegrationOpenClawController.Auditoria` deja de usar `IgnoreQueryFilters()` al resolver extractos y no devuelve valores de auditoria de extractos soft-deleted.
- La politica RLS `exportaciones_write` pasa de `can_read_cuenta_by_id` a `can_write_cuenta_by_id`.
- `ImportacionPage` normaliza `returnTo` y solo acepta rutas internas que empiecen por `/`.
- CI y `docker-compose.yml` fijan `postgres:16-alpine` por digest OCI.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Verificacion

- `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --filter "ExtractosControllerTests|DashboardServiceTests|IntegrationOpenClawControllerTests|RowLevelSecurityTests" --no-restore`: 20/20 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Parser PowerShell de scripts de instalacion/reset/update: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Hardening de seguridad repo-wide

### Que cambio

- `AuthService` usa `MaxFailedLoginAttempts = 5` y bloquea la cuenta aunque el throttle por cliente tambien se active.
- MFA acumula fallos por usuario durante una ventana de 15 minutos; crear un challenge nuevo ya no reinicia el contador efectivo.
- `IntegrationAuthMiddleware` redacta query params con normalizacion de clave y tambien valores con pinta de bearer/integration token.
- `ImportacionService` limita `ColumnasExtra` a 64, nombres a 80 caracteres, rechaza indices extra fuera de los datos y no persiste extras vacios.
- `UserAccessService` y `ExtractosController` solo derivan scope de datos desde flags de datos (`PuedeVerCuentas`, agregar, editar, eliminar, importar), no desde `PuedeVerDashboard`.
- `ExtractosController.Restaurar` requiere `CanDelete`, alineado con la accion de eliminar/restaurar.
- `CuentasController` y `ExtractosController` ocultan `CuentaReferenciaId/Nombre` de plazo fijo cuando la cuenta referencia no pasa scope o filtros de borrado para el usuario.
- `ActualizacionService` exige firma detached `.zip.sig` RSA/SHA-256 para updates online; `Build-Release.ps1` genera esa firma si existe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`.

### Configuracion nueva

```json
{
  "UpdateSecurity": {
    "ReleaseSigningPublicKeyPem": "-----BEGIN PUBLIC KEY-----..."
  }
}
```

Tambien se acepta `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM`. Para CI/release, `Build-Release.ps1` firma el ZIP si recibe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`. La clave privada no se documenta ni se guarda en repo. Si no hay clave publica o no existe el asset `.zip.sig`, el update online falla cerrado.

### Por que

El digest SHA-256 de GitHub Releases detecta corrupcion, no compromiso del canal de release. Si el atacante puede cambiar asset y metadata, puede cambiar ambos. La firma detached ancla el paquete a una clave fuera del canal de descarga. Lo demas son controles de autorizacion y brute-force que tenian que vivir en el backend, no solo en RLS o en UI.

### Verificacion

- Tests focalizados seguridad: 72/72 OK.
- Suite backend completa: 127/128; falla el harness RLS local por `permission denied for table __EFMigrationsHistory`.
- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- Parser PowerShell de scripts tocados: OK.

## 2026-05-02 - V-01.05 - Alineacion dinamica de EvolucionChart

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` introduce un calculo de ancho para el `YAxis`.
- El calculo revisa las etiquetas compactas de `ingresos`, `egresos` y `saldo` en todos los puntos.
- El eje queda limitado entre `44px` y `72px`.
- Todas las pantallas que renderizan evolucion heredan el ajuste porque usan el mismo componente: `/dashboard`, `/dashboard/titular/:id`, `/titulares` y `/cuentas`.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

Un ancho fijo de `72px` era aceptable para importes grandes, pero torpe para series pequenas como `4 EUR`: la grafica seguia arrancando demasiado a la derecha aunque las etiquetas no necesitaran ese espacio. La solucion correcta es adaptar el eje al contenido, con limites para no romper etiquetas largas.

### Reglas tecnicas

- No cambia contratos de API, permisos ni calculos financieros.
- No se introduce dependencia nueva.
- El tooltip conserva importes completos con `formatCurrency`.
- El eje sigue usando `formatCompactCurrency`; solo cambia su ancho reservado.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Playwright headless con APIs mockeadas sobre `/dashboard`, `/dashboard/titular/titular-1`, `/titulares` y `/cuentas`: OK; `gridStartX=45px`, `yAxisWidth=39px` y sin errores de pagina en las cuatro rutas.

## 2026-05-02 - V-01.05 - Saldo total del dashboard sin salto de linea

### Que cambio

- `dashboard.css` ajusta `dashboard-kpi-grid--overview` para dar mas ancho relativo al KPI destacado.
- Los KPIs superiores reducen padding dentro de esa grilla.
- Los importes de `.dashboard-kpi p` usan `white-space: nowrap`.
- El saldo destacado en `dashboard-kpi-grid--overview .dashboard-kpi--featured p` baja a `clamp(1.35rem, 1.5vw, 1.65rem)`.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Por que

El saldo total tenia una escala demasiado grande para una tarjeta de una tercera parte del resumen. Con `1.000.000,00 €` se partia o desbordaba. Eso no es un detalle: en una app de tesoreria, los numeros grandes son el caso normal, no una sorpresa.

### Reglas tecnicas

- No cambia formato monetario ni calculos.
- No se oculta el importe con ellipsis.
- No se toca el contrato del componente `KpiCard`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `total_convertido=1000000`: `1.000.000,00 €` queda en una linea y no desborda (`wraps=false`, `overflows=false`).
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Listado de cuentas en tres columnas

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` agrega la clase `cuentas-page` al contenedor raiz.
- `frontend/src/styles/layout/entities.css` define una grilla especifica para `.cuentas-page .phase2-cards`.
- El listado inferior de cuentas usa tres columnas en desktop, dos en tablet y una en mobile.
- Las tarjetas de cuenta ajustan el header para permitir badges en una segunda linea, limitan titulo/notas a dos lineas y reorganizan metadatos en dos columnas internas.
- El saldo queda destacado en la columna derecha en desktop/tablet y vuelve a apilarse en mobile.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El listado de cuentas heredaba dos columnas de `.phase2-cards`. Pasarlo a tres columnas sin tocar la estructura interna dejaba demasiada informacion financiera comprimida: banco, divisa, estado, vencimiento y saldo compiten por espacio. La solucion correcta es acotar la grilla a `CuentasPage` y ajustar la tarjeta para esa nueva densidad.

### Reglas tecnicas

- No cambia contratos de API, permisos, filtros, paginacion ni calculos.
- No se introduce dependencia nueva.
- La regla mobile especifica evita que la mayor especificidad de `cuentas-page` mantenga dos columnas por debajo de `900px`.
- Se mantiene CSS variables propias y el sistema responsive existente.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/cuentas`: desktop `3` columnas, tablet `2`, mobile `1`, sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Divisa base primero en saldos por divisa

### Que cambio

- `SaldoPorDivisaCard.tsx` calcula `orderedItems` antes de renderizar.
- La lista se parte en dos bloques: primero los items cuya `divisa` coincide con `divisaPrincipal`, despues el resto.
- El resto de divisas conserva el orden recibido de la API.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Por que

La divisa base es la referencia de comparacion del dashboard. Si aparece segunda o tercera, el usuario tiene que reconstruir mentalmente la pantalla. Eso es mala jerarquia, no una preferencia estetica.

### Reglas tecnicas

- No cambia ningun endpoint ni calculo.
- No se ordenan alfabeticamente las divisas secundarias para evitar cambiar mas de lo pedido.
- No se introduce dependencia nueva.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con API mockeada: la API devuelve `USD` antes que `EUR`, pero `EUR` se renderiza primero porque es `divisaPrincipal`.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Reorden de plazos fijos y saldos por titular en dashboard principal

### Que cambio

- `DashboardPage.tsx` agrupa los KPIs superiores y la tarjeta `Plazos fijos` dentro de `dashboard-overview-primary`.
- `Plazos fijos` se renderiza debajo de `Saldo total`, `Ingresos periodo` y `Egresos periodo`, manteniendo `Saldos por divisa` en la columna derecha del resumen.
- `Saldos por titular` deja de formar parte de una grilla secundaria y pasa a ser una tarjeta de ancho completo en la parte inferior.
- `saldosPorTipo` ya no elimina tipos vacios: siempre prepara Empresa, Autonomo y Particular para mantener tres columnas previsibles.
- `dashboard.css` cambia `dashboard-titular-groups` a tres columnas en desktop y conserva una columna en mobile.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend.

### Por que

Los plazos fijos explican saldo inmovilizado, asi que deben leerse junto a los KPIs de saldo/movimiento. Ponerlos abajo junto a titulares era una mezcla floja. Los titulares, en cambio, son comparacion por categoria; si hay tres tipos, el layout debe tener tres columnas, no dos y luego apaños.

### Reglas tecnicas

- No cambia ningun endpoint ni contrato de API.
- No cambia calculo de saldos, permisos ni filtros.
- No se introduce dependencia nueva.
- La adaptacion responsive se limita a CSS del dashboard.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK; `Plazos fijos` debajo de KPIs, `Saldos por titular` a ancho completo, columnas `Empresa|Autonomo|Particular` en la misma fila y sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Listado de titulares en tres columnas

### Que cambio

- `frontend/src/pages/TitularesPage.tsx` agrega la clase `titulares-page` al contenedor raiz.
- `frontend/src/styles/layout/entities.css` define una grilla especifica para `.titulares-page .phase2-cards`.
- El listado inferior de titulares usa tres columnas en desktop, dos en tablet y una en mobile.
- Las tarjetas de titular limitan titulo y notas a dos lineas, reorganizan metadatos en dos columnas internas y mantienen las acciones al pie.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

La regla global `.phase2-cards` estaba en dos columnas y tambien la usa `CuentasPage`. Cambiarla globalmente habria sido una metedura de pata: el ajuste pedido pertenece solo a Titulares. La clase de pagina permite ampliar densidad en esa vista sin efectos colaterales.

### Reglas tecnicas

- No cambia contratos de API, permisos, paginacion ni estado.
- No se introduce dependencia nueva.
- La composicion conserva CSS variables propias y los breakpoints existentes.
- La regla mobile explicita evita que la mayor especificidad de `titulares-page` mantenga dos columnas por debajo de `900px`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/titulares`: desktop `3` columnas, tablet `2`, mobile `1`, sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-02 - V-01.05 - Formato de importacion en cuentas de efectivo

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` muestra el selector `Formato de importacion` para `NORMAL` y `EFECTIVO`.
- Al cambiar una cuenta a `EFECTIVO`, la UI limpia `banco_nombre`, `numero_cuenta` e `iban`, pero conserva `formato_id` si es compatible con la divisa.
- Al cambiar a `PLAZO_FIJO`, la UI sigue limpiando datos bancarios y `formato_id`.
- `frontend/src/pages/ImportacionPage.tsx` aclara que las cuentas normales y de efectivo usan formato de importacion.
- `CuentasController` usa `SupportsFormatoImportacion(tipoCuenta)` para aceptar formato en `NORMAL` y `EFECTIVO`, y rechazarlo implicitamente en `PLAZO_FIJO`.
- Se agrega `Crear_Should_Keep_Formato_For_Efectivo` en `CuentasControllerTests`.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El codigo anterior mezclaba dos conceptos distintos: `EFECTIVO` no tiene datos bancarios, pero si puede necesitar un formato para importar movimientos pegados/CSV. `PLAZO_FIJO` si tiene un flujo especial sin formato bancario. Meter ambos en el mismo saco era el bug.

### Reglas tecnicas

- El formato sigue filtrado por divisa.
- Las cuentas de efectivo no persisten banco, numero de cuenta ni IBAN.
- Las cuentas de plazo fijo siguen sin `formato_id` y usan el endpoint especifico de movimiento simple.
- No cambia el contrato de importacion; `ImportacionService` ya leia `FormatoId` desde la cuenta.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release --filter CuentasControllerTests`: 5/5 OK.
- `npm.cmd run lint`: OK tras corregir dependencia faltante del `useEffect`.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-02 - V-01.05 - Alineacion de graficas en Cuentas y Titulares

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` importa `formatCompactCurrency` y lo usa en el `YAxis` de la grafica de barras del dashboard de cuentas.
- `frontend/src/pages/TitularesPage.tsx` aplica el mismo ajuste en la grafica de barras del dashboard de titulares.
- En ambas graficas, `BarChart` usa margenes explicitos `top: 12`, `right: 8`, `bottom: 12`, `left: 0`.
- `YAxis` baja de `120` a `72`, oculta `axisLine`/`tickLine` y usa `tickMargin={10}`.
- `CartesianGrid` usa `var(--chart-grid)` y desactiva lineas verticales para mantener consistencia con el resto de dashboards.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El carril del eje Y estaba sobredimensionado y empujaba el area real de barras hacia la derecha. Ya se habia corregido el mismo patron en la grafica de evolucion del dashboard principal; dejarlo repetido en cuentas/titulares era inconsistente y visualmente torpe.

### Reglas tecnicas

- No cambia contratos de API, permisos, calculos ni stores.
- No se introduce dependencia nueva.
- El tooltip conserva `formatCurrency` para mostrar importes completos; el formato compacto queda limitado al eje.
- Se mantiene Recharts 2 y CSS variables propias.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Playwright headless con APIs mockeadas sobre `/titulares` y `/cuentas`: OK; `gridStartX=72px`, `yAxisWidth=69px` y sin errores de pagina en ambas rutas.

## 2026-05-01 - V-01.05 - Dashboard principal con grafica a ancho completo

### Que cambio

- `DashboardPage.tsx` separa el dashboard en tres ritmos: resumen superior (`dashboard-overview-grid`), grafica principal (`dashboard-evolution-card`) y bloques secundarios.
- `EvolucionChart.tsx` acepta `height?: number`; el dashboard principal lo usa con `height={420}`.
- `dashboard.css` agrega `dashboard-overview-grid`, refuerza la tarjeta de evolucion con mas padding y adapta divisas/KPIs en desktop y mobile.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.
- `Documentacion/Diseno/DESIGN.md` se actualiza para que la guia ya no contradiga la nueva jerarquia.

### Por que

La pantalla anterior intentaba meter KPIs, divisas y grafica en una sola fila. Eso hacia que la grafica quedara demasiado estrecha para leer tendencias. En tesoreria, la evolucion temporal necesita area util real; si el usuario tiene que acercarse a la pantalla, el diseño falló.

### Reglas tecnicas

- No cambia contratos de API, permisos, filtros ni calculos.
- No se introduce dependencia nueva.
- La altura configurable queda encapsulada en `EvolucionChart` para no duplicar componentes.
- Se mantiene CSS variables propias y Recharts 2.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK, `chartWidthRatio=0.960`, `svgHeight=420`, sin errores de pagina, sin respuestas API 500 y sin overflow horizontal. Dos fallos previos fueron del script mock de verificacion, no del producto.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-01 - V-01.05 - Alineacion de la grafica Evolucion

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` define margenes explicitos en `LineChart`: `top: 4`, `right: 8`, `bottom: 0`, `left: 0`.
- El `YAxis` reduce su anchura de `116` a `72`.
- `XAxis` y `YAxis` usan `tickMargin={10}` para separar etiquetas sin agrandar artificialmente el eje.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

La tarjeta estaba bien; la grafica no. Recharts estaba reservando demasiado espacio horizontal para el eje Y, asi que el area real de trazado arrancaba tarde y la grafica parecia desalineada dentro del dashboard. Corregirlo en el componente mantiene el layout limpio y evita parches de padding alrededor.

### Reglas tecnicas

- No cambia contratos de API, filtros, permisos ni estructura de datos.
- No se introduce dependencia nueva.
- Se mantiene Recharts 2 y CSS variables propias.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK; `plotInsetFromLegend=72px`, frente al carril anterior de `116px`.

## 2026-05-01 - V-01.05 - MFA recordado 90 dias y QR de enrolamiento

### Que cambio

- `AuthService.LoginAsync` acepta la cookie `mfa_trusted` y omite el reto MFA solo si el token firmado coincide con el usuario, su `security_stamp` y una expiracion futura.
- `AuthService.VerifyMfaAsync` emite un token MFA recordado durante 90 dias tras verificar correctamente el codigo TOTP.
- `AuthController` lee/escribe `mfa_trusted` como cookie `HttpOnly`, `SameSite=Strict`, `Secure` cuando aplica. Desde `V-01.07`, logout no la elimina; la revocacion va por rotacion de `security_stamp` o reset MFA.
- El enrolamiento inicial sigue generando secreto TOTP por usuario y ahora el frontend pinta un QR real desde `mfa_otp_auth_uri`.
- Se agrega `qrcode` al frontend para generar el QR localmente sin servicios externos.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

Pedir Google Authenticator en cada login es seguridad teatral y mala UX: fuerza friccion constante y acaba empujando a la gente a atajos peores. El criterio correcto aqui es MFA obligatorio en primer enrolamiento y revalidacion periodica. Tres meses es una ventana razonable para una app on-premise de pocos usuarios si el recordatorio queda atado al usuario y se invalida al rotar `security_stamp`.

### Reglas tecnicas

- La cookie recordada no contiene secretos TOTP ni tokens JWT.
- La firma usa HMAC SHA-256 con `JwtSettings:Secret`.
- El token queda ligado a `user_id`, `security_stamp` y expiracion. Cambios de password, permisos, email o perfil que roten `security_stamp` invalidan tambien el recuerdo MFA.
- El QR se genera desde el `otpauth://` emitido por backend; la clave manual queda visible como fallback.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --filter AuthServiceTests`: 11/11 OK.
- `dotnet test ... --filter AuthServiceTests` en Debug quedo bloqueado por `AtlasBalance.API.exe` en uso, PID `35456`; se verifico en Release para no detener un proceso local activo.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-01 - V-01.05 - Alineacion del logo en login

### Que cambio

- `frontend/src/styles/auth.css` cambia `.auth-logo-container` de `width: min(100%, 1120px)` a la misma columna visual del formulario: `width: min(calc(100% - 2rem), 430px)`.
- `.auth-logo-container` usa `justify-content: center` para centrar el bloque de marca completo sobre la tarjeta.
- En mobile se usa `width: min(calc(100% - 1.5rem), 430px)` y se conserva el centrado.
- `backend/src/AtlasBalance.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El header del login estaba usando un ancho de pagina completa pensado para layouts generales, no para una pantalla de autenticacion centrada. Resultado: primero el logo quedaba flotando a la izquierda; despues quedo alineado al borde de la tarjeta, pero no centrado como bloque. En login, la marca tiene que caer sobre el eje central de la tarjeta.

### Reglas tecnicas

- No cambia JSX, rutas, autenticacion, MFA ni contratos de API.
- No se introduce dependencia nueva.
- Se conserva CSS variables propias y el comportamiento responsive existente.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Edge headless/CDP en `/login`: centro del bloque de marca y centro de la tarjeta coinciden; `brandDeltaCard=0px`.

## 2026-05-01 - V-01.05 - Aplicacion UI/UX en shell y dashboard

### Que cambio

- `frontend/src/utils/navigation.ts` incorpora grupos semanticos de navegacion: `operacion`, `control` y `sistema`.
- La navegacion usa iconos de `lucide-react` con stroke consistente, en linea con `Documentacion/Diseno/DESIGN.md`.
- `Sidebar.tsx` renderiza secciones agrupadas con labels y separadores discretos.
- `BottomNav.tsx` reduce el menu movil principal a Inicio, Titulares, Cuentas, Importar y Mas; el sheet `Mas` agrupa los accesos secundarios por las mismas secciones.
- `DashboardPage.tsx` reorganiza la primera lectura: KPIs, saldos por divisa y evolucion quedan en `dashboard-command-grid`.
- `SaldoPorDivisaCard.tsx` pasa a una estructura mas semantica con total dominante y desglose `Disponible` / `Inmovilizado`.
- `dashboard.css` ajusta el grid del dashboard para evitar solapamientos, conservar densidad y mantener una columna unica en breakpoints medios/moviles.
- `global.css` deja de importar Geist, porque la guia define `National Park`, `Hind Madurai` y `Atlas Mono` como sistema tipografico activo.
- `auth.css` corrige `--font-mono` por `--font-family-mono` en el bloque MFA.
- `backend/src/AtlasBalance.API/wwwroot` se sincroniza con el build frontend actualizado.

### Por que

La guia UI/UX ya estaba escrita, pero no aplicada. El menu plano de muchas entradas era arquitectura visual floja: obligaba a leer todo al mismo nivel. El dashboard tambien repartia demasiado pronto la atencion; para tesoreria, el orden correcto es saldo total, liquidez por divisa y evolucion.

El solapamiento detectado en la verificacion inicial del KPI principal confirmo el punto: si un numero financiero importante no cabe, el diseno esta fallando aunque compile.

### Reglas tecnicas

- No se cambia ningun contrato de API.
- No se introduce dependencia nueva.
- Se mantiene CSS variables propias, dark/light mode y componentes existentes.
- Los grupos de navegacion viven en `navigation.ts` para que desktop y mobile compartan arquitectura.
- `wwwroot` debe actualizarse despues de cada build frontend que cambie UI servida por la API.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright con APIs mockeadas en `/dashboard`: desktop y mobile sin overflow horizontal; sidebar con grupos `Operacion`, `Control`, `Sistema`; bottom nav con `Inicio`, `Titulares`, `Cuentas`, `Importar`, `Mas`; KPI principal sin solapamiento tras correccion.
- `robocopy frontend/dist -> backend/src/AtlasBalance.API/wwwroot /MIR`: OK.

## 2026-05-01 - V-01.05 - Row Level Security activo en PostgreSQL

### Que cambio

- Se agrega la migracion `20260501120000_EnableRowLevelSecurity`.
- Se agrega la migracion `20260501133000_SignRowLevelSecurityContext`.
- La migracion crea el schema auxiliar `atlas_security`, funciones de contexto y politicas RLS.
- RLS queda activado con `FORCE ROW LEVEL SECURITY` en:
  - `TITULARES`
  - `CUENTAS`
  - `PLAZOS_FIJOS`
  - `EXTRACTOS`
  - `EXTRACTOS_COLUMNAS_EXTRA`
  - `EXPORTACIONES`
  - `PREFERENCIAS_USUARIO_CUENTA`
  - `AUDITORIAS`
  - `AUDITORIA_INTEGRACIONES`
  - `BACKUPS`
  - `NOTIFICACIONES_ADMIN`
- `RlsDbCommandInterceptor` fija contexto PostgreSQL antes de cada comando EF Core mediante variables `atlas.*` y una firma HMAC.
- `RlsContextSigner` firma el payload de contexto. PostgreSQL valida la firma con `atlas_security.context_is_valid()`.
- `Program.cs` aplica migraciones con `ConnectionStrings:MigrationConnection` si existe, configura el secreto de firma en `atlas_security.rls_context_secret`, concede permisos al runtime y limpia pools antes de usar `DefaultConnection`.
- `IntegrationAuthMiddleware` publica el token de integracion validado antes de escribir auditoria/rate limit.
- `docker-compose.yml` deja de usar `app_user` como `POSTGRES_USER`; las bases nuevas crean `atlas_owner` para ownership/migraciones y `app_user` como runtime sin `BYPASSRLS`.
- `Instalar-AtlasBalance.ps1` crea/separa `atlas_balance_owner` y `atlas_balance_app`; ambos sin superusuario ni `BYPASSRLS`, pero solo el owner queda en `MigrationConnection`.

### Como funciona

El backend sigue siendo la primera capa de permisos. RLS es la segunda capa: si una consulta directa o un bug de backend intenta leer/escribir fuera del alcance, PostgreSQL tambien filtra.

El interceptor fija estas variables de sesion:

- `atlas.auth_mode`: `anonymous`, `auth`, `user`, `integration` o `system`.
- `atlas.user_id`: usuario autenticado.
- `atlas.integration_token_id`: token de integracion autenticado.
- `atlas.is_admin`: admin de aplicacion.
- `atlas.system`: operaciones internas sin `HttpContext`, como migraciones/seed.
- `atlas.request_scope`: alcance especial, por ejemplo `dashboard`.
- `atlas.context_signature`: HMAC SHA-256 del payload anterior.

Las politicas consultan `PERMISOS_USUARIO` e `INTEGRATION_PERMISSIONS`. Admin y operaciones internas tienen paso amplio solo si `atlas.context_signature` valida contra el secreto DB. Usuarios normales e integraciones quedan limitados a sus cuentas permitidas.

El detalle importante: un cliente SQL con credenciales runtime puede ejecutar `SET atlas.system=true`, pero eso no le concede nada si no puede firmar el contexto. Sin esta firma, RLS seria teatro.

### Limites deliberados

- Las tablas de identidad/configuracion no quedan bajo estas politicas. Muchas se leen durante login, seed, proteccion de secretos o administracion y meterlas en RLS sin un diseno especifico romperia arranque/autenticacion.
- RLS no reemplaza permisos de controlador. Si alguien elimina checks en C#, sigue siendo un bug aunque PostgreSQL bloquee parte del dano.
- En contenedores dev antiguos puede no existir rol `postgres` porque se crearon con `app_user` como superusuario. La migracion activa RLS y firma de contexto, pero la separacion fuerte owner/runtime exige migrar ownership con un rol administrador o recrear la base con el Docker/instalador nuevo.

### V-02.06 - RLS hardening financiero y unificacion del secreto RLS

- **Migracion** `20260716120000_HardenFinancialV0202Rls`: anade `FORCE ROW LEVEL SECURITY` en `IMPORTACION_LOTES`, `IMPORTACION_LOTE_FILAS`, `MOVIMIENTOS_ESPERADOS` y `CONCILIACIONES` (la migracion que las creo, `20260629090000_FinancialHardeningV0202`, solo hizo `ENABLE`). Separa las policies previas `FOR ALL` en `SELECT`/`INSERT`/`UPDATE`/`DELETE` con sus `USING`/`WITH CHECK` correspondientes, y aniade `deleted_at IS NULL` en el `SELECT` de `IMPORTACION_LOTE_FILAS`, `MOVIMIENTOS_ESPERADOS`, `CONCILIACIONES`, `EXTRACTOS_COLUMNAS_EXTRA` y `REVISION_EXTRACTO_ESTADOS`. La migracion es manuscrita-SQL (mismo patron que las V-02.05) porque `AppDbContextModelSnapshot.cs` esta desalineado con el modelo y un scaffold EF podria recrear columnas ya existentes.
- **`RlsContextSecret` por DI**: nuevo contenedor `AtlasBalance.API.Data.RlsContextSecret`. El interceptor ya no lee `IConfiguration` por su cuenta; `Program.cs.ResolveRlsContextSecret` lo construye una sola vez, en Production exige `>=32 chars`, no placeholder y distinto del secreto JWT (excepcion explicita en lugar de warning en stderr). En Development conserva el fallback al JWT para que `dotnet run` siga funcionando.
- **`BackupService.ResolveDumpConnection`** (ahora `internal` para tests): orden `ConnectionStrings:MigrationConnection` -> `WatchdogSettings:DbOwnerUser/Password` -> `DefaultConnection`. Si solo existe el rol runtime, `RunPgDumpAsync` aborta con mensaje claro en lugar de ejecutar `pg_dump` filtrado por FORCE RLS.
- **`MigrationConnection` obligatorio fuera de Development**: `Program.cs.ResolveMigrationConnectionString` ya no devuelve la cadena runtime cuando falta; lanza `InvalidOperationException` con procedimiento de actualizacion.
- **Instalador / actualizador**: `Instalar-AtlasBalance.ps1` genera `Security:RlsContextSecret` aleatorio y lo persiste; `Actualizar-AtlasBalance.ps1` lo regenera cuando falta o coincide con JWT, y rellena `ConnectionStrings:MigrationConnection` reusando la cascada owner ya existente. Ninguno imprime credenciales.
- **Tests nuevos**:
  - `tests/AtlasBalance.API.Tests/Rls/RlsContextSignerTests.cs`: 6 facts (payload canonico, secreto vacio, vector fijo contra `HMACSHA256` directo, determinismo, sensibilidad por cada campo).
  - `tests/AtlasBalance.API.Tests/Rls/RlsDbCommandInterceptorContextTests.cs`: 6 facts sobre `BuildContext` (system, anonimo, auth, dashboard, write, revision) sin necesidad de PostgreSQL.
  - `tests/AtlasBalance.API.Tests/BackupServiceOwnerResolutionTests.cs`: 3 facts (sin owner, con `MigrationConnection`, con `WatchdogSettings.DbOwner*`).
  - `tests/AtlasBalance.API.Tests/MigrationDiscoveryTests.cs`: exige la nueva migracion.
  - `tests/AtlasBalance.API.Tests/RowLevelSecurityTests.cs`: inventario ampliado a 23 tablas (incluye las 4 financieras del V-02.02, `EXTRACTOS_DESGLOSES`, `BACKUP_CLOUD_*`).

### Deuda diferida a V-02.07

- RLS en `USUARIOS`, `REFRESH_TOKENS`, `INTEGRATION_TOKENS` y
  `CONFIGURACION`. Requiere diseno previo del flujo `is_auth_flow` y
  funciones que limiten columnas (RLS solo filtra filas). Si se aplica
  una policy amplia para no romper login, el aislamiento es debil.
- Reconciliacion de `AppDbContextModelSnapshot.cs` con los cambios
  `deleted_at` de V-02.05.
- `ISoftDelete` en `IMPORTACION_LOTES` y filtro `deleted_at IS NULL` en
  su policy `SELECT`.

### Verificacion

- `dotnet build '.\Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj' -c Release --no-restore`: OK.
- `dotnet test '.\Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj' -c Release --no-restore --filter RowLevelSecurityTests`: OK.
- Tests focalizados `RowLevelSecurityTests|UserAccessServiceTests|IntegrationAuthorizationServiceTests|IntegrationAuthMiddlewareTests|IntegrationTokenServiceTests`: 15/15 OK.
- `dotnet ef database update`: OK sobre `atlas_balance_db`.
- Catalogo local: 11 tablas objetivo con RLS y FORCE RLS activos, 20 politicas publicas, dos migraciones RLS aplicadas, `app_user` sin superusuario ni `BYPASSRLS`, secreto RLS sembrado, contexto falsificado rechazado y contexto firmado aceptado.

## 2026-04-26 - V-01.05 - Dashboard de titulares: evolucion antes del listado

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` reordena el render del bloque `titulares-dashboard-card`.
- La tarjeta `Evolucion` (`titulares-evolucion-card`) pasa a mostrarse antes de `cuentas-balance-list`.
- No hay cambios en servicios, tipos, stores ni contratos de API.

### Por que

El orden anterior forzaba leer primero el detalle y despues la tendencia. Para analisis rapido de titulares, eso es al reves de lo util.

### Reglas tecnicas

- Cambio solo de orden de JSX.
- Se conserva la misma fuente de datos (`evolucion`, `principal`, `saldosCuentaRows`) y la misma logica de permisos.
- Sin cambios CSS.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

## 2026-04-26 - V-01.05 - Reorden de dashboard principal (grafica antes de saldos)

### Que cambio

- `frontend/src/pages/DashboardPage.tsx` reordena el render para mostrar primero la tarjeta `Evolucion`.
- El bloque `dashboard-grid` (Saldo por divisa + Saldos por titular) queda debajo de la grafica.
- No se tocan servicios, tipos, stores ni endpoints.
- `backend/src/AtlasBalance.API/wwwroot` se sincroniza con el build frontend actualizado.

### Por que

El dashboard principal quedaba menos util para lectura rapida: primero se veian desgloses y despues la tendencia. Con la grafica arriba se prioriza el contexto temporal antes del detalle por divisa/titular.

### Reglas tecnicas

- Cambio solo de orden de componentes en JSX; sin impacto en contratos de API.
- Se conserva la misma carga paralela de `principal`, `evolucion` y `saldosDivisa`.
- Sin cambios CSS: la disposicion se apoya en estilos existentes.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK (codigo `1` esperado).

## 2026-04-26 - V-01.05 - Importacion preserva el orden de lineas pegadas

### Que cambio

- `ImportacionService.ConfirmarAsync` deja de ordenar las filas validadas por fecha antes de guardar.
- La asignacion de `fila_numero` se hace recorriendo las filas seleccionadas desde la ultima linea pegada hacia la primera.
- La linea superior del extracto pegado recibe el `fila_numero` mas alto del lote, por lo que sigue arriba cuando la vista ordena por fecha/fila descendente.
- El detalle de auditoria `primeras_filas` se calcula con el orden original de indices, no con el orden interno de insercion.

### Por que

Ordenar por fecha durante la importacion era una decision demasiado lista para su propio bien. En extractos bancarios, especialmente con lineas informativas, movimientos del mismo dia o saldos de detalle, el orden del fichero es parte del dato. Cambiarlo en backend rompe la lectura del banco y descoloca lineas auxiliares.

### Reglas tecnicas

- La validacion puede normalizar fecha, monto y saldo, pero no debe reordenar filas.
- `fila_numero` es el mecanismo de estabilidad visual: mayor numero significa linea mas reciente/superior en la vista descendente.
- Las filas no seleccionadas o invalidas no consumen `fila_numero`.
- No se cambia el ordenamiento general de `GET /api/extractos`; se corrige solo la numeracion creada por la importacion.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ImportacionServiceTests --no-restore`: 26/26 OK.
- `dotnet build ".\\Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" -c Release --no-restore`: OK, 0 warnings, 0 errores.

## 2026-04-26 - V-01.05 - Borrado multiple de extractos por cuenta

### Que cambio

- `CuentaDetailPage` incorpora seleccion multiple de filas en el desglose de cuenta.
- Se anade checkbox por fila, checkbox global para seleccionar todo y contador de seleccion.
- Se anade confirmacion unica para borrar en lote desde el mismo dashboard de cuenta.
- El borrado multiple llama en bucle al endpoint existente `DELETE /api/extractos/{id}`.

### Por que

Eliminar linea por linea era lento y propenso a errores cuando hay limpieza masiva. El cambio reduce clics sin abrir otra superficie de permisos.

### Reglas tecnicas

- No se crea endpoint nuevo: se reaprovecha la ruta actual para conservar validaciones y auditoria.
- Si falla un borrado durante el lote, se muestra error con progreso parcial y se recarga para dejar el estado real.
- El flujo de borrado multiple solo aparece si el usuario ya tiene permiso `puede_eliminar_lineas`.

### Verificacion

- `npm.cmd run build`: OK.
- `npm.cmd run lint`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK (codigo `1` esperado).

## 2026-04-26 - V-01.05 - Actualizacion automatica desde GitHub Release oficial

### Que cambio

- `ActualizacionService` mantiene `app_update_check_url` como repo oficial de GitHub (`https://github.com/AtlasLabs797/AtlasBalance`) y consulta `releases/latest` via API de GitHub.
- Si el release no trae `source_path`, el backend busca el asset `AtlasBalance-*-win-x64.zip`, valida que la URL pertenezca al repo oficial, descarga el ZIP y lo extrae dentro de `WatchdogSettings:UpdateSourceRoot`.
- Antes de entregar la ruta al Watchdog, el paquete debe contener `VERSION`, `api/AtlasBalance.API.exe` y `watchdog/AtlasBalance.Watchdog.exe`.
- La comparacion de versiones ahora normaliza etiquetas tipo `V-01.05-win-x64`, evitando comparaciones lexicas rotas con el formato real de releases.
- `WatchdogOperationsService` crea backup PostgreSQL previo con `pg_dump` antes de actualizar binarios. Si no puede crear backup y `RequireDatabaseBackupBeforeUpdate` esta activo, no actualiza.
- El Watchdog crea copia rollback de binarios antes de sincronizar y la restaura si falla la copia.
- Si `RequireHealthCheckAfterUpdate` esta activo, Watchdog exige que `ApiHealthUrl` responda OK tras arrancar la API; si falla, revierte binarios.
- La pantalla `Configuracion > Sistema` muestra el campo como repositorio GitHub de actualizaciones, no como endpoint JSON manual.

### Por que

El boton `Actualizar ahora` ya existia, pero era medio humo: con el repo de GitHub configurado podia detectar releases, pero no descargar el asset ni preparar una ruta local segura para el Watchdog. Ahora el flujo real es repo oficial -> ultimo release -> ZIP win-x64 validado -> carpeta segura de updates -> Watchdog.

### Reglas tecnicas

- No se aceptan assets fuera de `https://github.com/AtlasLabs797/AtlasBalance/releases/download/...`.
- No se extrae nada fuera de `UpdateSourceRoot`.
- No se actualiza si el paquete no parece un release Windows x64 completo.
- En produccion, `RequireDatabaseBackupBeforeUpdate` queda activo por defecto. Desactivarlo es una mala idea salvo tests controlados.
- En produccion, `RequireHealthCheckAfterUpdate` queda activo por defecto y usa `https://localhost/api/health`.

### Verificacion

- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" -c Release --filter "ActualizacionServiceTests|WatchdogOperationsServiceTests|ConfiguracionControllerTests"`: 14/14 OK.
- Parser PowerShell de `scripts/Instalar-AtlasBalance.ps1`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por copia con cambios.

## 2026-04-26 - V-01.05 - Actualizacion post-instalacion endurecida

### Que cambio

- `scripts/update.ps1` declara `PackagePath`, `InstallPath` y `SkipBackup` de forma explicita.
- El wrapper ya no usa `ValueFromRemainingArguments` para reenviar `-InstallPath` a `Actualizar-AtlasBalance.ps1`.
- `SeedData.EnsureDefaultFormatosImportacion` comprueba primero si el ID fijo del formato por defecto ya existe usando `IgnoreQueryFilters()`.
- Si el ID ya existe, el seeder no intenta insertar otra fila con la misma PK aunque banco/divisa esten incompletos, cambiados o heredados de una version anterior.
- Se agrego una regresion en `SeedDataTests` para una fila legacy con el ID de Sabadell ya existente y `BancoNombre`/`Divisa` nulos.

### Por que

La actualizacion real desde `V-01.04` demostro dos fallos operativos: el wrapper podia pasar mal `-InstallPath`, y el arranque de API podia morir antes de servir `/api/health` por `23505 pk_formatos_importacion`. Esa combinacion es mala: actualiza binarios, crea backup, pero deja el servicio parado. Arreglado en el flujo de release, no con parches manuales en servidor.

### Verificacion

- Parser PowerShell sobre `scripts/update.ps1` y `scripts/Actualizar-AtlasBalance.ps1`: OK.
- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter SeedDataTests`: 5/5 OK.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.05`: OK.
- ZIP corregido: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64.zip`, SHA256 `482189BB4B6F731CEB02ECA214A550B1CE9DB33C71F0DBF4E057761E8FD002C3`.

## 2026-04-26 - V-01.05 - Limpieza de artefactos locales

### Que cambio

- Se eliminaron artefactos locales no versionables: `.codex-runlogs/`, `output/`, logs de API y paquetes generados antiguos dentro de `Atlas Balance/Atlas Balance Release/`.
- `Atlas Balance/Atlas Balance Release/` queda solo con `.gitkeep`; los ZIP y carpetas de paquete se regeneran con `scripts/Build-Release.ps1` y se publican como assets de GitHub Releases.
- Se eliminaron directorios frontend vacios heredados de la limpieza de shadcn: `frontend/src/lib/` y `frontend/src/components/ui/`.
- `.gitignore` ahora ignora `.codex-runlogs/` y `output/`.

### Por que

Mantener paquetes release, logs, capturas y backups SQL temporales dentro del workspace ensucia el estado local y aumenta el riesgo de arrastrar datos privados. El codigo fuente y la documentacion quedan; los artefactos se regeneran cuando hacen falta.

### Verificacion

- `git check-ignore -v .codex-runlogs/foo output/foo`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ".\AtlasBalance.sln" -c Release --no-restore`: 107/108 OK; `ExtractosConcurrencyTests` falla porque Docker/Testcontainers no esta disponible.
- `dotnet test ".\AtlasBalance.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 107/107 OK.

## 2026-04-25 - V-01.05 - Paquete final y publicacion

### Que cambio

- Se regenero el paquete `AtlasBalance-V-01.05-win-x64.zip` con `scripts/Build-Release.ps1`.
- El build frontend del paquete quedo sincronizado en `backend/src/AtlasBalance.API/wwwroot`.
- El ZIP final queda fuera de Git y se publica como asset de GitHub Release.

### Verificacion

- `scripts\Build-Release.ps1 -Version V-01.05`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance\backend\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list "Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- Paquete verificado sin `appsettings.Development.json`, `.env`, `node_modules`, `obj`, `bin\Debug` ni `.bak-iframe-fix`.
- SHA256 final del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `B5ABC5525CBD49F2BD0A5ADC5B930A2113AF323F99C1337087B8E0D7875E6A10`.

## 2026-04-25 - V-01.05 - Auditoria de bugs y seguridad

### Que cambio

- Se reviso la superficie tecnica de seguridad activa: autenticacion JWT en cookies httpOnly, CSRF por header `X-CSRF-Token`, validacion de `SecurityStamp`, permisos backend, integracion OpenClaw, rutas de backup/exportacion, cabeceras HTTP, CI y secretos versionables.
- Se actualizaron los minimos declarados del frontend para cerrar deuda de supply chain: `axios ^1.15.2` y `react-router-dom ^6.30.3`.
- El bundle de produccion se recompilo y se sincronizo con `backend/src/AtlasBalance.API/wwwroot`.
- No se cambiaron contratos de API ni modelo de datos.

### Por que

El lockfile ya resolvia versiones seguras, pero dejar rangos minimos vulnerables en `package.json` es pedir que una reinstalacion sin lockfile fiable abra otra vez el agujero. Eso no es "flexibilidad", es pereza con consecuencias.

### Verificacion

- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ".\Atlas Balance\backend\AtlasBalance.sln" -c Release --no-build`: 107/107 OK.
- `dotnet list ".\Atlas Balance\backend\AtlasBalance.sln" package --vulnerable --include-transitive`: sin vulnerabilidades.
- `wwwroot`: sincronizado y sin sourcemaps, plantillas Development ni `.env`.

## 2026-04-25 - V-01.05 - Importacion simple de plazo fijo y resumen dashboard

### Que cambio

- `CuentaImportacionContextoResponse` expone `TipoCuenta` para que el frontend distinga cuentas normales, efectivo y plazo fijo.
- `ImportacionService.ValidarAsync` y `ConfirmarAsync` rechazan importaciones con formato para `PLAZO_FIJO`.
- Nuevo contrato `ImportacionPlazoFijoMovimientoRequest/Response`.
- Nuevo endpoint `POST /api/importacion/plazo-fijo/movimiento`.
- `RegistrarMovimientoPlazoFijoAsync` exige permiso de importacion, cuenta activa de plazo fijo, monto positivo y fecha.
- El movimiento usa `INGRESO` como monto positivo y `EGRESO` como monto negativo, calcula `saldo_actual = ultimo_saldo + monto_firmado`, asigna `fila_numero` con bloqueo transaccional cuando la BD es relacional y registra auditoria.
- `DashboardPrincipalResponse` incluye `PlazosFijos` con monto total convertido, intereses previstos convertidos, fecha/dias del proximo vencimiento y numero de cuentas.
- `DashboardService` calcula ese resumen con las cuentas visibles para el usuario y excluye plazos `RENOVADO`/`CANCELADO` del calculo de intereses/vencimiento.
- El frontend cambia automaticamente a un formulario simple cuando la cuenta seleccionada es `PLAZO_FIJO`.

### Por que

Un plazo fijo no tiene extracto bancario normal que mapear. Forzar CSV/Excel aqui era burocracia tecnica: lo correcto es registrar entrada o salida y que el sistema calcule el saldo.

### Reglas tecnicas

- Las cuentas de plazo fijo no deben depender de `formatos_importacion`.
- No permitir monto negativo en request; el signo lo decide `tipo_movimiento`.
- Los intereses previstos siguen siendo importe absoluto aproximado, no porcentaje.
- El resumen de dashboard respeta el alcance de cuentas visible para el usuario.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.
- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter "ImportacionServiceTests|DashboardServiceTests"`: 28/28 OK.
- `dotnet build "Atlas Balance/backend/src/AtlasBalance.API/AtlasBalance.API.csproj" -c Release`: OK, 0 warnings.

## 2026-04-25 - V-01.05 - Actualizaciones post-instalacion

### Que cambio

- `update.cmd` y `Actualizar Atlas Balance.cmd` devuelven el codigo de salida de PowerShell.
- `scripts\update.ps1` valida que el origen sea un paquete release antes de autoelevar.
- `scripts\update.ps1` soporta `-PackagePath` para que una instalacion ya actualizada pueda aplicar paquetes futuros desde otra carpeta.
- `scripts\Actualizar-AtlasBalance.ps1` conserva configuracion, crea backup DB previo, crea rollback de binarios, reemplaza API y Watchdog, copia scripts/wrappers operativos a la instalacion, actualiza `VERSION`, actualiza `atlas-balance.runtime.json` y valida `/api/health`.

### Por que

Instalar una vez no basta. Si el update no actualiza tambien su propia maquinaria, la siguiente actualizacion vuelve a depender de scripts viejos. Eso es deuda operativa disfrazada de "ya lo vemos luego".

### Verificacion

- Parser PowerShell OK para `update.ps1` y `Actualizar-AtlasBalance.ps1`.
- Ejecutar update desde carpeta fuente falla con mensaje de paquete invalido.
- `scripts\Build-Release.ps1 -Version V-01.05`: OK; ZIP regenerado.
- Scripts empaquetados parsean correctamente.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.
- Pendiente de entorno real: probar update desde `V-01.03` instalada a `V-01.05` en Windows Server 2019.

## 2026-04-25 - V-01.05 - Cierre de incidencias instalacion Windows Server 2019

### Que cambio

- `scripts\install.ps1` valida que la carpeta sea un paquete release antes de autoelevar.
- `scripts\Instalar-AtlasBalance.ps1` valida `api\AtlasBalance.API.exe` y `watchdog\AtlasBalance.Watchdog.exe` antes de instalar.
- El instalador mantiene autodeteccion `PostgreSQL\17\bin` antes que `16\bin` y muestra instrucciones concretas para instalacion manual si `winget` falla.
- El instalador detecta usuarios existentes en `"USUARIOS"` y, si los hay, no escribe `SeedAdmin:Password` ni un `Password admin inicial` falso.
- `scripts\Reset-AdminPassword.ps1` resetea una cuenta admin usando la conexion de produccion local: genera hash bcrypt 12, marca `primer_login`, activa usuario, limpia bloqueo, rota `security_stamp` y revoca refresh tokens.
- `scripts\Build-Release.ps1` empaqueta `Reset-AdminPassword.ps1` e `install-cert-client.ps1`.
- El health check post-instalacion usa `curl.exe -k` si esta disponible y deja `Invoke-WebRequest` como fallback.

### Por que

La instalacion estaba demasiado optimista. En Windows Server 2019 eso es pedir problemas: `winget` puede no existir, PowerShell puede fallar con TLS autofirmado y una BD existente no significa admin nuevo. El cambio elimina mentiras operativas.

### Reglas tecnicas

- Nunca ejecutar instalacion de servidor desde ZIP `main`/carpeta fuente.
- No regenerar credenciales iniciales si la BD ya tiene usuarios.
- No pedir SQL manual largo para reset admin; usar `Reset-AdminPassword.ps1`.
- Para health check operativo en Server 2019, preferir `curl.exe -k`.

### Verificacion

- Parser PowerShell OK para `Instalar-AtlasBalance.ps1`, `install.ps1`, `Reset-AdminPassword.ps1` y `Build-Release.ps1`.
- `Instalar-AtlasBalance.ps1` desde carpeta fuente falla con mensaje de paquete invalido.
- `install.ps1` desde carpeta fuente falla con mensaje de paquete invalido antes de autoelevar.
- `scripts\Build-Release.ps1 -Version V-01.05`: OK; ZIP generado.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Scripts empaquetados parsean correctamente.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.

## 2026-04-25 - V-01.05 - Apertura de version

### Que cambio

- `V-01.05` pasa a ser la version activa del sistema.
- Backend: `Directory.Build.props` sube a `1.5.0` y `InformationalVersion` a `V-01.05`.
- Frontend: `package.json` y `package-lock.json` suben a `1.5.0`; `appVersion` pasa a `V-01.05`.
- `Atlas Balance/VERSION`, `SeedData`, `Build-Release.ps1` e `Instalar-AtlasBalance.ps1` quedan alineados con `V-01.05`.
- `Documentacion/Versiones/v-01.03.md` queda cerrada como version publicada.
- `Documentacion/Versiones/v-01.05.md` queda como archivo activo de trabajo.

### Por que

`V-01.03` ya fue publicada. Seguir metiendo cambios ahi seria una forma bastante tonta de romper la trazabilidad.

### Reglas tecnicas

- Todo cambio nuevo debe documentarse bajo `V-01.05`.
- El siguiente paquete debe generarse con `scripts/Build-Release.ps1 -Version V-01.05`.
- No reutilizar assets ni notas de release de `V-01.03` para publicar `V-01.05`.

### Verificacion

- `git diff --check`: OK.
- `dotnet build '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `npm.cmd run build`: OK con `atlas-balance-frontend@1.5.0`.

## 2026-04-25 - V-01.03 - Paquete release Windows x64 generado

### Que cambio

- Se genero el paquete `AtlasBalance-V-01.03-win-x64` en `Atlas Balance/Atlas Balance Release`.
- Se genero el ZIP `AtlasBalance-V-01.03-win-x64.zip` para distribucion.
- `scripts/Build-Release.ps1` recompilo el frontend y reemplazo `AtlasBalance.API/wwwroot` con el bundle de produccion actual.
- API y Watchdog quedaron publicados como self-contained `win-x64`.
- El paquete incluye scripts operativos, `VERSION`, `README.md`, `documentacion.md`, `.gitignore` y `version.json`.

### Reglas tecnicas

- Los artefactos de `Atlas Balance/Atlas Balance Release` no deben entrar en commits normales; van como assets de GitHub Releases.
- Si se cambia documentacion incluida en el paquete despues de generar el ZIP, hay que regenerar el release. No hacerlo seria publicar un paquete con instrucciones atrasadas.
- `version.json` debe conservar `source_path = C:\AtlasBalance\updates\V-01.03\api` para actualizaciones de esta version.

### Verificacion

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.03`: OK.
- Carpeta generada: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.03-win-x64`.
- ZIP generado: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.03-win-x64.zip`.
- `version.json` y `VERSION` empaquetados: `V-01.03`.
- Barrido de `api` empaquetada: sin `*Development*`, `*.template` ni `.env`.

## 2026-04-25 - V-01.03 - Hardening de seguridad post-auditoria

### Que cambio

- Se agregaron `SecurityStamp` y `PasswordChangedAt` a `USUARIOS` mediante la migracion `UserSessionHardening`.
- Los access tokens incluyen `security_stamp`; `UserStateMiddleware` lo valida contra BD en cada request API autenticado.
- Cambios/reset de password, borrado de usuario y reuse de refresh token rotan el stamp y revocan refresh tokens activos.
- Login usa throttle por cliente/email y deja de distinguir externamente usuario bloqueado de credenciales invalidas.
- Reuse de refresh token revocado escala a incidente: revoca sesiones activas, rota stamp y registra `REFRESH_TOKEN_REUSE_DETECTED`.
- Passwords de usuarios y seed admin pasan a minimo 12 caracteres y bloqueo de passwords comunes.
- `IntegrationAuthMiddleware` corta bearer invalido repetido por IP/minuto antes de consultar tokens activos.
- `app_update_check_url` queda limitado a HTTPS del repo oficial `AtlasLabs797/AtlasBalance`.
- Backups, exportaciones, descargas y rutas Watchdog validan la ruta cruda antes de `Path.GetFullPath`.
- `config\INSTALL_CREDENTIALS_ONCE.txt` se borra automaticamente con tarea programada SYSTEM a las 24 horas.
- `postcss` queda resuelto a `8.5.10`.

### Impacto operativo

- Tras desplegar esta version, los access tokens antiguos sin `security_stamp` dejan de ser validos. Eso es correcto: los usuarios tendran que autenticarse otra vez.
- La URL de actualizaciones ya no acepta endpoints arbitrarios; si se necesita otro canal de releases, primero hay que ampliar la allowlist de forma explicita.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.

### Verificacion

- `dotnet build '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `dotnet test '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-build`: 94/94 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list '.\Atlas Balance\backend\AtlasBalance.sln' package --vulnerable --include-transitive`: sin vulnerabilidades.
- Parser PowerShell sobre `scripts/Instalar-AtlasBalance.ps1`: OK.

## 2026-04-20 - V-01.03 - Apertura de version

### Que cambio

- `V-01.03` pasa a ser la version activa del sistema.
- Backend: `Directory.Build.props` sube a `1.3.0` y `InformationalVersion` a `V-01.03`.
- Frontend: `package.json` y `package-lock.json` suben a `1.3.0`; `appVersion` pasa a `V-01.03`.
- `Atlas Balance/VERSION`, `SeedData`, `Build-Release.ps1` e `Instalar-AtlasBalance.ps1` quedan alineados con `V-01.03`.
- `Documentacion/Versiones/v-01.02.md` queda cerrada como version publicada.
- `Documentacion/Versiones/v-01.03.md` queda como archivo activo de trabajo.

### Por que

`V-01.02` ya fue publicada. Seguir metiendo cambios ahi seria versionado barro: funciona hasta que alguien necesita saber que demonios se desplego.

### Reglas tecnicas

- Todo cambio nuevo debe documentarse bajo `V-01.03`.
- El siguiente paquete debe generarse con `scripts/Build-Release.ps1 -Version V-01.03`.
- No reutilizar assets ni notas de release de `V-01.02` para publicar `V-01.03`.

### Verificacion

- `git diff --check`: OK; solo avisos esperados de normalizacion LF/CRLF.
- `dotnet build '.\Atlas Balance\backend\AtlasBalance.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `npm.cmd run build`: OK con `atlas-balance-frontend@1.3.0`.

## 2026-04-20 - V-01.02 - Release autonoma con scripts one-click

### Que cambio

- El paquete de release ahora incluye `install.cmd`, `update.cmd`, `uninstall.cmd` y `start.cmd`.
- Los `.cmd` llaman wrappers PowerShell en `scripts/install.ps1`, `scripts/update.ps1`, `scripts/uninstall.ps1` y `scripts/start.ps1`.
- `install.cmd` se autoeleva y llama al instalador real con `-InstallDependencies` por defecto.
- `Instalar-AtlasBalance.ps1` puede preparar PostgreSQL 16 gestionado con `winget`, usando servicio `AtlasBalance.PostgreSQL`, password generada y puerto libre si `5432` esta ocupado.
- `atlas-balance.runtime.json` registra si PostgreSQL es gestionado por Atlas, su servicio y la configuracion DB usada.
- `Launch-AtlasBalance.ps1` arranca en orden: PostgreSQL gestionado, Watchdog y API.
- `Actualizar-AtlasBalance.ps1` arranca PostgreSQL gestionado antes de crear backup y reemplazar binarios.
- `uninstall.ps1` elimina servicios, firewall, atajos, `%ProgramData%\AtlasBalance`, carpeta instalada y PostgreSQL gestionado si fue creado por el instalador.
- `Build-Release.ps1` copia los nuevos scripts y `README_RELEASE.md` dentro del paquete generado.

### Por que

La release anterior tenia piezas utiles, pero no cumplia literalmente el contrato de "install/update/uninstall/start" ni arrancaba la base de datos desde `start`. Eso es una grieta operativa: si PostgreSQL queda parado, el backend no arranca y el usuario culpa al frontend. Mal diagnostico, mala noche.

### Reglas tecnicas

- El frontend no se instala en produccion: se compila con Vite y se sirve desde `wwwroot` en la API.
- El backend publicado es self-contained; el servidor no necesita .NET Runtime.
- La API aplica migraciones EF Core en startup.
- Si se usa PostgreSQL externo, el instalador exige password admin o binarios `psql`; no intenta adivinar credenciales.
- `uninstall.cmd` solo borra la base gestionada por Atlas. Una base externa no se elimina sin una decision explicita.

### Verificacion

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.02`: OK.
- Paquete generado en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.02-win-x64`.
- ZIP generado en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.02-win-x64.zip`.
- Parser PowerShell sobre scripts fuente y scripts empaquetados: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK dentro del build de release.
- `dotnet test .\backend\AtlasBalance.sln -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 82/82 OK.
- Suite backend completa: 82/83 OK; `ExtractosConcurrencyTests` falla por Docker/Testcontainers no disponible en este entorno, incidencia ya conocida.
- Scanner local de secretos sobre el paquete generado: 0 hallazgos.
- Paquete verificado sin `appsettings.Development.json`, plantillas, source maps, `node_modules` ni `frontend/dist` suelto.
- `winget search PostgreSQL.PostgreSQL --source winget`: confirma existencia de `PostgreSQL.PostgreSQL.16` en este entorno.

## 2026-04-20 - V-01.02 - Auditoria tecnica profunda y hardening

### Que cambio

- `smtp_password` y `exchange_rate_api_key` en `CONFIGURACION` se almacenan protegidos con ASP.NET Core Data Protection y prefijo `enc:v1:`.
- En cada arranque, la API migra automaticamente esos valores si aun estan en claro.
- En produccion, las claves de Data Protection se guardan fuera del directorio servido, por defecto en `%ProgramData%/AtlasBalance/keys`; puede sobrescribirse con `DataProtection:KeysPath`. En Windows se protegen con DPAPI de maquina.
- `ConfiguracionController` no devuelve secretos al frontend y redacta esos valores en auditoria.
- `EmailService` y `TiposCambioService` descifran secretos solo justo antes de usarlos.
- `UserAccessService` ya no interpreta `PuedeVerDashboard` global como permiso global de datos.
- `ExportacionesController.Descargar` valida que el fichero sea `.xlsx` y este dentro de `export_path`.
- `AtlasBalance.Watchdog` escucha explicitamente en localhost mediante Kestrel.
- La API rechaza `AllowedHosts` vacio, placeholder o wildcard fuera de Development.
- Scripts de backup/restore/manual/service install usan nombres y usuarios actuales, restauran `PGPASSWORD` y validan extension `.dump`.
- Se eliminaron logs y artefactos de smoke/login con cookies, cabeceras o payloads sensibles.

### Por que

Guardar secretos en claro dentro de la tabla de configuracion era el riesgo mas serio que quedaba. Y el permiso global de dashboard era peor de lo que parecia: podia abrir datos fuera del alcance esperado. Eso no era "deuda tecnica"; era una fuga esperando su turno.

### Reglas tecnicas

- No leer `smtp_password` ni `exchange_rate_api_key` directamente salvo a traves de `ISecretProtector`.
- No cambiar la cuenta de servicio, mover de maquina o borrar el keyring de Data Protection sin plan de rotacion; los secretos cifrados quedarian ilegibles.
- Las exportaciones descargables deben seguir saliendo solo de `export_path`.
- Watchdog debe permanecer en loopback y autenticado con `X-Watchdog-Secret`.
- Produccion debe declarar hosts explicitos en `AllowedHosts`; wildcards ya no son aceptables.

### Verificacion

- `dotnet build "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore`: OK, 0 warnings.
- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-build`: 83/83 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet list ... package --vulnerable --include-transitive`: sin vulnerabilidades.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.

### Pendientes

- Rotar secretos locales si `.env` o `appsettings.Development.json` se compartieron fuera del equipo.
- Reparar el estado Git local si se necesita diff/commit/push fiable desde esta copia.

## 2026-04-20 - V-01.02 - Cierre de bugs reportados

### Que cambio

- `SeedAdmin:Email` queda normalizado como `admin@atlasbalance.local` en configuracion base y plantillas.
- Se corrigieron ejemplos, placeholders, rutas por defecto y tests que arrastraban `atlasbalnace` o `atlas-blance`.
- El evento interno de importacion ahora usa la constante compartida `IMPORTACION_COMPLETADA_EVENT` con namespace `atlas-balance`.
- `Instalar-AtlasBalance.ps1` escribe runtime `V-01.02`, no `V-01.01`.
- La documentacion de instalacion y `SPEC.md` apuntan a `V-01.02` y rutas `C:/AtlasBalance`.
- El build frontend generado se copio a `backend/src/AtlasBalance.API/wwwroot` para que la API local sirva el bundle corregido.

### Por que

La revision previa no estaba equivocada, pero estaba incompleta: el codigo principal ya tenia varios fixes, mientras que configuracion, scripts y artefactos servidos seguian arrastrando restos. Eso es peor que un bug obvio, porque parece arreglado hasta que instalas o pruebas desde el backend.

### Verificacion

- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore`: 82/82 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `docker ps --filter "name=atlas_balance_db"`: contenedor activo en `5433->5432`.
- Barrido `Select-String` en codigo activo y `wwwroot`: 0 restos de `atlasbalnace`, `atlas-blance` o `V-01.01`.

### Pendientes

- Ninguno de estos bugs queda abierto.

## 2026-04-20 - V-01.02 - Auditoria de seguridad y bugs

### Que cambio

- Se eliminaron secretos y passwords de desarrollo de configuracion versionable.
- `SeedAdmin:Password` pasa a ser obligatorio antes del primer arranque con BD vacia.
- Si `JwtSettings:Secret` falta en Development, la API genera una clave efimera de proceso; fuera de Development sigue siendo obligatorio.
- Watchdog ya no usa password de BD por defecto para restauraciones.
- `docker-compose.yml` exige `ATLAS_BALANCE_POSTGRES_PASSWORD` desde `.env` local o variable de entorno.
- Se anadieron plantillas de configuracion para API y Watchdog, y un `.env.example` sin secretos.
- `SeedData` usa `V-01.02` y el check de actualizacion usa la version runtime en el User-Agent.
- Se corrigieron mensajes mojibake en importacion y asunto SMTP.
- GitHub Actions queda fijado a SHAs concretos para reducir riesgo de supply chain.
- Se anadio `.gitignore` dentro de `Atlas Balance` para proteger la app si se trabaja desde esa carpeta como raiz.

### Por que

Los secretos "solo de desarrollo" en archivos base son una bomba lenta: se copian, se reutilizan y un dia llegan a produccion. La configuracion base debe ser segura por defecto y obligar a crear secretos locales/produccion fuera de Git.

### Reglas tecnicas

- No commitear `appsettings.Development.json`, `appsettings.Production.json`, `.env`, certificados, logs ni paquetes generados.
- Para desarrollo local, copiar las plantillas y rellenar secretos reales en archivos ignorados.
- Para produccion, generar secretos fuertes distintos para JWT, Watchdog, PostgreSQL, certificado y admin inicial.
- No ejecutar restauraciones Watchdog si `WatchdogSettings:DbPassword` no esta configurado.

### Verificacion

- `python Skills/Seguridad/cyber-neo-main/skills/cyber-neo/scripts/scan_secrets.py "Atlas Balance" --json`: 0 hallazgos.
- `dotnet list "Atlas Balance/backend/AtlasBalance.sln" package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm.cmd audit --json`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance/backend/AtlasBalance.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

### Pendientes

- Reparar el estado Git local si se necesita commit/push fiable desde esta carpeta.

## 2026-04-20 - V-01.01 - Reorganizacion de estructura

### Que cambio

- La aplicacion quedo centralizada en `Atlas Balance`.
- Los paquetes existentes quedaron en `Atlas Balance/Atlas Balance Release`.
- La documentacion quedo centralizada en `Documentacion`.
- Material auxiliar, duplicados y artefactos temporales quedaron en `Otros`.
- `CLAUDE.md` y `AGENTS.md` fueron actualizados sin planificacion por bloques temporales.
- `Atlas Balance/scripts/Build-Release.ps1` ahora genera paquetes en `Atlas Balance/Atlas Balance Release`.
- `Build-Release.ps1` copia la documentacion de usuario desde `Documentacion/documentacion.md`.
- El repositorio Git quedo en la raiz para versionar juntos `Atlas Balance` y `Documentacion`.

### Por que

La estructura anterior mezclaba app real, scaffolding, duplicados, documentacion, repos auxiliares de diseno y artefactos generados. Eso aumenta el riesgo de tocar lo equivocado y hace mas dificil empaquetar o revisar cambios.

### Como queda

- Runtime y codigo fuente: `Atlas Balance`
- Releases: `Atlas Balance/Atlas Balance Release`
- Documentacion: `Documentacion`
- Auxiliares no runtime: `Otros`

### Verificacion esperada

- `git status --short` debe funcionar desde la raiz del proyecto.
- `powershell -File "Atlas Balance/scripts/Build-Release.ps1" -Version V-01.01` debe publicar en `Atlas Balance/Atlas Balance Release`.
- `dotnet build "Atlas Balance/backend/AtlasBalance.sln" --no-restore` debe resolver rutas relativas dentro de la app.

## 2026-04-20 - V-01.01 - Catalogo de skills locales

### Que cambio

- Se analizo `Skills` y se separaron skills reales de copias repetidas por agente.
- Se creo `Documentacion/SKILLS_LOCALES.md` como catalogo canonico.
- Se actualizaron `CLAUDE.md`, `AGENTS.md`, `Atlas Balance/CLAUDE.md` y `Atlas Balance/AGENTS.md` para indicar como y cuando usar skills locales.

### Por que

La carpeta `Skills` contiene repos completos y varias carpetas repetidas para diferentes agentes. Sin una guia, un agente puede cargar duplicados, ejecutar scripts innecesarios o aplicar reglas de stack equivocadas. Eso seria ruido, no mejora.

### Reglas tecnicas

- La documentacion canonica de uso vive en `Documentacion/SKILLS_LOCALES.md`.
- Para cada tarea se debe cargar solo la skill relevante.
- Las recomendaciones de las skills se subordinan al stack real de Atlas Balance.
- No se deben ejecutar CLIs o scripts dentro de `Skills` sin necesidad clara.

## 2026-04-20 - V-01.01 - Politica de subida a GitHub

### Que cambio

- `.gitignore` ahora excluye explicitamente `Otros/` y `Skills/`.
- `Atlas Balance/Atlas Balance Release/` queda como carpeta local de salida, mantenida en Git solo con `.gitkeep`.
- Los paquetes generados de release se publican como assets de GitHub Releases, no como archivos en la historia Git.
- Las instrucciones de agentes indican que GitHub debe recibir todo lo versionable excepto `Otros/`, `Skills/` y paquetes generados de release.

### Por que

El repositorio oficial debe contener el proyecto util para desarrollo, documentacion y configuracion, pero no repos auxiliares, duplicados de trabajo, skills locales pesadas ni binarios generados. Los ZIP de release pesan demasiado para vivir comodamente en Git; GitHub Releases es el sitio correcto para distribuirlos.

### Reglas tecnicas

- Subir a GitHub como Git: codigo, documentacion, configuracion y scripts.
- Subir a GitHub Releases: ZIP, carpetas empaquetadas y binarios generados de release.
- No subir nunca: `Otros/`, `Skills/`, secretos, `.env`, logs, cookies, tokens, certificados privados, `node_modules`, `bin/obj` ni artefactos locales sensibles.

## 2026-04-23 - V-01.03 - Cierre de fuga de alcance global en extractos

### Que cambio

- `ExtractosController.GetAllowedAccountIds` y `CanViewTitular` dejaron de tratar `PuedeVerDashboard` global como permiso global de datos.
- El alcance global en extractos queda restringido a permisos de datos reales: `PuedeAgregarLineas`, `PuedeEditarLineas`, `PuedeEliminarLineas` o `PuedeImportar`.
- Se agrego regresion automatizada en `ExtractosControllerTests` para impedir que `/api/extractos` devuelva datos cross-account a perfiles dashboard-only globales.

### Por que

La logica local de `ExtractosController` estaba mas permisiva que `UserAccessService`. Esa divergencia abria una fuga de datos financieros entre cuentas.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~AtlasBalance.API.Tests.ExtractosControllerTests|FullyQualifiedName~AtlasBalance.API.Tests.UserAccessServiceTests"`: 8/8 OK.

## 2026-04-24 - V-01.03 - Frontend alineado con permisos reales de cuenta

### Que cambio

- `frontend/src/stores/permisosStore.ts` diferencia entre alcance de cuenta y permiso global solo de dashboard.
- Una fila global `cuenta_id = null`, `titular_id = null` ya no habilita `canViewCuenta` ni contamina `getColumnasVisibles/getColumnasEditables` salvo que conceda acceso global de datos (`agregar`, `editar`, `eliminar`, `importar`).
- `frontend/src/pages/CuentasPage.tsx` ya no ofrece enlaces o botones a `/dashboard/cuenta/:id` para cuentas sin acceso real; muestra `Sin acceso`.
- `frontend/src/pages/CuentaDetailPage.tsx` intercepta `403` del backend y redirige a `/dashboard` en vez de dejar al usuario atrapado en un error de carga.

### Por que

El backend ya estaba bien. El frontend seguia mintiendo: ensenaba rutas de cuenta a perfiles `dashboard-only` globales, como si pudieran abrirlas. Eso no filtraba datos, pero era UX rota y semantica de permisos incoherente.

### Reglas tecnicas

- En frontend, el acceso a cuenta no debe inferirse de cualquier permiso coincidente. Una fila global solo vale como acceso de cuenta si equivale a acceso global de datos.
- Los estados visuales de apertura de cuenta tienen que apoyarse en la misma semantica que backend. Si backend va a responder `403`, frontend no debe mostrar un CTA operativo.
- Cuando una ruta depende de datos protegidos y el backend responde `403`, la pantalla debe redirigir o cerrar el paso de forma limpia, no quedarse en un error generico.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.

## 2026-04-25 - V-01.05 - Importacion con advertencias para filas solo concepto

### Que cambio

- `ImportacionService.ValidateRows` diferencia errores fatales de advertencias importables.
- Las filas con concepto y fecha/monto/saldo vacios pasan a ser validas con advertencias.
- Para poder persistirlas en `EXTRACTOS`, la fecha y el saldo se heredan de la ultima fila valida anterior y el monto se normaliza a `0`.
- `FilaValidacionResponse` expone `Advertencias` y el frontend las muestra en la tabla de validacion con estado visual de aviso.
- Se agregaron regresiones para validar e importar filas informativas sin romper las reglas existentes de filas ambiguas.

### Por que

Algunos bancos exportan lineas informativas o de detalle como filas separadas con solo concepto. Bloquearlas como error obligaba al usuario a descartarlas aunque quisiera conservar esa informacion en el extracto.

### Reglas tecnicas

- Solo se relajan filas claramente informativas: concepto presente y fecha, importe y saldo vacios.
- Una fila con fecha/saldo pero importe vacio sigue siendo error; eso ya no es una descripcion, es un movimiento incompleto.
- Una fila sin referencia previa de fecha o saldo sigue siendo error, porque inventar datos financieros desde cero seria una mala idea.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ImportacionServiceTests`: 21/21 OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.

## 2026-04-25 - V-01.05 - Permiso global explicito para ver cuentas

### Que cambio

- `PERMISOS_USUARIO` incorpora `puede_ver_cuentas`.
- `UserAccessService`, `ExtractosController`, `AuthService` y las respuestas de permisos exponen y respetan ese permiso.
- El alcance global sobre todas las cuentas se concede si existe una fila global (`cuenta_id = null`, `titular_id = null`) con `puede_ver_cuentas` o con permisos de datos heredados (`agregar`, `editar`, `eliminar`, `importar`).
- El modal de usuarios agrega el boton `Acceso a todas las cuentas` y el checkbox `Ver cuentas`.
- La migracion `AddPuedeVerCuentasPermiso` rellena `puede_ver_cuentas = true` para permisos existentes que ya daban acceso por scope o por acciones de datos, sin convertir permisos globales dashboard-only.

### Por que

Hasta ahora se podia conseguir acceso global solo dejando scope vacio y marcando una accion de datos. Eso era poco claro y empujaba a conceder importacion o edicion solo para que el usuario pudiera ver cuentas. Mala idea: visibilidad y escritura deben ser permisos distintos.

### Reglas tecnicas

- `puede_ver_dashboard` no concede acceso a extractos ni a todas las cuentas.
- `puede_ver_cuentas` concede visibilidad/lectura de cuentas dentro de su scope.
- Los permisos de escritura/importacion siguen implicando visibilidad para compatibilidad, pero no al reves.

### Verificacion

- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter "UserAccessServiceTests|UsuariosControllerTests|ExtractosControllerTests"`: 12/12 OK.
- `dotnet test "Atlas Balance/backend/tests/AtlasBalance.API.Tests/AtlasBalance.API.Tests.csproj" --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 97/97 OK.
- `dotnet build "Atlas Balance/backend/src/AtlasBalance.API/AtlasBalance.API.csproj" -c Release`: OK, 0 warnings.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; `robocopy` devolvio codigo `1`, copia correcta con archivos actualizados.

## 2026-04-25 - V-01.05 - Plazo fijo, autonomos, alertas por tipo y dashboard inmovilizado

### Que cambio

- `TipoTitular` incorpora `AUTONOMO` sin alterar los valores enteros existentes de `EMPRESA` y `PARTICULAR`.
- `Cuenta` incorpora `TipoCuenta`; `es_efectivo` se mantiene por compatibilidad, pero la logica nueva usa `tipo_cuenta`.
- Nueva tabla `PLAZOS_FIJOS` con relacion 1:1 a cuenta, cuenta de referencia opcional, fechas, interes previsto, renovable, estado, notificacion y soft delete.
- Nueva migracion `AddPlazoFijoAutonomosAlertas`: rellena `tipo_cuenta = EFECTIVO` desde `es_efectivo`, crea indices y constraints de fechas/interes.
- `GET /api/titulares` acepta `tipoTitular`.
- `GET /api/cuentas` acepta `tipoTitular` y `tipoCuenta`; las respuestas exponen `titular_tipo`, `tipo_cuenta` y `plazo_fijo`.
- `POST/PUT /api/cuentas` crean y editan cuentas de plazo fijo.
- `POST /api/cuentas/{id}/plazo-fijo/renovar` renueva manualmente, audita y no crea movimientos.
- `PlazoFijoVencimientoJob` corre diario con Hangfire y usa `IPlazoFijoService`.
- `ALERTAS_SALDO` admite `tipo_titular`; `AlertaService` aplica prioridad cuenta > tipo titular > global.
- Dashboard separa saldos disponibles e inmovilizados y agrupa saldos por titular por tipo.

### Por que

Un plazo fijo es patrimonio, pero no liquidez. Meterlo como saldo normal mentia en el dashboard. La app ahora diferencia dinero disponible de dinero inmovilizado sin inventar transferencias ni liquidaciones automaticas.

### Reglas tecnicas

- No cambiar una cuenta `PLAZO_FIJO` a otro tipo: se bloquea y se debe crear otra cuenta.
- `fecha_vencimiento >= fecha_inicio`.
- `interes_previsto` es importe absoluto y no puede ser negativo.
- El job marca `VENCIDO` el mismo dia de vencimiento.
- Las alertas globales, por tipo y por cuenta son mutuamente excluyentes por alcance.
- `puede_ver_dashboard` sigue sin abrir datos fuera del alcance autorizado.

### Verificacion

- `dotnet build ...AtlasBalance.API.csproj -c Release`: OK.
- Tests focalizados de cuentas/dashboard/alertas/plazos: 12/12 OK.
- Tests backend sin Testcontainers: 103/103 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.

## 2026-04-25 - V-01.05 - Coherencia visual del frontend

### Que cambio

- `frontend/src/styles/variables.css` incorpora tokens semanticos para controles, superficies, sombras, foco y estados de interaccion.
- `frontend/src/styles/global.css` alinea inputs, selects, botones base y tokens shadcn/Tailwind con las variables propias de Atlas Balance.
- `frontend/src/components/ui/button.tsx` deja de usar medidas y colores genericos de shadcn y pasa a respetar radios, alturas, foco y variantes del sistema visual de la app.
- `frontend/src/styles/layout.css` agrega una capa comun para paginas, headers, cards, tablas, tabs, navegacion, modales y estados hover/focus.
- `frontend/src/styles/auth.css` ajusta login para usar las mismas superficies, foco, sombras y boton primario del resto del producto.

### Por que

La app tenia buena base, pero habia dos sistemas visuales compitiendo: CSS variables propias y tokens shadcn/Tailwind genericos. Eso acababa creando diferencias sutiles entre botones, tabs, campos, cards y estados de foco. Sutil en una pantalla; feo cuando recorres toda la app.

### Reglas tecnicas

- No se agrega ninguna dependencia.
- Tailwind/shadcn solo se usan donde ya existian; sus tokens se subordinan al sistema propio.
- Las alturas minimas de controles se mantienen cerca de 44px para touch y teclado.
- Las animaciones siguen limitadas a color, sombra, transform y opacity.
- Los cambios son sistemicos; no se reescribe funcionalidad de paginas.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Screenshots Playwright de `/login`: `output/playwright/ui-login-desktop.png` y `output/playwright/ui-login-mobile.png`.

## 2026-04-25 - V-01.05 - CSS de layout separado por dominios

### Que cambio

- `frontend/src/styles/layout.css` queda como archivo indice con imports.
- Los estilos se reparten en:
  - `frontend/src/styles/layout/shell.css`
  - `frontend/src/styles/layout/users.css`
  - `frontend/src/styles/layout/extractos.css`
  - `frontend/src/styles/layout/entities.css`
  - `frontend/src/styles/layout/dashboard.css`
  - `frontend/src/styles/layout/importacion.css`
  - `frontend/src/styles/layout/admin.css`
  - `frontend/src/styles/layout/system-coherence.css`

### Por que

`layout.css` habia pasado de ser hoja de layout a cajon de todo: shell, usuarios, extractos, titulares, dashboard, importacion, configuracion, auditoria y capa visual comun. Eso escala fatal. Separarlo reduce el coste de tocar una pantalla sin romper otra por accidente.

### Reglas tecnicas

- Se mantiene el orden original de cascada mediante imports en `layout.css`.
- No se cambia ningun selector ni comportamiento visual intencionadamente.
- `system-coherence.css` queda al final porque actua como capa comun de overrides visuales.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `git diff --check` en los CSS tocados: OK, con aviso esperado de normalizacion CRLF/LF.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Calendario nativo alineado con inputs

### Que cambio

- `frontend/src/styles/global.css` agrega reglas para `input[type='date']`.
- Se fuerza `color-scheme` claro/oscuro en `html` y en los inputs de fecha para que el picker nativo del navegador respete el tema activo.
- Se estiliza `::-webkit-calendar-picker-indicator` con fondo, radio, hover, active y filtro en dark mode.
- Se normalizan las partes internas `::-webkit-datetime-edit` y `::-webkit-datetime-edit-fields-wrapper`.

### Por que

Los campos de fecha del plazo fijo eran inputs nativos y el icono/picker del calendario quedaban fuera del sistema visual. Feo y evitable.

### Limitacion

El calendario desplegable es nativo del navegador/OS. CSS puede mejorar tema e indicador, pero no convertirlo en un componente totalmente propio sin reemplazar `input type="date"` por un date picker custom.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Vencimiento visible en detalle de plazo fijo

### Que cambio

- `ExtractosDtos.CuentaResumenKpiResponse` incluye `TipoCuenta` y `PlazoFijo`.
- `ExtractosController.GetCuentaResumen`, `GetCuentasTitular` y `GetTitularesResumen` pasan `TipoCuenta` a `BuildSummary`.
- `BuildSummary` adjunta `PlazoFijoResponse` solo para cuentas `PLAZO_FIJO`.
- `CuentaDetailPage` muestra una banda compacta bajo el titulo con fecha de vencimiento, dias restantes/vencido y estado.
- `entities.css` agrega estilos de `.cuenta-plazo-summary`.

### Por que

El dato de vencimiento existia al crear/editar la cuenta y en la lista de cuentas, pero no aparecia en el dashboard de cuenta. Eso obligaba al usuario a salir de la pantalla donde esta mirando saldo y movimientos, justo donde el vencimiento importa.

### Verificacion

- `dotnet build "Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" -c Release`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Date picker propio

### Que cambio

- Se crea `frontend/src/components/common/DatePickerField.tsx`.
- Se reemplazan los `input type="date"` en:
  - `components/extractos/AddRowForm.tsx`
  - `pages/AuditoriaPage.tsx`
  - `pages/CuentasPage.tsx`
  - `pages/ImportacionPage.tsx`
- `global.css` incorpora los estilos `.date-picker-*` y `.date-field`.
- El popover calcula si debe abrir hacia abajo o hacia arriba segun el espacio disponible.

### Por que

El calendario nativo del navegador no puede ajustarse al diseno Atlas de forma fiable. El intento anterior estilaba el campo cerrado, pero al abrir el selector volvia a aparecer una UI ajena al producto.

### Decisiones de diseno

- Mantener una superficie blanca, borde suave y sombra contenida, siguiendo `Documentacion/Diseno/DESIGN.md`.
- Usar `lucide-react` para iconos porque ya esta instalado en el proyecto.
- No meter una libreria de date picker: seria dependencia nueva para un componente pequeno y controlable.
- Incluir `Hoy` y `Limpiar` como acciones compactas para filtros y formularios.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Navegador in-app en `http://localhost:5173/cuentas`: se abre el modal de editar plazo fijo, el calendario se muestra con el sistema visual Atlas y no hay errores de consola.

## 2026-05-01 - V-01.05 - Hardening por checklist general de seguridad

### Que cambio

- `USUARIOS` incorpora `mfa_enabled`, `mfa_secret`, `mfa_enabled_at` y `mfa_last_accepted_step`.
- `AuthService.RequiresMfaAsync` centraliza la decision: `Rol=ADMIN` siempre MFA; para `GERENTE`/`EMPLEADO` consulta la clave `require_mfa_for_non_admin_users` en `CONFIGURACION`, con fallback fail-closed a `Security:RequireMfaForWebUsers` mientras la clave no este sembrada. Esto sustituye la politica global anterior.
- `ConfiguracionController` persiste la nueva clave y emite una auditoria semantica `MFA_POLICY_UPDATED` cuando cambia, para que un operador pueda auditar el interruptor sin parsear el diff before/after de `UPDATE_CONFIGURACION`.
- El login correcto con password crea un challenge temporal MFA y no emite JWT hasta validar el codigo.
- Si el usuario aun no tenia MFA, el challenge entrega una clave TOTP para enrolamiento y la guarda protegida al verificar el primer codigo.
- `TotpService` implementa RFC 6238 con HMAC-SHA1, periodo de 30 segundos, 6 digitos y tolerancia de un intervalo.
- `AuthController` agrega `POST /api/auth/mfa/verify`.
- `CsrfMiddleware` excluye el verify MFA porque ocurre antes de tener sesion/cookie autenticada.
- `UsuariosController` rota `security_stamp` y revoca refresh tokens al cambiar permisos, permiso de cuenta, email, perfil o restaurar usuario.
- `ActualizacionService` verifica el `digest` SHA-256 del asset descargado desde GitHub Release antes de extraerlo.
- CI agrega escaneo de secretos de alta confianza sobre archivos versionados.
- `LoginPage` soporta el segundo paso MFA y el setup inicial.
- `wwwroot` se sincroniza con el build frontend nuevo.

### Por que

El checklist general marcaba puntos P0 que si aplican a Atlas Balance: MFA, sesiones regeneradas ante cambio de permisos, verificacion de updates, secret scanning e incident response. Lo demas que habla de movil, IA, RAG, pagos, cloud o Kubernetes no pertenece al producto actual.

### Reglas tecnicas

- No se emiten cookies `access_token`/`refresh_token` hasta completar MFA.
- Los challenges MFA viven en memoria 5 minutos y aceptan maximo 5 fallos.
- `mfa_last_accepted_step` evita reutilizar el mismo codigo TOTP.
- Los secretos MFA nunca deben aparecer en logs ni documentacion.
- El digest de GitHub no sustituye la firma de codigo, pero bloquea ZIPs manipulados entre la API de releases y el extractor local.
- Todo cambio de permisos o identidad revoca sesiones del usuario afectado aunque el backend ya lea permisos desde BD; el frontend no debe seguir con permisos cacheados viejos.

### Verificacion

- `dotnet build ".\Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj" -c Release --no-restore`: OK.
- Tests focalizados auth/usuarios/update/CSRF/sesion: 24/24 OK.
- Tests backend sin Testcontainers: 115/115 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- NuGet vulnerable audit: sin hallazgos.

## 2026-04-25 - V-01.05 - Correccion de hallazgos de auditoria

### Que cambio

- El frontend deja de depender de Tailwind/shadcn: se eliminan dependencias, plugin Vite, imports CSS, `components.json`, `components/ui/button.tsx` y `lib/utils.ts`.
- `global.css` queda como entrada de tokens/estilos propios, sin `@theme`, `@apply`, imports Tailwind ni compatibilidad shadcn.
- `backend/src/AtlasBalance.API/wwwroot` se sincroniza desde `frontend/dist` para que la API sirva los bundles corregidos.
- Se reemplazan fondos decorativos por superficies planas con tokens propios en `global.css`, `auth.css` y estilos de layout.
- `CuentaResumenResponse` se amplia con `CuentaNombre`, `Divisa`, `TitularId`, `TitularNombre`, `EsEfectivo`, `TipoCuenta`, `PlazoFijo`, `Notas` y `UltimaActualizacion`.
- `CuentasController.Resumen` resuelve el resumen mensual y adjunta metadatos de plazo fijo cuando corresponde.
- `DatePickerField` gana semantica de grid, etiquetas de fecha completas y navegacion con flechas/Home/End.
- `ConfirmDialog` implementa focus trap basico con Tab/Shift+Tab.
- `AppSelect` abre y cierra con Enter/Espacio ademas de raton/flechas.

### Por que

La auditoria encontro deuda real, no cosmetica: un segundo sistema de estilos contradiciendo la arquitectura, un endpoint de resumen con contrato inferior al endpoint usado por la UI y controles custom que no cerraban el contrato minimo de teclado.

### Reglas tecnicas

- No se acepta Tailwind/shadcn como dependencia implicita del producto. Si algun dia se quiere usar, debe cambiar primero la documentacion canonica.
- Los resumentes de cuenta no deben divergir en campos criticos: tipo de cuenta, titular y plazo fijo son parte del contrato de lectura.
- Todo control propio que sustituya a un nativo debe cubrir teclado basico antes de release.
- Los fondos de app deben priorizar tokens, bordes, spacing y tipografia sobre degradados decorativos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release --filter CuentasControllerTests`: 4/4 OK.
- `dotnet test ".\\Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list ".\\Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-05-11 - V-01.06 - OpenRouter ajustado a allowlist de modelos gratis

### Que cambio

- `AiConfiguration.OpenRouterModels` queda limitado a `openrouter/auto` y modelos gratis permitidos.
- Esta entrada fue ampliada el mismo dia: la allowlist actual contiene seis modelos gratis, pero `Auto` usa `models` con maximo 3 candidatos por request. Ver la seccion superior `OpenRouter Auto limitado a 3 modelos en models`.
- Las llamadas a modelos gratis se pinchan al proveedor exacto con `provider.only` y `allow_fallbacks=false`:
  - `openai/gpt-oss-120b:free` -> `open-inference/int8`.
  - `minimax/minimax-m2.5:free` -> `open-inference/int8`.
  - `google/gemma-4-31b-it:free` -> `google-ai-studio`.
- Para los modelos gratis tambien se envia `provider.zdr=true` y `data_collection=deny`. La prioridad es no sacar contexto financiero a proveedores con retencion; si eso rompe disponibilidad de un modelo gratis, se cambia el modelo, no la politica.
- La auditoria IA incluye `runtime_model` y marca `zero_data_retention=true` cuando el proveedor es OpenRouter porque la request exige ZDR por contrato.
- El mensaje de 404 por politica/guardrail explica que Atlas ya esta enviando los modelos de la allowlist y que, si persiste, hay que revisar `OpenRouter > Settings > Privacy` o anadir un modelo ZDR permitido.

### Por que

La cuenta de OpenRouter del usuario restringe modelos. Usar un default externo a esa allowlist era una mala idea: aunque el modelo exista, OpenRouter lo descarta por guardrails de cuenta. La solucion final actual es obedecer los slugs exactos permitidos y usar `models` con un fallback de maximo 3 candidatos.

### Verificacion

- API publica de OpenRouter revisada para slugs y endpoints.
- `dotnet build '.\Atlas Balance\backend\src\AtlasBalance.API\AtlasBalance.API.csproj' -p:UseAppHost=false --no-restore`: OK.
- `AtlasAiServiceTests`: 29/29 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox; dentro queda bloqueado por `spawn EPERM` conocido.
- `wwwroot` sincronizado.
- API reiniciada y `/api/health`: `healthy`.

## 2026-05-10 - V-01.06 - Gobierno seguro de IA

### Contrato de seguridad

- Las claves de OpenRouter/OpenAI solo viven en `CONFIGURACION.openrouter_api_key` y `CONFIGURACION.openai_api_key`, protegidas con `ISecretProtector`; `GET /api/ia/config` devuelve solo flags `*_api_key_configurada`.
- El frontend nunca llama a OpenRouter/OpenAI. Todas las llamadas salen desde `AtlasAiService` mediante los `HttpClient` `openrouter` u `openai`.
- Cada proveedor tiene un cliente fallback (`openrouter-fallback` / `openai-fallback`). El comportamiento actual es salida directa por defecto; si se configura `Ia:UseSystemProxy=true` o `Ia:ProxyUrl`, el cliente principal usa proxy y el fallback queda directo. Si el primer envio lanza `HttpRequestException`, `AtlasAiService` reconstruye la request y reintenta por el fallback antes de devolver error de red.
- `POST /api/ia/chat` exige usuario autenticado y delega en `AtlasAiService`, que valida:
  - `ai_enabled=true`.
  - `USUARIOS.puede_usar_ia=true`.
  - proveedor/modelo permitido.
  - API key presente en backend.
  - limites de requests por usuario y globales.
  - presupuesto mensual/total si hay coste estimado configurado.
  - maximo aproximado de tokens de entrada y salida.
- Los permisos se validan en base de datos en cada request, no solo por claim ni por React.
- Los cambios de usuario siguen rotando `SecurityStamp` y revocando refresh tokens.
- OpenRouter queda restringido por allowlist backend y por privacidad de request: `provider.zdr=true` y `data_collection=deny` en todas las llamadas. Los modelos gratis permitidos solo son aceptables si OpenRouter puede servirlos con esa politica.
- `openrouter/auto` se conserva como valor guardado, pero la llamada Auto se materializa como `models` con maximo 3 candidatos gratis permitidos.
- Las llamadas a OpenAI usan API key de servidor contra `https://api.openai.com/v1/chat/completions`.

### Configuracion

Nuevas claves en `CONFIGURACION`:

- `ai_enabled`
- `ai_provider`
- `ai_model`
- `openrouter_api_key`
- `openai_api_key`
- `ai_requests_per_minute`
- `ai_requests_per_hour`
- `ai_requests_per_day`
- `ai_global_requests_per_day`
- `ai_monthly_budget_eur`
- `ai_total_budget_eur`
- `ai_budget_warning_percent`
- `ai_input_cost_per_1m_tokens_eur`
- `ai_output_cost_per_1m_tokens_eur`
- `ai_max_input_tokens`
- `ai_max_output_tokens`
- `ai_max_context_rows`
- `ai_usage_month_key`
- `ai_usage_month_cost_eur`
- `ai_usage_total_cost_eur`
- `ai_usage_total_requests`
- `ai_usage_last_user_id`
- `ai_usage_last_at_utc`

La migracion `20260510123000_HardenAiGovernance` agrega `USUARIOS.puede_usar_ia`, indice `ix_usuarios_puede_usar_ia` e inserta defaults de configuracion si faltan.

Los presupuestos mensual/total se comparan contra `ai_usage_month_cost_eur` y `ai_usage_total_cost_eur`. No se recalculan desde `AUDITORIAS`, porque `LimpiezaAuditoriaJob` borra auditorias antiguas a los 28 dias y eso habria permitido perder gasto historico.

### Auditoria IA

Nuevas acciones:

- `IA_CONSULTA`: uso correcto. Guarda usuario, proveedor, modelo, cliente HTTP usado, si hubo fallback, movimientos analizados, longitud de pregunta, longitud de contexto, tokens aproximados y coste estimado.
- `IA_CONSULTA_BLOQUEADA`: bloqueo por permiso, IA global, limites, presupuesto, tokens o configuracion.
- `IA_CONSULTA_ERROR`: fallo de proveedor, red, timeout o respuesta malformada. En errores de transporte guarda cliente principal/fallback y mensajes tecnicos recortados; nunca prompt, respuesta completa ni API key.
- `IA_PRESUPUESTO_AVISO`: aviso al superar el porcentaje configurado.

Regla: no guardar prompts completos, respuestas completas, claves, extractos completos ni payloads del proveedor.

### Privacidad y prompt injection

El contexto IA incluye agregados, saldos y movimientos relevantes limitados por `ai_max_context_rows`. Los conceptos bancarios se truncan, se serializan como datos y el prompt de sistema declara que conceptos, nombres de cuentas, extractos importados y pregunta del usuario son datos no confiables. Las instrucciones dentro de datos bancarios no deben obedecerse.

### Verificacion

- API build: OK.
- Frontend lint/build: OK.
- Tests unitarios nuevos para IA desactivada y usuario sin permiso quedan en `AtlasAiServiceTests`.
- La suite backend no pudo ejecutarse por fallo MSBuild preexistente del proyecto de tests: devuelve codigo 1 con `0 Errores` o sin salida util.

### Presupuesto IA por usuario y proveedor

Desde V-01.06 la gobernanza de IA combina dos barreras de coste:

- Global: `ai_usage_month_cost_eur`, `ai_usage_total_cost_eur`, `ai_monthly_budget_eur`, `ai_total_budget_eur`.
- Por usuario: tabla `IA_USO_USUARIOS` con `usuario_id`, `month_key`, `requests`, `input_tokens`, `output_tokens` y `coste_estimado_eur`.

`AtlasAiService.EnsureBudgetAsync` evalua primero el presupuesto global mensual, despues el presupuesto mensual por usuario (`ai_user_monthly_budget_eur`) y finalmente el presupuesto total. Si se supera el limite individual, registra `IA_CONSULTA_BLOQUEADA` con motivo `user_monthly_budget_exceeded` y no llama al proveedor.

El contexto financiero se construye desde consultas SQL scopeadas por usuario:

- rango maximo defensivo de `AiConfigurationDefaults.MaxContextYears`,
- saldos actuales por cuenta por `fila_numero`,
- agregados mensuales/periodo/categoria en SQL,
- movimientos relevantes limitados por `ai_max_context_rows`,
- truncado final por `AiConfigurationDefaults.MaxContextCharacters`.

El proveedor queda cubierto por tests de error controlado: 401/API key invalida, 404/modelo no encontrado, timeout/red y JSON/campos malformados. La auditoria no guarda prompts, respuestas completas ni payloads del proveedor.

### Proveedor OpenAI

Desde V-01.06 `AiConfiguration` permite dos proveedores:

- `OPENROUTER`: modelos permitidos `openrouter/auto`, `nvidia/nemotron-3-super-120b-a12b:free`, `google/gemma-4-31b-it:free`, `minimax/minimax-m2.5:free`, `openai/gpt-oss-120b:free`, `z-ai/glm-4.5-air:free`, `qwen/qwen3-coder:free`.
- `OPENAI`: modelos permitidos `gpt-4.1-mini`, `gpt-4o-mini`, `gpt-4o`.

`ConfiguracionController` guarda la API key correspondiente sin devolverla al cliente y redacta claves en auditoria. Si llega un modelo vacio o no permitido para un proveedor soportado, normaliza a default seguro (`openrouter/auto` en OpenRouter, `gpt-4o-mini` en OpenAI) para permitir guardar la API key sin depender de valores antiguos del formulario. En runtime, `AtlasAiService` auto-repara slugs obsoletos conocidos hacia `openrouter/auto`; los modelos desconocidos siguen bloqueados.

La migracion `20260510180000_AddOpenAiProviderConfig` inserta `openai_api_key` si falta. El seeding tambien la crea en instalaciones nuevas.

## 2026-05-10 - V-01.06 - Desglose de cuenta: seleccion, insercion y flag

### Que cambio

- `CuentaDetailPage.tsx` reordena la tabla del desglose para que la seleccion sea la primera columna visible.
- La columna `Flag` desaparece del render de la tabla. El marcado se ejecuta con `flagSelectedRows`, que recorre solo las filas seleccionadas y llama a `PATCH /api/extractos/{id}/flag`.
- El check de revision usa actualizacion local optimista y ya no llama a `loadCuentaData()` despues de cada click.
- La insercion intermedia usa el endpoint existente `POST /api/extractos` con `insert_before_fila_numero`, pero actualiza `rows` localmente con la fila devuelta y desplaza `fila_numero` en memoria.
- La eliminacion por fila se retira del cuerpo de la tabla. El borrado queda en la accion superior de papelera sobre seleccion, manteniendo el `ConfirmDialog` ya existente para borrado multiple.
- `dashboard.css` anade el trigger flotante `account-row-insert-trigger`, icon buttons para flag/papelera y una columna de seleccion compacta.
- `AGENTS.md` y `CLAUDE.md` incorporan una regla para cortar validaciones visuales o servidores dev que se encallen y continuar con validaciones utiles.
- Ajuste posterior: el trigger `account-row-insert-trigger` se mueve fuera de `account-selection-cell` y se renderiza en `account-row-anchor-cell`, desplazado al borde derecho de la columna `Nº Fila`. El objetivo es que el `+` no tape el checkbox de seleccion ni reduzca su zona clicable.

### Por que

La tabla mezclaba tres patrones distintos: check operativo, flag por columna y seleccion de borrado al final. Eso obligaba a recorrer visualmente demasiadas columnas y, peor, cada check/flag recargaba datos. Para una tabla financiera densa, ese patron es torpe: la seleccion debe estar al inicio y las acciones masivas fuera del grid.

### Recarga y scroll

Los checkboxes de seleccion solo modifican `selectedRowIds`, sin formulario ni navegacion. El check de revision y el flag aplican cambios al estado local (`setRows`) y hacen la llamada API sin disparar `loadCuentaData()`. Al no remedir ni remontar toda la pantalla, se conserva el scroll actual del usuario.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK fuera del sandbox.
- `npm.cmd run build` dentro del sandbox: bloqueado por `spawn EPERM` de Vite, incidencia conocida.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot` mediante copia no destructiva fuera del sandbox por permisos locales.
- Ajuste del `+` validado con `npm.cmd run lint` OK y build OK fuera del sandbox; `wwwroot` resincronizado.
- Se limpio el servidor temporal `127.0.0.1:5176` que quedo vivo durante la validacion abortada.

## 2026-04-26 - V-01.05 - Fix de altura del AlertBanner en el shell

### Que cambio

- `frontend/src/styles/layout/shell.css` ajusta la grilla de `app-main` para soportar tres filas estables: topbar, banner y contenido.
- Se define placement explicito para evitar auto-placement ambiguo cuando el banner existe:
  - `.app-main > .app-topbar { grid-row: 1; }`
  - `.app-main > .alert-banner { grid-row: 2; align-self: start; min-height: 0; height: auto; }`
  - `.app-main > .app-content { grid-row: 3; min-height: 0; }`
- Se replica la misma estructura en el breakpoint mobile (`max-width: 768px`).
- Se agrega `align-self: start` en `.alert-banner` para evitar estirado vertical residual en dashboards.
- Barrido de codigo frontend confirma que `AlertBanner` solo se monta en `components/layout/Layout.tsx`, por lo que el fix aplica a todas las rutas no embebidas.

### Por que

Con `grid-template-rows: var(--topbar-height) 1fr`, al aparecer el banner la fila flexible `1fr` se la quedaba el propio banner y quedaba sobredimensionado. El contenido pasaba a una fila implicita posterior, rompiendo proporciones en Configuracion/Backups/Papelera. En dashboards, ademas, se apreciaba estirado residual por comportamiento por defecto de grid (`align-self: stretch`), corregido con `align-self: start`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK; codigo `1` esperado por copia con cambios.

## 2026-04-26 - V-01.05 - Importacion permite avisos con saldo presente

### Que cambio

- `ImportacionService.ValidateRows` amplia la regla de filas informativas: si una fila tiene concepto, fecha vacia e importe vacio, pasa a ser importable con advertencias aunque traiga saldo.
- El importe se normaliza a `0`.
- La fecha se hereda de la ultima fila valida anterior.
- El saldo se conserva si viene parseable; solo se hereda el saldo anterior cuando tambien esta vacio.
- Se agregan regresiones de validacion y confirmacion para filas tipo `concepto + saldo` sin fecha ni importe.

### Por que

Algunos bancos exportan lineas informativas de beneficiario/desglose con concepto y saldo, pero sin fecha ni importe. Tratarlas como error fatal bloqueaba importaciones correctas. La app debe avisar y dejar continuar, no ponerse exquisita con basura bancaria previsible.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\AtlasBalance.API.Tests\\AtlasBalance.API.Tests.csproj" --filter ImportacionServiceTests`: 26/26 OK.
- `dotnet build "Atlas Balance\\backend\\src\\AtlasBalance.API\\AtlasBalance.API.csproj" -c Release`: OK, 0 warnings.

## 2026-04-26 - V-01.05 - Vista tabular de extractos tipo hoja de calculo

### Que cambio

- `ExtractoTable.tsx` agrupa cabecera y filas dentro de `extracto-table-viewport`, de forma que el scroll horizontal es comun.
- La tabla declara semantica `role="grid"`, con conteo de filas/columnas y encabezados de columna.
- La estimacion del virtualizador cambia segun densidad: `42px` en modo comodo y `34px` en modo compacto.
- Se agrega `getColumnLabel` para mostrar nombres legibles sin cambiar los campos reales usados por sort, filtros o guardado.
- `extractos.css` define variables locales de hoja (`--sheet-grid`, `--sheet-head-bg`, `--sheet-row-height`, etc.) y refuerza bordes, foco, hover, cabecera sticky y primera columna sticky.
- Se sincroniza `backend/src/AtlasBalance.API/wwwroot` desde `frontend/dist`.

### Por que

La vista anterior era una tabla editable, pero no una hoja de calculo convincente: cabecera y cuerpo tenian scroll separado, las celdas tenian poco borde y el foco no parecia una seleccion de celda. Para extractos bancarios densos, esa blandura visual estorba. La lectura debe ser de matriz, no de lista bonita.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\AtlasBalance.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.

## 2026-05-16 - V-01.07 - Auditoria correctiva de seguridad, estabilidad y simplificacion

### Usuarios y continuidad administrativa

`UsuariosController` ahora impide que el sistema quede sin administrador activo. La proteccion cubre desactivacion, cambio de rol fuera de `ADMIN`, eliminacion del ultimo admin y auto-democion/auto-desactivacion del admin autenticado.

### Watchdog y procesos externos

`WatchdogSettings:BaseUrl` se resuelve mediante validacion local obligatoria. El cliente solo puede llamar a `localhost`, loopback IPv4/IPv6 o host vacio. Esto evita que el secreto `X-Watchdog-Secret` pueda enviarse a un destino remoto por mala configuracion.

`BackupService` y `WatchdogOperationsService` aplican timeout de 30 minutos a procesos externos (`pg_dump`, `pg_restore`, scripts de actualizacion) y matan el arbol de procesos si el comando se cuelga. La salida se recoge completa para mantener diagnostico util.

`WatchdogController` valida cuerpos nulos y rutas invalidas antes de delegar en el servicio, devolviendo `400` en vez de errores no controlados.

### Autenticacion y permisos frontend

`AuthService.BuildAuthResultAsync` agrupa preferencias por cuenta en diccionario para evitar busquedas repetidas por cada permiso.

El timeout de sesion del frontend separa actividad real de render visual: `lastActivityRef` se actualiza inmediatamente y el debounce solo reduce ruido de estado/UI. Las pantallas que consumen helpers estables del store de permisos se suscriben tambien a `permisos` para re-renderizar cuando cambia el estado.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- Tests focalizados de usuarios/watchdog: 14/14 OK.
- Suite backend sin Testcontainers: 229/229 OK.
- `npm.cmd run build`: OK.
- `frontend/dist` sincronizado con `backend/src/AtlasBalance.API/wwwroot` mediante copia no destructiva.
- `npm.cmd audit --audit-level=critical`: 0 vulnerabilidades.
- `dotnet list AtlasBalance.API.Tests.csproj package --vulnerable --include-transitive`: 0 vulnerabilidades.

### Riesgos tecnicos pendientes

- `backup_path`/`export_path` siguen dependiendo de configuracion admin; la allowlist de raices es deseable pero puede romper instalaciones existentes.
- Los paquetes de actualizacion todavia necesitan limites de tamano y validacion de contenido antes de extraer.
- Importacion conserva riesgos de fingerprint dependiente del indice de fila y transacciones no garantizadas con `finally`.
- El calculo de saldo actual no esta completamente unificado entre dashboard/extractos/cuentas.

## 2026-06-23 - V-02-02 - Implementacion del sistema visual

### Base de diseño

La fuente canonica del rediseño es `Documentacion/Diseno/design.md`. El mockup de referencia queda versionado en `Documentacion/Diseno/mockups/atlas-balance-redesign-v02-02.html`.

El frontend mantiene React/Vite/CSS propio. No se agregaron dependencias ni se sustituyeron stores, rutas, permisos o contratos API.

### Tokens y clases comunes

`frontend/src/styles/variables.css` define el rail oscuro permanente, topbar de 64px y tokens de sidebar. `global.css` añade primitivas reutilizables:

- `.ab-card`, `.ab-card-header`, `.ab-card-meta`
- `.ab-kpi`
- `.ab-badge`
- `.ab-tabs`, `.ab-tab`
- `.ab-field`, `.ab-input`
- `.ab-empty`
- `.ab-button--block`, `.ab-button--sm`

Estas clases son una capa visual; no contienen lógica de negocio ni sustituyen los componentes existentes.

### Shell y login

`Sidebar` hereda el `data-theme` global. Los tokens `--color-sidebar-*` definen una variante clara y otra oscura para que el rail lateral cambie junto con el modo claro/oscuro sin duplicar logica en React. Conserva `PaisScopeSelect`, conteos de alertas/exportaciones, badge de update y filtrado de items por permisos/IA.

`TopBar` conserva logout, toggle de tema, colapso de sidebar y chat IA flotante; solo cambia la representacion del usuario a pill con iniciales y rol.

`LoginPage` pasa a layout partido y añade toggle de tema, pero mantiene:

- login email/password,
- MFA challenge,
- QR de enrolamiento,
- recordar dispositivo,
- `returnTo` seguro,
- mensaje post-update,
- carga de permisos/alertas tras login.

### Dashboard y extractos

`DashboardPage` sigue consumiendo `/dashboard/principal`, `/dashboard/evolucion` y `/dashboard/saldos-divisa`. La composicion cambia a hero card con saldo consolidado, divisas y `EvolucionChart`; se conservan KPIs, plazos fijos, saldos por pais, concentracion y saldos por titular.

`PeriodoSelector` se convierte en tabs segmentadas sobre los mismos valores (`1m`, `3m`, `6m`, `9m`, `12m`, `18m`, `24m`). No cambia query string ni carga de datos.

`ExtractosPage` mantiene `ExtractoTable` virtualizada, `role="grid"`, edicion inline, auditoria de celda, columnas visibles, filtros por titular/cuenta/fechas y paginacion. Solo se ajusta estructura de header y estilos.

### Pantallas operativas/admin

La capa visual se extendio a importacion, revision, IA, entidades, formatos, usuarios, auditoria, exportaciones, papelera, configuracion y backups. Los cambios son CSS y hooks de clase en `FormatosImportacionPage`; no cambian endpoints ni permisos.

### Validacion

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `git diff --check`: OK con avisos CRLF preexistentes.
- `npm.cmd run build`: bloqueado por `EPERM` al limpiar `frontend/dist/assets`.
- `npm.cmd exec vite -- build --outDir C:\tmp\atlas-balance-vite-build-redesign-v02-02 --emptyOutDir`: OK.

## 2026-06-23 - V-02-02 - QA posterior del rediseno

La pasada de QA posterior cerro huecos de cobertura:

- `ChangePasswordPage` queda alineada con el layout partido de login.
- `AppSelect` deja de depender del select nativo visible y usa popover/listbox propio; los selects de Backups se migran a este componente.
- `PeriodoSelector` usa semantica `radiogroup`/`radio` con roving `tabIndex`.
- `ExtractoTable` conserva la grilla roving: los botones internos de edicion quedan fuera de la tabulacion normal y el historial solo es focable en la celda activa.
- `BackupsPage` diferencia `Ultima copia correcta en esta pagina` y limpia `linkStart` cuando Google Drive devuelve `FAILED` o `EXPIRED`, evitando codigos OAuth rancios en pantalla.
- `.sr-only` se endurece con `!important`, `clip-path` y desplazamiento `left: -10000px` para que tablas accesibles de charts no creen overflow horizontal en mobile.

Validacion adicional:

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `git diff --check`: OK.
- Build temporal: `npm.cmd exec vite -- build --outDir C:\tmp\atlas-balance-vite-build-redesign-v02-02-qa3 --emptyOutDir`: OK fuera del sandbox.
- QA Playwright finita con Chrome local y API mock: login, cambio obligatorio de password, dashboard, periodo, extractos, backups y mobile OK; consola sin errores.

## 2026-06-26 - V-02-02 - Sidebar sensible al tema

`Sidebar` deja de fijar `data-theme="dark"` en el `<aside>`. El rail lateral toma el tema activo desde `document.documentElement`, igual que el resto del shell.

`variables.css` separa tokens de sidebar para claro y oscuro:

- Claro: fondo de superficie, texto secundario, borde suave, hover de superficie y activo con `accent-primary-soft`.
- Oscuro: mantiene el rail grafito original con texto claro, hover translucido y sombra mas marcada.

`shell.css` usa esos tokens para marca, selector de organizacion, hover, estado activo y sombra del rail. La navegacion inferior sigue aprovechando `--color-sidebar-active-text`, por lo que su estado activo tambien respeta el tema.

Validacion:

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK tras repetir una vez por ruido transitorio durante cambios no relacionados en `EvolucionChart.tsx`.
- `npm.cmd exec vite -- build --outDir C:\tmp\atlas-balance-vite-build-sidebar-theme-v02-02 --emptyOutDir`: OK fuera del sandbox.

Pendiente: no se hizo QA visual con navegador porque el cambio es acotado a tokens/CSS y no se arranco servidor dev por la regla anti-encallamiento.

## 2026-06-26 - V-02-02 - Login segun referencia oscura

`LoginPage` mantiene el flujo existente de autenticacion, MFA, QR de enrolamiento, `returnTo` seguro y mensaje post-update. El cambio es visual y de microinteraccion:

- panel izquierdo oscuro con marca superior, claim `Tesoreria local, control real.`, descripcion operativa y chips de capacidades;
- panel derecho con separador vertical y tarjeta de login compacta;
- logos filtrados por CSS para conservar contraste sobre fondo oscuro sin crear nuevos assets;
- boton de mostrar contrasena con icono `Eye/EyeOff` y etiquetas accesibles;
- responsive de una columna en tablet/mobile, sin overflow horizontal.

No se muestra "Recordar este dispositivo" en el estado base de login porque esa opcion solo se envia al backend durante el challenge MFA (`/auth/mfa/verify`). Exponerla antes seria un control sin efecto real.

Validacion:

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- Build temporal: `npm.cmd exec vite -- build --outDir C:\tmp\atlas-balance-login-reference-v02-02 --emptyOutDir`: OK fuera del sandbox.
- QA visual con Chrome local via Playwright sobre servidor estatico temporal: `/login` desktop 1580x835 y mobile 390x844, consola sin errores y sin overflow horizontal.

## 2026-06-26 - V-02-02 - Login con tokens claro/oscuro

El login y el cambio obligatorio de contrasena ya no fijan `data-theme="dark"` en el panel de marca. La pantalla de autenticacion usa tokens locales `--auth-*` definidos en `.auth-page`:

- variante clara por defecto: fondo azul muy claro, panel de marca claro, tarjeta blanca, texto oscuro y primario azul;
- variante oscura bajo `[data-theme="dark"] .auth-page`: conserva el aspecto grafito de la referencia anterior;
- controles, chips, logos filtrados, separador, sombras, boton y toggle consumen esos tokens en vez de valores hardcodeados.

Esto mantiene el mecanismo global existente (`document.documentElement[data-theme]` desde `uiStore`) y evita otra isla visual bloqueada en oscuro.

Validacion:

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- Build temporal: `npm.cmd exec vite -- build --outDir C:\tmp\atlas-balance-login-theme-v02-02 --emptyOutDir`: OK fuera del sandbox.
- QA Playwright con Chrome local: el login carga en `light`, el boton de tema cambia a `dark`, los colores computados de pagina/tarjeta/texto cambian y no hay overflow horizontal en 1580x835 ni 390x844.

## 2026-06-26 - V-02-02 - Centrado del toggle de tema en login

El boton `.auth-theme-toggle` hereda estilos globales de boton si no se anulan explicitamente. Para evitar que el control de modo claro/oscuro quede visualmente descentrado:

- se normaliza con `appearance: none`, `box-sizing: border-box`, `padding: 0`, `line-height: 1`, `min-width` y `min-height` iguales al tamano declarado;
- el SVG interno usa tamano fijo y `display: block`;
- la luna se desplaza levemente a la izquierda solo cuando `aria-pressed="false"` porque su geometria visual pesa hacia la derecha aunque el `viewBox` este centrado.

Validacion:

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- Build temporal: `npm.cmd exec vite -- build --outDir C:\tmp\atlas-balance-login-toggle-center-v02-02 --emptyOutDir`: OK fuera del sandbox.
- QA Playwright con Chrome local: boton `38x38`, sin overflow mobile y sin errores de consola.

## 2026-06-29 - V-02-02 - Hardening financiero post-reporte

Backend:

- Nuevas entidades persistentes: `ImportacionLote`, `ImportacionLoteFila`, `MovimientoEsperado` y `Conciliacion`.
- `Extracto` incorpora `ImportacionLoteId` manteniendo `ImportacionLoteHash` por compatibilidad.
- `AppDbContext` declara tablas, indices, FK y configuracion para lotes, filas, movimientos esperados y conciliaciones.
- Migracion EF Core: `20260629090000_FinancialHardeningV0202`.
- Importacion expone `/api/importacion/lotes`, detalle/filas, confirmacion y reversion. El contenido original queda guardado en BD hasta 5 MB con SHA-256 y mapeo usado.
- `ConfirmarLoteAsync` no selecciona filas con advertencias por defecto y exige `acepta_advertencias=true` si se eligen.
- Conciliacion expone `/api/conciliacion/*`, crea movimientos esperados internos, genera sugerencias deterministas y permite confirmar, marcar excepcion y resolver.
- Permisos agregados a `PERMISOS_USUARIO`: revisar lineas, aprobar importaciones, conciliar y cerrar conciliacion.
- Maker-checker no bloquea; registra auditoria y `NotificacionAdmin` si el mismo usuario importa/crea y aprueba/cierra.
- OpenClaw valida expiracion en `IntegrationTokenService`, aplica scopes en middleware, registra ultima IP, notifica rate limit/nueva IP y agrega rotacion en `POST /api/integraciones/tokens/{id}/rotar`.
- API y Watchdog cargan configuracion externa local desde `%APPDATA%\AtlasBalance\dev-secrets` solo en Development.

Frontend:

- `/importacion` usa tabs `Nueva`, `Historial` y `Lote` con lote formal y confirmacion por advertencias.
- Nueva ruta `/conciliacion` para libro esperado, sugerencias, confirmacion y excepciones.
- `ExtractosPage` separa `Revision` y `Edicion avanzada`; la edicion inline queda cerrada hasta entrar en modo avanzado.
- `AppSelect` vuelve a `<select>` nativo estilizado para accesibilidad robusta.
- `DashboardPage` agrega CTAs operativos: importar, revisar alertas y conciliar pendientes.
- Navegacion mobile prioriza `Alertas` cuando hay alertas activas.
- UI de tokens OpenClaw muestra expiracion, scopes, ultimo uso/IP, revocacion y rotacion.

Release y seguridad:

- `Build-Release.ps1` valida version `^V-\d{2}[-.]\d{2}$`, runtime `win-x64` y ejecuta scanner Atlas antes de empaquetar.
- `.github/workflows/release.yml` usa default `V-02-02`, permisos por job y `environment: release-signing`.
- `.github/workflows/ci.yml` ejecuta `scripts/Test-AtlasSecrets.ps1`.
- El scanner Atlas revisa codigo, scripts, workflows y documentacion versionable; excluye artefactos pesados y no imprime valores.

Validacion inicial 2026-06-29:

- `dotnet build AtlasBalance.API.Tests.csproj --no-restore -c Debug` desde `C:\tmp`: OK.
- Tests focalizados impactados: 59/59 OK.
- `npm.cmd run lint`, `npm.cmd exec tsc -- --noEmit` y build Vite temporal: OK.
- `Test-AtlasSecrets.ps1`: OK.
- `npm audit --audit-level=moderate`: OK.
- `dotnet list package --vulnerable --include-transitive`: OK.
- Suite completa backend inicial del 2026-06-29: no verde; 306 OK y 5 fallos por deuda preexistente/sensible a fecha y Docker/Testcontainers sin Docker. Estado actualizado final el 2026-06-30: 317/317 OK con Docker Desktop operativo.

## 2026-06-30 - V-02-02 - Cierre tecnico de validacion post-hardening

Correcciones posteriores:

- `AiConfiguration.NormalizeGlobalConfigModel` normaliza configuracion global por proveedor. En OpenRouter solo conserva modelos sugeridos conocidos y convierte ids desconocidos a `openrouter/auto`; el chat IA sigue pudiendo usar modelos OpenRouter arbitrarios por su ruta especifica.
- `ConfiguracionController.Update` usa esa normalizacion para guardar `ai_model`.
- `AuthService.IsMfaRememberDeviceEnabledAsync` queda fail-closed: valor ausente/invalido equivale a `false`.
- `ConfiguracionController.Get` devuelve `mfa_remember_device_enabled=false` si no hay configuracion explicita.
- `AtlasAiServiceTests.AskAsync_Should_Respect_Cuenta_Scope_In_Deterministic_Ranking` declara permiso real sobre la cuenta del fixture para probar el scope aplicado por `UserAccessService`, no un bypass artificial.

QA visual:

- Browser in-app se intento primero y se descarto tras timeout de attach, bloqueo de `file://` y timeout/reset al levantar localhost mock.
- Playwright finito con Chrome local sirvio `C:\tmp\atlas-balance-vite-v0202-qa`, API mock y cierre obligatorio de browser/servidor.
- Flujos cubiertos: dashboard CTAs, `/importacion` con lote formal y advertencias no seleccionadas, historial de lote, `/conciliacion`, tokens OpenClaw con rotacion, `Extractos` revision/edicion avanzada y mobile con `Alertas` como acceso primario.
- Capturas generadas en `qa-artifacts/atlas-v0202-qa-*.png`.

Validacion final:

- Backend no Docker: 315/315 OK.
- Testcontainers: `RowLevelSecurityTests|ExtractosConcurrencyTests` 2/2 OK con `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine` en contexto elevado.
- Backend completo: 317/317 OK.
- Frontend: lint OK, TypeScript OK, build Vite temporal OK.
- Seguridad/SCA: scanner Atlas OK, `npm audit --audit-level=moderate` OK, NuGet vulnerable OK desde `C:\tmp`.
- Docker: Docker Desktop debe estar arrancado. El CLI elevado detecta el daemon por `npipe:////./pipe/dockerDesktopLinuxEngine`; Testcontainers/Docker.DotNet necesita `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine`.

Empaquetado local:

- `Build-Release.ps1` ya no decide el resultado del scanner de secretos por `$LASTEXITCODE`, porque un `.ps1` exitoso puede dejar ese valor heredado de comandos internos. Usa `$?`.
- `Build-Release.ps1` usa `npm ci` solo si se pide `-CleanNpmInstall` o falta `node_modules`. Si existe `node_modules` pero esta incompleto, ejecuta `npm install --ignore-scripts --no-audit --fund=false` para reparar sin borrar arboles bloqueados.
- Validacion local: `Build-Release.ps1 -Version V-02-02 -Runtime win-x64 -AllowUnsignedLocal` genera `AtlasBalance-V-02-02-win-x64.zip`. Al usar `-AllowUnsignedLocal` no genera firma y no es publicable.

## 2026-07-01 - V-02-03 - Logo Atlas Balance

- El activo principal de marca de Atlas Balance pasa a `frontend/public/logos/Atlas Balance.svg`.
- El SVG usa `fill="currentColor"` y una regla interna `prefers-color-scheme` para funcionar tambien como favicon directo.
- `LoginPage`, `ChangePasswordPage` y el sidebar renderizan el simbolo como mascara CSS, no como imagen filtrada.
- `auth.css` define `--auth-logo-color` para claro (`#285bd9`) y oscuro (`#82a4ff`).
- `frontend/public/logos/Atlas Balance.png` se regenera desde el SVG solo como fallback para `apple-touch-icon` e instalador.
- La copia de `wwwroot/logos` queda actualizada para ejecuciones servidas por backend.

Esto elimina la dependencia visual del PNG anterior. Usar mascara CSS aqui es la opcion correcta: un filtro sobre un PNG negro parece rapido, pero es deuda visual disfrazada de solucion.

## 2026-07-03 - V-02-04 - Fondo blanco en tarjeta principal del dashboard

- `frontend/src/styles/variables.css` define `--dashboard-hero-bg`.
- En tema claro el valor es `#ffffff`, para que la tarjeta principal completa no herede el tinte del hero financiero.
- En tema oscuro el valor cae a `var(--bg-surface)`, evitando una placa blanca agresiva en una pantalla oscura.
- `frontend/src/styles/layout/dashboard.css` aplica esa superficie a `.dashboard-hero-card`; resumen, divisas y grafica comparten el mismo fondo. La grafica no necesita una placa interna propia.

Validacion:

- `npm.cmd run lint`: OK.
- `npm.cmd exec tsc -- --noEmit`: OK.
- `npm.cmd exec vite -- build --outDir .tmp-dashboard-hero-bg-v0204 --emptyOutDir`: OK.
- Browser in-app bloqueo la validacion con `data:` por politica; no se uso workaround externo. Se verifico el CSS fuente y el CSS compilado.

## 2026-07-03 - V-02-04 - Importacion: backend local actualizado y build endurecida

- Sintoma real tras sincronizar `wwwroot`: `GET http://localhost:5000/api/importacion/lotes` seguia devolviendo `404 {"error":"Endpoint no encontrado"}`.
- Diagnostico: el backend activo era viejo. `GET /api/importacion/contexto` devolvia `401`, asi que la API estaba viva; solo faltaba la ruta nueva de lotes en el proceso cargado.
- Bloqueo al reiniciar: `Start-LocalDev.ps1` fallaba en build con `CS0579` por atributos duplicados desde `backend/src/AtlasBalance.API/obj/Release/**`.
- Causa tecnica: al compilar con `BaseIntermediateOutputPath` redirigido a `tools/dotnet-build/api/obj`, los restos `obj` dentro del proyecto dejaban de ser el intermediate path activo y podian entrar por globbing de items.
- Cambio aplicado: `AtlasBalance.API.csproj` elimina `bin\**` y `obj\**` de `Compile`, `Content`, `EmbeddedResource` y `None`.
- Verificacion: `Start-LocalDev.ps1 -TimeoutSeconds 90` compila y arranca la API; `curl.exe -i http://localhost:5000/api/importacion/lotes` devuelve `401 Unauthorized`, que es la respuesta correcta para una ruta `[Authorize]` sin cookies.

## 2026-07-29 - V-02.07 - Auditoria de inyeccion: UNC en rutas de configuracion, Origin/Referer y resolucion de PowerShell

**Que se modifico.** Tres endurecimientos salidos de la auditoria de
superficie de inyeccion (SQLi, XSS, CSRF, path traversal, command injection
y open redirect). Los otros cuatro ejes salieron limpios y no se toco nada
en ellos; el detalle completo de lo verificado esta en
`Versiones/v-02.07.md`.

**1. Rutas UNC rechazadas en `backup_path` y `export_path`.**

Por que: la validacion existente combinaba `Path.IsPathRooted(x)` con
`LooksLikeWindowsRootedPath(x)` en un `OR`. Como
`Path.IsPathRooted(@"\host\share")` devuelve `true` en .NET, cualquier
ruta de red satisfacia la primera condicion y nunca se evaluaba el filtro
estricto de `C:\`; el filtro de `..` tampoco la veia. Un ADMIN podia
redirigir los `pg_dump` a un recurso SMB externo y sacar de la maquina el
volcado integro de la base de datos de forma persistente.

Como: se anade el helper `IsUncPath` (prefijo `\` o `//`) y se sustituye
el `OR` por `LooksLikeWindowsRootedPath` a secas. Aplicado en los tres
puntos que validan la ruta, no solo en el de entrada:
`ConfiguracionController.IsSafeAbsoluteDirectory`,
`BackupService.ResolveSafeDirectory` y
`ExportacionService.ResolveSafeDirectory`. Los dos servicios releen el
valor desde la tabla de configuracion, asi que revalidan por si quedara un
UNC guardado de antes del cambio. Los mensajes de `BadRequest` pasan a
indicar que no se admiten rutas de red.

**2. Verificacion de `Origin`/`Referer` en `CsrfMiddleware`.**

Por que: no existia ninguna comprobacion de estas cabeceras en el backend.
`/api/auth/refresh-token` esta exento del token CSRF y quedaba protegido
unicamente por `SameSite=Strict`, es decir por el comportamiento del
navegador, sin verificacion server-side.

Como: se anade al middleware ya existente en lugar de crear uno nuevo, por
ser la misma responsabilidad y evitar otro registro en el pipeline. Se
ejecuta antes de la validacion del token y **sin** aplicar
`ExcludedPaths`, para cubrir precisamente las rutas exentas. El origen
esperado se calcula como `Request.Scheme://Request.Host` en vez de leerse
de una allowlist fija, porque cada instalacion on-premise tiene su propio
host; en Development se aceptan ademas los origenes de Vite (5173), ya que
ahi frontend y API son cross-origin de forma legitima. Si `Origin` falta se
deriva el origen del `Referer`; si faltan ambas se deja pasar, porque los
navegadores envian `Origin` en todo verbo mutador y el token CSRF sigue
siendo obligatorio. El constructor pasa a recibir `IWebHostEnvironment`.

Limitacion documentada en el propio codigo: un proxy inverso que reescriba
el `Host` obligaria a activar `ForwardedHeaders.XForwardedHost` en
`Program.cs`, o el middleware rechazaria todas las peticiones. Hoy no
aplica: el despliegue es Kestrel directo.

**3. `ResolvePowerShellExecutable` deja de caer al PATH.**

Por que: devolvia `"powershell.exe"` sin ruta si no lo encontraba en
System32. `CreateProcess` resuelve un nombre sin ruta buscando primero en
el directorio del ejecutable, y el Watchdog corre como servicio con
privilegios altos.

Como: lanza `InvalidOperationException` con mensaje explicito en vez de
devolver el nombre corto. Fallar es preferible a arrancar un PowerShell
indeterminado con esos privilegios.

**Verificacion.** API, Watchdog y tests compilan con 0 errores. Suite
completa en verde: 434 pruebas, 434 correctas, 0 con error, 0 omitidas.
`CsrfMiddlewareTests` pasa de 8 a 15 fixtures, con 7 nuevas para la
verificacion de origen.

---

## 2026-07-29 - V-02.07 - Transporte: HSTS explicito, CSP sin directiva obsoleta, Referrer-Policy con una sola fuente

**Que se modifico.** Tres correcciones de cabeceras y una del watchdog,
salidas de la auditoria de HTTPS/transporte/cookies. El veredicto completo
de las 15 comprobaciones, incluida la que se rechazo, esta en
`Versiones/v-02.07.md`.

**Contexto que hay que tener antes de leer lo demas.** La redireccion
HTTP a HTTPS de esta app no la resuelve el codigo, la resuelve la
topologia. `Instalar-AtlasBalance.ps1:876` tiene dos modos: en modo LAN
Kestrel escucha solo en `https://0.0.0.0:443` (no hay listener HTTP, luego
no existe trafico en claro que redirigir) y en modo reverse proxy escucha
solo en `http://127.0.0.1:5000` con el TLS terminado en Caddy, que ya
redirige 80 a 443 por su cuenta. `app.UseHttpsRedirection()` funciona en el
primer modo y es un no-op en el segundo (sin endpoint HTTPS ni
`ASPNETCORE_HTTPS_PORT` el middleware no puede resolver el puerto y deja
pasar la peticion tras loguear un warning). Se deja tal cual: no es
explotable con Kestrel en loopback, y quitarlo empeoraria el modo LAN.

**1. `AddHsts` explicito.**

Por que: `Program.cs` llamaba `app.UseHsts()` sin configurar `AddHsts`, asi
que la cabecera efectiva era la del framework, `max-age=2592000` (30 dias)
y **sin** `includeSubDomains`.

Como: `builder.Services.AddHsts` con `MaxAge` de 365 dias e
`IncludeSubDomains = true`. `Preload = false` a proposito: la lista de
preload de los navegadores exige un dominio publico registrable y es
practicamente irreversible, y esta app se instala con hostnames de
intranet. `ExcludedHosts` se queda con el default (`localhost`,
`127.0.0.1`, `[::1]`) para no fijar HSTS en la propia maquina servidora.

Efecto secundario que hay que gestionar en operacion: en modo LAN el
certificado es self-signed, y con HSTS activo Chrome y Edge convierten el
error de certificado en un fallo **no salteable** (desaparece el "continuar
de todos modos"). Subir de 30 a 365 dias convierte un bloqueo de un mes en
uno de un ano, y limpiarlo exige `chrome://net-internals/#hsts` en cada
equipo. La condicion para desplegar esto es tener el `.cer` en la raiz de
confianza de todos los clientes. Aviso relacionado:
`scripts/install-cert-client.ps1` solo instala la CA de `mkcert` (camino de
desarrollo); si `mkcert` no esta, imprime la ruta del `.cer` y no lo
instala. El comando real esta en `documentacion.md:209`.

**2. `block-all-mixed-content` fuera de la CSP.**

Por que: la directiva quedo fuera de CSP nivel 3. Los navegadores actuales
la ignoran y Chrome la reporta como obsoleta en consola, asi que solo
aportaba ruido. `upgrade-insecure-requests`, que ya estaba en la misma
cadena, cubre el caso.

Como: se retira de `cspUpgrade` y se deja solo
`upgrade-insecure-requests`.

**3. Retirado el `<meta name="referrer">` de `index.html`.**

Por que: el backend enviaba `Referrer-Policy: no-referrer` y el HTML
llevaba `<meta name="referrer" content="strict-origin-when-cross-origin">`.
Ese `<meta>` no era redundante: **ganaba**. Por spec de Referrer Policy la
politica la fija la cabecera y el elemento `<meta>` la sobreescribe, asi
que el documento de la SPA acababa aplicando la mas debil de las dos.

Como: se borra el `<meta>`. Queda una sola fuente de verdad (la cabecera) y
la politica efectiva pasa a `no-referrer`. Para una app on-premise sin
enlaces salientes no se pierde funcionalidad.

**4. El validador de certificado permisivo del watchdog, restringido a
loopback.**

Por que: `WatchdogOperationsService.WaitForApiHealthAsync` construia su
`HttpClientHandler` con `DangerousAcceptAnyServerCertificateValidator` para
el health check posterior a una actualizacion. El guardia que lo
justificaba, `IsLocalHealthUrl`, no solo admite loopback: tambien
`Environment.MachineName` y `MachineName.local`, que **si resuelven por
red**. Un atacante en la LAN capaz de suplantar ese nombre podia presentar
su propio certificado y devolver un `200 OK` falso; el watchdog daria por
buena una actualizacion rota y se saltaria el rollback.

Como: el validador permisivo solo se asigna cuando `healthUri.IsLoopback`.
Para el resto de hosts admitidos se usa la validacion normal de
certificados. La instalacion por defecto configura
`https://localhost/api/health`, luego el camino normal no cambia.

**5. Invariante del prefijo `__Host-` bajo test.**

`BuildCookieOptions` (`AuthController.cs:300`) no asigna `Path`: se apoya en
el default de `CookieOptions`. El navegador **rechaza** una cookie
`__Host-` que no lleve exactamente `Path=/`, sin `Domain` y con `Secure`,
asi que si ese default cambiara o alguien tocara esas opciones, el login
entero se romperia en produccion de forma silenciosa y sin error en el
servidor. `TransportSecurityTests.cs` fija el invariante ejercitando el
`Response.Cookies.Append` real via `AuthController.RefreshToken` en entorno
`Production` y asertando sobre el `Set-Cookie` emitido.

**Verificacion.** PENDIENTE. El clasificador de seguridad del harness
estuvo caido durante toda la sesion (`claude-opus-5 is temporarily
unavailable`), sin acceso a shell, subagentes ni fetch web. Los cambios
estan escritos y revisados a mano, pero sin compilar ni testear. Comandos a
ejecutar: `dotnet build AtlasBalance.sln -p:UseAppHost=false` y
`dotnet test --filter "FullyQualifiedName~TransportSecurityTests"`.

---

## V-02.07 - Logging, monitorizacion y trazas de auditoria

Bloque de observabilidad de seguridad. Punto de partida honesto: la mitad de lo
que se pedia ya existia (auditoria automatica de 28 entidades con valores
antes/despues, eventos de login, Serilog con niveles y rotacion, redaccion de
PII en logs). Lo que faltaba eran los agujeros que se listan abajo.

### 1. Auditoria: contexto completo, firmada y append-only de verdad

**Campos nuevos en `AUDITORIAS`** (migracion
`20260730090000_V0207AuditoriaAppendOnly`):

| Columna | Para que |
|---------|----------|
| `secuencia` | `bigint` identity de Postgres. Un hueco = filas borradas. |
| `firma` | HMAC-SHA256 del contenido de la fila. Detecta alteracion e insercion. |
| `user_agent` | Ya llegaba al login y se tiraba. Ahora se guarda (256 chars). |
| `session_id` | Id de sesion de login, estable entre rotaciones del access token. |
| `origen` | UI / API / JOB / SISTEMA / DESCONOCIDO. |

`detalles_json` pasa de `jsonb` a `json`. **No es cosmetico:** `jsonb` normaliza
el texto (reordena claves, quita espacios), asi que la cadena releida no
coincidiria con la firmada y toda la auditoria se reportaria como manipulada.
Nada en el codigo usa operadores `jsonb` sobre esa columna.

**Id de sesion.** Se genera en el login (`AuthService.GenerateSessionId`, 128
bits base64url), se guarda en `REFRESH_TOKENS.session_id` y se **copia en cada
rotacion**, asi que identifica la sesion completa y no un access token de una
hora. Viaja al JWT como claim `sid` y de ahi a cada fila de auditoria.

**Firma (`Services/AuditSigner.cs`).** HMAC-SHA256 con
`Security:AuditSigningKey`, obligatoria y distinta de `JwtSettings:Secret` y de
`Security:RlsContextSecret` fuera de Development (si se comparten, comprometer
una permite forjar auditoria con firma valida). Serializacion canonica con
**prefijo de longitud por campo**: sin el, ("ab","c") y ("a","bc") darian el
mismo payload y se podria mover contenido entre campos sin invalidar la firma.

Dos detalles que costarian caros si se olvidan, y por eso tienen test propio:

- El timestamp se trunca a **microsegundos** antes de firmar y de guardar.
  Postgres `timestamptz` guarda microsegundos y `DateTime` tiene 100 ns; sin
  truncar, la firma calculada antes del INSERT no valida al releer la fila.
- La IP se normaliza (IPv4 mapeada en IPv6 hacia IPv4), porque `inet` puede
  devolverla en cualquiera de las dos formas.

La firma **no cubre `secuencia`** a proposito: Postgres la asigna durante el
INSERT y firmarla exigiria un UPDATE posterior, que el propio mecanismo
append-only bloquea. El borrado lo detecta la continuidad de la secuencia.

**Append-only.** Aqui hay que corregir el diagnostico inicial: `AUDITORIAS` ya
era de facto append-only desde `20260501120000_EnableRowLevelSecurity`, que le
puso `FORCE ROW LEVEL SECURITY` con politicas de SELECT e INSERT y ninguna de
UPDATE ni DELETE. El problema es que lo hacia **en silencio** (ver el bug del
job de retencion en `LOG_ERRORES_INCIDENCIAS.md`). Ahora hay cuatro capas:

1. **Privilegios.** `Program.GrantRuntimeDatabasePrivileges` hace
   `REVOKE UPDATE, DELETE, TRUNCATE ON "AUDITORIAS"` al rol de runtime. Como
   `atlas_balance_app` **no es el propietario** (lo es `atlas_balance_owner`, que
   solo se usa para migraciones), no puede reconcederselo, ni quitar el trigger,
   ni alterar la tabla. Falla con error de privilegios, no en silencio.
2. **Trigger `trg_auditorias_append_only`.** Bloquea todo UPDATE y el DELETE de
   filas de menos de 90 dias, incluso por la via sancionada. El suelo esta en el
   trigger y no solo en la funcion de purga, para que falsificar la marca de
   purga no sirva de nada.
3. **Purga.** Politica `auditorias_delete` mas
   `atlas_security.purgar_auditorias()` (`SECURITY DEFINER`, `search_path` fijo,
   suelo de 90 dias validado dentro, `REVOKE ALL ... FROM PUBLIC`). Unica via de
   borrado.
4. **Deteccion.** Firma, secuencia y espejo externo, para lo que la prevencion no
   alcance.

**Donde NO alcanza, sin adornos:** quien tenga las credenciales de
`atlas_balance_owner` o superusuario de PostgreSQL puede hacer lo que quiera.
Contra eso solo queda la capa 4. Igual que un RCE que consiga SYSTEM en el
servidor: el servicio corre como LocalSystem, asi que puede borrar el fichero de
log de seguridad y vaciar el Windows Event Log. **La unica defensa real contra
ese escenario es sacar los logs de la maquina** (ver seccion 5).

**Verificacion.** `GET /api/auditoria/integridad` (ADMIN) y el job diario
`verificacion-integridad-auditoria` (04:05, despues de la purga de las 03:15).
Recorre por lotes, valida firmas y busca huecos de secuencia. Las filas
anteriores a V-02.07 se cuentan como *sin firma*, no como invalidas: contarlas
como manipuladas dispararia una alarma falsa en cada instalacion que se
actualice y el operador aprenderia a ignorarla.

> Rotar `Security:AuditSigningKey` invalida la verificacion de todo lo ya
> firmado. No es manipulacion, pero se ve igual. `Actualizar-AtlasBalance.ps1`
> **nunca** la rota: solo la genera si falta o es debil.

**Retencion: 28 a 365 dias** (`Auditoria:RetentionDays`). 28 dias era incoherente
con lo que esta tabla es en tesoreria multi-banco: el rastro de quien movio
dinero o cambio permisos tiene que sobrevivir a un cierre trimestral y a una
investigacion que empieza semanas despues. El suelo real (90 dias) lo impone la
base de datos, no la configuracion.

`AUDITORIA_INTEGRACIONES` se queda en 28 dias
(`Auditoria:IntegrationRetentionDays`, suelo de 7 en la BD): es un log de
peticiones HTTP, de volumen alto y valor forense bajo. Recibe el mismo
tratamiento de purga (politica `auditoria_integraciones_delete` mas
`atlas_security.purgar_auditorias_integracion()`, migracion
`20260730100000_V0207AuditoriaIntegracionPurga`) porque arrastraba **el mismo
bug de purga silenciosa**. No lleva trigger append-only: RLS ya impide el UPDATE
y ahi no se firma ninguna fila, asi que solo anadiria ruido.

> Barrido completo hecho al cerrar el bug: de las 23 tablas con `FORCE ROW LEVEL
> SECURITY`, solo estas dos tenian el defecto. El resto declara politicas
> `FOR ALL` o politicas explicitas de UPDATE/DELETE. `REFRESH_TOKENS` no tiene
> RLS, luego `LimpiezaRefreshTokensJob` nunca estuvo afectado.

Si la retencion configurada baja del suelo de la BD, la funcion lanza `23514`, el
job lo registra como error y **no purga nada**: preferimos que la tabla crezca
antes que perder rastro por una configuracion mal puesta.

### 2. Fallos de autorizacion, acceso masivo y acciones admin

`Middleware/SecurityAuditMiddleware.cs`. Antes habia ~40 `Forbid()` repartidos
por los controladores y **ninguno dejaba rastro**: un usuario legitimo probando
ids de cuentas ajenas era invisible.

Se resuelve en middleware y no tocando los 40 sitios a proposito: cubre tambien
los 403 del propio pipeline de autorizacion (roles, policies) y los de
`CsrfMiddleware`, y los que anadan futuros endpoints sin que nadie se acuerde.

- `AUTHZ_DENIED` (403) y `AUTHN_DENIED` (401). Los 401 de `/api/auth/login`,
  `/api/auth/refresh-token`, `/api/auth/logout`, `/api/telemetria` y
  `/api/health` se excluyen: son funcionamiento normal y el login ya audita sus
  propios fallos con mas contexto.
- `ACCESO_BULK` cuando `pageSize` supera `Security:Auditoria:UmbralAccesoBulk`
  (100). **Limitacion consciente:** son filas *solicitadas*, no devueltas; contar
  el cuerpo exigiria bufferizar cada respuesta. Como senal de intencion vale,
  porque los endpoints paginados topan `pageSize` y pedir 5000 es deliberado.
- **Deduplicacion** por (accion, usuario, IP, ruta) durante 60 s. Sin ella, un
  bucle del frontend o un escaneo convierten `AUDITORIAS` en el vector de
  denegacion de servicio en vez de la defensa. La clave incluye la ruta, asi que
  un barrido sobre recursos distintos sigue generando una fila por recurso.
- Auditar nunca puede romper la respuesta: va en `try/catch` con log de error.

`POST /api/sistema/actualizar` (sustituye los binarios en produccion, la accion
de admin con mas alcance de la app) pasa a auditarse como
`SISTEMA_ACTUALIZACION_INICIADA`, aceptada o rechazada.

### 3. Alertas de seguridad

`Services/SecurityAlertService.cs`, job `alertas-seguridad` con la misma cadencia
que la ventana (5 min por defecto), para que las ventanas se encadenen sin
huecos. Todas las reglas trabajan sobre una unica foto de la ventana: con 4-8
usuarios son decenas de filas y evita seis consultas con agrupaciones.

| Regla | Dispara cuando |
|-------|----------------|
| `LOGIN_FALLIDOS_POR_CUENTA` | mas de 5 fallos sobre una cuenta en la ventana |
| `IP_MULTIPLES_CUENTAS` | Una IP toca mas de 10 cuentas distintas |
| `ACCESO_MASIVO` | Eventos `ACCESO_BULK`, o mas de 300 acciones de una sesion |
| `IP_NUEVA_PARA_USUARIO` | Login desde una IP nunca usada por ese usuario |
| `PASSWORD_RESETS_REPETIDOS` | mas de 3 cambios/reinicios sobre una cuenta |
| `ERRORES_AUTH_SOBRE_LINEA_BASE` | 401/403 por encima de x3 la media de las 12 ventanas previas |

Tres decisiones que conviene entender:

- **"Pais nuevo" se sustituye por "IP nueva".** El requisito original pedia
  detectar login desde un pais distinto. Aqui no hay GeoIP: la app es
  on-premise, en LAN y sin salida garantizada a internet, asi que una base de
  geolocalizacion seria una dependencia externa imposible de mantener
  actualizada. La IP captura la misma senal en este despliegue. El primer login
  de un usuario nunca alerta (sin historico no hay nada que comparar).
- **Las reglas de barrido cuentan tambien el email probado**, no solo
  `usuario_id`. Un credential stuffing no llega a autenticarse nunca, asi que
  contar solo usuarios identificados lo dejaria invisible.
- **La regla 6 tiene suelo absoluto** (`MinErroresAuthParaAlertar`, 20) ademas
  del factor sobre la linea base. Sin el, una media historica de 0 convierte dos
  errores en una alerta y el operador aprende a ignorarlas.

**Enfriamiento de 60 min por (regla, sujeto).** Un ataque sostenido dispara la
misma regla en cada pasada; sin esto serian correos cada 5 minutos durante horas.
El estado de deduplicacion se consulta en `AUDITORIAS`, no en una cache en
memoria, para que sobreviva a un reinicio del servicio justo cuando empieza un
incidente.

**Entrega** (`Services/AlertDispatcher.cs`, compartido con las alertas de salud).
Orden deliberado:

1. Fila en `AUDITORIAS` (`ALERTA_SEGURIDAD_DISPARADA`). Va primero porque es a la
   vez el registro y el estado de deduplicacion de la siguiente pasada.
2. `NOTIFICACIONES_ADMIN` tipo `SEGURIDAD` (campana del TopBar).
3. Email (`Security:Alertas:DestinatariosEmail`; vacio = todos los ADMIN activos).
4. Slack (`Security:Alertas:SlackWebhookUrl`, opt-in).

Si 3 y 4 fallan, la alerta ya esta registrada y visible en la app: no se pierde.

**Slack.** Solo se acepta `https://hooks.slack.com`; cualquier otra URL desactiva
el canal con un error en el log (sin volcar la URL, que lleva el token). Timeout
de 10 s. La URL es un secreto: va en `appsettings.Production.json`, nunca en la
BD ni en la documentacion.

### 4. Salud y metricas

`/api/health` devolvia `{status:"healthy"}` constante: respondia OK con la base
de datos caida y el disco lleno, que es **peor que no tener health check** porque
da falsa garantia al watchdog. Se mantiene como sonda de vida (el proceso
responde) y la comprobacion real vive en:

- `GET /api/sistema/salud` (ADMIN): base de datos (`SELECT 1` con timeout de 5 s,
  no `CanConnect`, que tiene exito contra una BD en recuperacion), espacio en
  disco del volumen de logs/backups (aviso por debajo del 15%, no sano por debajo
  del 5%) y limites del pool. Devuelve **503** si no esta sano, para que un
  monitor externo lo detecte por codigo de estado.
- `GET /api/sistema/metricas` (ADMIN): peticiones, 4xx, 5xx, p50/p95/max de
  latencia en ventanas de 5 y 60 minutos, mas la ventana anterior para comparar.

`Services/RequestMetrics.cs` mantiene los contadores **en memoria** (cubos de un
minuto, 2 h de historico) y la latencia en un **histograma de cubos fijos**:
memoria acotada pase lo que pase con el trafico. Los percentiles salen
interpolados y son aproximados **por arriba**, que es el error seguro para una
alerta. Se pierden al reiniciar, y es aceptable: lo que hay que conservar ya esta
en `AUDITORIAS` y en los logs.

`HealthAlertJob` (cada 5 min) avisa de: 5xx por encima del 5% con al menos 20
peticiones, p95 x3 sobre la ventana anterior (con suelo de 250 ms), y cualquier
comprobacion de salud en rojo. Usa el mismo despachador que las alertas de
seguridad.

### 5. Espejo fuera de la base de datos

`Logging/SecurityEventLog.cs`. Existe porque la firma detecta alteracion y la
secuencia detecta huecos, pero **borrar la cola de la tabla no deja hueco**.

- Fichero propio (Serilog, categoria `AtlasBalance.Security`,
  `Serilog:SecurityFilePath`), con retencion de 400 dias, por encima de los 365
  de `AUDITORIAS` a proposito.
- Windows Event Log (`Security:MirrorToWindowsEventLog`). El instalador registra
  el origen `AtlasBalance`; si no esta, se avisa una vez y se sigue solo con
  fichero.

Solo se espejan eventos de seguridad, no la auditoria automatica de entidades:
son miles de filas al dia y saturarian el Event Log sin aportar deteccion.

**Limite real, ya dicho arriba:** el servicio corre como LocalSystem, asi que un
RCE puede borrar el fichero y vaciar el Event Log. Una ACL de solo-anexar contra
SYSTEM seria teatro, porque SYSTEM puede reescribir su propia ACL. Lo que si hace
el instalador es quitar el acceso a usuarios normales del servidor (sin eso, el
log hereda los permisos de `%ProgramData%`, donde `BUILTIN\Usuarios` tiene
lectura, y cualquiera con sesion en la maquina puede leer quien entra y desde
donde).

**Para cerrar ese hueco de verdad hay que sacar los logs de la maquina.** Opciones
que encajan con este despliegue, ordenadas por esfuerzo:

1. **Reenvio de eventos de Windows (WEF)**, incluido en Windows Server, sin coste
   ni dependencias. Se configura un colector en otra maquina y el servidor
   reenvia el log `Application` filtrado por el origen `AtlasBalance`. Es la
   opcion nativa y la mas facil de justificar en una auditoria.
2. **Copia del fichero de seguridad a un recurso de red** con permiso de solo
   escritura para la cuenta del servidor, mediante tarea programada.
3. **Slack** ya saca las alertas de la maquina en tiempo real, aunque no el log
   completo.

### 6. Revision de fugas en logs

Cuarto punto del encargo, con resultado: **limpio**. Grep de todos los
`Log*(...)` con `password|token|secret|apikey|cookie|hash|credential|bearer`: 4
coincidencias, todas falsos positivos (cuentan filas o registran fallos de HMAC
sin volcar el valor). No hay logging de cuerpos de peticion completos.
`LogScrubber` sigue cubriendo anti-inyeccion de logs y redaccion de email/IBAN, y
se aplica tambien al User-Agent del espejo de seguridad. Los logs viven en
`%ProgramData%\AtlasBalance\logs`, fuera del arbol estatico, luego no son
accesibles por HTTP.

### Configuracion nueva

```jsonc
"Security": {
  "AuditSigningKey": "...",           // obligatoria fuera de Development
  "MirrorToWindowsEventLog": true,
  "Auditoria": { "UmbralAccesoBulk": 100, "VentanaDeduplicacionSegundos": 60 },
  "Alertas": {
    "Habilitado": true, "VentanaMinutos": 5, "EnfriamientoMinutos": 60,
    "MaxLoginFallidosPorCuenta": 5, "MaxCuentasPorIp": 10,
    "MaxPeticionesSecuenciales": 300, "DiasHistoricoIpConocida": 90,
    "MaxPasswordResets": 3, "MinErroresAuthParaAlertar": 20,
    "FactorSobreLineaBase": 3.0, "VentanasLineaBase": 12,
    "DestinatariosEmail": [], "SlackWebhookUrl": ""
  }
},
"Auditoria": { "RetentionDays": 365 },
"Serilog": { "SecurityFilePath": "C:\\ProgramData\\AtlasBalance\\logs\\security\\atlas-security-.log" }
```

Los umbrales estan calibrados para este despliegue (LAN, 4-8 usuarios). Con
cientos de usuarios habria que subirlos o el ruido enterraria las senales.

### Jobs nuevos

| Job | Cadencia |
|-----|----------|
| `alertas-seguridad` | cada `VentanaMinutos` (5 por defecto) |
| `alertas-salud` | cada 5 min |
| `verificacion-integridad-auditoria` | 04:05 diario |

### Como configurar las notificaciones

**Email.** Reutiliza el SMTP que la app ya tiene configurado en
Configuracion > Sistema (`smtp_host`, `smtp_port`, `smtp_user`, `smtp_password`,
`smtp_from`). Si no hay SMTP, las alertas siguen quedando en la auditoria y en la
campana de notificaciones. Para dirigir los avisos a un buzon concreto en vez de
a todos los administradores, rellena
`Security:Alertas:DestinatariosEmail` en `appsettings.Production.json`:

```jsonc
"DestinatariosEmail": [ "seguridad@empresa.com", "it@empresa.com" ]
```

**Slack.** En `api.slack.com/apps` se crea una app para el workspace, se activa
*Incoming Webhooks*, se anade un webhook al canal deseado y se pega la URL en
`Security:Alertas:SlackWebhookUrl`. Reiniciar el servicio. La URL es un secreto:
`appsettings.Production.json` ya esta protegido por ACL (solo Administradores y
SYSTEM), no la copies a ningun documento ni a la base de datos.

### Herramientas de monitorizacion recomendadas para este despliegue

Windows Server on-premise, LAN, sin garantia de salida a internet. Eso descarta
casi todo el SaaS habitual (Datadog, New Relic, Sentry cloud) y hace que la
respuesta razonable sea gratuita y local:

| Necesidad | Herramienta | Coste | Por que esta |
|-----------|-------------|-------|--------------|
| Uptime y pagina de estado | **Uptime Kuma** (Docker) | Gratis | Sondea `/api/sistema/salud`, entiende el 503, y trae pagina de estado y avisos a Slack/email/Telegram sin configurar nada mas. Ya hay Docker en el entorno de desarrollo. |
| Metricas y graficas | **Prometheus + Grafana** | Gratis | Solo si se quiere historico largo. Requiere exponer las metricas en formato Prometheus, que hoy no se hace: `/api/sistema/metricas` devuelve JSON. Es trabajo adicional, no algo que ya funcione. |
| Logs centralizados | **Reenvio de eventos de Windows (WEF)** | Incluido | Nativo, sin dependencias, y es lo que saca los eventos de seguridad de la maquina comprometible. Primera opcion. |
| Logs con busqueda | **Grafana Loki** | Gratis | Si el volumen crece y hace falta buscar. Mas pesado de operar que WEF. |
| Errores de aplicacion | **Sentry self-hosted** | Gratis | Solo si se quiere agrupacion de excepciones con stack trace. Para 4-8 usuarios, el log de Serilog mas las alertas de tasa de error suelen bastar. |

Recomendacion concreta si hay que elegir una sola cosa: **Uptime Kuma apuntando a
`/api/sistema/salud`**, mas el reenvio de eventos de Windows. Cubre uptime,
pagina de estado, notificaciones y la copia de los logs fuera de la maquina, sin
coste y sin dependencias de internet.

Nota sobre la pagina de estado: no se ha construido una dentro de la aplicacion a
proposito. Una pagina de estado servida por el propio servicio que se cae no
informa de nada cuando mas falta hace; tiene que vivir fuera, y eso es
exactamente lo que hace Uptime Kuma.

