# Documentacion tecnica

## 2026-05-02 - V-01.05 - Fix de lockfile npm para CI GitHub

### Que cambio

- `Atlas Balance/frontend/package.json` declara `overrides.once = 1.4.0`.
- `Atlas Balance/frontend/package-lock.json` actualiza la entrada `node_modules/once` de `1.5.0` inexistente a `1.4.0`.
- No cambia codigo runtime ni bundle servido; es una correccion de reproducibilidad de instalacion.

### Por que

GitHub Actions ejecuta `npm ci` en entorno limpio. El lockfile versionado apuntaba a un tarball que npm no publica (`once-1.5.0.tgz`), por lo que CI fallaba antes de auditar, lintar o compilar.

### Verificacion

- `npm.cmd ci`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

## 2026-05-02 - V-01.05 - Paquete release Windows x64

### Que cambio

- Ejecutado `scripts/Build-Release.ps1 -Version V-01.05`.
- El script recompila frontend, sincroniza `frontend/dist` hacia `backend/src/GestionCaja.API/wwwroot`, publica API y Watchdog self-contained para `win-x64` y crea el paquete en `Atlas Balance/Atlas Balance Release`.
- Artefactos generados:
  - `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64`
  - `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64.zip`
- El ZIP queda fuera de Git por `.gitignore`; debe subirse como asset de GitHub Release.

### Verificacion

- `npm.cmd run build`: OK.
- `dotnet publish` API Release win-x64: OK.
- `dotnet publish` Watchdog Release win-x64: OK.
- SHA256 ZIP: `3E7A3ED22EFC4D18A161EA9D8D15CD9C12B3D51BDEF9AE38863767EC5CEAE299`.
- Tamano ZIP: `102350978` bytes.

### Pendiente operativo

- No se genero `AtlasBalance-V-01.05-win-x64.zip.sig` porque falta `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` en el entorno. Sin ese asset, el actualizador online falla cerrado.

## 2026-05-02 - V-01.05 - Cierre de hallazgos residuales del escaneo repo-wide

### Que cambio

- `Instalar-AtlasBalance.ps1` guarda credenciales iniciales en `C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt`.
- `Instalar-AtlasBalance.ps1` y `Reset-AdminPassword.ps1` protegen el directorio `config` con ACL `Administrators/SYSTEM` antes de escribir secretos; si `icacls` falla, no queda archivo de credenciales expuesto.
- `Reset-AdminPassword.ps1` exige ejecucion como Administrador.
- `ExtractosController.ToggleFlag` valida permisos por campo cambiado (`flagged` y `flagged_nota`).
- `DashboardService` ignora filas globales `PuedeVerDashboard` que no tengan permisos de datos; los dashboards de gerente quedan globales solo con alcance global real de datos o scopeados por titular/cuenta.
- `IntegrationOpenClawController.Auditoria` deja de usar `IgnoreQueryFilters()` al resolver extractos y no devuelve valores de auditoria de extractos soft-deleted.
- La politica RLS `exportaciones_write` pasa de `can_read_cuenta_by_id` a `can_write_cuenta_by_id`.
- `ImportacionPage` normaliza `returnTo` y solo acepta rutas internas que empiecen por `/`.
- CI y `docker-compose.yml` fijan `postgres:16-alpine` por digest OCI.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend.

### Verificacion

- `dotnet test ...GestionCaja.API.Tests.csproj -c Release --filter "ExtractosControllerTests|DashboardServiceTests|IntegrationOpenClawControllerTests|RowLevelSecurityTests" --no-restore`: 20/20 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Parser PowerShell de scripts de instalacion/reset/update: OK.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Hardening de seguridad repo-wide

### Que cambio

- `AuthService` usa `MaxFailedLoginAttempts = 5` y bloquea la cuenta aunque el throttle por cliente tambien se active.
- MFA acumula fallos por usuario durante una ventana de 15 minutos; crear un challenge nuevo ya no reinicia el contador efectivo.
- `IntegrationAuthMiddleware` redacta query params con normalizacion de clave y tambien valores con pinta de bearer/integration token.
- `ImportacionService` limita `ColumnasExtra` a 64, nombres a 80 caracteres, rechaza indices extra fuera de los datos y no persiste extras vacios.
- `UserAccessService` y `ExtractosController` solo derivan scope de datos desde flags de datos (`PuedeVerCuentas`, agregar, editar, eliminar, importar), no desde `PuedeVerDashboard`.
- `ExtractosController.Restaurar` requiere `CanDelete`, alineado con la accion de eliminar/restaurar.
- `CuentasController` y `ExtractosController` ocultan `CuentaReferenciaId/Nombre` de plazo fijo cuando la cuenta referencia no pasa scope o filtros de borrado para el usuario.
- `ActualizacionService` exige firma detached `.zip.sig` RSA/SHA-256 para updates online; `Build-Release.ps1` genera esa firma si existe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`.

### Configuracion nueva

```json
{
  "UpdateSecurity": {
    "ReleaseSigningPublicKeyPem": "-----BEGIN PUBLIC KEY-----..."
  }
}
```

Tambien se acepta `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM`. Para CI/release, `Build-Release.ps1` firma el ZIP si recibe `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM`. La clave privada no se documenta ni se guarda en repo. Si no hay clave publica o no existe el asset `.zip.sig`, el update online falla cerrado.

### Por que

El digest SHA-256 de GitHub Releases detecta corrupcion, no compromiso del canal de release. Si el atacante puede cambiar asset y metadata, puede cambiar ambos. La firma detached ancla el paquete a una clave fuera del canal de descarga. Lo demas son controles de autorizacion y brute-force que tenian que vivir en el backend, no solo en RLS o en UI.

### Verificacion

- Tests focalizados seguridad: 72/72 OK.
- Suite backend completa: 127/128; falla el harness RLS local por `permission denied for table __EFMigrationsHistory`.
- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- Parser PowerShell de scripts tocados: OK.

## 2026-05-02 - V-01.05 - Alineacion dinamica de EvolucionChart

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` introduce un calculo de ancho para el `YAxis`.
- El calculo revisa las etiquetas compactas de `ingresos`, `egresos` y `saldo` en todos los puntos.
- El eje queda limitado entre `44px` y `72px`.
- Todas las pantallas que renderizan evolucion heredan el ajuste porque usan el mismo componente: `/dashboard`, `/dashboard/titular/:id`, `/titulares` y `/cuentas`.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

Un ancho fijo de `72px` era aceptable para importes grandes, pero torpe para series pequenas como `4 EUR`: la grafica seguia arrancando demasiado a la derecha aunque las etiquetas no necesitaran ese espacio. La solucion correcta es adaptar el eje al contenido, con limites para no romper etiquetas largas.

### Reglas tecnicas

- No cambia contratos de API, permisos ni calculos financieros.
- No se introduce dependencia nueva.
- El tooltip conserva importes completos con `formatCurrency`.
- El eje sigue usando `formatCompactCurrency`; solo cambia su ancho reservado.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Playwright headless con APIs mockeadas sobre `/dashboard`, `/dashboard/titular/titular-1`, `/titulares` y `/cuentas`: OK; `gridStartX=45px`, `yAxisWidth=39px` y sin errores de pagina en las cuatro rutas.

## 2026-05-02 - V-01.05 - Saldo total del dashboard sin salto de linea

### Que cambio

- `dashboard.css` ajusta `dashboard-kpi-grid--overview` para dar mas ancho relativo al KPI destacado.
- Los KPIs superiores reducen padding dentro de esa grilla.
- Los importes de `.dashboard-kpi p` usan `white-space: nowrap`.
- El saldo destacado en `dashboard-kpi-grid--overview .dashboard-kpi--featured p` baja a `clamp(1.35rem, 1.5vw, 1.65rem)`.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend.

### Por que

El saldo total tenia una escala demasiado grande para una tarjeta de una tercera parte del resumen. Con `1.000.000,00 €` se partia o desbordaba. Eso no es un detalle: en una app de tesoreria, los numeros grandes son el caso normal, no una sorpresa.

### Reglas tecnicas

- No cambia formato monetario ni calculos.
- No se oculta el importe con ellipsis.
- No se toca el contrato del componente `KpiCard`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con `total_convertido=1000000`: `1.000.000,00 €` queda en una linea y no desborda (`wraps=false`, `overflows=false`).
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Listado de cuentas en tres columnas

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` agrega la clase `cuentas-page` al contenedor raiz.
- `frontend/src/styles/layout/entities.css` define una grilla especifica para `.cuentas-page .phase2-cards`.
- El listado inferior de cuentas usa tres columnas en desktop, dos en tablet y una en mobile.
- Las tarjetas de cuenta ajustan el header para permitir badges en una segunda linea, limitan titulo/notas a dos lineas y reorganizan metadatos en dos columnas internas.
- El saldo queda destacado en la columna derecha en desktop/tablet y vuelve a apilarse en mobile.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El listado de cuentas heredaba dos columnas de `.phase2-cards`. Pasarlo a tres columnas sin tocar la estructura interna dejaba demasiada informacion financiera comprimida: banco, divisa, estado, vencimiento y saldo compiten por espacio. La solucion correcta es acotar la grilla a `CuentasPage` y ajustar la tarjeta para esa nueva densidad.

### Reglas tecnicas

- No cambia contratos de API, permisos, filtros, paginacion ni calculos.
- No se introduce dependencia nueva.
- La regla mobile especifica evita que la mayor especificidad de `cuentas-page` mantenga dos columnas por debajo de `900px`.
- Se mantiene CSS variables propias y el sistema responsive existente.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/cuentas`: desktop `3` columnas, tablet `2`, mobile `1`, sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Divisa base primero en saldos por divisa

### Que cambio

- `SaldoPorDivisaCard.tsx` calcula `orderedItems` antes de renderizar.
- La lista se parte en dos bloques: primero los items cuya `divisa` coincide con `divisaPrincipal`, despues el resto.
- El resto de divisas conserva el orden recibido de la API.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend.

### Por que

La divisa base es la referencia de comparacion del dashboard. Si aparece segunda o tercera, el usuario tiene que reconstruir mentalmente la pantalla. Eso es mala jerarquia, no una preferencia estetica.

### Reglas tecnicas

- No cambia ningun endpoint ni calculo.
- No se ordenan alfabeticamente las divisas secundarias para evitar cambiar mas de lo pedido.
- No se introduce dependencia nueva.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con API mockeada: la API devuelve `USD` antes que `EUR`, pero `EUR` se renderiza primero porque es `divisaPrincipal`.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Reorden de plazos fijos y saldos por titular en dashboard principal

### Que cambio

- `DashboardPage.tsx` agrupa los KPIs superiores y la tarjeta `Plazos fijos` dentro de `dashboard-overview-primary`.
- `Plazos fijos` se renderiza debajo de `Saldo total`, `Ingresos periodo` y `Egresos periodo`, manteniendo `Saldos por divisa` en la columna derecha del resumen.
- `Saldos por titular` deja de formar parte de una grilla secundaria y pasa a ser una tarjeta de ancho completo en la parte inferior.
- `saldosPorTipo` ya no elimina tipos vacios: siempre prepara Empresa, Autonomo y Particular para mantener tres columnas previsibles.
- `dashboard.css` cambia `dashboard-titular-groups` a tres columnas en desktop y conserva una columna en mobile.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend.

### Por que

Los plazos fijos explican saldo inmovilizado, asi que deben leerse junto a los KPIs de saldo/movimiento. Ponerlos abajo junto a titulares era una mezcla floja. Los titulares, en cambio, son comparacion por categoria; si hay tres tipos, el layout debe tener tres columnas, no dos y luego apaños.

