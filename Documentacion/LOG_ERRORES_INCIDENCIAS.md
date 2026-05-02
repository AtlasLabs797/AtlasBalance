# Log de errores e incidencias

## 2026-05-02 - V-01.05 - CI GitHub fallaba en `npm ci` por lockfile corrupto

- Contexto: los workflows `push` y `pull_request` de GitHub Actions fallaban en la rama `V-01.05` durante `Install frontend dependencies`.
- Causa: `Atlas Balance/frontend/package-lock.json` resolvia `once` a `1.5.0` y a `https://registry.npmjs.org/once/-/once-1.5.0.tgz`, version que no existe en npm. El rango transitivo de `glob@7.2.3`/`inflight@1.0.6` es `^1.3.0`; la version publicada correcta es `once@1.4.0`.
- Solucion aplicada: se fija `overrides.once = 1.4.0` en `package.json` y se corrige el lockfile para que `node_modules/once` apunte a `once-1.4.0.tgz` con la integridad oficial.
- Verificacion: `npm.cmd ci` OK, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, `npm.cmd run lint` OK y `npm.cmd run build` OK.

## 2026-05-02 - V-01.05 - Cierre de hallazgos residuales del escaneo repo-wide

- Contexto: el escaneo repo-wide posterior encontro ocho problemas reales o de hardening que quedaban abiertos en scripts, autorizacion backend, integracion OpenClaw, frontend, RLS y CI.
- Hallazgos corregidos:
  - `Instalar-AtlasBalance.ps1` escribia `INSTALL_CREDENTIALS_ONCE.txt` antes de endurecer ACL y no comprobaba `icacls`. Ahora escribe en `C:\AtlasBalance\config`, restringe el directorio antes de volcar secretos y falla cerrado si ACL falla.
  - `Reset-AdminPassword.ps1` escribia la password temporal antes de ACL y degradaba el fallo a warning. Ahora exige Administrador, restringe `config` antes de escribir y borra/falla si no puede proteger el archivo.
  - `ExtractosController.ToggleFlag` permitia editar `flagged` o `flagged_nota` con permiso de una sola columna. Ahora exige permiso por cada campo que cambie.
  - `DashboardService` trataba una fila global `PuedeVerDashboard` como acceso global de datos. Ahora solo concede global si esa fila tambien tiene permisos de datos; los permisos dashboard-only deben estar scopeados.
  - `IntegrationOpenClawController.Auditoria` miraba extractos con `IgnoreQueryFilters()` y podia devolver valores de auditoria de extractos eliminados. Ahora respeta soft-delete para el mapa de extractos.
  - `ImportacionPage` renderizaba `returnTo` desde query directamente en `<Link>`. Ahora solo acepta rutas internas absolutas.
  - La politica RLS `exportaciones_write` usaba permiso de lectura. Ahora usa `can_write_cuenta_by_id`.
  - CI y `docker-compose.yml` usaban `postgres:16-alpine` mutable. Ahora se fija el digest `sha256:4e6e670bb069649261c9c18031f0aded7bb249a5b6664ddec29c013a89310d50`.
- Verificacion: tests focalizados 20/20 OK, `npm.cmd run lint` OK, `npm.cmd run build` OK, parser PowerShell OK y `robocopy frontend/dist -> wwwroot` OK.

## 2026-05-02 - V-01.05 - Revision repo-wide post-hardening encuentra fugas residuales

- Contexto: tras el escaneo previo de seguridad se hizo una pasada nueva con un subagente sobre todo el codigo (controllers, services, middleware, frontend, scripts y Watchdog). Se priorizaron hallazgos no cubiertos en auditorias anteriores.
- Hallazgos corregidos:
  - `IntegrationOpenClawController` devolvia el email del usuario creador de cada extracto al socio externo (PII innecesaria; ya solo se sustituia por `usuario-eliminado` cuando estaba borrado). Ahora retorna `nombre_completo`.
  - `IntegrationOpenClawController.Auditoria` enviaba `ip_address` del operador interno a OpenClaw. Eliminado del payload.
  - `scripts/Reset-AdminPassword.ps1` con `-GeneratePassword` imprimia la password temporal en consola (riesgo de quedar en historial/transcripts). Ahora la escribe en `C:\AtlasBalance\config\RESET_ADMIN_CREDENTIALS_ONCE.txt` con ACL restringida a Administrators y se solicita borrar el archivo tras el primer login.
  - `ActualizacionService` extraia el paquete con `ZipFile.ExtractToDirectory`. Aunque el digest SHA-256 y la firma RSA del asset ya cubren autenticidad, se anade defensa en profundidad: cada entrada se valida contra el `packageRoot` real antes de escribirse, se aborta el update y se borra la carpeta si una entrada saldria fuera.
- Bug abierto cerrado en la misma pasada: harness RLS de tests reasigna ahora ownership de tablas/secuencias/funciones de `public` y `atlas_security` al rol owner creado por el test, dejando la suite 129/129 OK.
- Verificacion: `dotnet test ...GestionCaja.API.Tests.csproj -c Release --no-build` 129/129 OK; parser PowerShell de `Reset-AdminPassword.ps1` OK; `npm.cmd run lint` OK, `npm.cmd run build` OK; `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades; `dotnet list ... package --vulnerable --include-transitive` sin vulnerabilidades.

## 2026-05-02 - V-01.05 - Escaneo de seguridad repo-wide encontro controles debiles

- Contexto: se pidio escanear todo el codigo con `codex-security` y subagentes, y corregir vulnerabilidades reales.
- Hallazgos corregidos: lockout de password no persistia al quinto intento por throttle previo; fallos MFA se reseteaban al crear challenge nuevo; auditoria de integraciones podia guardar secretos en query; importacion permitia amplificacion con columnas extra; permisos dashboard-only daban scope de datos; restaurar extractos exigia solo vista; plazos fijos filtraban mal cuenta de referencia; update online confiaba en digest del mismo canal.
- Solucion aplicada: lockout real a 5 intentos, contador MFA por usuario, redaccion normalizada de query, limites de columnas extra, permisos app-layer filtrados por flags de datos, restore con `CanDelete`, referencia de plazo fijo solo visible si la cuenta es accesible, y verificacion RSA/SHA-256 de `.zip.sig` para paquetes online.
- Verificacion: tests focalizados 72/72 OK, NuGet sin vulnerabilidades, npm audit 0 vulnerabilidades, parser PowerShell OK.
- Incidencia abierta relacionada: la suite backend completa queda 127/128 por permisos locales de PostgreSQL en `__EFMigrationsHistory` dentro de `RowLevelSecurityTests`; no es fallo de las correcciones, pero hay que arreglar el harness.

## 2026-05-02 - V-01.05 - Graficas de evolucion seguian reservando demasiado eje Y

- Contexto: las graficas `Evolucion` reutilizadas en dashboard principal, dashboard por titular, `Titulares` y `Cuentas` aun podian verse demasiado desplazadas a la derecha con importes pequenos.
- Causa: `EvolucionChart` ya habia reducido el eje Y a `72px`, pero seguia siendo un ancho fijo aunque las etiquetas fueran compactas y cortas.
- Solucion aplicada: el ancho del `YAxis` se calcula segun la etiqueta compacta mas larga, limitado entre `44px` y `72px`.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy` OK y Playwright headless confirma `gridStartX=45px` en `/dashboard`, `/dashboard/titular/titular-1`, `/titulares` y `/cuentas`.

