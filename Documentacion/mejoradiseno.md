# Informe de mejora UI/UX - Atlas Balance

## Ejecucion aplicada - 2026-04-25 - Coherencia entre pantallas

### Skills y criterio aplicado

| Skill local | Uso aplicado |
| --- | --- |
| `Skills/Diseno/taste-skill/.agents/skills/redesign-existing-projects/SKILL.md` | Auditar patrones genericos y corregir sin reescribir la app. |
| `Skills/Diseno/taste-skill/.agents/skills/design-taste-frontend/SKILL.md` | Mantener direccion sobria de producto financiero, sin gradientes chillones ni componentes de demo. |
| `Skills/Diseno/ui-ux-pro-max-skill/.agents/skills/ckm-design-system/SKILL.md` | Ordenar tokens de controles, superficies, foco, sombras y estados. |
| `Skills/Diseno/ui-ux-pro-max-skill/.agents/skills/ckm-ui-styling/SKILL.md` | Alinear accesibilidad, dark mode, formularios y navegacion con CSS variables propias. |
| `Skills/Diseno/impeccable/source/skills/polish/SKILL.md` | Pasada final de radios, espaciado, hover/focus, tabs, tablas y login. |

### Que se corrigio

- Se agregaron tokens compartidos para controles, superficies, sombras, foco y estados hover.
- Los tokens shadcn/Tailwind ahora apuntan al sistema visual propio; dos sistemas de color compitiendo era mala arquitectura visual.
- `Button` compartido usa alturas, radios, pesos y variantes coherentes con los botones clasicos de la app.
- Login usa la misma logica de superficie, borde, foco y boton primario que el resto de la aplicacion.
- Tabs de configuracion pasan a comportarse como control segmentado, no como una fila de botones sueltos.
- Cards, modales, tablas, filtros, estados hover y navegacion reciben una capa comun de coherencia.
- Las filas marcadas dejan el indicador lateral fuerte y usan tratamiento completo de fila. Mejor, menos "admin template de 2017".

### Verificacion visual

- `output/playwright/ui-login-desktop.png`
- `output/playwright/ui-login-mobile.png`

### Pendientes reales

- Validar pantallas internas con datos reales y una sesion activa: dashboard, cuentas, extractos, importacion y configuracion.
- Crear una pagina interna de referencia visual si se siguen sumando componentes nuevos.

### Refactor aplicado despues

El pendiente de separar `layout.css` se cerro el 2026-04-25:

- `layout.css` queda como indice de imports.
- `styles/layout/shell.css`: shell, sidebar, topbar, bottom nav y estados comunes de layout.
- `styles/layout/users.css`: usuarios, permisos y modales.
- `styles/layout/extractos.css`: filtros, formulario y tabla virtualizada.
- `styles/layout/entities.css`: titulares, cuentas y detalle de cuenta.
- `styles/layout/dashboard.css`: KPIs, saldos, charts y tablas de dashboard.
- `styles/layout/importacion.css`: wizard, preview, validacion y modal.
- `styles/layout/admin.css`: alertas, configuracion, auditoria, backups y exportaciones.
- `styles/layout/system-coherence.css`: capa comun de coherencia visual al final de la cascada.

## Ejecucion aplicada - 2026-04-19

### Skills aplicadas conjuntamente

| Skill local | Uso aplicado |
| --- | --- |
| `Diseno\emilkowalski-skill\skills\emil-design-eng\SKILL.md` | Motion util, feedback tactil, foco visible, transiciones cortas y nada de animacion decorativa que estorbe el trabajo diario. |
| `Diseno\impeccable\.codex\skills\impeccable\SKILL.md` | Direccion visual anti-generica: dashboard financiero sobrio, sin dependencia externa de fuentes, sin "AI purple", sin ruido gratuito. |
| `Diseno\taste-skill\skills\taste-skill\SKILL.md` | Criterio de producto denso y profesional: numeros tabulares, layouts robustos, touch targets mas sanos y jerarquia por escala/espacio. |
| `Diseno\ui-ux-pro-max-skill\.agents\skills\ui-ux-pro-max\SKILL.md` | Prioridad a accesibilidad, contraste AA, controles de 44px, responsive, estados de carga/vacio/error y navegacion clara. |

### Veredicto actualizado

La app ya tenia una base visual mucho mejor que una plantilla cutre, pero tenia tres fallos que iban a morder en produccion: contraste insuficiente en el boton primario claro, dependencia externa de Google Fonts en una app on-premise, y patrones interactivos que funcionaban pero no comunicaban suficiente estado.

Eso ya quedo corregido en la base. No he fingido un redisenio total: seria una burrada tocar 20 pantallas grandes en una sola pasada sin snapshots visuales por pantalla. Lo correcto fue arreglar el sistema comun para que el beneficio caiga sobre toda la app.

### Correcciones ejecutadas

