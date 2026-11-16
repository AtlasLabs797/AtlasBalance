# Informe Modern Web Guidance - frontend de Atlas Balance (V-03.02)

Fecha: 2026-11-16. Rama `V-03.02`. Auditoria **estatica**, solo lectura: no se toco codigo.

## 1. Alcance y metodo

- **Guias leidas desde disco** (no via `npx`, segun AGENTS.md secciones 7 y 9):
  - *Enteras o casi*: `accessibility` (secciones 1-12), `color`, `typography`, `responsive-design`, `responsive-table`, `motion`, `respect-os-text-scale` (inicio y estado de soporte), `dialog` (reglas y soporte), `tabs` (inicio).
  - *Parcialmente*: `dark-mode` (pasos 1-3 y soporte), `css` (secciones 2, 5, 6), `performance` (CSS, fuentes y code-splitting), `forms` (secciones 1-3), `form-factor-adaptation` (objetivos tactiles y contenedores). De `menu`, `sortable-table`, `customize-scrollbar-*`, `design-token-reactivity` y otras solo se leyo el estado Baseline.
  - *No leidas*: `html`, `css-layout`, `security`, `privacy`, `user-experience/content-first-noise-reduction`, `natural-interactions` y el resto de guias especificas.
- **Fuera de alcance, con motivo**: `chrome-extensions` (no hay extension), `wasm`, `webmcp`, `built-in-ai` (la IA es del backend), `pwa`, `passkeys` (LAN on-prem con MFA propio), carruseles, parallax, scrollytelling, WebGL (contradicen PRODUCT.md: "motion confirma estado").
- **`legacy-scan` no se ejecuto** (necesita `npx` con red y no se pidio autorizacion). Cubre ~20 % del catalogo; queda pendiente y opcional.
- **Cobertura de formularios y movil**: solo muestreo (login, cambio de contrasena, grep de atributos); ver seccion 3b. No se audito por pantalla.
- **Vara de medir**: lo que `PRODUCT.md` ya promete (contraste AA, teclado, foco visible, `prefers-reduced-motion`, mobile operativo, textos largos). Lo que lo incumple va primero; la modernizacion pura, despues.
- **Supuesto de navegadores**: no esta documentada una politica. Se asume Chrome/Edge actuales en Windows (4-8 usuarios en LAN). Los estados Baseline salen de las propias guias; donde la guia no dice nada, se marca "no indicado". Recomendacion: fijar la politica en `AGENTS.md` (p. ej. "Allow Newly Available, fallback solo si <= 20 lineas"), como sugiere la skill.
- **Decisiones previas respetadas**: `PRODUCT.md`, `Documentacion/Diseno/DESIGN.md` (tokens Apple-like con Geist autoalojado) y las auditorias V-02-02. Nada de aqui propone Tailwind ni dependencias nuevas.

## 2. Resumen ejecutivo

La base es buena: tokens en capas, Geist autoalojado con `preload`, 27 rutas `lazy`, `manualChunks`, grid virtualizado con `aria-rowcount`/`aria-colindex`/`aria-sort`, skip link, `lang="es"`, login con `autoComplete`/`aria-invalid`/`role="alert"`, tabla oculta `sr-only` como alternativa en `EvolucionChart` (no se comprobo en los demas graficos; el resto usa `role="img"` + `aria-label`), sin `dangerouslySetInnerHTML` en toda la app.

Los problemas reales no son de modernidad sino de **accesibilidad incumplida frente a PRODUCT.md**, y se concentran en cuatro cosas:

1. El anillo de foco es solo `box-shadow` de 1,44:1 y desaparece en Alto Contraste de Windows; ninguna regla lo restaura con `outline`.
2. Los bordes de campos (1,32:1) y el texto terciario (3,62:1) no llegan a AA. Arreglarlo cambia el lenguaje visual, asi que es decision tuya.
3. El titulo de pagina nunca cambia al navegar (WCAG 2.4.2, nivel A).
4. Las pestanas de Importacion usan `role="tab"` sin el comportamiento de teclado que ese rol exige (4.1.2, nivel A).

Tambien faltan el respeto a la preferencia de tamano de texto y al tema del sistema (P2). Lo demas (dialogos nativos, anchor positioning, view transitions...) es opcional y, en varios casos, **no conviene** (seccion 6). Los dialogos propios son fragiles pero **no se demostro ningun bug** (P2-4).

