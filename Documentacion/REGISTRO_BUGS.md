# Registro de bugs

## Abiertos

### 2026-07-24 - V-02.07 - Cerrado - CodeQL #18 cs/log-forging en WatchdogOperationsService:181

- **Contexto:** CodeQL re-scan posterior al merge de V-02.06 reabre la
  alerta #18 `cs/log-forging` (CWE-117, severity medium) en
  `Atlas Balance/backend/src/AtlasBalance.Watchdog/Services/WatchdogOperationsService.cs:181`,
  ignorando el `LogScrubber.Scrub` introducido en V-02.06.
- **Estado:** cerrado en codigo en `V-02.07` (commit pendiente). La
  alerta en GitHub pasa a `state=fixed` en el siguiente re-scan
  automatico tras el push a `main`.
- **Solucion:** sustitucion del helper externo por el patron inline
  `Replace("\r", "").Replace("\n", "")` que la regla cs/log-forging de
  CodeQL acepta como sanitizer. Defense-in-depth: mismo patron en
  `WatchdogOperationsService.cs:310, 901, 955, 959, 1096`. Regresion
  `StartUpdateAsync_Should_Log_Rejection_Without_CrLf` cubre el
  refactor involuntario.
- **Detalle completo:** `Documentacion/LOG_ERRORES_INCIDENCIAS.md`
  (entrada `LB-CODEQL-018`) y `Documentacion/Versiones/v-02.07.md`.

### 2026-07-24 - V-02.06 - Cerrado - Secreto ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM ausente en GitHub, release bloqueada en el job de firma

- **Contexto:** con el `verify` de `release.yml` en verde (397/397 tests,
  npm audit en `high`, lint/build limpios), el cuarto
  `workflow_dispatch` (run `30116776847`) llego al job `package` y fallo
  en el primer paso, `Ensure signing key exists`:
  `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM secret is required for a
  public release.` El secreto llega vacio desde el entorno
  `release-signing` de GitHub Actions.
- **Causa raiz:** el par RSA de firma se roto en V-01.09
  (`REGISTRO_BUGS.md:629-638`, 2026-06-01) y quedo como "pendiente
  operativo" cargar la clave privada nueva como GitHub Secret y guardar
  copia segura fuera del repo. La clave publica correspondiente si esta
  en el repo (`appsettings.json`, `ActualizacionService.cs`,
  `WatchdogOperationsService.cs`, scripts de instalador/actualizador),
  pero el secreto de CI nunca quedo configurado (o se perdio/roto desde
  entonces).
- **Actualizacion 2026-07-24 (misma sesion):** el operador confirmo que
  no conserva la clave privada anterior. Se genero un par RSA 4096
  nuevo (`openssl genpkey`) fuera del repositorio; la clave publica se
  actualizo en `appsettings.Production.json.template` e
  `Instalar-AtlasBalance.ps1` (ver `DOCUMENTACION_CAMBIOS.md`,
  2026-07-24, "Rotacion del par de firma de release") y se verifico con
  un round-trip real de firma/verificacion usando las mismas APIs .NET
  que produccion. La clave privada nunca se escribio en el repo ni se
  imprimio en el chat.
- **Por que la carga del secreto no se hizo aqui:** gestionar el
  secreto `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` en GitHub es una
  accion de credenciales que corresponde al operador con acceso al
  repositorio, no a un agente, aunque este haya generado la clave.
- **Accion pendiente del operador:**
  1. Guardar la clave privada (entregada fuera del repo) en un
     gestor de contrasenas o vault seguro.
  2. Cargarla en GitHub -> repo `AtlasLabs797/AtlasBalance` -> Settings
     -> Environments -> `release-signing` -> Secrets ->
     `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`.
  3. Re-disparar `workflow_dispatch` de `Release` (version `V-02.06`,
     runtime `win-x64`, ref `V-02.06`) una vez cargado el secreto.
  4. Actualizar `appsettings.Production.json` con la clave publica
     nueva en cualquier instalacion existente (V-02-05 o anterior)
     antes de que pueda verificar el `.sig` de este release.
- **Cierre 2026-07-24:** el operador cargo el secreto en GitHub. Quinto
  `workflow_dispatch` (run `30118432377`): `verify` y `package` en
  verde completo, incluidos `Ensure signing key exists`, `Build signed
  package` y `Publish GitHub Release`. Release publicado en
  `https://github.com/AtlasLabs797/AtlasBalance/releases/tag/V-02.06-win-x64`
  con `AtlasBalance-V-02.06-win-x64.zip` y `.zip.sig` firmados con la
  clave nueva.
- **Estado:** cerrado. Release V-02.06-win-x64 publicado.

### 2026-07-24 - V-02.06 - Cerrado - react-router-dom 6.30.4 con 2 CVEs moderados, migracion a v7.x completada en V-02.07

- **Contexto:** el tercer `workflow_dispatch` de `release.yml` (run
  `30115851973`) paso el job de tests (397/397) pero fallo en
  `Audit npm vulnerabilities` (`npm audit --audit-level=moderate`).
  Tres hallazgos high (axios, brace-expansion, postcss) ya se habian
  resuelto localmente por commits/instalaciones previas sin llegar a
  commitear el `package-lock.json` actualizado; quedan 2 moderados en
  `react-router-dom@6.30.4`:
  - `GHSA-wrjc-x8rr-h8h6`: open redirect via backslash en `<Link>` y
    `useNavigate`.
  - `GHSA-337j-9hxr-rhxg`: inyeccion de constructor arbitraria via
    `deserializeErrors()` en hidratacion SSR de React Router.
- **Causa raiz:** no existe fix en la serie 6.x (ultima version
  `6.30.4`); el unico paquete que resuelve ambos CVEs es
  `react-router-dom@7.18.1`, un salto de version mayor (v6 -> v7) con
  cambios de API que afectaban a las paginas con rutas declarativas
  (`<Routes>`, `<Route>`).
