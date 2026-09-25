param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$ServerName = $env:COMPUTERNAME,
    [int]$ApiPort = 443,
    [switch]$UseReverseProxy,
    [string]$PublicHost = "",
    [int]$PublicPort = 443,
    [int]$InternalApiPort = 5000,
    [string]$ReverseProxyIp = "127.0.0.1",
    [int]$WatchdogPort = 5001,
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432,
    [string]$DbName = "atlas_balance",
    [string]$DbOwnerUser = "atlas_balance_owner",
    [string]$DbOwnerPassword = "",
    [string]$DbUser = "atlas_balance_app",
    [string]$DbPassword = "",
    [string]$PostgresAdminUser = "postgres",
    [string]$PostgresAdminPassword = "",
    [string]$PostgresBinPath = "",
    [string]$PostgresPackageId = "PostgreSQL.PostgreSQL.16",
    [string]$PostgresServiceName = "AtlasBalance.PostgreSQL",
    [string]$PostgresInstallPath = "",
    [string]$PostgresDataPath = "",
    [string]$AdminEmail = "admin@atlasbalance.local",
    [string]$AdminPassword = "",
    [string]$ApiServiceAccount = "AtlasBalanceApiSvc",
    [string]$WatchdogServiceAccount = "AtlasBalanceWatchdogSvc",
    [switch]$SkipDatabaseSetup,
    [switch]$InstallDependencies,
    [switch]$AllowInternet
)

$ErrorActionPreference = "Stop"
$AppVersion = "V-03.01"
$ApiServiceName = "AtlasBalance.API"
$WatchdogServiceName = "AtlasBalance.Watchdog"
$ApiServiceAccountPrincipal = ""
$WatchdogServiceAccountPrincipal = ""
$ManagedPostgres = $false
$GeneratedPostgresAdminPassword = ""
$ExistingUsersDetected = $false
$ExistingConfigurationDetected = $false

# V-02.08: dot-source de la copia atomica con rollback. Mantener
# Sync-DirectoryPreserveConfig en un archivo separado permite
# cubrirla con tests unitarios sin tener que ejecutar el instalador
# entero. Si la funcion no se encuentra, abortamos aqui en lugar de
# mas adelante con un mensaje menos claro.
$syncModule = Join-Path $PSScriptRoot "Sync-AtlasDirectory.ps1"
if (Test-Path -LiteralPath $syncModule) {
    . $syncModule
}
$serviceSecurityModule = Join-Path $PSScriptRoot "ServiceSecurity.ps1"
if (-not (Test-Path -LiteralPath $serviceSecurityModule)) {
    throw "No se encontro ServiceSecurity.ps1; se aborta para no registrar una instalacion sin el runner seguro de actualizaciones."
}
. $serviceSecurityModule

$DefaultReleaseSigningPublicKeyPem = @"
-----BEGIN PUBLIC KEY-----
MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAxhpPLjgCCcX1jyi/BGyE
SMVCmgibdtH6VRap66R24sABZUzS0uF4sSGfii/9gH76MbyJBx82RVy9E5Ffg9IP
hTLlXhCICf2Jgq7x1X1hrxvSX1xHIF0L7FKO1sOTYVDjWCTywZ4Inb4QZaScIh2d
HvOZpJ7UqFrYwyMRlj6haoTqOrVeXWLWeInfacq+ujBJsylk0T+3J6L0J3sST8nh
ofI8PU1hN5Dsj6CpRP1KROAYlAmRmpnIAkIhLtNTMBrlabm82/+rTCOJJksr81tU
IRdrZuyOvfrDwsm+oQPz8d4PcvAQFKvTscHI8MFE7pvza9+vG2UNlcMxSaR4SgL1
lySo8DDCPU9UKU4RqGZRyuB6RF0Ne0EP4oUd0zi2wIUj4FNfntrrw8o57t7vWRGA
3f3CpQB0kOyeImDmniEkrdSB4LAPYTup+vkp2Dlzsh0vn+wLirHfCV1zRDtANfOU
+F5bfSXIFdLGtCtDVFhsNYE/tEdXSB+JXtztxkpqKTouTo4drnG0OZmiLxBq1pP6
12hXvWWiQstA29R2oHidxZAdw7r75Xqu0jwQKtpORTjXvsDERmaE7HRDKxUXD0CC
8SZKYqXRVrPhG6BPkonf3f7BMUNx14LG6GXWE6uTNCvLqsNinFa7iQfrMeN3qTV/
UWsagyNIy7PBlmnPJBzNv68CAwEAAQ==
-----END PUBLIC KEY-----
"@
$ReleaseSigningPublicKeyPem = if ([string]::IsNullOrWhiteSpace($env:ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM)) { $DefaultReleaseSigningPublicKeyPem } else { $env:ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM -replace "\\n", "`n" }

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Convert-SecureStringToPlain {
    param([Security.SecureString]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

function New-RandomSecret {
    param([int]$Length = 48)

    $alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!#%_-"
    $bytes = New-Object byte[] $Length
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }

    $chars = New-Object char[] $Length
    for ($i = 0; $i -lt $Length; $i++) {
        $chars[$i] = $alphabet[$bytes[$i] % $alphabet.Length]
    }
    return -join $chars
}

function Test-HostValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return $Value -notmatch '[:/\\\s*]'
}

function Test-IpValue {
    param([string]$Value)

    $address = $null
    return [Net.IPAddress]::TryParse($Value, [ref]$address)
}

function Protect-RestrictedDirectory {
    param([string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [System.Security.AccessControl.PropagationFlags]::None
    foreach ($sid in @("S-1-5-32-544", "S-1-5-18")) {
        $identity = New-Object System.Security.Principal.SecurityIdentifier($sid)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "FullControl", $inheritance, $propagation, "Allow")
        $acl.AddAccessRule($rule)
    }

    Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop
}

# V-02.07: carpeta del log de eventos de seguridad, con retencion propia y mas
# larga que el log de aplicacion.
#
# Que consigue esta ACL y que NO consigue, sin adornos:
#
#   SI: quita el acceso a usuarios normales del servidor. Solo Administradores y
#       SYSTEM pueden leer o tocar el historico de eventos de seguridad. Sin
#       esto, el log hereda los permisos de %ProgramData%, donde BUILTIN\Usuarios
#       tiene lectura, y cualquiera con sesion en la maquina puede leer quien
#       entra, desde donde y a que hora.
#
#   NO: no protege frente a quien ejecute codigo como SYSTEM. La cuenta SYSTEM
#       sigue siendo la identidad administrativa del sistema operativo, pero ni
#       API ni Watchdog se registran con ella.
#
# La defensa real contra ese escenario es sacar los logs de la maquina: reenvio
# del Event Log a un colector, envio a un syslog, o el webhook de Slack para las
# alertas. Esta documentado en DOCUMENTACION_TECNICA.md. La otra mitad del
# problema (la tabla AUDITORIAS) si esta cubierta de verdad, porque el rol de la
# aplicacion no es el propietario y tiene UPDATE/DELETE revocados.
function Protect-SecurityLogDirectory {
    param([string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null

    & icacls.exe $Path /inheritance:r `
        /grant:r "*S-1-5-32-544:(OI)(CI)F" `
        "*S-1-5-18:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "No se pudo restringir la ACL de $Path. El log de seguridad queda legible por usuarios del servidor."
        return
    }

    Write-Host "  Log de seguridad en $Path (solo Administradores y SYSTEM)."
}

# V-02.07: origen del Windows Event Log para el espejo de eventos de seguridad.
# Requiere admin y por eso se hace en la instalacion, no en runtime: el servicio
# no deberia tener privilegios para registrar origenes.
function Register-SecurityEventLogSource {
    param([string]$SourceName = "AtlasBalance")

    try {
        if ([System.Diagnostics.EventLog]::SourceExists($SourceName)) {
            Write-Host "  Origen de Event Log '$SourceName' ya registrado."
            return
        }

        New-EventLog -LogName "Application" -Source $SourceName -ErrorAction Stop
        Write-Host "  Origen de Event Log '$SourceName' registrado en el log Application."
    } catch {
        # No es fatal: sin el origen, el espejo se queda solo en fichero.
        Write-Warning "No se pudo registrar el origen de Event Log '$SourceName': $($_.Exception.Message). El espejo de eventos de seguridad quedara solo en fichero."
    }
}

# V-02-07: la BD, los backups locales y las exportaciones .xlsx guardan datos
# personales sin cifrado a nivel de columna. La unica defensa frente a robo del
# disco o de una copia del volumen es el cifrado en reposo del propio volumen.
# Aqui NO se activa BitLocker: encenderlo es un cambio de seguridad del sistema
# que debe decidir y ejecutar un administrador (y puede requerir TPM, reinicio y
# custodia de la clave de recuperacion). Esto solo comprueba y avisa.
function Test-VolumeEncryption {
    param([string[]]$Paths)

    $checked = @{}
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $drive = try { (Split-Path -Qualifier $path) } catch { $null }
        if ([string]::IsNullOrWhiteSpace($drive) -or $checked.ContainsKey($drive)) { continue }
        $checked[$drive] = $true

        # V-02.08: distinguir "BitLocker no esta instalado" de "el volumen esta
        # sin cifrar". Get-BitLockerVolume no existe en algunas ediciones
        # de Windows Server (sin BitLocker) y el catch original emitia
        # exactamente la misma advertencia para los dos casos, lo que
        # ocultaba el verdadero problema al operador.
        if ($null -eq (Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue)) {
            # V-02.08 (fix): el terminador del here-string estaba corrupto
            # (linea literal "-ForegroundColor Yellow), lo que convertia este
            # bloque y todo el try/catch de Get-BitLockerVolume en parte del
            # string: el check de cifrado era un no-op silencioso.
            Write-Warning @"
Esta edicion de Windows no expone Get-BitLockerVolume. No se puede
verificar el cifrado del volumen $drive desde este instalador.
"@
            Write-Host "    Verifica manualmente con: manage-bde -status $drive  o  fsutil behavior query disableencryption" -ForegroundColor Cyan
            continue
        }

        $status = $null
        try {
            $status = Get-BitLockerVolume -MountPoint $drive -ErrorAction Stop
        } catch {
            Write-Warning "No se pudo comprobar el cifrado del volumen $drive ($($_.Exception.Message)). Verificalo a mano."
            continue
        }

        if ($status.ProtectionStatus -eq 'On') {
            Write-Host "  [OK] Volumen $drive cifrado con BitLocker." -ForegroundColor Green
        } else {
            Write-Warning @"
Volumen $drive SIN cifrado en reposo (ProtectionStatus=$($status.ProtectionStatus)).
Ahi viven datos personales: base de datos, backups locales y exportaciones.
Sin cifrado de volumen, quien se lleve el disco o una copia del volumen los lee enteros.
Activalo de forma consciente con: Enable-BitLocker -MountPoint $drive  (guarda la clave de recuperacion).
"@
        }
    }
}

function Test-AtlasPreflight {
    # V-02.08: preflight de entorno antes de instalar. El incidente V-02.07
    # mostro que una primera instalacion se queda a medias sin rollback
    # cuando el entorno no cumple condiciones basicas. Esta funcion es
    # critica: PREFERIMOS abortar antes de escribir a que el operador se
    # encuentre con un sistema a medio instalar.
    param(
        [string]$InstallPath,
        [int]$ApiPort,
        [int]$InternalApiPort,
        [int]$WatchdogPort,
        [int]$DbPort,
        [int]$PublicPort,
        # V-02.08 (revision PR #33): solo cuando el instalador va a montar una
        # instancia PostgreSQL gestionada nueva exigimos que DbPort este libre.
        # Si se usa PostgreSQL externo/preexistente, postgres.exe YA esta
        # escuchando en DbPort a proposito y eso no es un conflicto.
        [bool]$WillInstallManagedDb = $false
    )

    $errores = @()

    # 1. Carpeta de instalacion escribible y con espacio.
    if (-not (Test-Path -LiteralPath $InstallPath)) {
        try {
            New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
        } catch {
            $mensaje = $_.Exception.Message
            $errores += ("No se puede crear la carpeta de instalacion {0}: {1}" -f $InstallPath, $mensaje)
        }
    }
    $testFile = Join-Path $InstallPath "atlas-preflight-$(Get-Random).tmp"
    try {
        Set-Content -LiteralPath $testFile -Value "ok" -ErrorAction Stop
        Remove-Item -LiteralPath $testFile -Force -ErrorAction SilentlyContinue
    } catch {
        $mensaje = $_.Exception.Message
        $errores += ("La carpeta {0} no es escribible por el usuario actual: {1}" -f $InstallPath, $mensaje)
    }

    # 2. Espacio libre >= 2 GB en el volumen de la instalacion.
    $drive = try { (Split-Path -Qualifier $InstallPath) } catch { $null }
    if ($drive) {
        try {
            $unidad = New-Object System.IO.DriveInfo($drive)
            $libresGb = [Math]::Round($unidad.AvailableFreeSpace / 1GB, 1)
            if ($libresGb -lt 2) {
                $errores += "Espacio libre insuficiente en $drive ($libresGb GB). Atlas Balance requiere >= 2 GB para PostgreSQL, binarios, backups y logs."
            }
        } catch {
            $mensaje = $_.Exception.Message
            $errores += ("No se pudo leer el espacio libre en {0}: {1}" -f $drive, $mensaje)
        }
    }

    # 3. Puertos no ocupados por otra aplicacion.
    $puertosAplicacion = @($ApiPort, $InternalApiPort, $WatchdogPort, $PublicPort) | Where-Object { $_ -gt 0 } | Select-Object -Unique
    foreach ($puerto in $puertosAplicacion) {
        $escuchador = Get-NetTCPConnection -LocalPort $puerto -State Listen -ErrorAction SilentlyContinue
        if ($escuchador) {
            $owningPid = ($escuchador | Select-Object -First 1).OwningProcess
            $owningProcess = Get-Process -Id $owningPid -ErrorAction SilentlyContinue
            $processName = if ($owningProcess) { $owningProcess.ProcessName } else { "?" }
            if ($processName -like "AtlasBalance.*") {
                Write-Host "  [OK] Puerto $puerto en uso por $processName (servicio actual de Atlas Balance)." -ForegroundColor DarkGray
            }
            else {
                $errores += "Puerto $puerto ocupado por $processName (PID $owningPid). Librelo o cambia el puerto."
            }
        }
    }

    # 3b. Puerto de BD: solo se exige libre si vamos a instalar una instancia
    #     PostgreSQL gestionada nueva. Con PostgreSQL externo/preexistente
    #     postgres.exe escucha ahi a proposito (ese es el camino soportado).
    if ($DbPort -gt 0) {
        $escuchadorDb = Get-NetTCPConnection -LocalPort $DbPort -State Listen -ErrorAction SilentlyContinue
        if ($escuchadorDb) {
            $owningPid = ($escuchadorDb | Select-Object -First 1).OwningProcess
            $owningProcess = Get-Process -Id $owningPid -ErrorAction SilentlyContinue
            $processName = if ($owningProcess) { $owningProcess.ProcessName } else { "?" }
            if ($processName -like "AtlasBalance.*" -or $processName -like "postgres*") {
                Write-Host "  [OK] Puerto $DbPort (BD) en uso por $processName." -ForegroundColor DarkGray
            }
            elseif ($WillInstallManagedDb) {
                $errores += "Puerto $DbPort ocupado por $processName (PID $owningPid). Librelo o cambia -DbPort antes de instalar la instancia PostgreSQL gestionada."
            }
            else {
                Write-Host "  [AVISO] Puerto $DbPort en uso por $processName (PID $owningPid); se asume la instancia PostgreSQL externa configurada con -DbHost/-DbPort." -ForegroundColor Yellow
            }
        }
        elseif ($WillInstallManagedDb) {
            Write-Host "  [OK] Puerto $DbPort libre para la instancia PostgreSQL gestionada." -ForegroundColor DarkGray
        }
    }

    # 4. Binarios del paquete no bloqueados. Si la API esta corriendo,
    #    el instalador la parara; probar primero que no hay un proceso
    #    externo bloqueando.
    $apiExe = Join-Path $InstallPath "api\AtlasBalance.API.exe"
    if (Test-Path -LiteralPath $apiExe) {
        try {
            $stream = [System.IO.File]::Open($apiExe, "Open", "Read", "None")
            $stream.Close()
        } catch {
            $mensaje = $_.Exception.Message
            $errores += ("El binario {0} esta bloqueado por otro proceso: {1}. Para los servicios AtlasBalance.API y AtlasBalance.Watchdog antes de continuar." -f $apiExe, $mensaje)
        }
    }

    # 5. Si ya existe una instalacion, los servicios deben existir o
    #    poder recrearse. Comprobar que no hay archivos huerfanos.
    if (Test-Path -LiteralPath (Join-Path $InstallPath "scripts")) {
        $huerfanos = Get-ChildItem -LiteralPath (Join-Path $InstallPath "scripts") -Filter "*.tmp" -File -ErrorAction SilentlyContinue
        if ($huerfanos) {
            Write-Host "  [AVISO] Hay $($huerfanos.Count) ficheros .tmp en scripts/. Probablemente de una instalacion interrumpida." -ForegroundColor Yellow
        }
    }

    if ($errores.Count -gt 0) {
        Write-Host ""
        Write-Host "Preflight FAIL: $($errores.Count) condicion(es) del entorno no se cumplen." -ForegroundColor Red
        foreach ($error in $errores) {
            Write-Host "  - $error" -ForegroundColor Red
        }
        throw "El preflight fallo. Resuelve las condiciones anteriores y ejecuta el instalador de nuevo."
    }

    Write-Host "Preflight OK: entorno listo para instalar." -ForegroundColor Green
}

function Protect-SecretFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "No existe el archivo secreto que se quiere proteger: $Path"
    }

    & icacls.exe $Path /inheritance:r /grant:r "*S-1-5-32-544:F" "*S-1-5-18:F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo restringir ACL en $Path."
    }
}

