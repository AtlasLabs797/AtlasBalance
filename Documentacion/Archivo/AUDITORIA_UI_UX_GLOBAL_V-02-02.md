# Informe global UI/UX premium - V-02-02

Fecha: 2026-06-09  
Version: `V-02-02`  
Producto: Atlas Balance.

## Veredicto

Atlas Balance ya tiene una base visual decente y bastante mas seria que una plantilla generica. Pero no esta todavia en nivel "cliente premium" de punta a punta. La parte fuerte es el shell/dashboard; la parte debil es la cobertura: no hay evidencia visual suficiente de extractos, importacion, revision, usuarios, configuracion, backups, auditoria y estados fallidos.

Puntuacion de salud UI/UX actual: **72/100**.

- 80+ seria client-ready con algunos retoques.
- 70-79 es buena base con deuda visible.
- Menos de 70 seria rediseño urgente.

La mejora global no debe ser "mas lujo". Debe ser mas confianza: jerarquia mas fuerte en dinero, menos ruido en chrome, estados duros probados y responsive real.

## Lo que esta bien

- Direccion visual clara en `Documentacion/Diseno/DESIGN.md`: tesoreria primero, numeros como protagonista, no circo SaaS.
- Stack correcto y sin dependencias de moda: React/Vite, CSS variables, lucide, Recharts, TanStack virtual.
- Paleta madura y semantica: azul financiero, neutros frios, estados suaves, dark/light mode.
- Tipografia propia con soporte para numeros: `National Park`, `Hind Madurai`, `Atlas Mono`, `tabular-nums`.
- Shell responsive existente: sidebar desktop, colapsado tablet, bottom nav y sheet mobile.
- Buenas bases de accesibilidad: skip link, muchos `aria-label`, focus tokens, reduced motion, tablas ocultas para charts en algunos casos.
- Estados comunes presentes: `EmptyState`, `PageSkeleton`, `AppErrorBoundary`, role guards, confirmaciones.
- Datos demo recientes para mirar la app con contenido realista.

## Problemas principales

### P0

No hay P0 visual confirmado en esta revision. Eso no significa que no existan; significa que la evidencia actual no permite declararlos.

### P1

1. **Cobertura visual incompleta.** Hay capturas recientes del shell/dashboard, pero no de todos los flujos donde se puede romper la confianza: extractos, importacion, revision, permisos, integraciones, backups, actualizador, auditoria y papelera.
2. **Jerarquia de dashboard aun demasiado card-driven.** Demasiadas superficies tienen peso parecido. En una app financiera, el dinero y el riesgo deben mandar mas que el contenedor.
3. **Mobile con competencia de overlays.** Bottom sheet, bottom nav y chat IA flotante pueden competir por la zona inferior. Eso es molesto en cualquier app; en una financiera es directamente mala ergonomia.
4. **Capa CSS correctiva fragil.** `system-coherence.css` arregla coherencia, pero tambien prueba que varios componentes no la tienen de origen.
5. **Estados duros no probados visualmente.** Long text, paths largos, IBANs largos, permisos revocados, API lenta, update bloqueado, backup fallido, IA no disponible y datos masivos siguen sin evidencia visual amplia.
6. **Scope global con fallo ambiguo.** El store de pais conserva `lastError`, pero el selector no lo comunica. Un fallo al cargar paises puede dejar al usuario sin saber si esta en `General`, un pais persistido o un scope degradado.

### P2

