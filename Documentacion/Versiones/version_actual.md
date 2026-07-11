# Version actual

Version actual del proyecto: `V-02-05`

Fecha de registro: 2026-07-10

## Fuentes de version

- Runtime backend: `Atlas Balance/Directory.Build.props`
- Runtime frontend: `Atlas Balance/frontend/package.json`
- Trazabilidad de paquete: `Atlas Balance/VERSION`
- Documentacion de version: `Documentacion/Versiones/v-02-05.md`

## Base anterior

- Version de trabajo previa: `V-02-04`
- Documentacion historica: `Documentacion/Versiones/v-02-04.md`

## Reglas

- Toda modificacion debe registrarse bajo la version actual.
- Antes de implementar cambios, revisar los archivos de esta carpeta cuyo nombre empiece por `v` o `version`.
- Si se crea una version nueva, actualizar este archivo antes de cerrar la tarea.
- Si se pide subir a GitHub, crear una rama con el nombre de esta version y hacer push a esa rama.

## Origen de V-02-05

Cierra los hallazgos CRITICAL y HIGH de la auditoria
`Documentacion/AUDITORIA_SEGURIDAD_BUGS_PRE_INTERNET_2026-07-10.md`. La app V-02-04
esta endurecida para LAN con 4-8 usuarios pero no para exposicion publica a
internet. V-02-05 corrige los bloqueantes para que pueda exponerse con una base
defendible.

