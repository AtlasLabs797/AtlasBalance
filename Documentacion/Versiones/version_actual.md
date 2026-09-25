# Version actual

Version actual del proyecto: `V-03.01`

Fecha de registro: 2026-09-16

## Fuentes de version

- Runtime backend: `Atlas Balance/Directory.Build.props` (`3.1.0` / `V-03.01`)
- Runtime frontend: `Atlas Balance/frontend/package.json` (`3.1.0` / `V-03.01`)
- Trazabilidad de paquete: `Atlas Balance/VERSION` (`V-03.01`)
- Documentacion de version: `Documentacion/Versiones/v-03.01.md`

Los tres archivos runtime estan **alineados** en `V-03.01` / `3.1.0`.

El script `Atlas Balance/scripts/Check-VersionAlignment.ps1` verifica
ademas `frontend/package-lock.json`, `backend/.../Data/SeedData.cs`,
`.github/workflows/release.yml`, `scripts/Build-Release.ps1`,
`scripts/Instalar-AtlasBalance.ps1` y `scripts/install.ps1`.

## Base anterior

- Version de trabajo previa: `V-02.09`
- Documentacion historica: `Documentacion/Versiones/v-02.09.md`

## Reglas

- Toda modificacion debe registrarse bajo la version actual.
- Antes de implementar cambios, revisar los archivos de esta carpeta cuyo nombre empiece por `v` o `version`.
- Si se crea una version nueva, actualizar este archivo antes de cerrar la tarea.
- Si se pide subir a GitHub, crear una rama con el nombre de esta version y hacer push a esa rama.

## Historial de la decision de version

El 2026-08-25 se adopto `V-02.09` como version vigente, alineando los
tres archivos runtime con la documentacion. Esa version permanecio
vigente hasta el 2026-09-16, cuando se cerro `V-02.09` y se abrio
`V-03.01` partiendo del HEAD de `main` (`main` apuntaba a
`e670749` y mantenia `V-02.09` como base del hotfix
`hotfix/V-02.09-actualizador-sonda-funcional`, ya mergeado).

El bump a `V-03.01` cubre solo alineacion de fuentes; el contenido
funcional del nuevo ciclo se documentara en `v-03.01.md` a medida que
se implemente. `v-02.09.md` permanece como historico sin tocar.