## 3. Hallazgos priorizados

Formato: evidencia, regla de la guia, estado Baseline/fallback, impacto, esfuerzo, riesgo. Prioridad P1 = incumple PRODUCT.md/WCAG A-AA; P2 = mejora clara; P3 = pulido.

### P1-1. Foco solo con `box-shadow`, sin `forced-colors` y con contraste insuficiente

- **Evidencia**: `src/styles/design-system/tokens/base.css:6` (`:focus-visible{outline:none;box-shadow:var(--ring-focus)}`), `src/styles/global.css:21-23, 456-458, 493-495, 799-802`, `elevation.css:15` (`--ring-focus: 0 0 0 3px var(--accent-ring)`), `semantic.css:9` (`--accent-ring` = acento al 28 % transparente). Cero reglas `forced-colors` en `src/`. Se comprobo ademas que **ninguna** declaracion `outline:` distinta de `none` restaura el foco en ningun componente.
- **Medido** (calculo sobre los tokens): anillo claro sobre blanco **1,44:1**, sobre `--surface-page` 1,42:1, en oscuro 1,38:1. Minimo exigido: 3:1.
- **Guia (`accessibility`, Focus indicators)**: "Prefer `outline` over other properties for focus rings: If `box-shadow` must be relied on for focus rings, provide an `outline`-based fallback for High Contrast Mode using the `forced-colors` media query." y "Don't disable browser native focus indicators without providing a replacement ... at least `3:1`". `color`: en Forced Colors el navegador "removes `box-shadow`".
- **Baseline**: `outline`, `outline-offset`, `:focus-visible`, `forced-colors`: base de la plataforma; sin fallback.
- **Como**: definir un unico `--focus-outline: 2px solid var(--accent-solid)` (cobalt-600 sobre blanco = 5,85:1; en oscuro usar `--accent-300`) con `outline-offset: 2px`, aplicarlo en el selector global de `base.css:6` y en los 9 sitios que repiten `outline: none` (`global.css` x7, `auth.css:282`, `admin.css:166`). Mantener el `box-shadow` solo como halo decorativo. Los campos de texto ya cambian `border-color` a acento al enfocar (`global.css:399`), asi que alli basta con el outline.
- **Impacto**: alto (teclado y usuarios con Alto Contraste). **Esfuerzo**: S. **Riesgo**: bajo; revisar clipping en contenedores con `overflow` (ver tambien guia `motion`: `clip-path` recorta el outline).

### P1-2. Bordes de controles por debajo de 3:1

- **Evidencia**: `global.css:81` (`border: 1px solid var(--border-default)`), `--border-default` = `#E0E0E0` -> **1,32:1** sobre blanco; `--border-strong` `#D2D2D7` 1,51:1; oscuro `--border-strong` 1,90:1.
- **Guia (`color`)**: "Ensure non-text contrast standards: Maintain a minimum contrast ratio of 3:1 for user interface component boundaries and states ... (e.g., input field borders)."
- **Matiz**: el hairline de bajo contraste es una decision del design system (DESIGN.md). Se puede respetar en tarjetas y tablas, que no son controles. El problema es solo el borde de `input`, `select`, `textarea`, checkbox y botones secundarios.
- **Como**: token nuevo `--border-control` (p. ej. `--gray-500` `#86868B` = 3,62:1 en claro; en oscuro subir a ~`rgba(255,255,255,.5)`) usado solo en esos controles. Alternativa que la guia admite: bordes suaves pero con relleno diferenciado que cumpla 3:1 contra el fondo.
- **Decision tuya**: cambia la estetica "calma" de formularios. **Esfuerzo**: S-M. **Riesgo**: visual, no funcional.

### P1-3. Texto terciario y "muted" por debajo de 4,5:1

