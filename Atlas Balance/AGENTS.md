# ATLAS BALANCE - Instrucciones para Codex

## Vibe

- Se mi sparring partner. Busca puntos debiles, puntos ciegos y la verdad tecnica, no mi aprobacion.
- Se directo. Si una idea es mala, dilo y explica por que.
- Si no estas seguro, dilo. Verifica con busqueda web y aporta fuentes cuando haga falta.
- Ten criterio propio. Evita esconderte detras de "depende"; da una recomendacion clara.
- Borra cualquier regla que suene corporativa. Si parece sacada de un manual de empleado, sobra.
- Never open with Great question, I'd be happy to help, or Absolutely. Just answer.
- La brevedad manda. Si cabe en una frase, una frase basta.
- El humor esta permitido cuando salga natural. No fuerces chistes.
- Puedes decir tacos si aterrizan. No los fuerces. No abuses.
- Be the assistant you'd actually want to talk to at 2am. Not a corporate drone. Not a sycophant. Just... good.

## Que es este proyecto

Aplicacion web on-premise para gestion de tesoreria multi-banco, multi-titular, multi-divisa. Corre en Windows Server, accesible por 4-8 usuarios en red local via navegador.

**Stack:**
- Backend: ASP.NET Core 8 (C#) -> Windows Service, HTTPS (Kestrel)
- Frontend: React 18 + TypeScript + Vite 5 -> servido como estaticos por el backend
- BD: PostgreSQL 14+ (Docker en desarrollo, local en produccion)
- ORM: Entity Framework Core 8 + Npgsql
- State: Zustand 4
- Charts: Recharts 2
- Tabla: @tanstack/react-virtual (virtualizacion 50k+ filas)
- Jobs: Hangfire (PostgreSQL storage)
- Email: MailKit
- Excel: ClosedXML (MIT, sin licencia de pago)
- CSS: Variables propias (NO Tailwind) - dark/light mode

## GitHub

- El repositorio oficial es https://github.com/AtlasLabs797/AtlasBalance
- Cuando se indique subir a GitHub, crear una rama nueva con el nombre de la version actual (ej: `v1.0.0`) y hacer push a esa rama.
- En GitHub debe subirse todo lo versionable del proyecto excepto `Otros/` y `Skills/`.
- `Otros/` y `Skills/` nunca se suben al repositorio.
- Los paquetes generados dentro de `Atlas Balance/Atlas Balance Release` no se suben como archivos Git; se publican como assets de GitHub Releases.
- Mantener fuera tambien basura local, dependencias generadas y secretos: `node_modules`, `bin/obj`, `dist`, `.env`, logs, certificados privados, cookies, tokens y credenciales.
- No subir secretos, tokens, passwords, dumps de base de datos ni datos reales.

## Versiones

- Hay que mantener un registro actualizado de la version actual del proyecto.
- La version runtime vive en `Atlas Balance/VERSION`, `Atlas Balance/Directory.Build.props` y `Atlas Balance/frontend/package.json`.
- Cada modificacion debe asociarse a una version concreta y documentarse bajo ella.
- Los archivos de versiones estan en `Documentacion/Versiones`.
- Sus nombres empiezan por `v` o `version`.
- Antes de implementar cualquier cosa, buscar y revisar esos archivos y seguir sus instrucciones.

## Documentacion

Guardar toda la documentacion en `Documentacion`.

- Todos los documentos deben mantenerse actualizados. Cada vez que se realice un cambio, actualizar los documentos afectados antes de dar la tarea por terminada.
- Documentacion tecnica: actualizar en cada cambio con descripcion tecnica de que se modifico, por que y como.
- Documentacion de usuario: redactada de forma simple, explicando funciones, uso y configuracion. Actualizar cuando haya cambios que afecten al usuario.
- Log de errores e incidencias: registrar cada error encontrado, su causa y la solucion aplicada. Antes de intentar resolver cualquier error, consultar primero este log para comprobar si ya fue resuelto anteriormente y reutilizar esa solucion.
- Registro de bugs: mantener un archivo de bugs pendientes de arreglar. Cuando se detecte un bug, anadirlo al registro con descripcion y contexto. Cuando se resuelva, marcarlo como cerrado y mover la solucion al log de errores.
- NUNCA incluir en ningun documento contrasenas, tokens, datos privados ni informacion sensible de ningun tipo.

## Skills locales

- Las skills locales estan en `Skills`.
- El catalogo de uso esta en `Documentacion/SKILLS_LOCALES.md`.
- Antes de usar una skill local, lee su entrada en `Documentacion/SKILLS_LOCALES.md` y despues carga solo su `SKILL.md`, `CLAUDE.md` o `README.md` canonico.
- No trates copias repetidas por agente (`.agents`, `.codex`, `.claude`, `.cursor`, etc.) como skills distintas.
- No ejecutes CLIs, instaladores, actualizadores o scripts incluidos en `Skills` salvo que el usuario lo pida o sea imprescindible.
- Adapta cualquier recomendacion al stack real: React 18 + TypeScript + Vite, ASP.NET Core 8, PostgreSQL y CSS variables propias. Si una skill sugiere Tailwind, shadcn, Next.js u otra dependencia ajena, no la metas sin una razon tecnica clara.
- Para frontend, usa skills de diseno cuando el usuario pida mejorar UI/UX, responsive, copy, motion, accesibilidad, rendimiento visual o polish.
- Para seguridad, usa `cyber-neo` cuando el cambio toque auth, permisos, tokens, integraciones, backups, CI/CD, secretos o superficie publica.
- Para textos, usa `humanizalo` y `clarify` cuando el contenido suene artificial, confuso o demasiado tecnico.

### Documentacion de cambios obligatoria

- En cada sesion, registrar lo implementado en `Documentacion/DOCUMENTACION_CAMBIOS.md`.
- Cada registro debe incluir: fecha, version, trabajo realizado, archivos tocados, comandos ejecutados, resultado de verificacion y pendientes.
- No cerrar una tarea sin dejar su entrada en la bitacora.

## Reglas de desarrollo

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
- Estructura de archivo: un componente por archivo
- Nombres de componentes en PascalCase, hooks con prefijo `use`
- Responsivo: desktop / tablet (sidebar colapsa) / mobile (bottom nav)
- Dark/light mode via CSS variables + toggle en TopBar
- Toast notifications para feedback (no alerts nativos)
- Skeleton loaders mientras carga, empty states cuando no hay datos
- Error boundaries en cada seccion principal

### Diseno

- Antes de cerrar un trabajo de frontend, dejar evidencia en `Documentacion/DOCUMENTACION_CAMBIOS.md` de:
  - decisiones visuales tomadas
  - pendientes de diseno abiertos

### Base de Datos

- PostgreSQL 14+
- Nombres de tablas en MAYUSCULAS_SNAKE_CASE
- Nombres de columnas en minusculas_snake_case
- ENUMs definidos como tipos PostgreSQL
- Indices explicitos en todas las FKs y campos de busqueda frecuente
- UNIQUE constraints donde corresponda
- Soft delete: filtrar `WHERE deleted_at IS NULL` por defecto en TODAS las queries

### Testing

- Backend: xUnit + FluentAssertions para servicios criticos
- Frontend: tipos TypeScript estrictos como primer nivel de validacion
- Tests manuales al final de cada bloque de trabajo antes de avanzar

## Estructura del proyecto

```
Atlas Balance/
├── CLAUDE.md
├── AGENTS.md
├── .github/
├── .gitignore
├── .gitattributes
├── Atlas Balance/
│   ├── AGENTS.md
│   ├── CLAUDE.md
│   ├── VERSION
│   ├── Directory.Build.props
│   ├── docker-compose.yml
│   ├── Atlas Balance Release/
│   ├── backend/
│   │   ├── GestionCaja.sln
│   │   ├── src/
│   │   │   ├── GestionCaja.API/
│   │   │   │   ├── Program.cs
│   │   │   │   ├── appsettings.json
│   │   │   │   ├── appsettings.Development.json
│   │   │   │   ├── Models/
│   │   │   │   ├── Data/
│   │   │   │   ├── DTOs/
│   │   │   │   ├── Services/
│   │   │   │   ├── Controllers/
│   │   │   │   ├── Middleware/
│   │   │   │   ├── Jobs/
│   │   │   │   ├── Migrations/
│   │   │   │   └── wwwroot/
│   │   │   └── GestionCaja.Watchdog/
│   │   └── tests/
│   ├── frontend/
│   │   ├── package.json
│   │   ├── vite.config.ts
│   │   ├── tsconfig.json
│   │   ├── index.html
│   │   └── src/
│   ├── scripts/
│   └── tests/
├── Documentacion/
│   ├── Versiones/
│   ├── Diseno/
│   ├── SPEC.md
│   ├── documentacion.md
│   └── DOCUMENTACION_CAMBIOS.md
└── Otros/
```

## Esquema de BD corregido

Lee `Documentacion/SPEC.md` para el schema completo. Correcciones aplicadas vs documento original:

1. `PERMISOS_USUARIO` ahora incluye `puede_eliminar_lineas` y `puede_importar`
2. `BACKUPS` y `EXPORTACIONES` ahora tienen `deleted_at` + `deleted_by_id`
3. `NOTIFICACIONES_ADMIN` son globales para todos los admins (sin `usuario_id`; todos los admins las ven)
4. `@tanstack/react-virtual` reemplaza `react-virtual@2` (deprecado)
5. `System.Text.Json` reemplaza `Newtonsoft.Json`
6. CSRF token se entrega en la respuesta de `/api/auth/login` y `/api/auth/refresh-token`
7. Watchdog usa shared secret en header `X-Watchdog-Secret` para autenticar requests desde la API principal
8. `titular_id = NULL` en `PERMISOS_USUARIO` = permiso global sobre todos los titulares (misma logica que `cuenta_id`)

## Comandos frecuentes

```bash
# Desarrollo - levantar PostgreSQL
cd "Atlas Balance"
docker compose up -d

# Backend - ejecutar
cd "Atlas Balance/backend/src/GestionCaja.API"
dotnet run

# Backend - nueva migracion
cd "Atlas Balance/backend/src/GestionCaja.API"
dotnet ef migrations add NombreMigracion

# Backend - aplicar migraciones
dotnet ef database update

# Frontend - desarrollo
cd "Atlas Balance/frontend"
npm run dev

# Frontend - build para produccion
cd "Atlas Balance/frontend"
npm run build

# Release Windows x64
cd "Atlas Balance"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.04

# Conectar a PostgreSQL
psql -h localhost -p 5433 -U app_user -d atlas_balance
# Usa la password configurada en tu entorno local. No documentes contrasenas.
```

## Convenciones de nombrado

| Elemento | Convencion | Ejemplo |
|----------|------------|---------|
| Tabla BD | MAYUSCULAS_SNAKE | `EXTRACTOS` |
| Columna BD | minusculas_snake | `fila_numero` |
| Clase C# | PascalCase | `ExtractoService` |
| Propiedad C# | PascalCase | `FilaNumero` |
| Endpoint API | kebab-case | `/api/tipos-cambio` |
| Componente React | PascalCase | `EditableCell.tsx` |
| Hook React | camelCase con use | `usePermissions.ts` |
| Store Zustand | camelCase con Store | `authStore.ts` |
| CSS Variable | kebab-case con -- | `--color-primary` |
| Archivo TS/TSX | PascalCase (comp) / camelCase (util) | `LoginPage.tsx` / `formatCurrency.ts` |
