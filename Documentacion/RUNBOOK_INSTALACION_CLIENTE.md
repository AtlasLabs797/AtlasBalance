# RUNBOOK: Instalacion de Atlas Balance en el servidor del cliente

Checklist operativo para el dia de instalacion en el servidor on-premise del cliente.

Herramienta principal: el paquete de release incluye `scripts/install.ps1` (lanza `Instalar-AtlasBalance.ps1` como Administrador), que automatiza instalacion, certificado, configuracion y servicios. Este runbook es la lista de control alrededor de ese instalador, no un procedimiento manual alternativo.

## 1. Requisitos previos

- [ ] Windows Server 2016+, x64
- [ ] PostgreSQL 16 instalado y corriendo en el servidor
  - [ ] Verificar puerto (por defecto 5432, la app usa 5433 por defecto)
  - [ ] Usuario `app_user` creado (solo lectura/escritura de BD)
  - [ ] Usuario `atlas_owner` creado (propietario de schema, migraciones)
  - [ ] Base de datos `atlas_balance` vacia creada
- [ ] .NET 8 Runtime (si el paquete de release es framework-dependent) o verificar que el paquete es self-contained
- [ ] Puerto HTTPS (443, o puerto alternativo en LAN) abierto en firewall solo para clientes de la LAN
- [ ] Si el despliegue sera publico por Internet: usar `-UseReverseProxy`, abrir solo 80/443 al proxy y mantener la API interna en loopback
- [ ] Puerto 5001 (Watchdog) accesible SOLO desde localhost (nunca exponerlo a la red)
- [ ] Certificado HTTPS para el hostname (ver seccion 3: el instalador genera uno self-signed; con CA interna, mejor)
- [ ] Descomprimir el paquete de release en `C:\AtlasBalance`
  - [ ] Carpeta `C:\AtlasBalance\api` con ejecutable principal
  - [ ] Carpeta `C:\AtlasBalance\watchdog` con Windows Service del Watchdog

## 2. Configuracion de secretos obligatorios

Los siguientes parametros NO PUEDEN quedar vacios o en placeholder. La aplicacion se rechazara arrancar sin ellos.

Archivo: `C:\AtlasBalance\api\appsettings.json`

- [ ] **ConnectionStrings.DefaultConnection**: conexion a PostgreSQL con usuario `app_user`
  ```
  "Host=<hostname-o-127.0.0.1>;Port=5433;Database=atlas_balance;Username=app_user;Password=<password-generada-aleatoria>"
  ```
  - No documentar la password en este checklist. Guardar en gestor de secretos del cliente.

- [ ] **JwtSettings.Secret**: clave para firmar JWT (minimo 32 caracteres aleatorios, sin espacios)
  - Generar con `System.Security.Cryptography.RandomNumberGenerator` o `openssl rand -base64 32`
  - Usar la salida en bruto (sin espacios, sin --)

- [ ] **WatchdogSettings.SharedSecret**: clave para autenticar llamadas del Watchdog (minimo 32 caracteres aleatorios)
  - Usar el mismo metodo que JWT

- [ ] **SeedAdmin.Password**: contrasena inicial del admin (minimo 12 caracteres, sin placeholders); obligatoria antes del primer arranque

- [ ] **AllowedHosts**: hostname EXPLICITO del servidor (sin wildcard)
  ```
  "AllowedHosts": "tesoreria.miempresa.local"
  ```
  - NO usar `*`. La app rechazara requests con Host diferente.
  - En modo Internet, debe incluir el dominio publico (`balance.tudominio.com`), no solo el nombre Windows del servidor.

Archivo: `C:\AtlasBalance\watchdog\appsettings.json`

- [ ] **WatchdogSettings.SharedSecret**: IGUAL que en API (ambos deben coincidir)

- [ ] **WatchdogSettings.DbPassword**: password de usuario `app_user` (mismo que en ConnectionStrings de API)

- [ ] **WatchdogSettings.BackupPath**: carpeta de backups (`C:\AtlasBalance\backups` es por defecto)
  - [ ] Crear carpeta si no existe
  - [ ] Verificar que el usuario del Windows Service tiene RWX en esa carpeta

