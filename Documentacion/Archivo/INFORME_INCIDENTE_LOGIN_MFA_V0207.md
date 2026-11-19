# Informe de incidencias de acceso en la instalacion V-02.07

**Fecha:** 2026-08-05  
**Alcance:** instalacion on-premise en `C:\AtlasBalance`, acceso HTTPS en `https://trakeria:8443` y correcciones preparadas bajo V-02.08.  
**Estado:** los dos cambios de codigo estan desplegados y verificados por build, pruebas focalizadas, hash y salud de servicios. La validacion real final del MFA sigue pendiente de iniciar sesion de nuevo y usar un TOTP generado despues del reinicio.

## Resumen ejecutivo

El bloqueo de acceso no fue un fallo de password ni de configuracion del secreto RLS. Fue una incompatibilidad entre el modo en que EF Core escribe entidades con clave generada (`INSERT ... RETURNING`) y el modelo deliberadamente restrictivo de RLS sobre `AUDITORIAS`: el flujo anonimo de autenticacion puede insertar un evento de seguridad, pero no puede leerlo. PostgreSQL considera `RETURNING` una lectura y exige tambien policy `SELECT`.

El mismo defecto aparecio dos veces por caminos diferentes:

1. Al terminar el login con password, `AuditService` insertaba el evento de acceso mediante EF Core.
2. Al verificar el primer MFA, `AuditSaveChangesInterceptor` agregaba una auditoria automatica al guardar usuario y refresh token.

Ambos caminos devolvian HTTP 500 aunque las credenciales y el codigo MFA fueran validos. La solucion no relaja RLS ni concede lectura de auditorias al contexto anonimo: evita `RETURNING` para eventos explicitos de auth y evita la auditoria generica duplicada durante la autenticacion anonima.

## Linea temporal comprobada

| Momento | Hecho comprobado | Consecuencia |
|---|---|---|
| Instalacion inicial | El reset de administrador fallaba en Windows PowerShell 5.1 al intentar cargar una DLL BCrypt de .NET 8. | No se podia preparar de forma fiable la credencial administrativa inicial. |
| Login inicial | La password era aceptada y se registraba `LOGIN_MFA_REQUIRED`, pero la respuesta era HTTP 500. | No se alcanzaba la pantalla MFA. |
| Primer hotfix | El login real respondio HTTP 200 y Chrome avanzo a `Verificar acceso`. | Confirmo que el primer 500 estaba corregido. |
| Primer TOTP | `POST /api/auth/mfa/verify` devolvio HTTP 500. El log mostro el batch EF con `INSERT INTO AUDITORIAS`, `INSERT INTO REFRESH_TOKENS` y `UPDATE USUARIOS`; PostgreSQL devolvio `42501` sobre `AUDITORIAS`. | El alta/verificacion MFA no podia completar la sesion. |
| Segundo hotfix | DLL nuevo desplegado, hash SHA-256 coincidente, API y Watchdog activos y puerto 8443 disponible. | El servidor esta listo para repetir el ensayo con un challenge MFA nuevo. |

## Incidencias y causa raiz

### 1. Reset de administrador incompatible con el runtime

**Sintoma.** `Reset-AdminPassword.ps1` intentaba cargar `BCrypt-Net-Next.dll`. En Windows PowerShell 5.1 aparecieron primero la marca de descarga y despues una `ReflectionTypeLoadException` por `System.Private.CoreLib 8.0`.

**Causa raiz.** El script se ejecuta en .NET Framework (PowerShell 5.1) y la DLL de BCrypt distribuida con la API requiere .NET 8. Son runtimes incompatibles; quitar la marca de descarga solo ocultaba el primer error.

**Correccion aplicada.** El hash bcrypt de coste 12 se genera en PostgreSQL con `pgcrypto`; la password se transmite a `psql` por stdin, no como argumento de proceso.

**Leccion.** Los scripts operativos deben depender solo del runtime que realmente ejecutan. No se deben cargar ensamblados de la aplicacion .NET 8 desde Windows PowerShell 5.1.

### 2. Login: auditoria explicita incompatible con RLS

**Sintoma.** Tras validar la password, el login devolvia HTTP 500 antes de MFA.

**Causa raiz.** EF Core insertaba `AUDITORIAS` con `RETURNING secuencia`. La policy RLS de auth permite `INSERT`, pero no `SELECT`, por diseno. PostgreSQL exige visibilidad SELECT para la fila devuelta en `RETURNING`, asi que respondio `42501`.

**Correccion aplicada.** En peticiones anonimas bajo `/api/auth`, `AuditService` usa un INSERT parametrizado sin `RETURNING`. La secuencia, la firma, `FORCE RLS` y el trigger append-only siguen aplicandose en la base de datos.

**Leccion.** No basta con probar que una policy permite INSERT: hay que probar el SQL que genera el ORM, incluyendo `RETURNING`, columnas generadas y batches.

### 3. MFA: auditoria automatica duplicada incompatible con RLS

**Sintoma.** La pantalla MFA aceptaba el codigo, pero la primera verificacion devolvia HTTP 500.