function Write-SecretFile {
    param(
        [string]$Path,
        [string[]]$Lines
    )

    $directory = Split-Path -Parent $Path
    Protect-RestrictedDirectory -Path $directory
    Set-Content -LiteralPath $Path -Value $Lines -Encoding UTF8
    & icacls.exe $Path /inheritance:r /grant:r "*S-1-5-32-544:F" "*S-1-5-18:F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        throw "No se pudo restringir ACL en $Path. El archivo de credenciales se elimino."
    }
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

function Quote-PgIdentifier {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 63) {
        throw "Identificador PostgreSQL invalido."
    }

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Identificador PostgreSQL invalido: $Value"
    }

    return '"' + $Value.Replace('"', '""') + '"'
}

function Find-PostgresBin {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath) -and
        (Test-Path (Join-Path $PreferredPath "psql.exe"))) {
        return (Resolve-Path $PreferredPath).Path
    }

    $psqlCommand = Get-Command "psql.exe" -ErrorAction SilentlyContinue
    if ($psqlCommand) {
        return Split-Path -Parent $psqlCommand.Source
    }

    $candidates = @(
        "C:\Program Files\PostgreSQL\18\bin",
        "C:\Program Files\PostgreSQL\17\bin",
        "C:\Program Files\PostgreSQL\16\bin",
        "C:\Program Files\PostgreSQL\15\bin",
        "C:\Program Files\PostgreSQL\16\bin"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate "psql.exe")) {
            return $candidate
        }
    }

    return ""
}

function Test-TcpPortAvailable {
    param([int]$Port)

    $listener = $null
    try {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        return $true
    } catch {
        return $false
    } finally {
        if ($listener) {
            $listener.Stop()
        }
    }
}

function Write-PostgresManualInstallHint {
    Write-Host ""
    Write-Host "PostgreSQL no quedo preparado automaticamente." -ForegroundColor Yellow
    Write-Host "En Windows Server 2019 instala PostgreSQL 16+ manualmente. PostgreSQL 17 es valido." -ForegroundColor Yellow
    Write-Host "Despues relanza el instalador indicando, por ejemplo:" -ForegroundColor Yellow
    Write-Host '.\install.cmd -InstallPath C:\AtlasBalance -ServerName NOMBRE_SERVIDOR -ApiPort 443 -PostgresAdminPassword <password> -PostgresBinPath "C:\Program Files\PostgreSQL\17\bin"' -ForegroundColor Cyan
    Write-Host 'Para dominio publico detras de proxy: .\install.cmd -InstallPath C:\AtlasBalance -UseReverseProxy -PublicHost balance.ejemplo.com -InternalApiPort 5000 -PostgresAdminPassword <password> -PostgresBinPath "C:\Program Files\PostgreSQL\17\bin"' -ForegroundColor Cyan
    Write-Host "No pegues passwords reales en chats, tickets ni documentacion." -ForegroundColor Yellow
    Write-Host ""
}

function Resolve-PostgresPort {
    param([int]$PreferredPort)

    if (Test-TcpPortAvailable -Port $PreferredPort) {
        return $PreferredPort
    }

    for ($port = 55432; $port -le 55499; $port++) {
        if (Test-TcpPortAvailable -Port $port) {
            Write-Host "Puerto PostgreSQL $PreferredPort ocupado; se usara $port para la instancia gestionada." -ForegroundColor Yellow
            return $port
        }
    }

    throw "No se encontro un puerto local libre para PostgreSQL."
}

function Try-InstallPostgres {
    param(
        [string]$PackageId,
        [int]$Port,
        [string]$SuperPassword,
        [string]$PrefixPath,
        [string]$DataPath,
        [string]$ServiceName
    )

    if (-not (Get-Command "winget.exe" -ErrorAction SilentlyContinue)) {
        return $false
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $PrefixPath) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path -Parent $DataPath) -Force | Out-Null

    $override = @(
        "--mode unattended",
        "--unattendedmodeui none",
        "--superpassword `"$SuperPassword`"",
        "--serverport $Port",
        "--servicename `"$ServiceName`"",
        "--prefix `"$PrefixPath`"",
        "--datadir `"$DataPath`"",
        "--enable-components server,commandlinetools",
        "--disable-components pgAdmin,stackbuilder",
        "--install_runtimes 1"
    ) -join " "

    Write-Host "Instalando PostgreSQL gestionado con winget: $PackageId" -ForegroundColor Yellow
    try {
        & winget.exe install --id $PackageId -e --accept-source-agreements --accept-package-agreements --silent --override $override
    } catch {
        Write-Host "winget fallo al instalar PostgreSQL: $($_.Exception.Message)" -ForegroundColor Yellow
        return $false
    }
    if ($LASTEXITCODE -eq 0) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($service -and $service.Status -ne "Running") {
            Start-Service -Name $ServiceName
            $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(60))
        }
        $script:PostgresAdminPassword = $SuperPassword
        $script:ManagedPostgres = $true
        $script:GeneratedPostgresAdminPassword = $SuperPassword
        return $true
    }

    Write-Host "winget termino con codigo $LASTEXITCODE al instalar PostgreSQL." -ForegroundColor Yellow
    return $false
}

# V-02.07 (DB-EXPOSURE): el instalador EDB de winget no fija listen_addresses y
# suele dejarlo en '*' (todas las interfaces), asi que el puerto de PostgreSQL
# queda accesible desde la LAN sin regla de firewall que lo cubra. Como esta
# instancia es local y la gestiona este instalador, la restringimos a
# localhost. Idempotente: si ya esta en 'localhost' no reescribe ni reinicia.
function Set-PostgresListenLocalhost {
    param(
        [string]$DataPath,
        [string]$ServiceName
    )

    $confPath = Join-Path $DataPath "postgresql.conf"
    if (-not (Test-Path -LiteralPath $confPath -PathType Leaf)) {
        Write-Warning "No se encontro postgresql.conf en '$confPath'; no se pudo restringir listen_addresses a localhost. Revisalo a mano."
        return
    }

    # Solo cuentan las lineas ACTIVAS. postgresql.conf se distribuye con
    # "#listen_addresses = 'localhost'" comentado, y el instalador EDB anade
    # aparte su propia linea activa. Si mirasemos tambien las comentadas,
    # dariamos por bueno un fichero que en realidad esta escuchando en '*'.
    $activePattern = "^\s*listen_addresses\s*="
    $lines = @(Get-Content -LiteralPath $confPath)
    $activeLines = @($lines | Where-Object { $_ -match $activePattern })
    if ($activeLines.Count -gt 0 -and
        -not ($activeLines | Where-Object { $_ -notmatch "listen_addresses\s*=\s*'localhost'" })) {
        return
    }

    $newLine = "listen_addresses = 'localhost'"
    $replaced = $false
    $newLines = foreach ($line in $lines) {
        if ($line -match $activePattern) {
            # Solo se conserva la primera: si quedasen varias activas, la
            # ultima ganaria y dejaria en vigor el valor que veniamos a quitar.
            if (-not $replaced) {
                $replaced = $true
                $newLine
            }
        } else {
            $line
        }
    }
    if (-not $replaced) {
        $newLines += $newLine
    }
    # UTF8 sin BOM a proposito: Set-Content -Encoding UTF8 en Windows
    # PowerShell 5.1 escribe BOM, y un BOM al inicio de postgresql.conf rompe
    # el parser de PostgreSQL y deja el servicio sin arrancar tras el reinicio.
    [System.IO.File]::WriteAllLines($confPath, [string[]]$newLines, (New-Object System.Text.UTF8Encoding($false)))

    Write-Host "PostgreSQL local: listen_addresses fijado a 'localhost' en $confPath." -ForegroundColor Green
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Restart-Service -Name $ServiceName
        $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(60))
        Write-Host "Servicio $ServiceName reiniciado para aplicar listen_addresses." -ForegroundColor Green
    } else {
        Write-Warning "No se encontro el servicio '$ServiceName' para reiniciarlo; el cambio de listen_addresses no tomara efecto hasta el proximo reinicio de PostgreSQL."
    }
}

