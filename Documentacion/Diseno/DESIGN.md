# DESIGN.md - Sistema de diseno Atlas Balance

> Fuente de verdad visual para Atlas Balance. Si una pantalla se desvia, se corrige la pantalla o se actualiza este documento. Lo de "ya se ve bien" no cuenta como argumento.

## 1. Principios

1. **Tesoreria primero.** La app existe para leer saldos, riesgo, movimientos, vencimientos y permisos. Todo lo demas es decorado sospechoso.
2. **Numeros como protagonista.** El dato financiero manda sobre iconos, sombras, ilustraciones y microcopy.
3. **Densidad util, no apretujada.** Atlas Balance debe parecer una cabina financiera sobria: mucha informacion, poca ansiedad visual.
4. **Jerarquia antes que color.** Usa tamano, peso, posicion y espaciado antes de tirar de azul en todo.
5. **Menus con criterio.** Un menu plano de 13 entradas es mala arquitectura visual. Agrupa, prioriza y esconde lo administrativo cuando no toca.
6. **Movimiento sobrio.** La animacion confirma estado; no hace teatro.
7. **Sin circo SaaS.** Nada de gradientes heroicos, cards flotantes porque si, iconos gigantes, sombras dramaticas o UI de plantilla.
8. **Accesible por defecto.** Focus visible, targets tocables, contraste real y navegacion por teclado no son extras.

## 2. Atmosfera visual

Atlas Balance debe sentirse como una herramienta financiera interna premium:

- **Calma:** fondos azulados muy suaves, superficies limpias y bordes discretos.
- **Precision:** alineacion fuerte, numeros tabulares, tablas legibles y estados claros.
- **Confianza:** nada de colores chillones ni efectos que parezcan crypto dashboard.
- **Madurez:** controles consistentes, menus previsibles, iconos finos, copy corto.

Escala objetivo:

| Eje | Valor | Significado |
|---|---:|---|
| Densidad | 7/10 | App operativa con tablas, filtros y dashboards reales. |
| Variacion | 4/10 | Composiciones variadas, pero no artisticas. Finanzas no necesitan fuegos artificiales. |
| Movimiento | 3/10 | Transiciones cortas y tactiles, sin rebote. |
| Calidez | 4/10 | Azul financiero con neutros suaves; nada frio tipo terminal. |

## 3. Stack visual obligatorio

- React 18 + TypeScript + Vite.
- CSS variables propias. **No Tailwind. No shadcn. No styled-components.**
- `lucide-react` como libreria preferida para iconos nuevos.
- `Icons.tsx` puede existir como compatibilidad, pero no debe crecer sin motivo. Si se anaden iconos, usar Lucide salvo que el logo/simbolo sea propio.
- Charts con Recharts, pero nunca con aspecto default.
- Tablas grandes con `@tanstack/react-virtual`.
- Dark/light mode mediante `data-theme` y las variables existentes.

## 4. Color

### Filosofia

La paleta actual se mantiene. Es buena: azul maduro, neutros frios y estados suaves. El problema no es el color; el problema seria usarlo como maquillaje para esconder mala jerarquia.

Distribucion recomendada:

- 80% neutros.
- 12% azul primario.
- 8% estados y colores de chart.

### Modo claro

```css
:root {
  --bg-app: #f3f7fb;
  --bg-canvas: #f7fafc;
  --bg-surface: #fbfcfe;
  --bg-surface-soft: #f1f5fa;
  --bg-surface-muted: #e9eff6;
  --bg-input: #fbfcfe;
  --bg-hover: #e9eff6;
  --bg-selected: #e6eefc;

  --border-soft: rgba(23, 33, 52, 0.10);
  --border-strong: rgba(23, 33, 52, 0.18);
  --border-focus: #285bd9;

  --text-primary: #172033;
  --text-secondary: #536174;
  --text-muted: #5f6b7a;
  --text-inverse: #f8fbff;
  --text-link: #244fbd;

  --accent-primary: #285bd9;
  --accent-primary-hover: #214cad;
  --accent-primary-soft: #e6eefc;

  --success-bg: #eaf8f0;
  --success-text: #176a3a;
  --warning-bg: #fff4df;
  --warning-text: #8a5200;
  --danger-bg: #fdecef;
  --danger-text: #a82234;
  --info-bg: #eaf1ff;
  --info-text: #315fcf;

  --amount-positive: #176a3a;
  --amount-negative: #a82234;
}
```

### Modo oscuro

