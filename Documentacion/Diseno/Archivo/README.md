# Archivo de material obsoleto de diseño

Contenido retirado del árbol activo de `Documentacion/Diseno/` en el **LOTE 8** de la
revisión integral (rama V-03.02), el **2026-11-18**.

Nada de lo que hay aquí se usa en runtime, en el build ni en la documentación viva.
Se **archiva** en vez de borrarse: todo lo que hay en esta carpeta es reversible con
`git mv` / `git revert`. No editar estos ficheros; no son fuente de verdad.

## Contenido

| Ruta anterior | Ruta actual | Motivo del archivado |
|---|---|---|
| `Documentacion/Diseno/design-tokens.css` | `Archivo/design-tokens.css` | Snapshot del sistema de diseño **anterior** al rediseño (paleta fría, Hind Madurai / National Park, sombras en escalera). El propio archivo lo marca como `OBSOLETO (2026-08-25)`. |
| `Documentacion/Diseno/Diseño/` | `Archivo/Diseño/` | 18 capturas `.png` (213 B – 2,7 KB) y `Palesta Y tipografia.txt`. Las capturas pesan menos de 3 KB y no sirven como evidencia visual (ver `Documentacion/Diseno/INFORME_IMPECCABLE_CRITIQUE_AUDIT_V-03.02.md`, sección "No hay captura renderizada"). El nombre de la carpeta además colisionaba visualmente con `Diseno/` por la tilde, lo que invite a errores de ruta. |

## Fuentes vivas (no confundir con lo archivado)

- Tokens de runtime: `Atlas Balance/frontend/src/styles/variables.css`
- Capa de tokens del proyecto de diseño: `Atlas Balance/frontend/src/styles/design-system/tokens/*.css`
- Capa de componentes: `Documentacion/Diseno/design-system/`
- Especificación: `Documentacion/Diseno/DESIGN.md`

## Nota sobre referencias

Antes de mover, se hizo `grep` de `design-tokens` y de sus tokens principales
(`--ease-premium`, `--duration-instant`, `--control-height`, `--chart-ingresos`,
`--font-family-heading`, `--radius-shell`, `--bg-surface-soft`) en todo el repositorio.
**No hay ningún `@import`, enlace ni uso en código**; las únicas apariciones son
menciones históricas en prosa:

- `Documentacion/DOCUMENTACION_TECNICA.md`
- `Documentacion/Versiones/v-02.08.md`
- `Documentacion/Versiones/v-02.09.md`
- `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md`

Esas menciones son histórico de versiones y describen el estado del archivo en su
momento; apuntan a la ruta antigua. No se han modificado por estar fuera del alcance
de este lote (LOTE 8 / M-11). Si en el futuro se actualizan, la ruta correcta es
`Documentacion/Diseno/Archivo/design-tokens.css`.