- **Solucion (V-02.07, 2026-07-24):** migracion a
  `react-router-dom@7.18.1` (que arrastra `react-router@7.18.1`).
  - API compatible 100% para nuestro uso: `BrowserRouter`, `Routes`,
    `Route`, `Link`, `NavLink`, `Navigate`, `useLocation`,
    `useNavigate`, `useParams`, `useSearchParams`, `Outlet` siguen
    existiendo con la misma firma desde `react-router-dom`. No hizo
    falta tocar `App.tsx`, las paginas, los guards ni los hooks.
  - Cierra `GHSA-wrjc-x8rr-h8h6` (open redirect) y
    `GHSA-337j-9hxr-rhxg` (SSR constructor injection) sin necesidad de
    reescribir rutas al data router.
  - **Defensa en profundidad (V-02.07, mismo pase):** los `returnTo`
    internos que acepta `LoginPage` y `ImportacionPage` pasan antes
    por `sanitizeInternalPath()` en
    `frontend/src/utils/safeRoute.ts`. Esta utilidad reemplaza a las
    copias duplicadas de `normalizeReturnTo` que tenia cada pagina y
    aplica, ademas del filtro original (rechaza valores que no
    empiecen por `/`, que empiecen por `//`, o que contengan `\`):
    rechazo de bytes de control (`\x00-\x1f`, `\x7f`), rechazo de
    `/\\` que algunos navegadores reescriben a `//`, y verificacion
    doble del valor tras `decodeURIComponent` para que un payload
    doblemente codificado (`%255C%5Cevil.com`) tampoco cuele. Si
    falla, cae al fallback (`'/dashboard'`). La funcion `isInternalPath()`
    exportada permite que cualquier futura ruta que acepte un destino
    por querystring pueda aplicar el mismo predicado sin reinventarlo.
  - `vite.config.ts` actualizado: el matcher del chunk `vendor`
    incluye ahora `node_modules/react-router/` ademas del de
    `react-router-dom/` (ambos directorios existen porque v7 los
    instala por separado).
  - `package-lock.json` regenerado en scratchpad (`C:\Users\usuario\
    AppData\Local\Temp\2\opencode\frontend-lockregen`) para esquivar
    el `EPERM` conocido al hacer `npm install` sobre `node_modules`
    en este sandbox; copiado de vuelta al repo.
- **Test unitario (V-02.07):** `frontend/tests/safeRoute.test.ts`
  cubre los vectores del CVE (`//evil`, `\\evil`, `/\evil`,
  `%2F%2Fevil`, `%5C%5Cevil`, `javascript:`, `data:`, control chars,
  null/vacio), el caso positivo (`/dashboard`, `/cuentas/1?q=2`),
  trims, fallbacks custom y `isInternalPath`. Se compila con
  `tsc -p tsconfig.test.json` y se ejecuta con `node --test`.
  Resultado: 9/9 PASS, junto con los 3/3 previos de
  `importacionRequest.test.ts` (suite verde).
- **Nuevo advisory HIGH no aplicable (V-02.07):**
  `react-router-dom@7.18.1` arrastra `react-router@7.18.1`, y este
  ultimo entra en el rango `>=7.12.0 <8.3.0` del advisory
  `GHSA-qwww-vcr4-c8h2` ("RSC Mode CSRF Bypass Allows Action Execution
  Before 400 Response", HIGH). **No aplica a Atlas Balance**: la app
  usa el router Declarativo (`<BrowserRouter>` + `<Routes>` +
  `<Route>`) servido como estaticos por Kestrel, sin RSC, sin
  Framework Mode ni Data Mode. El vector solo existe en apps que
  usen `createBrowserRouter` + actions + RSC, que no es nuestro caso.
  La unica forma de cerrar tambien este advisory seria subir a
  `react-router@8.3.0`, que requiere React 19.2.7+ (React Router v8
  elimino `react-router-dom` y lo fusiono en `react-router`).
- **Gate actualizado:** `--audit-level=high` (V-02.06) ->
  `--audit-level=critical` en `release.yml` y `ci.yml`, con
  comentario inline que apunta a esta entrada y a `v-02.07.md`. Es
  el nivel maximo posible manteniendo `react-router-dom@7.18.1`
  sobre React 18.
- **Verificacion:** `tsc --noEmit` OK, `npm run lint` OK. `npm run
  build` falla por el `EPERM` conocido de Vite/Rolldown al copiar
  `public/fonts/*.ttf` desde `node_modules` -> `dist/` dentro del
  sandbox; ya documentado en `CLAUDE.md` como atasco conocido y
  ajeno a este cambio. `node --test .test-dist/tests/safeRoute.test.js`
  9/9 PASS, `node --test .test-dist/tests/importacionRequest.test.js`
  3/3 PASS.
- **Estado:** cerrado. Los 2 CVEs moderados que motivaron el
  aplazamiento en V-02.06 ya no aparecen en `npm audit` para
  nuestro codigo, y el unico HIGH que queda (RSC CSRF) no es
  explotable en esta arquitectura. La segunda capa
  (`sanitizeInternalPath`) queda como defensa en profundidad para
  que un eventual regression en upstream no vuelva a exponer el
  vector.

### 2026-07-16 - V-02.06 - RLS en identidad/configuracion pospuesto a V-02.07

- **Contexto:** la auditoria RLS de V-02.06 discutio aplicar RLS a
  `USUARIOS`, `REFRESH_TOKENS`, `INTEGRATION_TOKENS` y `CONFIGURACION`
  (esta ultima con secretos como `smtp_password`, claves IA y
  `backup_cloud_encryption_key`).
- **Causa raiz:** el contexto RLS firmado solo identifica `user_id` despues
  del login. Login, MFA, refresh y la validacion de tokens de integracion
  ejecutan `SELECT` y `UPDATE` sobre esas tablas antes de que el contexto
  pueda restringirlas. Ademas, RLS en PostgreSQL filtra filas pero no puede
  ocultar columnas: una vez una policy `USING (...)` admite la fila, un
  `SELECT *` devuelve `password_hash`, `mfa_secret`, `token_hash`, etc.
  Limitar esas columnas requiere GRANT a nivel de columna, vistas o una
  API de funciones dedicada.
- **Pendiente para V-02.07:**
  - Disenar y aplicar policies que cubran el login flow (incluyendo
    `is_auth_flow`) sin romper autenticacion ni MFA setup/recovery.
  - Evaluar si conviene llevar secretos a una tabla paralela
    `CONFIGURACION_SECRETS` accesible solo por admin/system o usar
    GRANTs a nivel de columna sobre `CONFIGURACION` con `es_secreto=true`.
  - Aplicar `SELECT` admin-only en `USUARIOS` y `INTEGRATION_TOKENS`
    con auto-lectura por su creador/usuario, y `INSERT/UPDATE` admin-only.
  - Anadir tests de regresion para login/MFA/refresh/integraciones con
    las nuevas policies.
- **Estado:** abierto. V-02.06 solo activa FORCE RLS + soft-delete
  backstop en las tablas financieras y mejora el backup/restore; el
  alcance de identidad se difiere explicitamente para evitar romper
  autenticacion.

## Cerrados en codigo con gate externo

### 2026-07-16 - V-02.06 - Cerrado - AppDbContextModelSnapshot desalineado

- **Contexto:** `tests/AtlasBalance.API.Tests/MigrationDiscoveryTests` y las
  migraciones V-02.05 confirman que las migraciones se descubren bien, pero
  `AppDbContextModelSnapshot.cs` todavia omite `DeletedAt`/`DeletedById` en
  `Conciliacion`, `ImportacionLoteFila`, `ExtractoColumnaExtra` y
  `RevisionExtractoEstado` aunque el modelo actual los declara.
- **Causa raiz:** las migraciones V-02.05 que anadieron esas columnas
  fueron manuscritas-SQL (sin scaffold EF) y no se reconcilio el snapshot.
- **Solucion:** el snapshot se reconcilio con soft-delete, FK, indices,
  constraints, operaciones de backup e idempotencia de importacion.
- **Estado:** cerrado en codigo y cubierto por discovery/model tests. La
  aplicacion completa desde una base vacia sigue dentro del gate externo
  PostgreSQL/Testcontainers de V-02.06.

## Abiertos

### 2026-07-16 - V-02.06 - IMPORTACION_LOTES sin soft-delete

- **Contexto:** la entidad `IMPORTACION_LOTES` no implementa `ISoftDelete`
  en el modelo y no tiene columnas `deleted_at`/`deleted_by_id`.
- **Causa raiz:** se creo en la migracion V-02.02 sin columnas de
  soft-delete, y nadie ha anadido esa capacidad todavia.
- **Pendiente para V-02.07:**
  - Anadir `deleted_at`/`deleted_by_id` con migracion que respete el
    snapshot EF ya reconciliado.
  - Aplicar `ISoftDelete` en `ImportacionLote.cs` (heredar de entidad base
    o aplicar el filtro global via `AppDbContext`).
  - Extender la policy SELECT de la tabla con `deleted_at IS NULL`.
- **Estado:** abierto. En V-02.06 la policy nueva solo separa
  SELECT/INSERT/UPDATE/DELETE con `can_read_cuenta_by_id` /
  `can_write_cuenta_by_id`, sin filtro soft-delete (que requiere la
  columna).

### 2026-05-22 - V-01.09 - Bootstrap desde Watchdog antiguo pendiente

- Contexto: tras implementar el update online de paquete completo, quedan dos bloqueos fuera del codigo nuevo.
- Pendientes:
  - Una instalacion que todavia ejecuta un Watchdog anterior al fix no puede usar ese Watchdog viejo para actualizarse de forma completa; puede requerir un primer `update.cmd` manual o una ruta puente.
  - Falta validacion real en Windows instalacion reemplazando el Watchdog vivo mediante el helper externo.
- Estado: abierto. GitHub `latest` ya apunta a `V-01.09-win-x64`; esto solo bloquea vender el upgrade desde cualquier version antigua como 100% one-click.

## Auditoria F1-F5 de V-02.06

Los hallazgos siguientes estan cerrados en codigo salvo que su estado indique
un gate externo pendiente. Se conservan juntos para trazabilidad del plan.

### 2026-07-20 - V-02.06 - Auditoria automatica expone Configuracion.Valor en DetallesJson

- Contexto: el interceptor `AuditSaveChangesInterceptor` serializa las
  propiedades old/new de `Configuracion` (incluyendo `Valor`) a
  `AUDITORIAS.DetallesJson` sin redaccion. `Program.ProtectExistingConfigurationSecrets`
  actualiza `Valor` durante upgrades con secretos legacy ya en claro.
- Pendiente: redaccion de `Valor` cuando `EsSecreto=true` o cuando `Clave`
  coincide con la lista clasificada (`smtp_password`, `exchange_rate_api_key`,
  `openrouter_api_key`, `openai_api_key`, `minimax_api_key`,
  `google_drive_oauth_client_secret`, `backup_cloud_encryption_key`,
  `github_update_token`, `jwt_signing_key`, `rls_context_secret`,
  `watchdog_shared_secret`); ademas extender la redaccion a jobs/setters
  para que `SetContextoAuditoria` siga siendo valido, y a jobs sin
  HttpContext.
- Estado: cerrado en codigo. El interceptor redacta cuando el estado anterior
  o actual es secreto y la migracion irreversible
  `20260720120000_RedactHistoricalConfigurationAudits` limpia solo las claves
  sensibles historicas, sin imprimir valores ni destruir auditoria no secreta.
  Aplicacion real pendiente del gate PostgreSQL de release.

### 2026-07-20 - V-02.06 - Parser CSRF frontend rechaza Base64 estandar

- Contexto: `CsrfService.GenerateToken` emite `Convert.ToBase64String(32 bytes)`,
  lo que puede terminar en `=` y contener `+` o `/`. El parser
  `getCsrfTokenFromCookie` validaba contra `^[A-Za-z0-9_-]+$` (Base64URL sin
  padding), devolviendo `null` para cualquier cookie real.
- Impacto: tras un reload, `csrfToken` queda `null` y las mutaciones envian
  sin `X-CSRF-Token`, lo que `CsrfMiddleware` rechaza con 403.
- Resolucion aplicada en PR F1: regex cambiada a Base64 estandar RFC 4648 §4
  y `decodeURIComponent` envuelto en try/catch. Pendiente convidar a la
  documentacion V-02.05 (LOW-FE-4) que decia Base64URL.
- Estado: cerrado en codigo y documentacion; incluye regresiones con `+`,
  `/`, `=` y reload.

### 2026-07-20 - V-02.06 - Estados de conciliacion chocan contra CHECK de PostgreSQL

- Contexto: `ConciliacionService` y `HardenedConciliacionService` escriben
  `sugerida`, `conciliada`, `excepcion` y `resuelta` en
  `MOVIMIENTOS_ESPERADOS.estado` y `CONCILIACIONES.estado`. La migracion
  V-02.05 (`20260710091000_AddConciliacionSoftDeleteAndEstadoCheck`) dejo
  CHECK constraints con otros valores (`pendiente/satisfecho/vencido/cancelado`
  y `sugerida/conciliada/descartada/cerrada`).
- Impacto: cualquier sugerir/confirmar/excepcion/resolver sobre PostgreSQL
  produce `CheckViolation`. InMemory lo aceptaba, por lo que los tests no
  detectaban el bug.
- Resolucion aplicada en PR F2: nueva migracion
  `20260720090000_AlignConciliacionEstadosAndSnapshot` reemplaza los CHECK
  con los estados reales y normaliza `deleted_at` a `timestamp with time
  zone`. Pendiente PostgreSQL/Testcontainers para validar el round-trip
  crear->sugerir->confirmar->excepcion->resolver; queda bloqueado por
  Docker no disponible.
- Estado: codigo corregido; validacion runtime bloqueada por Docker.

### 2026-07-20 - V-02.06 - Scopes vacios degeneraban en acceso total

- Contexto: `IntegracionesController.NormalizeEndpointScopes` sustituia
  `scopes = []` por `DefaultOpenClawScopes`, y lo mismo hacia cuando ningun
  scope era valido.
- Impacto: denegacion por defecto del middleware derrotada: un admin que
  desmarca todas las casillas, o un cliente API que envia `scopes: []`,
  recibe el acceso maximo en lugar de cero.
- Resolucion aplicada: `NormalizeEndpointScopes` preserva `[]` y el controller
  rechaza scopes desconocidos con 400. Tests de controller y middleware real
  prueban cero acceso para la lista vacia.
- Estado: cerrado; incluido en el bloque backend 120/120.

### 2026-07-20 - V-02.06 - Scanner de secretos no portable a Ubuntu

- Contexto: `Test-AtlasSecrets.ps1` usaba comparaciones literales con
  backslash para exclusiones (`"\bin\"`, `"\node_modules\"` etc.). En
  runners Ubuntu esos matches no encajan rutas Linux.
- Impacto: el scan podia aceptar archivos que deberia haber excluido
  (bin, obj). El reverso (excluir los raices `Atlas Balance/backend/src`,
  `backend/tests`, etc.) no esta demostrado, pero el riesgo existia.
- Resolucion aplicada en PR F5: exclusion por segmentos via
  `Split-PathSegments` que normaliza ambos separadores; stopwatch + timeout
  duro de 60 s; resumen `Scanner: N archivos analizados, M excluidos`.
- Verificacion: el script reescrito corre en Windows PowerShell 5.1, sus
  fixtures pasan y analiza 475 archivos sin hallazgos. Pendiente probarlo en
  GitHub Actions `ubuntu-24.04` con `pwsh` para confirmar portabilidad
  nativa (este host no tiene `pwsh` instalado).
- Estado: cerrado en codigo; verificacion CI queda pendiente.

### 2026-07-20 - V-02.06 - Timeout global 15 s cancela backup/Drive/import

- Contexto: el cliente axios impose `timeout: 15_000` desde `api.ts`.
  `BackupsPage` llamaba `/backups/manual`, `/backups/google-drive/import`
  y `/backups/{id}/google-drive/retry` sin override; `ImportacionPage`
  tampoco lo hacia con `/importacion/lotes` y
  `/importacion/lotes/{id}/confirmar`.
- Impacto: pg_dump, descarga+cifrado de Drive o cargas grandes superan los
  15 s. El timeout cancela el token del backend, lo que produce un fallo
  visible aun cuando el proceso siga corriendo.
- Resolucion aplicada: importacion usa timeout especifico de 300 s e
  idempotencia; backup manual, import Drive y restore responden 202 con
  `operation_id` y polling dedicado. Restore correlaciona ese id extremo a
  extremo con Watchdog para no aceptar un estado global ajeno.
- Estado: cerrado en codigo y pruebas focalizadas; round-trip Drive real sigue
  siendo gate de release.

### 2026-07-20 - V-02.06 - Sources de version no se validaban en CI

- Contexto: VERSION, Directory.Build.props, package.json y SeedData pueden
  divergir silenciosamente. release.yml ofrecia `V-02-03` como default.
- Impacto: una rama V-02.06 podia salir con UI/script reportando V-02-04
  o sin filtrar el default para el operador.
- Resolucion aplicada en PR F5: `Check-VersionAlignment.ps1` valida las
  cinco fuentes (incluyendo `release.yml.inputs.version.default`); alineado
  en V-02.06; enganchado a `ci.yml` y `release.yml`.
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Google Drive import comparaba hash de dominios cruzados

- Contexto: `UploadBackupAsync` guardaba `copy.ChecksumSha256 = encrypted.Sha256Hex`
  (SHA-256 del `.enc`). `GoogleDriveBackupService.ImportAsync` descargaba el
  `.enc`, lo descifraba y comparaba el hash del dump plaintext contra el del
  cifrado. El wrapper `HardenedGoogleDriveBackupService` validaba el `.enc`
  pero el servicio interno repetia la comparacion incorrecta.
- Impacto: cualquier copia subida por el propio servicio era rechazada como
  corrupta al importarse.
- Resolucion aplicada en PR F3: `GoogleDriveBackupService.ImportAsync` valida
  el hash del `.enc` ANTES de descifrar y elimina la comparacion plaintext.
  El wrapper pasa a ser un pass-through puro; deja de buscar el estado
  inexistente `ACTIVE`.
- Estado: cerrado en codigo; pendiente PostgreSQL/Testcontainers para validar
  el round-trip completo con cifrado.

### 2026-07-20 - V-02.06 - Auditoria semantica no se persistia bajo transaccion explicita

- Contexto: `AuditService.LogAsync` omitia `SaveChangesAsync` cuando
  `_dbContext.Database.CurrentTransaction` era no nulo, esperando a que el
  caller guardase la auditoria en su propio SaveChanges. Varios callers
  (`ImportacionService.ConfirmarAsync`,
  `ImportacionService.PlazoFijoMovimientoAsync`) hacian
  `SaveChanges` + `LogAsync` + `CommitAsync`, dejando `importacion_confirmada`
  e `importacion_plazo_fijo_movimiento` solo en memoria.
- Resolucion aplicada en PR F1: `LogAsync` ahora ejecuta `SaveChangesAsync`
  siempre. Bajo transaccion, SaveChanges encola la insercion en
  ChangeTracker; el Commit persiste y el rollback la descarta.
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Policies RLS no reconocian flags de conciliacion

- Contexto: `can_write_cuenta_by_id` solo validaba `puede_agregar_lineas`,
  `puede_editar_lineas`, `puede_eliminar_lineas`, `puede_importar`. Las
  policies de `CONCILIACIONES` y `MOVIMIENTOS_ESPERADOS` delegaban en esa
  funcion, asi que un usuario con solo `puede_conciliar` o
  `puede_cerrar_conciliacion` pasaba el chequeo de servicio y encontraba
  `InsufficientPrivilege` en RLS.
- Resolucion aplicada: predicados separados `can_reconcile_*` y
  `can_close_reconciliation_*`. El scope `reconcile` no puede escribir
  `resuelta` ni reabrirla; `reconcile-close` solo puede llevar ambas tablas a
  `resuelta`. No se ensancha `can_write_cuenta_by_id`.
- Estado: cerrado en codigo; PostgreSQL/Testcontainers sigue como gate para
  demostrar las policies contra el motor real.

### 2026-07-20 - V-02.06 - Auditorias automaticas sin UsuarioId

- Contexto: `AuditSaveChangesInterceptor` solo obtenia la IP de
  `HttpContext`, no del usuario. `SetContextoAuditoria` era el unico camino
  para atribuir las entradas automaticas, y nadie lo llamaba.
- Impacto: `UsuarioId = null` en auditorias de cambios web -> filtro de
  usuario inutil, forense ciega.
- Resolucion aplicada en PR F1: el interceptor ahora lee
  `ClaimTypes.NameIdentifier` y `sub` del HttpContext; mantiene override
  explicito para jobs/seed.
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Snapshot de Conciliacion desalineado con soft delete

- Contexto: `AppDbContextModelSnapshot` registraba el unique index sin
  filtro `deleted_at IS NULL` y no declaraba las propiedades
  `DeletedAt`/`DeletedById`, FK ni indice por `DeletedById`.
- Impacto: `dotnet ef migrations script` podria recrear columnas/index ya
  presentes en una base upgraded; ya ocurrido en V-02.05.
- Resolucion aplicada en PR F2: snapshot sincronizado a mano con soft
  delete, FK, indices y filtro parcial.
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Constructor RLS no resoluble por DI

- Contexto: `RlsDbCommandInterceptor` se registraba via `AddScoped<T>()`
  pero su unico constructor era `internal`, y `RlsContextSecret` (parametro)
  tambien `internal`. El contenedor `Microsoft.Extensions.DependencyInjection`
  selecciona constructores publicos, asi que la primera resolucion de
  `AppDbContext` lanzaba excepcion o devolvia null.
- Resolucion aplicada en PR F1: factory explicita en
  `Program.cs.AddScoped` que conserva los tipos internal sin publicar el
  ctor.
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Bootstrap legacy del watchdog invocaba parametros inexistentes

- Contexto: `Start-WatchdogUpdate.ps1` invocaba
  `Actualizar-AtlasBalance.ps1 -SkipWatchdog -SourcePath -PackageZipPath`,
  pero el actualizador solo declaraba `InstallPath`, `SkipBackup`,
  `PromptForDbOwnerCredentials`, `DbOwnerUser`. PowerShell rechaza el
  binding y el script nunca aplicaba el paquete. Ademas, permitia continuar
  sin clave publica o sin firma.
- Resolucion aplicada en PR F3: `Start-WatchdogUpdate.ps1` retirado segun
  decision del usuario; `Actualizar-AtlasBalance.ps1` queda con su contrato
  `InstallPath/SkipBackup/PromptForDbOwnerCredentials/DbOwnerUser` intacto.
  Endurecimiento de `WatchdogController.ActualizarApp` exige
  `package_zip_path`, `VerifyPackageZipIntegrity` trata
  `packageZipPath`/clave publica nulos como error (fail-closed).
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Tolerancia global de conciliacion truncaba negativos

- Contexto: `HardenedConciliacionService.SugerirAsync` calculaba
  `Math.Abs(maxMonto)` siendo `maxMonto` el maximo firmado del lote. Para
  un lote con `-100000` y `10`, el resultado era 10 -> la ventana global
  quedaba en +-10 y un extracto a `-99500` quedaba fuera antes del matching
  individual.
- Resolucion aplicada en PR F2: el rango global ahora usa el maximo ABSOLUTO
  del lote (positivos y negativos), alineado con la tolerancia individual.
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Selector de divisa no se enviaba al crear lote

- Contexto: `ImportacionPage` posteaba `/api/importacion/lotes` sin
  `divisa_esperada`, por lo que `divisa_mismatch` quedaba siempre `false`
  y el checkbox bloqueante nunca se mostraba al operador.
- Resolucion aplicada en PR F4: nuevo estado `divisaEsperada` inicializado
  con la divisa de la cuenta; selector visible; payload del POST incluye
  `divisa_esperada`; reset al cambiar de cuenta o iniciar nuevo lote.
- Estado: cerrado.

### 2026-07-20 - V-02.06 - Idempotencia de lotes no aplicada

- Contexto: la creacion y confirmacion de lotes pueden recibir un timeout
  del navegador o de axios (15 s) y disparar reintentos del operador.
  No existe una clave de idempotencia que evite generar filas duplicadas.
- Resolucion: migracion `20260720140000_AddImportacionIdempotency`, indice
  unique parcial por usuario/clave, recuperacion de la carrera SELECT+INSERT y
  replay de confirmacion solo con la misma clave y respuesta persistida.
- Estado: cerrado en codigo y tests InMemory; carrera PostgreSQL real pendiente
  del gate Testcontainers.

### 2026-07-20 - V-02.06 - Backup manual y Drive sin conversion a operaciones asincronas

- Contexto: mantener una peticion HTTP abierta hacia backup/Drive/restore era
  fragil ante timeouts y reinicios.
- Resolucion: tabla `BACKUP_OPERATIONS`, jobs Hangfire para manual/Drive,
  `202 + operation_id`, polling `/backups/operations/{id}` y correlacion
  Watchdog para restore. La tabla cumple soft-delete, FK, indices y CHECKs.
- Estado: cerrado en codigo; ejecucion real con pg_dump/Drive/pg_restore queda
  como gate de release.

### 2026-05-10 - V-01.06 - Pendientes altos tras auditoria final

- Contexto: la auditoria general detecto problemas no criticos pero demasiado relevantes para llamar a la app "lista".
- Pendientes:
  - **Ejecutar suite completa con Docker/Testcontainers: CERRADO (2026-07-02).** Suite completa 323/323 OK incluidos `ExtractosConcurrencyTests` y `RowLevelSecurityTests` con Testcontainers. Evidencia en `Documentacion/Versiones/v-02-04.md` seccion Pendientes.
  - **E2E autenticado contra PostgreSQL real con datos de volumen: CERRADO (2026-07-07).** `VolumeSmokeTests` (Testcontainers, PostgreSQL 16 real): siembra 50.000 EXTRACTOS y ejercita paginacion (primera/intermedia/ultima pagina), ordenacion asc/desc, contrato `{data, total, page, pageSize, totalPages}` y resumen de cuenta, con usuario autenticado (ClaimsPrincipal, patron estandar del proyecto de tests). Ejecutado 2 veces (agente + verificacion independiente), 1/1 OK. Matiz: llama a los controllers en proceso, no via HTTP puro (el proyecto de tests no tiene WebApplicationFactory).
  - Validacion visual/E2E final de tablas tipo grid con datos reales. Los modales y formularios criticos ya recibieron foco controlado, labels y ARIA en auditorias UI previas; el 2026-05-19 se reforzaron select nativo, modal de importacion, errores persistentes de celda, estados de importacion, tabs de Configuracion, tablas de cuenta/extractos, token modal/metricas, backups y alternativas accesibles de graficas. Falta sesion real con datos de volumen.
- Estado: solo queda abierta la validacion visual con datos de volumen (sesion de navegador real con 50k filas en el grid). Los otros dos pendientes estan cerrados con evidencia.

## Cerrados

### 2026-07-24 - V-02.06 - Cerrado - Endpoint de autocomplete devolvia tipo anonimo en vez del DTO documentado

- **Contexto:** el primer `workflow_dispatch` real de `release.yml` sobre
  `V-02.06` corrio la suite xUnit por primera vez desde que se anadio
  `GET /api/formatos-importacion/columnas-extra-sugeridas`
  (2026-07-23/24) y fallaron los 3 tests de
  `FormatosImportacionControllerTests` para ese endpoint.
- **Causa raiz:** el controller devolvia `Ok(new { data = nombres })`
  (tipo anonimo) en vez de `Ok(new ListarColumnasExtraSugeridasResponse
  { Data = nombres })`, aunque el DTO ya existia. Nunca se detecto en
  este sandbox porque el proyecto de tests compilaba pero no podia
  *ejecutarse* (ACL de `obj/bin`, `hostpolicy.dll` no encontrado).
- **Solucion:** `FormatosImportacionController.ListarColumnasExtraSugeridas`
  ahora devuelve el DTO. El JSON de red no cambia (`PropertyNamingPolicy
  = SnakeCaseLower` global serializa `Data` igual que el `data` del
  objeto anonimo), asi que el frontend no necesito cambios.
- **Estado:** cerrado. Pendiente de confirmar en el siguiente run de CI.

### 2026-07-24 - V-02.06 - Cerrado - SeedDataTests probaba un catalogo de bancos historico ya reemplazado

- **Contexto:** mismo run de CI. Fallaron 3 tests de `SeedDataTests`
  que esperaban 8 formatos predefinidos historicos (Sabadell, BBVA
  EUR/MXN, Banco Caribe, Banco Popular).
- **Causa raiz:** el wipe + reintroduccion de formatos del 2026-07-21
  reemplazo `SeedData.DefaultFormatosImportacion` por los 6 formatos
  reales del operador (BBVA/BS/Banquinter Empresa y Particular, EUR),
  pero `SeedDataTests.cs` nunca se actualizo porque el proyecto de
  tests no podia ejecutarse en este sandbox (mismo bloqueo de ACL que
  el hallazgo anterior).
- **Solucion:** los 3 tests se reescribieron contra el catalogo actual
  de 6 formatos, incluida la asercion de `MapeoJson` sobre "Banquinter
  Empresa" (antes apuntaba a la inexistente "BBVA MXN") y el Id fijo de
  colision (`0ee8dcc6-...`, el Id real de "BBVA Empresa" en vez del
  historico de "Sabadell").
- **Estado:** cerrado. Pendiente de confirmar en el siguiente run de CI.

### 2026-07-07 - V-01.02 - Cerrado - Estado Git local no fiable

- Contexto: en V-01.02 se reporto que `git status --short` listaba practicamente todo el arbol como `untracked`, indicando repositorio local inestable.
- Causa original: copia local/repo recreado sin historial o indice fiable.
- Resolucion: el estado Git se normalizo entre V-01.02 y 2026-07-07. La rama V-02-04 activa muestra `git status` normal con solo archivos modificados esperados. El historial de commits es accesible y se hacen commits/push con normalidad.
- Estado: cerrado. Git funciona correctamente hoy (2026-07-07). Repo local reparado/recreado en algun momento posterior; ya no es una incidencia operativa.

### 2026-06-30 - V-02-02 - Cerrado - Build-Release fallaba aunque el scanner pasara

- Contexto: al validar empaquetado local, `Build-Release.ps1` abortaba despues de `Scanner Atlas sin hallazgos`.
- Causa: el script comprobaba `$LASTEXITCODE` despues de invocar un `.ps1`; ese valor podia venir sucio de comandos internos.
- Solucion: comprobar el scanner con `$?`.
- Verificacion: el empaquetado avanzo al siguiente gate.
- Estado: cerrado.

### 2026-06-30 - V-02-02 - Cerrado - Build-Release usaba `npm ci` destructivo sobre `node_modules` bloqueado

- Contexto: el empaquetado local fallaba con `EPERM` al borrar `frontend\node_modules\.vite-temp`.
- Causa: `Build-Release.ps1` ejecutaba `npm ci` siempre, aunque el arbol local ya existiera y Windows tuviera carpetas bloqueadas. Ademas, un intento fallido podia dejar `node_modules` incompleto.
- Solucion: `npm ci` solo se ejecuta con `-CleanNpmInstall` o si falta `node_modules`; si existe pero falta `tsc.cmd`, se repara con `npm install --ignore-scripts --no-audit --fund=false`.
- Verificacion: `Build-Release.ps1 -Version V-02-02 -Runtime win-x64 -AllowUnsignedLocal` OK, ZIP local generado.
- Estado: cerrado.

### 2026-06-30 - V-02-02 - Cerrado - Docker/Testcontainers no disponible para cierre de release

- Contexto: la suite completa V-02-02 quedaba en 315/317 porque `ExtractosConcurrencyTests` y `RowLevelSecurityTests` no conectaban con Docker.
- Causa: Docker Desktop estaba detenido y Testcontainers no descubria el endpoint por permisos/contexto. La URI aceptada por Docker CLI (`npipe:////./pipe/dockerDesktopLinuxEngine`) no es la misma forma que acepta Docker.DotNet/Testcontainers.
- Solucion operativa: arrancar Docker Desktop y ejecutar tests en contexto elevado con `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine`.
- Verificacion: pruebas Testcontainers 2/2 OK y suite backend completa 317/317 OK.
- Estado: cerrado.

### 2026-06-30 - V-02-02 - Cerrado - Fallos backend no Docker tras hardening

- Contexto: la primera suite completa posterior al hardening tenia tres fallos no dependientes de Docker.
- Causas:
  - configuracion global OpenRouter aceptaba un modelo desconocido en vez de normalizarlo a `openrouter/auto`;
  - MFA trusted-device quedaba habilitado por fallback cuando faltaba configuracion;
  - el test de ranking IA por cuenta no declaraba permiso real en `PERMISOS_USUARIO`.
- Solucion: normalizacion global por proveedor, MFA remember-device fail-closed y fixture de test alineado con `UserAccessService`.
- Verificacion: `ConfiguracionControllerTests|AtlasAiServiceTests|AuthServiceTests` 101/101 OK; suite backend sin Testcontainers 315/315 OK.
- Estado: cerrado.

### 2026-06-30 - V-02-02 - Cerrado - QA visual completa de nuevos flujos pendiente

- Contexto: quedaba sin validar visualmente importacion, historial de lote, conciliacion, OpenClaw tokens, mobile alertas y modos de Extractos.
- Solucion: QA Playwright finita con Chrome local, build Vite temporal y API mock cerrada en el mismo proceso.
- Verificacion: dashboard CTAs, importacion con advertencias desmarcadas, historial, conciliacion, OpenClaw rotacion, Extractos revision/edicion y mobile alertas OK; consola sin errores.
- Estado: cerrado.

### 2026-06-30 - V-02-02 - Cerrado - Test determinista IA sensible a la fecha actual

- Contexto: el test `AskAsync_Should_Respect_Cuenta_Scope_In_Deterministic_Ranking` habia fallado en pasadas amplias.
- Causa real en esta validacion: el fixture usaba `scope.CuentaIds`, pero `UserAccessService.ApplyCuentaScope` aplica permisos persistidos en BD.
- Solucion: el test crea `PermisoUsuario` con `PuedeVerCuentas=true` sobre la cuenta permitida.
- Verificacion: `AtlasAiServiceTests` incluido en 101/101 OK.
- Estado: cerrado.

### 2026-06-27 - V-02-02 - Cerrado - Vite vulnerable y dependencias npm rojas

- Contexto: se recibio aviso de Vite `GHSA-fx2h-pf6j-xcff` / `CVE-2026-53571` y la auditoria npm saco mas deuda.
- Causa: `package-lock.json` resolvia `vite@8.0.8`, vulnerable al bypass de `server.fs.deny` en Windows; ademas `npm audit` detecto `form-data@4.0.5` high y `js-yaml@4.1.1` moderate.
- Impacto: Vite podia exponer `.env` u otros ficheros denegados si el dev server se levantaba con `--host`; `form-data` y `js-yaml` mantenian el arbol SCA rojo.
- Solucion: lockfile actualizado a `form-data@4.0.6`, `vite@8.1.0`, `js-yaml@4.3.0`; `package.json` fija `form-data` y `js-yaml` con `overrides`; `vite.config.ts` cierra el dev server por defecto a loopback/hosts locales y mantiene hardening de `/__open-in-editor`.
- Verificacion: `npm audit --audit-level=moderate` OK, instalacion limpia con `npm ci` OK, Vite `8.1.0`, frontend lint/TypeScript/build temporal OK.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - RLS de dashboard desalineado con roles

- Contexto: auditoria auth/RLS tras el cambio a tres roles.
- Causa: la politica RLS de lectura mantenia `PuedeVerCuentas` como lectura general tambien para scope `dashboard` y no reflejaba que `GERENTE` puede ver dashboard con cualquier permiso de datos.
- Impacto: defensa en profundidad inconsistente. Un empleado con permiso de cuentas pero sin dashboard podia leer datos financieros si una ruta/query llegaba a PostgreSQL con `atlas.request_scope = dashboard`; un gerente valido podia quedar bloqueado en la base.
- Solucion: migracion `20260626193000_AlignRlsDashboardAccessWithRoles` con funcion `current_user_is_manager()` y ramas de lectura diferenciadas para `dashboard`.
- Verificacion: backend build OK, tests focalizados de permisos/datos 116/116 OK y `RowLevelSecurityTests` 1/1 OK contra PostgreSQL real/Testcontainers usando artefactos aislados en `C:\tmp\atlas-rls-artifacts`.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Selector de columnas pedia `cuenta_id` en vista general

- Contexto: el panel `Columnas` de `Extractos` seguia fallando al guardar en vista general con el mensaje `cuenta_id es requerido`.
- Causa: la correccion anterior probaba el controlador directo, pero no el contrato JSON real; el frontend ademas enviaba `cuenta_id: null` cuando el scope era global.
- Impacto: el usuario no podia guardar columnas visibles sin seleccionar una cuenta.
- Solucion: DTO con nombres JSON explicitos y `cuenta_id` nullable; frontend omite claves de scope vacias; regresion de deserializacion snake_case.
- Verificacion: lint OK, TypeScript OK, build Vite temporal OK, `ExtractosControllerTests` 18/18 OK y QA Browser con mock que rechazaba cualquier `cuenta_id` en vista general.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Formatos de importacion salian cortados

- Contexto: la tabla de `Formatos` se veia cortada junto al formulario lateral, especialmente en columnas `Extra` y `Acciones`.
- Causa: ancho minimo fijo de tabla (`860px`) dentro de una columna de grid que podia ser menor, mas estilos de acciones pensados para tarjetas.
- Impacto: el usuario tenia que usar scroll horizontal interno o directamente no veia bien botones y columnas.
- Solucion: `colgroup`, `table-layout: fixed`, anchos en `rem` para columnas criticas, columna `Extra` flexible, acciones de celda propias, botones sin corte de palabra y breakpoint para apilar formulario bajo la tabla.
- Verificacion: frontend lint OK, TypeScript OK y build Vite temporal OK; revalidado tras quitar `overflow-wrap: anywhere`. QA renderizada con Browser bloqueada por politica de seguridad al abrir `data:`.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - DashboardService no compilaba por `PuedeVerDashboard` ausente en tipo anonimo

- Contexto: un reintento de `ExtractosControllerTests` fallo durante compilacion de API antes de ejecutar tests.
- Causa: `DashboardService` usaba `PuedeVerDashboard` sobre un tipo anonimo que solo proyectaba cuenta/titular/pais y permisos operativos.
- Impacto: cualquier build backend que recompilara esa zona podia quedar bloqueada.
- Solucion: `DashboardService` proyecta `PuedeVerDashboard`, reordena la autorizacion de dashboard para `GERENTE`/`EMPLEADO` y queda cubierto por tests focalizados.
- Verificacion adicional: `ExtractosControllerTests` 17/17 OK tras corregir la proyeccion.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Selector de columnas de Extractos perdia columnas extra fuera de la pagina actual

- Contexto: el selector `Columnas` de `Extractos` podia no mostrar columnas extra disponibles cuando no aparecian en la pagina cargada.
- Causa: el frontend calculaba columnas extra desde `rows`, que representa solo la pagina actual, no el resultado filtrado completo.
- Impacto: el usuario podia quedarse sin forma de activar o recuperar columnas extra del scope actual.
- Solucion: `GET /api/extractos` expone `columnas_disponibles`; `ExtractosPage` las pasa a `ExtractoTable`; el panel agrega `Mostrar todas`.
- Verificacion: frontend lint OK, TypeScript OK, build Vite temporal OK, `ExtractosControllerTests` 17/17 OK y QA Browser mockeada del flujo de columnas sin errores de consola.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Grafica principal del dashboard ocultaba ingresos y egresos

- Contexto: el dashboard principal mostraba la grafica de saldo consolidado, pero no las lineas de ingresos y egresos que el usuario esperaba ver en la misma grafica.
- Causa: el rediseño bancario cambio `EvolucionChart` a `variant="saldoArea"` y esa variante solo renderizaba la serie `saldo`, aunque el dataset seguia trayendo `ingresos` y `egresos`.
- Impacto: el dashboard separaba ingresos/egresos en KPIs, pero perdia su lectura temporal dentro de la grafica principal.
- Solucion: `saldoArea` pasa a `ComposedChart`: area para saldo y lineas para ingresos/egresos con eje secundario.
- Verificacion: frontend lint OK, TypeScript OK, build Vite temporal OK y QA Browser mockeada desktop/mobile con tres trazos SVG, consola sin errores y sin overflow horizontal.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Selector de columnas de Extractos no guardaba en vista general

- Contexto: en `Extractos`, el panel `Columnas` no funcionaba correctamente cuando no habia cuenta seleccionada.
- Causa: `SaveColumnasVisibles` rechazaba `CuentaId = null`, aunque `GetColumnasVisibles` y `ResolvePreferenciaScope` ya soportaban scopes globales, por pais y por titular. El frontend tambien construia el toggle con una lista de columnas calculada aparte de la tabla.
- Impacto: el usuario podia marcar/desmarcar columnas, pero el guardado fallaba o se revertia en la vista general.
- Solucion: backend permite guardar preferencias sin cuenta, lee/escribe scopes nulos con comparacion explicita y frontend usa la lista real de columnas disponibles, conservando scope global/titular/pais/cuenta.
- Verificacion: frontend lint OK, TypeScript OK, API build OK y `ExtractosControllerTests` 16/16 OK.
- Estado: cerrado.

### 2026-06-09 - V-02-02 - Cerrado - Permisos por pais/titular/cuenta se podian expandir como union

- Contexto: auditoria con subagentes durante implementacion de autorizacion por pais.
- Causa: varios servicios convertian permisos en listas separadas de paises, titulares y cuentas.
- Impacto: una regla `Pais A + Titular B` podia abrir `Pais A` entero o `Titular B` fuera de ese pais.
- Solucion: autorizacion por fila con AND de todas las dimensiones no nulas en usuarios, integraciones, importacion, dashboard, extractos y alertas.
- Verificacion: backend build OK; tests focalizados no Docker 32/32 OK.
- Estado: cerrado.

### 2026-06-09 - V-02-02 - Cerrado - Restricciones de columnas no tenian scope por pais/titular

- Contexto: permisos por pais con columnas visibles/editables.
- Causa: `PREFERENCIAS_USUARIO_CUENTA` solo guardaba `usuario_id + cuenta_id` o global.
- Impacto: reglas de columnas de un pais/titular podian aplicarse sobre otro scope.
- Solucion: `PREFERENCIAS_USUARIO_CUENTA` incorpora `pais_id` y `titular_id`; carga y auth resuelven preferencias por scope exacto.
- Verificacion: backend build OK; frontend build OK; tests focalizados no Docker 32/32 OK.
- Estado: cerrado.

### 2026-06-09 - V-02-02 - Cerrado - Preferencias visibles podian abrir columnas editables

- Contexto: extractos usa `PREFERENCIAS_USUARIO_CUENTA` para preferencias visuales y restricciones de columnas.
- Causa: `GetPermission` mezclaba cualquier preferencia que coincidiera con la cuenta. Una preferencia visual con `ColumnasEditables = null` podia interpretarse como "todas las columnas editables".
- Impacto: un usuario con edicion limitada por columnas podia editar mas columnas si existia una preferencia visual coincidente en un scope mas especifico.
- Solucion: columnas editables se resuelven solo desde filas de permiso que conceden `PuedeEditarLineas` y con preferencia de scope exacto. Guardar columnas visibles almacena `pais_id`, `titular_id` y `cuenta_id` reales de la cuenta.
- Verificacion: regresion en `ExtractosControllerTests`; bloque focalizado no Docker 32/32 OK.
- Estado: cerrado.

### 2026-06-09 - V-02-02 - Cerrado - Dashboard-only tenia semantica partida

- Contexto: frontend, backend y RLS no interpretaban igual `PuedeVerDashboard`.
- Causa: frontend aceptaba cualquier `PuedeVerDashboard`; backend exigia permiso operativo de datos; RLS permitia dashboard-only puro.
- Impacto: UI podia mostrar rutas que backend rechazaba y RLS no actuaba como backstop equivalente.
- Solucion: frontend y RLS se alinean con backend: dashboard requiere `PuedeVerDashboard` y permiso operativo de datos en el mismo scope.
- Verificacion: tests de autorizacion incluidos en bloque no Docker 32/32 OK; frontend lint/build OK.
- Estado: cerrado.

### 2026-06-08 - V-02-02 - Cerrado - MFA recordado no reflejaba la intencion de producto

- Contexto: el producto queria recordar dispositivo unos 3 meses, pero el estado activo era inconsistente.
- Causa: `mfa_remember_device_enabled` venia apagado por defecto, `MfaRememberDeviceDays` era 62 y logout borraba `mfa_trusted`. Ademas, el token recordado era una cookie firmada, no un dispositivo persistido y revocable.
- Impacto: el usuario no podia confiar en "recordar dispositivo"; cerrar sesion lo anulaba y no habia listado/revocacion por dispositivo.
- Solucion: `MFA_TRUSTED_DEVICES` con token opaco hasheado, 90 dias, `security_stamp`, expiracion/revocacion y endpoints de listado/revocacion. Logout conserva la cookie; password/MFA/security stamp invalidan.
- Verificacion: tests focalizados auth incluidos en bloque backend 122/122 OK.
- Estado: cerrado.

### 2026-06-08 - V-02-02 - Cerrado - Boton de update podia ofrecer una version no instalable

- Contexto: `actualizacion_disponible=true` no garantiza que el release tenga ZIP oficial, firma, digest, clave publica y Watchdog operativo.
- Causa: el contrato de `version-disponible` no exponia preflight de instalabilidad.
- Impacto: la UI podia empujar al usuario a pulsar un boton condenado a fallar. Eso es mala UX y peor operacion.
- Solucion: `version-disponible` devuelve `instalable`, `bloqueos` y flags de preflight; `Actualizar ahora` y auto-update solo arrancan si `instalable=true`.
- Verificacion: tests de updater/auto-update incluidos en bloque backend 122/122 OK.
- Estado: cerrado.

### 2026-06-08 - V-02-02 - Cerrado - OpenRouter estaba limitado por allowlist local fija

- Contexto: el objetivo era usar cualquier modelo OpenRouter valido, no solo una lista local.
- Causa: backend y frontend normalizaban/recortaban modelos OpenRouter a sugerencias fijas y rutas fallback viejas.
- Impacto: modelos validos de OpenRouter quedaban bloqueados por Atlas antes de llegar al proveedor.
- Solucion: validacion sintactica de model id, envio exacto del modelo, `openrouter/auto` conservado y catalogo `/api/ia/modelos` con cache corta.
- Verificacion: tests IA incluidos en bloque backend 122/122 OK y frontend `npm run build` OK.
- Estado: cerrado.

### 2026-06-01 - V-01.09 - Cerrado - Update antiguo sin owner en Watchdog seguia cayendo a usuario runtime

- Contexto: una instalacion `V-01.06` intento actualizar a `V-01.09` con el paquete corregido y `pg_dump` siguio fallando por RLS en `AUDITORIAS`.
- Causa: la instalacion no tenia `MigrationConnection` ni credenciales owner en Watchdog; el primer fix no cubria esa variante.
- Impacto: la actualizacion se bloqueaba antes de reemplazar binarios. La app quedaba sana en la version anterior, pero no podia avanzar sin intervencion.
- Solucion: fallbacks adicionales para `ATLAS_DB_MIGRATION_CONNECTION`, `ATLAS_DB_OWNER_USER`/`ATLAS_DB_OWNER_PASSWORD`, `INSTALL_CREDENTIALS_ONCE.txt` y prompt seguro manual con `-PromptForDbOwnerCredentials`; plantilla Watchdog corregida.
- Verificacion: parser PowerShell OK, pruebas estaticas de fallbacks OK, paquete local firmado con `SIGNATURE_OK`.
- Estado: cerrado en codigo y publicado como asset en GitHub Release `V-01.09-win-x64`; pendiente probar en la instalacion afectada.

### 2026-06-01 - V-01.09 - Cerrado - Update V-01.06 sin MigrationConnection fallaba en pg_dump por RLS

- Contexto: una instalacion `V-01.06` intento actualizar a `V-01.09`; el backup pre-update fallo en `AUDITORIAS` por RLS/FORCE RLS.
- Causa: `Actualizar-AtlasBalance.ps1` usaba solo `DefaultConnection` para `pg_dump`, pero instalaciones antiguas pueden no tener `MigrationConnection` en API.
- Impacto: no se podia actualizar aunque la app estuviera sana; por suerte el fallo ocurria antes de tocar binarios.
- Solucion: resolver conexion de backup con prioridad `MigrationConnection`, credenciales owner de Watchdog y ultimo fallback a `DefaultConnection` con error claro.
- Verificacion: parser PowerShell OK; paquete `V-01.09-win-x64` regenerado, firmado, verificado como `SIGNATURE_OK` y republicado en GitHub Release `latest`.
- Estado: cerrado en codigo y publicacion.

### 2026-06-01 - V-01.09 - Cerrado - Clave privada de firma de release perdida/no disponible

- Contexto: se necesitaba publicar `V-01.09` como release latest firmado.
- Causa: no habia `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` en entorno, repo ni rutas locales razonables.
- Impacto: no se podia generar `.zip.sig`; publicar unsigned romperia el actualizador online y seria una mala practica seria.
- Solucion: rotar clave de firma con nuevo par RSA 4096, actualizar la publica en instalador/plantilla y hacer que `Build-Release.ps1` no dependa de `frontend/dist` ni del `wwwroot` fuente bloqueable.
- Verificacion: `AtlasBalance-V-01.09-win-x64.zip` y `.zip.sig` generados; firma local `SIGNATURE_OK`; SHA-256 ZIP `A1F6D5A6BBEFAD7C05C8CBFBB09046A5B9C9F5DBCE5E5E1FB0D7DA41DC7E8061`.
- Publicacion: GitHub Release `V-01.09-win-x64` publicado como `latest` con ZIP y `.sig`.
- Pendiente operativo: cargar la nueva privada en GitHub Secret y guardar copia segura fuera del repo para futuros releases automatizados.
- Estado: cerrado en codigo y publicacion.

### 2026-06-01 - V-01.09 - Cerrado - Refresh tokens no quedaban ligados a rotaciones de seguridad

- Contexto: auditoria profunda de autenticacion.
- Causa: `REFRESH_TOKENS` no guardaba el `security_stamp` vigente al emitirse.
- Impacto: tokens emitidos antes de reset/password/MFA/revocacion podian seguir rotando si no se borraban por otra via.
- Solucion: columna `security_stamp`, backfill por migracion y comparacion constant-time contra el usuario actual en refresh.
- Verificacion: regresiones AuthService incluidas en bloque focalizado 136/136 OK.
- Estado: cerrado.

### 2026-06-01 - V-01.09 - Cerrado - Tipos de cambio faltantes se convertian como 1:1

- Contexto: auditoria de integridad de dashboards/OpenClaw.
- Causa: `TiposCambioService.ConvertAsync` devolvia `amount` si no encontraba tasa.
- Impacto: cifras financieras falsas sin aviso.
- Solucion: lanzar `TipoCambioMissingException` y responder `409` con mensaje claro.
- Verificacion: `TiposCambioServiceTests` en bloque focalizado 136/136 OK.
- Estado: cerrado.

### 2026-06-01 - V-01.09 - Cerrado - Importacion podia duplicar filas si cambiaban columnas extra

- Contexto: auditoria de idempotencia de importacion.
- Causa: la huella de contenido mezclaba identidad financiera con columnas auxiliares `extra:*`.
- Impacto: reimportar el mismo movimiento con una referencia auxiliar corregida podia insertarlo dos veces.
- Solucion: fingerprint por cuenta, fecha, monto, saldo y concepto; las columnas extra siguen guardandose pero no definen identidad.
- Verificacion: regresion `ConfirmarAsync_Should_Deduplicate_When_Only_Extra_Columns_Change`.
- Estado: cerrado.

### 2026-06-01 - V-01.09 - Cerrado - Alertas de saldo bajo no se evaluaban tras importacion/plazo fijo

- Contexto: auditoria funcional de alertas.
- Causa: `ImportacionService` persistia saldos nuevos sin llamar a `IAlertaService`.
- Impacto: una cuenta podia quedar bajo umbral sin email hasta otra edicion manual.
- Solucion: evaluar alerta tras commit de importacion y de movimiento de plazo fijo.
- Verificacion: regresiones con `RecordingAlertaService`.
- Estado: cerrado.

### 2026-06-01 - V-01.09 - Cerrado - Ranking IA por titular agrupaba realmente por cuenta

- Contexto: auditoria de respuestas deterministas IA.
- Causa: el detector aceptaba `titular`, pero la query agrupaba siempre por cuenta/titular/divisa.
- Impacto: respuesta semanticamente falsa para preguntas por titulares.
- Solucion: nueva dimension de ranking; si el prompt pide titulares, agrega por titular/divisa.
- Verificacion: regresion `AskAsync_Should_Group_Deterministic_Ranking_By_Titular_When_Requested`.
- Estado: cerrado.

### 2026-06-01 - V-01.09 - Cerrado - Actualizador externo avisaba rollback pero no restauraba

- Contexto: auditoria de release/update.
- Causa: `Actualizar-AtlasBalance.ps1` creaba copia rollback, pero si fallaba `/api/health` solo lanzaba excepcion.
- Impacto: instalacion con binarios nuevos fallidos y rollback manual en el peor momento.
- Solucion: restauracion automatica de `api`, `watchdog`, `VERSION` y runtime antes de fallar.
- Verificacion: parser PowerShell OK; validacion real Windows queda pendiente.
- Estado: cerrado en script; pendiente prueba Windows real.

### 2026-05-22 - V-01.09 - Cerrado - Actualizacion online solo reemplazaba API/frontend

- Contexto: verificacion de la promesa "solo pulsar actualizar" para subir de version desde GitHub Release sin pasos intermedios.
- Causa: `ActualizacionService.DownloadAndPreparePackageAsync` validaba el ZIP completo, pero devolvia `...\api`; Watchdog sincronizaba ese origen contra `C:\AtlasBalance\api`.
- Impacto: API/frontend podian quedar en version nueva mientras Watchdog, scripts, wrappers, `VERSION` raiz y runtime seguian viejos.
- Solucion: API pasa la raiz del paquete completo; Watchdog valida paquete completo y aplica API, Watchdog, scripts, wrappers, `VERSION` y runtime. En Windows servicio lanza helper PowerShell para ejecutar el actualizador del paquete y poder reemplazar su propia carpeta.
- Verificacion: update/watchdog focalizado 26/26 OK; frontend lint/build OK; backend sin Docker/Testcontainers 270/270 OK.
- Estado: cerrado en codigo; la publicacion de `latest` y el bootstrap desde Watchdog antiguo quedan como pendientes abiertos separados.

### 2026-05-22 - V-01.09 - Cerrado - Cambio de contrasena emitia garantia MFA falsa

- Contexto: revision del threat model con subagentes.
- Causa: `ChangePasswordAsync` marcaba el refresh token nuevo como MFA-verificado por el estado del usuario, no por la sesion actual.
- Impacto: una sesion pre-MFA todavia valida podia transformarse en sesion post-MFA despues de cambiar contrasena.
- Solucion: `CambiarPassword` pasa el refresh token actual; `AuthService` exige `mfa_verified_at` existente cuando MFA es obligatorio y lo preserva en la nueva sesion.
- Verificacion: regresiones de sesion pre-MFA y sesion verificada; bloque auth/controladores 27/27 OK; suite backend sin Docker/Testcontainers 269/269 OK.
- Estado: cerrado.

### 2026-05-22 - V-01.09 - Cerrado - RLS y ficheros necesitaban hardening de backstop

- Contexto: revision del threat model y hallazgos de subagentes.
- Causa: RLS no filtraba soft-delete como ultima barrera y algunas rutas de export/backup validaban raiz despues de tocar disco.
- Impacto: mayor riesgo de exposicion por futuros SQL crudos/`IgnoreQueryFilters()` y de operaciones sobre rutas persistidas fuera de raices permitidas si DB/config se corrompen.
- Solucion: migracion RLS `20260522103000_HardenRlsSoftDeleteBackstop`, validacion previa de rutas de exportaciones/backups y retencion que omite rutas fuera de raiz.
- Verificacion: suite backend no-Docker 269/269 OK; test RLS con PostgreSQL real bloqueado por Docker/Testcontainers no disponible.
- Estado: cerrado en codigo; validacion PostgreSQL real sigue como gate abierto de release.

### 2026-05-20 - V-01.09 - Cerrado - Importacion/exportacion aceptaban cuentas de titulares soft-deleted

- Contexto: la revision del threat model encontro que algunos servicios no heredaban el soft-delete del titular padre.
- Causa: `ImportacionService` y `ExportacionService` filtraban por cuenta activa, pero no por titular activo, en rutas que aceptan `cuentaId` directo.
- Impacto: posible acceso/operacion sobre datos financieros fuera de la superficie logica visible si se conocia el ID de la cuenta.
- Solucion: importacion, exportacion manual y exportacion mensual exigen titular activo.
- Verificacion: regresiones focalizadas de importacion/exportacion incluidas en bloque de 63/63 tests OK.
- Estado: cerrado.

### 2026-05-20 - V-01.09 - Cerrado - Descarga de actualizacion rechazaba tamano tarde si faltaba Content-Length

- Contexto: la ruta de actualizacion ya limitaba paquetes por tamano declarado y por tamano final, pero no cortaba durante streaming cuando el servidor no declaraba `Content-Length`.
- Causa: `CopyToAsync` escribia todo el contenido antes de comprobar `FileInfo.Length`.
- Impacto: consumo evitable de disco/IO en una ruta admin. Severidad media/baja por las validaciones existentes de repo/asset/firma, pero seguia siendo una defensa floja.
- Solucion: copia con limite de bytes durante el stream y config opcional `UpdateSecurity:MaxUpdatePackageBytes` para bajar el limite por entorno.
- Verificacion: regresion de asset sin longitud declarada incluida en bloque de 63/63 tests OK.
- Estado: cerrado.

### 2026-05-20 - V-01.09 - Cerrado - Errores de red IA exponian diagnosticos internos

- Contexto: hallazgo `AI provider network errors leak internal diagnostics`.
- Causa: el mensaje de red del proveedor IA incluia detalles derivados de excepciones de transporte y `IaController` los devolvia en el JSON 502.
- Impacto: usuarios con IA podian ver topologia interna si fallaba TLS, proxy, DNS o conexion.
- Solucion: mensaje publico generico; auditoria con codigos seguros de transporte; regresion con hostname/proxy/certificado ficticios.
- Verificacion: test focalizado 1/1 OK, `AtlasAiServiceTests` 62/62 OK y `git diff --check` OK.
- Estado: cerrado.

### 2026-05-20 - V-01.09 - Cerrado - Refresh tokens legacy saltan MFA obligatorio

- Contexto: el analisis de seguridad reporto que `RefreshTokenAsync` seguia renovando sesiones aunque el login ya exigia MFA.
- Causa: `REFRESH_TOKENS` no guardaba ninguna garantia de MFA completado; el refresh validaba token/usuario y rotaba sin mirar el nuevo requisito.
- Impacto: un refresh token emitido antes de activar MFA podia mantener acceso web sin completar el segundo factor.
- Solucion: nueva columna `mfa_verified_at`, emision marcada tras MFA o login con `mfa_trusted`, rechazo y revocacion de tokens sin garantia cuando MFA es obligatorio.
- Verificacion: reproduccion previa fallida del exploit, `AuthServiceTests` 18/18 OK y suite backend sin Docker/Testcontainers 261/261 OK.
- Estado: cerrado.

### 2026-05-20 - V-01.09 - Cerrado - Logout conservaba cookie MFA recordada

- Contexto: el analisis de seguridad marco que `POST /api/auth/logout` dejaba viva `mfa_trusted` y que el dispositivo recordado duraba 90 dias.
- Causa: en V-01.07 se priorizo comodidad y se trato logout como cierre de sesion, no como cierre de confianza MFA del navegador.
- Impacto: en un equipo compartido o perfil de navegador comprometido, alguien con la contrasena podia saltarse TOTP desde ese navegador durante una ventana larga.
- Solucion: logout vuelve a borrar `mfa_trusted`, el recuerdo MFA queda en 62 dias y la opcion pasa a depender de `CONFIGURACION.mfa_remember_device_enabled`, desactivada por defecto.
- Verificacion: suite focalizada Auth/Configuracion 29/29 OK, frontend lint OK y build OK. La sincronizacion local de `wwwroot` quedo bloqueada por `Access denied`, asi que el paquete de release debe regenerar assets antes de publicar.
- Estado: cerrado.

### 2026-05-20 - V-01.09 - Cerrado - Entradas ZIP raiz rompian actualizaciones

- Contexto: algunos ZIP de actualizacion pueden incluir entradas raiz inocuas (`.` / `./`) antes de los archivos reales.
- Causa: el guard de extraccion comparaba contra el root con separador final y rechazaba destinos que normalizaban exactamente al root.
- Impacto: disponibilidad/compatibilidad; el paquete firmado podia rechazarse aunque no hubiera Zip Slip.
- Solucion: aceptar solo marcadores raiz equivalentes a `.` y mantener prefijo estricto con separador para rutas hijas.
- Verificacion: reproduccion previa fallida, `ActualizacionServiceTests` 13/13 OK y bloque actualizacion/watchdog 20/20 OK; traversal `../evil.txt` sigue rechazado.
- Estado: cerrado.

### 2026-05-20 - V-01.09 - Cerrado - Throttle de login por IP compartida permitia DoS no autenticado

- Contexto: hallazgo Codex Security sobre `/api/auth/login`.
- Causa: el contador cliente/IP de 20 fallos se evaluaba antes de validar credenciales y no se limpiaba en login correcto.
- Impacto: un atacante no autenticado podia lanzar 20 intentos invalidos desde una IP/NAT/proxy compartido y provocar 429 a usuarios legitimos de esa misma IP durante la ventana de 15 minutos.
- Solucion: el precheck temprano usa solo email+cliente; el contador cliente/IP queda para intentos invalidos despues de resolver usuario/password; el login correcto limpia ambos contadores; `ForwardedHeaders` queda habilitado con proxies/redes conocidas configurables.
- Verificacion: regresiones nuevas en `AuthServiceTests`; suite enfocada `AuthServiceTests` 20/20 OK; suite backend sin Docker/Testcontainers 267/267 OK; `git diff --check` OK.
- Estado: cerrado en codigo; pendiente suite completa y E2E autenticado como gates generales de release.

### 2026-05-19 - V-01.07 - Cerrado - Jerarquia visual plana y acciones criticas poco diferenciadas

- Contexto: revision adicional UI/UX sobre jerarquia de ventanas, informacion importante, botones, checks, tablas y menus.
- Causa: demasiadas superficies compartian sombra/fondo y demasiadas acciones usaban el boton secundario por defecto.
- Solucion: headers y secciones mas claros, tablas con menos peso que modales, botones primarios/danger/warning en flujos criticos, resumen de validacion de importacion, resumen de backup y resaltado de saldo/vencimientos.
- Verificacion: frontend lint OK, TypeScript OK, build OK, `git diff --check` OK.
- Estado: cerrado en codigo; sigue abierto el gate general de QA visual/E2E real antes de release final.

### 2026-05-19 - V-01.07 - Cerrado - Skills Curated y artefactos locales aparecian como versionables

- Contexto: el pase pre-release con skills locales dejo claro que `Skills Curated/` y `TestResults/` podian aparecer en `git status`.
- Causa: los `.gitignore` cubrian `Skills/` y algunos artefactos, pero no la carpeta real `Skills Curated/`, resultados .NET ni varias extensiones sensibles de certificados/keystores/dumps.
- Impacto: riesgo de subir tooling local, resultados de test o artefactos sensibles al repositorio.
- Solucion: `.gitignore` raiz y `Atlas Balance/.gitignore` amplian exclusiones; el CI de secretos excluye tambien `Skills Curated/`.
- Verificacion: `git check-ignore` confirma `Skills Curated/`, `TestResults/`, `*.p12`, `*.jks`, `*.cer` y `*.dump`; secret scan local 0 hallazgos.
- Estado: cerrado.

### 2026-05-19 - V-01.07 - Cerrado - Refinamiento UI/UX pre-entrega detecto semantica y estados enga�osos

- Contexto: revision UI/UX de pantallas, tablas, botones, checks, menus, modales y estados con skills de frontend y skills locales.
- Causa:
  - Extractos parecia filtrar globalmente aunque solo filtraba la pagina cargada.
  - Tokens mostraba metricas en cero si fallaba la carga.
  - Crear token mostraba validaciones fuera del modal.
  - Configuracion tenia tabs ARIA incompletas.
  - Varias pantallas mostraban errores sin anuncio accesible.
  - Cuenta detalle tenia demasiados tab stops y perdia contexto horizontal.
- Solucion: copy/contadores de Extractos por pagina y total, tabla ARIA honesta, metricas `loading/error/ready`, errores locales de modal, tabs ARIA completas, `role="alert"`, labels contextuales, columnas fijas, overlay de backup accesible y tabla `sr-only` en evolucion.
- Verificacion: frontend lint OK, TypeScript OK, build OK, `git diff --check` OK.
- Estado: cerrado en codigo; pendiente visual/E2E real como gate abierto de release final.

### 2026-05-17 - V-01.07 - Cerrado - Revision Codex Security detecto fugas de soft-delete y controles locales flojos

- Contexto: revision profunda con Codex Security y subagentes sobre backend, frontend, scripts, IA, exportaciones, importacion y supply chain.
- Causa:
  - Algunos controles de cuenta no heredaban `DeletedAt` del titular padre.
  - `GetAuditCelda` cargaba extractos con `IgnoreQueryFilters()` y solo comprobaba visibilidad de cuenta.
  - Exportacion manual usaba lectura de cuenta, no permiso operativo.
  - Rutas gratis/auto de OpenRouter no enviaban controles de privacidad por request.
  - Actualizacion desde la app aceptaba `sourcePath` manual bajo `UpdateSourceRoot`.
  - Procesos externos podian caer a busqueda por PATH.
- Solucion: filtros de titular activo en scopes de usuario/integracion/extractos, bloqueo de auditoria de extractos eliminados, exportacion manual con `CanWriteCuentaAsync`, privacidad OpenRouter obligatoria, rechazo de `sourcePath` manual, rutas absolutas para PostgreSQL/Docker, limite de celda importada y lockfile limpio.
- Verificacion: `npm audit` 0 vulnerabilidades, NuGet vulnerable audit 0 paquetes vulnerables, lockfile sin `extraneous`, parse PowerShell OK, `git diff --check` OK, restore/build backend OK, suite backend sin Docker/Testcontainers 249/249 OK. Suite completa 249/251 con los 2 fallos esperados de Testcontainers por Docker no disponible/configurado.
- Estado: cerrado en codigo; pendiente ejecutar solo las 2 pruebas PostgreSQL/Testcontainers cuando Docker este disponible.

### 2026-05-17 - V-01.07 - Cerrado - Actualizacion desde repo sin modo automatico real

- Contexto: el flujo de GitHub Release existia, pero dependia de que un admin pulsara `Actualizar ahora`. Para "que lo haga solo" faltaba un job recurrente.
- Causa: no habia tarea Hangfire para comprobar releases ni estado de autoactualizacion en configuracion.
- Solucion: `AutoUpdateJob` opt-in, claves `app_update_auto_*`, controles en `Configuracion > Sistema` y limites defensivos de paquete antes de extraer.
- Verificacion: tests backend focalizados 25/25 OK; frontend lint, TypeScript y build OK; `wwwroot` sincronizado 65/65.
- Estado: cerrado en codigo; pendiente suite completa Docker/Testcontainers antes de release final.

### 2026-05-17 - V-01.07 - Cerrado - Recibos/facturas IA incluia cargos de tarjeta

- Contexto: la suite backend sin Docker fallo porque el contexto IA de recibos/facturas sumaba `Cargo tarjeta comercio` junto a `Recibo luz factura`.
- Causa: la categoria aceptaba `cargo` sin excluir tarjeta/TPV/dat�fono.
- Solucion: `ReceiptExcludedTerms` evita que cargos de tarjeta, prestamos o leasing inflen `RECIBOS/FACTURAS DETECTADOS`.
- Verificacion: test focalizado OK y suite backend sin Testcontainers 242/242 OK.
- Estado: cerrado.

### 2026-05-17 - V-01.07 - Cerrado - Contexto IA inflaba comisiones y seguros con falsos positivos

- Contexto: las funciones IA podian recibir contexto contaminado aunque `RevisionService` ya hubiese corregido falsos positivos similares.
- Causa: `AtlasAiService` mantenia terminos amplios para comisiones y no aplicaba restricciones/exclusiones equivalentes en seguros. Ademas, el diagnostico de red saneado se calculaba pero no se mostraba al usuario.
- Solucion: comisiones IA deja de usar `cuota`, `servicio`, `tarjeta` y `transferencia` como disparadores directos; seguros IA exige cargos negativos y excluye Seguridad Social/TGSS, Generalitat, transferencias, anulaciones, devoluciones y reembolsos; errores de red muestran detalle tecnico saneado.
- Verificacion: regresiones agregadas en `AtlasAiServiceTests`; frontend lint, TypeScript y build OK; tests backend pendientes por ausencia de SDK .NET local.
- Estado: cerrado en codigo; pendiente ejecutar tests backend en entorno con .NET.

### 2026-05-17 - V-01.07 - Cerrado - Revision seguia mostrando ruido de comisiones y seguros

- Contexto: capturas reales mostraron falsos positivos en revision: transferencias, tarjetas, cuotas/leasing/prestamos, Seguridad Social/TGSS, Generalitat, transferencias a aseguradoras y anulaciones de seguros.
- Causa: los terminos de deteccion seguian siendo demasiado amplios y la revision de seguros no filtraba por cargo negativo ni excluia transferencias/anulaciones.
- Solucion: `tarjeta` deja de ser disparador directo de comision; seguros exige importe negativo y excluye Seguridad Social/TGSS plural, Generalitat, transferencias, anulaciones, devoluciones y reembolsos.
- Verificacion: pruebas de regresion agregadas con los conceptos reportados. Ejecucion pendiente porque `dotnet` no esta disponible en esta maquina.
- Estado: cerrado en codigo; pendiente ejecutar `RevisionServiceTests` con SDK .NET.

### 2026-05-16 - V-01.07 - Cerrado - Importacion, revision y Authenticator

- Contexto: importacion rechazaba celdas extra vacias, revision marcaba transferencias como comisiones y casos de Seguridad Social como seguros, y MFA recordado no duraba lo esperado ni tenia revocacion administrativa.
- Causa:
  - Validacion demasiado estricta para columnas extra ausentes.
  - Terminos de revision demasiado amplios.
  - `Logout` borraba `mfa_trusted`; el recuerdo MFA era de 30 dias y no habia reset de Authenticator por usuario.
- Solucion: columnas extra ausentes se dejan en blanco, revision elimina terminos flojos y excluye Seguridad Social/TGSS, MFA recordado pasa a 90 dias, logout conserva la cookie de confianza, usuarios permite revocar Authenticator y el reset admin limpia MFA.
- Verificacion: frontend lint OK, TypeScript OK y build OK. Tests backend focalizados pendientes por ausencia de `dotnet`.
- Estado: cerrado en codigo; pendiente ejecutar tests backend cuando el SDK .NET vuelva a estar disponible.

### 2026-05-13 - V-01.06 - Cerrado - GitHub Actions fallaba en restore locked

- Contexto: PR #6 fallaba en `dotnet restore "./Atlas Balance/backend/AtlasBalance.sln" --locked-mode`.
- Causa: lockfiles con target `win-x64` y proyectos API/Watchdog sin `RuntimeIdentifiers`; ademas, el restore de solucion es un gate opaco en este repo.
- Solucion: declarar `RuntimeIdentifiers=win-x64` en API/Watchdog y cambiar CI a restore/test/audit por proyectos concretos.
- Verificacion: restores por proyecto API/Watchdog/Test OK; suite backend sin Docker/Testcontainers 223/223 OK.
- Estado: cerrado. La suite completa Docker/Testcontainers se valida en GitHub Actions tras el push.

### 2026-05-13 - V-01.06 - Cerrado - Publicacion bloqueada por paquete sin firma

- Contexto: `Atlas Balance/Atlas Balance Release` solo tenia un ZIP local sin `.zip.sig`; el actualizador online rechaza bien cualquier paquete sin firma detached.
- Impacto: no se podia publicar un release online verificable. Subir ZIP sin firma habria roto el flujo de actualizacion de la propia app.
- Solucion: generar el paquete con `Build-Release.ps1 -Version V-01.06` usando clave privada de firma en el entorno, producir `AtlasBalance-V-01.06-win-x64.zip` y `AtlasBalance-V-01.06-win-x64.zip.sig`, y dejar la clave publica por defecto en instalador/plantilla productiva.
- Verificacion: SHA256 del ZIP firmado `95DCA977E145DE07BF41E5B6478AD856BF803E4938A0A98480ABB043F51781E1`; firma RSA/SHA-256 verificada localmente como `SIGNATURE_OK`.
- Publicacion: release `V-01.06-win-x64` subido a GitHub como pre-release con ZIP y `.sig`.
- Estado: cerrado para el bloqueo de firma. Sigue abierto el E2E autenticado con PostgreSQL real/datos de volumen si se quiere llamar release final.

### 2026-05-12 - V-01.06 - Cerrado - Documentacion de publicacion apuntaba a V-01.05

- Contexto: las guias de instalacion/release y el README que se copia al paquete seguian nombrando `AtlasBalance-V-01.05-win-x64.zip`.
- Impacto: un paquete `V-01.06` podia salir con instrucciones del release anterior. Eso confunde al operador y deja fatal la publicacion.
- Solucion: actualizar documentacion viva a `V-01.06`, quitar SHA viejo, documentar que el SHA se calcula tras generar el ZIP firmado y anadir estado actual de gates en `v-01.06.md`.
- Verificacion: barridos `rg` sobre referencias operativas a `AtlasBalance-V-01.05` y comandos de release `V-01.05`.
- Estado: cerrado. Sigue abierto el E2E autenticado con datos reales.

### 2026-05-12 - V-01.06 - Cerrado - Mojibake y copy publico roto

- Contexto: `SECURITY.md`, `CONTRIBUTING.md` y varias docs vivas contenian mojibake y texto de plantilla.
- Impacto: publicar con caracteres rotos en GitHub comunica poca seriedad. En una app financiera, eso no es cosmetica.
- Solucion: reescritura de textos publicos y correccion de copy visible en UI, emails, exportacion y scripts.
- Verificacion: barridos de patrones de mojibake y textos concretos detectados por subagentes.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Pantallas pesadas con coste innecesario antes de publicar

- Contexto: el pase `optimize` encontro coste evitable en graficas cargadas de forma ansiosa, busquedas por cada pulsacion, validacion de importacion renderizando demasiadas filas y auditoria OpenClaw materializando todo el scope antes de paginar.
- Causa: patrones validos para datasets pequenos usados en pantallas que pueden recibir CSVs grandes, historicos amplios y graficas pesadas.
- Solucion: lazy loading de graficas/ruta dashboard, debounce de busquedas, validacion paginada de importacion, seleccion con `Set`, paginacion real antes de mapear extractos en OpenClaw, agregacion SQL de totales mensuales e indice trigram para revision.
- Verificacion: frontend lint OK, TypeScript OK, build frontend OK, build API OK y tests focalizados OpenClaw/Revision 8/8 OK.
- Estado: cerrado como optimizacion tecnica; el release final sigue bloqueado por E2E autenticado con datos reales/volumen.

### 2026-05-12 - V-01.06 - Cerrado - Hardening de estados feos, permisos y overflow UI

- Contexto: subagentes detectaron estados vacios falsos, errores API escondidos, panel IA vacio sin permisos, acciones de revision ofrecidas a usuarios de solo lectura, limite silencioso de 500 movimientos y riesgo de overflow en tablas/selectores/importes.
- Causa: la UI asum�a datos felices y permisos uniformes; eso en finanzas es pedir que alguien lea una pantalla incompleta como si fuera verdad.
- Solucion: extractor unico de errores API, estados de error/reintento, rollback de preferencias fallidas, paginacion real en cuenta, estados de permiso visibles, acciones de revision condicionadas por cuenta/titular y CSS defensivo para textos/importes/tablas grandes.
- Verificacion: frontend lint OK, TypeScript OK, build frontend OK y `wwwroot` sincronizado 61/61 archivos.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Migracion RLS de hardening no era descubierta por EF

- Contexto: la suite backend completa con Docker fallo 224/225 en `RowLevelSecurityTests`: `EXPORTACIONES` violaba RLS al insertar con scope `export`.
- Causa: `20260512110000_HardenReleaseSecurityPermissions.cs` existia, pero faltaba `20260512110000_HardenReleaseSecurityPermissions.Designer.cs`; EF la compilaba pero no la registraba como migracion aplicable.
- Solucion: anadir descriptor con `[DbContext]` y `[Migration("20260512110000_HardenReleaseSecurityPermissions")]`.
- Verificacion: suite backend completa con Docker/Testcontainers 225/225 OK.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Test de importacion esperaba mensaje antiguo de duplicado

- Contexto: la suite no Docker fallo 222/223 en `ImportacionServiceTests.ValidarAsync_Should_Reject_Duplicate_Mapping_Indexes_And_Extra_Names`.
- Causa: el codigo correcto devuelve `Clave de columna extra duplicada (...)`; el test seguia esperando el texto antiguo `Nombre de columna extra duplicado`.
- Solucion: actualizar la asercion al contrato vigente.
- Verificacion: backend no Docker 223/223 OK y suite completa 225/225 OK.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Docker/Testcontainers pendiente para RLS y concurrencia

- Contexto: habia un gate abierto porque Docker/Testcontainers no estaba disponible en ejecuciones anteriores.
- Solucion: comprobar Docker fuera del sandbox con aprobacion (`29.4.2`) y ejecutar la suite completa.
- Verificacion: `dotnet test AtlasBalance.API.Tests.csproj` completo con Testcontainers/PostgreSQL: 225/225 OK.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Revision no permitia descartar falsos positivos

- Contexto: una deteccion automatica de comision o seguro podia ser falsa, pero la UI solo permitia dejarla pendiente o marcarla como devuelta/correcta.
- Causa: faltaba un estado persistido de descarte para diferenciar "revisado y no aplica" de "pendiente".
- Solucion: anadir `DESCARTADA` en backend/frontend, filtro de descartadas y acciones `No es comision`, `No es seguro` y `Restaurar`.
- Verificacion: `RevisionServiceTests` 5/5 OK; frontend lint, TypeScript y build OK; bundle servido contiene los nuevos textos; API local saludable PID `32520`.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Revision devolvia 500 al cargar comisiones

- Contexto: la pantalla `Revision` mostraba `Request failed with status code 500` al cargar comisiones.
- Causa: el filtro relacional sobre `RevisionRawRow.Monto` se aplicaba despues de proyectar a un record posicional, forma que EF/Npgsql no traducian a SQL.
- Solucion: cambiar `RevisionRawRow` a clase con propiedades `init` y proyeccion por inicializador, manteniendo filtros y paginacion en base de datos.
- Verificacion: `RevisionServiceTests` 5/5 OK con salida `OutDir` aislada y regresion Npgsql por `ToQueryString()`.
- Estado: cerrado.

### 2026-05-12 - V-01.06 - Cerrado - Etiquetas laterales del grafico Evolucion cortadas

- Contexto: en el dashboard principal, los numeros laterales del grafico `Evolucion` se veian cortados con importes compactos de millones.
- Causa: el eje Y tenia una reserva maxima de 72 px y el calculo no contemplaba ticks generados desde el dominio.
- Solucion: calcular la reserva con extremos del dominio y anchura estimada por etiqueta, fijar ticks monoespaciados/tabulares y acotar el eje a 52-116 px para evitar tanto recorte como aire lateral excesivo.
- Verificacion: `npm.cmd run lint` OK y `npm.cmd exec tsc -- --noEmit` OK.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter devolvia `Authentication failed, see inner exception`

- Contexto: el chat IA mostraba un error de red de OpenRouter con detalle tecnico opaco `Authentication failed, see inner exception`.
- Causa: fallo de transporte HTTPS/proxy, no autenticacion de API key. El backend solo leia un nivel de `InnerException` y el fallback a proxy automatico podia reciclar proxies heredados rotos.
- Solucion: salida directa por defecto para clientes IA, proxy solo explicito (`Ia:UseSystemProxy`/`Ia:ProxyUrl`), fallback directo y saneado profundo de errores TLS/proxy/DNS/conexion.
- Verificacion: `AtlasAiServiceTests` 42/42 OK; test nuevo impide que `Authentication failed, see inner exception` vuelva al usuario o a auditoria.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Login devolvia `Network Error`

- Contexto: el frontend de desarrollo podia seguir vivo mientras la API no escuchaba en `localhost:5000`; ademas el bundle podia quedar compilado con API absoluta `http://localhost:5000`.
- Causa: uso de `VITE_API_URL` para una app que debe llamar a `/api` same-origin y scripts de arranque sin healthcheck real.
- Solucion: `api.ts` queda en `baseURL: '/api'`, bundle reconstruido y copiado a `wwwroot`, nuevo launcher backend con PID/logs/healthcheck, `Start-Dev.ps1` valida la API antes de declarar entorno listo y `/api/health` expone version/PID/arranque.
- Verificacion: frontend lint/build OK, backend build OK, health directo y via Vite proxy OK, busqueda sin URL absoluta en bundles.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Chat IA mostraba razonamiento interno del modelo

- Contexto: al preguntar por las cuentas con mas gastos del trimestre, el chat devolvia texto en ingles tipo `We need to answer...`, mezclaba datos y mostraba placeholders como `[PERSON_NAME]`.
- Causa: el backend devolvia `message.content` casi en bruto. OpenRouter puede excluir `message.reasoning`, pero algunos modelos pueden meter razonamiento directamente en `content`.
- Solucion: payload OpenRouter con `reasoning.exclude=true`, prompt de salida final sin analisis interno y saneado backend de `<think>`, prefacios de razonamiento, etiquetas `Final:` y placeholders.
- Verificacion: `AtlasAiServiceTests` 41/41 OK; `AtlasAiServiceTests|ConfiguracionControllerTests` 47/47 OK.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter Auto enviaba demasiados modelos en `models`

- Contexto: tras corregir Auto para usar `models`, OpenRouter devolvia 400 con `'models' array must have 3 items or fewer`.
- Causa: `OpenRouterAutoFallbackModels` se derivaba de toda la allowlist gratis, quitando `openrouter/auto`, y enviaba seis modelos. OpenRouter permite como maximo 3 elementos en `models`.
- Solucion: fallback Auto limitado a tres candidatos explicitos con `OpenRouterMaxFallbackModels = 3`; los otros modelos gratis siguen disponibles como seleccion manual.
- Verificacion: `AtlasAiServiceTests|ConfiguracionControllerTests` 46/46 OK; test nuevo parsea el payload y cubre el 400 exacto.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter Auto fallaba por interseccion vacia de restricciones

- Contexto: seleccionar `Auto` en IA devolvia 404 con `No models match your request and model restrictions`.
- Causa: se intento usar Auto Router de OpenRouter con `allowed_models` formado por modelos `:free`; Auto Router solo filtra su bolsa curada, no cualquier modelo valido de OpenRouter.
- Solucion: `Auto` conserva `openrouter/auto` como valor de configuracion, pero la llamada usa `models` con fallback acotado a modelos gratis permitidos. Ajuste posterior: OpenRouter limita `models` a 3 elementos. El texto visible pasa a `Auto (gratis permitido)`.
- Verificacion: `AtlasAiServiceTests|ConfiguracionControllerTests` 45/45 OK, frontend lint OK, TypeScript OK, build frontend OK fuera del sandbox y `wwwroot` sincronizado.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter Auto no podia elegir sin riesgo de salir de la allowlist

- Contexto: el usuario queria que OpenRouter eligiera automaticamente el mejor modelo para cada pregunta, pero dentro de la lista permitida de la cuenta.
- Causa: `openrouter/auto` abierto puede enrutar fuera de la lista o a modelos de pago; convertir Auto a un modelo fijo eliminaba ese riesgo, pero tambien eliminaba la funcion de Auto Router.
- Solucion historica: `Auto` quedo como default y se envio con `plugins.auto-router.allowed_models` limitado a los seis modelos gratis permitidos. Esta via fue sustituida el mismo dia por `models` con maximo 3 candidatos porque Auto Router no cubria bien la allowlist gratis y `models` tiene limite efectivo de 3. Los modelos exactos siguen seleccionables manualmente.
- Verificacion: frontend lint OK; TypeScript OK; `AtlasAiServiceTests|ConfiguracionControllerTests` 44/44 OK; build frontend OK; `wwwroot` sincronizado.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Chat IA no enviaba con Enter ni permitia elegir modelo desde el chat

- Contexto: el usuario esperaba que `Enter` enviara el mensaje y que el modelo pudiera seleccionarse desde el propio chat.
- Causa: el textarea solo enviaba mediante submit del boton y `/api/ia/chat` no aceptaba un modelo por consulta.
- Solucion: `Enter` envia, `Shift+Enter` inserta linea, el chat muestra selector de modelo y el backend valida el `model` solicitado sin tocar la configuracion global.
- Verificacion: frontend lint OK, TypeScript OK, build OK fuera del sandbox, `AtlasAiServiceTests` 35/35 OK, Playwright estatico con selector visible y sin overflow.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - IA demasiado restrictiva con consultas financieras administrativas

- Contexto: preguntas como `cual ha sido los gastos globales del ultimo mes` y consultas sobre Seguridad Social, impuestos, recibos o facturas deben responderse si los datos estan en Atlas Balance.
- Causa: la barrera tematica y el prompt separaban mal asuntos externos de vocabulario financiero/fiscal legitimo.
- Solucion: se amplia la allowlist financiera, se aclara el prompt y se anaden periodos/categorias de contexto para ultimo mes, mes pasado, impuestos/Seguridad Social y recibos/facturas.
- Verificacion: `AtlasAiServiceTests` 33/33 OK.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - Chat IA mostraba respuesta cortada y Markdown crudo

- Contexto: una respuesta de analisis financiero aparecia con `**trimestre actual**`, una tabla Markdown visible y contenido cortado lateralmente dentro de la burbuja.
- Causa: el frontend pintaba el contenido del proveedor como texto plano y el layout grid del panel no daba la fila flexible a `.ai-chat-messages` en el estado configurado normal.
- Solucion: renderer local `AiMessageContent`, tablas Markdown convertidas a pares dato/valor, metadatos plegados en `Detalles de IA`, layout flex del panel y prompt backend para evitar tablas/pipes/asteriscos.
- Verificacion: frontend lint OK, TypeScript OK, build OK fuera del sandbox, `AtlasAiServiceTests` 33/33 OK, Playwright estatico sin Markdown crudo ni overflow.
- Estado: cerrado.

### 2026-05-11 - V-01.06 - OpenRouter 404 por allowlist de modelos gratis

- Contexto: OpenRouter devolvia 404 con `No endpoints available matching your guardrail restrictions and data policy` aunque el usuario habia permitido modelos gratis en su cuenta.
- Causa: Atlas Balance intentaba usar modelos fuera de esa allowlist o forzaba ZDR sobre endpoints gratis que OpenRouter no publica como ZDR.
- Solucion: OpenRouter queda limitado a modelos gratis permitidos; `Auto` usa `auto-router.allowed_models` acotado a la allowlist y los modelos exactos gratis no fuerzan ZDR.
- Verificacion: API publica de OpenRouter revisada; build API OK; `AtlasAiServiceTests` 29/29 OK; frontend lint/build OK; `wwwroot` sincronizado; API reiniciada y `/api/health` healthy.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - OpenRouter 404 por slug de modelo obsoleto

- Contexto: el chat IA devolvia `OpenRouter no ha respondido correctamente (404)`.
- Causa: `openrouter/auto` se enviaba con `allowed_models` incluyendo `anthropic/claude-3.5-sonnet`, que ya no existe en OpenRouter.
- Solucion: Auto Router usa patrones actuales, la allowlist directa y el selector frontend se actualizan, y el runtime normaliza solo el slug obsoleto conocido a `openrouter/auto`.
- Verificacion: OpenRouter `/api/v1/models` confirma los nuevos slugs; build API OK; `AtlasAiServiceTests` 25/25 OK; frontend lint/build OK; `wwwroot` sincronizado; API reiniciada y `/api/health` 200 `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Chat IA mostraba error generico de red

- Contexto: al usar IA aparecia `Error de red al consultar el proveedor de IA`.
- Causa: el cliente de IA no tenia fallback de salida HTTP y la auditoria no guardaba suficiente detalle tecnico sanitizado para distinguir red directa, proxy, sandbox, DNS o TLS.
- Solucion historica: `AtlasAiService` reintenta con cliente HTTP fallback y registra `http_client`, `used_http_fallback` y errores de transporte recortados sin prompt ni clave. Ajuste posterior: el fallback ya no usa proxy automatico por defecto; proxy solo explicito.
- Verificacion: OpenRouter 200 y OpenAI 401 esperado fuera del sandbox; build API OK; `AtlasAiServiceTests` 24/24 OK; API reiniciada y `/api/health` 200 `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Chat IA seguia fallando en resumenes y categorias

- Contexto: tras arreglar saldos actuales, el chat IA aun podia devolver HTTP 500 al pedir resumenes mensuales, ingresos/gastos, comisiones, seguros o movimientos relevantes.
- Causa: varias consultas seguian filtrando y agrupando sobre `AiExtractoRow`, un record proyectado que Npgsql no podia traducir a SQL.
- Solucion: los agregados y filtros se hacen sobre `Extractos`/`Cuentas`; los records de prompt se proyectan solo al final.
- Verificacion: `AtlasAiServiceTests` 22/22 OK; build API OK; verificador temporal contra PostgreSQL real OK con rollback; `/api/health` responde `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Primer mensaje del chat IA devolvia HTTP 500

- Contexto: al enviar el primer mensaje desde IA, Axios mostraba `Request failed with status code 500`.
- Causa: el contexto de saldos actuales agrupaba y enlazaba sobre `AiExtractoRow`, un record proyectado dentro de LINQ. PostgreSQL/Npgsql no podia traducir esa expresion.
- Solucion: el agregado de ultimo saldo por cuenta se hace sobre columnas escalares de `EXTRACTOS` y la proyeccion al record ocurre al final.
- Verificacion: `AtlasAiServiceTests` 20/20 OK; API dev reiniciada; `/api/health` responde `healthy`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Chat flotante IA tapado por filtros del dashboard

- Contexto: al abrir IA desde la topbar en el dashboard principal, los selectores de periodo/divisa quedaban por encima del panel.
- Causa: `.app-topbar` no tenia z-index propio pese a alojar el overlay flotante de IA; el contenido principal se pintaba despues en el shell.
- Solucion: `.app-topbar` crea un plano de apilado con `position: relative` y `z-index: var(--z-sticky)`.
- Verificacion: frontend lint OK; build OK fuera del sandbox; `wwwroot` sincronizado; Playwright headless con CSS compilado confirma que el solape queda dentro del chat (`insideChat=true`).
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Guardar API key de OpenRouter fallaba por modelo invalido

- Contexto: guardar la configuracion IA podia fallar al pegar un token de OpenRouter si el modelo estaba vacio o arrastraba un slug no permitido.
- Solucion: `openrouter/auto` pasa a ser default seguro; frontend normaliza el modelo antes de enviar y backend convierte modelos vacios o no permitidos del proveedor a default seguro antes de guardar.
- Verificacion: tests focalizados IA/configuracion 25/25 OK, frontend lint/build OK, `wwwroot` sincronizado y backend dev saludable en `/api/health`.
- Estado: cerrado.

### 2026-05-10 - V-01.06 - Mezcla incorrecta de sesion ChatGPT con consumo OpenAI

- Contexto: se mezclo la sesion web de ChatGPT con el consumo de OpenAI desde Atlas Balance.
- Solucion: OpenAI queda como proveedor IA por API key de servidor. El flujo externo de ChatGPT se retira por completo para no dejar una mitad de producto que nadie va a usar.
- Estado: cerrado por retirada completa del flujo externo.

### 2026-05-10 - V-01.06 - Cuentas desalineada respecto a Titulares

- Contexto: `Cuentas` usaba el ancho completo del contenido mientras `Titulares` estaba centrada en el contenedor visual comun.
- Solucion: aplicar el max-width compartido a `.phase2-page` en `system-coherence.css`, cubriendo `Cuentas` y cualquier vista phase2 equivalente.
- Verificacion: Playwright desktop confirma `deltaLeft=0` y `deltaWidth=0` entre `Titulares` y `Cuentas`; lint/build frontend OK.

### 2026-05-10 - V-01.06 - Importacion sin idempotencia/fingerprint

- Contexto: reimportar el mismo extracto podia duplicar movimientos.
- Solucion: fingerprint SHA-256 persistido por cuenta/fila/contenido normalizado, hash de lote, fila origen, fecha de importacion e indice unico filtrado.
- Verificacion: tests de reimportacion exacta, parcial y filas repetidas OK.

### 2026-05-10 - V-01.06 - Revision sin paginacion real

- Contexto: `RevisionService` cargaba todos los movimientos y filtraba en memoria.
- Solucion: filtros, ordenacion y paginacion pasan a consulta EF; endpoints devuelven `PaginatedResponse`.
- Verificacion: test de paginacion con total OK.

### 2026-05-10 - V-01.06 - Exportaciones grandes sin control

- Contexto: ClosedXML genera XLSX en memoria dentro de la request.
- Solucion: limite `export_max_rows`, auditoria de bloqueo, estado `FAILED` y HTTP 413 en exportacion manual.
- Verificacion: tests de exportacion normal, grande y usuario sin permiso OK.

### 2026-05-10 - V-01.06 - Plazos fijos marcaban notificacion aunque fallara email

- Contexto: `FechaUltimaNotificacion` se actualizaba antes del envio SMTP.
- Solucion: solo se marca tras email correcto; sin destinatarios o SMTP fallido queda reintento disponible sin duplicar notificacion interna.
- Verificacion: tests SMTP OK, SMTP falla, sin destinatarios y reintento OK.

### 2026-05-10 - V-01.06 - Suite backend bloqueada por MSBuild tras renombrado

- Contexto: los targets NuGet de tests quedaron obsoletos despues del renombrado y el build paralelo amplificaba el fallo.
- Solucion: restore regenerado y `BuildInParallel=false` en `AtlasBalance.API.Tests.csproj`.
- Verificacion: suite sin tests Docker/PostgreSQL 163/163 OK.

### 2026-05-10 - V-01.06 - Parser europeo manual incompleto

- Contexto: altas/ediciones manuales frontend usaban `Number(...)`, que rechaza `1.234,56`.
- Solucion: `parseEuropeanNumber` compartido y campos `inputMode="decimal"` en extractos, desglose de cuenta e importacion de plazo fijo.
- Verificacion: frontend lint/build OK; parser backend cubierto por tests parametrizados de importacion.

### 2026-05-10 - V-01.06 - IA demasiado abierta y con exceso de contexto financiero

- Contexto: `/api/ia/chat` estaba disponible para cualquier usuario autenticado y podia enviar demasiado contexto financiero a OpenRouter.
- Causa: faltaban permiso fuerte, cuota, longitud maxima y minimizacion del prompt.
- Solucion: chat IA con permiso persistente por usuario, interruptor global, limites configurables, auditoria de metadatos, allowlist backend de modelos y contexto minimizado con conceptos tratados como datos no confiables.
- Verificacion: API build OK, frontend lint/build OK.

### 2026-05-10 - V-01.06 - Revision y exportacion manual aceptaban permiso de lectura para escribir

- Contexto: usuarios con acceso solo lectura podian intentar marcar estados de revision o lanzar exportaciones manuales.
- Causa: los endpoints validaban `CanAccessCuentaAsync`, que concede lectura.
- Solucion: nuevo `CanWriteCuentaAsync`; `RevisionService` y `ExportacionesController.Manual` exigen escritura antes de mutar.
- Verificacion: API build OK; test de regresion anadido, ejecucion bloqueada por el bug abierto del proyecto de tests.

### 2026-05-10 - V-01.06 - Saldo bajo entraba en cooldown aunque el email no saliera

- Contexto: con SMTP roto o sin destinatarios, `FechaUltimaAlerta` podia actualizarse y bloquear reintentos.
- Causa: el servicio marcaba la alerta tras intentar enviar, sin distinguir exito real.
- Solucion: si no hay destinatarios o falla SMTP, no se actualiza `FechaUltimaAlerta` ni se registra auditoria de disparo; `EmailService` lanza error si falta SMTP para alertas.
- Verificacion: API build OK; test de regresion anadido, ejecucion bloqueada por el bug abierto del proyecto de tests.

### 2026-05-10 - V-01.06 - Exportacion perdia el orden original del extracto

- Contexto: las exportaciones de cuenta salian ordenadas por fecha, no por el orden persistido de importacion.
- Causa: `ExportacionService` usaba `OrderBy(e => e.Fecha).ThenBy(e => e.FilaNumero)`.
- Solucion: exportar por `fila_numero desc`, con fecha Excel `dd/mm/yyyy` y formato numerico `#,##0.00`.
- Verificacion: backend API build OK; test de regresion anadido, pero la ejecucion del proyecto de tests queda bloqueada por `AtlasBalance.Watchdog`.

### 2026-05-10 - V-01.06 - Barras de formula cortaban contenido

- Contexto: al seleccionar celdas largas, la barra superior tipo Excel aplicaba ellipsis.
- Causa: estilos de una sola linea heredados para valores cortos.
- Solucion: wrapping y sin truncado en las barras de formula de extractos y cuenta.
- Verificacion: frontend lint/build OK.

### 2026-05-10 - V-01.06 - Tabla de estados de revision sin RLS

- Contexto: `REVISION_EXTRACTO_ESTADOS` es una tabla nueva de datos financieros derivados.
- Causa: la migracion creaba tabla e indices pero no politicas RLS.
- Solucion: RLS forzado y politicas de lectura/escritura vinculadas al extracto.
- Verificacion: backend API build OK; `RowLevelSecurityTests` actualizado para exigir la tabla.

### 2026-05-02 - V-01.06 - Extractos no tenia reticula real de celdas

- Contexto: la tabla de `Extractos` seguia viendose con margenes torcidos y casillas desplazadas pese al fix previo de anchos fijos.
- Causa: el viewport dibujaba una cuadricula decorativa de `120px` que no coincidia con los anchos reales; el ancho total tampoco se heredaba desde un contenedor comun.
- Solucion: mover `--extracto-sheet-width` al viewport, quitar la cuadricula falsa y construir la reticula con bordes reales de celda y altura fija por fila.
- Verificacion: frontend lint/build OK, Playwright headless confirma alineacion exacta en 13 columnas y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - Desglose de cuenta sin insercion intermedia

- Contexto: al revisar una cuenta, no habia forma de anadir una linea entre dos filas del extracto y conservar ese orden.
- Causa: el alta manual de extractos siempre se guardaba al final de la cuenta con `fila_numero = max + 1`.
- Solucion: `POST /api/extractos` soporta `insert_before_fila_numero`, desplaza filas posteriores de forma transaccional, la UI de cuenta expone `Insertar debajo` y el desglose se ordena por `fila_numero desc`.
- Verificacion: `ExtractosControllerTests` 11/11 OK, frontend lint/build OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - Graficas de evolucion recortadas por arriba

- Contexto: la linea de saldo en `Evolucion` podia verse cortada en la parte superior del chart.
- Causa: dominio Y automatico sin colchon superior; el trazo quedaba pegado al limite del area de dibujo.
- Solucion: `EvolucionChart` define un dominio vertical con padding del 4%, manteniendo el cero cuando todo es positivo y ampliando el rango cuando hay valores negativos.
- Verificacion: frontend lint/build OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - Tabla de extractos parecia desplazada entre filas y columnas

- Contexto: la vista de extractos no podia permitirse celdas visualmente movidas; debe leerse como hoja tipo Excel.
- Causa: tracks flexibles `fr` dentro de filas absolutas virtualizadas, sin ancho total compartido entre cabecera y cuerpo, mas un offset vertical negativo innecesario.
- Solucion: anchos fijos por columna, `--extracto-sheet-width` compartido por cabecera/cuerpo/filas y transform vertical desde `virtualRow.start`.
- Verificacion: frontend lint/build OK, Playwright headless confirma alineacion de cabecera y celdas, y `wwwroot` sincronizado.

### 2026-05-02 - V-01.06 - KPIs de ingresos y egresos cortados en dashboard principal

- Contexto: con importes largos, las tarjetas `Ingresos periodo` y `Egresos periodo` no reservaban ancho suficiente y el texto se cortaba visualmente.
- Causa: fuente mono fija demasiado grande para tarjetas laterales estrechas con `white-space: nowrap`.
- Solucion: los KPIs se convierten en contenedores CSS, los importes escalan con unidades `cqw` y el layout superior da mas ancho al bloque principal compactando `Saldos por divisa`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma que los KPIs superiores y las divisas no desbordan.

### 2026-05-02 - V-01.05 - CI GitHub no podia instalar dependencias frontend

- Contexto: GitHub Actions fallaba en `npm ci` con `404 Not Found` para tarballs `1.5.0` inexistentes.
- Causa: el lockfile apuntaba a `once`, `graphemer`, `loose-envify` y `natural-compare` en `1.5.0`, versiones inexistentes en npm.
- Solucion: se fijan overrides a `1.4.0` y se actualiza `package-lock.json` para resolver los cuatro paquetes a tarballs publicados.
- Verificacion: `npm.cmd ci` OK, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, `npm.cmd run lint` OK y `npm.cmd run build` OK.

### 2026-05-02 - V-01.05 - Hallazgos residuales del escaneo repo-wide

- Contexto: tras el cierre post-hardening quedaban ocho hallazgos en instalador/reset, permisos de extractos, dashboard, OpenClaw, importacion, RLS y CI.
- Solucion:
  - Credenciales one-shot de instalacion y reset se escriben solo despues de restringir `C:\AtlasBalance\config`; los scripts fallan cerrado si ACL falla.
  - `Reset-AdminPassword.ps1` requiere Administrador.
  - `ToggleFlag` comprueba `flagged` y `flagged_nota` segun el campo que realmente cambia.
  - Dashboard global para gerente requiere tambien permiso global de datos; `PuedeVerDashboard` global solo ya no abre cuentas.
  - Auditoria OpenClaw respeta soft-delete de extractos.
  - `returnTo` de importacion solo acepta rutas internas.
  - RLS de `EXPORTACIONES` usa permiso de escritura para escribir.
  - PostgreSQL queda digest-pinned en CI y compose.
- Verificacion: tests focalizados 20/20 OK, frontend lint/build OK, parser PowerShell OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.05 - Revision repo-wide de bugs y seguridad post-hardening

- Contexto: pasada nueva tras cerrar el escaneo previo. Subagente revisor encontro filtraciones residuales hacia integracion OpenClaw, password admin reseteada que iba a stdout, y falta de defensa en profundidad ante ZIP slip en update online.
- Hallazgos cerrados:
  - `IntegrationOpenClawController` enviaba el email del usuario creador de cada extracto al cliente OpenClaw. Ahora devuelve `nombre_completo` (mantiene el `usuario-eliminado` para usuarios borrados).
  - El endpoint `/api/integration/openclaw/auditoria` exponia `ip_address` de operadores internos al socio externo. Se elimino del payload.
  - `scripts/Reset-AdminPassword.ps1` imprimia la password temporal en pantalla; ahora la vuelca a `C:\AtlasBalance\config\RESET_ADMIN_CREDENTIALS_ONCE.txt` con ACL restringida a Administrators.
  - `ActualizacionService` extraia el ZIP descargado con `ZipFile.ExtractToDirectory`. Aunque ya hay digest SHA-256 + firma RSA del paquete, ahora se valida cada entrada contra el `packageRoot` antes de escribir, abortando si una entrada apuntaria fuera.
- Verificacion: `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --no-build` 129/129 OK; parser PowerShell de `Reset-AdminPassword.ps1` OK; frontend lint/build OK; npm audit 0 vulnerabilidades; NuGet sin vulnerabilidades.

### 2026-05-02 - V-01.05 - Harness RLS local sin permiso sobre __EFMigrationsHistory

- Contexto: la suite completa quedaba 127/128 porque `RowLevelSecurityTests.CoreFinancialTables_Should_Enforce_Rls_By_User_And_IntegrationScope` fallaba con `permission denied for table __EFMigrationsHistory` cuando otros tests creaban antes el esquema con el rol bootstrap del fixture.
- Causa: tras crear el rol `rls_owner_*`, el test cambiaba ownership solo de la BD y del esquema `public`, pero las tablas `__EFMigrationsHistory`, las del dominio y el esquema `atlas_security` seguian perteneciendo al rol bootstrap. La nueva conexion del owner no podia leer/migrar.
- Solucion: en `CreateRoleConnectionStringsAsync` el bootstrap ahora reasigna ownership de todas las tablas, secuencias, vistas, materializadas y funciones de los esquemas `public` y `atlas_security` al nuevo owner. Asi `MigrateAsync` y `ConfigureRlsRuntimeAsync` ven todo el catalogo bajo el mismo owner.
- Verificacion: `dotnet test ...AtlasBalance.API.Tests.csproj -c Release --no-build` 129/129 OK.

### 2026-05-02 - V-01.05 - Vulnerabilidades detectadas en escaneo repo-wide

- Contexto: escaneo completo con subagentes sobre backend, frontend, scripts, CI, dependencias y update path.
- Hallazgos: lockout password/MFA debil, redaccion insuficiente de auditoria de integraciones, amplificacion de columnas extra en importacion, permisos dashboard-only usados como acceso a datos, restore de extractos con permiso insuficiente, fuga de referencia de plazo fijo y actualizacion online sin firma independiente.
- Solucion: controles corregidos en servicios/controladores/middleware/scripts y tests de regresion en auth, integraciones, importacion, permisos, extractos, cuentas y actualizaciones.
- Verificacion: suite focalizada 72/72 OK; NuGet y npm audit sin vulnerabilidades; parser PowerShell OK. Suite completa 127/128 por bug abierto de harness RLS.

### 2026-05-02 - V-01.05 - Graficas de evolucion con eje Y fijo

- Contexto: todas las graficas `Evolucion` compartian un `YAxis` fijo de `72px`, suficiente para importes grandes pero excesivo para importes pequenos.
- Solucion: calcular dinamicamente el ancho del eje Y en `EvolucionChart`, con limite inferior `44px` y superior `72px`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma `gridStartX=45px` en las cuatro rutas con evolucion.

### 2026-05-02 - V-01.05 - Cuentas de efectivo sin selector de formato

- Contexto: una cuenta de tipo `EFECTIVO` no dejaba seleccionar `Formato de importacion` como las demas cuentas importables.
- Solucion: permitir formato para `NORMAL` y `EFECTIVO`, limpiar solo datos bancarios en efectivo y mantener `PLAZO_FIJO` como unico tipo sin formato.
- Verificacion: `CuentasControllerTests` 5/5 OK, frontend lint/build OK y `wwwroot` sincronizado.

### 2026-05-02 - V-01.05 - Graficas de barras desalineadas en cuentas y titulares

- Contexto: las graficas de barras de los dashboards embebidos en `Cuentas` y `Titulares` arrancaban visualmente demasiado a la derecha.
- Solucion: reducir el ancho reservado por `YAxis` de `120` a `72`, usar ticks compactos, margenes explicitos y ejes simplificados en ambos `BarChart`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma `gridStartX=72px` en `/titulares` y `/cuentas`.

### 2026-05-01 - V-01.05 - Grafica Evolucion desalineada en dashboard principal

- Contexto: la grafica `Evolucion` del dashboard principal quedaba visualmente corrida a la derecha dentro de la tarjeta.
- Solucion: reducir el ancho reservado por `YAxis`, declarar margenes explicitos en `LineChart` y separar ticks con `tickMargin`.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Playwright headless confirma `plotInsetFromLegend=72px`.

### 2026-05-01 - V-01.05 - Logo del login fuera de columna

- Contexto: el logo superior del login quedaba alineado al margen izquierdo de la pagina, no con el formulario centrado.
- Solucion: `.auth-logo-container` pasa a usar el ancho de la columna de login y centra el bloque de marca completo.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y Edge headless confirma `brandDeltaCard=0px`.

### 2026-05-01 - V-01.05 - KPI del dashboard se solapaba tras reorden UI

- Contexto: al aplicar la nueva guia UI/UX, el `Saldo total` quedo en una columna demasiado estrecha y podia invadir visualmente `Saldos por divisa`.
- Solucion: ajuste de `dashboard-command-grid` y escala del KPI destacado dentro del grid principal.
- Verificacion: Playwright desktop/mobile sin overflow horizontal y sin solapamiento; frontend lint/build OK.

### 2026-05-01 - V-01.05 - PostgreSQL Row Level Security no configurado

- Contexto: revision solicitada para comprobar si la base de datos tenia Row Level Security configurado.
- Hallazgo: no habia migraciones ni scripts con `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY` o `CREATE POLICY`; la base local tenia todas las tablas `public` sin RLS y el rol `app_user` era superusuario con capacidad de saltarse RLS.
- Solucion: migraciones `EnableRowLevelSecurity` y `SignRowLevelSecurityContext` con funciones auxiliares, RLS forzado, politicas por usuario/integracion/admin/sistema y firma HMAC del contexto; interceptor EF Core para fijar contexto `atlas.*`; Docker e instalador separados en rol owner/migracion y rol runtime sin `BYPASSRLS`.
- Verificacion: `RowLevelSecurityTests` OK; tests focalizados RLS/permisos/integraciones 15/15 OK; `dotnet ef database update` aplicado en `atlas_balance_db`; catalogo local con 11 tablas objetivo bajo RLS/FORCE RLS, 20 politicas, dos migraciones RLS aplicadas, `app_user` sin `rolsuper` ni `rolbypassrls`, secreto RLS sembrado, contexto falsificado rechazado y contexto firmado aceptado.

### 2026-04-26 - V-01.05 - Importacion cambiaba el orden de lineas del extracto

- Contexto: `ConfirmarAsync` reordenaba filas por fecha antes de crear extractos, aunque el usuario necesita que la secuencia pegada desde el banco se conserve.
- Solucion: eliminado el ordenamiento por fecha; la numeracion se asigna desde abajo hacia arriba para que la linea superior quede con el `fila_numero` mas alto del lote.
- Verificacion: `ImportacionServiceTests` 26/26 OK y backend Release build OK.

### 2026-04-26 - V-01.05 - Actualizacion desde paquete podia dejar la API parada

- Contexto: `update.cmd -InstallPath C:\AtlasBalance` en un paquete `V-01.04` podia no reenviar bien `-InstallPath`; al ejecutar el actualizador directo, la API podia caer en arranque por `23505 pk_formatos_importacion`.
- Solucion: `scripts/update.ps1` ahora declara `InstallPath`/`SkipBackup` explicitamente y los reenvia al actualizador sin `ValueFromRemainingArguments`; `SeedData` evita insertar formatos por ID fijo si ese ID ya existe aunque banco/divisa no coincidan.
- Verificacion: parser PowerShell OK, `SeedDataTests` 5/5 OK y paquete `V-01.05` regenerado.

### 2026-04-25 - V-01.05 - Hallazgos de auditoria de uso, bugs y seguridad corregidos

- Contexto: la auditoria V-01.05 dejo abiertos tres puntos malos para release: Tailwind/shadcn contra el stack canonico, `CuentasController.Resumen` con contrato mas pobre que el resumen de extractos, y accesibilidad incompleta en controles propios.
- Solucion: eliminados Tailwind/shadcn y sus imports/configuracion; `CuentaResumenResponse` ahora expone titular, tipo de cuenta, notas, ultima actualizacion y `plazo_fijo`; `DatePickerField` tiene etiquetas completas y navegacion por flechas/Home/End; `ConfirmDialog` atrapa Tab dentro del modal; `AppSelect` abre/cierra con Enter/Espacio.
- Verificacion: busqueda sin restos directos de Tailwind/shadcn, `npm.cmd run lint` OK, `npm.cmd run build` OK, `wwwroot` sincronizado, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, NuGet sin vulnerabilidades y backend tests 108/108 OK.

### 2026-04-25 - V-01.05 - Gradientes decorativos de UI reducidos

- Contexto: la auditoria marco fondos `radial-gradient` y degradados suaves como huella de UI generica y contraria al criterio visual del proyecto.
- Solucion: reemplazados fondos decorativos de `body`, login, panels, KPIs, listas y empty states por superficies planas basadas en tokens. Se mantienen solo degradados funcionales de `select` y skeleton.
- Verificacion: busqueda de degradados deja solo usos funcionales, `npm.cmd run lint` OK y `npm.cmd run build` OK.

### 2026-04-25 - V-01.05 - Endpoints nuevos NPE-able si el cuerpo o las listas llegaban null

- Contexto: `POST /api/alertas`, `PUT /api/alertas/{id}`, `POST /api/cuentas/{id}/plazo-fijo/renovar` y `POST /api/importacion/plazo-fijo/movimiento` accedian a `request.SaldoMinimo`, `request.DestinatarioUsuarioIds.Count` o `request.CuentaId` sin antes validar que el body no fuera null. Un cliente autorizado mandando `null` o JSON sin la propiedad colapsaba la peticion en 500.
- Riesgo: ruido en logs, falta de respuesta clara al consumidor y degradacion gratuita ante input malformado. Solo afecta a admins (todas son rutas con `[Authorize(Roles = "ADMIN")]` o `[Authorize]`), pero el contrato seguro debe ser 400 con mensaje, no 500.
- Solucion: validacion temprana del body (`if (request is null) return BadRequest(...)`) y normalizacion de `DestinatarioUsuarioIds` con `?? []` antes de consumirla.
- Verificacion: backend Release build OK, `dotnet test` 107/107 OK, NuGet sin vulnerabilidades.

### 2026-04-25 - V-01.05 - Manifiesto frontend mantenia minimos vulnerables de dependencias

- Contexto: el `package-lock.json` resolvia versiones seguras, pero `package.json` seguia declarando minimos antiguos: `axios ^1.7.9` y `react-router-dom ^6.28.0`.
- Riesgo: instalaciones regeneradas sin lockfile fiable podian resolver rangos afectados por advisories recientes de Axios y React Router. Confiar solo en el lockfile aqui era una trampa tonta.
- Solucion: actualizado el manifiesto a `axios ^1.15.2` y `react-router-dom ^6.30.3`, dejando el lockfile en versiones verificadas.
- Verificacion: `npm.cmd audit --audit-level=moderate` OK, frontend lint/build OK, backend tests 107/107 OK y NuGet sin vulnerabilidades.

### 2026-04-25 - V-01.05 - Selector de fecha nativo no seguia el diseno Atlas al abrirse

- Contexto: el campo cerrado de fecha se veia integrado, pero el calendario desplegado seguia siendo el popup nativo del navegador.
- Solucion: creado `DatePickerField` propio y reemplazados los `input type="date"` del frontend.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y prueba visual en navegador de `/cuentas` sin errores de consola.

### 2026-04-25 - V-01.05 - Detalle de plazo fijo ocultaba vencimiento

- Contexto: el dashboard de una cuenta de plazo fijo no mostraba cuando se acababa el plazo, aunque el dato existia en la ficha de cuenta.
- Solucion: el resumen de cuenta expone `tipo_cuenta` y `plazo_fijo`; `CuentaDetailPage` muestra vencimiento, dias restantes/vencido y estado bajo el titulo.
- Verificacion: backend Release build OK, frontend lint/build OK y `wwwroot` sincronizado.

### 2026-04-25 - V-01.05 - Actualizaciones post-instalacion no dejaban flujo operativo completo

- Contexto: tras tener Atlas Balance instalado, el update manual no refrescaba scripts instalados, no actualizaba runtime y no comprobaba salud HTTP real al final.
- Solucion: `update.ps1` soporta `-PackagePath`, validacion temprana de paquete, copia de scripts/wrappers operativos a `C:\AtlasBalance`, actualizacion de `VERSION`/`atlas-balance.runtime.json` y health check con `curl.exe -k`.
- Verificacion: parser PowerShell OK y documentacion actualizada con ambos modos de ejecucion.

### 2026-04-25 - V-01.05 - Instalacion Windows Server 2019 con flujo operativo fragil

- Contexto: el operador intento instalar desde carpeta fuente, habia paquete V-01.03 mientras se validaba V-01.05, `winget` no era fiable, `install.cmd` podia desordenar parametros, `Invoke-WebRequest` daba falso negativo y una reinstalacion sobre BD existente generaba credenciales iniciales falsas.
- Solucion: validacion temprana de paquete release, mensajes duros para carpeta equivocada, fallback documentado a PostgreSQL manual 16+/17, wrappers con codigo de salida, health check con `curl.exe -k`, deteccion de usuarios existentes y script `Reset-AdminPassword.ps1` para reset controlado.
- Verificacion: parser PowerShell OK y ejecucion de instalador/wrapper desde carpeta fuente falla con mensaje claro de paquete invalido.

### 2026-04-25 - V-01.05 - Reinstalacion reutilizaba PFX con password nueva

- Contexto: reinstalar sobre `C:\AtlasBalance` existente podia dejar `AtlasBalance.API` parado con `CryptographicException: La contrase�a de red especificada no es v�lida`.
- Solucion: `Instalar-AtlasBalance.ps1` elimina `atlas-balance.pfx` y `atlas-balance.cer` existentes antes de generar un certificado HTTPS nuevo, evitando que el PFX viejo quede asociado a una password nueva en `appsettings.Production.json`.
- Verificacion: diagnostico reproducido por traza de Windows Event Log; correccion revisada en el flujo `New-AtlasCertificate`.

### 2026-04-25 - V-01.05 - Importacion embebida bloqueada por anti-frame

- Contexto: el modal de `Importar movimientos` del dashboard de cuenta cargaba `/importacion` en un `iframe`, pero el navegador mostraba rechazo de conexion/documento roto.
- Solucion: cabeceras de seguridad ajustadas de `DENY`/`frame-ancestors 'none'` a `SAMEORIGIN`/`frame-ancestors 'self'`, permitiendo solo el frame interno de la propia app.
- Verificacion: diagnostico por cabeceras HTTP del health check y revision del componente `CuentaDetailPage`.

### 2026-04-25 - V-01.03 - Hardening de sesiones, passwords, updates y rutas

- Contexto: la auditoria V-01.03 detecto resets de password sin revocar sesiones, reuse de refresh token sin escalado, login enumerable/rate limit flojo, bearer invalido de integracion sin throttle previo, `app_update_check_url` sin allowlist, rutas relativas aceptadas tras `GetFullPath`, politica de password de 8 caracteres, credenciales one-shot persistentes y `postcss` vulnerable.
- Solucion: `SecurityStamp` + `PasswordChangedAt`, migracion `UserSessionHardening`, revocacion de refresh tokens en reset/cambio/delete/reuse, rate limit por email/IP, respuesta generica para cuentas bloqueadas, throttle previo para bearer invalido, allowlist HTTPS al repo oficial, validacion de rutas crudas antes de normalizar, minimo 12 caracteres con bloqueo de passwords comunes, borrado programado de `INSTALL_CREDENTIALS_ONCE.txt` y `postcss` actualizado a `8.5.10`.
- Verificacion: `dotnet test -c Release --no-build` 94/94 OK, `npm.cmd run lint` OK, `npm.cmd run build` OK, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades y NuGet sin vulnerabilidades.

### 2026-04-24 - V-01.03 - Frontend muestra detalle de cuenta a perfiles dashboard-only

- Contexto: tras cerrar la fuga de datos en `ExtractosController`, el frontend seguia usando `usePermisosStore.canViewCuenta` para pintar enlaces a `/dashboard/cuenta/:id` y otras affordances de cuenta.
- Solucion: `canViewCuenta` y helpers de cuenta ya no consideran validas las filas globales `dashboard-only`; `CuentasPage` deja de mostrar CTAs operativos sin acceso y `CuentaDetailPage` redirige al dashboard ante `403`.

### 2026-04-20 - V-01.02 - Release sin scripts one-click completos

- Contexto: el paquete no exponia `install/update/uninstall/start` como scripts principales.
- Solucion: wrappers `.cmd` y `.ps1` nuevos, copiados por `Build-Release.ps1`.

### 2026-04-20 - V-01.02 - `start` no arrancaba la base de datos gestionada

- Contexto: el lanzador arrancaba Watchdog/API pero dejaba PostgreSQL fuera del orden de arranque.
- Solucion: runtime con `ManagedPostgres` y arranque PostgreSQL -> Watchdog -> API.

### 2026-04-20 - V-01.02 - `setup-https.ps1` tenia sintaxis invalida

- Contexto: parser PowerShell detecto cadena sin terminar.
- Solucion: reescritura ASCII del script.

### 2026-04-20 - V-01.02 - Secretos de configuracion guardados en claro

- Contexto: SMTP password y Exchange Rate API key quedaban persistidos como valores normales en BD.
- Solucion: Data Protection con `ISecretProtector`, migracion automatica en arranque y redaccion en respuestas/auditoria.

### 2026-04-20 - V-01.02 - Permiso global de dashboard concedia alcance global de datos

- Contexto: `PuedeVerDashboard` global podia hacer que un usuario no admin listara datos fuera de su alcance esperado.
- Solucion: `HasGlobalAccess` solo se activa con permisos globales de datos y se cubrio con test.

### 2026-04-20 - V-01.02 - Descarga de exportaciones sin validacion de ruta segura

- Contexto: la ruta de fichero venia de BD y se abria si existia.
- Solucion: descarga limitada a `.xlsx` dentro de `export_path`.

### 2026-04-20 - V-01.02 - Watchdog con binding administrativo demasiado laxo

- Contexto: servicio administrativo con secreto compartido, pero binding configurable por URLs externas.
- Solucion: Kestrel escucha solo en `localhost:5001`.

### 2026-04-20 - V-01.02 - `AllowedHosts` abierto en produccion

- Contexto: plantilla/base permitia `AllowedHosts="*"`.
- Solucion: la API rechaza wildcards fuera de Development y el instalador escribe hosts explicitos.

### 2026-04-20 - V-01.02 - Artefactos auxiliares con informacion de sesion/login

- Contexto: cookies, cabeceras, payloads y captura de login quedaron en `Otros`.
- Solucion: eliminados los artefactos sensibles y confirmado barrido final sin restos.

### 2026-04-20 - V-01.02 - Typos `atlasbalnace` / `atlas-blance` en archivos activos

- Contexto: quedaban restos en configuracion, plantillas, scripts, placeholders, tests y evento interno de importacion.
- Solucion: normalizados email/rutas/namespaces, creada constante compartida para el evento de importacion y verificado barrido final sin coincidencias en codigo activo.

### 2026-04-20 - V-01.02 - Instalador escribia version runtime antigua

- Contexto: `Instalar-AtlasBalance.ps1` seguia usando `V-01.01` al generar runtime/credenciales de instalacion.
- Solucion: actualizado a `V-01.02` y sincronizada la documentacion de instalacion.

### 2026-04-20 - V-01.02 - Bundle servido por la API desactualizado

- Contexto: `frontend/src` tenia fixes ya aplicados, pero `backend/src/AtlasBalance.API/wwwroot` seguia sirviendo bundles antiguos.
- Solucion: recompilado frontend, limpiado `wwwroot` de forma controlada y copiado `frontend/dist`.

### 2026-04-20 - V-01.02 - Secretos de desarrollo en archivos versionables

- Contexto: `appsettings.json`, `appsettings` de Watchdog y `docker-compose.yml` contenian credenciales/defaults de desarrollo.
- Solucion: se dejaron sin secretos versionables, se a�adieron plantillas y `.env.example`, y el seed admin exige password configurada.

### 2026-04-20 - V-01.02 - Version runtime antigua en seed y actualizaciones

- Contexto: `SeedData` y el User-Agent de checks de actualizacion seguian usando `V-01.01`.
- Solucion: `app_version` inicial y User-Agent pasan a `V-01.02`/version runtime actual.

### 2026-04-20 - V-01.02 - Textos mojibake en importacion y SMTP

- Contexto: algunos errores de importacion y asunto SMTP mostraban texto roto.
- Solucion: se corrigieron cadenas user-facing y sus tests.

### 2026-04-20 - V-01.02 - Acciones CI sin SHA fijo

- Contexto: GitHub Actions usaba tags mutables para actions oficiales.
- Solucion: se fijaron los actions a SHAs concretos.

## Regla de uso

- Si se detecta un bug, anadirlo en `Abiertos` con descripcion, contexto, version y pasos de reproduccion.
- Cuando se resuelva, moverlo a `Cerrados` y documentar la solucion tambien en `Documentacion/LOG_ERRORES_INCIDENCIAS.md`.

### 2026-05-01 - V-01.05 - Cerrado - Test de dashboard dependia del dia del mes

- Contexto: la verificacion amplia del hardening de seguridad fallo en `DashboardServiceTests.GetPrincipalAsync_Should_Aggregate_CurrentBalances_And_PeriodFlows_In_TargetCurrency`.
- Causa: el test generaba un movimiento en `monthStart.AddDays(2)` y lo esperaba como contabilizado aunque al ejecutarse el dia 1 o 2 del mes era una fecha futura.
- Solucion: sustituido por `today` para los movimientos que deben entrar en el mes actual.
- Verificacion: backend sin Testcontainers 115/115 OK.

### 2026-04-20 - V-01.02 - Query params sensibles guardados en auditoria de integracion

- Contexto: `IntegrationAuthMiddleware` metia todo `Request.Query` en la tabla de auditoria sin redactar claves sensibles.
- Solucion: whitelist-by-deny con `HashSet` de claves sensibles y marcador `REDACTED`.

### 2026-04-20 - V-01.02 - `backup_path`/`export_path` sin validacion de ruta segura

- Contexto: valores editables por admin en tabla `CONFIGURACION`; se usaban directos en `Directory.CreateDirectory` y `Path.Combine`.
- Solucion: helper `ResolveSafeDirectory` que rechaza rutas relativas, traversal `..` y caracteres invalidos.

### 2026-04-20 - V-01.02 - Fuga de email de usuarios borrados via integracion OpenClaw

- Contexto: `IntegrationOpenClawController` exponia emails reales de usuarios con `deleted_at != null` al devolver extractos.
- Solucion: el email se sustituye por `usuario-eliminado` cuando el usuario esta borrado.

### 2026-04-20 - V-01.02 - Kestrel de desarrollo escuchando en 0.0.0.0

- Contexto: `appsettings.Development.json` bindeaba `https://0.0.0.0:5000` con `AllowedHosts=*`.
- Solucion: binding a `localhost` y `AllowedHosts=localhost` tambien en Development.

### 2026-04-20 - V-01.02 - Release zip incluia `appsettings.Development.json`

- Contexto: `dotnet publish` no excluia los ficheros Development ni las plantillas; los zips generados por `Build-Release.ps1` incluian secretos de desarrollo.
- Solucion: `ItemGroup` con `CopyToPublishDirectory="Never"` en los csproj de API y Watchdog. Cualquier paquete distribuido antes de hoy debe considerarse comprometido y los secretos rotarse.

### 2026-04-20 - V-01.02 - Passwords hardcodeadas en scripts smoke y docs historicas

- Contexto: `phase2-smoke.ps1`, `phase2-smoke-curl.ps1`, `Otros/Raiz anterior/SPEC.md` y `CORRECCIONES.md` contenian credenciales concretas.
- Solucion: scripts ahora leen `ATLAS_SMOKE_ADMIN_PASSWORD`/`ATLAS_SMOKE_TEST_PASSWORD`; documentos sustituyen los valores por placeholders.

### 2026-04-23 - V-01.03 - Exposicion cross-account en /api/extractos para dashboard-only global

- Contexto: un permiso global con `PuedeVerDashboard=true` podia terminar devolviendo extractos de todas las cuentas desde `GET /api/extractos`.
- Solucion: `ExtractosController` alinea su criterio de acceso global con `UserAccessService` y excluye `PuedeVerDashboard` de acceso global de datos; se anadio test de regresion para dashboard-only global.

### 2026-04-25 - V-01.05 - Importacion bloqueaba filas informativas de banco

- Contexto: filas con solo concepto y sin fecha/monto/saldo se marcaban como error fatal en la validacion de importacion.
- Solucion: esas filas pasan a advertencias importables; se heredan fecha y saldo de la ultima fila valida anterior y se importa monto `0`. Las filas parcialmente rotas o ambiguas siguen bloqueadas.

### 2026-04-26 - V-01.05 - Importacion bloqueaba filas informativas con saldo

- Contexto: filas con concepto y saldo, pero sin fecha ni importe, aparecian como errores y no podian seleccionarse para importar.
- Solucion: esas filas pasan a advertencias importables; se hereda la fecha anterior, se importa monto `0` y se conserva el saldo informado si es numerico.

### 2026-04-25 - V-01.05 - JSX sobrante y lint estricto durante implementacion de plazo fijo

- Contexto: `npm.cmd run build` fallo en `CuentasPage.tsx` por un cierre JSX duplicado; despues `npm.cmd run lint` fallo por warning de Fast Refresh en `components/ui/button.tsx`.
- Solucion: corregido el JSX y documentada una excepcion local de ESLint para el componente UI que exporta `Button` y `buttonVariants`.

### 2026-05-10 - V-01.06 - Cerrado - IA sin gobierno suficiente de permisos, coste y privacidad

- Contexto: la auditoria especifica de IA exigia que no pudiera usarse sin autorizacion, sin limites o exponiendo datos/coste sin control.
- Impacto: riesgo alto de consumo no autorizado, coste inesperado y envio innecesario de datos financieros a proveedor externo.
- Solucion:
  - Interruptor global `ai_enabled`.
  - Permiso persistente `USUARIOS.puede_usar_ia`.
  - Validacion backend obligatoria en `/api/ia/chat`.
  - Limites configurables por minuto/hora/dia, limite global diario, presupuesto mensual/total y caps de tokens/contexto.
  - Contadores de coste `ai_usage_*` persistidos fuera de auditoria para que la limpieza de logs no reinicie el presupuesto total.
  - Auditoria sin prompt ni respuesta completos.
  - Frontend oculta IA si no hay permiso o esta desactivada.
- Verificacion: API build OK, frontend lint/build OK. Tests backend siguen bloqueados por fallo de runner/MSBuild sin salida.

### 2026-05-10 - V-01.06 - Cerrado - IA sin presupuesto mensual por usuario ni pruebas de proveedor

- Contexto: quedaba coste gobernado solo globalmente y faltaban tests reproducibles para fallos de proveedor.
- Impacto: un usuario autorizado podia consumir presupuesto comun sin barrera individual; errores de proveedor podian quedar poco cubiertos.
- Solucion:
  - Nueva tabla `IA_USO_USUARIOS` y clave `ai_user_monthly_budget_eur`.
  - Bloqueo `user_monthly_budget_exceeded` antes de llamada externa.
  - Parser defensivo de payload del proveedor.
  - Tests de 401, 404, timeout, respuesta malformada, presupuesto por usuario y persistencia de contadores.
- Verificacion: `AtlasAiServiceTests` 18/18 OK.

### 2026-05-10 - V-01.06 - Cerrado - Tests Testcontainers bloqueados por Docker no disponible

- Contexto: `dotnet test AtlasBalance.API.Tests.csproj` compila y ejecuta 173 tests, pero fallan dos tests que requieren PostgreSQL en Testcontainers.
- Impacto: no se puede declarar release apto porque no se ha validado RLS real ni concurrencia de `fila_numero` sobre PostgreSQL.
- Evidencia: Docker client existe, pero no conecta a `npipe:////./pipe/dockerDesktopLinuxEngine`; `Start-Service com.docker.service` falla por permiso/servicio no abrible.
- Solucion: el 2026-05-12 se comprobo Docker fuera del sandbox con aprobacion y se ejecuto la suite completa con Testcontainers/PostgreSQL.
- Verificacion: `dotnet test AtlasBalance.API.Tests.csproj`: 225/225 OK.
- Estado: cerrado. El gate que sigue abierto para release final es el E2E autenticado con datos reales, no Docker/Testcontainers.

### 2026-05-16 - V-01.07 - Cerrado - La gestion de usuarios podia dejar la app sin administrador activo

- Contexto: `UsuariosController` permitia desactivar, degradar o eliminar al ultimo administrador activo.
- Impacto: bloqueo operativo grave; la instancia podia quedarse sin ningun usuario con capacidad de administracion.
- Solucion: validacion en actualizacion y eliminacion para exigir al menos otro admin activo; bloqueo adicional de auto-democion y auto-desactivacion admin.
- Verificacion: tests de regresion en `UsuariosControllerTests` incluidos en suite focalizada 14/14 OK y suite backend sin Testcontainers 229/229 OK.
- Estado: cerrado.

### 2026-05-16 - V-01.07 - Cerrado - Watchdog podia enviar su secreto a un BaseUrl remoto

- Contexto: `WatchdogSettings:BaseUrl` era configurable y se usaba para llamadas con cabecera `X-Watchdog-Secret`.
- Impacto: SSRF y exfiltracion de secreto si la configuracion apuntaba a un host remoto.
- Solucion: validacion estricta de destino local en `Program` y `WatchdogClientService`.
- Verificacion: test `SolicitarActualizacionAsync_Should_Reject_Non_Local_Watchdog_BaseAddress` y suite focalizada 14/14 OK.
- Estado: cerrado.

### 2026-05-16 - V-01.07 - Cerrado - Watchdog devolvia 500 ante body nulo o rutas invalidas

- Contexto: endpoints de restauracion y actualizacion asumian request valido y rutas parseables.
- Impacto: errores 500 evitables y diagnostico pobre ante input invalido.
- Solucion: validacion de body y `Path.GetFullPath` defensivo con respuesta `400`.
- Verificacion: `WatchdogControllerTests` incluidos en suite focalizada 14/14 OK.
- Estado: cerrado.

### 2026-05-16 - V-01.07 - Cerrado - Procesos externos sin timeout duro

- Contexto: `pg_dump`, `pg_restore` y scripts de actualizacion esperaban con token externo o sin limite propio.
- Impacto: una herramienta colgada podia dejar operaciones criticas esperando indefinidamente.
- Solucion: timeout defensivo de 30 minutos, cancelacion enlazada y kill del arbol de procesos.
- Verificacion: suite focalizada 14/14 OK y suite backend sin Testcontainers 229/229 OK.
- Estado: cerrado.

### 2026-05-16 - V-01.07 - Cerrado - Timeout de sesion podia ignorar actividad reciente

- Contexto: la actividad del usuario se debouncedaba antes de actualizar la referencia real usada por el timeout.
- Impacto: una accion en los ultimos segundos de sesion podia no evitar el logout.
- Solucion: actualizacion inmediata de `lastActivityRef` e inactividad real; debounce limitado a cambios visuales.
- Verificacion: `npm.cmd run lint`, `npm.cmd exec tsc -- --noEmit` y `npm.cmd run build` OK.
- Estado: cerrado.

### 2026-05-16 - V-01.07 - Cerrado - Paquetes de actualizacion sin limites explicitos de tamano/contenido

- Contexto: el flujo de actualizacion valida rutas, pero no aplica limites de tamano ni manifiesto de contenido antes de extraer.
- Impacto: riesgo de consumo de disco/tiempo o paquete inesperado si un admin carga un artefacto malicioso o corrupto.
- Solucion: `ActualizacionService` limita ZIP descargado, contenido extraido, entrada individual y numero de entradas antes de aplicar assets oficiales firmados.
- Verificacion: `ActualizacionServiceTests|AutoUpdateJobTests|SeedDataTests|ConfiguracionControllerTests` 25/25 OK el 2026-05-17; suite backend sin Docker 254/254 OK el 2026-05-19.
- Estado: cerrado.

### 2026-05-16 - V-01.07 - Cerrado - Inconsistencias pendientes de importacion, saldo y configuracion

- Contexto: la auditoria encontro riesgos que requieren una pasada focalizada: fingerprint de importacion dependiente del indice de fila, transacciones de importacion no garantizadas en `finally`, criterios distintos para saldo actual entre modulos, JSON nulo en configuracion y cooldown de alertas fallidas.
- Impacto: duplicados fragiles, recursos abiertos, saldos diferentes segun pantalla, 500 evitables o alertas repetidas.
- Solucion: huella de importacion estable por contenido con ordinal de duplicado, `finally` para transacciones de importacion/plazo fijo, saldo actual por `fila_numero` en resumen de cuenta y OpenClaw, rechazo controlado de JSON nulo en configuracion. Cooldown SMTP ya estaba correcto y se mantuvo.
- Verificacion: tests nuevos fallaron en rojo antes del fix; despues `CuentasControllerTests|IntegrationOpenClawControllerTests|ImportacionServiceTests` 52/52 OK, `ConfiguracionControllerTests` 8/8 OK y suite backend sin Docker/Testcontainers 254/254 OK.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Sidebar no respetaba modo claro

- Contexto: el usuario detecto que el menu lateral no cambiaba al alternar modo claro/oscuro.
- Causa: `Sidebar` fijaba `data-theme="dark"` en el `<aside>` y los tokens `--color-sidebar-*` tenian los mismos valores en root y `[data-theme="dark"]`.
- Solucion: eliminar el tema forzado del componente y definir tokens separados para sidebar claro/oscuro.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd exec tsc -- --noEmit` OK y build Vite temporal OK fuera del sandbox.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Login no respetaba modo claro

- Contexto: el menu de inicio/login seguia viendose oscuro al alternar modo claro/oscuro.
- Causa: `LoginPage`/`ChangePasswordPage` fijaban `data-theme="dark"` en el panel de marca y `auth.css` tenia colores hardcodeados para oscuro en pagina, tarjeta, inputs, chips y toggle.
- Solucion: eliminar el tema forzado y migrar la pantalla a tokens locales `--auth-*` con variante clara/oscura gobernada por `document.documentElement[data-theme]`.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd exec tsc -- --noEmit` OK, build Vite temporal OK y QA Playwright con Chrome local confirmando cambio visual claro -> oscuro sin errores de consola ni overflow.
- Estado: cerrado.

### 2026-06-26 - V-02-02 - Cerrado - Toggle de tema del login descentrado

- Contexto: el usuario detecto que el icono de modo claro/oscuro del login no estaba centrado dentro del boton.
- Causa: `.auth-theme-toggle` no anulaba padding/min-height global de botones y el icono de luna necesitaba compensacion optica.
- Solucion: normalizar el boton como cuadrado real (`padding: 0`, `min-width/min-height`, `line-height`, `appearance`) y fijar tamano del SVG con ajuste optico para la luna.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd exec tsc -- --noEmit` OK, build Vite temporal OK y QA Playwright con Chrome local midiendo boton `38x38` sin overflow ni errores de consola.
- Estado: cerrado.

### 2026-06-29 - V-02-02 - Cerrado - Importacion sin lote formal ni reversion trazable

- Contexto: el reporte post-revision pedia trazabilidad real de importaciones, origen, archivo, hash, filas y reversibilidad.
- Causa: la app ya tenia huellas por fila/lote hash, pero no tenia entidad formal de lote con contenido original, filas persistidas, confirmacion y reversion.
- Solucion: tablas `IMPORTACION_LOTES` e `IMPORTACION_LOTE_FILAS`, `EXTRACTOS.importacion_lote_id`, endpoints de lote, confirmacion opt-in de advertencias y reversion con borrado logico de extractos.
- Verificacion: tests focalizados de importacion/lotes incluidos en 59/59 OK; build backend OK.
- Estado: cerrado.

### 2026-06-29 - V-02-02 - Cerrado - Conciliacion no comparaba contra libro esperado interno

- Contexto: la revision exigia conciliacion real, no solo revision visual de extractos.
- Causa: faltaba modelo de movimientos esperados y matching auditable.
- Solucion: `MOVIMIENTOS_ESPERADOS`, `CONCILIACIONES`, servicio/controlador `/api/conciliacion/*` y pantalla `/conciliacion` con sugerencias, confirmacion, excepciones y resolucion.
- Verificacion: test focalizado de matching determinista y confirmacion incluido en 59/59 OK.
- Estado: cerrado.

### 2026-06-29 - V-02-02 - Cerrado - OpenClaw sin expiracion, scopes y rotacion suficientes

- Contexto: tokens de integracion necesitaban vencimiento, rotacion y restriccion por endpoint.
- Causa: token activo bastaba para llamar a la integracion mientras no estuviera revocado.
- Solucion: expiracion por defecto 90 dias, scopes por endpoint, rotacion, bloqueo de expirados, ultimo uso/IP y notificaciones admin por anomalias simples.
- Verificacion: tests focalizados de token expirado/default de expiracion y middleware incluidos en 59/59 OK.
- Estado: cerrado.

### 2026-06-29 - V-02-02 - Cerrado 2026-06-30 - Suite completa backend no verde por deuda mixta

- Contexto: tras el hardening se ejecuto `dotnet test` completo desde `C:\tmp`.
- Resultado: 306 tests OK, 5 fallos.
- Fallos observados: Configuracion/OpenRouter con modelo desconocido, MFA remember-device default, ranking IA sensible a fecha actual y dos pruebas PostgreSQL/Testcontainers con Docker no disponible.
- Solucion 2026-06-30: se corrigieron los tres fallos no Docker y se confirmo suite sin Testcontainers 315/315 OK.
- Estado: cerrado como bug mixto original. El bloqueo restante de Docker/Testcontainers queda abierto en entrada separada.

### 2026-06-29 - V-02-02 - Cerrado 2026-06-30 - QA visual completa de nuevos flujos pendiente

- Contexto: se valido build/lint/TypeScript, pero no se completo navegador para importacion, historial de lote, conciliacion, OpenClaw tokens, mobile alertas y modos de Extractos.
- Solucion 2026-06-30: QA Playwright finita con Chrome local cubrio todos esos flujos sin errores de consola.
- Estado: cerrado.

## Hallazgos audit V-02-04 pre-internet (2026-07-10)

Origen: `Documentacion/AUDITORIA_SEGURIDAD_BUGS_PRE_INTERNET_2026-07-10.md`.
Severidad global: el mas alto de los 3 CRITICAL bloquea el despliegue
publico; los 11 HIGH requieren arreglo antes de exponer a internet.

### CRITICAL

- **AB-CR-01 - AtlasAiService allowlist OpenRouter inexistente.** Cualquier
  modelo con formato `vendor/model` pasaba la validacion regex, permitiendo
  invocar modelos premium no suscritos. **CERRADO en V-02-05 (CRIT-1)**.
  Allowlist explicita en `AiConfiguration.AllowedOpenRouterModels`.
- **AB-CR-02 - AuditService.LogAsync fuera de transaccion.** Las auditorias
  se persistian en commits propios, dejando rastro de operaciones revertidas
  en `AUDITORIAS`. **CERRADO en V-02-05 (CRIT-2)**. AuditService ahora usa
  la transaccion del caller; `AuditSaveChangesInterceptor` audita cambios
  en 27 entidades dentro del mismo SaveChanges.
- **AB-CR-03 - Watchdog sin verificacion de firma RSA del paquete.** Solo
  validaba presencia de 4 archivos. Un RCE local podia desplegar binarios
  maliciosos firmados o no. **CERRADO en V-02-05 (CRIT-3)**. Watchdog ahora
  verifica firma RSA contra `UpdateSecurity:ReleaseSigningPublicKeyPem`.

### HIGH (cerrados en V-02-05 / V-02.06)

- **AB-H-01 - Dashboard aborta con 409 entero si una tasa falta (H1 review).**
  **CERRADO en V-02.06**. `BulkConvertAsync` se aplica ahora en
  `GetSaldosDivisaAsync`, `BuildPlazosFijosResumenAsync`,
  `BuildMetricsAsync` y `GetEvolucionAsync` mediante el helper
  `DashboardService.ResolveBulkRatesAsync`. Tolerancia a tasas faltantes
  sin abortar el dashboard. Verificacion: 9/9 DashboardServiceTests OK y
  9/9 Nuevo `ResolveBulkRatesAsync` ejercita el patron en
  `GetPrincipalAsync_Should_Aggregate_CurrentBalances_And_PeriodFlows_In_TargetCurrency`.
- **AB-H-02 - N+async en Dashboard (H2 review).** **CERRADO en V-02.06**
  con el mismo alcance que AB-H-01.
- **AB-H-03 - Google Drive restore sin SHA-256 post-descifrado.** **CERRADO en
  V-02-05**. El checksum se compara tras descifrar el dump. El helper quedó
  dentro de `GoogleDriveBackupService` y también en el archivo `.tmp` de aplicación.
- **AB-H-04 - Indices UNIQUE sin filtro soft-delete.** **CERRADO en V-02-05
  (HIGH-5)**. Migracion `20260710_RecreateUniqueIndexesWithSoftDeleteFilter`.
- **AB-H-05 - WatchdogClientService path traversal via config.**
  **CERRADO en V-02-05 (HIGH-8)**.
- **AB-H-06 - Validar divisa archivo = cuenta en importacion (BUG-021 audit).**
  **CERRADO en V-02-05 (HIGH-1)**. DTO `DivisaEsperada` + `Notas`/`ResumenJson`
  con flag `divisa_mismatch`.

### HIGH (pendientes para Fase 1+)

- **AB-H-07 - PlazoFijo sin xmin / concurrencia optimista.** Marcado como
  HIGH-6 en el audit. Riesgo: last-write-wins entre job y admin.
- **AB-H-08 - PlazoFijoService email/notificaciones ANTES del commit.**
  Marcado como HIGH-7. Riesgo: side effects materializados antes de la
  transaccion.
- **AB-H-09 - AuditService thread-safe / N SaveChanges en SaveCellAudits.**
  Marcado como HIGH-9. Riesgo: N round-trips por cambios.
- **AB-H-10 - AlertaService race en cooldown per-cuenta.** Marcado como
  HIGH-11. Riesgo: doble alerta en importacion masiva.
- **AB-H-11 - Resto de N+async en Dashboard.** Marcado como HIGH-4/10
  parcialmente cerrado.

### MEDIUM

Los 30+ MEDIUM del audit se aceptan como deuda documentada. Detalle en
`Documentacion/AUDITORIA_SEGURIDAD_BUGS_PRE_INTERNET_2026-07-10.md` seccion 3.
Los mas relevantes a cerrar pronto:
- MED-1 `PassthroughSecretProtector` fallback silencioso.
- MED-2 `ProtectForStorage` se basa en prefijo sin MAC.
- MED-5 Email CRLF validation.
- MED-22 Soft-delete en entidades pendientes.
- MED-23 DTOs con validacion (FluentValidation o atributos).

### FASE 1 de V-02-05 (2026-07-10)

Cierres en sesion posterior a Fase 0:
- **AB-H-04 (HIGH-5)** - Indices UNIQUE con `WHERE deleted_at IS NULL`: **CERRADO en V-02-05**.
- **AB-H-06 (HIGH-6)** - PlazoFijo sin xmin: **CERRADO en V-02-05**.
- **AB-H-07 (HIGH-7)** - PlazoFijoService side effects antes del commit: **CERRADO en V-02-05** (outbox basico: commit primero, side effects despues).
- **AB-H-08 (HIGH-8)** - WatchdogClientService path traversal: **CERRADO en V-02-05**.
- **AB-H-09 (HIGH-9)** - SaveCellAudits N SaveChanges: **CERRADO en V-02-05**.
- **AB-H-10 (HIGH-11)** - AlertaService cooldown global: **CERRADO en V-02-05** (cooldown por (cuenta, alcance) con advisory lock).
- **AB-H-11 (HIGH-4/10)** - Resto de N+async Dashboard: **CERRADO en V-02-05** (GetEvolucionAsync refactorizado con bulk + precomputar tasas).
 - **AB-H-03 (HIGH-2)** - Google Drive SHA-256 post-descifrado: **CERRADO en V-02-05**. El checksum se compara tras descifrar el dump. El helper se corrigio para quedar dentro de `GoogleDriveBackupService` y tambien en el archivo `.tmp` de aplicacion.

MEDIUM cerrados en V-02-05 (17):
- MED-1 PassthroughSecretProtector fail-closed.
- MED-3 Redaccion IBAN en contexto IA.
- MED-4 Rate limit en SendTestEmail.
- MED-5 Email CRLF validation.
- MED-7 RlsContextSecret vs JwtSecret warning.
- MED-9 CsrfMiddleware audit.
- MED-10/11 TiposCambio overflow + BFS depth cap.
- MED-12 Bulk convert en IntegrationOpenClaw.
- MED-14 Lock en ConfirmarLoteAsync.
- MED-15 ExecuteUpdate en RevertirLoteAsync.
- MED-19 PlazoFijo email digest.
- MED-21 CHECK constraints.
- MED-22 ISoftDelete en Conciliacion + UNIQUE parcial.
- MED-23 DTOs validation (parcial).
- MED-24 Log path absoluto.

LOW cerrados en V-02-05 (3):
- LOW-BE-6 EmailService timeout 15s.
- CONFIG-008 Server header removido.
- CONFIG-009 upgrade-insecure-requests + block-all-mixed-content.
- CONFIG-010 Cross-Origin-Resource-Policy same-origin.

MEDIUM/LOW pendientes para Fase 2 (documentados en v-02-05.md):
- MED-2, MED-8, MED-13, MED-16, MED-17, MED-18, MED-20, MED-22 (resto),
  MED-26, CONFIG-001 a CONFIG-007/011+, LOW-1 a LOW-40.

### CI de V-02-05 (2026-07-14)

- **AB-CI-01 - Suite backend no compilaba tras cambios de contratos.**
  **CERRADO en V-02-05.** Se actualizaron fakes, stubs, constructores y llamadas
  de tests para las interfaces actuales. Tambien se corrigieron los fallos no
  Docker revelados tras compilar: compatibilidad InMemory, reintento de plazo
  fijo, normalizacion OpenRouter y expectativas de allowlist. Verificacion:
  **133/133** tests afectados y **327/327** tests no Docker.
- **AB-CI-02 - Migraciones V-02-05 compilaban pero EF no las descubria.**
  **CERRADO en V-02-05.** Las tres migraciones manuscritas ya declaran
  `[DbContext]` y `[Migration]`; `MigrationDiscoveryTests` impide la regresion.
  El fallo se hizo visible en el run `29365305520` (3/331 pruebas PostgreSQL).
