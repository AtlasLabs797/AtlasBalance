# Skills locales

## Regla base

La carpeta `Skills` contiene skills locales para mejorar Atlas Balance. Hay repos completos y muchas copias repetidas para distintos agentes (`.agents`, `.codex`, `.claude`, `.cursor`, etc.). No trates cada copia como una skill distinta. Usa las rutas canonicas de este documento.

Antes de aplicar una skill:

1. Lee solo el `SKILL.md`, `CLAUDE.md` o `README.md` canonico de esa skill.
2. Si el archivo referencia carpetas `references`, `knowledge`, `templates` o `scripts`, carga solo lo necesario para la tarea.
3. Adapta cualquier consejo al stack real de Atlas Balance: React 18, TypeScript, Vite, ASP.NET Core 8, PostgreSQL y CSS variables propias.
4. No introduzcas Tailwind, styled-components, shadcn/ui, Next.js, Clerk, Supabase, Stripe ni otra dependencia porque una skill lo sugiera. Este proyecto ya tiene stack decidido.
5. No ejecutes instaladores, actualizadores, CLIs o scripts incluidos en `Skills` salvo que el usuario lo pida o sea imprescindible para la tarea.
6. No documentes ni pegues passwords, tokens, secretos o datos privados al usar ninguna skill.

## Construccion

| Skill | Ruta canonica | Usar cuando | Como usarla |
|---|---|---|---|
| The Architect | `Skills/Construcion/the-architect-main/CLAUDE.md` | Hay que disenar una feature grande, modulo nuevo, arquitectura completa, integracion compleja o blueprint antes de codificar. | Lee `CLAUDE.md`, luego las preguntas y knowledge que correspondan. Produce un blueprint; no escribas codigo con esta skill. |

## Diseno: catalogo y criterio general

| Skill | Ruta canonica | Usar cuando | Como usarla |
|---|---|---|---|
| Catalogo de diseno | `Skills/Diseno/design.md` | Hay duda sobre que skill de diseno elegir. | Usalo como indice rapido. Luego carga la skill concreta, no todo el paquete. |
| emil-design-eng | `Skills/Diseno/emilkowalski-skill/skills/emil-design-eng/SKILL.md` | Hay que mejorar microinteracciones, transiciones, tacto de componentes, estados o detalles invisibles de calidad. | Aplica su filosofia de detalles compuestos. Ideal para componentes concretos, no para redisenar toda la app. |

## Diseno: Impeccable

Ruta base canonica: `Skills/Diseno/impeccable/source/skills`.

Estas skills comparten una regla: si no hay contexto de diseno claro, primero usa `impeccable` en modo `teach`. Para Atlas Balance, respeta siempre CSS variables y no metas Tailwind.