- **Evidencia**: `semantic.css:11` (`--text-tertiary: var(--gray-500)`), `variables.css` (`--text-muted: var(--text-tertiary)`); 55 usos de `--text-muted`/`--text-tertiary` en `src/styles`. `#86868B` sobre blanco **3,62:1**, sobre `#F5F5F7` **3,33:1**. En oscuro `#A1A1A6` sobre `#1D1D1F` = 6,54:1, correcto.
- **Guia (`color`)**: "Maintain 4.5:1 for normal text and 3:1 for large text".
- **Como**: el valor que llega a 4,5:1 es `--gray-600` (`#6E6E73` = 5,07:1), pero `--text-secondary` **ya es** `--gray-600` (`semantic.css:11`): el cambio fundiria secundario y terciario en el mismo color y se perderia un nivel de jerarquia. Opciones: (a) fundirlos y compensar con tamano/peso (PRODUCT.md: "jerarquia antes que color"); (b) un gris intermedio (~`#767679`, 4,5:1 sobre blanco) para terciario y mover el secundario a `--gray-700`; (c) limitar el terciario a texto grande/decorativo y no usarlo en etiquetas ni importes.
- **No verificado**: en cuantos de los 55 usos el texto es esencial (etiquetas de importes, ejes de grafico) frente a decorativo.
- **Esfuerzo**: XS tecnico, pero es **decision de sistema** (va a la seccion 7 junto a P1-2). **Riesgo**: visual.

### P1-4. Titulo de documento fijo en una SPA

- **Evidencia**: `index.html:20` (`<title>Atlas Balance</title>`); `grep document.title` en `src/` = 0 resultados.
- **Guia (`accessibility`, Titles)**: "Provide unique page titles" y "Update single-page application (SPA) titles during page transitions" (2.4.2, nivel A).
- **Como**: formato `Seccion | Atlas Balance`. La opcion mas simple, a confirmar: React 19 (instalado 19.2.8) eleva a `<head>` un `<title>` renderizado dentro de un componente, asi que bastaria un `<title>` por pagina sin hook ni efecto (dato del release de React 19, no de las guias; comprobar en el build). Si no, un hook `useDocumentTitle` o un mapa ruta -> titulo en `Layout`. Opcional: mover el foco al `h1` de la pagina tras navegar (el skip link a `#main-content` ya existe en `Layout.tsx:103`).
- **Impacto**: pestanas indistinguibles para todos y lectores de pantalla ciegos al cambio de pagina. **Esfuerzo**: S. **Riesgo**: nulo.

### P1-5. Pestanas con `role="tab"` sin comportamiento de pestana

- **Evidencia**: `ImportacionPage.tsx:733-745` (botones `role="tablist"/"tab"` sin `tabIndex` itinerante ni teclas de flecha; ningun `ArrowRight` en el archivo). En `ConfiguracionPage.tsx:725-765` el teclado esta bien resuelto para los botones (`tabIndex={isActive ? 0 : -1}`, `handleTabKeyDown` en :685), pero la pestana "Paises" es un `NavLink` (navegacion de ruta) dentro del `tablist`, y ambas pestanas llevan `aria-controls`.
- **Guia (`accessibility`)**: "Match ARIA implementations to actual behavior: When applying `role="tab"` to an element, it must then behave like a tab — including keyboard interactions" (4.1.2, nivel A). **`tabs`**: "DO NOT use this or the WAI-ARIA tabs pattern for page navigation" y "Do not add `aria-controls` to the tabs: no screen reader uses it". La guia propone `focusgroup="tablist"` (Limited availability: no depender de el hoy).
- **Como**: Importacion: reutilizar la logica de `handleTabKeyDown` de Configuracion (flechas + `tabIndex` itinerante) o, si son solo un cambio de vista, quitar los roles y usar botones con `aria-pressed`. Configuracion: sacar el enlace a "Paises" del `tablist` (enlace normal junto al control) y quitar `aria-controls`.
- **Impacto**: medio (teclado y lector de pantalla en dos pantallas de uso frecuente). **Esfuerzo**: S. **Riesgo**: bajo.

### P2-1. Tamano de texto: raiz fija en 16 px y escala en `px`

