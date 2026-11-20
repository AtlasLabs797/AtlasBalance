Las entradas anteriores a V-03.02 estan en `DOCUMENTACION_CAMBIOS_ARCHIVO.md` (historico de V-03.01 y anteriores).

# 2026-11-20 - V-03.02 - Pulido visual del frontend con referencia Astryx (Lotes 1 y 2)

- Trabajo: revision del frontend (estilos, componentes, plantillas e interacciones de todas las pantallas) con el design system Astryx (`C:\Users\usuario\Documents\Herramentas\Astryx`) como referencia. Decisiones del usuario: portar patrones a nuestras variables CSS **sin dependencias nuevas**, mantener la marca Atlas actual, alcance completo por lotes y capturas sacadas por el orquestador con el arnes de Playwright. `Documentacion/Diseno/DESIGN.md` manda en cualquier conflicto con Astryx. Metodo: dos auditorias de solo lectura (grupo A: shell, login, dashboards, titulares, cuentas, extractos, conciliacion y revision; grupo B: importacion, formatos, configuracion, paises, backups, usuarios, auditoria, papelera, alertas, MFA y modales), escritores Sonnet/Haiku con ficheros disjuntos, revision visual independiente antes/despues y una ronda de correccion de regresiones. Sin commit.
- **Lote 1 (sistema base y marco):**
  - Controles a 44 px reales (`min-height` + padding vertical 0) en inputs, selects, `.app-select-trigger` y `.date-picker-trigger`, todos con `--radius-field` (11) y `width:100%; min-width:0`. Antes, los inputs se montaban unos sobre otros en rejillas (modal de titular, conciliacion).
  - Botones de fila de tabla a 32 px y "Eliminar" como quiet-danger. `td.users-row-actions` deja de ser `display:flex`, que rompia la celda.
  - Modales con una sola cabecera (`--type-title-3`, padding 24) y a ancho completo en movil.
  - Esc: `useDialogFocus` ignora eventos con `defaultPrevented` y mantiene una pila de modales, asi que solo responde el de arriba. Combobox y DatePicker consumen Esc solo si estan abiertos.
  - Estado deshabilitado con `--text-disabled` en vez de `opacity`. Un solo anillo de foco, un solo hover de fila y badge neutro visible en oscuro.
  - Los 10 submits sin clase pasan a `button-primary`, con "Cancelar" como `button-secondary`.
  - Marco de la app:
    - Banner de alerta dentro del gutter del contenido y compacto en movil (de ~190 a ~70 px).
    - Bottom nav sin cajitas.
    - Reserva inferior para el boton flotante de IA.
    - Buscador del topbar con borde, visible en oscuro.
    - Sidebar con scroll propio: el grupo "Sistema" ya muestra "Usuarios".
    - Selector de pais compacto.
    - Pestanas de Configuracion en una fila con scroll horizontal.
- **Lote 2 (paginas):**
  - Cabeceras: el subtitulo de cabecera de todas las paginas usa `page-header__subtitle`. Desaparecen `page-header__title--bold` (tambien su regla en `page.css`) y `page-header--plain`. `dashboard-subtitle` y `config-subtitle` siguen solo como texto secundario dentro de tarjetas. Nace `.page-header__actions` (en `entities.css`).
  - Control segmentado: periodo y divisa del dashboard, modo y densidad de Extractos y pestanas de Revision (`.config-tabs--seg`) comparten la misma gramatica y tienen borde en reposo, visible en oscuro.
  - Extractos:
    - Filtros de cabecera a 44 px y alineados.
    - Fila marcada sin fondo coloreado: borde izquierdo de estado + punto, como pide DESIGN.md.
    - La lupa del filtro de columna se oculta en columnas estrechas (container query).
  - Conciliacion: cabecera con select y Actualizar en una fila, barra de sugerencias alineada, botones de fila a 32 px, paginacion compacta y tildes.
  - Dashboard: el saldo por divisa es una lista vertical en la columna derecha del hero, sin tarjetas anidadas y con el badge "Base" junto a su divisa. Las listas de titulares y cuentas son planas.
  - Detalle de cuenta: acciones a 44 px ("Volver" y "Ver en extractos" secundarios, "Importar" primario) e identidad como `dl` sin cajas.
  - Administracion:
    - Paises a ancho completo; las clases `paises-*` no tenian CSS.
    - Importacion: pestanas con la gramatica de `.config-tab`, la pestana "Lote" deshabilitada legible y un stepper sin aspecto de pestana.
    - Historial de importacion: sin rejilla vertical, SHA corto con `title` y estado como badge.
    - Formatos: panel lateral `minmax(22rem, 0.9fr)`, legend dentro del borde y rejilla apilada por debajo de 1199.98 px. A 1024 px las dos columnas minimas (38 rem + 22 rem) no cabian, el panel se salia por la derecha y la columna Extra pisaba el badge de estado. El defecto ya existia antes del lote.
    - Auditoria: filtros alineados, "Exportar CSV" en la cabecera y paginacion sin mono.
    - Configuracion General sin tarjeta dentro de tarjeta.
    - Alertas: casilla y texto en linea.
    - Usuarios: KPIs compactos y `—` en celdas vacias.
    - Backups: "Desvincular" como quiet-danger con contorno, y "Tamaño" con tilde.
    - Badges a 12 px / 600.
  - Revertir lote usa el tono `reversible`. El lote se puede volver a importar y el dialogo decia a la vez que se podia y que no se podia deshacer. Ya no muestra "Esta accion no se puede deshacer": decision confirmada por el usuario el 2026-11-20.
  - Login y cambiar contrasena: en movil la marca se alinea con la tarjeta y desaparecen la banda vacia y el corte de dos tonos. En oscuro el fondo es uniforme y la tarjeta tiene borde. Un solo anillo de foco.
  - Toast: boton de cierre de 32 px sin margen negativo.
  - Calendario: el titulo pasa de "Noviembre De 2026" a "Noviembre de 2026" (`formatMonthTitle` en `DatePickerField.tsx`, sin `text-transform: capitalize`).
  - Copy en sentence case y con tildes: "Nuevo titular", "Nueva cuenta", "Nuevo formato", "Alertas de saldo bajo", "Formatos de importación", "Auditoría del sistema", "Auditoría de integraciones", "Excepción", "Fecha de creación", "Días", "Página", "Programación", "automáticas" y "Sin país". "Revocar Authenticator" pasa a "Revocar MFA".
- Archivos (todos bajo `Atlas Balance/frontend/`):
  - Estilos: `src/styles/{global,auth}.css`, `src/styles/components/{modal,tabs}.css`, `src/styles/layout/{shell,page,users,dashboard,extractos,entities,admin,importacion}.css` y `src/styles/layout/routes/{extractos,conciliacion,revision,dashboard,cuenta-detail,cuentas,titulares,auditoria,configuracion,formatos-importacion,importacion,usuarios}.css`.
  - TSX: `src/hooks/useDialogFocus.ts`, `src/components/common/{DatePickerField,Combobox}.tsx`, `src/components/layout/AlertBanner.tsx`, `src/pages/{Alertas,Auditoria,Backups,Conciliacion,Configuracion,CuentaDetail,Cuentas,Dashboard,DashboardTitular,DispositivosMfa,Exportaciones,Extractos,FormatosImportacion,Importacion,Papelera,Revision,TitularDetail,Titulares,Usuarios}Page.tsx` y `src/pages/configuracion/PaisesPage.tsx`. Solo cambian clases, copy y marcado; ninguna llamada a la API ni logica de datos.
  - E2E:
    - `e2e/visual/capture.spec.ts`: rutas `/ia` y `/exportaciones`; capturas de las 6 pestanas de Configuracion, de las pestanas de Revision y del selector de titular y el calendario de Extractos abiertos. El locator del calendario comprobaba con `count()` sin esperar y la captura se saltaba siempre.
    - `e2e/README.md`: 23 rutas.
  - Se respetan los cambios previos sin commitear de `ConfiguracionPage`, `CuentaDetailPage`, `DashboardPage`, `DashboardTitularPage` y `TokenList`.
- Comandos (desde `frontend/`):
  - `npm run lint`: exit 0.
  - `npx tsc --noEmit -p tsconfig.json`: exit 0.
  - Tests unitarios compilados con `--outDir .test-dist-front`, porque el canonico da EPERM en `.test-dist/`: **337/337**. El test 337 es `tests/aiModels.test.ts`, del cambio MiniMax M3.1 Flash que hizo en paralelo otra sesion (`utils/aiModels.ts`, `components/ia/AiChatPanel.tsx`); este lote no lo ha tocado.
  - `E2E_SKIP_WEBSERVER=1 VISUAL_OUT=<scratchpad> npx playwright test e2e/visual/capture.spec.ts --reporter=list --output=<scratchpad>`: **6/6** (182 PNG, claro y oscuro a 1440/1024/390).
  - `states.spec.ts` + `extractos-comportamiento.spec.ts`: **25 pasan, 1 omitido**, igual que la linea base.
  - `npx vite build --outDir <scratchpad>`: correcto.
  - Las specs e2e no visuales no se ejecutaron. Un grep no encuentra en ellas ningun literal renombrado.
- Verificacion visual: las capturas de antes, Lote 1 y Lote 2 se revisaron a mano y con un revisor independiente, pantalla a pantalla. Se revisaron sobre todo 1440 y 390. De 1024 solo se abrieron Auditoria, Formatos, Conciliacion y Dashboard, y de oscuro Dashboard, Backups, Extractos y Login. Las regresiones que encontro el revisor se corrigieron y se recapturaron:
  - Marca del login desplazada en movil.
  - Franja de divisas diminuta.
  - "Desvincular" sin forma.
  - Pestana "Lote" apagada.
  - "Volver al titular" sin forma.
  - Legend de Formatos pisando el borde.
  - Etiqueta "Fecha" descolgada.
  - "De" capitalizado en el calendario.
- Decisiones visuales:
  - Campos, selects y fechas con radio 11; acciones y chips en pildora.
  - Botones dentro de tablas a 32 px.
  - Peligro en fila como texto rojo (quiet-danger), con contorno cuando va junto a otros botones con borde.
  - Filas marcadas nunca coloreadas.
  - Controles en reposo siempre con borde, para que se vean en oscuro.
  - Sentence case con tildes.
  - El icono del boton flotante de IA (se lee como "pausa") se deja: es la marca del sistema de diseno.
- Bloqueado:
  - Login real: la cuenta admin exige MFA, asi que todas las capturas son con la API simulada del arnes.
  - `test-results/` y `playwright-report/` del proyecto dan EPERM (son propiedad de otra cuenta de sandbox). Se usan `--output` y `--reporter=list` hacia el scratchpad.
- Pendientes de diseno:
  - Contraste del texto deshabilitado (~2,5:1 en claro): decidir si se acepta.
  - Detalle de cuenta: las tres acciones ocupan dos filas a 1440 px.
  - Conciliacion a 1024 px: "Crear esperado" se parte en dos lineas y la barra de sugerencias ocupa dos filas.
  - Tarjetas dentro de tarjeta que quedan: "Evolucion" en Cuentas, KPIs de Programacion en Backups, "Destinatarios" en Alertas y fieldset de Formatos.
  - Graficos: etiquetas de eje en dos lineas, barras rotadas y verde brillante en oscuro. No se han tocado.
  - Topbar que repite el titulo de la pagina.
  - Cambiar contrasena: los campos no tienen el boton de mostrar.
  - Conciliacion: formato de fecha `aaaa-mm-dd`.
  - Datos de ejemplo sin tilde ("Espana", "Autonomo", "Comision mantenimiento"): vienen del mock o de la semilla.
  - No se ha ejecutado `vite build` ni se ha validado en la app con backend real.

# 2026-11-18 - V-03.02 - Lote 8 del informe de revision integral (D-1, D-2, D-3, D-4, D-5, D-6, D-7, D-8, D-9, D-10, D-11, D-12, M-1, M-2, M-6, M-10, M-11, M-12, M-13, M-14, M-15, M-17, R-9, T-1, T-2)

> Entrada general del lote. Debajo de esta hay entradas de detalle de cada subagente (M-14/M-15, M-17 y D-5).

- Trabajo: correccion de los IDs del LOTE 8 de `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md`. Sin commit, sin push y sin cambio de version. Metodo: el orquestador leyo el contexto obligatorio (AGENTS.md, `version_actual.md`, `v-03.02.md`, PRODUCT.md, `.impeccable.md`, `Documentacion/Diseno/DESIGN.md` y los dos informes de diseno de V-03.02), fijo con el usuario las cuatro decisiones que cambiaban el resultado (M-1, M-15, M-17 y T-1) y repartio la ejecucion en subagentes con ficheros disjuntos, mas un subagente verificador que reviso el lote entero al final. El orquestador aplico en persona los 5 sitios de `showSign` que quedaban en el dashboard, el arreglo de la ampliacion `declare module 'axios'` que rompio el programa de tests, la regla CSS `.import-card > h2` y la actualizacion de DESIGN.md, y reviso los diffs de todos los subagentes.
- **D-1:** `formatPercent` en `src/utils/formatters.ts` con `Intl.NumberFormat('es-ES')`: coma decimal, signo menos real (U+2212), separador del locale antes del `%` (U+00A0, extraido de `formatToParts`, no escrito a mano) y cero sin «+». El valor entra ya en unidades de porcentaje, asi que el formateador usa `style:'decimal'` y no `style:'percent'`. Valor ausente: `—`. 9 `toFixed` de porcentajes sustituidos en el dashboard (11 llamadas a `formatPercent`); los `toFixed` de importes intactos. Test nuevo `tests/formatPercent.test.ts`.
- **D-2:** barrido de tildes y unificacion de formulas en `PaisesPage`, `ConfiguracionPage`, `AlertasPage` e `ImportacionPage`; 31 errores genericos convertidos en accionables («No se pudo <que>. <que hacer ahora>.»). Los comentarios del codigo no se tocan. Test nuevo `tests/copyYColoresConfiguracion.test.ts`.
- **D-3:** `PageSkeleton` pasa a ser el componente unico de carga, con `role="status"` y `aria-live="polite"`, esqueleto de tabla real (`variant="table"`) y modo `inline` para estados dentro de una seccion. Se retiran los 28 `<p>Cargando…</p>` de `src/`. Se quita `aria-busy` del contenedor: en una region viva silencia el anuncio. Test nuevo `tests/estadoCargaUnico.test.ts`.
- **D-4:** **todas las media queries de anchura de `src/styles/` quedan en los tres umbrales del sistema** (767.98 / 1023.98 / 1199.98, mas las dos formas inversas de la banda tablet). Se mapearon 23 umbrales en las hojas de pagina y se corrigieron los que quedaron fuera al repartirse el CSS: `components/tabs.css` (560px -> 767.98px y `768-1200` -> `768-1199.98px`), `layout/routes/auditoria.css` (1200px -> 1199.98px) y `layout/shell.css` (560px -> 767.98px). El bloque del movil estrecho (360px) **se integro tambien en 767.98px**: es el unico mapeo que cambia el aspecto en una anchura frecuente (390 px), asi que se valido con capturas (la barra inferior sigue mostrando las cinco etiquetas sin recorte). Consecuencias del mapeo: la banda 901-1023.98 px pasa a una columna donde antes habia dos (dashboard, configuracion, conciliacion, backups, extractos y login), la cabecera de Extractos colapsa a columna a partir de 1181 px y Configuracion recupera 3 columnas entre 1200 y 1280 px.
- **D-5:** la capa de parches `system-coherence.css` **deja de existir** (428 lineas repartidas). Nace `src/styles/layout/page.css` con las clases compartidas `.page` (+ `.page--wide`), `.page-header` (+ `--plain` para Conciliacion, `--static` para Papelera y Alertas, `--stacked` para Importacion) y `__title`/`__lead`/`__subtitle`, que se anaden a las 19 pantallas y 21 cabeceras sin quitar las clases propias. Las 6 reglas duplicadas de la cabecera desaparecen: **2.062 reglas CSS quedan byte a byte identicas** y las 77+14 que cambian son todas renombres de selector. Un resolutor de cascada por elemento (141 comprobaciones) da **cero diferencias de valor**. La validacion visual destapo un defecto real que el comparador de reglas no veia: un `<div class="auditoria-header auditoria-actions-header">` reutilizaba la clase de cabecera y perdia 12 px de padding y su borde (la tabla de Auditoria subia 13 px); corregido anadiendole `page-header`. Detalle en su entrada al final de este fichero.
- **D-7:** 265 tokens sin uso borrados (493 nombres definidos -> 228; 296 sitios de declaracion) en `variables.css` y `design-system/tokens/*`, con inventario que comprueba `var(--nombre)` y el literal desnudo en `src/`, `tests/`, `e2e/`, `public/` e `index.html` y que sigue las cadenas de alias. **El bloque `[data-theme="dark"]` de `variables.css` no era un duplicado**: `variables.css` importa `themes.css` antes de su propio `:root`, asi que su `:root` (misma especificidad) gana por orden de fuente. Probado en Chromium: sin ese bloque `--text-primary` pasa de `#f5f5f7` a `#1d1d1f` y `--amount-positive` de `#78c79a` a `#176a3a`. Se mantiene con 6 tokens y solo se borra `--text-link`, que si era redundante. 31 tokens x 2 temas (62 valores computados) dan el mismo resultado antes y despues.
- **D-8:** el campo de color del grafico valida el hexadecimal (`#RGB`/`#RRGGBB`/vacio) y no guarda nada invalido; el valor por defecto sale de los tokens `--chart-*` del tema activo, con paleta (`datalist`) y error junto al campo. `src/utils/chartColors.ts` (nuevo) resuelve el hexadecimal de un token a `var(--chart-N)` para que el color siga al tema; `EvolucionChart` lo consume. Test nuevo `tests/chartColors.test.ts`.
- **D-9:** los 10 modales montados a mano de 8 ficheros pasan a `<Modal>` (`UsuarioModal`, los 3 de confirmacion de `UsuariosPage`, `TitularesPage`, `CuentasPage`, `PaisesPage`, el de importacion de `CuentaDetailPage`, el overlay de restauracion de `BackupsPage` y el aviso de sesion de `SessionTimeoutWarning`). `<Modal>` gana `variant`, `panelClassName`, `headerClassName`, `subtitleClassName`, `subtitleId`, `bareBody` y `closeButtonClassName` para no perder el aspecto legacy. La validacion visual destapo un defecto real (el boton de cierre aparecia en cuatro modales que antes no lo tenian) y se corrigio con `showCloseButton={false}`. Test nuevo `tests/modalesUnicos.test.ts`.
- **D-10:** la premisa de los «23 `<th>` sin `scope`» estaba obsoleta (los 146 `<th>` del repo ya lo tienen). Lo real: 22 `<caption className="sr-only">` nuevos (de 2 a 24 tablas con nombre accesible, sin impacto visual), los 3 saltos `h3` -> `h2` de `ImportacionPage` y los **19 componentes compartidos que colgaban directamente del `h1`** (`EmptyState` y `KpiCard` pintaban un `h3` fijo): ambos aceptan ahora `headingLevel="h2"` y el CSS mantiene el tamano (21 px) en los dos niveles, de modo que la jerarquia se corrige sin cambiar el aspecto. Tests nuevos `tests/semanticaTablas.test.ts` y `tests/nivelesEncabezado.test.ts`.
- **D-11:** 29 usos de `showSign` hacen explicito el «+» del positivo. Las magnitudes con etiqueta propia («Ingresos», «Egresos», umbral minimo) y los negativos no se tocan. La guarda `sinSignoPrevio` (para no duplicar un «+» que ya traiga el dato crudo) pasa a `src/utils/formatters.ts` y se aplica en Importacion, AuditCellModal y **Auditoria** (esta ultima se habia quedado sin guarda: el mismo dato se pintaba «++1234,56» en la pantalla y sin «++» en el modal).
- **D-12:** `--text-16/20/32` eran alias muertos de `--text-17/21/34`: se borran. El 600 y el 700 tienen ya un solo nombre (`--weight-semibold` y `--weight-bold`): se retiran `--weight-medium` y `--font-weight-heavy` y sus 54 usos se renombran.
- **M-1 / M-2 / M-12 (backend):** `TendenciasAnomaliasService` (237 lineas) y su fichero de tests borrados (no estaba registrado en DI: la aplicacion no podia alcanzarlo); `AppendPeriodSummary_ObsoleteKeptForBuild` y la sobrecarga de 6 parametros de `AppendPeriodSummaryAsync` borrados (los 5 `await` restantes usan la de 4); `ICacheMetricsSink` borrada.
- **M-6:** 50 clases CSS sin uso borradas (la cifra del informe era una estimacion): restos de una rejilla tipo Excel (`.account-excel-*`, `.account-col-*`, `.account-cell-*`), la familia `.ab-*` de badges sin usar y varias clases del chat de IA. Se conservaron las que se construyen por concatenacion.
- **M-10:** la premisa estaba a medias (`.test-dist-v2/` ya estaba cubierto por `.test-dist-*/`); lo que faltaba era `frontend/playwright-report/`, que se anade al `.gitignore` y a los `ignores` de `eslint.config.js`. Se anaden tambien `pw-*/`, `test-results/` y `.l8-*` para que las capturas de validacion visual no acabar en un `git add -A`.
- **M-11:** `design-tokens.css` y la carpeta anidada `Documentacion/Diseno/Diseño/` **archivados** (movidos, no borrados) en `Documentacion/Diseno/Archivo/` con un `README.md` que explica el motivo.
- **M-13:** inventario con grep de cada simbolo de `src/types/index.ts` y `src/utils/*.ts`: los «solo interno» pierden el `export` y los «muertos» se borran. Al quitar el `export` de `errorToast.ts` desaparecia el `import type { AxiosRequestConfig } from 'axios'` que sostenia la ampliacion `declare module 'axios'` y el programa aislado de tests fallaba con TS2664: se resuelve con `import type {} from 'axios'`, sin dependencia en tiempo de ejecucion.
- **M-14:** `README_RELEASE.md` pasa a `V-03.02` y `Check-VersionAlignment.ps1` lo verifica con el patron de las demas fuentes; `scripts/install.ps1` lee la version del fichero `VERSION` y falla con mensaje claro si no existe.
- **M-15:** los tres pares de `.ps1` ingles/espanol ya eran alias finos y los tres `Start-*Dev.ps1` hacen cosas distintas: no se unifican. Lo que si era duplicado es la capa `.cmd`: `install.cmd` y `update.cmd` pasan a delegar en `Instalar Atlas Balance.cmd` y `Actualizar Atlas Balance.cmd`, que son los canonicos. `start.cmd` y `Atlas Balance.cmd` no se fusionan (resuelven el `InstallPath` de forma distinta). Nada se borra.
- **M-17:** 18 informes anteriores a V-03 movidos a `Documentacion/Archivo/` con indice; `DOCUMENTACION_CAMBIOS.md` pasa de **23.565 a 614 lineas** (solo V-03.02) y el historico de V-03.01 y anteriores queda en `DOCUMENTACION_CAMBIOS_ARCHIVO.md` (23.030 lineas). 18 referencias de ruta actualizadas.
- **R-9:** el CSS de cada ruta se carga desde su modulo lazy: 19 hojas nuevas en `src/styles/layout/routes/` y un `import` por pagina. El **CSS inicial baja de 228.149 a 163.965 B** (-28,1 % raw, -23,3 % gzip), pero **el total del bundle sube 9 KB gzip** (20 ficheros comprimen peor que 1): R-9 reparte el CSS en vez de reducirlo. Al dejar de ser global el orden de la cascada, **41 reglas se quedaron congeladas** en las hojas globales (las que al moverse invertian el orden con otra regla y cambiaban el valor final). Verificado con un resolutor de cascada propio (25 unidades x 32 contextos, 78.869.024 comprobaciones de propiedad con las combinaciones reales de clases del JSX) que da **0 diferencias**, y con una comparacion A/B de capturas sobre el mismo arbol en la que solo difieren las marcas de tiempo del mock. `layout/conciliacion.css` se borro (sus 12 reglas eran todas de esa ruta).
- **T-1 / T-2:** job `e2e` nuevo en `ci.yml` (solo push a `main`) y e2e visuales tambien en `release.yml`; `e2e/admin-smoke.spec.ts` excluido de forma explicita. Antes de esto los tres specs visuales fallaban por fixtures obsoletos en `e2e/visual/mockApi.ts` (`columnas_restringidas: []` en vez de `null`, que dejaba la fila sin celdas, y `/conciliacion` devolviendo un array en vez de `PaginatedResponse`): con el mock corregido, 29 pasan y 1 queda saltado. Los cinco `*.Tests.ps1` que no corrian se anaden al job `policy-gates` (tres en la matriz completa y dos solo en `windows-2022`, con el motivo en la cabecera de cada script).
- **Arnes visual:** `e2e/visual/capture.spec.ts` pasa de 9 a 21 rutas (mas fixtures nuevos en `mockApi.ts`) y de 2 a **3 anchuras**: 1440 (escritorio), **1024 (tablet)** y 390 (movil), en claro y oscuro, de modo que la reorganizacion del CSS se puede validar en escritorio, tablet y movil. 134 capturas por pasada.
- Archivos frontend: `src/utils/formatters.ts`, `src/utils/chartColors.ts` (nuevo), `src/components/common/PageSkeleton.tsx`, `src/components/common/Modal.tsx`, `src/components/usuarios/UsuarioModal.tsx`, `src/components/extractos/{AuditCellModal,EditableCell,ExtractoTable}.tsx`, `src/components/dashboard/{EvolucionChart,SaldoPorDivisaCard,ConcentracionDonutCharts}.tsx`, `src/components/auditoria/IntegrationAuditTable.tsx`, `src/components/integraciones/TokenList.tsx`, `src/components/auth/SessionTimeoutWarning.tsx`, `src/pages/*.tsx` (25), `src/styles/**`, `src/types/index.ts`, `src/utils/*.ts`, `e2e/visual/{capture.spec.ts,mockApi.ts}`, `e2e/README.md`, `package.json`, `tsconfig.test.v2.json`, `eslint.config.js`. Tests nuevos: `formatPercent`, `copyYColoresConfiguracion`, `chartColors`, `importesConSigno`, `estadoCargaUnico`, `modalesUnicos`, `semanticaTablas`, `nivelesEncabezado`.
- Archivos backend: `src/AtlasBalance.API/Services/AtlasAiService.cs`, `src/AtlasBalance.API/Caching/CacheMetrics.cs` (modificados); `src/AtlasBalance.API/Services/IaPlanner/TendenciasAnomaliasService.cs` y `tests/AtlasBalance.API.Tests/TendenciasAnomaliasServiceTests.cs` (borrados).
- Scripts y workflows: `Atlas Balance/scripts/{Check-VersionAlignment,install,update,start}.ps1`, `Atlas Balance/{install,update,start}.cmd`, `Atlas Balance/{Instalar,Actualizar} Atlas Balance.cmd`, `Atlas Balance/Atlas Balance.cmd`, `Atlas Balance/README_RELEASE.md`, `Atlas Balance/scripts/{Mfa-Totp,ServiceSecurity,Sync-AtlasDirectory,Installer-SecretPreservation,Smoke-Test-AtlasBalance}.Tests.ps1` (comentarios), `.github/workflows/{ci,release}.yml`.
- Documentacion: `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md` (estado por ID), `Documentacion/DOCUMENTACION_TECNICA.md`, `Documentacion/DOCUMENTACION_CAMBIOS.md` (esta entrada), `Documentacion/Versiones/v-03.02.md`, `Documentacion/Diseno/DESIGN.md` (tokens reales), `Documentacion/Archivo/` (18 informes + indice), `Documentacion/DOCUMENTACION_CAMBIOS_ARCHIVO.md`.
- Comandos: `dotnet test "tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" -c Release --artifacts-path artifacts-l8-final` (1282/1282) y `...AtlasBalance.Caching.Tests...` (15/15), siempre con la carpeta de compilacion DENTRO del repo (fuera, los 14 tests que leen ficheros de codigo fallan con `FileNotFoundException` por el localizador de fuentes); `npx tsc --noEmit`, `npx eslint src tests --max-warnings 0`, `npx tsc -p tsconfig.test.v2.json --outDir .test-dist-l8-*` + `node --test --test-force-exit` (**332/332** pruebas unitarias), `npm run build` con `VITE_BUILD_OUT_DIR` nueva, `npx playwright test e2e/visual` con `--output` y `VISUAL_OUT` nuevos (las carpetas `test-results/` y `playwright-report/` tienen permisos heredados de otra cuenta y dan EPERM), y `python .l8-css2-diff.py` para comparar capturas pixel a pixel.
- Incidente del orquestador: un `git checkout -- src/pages/AuditoriaPage.tsx` usado para deshacer una edicion parcial de un script **borro todo el trabajo sin commitear de ese fichero** (esqueleto de carga, clases compartidas, `<caption>`, guarda `sinSignoPrevio` e `import` del CSS de ruta). Se detecto por la comparacion de capturas (38 % de pixeles distintos en la pagina de Auditoria) y se repuso a mano; la recaptura deja la pagina identica a la linea base. Queda registrado en `LOG_ERRORES_INCIDENCIAS.md`.
- Decisiones visuales: el «+» de los importes positivos se anade tambien en saldos y saldos acumulados (la cifra era el unico portador del signo, no solo el color), pero **no** en las magnitudes con etiqueta propia («Ingresos», «Egresos», umbral minimo); los eskeletos de carga son mas altos que la linea de texto que sustituyen, lo que ensancha las secciones durante la carga; el esqueleto de tabla no cuadra con el numero real de columnas en `ExtractoTable` y `RevisionPage` (6 por defecto); las 22 `<caption>` van en `sr-only` para no mover ni un pixel; y el hueco de `aria-busy` en el overlay de restauracion se ha rellenado con un `aria-live="assertive"` sin `aria-busy` (en una region viva, `aria-busy` difiere el anuncio indefinidamente).
- Pendientes de diseno: el `role="alertdialog"` del overlay de restauracion, que ahora es `role="dialog"` (exigido por el componente comun; recuperarlo exigiria un modal propio); el `columns` por defecto del esqueleto de tabla (6, y en `ExtractoTable` y `RevisionPage` el numero real de columnas es variable); y el nombre `.users-pagination`, que sigue mintiendo porque lo usan 14 paginas: su renombrado a `.pagination` es un paso aparte y reversible, no aplicado. La banda tablet (901-1023.98 px) **ya no es un hueco**: el arnes captura 1024 x 768 y se reviso.
- Bloqueos: los comandos canonicos `npm run test:unit` y `npm run build` siguen fallando por permisos heredados de `frontend/.test-dist/` y `frontend/dist/`, que pertenecen a otra cuenta: el usuario ejecutor (`trakeria\usuario`) solo tiene `RX` sobre ellas, `icacls /grant` responde «Acceso denegado» y tampoco se pueden mover de sitio. El error es `TS5033 EPERM` al escribir, no un fallo de codigo: las mismas comprobaciones pasan con carpetas de salida nuevas (`--outDir` y `VITE_BUILD_OUT_DIR`). Sin sesion autenticada real ni backend en contra. Nada commiteado.

