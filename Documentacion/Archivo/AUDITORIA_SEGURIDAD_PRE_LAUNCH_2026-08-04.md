# Auditoria de seguridad pre-launch - 2026-08-04

## Dictamen

**El codigo queda sin fallos abiertos en los 13 controles revisados, pero el
despliegue sigue en NO-GO hasta completar los gates operativos del final de este
documento.** Una revision de repositorio no demuestra por si sola que el ZIP
final, Windows Server, PostgreSQL, certificado, proxy y ACL efectivas esten bien
configurados.

Algunos puntos del checklist generico requieren adaptacion a Atlas Balance:
es una aplicacion on-premise de mismo origen, no recibe `multipart/form-data`, y
guarda secretos runtime en ficheros externos al binario con ACL exclusiva de
Administradores/SYSTEM. Forzar variables de entorno no aporta por si solo mas
seguridad en un servicio Windows.

## Matriz pass/fail

| # | Control | Resultado despues de correcciones | Evidencia y alcance |
|---|---------|-----------------------------------|---------------------|
| 1 | Secretos fuera del codigo | **PASS adaptado** | El escaner del repo no encuentra secretos. Las credenciales runtime no estan en Git ni compiladas: el instalador las genera y guarda en configuracion externa con ACL restringida. No se exige que todo sea variable de entorno porque no es una mejora automatica en Windows Service. |
| 2 | Validacion backend de input | **PASS** | ASP.NET valida DTOs y los servicios aplican reglas de negocio. Se cerraron cotas ausentes en configuracion IA/Drive, permisos masivos, mapeos JSON y columnas extra, rutas del Watchdog y cuerpos HTTP. |
| 3 | Queries parametrizadas | **PASS** | EF Core parametriza las consultas. Los usos de SQL crudo revisados usan parametros; el unico identificador interpolado relevante es una funcion privada seleccionada mediante allowlist, no entrada del usuario. |
| 4 | Autenticacion y autorizacion en endpoints | **PASS** | Los 24 controladores y 157 acciones tienen autorizacion o anonimato explicito; OpenClaw y Watchdog usan sus autenticadores dedicados. Las excepciones anonimas son login/refresh/CSRF, salud y telemetria acotada. `ControllerAuthorizationCoverageTests` evita endpoints nuevos sin clasificar. |
| 5 | Errores sin detalles internos | **PASS** | La API usa respuestas genericas y el handler de produccion no expone stack traces. Se elimino del Watchdog la devolucion de rutas absolutas del paquete de actualizacion. |
| 6 | CORS restringido | **PASS / no aplica en produccion** | Produccion sirve frontend y API desde el mismo origen y no registra CORS. Solo Development habilita `http://localhost:5173`. Introducir una allowlist de dominio en produccion seria codigo innecesario mientras no exista un frontend de origen distinto. |
| 7 | Debug desactivado | **PASS en codigo** | Swagger, dashboard Hangfire y detalles de desarrollo estan limitados a `Development`; `Production` es el entorno por defecto de ASP.NET Core. Debe verificarse la variable efectiva del servicio instalado. |
| 8 | Cookies seguras | **PASS adaptado** | Cookies de acceso, refresh y MFA: `Secure`, `HttpOnly` y `SameSite=Strict`. La cookie CSRF es `Secure` y `SameSite=Strict`, pero deliberadamente no `HttpOnly` porque el patron double-submit necesita que React lea el token y lo envie en `X-CSRF-Token`. |
| 9 | HTTPS forzado | **PASS en arquitectura** | En produccion hay HSTS y redireccion HTTPS. El instalador usa Kestrel TLS o proxy inverso TLS con Kestrel ligado a loopback. Falta validar el certificado y la topologia reales del servidor. |
| 10 | Rate limiting | **PASS** | La API limita login, recuperacion, integracion y operaciones costosas. Se anadio al Watchdog limite global 120/min por IP, 5/min para restaurar/actualizar, cuerpo maximo de 16 KiB y `429` con `Retry-After`. `/health` queda exento sin compartir particion con el limitador global. |
| 11 | Ficheros validados y almacenados con seguridad | **PASS adaptado** | No hay uploads multipart. El CSV pegado tiene limites de 5 MiB, 50.000 filas, 64 columnas extra y 4.096 caracteres por celda. La importacion Drive valida ID y tamano por metadata, `Content-Length`, flujo real y descifrado (10 GiB configurable), borrando parciales. Instalador y actualizador fijan DACL exacta solo para Administradores/SYSTEM en `backups` y `exports`. |
| 12 | Dependencias sin vulnerabilidades criticas conocidas | **PASS a 2026-08-04** | `npm audit` da 0 vulnerabilidades en 283 dependencias. `dotnet list package --vulnerable --include-transitive` da 0 hallazgos en API, Watchdog, API.Tests y Caching.Tests. Es una fotografia temporal, no una garantia futura. |
| 13 | Sin credenciales/datos/artefactos de desarrollo | **PASS en repo** | No hay secretos, credenciales de prueba ni datos demo habilitables en produccion. El empaquetado elimina sourcemaps y excluye `.env`, logs, certificados privados, dumps, `node_modules`, `bin/obj` y otros artefactos. Falta inspeccionar el ZIP exacto que se desplegara. |

