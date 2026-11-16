# Informe Impeccable (critique + audit) - frontend de Atlas Balance (V-03.02)

⚠️ DEGRADED: single-context (el arnes no permite lanzar subagentes sin que lo pidas; Assessment A y B corrieron en el mismo contexto, y B se limita a lectura estatica).

Fecha: 2026-11-16. Rama `V-03.02`. Solo lectura: no se toco codigo de la app.
Modo de la superficie: **Operate** (app financiera de uso repetido). Skill usada: Impeccable 4.9.2 (`critique`, `audit`, `critique-rubric`), leida desde `Documents\Herramentas\Impecable Design`.

## 0. Que es y que no es este informe

- **Es** una critica de diseno y una auditoria tecnica **estatica**, con puntuaciones **provisionales**.
- **No hay captura renderizada.** La app no estaba levantada (5173/5000 sin respuesta; 443 contesta http.sys 404) y AGENTS.md sec. 8 prohibe levantar el stack "para mirar rapido". Los `.png` de `Documentacion/Diseno/Diseño` pesan menos de 3 KB: no sirven como evidencia visual.
- **No se ejecuto el detector `impeccable detect`** ni `impeccable context`: el launcher descarga un binario en la primera ejecucion, lo que choca con AGENTS.md sec. 7 y 9. Se puede hacer si lo autorizas (pregunta 3 del final).
- **No repite lo ya hecho hoy.** Existe `INFORME_MODERN_WEB_GUIDANCE_V-03.02.md` (accesibilidad/web moderna), y su sec. 8 marca como aplicados foco, bordes de control 3:1, texto terciario, titulos de pagina, pestanas, tablas con scroll y reduced motion. Esas lineas estan **en curso** (staged, sin validacion visual) y aqui no se reabren. Cada hallazgo de abajo lleva su estado: **NUEVO**, **ABIERTO** (ya citado en `AUDITORIA_UI_UX_GLOBAL_V-02-02.md` y sigue sin cerrar en el codigo) o **EN CURSO**.
- Respeta `PRODUCT.md` y `Documentacion/Diseno/DESIGN.md`. Nada de Tailwind ni dependencias nuevas.

## 1. Veredicto de especificidad de diseno

**Mixto, y la mezcla es el problema.** Lo que es propio de Atlas Balance y funciona: cobalto de tesoreria como unico acento, violeta reservado al canal IA, importes en mono y con signo (`--amount-positive/negative`), paleta de estados con paridad claro/oscuro, Geist autoalojado por CSP, agrupacion de navegacion Operacion/Control/Sistema, paleta de comandos `Ctrl K`.

Lo que viene de otro producto: **la escala tipografica y de medidas es la del sitio de marketing de Apple** (cuerpo 17 px, `--text-56` para display, controles de 44 px en pastilla, tracking de cuerpo `-0.022em`, `--tile-*` oscuros alternados). DESIGN.md (sec. 3.2, 3.3 y 8) lo fija como decision explicita ("cuerpo a 17 px; 14 px solo en celdas densas", fila 56 / 44 compacta). Funciona para una pagina que se lee; en una consola que se opera 8 horas al dia tira contra el principio 3 de PRODUCT.md, **"Denso, no apretado"**. Ojo: esto **desafia la especificacion**, no es un desvio del codigo (ver F3).

Deterministico (detector): **no ejecutado**. Lo que si se midio con `grep`/Node esta en las secciones 3 y 4.

## 2. Puntuaciones (provisionales, solo codigo)

### Salud de diseno (Nielsen, /40)

