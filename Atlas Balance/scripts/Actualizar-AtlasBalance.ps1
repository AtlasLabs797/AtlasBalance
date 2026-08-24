param(
    [string]$InstallPath = "C:\AtlasBalance",
    [switch]$SkipBackup,
    [switch]$PromptForDbOwnerCredentials,
    [string]$DbOwnerUser = ""
)

$ErrorActionPreference = "Stop"
$ApiServiceName = "AtlasBalance.API"
$WatchdogServiceName = "AtlasBalance.Watchdog"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
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

# V-02-06 (RLS-SEC-01): generador local de secretos aleatorios criptograficos.
# Mantenido igual que New-RandomSecret del instalador para no acoplar
# dependencias. La funcion NO imprime ni devuelve el valor por stdout para
# evitar que quede en transcript/logs; el caller debe persistirlo y nunca
# devolverlo al usuario.
function New-RandomSecret {
    param([int]$Length)
    if ($Length -lt 32) { $Length = 32 }
    $bytes = New-Object byte[] $Length
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=').Substring(0, $Length)
}

function Get-RelativePathCompat {
    param([string]$BasePath, [string]$FullPath)

    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $base = $base + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath($FullPath)

    if ($path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) {
        return $path.Substring($base.Length)
    }

    return Split-Path -Leaf $FullPath
}

function Sync-DirectoryPreserveConfig {
    param(
        [string]$Source,
        [string]$Target
    )

    if (-not (Test-Path $Source)) {
        throw "No existe la carpeta origen: $Source"
    }

    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    $sourceFiles = Get-ChildItem -LiteralPath $Source -Recurse -File
    $relativeFiles = New-Object "System.Collections.Generic.HashSet[string]" -ArgumentList ([StringComparer]::OrdinalIgnoreCase)

    foreach ($file in $sourceFiles) {
        $relative = Get-RelativePathCompat -BasePath $Source -FullPath $file.FullName
        [void]$relativeFiles.Add($relative)

        if ($relative -like "appsettings.Production.json" -and (Test-Path (Join-Path $Target $relative))) {
            continue
        }

        $destination = Join-Path $Target $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }

    $targetFiles = Get-ChildItem -LiteralPath $Target -Recurse -File
    foreach ($file in $targetFiles) {
        $relative = Get-RelativePathCompat -BasePath $Target -FullPath $file.FullName
        if ($relativeFiles.Contains($relative)) {
            continue
        }
        if ($relative -like "appsettings*.json" -or $relative -like "logs\*") {
            continue
        }
        Remove-Item -LiteralPath $file.FullName -Force
    }
}

function Parse-ConnectionString {
    param([string]$ConnectionString)

    $map = @{}
    foreach ($part in $ConnectionString.Split(";")) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part.IndexOf("=") -lt 0) {
            continue
        }

        $key = $part.Substring(0, $part.IndexOf("=")).Trim().ToLowerInvariant()
        $value = $part.Substring($part.IndexOf("=") + 1).Trim()
        $map[$key] = $value
    }

    return [ordered]@{
        Host = if ($map.ContainsKey("host")) { $map["host"] } else { "localhost" }
        Port = if ($map.ContainsKey("port")) { $map["port"] } else { "5432" }
        Database = if ($map.ContainsKey("database")) { $map["database"] } else { "atlas_balance" }
        Username = if ($map.ContainsKey("username")) { $map["username"] } elseif ($map.ContainsKey("user id")) { $map["user id"] } else { "atlas_balance_app" }
        Password = if ($map.ContainsKey("password")) { $map["password"] } else { "" }
        ApplicationName = if ($map.ContainsKey("application name")) { $map["application name"] } else { "" }
        MaximumPoolSize = if ($map.ContainsKey("maximum pool size")) { $map["maximum pool size"] } else { "" }
        MinimumPoolSize = if ($map.ContainsKey("minimum pool size")) { $map["minimum pool size"] } else { "" }
    }
}

function Get-ConfigValue {
    param([object]$Object, [string]$Name)

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($property) {
        return $property.Value
    }

    return $null
}

