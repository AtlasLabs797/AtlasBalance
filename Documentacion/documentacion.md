# Atlas Balance - instalacion y actualizaciones

Version actual del paquete: `V-01.04`.

No uses el ZIP `main` de GitHub como instalador de servidor. Ese ZIP es codigo fuente. El instalador valido es `AtlasBalance-V-01.04-win-x64.zip` y, al descomprimirlo, debe contener `api\GestionCaja.API.exe`, `watchdog\GestionCaja.Watchdog.exe`, `scripts` y los wrappers `.cmd`.

## Que queda preparado

La version `V-01.04` deja el proyecto listo para generar un paquete instalable de Windows:

- `scripts/Build-Release.ps1`: crea el paquete `Atlas Balance Release/AtlasBalance-V-01.04-win-x64.zip`.
- `install.cmd`: instalador de un clic.
- `update.cmd`: actualizador de un clic.
- `uninstall.cmd`: desinstalador de un clic.
- `start.cmd`: arrancador de un clic.
- `Instalar Atlas Balance.cmd`: instalador de servidor.
- `Actualizar Atlas Balance.cmd`: actualizador de servidor.
- `Atlas Balance.cmd`: lanzador; arranca servicios y abre la app.
- Atajo `Atlas Balance.lnk`: lo crea el instalador en escritorio/menu inicio con el logo de la app.

El frontend queda servido por el backend ASP.NET Core. En produccion no hace falta Node ni Vite en el servidor.

## Dependencias reales

El paquete publicado es self-contained:

- No requiere instalar .NET Runtime en el servidor.
- No requiere instalar Node.js en el servidor.
- Si requiere PostgreSQL 16+. PostgreSQL 17 es valido.
- En Windows Server 2019, la ruta recomendada es instalar PostgreSQL manualmente y pasar `-PostgresBinPath`; `winget` es un intento automatico, no una garantia.

Punto importante: PostgreSQL no es un detalle. Ahi viven los datos. Tratarlo como una carpeta mas seria una estupidez cara.

## Primera instalacion en un servidor

### 1. Generar el paquete

En la maquina de desarrollo, desde la carpeta `Atlas Balance`:

```powershell
.\scripts\Build-Release.ps1
```

Salida esperada:

```text
Atlas Balance Release\AtlasBalance-V-01.04-win-x64\
Atlas Balance Release\AtlasBalance-V-01.04-win-x64.zip
```

### 2. Copiar al servidor

1. Copia `AtlasBalance-V-01.04-win-x64.zip` al servidor.
2. Descomprime el ZIP, por ejemplo en:

```text
C:\Temp\AtlasBalance-V-01.04-win-x64
```

### 3. Ejecutar instalador

Abre PowerShell o CMD como Administrador en la carpeta descomprimida y ejecuta:

```powershell
.\install.cmd -InstallPath C:\AtlasBalance -ServerName NOMBRE_DEL_SERVIDOR -ApiPort 443
```

`install.cmd` se autoeleva si hace falta y pasa `-InstallDependencies` por defecto. En un servidor limpio intenta instalar PostgreSQL 16 con `winget`, genera password de superusuario, crea la base y deja la app lista.

Si `winget` falla en Windows Server 2019, instala PostgreSQL manualmente desde el instalador oficial y relanza el comando indicando la carpeta `bin`.

Si quieres usar una instancia PostgreSQL existente:

```powershell
.\install.cmd -InstallPath C:\AtlasBalance -ServerName NOMBRE_DEL_SERVIDOR -PostgresAdminPassword "PASSWORD_POSTGRES" -PostgresBinPath "C:\Program Files\PostgreSQL\17\bin"
```

Limitacion honesta: si ya tienes PostgreSQL instalado y no das password de administrador, ningun script puede adivinarla. Eso no es automatizacion; eso seria magia barata.