## 2026-05-02 - V-01.05 - Saldo total se partia con importes de un millon

- Contexto: en el dashboard principal, el KPI `Saldo total` podia partir `1.000.000,00 €` en dos lineas o desbordar la tarjeta superior.
- Causa: la grilla de KPIs superiores repartia el espacio de forma demasiado igualitaria y el saldo destacado tenia una escala excesiva para importes reales de tesoreria.
- Solucion aplicada: `dashboard-kpi-grid--overview` da mas ancho relativo al KPI principal, reduce padding de los KPIs superiores, baja la escala del importe destacado y fuerza `white-space: nowrap` en importes KPI.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy` OK y Playwright headless con `total_convertido=1000000` confirma `wraps=false` y `overflows=false`.

## 2026-05-02 - V-01.05 - Saldos por divisa no priorizaba la divisa base

- Contexto: en el dashboard principal, `Saldos por divisa` podia mostrar antes una divisa secundaria si la API la devolvia primero.
- Causa: `SaldoPorDivisaCard` renderizaba `items` en el orden recibido, dejando la jerarquia visual en manos del array.
- Solucion aplicada: el componente parte la lista para renderizar primero `divisaPrincipal` y despues el resto de divisas en su orden original.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy` OK y Playwright headless confirma que `EUR` aparece primero aunque la API devuelva `USD` antes.

## 2026-05-02 - V-01.05 - Cuentas de efectivo no permitian seleccionar formato de importacion

- Contexto: en `Cuentas`, al elegir tipo `Efectivo`, la pantalla ocultaba el selector `Formato de importacion`; ademas el backend descartaba cualquier `formato_id` enviado para ese tipo.
- Causa: la logica heredada trataba `EFECTIVO` igual que `PLAZO_FIJO`, aunque solo plazo fijo necesita flujo especial sin formato.
- Solucion aplicada: `EFECTIVO` conserva selector y `formato_id`; solo se limpian datos bancarios. `PLAZO_FIJO` sigue sin formato. Backend valida formato para `NORMAL` y `EFECTIVO`.
- Verificacion: `CuentasControllerTests` 5/5 OK, `npm.cmd run lint` OK, `npm.cmd run build` OK y `wwwroot` sincronizado.

## 2026-05-02 - V-01.05 - Graficas de barras desalineadas en dashboards de cuentas y titulares

- Contexto: en los dashboards embebidos de `Cuentas` y `Titulares`, la grafica de barras aparecia desplazada hacia la derecha dentro de su tarjeta.
- Causa: ambos `BarChart` reservaban `120px` para el `YAxis` y formateaban ticks con moneda completa, inflando el carril del eje igual que ocurrio antes con `EvolucionChart`.
- Solucion aplicada: ambos charts usan margenes explicitos, `YAxis` de `72px`, ticks compactos con `formatCompactCurrency`, `tickMargin` y ejes visuales simplificados.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy` OK y Playwright headless confirma `gridStartX=72px` en `/titulares` y `/cuentas`.

## 2026-05-01 - V-01.05 - Grafica Evolucion desalineada en dashboard principal

- Contexto: en el dashboard principal, la grafica `Evolucion` aparecia desplazada hacia la derecha dentro de su tarjeta.
- Causa: `EvolucionChart` reservaba `116px` para el `YAxis`, demasiado para etiquetas compactas como `4 EUR`, y Recharts desplazaba el area de trazado.
- Solucion aplicada: `LineChart` usa margenes explicitos, `YAxis` pasa a `72px` y ambos ejes usan `tickMargin` para conservar separacion sin inflar el layout.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy` OK y Playwright headless confirma `plotInsetFromLegend=72px`.

## 2026-05-01 - V-01.05 - Logo del login desalineado con la tarjeta