# 2026-11-18 - V-03.02 - Lote 8 del informe de revision integral (M-14, M-15)

- Trabajo: implementacion de M-14 (alineacion de `README_RELEASE.md` y de la version en `scripts/install.ps1`) y M-15 (unificacion de lanzadores en espanol con los ingleses como alias finos que delegan). Sin commit, sin push y sin cambio de version. M-16 (los dos `.gitignore`) no se ha tocado: solo se propone.
- M-14: `README_RELEASE.md` pasa de `V-02-03` a la version real `V-03.02` (titulo) y `V-03-02` en el nombre del ZIP (`AtlasBalance-V-03-02-win-x64.zip` y la carpeta `C:\Temp\AtlasBalance-V-03-02-win-x64`). La version real se leyo de `Atlas Balance/VERSION` (`V-03.02`), `Directory.Build.props` (`3.2.0` / `InformationalVersion V-03.02`) y `frontend/package.json` (`3.2.0`); no se cambio el numero de version.
- M-14: `scripts/Check-VersionAlignment.ps1` comprueba ahora `README_RELEASE.md` con el mismo patron que el resto de fuentes: extrae todas las apariciones de version del fichero (`V-\d{2}[-.]\d{2}`) y las mete en la tabla `$values` como `README_RELEASE.version[n]`, de modo que cada una se normaliza con `Normalize-AtlasVersion` y se compara con la version esperada. Si el README contiene una version distinta, el script falla con el mensaje estandar `Fuentes de version desalineadas: - README_RELEASE.version[0] = '...' (esperado '...')`.
- M-14: `scripts/install.ps1` ya no escribe la version a mano. Lee `VERSION` de la raiz del paquete, falla con mensaje claro si el fichero no existe o su contenido no tiene el formato `V-XX.XX`, y compone el nombre del ZIP con el valor leido. Como consecuencia, la comprobacion de `install.ps1` en `Check-VersionAlignment.ps1` cambia de "extraer un literal" a "debe leer el archivo VERSION y no debe reintroducir un literal de version" (mismo estilo de mensajes y mismo aggregating de `$issues`).
- M-15: comparacion previa de cada pareja. Los `.ps1` ingleses ya eran alias finos (no duplicados): `install.ps1` (54 lineas) delega en `Instalar-AtlasBalance.ps1` (2105), `update.ps1` (78) en `Actualizar-AtlasBalance.ps1` (1157) y `start.ps1` (17) en `Launch-AtlasBalance.ps1` (147). Los tres `Start-*Dev.ps1` hacen cosas distintas: `Start-Dev.ps1` usa Docker Compose y llama a `Start-BackendDev.ps1`; `Start-BackendDev.ps1` arranca solo la API (DLL compilado, health check); `Start-LocalDev.ps1` usa el PostgreSQL portatil de `tools/pgsql` sin Docker y redirige logs a `logs/dev`. No se unificaron porque no son duplicados.
- M-15: en la capa `.cmd`, `install.cmd` y `update.cmd` pasan a ser alias finos que delegan con `call` en `Instalar Atlas Balance.cmd` y `Actualizar Atlas Balance.cmd`, que son ahora los lanzadores canonicos y pasan por los wrappers `scripts/install.ps1` y `scripts/update.ps1` (autoelevacion y validacion previa del paquete). El codigo de salida se conserva: `call` propaga `ERRORLEVEL` y los dos `.cmd` siguen cerrando con `exit /b %ERRORLEVEL%`.
- M-15: `start.cmd` NO se fusiona con `Atlas Balance.cmd` porque resuelven la ruta de instalacion de forma distinta y una unificacion romperia uno de los dos casos: `Atlas Balance.cmd` usa la instalacion que contiene el lanzador (relativo, correcto con `-InstallPath` personalizado) y `start.cmd` usa `C:\AtlasBalance` (correcto cuando se ejecuta desde la carpeta del paquete para arrancar la instalacion ya existente, que es donde esta `atlas-balance.runtime.json` con el PostgreSQL gestionado). Ambos terminan en el mismo canonico `Launch-AtlasBalance.ps1`.
- M-15: `README_RELEASE.md` documenta ahora los lanzadores canonicos en espanol y deja los ingleses como alias. `Atlas Balance.cmd` y `start.cmd` se documentan por separado porque no son intercambiables (distinto `InstallPath` por defecto).
- M-15: `start-local-dev.cmd` y los tres `Start-*Dev.ps1` se conservan. `start-local-dev.cmd` no lo referencia nadie (solo aparece citado en el informe), y `Start-LocalDev.ps1` solo lo referencia ese `.cmd`; `Start-Dev.ps1` lo referencian `AGENTS.md` y `Launch-AtlasBalance.ps1`; `Start-BackendDev.ps1` lo referencian `start-backend.bat` y `Start-Dev.ps1`. No se borra nada: queda como propuesta.
- Archivos tocados: `Atlas Balance/README_RELEASE.md`, `Atlas Balance/scripts/Check-VersionAlignment.ps1`, `Atlas Balance/scripts/install.ps1`, `Atlas Balance/scripts/update.ps1`, `Atlas Balance/scripts/start.ps1`, `Atlas Balance/install.cmd`, `Atlas Balance/update.cmd`, `Atlas Balance/start.cmd`, `Atlas Balance/Atlas Balance.cmd`, `Atlas Balance/Instalar Atlas Balance.cmd`, `Atlas Balance/Actualizar Atlas Balance.cmd` y esta bitacora.
- Comandos: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Check-VersionAlignment.ps1"` (desde `Atlas Balance`), el mismo script con `-ExpectedVersion V-03.03` como prueba negativa, parser de PowerShell (`[System.Management.Automation.Language.Parser]::ParseFile`) sobre los cuatro `.ps1` tocados, ejecucion de `install.cmd`, `update.cmd`, `Instalar Atlas Balance.cmd` y `Actualizar Atlas Balance.cmd` desde la carpeta fuente (fallo esperado por no ser paquete de release), comprobacion de existencia de todas las rutas `%~dp0scripts\...` y `%~dp0*.cmd` de los lanzadores, `git diff --stat` y `git status --porcelain`.
- Resultado: `Check-VersionAlignment.ps1` **exit 0** (`Alineacion de version OK: V-03.02 (3.2.0).`). Sin errores de sintaxis en los cuatro `.ps1` tocados. Los cuatro lanzadores de instalacion/actualizacion fallan con el mensaje esperado de "no es el paquete" y **exit 1**, igual que antes del cambio. La prueba negativa con `-ExpectedVersion V-03.03` falla (exit 1) listando `README_RELEASE.version[0..2]`, lo que demuestra que la comprobacion nueva actua.
- Supuestos: el nombre del ZIP sigue el formato de tag (`V-03-02`) que es el que produce el workflow de release por defecto (`version.default`), mientras que el titulo del README usa la version de producto (`V-03.02`) tal cual aparece en `VERSION`; `Normalize-AtlasVersion` trata ambas como la misma version, asi que la comprobacion acepta las dos formas.
- Pendientes: `Instalar Atlas Balance.cmd` y `Actualizar Atlas Balance.cmd` pasan ahora por los wrappers, con lo que ganan la autoelevacion por UAC que antes solo tenian los alias ingleses (el canónico `Instalar-AtlasBalance.ps1` y `Actualizar-AtlasBalance.ps1` siguen lanzando error "Ejecuta este instalador/actualizador como Administrador." si se invocan sin elevar, y `install.ps1` sigue anadiendo `-InstallDependencies` por defecto). El flujo de instalacion real no se ha podido ejecutar en esta maquina (hace falta Administrador, PostgreSQL y servicios), asi que la validacion es de sintaxis, de rutas y de camino de error, no de instalacion completa.
- Propuesta sin ejecutar: archivar `start-local-dev.cmd` (sin referencias) y, si se decide unificar tambien la desinstalacion, crear `Desinstalar Atlas Balance.cmd` como canonico de `uninstall.cmd`; ambos cambios obligan a tocar las listas de ficheros del paquete en `Build-Release.ps1`, `Actualizar-AtlasBalance.ps1` y `WatchdogOperationsService.cs` para que no se pierdan en una actualizacion.
- Bloqueos: ninguno. Nada commiteado.

## 2026-11-16 - V-03.02 - Correccion del LOTE 2 del informe de revision integral (A-2, S-10, S-11, S-13, S-14, S-15, S-16, S-17, S-18, F-1)

### Trabajo realizado

- **Alcance:** los diez IDs del LOTE 2 de `INFORME_REVISION_INTEGRAL_2026-11-16.md`: A-2, S-10, S-11, S-13, S-14, S-15, S-16, S-17, S-18 y F-1. Ningun otro ID tocado, sin commit ni push, sin cambio de version.
- **Metodo:** el orquestador verifico las premisas leyendo el codigo citado y fijo con el usuario cuatro decisiones de producto (el bloqueo solo impide logins nuevos; vida maxima de sesion de 30 dias; el modo TLS del SMTP es explicito y admite envios sin cifrar; ventana de gracia con IP coincidente para el refresh concurrente). Cuatro subagentes implementadores con ficheros disjuntos escribieron el test que falla, el arreglo y el test en verde; el orquestador reviso cada diff, corrigio tres defectos introducidos por los subagentes (BOM comido en los `appsettings`, JSON invalido en las dos plantillas y cookie CSRF legacy leida por el frontend) y ejecuto las suites completas.
- **A-2:** el contador de fallos vuelve a 0 cuando el bloqueo ya caduco (login, verificacion MFA y cambio de contrasena), el incremento en BD es atomico y el bloqueo deja de cortar las sesiones ya abiertas: se retiran el 423 del refresh (`AuthService.RefreshTokenAsync`) y el rechazo por `LockedUntil` del `UserStateMiddleware`. Nuevo `POST /api/usuarios/{id}/desbloquear` (solo ADMIN) con auditoria `ACCOUNT_UNLOCKED`; el listado expone `bloqueado_hasta` e `intentos_fallidos` y la pagina de Usuarios muestra el estado de bloqueo con el boton "Desbloquear" y su confirmacion.
- **S-10:** antes de guardar el secreto MFA se comprueba que el usuario sigue sin MFA; si ya lo tiene, se invalida el desafio y se responde 401 sin sobrescribir el secreto.
- **S-11:** el contador de `USUARIOS.failed_login_attempts` se incrementa con una sola sentencia (`ExecuteUpdate` con reinicio a 0 si el bloqueo caduco) y la entidad trackeada se recarga, de modo que una rafaga paralela no pierde incrementos. El limite por IP se aplica tambien a cuentas que ya existen, con umbral propio (`LoginMaxFailuresPerIpExistingUser` = 20) y contador de cache separado, para no expulsar a toda la oficina que comparte IP detras de un proxy inverso.
- **S-13:** nueva columna `sesion_ini_en` en `REFRESH_TOKENS` (migracion `20261116210000_AddSesionIniEnRefreshToken`) que ancla la sesion y se hereda en cada rotacion. A los 30 dias (`JwtSettings:MaxSessionDays`) la sesion se revoca, se audita `SESSION_EXPIRED` y el usuario vuelve a iniciar sesion con MFA.
- **S-14:** en produccion solo se lee la cookie con prefijo `__Host-` (access token en `Program.cs`, `AuthController.ReadCookie` y cookie CSRF en `CsrfMiddleware`); en Development se aceptan las dos. El frontend pasa a leer siempre la cookie `__Host-` cuando existe, para que una cookie legacy que sobreviva en el navegador no provoque 403 al guardar.
- **S-15:** ventana de gracia de 30 s (`RefreshReuseGraceSeconds`) para el reintento concurrente: si el token reutilizado se acaba de rotar, su reemplazo sigue vivo y la IP coincide, se emite un token nuevo de la misma sesion sin rotar el `SecurityStamp` ni revocar otras sesiones (auditoria `REFRESH_TOKEN_REUSE_BENIGN`). Fuera de la ventana, la revocacion por reuso anti-robo queda intacta.
- **S-16:** la deduplicacion de `SecurityAuditMiddleware` ya no incluye la ruta: se deduplica por tipo de accion, IP y usuario dentro de la ventana, de modo que un escaner que prueba rutas distintas no llena `AUDITORIAS`.
- **S-17:** el modo TLS del SMTP pasa a ser explicito y configurable (`smtp_tls_mode`: `required`, `auto`, `none`), con semilla, DTO, validacion en `ConfiguracionController` y selector en Configuracion. Desaparece la heuristica que deducía el cifrado de si habia credenciales, y cuando el mensaje sale sin cifrar se escribe un warning en el log del servidor (host, puerto y modo; nunca contrasena ni cuerpo).
- **S-18:** las respuestas de `/api/*` llevan `Cache-Control: no-store` (`SecurityResponseHeaders.ApplyCacheControl`), sin pisar un `Cache-Control` ya puesto. Los estaticos de `wwwroot` conservan su politica de cache.
- **F-1:** la sesion solo se cierra cuando la renovacion del token responde 401/403. Un error de red, un 5xx, un 429, un timeout o un error local rechazan la peticion y avisan con "No se pudo renovar la sesion" sin limpiar estado ni redirigir a `/login`. El coordinador de refresh distingue el fallo invalidante del transitorio (`transient_error`), de modo que las demas pestanas tampoco se expulsan por un fallo que no invalida la sesion.