| Area | Antes | Despues | Impacto |
| --- | --- | --- | --- |
| Contraste primario | `--accent-primary #4c7dff` con texto claro daba `3.69:1`. | `--accent-primary #285bd9` con texto claro da `5.64:1`. | Botones primarios pasan AA en modo claro. |
| Texto muted | `--text-muted #6f7b8d` daba ratios cercanos a `4.1:1`. | `--text-muted #5f6b7a` da `5.18:1` sobre canvas claro y `4.69:1` sobre superficie muted. | Metadatos y textos auxiliares dejan de parecer lavados. |
| Fuentes | `index.html` cargaba Google Fonts. | Fuentes locales OFL en `frontend/public/fonts`: `National Park` y `Atlas Mono`. | La app on-premise deja de depender de internet para verse bien. |
| Tipografia de datos | Fallback parcial a fuentes del sistema si Google no cargaba. | `Atlas Mono` local para importes y columnas numericas. | Numeros mas estables, menos salto visual. |
| Foco global | Foco repartido por selectores. | `:focus-visible` global con token de foco. | Mejor teclado y accesibilidad sin perseguir componentes uno por uno. |
| Selectores | Popover funcional pero plano; semantica mejorable. | `AppSelect` usa `role="combobox"`, `aria-controls`, `aria-activedescendant`, `aria-labelledby`, `Home/End`, animacion `transform+opacity`. | Selectores mas claros para teclado y lector de pantalla. |
| Menu movil | Sheet sin `dialog` ni cierre por Escape. | `role="dialog"`, `aria-modal`, cierre por Escape, scroll interno y animacion corta. | Menos friccion en mobile y mejor accesibilidad. |
| Estados vacios | Varios estados planos tipo "Sin filas". | `EmptyState` con `role`, fondo intencional y uso en tabla de extractos. | Estados vacios explican accion recuperable. |
| Loading | Skeleton correcto pero algo lento y generico. | Shimmer mas rapido y tintado con acento; `PageSkeleton` tiene `aria-label`. | Carga percibida mas rapida y accesible. |
| Extractos | Botones de filtros/columnas con `aria-pressed`; loading/vacio textual. | `aria-expanded`, paneles enlazados, skeleton y empty state util. | La pantalla mas critica queda mas robusta. |
| Touch targets | Algunas acciones densas en `2rem`/`2.25rem`. | Se suben acciones criticas de tabla/cards a `2.5rem`. | Menos mis-taps en tablet/mobile sin reventar densidad. |
| Motion | Selects y sheet aparecian bruscos. | Entrada corta con `transform`/`opacity`; `prefers-reduced-motion` ya lo cubre. | Feedback sin convertir tesoreria en feria. |
| Build servido por backend | `frontend/dist` podia divergir de `wwwroot`. | `dist` reconstruido y copiado a `backend/src/AtlasBalance.API/wwwroot`. | Backend sirve la UI corregida. |

### Contraste verificado

| Par | Ratio anterior | Ratio actual | Estado |
| --- | ---: | ---: | --- |
| Primario claro sobre texto claro | `3.69:1` | `5.64:1` | Corregido |
| Hover primario claro sobre texto claro | `4.44:1` | `7.49:1` | Corregido |
| Link claro sobre superficie | `5.72:1` | `6.99:1` | Mejorado |
| Texto secundario claro sobre superficie | `5.43:1` | `6.14:1` | Mejorado |
| Texto muted claro sobre canvas | `4.10:1` | `5.18:1` | Corregido |
| Texto muted sobre superficie muted | `3.85:1` aproximado | `4.69:1` | Corregido |
| Primario oscuro | `14.42:1` | `14.42:1` | Correcto |
| Secundario oscuro | `8.77:1` | `8.77:1` | Correcto |

### Archivos modificados en esta pasada

| Archivo | Cambio |
| --- | --- |
| `frontend/index.html` | Eliminada carga de Google Fonts y actualizado `theme-color` claro. |
| `frontend/public/fonts/*` | Agregadas fuentes locales OFL para interfaz y numeros. |
| `frontend/src/styles/variables.css` | Nuevos `@font-face`, ajuste de paleta clara, contraste, aliases semanticos de spacing y familias tipograficas. |
| `frontend/src/styles/global.css` | Foco global, link underline offset, botones mas claros, selectores con mejor borde, animacion y estados. |
| `frontend/src/styles/layout.css` | Empty states, skeletons, page enter, bottom sheet, touch targets y tabla de extractos. |
| `frontend/src/components/common/AppSelect.tsx` | Semantica combobox/listbox y navegacion Home/End. |
| `frontend/src/components/common/EmptyState.tsx` | Roles accesibles para estados vacios/error. |
| `frontend/src/components/common/PageSkeleton.tsx` | `aria-label` de carga. |
| `frontend/src/components/layout/BottomNav.tsx` | Sheet como dialog modal y cierre con Escape. |
| `frontend/src/components/extractos/ExtractoTable.tsx` | Paneles enlazados, skeleton, empty state y microcopy "Historial". |
| `frontend/dist/` | Build regenerado. |
| `backend/src/AtlasBalance.API/wwwroot/` | Build sincronizado para servir la UI corregida. |

### Verificacion ejecutada

| Comando | Resultado |
| --- | --- |
| `npm.cmd run lint` | OK |
| `npm.cmd run build` | OK |
| Script local de contraste WCAG | OK en los pares corregidos principales |
| Copia de `frontend/dist` a `backend/src/AtlasBalance.API/wwwroot` | OK |

No ejecute `npm run test:e2e` porque exige `E2E_ADMIN_PASSWORD` y el propio README avisa que adivinar credenciales puede bloquear el admin por rate limit. Adivinar passwords en una app con bloqueo de login seria hacer el payaso.

### Pendientes reales

| Prioridad | Pendiente | Por que no se resolvio en esta pasada |
| --- | --- | --- |
| P1 | Auditoria visual con navegador en desktop/tablet/mobile y ambos temas. | Requiere sesion real o mocks de API para validar pantallas protegidas sin tocar rate limits. |
| P1 | Revisar contraste de charts con datos reales y fondos por tema. | Hay tokens mejores, pero Recharts necesita validacion visual con series reales. |
| P1 | Dividir `layout.css` en modulos. | Es una refactorizacion amplia; hacerla junto al redisenio meteria riesgo gratis. |
| P2 | Unificar todos los textos mojibake tipo `sesión` si existen en fuente. | Es deuda de encoding; conviene una pasada controlada para no romper strings. |
| P2 | Convertir mas estados textuales sueltos a `EmptyState`/`PageSkeleton`. | Ya se hizo en Extractos, la pantalla mas critica; queda replicar patron. |
| P2 | Revisar modales con foco atrapado completo. | Hay escape/cierre en varios, pero no todos tienen focus trap real. |
| P3 | Limpiar assets hash antiguos de `wwwroot/assets`. | No bloquea; no quise borrar sin una estrategia de limpieza explicita. |