- Contexto: en la pantalla de login, el bloque `Atlas Balance` aparecia pegado al margen izquierdo mientras la tarjeta `Iniciar sesion` estaba centrada.
- Causa: `.auth-logo-container` usaba un ancho maximo de `1120px`, heredado de un layout ancho, en una pantalla que realmente funciona como columna centrada de autenticacion.
- Solucion aplicada: el contenedor del logo adopta el mismo ancho visual que la tarjeta (`430px` con margen responsive) y centra el bloque de marca completo.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy` OK y Edge headless confirma `brandDeltaCard=0px`.

## 2026-05-01 - V-01.05 - KPI principal del dashboard se solapaba con saldos por divisa

- Contexto: durante la verificacion visual Playwright del rediseño UI/UX, el importe de `Saldo total` en desktop se extendia por debajo de la tarjeta `Saldos por divisa`.
- Causa: el nuevo `dashboard-command-grid` dejaba la primera columna demasiado estrecha para un importe financiero grande renderizado con fuente mono y escala KPI.
- Solucion aplicada: se amplia el ancho minimo de la columna KPI y se limita el tamano maximo del numero destacado en el contexto `dashboard-kpi-grid--command`.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK y Playwright confirma `kpiOverlapsDivisa=false`, sin overflow horizontal en desktop/mobile.

## 2026-05-01 - V-01.05 - Test de dashboard fallaba en los primeros dias del mes

- Contexto: durante la verificacion amplia del hardening de seguridad, `DashboardServiceTests.GetPrincipalAsync_Should_Aggregate_CurrentBalances_And_PeriodFlows_In_TargetCurrency` esperaba ingresos `252`, pero obtenia `132`.
- Causa: el test creaba un movimiento USD en `monthStart.AddDays(2)`. Si se ejecutaba el dia 1 o 2 del mes, ese movimiento quedaba en el futuro y el servicio no lo contabilizaba.
- Solucion aplicada: el test usa `today` para los movimientos del mes actual que deben contarse, manteniendo el movimiento anterior al periodo fuera del calculo.
- Verificacion: backend sin Testcontainers 115/115 OK.

## 2026-05-01 - V-01.05 - PostgreSQL no aplicaba Row Level Security

- Contexto: se pidio comprobar y despues activar Row Level Security. La base local y el codigo no tenian politicas RLS.
- Causa: el aislamiento por cuenta estaba implementado en backend, pero no en PostgreSQL. Ademas, el Docker de desarrollo creaba `app_user` como superusuario al usarlo como `POSTGRES_USER`, lo que hace inutil cualquier prueba seria de RLS.
- Solucion aplicada: migraciones EF Core con `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY`, politicas sobre tablas sensibles y firma HMAC del contexto; interceptor EF Core que fija contexto `atlas.*`; middleware de integraciones ajustado para exponer el token validado; Docker e instalador endurecidos para separar owner/migracion de runtime sin `BYPASSRLS`.
- Verificacion: `RowLevelSecurityTests` OK; tests focalizados RLS/permisos/integraciones 15/15 OK; migraciones aplicadas en `atlas_balance_db`; catalogo local con 11 tablas objetivo protegidas, 20 politicas, `app_user` sin superusuario ni `BYPASSRLS`, secreto RLS sembrado, `context_is_valid=false` ante firma invalida y `context_is_valid=true` ante firma valida.

## 2026-04-26 - V-01.05 - Importacion reordenaba lineas por fecha antes de guardar

- Contexto: al confirmar una importacion, el backend no respetaba estrictamente el orden de lineas pegadas. Ordenaba por fecha y luego por indice, lo que podia separar lineas informativas o alterar la lectura del extracto cuando el banco ya entrega la secuencia correcta.
- Causa: `ImportacionService.ConfirmarAsync` aplicaba `.OrderBy(item => item.Fecha).ThenBy(item => item.Row.Indice)` antes de asignar `fila_numero`.
- Solucion aplicada: se elimina el ordenamiento por fecha y se asigna `fila_numero` desde la ultima linea pegada hacia la primera, dejando la linea superior como la de numero mas alto del lote. La auditoria vuelve a registrar primeras filas por indice original.
- Verificacion: `ImportacionServiceTests` 26/26 OK y `dotnet build GestionCaja.API -c Release --no-restore` OK.

## 2026-04-26 - V-01.05 - Importacion bloqueaba filas con saldo pero sin fecha ni importe

- Contexto: al validar un extracto, varias filas informativas de beneficiario/desglose traian concepto y saldo, pero dejaban vacios fecha e importe. La UI las mostraba como errores (`Monto vacio | Fecha vacia`) y desactivaba su importacion.
- Causa: la regla de fila informativa solo se activaba cuando tambien faltaba el saldo. Si el banco informaba saldo en esa linea, el backend la consideraba parcialmente rota.
- Solucion aplicada: `ImportacionService.ValidateRows` permite filas con concepto, fecha vacia e importe vacio aunque traigan saldo; se importan con monto `0`, fecha heredada de la ultima fila valida anterior y saldo conservado si es numerico.
- Verificacion: `ImportacionServiceTests` 26/26 OK y `dotnet build GestionCaja.API -c Release` OK.

## 2026-04-26 - V-01.05 - AlertBanner ocupaba altura completa en algunas vistas

- Contexto: en Configuracion, Backups, Papelera y Dashboards el banner superior de alertas de saldo bajo aparecia exageradamente alto respecto al resto de banners de estado.
- Causa: `app-main` usaba `grid-template-rows: var(--topbar-height) 1fr`; al renderizar `<AlertBanner />` entre topbar y contenido, el auto-placement de CSS Grid asignaba la fila `1fr` al banner y desplazaba el contenido a una fila implicita.
- Solucion aplicada: `app-main` pasa a tres filas (`var(--topbar-height) auto minmax(0, 1fr)`) con asignacion explicita de fila para `.app-topbar`, `.alert-banner` y `.app-content`; el mismo ajuste se replica en mobile. Se agrega `align-self: start` en `.alert-banner` y guard rails en `.app-main > .alert-banner` (`align-self: start`, `min-height: 0`, `height: auto`) para bloquear estirado residual.
- Comprobacion global: barrido del frontend confirma que `AlertBanner` solo se monta una vez en `Layout`, por lo que la correccion cubre todas las rutas no embebidas.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK (codigo `1` esperado).

## 2026-04-26 - V-01.05 - Actualizacion V-01.04 dejaba API parada por wrapper y seed duplicado

- Contexto: al actualizar una instalacion `V-01.03` con el paquete `AtlasBalance-V-01.04-win-x64`, `update.cmd -InstallPath C:\AtlasBalance` paso mal los argumentos y el fallback directo a `Actualizar-AtlasBalance.ps1` copio binarios pero la API no arranco.
- Causa: `scripts/update.ps1` reenviaba parametros mediante `ValueFromRemainingArguments`, fragil para `-InstallPath`; ademas `SeedData.EnsureDefaultFormatosImportacion` solo comprobaba banco/divisa antes de insertar defaults con IDs fijos, por lo que filas legacy con el mismo `id` pero banco/divisa distintos provocaban `23505 pk_formatos_importacion`.
- Solucion aplicada: `update.ps1` declara explicitamente `-InstallPath` y `-SkipBackup` y reenvia esos parametros al actualizador; `SeedData` comprueba primero si el ID fijo ya existe con `IgnoreQueryFilters()` antes de insertar por banco/divisa.
- Verificacion: agregada regresion `Initialize_Should_Not_Duplicate_Default_Format_When_Fixed_Id_Already_Exists`; parser PowerShell de scripts de actualizacion OK, `SeedDataTests` 5/5 OK y paquete `V-01.05` regenerado.

## 2026-04-25 - V-01.05 - Hallazgos de auditoria corregidos antes de release

- Contexto: la auditoria de uso, bugs y seguridad detecto tres problemas que no eran aceptables para cerrar version: Tailwind/shadcn reintroducidos contra el stack canonico, contrato duplicado de resumen de cuenta sin metadatos de plazo fijo y controles propios con soporte de teclado incompleto.
- Causa: se mezclo una capa UI externa con el sistema de CSS variables propio, el endpoint historico de cuentas quedo por detras del resumen rico usado por el dashboard, y los controles custom no cerraron todo el contrato de accesibilidad al reemplazar controles nativos.
- Solucion aplicada: se eliminaron dependencias/configuracion/imports Tailwind/shadcn y `components.json`; `CuentasController.Resumen` ahora devuelve titular, tipo de cuenta, notas, ultima actualizacion y `plazo_fijo`; `DatePickerField`, `ConfirmDialog` y `AppSelect` mejoran etiquetas, navegacion de teclado y focus trap.
- Verificacion: busqueda sin restos directos de Tailwind/shadcn, `npm.cmd run lint` OK, `npm.cmd run build` OK, `wwwroot` sincronizado, `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, NuGet vulnerable sin hallazgos y `dotnet test ...GestionCaja.API.Tests.csproj -c Release` 108/108 OK.

## 2026-04-25 - V-01.05 - Gradientes decorativos marcados como deuda visual