### Archivos tocados

- Backend: `src/AtlasBalance.API/Services/AuthService.cs`, `src/AtlasBalance.API/Middleware/UserStateMiddleware.cs`, `src/AtlasBalance.API/Controllers/UsuariosController.cs`, `src/AtlasBalance.API/DTOs/UsuariosDtos.cs`, `src/AtlasBalance.API/Models/Entities.cs`, `src/AtlasBalance.API/RateLimiting/RateLimitingOptions.cs`, `src/AtlasBalance.API/Constants/AuditActions.cs`, `src/AtlasBalance.API/Migrations/20261116210000_AddSesionIniEnRefreshToken.cs` (nuevo) y `AppDbContextModelSnapshot.cs`, `src/AtlasBalance.API/Program.cs`, `src/AtlasBalance.API/Controllers/AuthController.cs`, `src/AtlasBalance.API/Middleware/CsrfMiddleware.cs`, `src/AtlasBalance.API/Middleware/SecurityAuditMiddleware.cs`, `src/AtlasBalance.API/Middleware/SecurityResponseHeaders.cs` (nuevo), `src/AtlasBalance.API/Services/EmailService.cs`, `src/AtlasBalance.API/Data/SeedData.cs`, `src/AtlasBalance.API/DTOs/ConfiguracionDtos.cs`, `src/AtlasBalance.API/Controllers/ConfiguracionController.cs`, `src/AtlasBalance.API/appsettings.json` y sus dos plantillas.
- Tests backend: nuevos `AuthLot2Tests.cs`, `AuthLot2PostgresTests.cs` (PostgreSQL real), `SecurityLot2Tests.cs`, `SmtpTlsModeTests.cs` y `AppsettingsValidezTests.cs`; ajustes puntuales en `AuthServiceTests.cs`, `CsrfMiddlewareTests.cs`, `AuthControllerTests.cs`, `SecurityAuditMiddlewareTests.cs` y `SerilogHttpClientLoggingTests.cs`.
- Frontend: `src/services/api.ts`, `src/services/sessionRefreshCoordinator.ts`, `src/utils/sessionErrors.ts` (nuevo), `src/utils/usuarioBloqueo.ts` (nuevo), `src/pages/UsuariosPage.tsx`, `src/pages/ConfiguracionPage.tsx` (seccion SMTP), `src/App.tsx` (cookie CSRF), `tests/sessionErrorHandling.test.ts` (nuevo), `tests/usuarioBloqueo.test.ts` (nuevo), `tests/sessionRefreshCoordinator.test.ts`, `tsconfig.test.v2.json`, `package.json`, `eslint.config.js` y `.gitignore` (higiene de artefactos: `.test-dist-*`, `dist-*` y `.artifacts/` ya no se versionan ni se lintean).
- Documentacion: `INFORME_REVISION_INTEGRAL_2026-11-16.md` (estado por ID y seccion 15), `LOG_ERRORES_INCIDENCIAS.md`, `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md`, `Versiones/v-03.02.md` y este fichero.

### Comandos ejecutados

- Backend: `dotnet test -c Release --artifacts-path "...\.artifacts\l2d"` (el canonico `dotnet test -c Release` falla porque una API de desarrollo viva, PID 17816, bloquea `bin\Release\net10.0\AtlasBalance.API.dll`), con `--filter-class` para correr subconjuntos (en xunit v3 sobre Microsoft Testing Platform el `--filter` de VSTest se ignora).
- Frontend: `npm.cmd run lint`, `npx.cmd tsc --noEmit`, `npx.cmd tsc -p tsconfig.test.v2.json --outDir .test-dist-l2` + `node --test` sobre los 19 ficheros de test, `npx.cmd vite build --outDir .dist-l2`.
- `npm run test:unit` y `npm run build` canonicos siguen fallando en esta maquina por permisos heredados de otra cuenta de sandbox (`.test-dist/` y `dist/`): los ficheros de esas carpetas no se pueden mover a la papelera ("La funcion no es compatible con este sistema"). Las mismas comprobaciones pasaron usando carpetas de salida nuevas. `npm run lint` ya pasa en verde tras ignorar `.test-dist-*` y `dist-*`.

### Resultado de verificacion

- **Verificado:** backend `AtlasBalance.API.Tests` 1170/1170 y `AtlasBalance.Caching.Tests` 15/15, sin fallos. Cada ID con su propio test en rojo antes del arreglo y en verde despues, incluidos los dos de concurrencia contra PostgreSQL real (Docker 29.9.2 disponible). Frontend: `npm run lint` 0 errores, `tsc --noEmit` 0 errores, 129/129 pruebas unitarias (123 previas + 6 nuevas) y build de Vite correcto.
- **Bloqueado:** login autenticado real en la app (MFA activo y sin credenciales de desarrollo), validacion visual en navegador, envio real de correo contra un servidor SMTP y comportamiento del limite por IP detras de un proxy inverso real. Nada de eso se da por validado.
- **Pendiente / no tocado (fuera de LOTE 2):** la deriva entre modelo y snapshot de EF sigue viva (el siguiente `dotnet ef migrations add` generara una migracion sucia con cambios ajenos); el desbloqueo administrativo limpia el estado en BD pero no los contadores de rate limit en memoria (hasta 15 minutos); la rama de cuenta bloqueada de `LoginAsync` sigue invocando `RecordLoginFailure`; `SecurityAlertService` mantiene su propia logica de "cuentas por IP" que solapa con S-11; y el error local "La respuesta de refresh pertenece a una sesion anterior" cae en la rama transitoria con un mensaje de conexion que no describe el problema real.

### Decisiones visuales

- Ninguna hoja de estilo nueva. El badge de bloqueo y el boton "Desbloquear" reutilizan las clases existentes de la pagina de Usuarios (`users-badge`, `users-badge--danger`, `users-confirm-modal`), igual que el modal de revocacion de MFA que ya estaba. El selector de modo TLS reutiliza el `AppSelect` de la seccion SMTP. No se ha hecho validacion visual en navegador.
## 2026-11-18 - V-03.02 - Correccion del LOTE 6 del informe de revision integral (S-1, S-2, S-3, S-5, S-7, S-8, S-9, M-3, M-5, R-3, D-13 y seccion 8.1)

### Trabajo realizado

- **Alcance:** los IDs del LOTE 6 de `INFORME_REVISION_INTEGRAL_2026-11-16.md`: S-1, S-2, S-3, S-5, S-7, S-8, S-9, M-3, M-5, R-3, D-13 y las dos pantallas que faltaban de la seccion 8.1. Ningun otro ID tocado, sin commit ni push, sin cambio de version.
- **Comprobacion previa:** el lote da por hecho que el LOTE 1 (A-1, permisos en `UsuarioModal`) ya esta aplicado. **Comprobado de punta a punta**: los cuatro flags que faltaban estan en el formulario (`UsuarioModal.tsx`), en el payload (`utils/permisosFormUtils.ts`), en el DTO (`SavePermisoUsuarioRequest`) y en la persistencia (`UsuariosController.cs`). No hizo falta tocar nada de A-1.
- **Decisiones de producto:** el cuestionario se respondio por **auto-seleccion de tiempo** (60 s por pregunta), asi que **ninguna esta confirmada por el usuario** y todas siguen siendo ajustables: S-1 (escribir no da lectura de saldos), S-2 (las columnas del admin son una restriccion), S-3 (revertir exige "eliminar lineas"), S-5 (PUT estricto, sin PATCH) y 8.1 (retirar los endpoints sin llamada). Los puntos exactos donde revertir cada una estan en `LOG_ERRORES_INCIDENCIAS.md`.
- **Metodo:** el orquestador confirmo las premisas leyendo el codigo citado y corrigio dos cosas del informe antes de tocar nada: (a) en S-2 **no existe el concepto de "columnas ocultas"**: hay dos *whitelists* (`columnas_visibles` y `columnas_editables`) que el administrador y el usuario escribian **en la misma columna de la misma fila** de `PREFERENCIAS_USUARIO_CUENTA`, de modo que el `PUT` del usuario pisaba la regla del administrador y el guardado del administrador borraba la preferencia del usuario; (b) en S-8 la parte de `CuentasController` **no tiene defecto** (ya respondia 403). Siete subagentes implementadores con ficheros disjuntos escribieron el test que falla, el arreglo y el test en verde; el orquestador aplico el cambio de esquema, el espejo en el registro de seguridad y el `sortDir=desc` de `/api/usuarios` que quedaban fuera del alcance de los subagentes.
- **S-1:** el dashboard exige `PuedeVerDashboard` **y** `PuedeVerCuentas` en la misma fila para todo rol no ADMIN (la excepcion de GERENTE, que ignoraba `PuedeVerDashboard`, desaparece) y se elimina `GrantsAccountDataAccess`, que concedia lectura con cualquier flag de escritura. ADMIN conserva el bypass.
- **S-3:** `RevertirLoteAsync` usa la accion `EliminarLineas` (`puede_eliminar_lineas`); antes bastaba `puede_importar` o `puede_aprobar_importaciones`. 404 (lote inexistente) != 403 (sin permiso).
- **S-9/M-3:** las reglas de permiso por accion tienen **una sola fuente**: el enum `UserAccessAction` y el mapa `PermissionPredicates` de `UserAccessService`, con `CanCuentaAsync` (chequeo por cuenta) y `GetCuentasAccesiblesAsync` (conjunto de cuentas en una query, sin N+1). `ConciliacionService` e `ImportacionService` dejan de reimplementarlas (`ResolveAccessibleCuentas` y el enum privado `ImportacionPermissionMode` desaparecen) y `CanConciliarCuentaAsync`/`CanCerrarConciliacionAsync` pasan a ser estrictos, que es la regla que de verdad se aplicaba. Sin cambio observable de permisos salvo S-3.
- **S-2:** columna nueva `columnas_visibles_admin` (jsonb) en `PREFERENCIAS_USUARIO_CUENTA` (migracion `20261118100000_AddColumnasVisiblesAdminPreferencias`, escrita a mano por la deriva previa entre modelo y snapshot). `ColumnasVisiblesAdmin` = regla del administrador; `ColumnasVisibles` + `OrdenColumnas` = preferencia del usuario; `ColumnasEditables` = regla de edicion (ya era solo del administrador). El conjunto efectivo es `admin ∩ usuario`; lo usan el filtro en servidor de `GET /api/extractos` y `GET /api/extractos/columnas-visibles`, y el `PUT` del usuario guarda la interseccion. El guardado de permisos del administrador ya no destruye la preferencia ni el orden del usuario. `permisos[].columnas_visibles` (en `/auth/me` y en el DTO de permisos) pasa a ser la regla del admin: mismo nombre JSON, otro significado.
- **S-5:** `UpdateUsuarioRequest` deja de tener valores por defecto destructivos. Los campos que deben distinguir "enviado" de "no enviado" pasan a ser nulables en el DTO y el controller responde 400 con la lista de campos ausentes (`Faltan campos obligatorios en la actualizacion del usuario: rol, activo.`). Obligatorios: `email`, `nombre_completo`, `rol`, `activo`, `primer_login`, `puede_usar_ia`, `emails`, `permisos`. `CreateUsuarioRequest` no se toca.
- **S-7:** `ExportarCsv` audita a quien extrae (`AUDITORIA_EXPORTACION_CSV`, con el numero de filas y los filtros), después de materializar las filas para que el CSV no se incluya a si mismo; la accion se anade tambien a `SecurityEventLog.AccionesRelevantes`.
- **S-8:** en `CuentasController.ActualizarNotas` **no habia defecto** (403 cuando la cuenta existe y falta permiso, 404 cuando no existe): se confirma con tests y no se toca el codigo. Se revisa `ExtractosController` en el mismo lote. Queda abierto el oraculo de existencia.
- **M-5:** `ExtractosController` inyecta `IUserAccessService` y deja de reimplementar el alcance. El criterio de aceptacion era que los tests de autorizacion existentes cubrieran los casos: `ExtractosControllerTests.cs` los cubre (admin, acceso global, permiso solo-dashboard, filtrado por pais, titular sin acceso, filas borradas, permisos de edicion y de desglose), asi que se hizo.
- **R-3 y seccion 9:** `GET /api/importacion/lotes/{id}/filas`, `GET /api/conciliacion` y `GET /api/conciliacion/movimientos-esperados` devuelven `{ data, total, page, page_size, total_pages }` y admiten `?sortBy`/`?sortDir` (los dos listados de conciliacion ya no se recortan a 500 filas). Se anade ordenacion a paises, auditoria, lotes, comisiones y seguros, y el caso por defecto de cuentas, titulares, formatos y usuarios deja de ignorar `sortDir=desc`. El frontend que consume los dos listados de conciliacion se actualiza en el mismo lote.
- **D-13:** `permisosStore` replica los predicados exactos del backend y anade `canCerrarConciliacionCuenta` y `canRevisarCuenta`; Conciliacion y Revision ocultan las acciones que el backend deniega. `canConciliarCuenta` pasa a usarse (no tenia ningun consumidor).
- **8.1 (pantallas que faltaban):** nueva pantalla de **dispositivos MFA de confianza** (`/seguridad/dispositivos-mfa`, entrada de menu "Dispositivos MFA") para ver y revocar los equipos con MFA recordado, y accion **Revertir** en el historial de lotes de la pantalla de Importacion, que solo aparece con permiso "eliminar lineas" y pide confirmacion.

### Archivos tocados

- Backend produccion: `Services/DashboardService.cs`, `Services/UserAccessService.cs`, `Services/ConciliacionService.cs`, `Services/ImportacionService.cs`, `Services/AuthService.cs`, `Controllers/UsuariosController.cs`, `Controllers/ExtractosController.cs`, `Controllers/AuditoriaController.cs`, `Controllers/ImportacionController.cs`, `Controllers/ConciliacionController.cs`, `Controllers/PaisesController.cs`, `Controllers/RevisionController.cs`, `Controllers/CuentasController.cs`, `Controllers/TitularesController.cs`, `Controllers/FormatosImportacionController.cs`, `Controllers/ExtractosController.cs`, `DTOs/UsuariosDtos.cs`, `DTOs/AuthDtos.cs`, `DTOs/ExtractosDtos.cs`, `Models/Entities.cs`, `Data/AppDbContext.cs`, `Migrations/20261118100000_AddColumnasVisiblesAdminPreferencias.cs` (nuevo), `Migrations/AppDbContextModelSnapshot.cs`, `Constants/AuditActions.cs`, `Logging/SecurityEventLog.cs`.
- Backend tests: nuevos `DashboardServicePermisosLecturaTests.cs`, `PermisosLote6Tests.cs`, `TestUserAccess.cs`, `AuditoriaControllerExportacionCsvAuditoriaTests.cs`, `CuentasControllerAutorizacionNotasTests.cs`, `UsuariosPermisosYCamposObligatoriosTests.cs` y los de R-3; ajustados `DashboardServiceTests.cs`, `UsuariosControllerTests.cs`, `ConciliacionLote5Tests.cs`, `ConciliacionServiceTests.cs`, `ConciliacionServiceToleranciaTests.cs`, `ImportacionLote5Tests.cs`, `ImportacionLote5PostgresTests.cs`, `ImportacionR7Tests.cs`, `ImportacionServiceTests.cs`, `ManualProcessResponseTests.cs`, `RevisionControllerTests.cs` y `ExtractosControllerTests.cs`.
- Frontend: `src/stores/permisosStore.ts`, `src/pages/ConciliacionPage.tsx`, `src/pages/RevisionPage.tsx`, `src/pages/ImportacionPage.tsx`, `src/pages/DispositivosMfaPage.tsx` (nuevo), `src/pages/ExtractosPage.tsx`, `src/pages/CuentaDetailPage.tsx`, `src/utils/dispositivosMfa.ts` (nuevo), `src/utils/revertirLote.ts` (nuevo), `src/utils/navigation.ts`, `src/App.tsx`, `src/types/index.ts`, `src/queries/invalidation.ts`, `tsconfig.test.v2.json`, `package.json` y los tests nuevos `tests/permisosConciliacionRevision.test.ts` y `tests/dispositivosMfaYRevertirLote.test.ts`.
- Documentacion: `INFORME_REVISION_INTEGRAL_2026-11-16.md` (estado por ID y seccion 19), `LOG_ERRORES_INCIDENCIAS.md`, `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md`, `Versiones/v-03.02.md` y este fichero.

### Comandos ejecutados

- Backend: `dotnet build` + `dotnet test "tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" -c Release --artifacts-path "C:\Proyectos\Atlas Balance Dev\.artifacts\l6-final"` -> **1292/1292**; y el proyecto de cache por separado -> **15/15**. El canonico `dotnet test -c Release` falla porque una API de desarrollo viva (PID 17816) bloquea `bin\Release\net10.0\AtlasBalance.API.dll`. En xunit v3 sobre Microsoft Testing Platform el filtro va **despues** de `--` (`-- --filter-class "*Clase*"`), y hay que apuntar al csproj de tests: a nivel de solucion `AtlasBalance.Caching.Tests` se queda con 0 tests y el runner aborta la corrida.
- Frontend: `npx tsc --noEmit` (0), `npx eslint src tests --max-warnings 0` (0), `npm run lint` canonico (0), la suite unitaria con la lista canonica de `package.json` compilada en `.test-dist-l6final` -> **265/265**, y `npx vite build --outDir .dist-l6final` -> correcto (4,12 s).
- Playwright: `npx.cmd playwright test e2e/visual/capture.spec.ts --reporter=list --output=".artifacts\l6-visual\test-results"` con `VISUAL_OUT` a `.artifacts\l6-visual` -> 4 tests pasados y 54 PNG. Chromium (`chromium_headless_shell-1234`) se descargo con `npx playwright install chromium` porque no estaba en la maquina.
- `npm run test:unit` y `npm run build` canonicos siguen fallando en esta maquina por permisos heredados de `.test-dist/` y `dist/` (otra cuenta de sandbox): `TS5033 EPERM` al escribir 22 ficheros de `.test-dist/` y `EPERM ... copyfile public/fonts/Geist-Bold.woff2 -> dist/fonts/Geist-Bold.woff2`. Las mismas comprobaciones pasaron con carpetas de salida nuevas.

### Resultado de verificacion

- **Verificado:** backend `AtlasBalance.API.Tests` **1292/1292** (suite completa, incluida la de PostgreSQL real con Testcontainers, 4 m 51 s) y `AtlasBalance.Caching.Tests` **15/15**. Cada ID con su test en rojo antes del arreglo y en verde despues, incluidos los que habia que reproducir (S-7) y los que habia que descartar (S-8 en `CuentasController`). Frontend: `npx tsc --noEmit` sin errores, `eslint src tests --max-warnings 0` sin errores, `npm run lint` (canonico) en verde, **265/265** pruebas unitarias (los 40 ficheros de la lista canonica de `package.json`) y build de Vite correcto (4,12 s). Capturas Playwright de las pantallas nuevas con la API simulada: **54 PNG** en `.artifacts/l6-visual` (la pantalla de dispositivos MFA, el historial de importacion con el boton "Revertir" y los dos dialogos de confirmacion, en tema claro y oscuro y a 1440 px y 390 px; 4 tests pasados).
- **Bloqueado:** sesion autenticada real y comportamiento con permisos ya dados de alta en una instalacion existente (en especial el efecto de S-1 en gerentes que no tuvieran "Ver dashboard"). El proyecto no tiene entorno de React en los tests unitarios, asi que **no hay test de render**: la cobertura del frontend es la de los helpers y del store, mas las capturas.
- **Pendiente / no tocado:** la migracion de datos que rellene `puede_ver_dashboard` en permisos ya existentes (decision del administrador, no del codigo); el oraculo de existencia que sigue abierto con la regla 403/404; la duplicacion de `ImportacionService.GetContextoAsync`, que mantiene su propia query de cuentas importables; `GetCuentasAccesiblesAsync`, que no se ha probado contra PostgreSQL real; y la retirada de los endpoints sin llamada de la seccion 8.1 (no hay cliente externo en el repositorio, pero la decision no esta confirmada).

### Decisiones visuales

- Ninguna hoja de estilo nueva. La pantalla de dispositivos MFA reutiliza el juego de clases de las paginas de tabla ya existente (`users-page`, `users-table-card`, `users-badge--ok/--danger`, `users-row-actions`); las dos confirmaciones usan el `ConfirmDialog` del proyecto, con tono `reversible` para revocar un dispositivo MFA y `irreversible` para revertir un lote de importacion.
- La paginacion de Conciliacion reutiliza el patron de paginador de Extractos y del detalle de cuenta, con el `PageSizeSelect` que ya existe (50/100/200, porque la tabla no esta virtualizada).
- Sin validacion visual en navegador de las pantallas nuevas: lo que hay son **capturas con la API simulada** (`frontend/e2e/visual/`), no una sesion real.

# DOCUMENTACION DE CAMBIOS