### Opinion final

La mejora correcta era tocar el sistema, no maquillarlo pantalla por pantalla. Ahora la app tiene mejor contraste, menos dependencia externa, mejores controles y estados mas serios. El siguiente salto de calidad no es "mas color"; es QA visual con datos reales y romper el CSS monolitico antes de que se convierta en un pantano.

Fecha: 2026-04-19
Proyecto analizado: `C:\Proyectos\Atlas Balance\atlas-blance-scaffolding\atlas-blance`
Skills aplicadas conjuntamente:

- `Diseno\emilkowalski-skill\.agents\skills\emil-design-eng\SKILL.md`
- `Diseno\impeccable\.agents\skills\impeccable\SKILL.md`
- `Diseno\taste-skill\.agents\skills\design-taste-frontend\SKILL.md`
- `Diseno\ui-ux-pro-max-skill\.agents\skills\ui-ux-pro-max\SKILL.md`

## Veredicto directo

La aplicacion tiene una base funcional seria, pero visualmente todavia parece una suma de parches: `variables.css`, `layout.css`, `auth.css`, `DESIGN.md` y algunas pantallas estan tirando en direcciones distintas.

El problema principal no es "falta de polish". Es peor: falta una unica fuente de verdad para el sistema visual. Mientras eso siga asi, cada mejora aislada va a producir mas inconsistencia.

La direccion correcta para este producto es clara: dashboard financiero premium, denso, sobrio, legible y rapido. Nada de show visual, nada de gradientes de startup, nada de microanimaciones tontas. Esto lo usan personas que gestionan dinero. Necesitan confianza, velocidad y precision.

## Sintesis combinada de las 4 skills

| Skill | Criterio aplicado | Decision para este proyecto |
| --- | --- | --- |
| Emil Kowalski Design Engineering | Los detalles invisibles crean calidad: estados, easing, feedback, interrupcion, tactilidad. | Usar motion minimo y util: press feedback, transiciones de paneles, skeletons y cambios de estado. Nada de animacion decorativa constante. |
| Impeccable | Evitar estetica generica de IA: card soup, gradientes, texto decorativo, jerarquia floja. | Eliminar duplicidades visuales, matar gradientes del login, reducir cards repetidas y hacer que los numeros manden. |
| Design Taste Frontend | Direccion visual fuerte, datos con densidad controlada, hardware acceleration, estados completos. | Ajustar el baseline: `VISUAL_DENSITY` debe subir para tablas/extractos, pero `MOTION_INTENSITY` debe bajar. Este no es Dribbble, es tesoreria. |
| UI/UX Pro Max | Accesibilidad, contraste, touch targets, formularios, responsive, charts y feedback como prioridad. | Subir contraste semantico, asociar labels, mejorar estados vacios/error, hacer tablas y charts accesibles. |

## Principios unificados recomendados

1. La UI debe parecer una herramienta financiera madura, no una plantilla de admin.
2. Los numeros son el contenido principal; iconos, bordes y fondos deben callarse.
3. Una pantalla debe tener una accion primaria clara, no cinco botones compitiendo.
4. La densidad se controla con jerarquia, no con texto pequeno y celdas apretadas.
5. El movimiento debe confirmar causa y estado. Si roba atencion, sobra.
6. El modo claro y el modo oscuro se disenan juntos. Invertir colores y rezar no es estrategia.
7. Figma y codigo deben compartir tokens. Si cada uno cuenta una historia distinta, ambos mienten.

## Hallazgos prioritarios

### P1 - El sistema visual esta partido en dos

Evidencia:

- `frontend/src/styles/variables.css` define una paleta crema/azul con radios pequenos de `4px`, `6px`, `8px`.
- `DESIGN.md` pide una direccion cool premium con superficies `#F5F8FC`, radios `18px-24px`, sombras suaves y dashboard financiero.
- `frontend/src/styles/auth.css` vuelve a definir otra paleta, otro spacing, otros radios y otros tokens.
- `frontend/src/styles/layout.css` contiene estilos globales, componentes y estilos de paginas en un archivo de 2205 lineas.

Impacto:

- La app no puede mantener coherencia pantalla a pantalla.
- Cualquier cambio de color o espaciado obliga a perseguir selectores.
- El login, el dashboard y las pantallas operativas no parecen partes del mismo producto.

Recomendacion:

- Convertir `DESIGN.md` en la fuente de verdad.
- Rehacer `variables.css` con tokens semanticos: `--bg-app`, `--bg-surface`, `--text-primary`, `--text-secondary`, `--accent-primary`, `--surface-border`, `--radius-card`, `--shadow-card`, `--ease-premium`.
- Eliminar los tokens duplicados de `auth.css`.
- Dividir `layout.css` en:
  - `styles/base.css`
  - `styles/layout.css`
  - `styles/components/buttons.css`
  - `styles/components/cards.css`
  - `styles/components/forms.css`
  - `styles/components/tables.css`
  - `styles/pages/dashboard.css`
  - `styles/pages/extractos.css`
  - `styles/pages/auth.css`

## Contraste y legibilidad