- Contexto: la auditoria marco fondos con `radial-gradient` y degradados suaves en login, layout y tarjetas como residuos de UI generica.
- Causa: la capa de coherencia visual habia introducido decoracion de fondo que no aporta informacion y contradice el criterio de superficies sobrias del proyecto.
- Solucion aplicada: se sustituyeron esos fondos por tokens planos (`var(--bg-app)`, `var(--bg-surface-soft)`, `var(--bg-surface)` y mezclas solidas). Se dejaron intactos los degradados funcionales de flecha de `select` y shimmer de skeleton.
- Verificacion: busqueda posterior solo encontro degradados funcionales, `npm.cmd run lint` OK y `npm.cmd run build` OK.

## 2026-04-25 - V-01.05 - Endpoints nuevos respondian 500 ante body o listas null

- Contexto: en una pasada extra de auditoria sobre los endpoints añadidos en V-01.05 (`POST /api/alertas`, `PUT /api/alertas/{id}`, `POST /api/cuentas/{id}/plazo-fijo/renovar` y `POST /api/importacion/plazo-fijo/movimiento`), se detecto que ninguno comprobaba que el cuerpo deserializado no fuera null y que `SaveAlertaSaldoRequest.DestinatarioUsuarioIds` se accedia directamente con `.Count` aunque deserializar `"destinatario_usuario_ids": null` deja la propiedad en null.
- Causa: los DTOs nuevos solo definian valor por defecto `= []`, pero el inicializador no se aplica cuando el JSON envia explicitamente `null`. Ningun controlador validaba previamente el cuerpo.
- Solucion aplicada: `if (request is null) return BadRequest(new { error = "Request invalido" });` al inicio de los endpoints afectados y `request.DestinatarioUsuarioIds ?? []` antes de validar/procesar destinatarios.
- Verificacion: `dotnet build -c Release` OK, `dotnet test --no-build` 107/107 OK, `dotnet list package --vulnerable --include-transitive` sin hallazgos, `npm audit` 0 vulnerabilidades.

## 2026-04-25 - V-01.05 - Manifiesto frontend mantenia minimos vulnerables pese a lockfile seguro

- Contexto: durante la auditoria de seguridad V-01.05, `npm ls` confirmo que el lockfile resolvia `axios@1.15.0` y `react-router-dom@6.30.3`, pero `package.json` seguia declarando `axios ^1.7.9` y `react-router-dom ^6.28.0`.
- Causa: actualizaciones previas habian dejado el lockfile en versiones seguras, pero no elevaron los rangos minimos declarados en el manifiesto.
- Solucion aplicada: se actualizo el manifiesto a `axios ^1.15.2` y `react-router-dom ^6.30.3`; el lockfile queda regenerado con `axios@1.15.2`.
- Verificacion: `npm.cmd audit --audit-level=moderate` 0 vulnerabilidades, `npm.cmd run lint` OK, `npm.cmd run build` OK, `dotnet test ... --no-build` 107/107 OK, NuGet vulnerable sin hallazgos y `wwwroot` sincronizado.

## 2026-04-25 - V-01.05 - Popup nativo de fecha no podia igualarse al diseno Atlas

