# CORRECCIONES A SPEC v3.0

Este documento lista todas las correcciones aplicadas al documento original `SPEC.md`.
Claude Code debe leer este archivo JUNTO con SPEC.md y aplicar estas correcciones.

---

## 1. PERMISOS_USUARIO — Campos añadidos

**Original:** Solo tenía `puede_agregar_lineas`, `puede_editar_lineas`, `puede_ver_dashboard`

**Corregido:** Añadir estos campos:
```sql
puede_eliminar_lineas  BOOLEAN DEFAULT false
puede_importar         BOOLEAN DEFAULT false
```

Schema completo corregido:
```sql
CREATE TABLE PERMISOS_USUARIO (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    usuario_id            UUID NOT NULL REFERENCES USUARIOS(id),
    cuenta_id             UUID REFERENCES CUENTAS(id),       -- NULL = todas las cuentas
    titular_id            UUID REFERENCES TITULARES(id),      -- NULL = todos los titulares
    puede_agregar_lineas  BOOLEAN DEFAULT false,
    puede_editar_lineas   BOOLEAN DEFAULT false,
    puede_eliminar_lineas BOOLEAN DEFAULT false,
    puede_importar        BOOLEAN DEFAULT false,
    puede_ver_dashboard   BOOLEAN DEFAULT false,
    columnas_visibles     JSONB,    -- null = todas
    columnas_editables    JSONB     -- null = todas permitidas por rol
);
```

**Lógica de NULLs:**
- `cuenta_id = NULL` → permiso aplica a TODAS las cuentas
- `titular_id = NULL` → permiso aplica a TODOS los titulares
- Ambos NULL → permiso global total (para ese campo)

---

## 2. BACKUPS y EXPORTACIONES — Soft delete añadido

**Original:** No tenían `deleted_at` ni `deleted_by_id`

**Corregido:** Añadir a ambas tablas:
```sql
deleted_at    TIMESTAMPTZ
deleted_by_id UUID REFERENCES USUARIOS(id)
```

---

## 3. NOTIFICACIONES_ADMIN — Aclaración

**Original:** No tenía `usuario_id`, no estaba claro quién las ve.

**Decisión:** Son GLOBALES para todos los admins. No se añade `usuario_id`.
Cualquier usuario con rol ADMIN ve todas las notificaciones.
`leida = true` es por notificación, no por usuario (si un admin la marca, desaparece para todos).

---

## 4. react-virtual@2 → @tanstack/react-virtual

**Original:** `react-virtual@2`
**Corregido:** `@tanstack/react-virtual` (paquete actual, mantenido activamente)

La API cambia ligeramente:
```tsx
// Viejo (react-virtual@2)
import { useVirtualizer } from 'react-virtual'

// Nuevo (@tanstack/react-virtual)
import { useVirtualizer } from '@tanstack/react-virtual'
```

---

## 5. Newtonsoft.Json eliminado

**Original:** Listado en dependencias NuGet
**Corregido:** Eliminado. Usar `System.Text.Json` nativo de ASP.NET Core 8.
Npgsql soporta System.Text.Json para JSONB sin necesidad de Newtonsoft.

---

## 6. ExchangeRate-API — Aclaración de límites

**Original:** "~60 requests/mes, límite 1,500/mes" (confuso)
**Corregido:**
- Límite del plan gratuito: 1,500 requests/mes
- Consumo estimado del sistema: ~60 requests/mes (sync cada 12h = 2/día × 30 = 60)
- Margen amplio: se usa <5% del límite

---

## 7. CSRF Token — Mecanismo de entrega definido

**Original:** No se especificaba cómo el frontend obtiene el CSRF token.

**Corregido:**
1. `POST /api/auth/login` → respuesta incluye `{ csrfToken: "..." }` en el body
2. `POST /api/auth/refresh-token` → respuesta incluye nuevo `csrfToken`
3. Frontend almacena el CSRF token en memoria (Zustand authStore)
4. Frontend envía el CSRF token en header `X-CSRF-Token` en TODAS las peticiones de mutación (POST/PUT/DELETE)
5. Backend valida el CSRF token contra el que está asociado a la sesión JWT

---

## 8. Watchdog — Shared secret definido

**Original:** Solo decía "localhost" como seguridad.

**Corregido:**
- Ambos servicios leen `WatchdogSharedSecret` de sus respectivos `appsettings.json`
- La API principal envía: `X-Watchdog-Secret: {secret}` en cada request al Watchdog
- El Watchdog valida el header antes de ejecutar cualquier operación
- Secret mínimo: 32 caracteres aleatorios

```json
// appsettings.json (ambos proyectos)
{
  "WatchdogSettings": {
    "SharedSecret": "clave-secreta-minimo-32-caracteres-generada-al-instalar"
  }
}
```

---

## 9. USUARIO_EMAILS — Relación con USUARIOS.email aclarada

**Decisión:** Son complementarios.
- `USUARIOS.email` = email de LOGIN (único, para autenticación)
- `USUARIO_EMAILS` = emails ADICIONALES para recibir notificaciones y alertas
- `es_principal` en USUARIO_EMAILS indica cuál es el email preferido para notificaciones
- El email de login NO se duplica en USUARIO_EMAILS

---

## 10. Dominio y HTTPS

**Original:** Asumía `caja.empresa.local` como dominio.

**Corregido para desarrollo:**
- Usar `localhost` con certificado mkcert
- Puerto 5000 (HTTPS) en desarrollo
- Sin dominio custom en desarrollo

**Para producción:**
- El admin configura el dominio local en `/etc/hosts` o DNS interno
- Script `setup-https.ps1` acepta el dominio como parámetro
- `app_base_url` en tabla CONFIGURACION almacena la URL final

---

## 11. Docker Compose — Solo para desarrollo

Docker se usa EXCLUSIVAMENTE para PostgreSQL en desarrollo.
En producción, PostgreSQL se instala nativamente en Windows Server.

El `docker-compose.yml` incluido levanta:
- PostgreSQL 14 expuesto en `localhost:5433`
- Volumen persistente para datos
- Usuario `app_user` con password configurada en entorno local
- BD `atlas_balance` creada automaticamente