## 3. Certificado HTTPS

- [ ] Por defecto el instalador (`Instalar-AtlasBalance.ps1`) genera un certificado self-signed `atlas-balance.pfx` en `C:\AtlasBalance\certs` y configura Kestrel con el.
  - Con self-signed hay que distribuir el certificado publico a cada equipo cliente con `scripts/install-cert-client.ps1` para evitar avisos del navegador.
- [ ] Opcion recomendada si el cliente tiene CA interna: emitir certificado para el hostname y reemplazar el `.pfx` generado (misma ruta y password que configuro el instalador en la seccion `Kestrel` de `appsettings.json`).
- [ ] Para Internet con dominio publico, usar reverse proxy: el certificado real vive en Caddy/IIS/Nginx y Kestrel escucha solo en `http://127.0.0.1:5000`. No intentes hacer competir Kestrel y el proxy por el puerto 443.
  ```powershell
  .\install.cmd -InstallPath C:\AtlasBalance -UseReverseProxy -PublicHost balance.tudominio.com -InternalApiPort 5000
  ```
- [ ] Recordatorio: las cookies de sesion usan prefijo `__Host-` y exigen HTTPS; no intentar servir la app por HTTP plano.
- [ ] Verificar que el puerto HTTPS elegido no esta ocupado
  ```powershell
  netstat -ano | findstr :443
  ```

## 4. Primer arranque

### 4.1 Migraciones de base de datos

- [ ] Arrancar servicio PostgreSQL (debe estar corriendo)
- [ ] Las migraciones se aplican AUTOMATICAMENTE al arrancar la API (`Database.Migrate()` en el startup). No hay comando manual de migracion.
  - Si `ConnectionStrings:MigrationConnection` esta configurada, se usa esa conexion (usuario `atlas_owner`) para migrar; la runtime usa `app_user`.
- [ ] Tras el primer arranque, revisar el log (`C:\AtlasBalance\api\logs\atlas-balance-<fecha>.log`) para confirmar que no hubo errores de migracion ni de seed.

### 4.2 Registrar Windows Services

- [ ] Los servicios los registra el instalador; si hay que hacerlo a mano, usar `scripts/install-services.ps1` como Administrador (no inventar comandos `sc.exe` sueltos).
- [ ] Verificar que ambos estan corriendo
  ```powershell
  Get-Service | Where-Object { $_.Name -like "AtlasBalance*" }
  ```
  - [ ] Ambos deben mostrar status `Running`

### 4.3 Login admin inicial

- [ ] Configurar `SeedAdmin:Email` y `SeedAdmin:Password` en `appsettings.json` ANTES del primer arranque.
  - La app se niega a arrancar sin `SeedAdmin:Password`, y en produccion rechaza placeholders y contrasenas que no cumplan la politica (minimo 12 caracteres).
- [ ] Abrir navegador en una maquina cliente de la LAN y navegar a `https://<hostname>/`
- [ ] Login con el email del seed admin y la password configurada
- [ ] El primer login exige cambio de password
  - [ ] Cambiar a una contrasena fuerte y guardarla en el gestor de secretos corporativo, no compartirla por email
  - [ ] Si se pierde, existe `scripts/Reset-AdminPassword.ps1` como via de recuperacion en el servidor

### 4.4 MFA obligatoria

- [ ] Tras el cambio de password, la app exige alta de MFA (TOTP)
  - [ ] Escanear el codigo QR o copiar la clave manual a una app TOTP (Google Authenticator, Microsoft Authenticator, etc.)
  - [ ] Nota: no hay codigos de recuperacion MFA; si el admin pierde el dispositivo TOTP, la recuperacion es por intervencion en servidor. Conviene dar de alta un segundo usuario ADMIN como respaldo.
- [ ] Confirmar el primer codigo TOTP (6 digitos)

## 5. Backups y restauracion

### 5.1 Configurar destino de backups

- [ ] Carpeta de backups debe existir y ser accesible por el Windows Service
  ```powershell
  New-Item -ItemType Directory -Path "C:\AtlasBalance\backups" -Force
  icacls "C:\AtlasBalance\backups" /grant "NT SERVICE\AtlasBalanceWatchdog:(OI)(CI)F" /T
  ```

