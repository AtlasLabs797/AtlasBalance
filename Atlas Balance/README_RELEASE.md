# Atlas Balance V-01.03 - release Windows x64

Este paquete es autonomo para servidor Windows: el frontend ya esta compilado, el backend y Watchdog van publicados self-contained y la base de datos se prepara desde el instalador.

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

El instalador intenta preparar PostgreSQL 16 con `winget` cuando no se pasa una instancia existente. Genera passwords fuertes y guarda las credenciales iniciales en:

```text
C:\AtlasBalance\INSTALL_CREDENTIALS_ONCE.txt
```

Guarda ese contenido en un gestor de passwords y borra el archivo despues del primer acceso. Dejarlo ahi es mala seguridad con sombrero.

Si ya tienes PostgreSQL y quieres usarlo:

```powershell
.\install.cmd -PostgresAdminPassword "PASSWORD_POSTGRES" -PostgresBinPath "C:\Program Files\PostgreSQL\16\bin"
```

No documentes passwords reales en tickets, docs ni chats.