- **Evidencia**: `global.css:10` (`html { font-size: 16px }`), `typography.css` (`--text-10..--text-56` en `px`; `--text-kpi` usa `px` en `clamp`), textos de 10-11 px en `extractos.css:481`, `revision-ai.css:1716, 1760, 1780`; 54 usos de `height: var(--control-height...)` y `--sheet-row-height: 44px` (`extractos.css:743`).
- **Guia (`typography`)**: "DON'T use `px` for font-size. Prefer `rem` to honor the user's browser font-size preferences" y "DON'T set a fixed `height` ... on elements that contain text, such as buttons" (WCAG 1.4.12). `respect-os-text-scale`: "Do not set a custom font size on `:root` or `html`"; opt-in con `<meta name="text-scale" content="scale">` (**Limited availability** segun la guia; se ignora donde no se soporta, asi que no hace dano).
- **Como**: borrar `font-size: 16px` de `html` (el valor por defecto ya es 16 px: cero cambio visual); convertir `typography.css` a `rem` (valor/16, mecanico). Ojo: quitar el 16 px de la raiz libera **todos** los `rem` de la app (471 apariciones en `src/styles`, espaciado incluido), no solo la tipografia, asi que el barrido visual debe ser global; `height` -> `min-block-size` en controles. **Excepcion deliberada**: las filas del grid virtualizado (`--sheet-row-height`) se quedan en altura fija porque `@tanstack/react-virtual` las estima; probarlas al 200 %.
- **Impacto**: medio (usuarios con baja vision; en LAN de 4-8 personas puede ser cero o muy relevante). **Esfuerzo**: M. **Riesgo**: medio-bajo, requiere barrido visual a 100/150/200 %.

### P2-2. Tema: no respeta el sistema y la barra del navegador puede desalinearse

- **Evidencia**: `main.tsx:15-16` (si no hay valor guardado, usa `'light'` sin consultar `prefers-color-scheme`); `index.html:10-11` (`theme-color` por media query del sistema); `global.css:14-19` (`color-scheme` solo ligado a `data-theme`); sin `<meta name="color-scheme">` en `index.html`; el tema se aplica desde `main.tsx`, tras cargar el bundle.
- **Efecto**: usuario con SO oscuro y sin preferencia guardada ve app clara bajo barra de navegador negra; usuario que alterna a mano puede ver la barra contraria; posible destello claro al cargar en oscuro.
- **Guia (`dark-mode`)**: "MANDATORY: ... place a `<meta>` tag in your `<head>`" con `color-scheme` `light dark`, y "Honor `@media (prefers-color-scheme: dark)`" (`color`). Soporte segun `dark-mode`: `color-scheme` Widely available (2022-02-03); `light-dark()` Newly available (2024-05-13).
- **Como**: tema inicial = guardado ?? `matchMedia('(prefers-color-scheme: dark)')`; `<meta name="color-scheme" content="light dark">`; actualizar el `theme-color` al alternar. La CSP del `index.html` prohibe scripts inline (`script-src 'self'`), asi que el script de arranque va como archivo externo sincrono en `<head>` (p. ej. `/theme-init.js`).
- **No hacer**: reescribir los tokens con `light-dark()`; es opcional segun la guia y `themes.css` ya funciona.
- **Decision tuya**: pasar de "claro por defecto" a "seguir al sistema". **Esfuerzo**: S. **Riesgo**: bajo.

### P2-3. Tablas con scroll horizontal no operables con teclado

- **Evidencia**: `.users-table-scroll` en `AuditoriaPage.tsx:334`, `BackupsPage.tsx:703`, `PaisesPage.tsx:291`, `ExportacionesPage.tsx:241` (y probablemente otras): sin `tabIndex`, `role="region"` ni nombre. Solo 2 `<caption>` en toda la app.
- **Guia (`responsive-table`)**: "Add a `tabindex="0"` attribute to the container, to ensure that people using only a keyboard can consistently scroll the table. Give the wrapper a `role="region"` attribute ... with an accessible name".
- **Como**: pequeno componente `TableScroll` o atributos en esos wrappers (`tabIndex={0}`, `role="region"`, `aria-labelledby` al titulo de la seccion). El foco ya sera visible tras P1-1.
- **Impacto**: medio, solo cuando hay desbordamiento. **Esfuerzo**: S. **Riesgo**: nulo.

### P2-4. Dialogos propios: fragiles, sin bug demostrado