### 5.2 Generar backup inicial

- [ ] En la interfaz, ir a `Sistema > Backups`

- [ ] Crear un backup manual
  - [ ] Nombrar: `BACKUP_INICIAL_<FECHA>`
  - [ ] Ingresar una clave de cifrado fuerte (minimo 32 caracteres)
  - [ ] Guardar la clave en el gestor de secretos del cliente

- [ ] Esperar a que termine (puede tomar varios minutos segun tamaño de BD)

- [ ] Verificar que el archivo `.backup.enc` aparece en `C:\AtlasBalance\backups`

### 5.3 Restauracion de prueba

ANTES de dar por entregada la instalacion, probar un restore. La restauracion se hace con el script CLI del paquete (no hay restore desde la interfaz):

- [ ] Detener el servicio API
  ```powershell
  Stop-Service AtlasBalance.API
  ```

- [ ] Restaurar desde el backup inicial con el script incluido (ADVERTENCIA: reemplaza TODA la base de datos)
  ```powershell
  .\scripts\restore-backup.ps1 -BackupFile "C:\AtlasBalance\backups\<nombre-backup>"
  ```
  - El script pide la password de BD de forma segura; parametros opcionales: `-DbName`, `-DbUser`, `-DbHost`, `-DbPort`, `-PgBinPath`.

- [ ] Verificar que no hay errores

- [ ] Arrancar API de nuevo y verificar que los datos estan intactos (login + dashboard visible)

- [ ] Documentar fecha, nombre del backup y resultado del ensayo de restauracion

Nota: en el repositorio existe ademas `Atlas Balance/scripts/Test-BackupRestore.ps1`, que ensaya el ciclo pg_dump -> pg_restore -> verificacion de recuentos contra una BD temporal sin tocar la original. Util como ensayo no destructivo.

## 6. Auto-update (si aplica)

Si el cliente quiere actualizaciones automaticas desde GitHub releases:

- [ ] Obtener la clave publica de firma de releases (`UpdateSecurity.ReleaseSigningPublicKeyPem`)
  - [ ] Guardarla en `appsettings.json` de la API

- [ ] Configurar token de GitHub (opcional, para evitar rate limiting)
  - [ ] `GitHubSettings.UpdateToken` en `appsettings.json`
  - [ ] Token debe ser personal (Classic) con solo permiso `public_repo`
  - [ ] NO documentar el token en ningun archivo del servidor; guardar en gestor de secretos

- [ ] Habilitar el Watchdog para que verifique actualizaciones cada X horas
  - [ ] Configurar intervalo en `appsettings.json` (por defecto cada 6 horas)

### Pendiente operativo conocido

- La clave privada de firma debe estar en GitHub Secrets de https://github.com/AtlasLabs797/AtlasBalance
- Mantener una copia segura de la clave privada fuera del repositorio (en un vault corporativo)
- Sin la clave privada, no es posible firmar nuevas releases; avisar si se pierde

## 7. Decisiones sobre IA

Si el cliente quiere usar las funciones de IA:

- [ ] **Decidir con el cliente si se activa IA**
  - Opcion A: entregar con IA desactivada (default seguro; sin API key configurada la funcion no opera)
  - Opcion B: activar con OpenRouter / OpenAI / MiniMax

- [ ] Si se activa:
  - [ ] Generar la API key en la plataforma del proveedor y pasarla por canal seguro (no email sin cifrar)
  - [ ] Configurarla desde la interfaz de administracion (Configuracion); se guarda CIFRADA en BD (DataProtection/DPAPI), NO va en `appsettings.json`
  - [ ] Documentar el coste/mes estimado

- [ ] Si se usan datos reales con cualquier proveedor:
  - [ ] ACCION DE NEGOCIO: obtener confirmacion escrita (o configuracion de cuenta) de retencion cero / no entrenamiento con los datos enviados
  - [ ] No activar IA con datos reales sin esa confirmacion

## 8. Red y firewall