1. **Motion algo alto para uso repetido.** Hay card entrances, pulses y animaciones en varias zonas. Algunas ayudan; otras hacen ruido.
2. **Small-caps/tracking usado con demasiada libertad.** Los labels uppercase ayudan en metadata, pero en exceso bajan legibilidad.
3. **Charts con riesgo de inconsistencia.** Algunos graficos tienen tooltip propio y tabla accesible; `TitularSaldoBarChart` aun usa tooltip Recharts default.
4. **Admin y acciones destructivas necesitan mas jerarquia de riesgo.** El usuario debe distinguir rapido entre accion normal, reversible, irreversible y operacion de sistema.
5. **Docs de skills locales estaban desfasadas.** El repo movio skills a `Skills/02_Design-UI-UX/...` y el documento canonico seguia apuntando a `Skills/Diseno/...`.
6. **Puertas falsas de permisos.** La navegacion puede mostrar `Dashboard` a usuarios que luego son redirigidos a `/extractos`. Mejor ocultar o explicar, no hacer magia silenciosa.
7. **Importacion mezcla vacios.** La UX debe distinguir sin permiso, sin cuentas en el pais activo y sin formato de importacion.

### P3

- Sobra algo de sombra/hover en superficies operativas.
- Hay duplicacion de patrones entre CSS global, CSS de pantalla y `system-coherence.css`.
- El dashboard tablet usa mucho espacio vertical.
- Falta una matriz documentada de QA visual por ruta/rol/pais/estado.
- Hay estilos inline puntuales en formatos de importacion y nombres CSS historicos como `.auth-error` reutilizados fuera de auth.

## Conflictos entre skills y decision final

| Tension | Decision |
|---|---|
| `bolder` vs `quieter` | Ser bold solo en datos, riesgo, accion primaria y scope activo. Ser quieto en chrome, fondos, cards y motion. |
| `colorize` vs accesibilidad | No inventar paleta. Validar contraste dark/light y reforzar labels/leyendas antes de sumar color. |
| `adapt` vs tablas financieras | No convertir extractos en cards decorativas. Mantener experiencia tipo spreadsheet con scroll, sticky headers/acciones y lectura tactil. |
| `animate` vs power users | Animar overlays y feedback tactil. Reducir entradas/pulsos en vistas que se abren muchas veces al dia. |
| `distill` vs compliance/seguridad | Simplificar pantalla, no ocultar consecuencias. En permisos, backups y update, el detalle critico debe seguir visible. |
| `design-taste-frontend` vs producto financiero | Usarla como filtro anti-slop, no como receta de landing. Atlas Balance no necesita hero, mesh gradient ni teatralidad. |
| `redesign-existing-projects` vs `impeccable craft` | Rediseño incremental con ambicion visual controlada. Primero sistema, despues pantallas. |

## Propuesta global de mejora

### 0. Contexto y trazabilidad

Estado: hecho en esta sesion.

- Crear `PRODUCT.md` para registrar producto, usuarios, principios y anti-referencias.
- Documentar el uso real de skills y corregir la ruta local de diseño.

### 1. Gate de confianza visual

Objetivo: dejar de opinar a ciegas.

- Crear un harness finito de Playwright que use build/demo y cierre servidor en el mismo comando.
- Capturar desktop, tablet y mobile para rutas P0/P1:
  - `/dashboard`
  - `/dashboard/titular/:id`
  - `/dashboard/cuenta/:id`
  - `/extractos`
  - `/importacion`
  - `/revision`
  - `/ia`
  - `/usuarios`
  - `/configuracion`
  - `/backups`
  - `/auditoria`
  - `/papelera`
- Fallar por errores de consola, pantalla en blanco, overflow horizontal inesperado fuera de tablas, modal inaccesible o texto roto.

### 2. Consolidar sistema visual

Objetivo: que la coherencia viva en componentes, no en parches.

- Extraer patrones repetidos de card, panel, toolbar, modal, table action bar, badge y empty state.
- Reducir dependencia de `system-coherence.css` moviendo reglas a componentes o CSS de familia.
- Unificar hover/active/focus en controles comunes.
- Sustituir nombres historicos genericos (`.auth-error` fuera de auth) por componentes/estilos semanticos.
- Sacar estilos inline de pantallas de producto hacia CSS/tokens.
- Mantener CSS variables actuales; no meter Tailwind, shadcn, styled-components ni otra capa.

### 3. Rehacer jerarquia del dashboard

Objetivo: que el dashboard lea como cabina financiera premium, no como coleccion de tarjetas.