| # | Heuristica | Nota | Hallazgo clave |
|---|---|---|---|
| 1 | Estado del sistema | 3 | Buen `role="status"`/`alert` en celdas editables y toasts; el Dashboard no distingue cargando/fallo parcial (F2) |
| 2 | Lenguaje del usuario | 3 | Copy en espanol claro; cada pagina tiene subtitulo explicativo (Revision, Conciliacion). Sin hallazgos |
| 3 | Control y libertad | 3 | Confirmaciones con `useConfirmDialog`, Escape en modales; papelera existe |
| 4 | Consistencia | 2 | 4 clases de backdrop de modal, dos sistemas de tokens conviviendo, capa `system-coherence.css` correctiva (F5) |
| 5 | Prevencion de errores | 3 | Confirmaciones y descarte de cambios; falta evidencia visual de jerarquia de riesgo en admin (F9) |
| 6 | Reconocimiento | 3 | Navegacion con etiquetas y grupos; filtros en URL en Dashboard/Extractos/Importacion |
| 7 | Flexibilidad y eficiencia | 3 | Paleta `Ctrl K`, grid con teclado y filas de 44 px en extractos; sin eleccion de densidad por usuario (F3) |
| 8 | Estetica minimalista | 2 | Dashboard de ~9 superficies con peso parecido; reloj en sidebar (F4, F10) |
| 9 | Recuperacion de errores | 2 | Dashboard: un error en 1 de 3 consultas lo vacia todo, sin reintento (F2) |
| 10 | Ayuda | 2 | Sin ayuda contextual; textos de apoyo solo en algunos formularios |
| | **Total** | **26/40** | **Aceptable (65 %)** |

**Carga cognitiva (checklist de la rubrica, 8 puntos):** 3 fallos de 8 -> **moderada**. Falla *jerarquia visual* (Dashboard con ~9 superficies de peso parecido, F4), *una cosa a la vez* (hero + 4 KPI + 5 bloques en una pantalla) y *chunking* (mas de 4 elementos por grupo en la zona de KPI y divisas). El resto no muestra fallo en codigo.

Sin render, un +/-3 es razonable. Ninguna nota 4 sin ver la pantalla.

### Salud tecnica (audit, /20)

| # | Dimension | Nota | Hallazgo clave |
|---|---|---|---|
| 1 | Accesibilidad | 3 | Base solida y en mejora hoy; boton primario oscuro a 4,43:1 en reposo y 3,36:1 al pulsar (F1) |
| 2 | Rendimiento | 3 | 22 rutas `lazy`, `manualChunks`, grid virtualizado; CSS de IA de 2.044 lineas siempre en el bundle |
| 3 | Theming | 3 | Tokens en capas y paridad real; 282 usos de nombres legacy frente a 683 de los nuevos |
| 4 | Responsive | 3 | Muchos `@media` (~20 umbrales distintos), bottom nav y sheet; tablas sin patron movil unico |
| 5 | Integridad de implementacion | 2 | `system-coherence.css` y ~40 `!important` (16 de los 21 de `global.css` son la utilidad `.sr-only`, legitima) delatan componentes que no nacieron coherentes |
| | **Total** | **14/20** | **Bueno, en el limite con Aceptable** |

## 3. Hallazgos

Formato: prioridad, estado, evidencia, impacto, correccion, comando Impeccable sugerido.

### P1

**F2. [P1 · NUEVO] Dashboard: un fallo parcial lo borra entero y confunde "error" con "vacio".**
- Evidencia: `DashboardPage.tsx:202-213`. Tres consultas independientes (`principal`, `evolucion`, `saldosDivisa`) se resumen en un solo `loading`/`error`. Si una falla (o `evolucion` llega nula), se renderiza `.page-placeholder` con solo `<h1>` y texto; sin boton de reintento. El mismo texto de reserva dice "Carga cuentas o extractos para ver saldos", que es un estado *vacio*, y aparece tambien cuando es un error de red. Mismo patron confirmado en `DashboardTitularPage.tsx:166-172`. `CuentaDetailPage.tsx` tambien usa `.page-placeholder`, pero no se ha leido su rama.
- Impacto: viola el principio 5 de PRODUCT.md ("vacio, carga, error, permisos, obsoleto, parcial explicitos"). Para un director financiero, "no veo saldos" sin saber si es fallo, permisos o ausencia de datos es una pantalla que quita confianza. Ademas ya existe un `EmptyState` con variantes `error`/`permission`/`setup` que aqui no se usa.
- Correccion: renderizar cada bloque con su propio estado (esqueleto por tarjeta, error por tarjeta con "Reintentar" que llama a `refetch`), mantener la barra de periodo/divisa visible siempre, y usar `EmptyState variant="setup"` solo cuando de verdad no hay cuentas. Mostrar "Actualizado hace X" para el estado obsoleto.
- Comando: `/impeccable harden`.