### Reglas tecnicas

- No cambia ningun endpoint ni contrato de API.
- No cambia calculo de saldos, permisos ni filtros.
- No se introduce dependencia nueva.
- La adaptacion responsive se limita a CSS del dashboard.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK; `Plazos fijos` debajo de KPIs, `Saldos por titular` a ancho completo, columnas `Empresa|Autonomo|Particular` en la misma fila y sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.

## 2026-05-02 - V-01.05 - Listado de titulares en tres columnas

### Que cambio

- `frontend/src/pages/TitularesPage.tsx` agrega la clase `titulares-page` al contenedor raiz.
- `frontend/src/styles/layout/entities.css` define una grilla especifica para `.titulares-page .phase2-cards`.
- El listado inferior de titulares usa tres columnas en desktop, dos en tablet y una en mobile.
- Las tarjetas de titular limitan titulo y notas a dos lineas, reorganizan metadatos en dos columnas internas y mantienen las acciones al pie.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

La regla global `.phase2-cards` estaba en dos columnas y tambien la usa `CuentasPage`. Cambiarla globalmente habria sido una metedura de pata: el ajuste pedido pertenece solo a Titulares. La clase de pagina permite ampliar densidad en esa vista sin efectos colaterales.

### Reglas tecnicas

- No cambia contratos de API, permisos, paginacion ni estado.
- No se introduce dependencia nueva.
- La composicion conserva CSS variables propias y los breakpoints existentes.
- La regla mobile explicita evita que la mayor especificidad de `titulares-page` mantenga dos columnas por debajo de `900px`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/titulares`: desktop `3` columnas, tablet `2`, mobile `1`, sin overflow horizontal.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-02 - V-01.05 - Formato de importacion en cuentas de efectivo

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` muestra el selector `Formato de importacion` para `NORMAL` y `EFECTIVO`.
- Al cambiar una cuenta a `EFECTIVO`, la UI limpia `banco_nombre`, `numero_cuenta` e `iban`, pero conserva `formato_id` si es compatible con la divisa.
- Al cambiar a `PLAZO_FIJO`, la UI sigue limpiando datos bancarios y `formato_id`.
- `frontend/src/pages/ImportacionPage.tsx` aclara que las cuentas normales y de efectivo usan formato de importacion.
- `CuentasController` usa `SupportsFormatoImportacion(tipoCuenta)` para aceptar formato en `NORMAL` y `EFECTIVO`, y rechazarlo implicitamente en `PLAZO_FIJO`.
- Se agrega `Crear_Should_Keep_Formato_For_Efectivo` en `CuentasControllerTests`.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El codigo anterior mezclaba dos conceptos distintos: `EFECTIVO` no tiene datos bancarios, pero si puede necesitar un formato para importar movimientos pegados/CSV. `PLAZO_FIJO` si tiene un flujo especial sin formato bancario. Meter ambos en el mismo saco era el bug.

### Reglas tecnicas

- El formato sigue filtrado por divisa.
- Las cuentas de efectivo no persisten banco, numero de cuenta ni IBAN.
- Las cuentas de plazo fijo siguen sin `formato_id` y usan el endpoint especifico de movimiento simple.
- No cambia el contrato de importacion; `ImportacionService` ya leia `FormatoId` desde la cuenta.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" -c Release --filter CuentasControllerTests`: 5/5 OK.
- `npm.cmd run lint`: OK tras corregir dependencia faltante del `useEffect`.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-02 - V-01.05 - Alineacion de graficas en Cuentas y Titulares

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` importa `formatCompactCurrency` y lo usa en el `YAxis` de la grafica de barras del dashboard de cuentas.
- `frontend/src/pages/TitularesPage.tsx` aplica el mismo ajuste en la grafica de barras del dashboard de titulares.
- En ambas graficas, `BarChart` usa margenes explicitos `top: 12`, `right: 8`, `bottom: 12`, `left: 0`.
- `YAxis` baja de `120` a `72`, oculta `axisLine`/`tickLine` y usa `tickMargin={10}`.
- `CartesianGrid` usa `var(--chart-grid)` y desactiva lineas verticales para mantener consistencia con el resto de dashboards.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El carril del eje Y estaba sobredimensionado y empujaba el area real de barras hacia la derecha. Ya se habia corregido el mismo patron en la grafica de evolucion del dashboard principal; dejarlo repetido en cuentas/titulares era inconsistente y visualmente torpe.

### Reglas tecnicas

- No cambia contratos de API, permisos, calculos ni stores.
- No se introduce dependencia nueva.
- El tooltip conserva `formatCurrency` para mostrar importes completos; el formato compacto queda limitado al eje.
- Se mantiene Recharts 2 y CSS variables propias.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Playwright headless con APIs mockeadas sobre `/titulares` y `/cuentas`: OK; `gridStartX=72px`, `yAxisWidth=69px` y sin errores de pagina en ambas rutas.

## 2026-05-01 - V-01.05 - Dashboard principal con grafica a ancho completo

### Que cambio

- `DashboardPage.tsx` separa el dashboard en tres ritmos: resumen superior (`dashboard-overview-grid`), grafica principal (`dashboard-evolution-card`) y bloques secundarios.
- `EvolucionChart.tsx` acepta `height?: number`; el dashboard principal lo usa con `height={420}`.
- `dashboard.css` agrega `dashboard-overview-grid`, refuerza la tarjeta de evolucion con mas padding y adapta divisas/KPIs en desktop y mobile.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.
- `Documentacion/Diseno/DESIGN.md` se actualiza para que la guia ya no contradiga la nueva jerarquia.

### Por que

La pantalla anterior intentaba meter KPIs, divisas y grafica en una sola fila. Eso hacia que la grafica quedara demasiado estrecha para leer tendencias. En tesoreria, la evolucion temporal necesita area util real; si el usuario tiene que acercarse a la pantalla, el diseño falló.

### Reglas tecnicas

- No cambia contratos de API, permisos, filtros ni calculos.
- No se introduce dependencia nueva.
- La altura configurable queda encapsulada en `EvolucionChart` para no duplicar componentes.
- Se mantiene CSS variables propias y Recharts 2.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK, `chartWidthRatio=0.960`, `svgHeight=420`, sin errores de pagina, sin respuestas API 500 y sin overflow horizontal. Dos fallos previos fueron del script mock de verificacion, no del producto.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.

## 2026-05-01 - V-01.05 - Alineacion de la grafica Evolucion

### Que cambio

- `frontend/src/components/dashboard/EvolucionChart.tsx` define margenes explicitos en `LineChart`: `top: 4`, `right: 8`, `bottom: 0`, `left: 0`.
- El `YAxis` reduce su anchura de `116` a `72`.
- `XAxis` y `YAxis` usan `tickMargin={10}` para separar etiquetas sin agrandar artificialmente el eje.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

La tarjeta estaba bien; la grafica no. Recharts estaba reservando demasiado espacio horizontal para el eje Y, asi que el area real de trazado arrancaba tarde y la grafica parecia desalineada dentro del dashboard. Corregirlo en el componente mantiene el layout limpio y evita parches de padding alrededor.

### Reglas tecnicas

- No cambia contratos de API, filtros, permisos ni estructura de datos.
- No se introduce dependencia nueva.
- Se mantiene Recharts 2 y CSS variables propias.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.
- Playwright headless con APIs mockeadas en `/dashboard`: OK; `plotInsetFromLegend=72px`, frente al carril anterior de `116px`.

## 2026-05-01 - V-01.05 - MFA recordado 90 dias y QR de enrolamiento

### Que cambio

- `AuthService.LoginAsync` acepta la cookie `mfa_trusted` y omite el reto MFA solo si el token firmado coincide con el usuario, su `security_stamp` y una expiracion futura.
- `AuthService.VerifyMfaAsync` emite un token MFA recordado durante 90 dias tras verificar correctamente el codigo TOTP.
- `AuthController` lee/escribe `mfa_trusted` como cookie `HttpOnly`, `SameSite=Strict`, `Secure` cuando aplica, y la elimina en logout.
- El enrolamiento inicial sigue generando secreto TOTP por usuario y ahora el frontend pinta un QR real desde `mfa_otp_auth_uri`.
- Se agrega `qrcode` al frontend para generar el QR localmente sin servicios externos.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

Pedir Google Authenticator en cada login es seguridad teatral y mala UX: fuerza friccion constante y acaba empujando a la gente a atajos peores. El criterio correcto aqui es MFA obligatorio en primer enrolamiento y revalidacion periodica. Tres meses es una ventana razonable para una app on-premise de pocos usuarios si el recordatorio queda atado al usuario y se invalida al rotar `security_stamp`.

### Reglas tecnicas

- La cookie recordada no contiene secretos TOTP ni tokens JWT.
- La firma usa HMAC SHA-256 con `JwtSettings:Secret`.
- El token queda ligado a `user_id`, `security_stamp` y expiracion. Cambios de password, permisos, email o perfil que roten `security_stamp` invalidan tambien el recuerdo MFA.
- El QR se genera desde el `otpauth://` emitido por backend; la clave manual queda visible como fallback.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ...GestionCaja.API.Tests.csproj -c Release --filter AuthServiceTests`: 11/11 OK.
- `dotnet test ... --filter AuthServiceTests` en Debug quedo bloqueado por `GestionCaja.API.exe` en uso, PID `35456`; se verifico en Release para no detener un proceso local activo.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.

## 2026-05-01 - V-01.05 - Alineacion del logo en login

### Que cambio

- `frontend/src/styles/auth.css` cambia `.auth-logo-container` de `width: min(100%, 1120px)` a la misma columna visual del formulario: `width: min(calc(100% - 2rem), 430px)`.
- `.auth-logo-container` usa `justify-content: center` para centrar el bloque de marca completo sobre la tarjeta.
- En mobile se usa `width: min(calc(100% - 1.5rem), 430px)` y se conserva el centrado.
- `backend/src/GestionCaja.API/wwwroot` queda sincronizado con el build frontend actualizado.

### Por que

El header del login estaba usando un ancho de pagina completa pensado para layouts generales, no para una pantalla de autenticacion centrada. Resultado: primero el logo quedaba flotando a la izquierda; despues quedo alineado al borde de la tarjeta, pero no centrado como bloque. En login, la marca tiene que caer sobre el eje central de la tarjeta.

### Reglas tecnicas

- No cambia JSX, rutas, autenticacion, MFA ni contratos de API.
- No se introduce dependencia nueva.
- Se conserva CSS variables propias y el comportamiento responsive existente.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK, codigo `1` esperado por copia con cambios.
- Edge headless/CDP en `/login`: centro del bloque de marca y centro de la tarjeta coinciden; `brandDeltaCard=0px`.

## 2026-05-01 - V-01.05 - Aplicacion UI/UX en shell y dashboard

### Que cambio

- `frontend/src/utils/navigation.ts` incorpora grupos semanticos de navegacion: `operacion`, `control` y `sistema`.
- La navegacion usa iconos de `lucide-react` con stroke consistente, en linea con `Documentacion/Diseno/DESIGN.md`.
- `Sidebar.tsx` renderiza secciones agrupadas con labels y separadores discretos.
- `BottomNav.tsx` reduce el menu movil principal a Inicio, Titulares, Cuentas, Importar y Mas; el sheet `Mas` agrupa los accesos secundarios por las mismas secciones.
- `DashboardPage.tsx` reorganiza la primera lectura: KPIs, saldos por divisa y evolucion quedan en `dashboard-command-grid`.
- `SaldoPorDivisaCard.tsx` pasa a una estructura mas semantica con total dominante y desglose `Disponible` / `Inmovilizado`.
- `dashboard.css` ajusta el grid del dashboard para evitar solapamientos, conservar densidad y mantener una columna unica en breakpoints medios/moviles.
- `global.css` deja de importar Geist, porque la guia define `National Park`, `Hind Madurai` y `Atlas Mono` como sistema tipografico activo.
- `auth.css` corrige `--font-mono` por `--font-family-mono` en el bloque MFA.
- `backend/src/GestionCaja.API/wwwroot` se sincroniza con el build frontend actualizado.

