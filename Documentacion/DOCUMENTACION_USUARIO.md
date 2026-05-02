# Documentacion de usuario

## Ubicacion principal

La aplicacion esta en la carpeta `Atlas Balance`.

## Navegacion y dashboard

Desde `V-01.05`, el menu se organiza en tres bloques:

- `Operacion`: Dashboard, Titulares, Cuentas, Extractos e Importacion.
- `Control`: Alertas y Exportaciones.
- `Sistema`: Usuarios, Auditoria, Formatos, Backups, Configuracion y Papelera.

En movil, la barra inferior muestra solo los accesos principales: Inicio, Titulares, Cuentas, Importar y Mas. El boton `Mas` abre el resto de secciones.

El dashboard principal prioriza la lectura financiera:

- Saldo total en la divisa base.
- Saldos por divisa, separando disponible e inmovilizado. La divisa base aparece siempre primero.
- Plazos fijos debajo del resumen de saldo, ingresos y egresos.
- Evolucion del periodo en una grafica ancha para leer la tendencia sin pelearse con tarjetas laterales.
- Saldos por titular en la parte inferior, agrupados en tres columnas: Empresa, Autonomo y Particular.

## Acceso con Google Authenticator

Atlas Balance usa MFA con aplicaciones compatibles tipo Google Authenticator.

La primera vez que entras, despues de email y contrasena, aparece un QR. Escanealo con Google Authenticator y escribe el codigo de 6 digitos. Si el QR no se puede escanear, usa la clave manual que aparece debajo.

Despues de verificarlo, ese navegador queda recordado durante 90 dias. No se pedira el codigo en cada entrada; se volvera a pedir cuando pasen esos 90 dias, cierres sesion y borres cookies, cambie la seguridad del usuario o uses otro navegador/equipo.

## Paquetes de instalacion

Los paquetes de release estan en:

```text
Atlas Balance/Atlas Balance Release
```

Paquete local actual generado para `V-01.05`:

```text
AtlasBalance-V-01.05-win-x64.zip
SHA256: 3E7A3ED22EFC4D18A161EA9D8D15CD9C12B3D51BDEF9AE38863767EC5CEAE299
```

Para instalar o actualizar desde una build local, usa los archivos del paquete generado para la version correspondiente.

No instales desde el ZIP `main` de GitHub ni desde una carpeta fuente. El paquete instalable debe llamarse como `AtlasBalance-V-01.05-win-x64.zip` y contener `api\GestionCaja.API.exe`, `watchdog\GestionCaja.Watchdog.exe`, `scripts` y wrappers `.cmd`.

Para actualizacion desde la app, el release de GitHub debe incluir tambien `AtlasBalance-V-01.05-win-x64.zip.sig`. Si falta la firma, el actualizador online lo rechazara. Bien rechazado: actualizar una app financiera sin firma es jugar con cerillas al lado de gasolina.

Scripts principales del paquete:

- `install.cmd`: instala dependencias, PostgreSQL, servicios y configuracion.
- `start.cmd`: arranca PostgreSQL gestionado, Watchdog y API; el frontend va servido por la API.
- `update.cmd`: actualiza binarios y aplica migraciones al arrancar.
- `uninstall.cmd`: elimina la instalacion y la base gestionada si fue creada por el instalador.

En Windows Server 2019, instala PostgreSQL 16+ manualmente si `winget` falla. PostgreSQL 17 es compatible. Para comprobar la API, usa:

```powershell
curl.exe -k -v https://NOMBRE_DEL_SERVIDOR/api/health
```

Si el navegador falla pero `curl.exe -k` responde, instala `C:\AtlasBalance\certs\atlas-balance.cer` como raiz confiable en el cliente.

Si reinstalas sobre una base existente, las credenciales iniciales no se regeneran. Usa el admin existente o ejecuta:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AtlasBalance\scripts\Reset-AdminPassword.ps1" -InstallPath C:\AtlasBalance -AdminEmail admin@atlasbalance.local -GeneratePassword
```

El reset de password debe ejecutarse como Administrador. Si genera password temporal, la escribe en `C:\AtlasBalance\config\RESET_ADMIN_CREDENTIALS_ONCE.txt` con acceso limitado a Administrators/SYSTEM.

Para actualizar una instalacion ya existente, descomprime el paquete nuevo y ejecuta:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance
```

Desde `V-01.05`, ese wrapper acepta `-InstallPath` directamente y crea backup antes de reemplazar binarios. Si una actualizacion anterior dejo la API parada por formatos de importacion duplicados, actualiza con un paquete `V-01.05` regenerado; el arranque ya no intenta duplicar esos formatos por ID fijo.

Si la instalacion ya tiene los scripts actualizados, tambien vale:

```powershell
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-01.05-win-x64 -InstallPath C:\AtlasBalance
```

La distribucion oficial de paquetes se publica como asset en GitHub Releases:

```text
https://github.com/AtlasLabs797/AtlasBalance/releases
```

Tambien puedes actualizar desde la propia app:

1. Entra como admin.
2. Ve a `Configuracion > Sistema`.
3. Deja el repo `https://github.com/AtlasLabs797/AtlasBalance`.
4. Pulsa `Verificar actualizacion`.
5. Si hay version nueva, pulsa `Actualizar ahora`.

La app descarga el ZIP oficial `win-x64`, verifica su firma `.zip.sig`, crea backup PostgreSQL previo, rollback de binarios y comprueba `/api/health`. Si no puede verificar firma, crear backup o levantar la API despues, no deja la actualizacion como buena.

Para que la actualizacion online funcione, la instalacion debe tener configurada la clave publica de firma en `UpdateSecurity:ReleaseSigningPublicKeyPem` o en `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM`. Sin esa clave, la app rechaza paquetes online. Es incomodo una vez; confiar en ZIPs sin firma seria peor.

## Seguridad de PostgreSQL

Desde `V-01.05`, Atlas Balance activa Row Level Security en PostgreSQL para las tablas sensibles de titulares, cuentas, extractos, plazos fijos, exportaciones, auditoria, backups y notificaciones.

Esto no cambia lo que ves en la app. Cambia lo importante: si una consulta backend sale mal filtrada, la base tambien aplica aislamiento por fila. Antes no lo hacia; eso era un agujero claro.

En instalaciones nuevas, PostgreSQL usa dos credenciales: una de migracion/owner y otra de aplicacion runtime. La app normal usa runtime, sin superusuario, sin ownership de tablas y sin `BYPASSRLS`. El contexto RLS va firmado; falsificar `atlas.system=true` a mano no basta.

En instalaciones existentes, la migracion activa RLS y firma de contexto. Si esa base antigua fue creada con el usuario de aplicacion como owner, merece migracion manual de ownership para dejar la frontera igual de fuerte que una instalacion nueva.

## Documentacion util

- Instalacion y actualizaciones: `Documentacion/documentacion.md`
- Version actual: `Documentacion/Versiones/version_actual.md`
- Cambios tecnicos: `Documentacion/DOCUMENTACION_CAMBIOS.md`

## Importacion de extractos

En la pantalla de validacion, las filas con concepto pero sin fecha ni monto ya no bloquean la importacion. Se muestran como avisos y quedan seleccionables.

Cuando se importan, Atlas Balance usa la fecha de la ultima fila valida anterior y guarda el monto como `0`. Si la fila trae saldo, conserva ese saldo; si tambien falta el saldo, usa el saldo de la ultima fila valida anterior. Si una fila tiene datos mezclados o ambiguos, sigue apareciendo como error y no se importa.

Al confirmar, Atlas Balance respeta el orden del extracto pegado. La linea superior queda como la ultima del lote (`fila_numero` mas alto), sin reordenar por fecha durante la importacion.

Los formatos de importacion permiten hasta 64 columnas extra y nombres de hasta 80 caracteres. Las columnas extra vacias no se guardan. Esto evita que un formato mal hecho convierta una importacion normal en basura multiplicada en base de datos.

Si la cuenta seleccionada es de `Plazo fijo`, no hay formato de importacion. Solo puedes:

- `Anadir dinero`.
- `Sacar dinero`.

Indica fecha, monto y concepto opcional. Atlas Balance calcula el saldo nuevo desde el ultimo saldo registrado.

## Usuarios y permisos

En `Usuarios`, el modal de alta/edicion incluye `Acceso a todas las cuentas`. Ese ajuste crea un permiso global para ver todas las cuentas sin conceder automaticamente edicion, eliminacion ni importacion.

Para permisos manuales, marca `Ver cuentas` cuando el usuario necesite abrir cuentas o extractos. Las acciones `Puede Agregar`, `Puede Editar`, `Puede Eliminar` y `Puede Importar` siguen siendo permisos separados.

## Titulares, cuentas y plazos fijos

Los titulares pueden ser `Empresa`, `Autonomo` o `Particular`. En `Titulares` puedes filtrar por tipo.

Las cuentas pueden ser:

- `Normal`: cuenta bancaria operativa.
- `Efectivo`: caja o saldo manual sin datos bancarios, con formato de importacion opcional.
- `Plazo fijo`: dinero inmovilizado hasta una fecha de vencimiento.

En cuentas normales y de efectivo puedes asociar un `Formato de importacion` desde `Cuentas`. Las normales ademas permiten banco, numero de cuenta e IBAN; las de efectivo no, porque ponerle IBAN a una caja es teatro administrativo.

Al crear una cuenta de plazo fijo debes indicar fecha de inicio, fecha de vencimiento y si es renovable. Opcionalmente puedes informar interes previsto, cuenta de referencia y notas.

En el dashboard de una cuenta de plazo fijo, la fecha de vencimiento aparece bajo el nombre de la cuenta, junto con los dias restantes o el aviso de vencido y el estado actual.

En ese mismo dashboard de cuenta, dentro de `Desglose de la cuenta`, ahora puedes seleccionar varias lineas y borrarlas de una vez:

- Marca las filas que quieras eliminar o usa `Seleccionar todas`.
- Pulsa `Eliminar seleccionadas`.
- Confirma la accion para enviarlas a papelera.