### P2

**F1. [P2 · NUEVO] Boton primario en tema oscuro: texto casi negro sobre cobalto-500, 4,43:1 en reposo y 3,36:1 al pulsar.**
- Evidencia: `.button-primary` (`global.css:574-587`) usa `background: var(--accent-primary)` y `color: var(--text-inverse)`. En oscuro `--accent-primary` resuelve a `--accent-solid` = `--accent-500` #3A72E6 (`themes.css:11`) y `--text-inverse` a `--gray-950` #0B0B0C (`themes.css:8`). Calculo WCAG en Node: reposo **4,43:1** (a 0,07 de AA), hover `--accent-400` **6,00:1** (cumple), pulsado `--accent-600` **3,36:1** (momentaneo, con `scale(.95)`). En claro: blanco sobre `--accent-600` 5,85:1.
- Nota: `themes.css:11` declara `--accent-on-solid: var(--white)` pero `.button-primary` no lo consume; hay dos intenciones distintas (blanco en el token, casi negro en el boton). No se ha visto como pinta el boton `auth.css` ni el `Button` compartido.
- Impacto: bajo-medio. Es el CTA principal y solo en oscuro, y el fallo es marginal: por eso P2, no P1.
- Correccion: en oscuro subir un peldano: reposo `--accent-400` (6,00:1 con texto oscuro), hover `--accent-300`, pulsado `--accent-500` manteniendo el `scale`. Antes de tocar, comprobar en render que `auth.css` y `Button` siguen el mismo par texto/fondo.
- Comando: `/impeccable colorize` (acotado a tokens oscuros) y `/impeccable audit` para recomprobar.

**F4. [P2 · ABIERTO desde V-02-02] Dashboard: "muro de tarjetas" con peso parecido.**
- Evidencia: `DashboardPage.tsx` encadena hero, 4 `KpiCard`, "Saldos por titular", "Plazos fijos", "Saldos por pais" y "Concentracion", todas `.dashboard-card` de 18 px de radio. La auditoria V-02-02 ya lo marco; solo el hero tiene jerarquia propia. Lo contrario a "el dinero y el riesgo mandan".
- Correccion: dos niveles. Nivel 1: saldo consolidado + disponible + inmovilizado (grandes, mono, con delta). Nivel 2: el resto sin tarjeta contenedora, como secciones con regla fina y tabla corta. Pasar "Plazos fijos" a una alerta contextual cuando el vencimiento sea <= 7 dias (ya existe el calculo, `dias_hasta_proximo_vencimiento`). Pregunta clave: que decision toma el usuario en los primeros 5 segundos? Hoy no esta explicito.
- Comando: `/impeccable distill` + `/impeccable layout`.

**F5. [P2 · ABIERTO] Dos sistemas de tokens y una capa correctiva.**
- Evidencia: `variables.css` es una capa de alias con nombres legacy (`--bg-*`, `--color-*`, `--accent-primary`, 282 usos de `--color-`/`--accent-primary`) sobre los tokens nuevos (683 usos). Cuatro clases de backdrop de modal (`modal-backdrop` x15, `users-modal-backdrop` x7, `config-modal-backdrop` x5, `import-modal-backdrop` x3). `system-coherence.css` (581 lineas) y ~40 `!important`. Hay un `!important` sobre `color`/`background` de estados activos en `global.css:362-369`.
- Impacto: cada pantalla nueva decide a mano su variante; la deuda se paga en regresiones visuales (el informe V-02-02 ya lo predijo).
- Correccion: un componente `Modal` unico (cabecera, cuerpo, pie, tamanos `sm/md/lg`) que absorba las cuatro clases, **sin** migrar a `<dialog>` (el informe de hoy ya lo descarta con motivo); migrar nombres legacy por tandas con un script de sustitucion verificado por captura; mover lo de `system-coherence.css` al sitio de origen cuando se toque cada pantalla.
- Comando: `/impeccable extract`.

