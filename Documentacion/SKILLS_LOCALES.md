# Skills y herramientas locales

Recursos de apoyo para agentes. Viven fuera del repo, en la maquina de desarrollo. Si una ruta no existe en la maquina actual, sigue sin ella: no hace falta para compilar ni para entregar.

Stack real de Atlas Balance: ASP.NET Core 10, React 19 + TypeScript + Vite 8, CSS variables propias y PostgreSQL 16+. Sin Tailwind, shadcn/ui, styled-components ni Next.js.

## 1. Donde estan y como buscarlos

| Recurso | Ruta | Contenido |
|---|---|---|
| Biblioteca de skills | `C:\Users\usuario\Documents\Skills` | Skills organizadas por categoria. Ruta canonica: `<categoria>\<slug>\SKILL.md`. |
| Indice | `Skills\INDEX.md` | Unas 294 entradas. Buscar aqui primero. |
| Algoritmo de uso | `Skills\README.md` | Como elegir y cargar una skill. |
| Herramientas de diseno | `C:\Users\usuario\Documents\Herramentas` | Cuatro paquetes (seccion 4). |

Notas sobre la biblioteca:

- `Skills\_ext` es extraccion temporal. Ignorarla.
- No hay copias por agente (`.agents`, `.claude`, `.cursor`...) en `Skills`. Si aparecen, usa solo la ruta canonica.
- Algunos slugs no coinciden con el `name` de la skill (por ejemplo `skill-docx` es `docx`). Busca en `INDEX.md`, no adivines.
- En `Herramentas` si hay copias por harness. Usa solo las rutas exactas de la seccion 4.
- Categorias usadas en este documento: `Desarrollo e Ingenieria`, `Diseno y Creatividad`, `Documentos y Productividad`, `Seguridad`.
- No hay skills dedicadas de C#/.NET, PostgreSQL ni documentacion tecnica. Ahi manda `AGENTS.md`.

## 2. Reglas

1. Carga solo el `SKILL.md` que necesites, y de sus `reference` solo lo que toque. No cargues paquetes enteros.
2. No ejecutes CLIs, instaladores, `npx`, launchers ni scripts de estas carpetas salvo peticion explicita del usuario. Algunos descargan y ejecutan binarios o paquetes remotos.
3. Adapta todo al stack real. No metas Tailwind, shadcn/ui, Next.js, StyleX ni otra dependencia porque una skill lo sugiera, salvo razon tecnica acordada.
4. Si una skill choca con `AGENTS.md`, manda `AGENTS.md`.
5. No pegues ni documentes passwords, tokens, secretos ni datos privados al usar una skill.
6. Antes de cerrar un cambio visual fuerte, deja las decisiones en `Documentacion\DOCUMENTACION_CAMBIOS.md`.

## 3. Necesidad -> recurso

Rutas relativas a `C:\Users\usuario\Documents\Skills`, salvo indicacion.