- **Evidencia**: `src/hooks/useDialogFocus.ts:45-70` registra un `keydown` en `window` por cada dialogo abierto (Escape y trampa de Tab). `ConfirmDialog` se renderiza **dentro** del modal que lo abre (p. ej. `DesgloseModal.tsx:257`) y `useConfirmDialog` es estado por componente.
- **Secuencia analizada** (lectura de `useConfirmDialog.ts` y `UsuarioModal.tsx:430-437`): con el modal sucio y el confirm abierto, un segundo Escape ejecuta primero `closeModal` del modal (lanza otro `confirm()`, que resuelve el pendiente como `false` y reabre) y despues el `onCancel` del confirm (lo cierra con `false`). El resultado final es el esperado (el confirm se cierra y el modal sigue abierto), **por orden de registro de los listeners**, no por diseno. Tab: el confirm esta dentro del DOM del modal, asi que su conjunto de focusables lo incluye. **No se encontro un fallo reproducible** y nada se probo en runtime.
- **Otros datos**: fondo sin `inert` (0 usos) y bloqueo de scroll con `document.body.style.overflow = 'hidden'` (`Layout.tsx:40-45`); `role="dialog"`/`aria-modal="true"` puestos a mano en 13 sitios.
- **Guia (`dialog`)**, descripcion de lo que da un `<dialog>` modal: "The browser provides: inert page, focus containment, top layer, `::backdrop`, `Esc` and back-gesture dismissal, and focus return to the invoker." La regla "DON'T lock scrolling with `overflow: hidden` ... on `html` or `body`" aplica al `<dialog>` nativo: es lo que una migracion eliminaria, no un incumplimiento actual. Soporte: `<dialog closedby>` Limited (Chrome 134, Firefox 141, sin Safari; degrada a Escape); `overscroll-behavior` Limited en la guia (Chrome 144, Firefox 150, sin Safari); Invoker Commands Newly (2025-12-12); `<dialog>`/`showModal()`: la guia no indica estado.
- **Recomendacion**: **no migrar**. Como mucho, y solo si aparece un fallo real, una pila a nivel de modulo en `useDialogFocus` para que solo el dialogo superior atienda Escape y Tab. Un piloto con `ConfirmDialog` + `showModal()` seria posible (API de promesa intacta; sin `command`), pero habria que traducir el CSS de `.modal-backdrop` a `::backdrop` y cubrirlo con Playwright. Es opcional y de prioridad baja.
- **Esfuerzo**: S (pila) / M (piloto). **Riesgo**: bajo / medio.

### P3-1. Movimiento reducido: reset global agresivo y animaciones infinitas

- **Evidencia**: `global.css:937-945` (`animation-duration: 1ms !important; transition-duration: 1ms !important` sobre `*`); animaciones infinitas ambientales de la cara IA en `revision-ai.css:338, 355, 924, 1435-1441`.
- **Guia (`motion`)**: "DO NOT globally apply `animation-duration: 0.01ms;` as it can cause certain animations to become _more_ jarring ... handle reduced motion on a case-by-case basis" y "Keep non-motion transitions ... enabled". Ademas, regla 2.2.2: movimiento automatico de mas de 5 s necesita mecanismo de pausa; `prefers-reduced-motion` no lo sustituye.
- **Como**: sacar las animaciones `infinite` a `@media (prefers-reduced-motion: no-preference)` (estatico por defecto) y quitar el `*` global dejando solo desplazamiento/escala. `design-qa.md` ya anota que la verificacion visual con movimiento reducido quedo pendiente.
- **Impacto**: bajo (solo canal IA). **Esfuerzo**: S. **Riesgo**: bajo.

### P3-2. Barras de scroll solo con `::-webkit-scrollbar`

- **Evidencia**: `base.css:8-10`; sin `scrollbar-color`/`scrollbar-width` en `src/styles`.
- **Guia**: `scrollbar-width` Newly available desde 2024-12-11, `scrollbar-color` desde 2025-12-12; si el objetivo es Widely Available, "include the non-standard `::-webkit-scrollbar` pseudo-elements as fallbacks".
- **Como**: anadir `scrollbar-color: var(--border-strong) transparent` y `scrollbar-width: thin` conservando las reglas webkit. Solo importa si algun usuario usa Firefox. **Esfuerzo**: XS.

### P3-3. Cascada: 45 `!important`, sin `@layer`, capa "system-coherence" al final

