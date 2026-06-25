# Atlas Balance — Rediseño · Especificación de diseño

> Documento de referencia del rediseño construido en `Atlas Balance Redesign.dc.html`.
> Todo el sistema se ancla en el **Atlas Balance Design System** (tokens CSS, sin Tailwind).
> Idioma de la UI: **español (España)**. Moneda en formato `€1.284.560,12` (punto miles, coma decimales).

---

## 1. Principios del rediseño

| Principio | Aplicación |
|---|---|
| **Calma y precisión** | Operaciones de tesorería, no marketing. Densidad media, mucho aire alrededor de las cifras. |
| **Un solo azul** | El azul de tesorería `#285bd9` lleva *toda* la acción primaria y el estado activo. Sin segundos acentos. |
| **El número manda** | Cada importe, IBAN o porcentaje va en mono tabular. El saldo consolidado es el héroe absoluto. |
| **Premium por restricción** | Sombras suaves en capas, bordes hairline, radios contenidos. Sin gradientes decorativos ni texturas. |
| **Rail oscuro permanente** | La barra lateral y el panel de marca del login son oscuros en *ambos* temas — ancla visual estable. |

**Cambio estructural clave frente al original:** el dashboard pasa de una pila de KPIs sueltos a una **tarjeta-héroe** que funde saldo consolidado + desglose por divisa + gráfico de evolución en una sola superficie destacada, con la fila de KPIs degradada a segundo nivel.

---

## 2. Color

### 2.1 Superficies (tema claro → oscuro)

| Token | Claro | Oscuro | Uso |
|---|---|---|---|
| `--bg-app` | `#f3f7fb` | `#12171f` | Fondo de la aplicación |
| `--bg-surface` | `#fbfcfe` | `#1b2330` | Tarjetas, paneles |
| `--bg-surface-soft` | `#f1f5fa` | `#202a38` | Cabeceras y pies de tabla, zonas hundidas |
| `--bg-surface-muted` | `#e9eff6` | `#293545` | Pista de barras de progreso, badges neutros |
| `--surface-highlight` | tinte azul 7 % sobre surface | íd. oscuro | Tarjeta-héroe del dashboard |
| `--bg-hover` | `#e9eff6` | `#242f3e` | Hover de filas y enlaces de nav |
| `--bg-app` (rail) `--color-bg-sidebar` | `#12171f` | `#12171f` | Rail lateral (oscuro en ambos temas) |

### 2.2 Texto

| Token | Claro | Oscuro |
|---|---|---|
| `--text-primary` | `#172033` | `#f1f5f9` |
| `--text-secondary` | `#536174` | `#b7c2d2` |
| `--text-muted` | `#5f6b7a` | `#98a6ba` |

### 2.3 Acento (azul tesorería)

| Token | Claro | Oscuro | Uso |
|---|---|---|---|
| `--accent-primary` | `#285bd9` | `#82a4ff` | Botón primario, barra de nav activa, línea de gráfica, máscara del logo |
| `--accent-primary-hover` | `#214cad` | `#a8bdff` | Hover de acción primaria |
| `--accent-primary-soft` | `#e6eefc` | `rgba(130,164,255,.14)` | Avatar, chip de banco activo, badge de acento |
| `--color-sidebar-active-bg` | tinte azul | tinte azul | Fondo del ítem de nav activo |
| `--color-sidebar-active-text` | azul | azul claro | Texto/icono del ítem activo |

### 2.4 Semánticos e importes

| Concepto | Token texto | Claro | Oscuro | Fondo |
|---|---|---|---|---|
| Éxito / Conciliado | `--success-text` | `#176a3a` | `#8de0ae` | `--success-bg` |
| Aviso / Pendiente | `--warning-text` | `#8a5200` | `#f3c270` | `--warning-bg` |
| Peligro / Revisar | `--danger-text` | `#a82234` | `#ff9aa7` | `--danger-bg` |
| Info / Plazo | `--info-text` | `#315fcf` | `#b9c9ff` | `--info-bg` |
| **Importe positivo** | `--color-amount-positive` | `#176a3a` | `#8de0ae` | — |
| **Importe negativo** | `--color-amount-negative` | `#a82234` | `#ff9aa7` | — |

> Negativos con **minus real `−`** + rojo; positivos con verde. Nunca neón. Fila marcada (pendiente de revisar): `--row-flagged-bg`.

### 2.5 Gráfica

- `--chart-saldo` (`#285bd9` claro / `#9fb6ff` oscuro) — línea de evolución, trazo 2,5 px, uniones y extremos redondeados.
- `--chart-grid` (`rgba(23,33,52,.10)`) — 5 líneas de retícula horizontales.
- **Relleno de área**: gradiente vertical `id="evoFill"` del color de saldo, opacidad **0.20 → 0.01**.
- **Punto final**: doble círculo (halo r=9 a 15 % de opacidad + núcleo r=4,5 sólido).