- Contexto: aunque el campo cerrado de fecha ya tenia mejor estilo, al abrir el calendario seguia apareciendo el selector nativo del navegador, fuera del sistema visual de Atlas.
- Causa: el popup interno de `input type="date"` no es estilizables de forma consistente entre navegadores/OS; CSS solo alcanza el campo cerrado y parte del indicador WebKit.
- Solucion aplicada: se reemplazaron los `input type="date"` del frontend por `DatePickerField`, un selector propio con popover, dias, mes, navegacion, estado seleccionado/hoy, acciones `Hoy`/`Limpiar` y posicionamiento hacia arriba cuando no cabe debajo.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK, `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK y comprobacion visual en navegador de `/cuentas` sin errores de consola.

## 2026-04-25 - V-01.05 - Dashboard de cuenta no mostraba vencimiento de plazo fijo

- Contexto: en el detalle de una cuenta `PLAZO_FIJO`, el usuario veia saldo, periodo, notas y desglose, pero no la fecha en la que vence el plazo fijo.
- Causa: el endpoint `/api/extractos/cuentas/{id}/resumen` no devolvia `tipo_cuenta` ni el bloque `plazo_fijo`; la UI de detalle no tenia dato que pintar.
- Solucion aplicada: el resumen de cuenta devuelve `TipoCuenta` y `PlazoFijoResponse` para cuentas de plazo fijo; `CuentaDetailPage` muestra vencimiento, dias restantes/vencido y estado bajo el titulo de la cuenta.
- Verificacion: backend Release build OK, `npm.cmd run lint` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.

## 2026-04-25 - V-01.05 - Date picker de plazo fijo no seguia el sistema visual

- Contexto: en el formulario de cuentas de tipo `PLAZO_FIJO`, los campos de fecha de inicio/vencimiento usaban `input type="date"` nativo y el selector de calendario no se veia como el resto de campos.
- Causa: los estilos globales cubrian inputs/selects, pero no ajustaban `color-scheme`, partes internas WebKit ni el indicador `::-webkit-calendar-picker-indicator` de los controles de fecha.
- Solucion aplicada: se agregaron reglas globales para `input[type='date']`, `::-webkit-datetime-edit`, `::-webkit-calendar-picker-indicator` y modo oscuro, manteniendo el popup nativo del navegador.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.

## 2026-04-25 - V-01.05 - Tests backend bloqueados por API Debug en ejecucion

- Contexto: al ejecutar `dotnet test` tras modificar importacion/dashboard, MSBuild no pudo copiar `GestionCaja.API.exe` ni `GestionCaja.API.dll` en `bin\Debug\net8.0`.
- Causa: habia un proceso local `GestionCaja.API` ejecutandose desde `backend/src/GestionCaja.API/bin/Debug/net8.0`, bloqueando los artefactos.
- Solucion aplicada: se identifico el PID con `Get-Process`, se detuvo el proceso local y se repitieron los tests.
- Verificacion: `dotnet test ... --filter "ImportacionServiceTests|DashboardServiceTests"` paso 28/28 y `dotnet build ... -c Release` paso sin warnings.

## 2026-04-25 - V-01.05 - Implementacion plazo fijo detecto rotura TypeScript y lint estricto

- Contexto: al compilar frontend tras agregar campos de plazo fijo, `tsc` fallo en `CuentasPage.tsx` por un cierre JSX sobrante. Despues, `npm.cmd run lint` fallo por `react-refresh/only-export-components` en `components/ui/button.tsx` porque el proyecto usa `--max-warnings 0`.
- Causa: el bloque condicional de plazo fijo dejo un `)}` duplicado; el warning de lint era una regla estricta sobre un componente UI que exporta tambien `buttonVariants`.
- Solucion aplicada: se elimino el cierre sobrante y se agrego una excepcion local de ESLint en `button.tsx` para mantener el contrato del componente sin mover archivos ahora.
- Verificacion: `npm.cmd run lint` OK y `npm.cmd run build` OK.

## 2026-04-25 - V-01.05 - Actualizador post-instalacion incompleto

- Contexto: una vez instalada la aplicacion, el flujo de actualizacion manual desde paquete no dejaba la instalacion preparada para futuras actualizaciones y no validaba salud real de la API tras reemplazar binarios.
- Causa: `update.cmd`/`update.ps1` seguian el patron inicial de wrapper minimo; `Actualizar-AtlasBalance.ps1` actualizaba API/Watchdog, pero no refrescaba scripts instalados ni `atlas-balance.runtime.json`, y no hacia health check con `curl.exe -k`.
- Solucion aplicada: `update.ps1` valida paquete antes de autoelevar y soporta `-PackagePath`; el actualizador copia scripts/wrappers operativos a la instalacion, actualiza `VERSION`/runtime, conserva configuracion, mantiene backup/rollback y falla si `/api/health` no responde tras arrancar.
- Mitigacion operativa: para actualizar desde un paquete nuevo, ejecutar `.\update.cmd -InstallPath C:\AtlasBalance` en la carpeta descomprimida; en instalaciones ya actualizadas se puede usar `C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-XX-win-x64 -InstallPath C:\AtlasBalance`.

## 2026-04-25 - V-01.05 - Incidencias de instalacion Windows Server 2019 cerradas en scripts

- Contexto: la instalacion real en Windows Server 2019 detecto confusion entre repo fuente y paquete release, wrappers fragiles, dependencia poco fiable de `winget`, falsos negativos de `Invoke-WebRequest`, credenciales iniciales falsas al reinstalar sobre BD existente y necesidad de reset admin soportado.
- Causa: el flujo operativo mezclaba documentacion de desarrollo con instalacion de servidor; el instalador asumia demasiadas cosas felices: carpeta correcta, PostgreSQL automatico, BD nueva y health check PowerShell fiable.
- Solucion aplicada: `install.ps1` e `Instalar-AtlasBalance.ps1` validan paquete release antes de instalar; `install.cmd`/`Instalar Atlas Balance.cmd` devuelven codigo de salida; el instalador detecta usuarios existentes y no genera password admin falsa; se agrega `Reset-AdminPassword.ps1`; `Build-Release.ps1` incluye scripts operativos nuevos; el health check usa `curl.exe -k` como prueba primaria.
- Mitigacion operativa: si la BD ya existe y no se conoce el admin, ejecutar `scripts\Reset-AdminPassword.ps1` desde la instalacion; si `curl.exe -k` responde pero el navegador no, instalar `atlas-balance.cer` como raiz confiable en el cliente.

## 2026-04-25 - V-01.05 - Reinstalacion falla por password HTTPS desalineada

- Contexto: en Windows Server 2019, tras reinstalar `V-01.03`, `AtlasBalance.API` quedaba detenido y el visor de eventos mostraba `System.Security.Cryptography.CryptographicException: La contraseña de red especificada no es válida` al cargar `atlas-balance.pfx`.
- Causa: `Instalar-AtlasBalance.ps1` reutilizaba `C:\AtlasBalance\certs\atlas-balance.pfx` si ya existia, pero generaba una password HTTPS nueva y la escribia en `appsettings.Production.json`. Eso dejaba certificado viejo con password nueva.
- Solucion aplicada: el instalador `V-01.05` elimina `atlas-balance.pfx` y `atlas-balance.cer` existentes antes de generar el certificado nuevo, garantizando que la password configurada y el PFX coincidan.
- Mitigacion operativa para instalaciones afectadas: detener `AtlasBalance.API`, borrar `C:\AtlasBalance\certs\atlas-balance.pfx` y `C:\AtlasBalance\certs\atlas-balance.cer`, y relanzar `scripts\Instalar-AtlasBalance.ps1` directamente desde el paquete.

## 2026-04-25 - V-01.05 - Modal de importacion rechazado por cabeceras anti-frame

- Contexto: en produccion, desde el dashboard de cuenta, el modal `Importar movimientos` mostraba un panel gris con icono de documento roto/rechazo de conexion.
- Causa: el frontend cargaba `/importacion` dentro de un `iframe`, pero la API aplicaba `X-Frame-Options: DENY` y `Content-Security-Policy: frame-ancestors 'none'` a todas las rutas, bloqueando incluso iframes same-origin.
- Solucion aplicada: las cabeceras pasan a `X-Frame-Options: SAMEORIGIN` y `frame-ancestors 'self'`, permitiendo solo embebidos del mismo origen y manteniendo bloqueado el clickjacking externo.
- Mitigacion operativa para `V-01.03` ya instalado: parchear el bundle servido para que el boton de importacion navegue a `/importacion` en pagina completa o publicar un paquete nuevo con la correccion.

## 2026-04-25 - V-01.03 - Auditoria profunda de seguridad y hardening aplicado

### Sesiones no revocadas tras reset/cambio de password

- Contexto: el reset admin y algunos cambios de estado cambiaban credenciales o usuario sin invalidar sesiones ya emitidas.
- Causa: JWT sin estado de sesion y refresh tokens activos aunque el password cambiara.
- Solucion aplicada: `SecurityStamp` en usuario, claim en access token, validacion en `UserStateMiddleware`, migracion `UserSessionHardening`, rotacion de stamp y revocacion de refresh tokens en cambio/reset/delete y reuse.

### Login y bearer de integracion con rate limit incompleto

- Contexto: login exponia diferencias utiles para enumeracion/bloqueo y la integracion OpenClaw consultaba BD antes de limitar bearer invalido repetido.
- Causa: bloqueo de cuenta demasiado distinguible y rate limit aplicado tarde.
- Solucion aplicada: respuesta generica para bloqueos, throttle por cliente/email antes de insistir, umbral de bloqueo global mas alto, y rate limit por IP/minuto para bearer invalido antes de consultar tokens.

### URL de actualizaciones y rutas configurables demasiado permisivas

- Contexto: `app_update_check_url`, `backup_path`, `export_path`, rutas de descarga/exportacion y rutas Watchdog pasaban por normalizacion que podia ocultar entradas relativas o destinos no oficiales.
- Causa: confianza excesiva en configuracion admin y `Path.GetFullPath` usado antes de validar si la ruta era explicitamente absoluta.
- Solucion aplicada: allowlist HTTPS estricta a `github.com/AtlasLabs797/AtlasBalance` o `api.github.com/repos/AtlasLabs797/AtlasBalance/...`; validacion de rutas crudas antes de normalizar; bloqueo de traversal y rutas relativas.

### Dependencia frontend vulnerable

- Contexto: `npm audit` marco `postcss <8.5.10` como vulnerabilidad moderada de XSS en serializacion CSS.
- Causa: dependencia transitiva resuelta a `postcss@8.5.9`.
- Solucion aplicada: `npm.cmd update postcss`, lockfile resuelto a `postcss@8.5.10`.

### Credenciales iniciales one-shot persistentes

- Contexto: `INSTALL_CREDENTIALS_ONCE.txt` quedaba con ACL restringida, pero podia sobrevivir si nadie lo borraba.
- Causa: flujo operativo manual.
- Solucion aplicada: el instalador registra una tarea programada SYSTEM para borrar el archivo automaticamente a las 24 horas.

## 2026-04-20 - V-01.02 - Release sin scripts one-click completos

- Contexto: la carpeta de release solo conservaba `.gitkeep` hasta generar paquete, y el empaquetado no incluia scripts `install/update/uninstall/start` con esos nombres.
- Causa: existian wrappers historicos en espanol y scripts parciales, pero faltaba el contrato operativo pedido para release autonoma.
- Solucion aplicada: creados wrappers `install.cmd`, `update.cmd`, `uninstall.cmd`, `start.cmd` y sus scripts PowerShell; `Build-Release.ps1` los copia al paquete.

## 2026-04-20 - V-01.02 - Arranque no levantaba PostgreSQL gestionado

- Contexto: `Launch-AtlasBalance.ps1` arrancaba Watchdog y API, pero no la base de datos.
- Causa: el script asumio que PostgreSQL ya estaba activo como dependencia externa.
- Solucion aplicada: el runtime registra `ManagedPostgres` y `PostgresServiceName`; `start` y `update` arrancan PostgreSQL gestionado antes de tocar backend/API.

## 2026-04-20 - V-01.02 - `setup-https.ps1` no parseaba en PowerShell

- Contexto: la validacion sintactica de scripts detecto error de cadena sin terminar en `scripts/setup-https.ps1`.
- Causa: archivo con texto mojibake/codificacion rota.
- Solucion aplicada: reescritura ASCII del script, manteniendo su funcion de desarrollo con `mkcert` y mensajes claros.

## 2026-04-20 - V-01.02 - Auditoria tecnica profunda

### Secretos de configuracion persistidos en claro

- Contexto: `smtp_password` y `exchange_rate_api_key` se guardaban como texto plano en la tabla `CONFIGURACION`.
- Causa: la pantalla de configuracion persistia valores sensibles igual que parametros normales.
- Solucion aplicada: `ISecretProtector` con Data Protection, prefijo `enc:v1:`, migracion automatica de valores legacy en arranque y lectura descifrada solo en SMTP/tipos de cambio.

### Permiso global de dashboard ampliaba indebidamente el alcance de datos

- Contexto: un permiso global con `PuedeVerDashboard` podia activar `HasGlobalAccess` y abrir consultas de cuentas/titulares/exportaciones.
- Causa: `UserAccessService` mezclaba permiso de visualizacion de dashboard con permiso global de datos.
- Solucion aplicada: `HasGlobalAccess` solo se concede por permisos globales de datos (`agregar`, `editar`, `eliminar`, `importar`). Se anadio test de regresion.

### Descarga de exportaciones confiaba en ruta guardada en BD

- Contexto: `ExportacionesController.Descargar` abria la ruta persistida si el usuario tenia acceso a la cuenta.
- Causa: faltaba comprobar raiz permitida y extension.
- Solucion aplicada: se bloquea cualquier descarga fuera de `export_path` y cualquier fichero que no sea `.xlsx`.

### Watchdog podia quedar expuesto por configuracion de URLs

- Contexto: Watchdog anadia `http://localhost:5001` con `app.Urls`, pero Kestrel podia recibir overrides externos.
- Causa: binding menos estricto del necesario para un servicio administrativo.
- Solucion aplicada: `ConfigureKestrel` fuerza `ListenLocalhost(5001)`.