- **Evidencia**: 45 `!important` (25 en `global.css`, 8 en `extractos.css`, 5 en `dashboard.css`); 0 `@layer`; `system-coherence.css` documentada como "capa comun de coherencia al final de la cascada".
- **Guia (`css`)**: "Use cascade layers (`@layer`) to define explicit priority zones ... declare their order upfront".
- **Como**: **no reescribir**. Si se toca, declarar `@layer tokens, base, layout, pages, coherence;` en `layout.css` y envolver cada import; no cambia el aspecto y permite ir quitando `!important` pagina a pagina. Riesgo de regresion visual no trivial: dejar para cuando haya QA visual automatizado.
- **Impacto**: mantenibilidad. **Esfuerzo**: M-L. **Prioridad**: la ultima.

### P3-4. Responsive por viewport en lugar de por contenedor

- **Evidencia**: 58 `@media` frente a 2 `@container` (`dashboard.css:482, 490`).
- **Guia (`responsive-design`)**: "Use `@container` queries to create component-driven responsive layouts". Util sobre todo en tarjetas del dashboard dentro de sidebar colapsable (el ancho del contenedor cambia sin que cambie el viewport). Ya hay `container-type` en 4 contenedores. Extender solo donde se vea un fallo real; no hay motivo para migrar los 58.

### 3b. Formularios y movil (muestreo, no auditoria)

Leidas `forms` (secciones 1-3) y `form-factor-adaptation`; revisado solo login, cambio de contrasena y `grep` de atributos.

- **Bien**: `autoComplete` correcto (`username`, `current-password`, `new-password`, `one-time-code`), `inputMode="numeric"` en el codigo MFA, `aria-invalid` + `aria-describedby` + `role="alert"` por campo, `AppSelect` sobre `<select>` nativo, `@media (pointer: coarse)` en 6 hojas, `100dvh` en lugar de `100vh`, alturas de control de 44 px.
- **Revisar (sin confirmar)**: 25 `type="number"` frente a 7 `inputMode="decimal"`: la guia desaconseja `type="number"` para codigos e identificadores, y los importes en formato europeo ya pasan por `parseEuropeanNumber`; habria que ver cuales de los 25 son importes. 0 `enterKeyHint`. Iconos de 24-26 px (`revision-ai.css:1988`, `admin.css:61`) cumplen AA (24 px) pero no el objetivo tactil de 44 px de la guia; solo importa si esa pantalla se usa en movil. 2 `<fieldset>` en toda la app: grupos de radios/checkboxes sin `legend` posibles.
- **No cubierto**: validacion con `:user-invalid` (la app usa React Hook Form), tablas en movil por pantalla, orden de tabulacion de formularios largos.

## 4. Verificado, no verificado, pendiente

- **Verificado (estatico)**: todo lo citado con `archivo:linea`; contrastes calculados en Node a partir de los hex de `colors.css`/`themes.css` con la formula WCAG 2.x; ausencia de `forced-colors`, `inert`, `document.title`, `@layer`, `scrollbar-color`, `dangerouslySetInnerHTML` y de `outline` distinto de `none` por `grep`; secuencia del Escape apilado por lectura de `useConfirmDialog`/`useDialogFocus`.
- **No verificado**: contraste real renderizado (el ring usa `color-mix` con transparencia; se calculo sobre el fondo plano), navegacion por teclado, Alto Contraste de Windows, lector de pantalla, INP/LCP, escalado de texto al 200 %, comportamiento real del Escape apilado, comportamiento de React 19 con `<title>` por componente, tamano de objetivos tactiles en mobile, subsetting de las fuentes Geist.
- **Pendiente**: ejecutar `legacy-scan` si lo autorizas; pasar axe/Playwright sobre login, dashboard, extractos y un modal; confirmar con el equipo que navegadores se usan para cerrar la politica.
- **Nota**: no hay validacion visual en este informe; AGENTS.md seccion 8 exige no confundir una auditoria estatica con ella.

## 5. Plan sugerido (por lotes entregables)

| Lote | Contenido | Verificacion |
|---|---|---|
| A (accesibilidad base, ~1 dia) | P1-1 foco, P1-4 titulos, P1-5 pestanas, P2-3 tablas con scroll | `npm run lint`, `test:unit`, captura en Alto Contraste, prueba de teclado en Importacion |
| B (dialogos, solo si hay fallo real) | P2-4 pila de Escape + test; piloto `ConfirmDialog` opcional | test unitario nuevo + Playwright del flujo "descartar cambios" |
| C (decision de diseno) | P1-2 bordes de control, P1-3 texto terciario, P2-2 tema por defecto | revision visual tuya en claro/oscuro |
| D (escala de texto) | P2-1 | barrido a 100/150/200 % |
| E (pulido) | P3-1, P3-2, y P3-3/P3-4 solo si hay QA visual | segun caso |