Si un verbo (audit, critique, polish, layout, typeset, harden, adapt, animate...) tiene playbook en `Impecable Design\...\impeccable\reference\`, usa ese; la skill de `Skills` es el respaldo.

| Necesidad | Recurso | Nota |
|---|---|---|
| Disenar una feature de UI antes de codificar | `Desarrollo e Ingenieria\shape\SKILL.md` | Produce un brief, no codigo. |
| Plan de implementacion | `Desarrollo e Ingenieria\writing-plans\SKILL.md` | Para cambios grandes o multi-archivo. |
| Frontend / UI nueva o rehecha | `Diseno y Creatividad\frontend-design\SKILL.md` | Combinar con las reglas de `Documentacion\Diseno`. |
| Decisiones amplias de UX/UI | `Diseno y Creatividad\ui-ux-pro-max\SKILL.md`, `ui-ux-designer\SKILL.md` | Inteligencia de diseno, no excusa para cambiar stack. |
| Mejorar una UI existente sin reescribirla | `Diseno y Creatividad\redesign-existing-projects\SKILL.md` | Diagnosticar primero, cambios pequenos. |
| Espaciado, jerarquia, composicion | `Desarrollo e Ingenieria\layout\SKILL.md` | Antes de tocar color o motion. |
| Tipografia y legibilidad | `Desarrollo e Ingenieria\typeset\SKILL.md` | Densidad adecuada para app financiera. |
| Color | `Diseno y Creatividad\colorize\SKILL.md` | Compatible con tema claro y oscuro. |
| Responsive, tablet, movil | `Desarrollo e Ingenieria\adapt\SKILL.md` | Revisar desktop, tablet y movil. |
| Animacion y microinteracciones | `Desarrollo e Ingenieria\animate\SKILL.md`; ver tambien Emil (seccion 4) | Motion con proposito. |
| Detalle final y consistencia | `Desarrollo e Ingenieria\polish\SKILL.md` | Antes de cerrar una entrega frontend. |
| Casos limite: vacios, errores, overflow | `Desarrollo e Ingenieria\harden\SKILL.md` | Probar datos extremos. |
| Accesibilidad (WCAG 2.2) | `Diseno y Creatividad\platform-design\SKILL.md`; `Desarrollo e Ingenieria\audit\SKILL.md` | `audit` entrega hallazgos priorizados. |
| Rendimiento de UI | `Desarrollo e Ingenieria\optimize\SKILL.md` | Medir antes y despues. |
| Opinion o critica UX | `Desarrollo e Ingenieria\critique\SKILL.md` | Diagnostico, no cambios. |
| Detalles de personalidad | `Desarrollo e Ingenieria\delight\SKILL.md` | Con mucha moderacion en una app de finanzas. |
| Seguridad | `Seguridad\cyber-neo\SKILL.md` | Usar cuando el cambio toca auth, permisos, tokens, integraciones, backups, CI/CD, secretos o superficie publica. No ejecutar herramientas externas sin necesidad clara. |
| Textos de UI, labels, errores | `Desarrollo e Ingenieria\clarify\SKILL.md` | Microcopy para usuarios no tecnicos. |
| Quitar tono de IA en textos y docs | `Documentos y Productividad\humanizalo\SKILL.md` | Para UI, combinar con `clarify`. |
| Depuracion | `Desarrollo e Ingenieria\systematic-debugging\SKILL.md` | Causa raiz antes de parchear. |
| Tests | `Desarrollo e Ingenieria\test-driven-development\SKILL.md` | Respetar los tests existentes del repo. |
| Simplificar codigo | `Desarrollo e Ingenieria\simplify-code\SKILL.md` | Tras terminar una funcionalidad. |
| Revision de codigo | `Desarrollo e Ingenieria\requesting-code-review\SKILL.md` | Antes de dar un cambio por cerrado. |
| Probar la app como usuario | `Desarrollo e Ingenieria\dogfood\SKILL.md` | QA exploratoria sobre la app en marcha. |

Combinaciones habituales:

- Pantalla nueva: `shape` -> implementacion -> `harden` -> `polish`.
- Rediseno: `redesign-existing-projects` -> `layout`/`typeset`/`colorize` segun el problema -> `polish`.
- Cambio sensible de seguridad: `cyber-neo` antes de implementar.

## 4. Herramientas de diseno

Rutas relativas a `C:\Users\usuario\Documents\Herramentas`. Usa solo la ruta indicada, no las copias por harness.

| Herramienta | Ruta | Que es y cuando usarla |
|---|---|---|
| Astryx | `Astryx\AGENTS.md` | Design system React 19 de Meta con StyleX. Solo referencia visual y de patrones: estados, popovers, dialogs, tablas, app shell. No instalar ni importar componentes. No ejecutar su CLI ni sus scripts. El pulido de V-03.02 la uso como referencia. |
| Emil Kowlaski Design | `Emil Kowlaski Design\.agents\skills\emil-design-eng\SKILL.md` | Pulido de interacciones y animacion en CSS/JS plano. Version canonica, mas reciente que la copia de `Skills`. Ignorar las skills de Swift/iOS y Sonner. |
| Impecable Design | `Impecable Design\.agents\skills\impeccable\SKILL.md` (v4.9.2) | Skill de diseno con playbooks en `...\impeccable\reference\*.md` (audit, critique, polish, animate, typeset, harden, adapt...). Es la version canonica frente a la copia de `Skills`. Espera `PRODUCT.md` (raiz del repo) y `Documentacion\Diseno\DESIGN.md`. El `.impeccable.md` de la raiz es un brief manual que la skill no lee. No ejecutar `scripts\impeccable`, `impeccable.cmd` ni su CLI: el launcher descarga un binario. Salta el paso 1 de la skill (`impeccable context`) y los verbos que llaman al launcher; lee a mano `PRODUCT.md`, `DESIGN.md` y el playbook de `reference\` que toque. Ignorar `ios.md` y `android.md`. |
| Modern Web Guidance | `Modern Web Guidance\repo\skills\modern-web-guidance\SKILL.md` y guias en `...\modern-web-guidance\guides\` | Patrones web nativos: dialog, popover, anchor positioning, view transitions, forms, accesibilidad, modo oscuro, rendimiento. Leer las guias markdown directamente. No ejecutar `npx modern-web-guidance`: descarga y ejecuta un paquete remoto. Ignorar `chrome-extensions`. |

Cuando usar cada una:

- Duda de patron visual (dialog, popover, tabla, estados): Astryx, solo como referencia.
- Pulido de transiciones, hover, foco, sensacion de componente: Emil.
- Auditoria, critica o pulido estructurado de una pantalla: Impecable (playbook correspondiente en `reference`).
- Implementar algo con APIs nativas del navegador en vez de librerias: Modern Web Guidance.