- KPI strip dominante para saldo total, disponible, inmovilizado, ingresos, egresos y delta.
- Charts como analisis secundario, con tooltips propios y leyendas claras.
- Menos cards anidadas; mas divisores y listas densas.
- Tablet: composicion de dos columnas utiles antes de apilar todo.
- Mobile: resumen compacto primero, charts con altura controlada y tablas/listas resumidas.

### 4. Endurecer extractos/importacion/revision

Objetivo: que los flujos donde se toca dinero aguanten datos reales.

- Extractos: sticky headers, toolbar persistente, seleccion visible, estados de permiso y columnas largas.
- Importacion: pasos claros, errores por fila, preview denso, confirmacion de impacto financiero.
- Revision: grupos por tipo de hallazgo, accion primaria clara, descartes/restauraciones con feedback reversible.
- Probar con muchos movimientos, nombres largos, divisas raras, conceptos extensos y permisos parciales.

### 5. Subir calidad de admin/riesgo

Objetivo: que acciones peligrosas se lean peligrosas sin gritar.

- Usuarios/permisos: matriz por pais/titular/cuenta con jerarquia visual de alcance.
- Integraciones: token creado, permisos, revocacion y eliminacion con copy directo.
- Backups/update: separar estado informativo, bloqueo accionable y accion irreversible.
- Configuracion IA/SMTP/sistema: agrupar por riesgo y frecuencia de uso.

### 6. Responsive serio

Objetivo: mobile/tablet usable, no solo "no se rompe".

- Definir patrones por tipo de modulo:
  - KPI strip compacto.
  - Tabla financiera con scroll controlado.
  - Formulario admin en secciones.
  - Modal destructivo con accion fija.
  - Sheet de navegacion sin competencia con chat IA.
- Revisar safe areas, touch targets, teclado virtual y overflow de labels.
- Mostrar errores de carga del selector de pais y resolver explicitamente scopes persistidos invalidos.
- Ocultar o explicar rutas no disponibles por permiso; evitar links que redirigen sin contexto.

### 7. Motion y polish final

Objetivo: tacto premium sin cansancio.

- Mantener `120-240ms` para controles/overlays.
- Eliminar o reducir pulses infinitos salvo alerta critica.
- Quitar animaciones de entrada en cards de vistas repetidas.
- Reemplazar tooltip default de charts por tooltip de sistema.
- Revisar dark mode y reduced motion al final, no como post-it.

## Matriz minima de QA visual

| Dimension | Casos |
|---|---|
| Viewport | 1440 desktop, 1024 tablet, 390 mobile |
| Tema | claro, oscuro |
| Rol | admin, usuario con permisos parciales, usuario sin acceso |
| Scope | General, pais con datos, pais sin datos, pais revocado |
| Datos | vacio, demo normal, datos masivos, textos largos |
| Estado | loading, error API, offline/parcial, permiso denegado |
| Riesgo | eliminar, restaurar, revocar, backup, update, importar |

## Orden recomendado de implementacion

1. Harness visual finito y matriz de evidencia.
2. Consolidacion de componentes/tokens para cards, modales, tablas, toolbars y badges.
3. Dashboard y charts.
4. Extractos/importacion/revision.
5. Usuarios/configuracion/backups/auditoria.
6. Responsive mobile/tablet.
7. Motion/polish final.

Este orden evita pisar mejoras: primero se crea el suelo, despues se mueven muebles. Hacerlo al reves genera CSS heroico y deuda nueva.

## Criterio de cierre

No considerar la UI "premium moderna" hasta que se cumplan estas condiciones:

- Capturas P0/P1 en desktop/tablet/mobile sin errores de consola.
- Dark/light revisados en pantallas criticas.
- Extractos/importacion/revision probados con datos largos y masivos.
- Acciones destructivas con copy, jerarquia y foco correctos.
- Motion reducida para vistas repetidas.
- `system-coherence.css` deja de ser el lugar donde se arregla todo.