## Fallos encontrados y corregidos

1. **Importacion desde Google Drive sin limite de descarga/descifrado.** Un
   fichero grande podia agotar disco o memoria operativa. Se valida el ID antes
   de crear el job, el tamano en tres capas y se eliminan ficheros parciales.
2. **Watchdog sin rate limiting ni limites fuertes de request.** Se anadieron
   cuotas global/sensible, cuerpo maximo y validacion de DTOs. Los intentos con
   secreto incorrecto tambien consumen cuota.
3. **Watchdog filtraba una ruta absoluta en un error.** La respuesta es ahora
   generica; el detalle queda solo en logs protegidos.
4. **Entradas administrativas sin cotas suficientes.** Se limitaron campos IA
   y Drive, listas de permisos, JSON de formatos y columnas extra.
5. **Backups/exportaciones heredaban ACL del directorio padre.** Instalaciones
   nuevas y actualizaciones reemplazan la DACL por una allowlist exacta de
   Administradores y SYSTEM. Los dumps locales siguen en claro: la defensa ante
   robo del volumen es BitLocker, que debe estar activo.
6. **Defecto detectado al revisar el trabajo delegado.** La primera version del
   limitador reutilizaba la particion de `/health` y podia dejar exenta la IP en
   otros endpoints; se separaron las claves y se fijo con una prueba. Tambien se
   hizo obligatorio que toda implementacion de cifrado respete el limite de
   descifrado y que la reparacion de ACL elimine permisos explicitos antiguos.

## Verificacion ejecutada

- Secret scanner: 558 archivos revisados, 0 hallazgos; fixtures del scanner OK.
- Alineacion de version: `V-02.07` / `2.7.0`, OK.
- Compilacion estable de API, Watchdog y tests: 0 errores. Permanecen warnings
  preexistentes; el restore aislado emitio `NU1900` al no poder consultar el
  feed, separado del SCA online que si termino sin hallazgos.
- Pruebas afectadas: **74/74 correctas**.
- Suite completa: **639 correctas y 17 bloqueadas**; los 17 fallos son
  exclusivamente `PostgresFixture`/Testcontainers porque Docker no responde en
  `npipe://./pipe/docker_engine`, no regresiones del cambio.
- SCA npm/NuGet: 0 vulnerabilidades conocidas en la consulta del 2026-08-04.
- Parser de instalador y actualizador: OK; constructores ACL comprobados.
- `git diff --check`: OK.

## Gates obligatorios antes de produccion

1. Construir el ZIP de release desde este arbol y repetir sobre el artefacto:
   secretos, sourcemaps, `.env`, logs, dumps, certificados y credenciales.
2. En el Windows Server real comprobar `ASPNETCORE_ENVIRONMENT=Production`,
   DACL de configuracion/backups/exports y BitLocker activo en el volumen.
3. Validar certificado confiable, HSTS, redireccion HTTP a HTTPS y, si hay proxy,
   `KnownProxies`/`KnownNetworks` y que Kestrel solo escuche en loopback.
4. Ejecutar las 17 pruebas PostgreSQL/Testcontainers en un host con Docker o CI.
5. Ejecutar `pg_dump` con el rol owner real y una restauracion completa, con
   recuentos no nulos y comprobacion RLS con `app_user`.
6. Hacer un smoke test autenticado de permisos, login/rate-limit, backup,
   restauracion y descarga/importacion Drive en la instalacion candidata.

Hasta que esos seis puntos tengan evidencia, **no autorizar el lanzamiento**.

## Referencias externas

- Microsoft: entornos ASP.NET Core (`Production` es el valor por defecto):
  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments
- Microsoft: CORS en ASP.NET Core:
  https://learn.microsoft.com/en-us/aspnet/core/security/cors
- OWASP: patron double-submit cookie para CSRF:
  https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html
- Google Drive API: metadata `size` y descarga de ficheros:
  https://developers.google.com/workspace/drive/api/reference/rest/v3/files
  y https://developers.google.com/workspace/drive/api/guides/manage-downloads
