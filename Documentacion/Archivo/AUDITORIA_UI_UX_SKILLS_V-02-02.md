# Auditoria UI/UX por skills - V-02-02

Fecha: 2026-06-09  
Version: `V-02-02`  
Producto: Atlas Balance, app financiera local React/Vite.

## Alcance y evidencia

Esta auditoria usa todas las skills pedidas por el usuario y las adapta al registro real del producto: herramienta financiera interna, densa, sobria y orientada a tesoreria.

Evidencia revisada:

- Contexto de producto: `PRODUCT.md`, `Documentacion/Diseno/DESIGN.md`.
- Rutas React: `Atlas Balance/frontend/src/App.tsx`.
- Sistema visual: `Atlas Balance/frontend/src/styles/variables.css`, `global.css`, `layout.css` y `layout/*.css`.
- Componentes criticos: shell, sidebar, topbar, bottom nav, dashboard charts, extractos, importacion, configuracion, usuarios, backups y chat IA.
- Capturas existentes de V-02-02: `output/playwright/shell-desktop-expanded.png`, `shell-desktop-collapsed.png`, `shell-tablet.png`, `shell-mobile-sheet.png`.
- Busquedas estaticas sobre rutas, `aria-label`, charts, animaciones, overlays y textos UTF-8.

Limitacion honesta: no se hizo una nueva pasada visual interactiva sobre todas las rutas. Las capturas actuales cubren shell/dashboard, no todos los flujos financieros. Vender esto como validacion visual completa seria autoengano.

## Rutas reales de skills usadas

El documento `Documentacion/SKILLS_LOCALES.md` aun conservaba rutas antiguas bajo `Skills/Diseno/...`. En este checkout, las rutas reales usadas son:

| Skill | Ruta real usada |
|---|---|
| `critique` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/critique.md` |
| `audit` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/audit.md` |
| `layout` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/layout.md` |
| `typeset` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/typeset.md` |
| `colorize` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/colorize.md` |
| `harden` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/harden.md` |
| `polish` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/polish.md` |
| `impeccable craft` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/craft.md` |
| `adapt` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/adapt.md` |
| `animate` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/animate.md` |
| `clarify` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/clarify.md` |
| `distill` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/distill.md` |
| `quieter` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/quieter.md` |
| `bolder` | `Skills/02_Design-UI-UX/impeccable/SKILL.md` -> `plugin/skills/impeccable/reference/bolder.md` |
| `redesign-existing-projects` | `Skills/02_Design-UI-UX/taste-skill/skills/redesign-skill/SKILL.md` |
| `design-taste-frontend` | `Skills/02_Design-UI-UX/taste-skill/skills/taste-skill/SKILL.md` |
| `emil-design-eng` | `Skills/02_Design-UI-UX/emilkowalski-skill/skills/emil-design-eng/SKILL.md` |

## 1. Critique

**Aciertos**

- El producto ya tiene una direccion visual clara: tesoreria primero, numeros protagonistas, dark/light mode y CSS variables.
- La navegacion nueva agrupa areas y evita el menu plano de trece entradas.
- Hay evidencia de estados reales: `EmptyState`, `PageSkeleton`, `AppErrorBoundary`, roles, permisos, scope por pais y chat IA condicionado.

**Problemas**

- La evidencia visual no cubre todas las pantallas criticas; solo shell/dashboard tienen capturas recientes.
- En mobile, la hoja de navegacion y el boton flotante de IA compiten por la misma zona de atencion.
- El dashboard conserva sensacion de "card wall": demasiadas superficies con peso similar reducen lectura ejecutiva.
- Extractos, importacion, permisos, backups y actualizador son flujos de alto riesgo visual, pero no estan auditados con capturas actuales.
- El selector global de pais puede fallar de forma muda: el store guarda `lastError`, pero el selector no lo expone. Si falla `/paises`, el usuario puede seguir operando con un pais persistido sin entender el estado.
- La IA flotante se renderiza globalmente desde `TopBar` y usa z-index de modal; en mobile puede quedar por encima del sheet de navegacion. La captura `shell-mobile-sheet.png` lo muestra.

**Mejora**

- Crear una matriz de QA visual por `ruta x rol x pais x estado de datos` antes de ensenar a clientes. La prioridad no es mas brillo; es no descubrir delante del cliente que un modal tapa un boton de backup.

## 2. Audit

**Aciertos**

- Hay base tecnica razonable: skip link, muchos `aria-label`, `role="img"` en charts, tablas accesibles ocultas para algunos graficos, focus tokens, reduced motion y componentes comunes.
- `variables.css` concentra paleta, tipografia, radios, z-index, duraciones y sombras.
- La busqueda UTF-8 confirma que los textos fuente del frontend no estan corruptos; las salidas raras venian de lectura de consola, no de la UI.

**Problemas**

- `system-coherence.css` funciona como capa de parche transversal. Eso arregla rapido, pero tambien oculta deuda de componentes y aumenta el riesgo de regresiones.
- Hay animaciones/pulsos repartidos en shell, dashboard, entidades, revision IA y selects. Algunas son utiles; otras suben ruido en una app que el usuario repetira muchas veces.
- Falta una prueba automatizada finita que abra las rutas principales con datos demo y falle por errores de consola, overflow grave o captura vacia.
- Las tablas y overlays administrativos necesitan auditoria especifica de foco, scroll lock, escape, lectura de screen reader y estados de permiso.
- `RevisionPage` usa botones con clase activa para cambiar secciones, no semantica completa de tabs accesibles como `ConfiguracionPage`.
- Hay estilos inline entrando en pantallas como formatos de importacion. No es grave aislado, pero rompe el contrato de sistema visual si crece.
- `.auth-error` se reutiliza como error generico fuera de auth. Es deuda semantica pequena, pero en un sistema premium los nombres tambien importan.

**Mejora**

- Anadir un harness Playwright finito de visual smoke para rutas P0/P1 usando build local cerrado en el mismo comando: dashboard, cuenta detalle, extractos, importacion, revision, configuracion, usuarios, backups y mobile sheet.

## 3. Layout

**Aciertos**

- Shell desktop/tablet/mobile ya tiene arquitectura propia: sidebar, topbar, bottom nav y sheet movil.
- Las tablas financieras se tratan como producto, no como relleno.
- El layout usa tokens de spacing y grid/flex consistentes en buena parte del sistema.

**Problemas**

- En tablet, el dashboard se vuelve demasiado vertical; hay mucho scroll antes de llegar a informacion secundaria.
- En desktop, varias cards del dashboard tienen peso visual parecido, asi que la jerarquia depende demasiado del orden.
- Hay nested-card feeling en zonas de dashboard y entidades: superficie dentro de superficie dentro de superficie.
- `dashboard.css` tambien contiene estilos de detalle de cuenta/spreadsheet, mezclando responsabilidades visuales.
- Las reglas de superficie/sombra se aplican de forma amplia desde `dashboard.css` y `system-coherence.css`, reforzando la lectura de card wall.

**Mejora**

- Reorganizar dashboard y detalle de cuenta con dos niveles maximos de superficie: shell/page y modulo. Los subelementos deben usar divisores, tabla, lista o fondos sutiles, no otra card.

## 4. Typeset

**Aciertos**

- Las familias actuales encajan: `National Park`, `Hind Madurai` y `Atlas Mono`.
- Los numeros tienen `tabular-nums` y existe mono para importes/tablas.
- La escala tipografica de `DESIGN.md` esta pensada para una app densa, no para una landing.

**Problemas**

- Hay demasiados labels uppercase con tracking alto (`0.06em`, `0.08em`, `0.09em`) en dashboard/shell. En finanzas eso se vuelve ruido rapido.
- El cuerpo base de 14px y tablas de 13px son aceptables para desktop financiero, pero en tablet/mobile requieren verificacion por pantallas, no fe.
- Los ejes de charts y etiquetas largas de titulares/cuentas pueden romper legibilidad cuando hay muchos datos reales.

**Mejora**

- Reducir small-caps a metadata de verdad; usar sentence case para subtitulos y acciones. En mobile/tablet, permitir truncado con tooltip/label accesible para titulares, bancos, paths y nombres de cuenta largos.

## 5. Colorize

**Aciertos**

- La paleta es buena: azul maduro, neutros frios, estados suaves y dark mode sobrio.
- Los colores semanticos estan bien separados: positivo, negativo, warning, danger, info.
- Los charts tienen serie principal y escalas extendidas sin depender de gradientes genericos.

**Problemas**

- El color puede estar haciendo trabajo que deberia hacer la jerarquia, especialmente en badges, nav activa, charts y estados.
- Algunos charts usan verde/rojo por signo; eso es correcto para importes, pero puede mezclar "negativo financiero" con "estado de error" si no hay labels claros.
- El modo oscuro necesita validacion visual por rutas administrativas; tener tokens no garantiza contraste real en todas las combinaciones.
- La configuracion permite colores de chart configurables; si no se valida contra la paleta/contraste, puede saltarse el sistema visual.

**Mejora**

- Mantener la paleta. El trabajo no es inventar colores nuevos; es limitar acentos a accion principal, seleccion, foco y series financieras con leyenda textual.

## 6. Harden

**Aciertos**

- Existen empty states, skeletons, errores de carga, guardas de rol, permisos por scope y confirmaciones para acciones de riesgo.
- Extractos y cuenta detalle contemplan seleccion multiple, paginacion, permisos y estados sin movimientos.
- Backups/update/usuarios tienen modales de confirmacion y copy de riesgo.

**Problemas**

- Falta evidencia de overflow con datos extremos: IBAN largo, ruta de backup larga, email largo, banco largo, titular largo, cientos de columnas/movimientos, divisas con nombres largos.
- No hay prueba visual de estados offline/parciales: API lenta, permiso revocado, pais eliminado, modelo IA no disponible, backup fallido, update bloqueado.
- Los flujos de importacion y extractos son donde mas caro sale un edge case visual; todavia no tienen captura de estres.
- Deep links con pais activo pueden devolver estados genericos. Por ejemplo, un titular fuera del scope puede leerse como "no encontrado" en vez de explicar scope o permiso.

**Mejora**

- Crear fixtures visuales de datos extremos y estados fallidos. Si una app financiera solo se ve bien con datos bonitos, no esta lista para produccion.

## 7. Polish

**Aciertos**

- El shell ya evita el default browser look y se siente producto propio.
- Hay detalles de calidad: active nav, collapsible sidebar, bottom nav, focus ring, controls compartidos, selected country scope y chat IA lazy-loaded.
- `DESIGN.md` es una buena fuente de verdad visual.

**Problemas**

- El acabado no esta al mismo nivel en todas las areas: dashboard esta mas trabajado que admin/importacion/auditoria.
- Las sombras/card hover elevan demasiado algunas superficies operativas.
- La revision final de cliente no puede basarse solo en lint/build; necesita ver pantallas con datos y errores reales.

**Mejora**

- Hacer un polish pass por familias de componentes: botones, filtros, tablas, modales, charts, banners, empty states y toasts. No por pantalla aislada, porque ahora el sistema necesita coherencia.

## 8. Redesign Existing Projects

**Aciertos**

- El proyecto ya usa el stack correcto y no necesita migracion: React 18, TypeScript, Vite, CSS variables, Recharts, Zustand.
- No hay que reescribir; hay que consolidar y mejorar lo que existe.
- El rediseño anterior ya movio la app hacia un shell mas premium.

**Problemas**

- La skill trae consejos de landing/marketing que chocan con Atlas Balance si se aplican sin filtro: mas imagenes, mas asimetria, mas efectos no son automaticamente mejor.
- Hay duplicacion de patrones de card, shadow, modal y acciones entre CSS global y CSS de pantallas.
- Algunas mejoras visuales parecen parcheadas a posteriori en `system-coherence.css`.

**Mejora**

- Rediseñar por sistema, no por ocurrencia: primero tokens/componentes base, luego dashboard, luego tablas/importacion, luego admin. Nada de cambio estetico que rompa funcionalidad financiera.

## 9. Impeccable Craft

**Aciertos**

- La skill exige contexto de producto. Se creo `PRODUCT.md` para que el criterio no salga de la nada.
- El registro correcto es `product UI`, no landing ni experimento visual.
- `craft` encaja para una mejora ambiciosa si se usa como sistema: intencion, componentes, estados, responsive y QA.

**Problemas**

- Usar `craft` directamente para "hacerlo premium" sin `PRODUCT.md` seria peligroso: puede empujar hacia decoracion o dramaticidad.
- El nivel de ambicion debe respetar la tarea financiera: precision y confianza valen mas que sorpresa.

**Mejora**

- Aplicar craft como refactor visual incremental: tablero ejecutivo, spreadsheet de extractos, importacion/revision y admin de riesgo. Cada bloque debe salir con estados, responsive y validacion, no solo CSS bonito.

## 10. Design Taste Frontend

**Aciertos**

- Es util como filtro anti-UI generica: no purple AI gradient, no cards por defecto, no copy inflado, no patrones de plantilla.
- Detecta sesgos LLM que Atlas Balance debe evitar.

**Problemas**

- La propia skill dice que no es para dashboards, data tables ni multi-step product UI. Atlas Balance es exactamente eso.
- Sus defaults de landing, Tailwind, Motion o librerias externas chocan con las reglas del repo.

**Mejora**

- Usarla solo como criterio negativo: detectar slop visual. La direccion positiva la mandan `DESIGN.md`, `PRODUCT.md`, `audit`, `layout`, `typeset`, `colorize`, `harden` y `polish`.

## 11. Adapt

**Aciertos**

- Ya existe respuesta desktop/tablet/mobile: sidebar, collapsed sidebar, bottom nav y sheet.
- La app reconoce que mobile necesita otro patron de navegacion.

**Problemas**

- Mobile sheet + chat IA flotante pueden solaparse conceptualmente y visualmente.
- Dashboard en mobile/tablet apila demasiado; charts y KPIs pierden densidad util.
- Las tablas financieras no deben convertirse en cards largas sin control: seria bonito y peor.
- La navegacion no oculta siempre puertas sin permiso. Dashboard puede aparecer aunque `DashboardRoute` redirija a `/extractos`.

**Mejora**

- Definir patrones responsive por tipo de contenido: KPI strip, chart summary, spreadsheet/table, admin form y destructive modal. Mobile debe priorizar lectura y accion segura, no igualdad visual con desktop.

## 12. Animate

**Aciertos**

- Las duraciones base (`120/180/240/420ms`) son razonables.
- Hay soporte `prefers-reduced-motion`.
- Selects, date picker, modales y sheets usan motion breve con proposito.

**Problemas**

- Hay pulsos infinitos para badges/update y animaciones de entrada de cards/empty states. En una herramienta repetida, eso puede cansar.
- La app no deberia animar cambios de datos financieros de forma que parezcan decoracion o escondan una actualizacion.
- `transition` sobre muchas propiedades puede crear jank si se extiende a tablas grandes.

**Mejora**

- Reducir motion a feedback tactil y transiciones de overlay. Quitar entrada animada de cards en vistas usadas a diario y limitar pulsos a alertas realmente accionables.

## 13. Emil Design Eng

| Before | After | Why |
| --- | --- | --- |
| Puntos de alerta/update con pulso infinito | Estado estatico con cambio de color/badge y pulso solo al aparecer | La atencion constante se convierte en ruido; el usuario financiero necesita foco sostenido. |
| Cards de dashboard con entrada animada | Render inmediato o fade minimo una sola vez en cambios raros | Un dashboard se abre muchas veces; animarlo cada vez lo hace sentir mas lento. |
| Hover/active repartido por clases de pantalla | Reglas base de pressable en controles comunes | Los detalles compuestos deben sentirse igual en toda la app. |
| Chat IA flotante encima de navegacion mobile | Posicion/safe-area coordinada con bottom nav y sheet | Un overlay premium nunca compite con la navegacion principal. |
| Recharts tooltip default en `TitularSaldoBarChart` | Tooltip propio con superficie/token igual al resto de charts | El grafico no debe revelar libreria por defecto justo en el dato principal. |
| `.auth-error` usado como error generico | `.app-error`/`InlineError` semantico y reutilizable | El CSS debe nombrar intencion, no accidente historico. |

## 14. Clarify

**Aciertos**

- El copy de empty states suele ser directo y accionable.
- Los errores de permisos son comprensibles: explican que la cuenta existe pero el usuario no tiene acceso.
- Las acciones de riesgo tienen confirmacion.

**Problemas**

- Hay etiquetas genericas como `General`, `Detalle`, `Operacion local` que pueden ser correctas, pero necesitan consistencia: en una app con pais/scope, `General` podria confundirse con configuracion general.
- Botones como `Eliminar`, `Restaurar`, `Actualizar ahora`, `Revocar` necesitan siempre objeto y consecuencia visible.
- Los errores de IA/update/backups deben distinguir usuario final de operador tecnico sin filtrar diagnosticos sensibles.
- Importacion necesita distinguir mejor "sin permiso", "sin cuentas en este pais" y "sin formato". Si todo cae en un vacio parecido, el usuario no sabe que corregir.

**Mejora**

- Crear microcopy canonico para: scope por pais, permisos, importacion, backup/restore, update bloqueado, IA no disponible y acciones destructivas. Nada de "algo salio mal" en dinero.

## 15. Distill

**Aciertos**

- La navegacion ya agrupa areas por intencion.
- El dashboard intenta poner saldos, ingresos/egresos y concentracion arriba.
- Hay componentes comunes para reducir variacion.

**Problemas**

- Dashboard, titulares/cuentas y detalle de cuenta pueden repetir resumenes parecidos sin dejar claro que pregunta responde cada modulo.
- Las pantallas administrativas concentran muchas opciones en la misma vista.
- Algunas cards existen porque el sistema visual las hace faciles, no porque la informacion necesite elevacion.

**Mejora**

- Reducir cada pantalla a una pregunta principal y dos secundarias. Todo lo demas debe ir a tabla, tab, disclosure o accion contextual.

## 16. Quieter

**Aciertos**

- La paleta base ya es quieta.
- El producto evita gradientes heroicos, ilustraciones y ruido de landing.
- Dark mode es sobrio y compatible con operacion prolongada.

**Problemas**

- Los pulsos, sombras hover y cards elevadas suben el volumen visual mas de lo necesario.
- Los badges de nav y el boton IA pueden competir con los datos financieros.
- Demasiadas superficies suaves pueden hacer que todo parezca importante.

**Mejora**

- Bajar intensidad del chrome: menos sombras, menos animacion, menos badges insistentes. Reservar peso visual para saldos, riesgo, permisos y acciones irreversibles.

## 17. Bolder

**Aciertos**

- La app ya tiene presencia propia: no parece una plantilla blanca sin criterio.
- La tipografia y paleta pueden soportar una jerarquia mas fuerte sin cambiar stack.

**Problemas**

- Algunos numeros principales no dominan lo suficiente frente al contenedor.
- En pantallas densas, la presencia se reparte entre demasiados modulos; ningun punto manda.
- "Bolder" mal aplicado se convertiria en una app financiera chillona. Eso seria una mala idea con mejores sombras.

**Mejora**

- Ser bold donde importa: KPI principal, variacion/riesgo, accion primaria, warning real, titulo de tarea y seleccion de scope. Ser quieto en todo lo demas.
