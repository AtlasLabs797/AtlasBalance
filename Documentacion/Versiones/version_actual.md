# Version actual

Version actual del proyecto: `V-02.08`

Fecha de registro: 2026-08-23

## Fuentes de version

- Runtime backend: `Atlas Balance/Directory.Build.props`
- Runtime frontend: `Atlas Balance/frontend/package.json`
- Trazabilidad de paquete: `Atlas Balance/VERSION`
- Documentacion de version: `Documentacion/Versiones/v-02.08.md`

## Base anterior

- Version de trabajo previa: `V-02.07`
- Documentacion historica: `Documentacion/Versiones/v-02.07.md`

## Reglas

- Toda modificacion debe registrarse bajo la version actual.
- Antes de implementar cambios, revisar los archivos de esta carpeta cuyo nombre empiece por `v` o `version`.
- Si se crea una version nueva, actualizar este archivo antes de cerrar la tarea.
- Si se pide subir a GitHub, crear una rama con el nombre de esta version y hacer push a esa rama.

## Estado de V-02.09

El ciclo de reescritura del asistente IA quedo registrado en
`Documentacion/Versiones/v-02.09.md` como documento historico. Por decision
del 2026-08-23 la version vigente vuelve a ser `V-02.08`: el trabajo nuevo se
registra en `v-02.08.md` y la bitacora.

Pendiente de realinear (no bloquea documentacion): los tres archivos runtime
(`VERSION`, `Directory.Build.props`, `frontend/package.json`) conservan el
valor `V-02.09` / `2.9.0` del ciclo anterior mientras no se decida rebajarlos.
