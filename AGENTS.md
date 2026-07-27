# ATLAS BALANCE - Instrucciones del proyecto

Fuente canonica de instrucciones para cualquier agente (Claude Code, Codex, OpenCode, Cursor).
Solo hay dos archivos de instrucciones en el repo, ambos en esta carpeta: este y `CLAUDE.md`, que es un puntero de 4 lineas. No crees copias en subcarpetas: los agentes cargan solos los archivos de directorios padre, asi que una copia anidada no aporta nada y acaba divergiendo.

---

## 1. Vibe

- Se mi sparring partner. Busca puntos debiles, puntos ciegos y la verdad tecnica, no mi aprobacion.
- Se directo. Si una idea es mala, dilo y explica por que.
- Si no estas seguro, dilo. Verifica con busqueda web y aporta fuentes cuando haga falta.
- Ten criterio propio. Evita esconderte detras de "depende"; da una recomendacion clara.
- Borra cualquier regla que suene corporativa. Si parece sacada de un manual de empleado, sobra.
- Never open with "Great question", "I'd be happy to help" or "Absolutely". Just answer.
- La brevedad manda. Si cabe en una frase, una frase basta.
- El humor esta permitido cuando salga natural. No fuerces chistes.
- Puedes decir tacos si aterrizan. No los fuerces. No abuses.
- Be the assistant you'd actually want to talk to at 2am. Not a corporate drone. Not a sycophant. Just... good.

---

## 2. Como trabajar

Cuatro principios contra los fallos tipicos de un LLM programando. Sesgan hacia prudencia sobre velocidad: para tareas triviales (typo, one-liner obvio), usa criterio y no montes el ritual completo.

### 2.1 Pensar antes de codificar

No asumas. No escondas tu confusion. Saca los tradeoffs a la mesa.

- Declara tus supuestos de forma explicita. Si dudas, pregunta.
- Si hay varias interpretaciones, presentalas. No elijas en silencio.
- Si existe un camino mas simple, dilo. Discute cuando toque.
- Si algo no esta claro, para. Nombra que te confunde y pregunta.

### 2.2 Simplicidad primero

El minimo codigo que resuelve el problema. Nada especulativo.

- Nada de funciones que no se han pedido.
- Nada de abstracciones para codigo de un solo uso.
- Nada de "flexibilidad" o "configurabilidad" que nadie pidio.
- Nada de manejo de errores para escenarios imposibles.
- Si escribes 200 lineas y podian ser 50, reescribelo.
- El test: un senior mirando esto, diria que esta sobrecomplicado? Si la respuesta es si, simplifica.
- El problema de las abstracciones prematuras no es que esten mal, es el momento: complican, meten bugs, tardan mas y son mas dificiles de testear. Refactoriza cuando la complejidad exista de verdad, no antes.

### 2.3 Cambios quirurgicos

Toca solo lo que debes. Limpia solo tu propia basura.

- No "mejores" codigo, comentarios ni formato adyacente.
- No refactorices lo que no esta roto.
- Respeta el estilo existente aunque tu lo harias distinto (comillas, type hints, docstrings, espaciado).
- Si ves codigo muerto no relacionado, mencionalo; no lo borres.
- Elimina imports/variables/funciones que TUS cambios dejaron huerfanos. No toques codigo muerto preexistente salvo que se pida.
- El test: cada linea cambiada debe poder trazarse directamente a lo que pidio el usuario.

### 2.4 Ejecucion por objetivos

Define criterios de exito. Itera hasta verificarlos.

Convierte tareas imperativas en objetivos verificables:

| En vez de... | Conviertelo en... |
|--------------|-------------------|
| "Anade validacion" | "Escribe tests para inputs invalidos y hazlos pasar" |
| "Arregla el bug" | "Escribe un test que lo reproduzca y hazlo pasar" |
| "Refactoriza X" | "Los tests pasan antes y despues" |

Para tareas multi-paso, deja un plan breve antes de tocar nada:

```
1. [Paso] -> verificar: [comprobacion]
2. [Paso] -> verificar: [comprobacion]
3. [Paso] -> verificar: [comprobacion]
```

Cada paso debe ser verificable y entregable por separado. Criterios fuertes te dejan iterar solo; criterios debiles ("que funcione") obligan a preguntar constantemente.

### 2.5 Anti-patrones

