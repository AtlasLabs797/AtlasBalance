# Atlas Balance - instalacion y actualizaciones

Version actual del paquete: `V-01.06`.

No uses el ZIP `main` de GitHub como instalador de servidor. Ese ZIP es codigo fuente. El instalador valido para esta version es `AtlasBalance-V-01.06-win-x64.zip` y, al descomprimirlo, debe contener `api\AtlasBalance.API.exe`, `watchdog\AtlasBalance.Watchdog.exe`, `scripts` y los wrappers `.cmd`.

## Que queda preparado

La version `V-01.06` deja el proyecto listo para generar un paquete instalable de Windows:

- `scripts/Build-Release.ps1`: crea el paquete `Atlas Balance Release/AtlasBalance-V-01.06-win-x64.zip`.
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

Desde `V-01.05`, la base activa Row Level Security en las tablas sensibles. El usuario de aplicacion debe ser runtime, sin superusuario, sin ownership de tablas y sin `BYPASSRLS`. La credencial de migracion/owner va separada en `ConnectionStrings:MigrationConnection`. Si alguien intenta "simplificar" usando un rol superusuario para la app, esta rompiendo la defensa de la base.

## Primera instalacion en un servidor

### 1. Generar el paquete

En la maquina de desarrollo, desde la carpeta `Atlas Balance`:

```powershell
.\scripts\Build-Release.ps1
```

Salida esperada:

```text
Atlas Balance Release\AtlasBalance-V-01.06-win-x64\
Atlas Balance Release\AtlasBalance-V-01.06-win-x64.zip
Atlas Balance Release\AtlasBalance-V-01.06-win-x64.zip.sig
```

Si quieres que la actualizacion online desde la app acepte el paquete, ejecuta `Build-Release.ps1` con `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` disponible en el entorno. La clave privada no va en el repo ni en documentacion.

### Limpieza de datos antes de publicar

Si la base local tiene datos de prueba, limpiala antes de entregar o generar una demo publicable:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Purge-DeliveryData.ps1" -ConfirmDeliveryPurge
```

La purga borra usuarios, titulares, cuentas, extractos, tokens, auditorias, backups/exportaciones registradas y consumo IA. Tambien vacia claves SMTP, OpenRouter, OpenAI y tipos de cambio externos.

No la ejecutes contra una base de cliente real salvo que quieras dejarla vacia. Esto no es un reset estetico; borra datos operativos.

Tras purgar, el primer arranque solo crea el admin inicial si `SeedAdmin:Password` esta configurado.

### 2. Copiar al servidor

1. Copia `AtlasBalance-V-01.06-win-x64.zip` al servidor.
2. Descomprime el ZIP, por ejemplo en:

```text
C:\Temp\AtlasBalance-V-01.06-win-x64
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
5. Crea o actualiza la base `atlas_balance`, el usuario owner/migracion `atlas_balance_owner` y el usuario runtime `atlas_balance_app` sin superusuario ni `BYPASSRLS`.
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
C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt
```

El archivo queda protegido para Administradores/SYSTEM. Si el explorador dice que no tienes permisos, abre PowerShell como Administrador:

```powershell
Get-Content "C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt"
```

Si reinstalas sobre una base existente, ese archivo no inventa una password nueva de admin. Mostrara que la base ya existia: usa el admin real o ejecuta el reset soportado:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AtlasBalance\scripts\Reset-AdminPassword.ps1" -InstallPath C:\AtlasBalance -AdminEmail admin@atlasbalance.local -GeneratePassword
```

Haz esto sin improvisar:

1. Entra con el admin inicial.
2. Cambia la password en el primer login.
3. Guarda la password definitiva en un gestor de passwords.
4. Borra `config\INSTALL_CREDENTIALS_ONCE.txt`.

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

### Actualizacion automatica desde la app

En `Configuracion > Sistema`, deja como repositorio de actualizaciones:

```text
https://github.com/AtlasLabs797/AtlasBalance
```

Al pulsar `Verificar actualizacion`, Atlas Balance consulta el ultimo GitHub Release oficial.

Al pulsar `Actualizar ahora`, Atlas Balance:

1. Descarga el asset `AtlasBalance-*-win-x64.zip` y su firma `*.zip.sig` del release oficial.
2. Valida digest y firma RSA/SHA-256 antes de usarlo.
3. Lo extrae dentro de `C:\AtlasBalance\updates`.
4. Crea backup PostgreSQL previo.
5. Crea rollback de binarios.
6. Reemplaza la API conservando configuracion.
7. Arranca de nuevo y aplica migraciones al iniciar.
8. Comprueba `/api/health`; si no responde, revierte binarios.

Si no puede verificar firma, crear backup previo o recuperar `/api/health`, no actualiza. Bien. Actualizar una app financiera sin backup o sin firma es una forma elegante de pedir problemas.

La instalacion debe tener `UpdateSecurity:ReleaseSigningPublicKeyPem` o `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM`. Desde el paquete firmado `V-01.06`, el instalador escribe una clave publica por defecto si no se proporciona override. Sin clave publica valida, la actualizacion online falla cerrado.

### 1. Generar nuevo paquete

En desarrollo, cuando haya una version nueva:

```powershell
.\scripts\Build-Release.ps1 -Version V-01.06
```

### 2. Copiar al servidor

1. Copia el ZIP nuevo al servidor.
2. Descomprime en una carpeta temporal, por ejemplo:

```text
C:\Temp\AtlasBalance-V-01.06-win-x64
```

### 3. Ejecutar actualizador

Desde la carpeta descomprimida, como Administrador:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance
```

Desde `V-01.05`, `update.cmd` pasa `-InstallPath` de forma explicita al actualizador. Si vienes de un paquete anterior y el wrapper falla, ejecuta el actualizador directo desde la carpeta descomprimida:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Actualizar-AtlasBalance.ps1" -InstallPath C:\AtlasBalance
```

Si ya estas trabajando desde una instalacion que tiene los scripts nuevos y quieres apuntar a un paquete descargado/descomprimido:

```powershell
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-01.06-win-x64 -InstallPath C:\AtlasBalance
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

## Notas de seguridad V-01.06

- `SeedAdmin:Password` y passwords de usuario requieren minimo 12 caracteres.
- El reset/cambio de password invalida sesiones anteriores; despues de actualizar a esta version, los tokens antiguos sin `security_stamp` no sirven.
- PostgreSQL aplica Row Level Security con politicas por usuario, integracion, admin y operaciones internas. El contexto va firmado; el rol runtime de la app no debe tener `BYPASSRLS` ni ser owner de las tablas.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.
- La URL de actualizaciones queda limitada al repo oficial de Atlas Balance en GitHub por HTTPS y el paquete online debe venir firmado con `.zip.sig`.
- `config\INSTALL_CREDENTIALS_ONCE.txt` se crea para el arranque inicial con ACL limitada a Administrators/SYSTEM y se programa para borrado automatico en 24 horas. No lo uses como almacen de secretos.

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

Para `V-01.06` queda fijado en:

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
5. Exporta `ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM` en el entorno de release.
6. Ejecuta `scripts/Build-Release.ps1 -Version V-XX.XX` desde `Atlas Balance`.