function Invoke-Psql {
    param(
        [string]$PsqlExe,
        [string]$Sql,
        [string]$Database = "postgres",
        [switch]$Scalar
    )

    $previousPassword = $env:PGPASSWORD
    if (-not [string]::IsNullOrWhiteSpace($PostgresAdminPassword)) {
        $env:PGPASSWORD = $PostgresAdminPassword
    }

    try {
        $args = @(
            "-h", $DbHost,
            "-p", [string]$DbPort,
            "-U", $PostgresAdminUser,
            "-d", $Database,
            "-v", "ON_ERROR_STOP=1"
        )

        if ($Scalar) {
            $args += @("-t", "-A")
        } else {
            $args += @("-q")
        }

        # V-02.08 (fix): 2>&1 sobre un nativo bajo EAP=Stop convierte cada
        # NOTICE/stderr de psql en ErrorRecord terminating y daba "psql fallo"
        # falso. Se baja EAP solo para la llamada.
        $previousEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $output = $Sql | & $PsqlExe @args 2>&1
        } finally {
            $ErrorActionPreference = $previousEap
        }
        if ($LASTEXITCODE -ne 0) {
            throw "psql fallo: $output"
        }

        if ($Scalar) {
            return (($output | Out-String).Trim())
        }
        return $output
    } finally {
        $env:PGPASSWORD = $previousPassword
    }
}

function Test-PostgresPreflight {
    param([string]$PostgresBin)

    $psql = Join-Path $PostgresBin "psql.exe"
    if (-not (Test-Path $psql)) {
        throw "No se encontro psql.exe en $PostgresBin."
    }

    Write-Host "PostgreSQL preflight: comprobando version, extensiones y el rol..." -ForegroundColor Cyan

    # 1. Version >= 16. Atlas Balance depende de caracteristicas de PG 16
    #    (FORCE RLS, MERGE, etc.). Un PG 13 impediria migrar.
    $serverVersion = Invoke-Psql -PsqlExe $psql -Scalar -Sql "SHOW server_version_num;"
    $versionNum = 0
    if (-not [int]::TryParse($serverVersion, [ref]$versionNum)) {
        throw "No se pudo leer server_version_num de PostgreSQL (PSQL devolvio '$serverVersion')."
    }
    if ($versionNum -lt 160000) {
        throw "PostgreSQL $versionNum detectado. Atlas Balance requiere PostgreSQL >= 16.000. Actualiza la instancia o instala una version soportada."
    }
    Write-Host "  [OK] PostgreSQL $($versionNum) >= 160000." -ForegroundColor Green

    # 2. Extension pgcrypto. V-02.07 lo necesita para Reset-AdminPassword
    #    (bcrypt con pgcrypto) y para la huella del importador.
    # V-02.08 (revision PR #33): este preflight corre ANTES de Ensure-Database,
    # asi que en fresh-install $DbName todavia no existe (psql -d $DbName
    # fallaria con "database does not exist"). pgcrypto tampoco se habilita
    # aqui: la migracion inicial la crea con CREATE EXTENSION IF NOT EXISTS al
    # arrancar la API. Lo que este preflight puede y debe comprobar ahora es
    # que el servidor la tiene DISPONIBLE, consultando contra "postgres"
    # (que siempre existe) en vez de contra $DbName.
    $pgcryptoDisponible = Invoke-Psql -PsqlExe $psql -Database "postgres" -Scalar -Sql "SELECT extname FROM pg_available_extensions WHERE extname = 'pgcrypto';"
    if ($pgcryptoDisponible -ne "pgcrypto") {
        throw "Esta instancia de PostgreSQL no tiene disponible la extension pgcrypto (paquete contrib). Instala el paquete que la incluye antes de continuar."
    }
    Write-Host "  [OK] Extension pgcrypto disponible en el servidor PostgreSQL (se habilitara en $DbName al arrancar la API)." -ForegroundColor Green

    # 3. El rol owner debe ser NOSUPERUSER. El instalador ya lo crea con
    #    NOSUPERUSER pero un operador que apunte a una instancia existente
    #    podria tener un owner con superpoderes: defenderse del propio
    #    instalador es absurdo, pero comprobarlo aqui evita que el
    #    incidente V-02.07 (mezcla managed->external) termine con un
    #    superusuario ejecutando backups.
    $ownerRole = Escape-SqlLiteral $DbOwnerUser
    $ownerAttrs = Invoke-Psql -PsqlExe $psql -Database "postgres" -Scalar -Sql "SELECT rolsuper::text || '|' || rolbypassrls::text FROM pg_roles WHERE rolname = '$ownerRole';"
    if ([string]::IsNullOrWhiteSpace($ownerAttrs)) {
        Write-Host "  [AVISO] El rol $DbOwnerUser no existe todavia. Se creara con NOSUPERUSER NOBYPASSRLS por defecto." -ForegroundColor Yellow
    }
    else {
        $parts = $ownerAttrs -split '\|'
        $isSuper = ($parts[0].Trim() -eq "t")
        $isBypassRls = ($parts[1].Trim() -eq "t")
        if ($isSuper) {
            Write-Host "  [AVISO] El rol $DbOwnerUser es SUPERUSER. Atlas Balance lo requiere NOSUPERUSER para que el modelo RLS funcione contra el rol." -ForegroundColor Yellow
        }
        if (-not $isBypassRls) {
            Write-Host "  [AVISO] El rol $DbOwnerUser no tiene BYPASSRLS. Los backups con pg_dump fallaran contra tablas con FORCE ROW LEVEL SECURITY." -ForegroundColor Yellow
        }
    }

    # 4. Smoke-test de RLS firmado. Inserta una fila firmada en AUDITORIAS
    #    con el contexto auth anonimo y revierte. Es la misma traza que
    #    sigue AuditService al loguear LOGIN_MFA_REQUIRED. Si el incidente
    #    V-02.07 se reproduce, falla aqui y el instalador aborta antes
    #    de tocar la API.
    $rlsSecret = ""
    $configPath = Join-Path $InstallPath "api\appsettings.Production.json"
    if (Test-Path -LiteralPath $configPath) {
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $rlsSecret = [string]$config.Security.RlsContextSecret
    }
    if (-not [string]::IsNullOrWhiteSpace($rlsSecret)) {
        $payload = "auth|||false|false|auth"
        $signature = ($payload | & "$env:WINDIR\System32\certutil.exe" -hashHMAC SHA256 -key $rlsSecret -noPrefix 2>$null) | Select-Object -Last 1
        if (-not [string]::IsNullOrWhiteSpace($signature)) {
            # Extrae solo el hex (certutil imprime lineas adicionales).
            $hexSignature = ($signature -replace '[^a-fA-F0-9]', '').ToLower()
            if ($hexSignature.Length -gt 0) {
                # V-02.08 (revision PR #33): la version anterior de esta sonda solo
                # ejecutaba set_config(), que siempre tiene exito sin importar si
                # context_is_valid() acepta la firma o si la policy auditorias_insert
                # realmente permite el INSERT. Ahora valida ambas cosas de verdad:
                # RAISE EXCEPTION si la firma no es valida, e INSERT+ROLLBACK contra
                # AUDITORIAS bajo ON_ERROR_STOP=1 para que un 42501 (permission
                # denied) aborte el instalador igual que antes de tocar la API.
                $smokeSql = @"
BEGIN;
SELECT set_config('atlas.auth_mode', 'auth', false),
       set_config('atlas.user_id', '', false),
       set_config('atlas.integration_token_id', '', false),
       set_config('atlas.is_admin', 'false', false),
       set_config('atlas.system', 'false', false),
       set_config('atlas.request_scope', 'auth', false),
       set_config('atlas.context_signature', '$hexSignature', false);
DO `$`$
BEGIN
    IF NOT atlas_security.context_is_valid() THEN
        RAISE EXCEPTION 'atlas_security.context_is_valid() devolvio false: el secreto RLS no esta alineado o la policy no esta desplegada.';
    END IF;
END
`$`$;
INSERT INTO "AUDITORIAS" (id, tipo_accion, entidad_tipo, origen, "timestamp", detalles_json)
VALUES (gen_random_uuid(), 'INSTALLER_SMOKE', 'SISTEMA', 'INSTALADOR', now(), '{}'::json);
ROLLBACK;
"@
                Invoke-Psql -PsqlExe $psql -Database $DbName -Sql $smokeSql | Out-Null
                Write-Host "  [OK] Contexto RLS firmable: context_is_valid() y el INSERT firmado en AUDITORIAS funcionan." -ForegroundColor Green
            }
        }
    }
    else {
        Write-Host "  [AVISO] Security:RlsContextSecret no estaba en appsettings.Production.json. La firma de contexto RLS se validara al iniciar la API." -ForegroundColor Yellow
    }
}

function Ensure-Database {
    param([string]$PostgresBin)

    $psql = Join-Path $PostgresBin "psql.exe"
    if (-not (Test-Path $psql)) {
        throw "No se encontro psql.exe en $PostgresBin."
    }

    if ([string]::IsNullOrWhiteSpace($PostgresAdminPassword)) {
        throw "PostgresAdminPassword no esta configurada. Usa install.cmd para preparar PostgreSQL automaticamente o pasa -PostgresAdminPassword si usas una instancia existente."
    }

    $ownerRoleName = Escape-SqlLiteral $DbOwnerUser
    $ownerRolePassword = Escape-SqlLiteral $DbOwnerPassword
    $roleName = Escape-SqlLiteral $DbUser
    $rolePassword = Escape-SqlLiteral $DbPassword
    $dbNameLiteral = Escape-SqlLiteral $DbName
    $ownerRoleIdentifier = Quote-PgIdentifier $DbOwnerUser
    $roleIdentifier = Quote-PgIdentifier $DbUser
    $dbIdentifier = Quote-PgIdentifier $DbName

    # V-02.07 (BACKUP-RLS): el owner necesita BYPASSRLS. Las tablas de negocio
    # llevan FORCE ROW LEVEL SECURITY, y pg_dump exige que el rol que lo ejecuta
    # pueda saltarse RLS o el backup falla con error (no sale vacio en silencio).
    $ownerRoleExists = Invoke-Psql -PsqlExe $psql -Scalar -Sql "SELECT 1 FROM pg_roles WHERE rolname = '$ownerRoleName';"
    if ($ownerRoleExists -eq "1") {
        Invoke-Psql -PsqlExe $psql -Sql "ALTER ROLE $ownerRoleIdentifier WITH LOGIN PASSWORD '$ownerRolePassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION BYPASSRLS;" | Out-Null
    } else {
        Invoke-Psql -PsqlExe $psql -Sql "CREATE ROLE $ownerRoleIdentifier WITH LOGIN PASSWORD '$ownerRolePassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION BYPASSRLS;" | Out-Null
    }

    $roleExists = Invoke-Psql -PsqlExe $psql -Scalar -Sql "SELECT 1 FROM pg_roles WHERE rolname = '$roleName';"
    if ($roleExists -eq "1") {
        Invoke-Psql -PsqlExe $psql -Sql "ALTER ROLE $roleIdentifier WITH LOGIN PASSWORD '$rolePassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;" | Out-Null
    } else {
        Invoke-Psql -PsqlExe $psql -Sql "CREATE ROLE $roleIdentifier WITH LOGIN PASSWORD '$rolePassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;" | Out-Null
    }

    $dbExists = Invoke-Psql -PsqlExe $psql -Scalar -Sql "SELECT 1 FROM pg_database WHERE datname = '$dbNameLiteral';"
    if ($dbExists -ne "1") {
        Invoke-Psql -PsqlExe $psql -Sql "CREATE DATABASE $dbIdentifier OWNER $ownerRoleIdentifier ENCODING 'UTF8';" | Out-Null
    } else {
        Invoke-Psql -PsqlExe $psql -Sql "ALTER DATABASE $dbIdentifier OWNER TO $ownerRoleIdentifier;" | Out-Null
    }

    Invoke-Psql -PsqlExe $psql -Database $DbName -Sql "ALTER SCHEMA public OWNER TO $ownerRoleIdentifier; GRANT CONNECT ON DATABASE $dbIdentifier TO $roleIdentifier; GRANT USAGE ON SCHEMA public TO $roleIdentifier;" | Out-Null
}