Alternativa soportada si quieres saltarte el wrapper:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Instalar-AtlasBalance.ps1" -InstallPath C:\AtlasBalance -ServerName NOMBRE_DEL_SERVIDOR -ApiPort 443 -PostgresAdminPassword "PASSWORD_POSTGRES" -PostgresBinPath "C:\Program Files\PostgreSQL\17\bin"
```

El instalador hace esto:

1. Crea `C:\AtlasBalance`.
2. Copia backend, frontend estatico y watchdog.
3. Genera secretos seguros para JWT, Watchdog, certificado, DB y admin inicial.
4. Instala o localiza PostgreSQL.
5. Crea o actualiza la base `atlas_balance` y el usuario `atlas_balance_app`.
6. Genera `appsettings.Production.json` para API y Watchdog.
7. Genera certificado HTTPS local en `C:\AtlasBalance\certs`.
8. Instala servicios Windows:
   - `AtlasBalance.PostgreSQL` si PostgreSQL fue gestionado por el instalador.
   - `AtlasBalance.API`
   - `AtlasBalance.Watchdog`
9. Abre firewall para el puerto HTTPS.
10. Crea el atajo `Atlas Balance` con el logo.
11. Arranca PostgreSQL, Watchdog y API en ese orden.

No metas secretos en los `appsettings*.json` versionados. Para entornos manuales, usa las plantillas `appsettings.Production.json.template` de API y Watchdog, crea los `appsettings.Production.json` reales en el servidor y mantenlos fuera de Git.

### 4. Primer acceso

Abre:

```text
https://NOMBRE_DEL_SERVIDOR
```

El instalador guarda las credenciales iniciales en:

```text
C:\AtlasBalance\INSTALL_CREDENTIALS_ONCE.txt
```

El archivo queda protegido para Administradores/SYSTEM. Si el explorador dice que no tienes permisos, abre PowerShell como Administrador:

```powershell
Get-Content "C:\AtlasBalance\INSTALL_CREDENTIALS_ONCE.txt"
```

Si reinstalas sobre una base existente, ese archivo no inventa una password nueva de admin. Mostrara que la base ya existia: usa el admin real o ejecuta el reset soportado:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AtlasBalance\scripts\Reset-AdminPassword.ps1" -InstallPath C:\AtlasBalance -AdminEmail admin@atlasbalance.local -GeneratePassword
```

Haz esto sin improvisar:

1. Entra con el admin inicial.
2. Cambia la password en el primer login.
3. Guarda la password definitiva en un gestor de passwords.
4. Borra `INSTALL_CREDENTIALS_ONCE.txt`.

Dejar ese archivo vivo en el servidor es pedir que te roben el sistema.

### 6. Certificado para clientes

El certificado publico queda en:

```text
C:\AtlasBalance\certs\atlas-balance.cer
```

Para evitar aviso del navegador en otros PCs, instala ese `.cer` en `Entidades de certificacion raiz de confianza` de cada cliente, o usa un certificado real emitido por la autoridad interna de la empresa.

Comando para cliente o servidor, en consola elevada:

```powershell
certutil -addstore -f Root "C:\AtlasBalance\certs\atlas-balance.cer"
```

Si `curl.exe -k` responde pero el navegador falla, no diagnostiques "API caida" a lo bruto: probablemente el certificado no esta confiado en el cliente.

## Abrir la app despues de instalada

Haz doble click en `start.cmd` desde el paquete o en el atajo `Atlas Balance` creado por el instalador.

Ese atajo:

1. Arranca `AtlasBalance.PostgreSQL` si fue gestionado por el instalador y esta parado.
2. Arranca `AtlasBalance.Watchdog` si esta parado.
3. Arranca `AtlasBalance.API` si esta parado.
4. Abre el navegador en la URL configurada.

## Actualizar sin perder datos

Regla de oro: los datos viven en PostgreSQL, no en la carpeta `api`. Una actualizacion buena cambia binarios y conserva base de datos, backups, exports y configuracion.

### 1. Generar nuevo paquete

En desarrollo, cuando haya una version nueva:

```powershell
.\scripts\Build-Release.ps1 -Version V-01.04
```

### 2. Copiar al servidor

1. Copia el ZIP nuevo al servidor.
2. Descomprime en una carpeta temporal, por ejemplo:

```text
C:\Temp\AtlasBalance-V-01.04-win-x64
```

### 3. Ejecutar actualizador

