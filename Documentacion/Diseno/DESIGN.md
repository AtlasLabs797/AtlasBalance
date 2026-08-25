# design.md — Atlas Balance

Especificación para aplicar el diseño nuevo de **Atlas Balance** sobre el código real: `AtlasLabs797/AtlasBalance`, rama `main`, raíz de front `Atlas Balance/frontend/src`.

Este documento es autosuficiente: quien lo lea puede migrar la app sin haber estado en la conversación. Los archivos que acompañan a este `design.md` (`styles.css`, `tokens/`, `components/`, `assets/`, `guidelines/`) son **la referencia de diseño**, escrita en CSS y React sin build. No se copian tal cual a producción: los tokens y el CSS **sí** se adoptan literalmente, los `.jsx` son la implementación de referencia que se traduce a los componentes TSX que ya existen en el repo.

**Fidelidad: alta.** Colores, tipografía, espaciado, radios, sombras, foco y movimiento son valores finales. Si algo del repo no encaja con un token, gana el token.

---

## 1. Qué cambia y por qué

El sistema se ha rehecho sobre la gramática de **Atlas Labs**. Lo único que se conserva del diseño anterior es el acento: **cobalto de tesorería `#285BD9`**.

| Eje | Antes (repo actual) | Ahora |
| --- | --- | --- |
| Tipografía | Hind Madurai + National Park + Atlas Mono (JetBrains) | **Geist + Geist Mono**, dos familias |
| Cuerpo | 14px (`--font-size-base: .875rem`) | **17px** |
| Escala de grises | fría azulada (`#f3f7fb`, `#172033`) | **neutros cálidos** (`#F5F5F7`, `#1D1D1F`) |
| Tarjetas | radio 8px + sombra (`0 8px 24px`) | **radio 18px + borde capilar 1px + sin sombra** |
| Botones | rect 8px | **píldora 999px** (+ un solo rect oscuro de 8px para utilidad de nav) |
| Sombras | escalera completa (sm/md/lg/card/shell) | **planas**: solo menús, toasts, modales y drawers tienen sombra |
| Hover | levanta (`translateY`, sombra mayor) | **cambia superficie o borde**; nada se levanta |
| Pulsación | — | `scale(.95)` en todo control |
| Foco | anillo 3px al 18% | anillo **3px cobalto al 28%** + paso de borde |
| Secciones | tarjetas sobre fondo | **bandas a sangre**, radio 0, el cambio de superficie es el separador |
| Filas de tabla | ~40px | **56px / 44px compacta** |

Motivo: densidad honesta y jerarquía por superficie en vez de por sombra. Software con el que se mueve dinero se lee mejor plano, con cifras grandes en mono tabular.

---

## 2. Instalación

### 2.1 Orden de carga

`styles.css` es el único archivo que se enlaza. Su orden de `@import` es obligatorio (los semánticos dependen de los primitivos):

```css
@import url("tokens/typography.css");
@import url("tokens/colors.css");
@import url("tokens/spacing.css");
@import url("tokens/radius.css");
@import url("tokens/elevation.css");
@import url("tokens/motion.css");
@import url("tokens/semantic.css");
@import url("tokens/themes.css");
@import url("tokens/base.css");
@import url("components/components.css");
@import url("components/shell.css");
@import url("components/advanced.css");
@import url("components/balance.css");
```

En el repo: copiar `tokens/` y `components/` a `src/styles/atlas/`, dejar `src/styles/atlas.css` con esos `@import` y cargarlo en `main.tsx` **antes** de `global.css` y `layout.css`.

### 2.2 Fuentes — la app es on-premise, autoaloja Geist

`tokens/typography.css` trae un `@import` a la API de Google Fonts. **Eso no vale para Atlas Balance**: la app se sirve en red local y tiene que arrancar sin internet. Sustituir ese `@import` por binarios locales, igual que hoy se hace con Hind Madurai:

1. Descargar `Geist` (300/400/600/700) y `Geist Mono` (400/500) en `woff2` a `public/fonts/`.
2. Crear `tokens/fonts.css` con los `@font-face` (`font-display: swap`), e importarlo primero en `styles.css`.
3. Borrar la línea `@import url("https://fonts.googleapis.com/...")` de `tokens/typography.css`.
4. Borrar de `public/fonts/` los `.ttf` de Hind Madurai, National Park y JetBrains Mono, y sus `@font-face` de `variables.css`.