| Skill | Usar cuando | Como usarla |
|---|---|---|
| impeccable | Crear o rehacer una interfaz de alta calidad, definir contexto de diseno o extraer tokens/componentes. | Carga `impeccable/SKILL.md`. Usa `teach` para contexto, `craft` para disenar e implementar, `extract` para tokens/componentes. |
| adapt | Hay problemas de responsive, tablet, mobile, breakpoints, touch targets o viewport. | Carga `adapt/SKILL.md`; revisa desktop/tablet/mobile y ajusta layout sin romper rutas existentes. |
| animate | El usuario pide animaciones, transiciones, hover, microinteracciones o UI mas viva. | Carga `animate/SKILL.md`; usa motion con proposito y valida rendimiento. |
| audit | El usuario pide auditoria UI, accesibilidad, rendimiento, responsive, theming o anti-patrones. | Carga `audit/SKILL.md`; entrega hallazgos P0-P3 antes de tocar codigo salvo que pidan arreglar. |
| bolder | La interfaz se ve sosa, generica, plana o sin personalidad. | Carga `bolder/SKILL.md`; sube impacto visual sin sacrificar legibilidad ni uso profesional. |
| clarify | Hay labels, errores, ayuda, microcopy o instrucciones confusas. | Carga `clarify/SKILL.md`; reescribe texto de UI para usuarios no tecnicos. |
| colorize | La UI se ve gris, fria, monocroma o sin jerarquia cromatica. | Carga `colorize/SKILL.md`; usa color estrategico compatible con dark/light mode. |
| critique | El usuario pide opinion, feedback o evaluacion UX de una pantalla/componente. | Carga `critique/SKILL.md`; da diagnostico con criterios UX y acciones. |
| delight | Se busca personalidad, pequenos detalles memorables o acabado emocional. | Carga `delight/SKILL.md`; aplica solo donde no distraiga del trabajo financiero. |
| distill | Hay que simplificar, quitar ruido, reducir pasos o limpiar una pantalla saturada. | Carga `distill/SKILL.md`; elimina complejidad antes de anadir elementos nuevos. |
| harden | Hay que hacer UI production-ready: empty states, errores, overflow, i18n, textos largos o edge cases. | Carga `harden/SKILL.md`; prueba datos extremos y estados reales. |
| layout | El problema es espaciado, alineacion, jerarquia, composicion o grillas monotonas. | Carga `layout/SKILL.md`; corrige estructura visual antes de color o motion. |
| optimize | La UI va lenta, pesada, con jank, bundle grande, imagenes pesadas o animaciones caras. | Carga `optimize/SKILL.md`; mide antes de tocar y valida despues. |
| overdrive | El usuario pide algo extraordinario, wow, tecnico, cinematico o muy ambicioso. | Carga `overdrive/SKILL.md`; usalo con moderacion en Atlas Balance. Finanzas internas no necesitan circo. |
| polish | Antes de cerrar una pantalla o entrega frontend. | Carga `polish/SKILL.md`; revisa alineacion, estados, consistencia, spacing, foco y detalles pequenos. |
| quieter | La UI quedo demasiado agresiva, chillona, pesada o visualmente intensa. | Carga `quieter/SKILL.md`; baja volumen sin volverla aburrida. |
| shape | Antes de codificar una feature UI compleja. | Carga `shape/SKILL.md`; produce brief UX/UI, no codigo. |
| typeset | Hay problemas de fuente, jerarquia, lectura, escala o textos que no encajan. | Carga `typeset/SKILL.md`; mejora legibilidad y densidad para app financiera. |

## Diseno: Taste Skill

Ruta base canonica: `Skills/Diseno/taste-skill/.agents/skills`.

| Skill | Usar cuando | Como usarla |
|---|---|---|
| design-taste-frontend | Hay que imponer una calidad UI fuerte en React/frontend y evitar sesgos visuales genericos. | Carga `design-taste-frontend/SKILL.md`; combina con las reglas propias de Atlas Balance. |
| high-end-visual-design | Landing, pagina publica, pantalla premium o redisenos visuales con nivel agencia. | Carga `high-end-visual-design/SKILL.md`; no copies sus bans literalmente si chocan con assets reales. |
| minimalist-ui | Se busca interfaz editorial, limpia, sobria, con baja decoracion y alta claridad. | Carga `minimalist-ui/SKILL.md`; buena opcion para pantallas financieras densas. |
| industrial-brutalist-ui | Dashboards densos, auditoria, telemetria, estados tecnicos o visual estilo operativo. | Carga `industrial-brutalist-ui/SKILL.md`; usar solo si encaja con la marca Atlas Balance. |
| redesign-existing-projects | Hay que mejorar una UI existente sin reescribir funcionalidad. | Carga `redesign-existing-projects/SKILL.md`; diagnostica primero, luego aplica cambios pequenos y seguros. |
| stitch-design-taste | Hay que generar o actualizar un `DESIGN.md` para reglas de diseno semanticas o herramientas tipo Stitch. | Carga `stitch-design-taste/SKILL.md`; salida principal: documento de reglas, no app code. |
| full-output-enforcement | El usuario pide salida completa, archivos largos, listas exhaustivas o cero truncamiento. | Carga `full-output-enforcement/SKILL.md`; no lo uses por defecto, porque aqui la brevedad sigue mandando. |
| gpt-taste | Se pide direccion visual muy creativa, motion avanzado o pagina tipo Awwwards. | Carga `gpt-taste/SKILL.md`; no es la opcion normal para pantallas internas de tesoreria. |