### Por que

La guia UI/UX ya estaba escrita, pero no aplicada. El menu plano de muchas entradas era arquitectura visual floja: obligaba a leer todo al mismo nivel. El dashboard tambien repartia demasiado pronto la atencion; para tesoreria, el orden correcto es saldo total, liquidez por divisa y evolucion.

El solapamiento detectado en la verificacion inicial del KPI principal confirmo el punto: si un numero financiero importante no cabe, el diseno esta fallando aunque compile.

### Reglas tecnicas

- No se cambia ningun contrato de API.
- No se introduce dependencia nueva.
- Se mantiene CSS variables propias, dark/light mode y componentes existentes.
- Los grupos de navegacion viven en `navigation.ts` para que desktop y mobile compartan arquitectura.
- `wwwroot` debe actualizarse despues de cada build frontend que cambie UI servida por la API.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- Playwright con APIs mockeadas en `/dashboard`: desktop y mobile sin overflow horizontal; sidebar con grupos `Operacion`, `Control`, `Sistema`; bottom nav con `Inicio`, `Titulares`, `Cuentas`, `Importar`, `Mas`; KPI principal sin solapamiento tras correccion.
- `robocopy frontend/dist -> backend/src/GestionCaja.API/wwwroot /MIR`: OK.

## 2026-05-01 - V-01.05 - Row Level Security activo en PostgreSQL

### Que cambio

- Se agrega la migracion `20260501120000_EnableRowLevelSecurity`.
- Se agrega la migracion `20260501133000_SignRowLevelSecurityContext`.
- La migracion crea el schema auxiliar `atlas_security`, funciones de contexto y politicas RLS.
- RLS queda activado con `FORCE ROW LEVEL SECURITY` en:
  - `TITULARES`
  - `CUENTAS`
  - `PLAZOS_FIJOS`
  - `EXTRACTOS`
  - `EXTRACTOS_COLUMNAS_EXTRA`
  - `EXPORTACIONES`
  - `PREFERENCIAS_USUARIO_CUENTA`
  - `AUDITORIAS`
  - `AUDITORIA_INTEGRACIONES`
  - `BACKUPS`
  - `NOTIFICACIONES_ADMIN`
- `RlsDbCommandInterceptor` fija contexto PostgreSQL antes de cada comando EF Core mediante variables `atlas.*` y una firma HMAC.
- `RlsContextSigner` firma el payload de contexto. PostgreSQL valida la firma con `atlas_security.context_is_valid()`.
- `Program.cs` aplica migraciones con `ConnectionStrings:MigrationConnection` si existe, configura el secreto de firma en `atlas_security.rls_context_secret`, concede permisos al runtime y limpia pools antes de usar `DefaultConnection`.
- `IntegrationAuthMiddleware` publica el token de integracion validado antes de escribir auditoria/rate limit.
- `docker-compose.yml` deja de usar `app_user` como `POSTGRES_USER`; las bases nuevas crean `atlas_owner` para ownership/migraciones y `app_user` como runtime sin `BYPASSRLS`.
- `Instalar-AtlasBalance.ps1` crea/separa `atlas_balance_owner` y `atlas_balance_app`; ambos sin superusuario ni `BYPASSRLS`, pero solo el owner queda en `MigrationConnection`.

### Como funciona

El backend sigue siendo la primera capa de permisos. RLS es la segunda capa: si una consulta directa o un bug de backend intenta leer/escribir fuera del alcance, PostgreSQL tambien filtra.

El interceptor fija estas variables de sesion:

- `atlas.auth_mode`: `anonymous`, `auth`, `user`, `integration` o `system`.
- `atlas.user_id`: usuario autenticado.
- `atlas.integration_token_id`: token de integracion autenticado.
- `atlas.is_admin`: admin de aplicacion.
- `atlas.system`: operaciones internas sin `HttpContext`, como migraciones/seed.
- `atlas.request_scope`: alcance especial, por ejemplo `dashboard`.
- `atlas.context_signature`: HMAC SHA-256 del payload anterior.

Las politicas consultan `PERMISOS_USUARIO` e `INTEGRATION_PERMISSIONS`. Admin y operaciones internas tienen paso amplio solo si `atlas.context_signature` valida contra el secreto DB. Usuarios normales e integraciones quedan limitados a sus cuentas permitidas.

El detalle importante: un cliente SQL con credenciales runtime puede ejecutar `SET atlas.system=true`, pero eso no le concede nada si no puede firmar el contexto. Sin esta firma, RLS seria teatro.

### Limites deliberados

- Las tablas de identidad/configuracion no quedan bajo estas politicas. Muchas se leen durante login, seed, proteccion de secretos o administracion y meterlas en RLS sin un diseno especifico romperia arranque/autenticacion.
- RLS no reemplaza permisos de controlador. Si alguien elimina checks en C#, sigue siendo un bug aunque PostgreSQL bloquee parte del dano.
- En contenedores dev antiguos puede no existir rol `postgres` porque se crearon con `app_user` como superusuario. La migracion activa RLS y firma de contexto, pero la separacion fuerte owner/runtime exige migrar ownership con un rol administrador o recrear la base con el Docker/instalador nuevo.

### Verificacion

- `dotnet build '.\Atlas Balance\backend\src\GestionCaja.API\GestionCaja.API.csproj' -c Release --no-restore`: OK.
- `dotnet test '.\Atlas Balance\backend\tests\GestionCaja.API.Tests\GestionCaja.API.Tests.csproj' -c Release --no-restore --filter RowLevelSecurityTests`: OK.
- Tests focalizados `RowLevelSecurityTests|UserAccessServiceTests|IntegrationAuthorizationServiceTests|IntegrationAuthMiddlewareTests|IntegrationTokenServiceTests`: 15/15 OK.
- `dotnet ef database update`: OK sobre `atlas_balance_db`.
- Catalogo local: 11 tablas objetivo con RLS y FORCE RLS activos, 20 politicas publicas, dos migraciones RLS aplicadas, `app_user` sin superusuario ni `BYPASSRLS`, secreto RLS sembrado, contexto falsificado rechazado y contexto firmado aceptado.

## 2026-04-26 - V-01.05 - Dashboard de titulares: evolucion antes del listado

### Que cambio

- `frontend/src/pages/CuentasPage.tsx` reordena el render del bloque `titulares-dashboard-card`.
- La tarjeta `Evolucion` (`titulares-evolucion-card`) pasa a mostrarse antes de `cuentas-balance-list`.
- No hay cambios en servicios, tipos, stores ni contratos de API.

### Por que

El orden anterior forzaba leer primero el detalle y despues la tendencia. Para analisis rapido de titulares, eso es al reves de lo util.

### Reglas tecnicas

- Cambio solo de orden de JSX.
- Se conserva la misma fuente de datos (`evolucion`, `principal`, `saldosCuentaRows`) y la misma logica de permisos.
- Sin cambios CSS.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

## 2026-04-26 - V-01.05 - Reorden de dashboard principal (grafica antes de saldos)

### Que cambio

- `frontend/src/pages/DashboardPage.tsx` reordena el render para mostrar primero la tarjeta `EvoluciÃ³n`.
- El bloque `dashboard-grid` (Saldo por divisa + Saldos por titular) queda debajo de la grafica.
- No se tocan servicios, tipos, stores ni endpoints.
- `backend/src/GestionCaja.API/wwwroot` se sincroniza con el build frontend actualizado.

### Por que

El dashboard principal quedaba menos util para lectura rapida: primero se veian desgloses y despues la tendencia. Con la grafica arriba se prioriza el contexto temporal antes del detalle por divisa/titular.

### Reglas tecnicas

- Cambio solo de orden de componentes en JSX; sin impacto en contratos de API.
- Se conserva la misma carga paralela de `principal`, `evolucion` y `saldosDivisa`.
- Sin cambios CSS: la disposicion se apoya en estilos existentes.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK (codigo `1` esperado).

## 2026-04-26 - V-01.05 - Importacion preserva el orden de lineas pegadas

### Que cambio

- `ImportacionService.ConfirmarAsync` deja de ordenar las filas validadas por fecha antes de guardar.
- La asignacion de `fila_numero` se hace recorriendo las filas seleccionadas desde la ultima linea pegada hacia la primera.
- La linea superior del extracto pegado recibe el `fila_numero` mas alto del lote, por lo que sigue arriba cuando la vista ordena por fecha/fila descendente.
- El detalle de auditoria `primeras_filas` se calcula con el orden original de indices, no con el orden interno de insercion.

### Por que

Ordenar por fecha durante la importacion era una decision demasiado lista para su propio bien. En extractos bancarios, especialmente con lineas informativas, movimientos del mismo dia o saldos de detalle, el orden del fichero es parte del dato. Cambiarlo en backend rompe la lectura del banco y descoloca lineas auxiliares.

### Reglas tecnicas

- La validacion puede normalizar fecha, monto y saldo, pero no debe reordenar filas.
- `fila_numero` es el mecanismo de estabilidad visual: mayor numero significa linea mas reciente/superior en la vista descendente.
- Las filas no seleccionadas o invalidas no consumen `fila_numero`.
- No se cambia el ordenamiento general de `GET /api/extractos`; se corrige solo la numeracion creada por la importacion.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" --filter ImportacionServiceTests --no-restore`: 26/26 OK.
- `dotnet build ".\\Atlas Balance\\backend\\src\\GestionCaja.API\\GestionCaja.API.csproj" -c Release --no-restore`: OK, 0 warnings, 0 errores.

## 2026-04-26 - V-01.05 - Borrado multiple de extractos por cuenta

### Que cambio

- `CuentaDetailPage` incorpora seleccion multiple de filas en el desglose de cuenta.
- Se anade checkbox por fila, checkbox global para seleccionar todo y contador de seleccion.
- Se anade confirmacion unica para borrar en lote desde el mismo dashboard de cuenta.
- El borrado multiple llama en bucle al endpoint existente `DELETE /api/extractos/{id}`.

### Por que

Eliminar linea por linea era lento y propenso a errores cuando hay limpieza masiva. El cambio reduce clics sin abrir otra superficie de permisos.

### Reglas tecnicas

- No se crea endpoint nuevo: se reaprovecha la ruta actual para conservar validaciones y auditoria.
- Si falla un borrado durante el lote, se muestra error con progreso parcial y se recarga para dejar el estado real.
- El flujo de borrado multiple solo aparece si el usuario ya tiene permiso `puede_eliminar_lineas`.

### Verificacion

- `npm.cmd run build`: OK.
- `npm.cmd run lint`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK (codigo `1` esperado).

## 2026-04-26 - V-01.05 - Actualizacion automatica desde GitHub Release oficial

### Que cambio