La renovacion de un plazo fijo es manual desde `Cuentas` con la accion `Renovar`. Atlas Balance no mueve dinero ni crea transferencias por ti. Bien: una app de tesoreria que inventa movimientos automaticamente es una bomba.

## Alertas

Las alertas de saldo bajo pueden configurarse con tres alcances:

- `Global`: aplica si no hay una alerta mas especifica.
- `Tipo de titular`: aplica a Empresa, Autonomo o Particular.
- `Cuenta`: aplica solo a una cuenta concreta.

La prioridad es cuenta > tipo de titular > global.

## Dashboard

El dashboard principal muestra:

- Saldo disponible: cuentas normales y efectivo.
- Saldo inmovilizado: cuentas de plazo fijo.
- Saldo total: disponible + inmovilizado.
- Saldos por divisa: la divisa base aparece primero y el resto debajo/despues segun el espacio disponible.
- Plazos fijos: monto total, intereses aproximados y dias hasta el proximo vencimiento, ubicados bajo los KPIs de saldo, ingresos y egresos.
- La grafica de evolucion se muestra en una franja ancha propia, despues del resumen superior de KPIs y saldos por divisa.
- En `Cuentas > Saldos y evolucion`, la grafica de `Evolucion` se muestra antes del listado de cuentas.

Los saldos por titular ocupan la parte inferior completa del dashboard y se agrupan en tres columnas: Empresa, Autonomo y Particular.

## Interfaz

La interfaz mantiene el mismo funcionamiento, pero ahora los botones, campos, pestanas, tarjetas, tablas y estados de foco usan un sistema visual comun. No cambia el flujo de trabajo: solo debe sentirse mas consistente al pasar de dashboard a cuentas, extractos, importacion, configuracion o administracion.

Los campos de fecha usan un selector propio de Atlas Balance. Al abrirlo veras el mes, los dias, la fecha seleccionada, el dia actual y las acciones `Hoy` y `Limpiar`. Si no cabe debajo del campo, se abre hacia arriba.

En tablets y pantallas pequenas se conservan los targets tactiles amplios y la navegacion inferior. Si algun texto largo o tabla concreta se desborda, hay que reportarlo con pantalla y ruta exacta; los fallos de UI vagos no se arreglan solos.

## Notas de seguridad

- Al iniciar sesion, Atlas Balance puede pedir un codigo MFA de 6 digitos.
- En el primer acceso con MFA, la pantalla muestra una clave para guardarla en una app de autenticacion. Despues hay que escribir el codigo generado para terminar el login.
- Si se cambian permisos, email o datos de un usuario, sus sesiones abiertas se cierran y tendra que entrar de nuevo.
- No guardes contrasenas en documentos.
- No pegues tokens ni credenciales en tickets, logs o notas.
- Las credenciales iniciales de instalacion deben tratarse como temporales y cambiarse en el primer acceso.
- El instalador escribe `C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt` con acceso limitado a Administrators/SYSTEM e intenta borrarlo automaticamente a las 24 horas. Si sigue ahi despues, borrarlo no es opcional.
- Los archivos `appsettings.Development.json`, `appsettings.Production.json` y `.env` son locales del servidor o del entorno de desarrollo. No van a Git.
- Para desarrollo local, copia las plantillas `appsettings.*.json.template`, rellena secretos propios y define `ATLAS_BALANCE_POSTGRES_PASSWORD` en un `.env` local.
- Si la aplicacion arranca por primera vez con una base vacia, `SeedAdmin:Password` debe estar configurado y tener al menos 12 caracteres; ya no existe una password admin por defecto. Bien. Eso era una mala idea.
- Las claves SMTP y de Exchange Rate API guardadas desde Configuracion se protegen automaticamente; las existentes en claro se migran al siguiente arranque.
- No borres, muevas ni copies a otra maquina `%ProgramData%/AtlasBalance/keys` en produccion sin plan de rotacion: ahi vive el keyring protegido que permite leer secretos cifrados.
- Las descargas de exportaciones solo sirven `.xlsx` generados dentro de la ruta `export_path`.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.
- La URL de actualizaciones debe apuntar por HTTPS al repositorio oficial de Atlas Balance en GitHub.
- Los scripts manuales de backup/restauracion piden la password en consola segura y no deben ejecutarse con passwords pegadas en comandos o documentos.
- En produccion, `AllowedHosts` debe contener el hostname real. `*` ya no arranca; comodo, pero inseguro.

## Extractos - vista tipo hoja de calculo

La tabla de `Extractos` ahora se lee mas como una hoja de calculo:

- La cabecera queda fija al desplazarte.
- La columna `Fila` queda fija al mover la tabla horizontalmente.
- Las celdas tienen bordes mas claros y foco visible al editar.
- Los importes y saldos usan alineacion derecha y numeros tabulares para comparar cifras rapido.
- Las columnas tecnicas se muestran con nombres legibles, por ejemplo `Importe` en vez de `monto`.

El funcionamiento no cambia: puedes filtrar, ordenar, editar celdas, abrir historial y cambiar columnas visibles igual que antes.
