# Informe de revision - 2026-09-24 - V-03.01

Alcance: codigo nuevo desde la auditoria integral del 2026-08-23 (ya registrada en
`REGISTRO_BUGS.md`), con foco en los commits de V-03.01: actualizador elevado del
Watchdog, cuentas de servicio dedicadas, OpenRouter gratuito, selector de modelo del
chat IA, coordinador de refresh de sesion y politicas RLS optimizadas. Lo ya
registrado como abierto no se repite salvo al final, como recordatorio.

Actualizacion 2026-09-25: los 8 hallazgos de este informe, mas un noveno
encontrado al corregirlos, quedaron cerrados en codigo el 2026-09-25. Ver
`Documentacion/REGISTRO_BUGS.md` (entrada "2026-09-25 - V-03.01 - Cerrado -
Revision 2026-09-24...") y `Documentacion/LOG_ERRORES_INCIDENCIAS.md` para el
detalle de cada correccion. La seccion "Estado (2026-09-25)" al final de este
informe resume el hallazgo nuevo y lo que queda pendiente de verificar
manualmente.

| # | Severidad | Hallazgo |
|---|-----------|----------|
| 1 | **ALTA** | Escalada cuenta Watchdog -> SYSTEM en el actualizador elevado |
| 2 | **ALTA** | Datos financieros a modelos gratis de OpenRouter sin ZDR, y por defecto |
| 3 | MEDIA | El runner elevado copia solo el `.exe`: la actualizacion elevada no puede arrancar |
| 4 | MEDIA | Cualquier usuario IA puede saltarse el modelo elegido por el admin (incluido `openrouter/auto`, de pago) |
| 5 | BAJA | `selectedModel` del chat no se invalida al cambiar el proveedor |
| 6 | BAJA | Regex de cuentas integradas en `ServiceSecurity.ps1` no casa `NT AUTHORITY\...` |
| 7 | BAJA | Mojibake en la respuesta de logout |
| 8 | INFO | `href` con datos del servidor en el chat IA (codigo muerto hoy) |

---

## 1. ALTA - Escalada de privilegios cuenta Watchdog -> SYSTEM

**Donde:** `backend/src/AtlasBalance.Watchdog/Services/ElevatedUpdateRunner.cs`,
`scripts/Run-AtlasElevatedUpdate.ps1`, `scripts/ServiceSecurity.ps1:207-235`.

**Que pasa:** la tarea programada `AtlasBalance.Update` corre como SYSTEM
(`ServiceSecurity.ps1:296`). La cuenta del Watchdog puede escribir la peticion y
lanzar la tarea (`GRGX`). El problema es que SYSTEM acaba ejecutando codigo que
esa misma cuenta puede modificar, por dos caminos:

1. **Se firma una cosa y se ejecuta otra.** `VerifyPackageSignature` valida la firma
   RSA del **ZIP**, pero luego ejecuta `PackageRoot\scripts\Actualizar-AtlasBalance.ps1`,
   que esta en el directorio **ya extraido**. Nada comprueba que ese directorio
   salga de ese ZIP. La cuenta Watchdog tiene `Modify` sobre `updates\`
   (`ServiceSecurity.ps1:208`), asi que le basta con dejar ahi un ZIP legitimo
   firmado (vale cualquier release anterior con su `.sig`) y, al lado, un
   `PackageRoot` con un `.ps1` suyo. SYSTEM lo ejecuta con `-ExecutionPolicy Bypass`.
2. **Directorio del runner escribible.** `config\update-runner` da `Modify` a la
   cuenta Watchdog (`ServiceSecurity.ps1:235`, `Instalar-AtlasBalance.ps1:1097`).
   SYSTEM copia ahi `AtlasBalance.Watchdog.exe` y lo ejecuta. El publish no es
   single-file (`Build-Release.ps1:200`), asi que el apphost carga
   `AtlasBalance.Watchdog.dll` (y sus dependencias) **desde ese directorio**:
   quien plante esa DLL ejecuta codigo como SYSTEM. Ademas hay una carrera entre
   `Copy-Item` y la ejecucion.

**Impacto:** la separacion de cuentas de V-03.01 deja de servir. Comprometer el
Watchdog (o la API, que tiene el secreto para hablar con el) equivale a tener
SYSTEM en el servidor.

**Correccion propuesta:**
- `config\update-runner`: solo Administrators y SYSTEM, **sin** la cuenta Watchdog
  (quitar el grant en `ServiceSecurity.ps1:235` e `Instalar-AtlasBalance.ps1:1097`).
- En el runner (SYSTEM): no usar `PackageRoot` para nada que se ejecute. Hacer:
  copiar el ZIP a un directorio temporal solo de SYSTEM/Administrators ->
  verificar la firma **sobre esa copia** -> extraer esa misma copia (con el
  anti-zip-slip que ya existe) -> ejecutar el script desde lo extraido.
  Verificar y extraer el mismo fichero cierra la carrera TOCTOU.
- Copiar el runner entero (exe + dll + `*.deps.json` + `*.runtimeconfig.json`)
  desde `watchdog\`, que es solo lectura para las cuentas de servicio, o
  ejecutarlo directamente desde `watchdog\` sin copiarlo.
- Test: un test Pester que falle si `update-runner` o el directorio de ejecucion
  del script contienen un ACE de escritura para una cuenta de servicio, y un test
  C# que compruebe que `PackageRoot` manipulado **no** se ejecuta.

## 2. ALTA - Datos financieros a modelos gratis sin ZDR (y ahora por defecto)

**Donde:** `Constants/AiConfiguration.cs:7-8` (`OpenRouterDefaultModel = "openrouter/free"`),
`Services/AtlasAiService.cs:~687-704` (se quita `provider` con `zdr`/`data_collection=deny`
para todo modelo `:free`), commit `3fcf394`.

**Que pasa:** el prompt lleva fechas, importes, saldos y **conceptos bancarios
completos** (`AtlasAiService.cs:1182, 1233, 1877`). El seudonimizador solo sustituye
nombres de titulares/cuentas/terceros conocidos; en los conceptos suelen ir
contrapartes, numeros de factura, fragmentos de IBAN, nombres de empleados... Los
proveedores de modelos gratis de OpenRouter pueden registrar prompts y usarlos para
entrenar: por eso esos modelos fallan con `data_collection=deny`, y por eso el
commit quita la restriccion en vez de cambiar de modelo. El comentario "la
seudonimizacion DLP sigue activa" da una sensacion de seguridad que no toca.

Lo grave es el **default**: una instalacion que active IA con una clave de
OpenRouter manda tesoreria real a terceros que la retienen, sin que nadie lo haya
decidido de forma explicita. Para una app de tesoreria on-premise eso es un
problema de RGPD (transferencia a encargados sin contrato ni garantias) y de
confianza del cliente.

**Correccion propuesta (recomendada):**
- Volver a un default que respete ZDR (`openrouter/auto` con el guard de
  privacidad, o un modelo de pago con ZDR).
- Los modelos `:free`/`openrouter/free` solo tras un opt-in explicito del admin:
  una clave de config `ai_allow_data_retention` (default `false`), con aviso claro
  en Configuracion ("estos modelos pueden guardar y usar tus datos"), registrada en
  auditoria. Si el flag esta a `false`, `AskAsync` rechaza los modelos free con
  `IaConfigurationException`.
- Documentarlo en `DOCUMENTACION_USUARIO.md` (hoy no hay aviso).

Si se decide mantener los free por coste, que sea decision escrita del cliente,
no un default.

## 3. MEDIA - El runner elevado copia solo el `.exe`

**Donde:** `scripts/Run-AtlasElevatedUpdate.ps1:7-15`.

Con `PublishSingleFile=false` el apphost necesita `AtlasBalance.Watchdog.dll`,
`.deps.json` y `.runtimeconfig.json` en su mismo directorio. En
`config\update-runner` solo hay el `.exe`, asi que el proceso deberia terminar con
"The application to execute does not exist" y la actualizacion elevada no
arrancaria nunca. No lo he ejecutado: esta deducido del comportamiento del apphost.
Si en vuestras pruebas funciono, es que ya habia una DLL en ese directorio, lo que
confirma el vector 2 del hallazgo 1. Se arregla con lo mismo que el hallazgo 1.

**Verificacion:** ejecutar la tarea `AtlasBalance.Update` en una VM limpia y
revisar el exit code.

## 4. MEDIA - Los usuarios pueden saltarse el modelo elegido por el admin

**Donde:** `Controllers/IaController.cs:106-113` -> `AtlasAiService.ApplyRequestedModel`
(`:2381`); nuevo selector en `components/ia/AiChatPanel.tsx:455`.

`AskAsync` acepta `request.Model` de cualquier usuario con `PuedeUsarIa` y solo
comprueba que este en la allowlist. En la allowlist estan a la vez `openrouter/auto`
(enruta a modelos **de pago**) y los `:free` (sin ZDR, ver hallazgo 2). Resultado:
el admin elige un modelo y cada usuario puede cambiarlo, en coste y en privacidad.
Los limites de peticiones acotan el gasto, pero no la decision de privacidad.

**Correccion propuesta:** que el admin guarde la lista de modelos que los usuarios
pueden elegir (por defecto, solo el configurado) y que `AskAsync` valide contra esa
lista, no contra la allowlist global. Si el selector del chat no aporta mucho, lo
mas simple es quitar el override por peticion.

## 5. BAJA - `selectedModel` no se invalida al cambiar de proveedor

**Donde:** `frontend/src/stores/aiChatStore.ts:88-120` (`ensureConfig`).

`ensureConfig` recarga `config` y ajusta `thinkingMode` al proveedor nuevo, pero no
toca `selectedModel`. Si el admin pasa de OpenRouter a OpenAI con la pestana
abierta, el chat sigue mandando `openrouter/...` y el backend responde 400
("Modelo de IA invalido") hasta hacer logout. `AiChatPanel` ademas mete el modelo
invalido como primera opcion del select (`AiChatPanel.tsx:231-236`).

**Correccion:** en `ensureConfig`, si `selectedModel` no esta en
`getAiModelOptions(data.provider)`, ponerlo a `null`. Test unitario del store.

## 6. BAJA - Regex de cuentas integradas incompleta

**Donde:** `scripts/ServiceSecurity.ps1:335`.

```powershell
'^(LocalSystem|LocalService|NetworkService|NT AUTHORITY\\|NT SERVICE\\|SYSTEM)$'
```

Por el `$`, las ramas `NT AUTHORITY\\` y `NT SERVICE\\` solo casan con el prefijo
exacto sin nada detras. Comprobado: `NT AUTHORITY\LocalService`,
`NT AUTHORITY\NetworkService`, `NT AUTHORITY\SYSTEM` y `NT SERVICE\X` devuelven
`False`. Hoy lo tapa el `Get-LocalUser` posterior, pero el mensaje de error es
enganoso y ese segundo check tambien acepta una cuenta de dominio si existe una
local con el mismo nombre (`-split '\\'` y `[-1]`).

**Correccion:**
```powershell
'^(LocalSystem|LocalService|NetworkService|SYSTEM|NT AUTHORITY\\.+|NT SERVICE\\.+)$'
```
y comparar el prefijo de dominio del `StartName` con `.` o `$env:COMPUTERNAME`
antes de llamar a `Get-LocalUser`. Anadir los cuatro casos a `ServiceSecurity.Tests.ps1`.

## 7. BAJA - Mojibake en logout

**Donde:** `Controllers/AuthController.cs:125`: `"Sesi�n cerrada"` (U+FFFD literal
en el fuente). Es el unico fichero `.cs/.ts/.tsx/.ps1` afectado. Correccion:
`"Sesion cerrada"`, o `"Sesión cerrada"` guardando el fichero en UTF-8.

## 8. INFO - `href` con datos del servidor en el chat IA

**Donde:** `components/ia/AiChatPanel.tsx:568-575`.

Pinta `<a href={enlace.ruta}>` a partir de `message.meta.enlaces`, pero el store
nunca rellena `enlaces` y el backend no lo devuelve: hoy es codigo muerto. Si algun
dia se conecta a algo que salga del LLM, React no bloquea `javascript:` en `href`
(solo avisa), asi que seria un XSS. Propuesta: borrarlo, o validar que `ruta`
empiece por `/` y no por `//` y navegar con `<Link>` del router.

---

## Sigue abierto en `REGISTRO_BUGS.md` (no se ha vuelto a auditar)

- Refresh concurrente sin ventana de gracia (revocacion total + alerta falsa).
- Cookies legacy sin `__Host-` aceptadas en produccion (`Program.cs:202-203`,
  `AuthController.cs:232-236`): anulan el proposito del prefijo. Propuesta: leer el
  nombre legacy solo en `IsDevelopment()`.
- `RlsDbCommandInterceptor` falla abierto (sin `HttpContext` -> contexto `System()`).
- Rama `is_auth_flow()` sin filtro en `permisos_usuario_select`.
- Jobs Hangfire sin `[DisableConcurrentExecution]`; `BackupOperationJob` puede
  quedarse en RUNNING para siempre.
- Tests de jobs sobre InMemory, que no evalua RLS.

## Orden recomendado

1. Hallazgos 1 y 3 juntos (mismo arreglo). Bloquean release: no publicar V-03.01
   con el runner actual.
2. Hallazgo 2: decision de producto hoy mismo; el cambio de codigo es pequeno.
3. Hallazgos 4 y 5 juntos (mismo flujo de seleccion de modelo).
4. 6, 7 y 8: una tanda de limpieza.

## Estado (2026-09-25)

Los 8 hallazgos de este informe se corrigieron y verificaron en codigo. Detalle
completo en `Documentacion/REGISTRO_BUGS.md` (entrada del 2026-09-25) y en
`Documentacion/LOG_ERRORES_INCIDENCIAS.md`.

**Hallazgo nuevo #9 (BLOQUEANTE), encontrado al corregir el #1:** al retirar el
acceso Modify del Watchdog sobre `updates\` para cerrar la escalada, quedo expuesto
que la cuenta de servicio de la API solo tenia lectura/ejecucion sobre `updates\` y
`backups\`, pero `ActualizacionService` y `BackupService` necesitan escribir ahi en
su operacion normal. Sin ese permiso, la actualizacion desde dentro de la app nunca
habia funcionado en ninguna instalacion V-03.01. Solucionado dando a la API permiso
de modificacion sobre `updates\` (no sobre `updates\requests`, que sigue siendo del
Watchdog) y sobre `backups\`; sigue siendo seguro porque el runner SYSTEM reverifica
firma y contenido sobre su propia copia privada antes de ejecutar nada.

Tambien se identifico y corrigio la causa raiz de que no se pudiera actualizar desde
una version anterior (hallazgo #10, no listado en la tabla original de este
informe): `Actualizar-AtlasBalance.ps1` exigia identidades de servicio no
integradas y toda instalacion V-02.09 corre como `LocalSystem`, asi que la
actualizacion abortaba siempre en el primer chequeo. Se anadio migracion
automatica de identidades (`Repair-AtlasServiceIdentities`) con rollback si falla.

**Verificado en esta sesion:** `dotnet test tests/AtlasBalance.API.Tests` completo
935/935 (0 skipped, incluye Postgres/Testcontainers); `npm run lint` y `npx tsc` OK;
tests unitarios frontend 75/75; `vite build` OK; parser PowerShell 5.1 sobre los 5
scripts tocados OK; los 6 `scripts/*.Tests.ps1` en verde. Nuevo
`ElevatedUpdateRunnerTests` (5 casos) prueba que un `PackageRoot` manipulado nunca
llega a ejecutarse.

**Pendiente de verificar manualmente (no cubierto por tests automatizados):**

- Actualizacion real en una VM Windows Server: instalar V-02.09, actualizar a
  V-03.01 con `Actualizar Atlas Balance.cmd` como Administrador y tambien desde la
  app (Watchdog viejo), y confirmar que los servicios terminan como
  `AtlasBalanceApiSvc` / `AtlasBalanceWatchdogSvc`.
- Que `icacls config\update-runner` no tenga ninguna ACE de cuenta de servicio tras
  la actualizacion.
- Que una segunda actualizacion en la app (V-03.01 -> siguiente version) pase
  correctamente por la tarea programada `AtlasBalance.Update`.
- El flujo de actualizacion interno
  (`WatchdogSettings:UseExternalPackageUpdater=false`, no usado por defecto en
  Windows) queda como limitacion conocida sin resolver: fallaria porque el
  Watchdog solo tiene RX sobre `api`/`watchdog`/`scripts`.
- Lo ya registrado como abierto en `REGISTRO_BUGS.md` (cookies legacy sin
  `__Host-`, ventana de gracia en refresh concurrente, interceptor RLS fail-open,
  etc.) sigue abierto; no se volvio a auditar en esta sesion.