function Test-ExistingApplicationUsers {
    param([string]$PostgresBin)

    $psql = Join-Path $PostgresBin "psql.exe"
    $sql = "SELECT CASE WHEN to_regclass('`"USUARIOS`"') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM `"USUARIOS`" WHERE deleted_at IS NULL) END;"
    $count = Invoke-Psql -PsqlExe $psql -Database $DbName -Scalar -Sql $sql
    $parsed = 0
    if ([int]::TryParse($count, [ref]$parsed)) {
        return ($parsed -gt 0)
    }
    return $false
}

function New-AtlasCertificate {
    param(
        [string]$CertDirectory,
        [string]$DnsName,
        [string]$Password
    )

    New-Item -ItemType Directory -Path $CertDirectory -Force | Out-Null
    $pfxPath = Join-Path $CertDirectory "atlas-balance.pfx"
    $cerPath = Join-Path $CertDirectory "atlas-balance.cer"
    if (Test-Path $pfxPath) {
        Remove-Item -LiteralPath $pfxPath -Force
    }
    if (Test-Path $cerPath) {
        Remove-Item -LiteralPath $cerPath -Force
    }

    $dnsNames = @($DnsName, "localhost", $env:COMPUTERNAME) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
    # V-02-05 (CONFIG-006): avisar al operador que el cert es self-signed.
    # Para produccion, distribuir el .cer y NO marcar "confiar en todos"
    # en los navegadores. Si tienen CA interna, pasar -CertPath y -CertPassword.
    Write-Warning "CONFIG-006: generando certificado self-signed. Para produccion real, use su CA interna (parametros -CertPath / -CertPassword)."
    $cert = New-SelfSignedCertificate `
        -DnsName $dnsNames `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -FriendlyName "Atlas Balance HTTPS" `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(5)

    # V-02.08: post-check de SAN. El incidente V-02.07 mostro que un cert
    # emitido con un DNSName mal escrito deja la web inalcanzable desde
    # el alias del operador. Verificar que la SAN devuelta por Windows
    # contiene el $DnsName solicitado. Si falta, avisar antes de cerrar
    # la instalacion.
    $san = $cert.DnsNameList | ForEach-Object { $_.Unicode }
    $sanFaltantes = $dnsNames | Where-Object { $san -notcontains $_ }
    if ($sanFaltantes) {
        Write-Warning @"
SAN del certificado emitido NO contiene uno o varios DNS solicitados:
  - Solicitados: $($dnsNames -join ', ')
  - Emitidos:    $($san -join ', ')
  - Faltantes:   $($sanFaltantes -join ', ')
El navegador del operador lanzara advertencia de cert invalido al
acceder por esos alias. Vuelve a emitir el cert con todos los DNS
incluidos o distribuye el .cer a los puestos cliente.
"@
    }

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    return @{ Path = $pfxPath; Password = $Password; PublicCer = $cerPath }
}

function Write-JsonFile {
    param([object]$Value, [string]$Path)

    $json = $Value | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

function Test-ServiceAccountName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$') {
        throw "El nombre de cuenta de servicio '$Name' no es valido. Usa un usuario local simple de hasta 20 caracteres."
    }
}

function Initialize-AtlasServiceAccount {
    param([string]$Name, [string]$Description)

    Test-ServiceAccountName -Name $Name
    $existing = Get-LocalUser -Name $Name -ErrorAction SilentlyContinue
    $password = New-RandomSecret -Length 48
    $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
    $password = $null

    if ($existing) {
        if (-not $existing.Enabled) {
            Enable-LocalUser -Name $Name -ErrorAction Stop
        }
        Set-LocalUser -Name $Name -Password $securePassword -PasswordNeverExpires $true -UserMayChangePassword $false -ErrorAction Stop
    }
    else {
        New-LocalUser -Name $Name `
            -Password $securePassword `
            -PasswordNeverExpires `
            -UserMayNotChangePassword `
            -AccountNeverExpires `
            -Description $Description `
            -ErrorAction Stop | Out-Null
    }

    $user = Get-LocalUser -Name $Name -ErrorAction Stop
    $privilegedGroupSids = @(
        "S-1-5-32-544", "S-1-5-32-548", "S-1-5-32-549",
        "S-1-5-32-550", "S-1-5-32-551", "S-1-5-32-547"
    )
    foreach ($groupSid in $privilegedGroupSids) {
        $members = @(Get-LocalGroupMember -SID $groupSid -ErrorAction SilentlyContinue)
        if ($members | Where-Object { $_.SID.Value -eq $user.SID.Value -or $_.Name -match "\\$([regex]::Escape($Name))$" }) {
            throw "La cuenta de servicio '$Name' pertenece a un grupo local privilegiado ($groupSid). Retirala antes de continuar."
        }
    }
    $principal = ".\$Name"
    [pscustomobject]@{
        Name = $Name
        Principal = $principal
        ComputerPrincipal = "$env:COMPUTERNAME\$Name"
        Sid = $user.SID.Value
        Credential = [pscredential]::new($principal, $securePassword)
    }
}

function Get-ServiceLogonRightSids {
    param([string]$PolicyPath)

    $content = Get-Content -LiteralPath $PolicyPath -Raw -ErrorAction Stop
    $match = [regex]::Match($content, '(?im)^\s*SeServiceLogonRight\s*=\s*(?<values>[^\r\n]*)')
    if (-not $match.Success) {
        return @()
    }

    return @($match.Groups['values'].Value -split ',' | ForEach-Object {
        $_.Trim().TrimStart('*')
    } | Where-Object { $_ -match '^S-1-' })
}

function Grant-LogOnAsService {
    param([string[]]$Sids, [string]$InstallPath)

    $securityDirectory = Join-Path $InstallPath "config\ServiceSecurity"
    New-Item -ItemType Directory -Path $securityDirectory -Force | Out-Null
    Protect-RestrictedDirectory -Path $securityDirectory
    $policyPath = Join-Path $securityDirectory "export.inf"
    $verifyPath = Join-Path $securityDirectory "verify.inf"
    $databasePath = Join-Path $securityDirectory "service-rights.sdb"

    try {
        & secedit.exe /export /cfg $policyPath /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "secedit /export devolvio $LASTEXITCODE." }

        $content = Get-Content -LiteralPath $policyPath -Raw -ErrorAction Stop
        $currentSids = @(Get-ServiceLogonRightSids -PolicyPath $policyPath)
        $deniedMatch = [regex]::Match($content, '(?im)^\s*SeDenyServiceLogonRight\s*=\s*(?<values>[^\r\n]*)')
        $deniedSids = if ($deniedMatch.Success) {
            @($deniedMatch.Groups['values'].Value -split ',' | ForEach-Object { $_.Trim().TrimStart('*') } | Where-Object { $_ -match '^S-' })
        } else { @() }
        $blockedSids = @($Sids | Where-Object { $deniedSids -contains $_.ToUpperInvariant() })
        if ($blockedSids.Count -gt 0) { throw "La politica SeDenyServiceLogonRight bloquea la cuenta de servicio: $($blockedSids -join ', ')." }
        $desiredSids = @($currentSids + $Sids | ForEach-Object { $_.ToUpperInvariant() } | Sort-Object -Unique)
        $line = "SeServiceLogonRight = " + (($desiredSids | ForEach-Object { "*$_" }) -join ",")
        $match = [regex]::Match($content, '(?im)^\s*SeServiceLogonRight\s*=\s*[^\r\n]*')
        if ($match.Success) {
            $content = $content.Remove($match.Index, $match.Length).Insert($match.Index, $line)
        }
        else {
            if ($content -notmatch '(?im)^\[Privilege Rights\]') {
                $content += "`r`n[Privilege Rights]`r`n"
            }
            $content += "$line`r`n"
        }
        Set-Content -LiteralPath $policyPath -Value $content -Encoding Unicode

        & secedit.exe /configure /db $databasePath /cfg $policyPath /areas USER_RIGHTS /quiet | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "secedit /configure devolvio $LASTEXITCODE." }
        & secedit.exe /export /cfg $verifyPath /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "No se pudo verificar SeServiceLogonRight." }

        $verifiedSids = @(Get-ServiceLogonRightSids -PolicyPath $verifyPath)
        foreach ($sid in $Sids) {
            if ($verifiedSids -notcontains $sid.ToUpperInvariant()) {
                throw "La cuenta con SID $sid no tiene el derecho 'Log on as a service' tras configurarlo."
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $securityDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-Icacls {
    param([string[]]$Arguments)

    & icacls.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls fallo sobre '$($Arguments[0])' con codigo $LASTEXITCODE."
    }
}

function Protect-AtlasInstallTree {
    param(
        [string]$InstallPath,
        [pscustomobject]$ApiAccount,
        [pscustomobject]$WatchdogAccount
    )

    $normalSids = @("*S-1-1-0", "*S-1-5-11", "*S-1-5-32-545", "*S-1-5-4")
    Invoke-Icacls -Arguments ((@($InstallPath, "/inheritance:r", "/remove:g") + $normalSids + @("/remove:d") + $normalSids))
    Invoke-Icacls -Arguments @($InstallPath, "/setowner", "*S-1-5-32-544")
    Invoke-Icacls -Arguments @(
        $InstallPath, "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F",
        "${ApiAccount.ComputerPrincipal}:(OI)(CI)RX", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX")

    foreach ($relative in @("api", "watchdog", "scripts", "backups", "exports", "updates", "logs", "api\logs", "watchdog\logs")) {
        $path = Join-Path $InstallPath $relative
        if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
        $grant = if ($relative -in @("api", "watchdog")) {
            "${ApiAccount.ComputerPrincipal}:(OI)(CI)RX", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX"
        } elseif ($relative -eq "scripts") {
            "${ApiAccount.ComputerPrincipal}:(OI)(CI)RX", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX"
        } elseif ($relative -eq "updates") {
            # SECURITY (P1a): igual que ServiceSecurity.ps1 Protect-AtlasInstallTree.
            # La API necesita Modify (descarga/verifica/extrae el paquete aqui);
            # el runner elevado (SYSTEM) nunca confia en updates\, re-verifica la
            # firma sobre su propia copia en config\update-runner. Watchdog solo
            # necesita Modify sobre updates\requests (mas abajo).
            "${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX"
        } elseif ($relative -eq "backups") {
            # La API crea/borra sus propios dumps (BackupService, GoogleDriveBackupService)
            # y Watchdog escribe backup previo + rollback de binarios al actualizar.
            "${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M"
        } elseif ($relative -eq "exports") {
            "${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX"
        } elseif ($relative -eq "api\logs") {
            "${ApiAccount.ComputerPrincipal}:(OI)(CI)M"
        } elseif ($relative -eq "watchdog\logs") {
            "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M"
        } elseif ($relative -in @("backups", "exports", "logs")) {
            "${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M"
        }
        if ($grant) {
            Invoke-Icacls -Arguments @($path, "/T", "/C", "/remove:g", $ApiAccount.ComputerPrincipal, $WatchdogAccount.ComputerPrincipal, "/remove:d", $ApiAccount.ComputerPrincipal, $WatchdogAccount.ComputerPrincipal)
            Invoke-Icacls -Arguments (@($path, "/grant:r") + @($grant))
        }
    }

    $requestPath = Join-Path $InstallPath "updates\requests"
    New-Item -ItemType Directory -Path $requestPath -Force | Out-Null
    Invoke-Icacls -Arguments @($requestPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M")

    # Logs y estado del Watchdog: igual que ServiceSecurity.ps1
    # Protect-AtlasInstallTree. DACL protegida (SYSTEM, Administrators y la
    # cuenta del Watchdog) y la cuenta como propietaria, para que
    # WatchdogLogConfiguration pueda fijarla al arrancar.
    foreach ($relative in @("watchdog\logs", "state")) {
        $path = Join-Path $InstallPath $relative
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        Invoke-Icacls -Arguments @($path, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M")
        Invoke-Icacls -Arguments @($path, "/setowner", $WatchdogAccount.ComputerPrincipal, "/T", "/C")
    }

    $configPath = Join-Path $InstallPath "config"
    if (Test-Path -LiteralPath $configPath) {
        Invoke-Icacls -Arguments @($configPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F")
    }
    # SECURITY (P1b): igual que ServiceSecurity.ps1 Protect-AtlasInstallTree.
    # Solo Administrators/SYSTEM escriben en update-runner.
    $runnerPath = Join-Path $configPath "update-runner"
    New-Item -ItemType Directory -Path $runnerPath -Force | Out-Null
    Invoke-Icacls -Arguments @($runnerPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F")

    # Los ficheros de configuracion y el certificado se protegen antes de
    # sincronizar el paquete. Conservar solo Administrators/SYSTEM dejaria a
    # los servicios sin poder arrancar; conceder Modify permitiria alterar
    # secretos o binarios. Cada servicio recibe solo lectura del recurso que
    # necesita.
    $apiConfigPath = Join-Path $InstallPath "api\appsettings.Production.json"
    if (Test-Path -LiteralPath $apiConfigPath) {
        Invoke-Icacls -Arguments @($apiConfigPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:F", "*S-1-5-18:F", "${ApiAccount.ComputerPrincipal}:R")
    }
    $watchdogConfigPath = Join-Path $InstallPath "watchdog\appsettings.Production.json"
    if (Test-Path -LiteralPath $watchdogConfigPath) {
        Invoke-Icacls -Arguments @($watchdogConfigPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:F", "*S-1-5-18:F", "${WatchdogAccount.ComputerPrincipal}:R")
    }
    $certsPath = Join-Path $InstallPath "certs"
    if (Test-Path -LiteralPath $certsPath) {
        Invoke-Icacls -Arguments @($certsPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F", "${ApiAccount.ComputerPrincipal}:(OI)(CI)RX")
    }

    foreach ($file in @("VERSION", "atlas-balance.runtime.json", "watchdog-state.json")) {
        $path = Join-Path $InstallPath $file
        if (Test-Path -LiteralPath $path) {
            Invoke-Icacls -Arguments @($path, "/grant:r", "${WatchdogAccount.ComputerPrincipal}:M")
        }
    }

    $keysPath = Join-Path $env:ProgramData "AtlasBalance\keys"
    if (Test-Path -LiteralPath $keysPath) {
        Invoke-Icacls -Arguments @($keysPath, "/grant:r", "${ApiAccount.ComputerPrincipal}:(OI)(CI)M")
    }
    $securityLogPath = Join-Path $env:ProgramData "AtlasBalance\logs\security"
    if (Test-Path -LiteralPath $securityLogPath) {
        Invoke-Icacls -Arguments @($securityLogPath, "/grant:r", "${ApiAccount.ComputerPrincipal}:(OI)(CI)M")
    }
}

function Grant-ServiceControl {
    param([string]$ServiceName, [string]$AccountSid)

    $descriptor = (& sc.exe sdshow $ServiceName | Where-Object { $_ -match '^D:' } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($descriptor)) { throw "No se pudo leer el descriptor de seguridad de $ServiceName." }
    $ace = "(A;;CCLCSWLOCRCRPWP;;;${AccountSid})"
    $descriptor = [regex]::Replace($descriptor, '\(A;;[^;]*;;;' + [regex]::Escape($AccountSid) + '\)', '')
    $descriptor = if ($descriptor -match 'S:') { $descriptor -replace 'S:', "$ace`$&" } else { $descriptor + $ace }
    & sc.exe sdset $ServiceName $descriptor | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo conceder a Watchdog el control minimo de $ServiceName." }

    $verified = (& sc.exe sdshow $ServiceName | Where-Object { $_ -match '^D:' } | Select-Object -First 1)
    if ($verified -notmatch [regex]::Escape($ace)) {
        throw "No se verifico el permiso de control del servicio $ServiceName para Watchdog."
    }
}

