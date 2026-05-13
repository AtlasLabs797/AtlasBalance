
# ATLAS BALANCE - Instrucciones para Codex

`CLAUDE.md` en esta misma carpeta es la fuente canonica de instrucciones del proyecto. Leelo y aplicalo antes de trabajar.

Las herramientas de larga duración (tests, docker, migraciones, etc.) deben invocarse siempre con tiempos de espera (timeouts) razonables o en modo batch no interactivo. Nunca dejes un comando esperando indefinidamente

Reglas criticas:

- Never open with Great question, I'd be happy to help, or Absolutely. Just answer.
- Antes de implementar, revisar `Documentacion/Versiones/version_actual.md` y el archivo `v*` correspondiente.
- Asociar cada cambio a la version actual.
- Actualizar documentacion afectada en `Documentacion` antes de cerrar.
- Consultar `Documentacion/LOG_ERRORES_INCIDENCIAS.md` antes de resolver errores.
- Consultar `Documentacion/SKILLS_LOCALES.md` antes de usar cualquier skill local de `Skills`.
- Maximo dos intentos por la misma via cuando una herramienta falle o se encalle. Si repite el mismo error, corta, cambia de estrategia o documenta el bloqueo.
- Los atascos conocidos son Vite/Rolldown/Chromium con `spawn EPERM`, servidores temporales vivos, `robocopy /MIR`, `wwwroot` bloqueado, `dotnet` con `apphost.exe` en uso, Docker/Testcontainers no disponible y limpiezas con `Access denied`. No los trates como problemas nuevos cada vez.
- Si una validacion visual, servidor dev o herramienta externa se encalla o repite el mismo fallo, corta el intento, registra el bloqueo y sigue con lint/build/validacion estatica util.
- No arranques servidores Node/Vite/HTTP de larga duracion desde `shell_command` para validar UI. Usa comandos finitos; si hace falta navegador, renderiza con Playwright `setContent` o cierra el proceso en el mismo comando con timeout.
- Para reiniciar backend/API, no lances `dotnet` con `Start-Process` ni `[Diagnostics.Process]` desde `shell_command` si puede heredar stdout/stderr y dejar la herramienta colgada. Usa script finito con logs redirigidos, healthcheck con timeout y salida obligatoria, o valida por tests/build si el reinicio no es imprescindible.
- Si una limpieza/verificacion emite errores repetidos de permisos o salida masiva, cortala, cambia a una comprobacion acotada con timeout y registra la incidencia. No te quedes mirando ruido.
- Para Vite/Rolldown/Chromium con `spawn EPERM`, no reintentar dentro del sandbox; usar lint/build finito fuera del sandbox con aprobacion si es imprescindible o registrar bloqueo.
- Para tests Docker/Testcontainers sin Docker disponible, ejecutar suite filtrada no Docker y dejar el release bloqueado por esos tests pendientes. No venderlo como verde.
- Para GitHub, subir todo lo versionable excepto `Otros/`, `Skills/` y paquetes generados de `Atlas Balance/Atlas Balance Release`; los paquetes van como assets de GitHub Releases.
- No documentar ni loguear contrasenas, tokens, secretos ni datos privados.
- Si se pide subir a GitHub, crear una rama nueva con el nombre de la version actual y hacer push a esa rama.