### Problema 1 - Algunos colores semanticos no pasan contraste AA

Contrastes calculados sobre los tokens actuales:

| Par | Ratio aproximado | Estado |
| --- | ---: | --- |
| `--color-text-primary` sobre `--color-bg-primary` | 16.86:1 | Bien |
| `--color-text-secondary` sobre `--color-bg-primary` | 11.07:1 | Bien |
| `--color-text-muted` sobre `--color-bg-primary` | 3.33:1 | Falla para texto normal |
| `--color-success` sobre `--color-bg-success` | 3.21:1 | Falla |
| `--color-danger` sobre `--color-bg-danger` | 4.08:1 | Falla por poco |
| `--color-warning` sobre `--color-bg-warning` | 2.34:1 | Falla fuerte |
| `--color-sidebar-text` sobre sidebar | 11.57:1 | Bien |
| `--color-text-muted` dark sobre dark bg | 3.92:1 | Falla para texto normal |

Recomendacion:

- No usar el mismo color semantico como texto sobre su fondo soft.
- Crear pares explicitos:
  - `--success-bg`, `--success-text`
  - `--warning-bg`, `--warning-text`
  - `--danger-bg`, `--danger-text`
  - `--info-bg`, `--info-text`
- Subir el warning text a un marron/ambar oscuro en claro, no naranja puro.
- Reservar `--text-muted` para metadata no critica; no usarlo en labels, importes, validaciones ni estados.

### Problema 2 - La tipografia actual no encaja con una herramienta de tesoreria

Actual:

- `Hind Madurai` para headings.
- `Palanquin` para cuerpo.
- Base de 14px.

Critica:

- `Palanquin` tiene una personalidad demasiado blanda y humanista para tablas financieras.
- `Hind Madurai` no da suficiente autoridad a KPIs ni headers.
- La combinacion no mejora lectura de numeros ni datos densos.

Recomendacion:

- Usar `Geist` o `Manrope` para interfaz.
- Usar `Geist Mono`, `JetBrains Mono` o `SF Mono` para importes, saldos, IDs, codigos y columnas numericas.
- Activar `font-variant-numeric: tabular-nums` de forma global para:
  - KPIs
  - celdas monetarias
  - columnas de saldo
  - fechas en tablas
  - badges numericos

Escala sugerida:

| Rol | Tamano | Peso | Uso |
| --- | ---: | ---: | --- |
| Page title | 32px | 700 | Titulo principal |
| Section title | 20px | 600 | Bloques de pagina |
| Card title | 14px | 600 | Labels de KPI |
| KPI hero | 36px-44px | 700/800 | Saldo total |
| KPI secondary | 24px-28px | 700 | Ingresos/egresos |
| Table body | 13px | 500 | Extractos |
| Meta | 12px | 500 | Timestamps, ayuda |

### Problema 3 - El favicon sigue siendo Vite

`frontend/index.html` usa `/vite.svg`. Eso en una app de gestion financiera es un detalle pequeno, pero canta. Si una herramienta maneja dinero y conserva el favicon de Vite, la confianza baja.

Recomendacion:

- Usar favicon propio de Atlas Balance.
- Agregar metadata basica:
  - `description`
  - `theme-color`
  - `apple-touch-icon`
  - `manifest` si aplica instalacion local/PWA.

## Diseno de pantallas

### Shell general

Estado actual:

- Sidebar oscuro fijo.
- TopBar con boton de sidebar a la izquierda y usuario/acciones a la derecha.
- El titulo real de pagina vive dentro de cada pantalla.

Problema:

- El sidebar oscuro pesa demasiado para la direccion "calm authority" definida en `DESIGN.md`.
- La topbar desaprovecha espacio. No muestra contexto, breadcrumb, busqueda ni estado del sistema.
- En pantallas densas, el usuario necesita orientacion persistente.

Recomendacion:

- Cambiar sidebar a superficie clara o apenas tintada en modo claro.
- Usar active item con pill suave, no solo color en fondo oscuro.
- TopBar deberia contener:
  - titulo de pagina actual
  - breadcrumb corto cuando haya detalle
  - busqueda global o selector de contexto cuando aplique
  - usuario y acciones del sistema
- En mobile, mantener bottom nav, pero cambiar "Mas" por "Menu" y evitar textos truncados tipo `Import.` o `Audit.` salvo que no haya alternativa.

### Login

Estado actual:

- `auth.css` define sistema propio.
- Usa gradiente de texto en marca.
- Usa boton con gradiente.
- Usa `transition: all`.
- Usa `border-left: 4px` en success.

Problema:

- Es la pantalla que deberia transmitir confianza, pero ahora se siente distinta al resto.
- El gradiente de texto y boton es un patron barato para este tipo de producto.
- `transition: all` es vago y puede animar propiedades caras.
- El success con barra lateral es uno de los patrones mas quemados en dashboards.

Recomendacion:

- Login sobrio, con marca clara, no espectacular.
- Fondo `--bg-app`, card con `--bg-surface`, borde suave y sombra premium.
- Boton primario solido, no gradiente.
- Success/error como bloque completo con icono lineal, fondo soft y texto con contraste AA.
- Permitir mostrar/ocultar password.
- Auto-focus inicial en email.
- Error de login con recuperacion concreta: "Revisa email y contrasena. Si has fallado varias veces, espera 30 minutos."

### Dashboard principal

Estado actual:

- Tres KPI cards iguales.
- Saldos por divisa y tabla por titular en layout basico.
- Chart Recharts con grid, legend y tooltip por defecto.

Problema:

- Todo tiene peso visual parecido.
- El saldo total deberia dominar, pero compite con cards hermanas.
- La grafica parece libreria por defecto, no producto financiero disenado.