## 2026-11-16 - V-03.02 - Correccion del LOTE 3 del informe de revision integral (A-3, A-4, J-1, J-2, J-3, J-4, J-5, J-6, R-1, S-19)

### Trabajo realizado

- **Alcance:** los diez IDs del LOTE 3 de `INFORME_REVISION_INTEGRAL_2026-11-16.md`: A-3, A-4, J-1, J-2, J-3, J-4, J-5, J-6, R-1 y S-19. Ningun otro ID tocado, sin commit ni push, sin cambio de version.
- **Metodo:** el orquestador confirmo las diez premisas leyendo el codigo citado (todos los hallazgos existen tal como los describe el informe) y repartio el trabajo en siete subagentes implementadores con ficheros disjuntos, uno por bloque de IDs. Cada subagente escribio el test que falla con el defecto presente, el arreglo minimo y el test en verde. El orquestador reviso cada diff linea a linea, corrigio por su cuenta un defecto que un subagente no vio (el recupero del backup tras una ejecucion interrumpida abandonaba tambien los backups MANUALES en curso) y termino el trabajo de un subagente que se quedo atascado sin escribir nada durante 27 minutos (J-4 y J-5).
- **A-3:** `BackupService` guarda el estado del backup con `CancellationToken.None` en los tres puntos que lo hacen: con el token de la peticion, ya cancelado al apagar el servicio, la fila se quedaba en `PENDING` para siempre y `BackupSchedulerJob` omitia todos los backups automaticos siguientes por ese motivo. Ahora el planificador abandona los `PENDING` con mas de 2 h y avisa al administrador cuando el ultimo volcado correcto es mas viejo de lo que permite la frecuencia configurada (HOURLY 1 dia, DAILY 2, WEEKLY 8, MONTHLY 32), con anti-spam y sin avisar si el backup automatico esta desactivado.
- **A-4:** el volcado se escribe en `<ruta final>.tmp` y solo se publica con `File.Move` cuando termina bien; `RutaArchivo` guarda siempre la ruta final (la que usan la retencion y la subida a Google Drive) y el nombre pasa a `backup_{yyyy-MM-dd_HHmmss}.dump` tambien en automatico. Dos backups automaticos del mismo dia ya no pueden pisarse.
- **J-1:** `[DisableConcurrentExecution]` y `[AutomaticRetry(3; 60/300/900 s)]` en los jobs de backup (3600 s), exportacion mensual (1800 s), limpiezas (900 s) y alertas de seguridad (600 s). El reintento del backup automatico es real: si la ejecucion anterior quedo en "STARTED", se abandonan los `PENDING` automaticos de esa ejecucion y se relanza el volcado. `SecurityAlertService` evalua desde la marca de la ultima evaluacion correcta (`CONFIGURACIONES.security_alerts_last_evaluated_utc`), asi que un job retrasado ya no deja eventos sin mirar, y la linea base de la regla 6 se mide sobre ventanas del tamaño del rango evaluado.
- **J-2:** `ExportacionService` recibe `ILogger` y registra cada cuenta fallida de la exportacion mensual (id de cuenta, tipo de excepcion y mensaje saneado) mas un recuento final de éxitos y fallos. La cancelacion se propaga en vez de contabilizarse como fallo.
- **J-3:** el aviso de "proximo a vencer" se reintenta mientras el plazo siga en ese estado y no se haya entregado, y el administrador recibe una notificacion por cada plazo del grupo (el digest de email sigue siendo uno por grupo). No se toco la consulta jsonb: es el ID A-9, de otro lote.
- **J-4:** la limpieza de exportaciones solo marca la fila cuando el fichero se borro (o no existia) y no toca ficheros fuera de la raiz configurada en `export_path` (mismo criterio que usa la retencion de backups).
- **J-5:** la limpieza de refresh tokens ignora el filtro global que ocultaba los tokens de los usuarios con soft delete y purga tambien los de esos usuarios, con independencia de su caducidad.
- **J-6:** la auto-actualizacion solo actua dentro de una ventana de madrugada en hora local del servidor (01:00-05:00 por defecto, configurable con `app_update_auto_window_start_local` y `app_update_auto_window_end_local`). Si el servidor no estaba encendido de madrugada, se aplaza al dia siguiente y no reinicia la app en horario laboral.
- **R-1:** la exportacion mensual usa `AsNoTracking`, limpia el `ChangeTracker` entre cuentas (solo en el camino mensual) y calcula el ancho de columnas sobre las primeras 200 filas en vez de las 50.000.
- **S-19:** el cifrado de backups pasa a un formato V3 que autentica el numero de bloques (el AAD del ultimo bloque lo incluye). Un `.enc` truncado en un limite de bloque ya no descifra "bien". Los formatos V1 y V2 quedan byte a byte identicos: los backups ya existentes se siguen restaurando.

### Archivos tocados

- Backend (`src/AtlasBalance.API`): `Services/BackupService.cs`, `Services/BackupConfigurationService.cs` (sin cambios), `Jobs/BackupSchedulerJob.cs`, `Jobs/BackupWeeklyJob.cs`, `Jobs/BackupOperationJob.cs`, `Jobs/ExportMensualJob.cs`, `Jobs/LimpiezaAuditoriaJob.cs`, `Jobs/LimpiezaMfaTrustedDevicesJob.cs`, `Jobs/LimpiezaImportacionLotesJob.cs`, `Jobs/LimpiezaExportacionesJob.cs`, `Jobs/LimpiezaRefreshTokensJob.cs`, `Jobs/SecurityAlertJob.cs`, `Jobs/AutoUpdateJob.cs`, `Services/SecurityAlertService.cs`, `Services/ExportacionService.cs`, `Services/PlazoFijoService.cs`, `Services/BackupEncryptionService.cs`.
- Tests (`tests/AtlasBalance.API.Tests`): nuevos `BackupSchedulerJobTests.cs`, `BackupDumpNamingTests.cs`, `JobConcurrencyAttributesTests.cs`, `LimpiezaRefreshTokensJobTests.cs`; ampliados `BackupServiceTests.cs`, `BackupEncryptionServiceTests.cs`, `SecurityAlertServiceTests.cs`, `ExportacionServiceTests.cs`, `PlazoFijoServiceTests.cs`, `LimpiezaExportacionesJobTests.cs`, `AutoUpdateJobTests.cs`.
- Documentacion: `INFORME_REVISION_INTEGRAL_2026-11-16.md` (estado por ID y seccion 14), `LOG_ERRORES_INCIDENCIAS.md`, `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md`, `Versiones/v-03.02.md` y este fichero.

### Comandos ejecutados

- `dotnet test ".\tests\AtlasBalance.API.Tests\AtlasBalance.API.Tests.csproj" -c Release --artifacts-path ".artifacts\<wA|wB|wC|wD|wE|wF|wG|orch>" -- --filter-class "..."` (el filtro nativo del runner; el `--filter` de VSTest se ignora en este repositorio por usar Microsoft.Testing.Platform, aviso MTP0001). Cada subagente con su propio `--artifacts-path` para no pisarse el `bin`/`obj` compartido.
- Suite completa del backend en `.artifacts\fullsuite`.
- `git diff`, `git status` y `grep` para revisar los cambios y las dependencias del patron de nombres de los backups.

### Resultado de verificacion

- **Verificado:** las doce clases de test del lote en verde (**105/105**) en una ejecucion del orquestador: `BackupServiceTests`, `BackupSchedulerJobTests`, `BackupDumpNamingTests`, `BackupEncryptionServiceTests`, `SecurityAlertServiceTests`, `JobConcurrencyAttributesTests`, `ExportacionServiceTests`, `PlazoFijoServiceTests`, `LimpiezaExportacionesJobTests`, `LimpiezaRefreshTokensJobTests`, `AutoUpdateJobTests` y `RowLevelSecurityTests` (esta ultima con PostgreSQL real y RLS FORCE). Cada ID tiene su test en rojo con el defecto presente y en verde despues. Suite completa del backend: **1170/1170**, sin fallos. Cuando se ejecuto la primera vez (2026-11-16) la suite daba 1160/1162 con dos fallos de `SerilogHttpClientLoggingTests` por JSON invalido en `appsettings.Production.json.template` y `appsettings.Development.json.template`: eran una edicion sin commitear de otra sesion sobre este mismo arbol (faltaba una coma), ajena a este lote, ya resuelta.
- **Bloqueado:** nada de este lote. Sin ejecucion de `pg_dump` real, sin subida real a Google Drive, sin restauracion de un `.enc` de produccion y sin validacion visual (el lote es solo backend).
- **Pendiente / no tocado (fuera de LOTE 3):** el campo "Hora UTC" de la pantalla de Configuracion sigue escribiendo `app_update_auto_hour_utc`, que J-6 deja sin efecto (la ventana local no es editable todavia); las claves `app_update_auto_window_start_local` y `..._end_local` no estan en `SeedData.cs` (el job usa los valores por defecto si no existen); los `.enc` V1/V2 siguen sin autenticar el numero de bloques, limite del formato legacy; la purga en bloque de refresh tokens (`ExecuteDeleteAsync`) no queda cubierta porque el proveedor InMemory no lo soporta; `BackupSchedule.Frequency` no distingue `HOURLY` con intervalo de 1 h de 168 h, asi que el umbral de 1 dia es valido para el intervalo corto pero conservador para el largo.

### Revision posterior del lote (2026-11-17)

A pedido del usuario ("revisa que todo este bien"), el orquestador auditó el lote entregado y encontró dos cosas que faltaban:

- **J-5 no estaba verificado de extremo a extremo.** El test de InMemory comprobaba que filas SELECCIONARIA la purga, pero el proveedor InMemory no soporta `ExecuteDeleteAsync`: nadie sabia si el borrado en bloque de EF Core 10 respeta de verdad el `IgnoreQueryFilters` y el criterio de usuario borrado. Se anadio un test contra PostgreSQL 16 real con RLS FORCE y el rol de runtime (`RowLevelSecurityRetentionJobsTests`): con el codigo anterior a J-5 el token del usuario con soft delete sobrevive ("Expected ... to be 0L because el usuario tiene soft delete: su token no puede seguir vivo, but found 1L") y con el arreglo se borra de verdad. El `ExecuteDelete` si respeta el `IgnoreQueryFilters`.
- **A-4 abria una fuga de datos que el propio arreglo introducia.** Al escribir el volcado en un temporal, un proceso que muere a mitad deja un `<nombre>.dump.tmp` con datos personales que la retencion no puede tocar (solo borra `.dump`): antes de A-4 un volcado a medias acababa en un `.dump` que si se eliminaba; con A-4 se quedaria en disco para siempre. `ApplyRetentionAsync` limpia ahora los `*.tmp` con mas de 2 h (el timeout de `pg_dump` son 30 minutos). Test con fase roja y verde comprobadas.

Tambien se reviso, sin tocar codigo, que las escrituras que hacen los jobs funcionan bajo RLS: `BACKUPS` y `NOTIFICACIONES_ADMIN` tienen policy `FOR ALL USING (atlas_security.is_admin_or_system())` (el job corre en `SystemContextScope`, asi que el UPDATE de A-3 y el INSERT de la alerta de A-3 pasan) y `CONFIGURACION` no tiene RLS (la marca de evaluacion de J-1 se puede escribir). Y que el `GET /api/configuracion` devuelve un DTO curado, no el mapa de configuraciones: la clave nueva no aparece en la pantalla.

### Decisiones visuales

- Ninguna. El LOTE 3 es solo backend: no se toco ninguna hoja de estilo ni ninguna pagina del frontend.

## 2026-11-16 - V-03.02 - Correccion del LOTE 4 del informe de revision integral (A-5, A-6, B-10, B-11, B-16, S-4, S-6, S-22, S-23, S-24, S-25, R-2)

### Trabajo realizado

- **Alcance:** los doce IDs del LOTE 4 de `INFORME_REVISION_INTEGRAL_2026-11-16.md`: A-5, A-6, B-10, B-11, B-16, S-4, S-6, S-22, S-23, S-24, S-25 y R-2. Ningun otro ID tocado, sin commit ni push, sin cambio de version.
- **Decisiones del usuario antes de implementar** (el cuestionario se respondio por tiempo de espera con las opciones propuestas; siguen siendo ajustables): vigencia maxima de un token de integracion de **365 dias** (S-23); quitar `vencimientos` y `auditoria` de los scopes **por defecto** (S-24, solo afecta a tokens nuevos); cuando el plan trae un filtro que la herramienta no puede aplicar, la IA **rechaza la pregunta con un motivo claro** en vez de devolver una cifra sin filtrar (A-6).
- **Metodo:** el orquestador leyo `AGENTS.md`, `version_actual.md`, `v-03.02.md`, el informe y `LOG_ERRORES_INCIDENCIAS.md`, y confirmo las premisas de los hallazgos marcados S antes de encargar nada. Dos subagentes en solo lectura verificaron los nueve IDs marcados S y **corrigieron dos premisas del propio informe**: el rechazo por scope del middleware no escribe notificacion de administrador (solo fila de auditoria) y el defecto real de `Rotar` con scopes vacios era la cadena vacia, no `"[]"`. Tres subagentes implementadores, con ficheros disjuntos, escribieron el test que falla, el arreglo y el test en verde. El orquestador reviso cada diff, cerro los dos huecos que quedaron entre ellos (el concepto no se guardaba en la memoria conversacional y la instruccion del planificador no mencionaba los importes) y ejecuto las suites completas.
- **A-5:** `AiConfiguration.OpenRouterPrivacyProvider(provider, runtimeModel)` es ahora el unico constructor del bloque `provider { zdr, data_collection }` y lo envian las dos salidas al proveedor: la llamada principal y la del planificador semantico. Se mantiene la excepcion de los modelos gratuitos (`openrouter/free`, `*:free`), incompatibles con ZDR.
- **A-6:** el concepto y el rango de importes se aplican en todas las herramientas que operan sobre movimientos (ultimo movimiento, totales, ranking, tendencia, revision, busqueda y comparacion de periodos). Los importes se comparan por magnitud (`Math.Abs(Monto)`): los gastos se guardan en negativo y el usuario razona en magnitudes. El planificador semantico ya acepta `importe_minimo` e `importe_maximo`. El filtro no aplicable (saldos, pendientes) se rechaza con motivo visible en el chat.
- **B-10:** los snapshots de comparacion se agrupan por **divisa** y la comparacion rechaza si los periodos abarcan varias divisas sin que el plan fije una; el ranking aplica el top N por divisa; `DUPLICADO_PROBABLE` exige tres dias de proximidad (`AnomalyDuplicateMaxDays = 3`), asi que una prima mensual legitima ya no se marca; el seguimiento restaura concepto, cuentas y titulares; `filtros.concepto` pasa a ser el termino de busqueda en las operaciones `Search`.
- **B-11:** el timeout del planificador devuelve un rechazo controlado ("El proveedor no respondio a tiempo. Reintenta en unos segundos.") con `SemanticCallAttempted = true`, en vez de subir como HTTP 500; la cancelacion real del llamante se propaga. Los contadores de coste se actualizan bajo un lock de proceso: con ocho peticiones concurrentes el total ya sale exacto.
- **B-16:** nuevo helper `Services/FechasOperativas.cs` (zona `Europe/Madrid`): "hoy", el corte de mes de los contadores y el detector de resumen anual (que llevaba el literal `2026`) dejan de depender de `DateTime.UtcNow`. La revision posterior al cierre del lote extendio el helper a las herramientas del planificador (saldos, tendencia, anomalias) y al ancla del plan (`PlanExecutor`), que seguian con el dia UTC; con un test nuevo que lo comprueba.
- **S-4:** `GET /api/ia/config` solo expone a ADMIN el presupuesto, el coste y los limites globales; para el resto de usuarios esas 17 propiedades se **omiten** del JSON (no se ponen a 0) y quedan su propio uso y si pueden usar la IA. El frontend marca esas propiedades como opcionales.
- **S-6:** las respuestas locales del planificador (ayuda, aclaracion, fallo de ejecucion y respuesta del plan) pasan por el control de limites, incrementan el contador del usuario y dejan fila de auditoria con `origen = "local"`. La ruta determinista no se toca: ya auditaba y contaba.
- **S-22:** el rate limit se aplica antes del chequeo de scope y la notificacion de administrador se emite una vez por token y ventana, no una por rechazo. La fila de auditoria por rechazo se mantiene y su escritura va en try/catch.
- **S-23:** el PUT sin fecha conserva la caducidad (ya no renueva a 90 dias), no se admiten caducidades pasadas ni a mas de 365 dias, `Rotar` rechaza tokens no activos o caducados sin emitir nada y hereda los scopes tal cual.
- **S-24:** `DefaultOpenClawScopes` pasa a ser `titulares, saldos, extractos, evolucion, alertas`. `vencimientos` deja de ser concedible (no tiene endpoint) y `auditoria` sigue siendo concedible de forma explicita, ya no por defecto.
- **S-25:** `[MaxLength]` en el DTO de telemetria (500 / 4000 / 4000 / 200 / 40) y log de `Error` a `Warning`.
- **R-2:** la configuracion de IA se carga una sola vez por peticion (snapshot por `DbContext` scoped) y el mapa de seudonimos se construye una sola vez (`Lazy<Task<AiPseudonymMap>>`).

### Archivos tocados

- Backend: `src/AtlasBalance.API/Constants/AiConfiguration.cs`; `src/AtlasBalance.API/Services/AtlasAiService.cs`; `src/AtlasBalance.API/Services/FechasOperativas.cs` (nuevo); `src/AtlasBalance.API/Services/IaPlanner/SemanticPlannerClient.cs`; `src/AtlasBalance.API/Services/IaPlanner/FinancialToolsService.cs`; `src/AtlasBalance.API/Services/IaPlanner/IntentPlanner.cs`; `src/AtlasBalance.API/Services/IaPlanner/ConversationMemory.cs`; `src/AtlasBalance.API/Services/IaPlanner/IaPlanValidator.cs`; `src/AtlasBalance.API/Services/IaPlanner/PlanExecutor.cs`; `src/AtlasBalance.API/Services/IntegrationTokenService.cs`; `src/AtlasBalance.API/Controllers/TelemetriaController.cs`; `src/AtlasBalance.API/Middleware/IntegrationAuthMiddleware.cs`; `src/AtlasBalance.API/Controllers/IntegracionesController.cs`; `src/AtlasBalance.API/DTOs/IaDtos.cs`; `src/AtlasBalance.API/DTOs/IntegracionesDtos.cs`; `src/AtlasBalance.API/DTOs/TelemetriaDtos.cs`.
- Tests nuevos: `tests/AtlasBalance.API.Tests/IaPrivacidadYContadoresTests.cs`, `IaConfigPermisosTests.cs`, `IaRespuestasLocalesAuditoriaTests.cs`, `IaFechasOperativasTests.cs`, `IaReutilizacionContextoTests.cs`, `FinancialToolsFiltrosTests.cs`, `FinancialToolsDivisaTests.cs`, `ConversationContextFiltrosTests.cs`, `IntentPlannerTimeoutTests.cs`, `IntegrationRateLimitScopeTests.cs`, `IntegrationTokenExpiryTests.cs`, `IntegrationDefaultScopesTests.cs`, `TelemetriaControllerTests.cs`.
- Frontend: `src/types/index.ts` (propiedades globales de `IaConfig` a opcionales, por el contrato de S-4). Sin cambios de UI.
- Documentacion: `INFORME_REVISION_INTEGRAL_2026-11-16.md` (estado por ID y seccion 13), `LOG_ERRORES_INCIDENCIAS.md`, `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md`, `INVENTARIO_DATOS_PERSONALES_V-03.02.md`, `Versiones/v-03.02.md` y este fichero.

### Comandos ejecutados

- Backend: `dotnet build -c Release --artifacts-path "Atlas Balance\backend\.artifacts\orq"` y `AtlasBalance.API.Tests.exe` (suite completa, sin `-class`). En este repositorio el `--filter` de VSTest se ignora (Microsoft.Testing.Platform, aviso MTP0001): el filtro efectivo es `-class` del runner sobre el binario ya compilado. La ruta de artefactos debe quedar **dentro** de `Atlas Balance\backend`: fuera de ahi, `SerilogHttpClientLoggingTests` no encuentra `src/AtlasBalance.API` subiendo desde el binario y falla por `DirectoryNotFoundException` (artefacto del runner, no del codigo).
- Frontend: `npx tsc --noEmit`, `npx eslint src tests --max-warnings 0`, `npx tsc -p tsconfig.test.v2.json --outDir .test-dist-l4` + `node --test` sobre los 19 ficheros de test, `npx vite build --outDir dist-l4`.
- `npm run lint`, `npm run test:unit` y `npm run build` canonicos: siguen bloqueados en esta maquina por permisos heredados de `.test-dist/`, `.test-dist-v2/` y `dist/` (otra cuenta de sandbox); es el problema ya anotado en el log de incidencias y como M-10 del informe. Las mismas comprobaciones pasaron con carpetas de salida nuevas.

### Resultado de verificacion

