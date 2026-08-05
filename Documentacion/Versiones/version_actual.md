# Version actual

Version actual del proyecto: `V-02.09`

Fecha de registro: 2026-08-05

## Fuentes de version

- Runtime backend: `Atlas Balance/Directory.Build.props`
- Runtime frontend: `Atlas Balance/frontend/package.json`
- Trazabilidad de paquete: `Atlas Balance/VERSION`
- Documentacion de version: `Documentacion/Versiones/v-02.09.md`

## Base anterior

- Version de trabajo previa: `V-02.08`
- Documentacion historica: `Documentacion/Versiones/v-02.08.md`

## Reglas

- Toda modificacion debe registrarse bajo la version actual.
- Antes de implementar cambios, revisar los archivos de esta carpeta cuyo nombre empiece por `v` o `version`.
- Si se crea una version nueva, actualizar este archivo antes de cerrar la tarea.
- Si se pide subir a GitHub, crear una rama con el nombre de esta version y hacer push a esa rama.

## Origen de V-02.09

V-02.09 arranca la ejecucion del plan de reescritura del asistente IA financiero
sobre Atlas Balance. La version queda abierta durante todo el ciclo de 12 fases
previsto; solo se cierra cuando se publique el paquete firmado y los gates de
CI (incluido el modulo PostgreSQL con Testcontainers) esten verdes.

## Publicacion

La release anterior es `V-02.08-win-x64`, pendiente de publicar. La rama de
trabajo de V-02.09 parte de `V-02.08` (commit `c040d1a`).