function Get-EnvironmentValue {
    param([string[]]$Names)

    foreach ($target in @(
        [EnvironmentVariableTarget]::Process,
        [EnvironmentVariableTarget]::User,
        [EnvironmentVariableTarget]::Machine
    )) {
        foreach ($name in $Names) {
            $value = [Environment]::GetEnvironmentVariable($name, $target)
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }

    return ""
}

function Read-InstallCredentialValue {
    param([string]$InstallPath, [string]$LabelPrefix)

    $credentialsPath = Join-Path (Join-Path $InstallPath "config") "INSTALL_CREDENTIALS_ONCE.txt"
    if (-not (Test-Path -LiteralPath $credentialsPath)) {
        return ""
    }

    foreach ($line in Get-Content -LiteralPath $credentialsPath -ErrorAction SilentlyContinue) {
        if ($line -like "$LabelPrefix*") {
            $separator = $line.IndexOf(":")
            if ($separator -ge 0) {
                return $line.Substring($separator + 1).Trim()
            }
        }
    }

    return ""
}

function New-OwnerConnection {
    param(
        [object]$BaseConnection,
        [object]$WatchdogSettings,
        [string]$OwnerUser,
        [string]$OwnerPassword,
        [string]$Source
    )

    $dbHost = Get-ConfigValue -Object $WatchdogSettings -Name "DbHost"
    $dbPort = Get-ConfigValue -Object $WatchdogSettings -Name "DbPort"
    $dbName = Get-ConfigValue -Object $WatchdogSettings -Name "DbName"

    return [ordered]@{
        Host = if ([string]::IsNullOrWhiteSpace([string]$dbHost)) { $BaseConnection.Host } else { [string]$dbHost }
        Port = if ([string]::IsNullOrWhiteSpace([string]$dbPort)) { $BaseConnection.Port } else { [string]$dbPort }
        Database = if ([string]::IsNullOrWhiteSpace([string]$dbName)) { $BaseConnection.Database } else { [string]$dbName }
        Username = [string]$OwnerUser
        Password = [string]$OwnerPassword
        Source = $Source
    }
}

function Get-ExplicitOwnerCredentials {
    $ownerUser = $DbOwnerUser
    if ([string]::IsNullOrWhiteSpace($ownerUser)) {
        $ownerUser = Get-EnvironmentValue -Names @("ATLAS_DB_OWNER_USER", "ATLAS_BALANCE_DB_OWNER_USER")
    }

    $ownerPassword = Get-EnvironmentValue -Names @("ATLAS_DB_OWNER_PASSWORD", "ATLAS_BALANCE_DB_OWNER_PASSWORD")
    if (-not [string]::IsNullOrWhiteSpace($ownerUser) -and
        -not [string]::IsNullOrWhiteSpace($ownerPassword)) {
        return [ordered]@{
            Username = [string]$ownerUser
            Password = [string]$ownerPassword
            Source = "param/env owner credentials"
        }
    }

    return $null
}

function Get-InstallFileOwnerCredentials {
    param([string]$InstallPath)

    $ownerUser = Read-InstallCredentialValue -InstallPath $InstallPath -LabelPrefix "Usuario DB migraci"
    $ownerPassword = Read-InstallCredentialValue -InstallPath $InstallPath -LabelPrefix "Password DB migraci"
    if (-not [string]::IsNullOrWhiteSpace($ownerUser) -and
        -not [string]::IsNullOrWhiteSpace($ownerPassword)) {
        return [ordered]@{
            Username = [string]$ownerUser
            Password = [string]$ownerPassword
            Source = "INSTALL_CREDENTIALS_ONCE.txt"
        }
    }

    return $null
}

function Request-OwnerCredentials {
    if (-not $PromptForDbOwnerCredentials) {
        return $null
    }

    $ownerUser = $DbOwnerUser
    if ([string]::IsNullOrWhiteSpace($ownerUser)) {
        $enteredUser = Read-Host "Usuario PostgreSQL owner/migracion [atlas_balance_owner]"
        $ownerUser = if ([string]::IsNullOrWhiteSpace($enteredUser)) { "atlas_balance_owner" } else { $enteredUser.Trim() }
    }

    $ownerPassword = Convert-SecureStringToPlain (Read-Host "Password PostgreSQL owner/migracion" -AsSecureString)
    if (-not [string]::IsNullOrWhiteSpace($ownerUser) -and
        -not [string]::IsNullOrWhiteSpace($ownerPassword)) {
        return [ordered]@{
            Username = [string]$ownerUser
            Password = [string]$ownerPassword
            Source = "interactive owner credentials"
        }
    }

    return $null
}

function Resolve-BackupConnection {
    param(
        [object]$ApiConfig,
        [object]$WatchdogConfig,
        [string]$InstallPath
    )

    $connectionStrings = Get-ConfigValue -Object $ApiConfig -Name "ConnectionStrings"
    $migrationConnection = Get-ConfigValue -Object $connectionStrings -Name "MigrationConnection"
    if (-not [string]::IsNullOrWhiteSpace([string]$migrationConnection)) {
        $connection = Parse-ConnectionString -ConnectionString ([string]$migrationConnection)
        $connection["Source"] = "MigrationConnection"
        return $connection
    }

    $environmentMigrationConnection = Get-EnvironmentValue -Names @("ATLAS_DB_MIGRATION_CONNECTION", "ATLAS_BALANCE_MIGRATION_CONNECTION")
    if (-not [string]::IsNullOrWhiteSpace([string]$environmentMigrationConnection)) {
        $connection = Parse-ConnectionString -ConnectionString ([string]$environmentMigrationConnection)
        $connection["Source"] = "environment MigrationConnection"
        return $connection
    }

    $defaultConnectionRaw = Get-ConfigValue -Object $connectionStrings -Name "DefaultConnection"
    if ([string]::IsNullOrWhiteSpace([string]$defaultConnectionRaw)) {
        throw "appsettings.Production.json no contiene ConnectionStrings:DefaultConnection."
    }

    $defaultConnection = Parse-ConnectionString -ConnectionString ([string]$defaultConnectionRaw)
    $watchdogSettings = Get-ConfigValue -Object $WatchdogConfig -Name "WatchdogSettings"

    $explicitOwner = Get-ExplicitOwnerCredentials
    if ($null -ne $explicitOwner) {
        return New-OwnerConnection `
            -BaseConnection $defaultConnection `
            -WatchdogSettings $watchdogSettings `
            -OwnerUser $explicitOwner.Username `
            -OwnerPassword $explicitOwner.Password `
            -Source $explicitOwner.Source
    }

    $ownerUser = Get-ConfigValue -Object $watchdogSettings -Name "DbOwnerUser"
    $ownerPassword = Get-ConfigValue -Object $watchdogSettings -Name "DbOwnerPassword"

    if (-not [string]::IsNullOrWhiteSpace([string]$ownerUser) -and
        -not [string]::IsNullOrWhiteSpace([string]$ownerPassword)) {
        return New-OwnerConnection `
            -BaseConnection $defaultConnection `
            -WatchdogSettings $watchdogSettings `
            -OwnerUser ([string]$ownerUser) `
            -OwnerPassword ([string]$ownerPassword) `
            -Source "WatchdogSettings.DbOwnerUser"
    }

    $installFileOwner = Get-InstallFileOwnerCredentials -InstallPath $InstallPath
    if ($null -ne $installFileOwner) {
        return New-OwnerConnection `
            -BaseConnection $defaultConnection `
            -WatchdogSettings $watchdogSettings `
            -OwnerUser $installFileOwner.Username `
            -OwnerPassword $installFileOwner.Password `
            -Source $installFileOwner.Source
    }

    $promptOwner = Request-OwnerCredentials
    if ($null -ne $promptOwner) {
        return New-OwnerConnection `
            -BaseConnection $defaultConnection `
            -WatchdogSettings $watchdogSettings `
            -OwnerUser $promptOwner.Username `
            -OwnerPassword $promptOwner.Password `
            -Source $promptOwner.Source
    }

    $defaultConnection["Source"] = "DefaultConnection"
    return $defaultConnection
}

# V-02-06 (BACKUP-02): devuelve una cadena de conexion owner completa o vacia,
# reutilizando la misma cascada que Resolve-BackupConnection. Solo se usa para
# rellenar appsettings.Production.json cuando el operador nunca configuro
# ConnectionStrings:MigrationConnection. No imprime credenciales.
function Resolve-MigrationConnectionForConfig {
    param([string]$ApiConfigPath)

    if (-not (Test-Path -LiteralPath $ApiConfigPath)) {
        return ""
    }

    $apiConfig = Read-JsonFile -Path $ApiConfigPath
    $watchdogPath = Join-Path (Split-Path -Parent $ApiConfigPath) "..\watchdog\appsettings.Production.json"
    $watchdogPath = [IO.Path]::GetFullPath($watchdogPath)
    $watchdogConfig = if (Test-Path -LiteralPath $watchdogPath) { Read-JsonFile -Path $watchdogPath } else { [pscustomobject]@{} }
    $installPath = Split-Path -Parent (Split-Path -Parent $ApiConfigPath)

    try {
        $ownerConn = Resolve-BackupConnection `
            -ApiConfig $apiConfig `
            -WatchdogConfig $watchdogConfig `
            -InstallPath $installPath
    } catch {
        return ""
    }

    if (-not $ownerConn -or $ownerConn.Source -eq "DefaultConnection") {
        return ""
    }

    $host = [string]$ownerConn.Host
    $port = if ([int]$ownerConn.Port -gt 0) { [int]$ownerConn.Port } else { 5432 }
    $database = [string]$ownerConn.Database
    $user = [string]$ownerConn.Username
    $password = [string]$ownerConn.Password
    if ([string]::IsNullOrWhiteSpace($user) -or [string]::IsNullOrWhiteSpace($password) -or [string]::IsNullOrWhiteSpace($database)) {
        return ""
    }

    # sslmode=require si el host no es localhost/127.0.0.1, igual que el
    # instalador. Mantener consistencia para que la API y el actualizador
    # usen el mismo modo SSL.
    $sslMode = if ($host -eq "localhost" -or $host -eq "127.0.0.1") { "" } else { ";sslmode=require" }
    $applicationName = if ([string]::IsNullOrWhiteSpace($ownerConn.ApplicationName)) { "AtlasBalance.Migrate" } else { $ownerConn.ApplicationName }
    $maximumPoolSize = if ([string]::IsNullOrWhiteSpace($ownerConn.MaximumPoolSize)) { "4" } else { $ownerConn.MaximumPoolSize }
    $minimumPoolSize = if ([string]::IsNullOrWhiteSpace($ownerConn.MinimumPoolSize)) { "0" } else { $ownerConn.MinimumPoolSize }
    return "Host=$host;Port=$port;Database=$database;Username=$user;Password=$password$sslMode;Application Name=$applicationName;Maximum Pool Size=$maximumPoolSize;Minimum Pool Size=$minimumPoolSize"
}

function Find-PostgresDump {
    param([string]$ConfiguredBinPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredBinPath)) {
        $candidate = Join-Path $ConfiguredBinPath "pg_dump.exe"
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $command = Get-Command "pg_dump.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($candidateBin in @(
        "C:\Program Files\PostgreSQL\18\bin",
        "C:\Program Files\PostgreSQL\17\bin",
        "C:\Program Files\PostgreSQL\16\bin",
        "C:\Program Files\PostgreSQL\15\bin",
        "C:\Program Files\PostgreSQL\16\bin"
    )) {
        $candidate = Join-Path $candidateBin "pg_dump.exe"
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return ""
}

function Stop-ServiceIfExists {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Stopped") {
        Stop-Service -Name $Name -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(45))
    }
}

function Start-ServiceIfExists {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Running") {
        Start-Service -Name $Name
        $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(45))
    }
}

function Set-ServiceBinaryPathIfExists {
    param(
        [string]$Name,
        [string]$ExePath
    )

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $service) {
        return
    }

    $quotedPath = '"' + $ExePath + '"'
    $result = & sc.exe config $Name binPath= $quotedPath
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo actualizar la ruta binaria del servicio $Name. sc.exe devolvio $LASTEXITCODE. $result"
    }
}

function Read-RuntimeConfig {
    param([string]$BasePath)

    $runtimePath = Join-Path $BasePath "atlas-balance.runtime.json"
    if (-not (Test-Path $runtimePath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Start-ManagedPostgresIfNeeded {
    param([object]$Runtime)

    if (-not $Runtime -or
        -not $Runtime.ManagedPostgres -or
        [string]::IsNullOrWhiteSpace([string]$Runtime.PostgresServiceName)) {
        return
    }

    Start-ServiceIfExists -Name ([string]$Runtime.PostgresServiceName)
    Start-Sleep -Seconds 2
}

function Backup-Database {
    param(
        [object]$ApiConfig,
        [object]$WatchdogConfig,
        [string]$Version
    )

    $connection = Resolve-BackupConnection -ApiConfig $ApiConfig -WatchdogConfig $WatchdogConfig -InstallPath $InstallPath
    $pgDump = Find-PostgresDump -ConfiguredBinPath $WatchdogConfig.WatchdogSettings.PostgresBinPath
    if ([string]::IsNullOrWhiteSpace($pgDump)) {
        throw "No se encontro pg_dump.exe. No actualizo sin backup."
    }

    $backupDir = Join-Path $InstallPath "backups"
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    $safeVersion = $Version.Replace(":", "-").Replace("/", "-").Replace("\", "-")
    $backupPath = Join-Path $backupDir ("pre_update_{0}_{1}.dump" -f $safeVersion, (Get-Date -Format "yyyyMMdd_HHmmss"))

    $previousPassword = $env:PGPASSWORD
    $env:PGPASSWORD = $connection.Password
    try {
        & $pgDump `
            "-h" $connection.Host `
            "-p" $connection.Port `
            "-U" $connection.Username `
            "-F" "c" `
            "-b" `
            "-v" `
            "-f" $backupPath `
            $connection.Database

        if ($LASTEXITCODE -ne 0) {
            if ($connection.Source -eq "DefaultConnection") {
                throw "pg_dump devolvio codigo $LASTEXITCODE. No hay MigrationConnection ni credenciales owner/migracion disponibles. Ejecuta update.cmd -PromptForDbOwnerCredentials o define ATLAS_DB_MIGRATION_CONNECTION antes de actualizar. El usuario runtime puede quedar bloqueado por RLS y no sirve para un backup completo."
            }

            throw "pg_dump devolvio codigo $LASTEXITCODE usando $($connection.Source)"
        }
    } finally {
        $env:PGPASSWORD = $previousPassword
    }

    return $backupPath
}

function Read-JsonFile {
    param([string]$Path)
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Write-JsonFile {
    param([object]$Value, [string]$Path)

    $json = $Value | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

function Ensure-JsonObjectProperty {
    param([object]$Object, [string]$Name)

    if (-not ($Object.PSObject.Properties.Name -contains $Name) -or $null -eq $Object.$Name) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue ([pscustomobject]@{}) -Force
    }
}

function Set-JsonDefault {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Value,
        [switch]$ReplaceBlank
    )

    $hasProperty = $Object.PSObject.Properties.Name -contains $Name
    if (-not $hasProperty) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
        return $true
    }

    if ($ReplaceBlank -and $Object.$Name -is [string] -and [string]::IsNullOrWhiteSpace([string]$Object.$Name)) {
        $Object.$Name = $Value
        return $true
    }

    return $false
}

function Get-PackagedReleasePublicKey {
    param([string]$ApiSource)

    $templatePath = Join-Path $ApiSource "appsettings.Production.json.template"
    if (-not (Test-Path -LiteralPath $templatePath)) {
        return ""
    }

    try {
        $template = Read-JsonFile -Path $templatePath
        return [string]$template.UpdateSecurity.ReleaseSigningPublicKeyPem
    } catch {
        return ""
    }
}

function Update-ProductionConfigDefaults {
    param(
        [string]$ApiConfigPath,
        [string]$WatchdogConfigPath,
        [string]$ApiSource,
        [string]$InstallPath,
        [object]$Runtime
    )

    $changed = $false
    $apiConfig = Read-JsonFile -Path $ApiConfigPath
    Ensure-JsonObjectProperty -Object $apiConfig -Name "Security"
    Ensure-JsonObjectProperty -Object $apiConfig -Name "App"
    Ensure-JsonObjectProperty -Object $apiConfig -Name "Ia"
    Ensure-JsonObjectProperty -Object $apiConfig -Name "ForwardedHeaders"
    Ensure-JsonObjectProperty -Object $apiConfig -Name "WatchdogSettings"
    Ensure-JsonObjectProperty -Object $apiConfig -Name "GitHubSettings"
    Ensure-JsonObjectProperty -Object $apiConfig -Name "UpdateSecurity"
    Ensure-JsonObjectProperty -Object $apiConfig -Name "DataProtection"

    # V-02-06 (BACKUP-02): persistir ConnectionStrings:MigrationConnection
    # cuando el operador la dejo vacia en una instalacion legacy. Solo se
    # rellena si se ha podido resolver un owner por las vias ya existentes
    # (MigrationConnection > env > INSTALL_CREDENTIALS_ONCE > prompt > runtime).
    # La cadena nunca se imprime; se escribe atomica y protege via la ACL
    # que el instalador ya aplica al appsettings de produccion.
    $ownerResolved = Resolve-MigrationConnectionForConfig -ApiConfigPath $ApiConfigPath
    Ensure-JsonObjectProperty -Object $apiConfig -Name "ConnectionStrings"
    Ensure-JsonObjectProperty -Object $apiConfig.ConnectionStrings -Name "MigrationConnection"
    $existingMigration = [string]$apiConfig.ConnectionStrings.MigrationConnection
    if ([string]::IsNullOrWhiteSpace($existingMigration) -and -not [string]::IsNullOrWhiteSpace($ownerResolved)) {
        $apiConfig.ConnectionStrings.MigrationConnection = $ownerResolved
        $changed = $true
        Write-Host "ConnectionStrings:MigrationConnection regenerado en appsettings.Production.json (no se imprime)." -ForegroundColor Cyan
    }

    $useReverseProxy = $Runtime -and $Runtime.UseReverseProxy
    $apiPort = if ($Runtime -and $Runtime.ApiPort) { [int]$Runtime.ApiPort } else { 443 }
    $internalApiPort = if ($Runtime -and $Runtime.InternalApiPort) { [int]$Runtime.InternalApiPort } else { 5000 }
    $apiHealthUrl = if ($useReverseProxy) { "http://localhost:$internalApiPort/api/health" } elseif ($apiPort -eq 443) { "https://localhost/api/health" } else { "https://localhost`:$apiPort/api/health" }
    $appBaseUrl = if ($Runtime -and $Runtime.AppUrl) { [string]$Runtime.AppUrl } else { "" }
    $publicKey = Get-PackagedReleasePublicKey -ApiSource $ApiSource

    $changed = (Set-JsonDefault -Object $apiConfig.Security -Name "RequireMfaForWebUsers" -Value $true) -or $changed

    # V-02-06 (RLS-SEC-01): persistir Security:RlsContextSecret si la
    # instalacion viene de versiones anteriores que no lo generaban. Solo se
    # escribe cuando esta vacio; nunca se rota ni se imprime. El valor se
    # queda dentro del mismo appsettings al que ya tienen acceso Administradores
    # y SYSTEM. Mantenemos el JWT y el secreto RLS siempre distintos.
    $rlsHasProperty = $apiConfig.Security.PSObject.Properties.Name -contains "RlsContextSecret"
    $rlsCurrent = if ($rlsHasProperty) { [string]$apiConfig.Security.RlsContextSecret } else { "" }
    $jwtCurrent = [string]$apiConfig.JwtSettings.Secret
    $needsRls = [string]::IsNullOrWhiteSpace($rlsCurrent) `
        -or $rlsCurrent -eq $jwtCurrent `
        -or $rlsCurrent.Length -lt 32
    if ($needsRls -and -not [string]::IsNullOrWhiteSpace($jwtCurrent)) {
        $newSecret = New-RandomSecret 64
        if ($rlsHasProperty) {
            $apiConfig.Security.RlsContextSecret = $newSecret
        } else {
            $apiConfig.Security | Add-Member -NotePropertyName "RlsContextSecret" -NotePropertyValue $newSecret -Force
        }
        $changed = $true
        Write-Host "Security:RlsContextSecret generado y persistido en appsettings.Production.json (no se imprime)." -ForegroundColor Cyan
    } elseif (-not $needsRls) {
        # Asegurar que la clave existe aun cuando ya estaba rellenada (defensivo
        # para upgrades que borraron la entrada manualmente).
        if (-not $rlsHasProperty) {
            $newSecret = New-RandomSecret 64
            $apiConfig.Security | Add-Member -NotePropertyName "RlsContextSecret" -NotePropertyValue $newSecret -Force
            $changed = $true
        }
    }
    # V-02.07: misma politica para Security:AuditSigningKey, la clave con la que
    # se firma cada fila de AUDITORIAS. Solo se genera si falta o es debil; NUNCA
    # se rota en una actualizacion, porque rotarla invalidaria la verificacion de
    # todas las filas ya firmadas y /api/auditoria/integridad las reportaria como
    # no verificables. Tambien se exige distinta de JWT y de RLS: si coincidiera,
    # comprometer cualquiera de las dos permitiria forjar auditoria.
    $auditHasProperty = $apiConfig.Security.PSObject.Properties.Name -contains "AuditSigningKey"
    $auditCurrent = if ($auditHasProperty) { [string]$apiConfig.Security.AuditSigningKey } else { "" }
    $rlsEffective = [string]$apiConfig.Security.RlsContextSecret
    $needsAuditKey = [string]::IsNullOrWhiteSpace($auditCurrent) `
        -or $auditCurrent -eq $jwtCurrent `
        -or $auditCurrent -eq $rlsEffective `
        -or $auditCurrent.Length -lt 32
    if ($needsAuditKey) {
        $newAuditKey = New-RandomSecret 64
        if ($auditHasProperty) {
            $apiConfig.Security.AuditSigningKey = $newAuditKey
        } else {
            $apiConfig.Security | Add-Member -NotePropertyName "AuditSigningKey" -NotePropertyValue $newAuditKey -Force
        }
        $changed = $true
        Write-Host "Security:AuditSigningKey generado y persistido en appsettings.Production.json (no se imprime)." -ForegroundColor Cyan
        Write-Host "  Las filas de AUDITORIAS anteriores a esta actualizacion no llevan firma: se reportan como 'sin firma', no como manipuladas." -ForegroundColor DarkGray
    }

    $changed = (Set-JsonDefault -Object $apiConfig.Security -Name "MirrorToWindowsEventLog" -Value $true) -or $changed

    if (-not [string]::IsNullOrWhiteSpace($appBaseUrl)) {
        $changed = (Set-JsonDefault -Object $apiConfig.App -Name "BaseUrl" -Value $appBaseUrl) -or $changed
    }
    $changed = (Set-JsonDefault -Object $apiConfig.Ia -Name "UseSystemProxy" -Value $false) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.Ia -Name "ProxyUrl" -Value "") -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.ForwardedHeaders -Name "KnownProxies" -Value @()) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.ForwardedHeaders -Name "KnownNetworks" -Value @()) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.WatchdogSettings -Name "UpdateSourceRoot" -Value (Join-Path $InstallPath "updates")) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.WatchdogSettings -Name "UpdateInstallPath" -Value $InstallPath) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.WatchdogSettings -Name "UpdateTargetPath" -Value (Join-Path $InstallPath "api")) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.WatchdogSettings -Name "RequireDatabaseBackupBeforeUpdate" -Value $true) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.WatchdogSettings -Name "RequireHealthCheckAfterUpdate" -Value $true) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.WatchdogSettings -Name "ApiHealthUrl" -Value $apiHealthUrl) -or $changed
    $changed = (Set-JsonDefault -Object $apiConfig.GitHubSettings -Name "UpdateToken" -Value "") -or $changed
    if (-not [string]::IsNullOrWhiteSpace($publicKey)) {
        $changed = (Set-JsonDefault -Object $apiConfig.UpdateSecurity -Name "ReleaseSigningPublicKeyPem" -Value $publicKey -ReplaceBlank) -or $changed
    }
    $changed = (Set-JsonDefault -Object $apiConfig.DataProtection -Name "KeysPath" -Value "C:\ProgramData\AtlasBalance\keys") -or $changed

    if ($changed) {
        Write-JsonFile -Value $apiConfig -Path $ApiConfigPath
        Write-Host "Config API actualizada con claves no secretas faltantes." -ForegroundColor Cyan
    }

    if (Test-Path -LiteralPath $WatchdogConfigPath) {
        $watchdogChanged = $false
        $watchdogConfig = Read-JsonFile -Path $WatchdogConfigPath
        Ensure-JsonObjectProperty -Object $watchdogConfig -Name "WatchdogSettings"
        $watchdogChanged = (Set-JsonDefault -Object $watchdogConfig.WatchdogSettings -Name "UpdateSourceRoot" -Value (Join-Path $InstallPath "updates")) -or $watchdogChanged
        $watchdogChanged = (Set-JsonDefault -Object $watchdogConfig.WatchdogSettings -Name "UpdateInstallPath" -Value $InstallPath) -or $watchdogChanged
        $watchdogChanged = (Set-JsonDefault -Object $watchdogConfig.WatchdogSettings -Name "UpdateTargetPath" -Value (Join-Path $InstallPath "api")) -or $watchdogChanged
        $watchdogChanged = (Set-JsonDefault -Object $watchdogConfig.WatchdogSettings -Name "RequireDatabaseBackupBeforeUpdate" -Value $true) -or $watchdogChanged
        $watchdogChanged = (Set-JsonDefault -Object $watchdogConfig.WatchdogSettings -Name "RequireHealthCheckAfterUpdate" -Value $true) -or $watchdogChanged
        $watchdogChanged = (Set-JsonDefault -Object $watchdogConfig.WatchdogSettings -Name "ApiHealthUrl" -Value $apiHealthUrl) -or $watchdogChanged
        if ($watchdogChanged) {
            Write-JsonFile -Value $watchdogConfig -Path $WatchdogConfigPath
            Write-Host "Config Watchdog actualizada con claves no secretas faltantes." -ForegroundColor Cyan
        }
    }
}

function Restore-UpdatedBinaries {
    param(
        [string]$RollbackRoot,
        [string]$InstallPath,
        [string]$ApiTarget,
        [string]$WatchdogTarget
    )

    # V-02.08 (fix): copia con reintento. Antes, un DLL bloqueado durante el
    # borrado dejaba una mezcla viejo/nuevo silenciosa (SilentlyContinue) y
    # el propio rollback restauraba a medias.
    function Copy-RollbackTree {
        param([string]$From, [string]$To)

        for ($attempt = 1; $attempt -le 2; $attempt++) {
            Remove-Item -LiteralPath $To -Recurse -Force -ErrorAction SilentlyContinue
            try {
                Copy-Item -LiteralPath $From -Destination $To -Recurse -Force -ErrorAction Stop
                return
            } catch {
                if ($attempt -eq 2) { throw }
                Write-Warning "Copia de rollback bloqueada ($($_.Exception.Message)); reintento en 3 segundos."
                Start-Sleep -Seconds 3
            }
        }
    }

    Write-Warning "Health check fallido. Restaurando binarios anteriores desde $RollbackRoot."
    Stop-ServiceIfExists -Name $ApiServiceName
    Stop-ServiceIfExists -Name $WatchdogServiceName

    if (Test-Path -LiteralPath (Join-Path $RollbackRoot "api")) {
        Copy-RollbackTree -From (Join-Path $RollbackRoot "api") -To $ApiTarget
        if (-not (Test-Path (Join-Path $ApiTarget "AtlasBalance.API.exe"))) {
            throw "El rollback no restauro AtlasBalance.API.exe; revisa $ApiTarget manualmente desde $RollbackRoot."
        }
    }
    if (Test-Path -LiteralPath (Join-Path $RollbackRoot "watchdog")) {
        Copy-RollbackTree -From (Join-Path $RollbackRoot "watchdog") -To $WatchdogTarget
        if (-not (Test-Path (Join-Path $WatchdogTarget "AtlasBalance.Watchdog.exe"))) {
            throw "El rollback no restauro AtlasBalance.Watchdog.exe; revisa $WatchdogTarget manualmente desde $RollbackRoot."
        }
    }
    Copy-IfExists -Source (Join-Path $RollbackRoot "VERSION") -Destination (Join-Path $InstallPath "VERSION")
    Copy-IfExists -Source (Join-Path $RollbackRoot "atlas-balance.runtime.json") -Destination (Join-Path $InstallPath "atlas-balance.runtime.json")

    Set-ServiceBinaryPathIfExists -Name $WatchdogServiceName -ExePath (Join-Path $WatchdogTarget "AtlasBalance.Watchdog.exe")
    Set-ServiceBinaryPathIfExists -Name $ApiServiceName -ExePath (Join-Path $ApiTarget "AtlasBalance.API.exe")
    Start-ServiceIfExists -Name $WatchdogServiceName
    Start-ServiceIfExists -Name $ApiServiceName
}

function Read-PackageVersion {
    param([string]$PackageRoot)

    $versionPath = Join-Path $PackageRoot "VERSION"
    if (Test-Path $versionPath) {
        return (Get-Content -LiteralPath $versionPath -Raw).Trim()
    }
    return "desconocida"
}

function Copy-IfExists {
    param([string]$Source, [string]$Destination)

    if (Test-Path $Source) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}

$packageRoot = Split-Path -Parent $PSScriptRoot
$apiSource = Join-Path $packageRoot "api"
$watchdogSource = Join-Path $packageRoot "watchdog"
$apiTarget = Join-Path $InstallPath "api"
$watchdogTarget = Join-Path $InstallPath "watchdog"

if (-not (Test-Path (Join-Path $apiSource "AtlasBalance.API.exe")) -or
    -not (Test-Path (Join-Path $watchdogSource "AtlasBalance.Watchdog.exe"))) {
    throw "Esta carpeta no es el paquete de actualizacion. Ejecuta update.cmd desde la carpeta descomprimida que contiene api\AtlasBalance.API.exe, watchdog\AtlasBalance.Watchdog.exe y scripts."
}
if (-not (Test-Path (Join-Path $apiTarget "appsettings.Production.json"))) {
    throw "No se encontro una instalacion existente en $InstallPath."
}

if (-not (Test-IsAdmin)) {
    throw "Ejecuta este actualizador como Administrador."
}

# Repara tambien instalaciones anteriores: backups y exportaciones contienen
# datos financieros y PII, y no deben heredar lectura para usuarios locales.
Protect-RestrictedDirectory -Path (Join-Path $InstallPath "backups")
Protect-RestrictedDirectory -Path (Join-Path $InstallPath "exports")

$newVersion = Read-PackageVersion -PackageRoot $packageRoot
$runtime = Read-RuntimeConfig -BasePath $InstallPath
$previousVersion = if ($runtime -and $runtime.Version) { [string]$runtime.Version } elseif (Test-Path (Join-Path $InstallPath "VERSION")) { (Get-Content -LiteralPath (Join-Path $InstallPath "VERSION") -Raw).Trim() } else { "desconocida" }
Start-ManagedPostgresIfNeeded -Runtime $runtime
$apiConfig = Read-JsonFile -Path (Join-Path $apiTarget "appsettings.Production.json")
$watchdogConfigPath = Join-Path $watchdogTarget "appsettings.Production.json"
$watchdogConfig = if (Test-Path $watchdogConfigPath) { Read-JsonFile -Path $watchdogConfigPath } else { [pscustomobject]@{ WatchdogSettings = [pscustomobject]@{ PostgresBinPath = "" } } }

if (-not $SkipBackup) {
    $backupPath = Backup-Database -ApiConfig $apiConfig -WatchdogConfig $watchdogConfig -Version $newVersion
    Write-Host "Backup previo creado: $backupPath" -ForegroundColor Green
} else {
    Write-Host "Actualizacion sin backup por -SkipBackup. Mala idea salvo que ya tengas uno reciente." -ForegroundColor Yellow
}

Stop-ServiceIfExists -Name $ApiServiceName
Stop-ServiceIfExists -Name $WatchdogServiceName

$rollbackRoot = Join-Path (Join-Path $InstallPath "backups") ("app_before_update_{0}" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Path $rollbackRoot -Force | Out-Null
Copy-Item -LiteralPath $apiTarget -Destination (Join-Path $rollbackRoot "api") -Recurse -Force
Copy-Item -LiteralPath $watchdogTarget -Destination (Join-Path $rollbackRoot "watchdog") -Recurse -Force
Copy-IfExists -Source (Join-Path $InstallPath "VERSION") -Destination (Join-Path $rollbackRoot "VERSION")
Copy-IfExists -Source (Join-Path $InstallPath "atlas-balance.runtime.json") -Destination (Join-Path $rollbackRoot "atlas-balance.runtime.json")

# V-02.08 (fix): antes solo un healthcheck negativo disparaba rollback;
# cualquier excepcion entre la copia y el healthcheck (JSON de config,
# sc.exe, Copy-Item bloqueado por AV...) moria con EAP=Stop dejando
# servicios parados, VERSION ya escrita y binarios a medias, sin restaurar.
$updateSucceeded = $false
try {
    Sync-DirectoryPreserveConfig -Source $apiSource -Target $apiTarget
    Sync-DirectoryPreserveConfig -Source $watchdogSource -Target $watchdogTarget
    Update-ProductionConfigDefaults `
        -ApiConfigPath (Join-Path $apiTarget "appsettings.Production.json") `
        -WatchdogConfigPath (Join-Path $watchdogTarget "appsettings.Production.json") `
        -ApiSource $apiSource `
        -InstallPath $InstallPath `
        -Runtime $runtime

    Set-ServiceBinaryPathIfExists -Name $WatchdogServiceName -ExePath (Join-Path $watchdogTarget "AtlasBalance.Watchdog.exe")
    Set-ServiceBinaryPathIfExists -Name $ApiServiceName -ExePath (Join-Path $apiTarget "AtlasBalance.API.exe")

    Set-Content -LiteralPath (Join-Path $InstallPath "VERSION") -Value $newVersion -Encoding UTF8

$installScriptsPath = Join-Path $InstallPath "scripts"
New-Item -ItemType Directory -Path $installScriptsPath -Force | Out-Null
foreach ($script in @(
    "Actualizar-AtlasBalance.ps1",
    "Instalar-AtlasBalance.ps1",
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
    "Sync-AtlasDirectory.ps1",
    "Sync-AtlasDirectory.Tests.ps1",
    "install-cert-client.ps1",
    "install.ps1",
    "start.ps1",
    "uninstall-services.ps1",
    "uninstall.ps1",
    "update.ps1"
)) {
    Copy-IfExists -Source (Join-Path $packageRoot "scripts\$script") -Destination (Join-Path $installScriptsPath $script)
}

foreach ($cmd in @(
    "Atlas Balance.cmd",
    "Actualizar Atlas Balance.cmd",
    "Instalar Atlas Balance.cmd",
    "install.cmd",
    "start.cmd",
    "uninstall.cmd",
    "update.cmd"
)) {
    Copy-IfExists -Source (Join-Path $packageRoot $cmd) -Destination (Join-Path $InstallPath $cmd)
}

$runtimePath = Join-Path $InstallPath "atlas-balance.runtime.json"
if ($runtime) {
    $runtime.Version = $newVersion
    if (-not ($runtime.PSObject.Properties.Name -contains "PreviousVersion")) {
        $runtime | Add-Member -NotePropertyName "PreviousVersion" -NotePropertyValue $previousVersion
    } else {
        $runtime.PreviousVersion = $previousVersion
    }
    if (-not ($runtime.PSObject.Properties.Name -contains "UpdatedAt")) {
        $runtime | Add-Member -NotePropertyName "UpdatedAt" -NotePropertyValue (Get-Date).ToString("o")
    } else {
        $runtime.UpdatedAt = (Get-Date).ToString("o")
    }
    Write-JsonFile -Value $runtime -Path $runtimePath
}

Start-ServiceIfExists -Name $WatchdogServiceName
Start-ServiceIfExists -Name $ApiServiceName
$updateSucceeded = $true
} catch {
    Write-Warning "La actualizacion fallo antes del health check: $($_.Exception.Message)"
    Restore-UpdatedBinaries -RollbackRoot $rollbackRoot -InstallPath $InstallPath -ApiTarget $apiTarget -WatchdogTarget $watchdogTarget
    throw "La actualizacion fallo y se restauraron los binarios anteriores desde $rollbackRoot. Causa: $($_.Exception.Message)"
}

$appUrl = if ($runtime -and $runtime.AppUrl) { [string]$runtime.AppUrl } else { "" }
if ([string]::IsNullOrWhiteSpace($appUrl)) {
    $appUrl = "https://localhost"
}

Start-Sleep -Seconds 5
$healthOk = $false
# V-02.08: el incidente V-02.07 demostro que /api/health devuelve 200 con
# login 500. Si el actualizador se conforma con eso, declara OK un sistema
# roto. /api/health/functional verifica que el contexto RLS esta firmado
# y que un INSERT firmado en AUDITORIAS funciona con el rol runtime. Si
# falla, el actualizador hace rollback del DLL.
$healthPath = "/api/health/functional"
# V-02.08 (fix): redirigir stderr de un nativo bajo EAP=Stop convierte cada
# linea en ErrorRecord terminating; un warning de TLS/proxy mataba el script
# fuera de todo try/catch. Se baja EAP solo para la llamada nativa, como ya
# hace Test-BackupRestore.ps1.
$curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
if ($curl) {
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $statusCode = (& curl.exe -k -s -o NUL -w "%{http_code}" "$appUrl$healthPath" 2>$null)
        $healthOk = ($LASTEXITCODE -eq 0 -and $statusCode -eq "200")
        if (-not $healthOk -and -not $appUrl.Equals("https://localhost", [StringComparison]::OrdinalIgnoreCase)) {
            $statusCode = (& curl.exe -k -s -o NUL -w "%{http_code}" "https://localhost$healthPath" 2>$null)
            $healthOk = ($LASTEXITCODE -eq 0 -and $statusCode -eq "200")
        }
    } finally {
        $ErrorActionPreference = $previousEap
    }
} else {
    try {
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            # V-02-05 (CONFIG-020): evitar tocar el callback global. Usar -SkipCertificateCheck
            # en este request especifico (es un health check self-signed durante instalacion).
            $health = Invoke-WebRequest -Uri "$appUrl$healthPath" -UseBasicParsing -TimeoutSec 20 -SkipCertificateCheck
            $healthOk = ($health.StatusCode -eq 200)
        } else {
            # V-02.08 (fix): -SkipCertificateCheck no existe en PS 5.1 (Windows
            # Server 2016 sin curl.exe): el binding error se tragaba el catch,
            # healthOk quedaba false y el actualizador hacia rollback SIEMPRE.
            # Callback TLS acotado a esta peticion, con restauracion garantizada.
            $previousCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
            try {
                [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
                $health = Invoke-WebRequest -Uri "$appUrl$healthPath" -UseBasicParsing -TimeoutSec 20
                $healthOk = ($health.StatusCode -eq 200)
            } finally {
                [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback
            }
        }
    } catch {
        $healthOk = $false
    }
}

if (-not $healthOk) {
    Restore-UpdatedBinaries -RollbackRoot $rollbackRoot -InstallPath $InstallPath -ApiTarget $apiTarget -WatchdogTarget $watchdogTarget
    throw "La actualizacion fallo porque la API no respondio al health check funcional ($healthPath). Se restauraron los binarios anteriores desde $rollbackRoot."
}

Write-Host ""
Write-Host "Atlas Balance actualizado a $newVersion." -ForegroundColor Green
Write-Host "Copia rollback de binarios: $rollbackRoot" -ForegroundColor Cyan
Write-Host "La base de datos no se reemplazo; las migraciones se aplican al arrancar la API." -ForegroundColor Cyan
Write-Host "Health check OK: $appUrl$healthPath" -ForegroundColor Green