- `ActualizacionService` mantiene `app_update_check_url` como repo oficial de GitHub (`https://github.com/AtlasLabs797/AtlasBalance`) y consulta `releases/latest` via API de GitHub.
- Si el release no trae `source_path`, el backend busca el asset `AtlasBalance-*-win-x64.zip`, valida que la URL pertenezca al repo oficial, descarga el ZIP y lo extrae dentro de `WatchdogSettings:UpdateSourceRoot`.
- Antes de entregar la ruta al Watchdog, el paquete debe contener `VERSION`, `api/GestionCaja.API.exe` y `watchdog/GestionCaja.Watchdog.exe`.
- La comparacion de versiones ahora normaliza etiquetas tipo `V-01.05-win-x64`, evitando comparaciones lexicas rotas con el formato real de releases.
- `WatchdogOperationsService` crea backup PostgreSQL previo con `pg_dump` antes de actualizar binarios. Si no puede crear backup y `RequireDatabaseBackupBeforeUpdate` esta activo, no actualiza.
- El Watchdog crea copia rollback de binarios antes de sincronizar y la restaura si falla la copia.
- Si `RequireHealthCheckAfterUpdate` esta activo, Watchdog exige que `ApiHealthUrl` responda OK tras arrancar la API; si falla, revierte binarios.
- La pantalla `Configuracion > Sistema` muestra el campo como repositorio GitHub de actualizaciones, no como endpoint JSON manual.

### Por que

El boton `Actualizar ahora` ya existia, pero era medio humo: con el repo de GitHub configurado podia detectar releases, pero no descargar el asset ni preparar una ruta local segura para el Watchdog. Ahora el flujo real es repo oficial -> ultimo release -> ZIP win-x64 validado -> carpeta segura de updates -> Watchdog.

### Reglas tecnicas

- No se aceptan assets fuera de `https://github.com/AtlasLabs797/AtlasBalance/releases/download/...`.
- No se extrae nada fuera de `UpdateSourceRoot`.
- No se actualiza si el paquete no parece un release Windows x64 completo.
- En produccion, `RequireDatabaseBackupBeforeUpdate` queda activo por defecto. Desactivarlo es una mala idea salvo tests controlados.
- En produccion, `RequireHealthCheckAfterUpdate` queda activo por defecto y usa `https://localhost/api/health`.

### Verificacion

- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" -c Release --filter "ActualizacionServiceTests|WatchdogOperationsServiceTests|ConfiguracionControllerTests"`: 14/14 OK.
- Parser PowerShell de `scripts/Instalar-AtlasBalance.ps1`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por copia con cambios.

## 2026-04-26 - V-01.05 - Actualizacion post-instalacion endurecida

### Que cambio

- `scripts/update.ps1` declara `PackagePath`, `InstallPath` y `SkipBackup` de forma explicita.
- El wrapper ya no usa `ValueFromRemainingArguments` para reenviar `-InstallPath` a `Actualizar-AtlasBalance.ps1`.
- `SeedData.EnsureDefaultFormatosImportacion` comprueba primero si el ID fijo del formato por defecto ya existe usando `IgnoreQueryFilters()`.
- Si el ID ya existe, el seeder no intenta insertar otra fila con la misma PK aunque banco/divisa esten incompletos, cambiados o heredados de una version anterior.
- Se agrego una regresion en `SeedDataTests` para una fila legacy con el ID de Sabadell ya existente y `BancoNombre`/`Divisa` nulos.

### Por que

La actualizacion real desde `V-01.04` demostro dos fallos operativos: el wrapper podia pasar mal `-InstallPath`, y el arranque de API podia morir antes de servir `/api/health` por `23505 pk_formatos_importacion`. Esa combinacion es mala: actualiza binarios, crea backup, pero deja el servicio parado. Arreglado en el flujo de release, no con parches manuales en servidor.

### Verificacion

- Parser PowerShell sobre `scripts/update.ps1` y `scripts/Actualizar-AtlasBalance.ps1`: OK.
- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" --filter SeedDataTests`: 5/5 OK.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.05`: OK.
- ZIP corregido: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.05-win-x64.zip`, SHA256 `482189BB4B6F731CEB02ECA214A550B1CE9DB33C71F0DBF4E057761E8FD002C3`.

## 2026-04-26 - V-01.05 - Limpieza de artefactos locales

### Que cambio

- Se eliminaron artefactos locales no versionables: `.codex-runlogs/`, `output/`, logs de API y paquetes generados antiguos dentro de `Atlas Balance/Atlas Balance Release/`.
- `Atlas Balance/Atlas Balance Release/` queda solo con `.gitkeep`; los ZIP y carpetas de paquete se regeneran con `scripts/Build-Release.ps1` y se publican como assets de GitHub Releases.
- Se eliminaron directorios frontend vacios heredados de la limpieza de shadcn: `frontend/src/lib/` y `frontend/src/components/ui/`.
- `.gitignore` ahora ignora `.codex-runlogs/` y `output/`.

### Por que

Mantener paquetes release, logs, capturas y backups SQL temporales dentro del workspace ensucia el estado local y aumenta el riesgo de arrastrar datos privados. El codigo fuente y la documentacion quedan; los artefactos se regeneran cuando hacen falta.

### Verificacion

- `git check-ignore -v .codex-runlogs/foo output/foo`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ".\GestionCaja.sln" -c Release --no-restore`: 107/108 OK; `ExtractosConcurrencyTests` falla porque Docker/Testcontainers no esta disponible.
- `dotnet test ".\GestionCaja.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 107/107 OK.

## 2026-04-25 - V-01.05 - Paquete final y publicacion

### Que cambio

- Se regenero el paquete `AtlasBalance-V-01.05-win-x64.zip` con `scripts/Build-Release.ps1`.
- El build frontend del paquete quedo sincronizado en `backend/src/GestionCaja.API/wwwroot`.
- El ZIP final queda fuera de Git y se publica como asset de GitHub Release.

### Verificacion

- `scripts\Build-Release.ps1 -Version V-01.05`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance\backend\tests\GestionCaja.API.Tests\GestionCaja.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list "Atlas Balance\backend\src\GestionCaja.API\GestionCaja.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- Paquete verificado sin `appsettings.Development.json`, `.env`, `node_modules`, `obj`, `bin\Debug` ni `.bak-iframe-fix`.
- SHA256 final del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `B5ABC5525CBD49F2BD0A5ADC5B930A2113AF323F99C1337087B8E0D7875E6A10`.

## 2026-04-25 - V-01.05 - Auditoria de bugs y seguridad

### Que cambio

- Se reviso la superficie tecnica de seguridad activa: autenticacion JWT en cookies httpOnly, CSRF por header `X-CSRF-Token`, validacion de `SecurityStamp`, permisos backend, integracion OpenClaw, rutas de backup/exportacion, cabeceras HTTP, CI y secretos versionables.
- Se actualizaron los minimos declarados del frontend para cerrar deuda de supply chain: `axios ^1.15.2` y `react-router-dom ^6.30.3`.
- El bundle de produccion se recompilo y se sincronizo con `backend/src/GestionCaja.API/wwwroot`.
- No se cambiaron contratos de API ni modelo de datos.

### Por que

El lockfile ya resolvia versiones seguras, pero dejar rangos minimos vulnerables en `package.json` es pedir que una reinstalacion sin lockfile fiable abra otra vez el agujero. Eso no es "flexibilidad", es pereza con consecuencias.

### Verificacion

- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet test ".\Atlas Balance\backend\GestionCaja.sln" -c Release --no-build`: 107/107 OK.
- `dotnet list ".\Atlas Balance\backend\GestionCaja.sln" package --vulnerable --include-transitive`: sin vulnerabilidades.
- `wwwroot`: sincronizado y sin sourcemaps, plantillas Development ni `.env`.

## 2026-04-25 - V-01.05 - Importacion simple de plazo fijo y resumen dashboard

### Que cambio

- `CuentaImportacionContextoResponse` expone `TipoCuenta` para que el frontend distinga cuentas normales, efectivo y plazo fijo.
- `ImportacionService.ValidarAsync` y `ConfirmarAsync` rechazan importaciones con formato para `PLAZO_FIJO`.
- Nuevo contrato `ImportacionPlazoFijoMovimientoRequest/Response`.
- Nuevo endpoint `POST /api/importacion/plazo-fijo/movimiento`.
- `RegistrarMovimientoPlazoFijoAsync` exige permiso de importacion, cuenta activa de plazo fijo, monto positivo y fecha.
- El movimiento usa `INGRESO` como monto positivo y `EGRESO` como monto negativo, calcula `saldo_actual = ultimo_saldo + monto_firmado`, asigna `fila_numero` con bloqueo transaccional cuando la BD es relacional y registra auditoria.
- `DashboardPrincipalResponse` incluye `PlazosFijos` con monto total convertido, intereses previstos convertidos, fecha/dias del proximo vencimiento y numero de cuentas.
- `DashboardService` calcula ese resumen con las cuentas visibles para el usuario y excluye plazos `RENOVADO`/`CANCELADO` del calculo de intereses/vencimiento.
- El frontend cambia automaticamente a un formulario simple cuando la cuenta seleccionada es `PLAZO_FIJO`.

### Por que

Un plazo fijo no tiene extracto bancario normal que mapear. Forzar CSV/Excel aqui era burocracia tecnica: lo correcto es registrar entrada o salida y que el sistema calcule el saldo.

### Reglas tecnicas

- Las cuentas de plazo fijo no deben depender de `formatos_importacion`.
- No permitir monto negativo en request; el signo lo decide `tipo_movimiento`.
- Los intereses previstos siguen siendo importe absoluto aproximado, no porcentaje.
- El resumen de dashboard respeta el alcance de cuentas visible para el usuario.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.
- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" --filter "ImportacionServiceTests|DashboardServiceTests"`: 28/28 OK.
- `dotnet build "Atlas Balance/backend/src/GestionCaja.API/GestionCaja.API.csproj" -c Release`: OK, 0 warnings.

## 2026-04-25 - V-01.05 - Actualizaciones post-instalacion

### Que cambio

- `update.cmd` y `Actualizar Atlas Balance.cmd` devuelven el codigo de salida de PowerShell.
- `scripts\update.ps1` valida que el origen sea un paquete release antes de autoelevar.
- `scripts\update.ps1` soporta `-PackagePath` para que una instalacion ya actualizada pueda aplicar paquetes futuros desde otra carpeta.
- `scripts\Actualizar-AtlasBalance.ps1` conserva configuracion, crea backup DB previo, crea rollback de binarios, reemplaza API y Watchdog, copia scripts/wrappers operativos a la instalacion, actualiza `VERSION`, actualiza `atlas-balance.runtime.json` y valida `/api/health`.

### Por que

Instalar una vez no basta. Si el update no actualiza tambien su propia maquinaria, la siguiente actualizacion vuelve a depender de scripts viejos. Eso es deuda operativa disfrazada de "ya lo vemos luego".

### Verificacion

- Parser PowerShell OK para `update.ps1` y `Actualizar-AtlasBalance.ps1`.
- Ejecutar update desde carpeta fuente falla con mensaje de paquete invalido.
- `scripts\Build-Release.ps1 -Version V-01.05`: OK; ZIP regenerado.
- Scripts empaquetados parsean correctamente.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.
- Pendiente de entorno real: probar update desde `V-01.03` instalada a `V-01.05` en Windows Server 2019.

## 2026-04-25 - V-01.05 - Cierre de incidencias instalacion Windows Server 2019

### Que cambio

