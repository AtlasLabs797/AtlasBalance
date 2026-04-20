# Atlas Balance — Especificación Técnica Completa

**Versión 3.0 — Abril 2026**

**Estado: Listo para desarrollo con Claude Code**



---



## Tabla de Contenidos



1. [Descripción del Proyecto](#1-descripción-del-proyecto)

2. [Arquitectura Técnica](#2-arquitectura-técnica)

3. [Características Principales](#3-características-principales)

4. [Integración OpenClaw](#4-integración-openclaw)

5. [Schema de Base de Datos](#5-schema-de-base-de-datos)

6. [API Endpoints](#6-api-endpoints)

7. [Arquitectura Frontend](#7-arquitectura-frontend)

8. [Arquitectura Backend](#8-arquitectura-backend)

9. [Stack Tecnológico y Dependencias](#9-stack-tecnológico-y-dependencias)

10. [Instalación y Requisitos](#10-instalación-y-requisitos)

11. [Plan de Ejecución — Fases de Desarrollo](#11-plan-de-ejecución--fases-de-desarrollo)

12. [Matriz de Permisos](#12-matriz-de-permisos)

13. [Registro de Decisiones de Diseño](#13-registro-de-decisiones-de-diseño)



---



## 1. Descripción del Proyecto



La aplicación de **Atlas Balance** es un sistema integral para administrar saldos bancarios y efectivo de múltiples bancos, cuentas y titulares desde una interfaz centralizada. Corre completamente en el servidor local del cliente (on-premise), accesible por red interna desde hasta 8 usuarios con navegador web.



No requiere conexión a internet para funcionar (salvo para sincronizar tipos de cambio). Incluye integración con el agente `main` de **OpenClaw** para consultas financieras en tiempo real mediante IA.



### Propósito Principal



- Centralizar información financiera de múltiples fuentes en una sola interfaz

- Automatizar importación de extractos bancarios con mapeo flexible de columnas

- Proporcionar análisis en tiempo real del flujo de caja con dashboards visuales

- Asegurar trazabilidad completa (auditoría) de todos los cambios realizados

- Facilitar gestión de permisos y acceso controlado a datos financieros sensibles

- Permitir que el agente IA OpenClaw consulte datos financieros de forma segura



### Público Objetivo



- Pequeñas y medianas empresas con múltiples cuentas bancarias

- Contables y gerentes financieros

- Administradores de tesorería



### Divisas Soportadas (inicial)



| Código | Nombre | Símbolo |

|--------|--------|---------|

| EUR | Euro | € |

| USD | Dólar Estadounidense | $ |

| MXN | Peso Mexicano | MX$ |

| DOP | Peso Dominicano | RD$ |



Arquitectura abierta: el admin puede añadir nuevas divisas desde la UI sin modificar código.



---



## 2. Arquitectura Técnica



### 2.1 Modelo de Despliegue



```

[4-8 Clientes: Chrome / Edge en red local]

         ↕ HTTPS (caja.empresa.local)

┌─────────────────────────────────────────┐

│  Windows Server / Windows 10+ Pro        │

│                                          │

│  ┌──────────────────────────────────┐   │

│  │  Windows Service: GestionCaja.API │   │

│  │  Puerto 443 (HTTPS)               │   │

│  │  - Sirve API REST                 │   │

│  │  - Sirve archivos estáticos React │   │

│  │  - Hangfire background jobs       │   │

│  └──────────────────────────────────┘   │

│                                          │

│  ┌──────────────────────────────────┐   │

│  │  Windows Service: GestionCaja    │   │

│  │  .Watchdog                        │   │

│  │  Puerto 5001 (solo localhost)     │   │

│  │  - Gestiona restauración backup   │   │

│  │  - Gestiona actualización de app  │   │

│  └──────────────────────────────────┘   │

│                                          │

│  ┌──────────────────────────────────┐   │

│  │  PostgreSQL 14+                   │   │

│  │  Puerto 5432 (solo localhost)     │   │

│  │  Pool 20 conexiones (Npgsql)      │   │

│  └──────────────────────────────────┘   │

└─────────────────────────────────────────┘

         ↑ Bearer Token (sk_gestion_caja_xxx)

[OpenClaw main agent — red local o misma máquina]

```



**Notas importantes:**

- El backend (Kestrel) sirve tanto la API como el build estático del frontend

- Un solo puerto expuesto al exterior (443 HTTPS)

- PostgreSQL NO es accesible desde la red — solo desde el backend local

- El Watchdog escucha en `localhost:5001` — nunca expuesto a la red

- Connection pool de Npgsql: 20 conexiones (suficiente para 8 usuarios)

- OpenClaw usa un token de integración independiente de la autenticación de usuarios



### 2.2 Frontend



- React 18 + TypeScript

- Vite 5 (bundler y dev server)

- React Router v6 (routing SPA)

- Zustand 4 (state management — elegido sobre Context API por la complejidad de permisos granulares, multi-divisa y estado cross-componente)

- Recharts 2 (gráficas/charts)

- @tanstack/react-virtual (virtualización de tabla para 50k+ filas sin lag)

- React Hook Form 7 (formularios)

- Axios 1.6 (HTTP client)

- CSS Variables propio (no Tailwind — control total sobre theming dark/light)

- Interfaz responsiva: Desktop / Tablet / Mobile



### 2.3 Backend



- ASP.NET Core 8 (C#)

- Desplegado como **Windows Service** (`UseWindowsService()`)

- API REST sobre HTTPS (Kestrel)

- Sirve archivos estáticos del frontend (build de React)

- Autenticación JWT en httpOnly cookies (access: 1h, refresh: 7 días)

- Autenticación separada por Bearer Token para integración OpenClaw

- Hangfire para background jobs (backups, exportaciones, sync divisas)

- MailKit para envío de emails SMTP



### 2.4 Base de Datos



- PostgreSQL 14+

- Alojado en el mismo servidor, acceso solo local

- Entity Framework Core 8 (ORM + migrations)

- Npgsql (driver PostgreSQL para .NET)

- Soft delete universal: ningún dato se borra físicamente



### 2.5 Seguridad



- **HTTPS** con dominio local (`caja.empresa.local`) via `mkcert`

  - Una sola configuración inicial en el servidor

  - Instalación del certificado CA en las máquinas cliente (script automático)

  - Sin advertencias de seguridad en el navegador

  - Permite httpOnly + Secure cookies para JWT

- **JWT** en httpOnly cookies (inmune a XSS)

- **Refresh tokens** almacenados en BD (tabla `REFRESH_TOKENS`) para invalidación correcta en logout y rotación automática

- **Bearer tokens** para integración OpenClaw (hasheados en BD, visibles solo una vez al crear)

- bcrypt con 12 salt rounds para contraseñas

- Rate limiting: 5 intentos de login fallidos → cuenta bloqueada 30 minutos

- Rate limiting para integración: 100 requests/minuto por token

- Primer login fuerza cambio de contraseña obligatorio

- CSRF tokens en header `X-CSRF-Token` para todas las mutaciones de usuarios

- Soft delete universal (auditable, recuperable)

- Permisos verificados en backend en cada request

- IP tracking en auditoría de acceso



---



## 3. Características Principales



### 3.1 Gestión de Datos Financieros



**Multi-Banco:**

- Crear y gestionar múltiples cuentas bancarias

- Cada banco puede tener su propio formato de extracto con columnas personalizadas

- Asociación flexible entre cuenta y formato de importación



**Multi-Titular:**

- Registrar múltiples titulares (Empresas o Particulares)

- Un titular puede tener múltiples cuentas

- Una cuenta pertenece a un único titular

- Dashboard independiente por titular



**Efectivo como Cuenta Especial:**

- El efectivo se trata como una cuenta con flag `es_efectivo = true`

- Mismo extracto que una cuenta bancaria: fecha, concepto, monto, saldo

- Mismo flujo de importación (Excel/CSV) y entrada manual desde la UI

- Puede haber múltiples cajas de efectivo por titular (ej: Caja Madrid EUR, Caja Barcelona USD)

- Sin número de cuenta ni IBAN

- Visualmente distinguida en la UI (icono/etiqueta diferente)



**Multi-Divisa:**

- Divisas iniciales: EUR, USD, MXN, DOP

- El admin puede añadir nuevas divisas desde la UI en cualquier momento

- Conversión automática mediante API de tipos de cambio

- Selector de divisa principal en dashboards (dropdown)

- Visualización de saldos en divisa nativa + total convertido



### 3.2 Importación de Extractos



**Formatos de Importación (Admin):**

- Admin crea plantillas reutilizables por banco

- Mapeo de columnas base por indice:

  - `una_columna`: Fecha, Concepto, Monto, Saldo

  - `dos_columnas`: Fecha, Concepto, Ingreso, Egreso, Saldo

  - `tres_columnas`: Fecha, Concepto, Ingreso, Egreso, Monto, Saldo

- En `dos_columnas` y `tres_columnas`, Ingreso se normaliza a monto positivo y Egreso se normaliza a monto negativo.

- En `tres_columnas`, Ingreso/Egreso calculan el monto firmado y Monto se usa como control de cuadre.

- Soporte de columnas extra personalizadas por banco (Referencia, Categoría, Número de cheque, etc.)

- Cada plantilla se asocia a una divisa específica

- Reutilizable entre cuentas del mismo banco



**Wizard de Importación (4 pasos):**



*Paso 1 — Pegar datos:*

- Textarea con detección automática de separador (tab, coma, punto y coma)

- Preview de las primeras 3 filas en tiempo real



*Paso 2 — Mapear columnas:*

- Si la cuenta tiene un formato guardado, se precarga automáticamente

- Si no, mapeo manual por el usuario

- Soporte para columnas extra: botón "Agregar columna extra"

- Aplica tanto a cuentas bancarias como a cajas de efectivo



*Paso 3 — Preview validado:*

- Tabla con ✓/✗ por fila

- Errores detectados: fechas inválidas, montos no numéricos, saldo vacío

- Filas con error marcadas en rojo con mensaje específico

- Permite continuar con filas válidas (importación parcial)



*Paso 4 — Confirmar:*

- Resumen: "47 filas OK, 3 con errores"

- Resultado registrado en auditoría



**Formatos de fecha aceptados:** `DD/MM/YYYY`, `YYYY-MM-DD`, `DD-MM-YYYY`, seriales numéricos de Excel

**Separadores aceptados:** Tabulador, coma, punto y coma



### 3.3 Visualización de Extractos — Tabla Excel-like



Esta es la pieza central de la aplicación.



**Columnas fijas (en este orden):** Nº Fila | Checkbox | Flag | Fecha | Concepto | Monto | Saldo



**Columnas extra (dinámicas, por banco):**

- Las definidas en el formato de importación del banco

- En la vista unificada: si dos cuentas tienen una columna extra con exactamente el mismo nombre, se fusionan. Si los nombres son diferentes, aparecen como columnas separadas con el nombre del banco como prefijo



**Interacción:**

- Doble clic en celda para editar (si tiene permiso) → Enter confirma, Esc cancela

- Solo columnas autorizadas por el admin son editables para cada usuario

- Click en header de columna: ordena ASC/DESC

- Filtro inline en cada header de columna

- Panel de visibilidad de columnas: toggle para mostrar/ocultar cualquier columna (persistido en BD por usuario)



**Checkbox por fila:** casilla marcable, registrada en auditoría (quién, cuándo)



**Flag por fila (alerta visual):**

- Marca la fila con fondo amarillo

- Permite añadir una nota textual opcional

- Icono de bandera en la columna Flag

- Registrado en auditoría

- Útil para marcar filas que requieren atención o revisión



**Referencia de celda estilo Excel:**

- Columna A = Fecha, B = Concepto, C = Monto, D = Saldo, E+ = columnas extra

- `fila_numero` es INMUTABLE: asignado al insertar (MAX+1 por cuenta)

- Si se borra una fila, el número queda hueco (como en Excel)



**Virtualización:** implementada con `react-virtual`, maneja 50.000+ filas sin degradación



**Vistas disponibles:**

1. **Vista Unificada:** TODOS los extractos de TODOS los titulares y cuentas

2. **Vista por Titular:** tabs por titular, dentro cada cuenta en sub-tabla

3. **Vista de Cuenta Detallada:** extracto completo + KPIs superiores (Saldo Actual, Ingresos Mes, Egresos Mes, Última Actualización)



### 3.4 Dashboards y Analítica



**Acceso:**

- Admin: acceso total

- Gerente: acceso si habilitado por el admin

- Empleado Ultra / Plus / Empleado: sin acceso



**Dashboard Principal (Global):**

- Saldos totales por divisa (EUR: X, USD: Y, MXN: Z, DOP: W)

- Total convertido a divisa principal (seleccionable en dropdown)

- Ingresos y egresos del mes actual

- Tabla de saldos por titular

- Gráficas de evolución con selector de período:

  - `1m` → puntos **diarios**

- `3m / 6m / 9m / 12m / 18m / 24m` → puntos **semanales** (inicio de semana)

  - Tres líneas: Ingresos (verde), Egresos (rojo), Saldo (gris)

  - Colores configurables por el admin desde ConfiguracionPage



**Dashboard por Titular:**

- Idéntico al principal pero filtrado para un solo titular

- Desglose por cuenta del titular con saldo individual



**Nota sobre saldo:** proviene del campo `saldo` del último extracto registrado para cada cuenta. No se recalcula — refleja el valor introducido por el usuario.



### 3.5 Alertas de Saldo Bajo



**Configuración (solo Admin):**

- Saldo mínimo GLOBAL (opcional, aplica a todas las cuentas sin alerta propia)

- Saldo mínimo POR CUENTA (sobreescribe el global para esa cuenta)

- Receptores de correo configurables por cuenta



**Disparo de alerta:** se evalúa CADA VEZ que se crea o edita una línea de extracto



**Contenido del email:**

- Nombre del titular y la cuenta afectada

- Saldo actual y saldo mínimo configurado

- Concepto del último movimiento

- Link directo: `{app_base_url}/cuentas/{cuenta_id}`



**Notificación in-app:** banner en la parte superior al cargar cualquier página, dismissible por sesión



### 3.6 Gestión de Usuarios y Permisos



**5 Roles:**



| Rol | Descripción |

|-----|-------------|

| **ADMIN** | Acceso total al sistema |

| **GERENTE** | Ver y editar cuentas asignadas, exportar, dashboard si habilitado |

| **EMPLEADO_ULTRA** | Ver + agregar + editar en cuentas autorizadas |

| **EMPLEADO_PLUS** | Ver + solo agregar nuevas líneas (no editar anteriores) |

| **EMPLEADO** | Solo lectura en cuentas autorizadas |



**Permisos granulares** (configurados por Admin por usuario):

- Qué titulares puede ver

- Qué cuentas puede ver/editar

- Qué columnas puede ver (incluye columnas extra)

- Qué columnas puede editar

- Si puede ver dashboards (solo aplica a Gerente)



### 3.7 Auditoría Completa



**Rastreo a nivel de celda:**

- Referencia de celda estilo Excel (A1, B5, C10...)

- Nombre legible de la columna

- Timestamp exacto, usuario, cuenta afectada, IP

- Valor ANTES y DESPUÉS del cambio



**Rastreo de sistema:**

- Creación/modificación/eliminación de todas las entidades

- Cambios de permisos

- Importaciones (filas importadas, errores)

- Login/logout (con IP)

- Cambios en alertas y configuración

- Operaciones de backup y exportación

- Accesos de la integración OpenClaw (tabla separada)



**Panel de auditoría (solo Admin):**

- Tabla paginada con filtros por usuario, fecha, tipo de acción, cuenta

- Exportación en CSV

- Pestaña separada para auditoría de accesos de integración



### 3.8 Backups Automáticos



- Frecuencia: cada domingo a las 02:00 AM (Hangfire)

- Retención: 6 semanas (eliminación automática del más antiguo)

- Formato: `pg_dump` → `backup_YYYY-MM-DD.dump`

- Restauración 100% desde UI via Watchdog Service (sin terminal)

- Flujo: confirmación doble → Watchdog para API → pg_restore → reinicia API → polling → redirección a login



### 3.9 Exportaciones



**Automáticas:** día 1 de cada mes a las 01:00 AM, XLSX por cada cuenta activa

**Manuales:** cualquier usuario con permiso puede exportar XLSX de cualquier cuenta on-demand

**Formato:** `NombreTitular_NombreCuenta_YYYY.MM.xlsx`

**Librería:** ClosedXML (MIT license, gratuita para uso comercial)



### 3.10 Tipos de Cambio



- API: ExchangeRate-API plan gratuito (límite 1,500 req/mes — consumo estimado ~60 req/mes con sync cada 12h)

- Sync automático cada 12 horas (Hangfire)

- Fallback a último valor almacenado si no hay internet

- Admin puede editar tasas manualmente y forzar sync desde ConfiguracionPage



### 3.11 Actualización de App



- Badge "Actualización disponible" en sidebar cuando hay nueva versión

- Admin pulsa "Actualizar ahora" → Watchdog gestiona el proceso completo

- Frontend hace polling durante la actualización → vuelve al login con mensaje de confirmación



### 3.12 Soft Delete y Papelera



- Ningún dato se borra físicamente de la BD

- Todas las entidades tienen `deleted_at` y `deleted_by_id`

- Admin tiene sección "Papelera" por entidad con opción de restaurar

- Eliminación y restauración registradas en auditoría



---



## 4. Integración OpenClaw



### 4.1 Descripción General



La integración OpenClaw permite que el agente `main` de OpenClaw acceda a los datos financieros de Atlas Balance en tiempo real para consultar saldos, analizar tendencias, revisar alertas y acceder al historial de auditoría.



**Caso de uso principal:**

```

Usuario → OpenClaw main: "¿Cuál es el saldo actual de todas nuestras cuentas?"

OpenClaw:

  1. Usa token de integración para autenticarse en la API

  2. Consulta GET /api/integration/openclaw/saldos

  3. Recibe JSON con saldos actualizados

  4. Procesa datos y responde al usuario

```



**Niveles de acceso:**



| Nivel | Capacidad | Estado |

|-------|-----------|--------|

| Lectura | Ver datos, consultar gráficas, revisar auditoría | Fase 1 — implementar |

| Escritura | Agregar líneas, crear alertas | Fase 2 — futuro Q3 2026 |

| Administración | Crear usuarios, cambiar permisos | ❌ No permitido nunca |



### 4.2 Arquitectura de Integración



```

┌─────────────┐

│  OpenClaw   │

│  (main)     │

└──────┬──────┘

       │ Authorization: Bearer sk_gestion_caja_xxx

       ?

┌──────────────────────────────┐

│   IntegrationAuthMiddleware  │

│   Valida token + extrae permisos

└──────┬───────────────────────┘

       │ Token inválido → 401

       ?

┌──────────────────────────────┐

│  IntegrationAuthorizationService

│  Filtra titulares/cuentas    │

│  permitidos para ese token   │

└──────┬───────────────────────┘

       │ Sin acceso → 403

       ?

┌──────────────────────────────┐

│  OpenClawIntegrationController

│  /api/integration/openclaw/* │

└──────┬───────────────────────┘

       ?

┌──────────────────────────────┐

│  PostgreSQL (lectura)        │

└──────┬───────────────────────┘

       ?

┌──────────────────────────────┐

│  Response JSON               │

│  format: full | simple       │

└──────────────────────────────┘

```



### 4.3 Gestión de Tokens



**Formato del token:**

```

sk_gestion_caja_[32 caracteres aleatorios Base64]

Ejemplo: sk_gestion_caja_a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6

```



**Ciclo de vida:**

1. Admin va a Configuración → Integraciones → "+ Nuevo Token"

2. Completa formulario: nombre, descripción, titulares/cuentas permitidas

3. Sistema genera token, lo hashea con SHA-256 y guarda el hash en BD

4. Admin ve el token **una sola vez** — debe copiarlo inmediatamente

5. Token se usa en `Authorization: Bearer` en cada request de OpenClaw

6. El backend registra `fecha_ultima_uso` en cada validación

7. Admin puede revocar en cualquier momento → estado `revocado`



**Rate limiting:** 100 requests/minuto por token → responde `429 Too Many Requests`



### 4.4 Endpoints de Integración



Todos los endpoints de integración requieren:

```

Authorization: Bearer sk_gestion_caja_xxx

```



Los datos devueltos están filtrados automáticamente a los titulares/cuentas permitidos para el token.



---



#### `GET /api/integration/openclaw/saldos`



Obtiene saldos actuales de todas las cuentas permitidas.



**Parámetros:**

```

?format=full|simple          (default: full)

?divisa=EUR|USD|MXN|DOP      (opcional, filtrar por divisa)

?titular_id=X                (opcional, solo un titular)

```



**Respuesta format=full:**

```json

{

  "exito": true,

  "datos": {

    "totales_por_divisa": {

      "EUR": 150000.50,

      "USD": 45000.00,

      "MXN": 120000.00,

      "DOP": 85000.00

    },

    "total_convertido": {

      "divisa_principal": "EUR",

      "monto": 197254.85

    },

    "tipo_cambio": {

      "EUR_USD": 1.10,

      "EUR_MXN": 18.50,

      "EUR_DOP": 60.20,

      "fecha_actualizacion": "2026-04-12T14:30:00Z"

    },

    "cuentas": [

      {

        "id": "uuid",

        "titular": { "id": "uuid", "nombre": "Empresa ABC", "tipo": "EMPRESA" },

        "nombre": "BBVA Cuenta Corriente",

        "iban": "ES9121000418450200051332",

        "es_efectivo": false,

        "divisa": "EUR",

        "saldo_actual": 75000.00,

        "ingresos_mes": 15000.00,

        "egresos_mes": 12500.00,

        "saldo_minimo_configurado": 10000.00,

        "estado_alerta": "ok",

        "fecha_ultimo_movimiento": "2026-04-12T10:30:00Z"

      }

    ],

    "generado_en": "2026-04-12T14:35:00Z"

  },

  "metadata": {

    "timestamp": "2026-04-12T14:35:00Z",

    "version_api": "1.0",

    "token_tipo": "openclaw"

  }

}

```



**Respuesta format=simple:**

```json

{

  "exito": true,

  "datos": {

    "EUR": 150000.50,

    "USD": 45000.00,

    "total_convertido": { "divisa": "EUR", "monto": 197254.85 }

  }

}

```



---



#### `GET /api/integration/openclaw/extractos`



Obtiene extractos de cuentas permitidas.



**Parámetros:**

```

?format=full|simple

?cuenta_id=X

?titular_id=X

?fecha_desde=YYYY-MM-DD

?fecha_hasta=YYYY-MM-DD

?limite=100           (default: 100, máximo: 1000)

?pagina=1

?ordenar_por=fecha|monto|saldo  (default: fecha)

?orden=asc|desc

```



**Nota:** `tipo_movimiento` en la respuesta es un campo **derivado** (calculado del signo de `monto`), no almacenado en la BD.



**Respuesta format=full:**

```json

{

  "exito": true,

  "datos": {

    "total_registros": 245,

    "pagina": 1,

    "registros_por_pagina": 100,

    "paginas_totales": 3,

    "extractos": [

      {

        "id": "uuid",

        "cuenta": { "id": "uuid", "nombre": "BBVA Cuenta Corriente", "divisa": "EUR" },

        "titular": { "id": "uuid", "nombre": "Empresa ABC" },

        "fecha": "2026-04-12",

        "concepto": "Ingreso de cliente",

        "monto": 5000.00,

        "tipo_movimiento": "INGRESO",

        "saldo": 75000.00,

        "fila_numero": 150,

        "checked": false,

        "flagged": false,

        "usuario_creacion": "juan.garcia@empresa.com",

        "fecha_creacion": "2026-04-12T10:30:00Z"

      }

    ],

    "resumen": {

      "total_ingresos": 25000.00,

      "total_egresos": -18000.00,

      "saldo_neto": 7000.00

    }

  }

}

```



---



#### `GET /api/integration/openclaw/grafica-evolucion`



Datos para gráficas de evolución (misma lógica que el dashboard interno).



**Parámetros:**

```

?format=full|simple

?periodo=1m|3m|6m|9m|12m|18m|24m   (default: 1m)

?titular_id=X

?cuenta_id=X

```



> **Nota:** `1m` → agregación diaria. `3m`, `6m`, `9m`, `12m`, `18m`, `24m` → agregación semanal.



**Respuesta format=full:**

```json

{

  "exito": true,

  "datos": {

    "periodo": "1m",

    "tipo_agregacion": "diario",

    "moneda_principal": "EUR",

    "puntos_datos": [

      {

        "fecha": "2026-03-12",

        "saldo": 70000.00,

        "ingresos": 15000.00,

        "egresos": -12000.00,

        "neto": 3000.00

      }

    ],

    "estadisticas": {

      "saldo_inicial": 70000.00,

      "saldo_final": 75000.00,

      "saldo_promedio": 72500.00,

      "saldo_minimo": 65000.00,

      "saldo_maximo": 76000.00,

      "total_ingresos": 75000.00,

      "total_egresos": -70000.00,

      "variacion_neta": 5000.00,

      "variacion_porcentaje": 7.14

    }

  }

}

```



---



#### `GET /api/integration/openclaw/alertas`



Alertas de saldo bajo activas para las cuentas permitidas.



**Parámetros:**

```

?format=full|simple

?estado=activa|inactiva|todos   (default: activa)

?titular_id=X

```



**Respuesta format=simple:**

```json

{

  "exito": true,

  "datos": [

    { "cuenta": "BBVA Cuenta Corriente", "saldo_actual": 75000.00, "saldo_minimo": 10000.00, "estado": "ok" },

    { "cuenta": "Efectivo Caja", "saldo_actual": 450.00, "saldo_minimo": 500.00, "estado": "activada" }

  ]

}

```



---



#### `GET /api/integration/openclaw/auditoria`



Acceso a logs de auditoría de cambios en extractos.



**Parámetros:**

```

?format=full|simple

?cuenta_id=X          (obligatorio si no se indica titular_id)

?titular_id=X

?fecha_desde=YYYY-MM-DD

?fecha_hasta=YYYY-MM-DD

?tipo_accion=all|EDIT_CELDA|CREATE_EXTRACTO

?limite=100

?pagina=1

```



---



#### `GET /api/integration/openclaw/titulares`



Lista de titulares y cuentas permitidos para el token actual.



**Parámetros:**

```

?format=full|simple

```



---



### 4.5 Formato de Respuesta General



Todas las respuestas de integración siguen esta estructura:



```json

{

  "exito": boolean,

  "datos": {},

  "errores": [],

  "advertencias": [],

  "metadata": {

    "timestamp": "ISO-8601",

    "version_api": "1.0",

    "token_tipo": "openclaw"

  }

}

```



**Códigos de error:**



| HTTP | Código | Causa |

|------|--------|-------|

| 400 | BAD_REQUEST | Parámetros inválidos |

| 401 | UNAUTHORIZED | Token inválido o revocado |

| 403 | FORBIDDEN | Sin permisos para acceder a esos datos |

| 404 | NOT_FOUND | Recurso no existe |

| 429 | RATE_LIMITED | Más de 100 requests/minuto |

| 500 | SERVER_ERROR | Error interno |



### 4.6 Panel de Gestión de Integraciones (UI Admin)



**Ubicación:** Sidebar → Configuración → Integraciones



**Vista de tokens activos:**

- Lista de tokens con nombre, fecha de creación, último uso, estado

- Botones: Ver Permisos, Revocar, Eliminar



**Crear nuevo token:**

- Formulario con nombre, descripción

- Selector de titulares y cuentas permitidas (checkboxes jerárquicos)

- Al crear: muestra el token **una sola vez** con botón "Copiar" y aviso de que no podrá volver a verlo



**Pestaña de auditoría de integración:**

- Tabla con timestamp, endpoint, código de respuesta, tiempo, IP

- Filtros por token, período, estado

- Métricas: total requests, % exitosos, tiempo promedio



### 4.7 Roadmap Futuro



| Fase | Período | Funcionalidad |

|------|---------|---------------|

| Fase 1 (actual) | — | Lectura: saldos, extractos, gráficas, alertas, auditoría |

| Fase 2 | Q3 2026 | Escritura: `POST /extractos`, `PUT /extractos/{id}` |

| Fase 3 | Q4 2026 | Webhooks: notificaciones push cuando hay cambios |

| Fase 4 | Q1 2027 | Analytics: reportes automáticos, predicciones de flujo de caja |



---



## 5. Schema de Base de Datos



### USUARIOS

```sql

id                      UUID PK DEFAULT gen_random_uuid()

email                   VARCHAR(255) UNIQUE NOT NULL

password_hash           VARCHAR(255) NOT NULL

nombre_completo         VARCHAR(255) NOT NULL

rol                     ENUM('ADMIN','GERENTE','EMPLEADO_ULTRA','EMPLEADO_PLUS','EMPLEADO')

activo                  BOOLEAN DEFAULT true

primer_login            BOOLEAN DEFAULT true   -- fuerza cambio de contraseña

fecha_creacion          TIMESTAMPTZ DEFAULT now()

fecha_ultima_login      TIMESTAMPTZ

failed_login_attempts   INTEGER DEFAULT 0

locked_until            TIMESTAMPTZ

deleted_at              TIMESTAMPTZ

deleted_by_id           UUID FK → USUARIOS

```

**Índices:** `email`, `rol`, `activo`



---



### USUARIO_EMAILS

```sql

id            UUID PK

usuario_id    UUID FK → USUARIOS NOT NULL

email         VARCHAR(255) NOT NULL

es_principal  BOOLEAN DEFAULT false

```

**Índices:** `usuario_id`



---



### REFRESH_TOKENS

```sql

id               UUID PK

usuario_id       UUID FK → USUARIOS NOT NULL

token_hash       VARCHAR(255) UNIQUE NOT NULL

expira_en        TIMESTAMPTZ NOT NULL

creado_en        TIMESTAMPTZ DEFAULT now()

revocado_en      TIMESTAMPTZ

reemplazado_por  VARCHAR(255)

ip_address       INET

```

**Índices:** `usuario_id`, `token_hash`, `expira_en`



---



### TITULARES

```sql

id                UUID PK

nombre            VARCHAR(255) NOT NULL

tipo              ENUM('EMPRESA','PARTICULAR')

identificacion    VARCHAR(50)

contacto_email    VARCHAR(255)

contacto_telefono VARCHAR(50)

notas             TEXT

fecha_creacion    TIMESTAMPTZ DEFAULT now()

deleted_at        TIMESTAMPTZ

deleted_by_id     UUID FK → USUARIOS

```

**Índices:** `nombre`, `tipo`, `deleted_at`



---



### CUENTAS

```sql

id                UUID PK

titular_id        UUID FK → TITULARES NOT NULL

nombre            VARCHAR(255) NOT NULL

numero_cuenta     VARCHAR(100)

iban              VARCHAR(50)

banco_nombre      VARCHAR(255)

divisa            VARCHAR(10) NOT NULL

formato_id        UUID FK → FORMATOS_IMPORTACION (nullable)

es_efectivo       BOOLEAN DEFAULT false

activa            BOOLEAN DEFAULT true

fecha_creacion    TIMESTAMPTZ DEFAULT now()

deleted_at        TIMESTAMPTZ

deleted_by_id     UUID FK → USUARIOS

```

> El saldo actual se obtiene con: `SELECT saldo FROM EXTRACTOS WHERE cuenta_id = ? AND deleted_at IS NULL ORDER BY fila_numero DESC LIMIT 1`



**Índices:** `titular_id`, `divisa`, `es_efectivo`, `activa`, `deleted_at`



---



### FORMATOS_IMPORTACION

```sql

id                  UUID PK

nombre              VARCHAR(255) NOT NULL

banco_nombre        VARCHAR(255)

divisa              VARCHAR(10)

mapeo_json          JSONB NOT NULL

usuario_creador_id  UUID FK → USUARIOS

fecha_creacion      TIMESTAMPTZ DEFAULT now()

activo              BOOLEAN DEFAULT true

deleted_at          TIMESTAMPTZ

deleted_by_id       UUID FK → USUARIOS

```



**Estructura de `mapeo_json` con importe en una columna:**

```json

{

  "tipo_monto": "una_columna",

  "fecha": 0,

  "concepto": 1,

  "monto": 2,

  "saldo": 3,

  "columnas_extra": [

    { "nombre": "Referencia", "indice": 4 },

    { "nombre": "Categoria",  "indice": 5 }

  ]

}

```



**Estructura de `mapeo_json` con ingresos/egresos separados:**

```json

{

  "tipo_monto": "dos_columnas",

  "fecha": 0,

  "concepto": 1,

  "ingreso": 2,

  "egreso": 3,

  "saldo": 4,

  "columnas_extra": [

    { "nombre": "Referencia", "indice": 5 }

  ]

}

```



En ambos modos, `EXTRACTOS.monto` se guarda siempre como importe firmado: ingreso positivo, egreso negativo.



**Estructura de `mapeo_json` con ingresos/egresos y monto de control:**

```json

{

  "tipo_monto": "tres_columnas",

  "fecha": 0,

  "concepto": 1,

  "ingreso": 2,

  "egreso": 3,

  "monto": 4,

  "saldo": 5,

  "columnas_extra": [

    { "nombre": "Referencia", "indice": 6 }

  ]

}

```



En `tres_columnas`, `ingreso`/`egreso` generan el valor final de `EXTRACTOS.monto`. La columna `monto` del banco debe coincidir con ese valor firmado o con su valor absoluto positivo; si no coincide, la fila se rechaza.



---



### EXTRACTOS

```sql

id                       UUID PK

cuenta_id                UUID FK → CUENTAS NOT NULL

fecha                    DATE NOT NULL

concepto                 TEXT

monto                    DECIMAL(18,4) NOT NULL

saldo                    DECIMAL(18,4) NOT NULL

fila_numero              INTEGER NOT NULL

  -- INMUTABLE: MAX(fila_numero)+1 por cuenta al insertar

  -- UNIQUE(cuenta_id, fila_numero)

checked                  BOOLEAN DEFAULT false

checked_at               TIMESTAMPTZ

checked_by_id            UUID FK → USUARIOS

flagged                  BOOLEAN DEFAULT false

flagged_nota             TEXT

flagged_at               TIMESTAMPTZ

flagged_by_id            UUID FK → USUARIOS

usuario_creacion_id      UUID FK → USUARIOS

fecha_creacion           TIMESTAMPTZ DEFAULT now()

usuario_modificacion_id  UUID FK → USUARIOS

fecha_modificacion       TIMESTAMPTZ

deleted_at               TIMESTAMPTZ

deleted_by_id            UUID FK → USUARIOS

```

**Índices (críticos para rendimiento):**

- `(cuenta_id, fila_numero)` UNIQUE

- `(cuenta_id, fecha)`

- `(cuenta_id, deleted_at)`

- `fecha`, `flagged`, `checked`



---



### EXTRACTOS_COLUMNAS_EXTRA

```sql

id              UUID PK

extracto_id     UUID FK → EXTRACTOS NOT NULL

nombre_columna  VARCHAR(100) NOT NULL

valor           TEXT

```

**Índices:** `extracto_id`, `nombre_columna`



---



### PERMISOS_USUARIO

```sql

id                    UUID PK

usuario_id            UUID FK → USUARIOS NOT NULL

cuenta_id             UUID FK → CUENTAS (nullable = permiso global)

titular_id            UUID FK → TITULARES (nullable)

puede_agregar_lineas  BOOLEAN DEFAULT false

puede_editar_lineas   BOOLEAN DEFAULT false

puede_eliminar_lineas BOOLEAN DEFAULT false

puede_importar        BOOLEAN DEFAULT false

puede_ver_dashboard   BOOLEAN DEFAULT false

columnas_visibles     JSONB   -- null = todas

columnas_editables    JSONB   -- null = todas permitidas por rol

```

**Índices:** `(usuario_id, cuenta_id)`, `usuario_id`



---



### ALERTAS_SALDO

```sql

id                   UUID PK

cuenta_id            UUID FK → CUENTAS (nullable = alerta global)

saldo_minimo         DECIMAL(18,4) NOT NULL

activa               BOOLEAN DEFAULT true

fecha_creacion       TIMESTAMPTZ DEFAULT now()

fecha_ultima_alerta  TIMESTAMPTZ

```

> Si `cuenta_id IS NULL` → es la alerta global. Si `cuenta_id IS NOT NULL` → sobreescribe la global para esa cuenta.



---



### ALERTA_DESTINATARIOS

```sql

id          UUID PK

alerta_id   UUID FK → ALERTAS_SALDO NOT NULL

usuario_id  UUID FK → USUARIOS NOT NULL

```



---



### AUDITORIAS

```sql

id                UUID PK

usuario_id        UUID FK → USUARIOS (nullable = acción de sistema/Hangfire)

tipo_accion       VARCHAR(50) NOT NULL

entidad_tipo      VARCHAR(50)

entidad_id        UUID

celda_referencia  VARCHAR(20)

columna_nombre    VARCHAR(100)

valor_anterior    TEXT

valor_nuevo       TEXT

timestamp         TIMESTAMPTZ DEFAULT now()

ip_address        INET

detalles_json     JSONB

```



**Valores de `tipo_accion`:** `EDIT_CELDA`, `CREATE_EXTRACTO`, `DELETE_EXTRACTO`, `RESTORE_EXTRACTO`, `IMPORT_EXTRACTOS`, `CHECK_FILA`, `FLAG_FILA`, `CREATE_USUARIO`, `UPDATE_USUARIO`, `DELETE_USUARIO`, `RESTORE_USUARIO`, `CREATE_CUENTA`, `UPDATE_CUENTA`, `DELETE_CUENTA`, `RESTORE_CUENTA`, `CREATE_TITULAR`, `UPDATE_TITULAR`, `DELETE_TITULAR`, `RESTORE_TITULAR`, `CAMBIO_PERMISOS`, `CREATE_FORMATO`, `UPDATE_FORMATO`, `DELETE_FORMATO`, `LOGIN`, `LOGOUT`, `LOGIN_FAILED`, `ACCOUNT_LOCKED`, `CONFIG_ALERTA`, `CONFIG_SISTEMA`, `BACKUP_CREADO`, `BACKUP_RESTAURADO`, `EXPORTACION_MANUAL`, `EXPORTACION_AUTO`, `SYNC_DIVISAS`, `UPDATE_DIVISA_MANUAL`



**Índices:** `(usuario_id, timestamp)`, `tipo_accion`, `entidad_id`, `timestamp`



---



### INTEGRATION_TOKENS

```sql

id                  UUID PK

token_hash          VARCHAR(255) UNIQUE NOT NULL  -- SHA-256 del token real

nombre              VARCHAR(100) NOT NULL

descripcion         TEXT

tipo                VARCHAR(50) DEFAULT 'openclaw'

estado              ENUM('activo','revocado') DEFAULT 'activo'

permiso_lectura     BOOLEAN DEFAULT true

permiso_escritura   BOOLEAN DEFAULT false         -- para Fase 2 futura

fecha_creacion      TIMESTAMPTZ DEFAULT now()

fecha_ultima_uso    TIMESTAMPTZ

fecha_revocacion    TIMESTAMPTZ

usuario_creador_id  UUID FK → USUARIOS NOT NULL

deleted_at          TIMESTAMPTZ

deleted_by_id       UUID FK → USUARIOS

```

**Índices:** `token_hash`, `estado`



---



### INTEGRATION_PERMISSIONS

```sql

id           UUID PK

token_id     UUID FK → INTEGRATION_TOKENS NOT NULL

titular_id   UUID FK → TITULARES (nullable)

cuenta_id    UUID FK → CUENTAS (nullable)

acceso_tipo  VARCHAR(50) NOT NULL DEFAULT 'lectura'

fecha_creacion TIMESTAMPTZ DEFAULT now()

```

> Si `titular_id` tiene valor y `cuenta_id` es null → acceso a todas las cuentas del titular.

> Si ambos tienen valor → acceso solo a esa cuenta específica.



**Índices:** `token_id`, `titular_id`, `cuenta_id`



---



### AUDITORIA_INTEGRACIONES

```sql

id                  UUID PK

token_id            UUID FK → INTEGRATION_TOKENS NOT NULL

endpoint            VARCHAR(255) NOT NULL

metodo              VARCHAR(10) NOT NULL

parametros          JSONB

codigo_respuesta    INTEGER

timestamp           TIMESTAMPTZ DEFAULT now()

ip_address          INET

tiempo_ejecucion_ms INTEGER

```

**Índices:** `token_id`, `timestamp`, `codigo_respuesta`



---



### TIPOS_CAMBIO

```sql

id                  UUID PK

divisa_origen       VARCHAR(10) NOT NULL

divisa_destino      VARCHAR(10) NOT NULL

tasa                DECIMAL(18,8) NOT NULL

fecha_actualizacion TIMESTAMPTZ DEFAULT now()

fuente              ENUM('API','MANUAL')

UNIQUE(divisa_origen, divisa_destino)

```



---



### DIVISAS_ACTIVAS

```sql

codigo    VARCHAR(10) PK

nombre    VARCHAR(100)

simbolo   VARCHAR(5)

activa    BOOLEAN DEFAULT true

es_base   BOOLEAN DEFAULT false  -- solo una puede ser true (EUR por defecto)

```



---



### CONFIGURACION

```sql

clave                    VARCHAR(100) PK

valor                    TEXT NOT NULL

tipo                     VARCHAR(20)

descripcion              TEXT

fecha_modificacion       TIMESTAMPTZ

usuario_modificacion_id  UUID FK → USUARIOS

```



**Claves iniciales (seed):**

| Clave | Valor inicial |

|-------|---------------|

| `app_base_url` | `https://caja.empresa.local` |

| `saldo_minimo_global` | `0` |

| `exchange_rate_sync_hours` | `12` |

| `backup_retention_weeks` | `6` |

| `backup_path` | `C:/AtlasBalance/backups` |

| `export_path` | `C:/AtlasBalance/exports` |

| `app_version` | `1.0.0` |

| `app_update_check_url` | *(URL del servidor de actualizaciones)* |

| `smtp_host` | *(configurable por admin)* |

| `smtp_port` | `587` |

| `smtp_user` | *(configurable por admin)* |

| `smtp_password` | *(cifrado)* |

| `smtp_from` | `noreply@empresa.com` |

| `divisa_principal_default` | `EUR` |

| `dashboard_color_ingresos` | `#43B430` |

| `dashboard_color_egresos` | `#FF4757` |

| `dashboard_color_saldo` | `#7B7B7B` |

| `integration_rate_limit_per_minute` | `100` |



---



### BACKUPS

```sql

id               UUID PK

fecha_creacion   TIMESTAMPTZ DEFAULT now()

ruta_archivo     VARCHAR(500) NOT NULL

tamanio_bytes    BIGINT

estado           ENUM('PENDING','SUCCESS','FAILED')

tipo             ENUM('AUTO','MANUAL')

iniciado_por_id  UUID FK → USUARIOS (nullable para auto)

notas            TEXT

deleted_at       TIMESTAMPTZ

deleted_by_id    UUID FK → USUARIOS

```



---



### EXPORTACIONES

```sql

id                UUID PK

cuenta_id         UUID FK → CUENTAS NOT NULL

fecha_exportacion TIMESTAMPTZ DEFAULT now()

ruta_archivo      VARCHAR(500)

tamanio_bytes     BIGINT

estado            ENUM('PENDING','SUCCESS','FAILED')

tipo              ENUM('AUTO','MANUAL')

iniciado_por_id   UUID FK → USUARIOS (nullable para auto)

deleted_at        TIMESTAMPTZ

deleted_by_id     UUID FK → USUARIOS

```



---



### NOTIFICACIONES_ADMIN

```sql

id             UUID PK

tipo           VARCHAR(50)

mensaje        TEXT

leida          BOOLEAN DEFAULT false

fecha          TIMESTAMPTZ DEFAULT now()

detalles_json  JSONB

```



---



## 6. API Endpoints



> **Paginación:** todos los endpoints con listas aceptan `?page=1&pageSize=100` y devuelven `{ data, total, page, pageSize, totalPages }`

> **Ordenación:** `?sortBy=campo&sortDir=asc|desc`



### Autenticación

```

POST   /api/auth/login

POST   /api/auth/logout

POST   /api/auth/refresh-token

GET    /api/auth/me

PUT    /api/auth/cambiar-password

```



### Usuarios (solo Admin)

```

GET    /api/usuarios

POST   /api/usuarios

GET    /api/usuarios/{id}

PUT    /api/usuarios/{id}

DELETE /api/usuarios/{id}

POST   /api/usuarios/{id}/restaurar

PUT    /api/usuarios/{id}/permisos

GET    /api/usuarios/{id}/permisos

PUT    /api/usuarios/{id}/permisos/cuenta/{cuentaId}

GET    /api/usuarios/{id}/emails

POST   /api/usuarios/{id}/emails

DELETE /api/usuarios/{id}/emails/{emailId}

```



### Titulares

```

GET    /api/titulares

POST   /api/titulares

GET    /api/titulares/{id}

PUT    /api/titulares/{id}

DELETE /api/titulares/{id}

POST   /api/titulares/{id}/restaurar

GET    /api/titulares/{id}/cuentas

GET    /api/titulares/{id}/dashboard

```



### Cuentas

```

GET    /api/cuentas

POST   /api/cuentas

GET    /api/cuentas/{id}

PUT    /api/cuentas/{id}

DELETE /api/cuentas/{id}

POST   /api/cuentas/{id}/restaurar

GET    /api/cuentas/{id}/extractos

GET    /api/cuentas/{id}/resumen

```



### Extractos

```

GET    /api/extractos

POST   /api/extractos

PUT    /api/extractos/{id}

DELETE /api/extractos/{id}

POST   /api/extractos/{id}/restaurar

PUT    /api/extractos/{id}/check

PUT    /api/extractos/{id}/flag

GET    /api/extractos/{id}/auditoria

GET    /api/extractos/celda/{cuentaId}/{filaNumero}/{columna}

```



### Importación

```

POST   /api/importacion/validar

POST   /api/importacion/confirmar

GET    /api/formatos

POST   /api/formatos

GET    /api/formatos/{id}

PUT    /api/formatos/{id}

DELETE /api/formatos/{id}

```



### Dashboards

```

GET    /api/dashboard/principal

GET    /api/dashboard/titular/{id}

GET    /api/dashboard/evolucion?periodo=1m|3m|6m|9m|12m|18m|24m&titular_id=X

GET    /api/dashboard/saldos-divisa

GET    /api/dashboard/ingresos-egresos?periodo=1m|3m|6m|9m|12m|18m|24m

```



### Alertas

```

GET    /api/alertas

POST   /api/alertas

PUT    /api/alertas/{id}

DELETE /api/alertas/{id}

GET    /api/alertas/activas

```



### Tipos de Cambio y Divisas

```

GET    /api/tipos-cambio

PUT    /api/tipos-cambio/{origen}/{destino}

POST   /api/tipos-cambio/sincronizar

GET    /api/divisas

POST   /api/divisas

PUT    /api/divisas/{codigo}

```



### Auditoría (solo Admin)

```

GET    /api/auditoria

GET    /api/auditoria/exportar

```



### Backups (solo Admin)

```

GET    /api/backups

POST   /api/backups/manual

POST   /api/backups/{id}/restaurar

DELETE /api/backups/{id}

```



### Exportaciones

```

GET    /api/exportaciones

POST   /api/exportaciones/manual?cuenta_id=X

GET    /api/exportaciones/{id}/descargar

```



### Configuración (solo Admin)

```

GET    /api/configuracion

PUT    /api/configuracion/{clave}

GET    /api/notificaciones-admin

PUT    /api/notificaciones-admin/{id}/leer

PUT    /api/notificaciones-admin/leer-todas

```



### Sistema y Actualizaciones (solo Admin)

```

GET    /api/sistema/version-actual

GET    /api/sistema/version-disponible

POST   /api/sistema/actualizar

GET    /api/sistema/estado

```



### Papelera (solo Admin)

```

GET    /api/papelera/titulares

GET    /api/papelera/cuentas

GET    /api/papelera/extractos

GET    /api/papelera/usuarios

POST   /api/papelera/{entidad}/{id}/restaurar

```



### Integración OpenClaw (Bearer Token)

```

GET    /api/integration/openclaw/saldos

GET    /api/integration/openclaw/extractos

GET    /api/integration/openclaw/grafica-evolucion

GET    /api/integration/openclaw/alertas

GET    /api/integration/openclaw/auditoria

GET    /api/integration/openclaw/titulares

```



### Gestión de Tokens de Integración (solo Admin)

```

GET    /api/integraciones/tokens

POST   /api/integraciones/tokens

GET    /api/integraciones/tokens/{id}

PUT    /api/integraciones/tokens/{id}

POST   /api/integraciones/tokens/{id}/revocar

DELETE /api/integraciones/tokens/{id}

GET    /api/integraciones/tokens/{id}/auditoria

```



---



## 7. Arquitectura Frontend



### 7.1 Stores Zustand



| Store | Responsabilidad |

|-------|----------------|

| `authStore` | usuario actual, token, expiración, logout |

| `permisosStore` | mapa cuentas/columnas accesibles para el usuario |

| `uiStore` | theme, sidebar collapse, modal activo, toasts |

| `divisaStore` | divisas activas, divisa principal seleccionada, tasas |

| `alertasStore` | cuentas bajo mínimo, badge count sidebar |



### 7.2 Páginas



```

LoginPage

DashboardPage                  -- global

TitularesPage                  -- lista de titulares

TitularDetailPage              -- dashboard del titular + cuentas

CuentasPage                    -- lista de cuentas

CuentaDetailPage               -- tabla Excel-like + KPIs + acciones

ExtractosPage                  -- vista unificada

ImportacionPage                -- wizard 4 pasos

AlertasPage                    -- configuración + activas

ExportacionesPage              -- historial + exportación manual

UsuariosPage                   -- CRUD (solo Admin)

AuditoriaPage                  -- log completo + pestaña integraciones (Admin)

ConfiguracionPage              -- SMTP, URL, divisas, backups, integraciones (Admin)

BackupsPage                    -- historial + restaurar (solo Admin)

PapeleraPage                   -- entidades eliminadas + restaurar (Admin)

NotFoundPage

```



### 7.3 Componentes Principales



```

Layout/

  Sidebar

  TopBar

  PermissionGuard

  AlertBanner



ExtractoTable/

  VirtualizedGrid

  EditableCell

  ColumnHeader

  RowCheckbox

  RowFlag

  AuditCellModal

  ColumnVisibilityPanel

  AddRowForm



ImportWizard/

  Step1_Paste

  Step2_MapColumns

  Step3_Preview

  Step4_Confirm



Dashboard/

  KPICard

  SaldoPorDivisaCard

  EvolucionChart

  IngresoEgresoBar

  DivisaSelector



Integraciones/

  TokenList

  CreateTokenModal

  TokenCreatedModal      -- muestra el token una sola vez

  TokenPermissionsEditor

  IntegrationAuditTable



Shared/

  ConfirmModal

  SoftDeleteBadge

  CurrencyBadge

  RoleBadge

  ToastNotification

  LoadingOverlay

  EmptyState

```



### 7.4 Navegación (sidebar)



```

Dashboard

Titulares

Cuentas

Extractos

Importación

Alertas

Exportaciones

─────────────── (solo Admin)

Usuarios

Auditoría

Papelera

Configuración

  └── Integraciones (subtab)

```



---



## 8. Arquitectura Backend



### 8.1 Servicios



```

ExtractoService

ImportacionService

AlertaService

DashboardService

TiposCambioService

PermisoService

AuditoriaService

EmailService

BackupService

ExportacionService

ActualizacionService

ConfiguracionService

IntegrationTokenService          -- gestión de tokens OpenClaw

IntegrationAuthorizationService  -- filtrado de datos por permisos del token

```



### 8.2 Middleware



```

AuthenticationMiddleware         -- valida JWT de cookie httpOnly (usuarios)

IntegrationAuthMiddleware        -- valida Bearer token (integración OpenClaw)

PermissionMiddleware             -- permisos por cuenta para usuarios

RateLimitMiddleware              -- login: 5 intentos; integración: 100 req/min

AuditMiddleware                  -- registra cambios automáticamente

ErrorHandlingMiddleware          -- respuestas de error consistentes

CsrfMiddleware                   -- valida X-CSRF-Token en mutaciones de usuarios

```



### 8.3 Background Jobs (Hangfire)



| Job | Frecuencia |

|-----|-----------|

| `SyncTiposCambioJob` | cada 12 horas |

| `BackupWeeklyJob` | cada domingo a las 02:00 |

| `ExportMensualJob` | día 1 de cada mes a las 01:00 |

| `LimpiezaRefreshTokensJob` | cada día |



### 8.4 Watchdog Service (proceso independiente)



- Proyecto `GestionCaja.Watchdog` como Windows Service separado

- Puerto `localhost:5001` (NUNCA expuesto a la red)

- `POST /watchdog/restaurar-backup`: para API → `pg_restore` → reinicia API

- `POST /watchdog/actualizar-app`: para API → reemplaza binarios → reinicia API

- Escribe estado en `watchdog-state.json` leído por el backend principal



---



## 9. Stack Tecnológico y Dependencias



### Frontend (npm)



```

react@18

react-dom@18

typescript@5

react-router-dom@6

zustand@4

axios@1.6

recharts@2

react-virtual@2          → CORREGIDO: usar @tanstack/react-virtual

react-hook-form@7

vite@5

@vitejs/plugin-react@4

@types/react@18

@types/node@20

eslint@8

prettier@3

```



### Backend (.NET NuGet)



```

Microsoft.AspNetCore.Authentication.JwtBearer

Microsoft.EntityFrameworkCore@8

Microsoft.EntityFrameworkCore.Design@8

Npgsql.EntityFrameworkCore.PostgreSQL@8

FluentValidation.AspNetCore@11

Hangfire.AspNetCore@1.8

Hangfire.PostgreSql@1.8      storage de jobs en la misma BD PostgreSQL

MailKit@4

MimeKit@4

ClosedXML@0.102              generación XLSX — MIT license, gratuito sin restricciones

                             (reemplaza EPPlus que requiere licencia comercial de pago)

BCrypt.Net-Next@4

Serilog.AspNetCore@7

```



### Base de Datos

```

PostgreSQL 14+

pgAdmin 4 (opcional, herramienta admin visual)

Entity Framework Migrations

```



### DevOps y Herramientas

```

Git

GitHub / GitLab

Docker + Docker Compose   (solo para entorno de desarrollo)

mkcert                    (certificados HTTPS locales)

Windows Service           (despliegue en producción)

```



### Integraciones Externas

```

ExchangeRate-API          tipos de cambio — plan gratuito (1.500 req/mes)

SMTP del cliente          correos de alerta vía MailKit

OpenClaw main agent       integración de lectura de datos financieros

```



---



## 10. Instalación y Requisitos



### 10.1 Requisitos del Servidor



| Componente | Mínimo | Recomendado |

|-----------|--------|-------------|

| SO | Windows Server 2016+ o Win 10/11 Pro | Windows Server 2022 |

| CPU | 2 cores | 4 cores |

| RAM | 8 GB | 16 GB |

| Almacenamiento | 100 GB SSD | 500 GB SSD |

| Red | Ethernet, IP estática | Gigabit Ethernet |

| Puerto | 443 (HTTPS) accesible en red local | — |



### 10.2 Requisitos de Clientes



- Windows 10+, macOS, Linux

- Chrome 90+, Edge 90+, Firefox 88+

- Conexión a red local del servidor

- Sin instalación requerida (web local)



### 10.3 Software a Instalar en el Servidor



**1. PostgreSQL 14+**

```

Descargar: https://www.postgresql.org/download/windows/

- Ejecutar instalador

- Contraseña para usuario 'postgres'

- Puerto: 5432 (por defecto)

- Timezone: UTC

- Verificar: psql --version

```



**2. .NET 8 SDK**

```

Descargar: https://dotnet.microsoft.com/download/dotnet/8.0

- Descargar SDK (no solo runtime)

- Reiniciar Windows después de instalar

- Verificar: dotnet --version

```



**3. Node.js 18+ LTS**

```

Descargar: https://nodejs.org/

- Solo necesario para build del frontend

- Verificar: node --version && npm --version

```



**4. Git**

```

Descargar: https://git-scm.com/download/win

- Opciones por defecto

- Verificar: git --version

```



**5. mkcert**

```

Descargar: https://github.com/FiloSottile/mkcert/releases

- Descargar mkcert-windows-amd64.exe

- Renombrar a mkcert.exe, añadir al PATH

- Verificar: mkcert --version

```



**6. pgAdmin 4 (opcional)**

```

Descargar: https://www.pgadmin.org/download/

```



### 10.4 Proceso de Instalación Completo



**Paso 1 — Configurar HTTPS local**

```powershell

# Ejecutar como administrador en el servidor

mkcert -install

mkcert caja.empresa.local localhost 127.0.0.1



# Copiar certificados generados a:

# C:/AtlasBalance/certs/



# Añadir al archivo hosts del servidor:

# C:/Windows/System32/drivers/etc/hosts

# 127.0.0.1    caja.empresa.local



# En cada máquina cliente, ejecutar:

# ./scripts/install-cert-client.ps1

# Y añadir al hosts del cliente:

# {IP_DEL_SERVIDOR}    caja.empresa.local

```



**Paso 2 — Preparar Base de Datos**

```sql

createdb gestion_caja

createuser app_user --pwprompt

GRANT ALL PRIVILEGES ON DATABASE gestion_caja TO app_user;

```



**Paso 3 — Preparar Backend**

```bash

git clone <repo-url>

cd backend/src/GestionCaja.API



# Editar appsettings.Production.json:

# - ConnectionStrings.DefaultConnection

# - JwtSettings.Secret (mínimo 32 caracteres)

# - CertificatePath / CertificateKeyPath



dotnet restore

dotnet ef database update    # ejecuta migraciones + seed inicial

dotnet build --configuration Release

```



**Paso 4 — Preparar Frontend**

```bash

cd frontend

npm install

npm run build

# El build se copia automáticamente a /backend/wwwroot/

```



**Paso 5 — Instalar Windows Services**

```powershell

# Ejecutar como administrador:

./scripts/install-services.ps1



# Instala y configura:

# - GestionCaja.API (puerto 443, arranque automático, reinicio si falla)

# - GestionCaja.Watchdog (localhost:5001, arranque automático)

```



**Paso 6 — Verificar instalación**

```

1. Abrir https://caja.empresa.local en el servidor

2. Sin advertencias de seguridad = certificado correcto

3. Login con el admin inicial generado o configurado durante la instalacion.

4. El sistema forzará cambio de contraseña en primer acceso

5. Configurar SMTP en Configuración

6. Crear titulares, cuentas y usuarios

7. Generar primer token de integración si se usa OpenClaw

```



### 10.5 Variables de Entorno y Configuración



**Backend — `appsettings.Production.json`:**

```json

{

  "ConnectionStrings": {

    "DefaultConnection": "Host=localhost;Database=gestion_caja;User Id=app_user;Password=XXXX"

  },

  "JwtSettings": {

    "Secret": "clave-secreta-minimo-32-caracteres",

    "AccessTokenExpMinutes": 60,

    "RefreshTokenExpDays": 7

  },

  "CertificatePath": "C:/AtlasBalance/certs/caja.empresa.local+2.pem",

  "CertificateKeyPath": "C:/AtlasBalance/certs/caja.empresa.local+2-key.pem",

  "WatchdogBaseUrl": "http://localhost:5001"

}

```



**Frontend — `.env.production`:**

```

VITE_API_URL=https://caja.empresa.local

```



> El resto de configuración (SMTP, rutas de backup/export, URL base, colores del dashboard, rate limits de integración, etc.) se gestiona desde la tabla `CONFIGURACION` en BD, accesible para el admin desde la UI en ConfiguracionPage.



### 10.6 Scripts Incluidos



```

/scripts/setup-https.ps1           configura mkcert + dominio local en servidor

/scripts/install-cert-client.ps1   instala CA cert en máquina cliente

/scripts/install-services.ps1      instala ambos Windows Services

/scripts/uninstall-services.ps1    desinstala los servicios

/scripts/backup-manual.ps1         backup manual desde línea de comandos

/scripts/restore-backup.ps1        restauración desde línea de comandos

```



---



## 11. Plan de Ejecución — Fases de Desarrollo



**Herramienta:** Claude Code

**Estimación total:** ~50 días de desarrollo a jornada completa

**Metodología:** Una fase a la vez, sin pasar a la siguiente sin que la anterior funcione



---



### Fase 0 — Scaffolding e Infraestructura (4 días)



**Días 1-2: Backend**

- Crear solución `GestionCaja.sln` con dos proyectos: `API` y `Watchdog`

- Configurar Windows Service con `UseWindowsService()` en ambos

- Kestrel configurado para HTTPS + archivos estáticos

- Definir TODOS los EF Core models (todas las tablas del schema)

- Migración inicial con todas las tablas e índices

- Seed: admin por defecto, 4 divisas, configuración inicial, tipos de cambio base

- Docker Compose para PostgreSQL en desarrollo local

- Hangfire configurado con storage en PostgreSQL



**Días 3-4: Frontend**

- Proyecto Vite + React 18 + TypeScript

- Instalar todas las dependencias

- Layout shell: Sidebar, TopBar, páginas placeholder

- React Router v6 con todas las rutas

- Todos los stores Zustand (vacíos pero con estructura)

- CSS Variables base: dark/light mode, tipografía, spacing

- Scripts: `setup-https.ps1`, `install-services.ps1`



> **Entregable:** App carga en `https://caja.empresa.local`, layout visible, BD creada con schema completo.



---



### Fase 1 — Autenticación y Usuarios (5 días)



**Días 5-6: Backend Auth**

- `POST /api/auth/login` con rate limiting (5 intentos → bloqueo 30min)

- JWT en httpOnly cookie + CSRF token

- Refresh token con rotación, tabla `REFRESH_TOKENS`

- `POST /api/auth/logout` (revoca refresh en BD)

- Primer login fuerza cambio de contraseña



**Días 7-8: CRUD Usuarios + Permisos**

- Endpoints CRUD con soft delete y restauración

- `PermisoService` completo

- Gestión de emails adicionales (`USUARIO_EMAILS`)



**Día 9: Frontend Auth + Usuarios**

- LoginPage con validación

- `authStore` + `permisosStore` funcionales

- Route guards por rol

- Flujo primer login obligatorio

- UsuariosPage: CRUD completo + modal de permisos + emails de notificación



> **Entregable:** Login funcional, JWT en cookies, admin puede crear usuarios y asignar permisos.



---



### Fase 2 — Titulares y Cuentas (3 días)



**Día 10: Backend**

- CRUD Titulares con soft delete

- CRUD Cuentas (bancarias y efectivo) con soft delete

- `GET /api/cuentas/{id}/resumen`

- CRUD Formatos de Importación



**Días 11-12: Frontend**

- TitularesPage: cards + crear/editar/eliminar

- CuentasPage: lista, crear/editar con selector de divisa, checkbox `es_efectivo`

- FormatsPage: creator de formato con columnas base + columnas extra



> **Entregable:** Admin puede crear titulares, cuentas y formatos de importación.



---



### Fase 3 — Extractos (8 días) — Fase más crítica



**Días 13-14: Backend**

- CRUD con filtros, paginación y ordenación

- `fila_numero` inmutable (MAX+1 por cuenta, UNIQUE constraint)

- Soft delete + restauración

- Toggle check y flag con auditoría

- Auditoría por celda: cada campo modificado → entrada con valor antes/después

- Columnas extra via `EXTRACTOS_COLUMNAS_EXTRA`



**Días 15-16: ExtractoTable — base**

- Virtualización con `react-virtual`

- Columnas fijas + columnas extra dinámicas

- Sort, filter inline, paginación

- Panel de visibilidad de columnas (persistido en BD)

- Fusión de columnas extra en vista unificada



**Días 17-18: ExtractoTable — edición**

- `EditableCell`: doble clic → Enter confirma, Esc cancela

- Solo columnas autorizadas editables

- Checkbox y Flag por fila con visual

- `AuditCellModal`: historial completo de una celda

- Formulario de nueva fila



**Días 19-20: Vistas completas**

- `ExtractosPage`: vista unificada con columnas Titular + Cuenta + Divisa

- `TitularDetailPage`: tabs por titular

- `CuentaDetailPage`: KPIs + tabla + botones de acción



> **Entregable:** Tabla de extractos completamente funcional con edición, checkbox, flag, auditoría y vistas múltiples.



---



### Fase 4 — Importación (4 días)



**Días 21-22: Backend**

- `POST /api/importacion/validar`: retorna `{ filas_ok, filas_error, resumen }`

- `POST /api/importacion/confirmar`: bulk insert + auditoría

- Detección automática de separador

- Parseo de múltiples formatos de fecha

- Mapeo de columnas extra

- Funciona igual para cuentas bancarias y efectivo



**Días 23-24: Frontend Wizard**

- Step 1: textarea + preview tiempo real

- Step 2: mapeo con preload de formato + columnas extra dinámicas

- Step 3: tabla ✓/✗, errores en rojo, mensajes específicos

- Step 4: resumen + confirmación + feedback



> **Entregable:** Importación completa con validación visual y manejo de errores.



---



### Fase 5 — Dashboards (6 días)



**Días 25-26: Backend**

- `GET /api/dashboard/principal`

- `GET /api/dashboard/evolucion` (períodos 1m/3m/6m/9m/12m/18m/24m, diario vs semanal)

- `GET /api/dashboard/titular/{id}`

- Conversión de divisas via `TiposCambioService`



**Días 27-28: Frontend Dashboard Principal**

- KPI cards, `SaldoPorDivisaCard`, `DivisaSelector`

- Tabla de saldos por titular

- `EvolucionChart`: tres líneas, selector de período, colores desde CONFIGURACION



**Días 29-30: Frontend Dashboard Titular**

- Mismo layout filtrado por titular

- Tabs de cuentas con saldo individual

- Control de acceso: si `puede_ver_dashboard = false` → redirige



> **Entregable:** Dashboards global y por titular con gráficas y conversión multi-divisa.



---



### Fase 6 — Tipos de Cambio (2 días)



**Días 31-32:**

- `TiposCambioService`: ExchangeRate-API + cache + fallback

- Hangfire `SyncTiposCambioJob` cada 12h

- Hangfire `LimpiezaRefreshTokensJob` cada día

- ConfiguracionPage (parcial): tasas actuales, sync manual, edición manual, añadir divisa

- Indicador visual si tasas tienen >24h de antigüedad



> **Entregable:** Tipos de cambio funcionando con sync automático y gestión desde UI.



---



### Fase 7 — Alertas de Saldo Bajo (3 días)



**Días 33-34: Backend**

- `AlertaService.EvaluarSaldoPost()` llamado automáticamente en cada Create/Update de extracto

- `EmailService` con MailKit: template HTML con link a la cuenta

- Lectura de `app_base_url` desde CONFIGURACION para construir el link



**Día 35: Frontend**

- AlertasPage: crear/editar/eliminar alertas + asignación de destinatarios + saldo mínimo global

- `alertasStore`: carga cuentas bajo mínimo al iniciar

- `AlertBanner`: banner top dismissible

- Badge en sidebar



> **Entregable:** Alertas automáticas por email y notificación in-app.



---



### Fase 8 — Auditoría UI (2 días)



**Días 36-37:**

- AuditoriaPage: tabla paginada con todos los filtros

- Expandir fila para ver valor antes/después

- Referencia de celda legible (A1 = Fecha, B1 = Concepto...)

- Exportar CSV con filtros aplicados



> **Entregable:** Admin ve el historial completo de todos los cambios y puede exportarlo.



---



### Fase 9 — Backups, Exportaciones y Watchdog (5 días)



**Días 38-39: Watchdog Service**

- Proyecto `GestionCaja.Watchdog` completo como Windows Service

- `POST /watchdog/restaurar-backup`: para API → pg_restore → reinicia API

- `POST /watchdog/actualizar-app`: para → reemplaza → reinicia

- `GET /api/sistema/estado` en backend principal para polling



**Días 40-41: Backups**

- Hangfire `BackupWeeklyJob`: pg_dump, naming, limpieza de retención

- BackupsPage: lista + backup manual + restaurar con ConfirmModal doble

- `LoadingOverlay` con polling durante restauración → redirección al login



**Día 42: Exportaciones**

- Hangfire `ExportMensualJob`: XLSX con ClosedXML

- `POST /api/exportaciones/manual`: generación on-demand

- ExportacionesPage: historial + descarga + botón manual por cuenta

- Notificaciones admin en sidebar



> **Entregable:** Backups automáticos, restauración desde UI, exportaciones mensuales y manuales.



---



### Fase 10 — Actualización de App (2 días)



**Días 43-44:**

- `ActualizacionService.CheckVersionDisponible()`

- Badge en sidebar para admin

- Flujo completo: botón → Watchdog → polling → login con mensaje

- Migraciones automáticas al reiniciar



> **Entregable:** Admin actualiza la app desde la UI con un clic.



---



### Fase 11 — Papelera y Configuración Completa (2 días)



**Días 45-46:**

- `PapeleraPage`: tabs por entidad, botón restaurar, auditoría de restauración

- `ConfiguracionPage` completa:

  - SMTP: todos los campos + botón "Enviar email de prueba"

  - URL base, rutas backup/export

  - Divisas activas + añadir nueva

  - Tipos de cambio

  - Colores del dashboard

  - Sistema: versión, actualizar

  - **Integraciones**: gestión de tokens OpenClaw (incluida en esta sección)



> **Entregable:** Papelera funcional, configuración completa del sistema, interfaz de gestión de tokens OpenClaw.



---



### Fase 12 — Integración OpenClaw (3 días)



**Días 47-48: Backend**

- Tablas: `INTEGRATION_TOKENS`, `INTEGRATION_PERMISSIONS`, `AUDITORIA_INTEGRACIONES`

- `IntegrationAuthMiddleware`: valida Bearer token, hashea con SHA-256 para comparar

- `IntegrationAuthorizationService`: filtra datos por permisos del token

- `IntegrationTokenService`: generación, validación, revocación

- Todos los endpoints `GET /api/integration/openclaw/*`

- Rate limiting: 100 req/min por token → 429

- Registro automático de cada request en `AUDITORIA_INTEGRACIONES`



**Día 49: Frontend + Gestión de Tokens**

- `TokenList`, `CreateTokenModal`, `TokenCreatedModal` (muestra token una sola vez)

- `TokenPermissionsEditor`: selector jerárquico titular/cuenta

- `IntegrationAuditTable` en AuditoriaPage (nueva pestaña)

- Métricas del token: requests totales, % exitosos, tiempo promedio



> **Entregable:** OpenClaw puede consultar datos financieros en tiempo real. Admin gestiona tokens desde la UI.



---



### Fase 13 — Polish, Seguridad y Responsive (1 día)



**Día 50:**

- Dark/light mode: verificar todos los componentes

- Responsive: tablet (sidebar colapsa), mobile (bottom nav)

- Revisar CSRF en todos los endpoints de mutación

- Verificar índices de BD en migraciones

- Error boundaries en React

- Mensajes de error consistentes (toast notifications)

- Estados vacíos bien diseñados

- Página 404, skeleton loaders

- Verificar que datos sensibles no aparecen en logs

- Verificar que token de integración nunca aparece en logs (solo el hash)



> **Entregable:** App completamente pulida, segura, responsive y lista para producción.



---



### Resumen de Fases



| Fase | Descripción | Días |

|------|-------------|------|

| 0 | Scaffolding e Infraestructura | 4 |

| 1 | Auth y Usuarios | 5 |

| 2 | Titulares y Cuentas | 3 |

| 3 | Extractos (Tabla Excel-like) | **8** |

| 4 | Importación | 4 |

| 5 | Dashboards | 6 |

| 6 | Tipos de Cambio | 2 |

| 7 | Alertas de Saldo Bajo | 3 |

| 8 | Auditoría UI | 2 |

| 9 | Backups, Exportaciones y Watchdog | 5 |

| 10 | Actualización de App | 2 |

| 11 | Papelera y Configuración Completa | 2 |

| **12** | **Integración OpenClaw** | **3** |

| 13 | Polish, Seguridad y Responsive | 1 |

| **TOTAL** | | **50 días** |



---



## 12. Matriz de Permisos



|  | ADMIN | GERENTE | EMP.ULTRA | EMP.PLUS | EMPLEADO |

|--|-------|---------|-----------|----------|---------|

| Ver extractos (cuentas asignadas) | ✅ | ✅* | ✅* | ✅* | ✅* |

| Agregar filas nuevas | ✅ | ✅* | ✅* | ✅* | ❌ |

| Editar filas existentes | ✅ | ✅* | ✅* | ❌ | ❌ |

| Eliminar filas | ✅ | ✅* | ❌ | ❌ | ❌ |

| Checkbox / Flag por fila | ✅ | ✅ | ✅ | ✅ | ✅ |

| Ver dashboard | ✅ | Si auth | ❌ | ❌ | ❌ |

| Exportar datos | ✅ | ✅ | ❌ | ❌ | ❌ |

| Importar extractos | ✅ | ✅ | ✅* | ❌ | ❌ |

| Gestión usuarios | ✅ | ❌ | ❌ | ❌ | ❌ |

| Gestión titulares/cuentas | ✅ | ❌ | ❌ | ❌ | ❌ |

| Gestión formatos importación | ✅ | ❌ | ❌ | ❌ | ❌ |

| Ver auditoría completa | ✅ | ❌ | ❌ | ❌ | ❌ |

| Configurar alertas | ✅ | ❌ | ❌ | ❌ | ❌ |

| Backups / Restauración | ✅ | ❌ | ❌ | ❌ | ❌ |

| Gestión tokens integración | ✅ | ❌ | ❌ | ❌ | ❌ |

| Configuración del sistema | ✅ | ❌ | ❌ | ❌ | ❌ |

| Papelera / Restaurar | ✅ | ❌ | ❌ | ❌ | ❌ |

| Recibir alertas email | ✅ siempre | Si asig. | Si asig. | Si asig. | Si asig. |



> \* Solo cuentas/columnas autorizadas por el admin para ese usuario



---



## 13. Registro de Decisiones de Diseño



| # | Decisión | Razón |

|---|----------|-------|

| 1 | **CSS Variables propio** en lugar de Tailwind | Control total sobre theming dark/light, consistencia con design system |

| 2 | **Zustand** en lugar de Context API | Complejidad de permisos granulares, multi-divisa y estado cross-componente hace Context inmanejable |

| 3 | **ClosedXML** en lugar de EPPlus | EPPlus v5+ requiere licencia comercial de pago. ClosedXML es MIT, gratuito sin restricciones |

| 4 | **saldo_minimo_alerta** eliminado de CUENTAS | Vive exclusivamente en `ALERTAS_SALDO`. Elimina duplicación. Alerta global via `cuenta_id = null` |

| 5 | **USUARIO_EMAILS** tabla separada | Reemplaza array en USUARIOS. Diseño relacional correcto |

| 6 | **ALERTA_DESTINATARIOS** tabla separada | Reemplaza array en ALERTAS_SALDO |

| 7 | **puede_editar eliminado** de PERMISOS_USUARIO | Era redundante con `puede_editar_lineas` |

| 8 | **Período 9m y 18m añadidos** a todos los endpoints | Estaban en la descripción pero faltaban en la spec de API |

| 9 | **HTTPS con mkcert** en lugar de HTTP o certificado autofirmado | Una configuración inicial → cookies httpOnly seguras → sin advertencias de navegador para siempre |

| 10 | **Windows Service** en lugar de IIS o consola | Arranque automático, reinicio automático si falla, scripteable para actualizaciones |

| 11 | **Watchdog Service** independiente | El backend no puede gestionarse a sí mismo mientras corre. El Watchdog resuelve restauración y actualización |

| 12 | **Soft delete universal** con Papelera | Consistente con requisito de "impossibilidad de borrar datos". Recuperable por admin |

| 13 | **REFRESH_TOKENS** tabla explícita | Necesaria para invalidación correcta en logout y rotación segura de tokens |

| 14 | **TIPOS_CAMBIO y DIVISAS_ACTIVAS** tablas nuevas | No existían en spec original. Necesarias para cache, fallback y gestión de divisas desde UI |

| 15 | **CONFIGURACION** tabla nueva | Centraliza toda la configuración del sistema accesible desde UI. Evita acceso al servidor para cambiar SMTP, URL, etc. |

| 16 | **Conexiones PostgreSQL** sin límite artificial | El límite de "4 conexiones" del doc original era un error conceptual. Npgsql gestiona pool de 20 internamente |

| 17 | **Token de integración hasheado** (SHA-256) | El token real nunca se almacena. Solo el hash. El token se muestra una única vez al admin |

| 18 | **UUIDs** para todas las PKs de integración | Consistencia con el resto del schema. Las tablas originales usaban INTEGER |

| 19 | **tipo_movimiento derivado** en respuestas de integración | No se almacena en BD. Se calcula del signo de `monto` al serializar la respuesta |

| 20 | **Rate limit 100 req/min** para integración | Protege el servidor de uso abusivo. Configurable desde CONFIGURACION |



---



*Documento compilado: Abril 2026 — Versión 3.0*

*Estado: LISTO PARA DESARROLLO CON CLAUDE CODE*

*Próximo paso: Comenzar Fase 0 — Scaffolding e Infraestructura*
