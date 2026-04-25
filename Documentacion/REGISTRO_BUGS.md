# Registro de bugs

## Abiertos

### 2026-04-20 - V-01.02 - Estado Git local no fiable

- Contexto: `git status --short` funciona, pero lista practicamente todo el arbol como `untracked`.
- Causa probable: copia local/repo recreado sin historial o indice util para esta carpeta.
- Impacto: no se puede obtener diff fino ni preparar commit fiable desde esta copia sin reparar el estado Git.
- Estado: abierto. No se ha tocado `.git` para evitar empeorar el repositorio local.

## Cerrados

### 2026-04-25 - V-01.04 - Hallazgos de auditoria de uso, bugs y seguridad corregidos

- Contexto: la auditoria V-01.04 dejo abiertos tres puntos malos para release: Tailwind/shadcn contra el stack canonico, `CuentasController.Resumen` con contrato mas pobre que el resumen de extractos, y accesibilidad incompleta en controles propios.
- Solucion: eliminados Tailwind/shadcn y sus imports/configuracion; `CuentaResumenResponse` ahora expone titular, tipo de cuenta, notas, ultima actualizacion y `plazo_fijo`; `DatePickerField` tiene etiquetas completas y navegacion por flechas/Home/End; `ConfirmDialog` atrapa Tab dentro del modal; `AppSelect` abre/cierra con Enter/Espacio.
- Verificacion: busqueda sin restos directos de Tailwind/shadcn, `npm.cmd run lint` OK, `npm.cmd run build` OK, `wwwroot` sincronizado, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, NuGet sin vulnerabilidades y backend tests 108/108 OK.

### 2026-04-25 - V-01.04 - Gradientes decorativos de UI reducidos

- Contexto: la auditoria marco fondos `radial-gradient` y degradados suaves como huella de UI generica y contraria al criterio visual del proyecto.
- Solucion: reemplazados fondos decorativos de `body`, login, panels, KPIs, listas y empty states por superficies planas basadas en tokens. Se mantienen solo degradados funcionales de `select` y skeleton.
- Verificacion: busqueda de degradados deja solo usos funcionales, `npm.cmd run lint` OK y `npm.cmd run build` OK.

### 2026-04-25 - V-01.04 - Endpoints nuevos NPE-able si el cuerpo o las listas llegaban null

- Contexto: `POST /api/alertas`, `PUT /api/alertas/{id}`, `POST /api/cuentas/{id}/plazo-fijo/renovar` y `POST /api/importacion/plazo-fijo/movimiento` accedian a `request.SaldoMinimo`, `request.DestinatarioUsuarioIds.Count` o `request.CuentaId` sin antes validar que el body no fuera null. Un cliente autorizado mandando `null` o JSON sin la propiedad colapsaba la peticion en 500.
- Riesgo: ruido en logs, falta de respuesta clara al consumidor y degradacion gratuita ante input malformado. Solo afecta a admins (todas son rutas con `[Authorize(Roles = "ADMIN")]` o `[Authorize]`), pero el contrato seguro debe ser 400 con mensaje, no 500.
- Solucion: validacion temprana del body (`if (request is null) return BadRequest(...)`) y normalizacion de `DestinatarioUsuarioIds` con `?? []` antes de consumirla.
- Verificacion: backend Release build OK, `dotnet test` 107/107 OK, NuGet sin vulnerabilidades.

### 2026-04-25 - V-01.04 - Manifiesto frontend mantenia minimos vulnerables de dependencias

- Contexto: el `package-lock.json` resolvia versiones seguras, pero `package.json` seguia declarando minimos antiguos: `axios ^1.7.9` y `react-router-dom ^6.28.0`.
- Riesgo: instalaciones regeneradas sin lockfile fiable podian resolver rangos afectados por advisories recientes de Axios y React Router. Confiar solo en el lockfile aqui era una trampa tonta.
- Solucion: actualizado el manifiesto a `axios ^1.15.2` y `react-router-dom ^6.30.3`, dejando el lockfile en versiones verificadas.
- Verificacion: `npm.cmd audit --audit-level=moderate` OK, frontend lint/build OK, backend tests 107/107 OK y NuGet sin vulnerabilidades.

### 2026-04-25 - V-01.04 - Selector de fecha nativo no seguia el diseno Atlas al abrirse

- Contexto: el campo cerrado de fecha se veia integrado, pero el calendario desplegado seguia siendo el popup nativo del navegador.
- Solucion: creado `DatePickerField` propio y reemplazados los `input type="date"` del frontend.
- Verificacion: frontend lint/build OK, `wwwroot` sincronizado y prueba visual en navegador de `/cuentas` sin errores de consola.