```css
[data-theme="dark"] {
  --bg-app: #12171f;
  --bg-canvas: #151b24;
  --bg-surface: #1b2330;
  --bg-surface-soft: #202a38;
  --bg-surface-muted: #293545;
  --bg-input: #161d27;
  --bg-hover: #242f3e;
  --bg-selected: #233558;

  --border-soft: rgba(216, 226, 240, 0.10);
  --border-strong: rgba(216, 226, 240, 0.18);
  --border-focus: #82a4ff;

  --text-primary: #f1f5f9;
  --text-secondary: #b7c2d2;
  --text-muted: #98a6ba;
  --text-inverse: #101720;
  --text-link: #a8bdff;

  --accent-primary: #82a4ff;
  --accent-primary-hover: #a8bdff;
  --accent-primary-soft: rgba(130, 164, 255, 0.14);
}
```

### Reglas de uso

- Azul primario solo para accion principal, foco, estado activo y serie principal de charts.
- Verde solo para saldo positivo, exito o entrada de dinero.
- Rojo solo para saldo negativo, error, eliminacion o salida de dinero.
- Amarillo solo para aviso accionable: vencimiento, alerta o fila marcada.
- No usar azul para texto largo.
- No crear nuevos colores "porque queda bonito". Si necesitas uno, primero demuestra que los tokens actuales no sirven.

## 5. Tipografia

### Familias

```css
--font-family-heading: "Hind Madurai", "National Park", "Aptos Display", "Segoe UI", sans-serif;
--font-family: "National Park", "Aptos", "Segoe UI", sans-serif;
--font-family-mono: "Atlas Mono", "JetBrains Mono", "SF Mono", monospace;
```

`Geist` existe como dependencia, pero el sistema actual usa `National Park`, `Hind Madurai` y `Atlas Mono`. No metas otra familia en una pantalla suelta.

### Escala

| Token | Tamano | Peso | Uso |
|---|---:|---:|---|
| `page-title` | 2rem / 32px | 700 | Titulo principal de vista. |
| `section-title` | 1.5rem / 24px | 600 | Bloques grandes del dashboard. |
| `panel-title` | 1.125rem / 18px | 600 | Cards, modales y paneles. |
| `body` | 0.875rem / 14px | 400-500 | Texto general. |
| `table` | 0.8125rem / 13px | 500 | Tablas densas. |
| `meta` | 0.75rem / 12px | 500-600 | Labels, badges, ayuda. |
| `kpi` | clamp(2rem, 3vw, 2.75rem) | 700-800 | Numeros principales. |

### Reglas

- `font-variant-numeric: tabular-nums` para saldos, importes, fechas tecnicas y totales.
- `Atlas Mono` para importes en tablas, IDs cortos, codigos, auditoria y columnas muy numericas.
- Maximo 3 pesos por pantalla.
- Letter spacing por defecto: `0`. En labels uppercase se permite `0.03em`.
- No usar italic salvo citas o nombres de campos tecnicos heredados.

## 6. Espaciado

Base actual: 4/8px.

```css
--space-1: 0.25rem; /* 4px */
--space-2: 0.5rem;  /* 8px */
--space-3: 0.75rem; /* 12px */
--space-4: 1rem;    /* 16px */
--space-5: 1.5rem;  /* 24px */
--space-6: 2rem;    /* 32px */
--space-8: 3rem;    /* 48px */
--space-10: 4rem;   /* 64px */
```

Uso recomendado:

- Gutters desktop: 24px.
- Gutters mobile: 12px.
- Gap entre cards: 16-24px.
- Padding de card normal: 20-24px.
- Padding de panel denso o tabla: 12-16px.
- Separacion entre bloques principales: 32-40px.

Regla simple: si una pantalla financiera necesita scroll, perfecto; si necesita lupa, has fracasado.

## 7. Radios, bordes y sombras

```css
--radius-control: 8px;
--radius-card: 8px;
--radius-panel: 18px;
--radius-shell: 22px;
--radius-pill: 999px;

--shadow-card: 0 8px 24px color-mix(in srgb, var(--text-primary) 6%, transparent);
--shadow-card-hover: 0 16px 34px color-mix(in srgb, var(--text-primary) 10%, transparent);
--shadow-overlay: 0 24px 60px color-mix(in srgb, var(--text-primary) 16%, transparent);
```

Reglas:

- Cards estandar: `radius-card`, border soft, shadow card.
- Paneles grandes y modales: `radius-panel`.
- Controles: `radius-control`.
- No meter cards dentro de cards.
- No aumentar sombras para "hacerlo premium". Sombra grande casi siempre significa inseguridad visual.
- En tablas densas, usa bordes y fondos sutiles antes que elevacion.

## 8. Iconografia

### Libreria y estilo