Los iconos (Lucide 0.470.0) hoy se enmascaran desde `unpkg.com`. Mismo problema: **descargar el set a `public/icons/` y apuntar `--atl-icon-base` ahí**, o seguir usando `src/components/Icons.tsx` con los paths de Lucide en línea. No dejar la UI dependiendo del CDN.

### 2.3 Ejes de tema

```html
<html data-theme="light|dark">
```

- `data-theme` — claro (por defecto) y oscuro, con paridad total. Reemplaza al `[data-theme="dark"]` actual de `variables.css`.
- `data-app="atlas"` — **solo** para la marca del fabricante («by Atlas Labs») y superficies compartidas de Atlas Labs. Cambia el acento a azul Action `#0066CC`. No usarlo en pantallas de producto.
- El acento por defecto de `:root` ya es el cobalto de Balance. No hay que poner `data-app="balance"` en ningún sitio.

---

## 3. Tokens

Valores canónicos. Nunca escribir un hex en un componente: siempre `var(--*)`.

### 3.1 Color

**Neutros** — `--gray-25 #FDFDFD` · `50 #F5F5F7` · `100 #EDEDEF` · `200 #E0E0E2` · `300 #D2D2D7` · `400 #A1A1A6` · `500 #86868B` · `600 #6E6E73` · `700 #48484A` · `800 #333336` · `900 #1D1D1F` · `950 #0B0B0C`. Tinta `--ink #1D1D1F`, pergamino `--parchment #F5F5F7`, perla `--pearl #FAFAFC`, capilar `--hairline #E0E0E0`, divisor `--divider-soft #F0F0F0`. Bandas oscuras `--tile-1 #272729` · `--tile-2 #2A2A2C` · `--tile-3 #252527`.

**Acento (cobalto de tesorería)** — `--cobalt-600 #285BD9` sólido, hover `--cobalt-500 #3A72E6`, activo `--cobalt-700 #214CAD`, sobre oscuro `--cobalt-300 #8AABEF`. Se consume siempre por el canal semántico: `--accent-solid`, `--accent-solid-hover`, `--accent-solid-active`, `--accent-text`, `--accent-soft`, `--accent-soft-strong`, `--accent-soft-border`, `--accent-ring`, `--accent-on-solid`.

**Superficies** — `--surface-page` (pergamino) · `--surface-card` (blanco) · `--surface-muted` (perla) · `--surface-sunken` · `--surface-hover` (`rgba(29,29,31,.04)`) · `--surface-active` (`.08`) · `--surface-tile{,-2,-3}`.

**Bordes** — tres intenciones y nada más: `--border-subtle` separadores dentro de una tarjeta · `--border-default` el borde de la tarjeta · `--border-strong` hover y controles en reposo.

**Texto** — `--text-primary` `#1D1D1F` · `--text-secondary` `#6E6E73` · `--text-tertiary` `#86868B` · `--text-disabled` `#A1A1A6` · `--text-inverse` · `--text-link`.

**Estado** (triplete `bg`/`border`/`text` + `solid`) — éxito verde `#1C8A4B`/`#176A3A`, aviso ámbar `#B87400`/`#8A5200`, peligro carmesí `#C92B3F`/`#A82234`, información azul Action `#0066CC`/`#0055AB`, neutro gris.

**Dinero** — `--amount-positive` `#176A3A`, `--amount-negative` `#A82234`, `--amount-neutral`, `--amount-muted`. Se colorea **por significado, no por signo**.

**IA** — violeta `--ai-solid #7340E0`, `--ai-text`, `--ai-soft`, `--ai-border`. No cambia nunca, ni en oscuro ni por app.

**Gráficos** — `--chart-1…10` en orden fijo: `#0066CC #0F8B8D #E0761A #8B5CF6 #1C8A4B #4046D6 #C92B3F #6E6E73 #B8478F #6B8F1E`. Más `--chart-grid`, `--chart-axis`, `--chart-track`, `--chart-area-from/to`. Máximo cinco series por gráfico; la sexta va a «Otros» con `--chart-8`.

### 3.2 Tipografía

Familias: `--font-ui` y `--font-display` = Geist; `--font-mono` = Geist Mono con `font-variant-numeric: tabular-nums`.

