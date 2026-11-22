# Diagnostico de permisos de cuentas - V-03.02

Fecha: 2026-10-05. Diagnostico inicial y correccion posterior del frontend en esta fecha. Base de datos y autorizacion backend sin cambios.

## Estado despues de la correccion

Corregido en `permisosStore.setPermisos`: normaliza ids y reglas de columnas
omitidos a `null`, conservando valores concretos, listas vacias y acciones.
Se ejecutaron 16 regresiones con store/Zustand reales y auth aislado: antes
11 fallos, despues 16/16 OK. Suite frontend 92/92, lint y build OK.
Pendientes despliegue y sesion del gerente afectado; sin validacion de HTTP
o PostgreSQL de esa instalacion. Las secciones siguientes conservan la
evidencia y las limitaciones del diagnostico **anterior al cambio**.

## Conclusion

Hay un fallo comprobado entre la serializacion de la API y el frontend: la API omite los identificadores de alcance cuyo valor es `null`, pero el frontend exige `=== null` para reconocer una dimension sin restriccion. Un permiso global valido puede convertirse en un falso «Sin acceso» al abrir una cuenta.

Reproduce el escenario descrito si la fila tiene `puede_ver_cuentas = true`. No se han consultado los permisos persistidos del gerente afectado ni una respuesta HTTP de su instalacion; no se asegura que sea la unica causa de su incidencia.

## Funcionamiento esperado

- Cada fila combina pais, titular y cuenta mediante interseccion: una dimension concreta restringe y una dimension `null` no restringe. Las filas se suman: basta una compatible con la accion.
- `UsuarioModal.buildPermisosPayload` envia los selectores vacios como `null`.
- Tres ids `null` y «Ver cuentas» marcado conceden lectura global sin enumerar cuentas ni conceder edicion.
- Cuenta vacia con pais/titular concretos abarca todas las cuentas compatibles con esas dimensiones, si esta marcado «Ver cuentas».
- GERENTE no equivale a ADMIN: solo ADMIN evita la matriz. GERENTE tiene un tratamiento especial para dashboard, pero necesita permisos de datos asignados.
- Una fila totalmente vacia, sin acciones ni preferencias, se descarta al construir el payload. Elegir «Todos» no activa por si mismo «Ver cuentas».
- Para no administradores se excluyen cuentas y titulares eliminados logicamente.

## Causa y fuentes

1. `backend/src/AtlasBalance.API/Program.cs:340`: `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`.
2. `DTOs/AuthDtos.cs`, `PermisoUsuarioResponse`: `CuentaId`, `TitularId` y `PaisId` son nullable, sin excepciones a la omision. `Services/AuthService.cs` los copia del permiso a la respuesta de autenticacion.
3. Login, restauracion y refresh entregan las filas a `frontend/src/stores/permisosStore.ts`; `setPermisos` las guarda sin normalizar.
4. `getMatchingPermisos` compara cada dimension con `=== null` o el id de la cuenta. Un campo omitido es `undefined`: no satisface ninguna comparacion cuando la cuenta tiene un identificador concreto.
5. `CuentasPage` muestra «Sin acceso» segun `canViewCuenta`; `CuentaDetailPage` bloquea la vista segun el mismo helper. El dashboard puede seguir permitido porque `canViewDashboard` consulta acciones sin comparar estos ids.

Ejemplo reducido: la respuesta global contiene `{"puede_ver_cuentas":true,"puede_ver_dashboard":true}` y omite los tres ids. El frontend rechaza ese alcance. Con los tres ids explicitamente `null`, lo admite.

## Backend y RLS: revision de codigo

- `Services/UserAccessService.cs`: `LoadScopeFromDatabaseAsync` reconoce acceso global cuando los tres ids son `null` y `PuedeVerCuentas` esta activo. `ApplyCuentaScope` respeta ese acceso y las dimensiones de cada fila.
- `Controllers/CuentasController.cs`, `Listar`: aplica ese servicio; no exige filas individuales por cuenta cuando existe lectura global.
- La migracion `20260626193000_AlignRlsDashboardAccessWithRoles.cs`, funcion `atlas_security.can_read_cuenta`, acepta `p.cuenta_id IS NULL` como comodin y exige la accion correspondiente. Las migraciones posteriores revisadas no sustituyen esa funcion de tres argumentos. Esto acredita el SQL del repo, no las policies instaladas de una base concreta.
- Ya existe `UserAccessScopeMatrixTests.PermisoGlobal_DeberiaPermitirTodasLasCuentasSinPermisosPorCuenta`; su existencia no equivale a haberlo ejecutado en esta sesion.

## Reproduccion ejecutada

Se leyo el archivo real `permisosStore.ts`, se quitaron tipos con `node:module.stripTypeScriptTypes` y se ejecuto en `node:vm`. Solo se sustituyeron imports por adaptadores minimos de estado y rol; las comparaciones y helpers se ejecutaron sin cambios. No es una prueba de React, de Zustand instalado ni visual. Datos sinteticos.

| Caso | `canViewCuenta` |
|---|---|
| GERENTE, lectura global con tres `null` explicitos | `true` |
| GERENTE, misma lectura global con ids omitidos | `false` (fallo) |
| GERENTE, pais y titular concretos, cuenta omitida | `false` (fallo) |
| GERENTE, mismos pais/titular, cuenta `null` | `true` |
| GERENTE, cuenta concreta con los tres ids presentes | `true` |
| GERENTE, alcance global sin «Ver cuentas» | `false` (correcto) |
| EMPLEADO, lectura global con ids omitidos | `false` (fallo) |
| ADMIN, lectura global con ids omitidos | `true` (bypass del rol) |

Ocho comprobaciones correctas del comportamiento actual. Para GERENTE con lectura e ids omitidos, `canViewDashboard` devuelve `true` mientras `canViewCuenta` devuelve `false`.

## Correccion recomendada en el diagnostico inicial (ya implementada)

Normalizar ids omitidos a `null` en `setPermisos`, punto comun de login, restauracion y refresh. Conservar las acciones y restricciones; no convertir una lista vacia de permisos en acceso global.

Probar respuestas serializadas reales: global, pais, titular, cuenta concreta, sin lectura, multiples filas y ambos roles no administradores. Despues verificar navegador autenticado y PostgreSQL con rol runtime. Emitir explicitamente `null` en el DTO tambien permitiria fijar el contrato; no hace falta cambiar la politica JSON de toda la API.

No asignar individualmente todas las cuentas ni dar ADMIN al gerente: enmascara el fallo y altera alcance o privilegios.

## Verificado, bloqueado y pendiente

- **Verificado:** trazado del formulario, auth, store, vistas, servicio de acceso y SQL RLS del repo; ocho casos mediante Node; revision de tests existentes.
- **Bloqueado:** `dotnet test --no-restore -p:UseAppHost=false -p:UseArtifactsOutput=true -p:ArtifactsPath=<workspace>/tools/dotnet-build/diagnostico-permisos -- --filter-class '*UserAccess*' --timeout 60s` termina con `NETSDK1004` por ausencia de `project.assets.json`. TypeScript tampoco esta instalado en frontend; se uso el soporte incorporado de Node sin descargar dependencias.
- **Pendiente:** permisos persistidos del usuario, HTTP real, version desplegada y policies aplicadas; correccion, regresiones, navegador autenticado y PostgreSQL. Sin arranque de servicios ni cambios de usuarios, permisos o datos.