**Causa raiz.** `IssueTokensAsync` guardaba un cambio de `USUARIOS` y un nuevo `REFRESH_TOKENS`. El interceptor `AuditSaveChangesInterceptor` agregaba una fila generica de `AUDITORIAS` en ese mismo `SaveChanges`; EF volvia a generar `INSERT ... RETURNING secuencia`. Aunque los eventos `MFA_ENABLED`, `MFA_VERIFIED` y `LOGIN` ya estaban auditados de forma explicita, el interceptor duplicado seguia abortando la transaccion.

**Correccion aplicada.** El interceptor no crea auditorias genericas para rutas anonimas `/api/auth`. Se conservan los eventos semanticos y firmados de seguridad (login, MFA, fallos y bloqueos), emitidos por `AuditService` sin `RETURNING`. Las peticiones autenticadas y los jobs mantienen la auditoria automatica.

**Leccion.** La auditoria explicita y la automatica deben tener una frontera definida. En auth anonimo, las auditorias semanticas son la fuente de verdad; grabar tambien diffs de entidad no aporta evidencia util y puede introducir efectos secundarios en el mismo `SaveChanges`.

### 4. Despliegue: UAC, entrecomillado y challenges volatiles

**Sintoma.** Los primeros intentos de copiar el DLL no modificaron produccion: uno carecia de token administrador y otro no transmitio correctamente argumentos con rutas que contienen espacios. El despliegue correcto requirio UAC explicito.

**Causa raiz.** La ejecucion elevada con `Start-Process` es sensible al entrecomillado de `-File`, `-InstallPath` y `-PatchedDllPath`. Ademas, reiniciar la API elimina los challenges MFA en memoria, comportamiento esperado de una aplicacion que no los persiste.

**Correccion aplicada.** Se uso un script de despliegue con backup del DLL, verificacion SHA-256, reinicio de API/Watchdog, espera acotada de 8443 y rollback si la API no queda disponible.

**Leccion.** El procedimiento de despliegue debe probarse con rutas reales de Windows y documentar que cualquier challenge MFA abierto se invalida al reiniciar.

## Riesgos que no se deben aceptar como solucion

- Conceder `SELECT` sobre `AUDITORIAS` al contexto anonimo de auth.
- Desactivar `FORCE ROW LEVEL SECURITY`, relajar las policies o usar `BYPASSRLS` para el runtime.
- Eliminar toda auditoria durante autenticacion.
- Sustituir DLLs de forma manual sin backup, hash, parada coordinada y rollback.

## Trabajo obligatorio para la proxima version

1. **Prueba de integracion de auth con PostgreSQL y rol runtime.** Debe cubrir login, enrolamiento MFA inicial, verify MFA, emision de refresh token y renovacion con las mismas policies RLS y sin SELECT sobre `AUDITORIAS`.
2. **Contrato de auditoria por flujo.** Declarar y probar que auth anonimo usa solo eventos semanticos; operaciones autenticadas y jobs usan tambien auditoria automatica. Incluir una prueba que compruebe que el evento MFA queda firmado y persistido.
3. **CI con Docker/Testcontainers disponible.** La regresion PostgreSQL no puede considerarse validada solo por compilar; debe ser un gate de version.
4. **Smoke test post-instalacion.** Comprobar salud HTTPS, login y MFA con una cuenta de prueba controlada, sin registrar contrasenas ni secretos TOTP.
5. **Script elevado probado.** Mantener un unico comando documentado para UAC, con rutas entrecomilladas y salida/exit code verificables.
6. **Paquete completo para la siguiente release.** Incluir estas correcciones en el ZIP firmado y versionado; no depender de un DLL hotfix aislado.
7. **Checklist de reinicios.** Advertir que se invalidan challenges MFA de memoria y pedir un challenge nuevo despues del servicio.

## Criterios de cierre para la proxima version

- Login con password responde HTTP 200 y crea challenge MFA.
- El primer TOTP habilita MFA, emite sesion y redirige sin HTTP 500.
- Un login posterior con MFA ya habilitado tambien completa la sesion.
- Los eventos `LOGIN_MFA_REQUIRED`, `MFA_ENABLED`, `MFA_VERIFIED` y `LOGIN` aparecen firmados en `AUDITORIAS` sin exponer secretos.
- La suite PostgreSQL/RLS pasa en CI y el paquete firmado incorpora el arreglo.
- API, Watchdog y PostgreSQL permanecen saludables tras el despliegue.

## Evidencia disponible

- Codigo: `AuditService.cs` y `AuditSaveChangesInterceptor.cs`.
- Pruebas focalizadas: 4/4 correctas en `AuditSaveChangesInterceptorTests`.
- Compilacion Release: 0 errores, 7 advertencias preexistentes.
- DLL desplegada: SHA-256 `09D194A9AB2FE9AEF241E402E79E1A2229342CBEBFB5970AA79AE2DCA0EF8932`.
- Servicios `AtlasBalance.API` y `AtlasBalance.Watchdog` en ejecucion; TCP 8443 disponible tras el despliegue.
- Regresion PostgreSQL creada para el primer defecto; su ejecucion local sigue bloqueada por ausencia de Docker/Testcontainers.