Escala: `10 · 11 · 12 · 13 · 14 · 17 · 18 · 21 · 24 · 28 · 34 · 40 · 48 · 56`. KPI `clamp(22px, 3.2vw, 44px)`.

Pesos **300 / 400 / 600 / 700**. El **500 no existe**: donde el repo use `--font-weight-medium: 500`, pasa a **600** (en el sistema, `--weight-medium` ya resuelve a 600).

Alias heredados que siguen resolviendo dentro de la escala nueva: `--text-16 → --text-17`, `--text-20 → --text-21`, `--text-32 → --text-34`. No escribirlos en código nuevo.

Tracking: `-0.005em` display · `-0.011em` heading · **`-0.022em` cuerpo** · `-0.016em` label · `+0.08em` overline.

Roles compuestos (usar el shorthand, no reconstruirlo): `--type-hero` 56/1.07 600 · `--type-title-1` 40 · `--type-title-2` 28 · `--type-title-3` 21 · `--type-lead` 28/400 · `--type-lead-airy` 24/300 · `--type-subhead` 17/600 · **`--type-body` 17/1.47 400** · `--type-body-sm` 14 · `--type-label` 14/600 · `--type-caption` 14/400 · `--type-overline` 12/600 + caps + `--tracking-caps` · `--type-fine` 12 · `--type-micro` 10 · `--type-mono` 14 · `--type-mono-lg` 21 · `--type-kpi` mono 600.

### 3.3 Espaciado y layout

Escala de 4px: `0 2 4 6 8 10 12 17 20 24 32 40 48 64 80 120` (`--space-0…15`). Los que trabajan de verdad: **8 / 12 / 17 / 24**.

Padding: tarjeta 24 (compacta 20), campo 12×20, modal 32, banda 80. Margen de página 32 (48 ancho).

Cromo: sidebar **264px** (raíl 72, navegación inferior por debajo de 720px), topbar **64px**, contenido máx **1440** (980 en superficies de texto), drawer 560, panel de IA 420.

Alturas: fila **56 / 44 compacta**; control **44** (sm 32, lg 52).

Rejillas sancionadas: `--grid-kpi` (`2fr 1fr 1fr`), `--split`, `--detail`, `--2/3/4`. Vía clases: `.atl-grid--kpi|split|detail|2|3|4`.

### 3.4 Radios

`0` bandas (`--radius-tile`) · `5` xs · `8` utilidad oscura (`--radius-sm`) · **`11` campos y cápsula perla** (`--radius-field`, `--radius-md`) · **`18` tarjetas, paneles y modales** (`--radius-card/panel/modal`, y también `--radius-lg`/`--radius-xl`) · `999` botones, chips, badges, tags, avatares (`--radius-control`, `--radius-full`). Nada intermedio.

Tarjeta = **18px + borde 1px + sin sombra**. Es la forma más reconocible del sistema.

### 3.5 Elevación

`--shadow-xs/sm/card/md/control/inset-top` resuelven a **`none`**. Solo tienen sombra las capas desprendidas: `--shadow-lg` `0 8px 28px rgba(0,0,0,.10)` (menús, toasts) y `--shadow-xl` `0 18px 60px rgba(0,0,0,.14)` (modales, drawers). `--shadow-product` (`rgba(0,0,0,.22) 3px 5px 30px`) es la única sombra real del sistema y pertenece a imágenes de producto apoyadas en una superficie: nunca a una tarjeta, un botón ni a texto.

Foco `--ring-focus` `0 0 0 3px var(--accent-ring)` (cobalto al 28%); en destructivo `--ring-danger` (carmesí al 26%). Velo `rgba(0,0,0,.48)` + `blur(4px)`. Cristal: `saturate(200%) blur(24px)` sobre superficie al 55% (`--glass-surface`/`--glass-blur`), solo en topbar, sidenav, navegación inferior y popover de IA; los paneles helados de la subnav usan `--blur-panel` `saturate(180%) blur(20px)`.

### 3.6 Movimiento

Un solo easing: **`cubic-bezier(.22,1,.36,1)`**. Duraciones 100 / 120 / 180 / 240 / 420ms. Hover cambia color, nunca posición (`--lift-hover: 0px`). Pulsación `scale(.95)` (superficies `.99`). Entrada: modal fundido + 8px de subida; drawer 24px de deslizamiento. Nada se anima al cargar, las listas no entran en cascada. Todo se anula bajo `prefers-reduced-motion`.