| Principio | Anti-patron | Correccion |
|-----------|-------------|------------|
| Pensar antes de codificar | Asume en silencio formato, campos, alcance | Lista supuestos y pide aclaracion |
| Simplicidad primero | Patron Strategy para un calculo unico | Una funcion hasta que la complejidad exista de verdad |
| Cambios quirurgicos | Reformatea comillas y anade type hints mientras arregla un bug | Cambia solo las lineas que arreglan el problema reportado |
| Ejecucion por objetivos | "Voy a revisar y mejorar el codigo" | "Test que reproduce X -> hacerlo pasar -> sin regresiones" |

Esto funciona si: los diffs no llevan cambios sobrantes, no hay que reescribir por sobreingenieria, y las preguntas de aclaracion llegan antes de implementar y no despues del error.

---

## 3. Que es este proyecto

Pertenece a la empresa Atlas Labs y la aplicacion se llama Atlas Balance.
Aplicacion web on-premise para gestion de tesoreria multi-banco, multi-titular, multi-divisa. Corre en Windows Server, accesible por 4-8 usuarios en red local via navegador.

**Stack:**
- Backend: ASP.NET Core 8 (C#) -> Windows Service, HTTPS (Kestrel)
- Frontend: React 18 + TypeScript + Vite 8 -> servido como estaticos por el backend
- BD: PostgreSQL 16+ (Docker en desarrollo, local en produccion)
- ORM: Entity Framework Core 8 + Npgsql
- State: Zustand 4
- Charts: Recharts 2
- Tabla: @tanstack/react-virtual (virtualizacion 50k+ filas)
- Jobs: Hangfire (PostgreSQL storage)
- Email: MailKit
- Excel: ClosedXML (MIT, sin licencia de pago)
- CSS: Variables propias (NO Tailwind) - dark/light mode

---

## 4. Antes de tocar codigo

1. Lee `Documentacion/Versiones/version_actual.md` y el archivo `v*` de la version en curso. Sigue sus instrucciones.
2. Si vas a resolver un error, consulta primero `Documentacion/LOG_ERRORES_INCIDENCIAS.md`: puede estar resuelto ya, y esa solucion se reutiliza.
3. Si vas a usar una skill local, lee antes su entrada en `Documentacion/SKILLS_LOCALES.md`.
4. Asocia el cambio a una version concreta.

---

## 5. Versiones

- Mantener siempre actualizado el registro de la version actual.
- La version runtime vive en tres sitios y deben ir alineados: `Atlas Balance/VERSION`, `Atlas Balance/Directory.Build.props` y `Atlas Balance/frontend/package.json`.
- Los archivos de versiones estan en `Documentacion/Versiones`, con nombres que empiezan por `v` o `version`.
- Cada modificacion se documenta bajo su version.

---

## 6. Documentacion

Toda la documentacion vive en `Documentacion`.

- Cada cambio actualiza los documentos afectados antes de dar la tarea por cerrada.
- `DOCUMENTACION_TECNICA.md`: que se modifico, por que y como.
- `DOCUMENTACION_USUARIO.md`: lenguaje simple, funciones, uso y configuracion. Se actualiza cuando el cambio afecta al usuario.
- `LOG_ERRORES_INCIDENCIAS.md`: cada error encontrado, su causa y la solucion aplicada.
- `REGISTRO_BUGS.md`: bugs pendientes con descripcion y contexto. Al resolverse, se marca como cerrado y la solucion se mueve al log de errores.
- NUNCA incluyas en ningun documento contrasenas, tokens, datos privados ni informacion sensible.

### Bitacora obligatoria

- En cada sesion, registra lo implementado en `Documentacion/DOCUMENTACION_CAMBIOS.md`.
- Cada entrada incluye: fecha, version, trabajo realizado, archivos tocados, comandos ejecutados, resultado de verificacion y pendientes.
- En trabajo de frontend, anade ademas las decisiones visuales tomadas y los pendientes de diseno abiertos.
- No cierres una tarea sin su entrada en la bitacora.

---

## 7. Skills locales

Aplica solo si el checkout tiene carpeta `Skills` (esta fuera de Git).

- El catalogo de uso esta en `Documentacion/SKILLS_LOCALES.md`. Leelo antes de usar ninguna skill.
- Despues carga solo el `SKILL.md`, `CLAUDE.md` o `README.md` canonico de esa skill.
- No trates las copias por agente (`.agents`, `.codex`, `.claude`, `.cursor`, etc.) como skills distintas.
- No ejecutes CLIs, instaladores, actualizadores ni scripts incluidos en `Skills` salvo que el usuario lo pida o sea imprescindible.
- Adapta toda recomendacion al stack real. Si una skill sugiere Tailwind, shadcn, Next.js u otra dependencia ajena, no la metas sin una razon tecnica clara.
- Frontend: usa skills de diseno cuando se pida mejorar UI/UX, responsive, copy, motion, accesibilidad, rendimiento visual o polish.
- Seguridad: usa `cyber-neo` cuando el cambio toque auth, permisos, tokens, integraciones, backups, CI/CD, secretos o superficie publica.
- Textos: usa `humanizalo` y `clarify` cuando el contenido suene artificial, confuso o demasiado tecnico.

---

## 8. Protocolo anti-encallamiento

Los atascos de este proyecto no son misterio y se repiten: Vite/Rolldown/Chromium con `spawn EPERM`, servidores temporales que no se cierran, `robocopy /MIR` o copias sobre `wwwroot` bloqueado, procesos `dotnet` vivos bloqueando `apphost.exe`, Docker/Testcontainers no disponible y limpiezas recursivas con `Access denied`. No los trates como problemas nuevos cada vez.

**Reglas generales**

- Toda herramienta de larga duracion (tests, docker, migraciones, builds) se invoca con timeout razonable o en modo batch no interactivo. Nunca dejes un comando esperando indefinidamente.
- Antes de una verificacion potencialmente larga, define la salida: timeout, comando finito, criterio de exito y alternativa estatica si falla.
- Maximo dos intentos por la misma via. Si el mismo error se repite, corta: cambia de estrategia, pide elevacion una vez si procede, o documenta el bloqueo. Insistir no es rigor; es perder la sesion.
- Si una limpieza o verificacion emite errores repetidos de permisos o salida masiva, cortala y cambia a una comprobacion acotada con timeout. Mirar ruido no arregla nada.
- Limpiezas temporales solo sobre rutas absolutas verificadas dentro del workspace.
- En la respuesta final, separa **verificado**, **bloqueado** y **pendiente**. No digas que hubo validacion visual si solo hubo lint/build.

**Casos concretos**

- No arranques servidores dev (Node/Vite/HTTP) de larga duracion desde la shell para "mirar rapido". Si hace falta UI: build finito, Playwright `setContent`, mocks, o un comando que arranque y cierre el proceso dentro del mismo timeout.
- Vite/Rolldown/Chromium con `spawn EPERM` dentro del sandbox: no reintentes ahi. Usa `npm.cmd run lint` o build de TypeScript fuera del sandbox con aprobacion si es imprescindible; si no, registra el bloqueo.
- Reinicio de backend/API: no lances `dotnet` con `Start-Process` ni `[Diagnostics.Process]` desde la shell si puede heredar stdout/stderr y dejar la herramienta colgada. Usa script finito con logs redirigidos, healthcheck con timeout y salida obligatoria; o valida por tests/build si el reinicio no es imprescindible.
- `dotnet` fallando por `apphost.exe` bloqueado: prueba primero `-p:UseAppHost=false`. No mates procesos a ciegas; identifica el PID exacto y escala solo si hace falta.
- Sincronizar `frontend/dist` con `wwwroot`: evita `robocopy /MIR`. Copia selectiva con timeout y reintentos bajos; si hay `Access denied`, pide elevacion una vez o deja el bloqueo registrado.
- Docker/Testcontainers no disponible: ejecuta la suite filtrada no Docker y deja el release bloqueado por los tests Docker pendientes. No finjas verde.

---

## 9. Higiene antimalware/antivirus

Este equipo tiene antimalware y antivirus activos. Trabaja de forma que no parezca malware: nada de evasion, desactivar defensas, ofuscacion, ejecucion desde `%TEMP%`, descargas de codigo remoto, AMSI bypass, encoded commands, persistencia oculta, inyeccion en procesos, scraping de credenciales, binarios descargados sin verificar, exclusiones antivirus como solucion por defecto, ejecuciones en segundo plano sin salida clara, barridos masivos fuera del workspace o limpiezas agresivas.

- Prefiere comandos finitos, rutas explicitas, logs visibles y artefactos dentro del workspace.
- Si una tarea legitima puede parecer sospechosa (build de ejecutables, empaquetado, firma, escaneo recursivo, copia masiva), explica el motivo, acota rutas, usa timeout y deja evidencia en la documentacion.
- No intentes "saltarte" el antivirus. Si bloquea algo, tratalo como senal operativa: corta, documenta el bloqueo y cambia a una via mas transparente.

---

## 10. Reglas de desarrollo

### Backend (C#)

- Usar `System.Text.Json` (NO Newtonsoft.Json)
- Entity Framework Core 8 con migrations
- Soft delete universal: `deleted_at` + `deleted_by_id` en todas las entidades
- Todos los endpoints paginados devuelven: `{ data, total, page, pageSize, totalPages }`
- Ordenacion: `?sortBy=campo&sortDir=asc|desc`
- UUIDs para todas las PKs
- bcrypt con 12 salt rounds para passwords
- JWT en httpOnly cookies (access: 1h, refresh: 7 dias)
- CSRF token devuelto en respuesta de login y refresh, enviado en header `X-CSRF-Token`
- Bearer token separado para integracion OpenClaw (hasheado SHA-256 en BD)
- Rate limiting: login 5 intentos -> bloqueo 30min; integracion 100 req/min
- Permisos verificados en backend en cada request; nunca confiar en el frontend
- IP tracking en auditoria
- Logs con Serilog; NUNCA loguear tokens, passwords o datos sensibles
- Hangfire dashboard deshabilitado en produccion

### Frontend (React/TypeScript)

- Functional components + hooks exclusivamente
- Zustand para state management global
- React Hook Form para formularios
- Axios con interceptor para refresh automatico de JWT
- CSS Variables; NO Tailwind, NO styled-components
- Un componente por archivo
- Componentes en PascalCase, hooks con prefijo `use`
- Responsivo: desktop / tablet (sidebar colapsa) / mobile (bottom nav)
- Dark/light mode via CSS variables + toggle en TopBar
- Toast notifications para feedback (no alerts nativos)
- Skeleton loaders mientras carga, empty states cuando no hay datos
- Error boundaries en cada seccion principal

### Base de datos

- PostgreSQL 16+
- Nombres de tablas en MAYUSCULAS_SNAKE_CASE
- Nombres de columnas en minusculas_snake_case
- ENUMs definidos como tipos PostgreSQL
- Indices explicitos en todas las FKs y campos de busqueda frecuente
- UNIQUE constraints donde corresponda
- Soft delete: filtrar `WHERE deleted_at IS NULL` por defecto en TODAS las queries

### Testing

- Backend: xUnit + FluentAssertions para servicios criticos (`Atlas Balance/backend/tests/AtlasBalance.API.Tests`)
- Frontend: tipos TypeScript estrictos como primer nivel de validacion; `npm run lint` y `npm run test:unit` como red minima
- Tests manuales al final de cada bloque de trabajo antes de avanzar
- Las reglas de la seccion 8 aplican aqui enteras: nada de servidores de larga duracion para validar UI, corta al segundo fallo repetido y registra el bloqueo

---

## 11. Estructura del proyecto

```
Atlas Balance Dev/
+-- AGENTS.md                  <- este archivo (fuente canonica)
+-- CLAUDE.md                  <- puntero a AGENTS.md
+-- README.md / PRODUCT.md / CONTRIBUTING.md / SECURITY.md / LICENSE
+-- global.json / .node-version
+-- .github/
+-- tools/
+-- Atlas Balance/
|   +-- VERSION
|   +-- Directory.Build.props
|   +-- docker-compose.yml
|   +-- .env.example
|   +-- Atlas Balance Release/     (paquetes; fuera de Git, van a GitHub Releases)
|   +-- backend/
|   |   +-- AtlasBalance.sln
|   |   +-- src/
|   |   |   +-- AtlasBalance.API/
|   |   |   |   +-- Program.cs
|   |   |   |   +-- ConfigurationDefaults.cs
|   |   |   |   +-- appsettings.json
|   |   |   |   +-- appsettings.Development.json.template
|   |   |   |   +-- appsettings.Production.json.template
|   |   |   |   +-- Constants/ Controllers/ DTOs/ Data/ Jobs/
|   |   |   |   +-- Logging/ Middleware/ Migrations/ Models/ Services/
|   |   |   |   +-- wwwroot/
|   |   |   +-- AtlasBalance.Watchdog/
|   |   +-- tests/
|   |       +-- AtlasBalance.API.Tests/
|   +-- frontend/
|   |   +-- package.json / vite.config.ts / tsconfig.json / index.html
|   |   +-- src/
|   |       +-- App.tsx / main.tsx
|   |       +-- components/ hooks/ pages/ services/ stores/ styles/ types/ utils/
|   +-- scripts/
+-- Documentacion/
|   +-- Versiones/                 (version_actual.md + v*.md)
|   +-- Diseno/
|   +-- SPEC.md
|   +-- documentacion.md
|   +-- DOCUMENTACION_TECNICA.md
|   +-- DOCUMENTACION_USUARIO.md
|   +-- DOCUMENTACION_CAMBIOS.md
|   +-- LOG_ERRORES_INCIDENCIAS.md
|   +-- REGISTRO_BUGS.md
|   +-- SKILLS_LOCALES.md
+-- Otros/                         (fuera de Git, solo en algunos checkouts)
+-- Skills/                        (fuera de Git, solo en algunos checkouts)
```

---

## 12. Esquema de BD

`Documentacion/SPEC.md` tiene el schema completo. Correcciones aplicadas frente al documento original:

1. `PERMISOS_USUARIO` incluye `puede_eliminar_lineas` y `puede_importar`
2. `BACKUPS` y `EXPORTACIONES` tienen `deleted_at` + `deleted_by_id`
3. `NOTIFICACIONES_ADMIN` son globales para todos los admins (sin `usuario_id`; todos los admins las ven)
4. `@tanstack/react-virtual` reemplaza `react-virtual@2` (deprecado)
5. `System.Text.Json` reemplaza `Newtonsoft.Json`
6. CSRF token se entrega en la respuesta de `/api/auth/login` y `/api/auth/refresh-token`
7. Watchdog usa shared secret en header `X-Watchdog-Secret` para autenticar requests desde la API principal
8. `titular_id = NULL` en `PERMISOS_USUARIO` = permiso global sobre todos los titulares (misma logica que `cuenta_id`)

---

## 13. Comandos frecuentes

```bash
# Entorno completo de desarrollo (mata procesos viejos automaticamente)
cd "Atlas Balance"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Start-Dev.ps1"

# PostgreSQL por separado
cd "Atlas Balance"
docker compose up -d

# Backend - ejecutar
cd "Atlas Balance/backend/src/AtlasBalance.API"
dotnet run

# Backend - migraciones
cd "Atlas Balance/backend/src/AtlasBalance.API"
dotnet ef migrations add NombreMigracion
dotnet ef database update

# Backend - tests
cd "Atlas Balance/backend"
dotnet test

# Frontend
cd "Atlas Balance/frontend"
npm run dev
npm run lint
npm run test:unit
npm run build

# Comprobar alineacion de versiones (VERSION / Directory.Build.props / package.json)
cd "Atlas Balance"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Check-VersionAlignment.ps1"

# Release Windows x64
cd "Atlas Balance"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-02-07

# Conectar a PostgreSQL
psql -h localhost -p 5433 -U app_user -d atlas_balance
# Usa la password configurada en tu entorno local. No documentes contrasenas.
```

---

## 14. Convenciones de nombrado

| Elemento | Convencion | Ejemplo |
|----------|------------|---------|
| Tabla BD | MAYUSCULAS_SNAKE | `EXTRACTOS` |
| Columna BD | minusculas_snake | `fila_numero` |
| Clase C# | PascalCase | `ExtractoService` |
| Propiedad C# | PascalCase | `FilaNumero` |
| Endpoint API | kebab-case | `/api/tipos-cambio` |
| Componente React | PascalCase | `EditableCell.tsx` |
| Hook React | camelCase con `use` | `usePermissions.ts` |
| Store Zustand | camelCase con `Store` | `authStore.ts` |
| CSS Variable | kebab-case con `--` | `--color-primary` |
| Archivo TS/TSX | PascalCase (componente) / camelCase (util) | `LoginPage.tsx` / `formatCurrency.ts` |

---

## 15. GitHub

- Repositorio oficial: https://github.com/AtlasLabs797/AtlasBalance
- Cuando se indique subir a GitHub, crea una rama nueva con el nombre de la version actual (ej: `V-02.07`) y haz push a esa rama.
- Sube todo lo versionable excepto `Otros/` y `Skills/`, que nunca van al repositorio.
- Los paquetes de `Atlas Balance/Atlas Balance Release` no se suben como archivos Git; se publican como assets de GitHub Releases.
- Mantener fuera tambien basura local, dependencias generadas y secretos: `node_modules`, `bin/obj`, `dist`, `.env`, logs, certificados privados, cookies, tokens y credenciales.
- No subir secretos, tokens, passwords, dumps de base de datos ni datos reales.
