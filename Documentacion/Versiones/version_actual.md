# Version actual

Version actual del proyecto: `V-02.09`

Fecha de registro: 2026-08-25

## Fuentes de version

- Runtime backend: `Atlas Balance/Directory.Build.props` (`2.9.0` / `V-02.09`)
- Runtime frontend: `Atlas Balance/frontend/package.json` (`2.9.0` / `V-02.09`)
- Trazabilidad de paquete: `Atlas Balance/VERSION` (`V-02.09`)
- Documentacion de version: `Documentacion/Versiones/v-02.09.md`

Los tres archivos runtime estan **alineados** en `V-02.09` / `2.9.0`.

## Base anterior

- Version de trabajo previa: `V-02.08`
- Documentacion historica: `Documentacion/Versiones/v-02.08.md`

## Reglas

- Toda modificacion debe registrarse bajo la version actual.
- Antes de implementar cambios, revisar los archivos de esta carpeta cuyo nombre empiece por `v` o `version`.
- Si se crea una version nueva, actualizar este archivo antes de cerrar la tarea.
- Si se pide subir a GitHub, crear una rama con el nombre de esta version y hacer push a esa rama.

## Historial de la decision de version

El 2026-08-23 se decidio que la version vigente volvia a ser `V-02.08` mientras
los tres archivos runtime conservaban `V-02.09` / `2.9.0` del ciclo anterior.
Esa discrepancia queda **resuelta el 2026-08-25**: se adopta `V-02.09` como
version vigente, que es lo que ya declaraban los tres archivos runtime y lo que
nombra la rama de trabajo.

**Donde esta documentado el redisenio.** El ciclo de redisenio de Claude Design
(agosto 2026) se ejecuto y se registro bajo `v-02.08.md`, porque en ese momento
esa era la version vigente segun este archivo. No se ha movido de sitio para no
romper la trazabilidad de lo ya commiteado. Las tres entradas relevantes, todas
en `Documentacion/Versiones/v-02.08.md`, son:

1. "Verificacion y cierre del redisenio de Claude Design (2026-08-25)"
2. "Cierre de los pendientes de diseno del redisenio (2026-08-25, segunda pasada)"
3. "Sincronizacion con el proyecto de Claude Design (2026-08-25, tercera pasada)"

El trabajo **nuevo** a partir de esta fecha se registra en `v-02.09.md`.