- `scripts\install.ps1` valida que la carpeta sea un paquete release antes de autoelevar.
- `scripts\Instalar-AtlasBalance.ps1` valida `api\GestionCaja.API.exe` y `watchdog\GestionCaja.Watchdog.exe` antes de instalar.
- El instalador mantiene autodeteccion `PostgreSQL\17\bin` antes que `16\bin` y muestra instrucciones concretas para instalacion manual si `winget` falla.
- El instalador detecta usuarios existentes en `"USUARIOS"` y, si los hay, no escribe `SeedAdmin:Password` ni un `Password admin inicial` falso.
- `scripts\Reset-AdminPassword.ps1` resetea una cuenta admin usando la conexion de produccion local: genera hash bcrypt 12, marca `primer_login`, activa usuario, limpia bloqueo, rota `security_stamp` y revoca refresh tokens.
- `scripts\Build-Release.ps1` empaqueta `Reset-AdminPassword.ps1` e `install-cert-client.ps1`.
- El health check post-instalacion usa `curl.exe -k` si esta disponible y deja `Invoke-WebRequest` como fallback.

### Por que

La instalacion estaba demasiado optimista. En Windows Server 2019 eso es pedir problemas: `winget` puede no existir, PowerShell puede fallar con TLS autofirmado y una BD existente no significa admin nuevo. El cambio elimina mentiras operativas.

### Reglas tecnicas

- Nunca ejecutar instalacion de servidor desde ZIP `main`/carpeta fuente.
- No regenerar credenciales iniciales si la BD ya tiene usuarios.
- No pedir SQL manual largo para reset admin; usar `Reset-AdminPassword.ps1`.
- Para health check operativo en Server 2019, preferir `curl.exe -k`.

### Verificacion

- Parser PowerShell OK para `Instalar-AtlasBalance.ps1`, `install.ps1`, `Reset-AdminPassword.ps1` y `Build-Release.ps1`.
- `Instalar-AtlasBalance.ps1` desde carpeta fuente falla con mensaje de paquete invalido.
- `install.ps1` desde carpeta fuente falla con mensaje de paquete invalido antes de autoelevar.
- `scripts\Build-Release.ps1 -Version V-01.05`: OK; ZIP generado.
- Paquete verificado sin `*Development*`, `*.template`, `.env`, `node_modules` ni `.bak-iframe-fix`.
- Scripts empaquetados parsean correctamente.
- Backend tests filtrados sin Testcontainers: 95/95 OK.
- SHA256 del ZIP `AtlasBalance-V-01.05-win-x64.zip`: `42994915A8AFD014EF807D99E6335944302662FAA21927206ACAF1B8FDE46304`.

## 2026-04-25 - V-01.05 - Apertura de version

### Que cambio

- `V-01.05` pasa a ser la version activa del sistema.
- Backend: `Directory.Build.props` sube a `1.5.0` y `InformationalVersion` a `V-01.05`.
- Frontend: `package.json` y `package-lock.json` suben a `1.5.0`; `appVersion` pasa a `V-01.05`.
- `Atlas Balance/VERSION`, `SeedData`, `Build-Release.ps1` e `Instalar-AtlasBalance.ps1` quedan alineados con `V-01.05`.
- `Documentacion/Versiones/v-01.03.md` queda cerrada como version publicada.
- `Documentacion/Versiones/v-01.05.md` queda como archivo activo de trabajo.

### Por que

`V-01.03` ya fue publicada. Seguir metiendo cambios ahi seria una forma bastante tonta de romper la trazabilidad.

### Reglas tecnicas

- Todo cambio nuevo debe documentarse bajo `V-01.05`.
- El siguiente paquete debe generarse con `scripts/Build-Release.ps1 -Version V-01.05`.
- No reutilizar assets ni notas de release de `V-01.03` para publicar `V-01.05`.

### Verificacion

- `git diff --check`: OK.
- `dotnet build '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `npm.cmd run build`: OK con `atlas-balance-frontend@1.5.0`.

## 2026-04-25 - V-01.03 - Paquete release Windows x64 generado

### Que cambio

- Se genero el paquete `AtlasBalance-V-01.03-win-x64` en `Atlas Balance/Atlas Balance Release`.
- Se genero el ZIP `AtlasBalance-V-01.03-win-x64.zip` para distribucion.
- `scripts/Build-Release.ps1` recompilo el frontend y reemplazo `GestionCaja.API/wwwroot` con el bundle de produccion actual.
- API y Watchdog quedaron publicados como self-contained `win-x64`.
- El paquete incluye scripts operativos, `VERSION`, `README.md`, `documentacion.md`, `.gitignore` y `version.json`.

### Reglas tecnicas

- Los artefactos de `Atlas Balance/Atlas Balance Release` no deben entrar en commits normales; van como assets de GitHub Releases.
- Si se cambia documentacion incluida en el paquete despues de generar el ZIP, hay que regenerar el release. No hacerlo seria publicar un paquete con instrucciones atrasadas.
- `version.json` debe conservar `source_path = C:\AtlasBalance\updates\V-01.03\api` para actualizaciones de esta version.

### Verificacion

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.03`: OK.
- Carpeta generada: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.03-win-x64`.
- ZIP generado: `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.03-win-x64.zip`.
- `version.json` y `VERSION` empaquetados: `V-01.03`.
- Barrido de `api` empaquetada: sin `*Development*`, `*.template` ni `.env`.

## 2026-04-25 - V-01.03 - Hardening de seguridad post-auditoria

### Que cambio

- Se agregaron `SecurityStamp` y `PasswordChangedAt` a `USUARIOS` mediante la migracion `UserSessionHardening`.
- Los access tokens incluyen `security_stamp`; `UserStateMiddleware` lo valida contra BD en cada request API autenticado.
- Cambios/reset de password, borrado de usuario y reuse de refresh token rotan el stamp y revocan refresh tokens activos.
- Login usa throttle por cliente/email y deja de distinguir externamente usuario bloqueado de credenciales invalidas.
- Reuse de refresh token revocado escala a incidente: revoca sesiones activas, rota stamp y registra `REFRESH_TOKEN_REUSE_DETECTED`.
- Passwords de usuarios y seed admin pasan a minimo 12 caracteres y bloqueo de passwords comunes.
- `IntegrationAuthMiddleware` corta bearer invalido repetido por IP/minuto antes de consultar tokens activos.
- `app_update_check_url` queda limitado a HTTPS del repo oficial `AtlasLabs797/AtlasBalance`.
- Backups, exportaciones, descargas y rutas Watchdog validan la ruta cruda antes de `Path.GetFullPath`.
- `config\INSTALL_CREDENTIALS_ONCE.txt` se borra automaticamente con tarea programada SYSTEM a las 24 horas.
- `postcss` queda resuelto a `8.5.10`.

### Impacto operativo

- Tras desplegar esta version, los access tokens antiguos sin `security_stamp` dejan de ser validos. Eso es correcto: los usuarios tendran que autenticarse otra vez.
- La URL de actualizaciones ya no acepta endpoints arbitrarios; si se necesita otro canal de releases, primero hay que ampliar la allowlist de forma explicita.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.

### Verificacion

- `dotnet build '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `dotnet test '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-build`: 94/94 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet list '.\Atlas Balance\backend\GestionCaja.sln' package --vulnerable --include-transitive`: sin vulnerabilidades.
- Parser PowerShell sobre `scripts/Instalar-AtlasBalance.ps1`: OK.

## 2026-04-20 - V-01.03 - Apertura de version

### Que cambio

- `V-01.03` pasa a ser la version activa del sistema.
- Backend: `Directory.Build.props` sube a `1.3.0` y `InformationalVersion` a `V-01.03`.
- Frontend: `package.json` y `package-lock.json` suben a `1.3.0`; `appVersion` pasa a `V-01.03`.
- `Atlas Balance/VERSION`, `SeedData`, `Build-Release.ps1` e `Instalar-AtlasBalance.ps1` quedan alineados con `V-01.03`.
- `Documentacion/Versiones/v-01.02.md` queda cerrada como version publicada.
- `Documentacion/Versiones/v-01.03.md` queda como archivo activo de trabajo.

### Por que

`V-01.02` ya fue publicada. Seguir metiendo cambios ahi seria versionado barro: funciona hasta que alguien necesita saber que demonios se desplego.

### Reglas tecnicas

- Todo cambio nuevo debe documentarse bajo `V-01.03`.
- El siguiente paquete debe generarse con `scripts/Build-Release.ps1 -Version V-01.03`.
- No reutilizar assets ni notas de release de `V-01.02` para publicar `V-01.03`.

### Verificacion

- `git diff --check`: OK; solo avisos esperados de normalizacion LF/CRLF.
- `dotnet build '.\Atlas Balance\backend\GestionCaja.sln' -c Release --no-restore`: OK, 0 warnings, 0 errores.
- `npm.cmd run build`: OK con `atlas-balance-frontend@1.3.0`.

## 2026-04-20 - V-01.02 - Release autonoma con scripts one-click

### Que cambio

- El paquete de release ahora incluye `install.cmd`, `update.cmd`, `uninstall.cmd` y `start.cmd`.
- Los `.cmd` llaman wrappers PowerShell en `scripts/install.ps1`, `scripts/update.ps1`, `scripts/uninstall.ps1` y `scripts/start.ps1`.
- `install.cmd` se autoeleva y llama al instalador real con `-InstallDependencies` por defecto.
- `Instalar-AtlasBalance.ps1` puede preparar PostgreSQL 16 gestionado con `winget`, usando servicio `AtlasBalance.PostgreSQL`, password generada y puerto libre si `5432` esta ocupado.
- `atlas-balance.runtime.json` registra si PostgreSQL es gestionado por Atlas, su servicio y la configuracion DB usada.
- `Launch-AtlasBalance.ps1` arranca en orden: PostgreSQL gestionado, Watchdog y API.
- `Actualizar-AtlasBalance.ps1` arranca PostgreSQL gestionado antes de crear backup y reemplazar binarios.
- `uninstall.ps1` elimina servicios, firewall, atajos, `%ProgramData%\AtlasBalance`, carpeta instalada y PostgreSQL gestionado si fue creado por el instalador.
- `Build-Release.ps1` copia los nuevos scripts y `README_RELEASE.md` dentro del paquete generado.

### Por que

La release anterior tenia piezas utiles, pero no cumplia literalmente el contrato de "install/update/uninstall/start" ni arrancaba la base de datos desde `start`. Eso es una grieta operativa: si PostgreSQL queda parado, el backend no arranca y el usuario culpa al frontend. Mal diagnostico, mala noche.

### Reglas tecnicas

- El frontend no se instala en produccion: se compila con Vite y se sirve desde `wwwroot` en la API.
- El backend publicado es self-contained; el servidor no necesita .NET Runtime.
- La API aplica migraciones EF Core en startup.
- Si se usa PostgreSQL externo, el instalador exige password admin o binarios `psql`; no intenta adivinar credenciales.
- `uninstall.cmd` solo borra la base gestionada por Atlas. Una base externa no se elimina sin una decision explicita.

### Verificacion

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Release.ps1" -Version V-01.02`: OK.
- Paquete generado en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.02-win-x64`.
- ZIP generado en `Atlas Balance/Atlas Balance Release/AtlasBalance-V-01.02-win-x64.zip`.
- Parser PowerShell sobre scripts fuente y scripts empaquetados: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK dentro del build de release.
- `dotnet test .\backend\GestionCaja.sln -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 82/82 OK.
- Suite backend completa: 82/83 OK; `ExtractosConcurrencyTests` falla por Docker/Testcontainers no disponible en este entorno, incidencia ya conocida.
- Scanner local de secretos sobre el paquete generado: 0 hallazgos.
- Paquete verificado sin `appsettings.Development.json`, plantillas, source maps, `node_modules` ni `frontend/dist` suelto.
- `winget search PostgreSQL.PostgreSQL --source winget`: confirma existencia de `PostgreSQL.PostgreSQL.16` en este entorno.

## 2026-04-20 - V-01.02 - Auditoria tecnica profunda y hardening

### Que cambio