### `AllowedHosts` permisivo en produccion

- Contexto: configuracion base/plantilla permitia `AllowedHosts="*"`.
- Causa: default comodo heredado de desarrollo.
- Solucion aplicada: fuera de Development la API rechaza `AllowedHosts` vacio, placeholder o wildcard; instalador escribe `$ServerName;localhost`.

### Artefactos locales con cookies/cabeceras/payloads de login

- Contexto: quedaban ficheros auxiliares de smoke/login y logs temporales fuera de Git.
- Causa: ejecuciones manuales dejaron outputs con informacion sensible o de sesion.
- Solucion aplicada: eliminados logs API temporales y artefactos de login/cookies/cabeceras en `Otros/Auxiliares/artifacts`.

## 2026-04-20 - V-01.02

### Typos activos en email, rutas y evento interno

- Contexto: la revision V-01.02 marcaba `atlasbalnace` y `atlas-blance`; el codigo principal ya estaba corregido, pero quedaban restos activos en `appsettings`, plantillas, scripts, placeholders, tests y evento de importacion.
- Causa: se corrigieron algunos literals del backend, pero no se hizo barrido completo sobre archivos versionables ni bundle servido.
- Solucion aplicada: normalizacion a `atlasbalance`/`atlas-balance`, rutas `C:/AtlasBalance`, constante compartida `IMPORTACION_COMPLETADA_EVENT` y rebuild/copias de `wwwroot`.

### Version `V-01.01` residual en instalador y documentacion de paquete

- Contexto: `Instalar-AtlasBalance.ps1` seguia escribiendo `V-01.01` en runtime y `Documentacion/documentacion.md` describia el paquete `V-01.01`.
- Causa: el cambio de version runtime no alcanzo scripts de instalacion ni documentacion de usuario.
- Solucion aplicada: instalador, comandos y documentacion de paquete actualizados a `V-01.02`.

### Bundle frontend servido desactualizado

- Contexto: `frontend/src` ya tenia fixes para CSRF, refresh concurrente y contador de alertas, pero `backend/src/GestionCaja.API/wwwroot` conservaba bundles antiguos ignorados por Git.
- Causa: se compilo frontend sin sincronizar siempre el resultado con el `wwwroot` que sirve la API local.
- Solucion aplicada: `npm.cmd run build`, limpieza segura de `wwwroot` y copia de `frontend/dist`; barrido final sin restos de typos/version antigua.

### Secretos de desarrollo en configuracion versionable

- Contexto: la auditoria con `cyber-neo` y revision manual detecto credenciales/defaults de desarrollo en configuracion versionable.
- Causa: valores comodos para bootstrap quedaron en archivos base.
- Solucion aplicada: `appsettings.json`, Watchdog y `docker-compose.yml` ya no incluyen secretos reales; se añadieron plantillas y `.env.example`; `SeedAdmin:Password` debe configurarse antes del primer arranque.

### Textos mojibake en importacion y correo SMTP

- Contexto: errores como "Indice", "Fecha vacia" y el asunto SMTP aparecian con caracteres rotos.
- Causa: cadenas arrastradas con codificacion incorrecta.
- Solucion aplicada: se corrigieron las cadenas y los tests que esperaban texto roto.

### Version `V-01.01` residual

