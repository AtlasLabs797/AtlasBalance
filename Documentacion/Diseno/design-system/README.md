# Atlas Balance Design System — capa de componentes

Sincronizado desde el proyecto de Claude Design **"Atlas Balance Design System"**
(`396d43b2-95b5-4a95-866f-728b22c54396`) el **2026-08-25**, con la herramienta
`DesignSync`.

## Por que existe esta carpeta

`Documentacion/Diseno/DESIGN.md` seccion 9 lista los archivos que debian
acompanar a la especificacion. Hasta esta sincronizacion **ninguno estaba en el
repo**: solo habia el documento. Eso bloqueaba el camino de migracion de la
seccion 5 ("reescribir el componente contra el `.jsx` de referencia") y obligaba
a trabajar deduciendo las reglas de la prosa, que en varios puntos **no coincide
con el CSS real** (ver abajo).

## Que hay aqui

| Ruta | Que es |
| --- | --- |
| `styles.css` | Orden de carga canonico. Los `@import` de tokens apuntan a la copia viva del frontend, que se verifico identica a la del proyecto. |
| `components/components.css` | Primitivas: `Button`, `IconButton`, `Card`, `Badge`, `Tag`, `Field`/`Input`/`Select`/`Textarea`, `Checkbox`/`Radio`/`Switch`, `Tabs`, `SideNav`, `Table`, `Avatar`, `StatCard`, `Dialog`, `Toast`, `Tooltip`, `EmptyState`, bandas y nav global. |
| `components/shell.css` | Marco: `AppShell`, `TopBar`, buscador global, andamiaje de pagina, rejillas sancionadas, rail colapsado, FAB y popover de IA, nav inferior, vista partida, listas de definicion. |
| `components/advanced.css` | Capas, pickers, vistas de datos (`DataGrid`), chat de IA, `NotificationList` y primitivas de grafica. |
| `components/balance.css` | Los cinco propios de Balance: `EditableCell`, `AlertBanner`, `CurrencyBreakdown`, `PermissionGrid`, `ImportMapper`. |

Los tokens **no se duplican aqui**: viven en
`Atlas Balance/frontend/src/styles/design-system/tokens/` y se comprobo que
coinciden con los del proyecto. No hay deriva.

## Como se usa hoy

**La app no carga este CSS.** Las clases `.atl-*` no estan en el arbol de la
aplicacion: el redisenio se aplico reestilando el CSS existente con los tokens,
conservando los nombres de clase del proyecto. Esta carpeta es **la referencia
normativa contra la que se audita** ese CSS, y el punto de partida si algun dia
se decide la migracion literal de la seccion 5 de DESIGN.md.

Regla practica: **cuando la prosa de `DESIGN.md` y este CSS discrepen, manda el
CSS.** Es el que renderiza.

## Discrepancias detectadas entre DESIGN.md y el CSS real

Encontradas al sincronizar; las tres primeras habian producido decisiones
equivocadas en la app, ya corregidas.

| Punto | Dice DESIGN.md | Dice el CSS real |
| --- | --- | --- |
| Marca del estado vacio | "`.atl-empty__mark` 20px" | Pastilla de **48px**, redonda, sobre `--surface-sunken` y con borde capilar |
| `AlertBanner` | "variantes `--info` y `--danger`, punto con pulso" | La base es **warning**; `--danger` e `--info` son modificadores. No hay punto con pulso: hay `__icon` |
| Titulo de pagina | "titulo 28" (seccion 5.1) | `.atl-page__title` es `--type-title-1` (**40px**) y `.atl-section__title` es 28. Pero la plantilla `balance-console` **no usa ninguno de los dos**: el titulo vive en el `TopBar` a 21px |
| Lanzador de IA | (no lo detalla) | `.atl-fab` es **transparente**; solo al abrirse toma `--surface-card` + `--shadow-lg` |
| Nav inferior | "por debajo de 720px" | `@media (max-width:720px)` — coincide. La app usa 767.98px de forma unificada (desviacion documentada) |

## Confirmaciones utiles

La plantilla `templates/balance-console/BalanceConsole.dc.html` es la pantalla de
referencia (dashboard + extractos) y confirma:

- Celdas densas a **14px** (`--type-body-sm`) y filas de **44px**.
- Cabeceras de columna en overline de **12px**, no 14.
- Prosa del chat de IA a **17px** (`.atl-msg__bubble` usa `--type-body`).
- Estado de fila como **badge con punto** (`Pendiente` / `Sin mapear` /
  `Conciliado`), nunca fila tenida.
- Importes con menos real `−` y `€` detras: `−84.120,50 €`.
- Cada KPI con `delta` **y** `note` ("+3,4%" / "vs. cierre de julio").
- `TopBar` a 64px (el `height:74px` de `shell.css` no se usa en la plantilla).

## Como re-sincronizar

Con la herramienta `DesignSync`: `list_files` sobre el proyecto y `get_file` de
lo que haya cambiado. No hace falta subir nada — el flujo aqui es de **bajada**,
del proyecto de diseno al repo.