- `smtp_password` y `exchange_rate_api_key` en `CONFIGURACION` se almacenan protegidos con ASP.NET Core Data Protection y prefijo `enc:v1:`.
- En cada arranque, la API migra automaticamente esos valores si aun estan en claro.
- En produccion, las claves de Data Protection se guardan fuera del directorio servido, por defecto en `%ProgramData%/AtlasBalance/keys`; puede sobrescribirse con `DataProtection:KeysPath`. En Windows se protegen con DPAPI de maquina.
- `ConfiguracionController` no devuelve secretos al frontend y redacta esos valores en auditoria.
- `EmailService` y `TiposCambioService` descifran secretos solo justo antes de usarlos.
- `UserAccessService` ya no interpreta `PuedeVerDashboard` global como permiso global de datos.
- `ExportacionesController.Descargar` valida que el fichero sea `.xlsx` y este dentro de `export_path`.
- `GestionCaja.Watchdog` escucha explicitamente en localhost mediante Kestrel.
- La API rechaza `AllowedHosts` vacio, placeholder o wildcard fuera de Development.
- Scripts de backup/restore/manual/service install usan nombres y usuarios actuales, restauran `PGPASSWORD` y validan extension `.dump`.
- Se eliminaron logs y artefactos de smoke/login con cookies, cabeceras o payloads sensibles.

### Por que

Guardar secretos en claro dentro de la tabla de configuracion era el riesgo mas serio que quedaba. Y el permiso global de dashboard era peor de lo que parecia: podia abrir datos fuera del alcance esperado. Eso no era "deuda tecnica"; era una fuga esperando su turno.

### Reglas tecnicas

- No leer `smtp_password` ni `exchange_rate_api_key` directamente salvo a traves de `ISecretProtector`.
- No cambiar la cuenta de servicio, mover de maquina o borrar el keyring de Data Protection sin plan de rotacion; los secretos cifrados quedarian ilegibles.
- Las exportaciones descargables deben seguir saliendo solo de `export_path`.
- Watchdog debe permanecer en loopback y autenticado con `X-Watchdog-Secret`.
- Produccion debe declarar hosts explicitos en `AllowedHosts`; wildcards ya no son aceptables.

### Verificacion

- `dotnet build "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore`: OK, 0 warnings.
- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-build`: 83/83 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `dotnet list ... package --vulnerable --include-transitive`: sin vulnerabilidades.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.

### Pendientes

- Rotar secretos locales si `.env` o `appsettings.Development.json` se compartieron fuera del equipo.
- Reparar el estado Git local si se necesita diff/commit/push fiable desde esta copia.

## 2026-04-20 - V-01.02 - Cierre de bugs reportados

### Que cambio

- `SeedAdmin:Email` queda normalizado como `admin@atlasbalance.local` en configuracion base y plantillas.
- Se corrigieron ejemplos, placeholders, rutas por defecto y tests que arrastraban `atlasbalnace` o `atlas-blance`.
- El evento interno de importacion ahora usa la constante compartida `IMPORTACION_COMPLETADA_EVENT` con namespace `atlas-balance`.
- `Instalar-AtlasBalance.ps1` escribe runtime `V-01.02`, no `V-01.01`.
- La documentacion de instalacion y `SPEC.md` apuntan a `V-01.02` y rutas `C:/AtlasBalance`.
- El build frontend generado se copio a `backend/src/GestionCaja.API/wwwroot` para que la API local sirva el bundle corregido.

### Por que

La revision previa no estaba equivocada, pero estaba incompleta: el codigo principal ya tenia varios fixes, mientras que configuracion, scripts y artefactos servidos seguian arrastrando restos. Eso es peor que un bug obvio, porque parece arreglado hasta que instalas o pruebas desde el backend.

### Verificacion

- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore`: 82/82 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `docker ps --filter "name=atlas_balance_db"`: contenedor activo en `5433->5432`.
- Barrido `Select-String` en codigo activo y `wwwroot`: 0 restos de `atlasbalnace`, `atlas-blance` o `V-01.01`.

### Pendientes

- Ninguno de estos bugs queda abierto.

## 2026-04-20 - V-01.02 - Auditoria de seguridad y bugs

### Que cambio

- Se eliminaron secretos y passwords de desarrollo de configuracion versionable.
- `SeedAdmin:Password` pasa a ser obligatorio antes del primer arranque con BD vacia.
- Si `JwtSettings:Secret` falta en Development, la API genera una clave efimera de proceso; fuera de Development sigue siendo obligatorio.
- Watchdog ya no usa password de BD por defecto para restauraciones.
- `docker-compose.yml` exige `ATLAS_BALANCE_POSTGRES_PASSWORD` desde `.env` local o variable de entorno.
- Se aÃ±adieron plantillas de configuracion para API y Watchdog, y un `.env.example` sin secretos.
- `SeedData` usa `V-01.02` y el check de actualizacion usa la version runtime en el User-Agent.
- Se corrigieron mensajes mojibake en importacion y asunto SMTP.
- GitHub Actions queda fijado a SHAs concretos para reducir riesgo de supply chain.
- Se aÃ±adio `.gitignore` dentro de `Atlas Balance` para proteger la app si se trabaja desde esa carpeta como raiz.

### Por que

Los secretos "solo de desarrollo" en archivos base son una bomba lenta: se copian, se reutilizan y un dia llegan a produccion. La configuracion base debe ser segura por defecto y obligar a crear secretos locales/produccion fuera de Git.

### Reglas tecnicas

- No commitear `appsettings.Development.json`, `appsettings.Production.json`, `.env`, certificados, logs ni paquetes generados.
- Para desarrollo local, copiar las plantillas y rellenar secretos reales en archivos ignorados.
- Para produccion, generar secretos fuertes distintos para JWT, Watchdog, PostgreSQL, certificado y admin inicial.
- No ejecutar restauraciones Watchdog si `WatchdogSettings:DbPassword` no esta configurado.

### Verificacion

- `python Skills/Seguridad/cyber-neo-main/skills/cyber-neo/scripts/scan_secrets.py "Atlas Balance" --json`: 0 hallazgos.
- `dotnet list "Atlas Balance/backend/GestionCaja.sln" package --vulnerable --include-transitive`: sin paquetes vulnerables.
- `npm.cmd audit --json`: 0 vulnerabilidades.
- `dotnet test "Atlas Balance/backend/GestionCaja.sln" -c Release --no-restore --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 81/81 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.

### Pendientes

- Reparar el estado Git local si se necesita commit/push fiable desde esta carpeta.

## 2026-04-20 - V-01.01 - Reorganizacion de estructura

### Que cambio

- La aplicacion quedo centralizada en `Atlas Balance`.
- Los paquetes existentes quedaron en `Atlas Balance/Atlas Balance Release`.
- La documentacion quedo centralizada en `Documentacion`.
- Material auxiliar, duplicados y artefactos temporales quedaron en `Otros`.
- `CLAUDE.md` y `AGENTS.md` fueron actualizados sin planificacion por bloques temporales.
- `Atlas Balance/scripts/Build-Release.ps1` ahora genera paquetes en `Atlas Balance/Atlas Balance Release`.
- `Build-Release.ps1` copia la documentacion de usuario desde `Documentacion/documentacion.md`.
- El repositorio Git quedo en la raiz para versionar juntos `Atlas Balance` y `Documentacion`.

### Por que

La estructura anterior mezclaba app real, scaffolding, duplicados, documentacion, repos auxiliares de diseno y artefactos generados. Eso aumenta el riesgo de tocar lo equivocado y hace mas dificil empaquetar o revisar cambios.

### Como queda

- Runtime y codigo fuente: `Atlas Balance`
- Releases: `Atlas Balance/Atlas Balance Release`
- Documentacion: `Documentacion`
- Auxiliares no runtime: `Otros`

### Verificacion esperada

- `git status --short` debe funcionar desde la raiz del proyecto.
- `powershell -File "Atlas Balance/scripts/Build-Release.ps1" -Version V-01.01` debe publicar en `Atlas Balance/Atlas Balance Release`.
- `dotnet build "Atlas Balance/backend/GestionCaja.sln" --no-restore` debe resolver rutas relativas dentro de la app.

## 2026-04-20 - V-01.01 - Catalogo de skills locales

### Que cambio

- Se analizo `Skills` y se separaron skills reales de copias repetidas por agente.
- Se creo `Documentacion/SKILLS_LOCALES.md` como catalogo canonico.
- Se actualizaron `CLAUDE.md`, `AGENTS.md`, `Atlas Balance/CLAUDE.md` y `Atlas Balance/AGENTS.md` para indicar como y cuando usar skills locales.

### Por que

La carpeta `Skills` contiene repos completos y varias carpetas repetidas para diferentes agentes. Sin una guia, un agente puede cargar duplicados, ejecutar scripts innecesarios o aplicar reglas de stack equivocadas. Eso seria ruido, no mejora.

### Reglas tecnicas

- La documentacion canonica de uso vive en `Documentacion/SKILLS_LOCALES.md`.
- Para cada tarea se debe cargar solo la skill relevante.
- Las recomendaciones de las skills se subordinan al stack real de Atlas Balance.
- No se deben ejecutar CLIs o scripts dentro de `Skills` sin necesidad clara.

## 2026-04-20 - V-01.01 - Politica de subida a GitHub

### Que cambio

- `.gitignore` ahora excluye explicitamente `Otros/` y `Skills/`.
- `Atlas Balance/Atlas Balance Release/` queda como carpeta local de salida, mantenida en Git solo con `.gitkeep`.
- Los paquetes generados de release se publican como assets de GitHub Releases, no como archivos en la historia Git.
- Las instrucciones de agentes indican que GitHub debe recibir todo lo versionable excepto `Otros/`, `Skills/` y paquetes generados de release.

### Por que

El repositorio oficial debe contener el proyecto util para desarrollo, documentacion y configuracion, pero no repos auxiliares, duplicados de trabajo, skills locales pesadas ni binarios generados. Los ZIP de release pesan demasiado para vivir comodamente en Git; GitHub Releases es el sitio correcto para distribuirlos.

### Reglas tecnicas

- Subir a GitHub como Git: codigo, documentacion, configuracion y scripts.
- Subir a GitHub Releases: ZIP, carpetas empaquetadas y binarios generados de release.
- No subir nunca: `Otros/`, `Skills/`, secretos, `.env`, logs, cookies, tokens, certificados privados, `node_modules`, `bin/obj` ni artefactos locales sensibles.

## 2026-04-23 - V-01.03 - Cierre de fuga de alcance global en extractos

### Que cambio

- `ExtractosController.GetAllowedAccountIds` y `CanViewTitular` dejaron de tratar `PuedeVerDashboard` global como permiso global de datos.
- El alcance global en extractos queda restringido a permisos de datos reales: `PuedeAgregarLineas`, `PuedeEditarLineas`, `PuedeEliminarLineas` o `PuedeImportar`.
- Se agrego regresion automatizada en `ExtractosControllerTests` para impedir que `/api/extractos` devuelva datos cross-account a perfiles dashboard-only globales.

### Por que

La logica local de `ExtractosController` estaba mas permisiva que `UserAccessService`. Esa divergencia abria una fuga de datos financieros entre cuentas.

### Verificacion

- `dotnet test ".\\Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~GestionCaja.API.Tests.ExtractosControllerTests|FullyQualifiedName~GestionCaja.API.Tests.UserAccessServiceTests"`: 8/8 OK.

## 2026-04-24 - V-01.03 - Frontend alineado con permisos reales de cuenta

### Que cambio

- `frontend/src/stores/permisosStore.ts` diferencia entre alcance de cuenta y permiso global solo de dashboard.
- Una fila global `cuenta_id = null`, `titular_id = null` ya no habilita `canViewCuenta` ni contamina `getColumnasVisibles/getColumnasEditables` salvo que conceda acceso global de datos (`agregar`, `editar`, `eliminar`, `importar`).
- `frontend/src/pages/CuentasPage.tsx` ya no ofrece enlaces o botones a `/dashboard/cuenta/:id` para cuentas sin acceso real; muestra `Sin acceso`.
- `frontend/src/pages/CuentaDetailPage.tsx` intercepta `403` del backend y redirige a `/dashboard` en vez de dejar al usuario atrapado en un error de carga.

### Por que

El backend ya estaba bien. El frontend seguia mintiendo: enseÃ±aba rutas de cuenta a perfiles `dashboard-only` globales, como si pudieran abrirlas. Eso no filtraba datos, pero era UX rota y semantica de permisos incoherente.

### Reglas tecnicas

- En frontend, el acceso a cuenta no debe inferirse de cualquier permiso coincidente. Una fila global solo vale como acceso de cuenta si equivale a acceso global de datos.
- Los estados visuales de apertura de cuenta tienen que apoyarse en la misma semantica que backend. Si backend va a responder `403`, frontend no debe mostrar un CTA operativo.
- Cuando una ruta depende de datos protegidos y el backend responde `403`, la pantalla debe redirigir o cerrar el paso de forma limpia, no quedarse en un error generico.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.

## 2026-04-25 - V-01.05 - Importacion con advertencias para filas solo concepto

### Que cambio

- `ImportacionService.ValidateRows` diferencia errores fatales de advertencias importables.
- Las filas con concepto y fecha/monto/saldo vacios pasan a ser validas con advertencias.
- Para poder persistirlas en `EXTRACTOS`, la fecha y el saldo se heredan de la ultima fila valida anterior y el monto se normaliza a `0`.
- `FilaValidacionResponse` expone `Advertencias` y el frontend las muestra en la tabla de validacion con estado visual de aviso.
- Se agregaron regresiones para validar e importar filas informativas sin romper las reglas existentes de filas ambiguas.

### Por que

Algunos bancos exportan lineas informativas o de detalle como filas separadas con solo concepto. Bloquearlas como error obligaba al usuario a descartarlas aunque quisiera conservar esa informacion en el extracto.

### Reglas tecnicas

- Solo se relajan filas claramente informativas: concepto presente y fecha, importe y saldo vacios.
- Una fila con fecha/saldo pero importe vacio sigue siendo error; eso ya no es una descripcion, es un movimiento incompleto.
- Una fila sin referencia previa de fecha o saldo sigue siendo error, porque inventar datos financieros desde cero seria una mala idea.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" --filter ImportacionServiceTests`: 21/21 OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; `wwwroot` actualizado con el bundle corregido.