- Contexto: `SeedData` insertaba `app_version = V-01.01` y el check de actualizacion enviaba User-Agent viejo.
- Causa: valores literales no actualizados al pasar a `V-01.02`.
- Solucion aplicada: seed inicial usa `V-01.02`; User-Agent usa la version runtime resuelta desde assembly.

### `npm.ps1` bloqueado por ExecutionPolicy

- Contexto: `npm audit`, `npm run lint` y `npm run build` fallan si se invoca `npm` desde PowerShell.
- Causa: PowerShell bloquea `C:\Program Files\nodejs\npm.ps1`.
- Solucion aplicada: usar `npm.cmd` en este entorno.

### Tests con Testcontainers bloqueados por Docker no disponible

- Contexto: la suite completa falla en `ExtractosConcurrencyTests` si Docker no esta arrancado/configurado.
- Causa: `PostgresFixture` necesita Docker/Testcontainers.
- Solucion aplicada: para verificacion local sin Docker se ejecuto `dotnet test ... --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`. En auditoria posterior con Docker disponible, la suite completa quedo en 83/83 OK.

### Estado Git local no fiable

- Contexto: `git status --short` ya responde, pero lista practicamente todo el arbol como `untracked`.
- Causa probable: copia local/repo recreado sin historial o indice util para esta carpeta.
- Solucion aplicada: no se modifico `.git`; reparar el estado Git requiere decision explicita para recrear o relinkar correctamente la copia.

### Frontend aparentemente caido por API sin cadena de conexion

- Contexto: la app parecia "no funcionar", pero el frontend compilaba/renderizaba; al arrancar API se rompia en startup.
- Causa: `ConnectionStrings:DefaultConnection` vacia en [appsettings.json](C:/Proyectos/Atlas%20Balance%20Dev/Atlas%20Balance/backend/src/GestionCaja.API/appsettings.json:3), provocando `Host can't be null` al ejecutar migraciones en [Program.cs](C:/Proyectos/Atlas%20Balance%20Dev/Atlas%20Balance/backend/src/GestionCaja.API/Program.cs:152).
- Solucion aplicada: diagnostico confirmado ejecutando API con y sin `ConnectionStrings__DefaultConnection`; sin valor falla por host nulo, con valor pasa a autenticar contra PostgreSQL (fallo esperado si password no coincide). Verificar que el entorno de ejecucion tenga cadena de conexion valida antes de levantar backend.

### Password de PostgreSQL desalineada con el contenedor activo

- Contexto: tras configurar cadena de conexion local, la API seguia fallando con `28P01: password authentication failed for user "app_user"`.
- Causa: el contenedor `atlas_balance_db` ya existente estaba inicializado con una password distinta a la configuracion local nueva.
- Solucion aplicada: se sincronizo la configuracion local de desarrollo (`.env` y `appsettings.Development.json`, ambos fuera de Git) con las credenciales reales del contenedor activo y la API quedo operativa (`/api/health` HTTP 200).

## 2026-04-20 - V-01.01

### `rg.exe` bloqueado por acceso denegado

- Contexto: al listar archivos del proyecto, `rg --files` fallo con `Acceso denegado`.
- Causa probable: binario `rg.exe` no ejecutable desde este entorno.
- Solucion aplicada: usar PowerShell (`Get-ChildItem` y `Select-String`) para inspeccion y busqueda.

### Raiz inicial sin repositorio Git

- Contexto: `git status` en `C:\Proyectos\Atlas Balance` devolvio que no era repositorio.
- Causa: el repositorio real estaba anidado en `atlas-blance-scaffolding/atlas-blance`.
- Solucion aplicada: mover la app a `Atlas Balance` y dejar `.git` en la raiz para que tambien se versionen `Documentacion` y la configuracion de GitHub.

### `DOCUMENTACION_CAMBIOS.md` no era UTF-8 valido

- Contexto: `apply_patch` no pudo modificar el archivo por una secuencia UTF-8 invalida.
- Causa probable: mezcla historica de codificaciones.
- Solucion aplicada: tratar ese archivo con PowerShell usando lectura de codificacion del sistema y reescritura UTF-8 cuando hizo falta actualizarlo.

### Regex invalida al filtrar `git status`

- Contexto: un `Select-String -Pattern` con barras invertidas sin escapar fallo al buscar `bin/obj/dist` en el estado Git.
- Causa: PowerShell interpreto `\o` como secuencia regex invalida.
- Solucion aplicada: repetir la busqueda con `Select-String -SimpleMatch`.

### Carpeta `Skills` con duplicados por agente

- Contexto: el inventario de `Skills` mostro muchas copias de la misma skill en `.agents`, `.codex`, `.claude`, `.cursor`, `.gemini`, etc.
- Causa: varios paquetes instalan la misma skill para multiples agentes.
- Solucion aplicada: documentar rutas canonicas en `Documentacion/SKILLS_LOCALES.md` y ordenar a los agentes que no traten cada copia como una skill distinta.

### Whitespace en release y documento de paleta