---

## 4. Migración de variables (lo primero que hay que hacer)

`src/styles/variables.css` desaparece. Todo el CSS del repo (≈200 KB en `styles/layout/*.css` + `global.css` + `auth.css`) consume sus nombres, así que la migración va en dos pasos.

### 4.1 Capa de compatibilidad — pegar como `src/styles/atlas-compat.css`, después de `atlas.css`

Estos nombres antiguos se pueden mantener porque su significado no cambia:

```css
:root{
  --bg-app:var(--surface-page);--bg-canvas:var(--surface-page);--bg-surface:var(--surface-card);
  --bg-surface-soft:var(--surface-muted);--bg-surface-muted:var(--surface-sunken);
  --dashboard-hero-bg:var(--surface-card);--bg-input:var(--surface-card);
  --bg-hover:var(--surface-hover);--bg-selected:var(--accent-soft);
  --border-soft:var(--border-default);--border-medium:var(--border-strong);--border-focus:var(--accent-solid);
  --text-muted:var(--text-tertiary);
  --accent-primary:var(--accent-solid);--accent-primary-hover:var(--accent-solid-hover);--accent-primary-soft:var(--accent-soft);
  --success-bg:var(--status-success-bg);--success-text:var(--status-success-text);
  --warning-bg:var(--status-warning-bg);--warning-text:var(--status-warning-text);
  --danger-bg:var(--status-danger-bg);--danger-text:var(--status-danger-text);
  --info-bg:var(--status-info-bg);--info-text:var(--status-info-text);
  --chart-ingresos:var(--chart-5);--chart-egresos:var(--chart-7);--chart-saldo:var(--chart-1);
  --chart-series-1:var(--chart-1);--chart-series-2:var(--chart-2);--chart-series-3:var(--chart-3);
  --chart-series-4:var(--chart-4);--chart-series-5:var(--chart-5);--chart-series-6:var(--chart-6);
  --chart-series-7:var(--chart-7);--chart-series-8:var(--chart-8);--chart-series-9:var(--chart-9);
  --chart-series-10:var(--chart-10);--chart-series-other:var(--chart-8);
  --row-flagged-bg:var(--status-warning-bg);--row-flagged-border:var(--status-warning-border);
  --font-family:var(--font-ui);--font-family-heading:var(--font-display);--font-family-mono:var(--font-mono);
  --font-weight-medium:600;
  --line-height-tight:var(--leading-snug);--line-height-normal:var(--leading-normal);
  --control-height-compact:36px;--control-padding-x:var(--space-7);
  --control-border:var(--border-strong);--control-bg:var(--surface-card);--control-bg-hover:var(--surface-hover);
  --control-ring:var(--ring-focus);--shadow-focus:var(--ring-focus);
  --surface-border:var(--border-default);--surface-border-hover:var(--border-strong);
  --surface-bg-raised:var(--surface-raised);--surface-bg-sunken:var(--surface-sunken);--surface-highlight:var(--accent-soft);
  --shadow-overlay:var(--shadow-xl);--shadow-shell:none;--shadow-card-hover:none;
  --transition-fast:var(--duration-fast) var(--ease-premium);
  --transition-normal:var(--duration-normal) var(--ease-premium);
  --sidebar-collapsed-width:var(--sidebar-width-collapsed);
  --mobile-bottom-nav-height:72px;
  --radius-pill:var(--radius-full);--radius-shell:var(--radius-card);
}
```

Se conservan tal cual, ya con valores nuevos: `--text-primary`, `--text-secondary`, `--text-inverse`, `--text-link`, `--border-strong`, `--amount-positive`, `--amount-negative`, `--chart-grid`, `--sidebar-width`, `--topbar-height`, `--control-height`, `--radius-card`, `--radius-panel`, `--radius-control`, `--shadow-sm/md/lg/card`, `--ease-premium`, `--duration-*`, `--z-*`.

Los alias de segundo nivel del repo (`--color-bg-*`, `--color-text-*`, `--color-sidebar-*`, `--color-button-secondary-*`, `--color-accent*`, `--color-border-*`, `--chart-color-*`, `--color-row-flagged*`) siguen funcionando porque apuntan a los de arriba. **Excepción:** `--color-sidebar-shadow` pasa a `none` y `--color-sidebar-active-ring` a `var(--accent-soft-border)`.