**F6. [P2 · NUEVO] Tablas: semantica y patron movil heterogeneos.**
- Evidencia: 173 `<th>` y solo 4 `scope="col"`. Una de las tablas (`DesgloseModal.tsx:187`) usa `role="table"` sobre `div`. Mobile: varias paginas reutilizan `users-table-card`; la busqueda de `data-label`/tarjetas no hallo un patron unico, pero no se revisaron los `@media (max-width)` de cada hoja: **a confirmar en render** (PRODUCT.md principio 6).
- Correccion: `scope="col"` en cabeceras (tarea mecanica y segura); si el render confirma que falta, definir para Alertas, Revision y Exportaciones una variante movil de fila apilada con importe y estado arriba y acciones en menu. El scroll horizontal de los informes anchos puede quedarse, ya con foco por teclado (EN CURSO).
- Comando: `/impeccable adapt`.

**F7. [P3 · ABIERTO] Graficos: color como portador del signo y tooltip por defecto.**
- Evidencia (Node): `--chart-3` #E0761A 3,10:1; `--chart-2` #0F8B8D 4,12:1; `--chart-10` #6B8F1E 3,77:1. Para marcas no textuales basta 3:1, asi que **cumple**, pero las barras de saldo positivo/negativo dependen del color; el signo ya va en el valor del tooltip, pero conviene confirmarlo en render. No se localizo un grafico que combine verde y carmesi como series. `TitularSaldoBarChart.tsx:50` usa el `Tooltip` de Recharts con `formatter`, no uno propio (ya citado en V-02-02); las barras usan `--color-success`/`--color-danger` para el signo.
- Correccion: no depender del color como unico portador: etiquetas de valor o patron/forma, leyenda interactiva, mismo tooltip propio en todos los graficos. Mantener la tabla `sr-only` (ya existe en `EvolucionChart` y `TitularSaldoBarChart`).
- Comando: `/impeccable polish` sobre `components/dashboard`.

**F3. [P2 · NUEVO, decision de producto] Densidad: la escala esta calibrada para leer y no hay eleccion por usuario.**
- Evidencia: `ExtractoTable.tsx:350` aplica siempre `extracto-table-section--compact`, que fija filas de **44 px** (`extractos.css:742-743`): lo que DESIGN.md sec. 3.3 y 5.3 piden ("fila 56 / 44 compacta"). El 56 px de `extractos.css:117` es solo el valor por defecto. El grid **cumple la especificacion**. Lo que queda: cuerpo `--font-size-base` 17 px (`variables.css:150`), tracking negativo `-0.022em` en `body` (`global.css:35`), `h1` global a 40 px (`global.css:59`; falta ver cuantas paginas lo sobrescriben) y ninguna opcion de densidad por usuario (`--row-height-compact` esta definido sin selector).
- Impacto: en 1080p caben unas 18 filas a 44 px frente a ~25-30 a 32-36 px. Para quien concilia miles de lineas es la palanca de eficiencia mas grande que queda, pero **desafia DESIGN.md**: no es un bug.
- Opciones: (a) dejarlo; (b) selector Comodo/Compacto en `uiStore` con compacto = filas 36 px solo en el grid; (c) bajar tracking y h1 en pantallas de trabajo. Pregunta 1.
- Comando: `/impeccable layout` + `/impeccable typeset` (solo si eliges b o c).

**F9. [P2 · ABIERTO] Jerarquia de riesgo en acciones administrativas.**
- Evidencia: V-02-02 P2.4. Sin render no se puede confirmar si hoy "restaurar copia", "borrar definitivo" y "cambiar permiso" se distinguen por forma ademas de color.
- Correccion: tres niveles estables (normal / reversible con deshacer / irreversible con confirmacion escrita), un unico tratamiento visual por nivel, y el nombre de la entidad en el boton ("Eliminar cuenta ES91..."). Color nunca como unico portador.
- Comando: `/impeccable harden` + `/impeccable clarify`.

### P3

**F10. [P3 · NUEVO] Ruido decorativo en el chrome de trabajo.**
- Reloj en el sidebar que se refresca cada 30 s (`Sidebar.tsx:153`): el SO ya lo muestra y compite con la etiqueta de version. 28 `@keyframes` (sin juzgar su peso) y animaciones `infinite` de la cara IA (`revision-ai.css:352-1449`): correctas bajo `no-preference`, pero el widget IA flotante anima aunque este cerrado? Comprobar con render. 33 declaraciones `text-transform: uppercase` frente a `--tracking-caps` de `0.08em`: limitarlas a metadatos cortos.
- Comando: `/impeccable quieter`.