- Preferencia: `lucide-react`.
- Stroke: 1.75-2px.
- Tamano sidebar/topbar: 20px.
- Tamano botones icono: 18-20px.
- Tamano empty state: 32-40px.
- Iconos siempre lineales, nunca rellenos pesados.

### Mapa recomendado

| Area | Icono Lucide recomendado |
|---|---|
| Dashboard | `LayoutDashboard` |
| Titulares | `Building2` / `UserRound` segun contexto |
| Cuentas | `WalletCards` |
| Extractos | `TableProperties` |
| Importacion | `Upload` |
| Formatos | `FileCog` |
| Alertas | `BellRing` |
| Exportaciones | `DownloadCloud` |
| Usuarios | `UsersRound` |
| Auditoria | `ClipboardList` |
| Configuracion | `Settings` |
| Backups | `DatabaseBackup` |
| Papelera | `Trash2` |
| Plazo fijo | `LockKeyhole` o `CalendarClock` |
| Autonomo | `BriefcaseBusiness` |

### Reglas

- Un icono no sustituye un label en desktop salvo sidebar colapsada.
- Todo boton solo-icono lleva `aria-label` y `title`.
- No mezclar estilos: nada de iconos 3D, emojis, pictogramas de colores o SVGs con pesos distintos.
- Los badges numericos en menu deben ser pequenos y funcionales, no medallas de carnaval.

## 9. Navegacion

### Problema actual a vigilar

Atlas Balance tiene muchas areas reales. Si todas aparecen al mismo nivel, el menu deja de orientar. No es cuestion estetica; es coste cognitivo.

### Arquitectura recomendada

Agrupar la navegacion en tres bloques:

1. **Operacion**
   - Dashboard
   - Titulares
   - Cuentas
   - Extractos
   - Importacion
2. **Control**
   - Alertas
   - Exportaciones
3. **Sistema** solo admin
   - Usuarios
   - Auditoria
   - Formatos
   - Backups
   - Configuracion
   - Papelera

En desktop puede ser sidebar con separadores discretos. En mobile debe ser bottom nav con maximo 5 destinos principales y un sheet `Mas` para el resto.

### Sidebar

- Ancho expandido: 252px.
- Ancho colapsado: 72px.
- Padding: 16px.
- Fondo: `--color-bg-sidebar`.
- Active: pill con `--color-sidebar-active-bg`.
- Hover: `--bg-hover`.
- Label truncado con ellipsis.
- Badge solo si hay trabajo pendiente real.

Reglas:

- El logo no debe competir con la navegacion.
- Active item debe ser obvio sin depender solo del color.
- Separadores de grupo: finos, con label opcional en `meta`.
- En colapsado, tooltips obligatorios si se implementan nuevos grupos.

### Topbar

- Alto: 68px.
- Debe resolver contexto: titulo de pagina + breadcrumb corto.
- Acciones a la derecha: usuario, tema, logout y acciones propias de la pagina si caben.
- No llenar la topbar con filtros complejos; filtros viven en la pantalla.
- Buscador global solo si busca de verdad en entidades relevantes. Un buscador falso es basura.

### Mobile

- Bottom nav con 5 slots maximo.
- Recomendado: Inicio, Titulares, Cuentas, Importar, Mas.
- El sheet de `Mas` lista Extractos, Alertas, Exportaciones y Sistema segun permisos.
- Target tactil minimo: 44px.
- Nada de sidebar drawer si el bottom nav ya resuelve el flujo principal.

## 10. Componentes core

### Botones

```css
.button-primary   /* accion principal de la vista */
.button-secondary /* accion normal */
.button-tertiary  /* enlace/accion ligera */
.button-danger    /* borrar, cancelar destructivo */
.button-icon      /* solo icono */
```

Reglas:

- Una accion primaria visible por bloque.
- Botones destructivos nunca comparten color con acciones normales.
- Active: `scale(0.98)` o translate minimo.
- Loading: spinner pequeno o texto "Guardando"; no bloquear la pantalla entera salvo operacion global.
- Disabled: opacidad 0.5 y cursor disabled.

### Inputs

- Label arriba, siempre.
- Helper debajo si evita errores reales.
- Error debajo, junto al campo.
- Focus: `--border-focus` + `--shadow-focus`.
- Altura: `--control-height` (44px).
- No floating labels. Son bonitos en screenshots y molestos en trabajo repetido.

### Selects y menus

- Usar `AppSelect` para selects custom accesibles.
- Opcion seleccionada con fondo `--accent-primary-soft`.
- Enter/Espacio abren y seleccionan.
- Escape cierra.
- No crear dropdowns distintos para cada pantalla.