## 2026-04-25 - V-01.05 - Permiso global explicito para ver cuentas

### Que cambio

- `PERMISOS_USUARIO` incorpora `puede_ver_cuentas`.
- `UserAccessService`, `ExtractosController`, `AuthService` y las respuestas de permisos exponen y respetan ese permiso.
- El alcance global sobre todas las cuentas se concede si existe una fila global (`cuenta_id = null`, `titular_id = null`) con `puede_ver_cuentas` o con permisos de datos heredados (`agregar`, `editar`, `eliminar`, `importar`).
- El modal de usuarios agrega el boton `Acceso a todas las cuentas` y el checkbox `Ver cuentas`.
- La migracion `AddPuedeVerCuentasPermiso` rellena `puede_ver_cuentas = true` para permisos existentes que ya daban acceso por scope o por acciones de datos, sin convertir permisos globales dashboard-only.

### Por que

Hasta ahora se podia conseguir acceso global solo dejando scope vacio y marcando una accion de datos. Eso era poco claro y empujaba a conceder importacion o edicion solo para que el usuario pudiera ver cuentas. Mala idea: visibilidad y escritura deben ser permisos distintos.

### Reglas tecnicas

- `puede_ver_dashboard` no concede acceso a extractos ni a todas las cuentas.
- `puede_ver_cuentas` concede visibilidad/lectura de cuentas dentro de su scope.
- Los permisos de escritura/importacion siguen implicando visibilidad para compatibilidad, pero no al reves.

### Verificacion

- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" --filter "UserAccessServiceTests|UsuariosControllerTests|ExtractosControllerTests"`: 12/12 OK.
- `dotnet test "Atlas Balance/backend/tests/GestionCaja.API.Tests/GestionCaja.API.Tests.csproj" --filter "FullyQualifiedName!~ExtractosConcurrencyTests"`: 97/97 OK.
- `dotnet build "Atlas Balance/backend/src/GestionCaja.API/GestionCaja.API.csproj" -c Release`: OK, 0 warnings.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; `robocopy` devolvio codigo `1`, copia correcta con archivos actualizados.

## 2026-04-25 - V-01.05 - Plazo fijo, autonomos, alertas por tipo y dashboard inmovilizado

### Que cambio

- `TipoTitular` incorpora `AUTONOMO` sin alterar los valores enteros existentes de `EMPRESA` y `PARTICULAR`.
- `Cuenta` incorpora `TipoCuenta`; `es_efectivo` se mantiene por compatibilidad, pero la logica nueva usa `tipo_cuenta`.
- Nueva tabla `PLAZOS_FIJOS` con relacion 1:1 a cuenta, cuenta de referencia opcional, fechas, interes previsto, renovable, estado, notificacion y soft delete.
- Nueva migracion `AddPlazoFijoAutonomosAlertas`: rellena `tipo_cuenta = EFECTIVO` desde `es_efectivo`, crea indices y constraints de fechas/interes.
- `GET /api/titulares` acepta `tipoTitular`.
- `GET /api/cuentas` acepta `tipoTitular` y `tipoCuenta`; las respuestas exponen `titular_tipo`, `tipo_cuenta` y `plazo_fijo`.
- `POST/PUT /api/cuentas` crean y editan cuentas de plazo fijo.
- `POST /api/cuentas/{id}/plazo-fijo/renovar` renueva manualmente, audita y no crea movimientos.
- `PlazoFijoVencimientoJob` corre diario con Hangfire y usa `IPlazoFijoService`.
- `ALERTAS_SALDO` admite `tipo_titular`; `AlertaService` aplica prioridad cuenta > tipo titular > global.
- Dashboard separa saldos disponibles e inmovilizados y agrupa saldos por titular por tipo.

### Por que

Un plazo fijo es patrimonio, pero no liquidez. Meterlo como saldo normal mentia en el dashboard. La app ahora diferencia dinero disponible de dinero inmovilizado sin inventar transferencias ni liquidaciones automaticas.

### Reglas tecnicas

- No cambiar una cuenta `PLAZO_FIJO` a otro tipo: se bloquea y se debe crear otra cuenta.
- `fecha_vencimiento >= fecha_inicio`.
- `interes_previsto` es importe absoluto y no puede ser negativo.
- El job marca `VENCIDO` el mismo dia de vencimiento.
- Las alertas globales, por tipo y por cuenta son mutuamente excluyentes por alcance.
- `puede_ver_dashboard` sigue sin abrir datos fuera del alcance autorizado.

### Verificacion

- `dotnet build ...GestionCaja.API.csproj -c Release`: OK.
- Tests focalizados de cuentas/dashboard/alertas/plazos: 12/12 OK.
- Tests backend sin Testcontainers: 103/103 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.

## 2026-04-25 - V-01.05 - Coherencia visual del frontend

### Que cambio

- `frontend/src/styles/variables.css` incorpora tokens semanticos para controles, superficies, sombras, foco y estados de interaccion.
- `frontend/src/styles/global.css` alinea inputs, selects, botones base y tokens shadcn/Tailwind con las variables propias de Atlas Balance.
- `frontend/src/components/ui/button.tsx` deja de usar medidas y colores genericos de shadcn y pasa a respetar radios, alturas, foco y variantes del sistema visual de la app.
- `frontend/src/styles/layout.css` agrega una capa comun para paginas, headers, cards, tablas, tabs, navegacion, modales y estados hover/focus.
- `frontend/src/styles/auth.css` ajusta login para usar las mismas superficies, foco, sombras y boton primario del resto del producto.

### Por que

La app tenia buena base, pero habia dos sistemas visuales compitiendo: CSS variables propias y tokens shadcn/Tailwind genericos. Eso acababa creando diferencias sutiles entre botones, tabs, campos, cards y estados de foco. Sutil en una pantalla; feo cuando recorres toda la app.

### Reglas tecnicas

- No se agrega ninguna dependencia.
- Tailwind/shadcn solo se usan donde ya existian; sus tokens se subordinan al sistema propio.
- Las alturas minimas de controles se mantienen cerca de 44px para touch y teclado.
- Las animaciones siguen limitadas a color, sombra, transform y opacity.
- Los cambios son sistemicos; no se reescribe funcionalidad de paginas.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Screenshots Playwright de `/login`: `output/playwright/ui-login-desktop.png` y `output/playwright/ui-login-mobile.png`.

## 2026-04-25 - V-01.05 - CSS de layout separado por dominios

### Que cambio

- `frontend/src/styles/layout.css` queda como archivo indice con imports.
- Los estilos se reparten en:
  - `frontend/src/styles/layout/shell.css`
  - `frontend/src/styles/layout/users.css`
  - `frontend/src/styles/layout/extractos.css`
  - `frontend/src/styles/layout/entities.css`
  - `frontend/src/styles/layout/dashboard.css`
  - `frontend/src/styles/layout/importacion.css`
  - `frontend/src/styles/layout/admin.css`
  - `frontend/src/styles/layout/system-coherence.css`

### Por que

`layout.css` habia pasado de ser hoja de layout a cajon de todo: shell, usuarios, extractos, titulares, dashboard, importacion, configuracion, auditoria y capa visual comun. Eso escala fatal. Separarlo reduce el coste de tocar una pantalla sin romper otra por accidente.

### Reglas tecnicas

- Se mantiene el orden original de cascada mediante imports en `layout.css`.
- No se cambia ningun selector ni comportamiento visual intencionadamente.
- `system-coherence.css` queda al final porque actua como capa comun de overrides visuales.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `git diff --check` en los CSS tocados: OK, con aviso esperado de normalizacion CRLF/LF.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Calendario nativo alineado con inputs

### Que cambio

- `frontend/src/styles/global.css` agrega reglas para `input[type='date']`.
- Se fuerza `color-scheme` claro/oscuro en `html` y en los inputs de fecha para que el picker nativo del navegador respete el tema activo.
- Se estiliza `::-webkit-calendar-picker-indicator` con fondo, radio, hover, active y filtro en dark mode.
- Se normalizan las partes internas `::-webkit-datetime-edit` y `::-webkit-datetime-edit-fields-wrapper`.

### Por que

Los campos de fecha del plazo fijo eran inputs nativos y el icono/picker del calendario quedaban fuera del sistema visual. Feo y evitable.

### Limitacion

El calendario desplegable es nativo del navegador/OS. CSS puede mejorar tema e indicador, pero no convertirlo en un componente totalmente propio sin reemplazar `input type="date"` por un date picker custom.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Vencimiento visible en detalle de plazo fijo

### Que cambio

- `ExtractosDtos.CuentaResumenKpiResponse` incluye `TipoCuenta` y `PlazoFijo`.
- `ExtractosController.GetCuentaResumen`, `GetCuentasTitular` y `GetTitularesResumen` pasan `TipoCuenta` a `BuildSummary`.
- `BuildSummary` adjunta `PlazoFijoResponse` solo para cuentas `PLAZO_FIJO`.
- `CuentaDetailPage` muestra una banda compacta bajo el titulo con fecha de vencimiento, dias restantes/vencido y estado.
- `entities.css` agrega estilos de `.cuenta-plazo-summary`.