**F11. [P3 · NUEVO] CSS de IA de 2.044 lineas en el bundle global.**
- `revision-ai.css` es la mayor hoja y mezcla Revision, IA y el widget. Dividirla por ruta/lazy si el peso de CSS inicial importa (medir con `vite build` antes de decidir).
- Comando: `/impeccable optimize`.

## 4. Lo que funciona (conservar)

- Tokens en capas con paridad total claro/oscuro, estados semanticos y canal IA separado del acento.
- Importes mono, con signo y `tabular-nums` global.
- Grid de extractos virtualizado con `aria-rowcount`/`aria-sort`, edicion por celda con estado `Guardando/Guardado/Error`.
- Paleta de comandos, skip link, tabla `sr-only` como alternativa de graficos, `lang="es"`, fuentes autoalojadas con `preload`, CSP coherente.
- Navegacion agrupada y ocultacion por rol/permiso; filtros en la URL.
- No se hallaron botones solo-icono sin nombre accesible (busqueda multilinea: 0 casos), ni `window.confirm/alert`, ni `dangerouslySetInnerHTML`.

## 5. Personas (recorrido sobre codigo)

- **Alex (analista que concilia a diario):** filas de 44 px (spec), sin eleccion de densidad, cuerpo de 17 px y h1 global de 40 px que se comen altura. Riesgo de abandono bajo-medio (F3).
- **Sam (teclado y lector):** buen terreno (skip link, foco, `aria-*`). Pendiente de validar en vivo lo aplicado hoy; en oscuro el CTA queda a 4,43:1 (F1); 169 `<th>` sin `scope` (F6).
- **Responsable de tesoreria (de PRODUCT.md):** abre el Dashboard para saber "cuanto hay disponible y que esta en riesgo". Si una consulta falla ve una pagina casi vacia sin saber por que (F2), y el riesgo (vencimientos, inmovilizado) comparte peso con tarjetas informativas (F4).

## 6. Plan sugerido (lotes, cada uno con version, bitacora y captura)

| Lote | Contenido | Comando | Verificacion |
|---|---|---|---|
| 0 | Capturas reales: login, dashboard, extractos, importacion, configuracion, modal, claro/oscuro, 390 px y 1440 px | `/impeccable audit` | Playwright con datos demo + `impeccable detect` si lo autorizas |
| 1 | F2 (estados por tarjeta + reintento), F1 (tokens oscuros) | `harden`, `colorize` | tests de render con mocks, captura en oscuro |
| 2 | F3 (densidad, solo si decides) | `layout`, `typeset` | decision de producto antes; captura comodo/compacto |
| 3 | F4 (jerarquia del dashboard) | `distill`, `layout` | captura antes/despues |
| 4 | F5 (`Modal` unico, tokens legacy), F6 (tablas y movil) | `extract`, `adapt` | `tsc`, lint, capturas por pantalla afectada |
| 5 | F9, F7, F10, F11 | `harden`, `polish`, `quieter`, `optimize` | segun caso |
| Final | pasada global | `polish` | `audit` de nuevo para medir la mejora |

## 7. Lo que NO haria

- No cambiar de estilo ni de acento: la identidad cobalto + mono + violeta IA esta bien. No es un rediseno.
- No meter `bolder`/`overdrive`/`delight`: PRODUCT.md es explicito (premium sin ruido, calma bajo presion).
- No migrar a `<dialog>`, `@layer` ni container queries ahora (ya descartado hoy con motivo, y sin QA visual automatizado el riesgo supera el beneficio).
- No quitar la capa de compatibilidad de `variables.css` de golpe.

## 8. Verificado, bloqueado y pendiente

