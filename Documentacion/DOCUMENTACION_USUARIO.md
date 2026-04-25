# Documentacion de usuario

## Ubicacion principal

La aplicacion esta en la carpeta `Atlas Balance`.

## Paquetes de instalacion

Los paquetes de release estan en:

```text
Atlas Balance/Atlas Balance Release
```

Para instalar o actualizar desde una build local, usa los archivos del paquete generado para la version correspondiente.

No instales desde el ZIP `main` de GitHub ni desde una carpeta fuente. El paquete instalable debe llamarse como `AtlasBalance-V-01.04-win-x64.zip` y contener `api\GestionCaja.API.exe`, `watchdog\GestionCaja.Watchdog.exe`, `scripts` y wrappers `.cmd`.

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

Para actualizar una instalacion ya existente, descomprime el paquete nuevo y ejecuta:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance
```

Si la instalacion ya tiene los scripts actualizados, tambien vale:

```powershell
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-01.04-win-x64 -InstallPath C:\AtlasBalance
```

La distribucion oficial de paquetes se publica como asset en GitHub Releases:

```text
https://github.com/AtlasLabs797/AtlasBalance/releases
```

## Documentacion util

- Instalacion y actualizaciones: `Documentacion/documentacion.md`
- Version actual: `Documentacion/Versiones/version_actual.md`
- Cambios tecnicos: `Documentacion/DOCUMENTACION_CAMBIOS.md`

## Importacion de extractos

En la pantalla de validacion, las filas con solo concepto y sin fecha, monto ni saldo ya no bloquean la importacion. Se muestran como avisos y quedan seleccionables.

Cuando se importan, Atlas Balance usa la fecha y el saldo de la ultima fila valida anterior y guarda el monto como `0`. Si una fila tiene datos mezclados o ambiguos, sigue apareciendo como error y no se importa.

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
- `Efectivo`: caja o saldo manual sin formato de importacion.
- `Plazo fijo`: dinero inmovilizado hasta una fecha de vencimiento.

Al crear una cuenta de plazo fijo debes indicar fecha de inicio, fecha de vencimiento y si es renovable. Opcionalmente puedes informar interes previsto, cuenta de referencia y notas.

En el dashboard de una cuenta de plazo fijo, la fecha de vencimiento aparece bajo el nombre de la cuenta, junto con los dias restantes o el aviso de vencido y el estado actual.

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
- Plazos fijos: monto total, intereses aproximados y dias hasta el proximo vencimiento.

Los saldos por titular se agrupan por Empresa, Autonomo y Particular.

## Interfaz

La interfaz mantiene el mismo funcionamiento, pero ahora los botones, campos, pestanas, tarjetas, tablas y estados de foco usan un sistema visual comun. No cambia el flujo de trabajo: solo debe sentirse mas consistente al pasar de dashboard a cuentas, extractos, importacion, configuracion o administracion.

Los campos de fecha usan un selector propio de Atlas Balance. Al abrirlo veras el mes, los dias, la fecha seleccionada, el dia actual y las acciones `Hoy` y `Limpiar`. Si no cabe debajo del campo, se abre hacia arriba.

En tablets y pantallas pequenas se conservan los targets tactiles amplios y la navegacion inferior. Si algun texto largo o tabla concreta se desborda, hay que reportarlo con pantalla y ruta exacta; los fallos de UI vagos no se arreglan solos.

## Notas de seguridad

- No guardes contrasenas en documentos.
- No pegues tokens ni credenciales en tickets, logs o notas.
- Las credenciales iniciales de instalacion deben tratarse como temporales y cambiarse en el primer acceso.
- El instalador intenta borrar `INSTALL_CREDENTIALS_ONCE.txt` automaticamente a las 24 horas. Si sigue ahi despues, borrarlo no es opcional.
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