Recomendacion:

- Crear una fila hero:
  - `Saldo total` como card dominante, 2 columnas de ancho.
  - `Ingresos periodo` y `Egresos periodo` como supporting metrics.
  - Selector de divisa y periodo integrados arriba, no flotando como controles de formulario genericos.
- Redisenar chart:
  - gridlines mucho mas suaves
  - sin legend generica si las series se pueden etiquetar directamente
  - tooltip custom con superficie, fecha clara, importes tabulares
  - `aria-label` o resumen textual para lectores de pantalla
  - color de egresos menos saturado, porque el rojo actual grita demasiado
- La tabla por titular deberia usar importes alineados a la derecha, datos tabulares y accion secundaria discreta.

### Extractos y tabla Excel-like

Estado actual:

- Usa `@tanstack/react-virtual`, buena decision.
- Columnas base y dinamicas.
- Filtros inline en cada header.
- Panel de visibilidad siempre visible.
- Auditoria de celda se abre con click derecho.

Problema:

- La tabla es la zona mas critica del producto y aun se siente como prototipo.
- El panel de columnas ocupa demasiado para una tarea secundaria.
- Los filtros debajo de cada header aumentan altura y ruido.
- Click derecho para auditoria es descubribilidad casi nula.
- Las columnas usan `repeat(... minmax(120px, 1fr))`, que no respeta tipos de dato.

Recomendacion:

- Darle tratamiento de data grid profesional:
  - header sticky
  - primera columna sticky (`fila_numero`)
  - columnas monetarias con ancho fijo y alineacion derecha
  - fecha con ancho fijo
  - concepto flexible
  - columnas extra con ancho guardable
  - densidad configurable: comoda / compacta
- Mover visibilidad de columnas a un boton "Columnas" con popover.
- Convertir filtros inline en una fila de filtros colapsable o filtros por columna al pulsar icono.
- Mostrar audit trail con accion visible en celda o toolbar contextual, no solo click derecho.
- Agregar estados de guardado por celda:
  - editando
  - guardando
  - guardado
  - error con retry
- Check y flag necesitan labels accesibles por fila: `aria-label="Marcar fila 128 como revisada"`.
- Flag no deberia ser solo color amarillo. Debe incluir icono/label o indicador textual.

### Titulares y cuentas

Estado actual:

- Cards de titulares y cuentas comparten tratamiento visual.
- Cuenta bancaria y efectivo se distinguen, pero no hay suficiente jerarquia de riesgo/dato.

Recomendacion:

- Titular card:
  - nombre como elemento principal
  - tipo como badge secundario
  - numero de cuentas y saldo agregado si existe
  - accion "Abrir" como link o boton secundario, no primario en cada card
- Cuenta card:
  - banco/cuenta y divisa deben escanearse rapido
  - efectivo debe usar un tratamiento de estado, no solo texto
  - saldo actual debe tener mas peso que metadatos
- Evitar que todas las cards tengan identica presencia. Titulares con actividad reciente pueden tener mas contexto; cuentas secundarias pueden ser mas compactas.

### Importacion

Estado actual:

- UI de 2 pasos: pegar y validar/confirmar.
- La especificacion del proyecto describe un wizard de 4 pasos.

Problema:

- Para usuarios no tecnicos, importar extractos es una tarea de alto riesgo.
- Meter preview, mapeo, validacion y confirmacion en 2 pasos reduce claridad.

Recomendacion:

- Volver al wizard de 4 pasos:
  1. Pegar datos y seleccionar cuenta.
  2. Confirmar mapeo y columnas extra.
  3. Revisar validacion con errores por fila.
  4. Confirmar resumen final.
- La tabla de errores debe priorizar filas problematicas, no obligar a leer todo.
- Usar icono + texto para valido/invalido, no solo `check` y `x`.
- Permitir "importar solo validas" con copy claro y contador visible.
- Para datos pegados desde Excel, mostrar una mini-preview con columnas numeradas y ejemplos.

### Configuracion, auditoria, backups y exportaciones

Problema comun:

- Son pantallas administrativas con muchas acciones peligrosas o irreversibles.
- Varias acciones parecen botones normales cuando deberian tener jerarquia de riesgo.

Recomendacion:

- Configuracion:
  - separar en secciones con subtabs persistentes
  - guardar por seccion, no un mega-form mental
  - mostrar estado de SMTP, backups, exchange rates e integraciones como paneles de salud
- Auditoria:
  - filtros guardables
  - columnas con prioridad: fecha, usuario, entidad, accion, resumen
  - expandir detalle con diff legible, no `pre` crudo cuando haya JSON
- Backups:
  - restaurar debe ser accion destructiva con doble confirmacion real
  - mostrar ultimo backup valido y proximo programado
- Exportaciones:
  - diferenciar pendiente, generando, disponible, error
  - accion de descarga como boton secundario, no link suelto

## Botones y elementos interactivos

### Problema 1 - Falta feedback tactil global

El estilo base de `button` no aplica estado `:active` con transform. La skill de Emil es muy clara aqui: un boton sin respuesta fisica se siente muerto.

Recomendacion:

```css
button,
.button,
.dashboard-open-link {
  transition:
    background-color var(--duration-fast) var(--ease-premium),
    border-color var(--duration-fast) var(--ease-premium),
    box-shadow var(--duration-fast) var(--ease-premium),
    transform 120ms var(--ease-premium),
    opacity var(--duration-fast) var(--ease-premium);
}

button:active:not(:disabled),
.button:active:not([aria-disabled="true"]) {
  transform: scale(0.98);
}
```