### Cards

- Card normal: superficie, border soft, shadow card, radius card.
- Card interactiva: hover con shadow card hover y borde algo mas claro.
- Card de KPI: label pequeno, numero grande, delta discreto.
- Card de chart: titulo, filtro compacto, grafica con aire.
- Card de tabla: evitar padding excesivo; la tabla necesita area util.

### Modales

- Overlay oscuro con blur moderado.
- Container `radius-panel`, max width segun contenido.
- Cerrar con Escape y click fuera si no hay cambios sin guardar.
- Focus trap obligatorio.
- Accion primaria abajo a la derecha; cancelar a su izquierda.
- No modal dentro de modal. Si lo necesitas, el flow esta mal.

### Toasts

- Bottom-right en desktop.
- Encima del bottom nav en mobile.
- Texto corto y accionable.
- Error persistente un poco mas, exito breve.
- Nunca usar `alert()` nativo.

## 11. Tablas y datos financieros

Las tablas son producto, no relleno.

### Tablas grandes

- Header sticky.
- Primera columna sticky cuando el usuario necesita orientarse por fila.
- Numeros alineados a la derecha.
- Importes en mono/tabular.
- Separadores de celda claros pero suaves.
- Hover de fila ligero.
- Foco de celda visible.
- Virtualizacion cuando hay muchas filas.

### Extractos tipo hoja de calculo

- La rejilla debe ganar sobre la estetica de card.
- Columnas con ancho estable.
- No ocultar saldos/importes detras de badges.
- Estados de fila:
  - marcada: amarillo suave.
  - error: rojo suave.
  - seleccionada: azul suave.
  - foco: borde/fondo visible.

### Formato numerico

- Usar `Intl.NumberFormat`.
- Mostrar divisa cerca del importe, no en una leyenda lejana.
- No mezclar formatos `1.000,00` y `1,000.00`.
- Valores negativos: color danger + signo, no solo color.

## 12. Charts

```css
--chart-ingresos: #2f9f68;
--chart-egresos: #c94a5a;
--chart-saldo: #285bd9;
--chart-grid: rgba(23, 33, 52, 0.10);
```

Reglas:

- Gridlines casi invisibles.
- Leyendas compactas.
- Maximo 3 series importantes por chart.
- Azul para saldo, verde para ingresos, rojo para egresos.
- Area fills translucidos.
- Tooltips con superficie Atlas, no default Recharts.
- El chart no debe esconder el numero principal.

## 13. Estados

### Loading

- Skeleton con la forma real del contenido.
- Tablas: 5-8 filas skeleton.
- Cards: skeleton de label + numero + linea secundaria.
- Spinner solo dentro de botones o acciones puntuales.

### Empty state

- Icono lineal 32-40px.
- Titulo claro.
- Texto de una frase.
- CTA si el usuario puede resolver el vacio.
- Nunca escribir "No hay datos" y ya. Eso es pereza.

### Error state

- Inline cuando afecta un campo.
- Card/panel cuando afecta una seccion.
- Mensaje: que paso + que puede hacer el usuario.
- Para errores tecnicos, detalle colapsable o copyable si ayuda a soporte.

### Permisos

- Si el usuario no tiene permiso, no mostrar acciones imposibles.
- Si una seccion esta bloqueada, explicar en una frase.
- No filtrar solo en frontend. Backend manda.

## 14. Formularios

### Layout

- Formularios cortos: una columna.
- Formularios medios: dos columnas desktop, una mobile.
- Campos dependientes aparecen debajo del selector que los activa.
- Agrupar por secciones con titulo pequeno, no con cards anidadas.

### Cuentas y plazo fijo

- Tipo de cuenta visible temprano.
- Si `PLAZO_FIJO`, mostrar fecha inicio, vencimiento, interes previsto, renovable, cuenta referencia y notas.
- Estado de plazo fijo con badge: Activo, Proximo a vencer, Vencido, Renovado, Cancelado.
- Accion `Renovar` separada de edicion general.

### Importacion

- El usuario debe ver: origen, cuenta, formato, preview, errores/avisos y confirmacion.
- Errores bloqueantes y avisos importables deben verse distintos.
- No esconder columnas criticas en mobile; mejor permitir scroll horizontal controlado en preview financiero que destruir la tabla.

## 15. Responsive

Breakpoints operativos:

```css
mobile:  < 768px
tablet:  768px - 1024px
desktop: > 1024px
wide:    > 1440px
```

Reglas:

- Desktop es el caso principal.
- Tablet colapsa sidebar.
- Mobile usa bottom nav.
- Tablas densas pueden simplificar controles, pero no falsear datos.
- Sin overflow horizontal global.
- Touch target minimo: 44px.
- Texto de botones debe caber; si no cabe, icono + tooltip/title o texto mas corto.

## 16. Motion

```css
--ease-premium: cubic-bezier(0.22, 1, 0.36, 1);
--duration-instant: 120ms;
--duration-fast: 180ms;
--duration-base: 240ms;
--duration-slow: 420ms;
```

Uso:

- Hover/focus: 120-180ms.
- Popovers/selects: 160-220ms.
- Cambio de pagina: 220ms max.
- Charts: 400-700ms solo en primera carga.

Reglas:

- Animar `transform` y `opacity`.
- No animar ancho, alto, top o left si puede evitarse.
- Respetar `prefers-reduced-motion`.
- Nada de rebotes en dashboard financiero. No es una app de stickers.

## 17. Accesibilidad

- Contraste WCAG AA minimo.
- Focus visible siempre.
- `aria-label` en botones solo-icono.
- `aria-current` o clase activa clara en navegacion.
- Skip link al contenido principal.
- Escape cierra modal, popover y sheet.
- Tab no se escapa de modales.
- Los mensajes de error deben asociarse al campo.
- Las tablas deben conservar semantica de tabla cuando sean datos tabulares.

## 18. Copy de interfaz

Tono:

- Corto.
- Directo.
- Sin humo.
- En castellano claro.

Reglas:

- Botones con verbo: `Guardar`, `Importar`, `Renovar`, `Eliminar`.
- Evitar frases tipo "Gestiona de forma sencilla..." dentro de la app. El usuario ya esta dentro; no le vendas la moto.
- Errores: "No se pudo guardar la cuenta" mejor que "Algo salio mal".
- Labels: `Saldo inmovilizado`, no `Monto bloqueado fancy`.

## 19. Patrones por pantalla

### Dashboard

- Primera lectura: KPIs + saldos por divisa como resumen compacto; la grafica de evolucion debe tener una franja propia de ancho completo cuando el usuario necesite leer tendencia.
- Mostrar disponible, inmovilizado y total sin esconder inmovilizado.
- Saldos por titular agrupados por Empresa, Autonomo y Particular.
- Si todo tiene el mismo tamano, nada importa. Variedad controlada.

### Titulares

- Filtro por tipo arriba.
- Lista/table con nombre, tipo, total, cuentas y estado.
- Detalle con resumen financiero antes de datos administrativos.

### Cuentas

- Filtros por titular, tipo de titular, tipo de cuenta y divisa.
- Tipo de cuenta visible como badge.
- Plazo fijo debe destacar vencimiento y estado.

### Extractos

- Vista principal tipo hoja.
- Acciones masivas arriba de la tabla.
- Historial/auditoria accesible, pero no ensuciando cada celda.

### Alertas

- Alcance visible: Global, Tipo titular, Cuenta.
- Prioridad explicita: Cuenta > Tipo titular > Global.
- Estados con color suave, no banners rojos por todo.

### Configuracion/Sistema

- Tabs o secciones claras.
- No mezclar configuracion tecnica, usuarios y backups en el mismo panel visual.
- Acciones peligrosas con confirmacion y copy especifico.

## 20. Anti-patrones prohibidos

- Gradientes decorativos en fondos principales.
- UI monocromatica azul donde todo parece activo.
- Tres o cuatro cards iguales por defecto en cada pantalla.
- Iconos gigantes compitiendo con saldos.
- Emojis en UI.
- Sombras grandes bajo cada bloque.
- Texto gris claro ilegible.
- Badges para todo.
- Pills infinitas para filtros que deberian ser selects.
- Modales anidados.
- Dropdowns sin teclado.
- Scroll horizontal global.
- Charts con colores arcoiris.
- Placeholders `Lorem ipsum`, `John Doe`, `Acme`.
- Copy de IA: "eleva", "potencia", "sin friccion", "next-gen".
- Cambiar el stack visual porque una libreria de moda trae componentes bonitos. Eso es como comprar un ferrari para ir a por pan.

## 21. Checklist antes de cerrar UI

- La informacion principal se entiende en 2 segundos.
- El numero importante es el elemento dominante.
- Hay una sola accion primaria por bloque.
- Los menus estan agrupados por intencion, no por orden historico.
- Iconos con mismo peso visual.
- Focus visible probado con teclado.
- Mobile no tiene overflow global.
- Dark mode no rompe contraste.
- Loading, empty y error existen.
- Las tablas financieras mantienen alineacion y numeros tabulares.
- No se introdujo Tailwind, shadcn ni otro sistema visual paralelo.