Desde la carpeta descomprimida, como Administrador:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance
```

Si ya estas trabajando desde una instalacion que tiene los scripts nuevos y quieres apuntar a un paquete descargado/descomprimido:

```powershell
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-01.04-win-x64 -InstallPath C:\AtlasBalance
```

El actualizador hace esto:

1. Crea backup PostgreSQL previo en `C:\AtlasBalance\backups`.
2. Crea copia rollback de binarios en `C:\AtlasBalance\backups\app_before_update_*`.
3. Detiene `AtlasBalance.API` y `AtlasBalance.Watchdog`.
4. Reemplaza binarios de `api` y `watchdog`.
5. Conserva `appsettings.Production.json`, logs, backups y exports.
6. Actualiza scripts operativos instalados (`start`, `update`, `uninstall`, reset admin y certificado cliente).
7. Actualiza `VERSION` y `atlas-balance.runtime.json`.
8. Arranca servicios.
9. La API aplica migraciones EF Core automaticamente al arrancar.
10. Verifica `/api/health` con `curl.exe -k`.

Si el health check falla, no sigas tocando a ciegas. Revisa servicios y logs. La copia rollback de binarios queda indicada en consola.

## Desinstalar completamente

Desde el paquete, como Administrador:

```powershell
.\uninstall.cmd -InstallPath C:\AtlasBalance
```

El desinstalador elimina servicios, firewall, atajos, `%ProgramData%\AtlasBalance`, la carpeta instalada y PostgreSQL gestionado si lo creo `install.cmd`.

Si usaste una base externa, el script no intenta borrarla sin credenciales explicitas. Borrar una BD externa a ciegas seria una temeridad, no una feature.

### 4. Verificar

Comprueba:

```powershell
curl.exe -k -v https://NOMBRE_DEL_SERVIDOR/api/health
```

En Windows Server 2019, `Invoke-WebRequest` puede dar falsos negativos con TLS/certificados autofirmados. La prueba primaria es `curl.exe -k`.

Luego entra a la app y revisa:

1. Login.
2. Dashboard.
3. Extractos.
4. Backups.
5. Exportaciones.

## Que no debes tocar al actualizar

No borres ni machaques estas rutas:

```text
C:\AtlasBalance\api\appsettings.Production.json
C:\AtlasBalance\watchdog\appsettings.Production.json
C:\AtlasBalance\backups
C:\AtlasBalance\exports
C:\AtlasBalance\certs
Datos de PostgreSQL
```

Si alguien te dice "copia encima toda la carpeta y ya", dile que no. Eso es exactamente como se pierden configuraciones y luego todo el mundo mira al techo.

## Notas de seguridad V-01.04

- `SeedAdmin:Password` y passwords de usuario requieren minimo 12 caracteres.
- El reset/cambio de password invalida sesiones anteriores; despues de actualizar a esta version, los tokens antiguos sin `security_stamp` no sirven.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.
- La URL de actualizaciones queda limitada al repo oficial de Atlas Balance en GitHub por HTTPS.
- `INSTALL_CREDENTIALS_ONCE.txt` se crea para el arranque inicial y se programa para borrado automatico en 24 horas. No lo uses como almacen de secretos.

## Recuperacion si una actualizacion falla

1. Para servicios:

```powershell
Stop-Service AtlasBalance.API
Stop-Service AtlasBalance.Watchdog
```

2. Restaura los binarios desde:

```text
C:\AtlasBalance\backups\app_before_update_*
```

3. Si tambien hay que volver la base de datos, usa el `.dump` creado antes de actualizar con `pg_restore`.
4. Arranca servicios:

```powershell
Start-Service AtlasBalance.Watchdog
Start-Service AtlasBalance.API
```

## Versionado

La version visible del backend se toma de `AssemblyInformationalVersion`.

Para `V-01.04` queda fijado en:

```text
Atlas Balance/Directory.Build.props
Atlas Balance/VERSION
Atlas Balance/frontend/package.json -> appVersion
Documentacion/Versiones/version_actual.md
```

Para una version futura:

1. Cambia `VERSION`.
2. Cambia `Directory.Build.props`.
3. Cambia `frontend/package.json` y `package-lock.json`.
4. Actualiza `Documentacion/Versiones/version_actual.md` y crea o actualiza el archivo de version correspondiente.
5. Ejecuta `scripts/Build-Release.ps1 -Version V-XX.XX` desde `Atlas Balance`.