No usar `transition: all`. Es pereza con coste tecnico.

### Problema 2 - Jerarquia de botones insuficiente

Ahora muchos botones comparten aspecto. Eso obliga al usuario a leer mas de la cuenta.

Sistema recomendado:

| Variante | Uso |
| --- | --- |
| Primary | Una accion principal por pantalla o formulario |
| Secondary | Acciones normales: guardar parcial, abrir, descargar |
| Tertiary/text | Acciones de bajo peso: cancelar, ver detalle |
| Danger | Eliminar, restaurar backup, revocar token |
| Icon | Solo herramientas compactas, siempre con `aria-label` y tooltip |

Regla:

- Si una pantalla tiene mas de un boton primario visible al mismo tiempo, probablemente esta mal.

### Problema 3 - Touch targets pequenos

Ejemplo: el boton de cerrar toast mide `1.35rem`. En mobile eso no llega a 44px. Parece detalle menor, hasta que alguien intenta cerrarlo con el dedo y falla.

Recomendacion:

- Minimo interactivo real: `44px x 44px`.
- Si el icono es pequeno, ampliar hit area con padding.
- Tooltips para icon-only buttons en desktop.

## Jerarquia visual

### Problema 1 - Las cards pesan todas igual

El dashboard, titulares, cuentas, configuracion y alertas usan muchas cards con borde, fondo y sombra parecidos.

Impacto:

- La mirada no sabe donde empezar.
- Lo importante no domina.
- La app se acerca a "card soup".

Recomendacion:

- Usar cards solo cuando separen una unidad funcional real.
- Para grupos secundarios, preferir:
  - lineas divisorias
  - espacio
  - backgrounds suaves
  - headers claros
- En dashboard:
  - una card hero
  - cards secundarias
  - tabla/lista sin exceso de caja

### Problema 2 - Radios demasiado pequenos para la direccion definida

Los tokens actuales:

- `--border-radius-md: 6px`
- `--border-radius-lg: 8px`

El `DESIGN.md` pide:

- controles: `10px-12px`
- cards: `18px-24px`
- shell: `28px-32px`

Recomendacion:

- Subir radios, pero no volverlo juguete.
- Propuesta:
  - `--radius-control: 12px`
  - `--radius-card: 18px`
  - `--radius-panel: 24px`
  - `--radius-shell: 30px`

### Problema 3 - KPIs demasiado timidos

En `dashboard-kpi p`, el valor usa `var(--font-size-xl)` equivalente a 24px. Para saldo total, eso es pequeno. En finanzas, el numero manda.

Recomendacion:

- KPI principal: 40px desktop, 32px mobile.
- KPI secundario: 26px desktop, 24px mobile.
- Labels: 12px-13px, uppercase solo si se usa con moderacion y letter-spacing controlado.
- Delta y contexto: debajo del numero, no compitiendo al lado.

## Animaciones y motion

### Principio

No metas animaciones porque una skill lo dice. Para esta app, meter magnetic buttons o micro-animaciones perpetuas seria una tonteria. Esto es tesoreria, no una landing de inteligencia artificial.

Motion recomendado:

| Elemento | Motion |
| --- | --- |
| Botones | `scale(0.98)` en active, 120ms |
| Hover de cards clicables | translateY(-2px), sombra suave, 160ms |
| Modales | fade + scale desde 0.97, 180-220ms |
| Bottom sheet mobile | translateY(100%) a 0, 220ms |
| Tabs/filtros | fade + desplazamiento 6px, 180ms |
| Skeleton | shimmer, pero desactivado/reducido con `prefers-reduced-motion` |
| Chart load | reveal discreto, 400-600ms, no rebote |

### Problemas actuales

- `auth.css` usa `transition: all`.
- No hay `@media (prefers-reduced-motion: reduce)`.
- El skeleton shimmer corre siempre.
- No hay sistema unico de easing.
- Modales aparecen/desaparecen sin tratamiento consistente.

Recomendacion:

```css
:root {
  --ease-premium: cubic-bezier(0.22, 1, 0.36, 1);
  --duration-instant: 120ms;
  --duration-fast: 180ms;
  --duration-base: 240ms;
  --duration-slow: 420ms;
}

@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 1ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 1ms !important;
    scroll-behavior: auto !important;
  }
}
```

## Consistencia general

### Problema 1 - `auth.css` debe dejar de ser isla

`auth.css` redefine:

- paleta
- dark palette
- spacing
- radii
- shadows
- fonts

Eso rompe el sistema. La pantalla de login debe consumir los mismos tokens que el resto.

### Problema 2 - Los componentes comunes son demasiado pobres

Componentes actuales:

- `EmptyState` solo muestra titulo/subtitulo.
- `PageSkeleton` es generico.
- `ConfirmDialog` funciona, pero no gestiona foco inicial ni retorno de foco.
- `ToastViewport` esta bien encaminado, pero el cierre es pequeno y faltan variantes visuales fuertes.

Recomendacion:

- `EmptyState` debe aceptar:
  - icono opcional
  - accion primaria
  - accion secundaria
  - descripcion util
  - variante segun contexto: data, error, permission, setup
- `PageSkeleton` debe tener variantes:
  - dashboard
  - table
  - form
  - detail
- `ConfirmDialog` debe:
  - enfocar el boton menos destructivo al abrir
  - hacer trap de foco
  - devolver foco al trigger al cerrar
  - separar visualmente accion destructiva
- `ToastViewport` debe:
  - usar `role="status"` para info/success y `role="alert"` para error
  - tener boton de cierre 44px
  - pausar timeout en hover/focus si el usuario intenta leerlo

### Problema 3 - Los formularios no tienen una gramatica visual consistente