### 4.2 Renombrados obligatorios — hay colisión, toca buscar y reemplazar

Estos nombres existen en los dos sistemas con **valores distintos**. Si se dejan, el layout se rompe en silencio. Reemplazo mecánico en todo `src/`:

| Antiguo (valor antiguo) | Nuevo |
| --- | --- |
| `--space-1` (4px) | `--space-2` |
| `--space-2` (8px) | `--space-4` |
| `--space-3` (12px) | `--space-6` |
| `--space-4` (16px) | `--space-7` (17px) |
| `--space-5` (24px) | `--space-9` |
| `--space-6` (32px) | `--space-10` |
| `--space-8` (48px) | `--space-12` |
| `--space-10` (64px) | `--space-13` |
| `--space-xxs … --space-3xl` | `--space-2 · 4 · 6 · 7 · 9 · 10 · 12 · 13` |
| `--font-size-xs` (12) | `--text-12` |
| `--font-size-sm` (13) | `--text-13` |
| `--font-size-base` (14) | **`--text-17`** en prosa y controles; `--text-14` solo en celdas densas, etiquetas y pies |
| `--font-size-md` (16) | `--text-17` |
| `--font-size-lg` (18) | `--text-21` |
| `--font-size-xl` (24) | `--text-28` |
| `--font-size-2xl` (32) | `--text-40` |
| `--font-size-kpi` | `--text-kpi` |
| `--font-weight-normal/semibold/bold/heavy` | `--weight-regular / --weight-semibold / --weight-bold / --weight-bold` |
| `--border-radius-sm` (8) | `--radius-xs` (5) en chips internos, `--radius-field` (11) en campos |
| `--border-radius-md` (8) | `--radius-field` (11) |
| `--border-radius-lg` (12) | `--radius-card` (18) |
| `--border-radius-full` | `--radius-full` |

**Hazlo en este orden** (de mayor a menor índice) para no reasignar dos veces: `--space-10 → --space-13`, `--space-8 → --space-12`, `--space-6 → --space-10`, `--space-5 → --space-9`, `--space-4 → --space-7`, `--space-3 → --space-6`, `--space-2 → --space-4`, `--space-1 → --space-2`.

### 4.3 Barridos manuales

Después del reemplazo, buscar y arreglar a mano:

1. **`box-shadow`** — cualquier sombra en tarjetas, botones, campos, KPIs, sidebar y filas: fuera. Se sustituye por `border:1px solid var(--border-default)`. Solo sobreviven menú, toast, modal, drawer y popover.
2. **`translateY` / `translate3d` en `:hover`** — fuera. Hover cambia `background` a `--surface-muted` o `border-color` a `--border-strong`.
3. **`:active`** — añadir `transform:scale(var(--press-scale))` a botones, filas clicables y celdas editables.
4. **`border-radius: 8px`** literal — decidir entre 11 (campo) y 18 (tarjeta/panel); 8 solo en el botón oscuro de utilidad.
5. **Hex literales** — cero. Todo por token.
6. **`font-weight: 500`** — pasa a 600.
7. **`outline`** de foco — pasa a `box-shadow: var(--ring-focus)`.

---

## 5. Componentes: qué usa cada pantalla

Los `.jsx` de `components/` son la implementación de referencia (props en el `.d.ts`, reglas y ejemplo en el `.prompt.md`). En el repo hay dos caminos y ambos son válidos por archivo: **reestilar el TSX existente con las clases `.atl-*`** (rápido, recomendado para el 80%) o **reescribir el componente siguiendo el `.jsx`** (para los cinco propios de Balance, que ya se modelaron a partir del repo).

### 5.1 Marco de la app