### Por que

El dato de vencimiento existia al crear/editar la cuenta y en la lista de cuentas, pero no aparecia en el dashboard de cuenta. Eso obligaba al usuario a salir de la pantalla donde esta mirando saldo y movimientos, justo donde el vencimiento importa.

### Verificacion

- `dotnet build "Atlas Balance\\backend\\src\\GestionCaja.API\\GestionCaja.API.csproj" -c Release`: OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-25 - V-01.05 - Date picker propio

### Que cambio

- Se crea `frontend/src/components/common/DatePickerField.tsx`.
- Se reemplazan los `input type="date"` en:
  - `components/extractos/AddRowForm.tsx`
  - `pages/AuditoriaPage.tsx`
  - `pages/CuentasPage.tsx`
  - `pages/ImportacionPage.tsx`
- `global.css` incorpora los estilos `.date-picker-*` y `.date-field`.
- El popover calcula si debe abrir hacia abajo o hacia arriba segun el espacio disponible.

### Por que

El calendario nativo del navegador no puede ajustarse al diseno Atlas de forma fiable. El intento anterior estilaba el campo cerrado, pero al abrir el selector volvia a aparecer una UI ajena al producto.

### Decisiones de diseno

- Mantener una superficie blanca, borde suave y sombra contenida, siguiendo `Documentacion/Diseno/DESIGN.md`.
- Usar `lucide-react` para iconos porque ya esta instalado en el proyecto.
- No meter una libreria de date picker: seria dependencia nueva para un componente pequeno y controlable.
- Incluir `Hoy` y `Limpiar` como acciones compactas para filtros y formularios.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.
- Navegador in-app en `http://localhost:5173/cuentas`: se abre el modal de editar plazo fijo, el calendario se muestra con el sistema visual Atlas y no hay errores de consola.

## 2026-05-01 - V-01.05 - Hardening por checklist general de seguridad

### Que cambio

- `USUARIOS` incorpora `mfa_enabled`, `mfa_secret`, `mfa_enabled_at` y `mfa_last_accepted_step`.
- `AuthService` exige MFA TOTP cuando `Security:RequireMfaForWebUsers=true`.
- El login correcto con password crea un challenge temporal MFA y no emite JWT hasta validar el codigo.
- Si el usuario aun no tenia MFA, el challenge entrega una clave TOTP para enrolamiento y la guarda protegida al verificar el primer codigo.
- `TotpService` implementa RFC 6238 con HMAC-SHA1, periodo de 30 segundos, 6 digitos y tolerancia de un intervalo.
- `AuthController` agrega `POST /api/auth/mfa/verify`.
- `CsrfMiddleware` excluye el verify MFA porque ocurre antes de tener sesion/cookie autenticada.
- `UsuariosController` rota `security_stamp` y revoca refresh tokens al cambiar permisos, permiso de cuenta, email, perfil o restaurar usuario.
- `ActualizacionService` verifica el `digest` SHA-256 del asset descargado desde GitHub Release antes de extraerlo.
- CI agrega escaneo de secretos de alta confianza sobre archivos versionados.
- `LoginPage` soporta el segundo paso MFA y el setup inicial.
- `wwwroot` se sincroniza con el build frontend nuevo.

### Por que

El checklist general marcaba puntos P0 que si aplican a Atlas Balance: MFA, sesiones regeneradas ante cambio de permisos, verificacion de updates, secret scanning e incident response. Lo demas que habla de movil, IA, RAG, pagos, cloud o Kubernetes no pertenece al producto actual.

### Reglas tecnicas

- No se emiten cookies `access_token`/`refresh_token` hasta completar MFA.
- Los challenges MFA viven en memoria 5 minutos y aceptan maximo 5 fallos.
- `mfa_last_accepted_step` evita reutilizar el mismo codigo TOTP.
- Los secretos MFA nunca deben aparecer en logs ni documentacion.
- El digest de GitHub no sustituye la firma de codigo, pero bloquea ZIPs manipulados entre la API de releases y el extractor local.
- Todo cambio de permisos o identidad revoca sesiones del usuario afectado aunque el backend ya lea permisos desde BD; el frontend no debe seguir con permisos cacheados viejos.

### Verificacion

- `dotnet build ".\Atlas Balance\backend\src\GestionCaja.API\GestionCaja.API.csproj" -c Release --no-restore`: OK.
- Tests focalizados auth/usuarios/update/CSRF/sesion: 24/24 OK.
- Tests backend sin Testcontainers: 115/115 OK.
- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- NuGet vulnerable audit: sin hallazgos.

## 2026-04-25 - V-01.05 - Correccion de hallazgos de auditoria

### Que cambio

- El frontend deja de depender de Tailwind/shadcn: se eliminan dependencias, plugin Vite, imports CSS, `components.json`, `components/ui/button.tsx` y `lib/utils.ts`.
- `global.css` queda como entrada de tokens/estilos propios, sin `@theme`, `@apply`, imports Tailwind ni compatibilidad shadcn.
- `backend/src/GestionCaja.API/wwwroot` se sincroniza desde `frontend/dist` para que la API sirva los bundles corregidos.
- Se reemplazan fondos decorativos por superficies planas con tokens propios en `global.css`, `auth.css` y estilos de layout.
- `CuentaResumenResponse` se amplia con `CuentaNombre`, `Divisa`, `TitularId`, `TitularNombre`, `EsEfectivo`, `TipoCuenta`, `PlazoFijo`, `Notas` y `UltimaActualizacion`.
- `CuentasController.Resumen` resuelve el resumen mensual y adjunta metadatos de plazo fijo cuando corresponde.
- `DatePickerField` gana semantica de grid, etiquetas de fecha completas y navegacion con flechas/Home/End.
- `ConfirmDialog` implementa focus trap basico con Tab/Shift+Tab.
- `AppSelect` abre y cierra con Enter/Espacio ademas de raton/flechas.

### Por que

La auditoria encontro deuda real, no cosmetica: un segundo sistema de estilos contradiciendo la arquitectura, un endpoint de resumen con contrato inferior al endpoint usado por la UI y controles custom que no cerraban el contrato minimo de teclado.

### Reglas tecnicas

- No se acepta Tailwind/shadcn como dependencia implicita del producto. Si algun dia se quiere usar, debe cambiar primero la documentacion canonica.
- Los resumentes de cuenta no deben divergir en campos criticos: tipo de cuenta, titular y plazo fijo son parte del contrato de lectura.
- Todo control propio que sustituya a un nativo debe cubrir teclado basico antes de release.
- Los fondos de app deben priorizar tokens, bordes, spacing y tipografia sobre degradados decorativos.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `npm.cmd audit --audit-level=moderate`: 0 vulnerabilidades.
- `dotnet test ".\\Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" -c Release --filter CuentasControllerTests`: 4/4 OK.
- `dotnet test ".\\Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" -c Release`: 108/108 OK.
- `dotnet list ".\\Atlas Balance\\backend\\src\\GestionCaja.API\\GestionCaja.API.csproj" package --vulnerable --include-transitive`: sin hallazgos.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por archivos actualizados.

## 2026-04-26 - V-01.05 - Fix de altura del AlertBanner en el shell

### Que cambio

- `frontend/src/styles/layout/shell.css` ajusta la grilla de `app-main` para soportar tres filas estables: topbar, banner y contenido.
- Se define placement explicito para evitar auto-placement ambiguo cuando el banner existe:
  - `.app-main > .app-topbar { grid-row: 1; }`
  - `.app-main > .alert-banner { grid-row: 2; align-self: start; min-height: 0; height: auto; }`
  - `.app-main > .app-content { grid-row: 3; min-height: 0; }`
- Se replica la misma estructura en el breakpoint mobile (`max-width: 768px`).
- Se agrega `align-self: start` en `.alert-banner` para evitar estirado vertical residual en dashboards.
- Barrido de codigo frontend confirma que `AlertBanner` solo se monta en `components/layout/Layout.tsx`, por lo que el fix aplica a todas las rutas no embebidas.

### Por que

Con `grid-template-rows: var(--topbar-height) 1fr`, al aparecer el banner la fila flexible `1fr` se la quedaba el propio banner y quedaba sobredimensionado. El contenido pasaba a una fila implicita posterior, rompiendo proporciones en Configuracion/Backups/Papelera. En dashboards, ademas, se apreciaba estirado residual por comportamiento por defecto de grid (`align-self: stretch`), corregido con `align-self: start`.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK; codigo `1` esperado por copia con cambios.

## 2026-04-26 - V-01.05 - Importacion permite avisos con saldo presente

### Que cambio

- `ImportacionService.ValidateRows` amplia la regla de filas informativas: si una fila tiene concepto, fecha vacia e importe vacio, pasa a ser importable con advertencias aunque traiga saldo.
- El importe se normaliza a `0`.
- La fecha se hereda de la ultima fila valida anterior.
- El saldo se conserva si viene parseable; solo se hereda el saldo anterior cuando tambien esta vacio.
- Se agregan regresiones de validacion y confirmacion para filas tipo `concepto + saldo` sin fecha ni importe.

### Por que

Algunos bancos exportan lineas informativas de beneficiario/desglose con concepto y saldo, pero sin fecha ni importe. Tratarlas como error fatal bloqueaba importaciones correctas. La app debe avisar y dejar continuar, no ponerse exquisita con basura bancaria previsible.

### Verificacion

- `dotnet test "Atlas Balance\\backend\\tests\\GestionCaja.API.Tests\\GestionCaja.API.Tests.csproj" --filter ImportacionServiceTests`: 26/26 OK.
- `dotnet build "Atlas Balance\\backend\\src\\GestionCaja.API\\GestionCaja.API.csproj" -c Release`: OK, 0 warnings.

## 2026-04-26 - V-01.05 - Vista tabular de extractos tipo hoja de calculo

### Que cambio

- `ExtractoTable.tsx` agrupa cabecera y filas dentro de `extracto-table-viewport`, de forma que el scroll horizontal es comun.
- La tabla declara semantica `role="grid"`, con conteo de filas/columnas y encabezados de columna.
- La estimacion del virtualizador cambia segun densidad: `42px` en modo comodo y `34px` en modo compacto.
- Se agrega `getColumnLabel` para mostrar nombres legibles sin cambiar los campos reales usados por sort, filtros o guardado.
- `extractos.css` define variables locales de hoja (`--sheet-grid`, `--sheet-head-bg`, `--sheet-row-height`, etc.) y refuerza bordes, foco, hover, cabecera sticky y primera columna sticky.
- Se sincroniza `backend/src/GestionCaja.API/wwwroot` desde `frontend/dist`.

### Por que

La vista anterior era una tabla editable, pero no una hoja de calculo convincente: cabecera y cuerpo tenian scroll separado, las celdas tenian poco borde y el foco no parecia una seleccion de celda. Para extractos bancarios densos, esa blandura visual estorba. La lectura debe ser de matriz, no de lista bonita.

### Verificacion

- `npm.cmd run lint`: OK.
- `npm.cmd run build`: OK.
- `robocopy dist ..\\backend\\src\\GestionCaja.API\\wwwroot /MIR`: OK.
- Prueba visual/funcional Playwright con app real y APIs mockeadas en `/extractos`: OK; 120 filas, scroll horizontal/vertical, cabecera y primera columna sticky, foco de celda, filtros, panel de columnas y consola sin errores.