function Test-ServiceRegistration {
    param([string]$Name, [string]$ExpectedPrincipal)

    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
    if ($null -eq $service -or $service.StartName -notin @($ExpectedPrincipal, "$env:COMPUTERNAME\$($ExpectedPrincipal.TrimStart('.\'))")) {
        throw "El servicio $Name no esta registrado con la cuenta minima esperada '$ExpectedPrincipal'."
    }
}

function Get-JsonPathValue {
    param(
        [object]$Object,
        [string[]]$Path
    )

    $current = $Object
    foreach ($part in $Path) {
        if ($null -eq $current -or -not ($current.PSObject.Properties.Name -contains $part)) {
            return $null
        }
        $current = $current.$part
    }

    return $current
}

function Get-ConnectionStringValue {
    param(
        [string]$ConnectionString,
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        return ""
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $partStart = 0
    $quote = [char]0
    for ($index = 0; $index -lt $ConnectionString.Length; $index++) {
        $character = $ConnectionString[$index]
        if ($quote -ne [char]0) {
            if ($character -eq $quote) {
                if ($index + 1 -lt $ConnectionString.Length -and $ConnectionString[$index + 1] -eq $quote) {
                    $index++
                }
                else {
                    $quote = [char]0
                }
            }
            continue
        }

        if ($character -eq [char]39 -or $character -eq [char]34) {
            $quote = $character
        }
        elseif ($character -eq ';') {
            $parts.Add($ConnectionString.Substring($partStart, $index - $partStart))
            $partStart = $index + 1
        }
    }
    $parts.Add($ConnectionString.Substring($partStart))

    foreach ($part in $parts) {
        $separator = $part.IndexOf('=')
        if ($separator -lt 0) { continue }

        $key = $part.Substring(0, $separator).Trim()
        if (-not [string]::Equals($key, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $value = $part.Substring($separator + 1).Trim()
        if ($value.Length -ge 2 -and (($value[0] -eq [char]39 -and $value[$value.Length - 1] -eq [char]39) -or ($value[0] -eq [char]34 -and $value[$value.Length - 1] -eq [char]34))) {
            $quoteCharacter = $value[0]
            $value = $value.Substring(1, $value.Length - 2).Replace([string]$quoteCharacter + [string]$quoteCharacter, [string]$quoteCharacter)
        }
        return $value
    }

    return ""
}

function Read-ExistingInstallConfiguration {
    param([string]$InstallPath)

    $apiConfigPath = Join-Path $InstallPath "api\appsettings.Production.json"
    if (-not (Test-Path -LiteralPath $apiConfigPath -PathType Leaf)) {
        return $null
    }

    try {
        $apiConfig = Get-Content -LiteralPath $apiConfigPath -Raw | ConvertFrom-Json
    } catch {
        throw "No se pudo leer la configuracion existente de Atlas Balance en ${apiConfigPath}: $($_.Exception.Message)"
    }

    $watchdogConfigPath = Join-Path $InstallPath "watchdog\appsettings.Production.json"
    $watchdogConfig = $null
    if (Test-Path -LiteralPath $watchdogConfigPath -PathType Leaf) {
        try {
            $watchdogConfig = Get-Content -LiteralPath $watchdogConfigPath -Raw | ConvertFrom-Json
        } catch {
            throw "No se pudo leer la configuracion existente del Watchdog en ${watchdogConfigPath}: $($_.Exception.Message)"
        }
    }

    $defaultConnection = [string](Get-JsonPathValue -Object $apiConfig -Path @("ConnectionStrings", "DefaultConnection"))
    $migrationConnection = [string](Get-JsonPathValue -Object $apiConfig -Path @("ConnectionStrings", "MigrationConnection"))
    $watchdogSettings = Get-JsonPathValue -Object $watchdogConfig -Path @("WatchdogSettings")
    $apiWatchdogSettings = Get-JsonPathValue -Object $apiConfig -Path @("WatchdogSettings")
    $certificate = Get-JsonPathValue -Object $apiConfig -Path @("Kestrel", "Endpoints", "Https", "Certificate")

    $watchdogSecret = [string](Get-JsonPathValue -Object $watchdogSettings -Path @("SharedSecret"))
    if ([string]::IsNullOrWhiteSpace($watchdogSecret)) {
        $watchdogSecret = [string](Get-JsonPathValue -Object $apiWatchdogSettings -Path @("SharedSecret"))
    }

    $ownerUser = Get-ConnectionStringValue -ConnectionString $migrationConnection -Name "Username"
    $ownerPassword = Get-ConnectionStringValue -ConnectionString $migrationConnection -Name "Password"
    if ([string]::IsNullOrWhiteSpace($ownerUser)) {
        $ownerUser = [string](Get-JsonPathValue -Object $watchdogSettings -Path @("DbOwnerUser"))
    }
    if ([string]::IsNullOrWhiteSpace($ownerPassword)) {
        $ownerPassword = [string](Get-JsonPathValue -Object $watchdogSettings -Path @("DbOwnerPassword"))
    }

    return [pscustomobject]@{
        JwtSecret = [string](Get-JsonPathValue -Object $apiConfig -Path @("JwtSettings", "Secret"))
        RlsContextSecret = [string](Get-JsonPathValue -Object $apiConfig -Path @("Security", "RlsContextSecret"))
        AuditSigningKey = [string](Get-JsonPathValue -Object $apiConfig -Path @("Security", "AuditSigningKey"))
        WatchdogSecret = $watchdogSecret
        DbHost = Get-ConnectionStringValue -ConnectionString $defaultConnection -Name "Host"
        DbPort = Get-ConnectionStringValue -ConnectionString $defaultConnection -Name "Port"
        DbName = Get-ConnectionStringValue -ConnectionString $defaultConnection -Name "Database"
        DbUser = Get-ConnectionStringValue -ConnectionString $defaultConnection -Name "Username"
        DbPassword = Get-ConnectionStringValue -ConnectionString $defaultConnection -Name "Password"
        DbOwnerUser = $ownerUser
        DbOwnerPassword = $ownerPassword
        CertificatePath = [string](Get-JsonPathValue -Object $certificate -Path @("Path"))
        CertificatePassword = [string](Get-JsonPathValue -Object $certificate -Path @("Password"))
    }
}

function Write-AppSettings {
    param(
        [string]$ApiPath,
        [string]$WatchdogPath,
        [string]$PostgresBin,
        [string]$CertPath,
        [string]$CertPassword,
        [string]$JwtSecret,
        [string]$WatchdogSecret,
        [string]$RlsContextSecret,
        [string]$AuditSigningKey
    )

    # El estado vive en un directorio propio del Watchdog (ver
    # Protect-AtlasInstallTree): la raiz de instalacion es de solo lectura
    # para su cuenta y WatchdogStateStore escribe un temporal junto al fichero.
    $stateFile = Join-Path $InstallPath "state\watchdog-state.json"
    $watchdogLogDirectory = Join-Path $InstallPath "watchdog\logs"
    $updateRoot = Join-Path $InstallPath "updates"
    $backupPath = Join-Path $InstallPath "backups"
    $exportPath = Join-Path $InstallPath "exports"
    $apiTarget = Join-Path $InstallPath "api"
    $dataProtectionKeysPath = Join-Path $env:ProgramData "AtlasBalance\keys"
    $securityLogPath = Join-Path $env:ProgramData "AtlasBalance\logs\security"
    # V-02-05 (CONFIG-002): anadir sslmode=require a la connection string cuando el
    # host NO es localhost. Para localhost (caso comun) el SSL es opcional.
    $sslMode = if ($DbHost -eq "localhost" -or $DbHost -eq "127.0.0.1") { "" } else { ";sslmode=require" }
    $connection = "Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbUser;Password=$DbPassword$sslMode;Application Name=AtlasBalance.API;Maximum Pool Size=20;Minimum Pool Size=0"
    $migrationConnection = "Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbOwnerUser;Password=$DbOwnerPassword$sslMode;Application Name=AtlasBalance.Migrate;Maximum Pool Size=4;Minimum Pool Size=0"
    $forwardedKnownProxies = if ($UseReverseProxy) { @($ReverseProxyIp) } else { @() }
    $allowedHosts = if ($UseReverseProxy) {
        "$effectivePublicHost;$ServerName;localhost"
    } else {
        "$ServerName;localhost"
    }
    $kestrelEndpointName = if ($UseReverseProxy) { "Http" } else { "Https" }
    $kestrelEndpoint = [ordered]@{
        Url = $internalApiUrl
    }
    if (-not $UseReverseProxy) {
        $kestrelEndpoint.Certificate = [ordered]@{
            Path = $CertPath
            Password = $CertPassword
        }
    }
    $kestrelEndpoints = [ordered]@{}
    $kestrelEndpoints[$kestrelEndpointName] = $kestrelEndpoint

    $seedAdminPassword = if ($ExistingUsersDetected) { "" } else { $AdminPassword }

    $apiConfig = [ordered]@{
        ConnectionStrings = [ordered]@{
            DefaultConnection = $connection
            MigrationConnection = $migrationConnection
        }
        JwtSettings = [ordered]@{
            Secret = $JwtSecret
            AccessTokenExpMinutes = 60
            RefreshTokenExpDays = 7
        }
        SeedAdmin = [ordered]@{
            Email = $AdminEmail
            Password = $seedAdminPassword
        }
        App = [ordered]@{
            BaseUrl = $appUrl
        }
        Security = [ordered]@{
            RequireMfaForWebUsers = $true
            # V-02-06 (RLS-SEC-01): clave independiente del secreto JWT para
            # firmar contextos RLS. Se genera siempre aleatoria en instalacion
            # nueva; la persistencia la gestiona Actualizar-AtlasBalance.ps1
            # para instalaciones existentes.
            RlsContextSecret = $RlsContextSecret
            # V-02.07: clave con la que se firma cada fila de AUDITORIAS. Tiene
            # que ser distinta de JwtSettings:Secret y de RlsContextSecret: si se
            # comparten, comprometer una permite forjar auditoria con firma
            # valida y el rastro deja de servir como prueba.
            AuditSigningKey = $AuditSigningKey
            MirrorToWindowsEventLog = $true
            Alertas = [ordered]@{
                Habilitado = $true
                # Vacio = se avisa a todos los usuarios con rol ADMIN activos.
                DestinatariosEmail = @()
                # Opt-in. Es un secreto: quien tenga la URL puede publicar en el
                # canal. Rellenar a mano tras la instalacion si se quiere Slack.
                SlackWebhookUrl = ""
            }
        }
        Auditoria = [ordered]@{
            # V-02.07: sube de 28 a 365 dias. El suelo real (90) lo impone el
            # trigger append-only de la base de datos.
            RetentionDays = 365
        }
        Documentation = [ordered]@{
            # V-02.08: sin esto, DocumentationHelpService cae en el fallback de
            # Program.cs (subir 4 niveles desde ContentRootPath), que solo
            # resuelve en el checkout de desarrollo. En produccion la API corre
            # desde <InstallPath>\api, donde Build-Release.ps1 empaqueta el
            # manual en api\Documentacion\DOCUMENTACION_USUARIO.md.
            UserPath = Join-Path $apiTarget "Documentacion\DOCUMENTACION_USUARIO.md"
        }
        ForwardedHeaders = [ordered]@{
            KnownProxies = $forwardedKnownProxies
            KnownNetworks = @()
        }
        Ia = [ordered]@{
            UseSystemProxy = $false
            ProxyUrl = ""
        }
        WatchdogSettings = [ordered]@{
            BaseUrl = "http://localhost:$WatchdogPort"
            SharedSecret = $WatchdogSecret
            PostgresBinPath = $PostgresBin
            StateFilePath = $stateFile
            DockerPostgresContainer = "atlas_balance_db"
            DockerCliPath = "C:\Program Files\Docker\Docker\resources\bin\docker.exe"
            UpdateSourceRoot = $updateRoot
            UpdateInstallPath = $InstallPath
            UpdateTargetPath = $apiTarget
            RequireDatabaseBackupBeforeUpdate = $true
            RequireHealthCheckAfterUpdate = $true
            # V-02.09: sonda funcional, no liveness; con /api/health una
            # actualizacion con login roto y proceso vivo daria por buena la
            # actualizacion (incidente V-02.07).
            ApiHealthUrl = ($healthUrl -replace '/api/health$', '/api/health/functional')
        }
        GitHubSettings = [ordered]@{
            UpdateToken = ""
        }
        UpdateSecurity = [ordered]@{
            ReleaseSigningPublicKeyPem = $ReleaseSigningPublicKeyPem
        }
        DataProtection = [ordered]@{
            KeysPath = $dataProtectionKeysPath
        }
        Kestrel = [ordered]@{
            Endpoints = $kestrelEndpoints
        }
        Serilog = [ordered]@{
            MinimumLevel = [ordered]@{
                Default = "Information"
                Override = [ordered]@{
                    Microsoft = "Warning"
                    "Microsoft.EntityFrameworkCore" = "Warning"
                    Hangfire = "Warning"
                }
            }
            # V-02.07: los eventos de seguridad ademas van a su propio fichero,
            # con retencion mas larga y ACL propia (ver Protect-SecurityLogDirectory).
            SecurityFilePath = Join-Path $securityLogPath "atlas-security-.log"
        }
        AllowedHosts = $allowedHosts
    }

    $watchdogConfig = [ordered]@{
        WatchdogSettings = [ordered]@{
            SharedSecret = $WatchdogSecret
            ApiServiceName = $ApiServiceName
            PostgresBinPath = $PostgresBin
            BackupPath = $backupPath
            StateFilePath = $stateFile
            LogDirectory = $watchdogLogDirectory
            DbHost = $DbHost
            DbPort = [string]$DbPort
            DbName = $DbName
            DbOwnerUser = $DbOwnerUser
            DbOwnerPassword = $DbOwnerPassword
            DbUser = $DbUser
            DbPassword = $DbPassword
            DockerPostgresContainer = "atlas_balance_db"
            DockerCliPath = "C:\Program Files\Docker\Docker\resources\bin\docker.exe"
            UpdateSourceRoot = $updateRoot
            UpdateInstallPath = $InstallPath
            UpdateTargetPath = $apiTarget
        }
        UpdateSecurity = [ordered]@{
            ReleaseSigningPublicKeyPem = $ReleaseSigningPublicKeyPem
        }
        Serilog = [ordered]@{
            MinimumLevel = [ordered]@{
                Default = "Information"
            }
        }
    }

    $apiSettingsPath = Join-Path $ApiPath "appsettings.Production.json"
    $watchdogSettingsPath = Join-Path $WatchdogPath "appsettings.Production.json"

    # La configuracion productiva contiene secretos y decisiones del operador.
    # En una reinstalacion no se debe regenerar ni siquiera si el resto del
    # paquete se vuelve a copiar. Exigimos ambos ficheros para no crear un
    # Watchdog con un secreto distinto al de una API ya existente.
    $apiExists = Test-Path -LiteralPath $apiSettingsPath -PathType Leaf
    $watchdogExists = Test-Path -LiteralPath $watchdogSettingsPath -PathType Leaf
    if ($apiExists -or $watchdogExists) {
        if (-not ($apiExists -and $watchdogExists)) {
            throw "Instalacion existente incompleta: deben existir api\appsettings.Production.json y watchdog\appsettings.Production.json para preservar la configuracion sin desalinear secretos."
        }

        Write-Host "Configuracion productiva existente preservada; no se regeneran secretos ni se sobrescribe appsettings.Production.json." -ForegroundColor Yellow
    }
    else {
        Write-JsonFile -Value $apiConfig -Path $apiSettingsPath
        Write-JsonFile -Value $watchdogConfig -Path $watchdogSettingsPath
    }
    Protect-SecretFile -Path $apiSettingsPath
    Protect-SecretFile -Path $watchdogSettingsPath
    Protect-RestrictedDirectory -Path $dataProtectionKeysPath
    Protect-SecurityLogDirectory -Path $securityLogPath
    Register-SecurityEventLogSource
}

function Install-OrReplaceService {
    param(
        [string]$Name,
        [string]$DisplayName,
        [string]$Description,
        [string]$ExePath,
        [pscredential]$Credential,
        [string]$ExpectedPrincipal
    )

    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne "Stopped") {
            Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
            $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
        }
        sc.exe delete $Name | Out-Null
        Start-Sleep -Seconds 2
    }

    New-Service `
        -Name $Name `
        -BinaryPathName ('"' + $ExePath + '"') `
        -DisplayName $DisplayName `
        -Description $Description `
        -StartupType Automatic `
        -Credential $Credential | Out-Null

    sc.exe failure $Name reset=86400 actions=restart/10000/restart/30000/restart/60000 | Out-Null
    Test-ServiceRegistration -Name $Name -ExpectedPrincipal $ExpectedPrincipal
}

function New-AtlasIcon {
    param([string]$PngPath, [string]$IcoPath)

    if (-not (Test-Path $PngPath)) {
        return $false
    }

    try {
        Add-Type -AssemblyName System.Drawing
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class AtlasNativeIcon {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);
}
"@
        $source = [Drawing.Bitmap]::FromFile($PngPath)
        $bitmap = [Drawing.Bitmap]::new(64, 64)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.DrawImage($source, 0, 0, 64, 64)
        $handle = $bitmap.GetHicon()
        $icon = [Drawing.Icon]::FromHandle($handle)
        $stream = [IO.File]::Create($IcoPath)
        try {
            $icon.Save($stream)
        } finally {
            $stream.Dispose()
            $icon.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
            $source.Dispose()
            [AtlasNativeIcon]::DestroyIcon($handle) | Out-Null
        }
        return $true
    } catch {
        return $false
    }
}

