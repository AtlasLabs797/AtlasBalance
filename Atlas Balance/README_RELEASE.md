# Atlas Balance V-01.06 - release Windows x64

Este paquete es autonomo para servidor Windows: el frontend ya esta compilado, el backend y Watchdog van publicados self-contained y la base de datos se prepara desde el instalador.

El ZIP `main` de GitHub no sirve como instalador. Usa `AtlasBalance-V-01.06-win-x64.zip`; dentro deben existir `api\AtlasBalance.API.exe` y `watchdog\AtlasBalance.Watchdog.exe`.

## Scripts de un clic

- `install.cmd`: instala dependencias, prepara PostgreSQL, crea base de datos, copia API/Watchdog/frontend estatico, genera configuracion de produccion, certificado local, servicios Windows y atajos.
- `update.cmd`: actualiza una instalacion existente, crea backup previo de PostgreSQL, conserva configuracion y deja que la API aplique migraciones al arrancar.
- `uninstall.cmd`: elimina servicios, reglas de firewall, atajos, claves de Data Protection, carpeta instalada y PostgreSQL gestionado si lo instalo Atlas Balance.
- `start.cmd`: arranca PostgreSQL gestionado, Watchdog y API en ese orden; el frontend se sirve desde la API.

## Uso rapido

Ejecuta los `.cmd` con doble clic. Si Windows pide permisos de administrador, aceptalo; sin eso no se pueden crear servicios ni abrir firewall.

```powershell
.\install.cmd
.\start.cmd
.\update.cmd
.\uninstall.cmd
```

Por defecto la instalacion queda en `C:\AtlasBalance`. Para usar otra ruta:

```powershell
.\install.cmd -InstallPath D:\AtlasBalance
.\start.cmd -InstallPath D:\AtlasBalance
.\update.cmd -InstallPath D:\AtlasBalance
.\uninstall.cmd -InstallPath D:\AtlasBalance
```

## Base de datos

El requisito real es PostgreSQL 16+. PostgreSQL 17 es valido.

En Windows Server 2019, instala PostgreSQL manualmente si `winget` falla o no esta disponible. `winget` no es una base fiable para prometer instalacion "one click" en servidores limpios.

El instalador genera passwords fuertes y guarda las credenciales iniciales en:

```text
C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt
```

El directorio `config` queda restringido a Administrators/SYSTEM antes de escribir ese archivo. Guarda ese contenido en un gestor de passwords y borra el archivo despues del primer acceso. Si se queda en el servidor, quedan credenciales recuperables.

Si ya tienes PostgreSQL y quieres usarlo:

```powershell
.\install.cmd -PostgresAdminPassword "PASSWORD_POSTGRES" -PostgresBinPath "C:\Program Files\PostgreSQL\17\bin"
```

No documentes passwords reales en tickets, docs ni chats.

Si reinstalas sobre una BD existente, las credenciales iniciales no se regeneran. Usa el admin ya creado o:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Reset-AdminPassword.ps1" -InstallPath C:\AtlasBalance -AdminEmail admin@atlasbalance.local -GeneratePassword
```

Ejecuta el reset como Administrador. Si usas `-GeneratePassword`, la password temporal queda en `C:\AtlasBalance\config\RESET_ADMIN_CREDENTIALS_ONCE.txt`.

Health check recomendado:

```powershell
curl.exe -k -v https://localhost/api/health
```

## Actualizar una instalacion existente

Desde la carpeta descomprimida de este paquete:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance
```

Si la instalacion ya tiene los scripts nuevos, tambien puedes lanzar desde la carpeta instalada apuntando al paquete:

```powershell
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-01.06-win-x64 -InstallPath C:\AtlasBalance
```

El actualizador crea backup previo, conserva configuracion, reemplaza API/Watchdog, actualiza scripts operativos instalados, actualiza `VERSION`/runtime y valida `/api/health` con `curl.exe -k`.

## Actualizacion desde la app

En `Configuracion > Sistema`, deja el repositorio:

```text
https://github.com/AtlasLabs797/AtlasBalance
```

`Verificar actualizacion` consulta el ultimo GitHub Release. `Actualizar ahora` descarga el asset `AtlasBalance-*-win-x64.zip`, lo valida, lo prepara en `C:\AtlasBalance\updates` y pide al Watchdog aplicar la API nueva.

El Watchdog crea backup PostgreSQL previo, rollback de binarios y health check posterior. Si no puede crear backup o la API no responde despues de actualizar, revierte o rechaza la operacion.
