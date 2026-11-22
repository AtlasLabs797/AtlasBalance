# ATLAS BALANCE - Instrucciones del proyecto

Fuente canonica de instrucciones para cualquier agente (Claude Code, Codex, OpenCode, Cursor). `CLAUDE.md` solo la importa. No crees copias en subcarpetas: acaban divergiendo. Las copias dentro de `.artifacts/` (ignorada por Git) no cuentan.

Los numeros de seccion se citan desde otros documentos: no los cambies.

---

## 1. Tono

- Se mi sparring partner: busca puntos debiles, puntos ciegos y la verdad tecnica, no mi aprobacion. Si una idea es mala, dilo y explica por que.
- Da una recomendacion clara en vez de esconderte en "depende". Si no estas seguro, dilo y verifica (busqueda web con fuentes cuando haga falta).
- Responde directamente, sin preambulos ni relleno. Si cabe en una frase, una frase.
- Humor o algun taco, solo si salen naturales.

---

## 2. Como trabajar

Para tareas triviales (typo, one-liner obvio) usa criterio y actua sin ceremonia.

### 2.1 Pensar antes de codificar

- Si hay una interpretacion razonable, actua con ella y declara el supuesto en una linea al entregar.
- Pregunta antes de implementar solo si hay interpretaciones con resultados muy distintos, o si el paso es destructivo o dificil de revertir.
- Si existe un camino mas simple, dilo antes de implementar el complejo.

### 2.2 Simplicidad primero

El minimo codigo que resuelve el problema. Sin funciones no pedidas, abstracciones para un solo uso, configurabilidad que nadie pidio ni manejo de errores para escenarios imposibles. Si escribes 200 lineas y podian ser 50, reescribelo. Refactoriza cuando la complejidad exista de verdad, no antes: las abstracciones prematuras complican, meten bugs y cuestan de testear.

### 2.3 Cambios quirurgicos

- Toca solo lo que pide la tarea: no "mejores" codigo, comentarios ni formato adyacente, y respeta el estilo existente.
- Si ves codigo muerto ajeno, mencionalo; no lo borres salvo que se pida. Elimina solo lo que tus cambios dejaron huerfano.
- Cada linea cambiada debe poder trazarse a lo que pidio el usuario.

### 2.4 Ejecucion por objetivos

Convierte tareas en objetivos verificables: "arregla el bug" pasa a "test que lo reproduce, hacerlo pasar, sin regresiones"; "refactoriza X" pasa a "los tests pasan antes y despues".

Para tareas de 3 o mas pasos, o que toquen varios modulos, deja antes un plan breve con una linea por paso y su verificacion. Para el resto, actua directamente.

---

## 3. Que es este proyecto

Atlas Balance, de Atlas Labs: aplicacion web on-premise de tesoreria multi-banco, multi-titular y multi-divisa. Corre en Windows Server y la usan 4-8 usuarios en red local via navegador.