- **Verificado (estatico):** estructura de tokens y temas; contrastes calculados en Node con la formula WCAG 2.x sobre los hex de `colors.css`/`themes.css`; `grep` de `th`/`scope`, backdrops, keyframes, `outline`, uppercase, `aria-*`; lectura de `DashboardPage.tsx`, `KpiCard`, `EmptyState`, `Sidebar`, `navigation.ts`; ausencia de botones solo-icono sin nombre (regex multilinea).
- **Bloqueado:** captura renderizada (app sin levantar; AGENTS.md sec. 8), `impeccable detect` (descarga de binario; AGENTS.md sec. 7 y 9), subagentes (el arnes no los permite sin peticion expresa).
- **Pendiente:** todas las puntuaciones se confirman con Lote 0; densidad real del grid, jerarquia visual del dashboard, jerarquia de riesgo en admin y comportamiento movil no se han visto; el efecto de las correcciones de hoy (foco, bordes, texto, escala) sin validar visualmente.

## 9. Preguntas abiertas

1. **Densidad (F3):** DESIGN.md fija 17 px de cuerpo y filas de 44 px. Quieres (a) mantenerlo, (b) anadir un selector Comodo/Compacto solo para el grid de extractos, o (c) bajar tambien cuerpo y h1 en las pantallas de trabajo?
2. **Por donde empezar:** (a) Dashboard (F2 estados por tarjeta + F4 jerarquia), (b) tokens del boton oscuro (F1) y `Modal` unico (F5), o (c) primero capturas reales (Lote 0).
3. **Evidencia visual:** autorizas (a) levantar la app con datos demo para capturas y pasar `impeccable detect`, (b) solo capturas con Playwright sobre un build, o (c) seguir solo con analisis estatico?


## 10. Estado de los hallazgos tras la correccion (2026-11-16)

| Hallazgo | Estado | Notas |
|---|---|---|
| F1 boton primario oscuro | Aplicado y medido | 6,00 / 8,57 / 6,00 :1 en `.button-primary` y `.auth-button` (medido en navegador con texto rgb(11,11,12)) |
| F2 Dashboard por bloques | Aplicado y comprobado | Error en `principal` y en `saldos-divisa`: un bloque de error con "Reintentar", barra de periodo/divisa visible; estado `setup` sin cuentas; sin Reintentar espurios |
| F3 densidad | Aplicado y comprobado | Comodo 44 px (defecto), Compacto 36 px, `data-density` en `<html>`; scroll virtualizado sin huecos ni solapes (delta < 0,75 px) |
| F4 jerarquia Dashboard | Aplicado | Hero + secciones planas; revisado en capturas 1440 y 390 |
| F5 Modal unico | Parcial | Backdrop unico; `Modal.tsx` migra 5 de 13 (ConfirmDialog, AuditCellModal, DesgloseModal, CreateTokenModal, TokenCreatedModal); 8 con logica de cierre propia sin migrar; 95 alias legacy migrados (32/36 capturas identicas, 4 no deterministas) |
| F6 tablas | Aplicado | `scope="col"` en 150 cabeceras; tablas apiladas en movil conservan la semantica (misma cuenta de table/row/cell/columnheader a 1440 y 390 segun el arbol de accesibilidad de Chromium); Backups/Usuarios sin desborde |
| F7 graficos | Aplicado | Tooltip propio con signo; eje X sin meses repetidos |
| F8 | Retirado | Era un hallazgo inexacto (cada pagina ya tiene subtitulo) |
| F9 riesgo | Aplicado y comprobado | Tres niveles; el texto "no se puede deshacer" solo en irreversible; Escape, foco y clic en fondo verificados; **defecto previo hallado y corregido:** doble confirmacion al Revocar/Eliminar token (TokenList + `ConfiguracionPage`), ya solo confirma TokenList |
| F10 ruido | Parcial | Reloj fuera; 6 de 34 `uppercase` quitados; sin barrido de keyframes (28 definidos, sin evidencia) |
| F11 CSS de IA | No aplicado | ~8 KB gzip de ganancia frente al riesgo de orden con `system-coherence.css` y `IconAiFace` global |

**Pendiente:** `AuditCellModal` sin test propio, dialogos reversibles de Titulares/Importacion y dialogos a 390 px (solo el irreversible), temas oscuros de dialogos y modales, comportamiento con backend real, y el Escape apilado de Desglose (funciona pero depende del orden de `setState` en dos listeners de `window` en `useDialogFocus`). Arnes: `frontend/e2e/visual/capture.spec.ts` y `states.spec.ts` (el test 1g queda en `fixme` hasta tener un mock con latencia).