Hay labels asociados y otros sueltos. En `ImportacionPage`, por ejemplo, aparecen labels antes de selects/textareas sin `htmlFor` ni wrapping semantico completo.

Recomendacion:

- Crear `Field`, `FieldLabel`, `FieldHint`, `FieldError`.
- Cada input debe tener:
  - `id`
  - `label htmlFor`
  - helper si la decision no es obvia
  - error debajo del campo
  - `aria-invalid`
  - `aria-describedby`
- Validar en blur o submit, no gritar mientras el usuario escribe.

## Charts y visualizacion de datos

### Problemas actuales

- `EvolucionChart` usa `CartesianGrid`, `Legend`, `Tooltip`, `XAxis`, `YAxis` casi por defecto.
- No hay resumen accesible.
- No hay control de contraste entre lineas y fondo.
- No hay degradacion responsive si el ancho es pequeno.

Recomendacion:

- Crear `ChartCard` comun.
- Crear tooltip custom.
- Ocultar o suavizar gridlines: `stroke="var(--chart-grid)"`, opacidad baja.
- Usar labels directos o leyenda compacta custom.
- En mobile:
  - reducir ticks
  - ocultar Y axis si no cabe
  - mostrar resumen arriba
- Agregar texto accesible:
  - "Evolucion de saldo, ingresos y egresos durante el periodo seleccionado."
  - insight principal si se puede calcular: saldo final, variacion, maximo egreso.

## Responsive y mobile

Lo bueno:

- Existe bottom nav.
- Se usa `100dvh` en algunos modales.
- Hay breakpoints en varias zonas.

Lo flojo:

- `app-shell` y `auth-page` usan `min-height: 100vh` en varios sitios. En mobile es menos estable que `100dvh`.
- Algunas tablas dependen de scroll horizontal sin estrategia de columnas prioritarias.
- Bottom nav tiene labels comprimidas y menu secundario con mucho texto.
- Algunas acciones administrativas se vuelven full-width, pero no siempre con orden correcto.

Recomendacion:

- Cambiar contenedores fullscreen a `min-height: 100dvh`.
- En tablas mobile, usar:
  - columnas prioritarias
  - summary row/card para detalle
  - accion "ver detalle" cuando la tabla tenga demasiadas columnas
- Mantener bottom nav con maximo 5 destinos, pero revisar labels:
  - Inicio
  - Titulares
  - Cuentas
  - Extractos
  - Menu
- En menu bottom sheet, usar icono + label + badge, no frases explicativas largas.

## Accesibilidad

### Prioridades

1. Corregir contraste semantico.
2. Asociar todos los labels a campos.
3. Hacer checkboxes de tablas descriptivos.
4. Crear foco visible consistente para links, buttons, inputs y NavLinks.
5. Gestionar foco en modales.
6. Respetar `prefers-reduced-motion`.
7. No comunicar estado solo por color.
8. Dar resumen textual a charts.

### Casos concretos

- `EditableCell` entra en edicion por doble click. Eso no basta:
  - debe admitir Enter/F2 para editar
  - debe anunciar que la celda es editable
  - debe exponer estado de guardado/error
- Auditoria por click derecho:
  - anadir accion visible y accesible
  - mantener click derecho como atajo, no como unica via
- Check/flag:
  - anadir labels por fila
  - no depender solo de amarillo para flagged

## Propuesta de tokens base

Esto deberia sustituir gradualmente el nucleo de `variables.css`.

```css
:root {
  --bg-app: #F5F8FC;
  --bg-canvas: #F8FAFD;
  --bg-surface: #FFFFFF;
  --bg-surface-soft: #F2F6FC;
  --bg-surface-muted: #EEF3FA;

  --border-soft: rgba(23, 33, 52, 0.08);
  --border-strong: rgba(23, 33, 52, 0.14);

  --text-primary: #1E2430;
  --text-secondary: #5F6B7A;
  --text-muted: #7E8A9A;
  --text-inverse: #FFFFFF;

  --accent-primary: #4C7DFF;
  --accent-primary-hover: #3F6FEF;
  --accent-primary-soft: #EAF1FF;

  --success-bg: #EAF8F0;
  --success-text: #176A3A;
  --warning-bg: #FFF4DF;
  --warning-text: #8A5200;
  --danger-bg: #FDECEF;
  --danger-text: #A82234;

  --chart-ingresos: #2FB36D;
  --chart-egresos: #D84A5B;
  --chart-saldo: #4C7DFF;
  --chart-grid: rgba(23, 33, 52, 0.08);

  --font-sans: "Geist", "Manrope", -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  --font-mono: "Geist Mono", "SF Mono", "Roboto Mono", monospace;

  --radius-control: 12px;
  --radius-card: 18px;
  --radius-panel: 24px;
  --radius-shell: 30px;
  --radius-pill: 999px;

  --shadow-card: 0 8px 24px rgba(16, 24, 40, 0.06);
  --shadow-card-hover: 0 12px 28px rgba(16, 24, 40, 0.08);
  --shadow-shell: 0 24px 60px rgba(16, 24, 40, 0.10);

  --ease-premium: cubic-bezier(0.22, 1, 0.36, 1);
  --duration-fast: 180ms;
  --duration-base: 240ms;
  --duration-slow: 420ms;
}
```

Dark mode:

- No invertir automaticamente.
- Usar charcoal profundo, no negro puro.
- Recalcular contraste de todos los pares semanticos.
- Reducir saturacion de acentos.

## Plan de ejecucion recomendado

### Fase A - Base visual y accesibilidad

Objetivo: arreglar el sistema antes de tocar pantallas sueltas.