- [ ] Firewall debe permitir:
  - [ ] Entrada HTTPS (puerto 443 o alternativo) SOLO desde rango de IPs de la LAN del cliente
  - [ ] NO abrir el puerto 5001 del Watchdog a internet (solo localhost)
  - [ ] Salida HTTP/HTTPS si se usa IA o auto-update (para contactar OpenRouter/GitHub)

- [ ] Si hay proxy inverso (IIS, Nginx, etc.):
  - [ ] Configurar `ForwardedHeaders.KnownProxies` con la IP del proxy
  - [ ] La app confia en headers `X-Forwarded-For` SOLO si vienen del proxy declarado

- [ ] Verificar que PostgreSQL NO es accesible desde la red (puerto 5432/5433 debe estar cerrado al exterior)

## 9. Verificacion final

- [ ] GET `/api/health` devuelve `200 OK` con `{ "status": "healthy" }`
  ```powershell
  $cert = [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
  Invoke-WebRequest -Uri "https://tesoreria.miempresa.local/api/health" -SkipCertificateCheck
  ```

- [ ] Login de usuario administrador funciona
  - [ ] Cambiar password OK
  - [ ] MFA se sincroniza OK
  
- [ ] Login de usuario normal funciona (crear un usuario de prueba si es necesario)
  - [ ] Dashboard carga sin errores
  - [ ] Puede ver al menos una cuenta (si hay datos)

- [ ] Backup manual se puede crear desde interfaz
  - [ ] Archivo aparece en `C:\AtlasBalance\backups`

- [ ] Revisar logs de la aplicacion (Serilog escribe a archivo, no al Visor de eventos)
  ```powershell
  Get-Content "C:\AtlasBalance\api\logs\atlas-balance-$(Get-Date -Format yyyyMMdd).log" -Tail 50
  ```
  - [ ] No hay errores (warnings OK, pero revisar contexto)

- [ ] Verificar que ambos servicios se reinician solos al rebootear el servidor
  - [ ] Apagar el servidor
  - [ ] Encender de nuevo
  - [ ] Esperar 30 segundos
  - [ ] Verificar que API y Watchdog estan en status `Running`

## 10. Traspaso y documentacion

- [ ] Entregar al cliente:
  - [ ] Credenciales admin (username/password temporal aceptado, sera cambiado el primer login)
  - [ ] Clave del backup inicial (en gestor de secretos corporativo)
  - [ ] Clave JWT y SharedSecret del Watchdog (solo si el cliente lo requiere, normalmente no)
  - [ ] Documento con procedimiento de restauracion en caso de emergencia

- [ ] Documentar en la bitacora del proyecto:
  - [ ] Fecha de instalacion
  - [ ] Hostname del servidor
  - [ ] Version de Atlas Balance desplegada
  - [ ] Incidencias encontradas y resueltas
  - [ ] Aceptacion final del cliente (firma o email)

## 11. Troubleshooting rapido

| Problema | Causa probable | Solucion |
|----------|---|---|
| "Certificate validation failed" en navegador | Certificado autofirmado o no emitido para el hostname | Reemitir certificado con hostname correcto desde CA |
| 500 error en `/api/health` | Conexion a PostgreSQL falla | Verificar `ConnectionStrings.DefaultConnection` y que PostgreSQL esta corriendo |
| Password del admin perdida | Se cambio en el primer login y no se guardo | Ejecutar `scripts/Reset-AdminPassword.ps1` en el servidor |
| La app no arranca: "must be configured" | Secreto vacio o placeholder en `appsettings.json` | Configurar JwtSettings:Secret, SeedAdmin:Password, SharedSecret o AllowedHosts reales (comportamiento esperado, ver seccion 2) |
| Windows Service no inicia | Archivo `.exe` falta o path incorrecto en `sc create` | Verificar que `C:\AtlasBalance\api\AtlasBalance.API.exe` existe y acceso OK |
| Backup no se guarda | Carpeta `C:\AtlasBalance\backups` no existe o sin permisos | Crear carpeta y dar permisos RWX al Windows Service |
| Clave JWT vacia | `JwtSettings.Secret` no configurada en `appsettings.json` | Generar clave aleatoria (32 chars) y reemplazar valor vacio |