- **Verificado:** cada ID con su propio test en rojo antes del arreglo y en verde despues (detalle por ID en la seccion 13 del informe). El area del LOTE 4 esta en verde: **60/60** en las catorce clases nuevas de este lote (los trece ficheros nuevos; `IaPrivacidadYContadoresTests.cs` contiene ademas `IaContadoresConcurrenciaTests`), **217/217** en las suites previas de IA y planificador (`AtlasAiService*`, `IaAcceptanceTests`, `IaPlanValidatorTests`, `FinancialToolsServiceTests`, `PlanExecutorTests`, `IntentPlannerTests`, `ConversationMemoryTests`) y **131/131** en las previas de integraciones, telemetria y DTOs (`IntegrationAuthMiddlewareTests`, `IntegrationTokenServiceTests`, `IntegracionesControllerTests`, `IntegrationOpenClawScopeCoverageTests`, `IntegrationOpenClawControllerTests`, `DtoValidationTests`, `ControllerAuthorizationCoverageTests`, `SecurityPolicyTests`, `RateLimitingSetupTests`). PostgreSQL real via Testcontainers en los tests de contadores, reutilizacion de contexto y duplicados. Frontend con `tsc` y `eslint` sin errores, 129/129 pruebas unitarias y build de Vite correcto. Sin llamadas reales a OpenAI, OpenRouter ni MiniMax: los tests usan el handler HTTP de prueba.
- **Suite completa:** `AtlasBalance.API.Tests` da **1171 tests con 0 fallos** en la medicion posterior al cierre de este lote (el total sube conforme las otras sesiones anaden sus pruebas). Durante la aplicacion del lote la suite daba 7 fallos que eran de trabajo en vuelo de otra sesion sobre este mismo arbol (cifrado de backups, limpieza de exportaciones, purga de refresh tokens y las plantillas `appsettings*.json.template`, que tenian un JSON invalido por una coma que faltaba); no se tocaron esos ficheros y, al terminar la otra sesion, la suite quedo en verde.
- **Bloqueado:** los tres comandos canonicos del frontend por el problema de permisos descrito; `dotnet test -c Release` canonico por el bloqueo del binario de la API de desarrollo viva. Sin validacion visual en navegador, sin ejecucion de la app y sin llamadas a proveedores.
- **Pendiente / no tocado (fuera de LOTE 4):** `PermisoEscritura` de los tokens de integracion sigue sin efecto (no hay endpoints de escritura y el flag no se consulta en runtime); el endpoint de `auditoria` de OpenClaw sigue devolviendo `valor_anterior` y `valor_nuevo` (solo deja de concederse por defecto); el lock de los contadores de coste es de proceso y valido para una sola instancia; el corte de dia de los limites puede desviarse una hora en los dos dias del cambio de hora; el `Limite` del ranking es ahora "por divisa"; `M-4` (corte de `AtlasAiService.cs`) sigue fuera de este lote.

### Decisiones visuales

- Ninguna. El unico cambio de frontend es de tipos: las propiedades globales de `/ia/config` pasan a opcionales porque el backend ya no las envia a los usuarios que no son ADMIN. Ninguna hoja de estilos ni componente de UI tocado.

## 2026-11-16 - V-03.02 - Correccion del LOTE 1 del informe de revision integral (A-1, A-7, A-8, B-12, F-3)

### Trabajo realizado

- **Alcance:** los cinco IDs del LOTE 1 de `INFORME_REVISION_INTEGRAL_2026-11-16.md`: A-1, A-7, A-8, B-12 y F-3. Ningun otro ID tocado, sin commit ni push, sin cambio de version.
- **Metodo:** el orquestador verifico cada premisa leyendo el codigo cited antes de encargar el arreglo (A-1, A-7 y B-12 confirmados leyendo el codigo; el resto, leyendo las lineas citadas). Cuatro subagentes implementadores, uno por ID y con ficheros disjuntos, escribieron el test que falla, el arreglo y el test en verde; el orquestador reviso cada diff y ejecuto las suites completas.
- **A-1:** `UsuarioModal` no cargaba ni enviaba `puede_revisar_lineas`, `puede_aprobar_importaciones`, `puede_conciliar` ni `puede_cerrar_conciliacion`, y el backend reemplaza la matriz de permisos con lo que llega, asi que cada guardado los revocaba. El payload de permisos se movio a codigo puro en `utils/permisosFormUtils.ts` (`mapPermisoApiRow`, `buildPermisosPayload`, `permisoHasFlags`, `isPermisoRowBlank`) y se anadieron los cuatro flags a la fila, al inicializador, a la carga, al payload y a la interfaz (un checkbox por flag, con las clases existentes).
- **A-7:** `invalidate('catalogo' as never)` apuntaba a una clave inexistente: `mutationInvalidation['catalogo']` era `undefined` y la llamada lanzaba `TypeError`, que el `catch` mostraba como "No se pudo validar la importacion". Nueva clave real `importacionLoteCrear` (invalida el prefijo `catalogo`) y uso sin cast.
- **A-8:** las acciones menores de Configuracion llamaban a `load()`, que ponia la pagina en esqueleto y recargaba cinco endpoints, pisando lo no guardado. `ConfiguracionPage` tiene ahora un recargador por seccion (configuracion, tipos de cambio, divisas, tokens, catalogos); `load()` los compone y sigue siendo la carga inicial. `guardarDivisaPorDefecto` recarga divisas (la divisa base solo sale de `GET /divisas`) y no configuracion, que es lo que evita perder lo escrito.
- **B-12:** la rama de alta de `SmtpTestRateLimit.TryAcquireAsync` no marcaba `allowed`, de modo que el primer intento de cada usuario devolvia `false` y el controlador respondia 429. La rama marca ahora `allowed = true`; limite de 5 por minuto y usuario sin cambios.
- **F-3:** el backend omite los `null` (`WhenWritingNull`), asi que el campo llega `undefined` y `!== null` era true. Comparacion con `!= null` en `AuditoriaPage` y `AuditCellModal`, y `valor_anterior`/`valor_nuevo` opcionales en `AuditCellEntry`, `Auditoria` y `AuditoriaListItem`.

### Archivos tocados

- Backend: `src/AtlasBalance.API/Services/SmtpTestRateLimit.cs`; nuevo `tests/AtlasBalance.API.Tests/SmtpTestRateLimitTests.cs`.
- Frontend: `src/components/usuarios/UsuarioModal.tsx`, `src/utils/permisosFormUtils.ts`, `src/queries/invalidation.ts`, `src/pages/ImportacionPage.tsx`, `src/pages/ConfiguracionPage.tsx`, `src/pages/AuditoriaPage.tsx`, `src/components/extractos/AuditCellModal.tsx`, `src/types/index.ts`, `tests/permisosFormUtils.test.ts`, `tests/invalidation.test.ts`, nuevos `tests/auditoriaValores.test.ts` y `tests/configuracionSecciones.test.ts`, `package.json` (registro de los dos tests nuevos en `test:unit`).
- Documentacion: `INFORME_REVISION_INTEGRAL_2026-11-16.md` (estado por ID y seccion 12), `LOG_ERRORES_INCIDENCIAS.md`, `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md`, `Versiones/v-03.02.md` y este fichero.

### Comandos ejecutados

- Backend: `dotnet test -c Release --artifacts-path ...\.artifacts\full` (el canonico `dotnet test -c Release` falla porque una API de desarrollo viva, PID 17816, bloquea `bin\Release\net10.0\AtlasBalance.API.dll`).
- Frontend: `npx eslint src tests --max-warnings 0`, `npx tsc --noEmit`, `npx tsc -p tsconfig.test.tmp-verify.json` + `node --test` sobre los 17 ficheros de test, `npx vite build --outDir dist-verify`.
- `npm run lint`, `npm run test:unit` y `npm run build` canonicos: fallan por permisos heredados de `.test-dist/`, `.test-dist-v2/` y `dist/` (ficheros de otra cuenta de sandbox); es el problema ya anotado en el log de incidencias y como M-10 del informe. Las mismas comprobaciones pasaron con carpetas de salida nuevas.

### Resultado de verificacion

- **Verificado:** `AtlasBalance.API.Tests` 1002/1002 y `AtlasBalance.Caching.Tests` 15/15; `eslint src tests` 0 errores; `tsc --noEmit` 0 errores; 123/123 pruebas unitarias del frontend (104 previas + 19 nuevas); build de Vite correcto (2.671 modulos). Cada ID con su propio test en rojo antes del arreglo y en verde despues.
- **Bloqueado:** los tres comandos canonicos del frontend por el problema de permisos descrito; `dotnet test -c Release` canonico por el bloqueo del binario de la API viva. Sin validacion visual en navegador, sin ejecucion de la app y sin PostgreSQL.
- **Pendiente / no tocado (fuera de LOTE 1):** `SmtpTestRateLimit` puede invocar el delegado de `AddOrUpdate` mas de una vez y esquivar el limite con concurrencia extrema; los permisos que el backend omite (`WhenWritingNull`) siguen siendo opcionales en los tipos, lo que deja `undefined` como valor de hecho en los registros de auditoria; la carpeta `.test-dist-v2/` sin trackear (M-10) y `.test-dist/` con permisos ajenos siguen pendiente de limpieza.

### Decisiones visuales

- Ninguna. Los cuatro checkbox de A-1 reutilizan el patron y las clases (`users-check-input`) de los seis ya existentes; no se toco ninguna hoja de estilo. A-8 y F-3 no cambian el aspecto, solo el dato que se pinta.

## 2026-11-16 - V-03.02 - Revision integral de la app (solo informe)

### Trabajo realizado

- Revision integral de backend, frontend, diseno, seguridad, rendimiento y codigo muerto. Informe en `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md`: 9 hallazgos altos, 44 medios, 38 bajos o sospechas y 29 de codigo muerto, mantenibilidad, CI y tests, con orden de trabajo propuesto.
- Metodo: siete subagentes en solo lectura, uno por area (auth y superficie de seguridad; controladores y permisos; servicios, jobs y datos; IA e integraciones; logica del frontend; diseno y UX; codigo muerto e higiene). El orquestador releyo el codigo citado de los 9 altos y de unos 25 medios y bajos; el informe marca cada hallazgo como verificado (V), reportado por subagente (S) o sospecha (?).
- Hallazgos principales: guardar un usuario revoca cuatro permisos (`UsuarioModal` no envia `puede_revisar_lineas`, `puede_aprobar_importaciones`, `puede_conciliar`, `puede_cerrar_conciliacion`); bloqueo de cuenta indefinido (el contador de fallos no se reinicia al caducar el bloqueo); un backup en `PENDING` detiene los automaticos; backups `AUTO` del mismo dia comparten fichero; el planificador semantico de IA no envia `zdr`/`data_collection=deny`; la IA ignora el filtro por concepto; validar una importacion muestra un error falso (`invalidate('catalogo' as never)`).
- **Sin cambios de codigo de aplicacion.**

### Archivos tocados

- `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md` (nuevo).
- `Documentacion/DOCUMENTACION_CAMBIOS.md`, `Documentacion/Versiones/v-03.02.md`.

### Comandos ejecutados

- Solo lectura: `git ls-files`, `git check-ignore`, `grep`, `sed -n`, `wc`. Ningun build, test, servidor ni script.

### Resultado de verificacion

- Verificado: lectura estatica del codigo citado.
- Bloqueado: nada se ejecuto (sin build, tests, app ni PostgreSQL) y no hubo validacion visual. Tres subagentes se cortaron por limite de uso y se reanudaron.

### Pendientes

- Ningun hallazgo esta corregido ni dado de alta en `REGISTRO_BUGS.md`; falta decidir cuales se registran.
- Sin revisar: scripts PowerShell de instalacion y actualizacion y sus ACL, politicas RLS y migraciones, `GoogleDriveBackupService` a fondo.
- Las sospechas del informe necesitan ejecucion (`EXPLAIN`, routing con barra final, orden de pegado en importacion, idempotencia de confirmar lote).
- Decisiones visuales: ninguna tomada. Pendientes de diseno abiertos en la seccion 7 del informe (estados de carga, `formatPercent`, breakpoints, `system-coherence.css`, tokens sin uso).

## 2026-11-16 - V-03.02 - Arranque del stack de desarrollo (API 5002 + Vite 5173)

### Trabajo realizado

- **Arranque del entorno de desarrollo:** PostgreSQL en Docker (`atlas_balance_db`, 127.0.0.1:5433), API en `http://localhost:5002` y frontend Vite en `http://localhost:5173`, con el proxy `/api` de Vite apuntando a la API.
- **`wwwroot` del API:** la carpeta `backend/src/AtlasBalance.API/wwwroot` es generada (su contenido esta en `.gitignore`: `index.html`, `assets/`, `fonts/`, `logos/`). En este checkout no existia y la API moria al arrancar con `DirectoryNotFoundException` en `StaticWebAssetsLoader`. Se creo `wwwroot/index.html` como placeholder que redirige al frontend de Vite; el contenido real lo sigue poniendo `npm run build` + `Build-Release.ps1`.
- **Sin cambios de codigo de aplicacion.** No se toco `Program.cs`, controllers ni frontend.

### Archivos tocados

- `Atlas Balance/backend/src/AtlasBalance.API/wwwroot/index.html` (nuevo; el path esta en `.gitignore`, no aparece en `git status`).
- `Documentacion/DOCUMENTACION_CAMBIOS.md`, `Documentacion/LOG_ERRORES_INCIDENCIAS.md`.

### Comandos ejecutados

- `scripts/Start-Dev.ps1 -TimeoutSeconds 120` (backend con build en `tools/dotnet-build/api`, frontend con `npm run dev`).
- Verificaciones HTTP contra 5002 y 5173.

### Resultado de verificacion

- `GET http://localhost:5002/api/health` -> 200 `{"status":"healthy"}`.
- `GET http://localhost:5173` -> 200; `GET http://localhost:5173/api/health` (proxy) -> 200 `{"status":"healthy"}`.
- Vite transforma `src/main.tsx` y `src/App.tsx` sin error (200).
- `POST http://localhost:5002/api/auth/login` con `{}` -> 400 de validacion (la ruta existe; no es 404).
- Hangfire instalado contra PostgreSQL (`Host: localhost, DB: atlas_balance, Schema: hangfire`), sin migraciones pendientes en el log de arranque.

### Pendientes

- **Login autenticado no probado:** no hay credenciales de desarrollo y MFA esta activo (ver `LOG_ERRORES_INCIDENCIAS.md` 2026-10-05, clave de Data Protection).
- **`42883: jsonb ~~ jsonb` sigue presente** en el job de Hangfire `PlazoFijoVencimientoJob` -> `PlazoFijoService.TryAddAdminNotificationAsync` (`DetallesJson.Contains(...)` sobre una columna `jsonb`). Es un defecto real de codigo, ya registrado como "no relacionado" el 2026-10-05, y sigue sin corregir.
- `Start-Dev.ps1` lanza el frontend sin redirigir stdout/stderr: el hijo hereda los handles del shell y la tarea que lo invoca no termina nunca. `Start-LocalDev.ps1` si redirige, pero exige `.dotnet` y PostgreSQL portatil en el workspace, que este checkout no tiene.

## 2026-11-16 - V-03.02 - Retencion de MFA e importacion, IBAN solo para ADMIN y auditoria

### Trabajo realizado

- **Dispositivos MFA de confianza:** nuevo job `LimpiezaMfaTrustedDevicesJob` (diario, id `limpieza-mfa-trusted-devices`, registrado en `Program.cs`). Borra de `MFA_TRUSTED_DEVICES` los dispositivos caducados o revocados hace mas de 1 dia.
- **Retencion de lotes de importacion:** nuevo job `LimpiezaImportacionLotesJob` (diario, id `limpieza-importacion-lotes`). Pone `datos_json` a `'{}'` en las filas de lotes CERRADOS (confirmado, revertido o error) con mas de `importacion_contenido_retention_days` (180 por defecto). No borra filas.
  - Limites: los lotes abiertos (validado, validado_con_errores) no se purgan porque confirmar reconstruye desde `datos_json`. Las filas soft-deleted no se tocan: la policy RLS de update exige `deleted_at IS NULL`, y cambiarlo requeriria migracion. `errores_json` y `advertencias_json` no se vacian.
- **IBAN en `GET /api/cuentas/{id}`:** el IBAN completo solo sale a ADMIN; el resto recibe `PiiMasking.MaskIban`. Un `PUT` con IBAN que contiene `*` se trata como sin cambio (el frontend envia `*` cuando no se edita el IBAN). La busqueda por IBAN queda solo para ADMIN. `NumeroCuenta` sigue completo y buscable para no-ADMIN (no pedido, pendiente).
- **Auditoria (`AuditSaveChangesInterceptor.CapturarValores`):** enmascara `Cuenta.Iban` y `NumeroCuenta` antes de serializar y firmar. En UPDATE con cambio de IBAN anade `IbanCambiado: true`. Decision del usuario: solo IBAN (no nombre ni concepto). Las filas antiguas NO se tocan: siguen con IBAN en claro hasta su caducidad de 365 dias, porque modificarlas rompe la firma de integridad.
- **Proveedor de IA:** sin cambios de codigo. OpenRouter ya recibe `zdr=true` y `data_collection=deny` (`AtlasAiService`). OpenAI y MiniMax no tienen clausula equivalente en codigo. Queda pendiente contractual (DPA y politica de retencion con el proveedor elegido).
- **Cifrado de backup local y xlsx:** no implementado. Decision del usuario: dejarlo a BitLocker (gate operativo abierto desde la auditoria de 2026-08-04).

### Archivos tocados

- Backend: `Atlas Balance/backend/src/AtlasBalance.API/Jobs/LimpiezaMfaTrustedDevicesJob.cs` (nuevo), `Jobs/LimpiezaImportacionLotesJob.cs` (nuevo), `Program.cs` (registro DI y `AddOrUpdate` de ambos jobs), `Controllers/CuentasController.cs`, `Data/AuditSaveChangesInterceptor.cs`.
- Tests: `backend/tests/AtlasBalance.API.Tests/RowLevelSecurityRetentionJobsTests.cs` (nuevo, Postgres real con RLS FORCE), `CuentasControllerTests.cs` (3 tests nuevos), `AuditSaveChangesInterceptorTests.cs` (1 test nuevo).
- Frontend: `Atlas Balance/frontend/src/pages/CuentasPage.tsx` (en el mismo lote; envia `*` cuando no se edita el IBAN).
- Documentacion: `DOCUMENTACION_CAMBIOS.md`, `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md`, `Versiones/v-03.02.md`, `INVENTARIO_DATOS_PERSONALES_V-03.02.md`, `AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md`.

### Comandos ejecutados

- `dotnet test -c Release` en `Atlas Balance/backend` (AtlasBalance.API.Tests y AtlasBalance.Caching.Tests).
- Frontend: `tsc` y `npm run lint`.

### Resultado de verificacion

- Backend: AtlasBalance.API.Tests 994/994 y AtlasBalance.Caching.Tests 15/15.
- Frontend: `tsc` y lint limpios. `vitest` no ejecutado en este lote.
- No se probo en la app de desarrollo autenticada (MFA obligatorio).

### Pendientes

- `NumeroCuenta` sigue completo y buscable para no-ADMIN (no pedido).
- Proveedor de IA (OpenAI, MiniMax): DPA y politica de retencion. Contractual, no de codigo.
- Cifrado de backup local y xlsx: BitLocker, gate operativo abierto desde la auditoria de 2026-08-04.
- Filas de auditoria anteriores al cambio: IBAN en claro hasta caducar (365 dias).
- Purga de importacion: lotes abiertos, filas soft-deleted, `errores_json` y `advertencias_json` quedan fuera.
- Prueba manual autenticada tras desplegar, y `vitest`.


## 2026-11-16 - V-03.02 - Correccion de los hallazgos de la auditoria del checklist de seguridad

### Trabajo realizado

Cuatro lotes orquestados con subagentes Sonnet y verificacion propia tras cada uno. Detalle tecnico en `DOCUMENTACION_TECNICA.md` ("V-03.02 - Correcciones de la auditoria del checklist").

- **Lote 1:** P0-1 axios/follow-redirects + `npm audit fix` (0 high); P0-2 MailKit/MimeKit 4.18.2 (Pkcs corregido) con lockfiles.
- **Lote 2:** M-1 test RLS que enumera todas las tablas; M-2 aviso por email al bloquear una cuenta.
- **Lote 3:** M-3 lista de contrasenas (SecLists, 12+); M-5 test de nivel de log de HttpClient; M-7 escaner de secretos coherente con git; M-8 workflow de auditoria semanal.
- **Lote 4:** M-4 CSP `style-src 'self'` (cabecera y `<meta>`); M-6 inventario de datos personales.
- Entorno: el `node_modules` del frontend tenia ficheros con ACL que daban `EPERM`; se movio a `node_modules.blocked-20261116` (ignorado por git, no se pudo borrar) y se reinstalo con `npm ci`.

### Archivos tocados

Frontend: `package.json`, `package-lock.json`, `index.html`, `vite.config.ts` (plugin que relaja el meta CSP solo en `vite serve`). Backend: `AtlasBalance.API.csproj`, 3 `packages.lock.json` (+ Watchdog solo por fin de linea), `Program.cs`, `Constants/SecurityPolicy.cs`, `Services/AuthService.cs`, `Services/EmailService.cs`, `Services/AccountLockoutNotifier.cs` (nuevo), `Resources/common-passwords-12plus.txt` (nuevo); tests: `RowLevelSecurityTests`, `SecurityPolicyTests`, `AccountLockoutNotificationTests` (nuevo), `SerilogHttpClientLoggingTests` (nuevo) y 3 fakes de email. Scripts/CI: `Test-AtlasSecrets.ps1` y su test, `.github/workflows/dependency-audit.yml` (nuevo). Docs: informe de auditoria, inventario de datos personales, tecnica, usuario, esta bitacora y `v-03.02.md`.

### Comandos ejecutados

`npm audit` / `npm audit fix` / `npm ci`, `eslint`, `tsc --noEmit`, tests unitarios frontend (104/104, outDir alterno), `vite build`; `dotnet restore --force-evaluate` y `--locked-mode`, `dotnet list package --vulnerable --include-transitive` (0 en los 4 proyectos), `dotnet build AtlasBalance.sln -c Release`, `dotnet test` de API.Tests completo (986) y Caching.Tests (15); `curl` de `Pwdb_top-1000000.txt` de SecLists (raw.githubusercontent.com, ~8,7 MB, descargado como texto con autorizacion del usuario y filtrado a 12+ caracteres); Playwright con API mockeada para CSP; arranque de la API de desarrollo con `Start-BackendDev.ps1`, `/api/health`, `/ready` y `/functional` (200) y carga de `/login` en el navegador integrado.