### 2026-04-25 - V-01.04 - Detalle de plazo fijo ocultaba vencimiento

- Contexto: el dashboard de una cuenta de plazo fijo no mostraba cuando se acababa el plazo, aunque el dato existia en la ficha de cuenta.
- Solucion: el resumen de cuenta expone `tipo_cuenta` y `plazo_fijo`; `CuentaDetailPage` muestra vencimiento, dias restantes/vencido y estado bajo el titulo.
- Verificacion: backend Release build OK, frontend lint/build OK y `wwwroot` sincronizado.

### 2026-04-25 - V-01.04 - Actualizaciones post-instalacion no dejaban flujo operativo completo

- Contexto: tras tener Atlas Balance instalado, el update manual no refrescaba scripts instalados, no actualizaba runtime y no comprobaba salud HTTP real al final.
- Solucion: `update.ps1` soporta `-PackagePath`, validacion temprana de paquete, copia de scripts/wrappers operativos a `C:\AtlasBalance`, actualizacion de `VERSION`/`atlas-balance.runtime.json` y health check con `curl.exe -k`.
- Verificacion: parser PowerShell OK y documentacion actualizada con ambos modos de ejecucion.

### 2026-04-25 - V-01.04 - Instalacion Windows Server 2019 con flujo operativo fragil

- Contexto: el operador intento instalar desde carpeta fuente, habia paquete V-01.03 mientras se validaba V-01.04, `winget` no era fiable, `install.cmd` podia desordenar parametros, `Invoke-WebRequest` daba falso negativo y una reinstalacion sobre BD existente generaba credenciales iniciales falsas.
- Solucion: validacion temprana de paquete release, mensajes duros para carpeta equivocada, fallback documentado a PostgreSQL manual 16+/17, wrappers con codigo de salida, health check con `curl.exe -k`, deteccion de usuarios existentes y script `Reset-AdminPassword.ps1` para reset controlado.
- Verificacion: parser PowerShell OK y ejecucion de instalador/wrapper desde carpeta fuente falla con mensaje claro de paquete invalido.

### 2026-04-25 - V-01.04 - Reinstalacion reutilizaba PFX con password nueva

- Contexto: reinstalar sobre `C:\AtlasBalance` existente podia dejar `AtlasBalance.API` parado con `CryptographicException: La contraseña de red especificada no es válida`.
- Solucion: `Instalar-AtlasBalance.ps1` elimina `atlas-balance.pfx` y `atlas-balance.cer` existentes antes de generar un certificado HTTPS nuevo, evitando que el PFX viejo quede asociado a una password nueva en `appsettings.Production.json`.
- Verificacion: diagnostico reproducido por traza de Windows Event Log; correccion revisada en el flujo `New-AtlasCertificate`.

### 2026-04-25 - V-01.04 - Importacion embebida bloqueada por anti-frame

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

- Contexto: `frontend/src` tenia fixes ya aplicados, pero `backend/src/GestionCaja.API/wwwroot` seguia sirviendo bundles antiguos.
- Solucion: recompilado frontend, limpiado `wwwroot` de forma controlada y copiado `frontend/dist`.

### 2026-04-20 - V-01.02 - Secretos de desarrollo en archivos versionables

- Contexto: `appsettings.json`, `appsettings` de Watchdog y `docker-compose.yml` contenian credenciales/defaults de desarrollo.
- Solucion: se dejaron sin secretos versionables, se añadieron plantillas y `.env.example`, y el seed admin exige password configurada.

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

### 2026-04-25 - V-01.04 - Importacion bloqueaba filas informativas de banco

- Contexto: filas con solo concepto y sin fecha/monto/saldo se marcaban como error fatal en la validacion de importacion.
- Solucion: esas filas pasan a advertencias importables; se heredan fecha y saldo de la ultima fila valida anterior y se importa monto `0`. Las filas parcialmente rotas o ambiguas siguen bloqueadas.

### 2026-04-25 - V-01.04 - JSX sobrante y lint estricto durante implementacion de plazo fijo

- Contexto: `npm.cmd run build` fallo en `CuentasPage.tsx` por un cierre JSX duplicado; despues `npm.cmd run lint` fallo por warning de Fast Refresh en `components/ui/button.tsx`.
- Solucion: corregido el JSX y documentada una excepcion local de ESLint para el componente UI que exporta `Button` y `buttonVariants`.