### 2.6 Mecánica de tema — **crítico**

`data-theme` se espeja en `<html>` vía `componentDidMount` y en cada toggle:

```js
document.documentElement.setAttribute('data-theme', next);
```

**Por qué:** los alias semánticos `--color-*` se definen solo en `:root`, así que se resuelven en `<html>`. Si `data-theme` viviera en un wrapper interno, esos alias nunca cambiarían de tema. El **rail lateral** y el **panel de marca del login** llevan `data-theme="dark"` a nivel de elemento (consumen tokens base directos, que sí se redefinen en el bloque oscuro). El **titular del panel de marca** lleva `color: var(--text-primary)` **inline** para resolverse en el ámbito oscuro del propio panel.

---

## 3. Tipografía

| Familia | Token | Uso |
|---|---|---|
| **Hind Madurai** | `--font-family-heading` | Titulares y display. Títulos en peso **800** (heavy). |
| **National Park** | `--font-family` | Cuerpo, UI, etiquetas. |
| **Atlas Mono** (JetBrains Mono) | `--font-family-mono` | Todo número: importes, saldos, fechas, %, IBAN. Con `font-variant-numeric: tabular-nums`. |

### 3.1 Escala aplicada en el rediseño

| Rol | Tamaño | Peso | Familia | Line-height |
|---|---|---|---|---|
| Héroe — saldo consolidado | `--font-size-kpi` (≈ 2,75rem fluido) | 800 | Mono | 1.05 |
| H1 de página (Dashboard/Extractos) | 26px | 800 | Heading | 1.18 |
| Titular login | `clamp(2.1rem, 3.2vw, 2.9rem)` | 800 | Heading | 1.12 |
| H2 de tarjeta | 24px / 17px | 800 / 700 | Heading | 1.18 |
| Valor KPI | ~1.5rem | 700 | Mono | — |
| Cuerpo / descripción | 13–15px | 400–600 | Body | 1.5–1.6 |
| **Micro-etiqueta** (KPI title, grupo nav, cabecera tabla) | 10.5–11.5px | 700 | Body | `text-transform: uppercase; letter-spacing: .08–.12em` |
| Importe en tabla | 12.5–14.5px | 600 | Mono | — |

> Convención de casing del producto: **MAYÚSCULAS con tracking** para micro-etiquetas; *sentence case* para frases. Title case evitado.

---

## 4. Espaciado, radios y elevación

- **Ritmo base 8px**. Paddings de tarjeta 24px (héroe 28px). Gap de rejilla 16px; gap de sección 18–20px.
- **Shell**: sidebar fijo **252px**, topbar **64px**, contenido `max-width: 1240px` centrado, padding 28–32px.
- **Radios**: 8px controles/ítems de nav · 10px chips de divisa · `--radius-panel` (18px) tarjetas/login · 999px pills, badges y barras de progreso.
- **Bordes**: hairline `--border-soft` (`rgba(23,33,52,.10)`), `--border-strong` (`.18`) en separadores de tabla.
- **Sombras** (mezcladas desde el color de texto): `--shadow-card` por defecto · `--shadow-lg` en login · la tarjeta-héroe y la tarjeta de login añaden **inner top highlight** `inset 0 1px 0 color-mix(--accent-primary 14%, transparent)`.

---

## 5. Movimiento

- **Easing único** — `cubic-bezier(0.22, 1, 0.36, 1)` ("ease-premium"), curva decelerante.
- **Duraciones**: 120ms (hover de fila de tabla) · 180ms (color/fondo de nav, chips, botones de header) · 240ms (transición de fondo de tema en el contenedor raíz).
- **Hover**:
  - Ítems de nav y enlaces de titular → fondo `--bg-hover`, texto a `--text-primary`.
  - Ítem de nav activo → barra lateral de acento (`width:3px`, `opacity` 0 → 1 con transición de 180ms).
  - Filas de tabla → fondo `--bg-hover` (120ms).
  - Botón de logout → fondo `--danger-bg`, icono `--danger-text`.
- **Transición de tema**: `transition: background-color 240ms ease-premium` en el contenedor raíz para un cambio claro/oscuro suave.
- **Sin** animaciones decorativas en bucle. Respeta `prefers-reduced-motion` (heredado de los tokens del DS).

---

## 6. Iconografía

- Set de líneas **24×24, trazo 1.75, caps/joins redondeados, `fill:none`, `stroke: currentColor`** — idéntico en geometría al `Icon` del DS.
- **Implementación:** dibujados desde la clase lógica con `React.createElement` (método `icon(name, size)`), **no** vía el bundle, para no depender del orden de carga de React. El glifo hereda el color del texto del contenedor.
- **Cobertura usada:** `dashboard, titulares, cuentas, extractos, importacion, alertas, auditoria, usuarios, backups, configuracion, salir, sun, moon, search, plus, exportaciones`.
- Tamaños: 20px nav · 19px botones de header · 18px botones de acción · 22px estados vacíos · 17px campo de búsqueda.
- **Logos** (`assets/logos/`): glifos monocromos tintados por CSS `mask` para adoptar `--accent-primary` o `currentColor`.