### Resultado de verificacion

- Backend: 986/986 correctos (incluye Postgres/Testcontainers). Linea base previa: exit 0.
- Frontend: lint, `tsc`, 104/104 y build OK. `npm audit`: 0 high/critical, 7 moderate.
- CSP: cabecera real con `style-src 'self'`; login sin violaciones. Paginas autenticadas: solo con API mockeada (24 rutas, 0 violaciones).
- Escaner de secretos: 0 hallazgos en el repo; secreto sembrado en un directorio sin git se detecta.

### Bloqueado / no verificado

- Login real en la app de desarrollo: bloqueado por MFA obligatorio; no se altero la configuracion ni se inscribio TOTP en la BD de desarrollo.
- Email de bloqueo con SMTP real, y `SystemContextScope` del notificador contra RLS real (tests InMemory).
- `dependency-audit.yml` (schedule solo desde `main`) y los tests del escaner bajo `pwsh` en ubuntu.
- `npm audit` sigue con 7 moderate (cadena `typescript-eslint`, requiere salto mayor).
- Caching.Tests y Watchdog no se ejecutaron (solo build de la solucion).

### Pendientes

Smoke manual autenticado tras desplegar (paginas con graficos con la CSP nueva, envio de correo de prueba desde Configuracion tras MailKit 4.18.2); mergear para activar el workflow semanal; el bug abierto de `PlazoFijoVencimientoJob` (jsonb) sigue sin tocar; ver hallazgos nuevos del inventario de datos personales.


## 2026-11-16 - V-03.02 - Auditoria del checklist de seguridad/rendimiento (solo informe)

### Trabajo realizado

- Contraste de cada punto de `Securituy app.txt` con el codigo. Informe: `Documentacion/AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md` (estado por punto, evidencia, mejoras priorizadas P0/P1/P2, que no aplica y por que). Uso del advisor para validar el alcance.
- Hallazgos nuevos frente a la auditoria de 2026-08-04: `npm audit` con 2 high (axios, follow-redirects) y NuGet con 1 transitiva high (`System.Security.Cryptography.Pkcs 10.0.0`, via MailKit/MimeKit). Los gates de CI fallarian con el arbol actual.
- Mejoras de endurecimiento propuestas: test RLS por tabla, aviso de bloqueo de cuenta, blocklist local ampliada, inventario de PII, scanner de secretos coherente con CI.

### Archivos tocados

Solo documentacion: el informe nuevo y esta bitacora. Sin cambios de codigo ni de dependencias.

### Comandos ejecutados

`npm audit --omit=dev`, `dotnet list package --vulnerable --include-transitive`, `dotnet nuget why`, `Test-AtlasSecrets.ps1 -Root .`, `git log --all` sobre `.env`/`appsettings.*`/`.pfx`/`.pem`/`.key`, lecturas y busquedas estaticas.

### Resultado de verificacion

Verificado: auditorias de dependencias, scanner de secretos (1 hallazgo local en `appsettings.Development.json`, ignorado por git, no esta en CI) y lectura de los ficheros citados. Bloqueado/no ejecutado: tests, API en marcha, ZIP de release, servidor real.

### Pendientes

P0-1 y P0-2 (dependencias), M-1 a M-8 del informe. Informe interno con vulnerabilidades abiertas: no publicar hasta cerrar P0.

## 2026-11-16 - V-03.02 - Correccion de los hallazgos del informe Impeccable (F1-F10)

### Trabajo realizado

Aplicados en cuatro lotes con subagentes (Sonnet y Haiku, archivos disjuntos por agente) y verificados por el orquestador tras cada lote. Detalle tecnico en `DOCUMENTACION_TECNICA.md` (misma fecha) y estado por hallazgo en `Diseno/INFORME_IMPECCABLE_CRITIQUE_AUDIT_V-03.02.md` sec. 10.

- **Lote 1:** Dashboard con estado por bloque, reintento y jerarquia en dos niveles (F2, F4, F7); boton primario oscuro (F1); densidad Comodo/Compacto, h1 a 28 px y tracking -0.011em (F3); reloj del sidebar fuera (F10).
- **Lote 2:** `Modal` unico y un solo backdrop (F5): 5 modales migrados, 8 no migrados con motivo.
- **Lote 3:** `scope="col"` en 150 cabeceras, tablas apiladas en movil y desborde de Backups/Usuarios (F6); tres niveles de confirmacion (F9).
- **Lote 4:** 95 alias legacy puros migrados (1.700 usos, capas de compatibilidad intactas); 6 de 34 `uppercase` quitados (F10).
- **Defecto previo hallado por el arnes de estados:** doble confirmacion al Revocar/Eliminar token (`TokenList` y `ConfiguracionPage.revokeToken` pedian confirm). Corregido quitando la confirmacion de la pagina; Rotar conserva la suya. Test `1f` lo cubre.
- **No aplicado:** F11 (division del CSS de IA) por ganancia (~8 KB gzip) menor que el riesgo de orden con `system-coherence.css`.
- **Arnes visual** nuevo en `frontend/e2e/visual/` (API mockeada, sin backend).

### Archivos tocados

Frontend: ~55 archivos en `src/` (tokens, `global.css`, `layout/*.css`, Dashboard, `ExtractoTable`, modales, paginas con tablas), `public/theme-init.js`, `package.json` (test de densidad), `tests/density.test.ts`, `src/utils/density.ts`, `components/common/Modal.tsx`, `components/dashboard/DashboardBlockStatus.tsx` y `DashboardTooltip.tsx`, `e2e/visual/*`. Docs: `DESIGN.md`, informe, tecnica, usuario, log de errores, esta bitacora, `v-03.02.md`.

### Comandos ejecutados

`tsc --noEmit`, `npm run lint`, `tsc -p tsconfig.test.v2.json` con `outDir` dentro de `frontend` + `node --test` (104/104), `vite build` a directorio temporal (borrado), Playwright con API mockeada (capturas de 9 rutas, claro/oscuro, 1440/390), comparacion de capturas por hash tras la migracion de alias (32/36 identicas; las 4 restantes son no deterministas entre ejecuciones iguales).

### Resultado de verificacion

- **Verificado:** tsc y lint limpios, 104/104 tests, build, capturas de dashboard (normal y con fallo forzado de un endpoint), extractos con el selector de densidad, configuracion, backups y usuarios a 390 px, modal de titular a 390 px.
- **Bloqueado:** backend local (falta PostgreSQL portatil y credenciales), `impeccable detect` (descarga de binario).
- **No verificado visualmente (a menos que el apartado de estados del informe diga lo contrario):** `Modal.tsx` y los modales migrados, los tres niveles de `ConfirmDialog`, densidad compacta con scroll virtualizado, tablas apiladas en movil y semantica de tabla, estado `setup` del Dashboard, contraste real del boton oscuro.
- Nada esta commiteado. El trabajo staged previo (accesibilidad, 35 archivos) queda intacto; este trabajo esta sin `git add`.

### Decisiones visuales

Cuerpo 17/14 px se mantiene; titulos de pantalla de trabajo a 28 px; densidad Comodo (44 px, por defecto) y Compacto (36 px, solo grid de Extractos); boton primario oscuro un peldano mas claro; Dashboard en dos niveles; confirmaciones en tres niveles (icono + texto, no solo color).

### Pendientes

- Validacion visual de lo "No verificado" con datos reales o el arnes de estados.
- Confirmar con el usuario si la densidad Compacto debe llegar a mas tablas.
- Migrar a `Modal` los 8 modales con logica de cierre propia (Titulares, Cuentas, Paises, Usuario, Usuarios, CuentaDetail, Backups, SessionTimeout).
- Alias legacy diferidos (valor propio, `color-mix`, redefinidos por tema).
- Semantica de tabla en tablas apiladas si el arnes confirma que se pierde.

## 2026-11-16 - V-03.02 - Correccion de los hallazgos del informe Modern Web Guidance

### Trabajo realizado

Se aplicaron los hallazgos de `Documentacion/Diseno/INFORME_MODERN_WEB_GUIDANCE_V-03.02.md` en cinco lotes, orquestados con subagentes (Sonnet y Haiku, con archivos disjuntos por agente) y revisados con el advisor.

- **A, accesibilidad base:** foco con `outline` + regla `forced-colors` (tokens `--focus-outline*`); titulo de pagina por ruta (`useDocumentTitle`); pestanas de Importacion y Configuracion con teclado de pestana (la de Paises ya no es un `NavLink`, navega con `replace`); 9 tablas con scroll con `tabIndex`, `role="region"` y nombre.
- **B, color y tema:** `--text-tertiary` #68686D y `--text-secondary` #58585C (claro); token nuevo `--border-control` para bordes de controles (3:1); tema inicial = guardado o sistema, `<meta name="color-scheme">`, un solo `theme-color` dinamico y `public/theme-init.js` sincrono (la CSP prohibe inline).
- **C, escala de texto:** raiz sin `font-size: 16px`, escala `--text-*` en `rem` (identidad a 16 px comprobada por script), `<meta name="text-scale">`, `height` a `min-height` solo en 4 controles con texto.
- **D, movimiento, scrollbar y formularios:** se elimino el reset global `*` de movimiento reducido (casos concretos en `reduce`, infinitas bajo `no-preference`); `scrollbar-color` estandar solo donde no existe `::-webkit-scrollbar`; 12 importes/tasas pasan de `type="number"` a texto con `inputMode="decimal"` y `parseStrictDecimal`; atributos de teclado en login/busquedas; `fieldset`+`legend` sr-only en permisos; objetivos tactiles de 44 px solo en `pointer: coarse`.
- **No se hizo, con motivo (informe seccion 6):** migrar modales a `<dialog>` (sin bug demostrado), `@layer`, container queries.

### Archivos tocados

Frontend (`Atlas Balance/frontend`): `index.html`, `public/theme-init.js` (nuevo), `eslint.config.js` (ignora `public`), `package.json` y `tsconfig.test.v2.json` (tests nuevos), `src/hooks/useDocumentTitle.ts` y `src/utils/theme.ts` (nuevos), `src/main.tsx`, `src/stores/uiStore.ts`, `src/utils/formatters.ts` (`parseStrictDecimal`), `Layout.tsx`, `LoginPage`, `ChangePasswordPage`, `ImportacionPage`, `ConfiguracionPage`, `AlertasPage`, `CuentasPage`, `TitularesPage`, `UsuariosPage`, `FormatosImportacionPage`, `PaisesPage`, `CommandPalette`, `UsuarioModal`, `CreateTokenModal`, 7 paginas/componentes con tablas, `tests/theme.test.ts` y `tests/parseEuropeanNumber.test.ts` (nuevos) y 20 hojas CSS de `src/styles` (tokens, global, auth y layout).

### Comandos ejecutados

`tsc --noEmit`, `npm run lint`, `tsc -p tsconfig.test.v2.json` con `--outDir` alternativo y `node --test` sobre ese directorio, `vite build` a un directorio temporal del workspace (borrado despues), `git diff --check`. Contrastes calculados con scripts Node de una linea.

### Resultado de verificacion

- **Verificado:** `tsc` limpio; lint con `--max-warnings 0` limpio; 101/101 tests unitarios (hay 2 archivos de test nuevos); `vite build` correcto y `theme-init.js` sale en el build; `git diff --check` sin errores; contrastes por script (foco 5,85:1 claro y 7,33:1 oscuro; terciario >= 4,74:1 sobre las 4 superficies; bordes de control >= 3,10:1 claro y >= 4,86:1 oscuro).
- **Bloqueado:** `npm run test:unit` falla con `EPERM` al escribir en el antiguo `.test-dist` (carpeta de octubre con permisos heredados). Se ejecuto la misma suite con otro `outDir`. Release no se ha tocado.
- **No verificado (sin validacion visual ni axe):** Alto Contraste de Windows, teclado real, escalado de texto al 200 %, recorte del outline en contenedores con `overflow`, indicador de foco en `.app-palette-input`, contraste de `--sheet-selected`, `tabpanel` en Importacion, movil/tactil (popup de IA, filtros de extractos), tema oscuro completo, `scrollbar-color` en Firefox.

### Decisiones visuales