| Repo | Sistema | Notas |
| --- | --- | --- |
| `components/layout/Layout.tsx` | `AppShell` · `.atl-shell`, `.atl-shell__main`, `.atl-shell__scroll` | Grid `264px 1fr`, scroll solo en el panel derecho |
| `components/layout/Sidebar.tsx` | `SideNav` · `.atl-sidenav`, `.atl-sidenav--collapsed`, `.atl-navitem`, `.atl-navitem--active` | 264/72px, sin sombra lateral. Activo = `--accent-soft` + texto `--accent-text`, sin anillo |
| `components/layout/TopBar.tsx` | `TopBar` · `.atl-topbar`, `.atl-topbar__titles`, `.atl-topbar__actions` | 64px, cristal al 88%, borde inferior capilar |
| `components/layout/BottomNav.tsx` | `.atl-bottomnav` | Por debajo de 720px, 72px, cristal |
| `components/layout/AlertBanner.tsx` | **`AlertBanner`** · `.atl-alertbanner` | Ya modelado desde el repo. Variantes `--info` y `--danger`, punto con pulso |
| `components/layout/PaisScopeSelect.tsx` | `Select` · `.atl-selectwrap`, `.atl-select--pill` | Píldora en el topbar |
| Cabecera de página en cada `pages/*.tsx` | `.atl-page__head`, `__eyebrow`, `__title`, `__desc`, `__actions` | Ritmo fijo: overline 12 caps → título 28 → una frase de descripción |

### 5.2 Dashboard

| Repo | Sistema |
| --- | --- |
| `dashboard/KpiCard.tsx` | `StatCard` · `.atl-stat`, `.atl-stat--featured`, `.atl-stat__value` (KPI mono 600), `__delta--up/down/flat`. Máximo 4 por fila, uno `featured` |
| `dashboard/SaldoPorDivisaCard.tsx` | **`CurrencyBreakdown`** · `.atl-divisas__grid`, `__item`, `__code`, `__conv`, `__total` |
| `dashboard/EvolucionChart.tsx` | Receta `.atl-chart` (`__svg`, `__grid`, `__area`, `__line`, `__point`, `__tip`, `__legend`). `--chart-area-from/to` para el relleno |
| `dashboard/TitularSaldoBarChart.tsx` | Receta `.atl-bars` (`__col`, `__bar`, `__tick`) |
| `dashboard/ConcentracionDonutCharts.tsx` | Receta `.atl-donut` (`__ring`, `__track`, `__hole`, `__value`, `__label`) |
| `dashboard/PeriodoSelector.tsx`, `DivisaSelector.tsx` | `SegmentedControl` · `.atl-seg`, `.atl-seg__btn--on` |

Los gráficos **no son componentes**: son tokens y recetas CSS/SVG. Los patrones exactos están en `guidelines/charts-*.card.html`. No se añade ninguna librería.

### 5.3 Extractos e importación

| Repo | Sistema |
| --- | --- |
| `extractos/ExtractoTable.tsx` | `DataGrid` · `.atl-datagrid` (`__bar`, `__tools`, `__table--compact`, `__frow`, `__foot`). Filas 44 compactas, cifras a la derecha en mono |
| `extractos/EditableCell.tsx` | **`EditableCell`** · `.atl-cell`, `__input`, `__button`, `--locked`, `--right`, `__state--ok/--error` |
| `extractos/DesgloseModal.tsx`, `AuditCellModal.tsx` | `Dialog` · `.atl-dialog` (radio 18, `--shadow-xl`, velo 48%) |
| `pages/ImportacionPage.tsx`, `FormatosImportacionPage.tsx` | **`ImportMapper`** · `.atl-mapper__row`, `__source`, `__arrow`, `__name`, `__sample`, `__status` + `Stepper` |
| `pages/ConciliacionPage.tsx` | `Table` + `Badge` con el vocabulario financiero |

### 5.4 Entidades, usuarios, administración

| Repo | Sistema |
| --- | --- |
| `pages/CuentasPage.tsx`, `TitularesPage.tsx` | `.atl-grid--detail` + `Table` + `FilterBar` (`.atl-filters`, `.atl-filterchip--on`) |
| `pages/CuentaDetailPage.tsx`, `TitularDetailPage.tsx` | `.atl-split` (lista + panel), `.atl-deflist` para pares clave/valor, `Timeline` (`.atl-tl`) para auditoría |
| `usuarios/UsuarioModal.tsx` | **`PermissionGrid`** · `.atl-permgrid`, `__group`, `__cell`, `__help` |
| `integraciones/*` | `Dialog` + `Table` + `Tag` (`.atl-tag`, `.atl-tag__x`) |
| `pages/AuditoriaPage.tsx` | `Timeline` + `Table`; `--shadow` ninguna |
| `pages/AlertasPage.tsx` | `NotificationList` (`.atl-notif--unread`, `__icon--danger/warning/success`) |
| `pages/BackupsPage.tsx`, `ExportacionesPage.tsx`, `PapeleraPage.tsx` | `Table` + `EmptyState` (`.atl-empty__mark` 20px) |
| `pages/ConfiguracionPage.tsx` | `.atl-section` + `Field`/`Input`/`Switch`/`Checkbox` |
| `pages/LoginPage.tsx`, `ChangePasswordPage.tsx` (`styles/auth.css`) | Tarjeta 18px centrada sobre `--surface-page`, `Field` + `Input` + botón píldora de ancho completo. `auth.css` se reescribe entero: es donde más sombras y radios viejos hay |