## Diseno: UI/UX Pro Max y CKM

Ruta base canonica: `Skills/Diseno/ui-ux-pro-max-skill/.agents/skills`.

| Skill | Usar cuando | Como usarla |
|---|---|---|
| ui-ux-pro-max | Hay decisiones amplias de UX/UI: estructura, patrones, accesibilidad, color, tipografia, charts o responsive. | Carga `ui-ux-pro-max/SKILL.md`; usalo como inteligencia de diseno, no como excusa para cambiar stack. |
| ckm-design | Se pide trabajo integral de marca, tokens, UI, logos, slides, banners, iconos o social assets. | Carga `ckm-design/SKILL.md`; usar solo cuando el alcance sea de identidad/creativos completos. |
| ckm-ui-styling | Se necesita estilado UI accesible, componentes, theming, dark mode o consistencia visual. | Carga `ckm-ui-styling/SKILL.md`; adapta las recomendaciones a CSS variables, no Tailwind/shadcn. |
| ckm-design-system | Hay que crear tokens, especificacion de componentes, estados o sistema visual. | Carga `ckm-design-system/SKILL.md`; sincroniza resultado con `Documentacion/Diseno` si aplica. |
| ckm-brand | Se trabaja tono de marca, mensajes, identidad, guias o consistencia de activos. | Carga `ckm-brand/SKILL.md`; util para textos publicos y material de release, menos para CRUD interno. |
| ckm-banner-design | Se piden banners, headers, ads, covers o piezas visuales para redes/marketing. | Carga `ckm-banner-design/SKILL.md`; no usar para pantallas de app. |
| ckm-slides | Se piden presentaciones, pitch decks o slides HTML con graficas. | Carga `ckm-slides/SKILL.md`; salida principal: presentacion, no UI de producto. |

## Escritura

| Skill | Ruta canonica | Usar cuando | Como usarla |
|---|---|---|---|
| humanizalo | `Skills/Escritura/humanizalo-main/SKILL.md` | Hay que humanizar textos, documentacion, mensajes de error, onboarding, emails o contenido que suena a IA. | Carga `SKILL.md`; reescribe, audita tono y entrega version final clara. Para UI, combinar con `clarify`. |

## Seguridad

| Skill | Ruta canonica | Usar cuando | Como usarla |
|---|---|---|---|
| cyber-neo | `Skills/Seguridad/cyber-neo-main/skills/cyber-neo/SKILL.md` | El usuario pide auditoria de seguridad, vulnerabilidades, secretos, auth, permisos, criptografia, CI/CD, supply chain o pentest ligero. | Carga `SKILL.md`; revisa OWASP/CWE, secretos, dependencias, authz/authn y genera reporte priorizado. No ejecutes herramientas externas si no estan instaladas o si el coste/riesgo no esta claro. |

## Orden recomendado por tipo de tarea

- Nueva feature grande: `The Architect` -> `shape` si hay UI -> implementacion normal -> `harden` -> `polish`.
- Redisenar pantalla existente: `redesign-existing-projects` o `impeccable craft` -> `layout/typeset/colorize` segun problema -> `polish`.
- Mejorar responsive: `adapt` -> prueba desktop/tablet/mobile.
- Revisar calidad visual: `critique` o `audit`; `critique` para UX, `audit` para calidad tecnica medible.
- Seguridad: `cyber-neo` antes de cambios sensibles de auth, permisos, tokens, integraciones, backups o CI.
- Textos/documentacion: `humanizalo`; si es UI, combinar con `clarify`.
- Antes de cerrar frontend: minimo `harden` y `polish`; si hubo cambios visuales fuertes, documentar decisiones en `Documentacion/DOCUMENTACION_CAMBIOS.md`.