- Contexto: `git diff --cached --check` detecto trailing whitespace en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.01-win-x64/api/wwwroot/index.html` y `Documentacion/Diseno/Diseño/Palesta Y tipografia.txt`.
- Causa probable: archivos generados o movidos con espacios finales heredados.
- Solucion aplicada: limpieza mecanica de espacios finales en ambos archivos, re-stage y repeticion de `git diff --cached --check` sin errores.

### Advertencia GitHub por ZIP grande

- Contexto: `git push -u origin V-01.01` subio correctamente, pero GitHub aviso que `AtlasBalance-V-01.01-win-x64.zip` pesa 97.49 MiB y supera el maximo recomendado de 50 MiB.
- Causa: se incluyo el paquete de release completo en Git porque la instruccion fue subir todo el proyecto salvo `Otros/` y `Skills/`.
- Solucion aplicada: se cambio la politica para mantener `Atlas Balance/Atlas Balance Release` fuera de Git salvo `.gitkeep` y publicar paquetes como assets de GitHub Releases.

### Release GitHub inmutable antes de subir asset

- Contexto: el primer intento de publicar `V-01.01` creo un release publicado antes de adjuntar el ZIP y la API devolvio `Cannot upload assets to an immutable release`.
- Causa: en este repositorio, un release publicado queda inmutable para subida posterior de assets.
- Solucion aplicada: publicar el paquete Windows x64 en un tag especifico `V-01.01-win-x64`, creando primero el release como draft, subiendo el asset y publicandolo despues. El draft untagged generado por el intento fallido se elimino. Tambien se elimino el tag remoto accidental `V-01.01` para evitar ambiguedad con la rama del mismo nombre.

### PR rechazado por historia Git sin ancestro comun

- Contexto: la API de GitHub rechazo el PR desde `V-01.01` a `main` con `The V-01.01 branch has no history in common with main`.
- Causa: `origin/main` habia sido forzado al commit inicial con `LICENSE`, dejando la rama de version sin ancestro comun con la rama base remota.
- Solucion aplicada: fusionar `origin/main` en `V-01.01` con `git merge --allow-unrelated-histories --no-edit origin/main`, incorporando `LICENSE` en raiz.

### 2026-04-20 - V-01.02 - Query params sensibles en auditoria de integracion

- Contexto: `IntegrationAuthMiddleware` serializaba todo `context.Request.Query` al registro de auditoria de integracion.
- Causa: la serializacion no distinguia claves sensibles; un cliente mal configurado podia meter `?token=...` o `?api_key=...` en la URL y quedar guardado en claro.
- Solucion aplicada: `HashSet` de claves sensibles (`token`, `api_key`, `apikey`, `secret`, `password`, `authorization`, `access_token`, `refresh_token`, `bearer`) y reemplazo por marcador `REDACTED` en la serializacion del registro de auditoria.

### 2026-04-20 - V-01.02 - `backup_path` y `export_path` sin validacion de traversal

- Contexto: ambos valores vienen de la tabla `CONFIGURACION` editable por admins. Se usaban directo en `Path.Combine`/`Directory.CreateDirectory`.
- Causa: faltaba validar ruta absoluta, caracteres invalidos y segmentos `..`. Un admin podia elegir una ruta relativa o apuntar a una carpeta fuera de la raiz prevista.
- Solucion aplicada: helper `ResolveSafeDirectory` que rechaza rutas no rooted, con caracteres invalidos o con segmentos `..`, aplicado en `BackupService.CreateBackupAsync` y `ExportacionService`.

### 2026-04-20 - V-01.02 - Email de usuarios borrados expuesto via integracion

- Contexto: `IntegrationOpenClawController` hacia `IgnoreQueryFilters()` al resolver `creado_por_id -> email` y devolvia emails de usuarios con `deleted_at != null`.
- Causa: necesidad de rellenar el historico incluso si el usuario ya no existe; se cargaba el email real del usuario borrado.
- Solucion aplicada: se sigue cargando la fila, pero si `deleted_at` no es nulo el email devuelto es el literal `usuario-eliminado`. Asi se mantiene el historico sin filtrar PII.

### 2026-04-20 - V-01.02 - Kestrel de desarrollo escuchando en todas las interfaces

- Contexto: `appsettings.Development.json` y su plantilla bindeaban `https://0.0.0.0:5000` con `AllowedHosts="*"`.
- Causa: comodin de desarrollo para acceso desde otros equipos de la LAN.
- Solucion aplicada: binding a `localhost` y `AllowedHosts=localhost` tambien en Development. Si hace falta LAN, hay que pedirlo explicito.

### 2026-04-20 - V-01.02 - `dotnet publish` empaquetaba secretos de desarrollo

- Contexto: `scripts/Build-Release.ps1` ejecuta `dotnet publish` sin exclusiones explicitas. Cualquier paquete generado por el script incluia `appsettings.Development.json` y las plantillas dentro de la carpeta `api` del release.
- Causa: los csproj de API y Watchdog no marcaban esos archivos con `CopyToPublishDirectory="Never"`.
- Solucion aplicada: `ItemGroup` con `Content Update="..." CopyToPublishDirectory="Never" ExcludeFromSingleFile="true"` para los tres ficheros en ambos csproj. Cualquier release futuro queda limpio de secretos de desarrollo.

## 2026-04-25 - V-01.05 - Importacion bloqueaba filas informativas con concepto pero sin fecha/monto/saldo

- Contexto: al validar extractos pegados desde banco, algunas filas con solo concepto aparecian como error (`Monto vacio`, `Fecha vacia`, `Saldo vacio`) y no podian importarse.
- Causa: la validacion trataba cualquier campo obligatorio vacio como error fatal, aunque esas filas fueran descripciones/informacion adicional exportada por el banco.
- Solucion aplicada: si una fila tiene concepto y deja vacios fecha, importe y saldo, se convierte en fila importable con advertencias: fecha y saldo se heredan de la ultima fila valida anterior y el monto se importa como `0`. Las filas ambiguas o parcialmente rotas siguen siendo errores.
- Verificacion: `dotnet test Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj --filter ImportacionServiceTests` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.

### 2026-04-20 - V-01.02 - Scripts smoke y docs historicas con credenciales

- Contexto: `phase2-smoke.ps1`, `phase2-smoke-curl.ps1`, `Otros/Raiz anterior/SPEC.md` y `CORRECCIONES.md` contenian passwords/usuarios concretos.
- Causa: artefactos antiguos de pruebas y planificacion quedaron con datos reales aunque viven en `Otros/` (fuera del repo principal, pero presentes en la maquina de trabajo).
- Solucion aplicada: los scripts leen las passwords de `ATLAS_SMOKE_ADMIN_PASSWORD`/`ATLAS_SMOKE_TEST_PASSWORD` (fallan si no existen). Los documentos historicos sustituyen los valores por placeholders.

## 2026-04-23 - V-01.03 - ExtractosController concedia alcance global con dashboard-only

- Contexto: `GET /api/extractos` usaba `GetAllowedAccountIds`, y ese helper concedia acceso a todas las cuentas si existia permiso global con `PuedeVerDashboard=true`.
- Causa: logica de autorizacion local mas permisiva que `UserAccessService`.
- Solucion aplicada: `GetAllowedAccountIds` y `CanViewTitular` ahora solo conceden alcance global con permisos de datos (`agregar`, `editar`, `eliminar`, `importar`), excluyendo `PuedeVerDashboard`.
- Verificacion: test de regresion en `ExtractosControllerTests` + ejecucion de `ExtractosControllerTests` y `UserAccessServiceTests` (8/8 OK).

## 2026-04-24 - V-01.03 - Frontend mostraba dashboards de cuenta a perfiles dashboard-only globales

- Contexto: tras cerrar la fuga de datos en extractos, el frontend seguia ofreciendo enlaces y botones a `/dashboard/cuenta/:id` desde `CuentasPage` y otras vistas, aunque el backend ya bloqueaba ese detalle para perfiles con permiso global solo de dashboard.
- Causa: `permisosStore.canViewCuenta` trataba cualquier fila global (`cuenta_id/titular_id null`) como acceso de cuenta, sin distinguir si era solo `PuedeVerDashboard`.
- Solucion aplicada: `canViewCuenta`, `canAddInCuenta`, `canEditCuenta`, `canDeleteInCuenta`, `canImportInCuenta`, `getColumnasVisibles` y `getColumnasEditables` pasan a ignorar filas globales `dashboard-only`; solo cuentan filas scopeadas de cuenta/titular o filas globales con acceso global de datos. `CuentasPage` muestra `Sin acceso` en vez de CTA operativos y `CuentaDetailPage` redirige al dashboard si recibe `403`.
- Verificacion: `npm.cmd run lint` OK, `npm.cmd run build` OK y `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR` OK.