Tareas:

1. Rehacer `variables.css` segun `DESIGN.md`.
2. Eliminar tokens duplicados de `auth.css`.
3. Crear variantes base para botones, fields, cards, badges, modales y toasts.
4. Corregir contraste semantico.
5. Anadir `prefers-reduced-motion`.
6. Cambiar favicon y metadata.

Criterio de aceptacion:

- Ningun token duplicado entre `auth.css` y `variables.css`.
- Contraste AA para texto normal y estados.
- Botones con variantes claras y feedback tactil.
- Build frontend OK.

### Fase B - Shell, login y dashboard

Objetivo: que la primera impresion deje de parecer plantilla.

Tareas:

1. Redisenar login con tokens globales.
2. Redisenar sidebar/topbar.
3. Convertir dashboard a layout hero + soporte.
4. Customizar charts.
5. Crear `ChartCard`, `MetricCard`, `Toolbar`, `PageHeader`.

Criterio de aceptacion:

- Dashboard comunica saldo total en menos de 2 segundos.
- Chart no parece Recharts por defecto.
- Login, shell y dashboard comparten la misma personalidad visual.

### Fase C - Extractos como data grid serio

Objetivo: mejorar la zona mas critica.

Tareas:

1. Sticky header y columnas clave.
2. Widths por tipo de dato.
3. Popover de columnas.
4. Filtros menos invasivos.
5. Auditoria visible y accesible.
6. Estados de guardado por celda.
7. Densidad configurable.

Criterio de aceptacion:

- 10k filas siguen fluidas.
- Se puede editar con teclado.
- Audit trail se descubre sin conocer click derecho.
- Mobile no intenta meter 12 columnas como si nada.

### Fase D - Formularios administrativos y flujos de riesgo

Objetivo: reducir errores humanos.

Tareas:

1. Crear componentes de formulario comunes.
2. Rehacer Importacion como wizard de 4 pasos.
3. Mejorar Configuracion por secciones.
4. Mejorar Backups con jerarquia de riesgo.
5. Mejorar Auditoria con diff legible.

Criterio de aceptacion:

- Cada accion destructiva tiene confirmacion clara.
- Cada error explica causa y recuperacion.
- Los formularios no dependen de labels sueltos ni texto ambiguo.

### Fase E - QA visual y Figma

Objetivo: cerrar el circulo.

Tareas:

1. Actualizar Figma en la misma sesion de cada cambio UI.
2. Registrar en `DOCUMENTACION_CAMBIOS.md` nodo/pantalla tocada.
3. Verificar desktop, tablet y mobile.
4. Verificar dark mode.
5. Verificar reduced motion.
6. Verificar contrastes.

Criterio de aceptacion:

- Codigo y Figma sincronizados.
- Sin pendientes de contraste.
- Sin componentes principales fuera del sistema.

## Checklist por categoria solicitada

### Contraste y legibilidad

- [ ] Recalcular pares semanticos success/warning/danger.
- [ ] No usar `--text-muted` para informacion funcional.
- [ ] Cambiar tipografia a sans de interfaz mas financiera.
- [ ] Aplicar tabular numbers globalmente a datos.
- [ ] Redisenar charts con contraste minimo 3:1 para trazos.

### Diseno de pantallas

- [ ] Login sin gradiente ni sistema propio.
- [ ] Sidebar claro/soft en modo claro.
- [ ] TopBar con titulo/contexto real.
- [ ] Dashboard con KPI dominante.
- [ ] Extractos con data grid profesional.
- [ ] Importacion en wizard de 4 pasos.

### Botones e interactivos

- [ ] Sistema de variantes: primary, secondary, tertiary, danger, icon.
- [ ] Un solo primary visible por contexto.
- [ ] `:active` tactile feedback.
- [ ] Icon buttons con 44px y tooltip.
- [ ] Destructivos separados visualmente.

### Jerarquia visual

- [ ] Reducir cards repetidas.
- [ ] Subir radios al rango definido en `DESIGN.md`.
- [ ] Subir peso visual de importes principales.
- [ ] Alinear numeros a la derecha en tablas.
- [ ] Usar espacio y escala antes que color.

### Animaciones

- [ ] Definir tokens de easing/duracion.
- [ ] Eliminar `transition: all`.
- [ ] Agregar `prefers-reduced-motion`.
- [ ] Animar solo transform/opacity.
- [ ] Motion sobrio: feedback, paneles, filtros, charts.

### Consistencia general

- [ ] Unificar tokens.
- [ ] Separar CSS monolitico.
- [ ] Componentizar estados comunes.
- [ ] Alinear Figma y codigo.
- [ ] Documentar cada cambio visual en bitacora.

## Riesgos si no se hace

- Cada fase nueva va a meter mas CSS especifico y mas excepciones.
- Dark mode se va a romper pantalla por pantalla.
- La tabla de extractos va a quedarse funcional pero incomoda, justo donde mas uso diario hay.
- Figma dejara de ser fuente de verdad y se convertira en decoracion.
- La app parecera "suficientemente hecha", que es la trampa mas cara: funciona, pero no inspira confianza.

## Recomendacion final

No empieces cambiando colores al azar. Eso seria maquillaje barato.

Primero hay que arreglar el sistema: tokens, tipografia, contraste, botones, cards, forms, motion y estructura CSS. Despues se redisenan pantallas criticas en este orden:

1. Login y shell.
2. Dashboard principal.
3. Extractos.
4. Importacion.
5. Configuracion, auditoria, backups y exportaciones.

Esa secuencia ataca percepcion, operacion diaria y riesgo. Es la ruta con mas impacto y menos desperdicio.
