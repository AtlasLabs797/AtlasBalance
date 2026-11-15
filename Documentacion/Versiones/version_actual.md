# Version actual

Version actual del proyecto: `V-03.02`

Fecha de registro: 2026-10-04

## Fuentes de version

- Runtime backend: `Atlas Balance/Directory.Build.props` (`3.2.0` / `V-03.02`)
- Runtime frontend: `Atlas Balance/frontend/package.json` (`3.2.0` / `V-03.02`)
- Trazabilidad de paquete: `Atlas Balance/VERSION` (`V-03.02`)
- Documentacion de version: `Documentacion/Versiones/v-03.02.md`

Los tres archivos runtime estan alineados en `V-03.02` / `3.2.0`.

El script `Atlas Balance/scripts/Check-VersionAlignment.ps1` verifica
ademas `frontend/package-lock.json`, `backend/.../Data/SeedData.cs`,
`.github/workflows/release.yml`, `scripts/Build-Release.ps1`,
`scripts/Instalar-AtlasBalance.ps1` y `scripts/install.ps1`.

## Base anterior

- Version anterior: `V-03.01`
- Documentacion historica: `Documentacion/Versiones/v-03.01.md`

## Reglas

- Toda modificacion se registra bajo la version actual.
- Antes de implementar cambios, revisar los archivos de esta carpeta cuyo nombre empiece por `v` o `version`.
- Si se crea una version nueva, actualizar este archivo antes de cerrar la tarea.
- Si se pide subir a GitHub, crear una rama con el nombre de esta version y hacer push a esa rama.

## Origen de la version

La rama `V-03.02` se crea desde `main` actualizado el 2026-10-04, en el
commit `fa8abef` (`Merge pull request #39 from AtlasLabs797/claude/eager-turing-01qoda`).
El cambio inicial de esta version alinea las fuentes runtime y la configuracion
de release en `V-03.02` / `3.2.0`; el trabajo funcional se documentara en
`v-03.02.md`.