### 5.5 Canal de IA

| Repo | Sistema |
| --- | --- |
| `ia/AiChatPanel.tsx` | `ChatPanel` · `.atl-chat--panel` (420px), `.atl-msg--ai/--user`, `.atl-typing`, `.atl-chat__composer` |
| `ia/AiMessageContent.tsx` | `.atl-msg__bubble`, `__data`, `__cites`, `.atl-cite` |
| Marca del asistente | **`AiFace`** · `.atl-face--idle/--listening/--thinking`. Sustituye al icono `bot` |

Todo el canal de IA va en **violeta** (`--ai-*`), nunca en cobalto, en claro y en oscuro. Es lo que hace que «esto lo ha generado la IA» se lea igual en todos los productos de Atlas.

### 5.6 Primitivas

`Button` (`.atl-btn--primary|secondary|ghost|danger|quiet-danger|utility|pearl`, tamaños `--sm|--lg|--block`), `IconButton`, `Icon`, `Card` (`__head/__title/__desc/__body/__foot`), `Badge` (`--success|warning|danger|info|neutral|accent`, `__dot`), `Tag`, `Field`, `Input`, `Textarea`, `Select`, `Checkbox`, `Radio`, `Switch`, `Combobox`, `MultiSelect`, `DateRangePicker`, `Tabs`, `SegmentedControl`, `Menu`, `CommandPalette`, `FilterBar`, `Breadcrumbs`, `Pagination`, `Stepper`, `Table`, `Avatar`/`AvatarStack`, `SignedAmount`, `Timeline`, `KanbanBoard`, `Dialog`, `Drawer`, `Toast`, `Tooltip`, `EmptyState`, `NotificationList`.

**Dos gramáticas de botón y ninguna más:** la píldora cobalto para acción (y su fantasma con borde cobalto para la secundaria — nunca un botón gris) y el rectángulo oscuro de 8px y 36px de alto para utilidad de navegación. `--color-button-secondary-*` del repo deja de usarse.

---

## 6. Contenido, cifras y estados

- **Español (España), tú informal.** Verbos en imperativo: *Importar, Exportar, Conciliar, Revisar, Guardar, Entrar*. Botones de 1 a 3 palabras, verbo primero.
- **Sentence case en todo**, botones incluidos. Las MAYÚSCULAS las pone CSS (`text-transform`) en overlines, cabeceras de tabla y etiquetas de KPI. Nunca Title Case en el texto fuente.
- **Dinero:** `1.284.560,12 €` — punto de miles, coma de decimales, menos real `−`, símbolo **detrás** del número, mono tabular, a la derecha. Revisar `utils/formatters.ts` para que el signo menos sea `−` (U+2212) y no `-`.
- **Una métrica sin comparación no se publica:** «1.284.560,12 € · +3,4% vs. cierre de julio».
- **Color por significado, no por signo:** un coste que sube es rojo aunque el número sea positivo.
- **Tiempo:** relativo por debajo de un día (`hace 12 min`, `4h`), absoluto por encima (`14 ago`, `14/08/2026`). ISO en campos de registro.
- **Vocabulario de estado, literal:** financiero **Conciliado · Pendiente · Sin mapear**; tareas **Pendiente · En progreso · En revisión · Completada · Cancelada · Bloqueada**.
- **Errores:** nombra el sistema y el hecho — «Santander devolvió 502. Reintentando en 30s.». En diálogos, la consecuencia antes de la acción.
- **Confirmaciones:** pasado + lo que ocurre después — «147 movimientos conciliados — el extracto de julio queda cerrado.».
- **Estados vacíos:** dos líneas, para qué sirve la superficie y la siguiente acción útil.
- **Prohibido:** emoji, exclamaciones, «simplemente», «solo tienes que», «potente», «sin fricción», cadencia de marketing.