---

## 7. Componentes y pantallas

### 7.1 Shell

- **Sidebar** (`data-theme="dark"`, 252px fijo): logo Atlas Balance + "by Atlas Labs"; nav agrupada en **Operación / Control / Sistema** con micro-etiquetas; cada ítem = barra de acento + icono + label + badge opcional (p. ej. Alertas `2` en `--danger-bg`); pie con versión y reloj en mono.
- **Topbar** (64px, sticky): título + breadcrumb de página a la izquierda; a la derecha, pill de usuario (avatar `MR` + nombre + rol Admin), toggle de tema (sol/luna) y logout. Fondo translúcido `color-mix(--bg-app 82%, transparent)` + `backdrop-filter: blur(10px)`.

### 7.2 Dashboard

1. **Header** con H1 + descripción y **tabs de periodo** (`1M / 3M / 12M / 24M`, clase `.ab-tab` / `.ab-tab--active`).
2. **Tarjeta-héroe** (`--surface-highlight`, borde de acento, inner highlight):
   - Saldo consolidado en mono 2,75rem + badge `+3,4 %` de éxito + contexto "vs. periodo anterior".
   - Rejilla 2×2 de **desglose por divisa** (EUR/USD/MXN/DOP, nº de cuentas + importe).
   - **Gráfico de evolución** SVG (área + línea, retícula, etiquetas de eje en mono, 12 meses).
3. **Fila de 4 KPIs** (`.ab-kpi`): Ingresos (verde), Egresos (rojo), Disponible, Inmovilizado, con helper de variación.
4. **Rejilla 2fr / 1fr**:
   - *Saldos por titular*: filas con nombre + badge de tipo + importe mono + **barra de cuota** (pista `--bg-surface-muted`, relleno `--accent-primary`) + % y disponible.
   - Lateral: *Plazos fijos* (monto, intereses en verde, próximo vencimiento con badge de aviso) y *Alertas* (saldo bajo, vencimiento de plazo).

### 7.3 Extractos

- Header con H1 + acciones **Exportar** (`.button-secondary`) y **Añadir línea** (`.button-primary`).
- Barra de filtros: **búsqueda** en vivo (filtra concepto/banco), **chips de banco** (`Todos / BBVA / Santander / CaixaBank`), badge de divisa y contador "N de 248".
- **Tabla**: Fecha (mono) · Concepto · Banco · Importe (mono, verde/rojo) · Saldo (mono) · Estado (badge con punto). Cabecera/pie en `--bg-surface-soft`; filas marcadas con `--row-flagged-bg`; hover de fila a 120ms. Pie con **neto visible** calculado en vivo (verde/rojo).
- **Estado vacío** (`.ab-empty`): icono + "Sin coincidencias." + sugerencia, cuando el filtro no devuelve filas.

### 7.4 Login

- Layout dividido `1.05fr / 1fr`.
- **Panel de marca** (siempre oscuro): logo, titular *"Tesorería local, control real."*, descripción, badges (Multi-banco / Multi-divisa / Red local), firma "by Atlas Labs".
- **Panel de acceso**: toggle de tema arriba a la derecha; tarjeta `.ab-card` con inner highlight, campos Email/Contraseña (`.ab-field` + `.ab-input`), checkbox "Recordar este dispositivo durante 62 días" y botón **Entrar** a bloque.

### 7.5 Pantalla pendiente (placeholder)

Para ítems de nav fuera del alcance: estado vacío con icono de la sección y enlace "Volver al dashboard" — mantiene el mismo patrón visual.

---

## 8. Clases del Design System utilizadas

`.ab-card` · `.ab-card--flush` · `.ab-card-header` · `.ab-card-meta` · `.ab-kpi` · `.ab-kpi-helper` · `.ab-badge` (+ `--success / --warning / --danger / --info / --accent / --neutral`) · `.ab-badge-dot` · `.ab-tabs` · `.ab-tab` (+ `--active`) · `.ab-field` · `.ab-field-label` · `.ab-input` · `.ab-empty` (+ `-icon`) · `.button-primary` · `.button-secondary` · `.ab-button--block` · `.ab-button--sm`.

Stylesheets cargados en `<helmet>`: `fonts, colors, typography, spacing, effects, base, components` + `styles.css`.

---

## 9. Interactividad (estado de la clase lógica)

`screen` (login / dashboard / extractos / placeholder) · `theme` (light / dark, espejado en `<html>`) · `periodo` (1m/3m/12m/24m) · `query` (búsqueda de extractos) · `banco` (chip activo). Filtrado y neto se recalculan en `renderVals()` a partir de la semilla de 12 movimientos.