**Stack**
- Backend: ASP.NET Core 10 (C#) como Windows Service, HTTPS con Kestrel. EF Core 10 + Npgsql.
- Frontend: React 19 + TypeScript + Vite 8, servido como estaticos por el backend.
- BD: PostgreSQL 16+ (Docker en desarrollo, local en produccion).
- Librerias: react-router 8 (importa de `react-router`; `react-router-dom` ya no existe), Zustand 5, TanStack Query 5, React Hook Form, Axios, Recharts 3, `@tanstack/react-virtual` (tablas de 50k+ filas), Hangfire (storage PostgreSQL), MailKit, ClosedXML.
- CSS: variables propias con dark/light mode. Sin Tailwind ni styled-components.

---

## 4. Antes de tocar codigo

1. Lee `Documentacion/Versiones/version_actual.md` y el archivo `v*` de la version en curso, y sigue sus instrucciones.
2. Si vas a resolver un error, busca antes en `Documentacion/LOG_ERRORES_INCIDENCIAS.md`: puede estar resuelto y su solucion se reutiliza.
3. Si vas a usar una skill o herramienta externa, mira la seccion 7.
4. Asocia el cambio a una version concreta.

---

## 5. Versiones

- La version runtime vive en tres sitios que deben ir alineados: `Atlas Balance/VERSION`, `Atlas Balance/Directory.Build.props` y `Atlas Balance/frontend/package.json` (lo comprueba `Check-VersionAlignment.ps1`).
- Formato canonico `V-MM.NN` con punto (ej: `V-03.02`). Los archivos antiguos `v-02-0x` con guion son historicos; no los renombres.
- Los registros de version estan en `Documentacion/Versiones`. Cada modificacion se documenta bajo su version.

---

## 6. Documentacion

Toda la documentacion vive en `Documentacion`. Cada cambio actualiza los documentos afectados antes de cerrar la tarea:

- `DOCUMENTACION_TECNICA.md`: que se modifico, por que y como.
- `DOCUMENTACION_USUARIO.md`: lenguaje simple; solo cuando el cambio afecta al usuario.
- `LOG_ERRORES_INCIDENCIAS.md`: cada error encontrado, su causa y la solucion.
- `REGISTRO_BUGS.md`: bugs pendientes. Al resolverse se marcan cerrados y la solucion pasa al log de errores.
- No pongas contrasenas, tokens ni datos privados en ningun documento: el repo y sus docs se comparten.

### Bitacora de cambios

- Toda sesion que cambie codigo, configuracion, documentacion de proyecto o comportamiento anade una entrada al principio de `Documentacion/DOCUMENTACION_CAMBIOS.md`. Typos y cambios de formato sin efecto no la necesitan.
- Cada entrada: fecha, version, trabajo realizado, archivos tocados, verificacion ejecutada con su resultado y pendientes. En frontend, anade las decisiones visuales y los pendientes de diseno.

---

## 7. Skills y herramientas externas

Viven fuera del repo, en esta maquina. Si una ruta no existe (otro equipo, CI), sigue sin ella y no la busques por el disco. El catalogo detallado por necesidad esta en `Documentacion/SKILLS_LOCALES.md`.

- **Skills:** `C:\Users\usuario\Documents\Skills`, con ruta `<categoria>\<slug>\SKILL.md`. Busca en `INDEX.md` o sigue `README.md`. Ignora `_ext`. `impeccable` y `emil-design-eng` salen en `INDEX.md`, pero se cargan desde Herramentas: las copias de `Skills` estan desfasadas.
- **Herramientas de diseno:** `C:\Users\usuario\Documents\Herramentas`. Tienen copias por agente (`.agents`, `.claude`, `.cursor`...): usa solo la ruta de la tabla.

| Herramienta | Entrada | Para que |
|-------------|---------|----------|
| Impecable Design | `Impecable Design\.agents\skills\impeccable\SKILL.md` + `reference\*.md` | Auditar, criticar y pulir UI (audit, critique, polish, typeset, layout, harden, adapt, animate). Salta su paso 1 (`impeccable context`) y los verbos que llaman al launcher; lee a mano `PRODUCT.md`, `Documentacion/Diseno/DESIGN.md` y el playbook que toque |
| Emil Kowlaski Design | `Emil Kowlaski Design\.agents\skills\emil-design-eng\SKILL.md` | Detalle de interacciones y animacion: hover/press, popovers, tooltips, timing, transiciones. Ignora las skills Swift/iOS y Sonner |
| Modern Web Guidance | `Modern Web Guidance\repo\skills\modern-web-guidance\SKILL.md` + `guides\` | Patrones web nativos antes de escribir dialogs, popovers, anchor positioning, view transitions, formularios, a11y, dark mode o rendimiento. Lee las guias markdown directamente |
| Astryx | `Astryx\AGENTS.md` | Referencia visual y de patrones (estados, overlays, tablas, app shell). No instales ni importes sus componentes: usa StyleX, ajeno al stack |

**Skills mas utiles** (bajo `C:\Users\usuario\Documents\Skills`):
- Seguridad: `Seguridad\cyber-neo` cuando el cambio toque auth, permisos, tokens, integraciones, backups, CI/CD, secretos o superficie publica.
- Textos: `Documentos y Productividad\humanizalo` (tono artificial) y `Desarrollo e Ingenieria\clarify` (microcopy, labels, errores de UI).
- Frontend: `Diseno y Creatividad\frontend-design` y `ui-ux-pro-max`; `Desarrollo e Ingenieria\audit` (accesibilidad y calidad) y `optimize` (rendimiento).
- Ingenieria: `Desarrollo e Ingenieria\systematic-debugging`, `test-driven-development` y `requesting-code-review`.
- No hay skills de C#/.NET ni PostgreSQL: usa este archivo y `Documentacion/`.

**Reglas**
- Carga solo el `SKILL.md` que vayas a usar.
- No ejecutes CLIs, instaladores, launchers ni `npx` de estas carpetas salvo que el usuario lo pida: descargan y ejecutan codigo remoto (seccion 9). Eso incluye `npx modern-web-guidance`, `scripts\impeccable` / `impeccable.cmd` y la CLI de Astryx.
- Adapta toda recomendacion al stack real. Si una skill sugiere Tailwind, shadcn, Next.js, StyleX u otra dependencia ajena, no la metas sin una razon tecnica clara.

---

## 8. Protocolo anti-encallamiento

Los atascos de este proyecto se repiten: Vite/Rolldown/Chromium con `spawn EPERM`, servidores temporales que no se cierran, `robocopy /MIR` o copias sobre `wwwroot` bloqueado, procesos `dotnet` vivos bloqueando `apphost.exe`, Docker/Testcontainers no disponible y limpiezas recursivas con `Access denied`. Aplica la solucion conocida en vez de investigarlos de nuevo.

**Reglas generales**
- Lanza tests, docker, migraciones y builds con timeout o en modo batch no interactivo, y define antes el criterio de exito y una alternativa estatica si falla. Un comando colgado bloquea la sesion entera.
- Maximo dos intentos por la misma via. Si el error se repite, cambia de estrategia, pide elevacion una vez si procede, o documenta el bloqueo.
- Si una limpieza o verificacion emite errores de permisos repetidos o salida masiva, cortala y pasa a una comprobacion acotada.
- Limpiezas temporales solo sobre rutas absolutas verificadas dentro del workspace.
- En la respuesta final separa **verificado**, **bloqueado** y **pendiente**. Lint o build no son validacion visual.

**Casos concretos**
- No arranques servidores dev (Node/Vite/HTTP) de larga duracion desde la shell para "mirar rapido". `Start-Dev.ps1` y `npm run dev` (seccion 13) son para el usuario o cuando el lo pida. Para UI usa build finito, Playwright `setContent`, mocks, o un comando que arranque y cierre el proceso dentro del mismo timeout.
- Vite/Rolldown/Chromium con `spawn EPERM` dentro del sandbox: no reintentes ahi. Usa `npm.cmd run lint` o build de TypeScript fuera del sandbox con aprobacion si es imprescindible; si no, registra el bloqueo.
- Reinicio de backend/API: no lances `dotnet` con `Start-Process` ni `[Diagnostics.Process]` si puede heredar stdout/stderr y colgar la herramienta. Usa script finito con logs redirigidos, healthcheck con timeout y salida obligatoria; o valida por tests/build si el reinicio no es imprescindible.
- `dotnet` fallando por `apphost.exe` bloqueado: prueba primero `-p:UseAppHost=false`. No mates procesos a ciegas; identifica el PID exacto.
- Sincronizar `frontend/dist` con `wwwroot`: evita `robocopy /MIR`. Copia selectiva con timeout y pocos reintentos; si hay `Access denied`, pide elevacion una vez o registra el bloqueo.
- Docker/Testcontainers no disponible: ejecuta la suite filtrada sin Docker y deja el release bloqueado por los tests Docker pendientes. No lo presentes como verde.

---

## 9. Higiene antimalware/antivirus

El equipo tiene antimalware y antivirus activos, y lo que parezca malware se bloquea o dispara alertas. Evita evasion, desactivar defensas, ofuscacion, ejecucion desde `%TEMP%`, descargas de codigo remoto, AMSI bypass, encoded commands, persistencia oculta, inyeccion en procesos, scraping de credenciales, binarios sin verificar, exclusiones antivirus como solucion por defecto, procesos en segundo plano sin salida clara, barridos fuera del workspace y limpiezas agresivas.

- Prefiere comandos finitos, rutas explicitas, logs visibles y artefactos dentro del workspace. El `-ExecutionPolicy Bypass` de la seccion 13 es aceptable: afecta solo a ese proceso y a scripts del repo con ruta explicita.
- Si una tarea legitima puede parecer sospechosa (build de ejecutables, empaquetado, firma, escaneo recursivo, copia masiva), explica el motivo, acota rutas, usa timeout y deja evidencia en la documentacion.
- Si el antivirus bloquea algo, tratalo como senal operativa: corta, documenta el bloqueo y busca una via mas transparente.

---

## 10. Reglas de desarrollo

### Backend (C#)

- Serializacion con `System.Text.Json`. `Newtonsoft.Json` esta en `AtlasBalance.API.csproj` solo como pin de seguridad: Hangfire.Core arrastra la 11.0.1 vulnerable y el pin fuerza la 13.0.4. No lo uses en codigo ni quites el pin mientras Hangfire dependa de una version vulnerable.
- EF Core con migrations. UUIDs para todas las PKs.
- Soft delete: ver seccion 12.
- Endpoints paginados devuelven `{ data, total, page, pageSize, totalPages }`; ordenacion con `?sortBy=campo&sortDir=asc|desc`.
- Passwords con bcrypt, 12 salt rounds.
- JWT en cookies httpOnly (access 1h, refresh 7 dias). CSRF segun seccion 12.
- Integracion OpenClaw con Bearer token propio, hasheado SHA-256 en BD.
- Rate limiting: login 5 intentos y bloqueo 30 min; integracion 100 req/min.
- Permisos verificados en backend en cada request: el frontend no es frontera de seguridad.
- IP en auditoria. Logs con Serilog, sin tokens, passwords ni datos sensibles.
- Dashboard de Hangfire deshabilitado en produccion.

### Frontend (React/TypeScript)

- Functional components + hooks; un componente por archivo.
- Zustand para estado global, React Hook Form para formularios, Axios con interceptor de refresh automatico de JWT.
- Estilos con CSS variables; dark/light via variables y toggle en TopBar.
- Responsivo: desktop / tablet (sidebar colapsa) / mobile (bottom nav).
- Feedback con toasts, no con alerts nativos. Skeleton loaders al cargar, empty states sin datos, error boundaries en cada seccion principal.

### Base de datos

- PostgreSQL 16+. Los enums C# se guardan como `integer` (ordinal); los tipos enum de PostgreSQL del snapshot no los usa ninguna columna. No reordenes ni insertes valores en medio de un enum: cambiaria el significado de los datos guardados.
- Indices explicitos en todas las FKs y campos de busqueda frecuente; UNIQUE donde corresponda.
- Nombrado: seccion 14.

### Testing

- Backend: xUnit + FluentAssertions para servicios criticos (`Atlas Balance/backend/tests/AtlasBalance.API.Tests`).
- Frontend: tipos TypeScript estrictos como primer nivel; `npm run lint` y `npm run test:unit` como red minima.
- Tests manuales al final de cada bloque de trabajo antes de avanzar. La seccion 8 aplica tambien aqui.

---

## 11. Rutas clave

- `Atlas Balance/backend/src/AtlasBalance.API/` (API: `Controllers`, `Services`, `Models`, `Data`, `Migrations`, `Middleware`, `Jobs`) y `AtlasBalance.Watchdog/`.
- `Atlas Balance/backend/tests/` (`AtlasBalance.API.Tests`, `AtlasBalance.Caching.Tests`).
- `Atlas Balance/frontend/src/` (`components`, `pages`, `hooks`, `services`, `stores`, `styles`, `types`, `utils`).
- `Atlas Balance/scripts/` (dev, release, alineacion de versiones).
- `Documentacion/` (versiones, SPEC, documentacion, bitacora) y `Documentacion/Diseno/` (`DESIGN.md`, design system, mockups).
- Raiz: `PRODUCT.md`, `.impeccable.md` y `design-qa.md` (contexto de diseno).
- Fuera de Git: `Atlas Balance/Atlas Balance Release/` (paquetes), `.artifacts/` y, si existe en el checkout, `Otros/`.

---

## 12. Esquema de BD

La fuente de verdad es el codigo: `Models/Entities.cs`, `Data/AppDbContext.cs` y `Migrations/`. `Documentacion/SPEC.md` seccion 5 documenta todas las tablas (actualizada contra el snapshot en V-03.02). Si chocan, manda el codigo. Si cambias el esquema, actualiza tambien SPEC.

El snapshot de EF tiene deriva conocida frente al modelo (M-9 en `Documentacion/REGISTRO_BUGS.md`, fijada por `SnapshotModelDriftTests`). Por eso `dotnet ef migrations add` genera migraciones sucias con cambios ajenos: escribe las migraciones a mano, como las de V-03.02, y no quites el `Ignore` de `PendingModelChangesWarning` en `AppDbContext` hasta cerrar M-9.

Lo que SPEC no recoge o recoge mal:

1. **Soft delete:** solo las entidades que implementan `ISoftDelete` tienen `deleted_at` + `deleted_by_id`. `AppDbContext.ApplySoftDeleteQueryFilters` les aplica un filtro global (`HasQueryFilter`), asi que no anadas `WHERE deleted_at IS NULL` a mano; usa `IgnoreQueryFilters()` solo con motivo (papelera, restauracion). Otras tablas no tienen soft delete a proposito: auditoria (append-only), tokens, permisos, configuracion, tipos de cambio, notificaciones admin...
2. **Alcance de permisos (`PERMISOS_USUARIO`):** `pais_id`, `titular_id` y `cuenta_id` son nullables; null en una dimension significa "todas" en esa dimension. El acceso global solo existe con las tres a null y `puede_ver_cuentas` (`UserAccessService`). Ademas de `puede_eliminar_lineas` y `puede_importar` hay `puede_revisar_lineas`, `puede_aprobar_importaciones`, `puede_conciliar` y `puede_cerrar_conciliacion`.
3. **`NOTIFICACIONES_ADMIN`:** globales, sin `usuario_id` y sin soft delete. `leida` es compartida: si un admin la lee, queda leida para todos.
4. **CSRF:** login, `/api/auth/refresh-token` y la verificacion MFA devuelven el token en el body y en la cookie `csrf_token` (no httpOnly). El frontend lo envia en el header `X-CSRF-Token` en cada mutacion.
5. **Watchdog:** la API se autentica con un shared secret en el header `X-Watchdog-Secret`; solo el health check queda exento.

---

## 13. Comandos frecuentes

```bash
# Entorno completo de desarrollo (mata procesos viejos; larga duracion, ver seccion 8)
cd "Atlas Balance"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Start-Dev.ps1"

# PostgreSQL por separado (puerto 5433)
cd "Atlas Balance"
docker compose up -d

# Backend
cd "Atlas Balance/backend/src/AtlasBalance.API"
dotnet run
dotnet ef database update
# Migraciones nuevas: a mano, no con "dotnet ef migrations add" (ver seccion 12)

# Backend - tests
cd "Atlas Balance/backend"
dotnet test

# Frontend
cd "Atlas Balance/frontend"
npm run dev        # larga duracion, ver seccion 8
npm run lint
npm run test:unit
npm run build

# Alineacion de versiones
cd "Atlas Balance"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Check-VersionAlignment.ps1"

# Release Windows x64
cd "Atlas Balance"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-03.02

# Conectar a PostgreSQL (password del entorno local; no la documentes)
psql -h localhost -p 5433 -U app_user -d atlas_balance
```

---

## 14. Convenciones de nombrado

| Elemento | Convencion | Ejemplo |
|----------|------------|---------|
| Tabla BD | MAYUSCULAS_SNAKE | `EXTRACTOS` |
| Columna BD | minusculas_snake | `fila_numero` |
| Clase / propiedad C# | PascalCase | `AlertaService`, `FilaNumero` |
| Endpoint API | kebab-case | `/api/tipos-cambio` |
| JSON de la API | snake_case | `codigo_iso2` |
| Componente React | PascalCase | `EditableCell.tsx` |
| Hook React | camelCase con `use` | `useDebouncedValue.ts` |
| Store Zustand | camelCase con `Store` | `authStore.ts` |
| CSS Variable | kebab-case con `--` | `--color-primary` |
| Archivo TS/TSX | PascalCase (componente) / camelCase (util) | `LoginPage.tsx` / `chartColors.ts` |

---

## 15. GitHub

- Repositorio: https://github.com/AtlasLabs797/AtlasBalance
- Cuando se pida subir a GitHub, crea una rama con el nombre de la version actual (ej: `V-03.02`) y haz push a esa rama.
- Lo que ignora `.gitignore` queda fuera (`Otros/`, dependencias, `bin/obj`, `dist`, `.env`, logs). Los paquetes de `Atlas Balance Release` se publican como assets de GitHub Releases, no en Git.
- No subas secretos, tokens, passwords, certificados privados, dumps de base de datos ni datos reales.