function New-AtlasShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$IconPath,
        [string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    if (Test-Path $IconPath) {
        $shortcut.IconLocation = $IconPath
    }
    $shortcut.Description = "Abrir Atlas Balance"
    $shortcut.Save()
}

function Register-CredentialsCleanupTask {
    param([string]$CredentialsPath)

    if ([string]::IsNullOrWhiteSpace($CredentialsPath) -or -not [IO.Path]::IsPathRooted($CredentialsPath)) {
        return
    }

    $taskName = "AtlasBalance.DeleteInstallCredentialsOnce"
    $escapedPath = $CredentialsPath.Replace("'", "''")
    $escapedTaskName = $taskName.Replace("'", "''")
    $command = "Remove-Item -LiteralPath '$escapedPath' -Force -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName '$escapedTaskName' -Confirm:`$false -ErrorAction SilentlyContinue"

    try {
        $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -Command `"$command`""
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddHours(24)
        Register-ScheduledTask `
            -TaskName $taskName `
            -Action $action `
            -Trigger $trigger `
            -Description "Borra el archivo temporal de credenciales iniciales de Atlas Balance." `
            -RunLevel Highest `
            -User "SYSTEM" `
            -Force | Out-Null
    } catch {
        Write-Host "No se pudo programar el borrado automatico de credenciales: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Write-RuntimeAndCredentials {
    param([string]$AppUrl)

    $runtime = [ordered]@{
        Version = $AppVersion
        AppUrl = $AppUrl
        UseReverseProxy = [bool]$UseReverseProxy
        PublicHost = $effectivePublicHost
        PublicPort = $PublicPort
        ApiPort = $ApiPort
        InternalApiPort = $InternalApiPort
        ApiServiceName = $ApiServiceName
        WatchdogServiceName = $WatchdogServiceName
        ApiServiceAccount = $ApiServiceAccount
        WatchdogServiceAccount = $WatchdogServiceAccount
        PostgresServiceName = if ($ManagedPostgres) { $PostgresServiceName } else { "" }
        ManagedPostgres = [bool]$ManagedPostgres
        DbHost = $DbHost
        DbPort = $DbPort
        DbName = $DbName
        InstalledAt = (Get-Date).ToString("o")
    }
    Write-JsonFile -Value $runtime -Path (Join-Path $InstallPath "atlas-balance.runtime.json")
    Set-Content -LiteralPath (Join-Path $InstallPath "VERSION") -Value $AppVersion -Encoding UTF8

    if ($ExistingConfigurationDetected) {
        Write-Host "Instalacion existente: se conserva INSTALL_CREDENTIALS_ONCE.txt y no se vuelven a emitir credenciales." -ForegroundColor Yellow
        return
    }

    $credentialsPath = Join-Path (Join-Path $InstallPath "config") "INSTALL_CREDENTIALS_ONCE.txt"
    if ($ExistingUsersDetected) {
        $lines = @(
            "Atlas Balance $AppVersion",
            "URL: $AppUrl",
            "Base existente detectada.",
            "Las credenciales iniciales no se regeneran.",
            "Usa el admin ya creado o ejecuta scripts\Reset-AdminPassword.ps1 desde la instalacion.",
            "Base de datos: $DbName",
            "Usuario DB app: $DbUser",
            "Password DB app: $DbPassword",
            "Usuario DB migracion/owner: $DbOwnerUser",
            "Password DB migracion/owner: $DbOwnerPassword",
            "PostgreSQL gestionado por Atlas: $ManagedPostgres",
            "",
            "Guarda esto en un gestor de passwords y borra este archivo.",
            "Si nadie lo borra antes, el instalador intentara eliminarlo automaticamente en 24 horas.",
            "Borra este archivo tras el primer acceso. Si permanece en el servidor, la instalacion no es segura."
        )
    } else {
        $lines = @(
            "Atlas Balance $AppVersion",
            "URL: $AppUrl",
            "Admin inicial: $AdminEmail",
            "Password admin inicial: $AdminPassword",
            "Base de datos: $DbName",
            "Usuario DB app: $DbUser",
            "Password DB app: $DbPassword",
            "Usuario DB migracion/owner: $DbOwnerUser",
            "Password DB migracion/owner: $DbOwnerPassword",
            "PostgreSQL gestionado por Atlas: $ManagedPostgres",
            "",
            "Guarda esto en un gestor de passwords y borra este archivo.",
            "Si nadie lo borra antes, el instalador intentara eliminarlo automaticamente en 24 horas.",
            "Borra este archivo tras el primer acceso. Si permanece en el servidor, la instalacion no es segura."
        )
    }
    if ($ManagedPostgres) {
        $lines = @(
            $lines[0..6],
            "Servicio PostgreSQL: $PostgresServiceName",
            "Puerto PostgreSQL: $DbPort",
            "Password superusuario PostgreSQL: $GeneratedPostgresAdminPassword",
            $lines[7..($lines.Count - 1)]
        ) | ForEach-Object { $_ }
    }
    # V-02.08 (cierre del bug V-02.07): las credenciales se imprimen por
    # consola UNA SOLA VEZ y tambien se guardan en
    # config\INSTALL_CREDENTIALS_ONCE.txt con ACL restringida
    # (Administrators + SYSTEM, herencia desactivada). La tarea programada
    # `AtlasBalance.DeleteInstallCredentialsOnce` borra el archivo a las
    # 24h. Si el instalador no consigue escribir el archivo, aborta con
    # error: un instalador que no deja credenciales accesibles es mejor
    # que uno que dice que las dejo y miente.
    Write-SecretFile -Path $credentialsPath -Lines $lines
    Register-CredentialsCleanupTask -CredentialsPath $credentialsPath

    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Yellow
    Write-Host "CREDENCIALES INICIALES (captura esto en tu gestor de passwords)" -ForegroundColor Yellow
    Write-Host "=============================================" -ForegroundColor Yellow
    foreach ($line in $lines) {
        Write-Host $line
    }
    Write-Host "=============================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Archivo de credenciales: $credentialsPath" -ForegroundColor Yellow
    Write-Host "Se borrara automaticamente en 24 horas. Borralo a mano tras el primer acceso." -ForegroundColor Yellow
}

$packageRoot = Split-Path -Parent $PSScriptRoot
$apiSource = Join-Path $packageRoot "api"
$watchdogSource = Join-Path $packageRoot "watchdog"

if (-not (Test-Path (Join-Path $apiSource "AtlasBalance.API.exe")) -or
    -not (Test-Path (Join-Path $watchdogSource "AtlasBalance.Watchdog.exe"))) {
    throw "Esta carpeta no es el paquete instalable. Genera o descarga AtlasBalance-$AppVersion-win-x64.zip. Ejecuta el instalador desde la carpeta descomprimida que contiene api\AtlasBalance.API.exe, watchdog\AtlasBalance.Watchdog.exe, scripts e install.cmd."
}

if (-not (Test-IsAdmin)) {
    throw "Ejecuta este instalador como Administrador."
}

$effectivePublicHost = if ([string]::IsNullOrWhiteSpace($PublicHost)) { $ServerName.Trim() } else { $PublicHost.Trim() }
if (-not (Test-HostValue $ServerName)) {
    throw "ServerName debe ser un hostname sin esquema, puerto, rutas ni comodines."
}
if (-not (Test-HostValue $effectivePublicHost)) {
    throw "PublicHost debe ser un dominio/hostname sin esquema, puerto, rutas ni comodines. Usa balance.ejemplo.com, no https://balance.ejemplo.com."
}
if ($UseReverseProxy -and -not (Test-IpValue $ReverseProxyIp)) {
    throw "ReverseProxyIp debe ser una IP valida del proxy inverso, por ejemplo 127.0.0.1."
}
if ($UseReverseProxy -and $InternalApiPort -eq $PublicPort) {
    throw "InternalApiPort y PublicPort no deben ser el mismo puerto en modo reverse proxy."
}

# V-02.08: preflight obligatorio antes de tocar nada. El incidente V-02.07
# mostro que la primera instalacion se queda a medias por condiciones del
# entorno que el instalador no comprueba hasta que ya ha escrito cosas.
# Bloquea la instalacion si el disco esta casi lleno, si los puertos estan
# ya ocupados por otro proceso, si los binarios previos estan bloqueados
# o si la carpeta de instalacion no es escribible. Esto es lo que evita
# el "lo dejo a medias pero no rollback" que fue el peor sintoma del
# incidente.
# V-02.08 (revision PR #33): misma condicion que mas abajo decide si se monta
# PostgreSQL gestionado (linea ~1316): -InstallDependencies sin
# -PostgresAdminPassword. Solo en ese caso el preflight debe exigir DbPort libre.
$existingInstallConfiguration = Read-ExistingInstallConfiguration -InstallPath $InstallPath
$ExistingConfigurationDetected = $null -ne $existingInstallConfiguration

if ($ExistingConfigurationDetected) {
    # En una reinstalacion la configuracion ya escrita por el operador es la
    # autoridad. Reutilizar tambien las credenciales de BD evita que
    # Ensure-Database cambie las contrasenas de los roles y deje el appsettings
    # conservado apuntando a credenciales antiguas.
    if (-not [string]::IsNullOrWhiteSpace($existingInstallConfiguration.DbHost)) { $DbHost = $existingInstallConfiguration.DbHost }
    if (-not [string]::IsNullOrWhiteSpace($existingInstallConfiguration.DbPort)) { $DbPort = [int]$existingInstallConfiguration.DbPort }
    if (-not [string]::IsNullOrWhiteSpace($existingInstallConfiguration.DbName)) { $DbName = $existingInstallConfiguration.DbName }
    if (-not [string]::IsNullOrWhiteSpace($existingInstallConfiguration.DbUser)) { $DbUser = $existingInstallConfiguration.DbUser }
    if (-not [string]::IsNullOrWhiteSpace($existingInstallConfiguration.DbPassword)) { $DbPassword = $existingInstallConfiguration.DbPassword }
    if (-not [string]::IsNullOrWhiteSpace($existingInstallConfiguration.DbOwnerUser)) { $DbOwnerUser = $existingInstallConfiguration.DbOwnerUser }
    if (-not [string]::IsNullOrWhiteSpace($existingInstallConfiguration.DbOwnerPassword)) { $DbOwnerPassword = $existingInstallConfiguration.DbOwnerPassword }

    $jwtSecret = $existingInstallConfiguration.JwtSecret
    $rlsContextSecret = $existingInstallConfiguration.RlsContextSecret
    $auditSigningKey = $existingInstallConfiguration.AuditSigningKey
    $watchdogSecret = $existingInstallConfiguration.WatchdogSecret
    # -InstallDependencies es una orden para preparar una instancia nueva.
    # En una reinstalacion no puede sustituir el host/puerto de la configuracion
    # conservada ni crear roles en otra base de datos.
    if ($InstallDependencies) {
        Write-Host "Instalacion existente detectada: se omite la preparacion automatica de PostgreSQL y se conserva su host/puerto." -ForegroundColor Yellow
        $InstallDependencies = $false
    }
}
else {
    $jwtSecret = New-RandomSecret 64
    $rlsContextSecret = New-RandomSecret 64
    $auditSigningKey = New-RandomSecret 64
    $watchdogSecret = New-RandomSecret 64
}

if ($ExistingConfigurationDetected) {
    if (-not $SkipDatabaseSetup -and [string]::IsNullOrWhiteSpace($DbPassword)) {
        throw "La configuracion existente no contiene una contraseña parseable para DbUser; se cancela para no cambiar la contraseña de la base de datos."
    }
    if (-not $SkipDatabaseSetup -and [string]::IsNullOrWhiteSpace($DbOwnerPassword)) {
        throw "La configuracion existente no contiene una contraseña parseable para DbOwnerUser; se cancela para no cambiar la contraseña de la base de datos."
    }
}
else {
    if ([string]::IsNullOrWhiteSpace($DbPassword)) { $DbPassword = New-RandomSecret 40 }
    if ([string]::IsNullOrWhiteSpace($DbOwnerPassword)) { $DbOwnerPassword = New-RandomSecret 40 }
}
if ([string]::IsNullOrWhiteSpace($AdminPassword) -and -not $ExistingConfigurationDetected) { $AdminPassword = New-RandomSecret 24 }
if ([string]::IsNullOrWhiteSpace($PostgresInstallPath)) { $PostgresInstallPath = Join-Path $InstallPath "postgresql\16" }
if ([string]::IsNullOrWhiteSpace($PostgresDataPath)) { $PostgresDataPath = Join-Path $InstallPath "postgres-data" }
$certPassword = if ($ExistingConfigurationDetected) { "" } else { New-RandomSecret 40 }

if ($ExistingConfigurationDetected -and -not $UseReverseProxy) {
    if ([string]::IsNullOrWhiteSpace($existingInstallConfiguration.CertificatePath)) {
        throw "La instalacion existente no contiene Kestrel:Endpoints:Https:Certificate:Path; no se puede regenerar el certificado sin sobrescribir appsettings.Production.json."
    }
    $certPassword = $existingInstallConfiguration.CertificatePassword
}

$willInstallManagedDb = (-not $SkipDatabaseSetup) -and $InstallDependencies -and [string]::IsNullOrWhiteSpace($PostgresAdminPassword)
Test-AtlasPreflight -InstallPath $InstallPath -ApiPort $ApiPort -InternalApiPort $InternalApiPort -WatchdogPort $WatchdogPort -DbPort $DbPort -PublicPort $PublicPort -WillInstallManagedDb $willInstallManagedDb
$internalApiUrl = if ($UseReverseProxy) { "http://127.0.0.1:$InternalApiPort" } else { "https://0.0.0.0:$ApiPort" }
$healthUrl = if ($UseReverseProxy) { "http://localhost:$InternalApiPort/api/health" } elseif ($ApiPort -eq 443) { "https://localhost/api/health" } else { "https://localhost`:$ApiPort/api/health" }
$appUrl = if ($UseReverseProxy) {
    if ($PublicPort -eq 443) { "https://$effectivePublicHost" } else { "https://$effectivePublicHost`:$PublicPort" }
} else {
    if ($ApiPort -eq 443) { "https://$ServerName" } else { "https://$ServerName`:$ApiPort" }
}

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
foreach ($dir in @("api", "watchdog", "scripts", "backups", "exports", "logs", "certs", "updates", "config")) {
    New-Item -ItemType Directory -Path (Join-Path $InstallPath $dir) -Force | Out-Null
}

# Backups y exportaciones contienen datos financieros y PII en claro. No deben
# heredar lectura para usuarios locales del servidor; las ACL definitivas se
# aplican despues de sincronizar el paquete, con permisos de API/Watchdog
# separados.
Protect-RestrictedDirectory -Path (Join-Path $InstallPath "backups")
Protect-RestrictedDirectory -Path (Join-Path $InstallPath "exports")

$apiServiceAccount = Initialize-AtlasServiceAccount `
    -Name $ApiServiceAccount `
    -Description "Cuenta minima del servicio Atlas Balance API"
$watchdogServiceAccount = Initialize-AtlasServiceAccount `
    -Name $WatchdogServiceAccount `
    -Description "Cuenta minima del servicio Atlas Balance Watchdog"
Grant-LogOnAsService -Sids @($apiServiceAccount.Sid, $watchdogServiceAccount.Sid) -InstallPath $InstallPath
$ApiServiceAccountPrincipal = $apiServiceAccount.ComputerPrincipal
$WatchdogServiceAccountPrincipal = $watchdogServiceAccount.ComputerPrincipal

if (-not $SkipDatabaseSetup) {
    if ($InstallDependencies -and [string]::IsNullOrWhiteSpace($PostgresAdminPassword)) {
        $DbHost = "localhost"
        $DbPort = Resolve-PostgresPort -PreferredPort $DbPort
        $generatedSuperPassword = New-RandomSecret 40
        $postgresInstalled = Try-InstallPostgres `
            -PackageId $PostgresPackageId `
            -Port $DbPort `
            -SuperPassword $generatedSuperPassword `
            -PrefixPath $PostgresInstallPath `
            -DataPath $PostgresDataPath `
            -ServiceName $PostgresServiceName
        if (-not $postgresInstalled) {
            Write-PostgresManualInstallHint
        }
        $PostgresBinPath = Find-PostgresBin -PreferredPath (Join-Path $PostgresInstallPath "bin")
    }

    if ([string]::IsNullOrWhiteSpace($PostgresBinPath)) {
        $PostgresBinPath = Find-PostgresBin -PreferredPath $PostgresBinPath
    }
    if ([string]::IsNullOrWhiteSpace($PostgresBinPath) -and $InstallDependencies) {
        Write-PostgresManualInstallHint
        throw "No se pudo preparar PostgreSQL automaticamente. Instala PostgreSQL 16+ manualmente o pasa -PostgresAdminPassword y -PostgresBinPath para usar una instancia existente."
    }
    if ([string]::IsNullOrWhiteSpace($PostgresBinPath)) {
        Write-PostgresManualInstallHint
        throw "No se encontro PostgreSQL 16+. Indica -PostgresBinPath o instala PostgreSQL manualmente."
    }

    # V-02.08: preflight obligatorio antes de tocar la base. El incidente
    # V-02.07 demostro que una version incompatible o una extension que
    # falta se manifiestan tarde (en la primera migracion o el primer
    # login) y dejan el sistema a medio instalar.
    Test-PostgresPreflight -PostgresBin $PostgresBinPath

    Ensure-Database -PostgresBin $PostgresBinPath
    $ExistingUsersDetected = Test-ExistingApplicationUsers -PostgresBin $PostgresBinPath
    if ($ExistingUsersDetected) {
        Write-Host "Base existente detectada. Las credenciales iniciales no se regeneran." -ForegroundColor Yellow
        Write-Host "Usa el admin ya creado o ejecuta scripts\Reset-AdminPassword.ps1 despues de instalar." -ForegroundColor Yellow
    }
}

# V-02.07 (DB-EXPOSURE): solo tocamos la configuracion de PostgreSQL cuando la
# instancia es local Y la gestiona este instalador (via winget). Si el
# operador apunta a un Postgres externo/preexistente, o si SkipDatabaseSetup
# esta activo, no tocamos nada ajeno: solo avisamos por consola.
if ($SkipDatabaseSetup) {
    Write-Host "SkipDatabaseSetup activo: no se modifica la configuracion de PostgreSQL de este equipo (listen_addresses)." -ForegroundColor Yellow
} elseif ($DbHost -notin @("localhost", "127.0.0.1", "::1")) {
    Write-Host "DbHost ('$DbHost') no es local: no se modifica la configuracion de PostgreSQL de este equipo." -ForegroundColor Yellow
} elseif ($ManagedPostgres) {
    Set-PostgresListenLocalhost -DataPath $PostgresDataPath -ServiceName $PostgresServiceName
} else {
    Write-Host "PostgreSQL local no gestionado por este instalador: no se modifica su postgresql.conf. Verifica listen_addresses manualmente." -ForegroundColor Yellow
}

$apiPath = Join-Path $InstallPath "api"
$watchdogPath = Join-Path $InstallPath "watchdog"
Sync-DirectoryPreserveConfig -Source $apiSource -Target $apiPath
Sync-DirectoryPreserveConfig -Source $watchdogSource -Target $watchdogPath

Copy-Item -LiteralPath (Join-Path $packageRoot "Atlas Balance.cmd") -Destination (Join-Path $InstallPath "Atlas Balance.cmd") -Force

# V-02.08: copia los scripts de soporte que el operador necesitara si algo
# se rompe en campo. V-02.07 demostro que sin ellos un cliente se queda
# sin herramientas (los hotfix se quedan en el ZIP y el operador no sabe
# donde mirar). Si el script no existe en el paquete se omite sin error.
foreach ($supportScript in @(
    "Launch-AtlasBalance.ps1",
    "Reset-AdminPassword.ps1",
    "Repair-RlsContext.ps1",
    "Deploy-RlsHotfix.ps1",
    "Grant-OwnerBypassRls.ps1",
    "Test-BackupRestore.ps1",
    "Test-AtlasSecrets.ps1",
    "Test-AtlasSmtp.ps1",
    "Smoke-Test-AtlasBalance.ps1",
    "Mfa-Totp.ps1",
    "Mfa-Totp.Tests.ps1",
    "ServiceSecurity.ps1",
    "Run-AtlasElevatedUpdate.ps1",
    "Sync-AtlasDirectory.ps1",
    "Sync-AtlasDirectory.Tests.ps1"
)) {
    $source = Join-Path $packageRoot "scripts\$supportScript"
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $InstallPath "scripts\$supportScript") -Force
    }
}