Cada lote iria con su version, bitacora y documentacion tecnica segun AGENTS.md.

## 6. Lo que NO haria

- **Migrar los ~16 modales a `<dialog>`**: funcionan y no se demostro ningun defecto (P2-4). Solo tendria sentido ante un fallo real, empezando por `ConfirmDialog`.
- **Anchor positioning / `popover` para `DatePickerField` y los dropdowns**: la guia marca Anchor positioning como "Limited availability" y `popover` como Newly available (2025-01-27); el calculo en JS actual (`DatePickerField.tsx:177-190`) funciona y ya gestiona el desbordamiento. Sin defecto, es riesgo sin retorno.
- **Sustituir el virtualizador por `content-visibility: auto`**: la guia lo reserva para "paginas grandes y complejas" y no para 50k filas editables con navegacion de celdas; perderia `aria-rowcount` correcto y el foco por celda.
- **View Transitions entre rutas**: PRODUCT.md dice "motion nunca debe frenar el trabajo repetido".
- **Passkeys, WebAuthn, trusted types**: la app es LAN con MFA propio y no inyecta HTML; coste alto, ganancia marginal ahora.
- **Reescribir tokens con `light-dark()` o `oklch`**: opcional segun la guia; el sistema actual de tokens en capas ya cumple.
- **Reorganizar el CSS en `@layer` ahora**: sin QA visual automatizado, el riesgo supera el beneficio.

## 7. Decisiones abiertas para ti

1. Subir el contraste de bordes de controles a 3:1 aunque cambie la estetica de hairline (P1-2).
2. Como llegar a 4,5:1 en texto terciario sin fundirlo con el secundario (P1-3).
3. Seguir al tema del sistema por defecto (P2-2).
4. Politica de navegadores soportados en `AGENTS.md` (condiciona todo fallback).
5. Prioridad de la escala de texto en `rem` (P2-1): depende de si hay usuarios con baja vision.

## Modern Web Guidance

Guias consultadas y aplicadas a los hallazgos: `accessibility`, `color`, `typography`, `respect-os-text-scale`, `motion`, `dark-mode`, `dialog`, `tabs`, `responsive-table`, `responsive-design`, `css`, `performance`, `forms` y `form-factor-adaptation` (las dos ultimas, parcialmente). Los estados Baseline citados proceden de las guias; donde la guia no lo indica (`<dialog>`/`showModal()`), se dice y no se afirma.

## 8. Estado de los hallazgos (2026-11-16)

| Hallazgo | Estado |
|---|---|
| P1-1 foco | Aplicado (outline + forced-colors; incluidas 9 reglas de `layout/*.css`) |
| P1-2 bordes de control | Aplicado con `--border-control` (decision tomada: 3:1) |
| P1-3 texto terciario | Aplicado: terciario #68686D, secundario #58585C |
| P1-4 titulo de pagina | Aplicado con `useDocumentTitle` |
| P1-5 pestanas | Aplicado (Paises como boton con `replace`) |
| P2-1 escala de texto | Aplicado, pendiente de validacion visual al 200 % |
| P2-2 tema | Aplicado (sigue al sistema) |
| P2-3 tablas con scroll | Aplicado en 9 wrappers |
| P2-4 dialogos | No aplicado a proposito (sin bug demostrado) |
| P3-1 movimiento | Aplicado sin reset global; skeleton, hoja movil y modal de importacion cubiertos en `system-coherence.css` |
| P3-2 scrollbar | Aplicado con `@supports not selector(::-webkit-scrollbar)` |
| P3-3 `@layer` | No aplicado a proposito |
| P3-4 container queries | No aplicado a proposito |
| 3b formularios y movil | Aplicado: importes, atributos de teclado, `fieldset`, objetivos tactiles |

Fuera del informe original: `parseStrictDecimal` y su test. Detalle de verificacion y pendientes en `DOCUMENTACION_CAMBIOS.md`.