- Contraste mas alto en toda la app: `--text-secondary` pasa a #58585C y `--text-tertiary` a #68686D (revertir: `semantic.css`, `--text-secondary-light` y `--text-tertiary-light`).
- Los bordes de controles (inputs, selects, checkbox, botones del shell, toggles) usan `--border-control` (#86868B claro; blanco 50 % oscuro). Revertir: `--border-control` y `--control-border` en `variables.css`.
- Tema por defecto = sistema (antes siempre claro). Revertir: `resolveInitialTheme` y `public/theme-init.js`.
- Foco: outline de 2 px en acento mas halo existente.

### Pendientes

- **Comprobacion manual por pantalla (orden de riesgo):** (1) raiz sin 16 px + `text-scale`: dashboard, extractos (filas del grid) y modales al 100/150/200 %; (2) foco con Tab en claro/oscuro y en Alto Contraste; (3) bordes y texto terciario en claro/oscuro; (4) formularios con importes: escribir `1,085`, `1.085` y `1.234,50` en tasa manual, saldo minimo e interes; (5) pestanas de Importacion/Configuracion con flechas y boton Atras; (6) movil/tactil.
- Politica de navegadores en `AGENTS.md` (condiciona los fallback).
- `legacy-scan` no ejecutado; axe/Playwright pendiente.
- Latente, sin tocar: `parseEuropeanNumber('0.001')` devuelve 1 (punto con tres decimales y parte entera 0 se toma como millar) en extractos/importacion; el backend tiene su propio parser en `ImportacionService`. Decidir si se alinean.

## 2026-11-16 - V-03.02 - Informe Modern Web Guidance del frontend

### Trabajo realizado

- Auditoria estatica del frontend contra las guias de Modern Web Guidance.
  Resultado en `Documentacion/Diseno/INFORME_MODERN_WEB_GUIDANCE_V-03.02.md`.
- Sin cambios de codigo.

### Archivos tocados

- `Documentacion/Diseno/INFORME_MODERN_WEB_GUIDANCE_V-03.02.md` (nuevo), `Documentacion/Versiones/v-03.02.md` y este archivo.

### Comandos ejecutados

- Lecturas y greps sobre `frontend/src`; guias leidas desde disco. Contrastes calculados con un script de Node de una sola linea (sin ficheros). `legacy-scan` via npx denegado por el sandbox: no ejecutado.

### Resultado de verificacion

- No hubo validacion visual ni axe/Lighthouse; los hallazgos son estaticos.

### Pendientes

- Decisiones de diseno abiertas: contraste de bordes de control (3:1), tema por defecto del sistema, politica de navegadores en `AGENTS.md`, escala tipografica en `rem`.
- Hallazgos P1 pendientes de implementar: focus ring con `outline`/`forced-colors`, titulo de pagina por ruta, pestanas de Importacion con teclado de pestana. Texto terciario y bordes de control a AA quedan como decision de diseno.
- Dialogos propios: fragiles pero sin bug demostrado (Escape apilado analizado por lectura, no en runtime); no se recomienda migrar a `<dialog>`.
- Nota: esta entrada ya existia sin commitear al empezar la sesion, escrita antes que el informe; se completo y corrigio aqui. Se anadio tambien la entrada en `Documentacion/Versiones/v-03.02.md`.
- Pendientes de verificacion: axe/Playwright, Alto Contraste de Windows, escalado al 200 %, `legacy-scan` si se autoriza.

## 2026-10-05 - V-03.02 - Arranque del entorno de desarrollo aislado

### Trabajo realizado

- API de desarrollo iniciada en `http://localhost:5002` (PID 3200) con
  `Start-BackendDev.ps1`; compilada en `tools/dotnet-build/api`.
- Vite ya estaba activo en `http://localhost:5173` (Node PID 26320); se dejó
  intacto. Su proxy `/api` responde desde la API de desarrollo.
- PostgreSQL ya estaba activo en Docker. Las etiquetas identifican el proyecto
  `atlasbalance` y el compose de este checkout; usa el volumen
  `atlasbalance_pgdata`.
- El arranque ejecutó migraciones, instalación de Hangfire, permisos de BD,
  seed y protección de configuración solo contra la BD de desarrollo. También
  corrieron tareas de limpieza; no se conectó a la instalación real.
- Sin cambios en código de la aplicación ni en los servicios de producción.

### Archivos tocados

- `Documentacion/DOCUMENTACION_CAMBIOS.md` y `Documentacion/REGISTRO_BUGS.md`.
- Archivos locales ignorados: artefactos en `tools/dotnet-build/api`, logs/PID
  en `Atlas Balance/logs/dev` y claves locales en `.dataprotection-keys`.

### Comandos ejecutados

- `Start-BackendDev.ps1 -TimeoutSeconds 60 -ApiPort 5002`: restauración y
  compilación correctas; un aviso CS0618 por sobrecarga obsoleta.
- `pg_isready` dentro del contenedor de desarrollo: aceptando conexiones.
- Los logs registran fallos de tareas programadas: `PlazoFijoVencimientoJob`
  devuelve PostgreSQL `42883` en `PlazoFijoService.cs:197`; el backup no encuentra
  `pg_dump.exe` configurado y la sincronización de tipos requiere su clave API.
  El fallo JSONB quedó abierto en `REGISTRO_BUGS.md`.
- `Invoke-WebRequest` a `http://localhost:5002/api/health`,
  `http://localhost:5173` y `http://localhost:5173/api/health`: HTTP 200.
- Primer intento de restauración dentro del sandbox bloqueado por `NU1301`;
  segundo intento con acceso de red aprobado completado correctamente.

### Pendientes

- No se hizo prueba visual ni de inicio de sesión. API y frontend permanecen
  activos; la API responde al health, pero las tareas indicadas siguen pendientes.


## 2026-10-05 - V-03.02 - Correccion de lectura de cuentas para no administradores

### Trabajo realizado

- Normalizados ids y reglas de columnas omitidos por API en el setter comun.
  Conservadas acciones, ids concretos y listas vacias.
- Anadidas 16 regresiones a `test:unit`, con store/Zustand reales, auth aislado
  y payloads que omiten null. Cerrado el defecto en codigo.
- Decisiones visuales: ninguna; las vistas existentes vuelven a reconocer los
  permisos. Sin pendientes de diseno nuevos.

### Archivos tocados

- Frontend: `src/stores/permisosStore.ts`, `tests/permisosStore.test.ts` y
  `package.json` (incorporar prueba al script).
- Documentacion: diagnostico, documentos tecnico/usuario, log de errores,
  registro de bugs, esta bitacora y `Versiones/v-03.02.md`.

### Comandos ejecutados y verificacion

- `npm.cmd ci --offline --ignore-scripts --no-audit --no-fund`: 258 paquetes
  desde cache local; sin cambios del lockfile ni scripts de instalacion.
- `npm.cmd exec -- tsc -p tsconfig.test.v2.json`: primer intento encontro el
  reexport de stores en `types/index.ts` y `Object.hasOwn` fuera de ES2020;
  test ajustado a payload estructural y `hasOwnProperty`, compilacion OK.
- `node --test --test-force-exit .test-dist/tests/permisosStore.test.js` antes
  del fix: 11 fallos/16 casos, reproduciendo el defecto.
- `npm.cmd run test:unit`: 92/92 OK, incluidas las 16 regresiones.
- `npm.cmd run lint`: OK. `npm.cmd run build`: OK; aviso preexistente de Vite
  sobre `__dirname` en su configuracion.
- `git diff --check`: OK.
- `Check-VersionAlignment.ps1 -ExpectedVersion V-03.02`: OK (`3.2.0`).

### Pendientes

- Desplegar frontend y verificar navegador con el gerente afectado. No se
  afirma validacion visual ni de PostgreSQL. Backend/datos sin cambios.
- WIP previo conservado, sin commit ni push.

## 2026-10-05 - V-03.02 - Diagnostico de lectura de cuentas para no administradores

### Trabajo realizado

- Revisado el flujo formulario, guardado, auth, store, vistas, servicio backend y SQL RLS del repo.
- Reproducido que ids omitidos por `WhenWritingNull` bloquean permisos globales/por pais/titular en el frontend, que exige `=== null`.
- Informe con causa, ocho casos, limites y correccion recomendada; bug abierto. Sin cambios funcionales ni de datos.

### Archivos tocados

- `Documentacion/DIAGNOSTICO_PERMISOS_CUENTAS_2026-10-05.md`, `REGISTRO_BUGS.md`, `DOCUMENTACION_CAMBIOS.md` y `Versiones/v-03.02.md`.

### Comandos ejecutados

- `git status --short`, `git branch --show-current`, `rg` y `Get-Content` sobre instrucciones, documentacion y fuentes.
- `node` via stdin: primer intento con TypeScript externo bloqueado por `MODULE_NOT_FOUND`; alternativa con `stripTypeScriptTypes` incorporado y `vm`, ocho comprobaciones OK.
- `dotnet test <AtlasBalance.API.Tests.csproj> --no-restore -p:UseAppHost=false -p:UseArtifactsOutput=true -p:ArtifactsPath=<workspace>/tools/dotnet-build/diagnostico-permisos -- --filter-class '*UserAccess*' --timeout 60s`: bloqueado por `NETSDK1004`, faltan assets NuGet.
- `git diff --check` sobre documentos de la sesion: OK.

### Resultado de verificacion y pendientes

- Verificado el defecto ejecutando helpers del store con adaptadores de estado/auth; no es validacion visual ni de BD real.
- Pendientes: correccion, regresiones, permisos guardados y HTTP del gerente concreto, navegador autenticado y PostgreSQL. Conservado el WIP previo de inicio de version.

## 2026-10-04 - V-03.02 - Inicio de version

### Trabajo realizado

- Actualizado `main` desde `origin` y creada la rama `V-03.02` desde su HEAD.
- Alineadas las versiones runtime en `V-03.02` / `3.2.0` y los valores de release e instalacion.
- Actualizados `version_actual.md` y creado `v-03.02.md`.

### Archivos tocados

- `Atlas Balance/VERSION`, `Atlas Balance/Directory.Build.props`, frontend `package.json` y `package-lock.json`, `SeedData.cs`, `.github/workflows/release.yml`, scripts de release/instalacion y documentos de version.

### Comandos ejecutados

- `git fetch origin main`
- `git pull --ff-only origin main`
- `git switch -c V-03.02`
- `Check-VersionAlignment.ps1 -ExpectedVersion V-03.02`: OK. `git diff --check`: OK.

### Resultado de verificacion y pendientes

- Pull: `main` ya estaba actualizado respecto a `origin/main`.
- La version runtime y los defaults de release quedan alineados en `V-03.02` / `3.2.0`.
- No se ha hecho push.

## Objetivo

## 2026-10-05 - V-03.02 - Arranque parcial del entorno de desarrollo

### Trabajo realizado

- Se inició Vite del checkout en `http://localhost:5173` (PID 14748).
- El backend no se inició: Docker no está disponible, PostgreSQL no escucha en
  `5433` y no hay `appsettings.Development.json` ni cadena de conexión local.
  `appsettings.json` mantiene `DefaultConnection` vacío; no se conectó a la BD real.

### Archivos tocados

- `Documentacion/DOCUMENTACION_CAMBIOS.md`
- Logs locales ignorados: `Atlas Balance/frontend/logs/dev/vite-dev.out.log` y
  `vite-dev.err.log`.

### Comandos ejecutados

- Inspección de procesos/puertos, configuración y artefactos locales.
- Inicio de `npm.cmd run dev -- --host 127.0.0.1 --port 5173 --strictPort`.
- `Invoke-WebRequest http://localhost:5173`: HTTP 200.

### Verificación y pendientes

- Frontend responde por HTTP 200 en el puerto de desarrollo.
- API en `5002` y PostgreSQL de desarrollo en `5433` no están activos.
- Pendiente configurar/arrancar Docker PostgreSQL local y cadena de conexión de
  desarrollo; después iniciar API en `5002` y comprobar `/api/health`.

---

## 2026-10-05 - V-03.02 - Acceso del administrador local de desarrollo

### Trabajo realizado

- Restablecida la contraseña de la cuenta administradora en la base local de desarrollo (`atlas_balance_db`, puerto 5433, volumen `atlasbalance_pgdata`).
- La cuenta quedó activa y desbloqueada; se conservaron MFA y el estado de primer acceso. Se rotó el sello de seguridad y no quedaron sesiones de refresco activas.
- El valor de la contraseña no se registra. No se ejecutó ninguna operación contra la instalación real.

### Archivos y comandos

- Archivo actualizado: `Documentacion/DOCUMENTACION_CAMBIOS.md`.
- Base de datos local: actualización de la cuenta admin y revocación de tokens de refresco vigentes.
- Inspección acotada de `atlas_balance_db` y consultas SQL de estado; contraseña y hash omitidos.
- Comprobación HTTP de `http://localhost:5002/api/health` y del proxy de `http://localhost:5173/api/health`.

### Verificación y pendiente

- La consulta de base de datos confirmó cuenta activa, desbloqueada, MFA conservado, bcrypt con coste 12, cero fallos y cero tokens vigentes.
- API y frontend respondieron HTTP 200 en sus puertos de desarrollo (5002 y 5173).
- Pendiente: iniciar sesión manualmente en la pestaña del navegador.

---

## 2026-10-05 - V-03.02 - Diagnóstico del error 500 de login en desarrollo

### Trabajo realizado

- Se correlacionó el mensaje visible del login con el log del backend: falla al descifrar el secreto MFA porque falta la clave de Data Protection que lo protegía.
- Se comprobó que el anillo local del checkout contiene solo la clave nueva. Tras autorización expresa, se eliminó el secreto MFA irrecuperable únicamente de la cuenta de desarrollo; la API permanece sana en `/api/health`.
- El MFA se restableció únicamente en desarrollo tras autorización expresa. La instalación real no se tocó.

### Archivos y comandos

- Archivos actualizados: `Documentacion/LOG_ERRORES_INCIDENCIAS.md` y `Documentacion/DOCUMENTACION_CAMBIOS.md`.
- Inspección de logs y código de login/Data Protection; lectura de nombres de claves, sin abrir ni copiar su contenido; consulta de solo lectura del estado MFA.
- No se enviaron credenciales desde el navegador ni se ejecutaron tests.

### Verificación y pendiente

- Confirmada la excepción `CryptographicException` de Data Protection durante la creación del reto MFA; el 500 no es el error JSONB de Hangfire.
- Verificación posterior: MFA desactivado y secreto anterior eliminado; cuenta activa, contraseña intacta, sin bloqueos ni tokens de refresco vigentes.
- Pendiente: iniciar sesión manualmente en la pestaña y completar la nueva configuración de MFA.

## 2026-11-16 - V-03.02 - Extractos: avance parcial

- Trabajo: resaltado de filas con alerta en extractos y cuenta; barra horizontal superior sincronizada en extractos. La tabla de cuenta sigue siendo independiente.
- Archivos: `frontend/src/components/extractos/ExtractoTable.tsx`, `frontend/src/styles/layout/extractos.css`, `frontend/src/styles/layout/dashboard.css`, documentacion tecnica, de usuario, de version y esta bitacora.
- Comandos: `npx tsc --noEmit`, `npm run lint`, `npx eslint src --max-warnings 0`, `git diff --check`.
- Verificacion: TypeScript y ESLint de `src` correctos. Lint general falla por 29 errores en `.test-dist-v2`, carpeta generada preexistente. Sin validacion visual ni funcional autenticada.
- Decisiones visuales: fondo con token de aviso existente; barra superior adicional. Pendiente validar contraste y sincronizacion en navegador.
- Pendientes: visibilidad de columnas con datos, orden persistente de visibles y ocultas, tabla compartida con filtros y acciones en cuenta y scroll superior en cuenta. No se considera finalizada la tarea.
# 2026-11-16 - V-03.02 - Busqueda global de titulares y cuentas

- Trabajo: busqueda diferida en los listados existentes, con pais activo y permisos del servidor; acceso directo al detalle con raton o teclado. Se mantienen los grupos y estilo de la paleta; se identifican las cuentas por titular. Limite: diez resultados por categoria, sin paginacion en la paleta.
- Archivos: `frontend/src/components/layout/CommandPalette.tsx`, `TopBar.tsx`, documentos tecnico, usuario, version y bitacora.
- Comandos: `npx tsc --noEmit`, compilacion de tests con `tsconfig.test.v2.json` hacia `.test-dist-v2` y `node --test --test-force-exit .test-dist-v2/tests/*.test.js`, `npm run lint`, `npx eslint . --max-warnings 0 --ignore-pattern '.test-dist-v2/**'`, `git diff --check`.
- Resultado: TypeScript y 104 tests correctos. `npm run lint` incluye los JS generados preexistentes de `.test-dist-v2` y falla; ESLint excluyendo esa carpeta pasa. `git diff --check` pasa. Sin prueba visual ni sesion autenticada. Pendiente comprobar busqueda real con distintos permisos y paises.

## 2026-11-16 - V-03.02 - Finalizacion de columnas y tabla de extractos

- Trabajo: columnas extra con datos disponibles en el ambito autorizado; orden y visibilidad persistentes; tabla compartida en detalle de cuenta con seleccion masiva, busqueda, fechas, filtros, alerta amarilla y barras horizontales sincronizadas. Las tres columnas de contexto se excluyen de la cuenta. Se bloquean guardados simultaneos de preferencias.
- Archivos: `backend/src/AtlasBalance.API/Controllers/ExtractosController.cs`, `UsuariosController.cs`, `DTOs/ExtractosDtos.cs`, `Data/AppDbContext.cs`, `Models/Entities.cs`, `Migrations/AppDbContextModelSnapshot.cs`, `Migrations/20261116090000_AddOrdenColumnasPreferencias.cs`, tests de ExtractosController y UsuariosController; `frontend/src/components/extractos/ExtractoTable.tsx`, `extractoColumns.ts`, `pages/ExtractosPage.tsx`, `pages/CuentaDetailPage.tsx`, estilos de extractos y dashboard, `e2e/visual/extractos-comportamiento.spec.ts`; version y documentacion tecnica, de usuario y de incidencias.
- Comandos: `npx tsc --noEmit`, `npx eslint . --max-warnings 0 --ignore-pattern .test-dist-v2`, `npm run test:unit`, `npx tsc -p tsconfig.test.v2.json --outDir .test-dist-atlas-columnas --tsBuildInfoFile .test-dist-atlas-columnas/tsconfig.tsbuildinfo`, `node --test --test-force-exit` sobre tests compilados, `npm run build`, compilacion backend y tests xUnit dirigidos, Playwright de `e2e/visual/extractos-comportamiento.spec.ts` con servidor existente y API simulada, `docker info`, `git diff --check`.
- Resultado: TypeScript y ESLint con exclusion de artefactos previos correctos; 104/104 tests unitarios frontend en salida alternativa; backend compilado y pruebas dirigidas de extractos 2/2 y permisos 4/4; Playwright 3/3 y 2/2 de guardado lento con API simulada. El comando normal de tests frontend falla por EPERM en `.test-dist` existente; lint sin exclusion falla por `.test-dist-v2` preexistente. Docker no se pudo verificar por acceso denegado a su configuracion.
- Decisiones visuales: fondo de aviso existente para alerta en ambos temas; barra horizontal arriba y abajo; flechas accesibles para ordenar. Sin pendientes de diseno identificados en pruebas simuladas.
- Pendientes: validar migracion y persistencia real en PostgreSQL, pruebas autenticadas y despliegue. El filtro por columna se aplica solo a la pagina cargada. Las preferencias antiguas muestran una vez todas las columnas extra con datos, incluso las que antes se ocultaron, porque no se distinguian de las nuevas.

## 2026-11-16 - V-03.02 - Buscador y permisos

- Se ocultan resultados de cache mientras se revalidan o falla la consulta, para no mostrar coincidencias anteriores tras cambios de permisos. El servidor sigue filtrando titulares y cuentas por alcance, y el detalle comprueba acceso. No se modifican permisos.
- Archivos: `frontend/src/components/layout/CommandPalette.tsx`, bitacora, log y version. Decision visual: se mantiene la paleta; sin cambios de diseno.
- Verificacion: build en `.search-build`, ESLint de componentes, 19 pruebas de `UserAccessServiceTests` y `UserAccessScopeMatrixTests`; Vite en 127.0.0.1:5173 entrega los componentes actualizados. La inspeccion visual del navegador se bloqueo por identificacion insegura de URL. Pendiente recargar pagina y probar con usuarios y permisos reales.
- Comandos: `npm run build` con `VITE_BUILD_OUT_DIR=.search-build`, `npx eslint` sobre ambos componentes, `dotnet` ejecutando el runner xUnit con filtros de clase, consulta HTTP local a componentes de Vite.

## 2026-11-16 - V-03.02 - Validacion de migracion y entorno demo

- Trabajo: comprobacion de la aplicacion en Chrome, migracion en PostgreSQL aislado y en la BD demo, reinicio de la API local con binario Release y comprobaciones de salud. No se despliega ni publica release.
- Archivos: este registro, `Versiones/v-03.02.md` y log de incidencias. Se creo y elimino el script temporal de reinicio; logs de validacion en `Atlas Balance/logs/dev`.
- Comandos: `docker ps`, consultas `psql` de esquema e historial, `dotnet test ... -c Release --no-restore`, test xUnit dirigido de concurrencia en PostgreSQL/Testcontainers, `Check-VersionAlignment.ps1`, healthchecks HTTP locales, `git diff --check`.
- Resultado: alineacion OK; test PostgreSQL 1/1 con `Database.MigrateAsync`; columna e historial de migracion presentes en demo; API y proxy HTTP 200. Chrome mostro filas y selector de columnas; despues el control de ventana devolvio acceso denegado. Suite completa interrumpida tras cinco minutos sin resultado, no se considera aprobada.
- Incidencia: el primer reinicio consulto salud por 127.0.0.1, no por localhost, y dio falso negativo; el segundo arranco la API y verifico salud por localhost. Clave de firma ausente en el entorno del proceso.
- Pendientes: completar suite de backend con Docker, validar funcionalmente en Chrome autenticado contra la API actualizada y preparar release firmado. No declarar lista la publicacion hasta cerrar estos puntos.

## 2026-11-16 - V-03.02 - Modos de revision y edicion en cuenta

- Trabajo: selector de revision y edicion avanzada en el desglose de cuenta. La revision es inicial, bloquea las acciones de movimiento y limpia la seleccion al volver; edicion avanzada respeta los permisos existentes.
- Archivos: `Atlas Balance/frontend/src/pages/CuentaDetailPage.tsx`, `Atlas Balance/frontend/e2e/visual/extractos-comportamiento.spec.ts`, `Documentacion/Versiones/v-03.02.md`, documentacion tecnica, de usuario y esta bitacora.
- Comandos: `npx tsc --noEmit`, `npx eslint` sobre los dos archivos, `npm run build`, compilacion de unitarios con `tsc -p tsconfig.test.v2.json` a `.test-dist-modo-cuenta` y `node --test`, Playwright del caso `detalle de cuenta` con API simulada y Vite existente, `git diff --check`.
- Verificacion: TypeScript, ESLint y build correctos; 104/104 unitarios y prueba de navegador 1/1, incluyendo bloqueo, desbloqueo y limpieza de seleccion. No se comprobo en sesion autenticada real.
- Decision visual: se reutiliza el selector y estilos de Extractos, situado junto al desglose de cuenta. Sin pendientes de diseno conocidos; pendiente comprobacion manual autenticada y gates de release previamente registrados.

## 2026-11-17 - V-03.02 - LOTE 7 del informe de revision integral (F-2, F-4, F-5, F-6, F-7, F-8, F-9, F-10, R-4, R-5, R-6, R-7, R-8, B-16)

- Trabajo: correccion de los catorce IDs del LOTE 7 (frontend con apoyo en backend). Papelera con filtro de solo eliminados y paginacion real en el servidor; los sondeos de Configuracion y Backups sin toast y con cancelacion al desmontar; el banner de alertas con un solo escritor y descarte anclado al contenido; la nota de alerta sin PATCH inutil; borrado y marcado en lote con concurrencia limitada (4) en vez de una peticion por fila; campos numericos de Configuracion que se pueden vaciar sin mandar ceros silenciosos; la celda editable conserva lo escrito si el guardado falla; clave de idempotencia regenerada por lote (y por cambio de cuenta en la creacion, que era un 409 real); el detalle de cuenta deja de invalidar la consulta que tiene activa; los permisos de la tabla de extractos se evaluan una vez por celda y render; debounce en las busquedas de Formatos, Usuarios y "Modelo"; N+1 de los resumenes de titulares eliminado e insercion masiva troceada en bloques de 1.000 dentro de la misma transaccion; `/ia/config` sin sondeo cada 30 s; "hoy" unificado en un helper compartido con la zona horaria del usuario.
- Decisiones de producto: F-7 se resuelve con concurrencia limitada en el cliente (no endpoint de lote en el backend); F-8 vacia el campo pero conserva el ultimo valor valido, porque el backend no admite `null` en esos DTO.
- Archivos frontend: `src/pages/PapeleraPage.tsx`, `src/pages/CuentaDetailPage.tsx`, `src/pages/ConfiguracionPage.tsx`, `src/pages/BackupsPage.tsx`, `src/pages/ImportacionPage.tsx`, `src/pages/ConciliacionPage.tsx`, `src/pages/FormatosImportacionPage.tsx`, `src/pages/UsuariosPage.tsx`, `src/components/extractos/ExtractoTable.tsx`, `src/components/extractos/EditableCell.tsx`, `src/services/api.ts`, `src/hooks/queries/useAlertasActivasQuery.ts`, `src/hooks/queries/useIaConfigQuery.ts`, `src/stores/alertasStore.ts`, `src/utils/` (`hoy.ts`, `concurrencia.ts`, `errorToast.ts`, `pollLifecycle.ts`, `numberInput.ts`, `notaAlerta.ts`, `canEditCellCache.ts`, `editableCellSave.ts`, `iaConfigSource.ts`, `peticionSecuencial.ts`), `eslint.config.js` (ignora `.dist-*`, carpeta de build residual que hacia fallar el lint) y `package.json` (los 17 tests nuevos en `test:unit`).
- Archivos backend: `src/AtlasBalance.API/Controllers/TitularesController.cs`, `CuentasController.cs`, `ExtractosController.cs`, `UsuariosController.cs` (parametro `soloEliminados` y resumenes agrupados) y `Services/ImportacionService.cs` (insercion por bloques).
- Tests nuevos frontend (17): `hoy`, `notaAlerta`, `canEditCellCache`, `loteConcurrencia`, `cuentaDetailInvalidacion`, `cuentaDetailHoy`, `busquedaDebounce`, `errorToast`, `sondeosSinToast`, `configuracionCamposNumericos`, `configuracionDebounceModelo`, `alertasBannerDismissal`, `editableCellSave`, `importacionIdempotencia`, `iaConfigSondeo`, `papeleraSoloEliminados`, `papeleraRespuestasFueraDeOrden`. Tests nuevos backend (3): `PapeleraSoloEliminadosTests`, `ExtractosResumenConsultasTests`, `ImportacionR7Tests`.
- Comandos: `npx tsc -p tsconfig.json --noEmit`, `npx tsc -p tsconfig.test.v2.json --outDir .test-dist-l7-final`, `node --test --test-force-exit .test-dist-l7-final/tests/*.test.js`, `npx eslint . --max-warnings 0`, `npx vite build --outDir dist-l7-final`, `dotnet test -c Release --artifacts-path artifacts-l7-orchestrator`, `icacls .test-dist` / `icacls dist`, `git cat-file -e HEAD:<fichero>`, `git diff --check`.
- Nota sobre el script `test:unit`: su lista queda en **37 ficheros**, los 37 que se compilan (20 previos + 17 de este lote, mas el de la sesion paralela `dashboardDivisasSinTipoCambio.test.ts`, que se anadio a la lista durante este lote). Comprobado fichero a fichero: no queda ninguno sin listar, de modo que el script canonico ejecuta todo lo que hay.
- Resultado: TypeScript 0 errores, ESLint 0 errores, **227/227 pruebas unitarias del frontend**, build de Vite correcto (5,5 s). En backend, suite completa **1230/1231** con 1 unico fallo, ajeno a este lote: `AuthServiceTests.Login_Should_Not_Reveal_When_User_Is_Already_Locked` espera 401 y recibe 429 por el limite por IP introducido en el commit ajeno `01ffeb3` (LOTE 2); reproducido en aislamiento (43 correctos y 1 fallo en esa clase). Los 5 fallos de `TiposCambioManualTests` que habia antes los corrigio la sesion paralela durante este lote.
- Revision posterior (2026-11-17, tras la revision del lote): F-2 incluia en el encargo "descarta respuestas fuera de orden" y no estaba implementado. Corregido con `utils/peticionSecuencial.ts`: `load()` numera cada peticion y solo escribe filas, total, error y estado de carga si su respuesta sigue siendo la vigente. El test nuevo `papeleraRespuestasFueraDeOrden` pasa de 3 de 6 en rojo a 6/6 y la suite del frontend sube a 227/227.
- Comprobaciones de entorno que conviene no dar por codigo: `npm run test:unit` y `npm run build` canonicos **si se ejecutaron** y fallaron por permisos (`TS5033 EPERM` sobre 22 ficheros de `.test-dist/` y `EPERM ... copyfile public/fonts/Geist-Bold.woff2 -> dist/fonts/...`). `icacls` confirma que `.test-dist` y `dist` conceden `(M)` solo a dos SID y a `TRAKERIA\CodexSandboxUsers`, y a `BUILTIN\Usuarios` solo `(RX)`: el usuario que ejecuta (`trakeria\usuario`) no puede escribir ahi. Con `--outDir` propio, las mismas comprobaciones pasan.
- **Aviso de estado del repositorio:** el HEAD actual (`01ffeb3`, "LOTE 2") se llevo por delante codigo en vuelo de este LOTE 7 (`ConfiguracionPage.tsx` y `services/api.ts`) que importan `@/utils/numberInput`, `@/utils/pollLifecycle` y `@/utils/errorToast`: esos tres ficheros son nuevos y **no estan en HEAD**, asi que un clon limpio de HEAD no compila hasta que se commitee el LOTE 7. No se ha hecho commit porque este lote no commitea sin que lo pidas; la accion pendiente es un commit propio con `git commit --only` y sus ficheros.
- Al compilar la suite de backend **fuera** del arbol (`--artifacts-path` en `%TEMP%`) aparecen 14 fallos con `FileNotFoundException` y ninguno de los fallos reales: son los tests que leen ficheros de codigo o de configuracion (`AppsettingsValidezTests`, `SerilogHttpClientLoggingTests`, `IaFechasOperativasTests`, `ControllerAuthorizationCoverageTests` y `WatchdogLogConfigurationTests`). La causa es `TestSourceLocator.Find`, que sube desde el binario del test buscando `src/...` y, si la carpeta de compilacion esta fuera del repositorio, no encuentra la raiz. Con `--artifacts-path` dentro del repo esos tests pasan. Es infraestructura compartida de tests y no se ha tocado.
- Decisiones visuales: la papelera usa el bloque de paginacion existente (`users-pagination`, mismo estilo que Usuarios); el importe de los extractos se muestra en la divisa real de la cuenta; la celda en modo edicion mantiene su ancho original (`justifyItems: start` en `.cell-edit-shell`) y muestra debajo el aviso "No guardado. Revisa el dato e intentalo otra vez." reutilizando `cell-save-state--error`; el banner de alertas conserva su aspecto y su boton "Ocultar"; el aviso de error de la celda en densidad compacta (36 px) puede solaparse con la fila siguiente: pendiente de comprobacion visual.
- Pendientes de diseno: ninguno abierto de forma bloqueante; queda por mirar en navegador el aviso de error de la celda en densidad compacta y la paginacion de la papelera.
- Pendientes tecnicos: los extractos de cuentas YA borradas siguen sin salir en la papelera (filtro de alcance preexistente); la atomicidad del troceado no se ha probado contra PostgreSQL (el test usa InMemory y solo comprueba la estructura del troceado); `iaAvailabilityStore.load()` y `aiChatStore.ensureConfig()` siguen siendo copias de la configuracion de IA; el N+1 de la evolucion de OpenClaw (`IntegrationOpenClawController.cs:520`) queda pendiente: no es N+1 de SQL (el catalogo de tipos de cambio esta cacheado) sino N llamadas async por extracto.
- Bloqueos: `npm run test:unit` y `npm run build` canonicos siguen fallando por permisos heredados de otra cuenta en `.test-dist/` y `dist/` (M-10 del informe); las mismas comprobaciones pasaron con carpetas de salida nuevas. Sin validacion visual ni sesion autenticada. Nada commiteado.

# 2026-11-17 - V-03.02 - Lote 5 del informe de revision integral (A-9, B-1 a B-9, B-13, B-14, B-15, M-9 parcial; B-17 pendiente)

- Trabajo: correccion de los quince IDs del LOTE 5 del informe `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md`. Sin commit, sin push y sin cambio de version. Las cuatro decisiones previas (B-1, B-2, B-14 y B-17) se pidieron al usuario y se respondieron por auto-seleccion de tiempo del formulario, no por el usuario: quedan aplicadas como supuesto y son reversibles.
- Archivos backend: `src/AtlasBalance.API/Services/PlazoFijoService.cs`, `DashboardService.cs`, `TiposCambioService.cs`, `ImportacionService.cs`, `ConciliacionService.cs`, `AlertaService.cs`, `RevisionService.cs`; `Controllers/ExtractosController.cs`, `AuditoriaController.cs`, `CuentasController.cs`, `TiposCambioController.cs`, `IntegrationOpenClawController.cs`; `DTOs/DashboardDtos.cs`; `Data/AppDbContext.cs`; `Migrations/AppDbContextModelSnapshot.cs` y `Migrations/20261117120000_IndicesParcialesHuellaYConciliacion.cs` (escrita a mano, con SQL idempotente, sin scaffold).
- Tests backend nuevos: `PlazoFijoJsonbPostgresTests`, `DashboardDivisasSinTipoCambioTests`, `TiposCambioManualTests`, `ImportacionLote5Tests`, `ImportacionLote5PostgresTests`, `ConciliacionLote5Tests`, `ConciliacionExtractoUnicoTests`, `ExtractosAlertaPostTests`, `PaginacionDesempateTests`, `CuentasDivisaConExtractosTests`, `AlertaAdvisoryLockPostgresTests`, `IndicesParcialesLote5Tests`, `SnapshotModelDriftTests` (13 ficheros; 5 de ellos contra PostgreSQL real con Testcontainers).
- Archivos frontend: `src/utils/formatters.ts`, `src/utils/dashboardDivisasSinTipoCambio.ts` (nuevo), `src/components/dashboard/DivisasSinTipoCambioAviso.tsx` (nuevo), `src/types/index.ts`, `src/pages/DashboardPage.tsx`, `DashboardTitularPage.tsx`, `CuentasPage.tsx`, `ConfiguracionPage.tsx`, `ConciliacionPage.tsx`, `src/styles/layout/dashboard.css`, `package.json` (alta del test `dashboardDivisasSinTipoCambio` en el script `test:unit`, que es una lista explicita). Tests frontend nuevos: `parseEuropeanNumber.test.ts`, `dashboardDivisasSinTipoCambio.test.ts`.
- Documentacion: `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md` (seccion 17 y estado por ID), `Versiones/v-03.02.md`, `LOG_ERRORES_INCIDENCIAS.md` (9 entradas), `REGISTRO_BUGS.md` (cierre del bug de `jsonb` de A-9, cierre del test dependiente de la fecha, actualizacion de la entrada de M-9 y alta de un bug nuevo del advisory lock), `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md` y esta bitacora.
- Comandos: `dotnet test -c Release --artifacts-path .artifacts/orch-full` (suite completa, con PostgreSQL real via Testcontainers), `dotnet test -c Release --artifacts-path .artifacts/orch-b2 -- --filter-class AtlasBalance.API.Tests.TiposCambioManualTests`, `dotnet test -c Release --artifacts-path .artifacts/orch-conc -- --filter-class AtlasBalance.API.Tests.ConciliacionLote5Tests`, `dotnet test -c Release --artifacts-path .artifacts/orch-conc2 -- --filter-class AtlasBalance.API.Tests.ConciliacionExtractoUnicoTests`, `npx tsc --noEmit`, `npx eslint src tests --max-warnings 0`, `npx tsc -p tsconfig.test.v2.json --outDir .test-dist-l5` + `node --test --test-force-exit` sobre los 36 ficheros compilados, `npx vite build --outDir ../.artifacts/fe-l5-dist`.
- Resultado: backend **1231/1231** en `AtlasBalance.API.Tests` (incluida la suite de PostgreSQL real) y **15/15** en `AtlasBalance.Caching.Tests`, sin fallos. Frontend: TypeScript 0 errores, ESLint 0 errores, **227/227** pruebas unitarias y build de Vite correcto (2,7 s).
- Metodo: once subagentes con ficheros disjuntos (A-9; B-3/B-4/B-9/B-13 backend; B-5/B-6; B-8; M-9 + indices; B-1; B-2; B-7/B-15; B-13 frontend; B-14). El orquestador reviso cada diff y corrigio tres cosas: la matriz de transiciones de conciliacion (el subagente bloqueaba `excepcion -> conciliada`, que el boton "Conciliar" de la pantalla si permite), el desempate de la paginacion de lotes (fuera del ownership del subagente) y una asercion que no encajaba con el indice unico nuevo (EF envuelve el 23505 en `DbUpdateException`). El subagente de B-2 se quedo atascado a mitad y lo termino el orquestador reutilizando el test que ya habia escrito en rojo.
- Correccion adicional del orquestador, no prevista en el encargo: al verificar B-3 contra PostgreSQL real se vio que `POST /api/importacion/lotes/{id}/confirmar` (el camino que usa el frontend) respondia **siempre 409** porque `TryAcquireAdvisoryXactLockAsync` leia con `SqlQueryRaw<int>` el boolean que devuelve `pg_try_advisory_xact_lock` (Npgsql lanza `InvalidCastException` y el fail-closed devolvia `false`). Sin ese arreglo la reimportacion de un lote revertido era inservible, asi que B-3 no era realmente "corregido": se lee como `SqlQueryRaw<bool>` y se anade un test de extremo a extremo por el camino real de la API (rojo sin el arreglo: 1 de 3 fallos; verde con el: 3/3). Es el unico cambio del lote que toca un codigo que no estaba en la lista de IDs, y se hace precisamente porque bloqueaba uno que si lo estaba.
- B-17 (parcial): el **criterio** del saldo actual no se cambia (sigue siendo la fila con mayor `fila_numero`), pero la validacion **detecta por las fechas** si el fichero pegado viene del mas antiguo al mas reciente y **avisa antes de confirmar** (`ImportacionValidarResponse.Advertencias` + aviso en la pantalla de importacion). La deteccion no tiene falsos positivos: solo avisa con al menos dos fechas distintas y en orden estrictamente creciente al bajar por el bloque. La pregunta al usuario se respondio por auto-seleccion y la opcion elegida contradice la convencion documentada de la app y de los datos de demostracion del propio producto, asi que el cambio de criterio queda pendiente de una respuesta confirmada.
- Decisiones visuales: el aviso de divisas sin tipo de cambio reutiliza la clase de aviso que ya existia en el dashboard (`.dashboard-aviso`, agrupada con `.dashboard-plazo-aviso`) y aparece bajo el total en el dashboard, bajo el KPI de total en el titular y bajo "Saldos por divisa" en Cuentas, con texto "El total no incluye GBP y USD. No hay tipo de cambio para esas divisas." Las filas de conciliacion en estado `resuelta` dejan de oferecer "Conciliar" y "Excepcion"; las que estan en `excepcion` los siguen ofreciendo.
- Pendientes de diseno: el aviso de la celda editable en densidad compacta (senalado por el LOTE 7) y el aspecto del aviso nuevo de divisas sin tasa siguen sin mirar en navegador.
- Pendientes tecnicos: **B-17** queda sin corregir (ver seccion 17 del informe): la pregunta al usuario se respondio por auto-seleccion y la opcion elegida contradice la convencion documentada de la numeracion de filas, asi que no se toco codigo. **M-9** queda parcial: el `xmin` de `PLAZOS_FIJOS` ya esta en el snapshot, pero la deriva restante (27 diferencias y divergencias de esquema en la base real) exige una migracion de esquema propia, asi que el `Ignore` de `PendingModelChangesWarning` se mantiene. El bug del `pg_try_advisory_xact_lock` (confirmacion de lote siempre 409) se ha cerrado en este lote, con su entrada en `REGISTRO_BUGS.md` marcada como cerrada.
- Bloqueos: `npm run test:unit` y `npm run build` canonicos siguen fallando por permisos heredados de otra cuenta en `.test-dist/` y `dist/` (M-10 del informe); las mismas comprobaciones pasaron con carpetas de salida nuevas. Sin validacion visual en navegador ni sesion autenticada real. Nada commiteado.

# 2026-11-18 - V-03.02 - Lote 8 del informe de revision integral (M-17)

- Trabajo: implementacion de **M-17** del informe de revision integral (`Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md`, seccion 8.2): archivado de la documentacion anterior a V-03 y partida de la bitacora. Decision ya fijada por el usuario: archivar en vez de borrar, y partir la bitacora. Sin commit, sin push y sin cambio de version.
- M-17 (archivado): 18 ficheros movidos con `git mv` a `Documentacion/Archivo/`, que quedan como renombrados `R` en git (reversibles con `git revert`): 15 de la raiz de `Documentacion/` (`INFORME_INCIDENTE_LOGIN_MFA_V0207.md`, `REVIEW_REPORT_2026-06-30.md`, `AUDITORIA_SEGURIDAD_PRE_LAUNCH_2026-08-04.md`, `AUDITORIA_CONCURRENCIA_2026-07-10.md`, `AUDITORIA_SEGURIDAD_BUGS_PRE_INTERNET_2026-07-10.md`, `INFORME_REVISION_2026-09-24.md`, `AUDITORIA_USO_BUGS_SEGURIDAD_V-01.05_2026-04-25.md`, `SEGURIDAD_AUDITORIA_V-01.02.md`, `SEGURIDAD_AUDITORIA_V-01.03.md`, `SEGURIDAD_AUDITORIA_V-01.05.md`, `SEGURIDAD_AUDITORIA_V-01.06.md`, `SEGURIDAD_AUDITORIA_V-02-04.md`, `SEGURIDAD_CHECKLIST_APP_V-01.05_2026-05-01.md`, `INCIDENCIAS_INSTALACION_WINDOWS_SERVER_2019_V-01.05.txt`, `mejoradiseno.md`) y 3 de `Documentacion/Diseno/` (`AUDITORIA_UI_UX_GLOBAL_V-02-02.md`, `AUDITORIA_GENERAL_MAVIS_V-02-02.md`, `AUDITORIA_UI_UX_SKILLS_V-02-02.md`). No se ha reescrito el contenido de ninguno; indice en `Documentacion/Archivo/README.md`.
- M-17 (no movidos): `DIAGNOSTICO_PERMISOS_CUENTAS_2026-10-05.md` y `AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md` son de V-03.02 y los cita la documentacion viva; `CORRECCIONES.md` es el companion de `SPEC.md` (no es un informe) y `SEGURIDAD_RESPUESTA_INCIDENTES.md` es un procedimiento de respuesta ante incidentes (no hay documento que lo sustituya). No se han tocado `Documentacion/Versiones/`, `SPEC.md`, `documentacion.md`, `DOCUMENTACION_TECNICA.md`, `DOCUMENTACION_USUARIO.md`, `SKILLS_LOCALES.md`, `INFORME_REVISION_INTEGRAL_2026-11-16.md`, `RUNBOOK_INSTALACION_CLIENTE.md`, `INVENTARIO_DATOS_PERSONALES_V-03.02.md` ni `frontend/**`, `backend/**` y `.github/**`.
- M-17 (bitacora partida): `DOCUMENTACION_CAMBIOS.md` pasa de **23.565 lineas** a **614 lineas** con solo las entradas de V-03.02 (30 con esta). El historico de V-03.01 y anteriores pasa a `Documentacion/DOCUMENTACION_CAMBIOS_ARCHIVO.md` (**23.030 lineas**, con cabecera que dice desde donde se continua en el fichero vivo). El corte no es solo el principio del fichero: las entradas de V-03.02 estan dispersas (bloque inicial, lineas 1-620 del original, y bloque final, lineas 28.170-28.344), asi que se han movido las dos zonas. La entrada de V-03.02 que otro agente anadio hoy al final (`# 2026-11-18 - V-03.02 - Lote 8 del informe de revision integral (M-14, M-15)`) se ha subido al principio del fichero vivo. Al principio del fichero vivo hay una linea de puntero al historico.
- M-17 (referencias): actualizadas a la nueva ruta 18 referencias en 6 ficheros: `Documentacion/AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md` (2), `Documentacion/DOCUMENTACION_TECNICA.md` (4), `Documentacion/REGISTRO_BUGS.md` (6), `Documentacion/LOG_ERRORES_INCIDENCIAS.md` (4), `Documentacion/Archivo/SEGURIDAD_AUDITORIA_V-01.05.md` (1) y `Documentacion/Archivo/AUDITORIA_GENERAL_MAVIS_V-02-02.md` (1). Se dejan sin tocar, por historico o por alcance, las menciones de `Documentacion/Versiones/*.md`, del informe fuente, de `Documentacion/Diseno/INFORME_IMPECCABLE_CRITIQUE_AUDIT_V-03.02.md` (linea 13) y del comentario de codigo en `Atlas Balance/backend/src/AtlasBalance.API/Caching/CacheService.cs` (linea 19).
- Archivos tocados: `Documentacion/Archivo/` (18 ficheros movidos + `README.md` nuevo), `Documentacion/DOCUMENTACION_CAMBIOS.md` (partida), `Documentacion/DOCUMENTACION_CAMBIOS_ARCHIVO.md` (nuevo), `Documentacion/AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md`, `Documentacion/DOCUMENTACION_TECNICA.md`, `Documentacion/REGISTRO_BUGS.md`, `Documentacion/LOG_ERRORES_INCIDENCIAS.md` (solo referencias de ruta) y esta bitacora.
- Comandos: `git mv` para los 18 movimientos, `git status --porcelain` para comprobar que salen como renombrados (`R`), lectura byte a byte del original de la bitacora (28.344 lineas, LF, sin BOM) y corte en los limites de bloque verificados con `Select-String '^#{1,2} \d{4}-\d{2}-\d{2}'`, y `grep` (ripgrep) del nombre de cada fichero movido en todo el repositorio antes de moverlo.
- Resultado: los 18 movimientos aparecen como renombrados `R` en `git status --porcelain`; `Documentacion/DOCUMENTACION_CAMBIOS.md` queda con 30 entradas de V-03.02 y `DOCUMENTACION_CAMBIOS_ARCHIVO.md` con las 23.030 lineas de V-03.01 y anteriores mas cabecera; las 15 referencias por ruta apuntan ya a `Documentacion/Archivo/`; `git status --porcelain -- Documentacion/Versiones Documentacion/SPEC.md Documentacion/DOCUMENTACION_TECNICA.md Documentacion/DOCUMENTACION_USUARIO.md` solo muestra `DOCUMENTACION_TECNICA.md` por las 4 referencias de ruta actualizadas (no por cambios de contenido).
- Supuestos: (1) la decision de archivar estaba ya tomada por el usuario y no se ha cuestionado; (2) "anterior a V-03" se interpreta por la version de la entrada, no por la fecha, asi que los documentos de V-03.02 (`DIAGNOSTICO_PERMISOS_CUENTAS_2026-10-05.md`, `AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md`) se quedan; (3) las menciones en prosa historica no son enlaces, asi que no se reescriben: solo las referencias por ruta.
- Pendientes: la cita en prosa de `AUDITORIA_UI_UX_GLOBAL_V-02-02.md` en `Documentacion/Diseno/INFORME_IMPECCABLE_CRITIQUE_AUDIT_V-03.02.md` (linea 13) y el comentario de `CacheService.cs` (linea 19) siguen nombrando ficheros ya archivados; corregirlos cuando se toque ese codigo o ese informe. Los tres informes V-02-02 estan en `Documentacion/Archivo/` y no en `Documentacion/Diseno/Archivo/` (que es de M-11); si se prefiere la carpeta de diseno, es un `git mv` de tres ficheros. Bloqueos: ninguno. Nada commiteado.

# 2026-11-18 - V-03.02 - Lote 8 del informe de revision integral (D-5)

- Trabajo: implementacion de **D-5** del informe de revision integral (`Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md`, seccion 7): clases compartidas `.page` y `.page-header` y cierre de la capa de parches `layout/system-coherence.css`. Sin commit, sin push y sin cambio de version. Regla de oro aplicada: se mueve CSS, no se reescribe; el conjunto de reglas que se aplican a cada pantalla debe quedar identico.
- Clases compartidas nuevas (`src/styles/layout/page.css`): `.page` (contenedor `width:100%; max-width:1280px; margin-inline:auto`), `.page--wide` (`max-width:1600px`), `.page-header` (base de cabecera en flex + borde inferior + `padding-bottom`), `.page-header__title` (h1 a 28/600, `line-height:1.08`, `text-wrap:balance`), `.page-header__lead` (`min-width:0` del primer hijo), `.page-header__subtitle` (72ch, `--text-secondary`, `text-wrap:pretty`) y `.page-header__subtitle--narrow` (62ch para Extractos). Modificadores de excepcion: `.page-header--plain` (Conciliacion, sin borde), `.page-header--static` (Papelera y Alertas, cabecera en bloque), `.page-header--stacked` (Importacion, cabecera en rejilla) y `.page-header__title--bold` (Alertas, el unico titulo que hoy queda a 700).
- Ficheros TSX tocados (21): `pages/DashboardPage.tsx`, `DashboardTitularPage.tsx`, `RevisionPage.tsx`, `TitularDetailPage.tsx`, `CuentaDetailPage.tsx`, `UsuariosPage.tsx`, `DispositivosMfaPage.tsx`, `ExtractosPage.tsx`, `ImportacionPage.tsx` (3 cabeceras), `ConfiguracionPage.tsx`, `AuditoriaPage.tsx`, `BackupsPage.tsx`, `ExportacionesPage.tsx`, `PapeleraPage.tsx`, `AlertasPage.tsx`, `ConciliacionPage.tsx`, `TitularesPage.tsx`, `CuentasPage.tsx`, `FormatosImportacionPage.tsx`, `configuracion/PaisesPage.tsx`, `IaPage.tsx`. Las clases propias de cada pantalla se conservan; las nuevas se anaden.
- CSS: `src/styles/layout/page.css` (nuevo), `src/styles/motion.css` (nuevo: animaciones, `@keyframes` y `prefers-reduced-motion`), `src/styles/layout.css` (orden de `@import`), `src/styles/global.css` y `src/styles/layout/{shell,users,extractos,entities,dashboard,importacion,admin}.css`. Desaparecido `src/styles/layout/system-coherence.css` (428 lineas): el bloque de 15 prefijos de pagina, el de 11 cabeceras y su media van a `.page`/`.page-header`; el resto se reparte por destino natural.
- Comandos: `npx tsc --noEmit` (0 errores), `npx eslint src tests --max-warnings 0` (0 errores), `npx tsc -p tsconfig.test.v2.json --outDir .test-dist-l8-d5` + `node --test --test-force-exit` (330/330), `npm run build` con `VITE_BUILD_OUT_DIR=dist-l8-d5` (correcto), `npx playwright test e2e/visual --output pw-l8-d5b` con `VISUAL_OUT=pw-l8-d5b-visual` (29 pasan, 1 skip).
- Resultado: comparador propio de triplets (media, selector, declaraciones) sobre todo `src/styles`: 2062 reglas identicas (puras mudanzas de fichero), 77 solo en el estado anterior y 14 solo en el nuevo, todas ellas renombres de selector documentados uno a uno. Segundo comprobante, resolutor de cascada por elemento (especificidad + orden) sobre 47 elementos representativos x 3 estados de media: 8 diferencias, todas "valor inicial declarado de forma explicita" (por ejemplo `display:block` en una cabecera que ya era bloque) y por tanto sin cambio de render.
- Supuestos: (1) el reparto del resto de la capa se hizo por destino natural (hoja de la pagina propietaria) en vez de a `global.css` cuando la regla ganaba por orden de fuente a la hoja de su pagina; (2) `.app-content th` pasa a `.app-content table th` para conservar la especificidad al bajar a `global.css`; (3) el titulo compartido declara 600 porque ese era el peso efectivo (ganaba `.app-content h1` al 700 del parche).
- Pendientes: la bitacora de este lote la mantiene el orquestador; el comparador de capturas pixel a pixel tiene ruido de marcas de tiempo, por lo que la lectura se ha hecho por zona de panel.
- Bloqueos: ninguno. Nada commiteado.