$certPath = ""
$effectiveCertPassword = ""
if (-not $UseReverseProxy) {
    if ($ExistingConfigurationDetected) {
        $certPath = $existingInstallConfiguration.CertificatePath
        $effectiveCertPassword = $existingInstallConfiguration.CertificatePassword
        Write-Host "Certificado HTTPS existente preservado; no se vuelve a emitir." -ForegroundColor Yellow
    }
    else {
        $cert = New-AtlasCertificate -CertDirectory (Join-Path $InstallPath "certs") -DnsName $ServerName -Password $certPassword
        Protect-RestrictedDirectory -Path (Join-Path $InstallPath "certs")
        Protect-SecretFile -Path $cert.Path
        $certPath = $cert.Path
        $effectiveCertPassword = $cert.Password
    }
}
Write-AppSettings `
    -ApiPath $apiPath `
    -WatchdogPath $watchdogPath `
    -PostgresBin $PostgresBinPath `
    -CertPath $certPath `
    -CertPassword $effectiveCertPassword `
    -JwtSecret $jwtSecret `
    -WatchdogSecret $watchdogSecret `
    -RlsContextSecret $rlsContextSecret `
    -AuditSigningKey $auditSigningKey

$apiExe = Join-Path $apiPath "AtlasBalance.API.exe"
$watchdogExe = Join-Path $watchdogPath "AtlasBalance.Watchdog.exe"
Protect-AtlasInstallTree -InstallPath $InstallPath -ApiAccount $apiServiceAccount -WatchdogAccount $watchdogServiceAccount
Install-AtlasUpdateTask -InstallPath $InstallPath -WatchdogAccount $watchdogServiceAccount
Install-OrReplaceService `
    -Name $WatchdogServiceName `
    -DisplayName "Atlas Balance - Watchdog" `
    -Description "Backups y actualizaciones de Atlas Balance" `
    -ExePath $watchdogExe `
    -Credential $watchdogServiceAccount.Credential `
    -ExpectedPrincipal $watchdogServiceAccount.Principal
Install-OrReplaceService `
    -Name $ApiServiceName `
    -DisplayName "Atlas Balance - API" `
    -Description "API y frontend de Atlas Balance" `
    -ExePath $apiExe `
    -Credential $apiServiceAccount.Credential `
    -ExpectedPrincipal $apiServiceAccount.Principal
Grant-ServiceControl -ServiceName $ApiServiceName -AccountSid $watchdogServiceAccount.Sid
Grant-ServiceControl -ServiceName $WatchdogServiceName -AccountSid $watchdogServiceAccount.Sid

$firewallPort = if ($UseReverseProxy) { $PublicPort } else { $ApiPort }
$firewallName = if ($UseReverseProxy) { "Atlas Balance Public HTTPS $PublicPort" } else { "Atlas Balance HTTPS $ApiPort" }
# V-02-05 (CONFIG-001): por defecto, restringir el firewall al rango de la LAN
# local. Si el operador quiere exponer a internet, debe pasar -AllowInternet 1
# explicitamente.
$firewallRemoteAddress = if ($AllowInternet) { "Any" } else { "LocalSubnet" }
if (-not (Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue)) {
    if ($AllowInternet) {
        Write-Warning "CONFIG-001: abriendo firewall a internet. Esto es INSEGURO salvo que haya un WAF/reverse proxy externo."
    }
    New-NetFirewallRule -DisplayName $firewallName -Direction Inbound -Protocol TCP -LocalPort $firewallPort -RemoteAddress $firewallRemoteAddress -Action Allow | Out-Null
}

Start-Service -Name $WatchdogServiceName
Start-Service -Name $ApiServiceName

Write-RuntimeAndCredentials -AppUrl $appUrl

$logoPng = Join-Path $apiPath "wwwroot\logos\Atlas Balance.png"
$iconPath = Join-Path $InstallPath "Atlas Balance.ico"
[void](New-AtlasIcon -PngPath $logoPng -IcoPath $iconPath)

$shortcutTargets = @(
    [Environment]::GetFolderPath("Desktop"),
    [Environment]::GetFolderPath("CommonDesktopDirectory"),
    [Environment]::GetFolderPath("CommonStartMenu")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

foreach ($shortcutRoot in $shortcutTargets) {
    $shortcutPath = Join-Path $shortcutRoot "Atlas Balance.lnk"
    New-AtlasShortcut -ShortcutPath $shortcutPath -TargetPath (Join-Path $InstallPath "Atlas Balance.cmd") -IconPath $iconPath -WorkingDirectory $InstallPath
}

# V-02-05 (CONFIG-020): usar -SkipCertificateCheck en lugar de tocar el callback global.
# V-02.08 (revision PR #33): /api/health/functional (no /api/health) es el
# unico endpoint que de verdad ejercita el contexto RLS y la policy de
# AUDITORIAS; /api/health solo confirma que el proceso responde, y con eso el
# instalador podia reportar exito aunque el incidente V-02.07 (INSERT
# rechazado por RLS) se hubiera reproducido en produccion.
# V-02.09 (revision PR #33): en instalacion limpia la sonda SQL de
# Test-PostgresPreflight se salta (appsettings.Production.json aun no existe),
# asi que este endpoint es LA verificacion real de RLS: su fallo ahora es
# BLOQUEANTE en vez de un aviso que permite declarar exito falso. Se sondea
# $healthUrl (endpoint interno directo) y no $appUrl, porque en modo reverse
# proxy el proxy externo puede no existir todavia durante la instalacion.
# Se reintenta ~2 minutos: el primer arranque ejecuta las migraciones de EF.
Start-Sleep -Seconds 5
$functionalUrl = $healthUrl -replace '/api/health$', '/api/health/functional'
$functionalOk = $false
$intentosHealth = 24
for ($intentoHealth = 1; $intentoHealth -le $intentosHealth; $intentoHealth++) {
    try {
        $curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
        if ($curl) {
            $statusCode = (& curl.exe -k -s -o NUL -w "%{http_code}" "$functionalUrl" 2>$null)
            $functionalOk = ($LASTEXITCODE -eq 0 -and $statusCode -eq "200")
        } else {
            $health = Invoke-WebRequest -Uri "$functionalUrl" -UseBasicParsing -TimeoutSec 20
            $functionalOk = ($health.StatusCode -eq 200)
        }
    } catch {
        $functionalOk = $false
    }
    if ($functionalOk) { break }
    Write-Host "Intento $intentoHealth de ${intentosHealth}: $functionalUrl todavia no responde HTTP 200 (la primera arranque ejecuta migraciones). Esperando 5 segundos..." -ForegroundColor DarkGray
    Start-Sleep -Seconds 5
}
if (-not $functionalOk) {
    throw "La API arranco pero $functionalUrl no devolvio HTTP 200 tras $intentosHealth intentos (~2 min). El contexto RLS o la policy de AUDITORIAS pueden estar mal desplegados y la instalacion NO se puede dar por buena. Diagnostico: curl.exe -k -v $functionalUrl, revisa el log de la API y repara antes de reinstalar."
}
Write-Host "Health check funcional HTTP 200 en ${functionalUrl}: contexto RLS y policy de AUDITORIAS verificados." -ForegroundColor Green

Write-Host ""
Write-Host "Comprobando cifrado en reposo de los volumenes con datos personales..." -ForegroundColor Cyan
Test-VolumeEncryption -Paths @(
    $InstallPath,
    (Join-Path $InstallPath "backups"),
    (Join-Path $InstallPath "exports"),
    $PostgresDataPath,
    (Join-Path $env:ProgramData "AtlasBalance"))

Write-Host ""
Write-Host "Atlas Balance $AppVersion instalado." -ForegroundColor Green
Write-Host "URL: $appUrl" -ForegroundColor Cyan
Write-Host "Credenciales iniciales (captura esto en tu gestor de passwords):" -ForegroundColor Yellow
Write-Host "  $InstallPath\config\INSTALL_CREDENTIALS_ONCE.txt" -ForegroundColor Yellow
Write-Host "Atajo creado: Atlas Balance" -ForegroundColor Cyan