Los estados de fila marcada (`--row-flagged-*`) pasan al triplete de aviso: **badge con punto, nunca fila coloreada**. La fila seleccionada toma `--accent-soft` con borde izquierdo de 2px en `--accent-solid`.

---

## 7. Orden de trabajo sugerido

1. Autoalojar Geist y los iconos; borrar las fuentes viejas.
2. Meter `tokens/` y `components/` en `src/styles/atlas/`, cargar `atlas.css` antes de `global.css`.
3. Escribir `atlas-compat.css` (§4.1) y borrar `variables.css` menos su bloque `[data-theme="dark"]`, que se elimina también (lo cubre `tokens/themes.css`).
4. Ejecutar los renombrados de §4.2 en el orden indicado.
5. Barridos de §4.3 sobre `global.css`, `auth.css` y los 10 archivos de `styles/layout/`.
6. Marco primero: `Layout`, `Sidebar`, `TopBar`, `BottomNav`, `AlertBanner`. Con eso toda la app cambia de cara.
7. `DashboardPage` y `ExtractosPage`: son las dos pantallas que fijan la densidad. La plantilla `templates/balance-console/` es la referencia visual de ambas.
8. El resto de páginas por orden de tráfico.
9. `auth.css` al final, reescrito.

## 8. Lista de aceptación

- [ ] Ninguna tarjeta, botón, campo, KPI ni fila tiene `box-shadow`.
- [ ] Ningún hover mueve nada de sitio.
- [ ] Todo control tiene `scale(.95)` al pulsar y anillo cobalto de 3px al enfocar.
- [ ] Cuerpo a 17px; 14px solo en celdas densas, etiquetas y pies.
- [ ] Ningún `font-weight: 500` en el árbol.
- [ ] Todas las tarjetas y modales a 18px; todos los botones son píldora salvo el oscuro de utilidad.
- [ ] Cero hex literales en `src/`; todo `var(--*)`.
- [ ] Importes en mono tabular, a la derecha, con `€` detrás y `−` real.
- [ ] Cada KPI lleva comparación.
- [ ] Modo oscuro revisado en dashboard, extractos, login y panel de IA.
- [ ] `prefers-reduced-motion` anula todas las animaciones.
- [ ] La app arranca sin conexión a internet (fuentes e iconos locales).

---

## 9. Archivos que acompañan a este documento

| Ruta | Qué es |
| --- | --- |
| `styles.css` | El único archivo que se enlaza. Solo `@import`s. |
| `tokens/` | Los nueve archivos de tokens. Se adoptan literalmente. |
| `components/*.css` | `components.css` primitivas · `shell.css` marco y scaffolding de página · `advanced.css` capas, pickers, vistas de datos, chat, gráficos · `balance.css` los cinco propios de Balance |
| `components/<grupo>/*.jsx` | Implementación de referencia sin build. |
| `components/<grupo>/*.d.ts` | Contrato de props de cada componente. |
| `components/<grupo>/*.prompt.md` | Qué es, cuándo se usa, reglas y ejemplo. |
| `components/<grupo>/*.card.html` | Especímenes que se abren en el navegador. |
| `guidelines/*.card.html` | Fundamentos: tipografía, color, espaciado, marca, bandas y **recetas de gráficos**. |
| `templates/balance-console/` | Pantalla de partida: dashboard + extractos con el sistema aplicado. |
| `assets/logos/` | Glifos de Atlas Balance y Atlas Labs (PNG monocromo, se tiñen con `mask` + `currentColor`). |
| `_ds_bundle.js` | Todos los componentes compilados, para abrir las cards y la plantilla sin build. |
| `README.md` | La guía completa del sistema (voz, fundamentos, iconografía, sustituciones). |

**Sustituciones pendientes de material real:** Geist sustituye a una tipografía de marca que no existe; Lucide sustituye a un set de iconos que no se entregó; el logotipo es un glifo PNG más wordmark tipográfico. Si Atlas Balance tiene `woff2` o un SVG de marca, entran en `assets/` con un `tokens/fonts.css` y no cambia nada más.
