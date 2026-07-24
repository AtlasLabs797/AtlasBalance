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
    [switch]$SkipDatabaseSetup,
    [switch]$InstallDependencies
)

$ErrorActionPreference = "Stop"
$AppVersion = "V-02.06"
$ApiServiceName = "AtlasBalance.API"
$WatchdogServiceName = "AtlasBalance.Watchdog"
$ManagedPostgres = $false
$GeneratedPostgresAdminPassword = ""
$ExistingUsersDetected = $false
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

function Protect-SecretDirectory {
    param([string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & icacls.exe $Path /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo restringir ACL en $Path. No se escribiran credenciales en claro."
    }
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
    Protect-SecretDirectory -Path $directory
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

        $output = $Sql | & $PsqlExe @args 2>&1
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

    $ownerRoleExists = Invoke-Psql -PsqlExe $psql -Scalar -Sql "SELECT 1 FROM pg_roles WHERE rolname = '$ownerRoleName';"
    if ($ownerRoleExists -eq "1") {
        Invoke-Psql -PsqlExe $psql -Sql "ALTER ROLE $ownerRoleIdentifier WITH LOGIN PASSWORD '$ownerRolePassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;" | Out-Null
    } else {
        Invoke-Psql -PsqlExe $psql -Sql "CREATE ROLE $ownerRoleIdentifier WITH LOGIN PASSWORD '$ownerRolePassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;" | Out-Null
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

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    return @{ Path = $pfxPath; Password = $Password; PublicCer = $cerPath }
}

function Write-JsonFile {
    param([object]$Value, [string]$Path)

    $json = $Value | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
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
        [string]$RlsContextSecret
    )

    $stateFile = Join-Path $InstallPath "watchdog-state.json"
    $updateRoot = Join-Path $InstallPath "updates"
    $backupPath = Join-Path $InstallPath "backups"
    $exportPath = Join-Path $InstallPath "exports"
    $apiTarget = Join-Path $InstallPath "api"
    $dataProtectionKeysPath = Join-Path $env:ProgramData "AtlasBalance\keys"
    # V-02-05 (CONFIG-002): anadir sslmode=require a la connection string cuando el
    # host NO es localhost. Para localhost (caso comun) el SSL es opcional.
    $sslMode = if ($DbHost -eq "localhost" -or $DbHost -eq "127.0.0.1") { "" } else { ";sslmode=require" }
    $connection = "Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbUser;Password=$DbPassword$sslMode"
    $migrationConnection = "Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbOwnerUser;Password=$DbOwnerPassword$sslMode"
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
            ApiHealthUrl = $healthUrl
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
        Serilog = [ordered]@{
            MinimumLevel = [ordered]@{
                Default = "Information"
            }
        }
    }

    $apiSettingsPath = Join-Path $ApiPath "appsettings.Production.json"
    $watchdogSettingsPath = Join-Path $WatchdogPath "appsettings.Production.json"
    Write-JsonFile -Value $apiConfig -Path $apiSettingsPath
    Write-JsonFile -Value $watchdogConfig -Path $watchdogSettingsPath
    Protect-SecretFile -Path $apiSettingsPath
    Protect-SecretFile -Path $watchdogSettingsPath
    Protect-SecretDirectory -Path $dataProtectionKeysPath
}

function Install-OrReplaceService {
    param(
        [string]$Name,
        [string]$DisplayName,
        [string]$Description,
        [string]$ExePath
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
        -StartupType Automatic | Out-Null

    sc.exe failure $Name reset=86400 actions=restart/10000/restart/30000/restart/60000 | Out-Null
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
        PostgresServiceName = if ($ManagedPostgres) { $PostgresServiceName } else { "" }
        ManagedPostgres = [bool]$ManagedPostgres
        DbHost = $DbHost
        DbPort = $DbPort
        DbName = $DbName
        InstalledAt = (Get-Date).ToString("o")
    }
    Write-JsonFile -Value $runtime -Path (Join-Path $InstallPath "atlas-balance.runtime.json")
    Set-Content -LiteralPath (Join-Path $InstallPath "VERSION") -Value $AppVersion -Encoding UTF8

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
    # V-02-05 (MED-26): mostrar credenciales en pantalla en lugar de escribirlas
    # a un archivo. El operador debe capturarlas en su gestor de secretos.
    # El archivo INSTALL_CREDENTIALS_ONCE.txt sigue existiendo como path para
    # la tarea de limpieza (que se registra pero no tiene archivo que limpiar).
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Yellow
    Write-Host "CREDENCIALES INICIALES (captura esto en tu gestor de passwords)" -ForegroundColor Yellow
    Write-Host "=============================================" -ForegroundColor Yellow
    foreach ($line in $lines) {
        Write-Host $line
    }
    Write-Host "=============================================" -ForegroundColor Yellow
    Write-Host ""
    # Mantenemos el task de limpieza por compatibilidad, pero no escribimos archivo.
    if (Test-Path -LiteralPath $credentialsPath) {
        Remove-Item -LiteralPath $credentialsPath -Force -ErrorAction SilentlyContinue
    }
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
$internalApiUrl = if ($UseReverseProxy) { "http://127.0.0.1:$InternalApiPort" } else { "https://0.0.0.0:$ApiPort" }
$healthUrl = if ($UseReverseProxy) { "http://localhost:$InternalApiPort/api/health" } elseif ($ApiPort -eq 443) { "https://localhost/api/health" } else { "https://localhost`:$ApiPort/api/health" }
$appUrl = if ($UseReverseProxy) {
    if ($PublicPort -eq 443) { "https://$effectivePublicHost" } else { "https://$effectivePublicHost`:$PublicPort" }
} else {
    if ($ApiPort -eq 443) { "https://$ServerName" } else { "https://$ServerName`:$ApiPort" }
}

if ([string]::IsNullOrWhiteSpace($DbPassword)) { $DbPassword = New-RandomSecret 40 }
if ([string]::IsNullOrWhiteSpace($DbOwnerPassword)) { $DbOwnerPassword = New-RandomSecret 40 }
if ([string]::IsNullOrWhiteSpace($AdminPassword)) { $AdminPassword = New-RandomSecret 24 }
if ([string]::IsNullOrWhiteSpace($PostgresInstallPath)) { $PostgresInstallPath = Join-Path $InstallPath "postgresql\16" }
if ([string]::IsNullOrWhiteSpace($PostgresDataPath)) { $PostgresDataPath = Join-Path $InstallPath "postgres-data" }
$jwtSecret = New-RandomSecret 64
# V-02-06 (RLS-SEC-01): el secreto RLS debe ser independiente del JWT. Se
# genera aleatorio y se persiste en el appsettings efectivo durante la
# generacion de configuracion mas abajo.
$rlsContextSecret = New-RandomSecret 64
$watchdogSecret = New-RandomSecret 64
$certPassword = New-RandomSecret 40

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
foreach ($dir in @("api", "watchdog", "scripts", "backups", "exports", "logs", "certs", "updates", "config")) {
    New-Item -ItemType Directory -Path (Join-Path $InstallPath $dir) -Force | Out-Null
}

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

    Ensure-Database -PostgresBin $PostgresBinPath
    $ExistingUsersDetected = Test-ExistingApplicationUsers -PostgresBin $PostgresBinPath
    if ($ExistingUsersDetected) {
        Write-Host "Base existente detectada. Las credenciales iniciales no se regeneran." -ForegroundColor Yellow
        Write-Host "Usa el admin ya creado o ejecuta scripts\Reset-AdminPassword.ps1 despues de instalar." -ForegroundColor Yellow
    }
}

$apiPath = Join-Path $InstallPath "api"
$watchdogPath = Join-Path $InstallPath "watchdog"
Sync-DirectoryPreserveConfig -Source $apiSource -Target $apiPath
Sync-DirectoryPreserveConfig -Source $watchdogSource -Target $watchdogPath

Copy-Item -LiteralPath (Join-Path $packageRoot "Atlas Balance.cmd") -Destination (Join-Path $InstallPath "Atlas Balance.cmd") -Force
Copy-Item -LiteralPath (Join-Path $packageRoot "scripts\Launch-AtlasBalance.ps1") -Destination (Join-Path $InstallPath "scripts\Launch-AtlasBalance.ps1") -Force

$certPath = ""
$effectiveCertPassword = ""
if (-not $UseReverseProxy) {
    $cert = New-AtlasCertificate -CertDirectory (Join-Path $InstallPath "certs") -DnsName $ServerName -Password $certPassword
    Protect-SecretDirectory -Path (Join-Path $InstallPath "certs")
    Protect-SecretFile -Path $cert.Path
    $certPath = $cert.Path
    $effectiveCertPassword = $cert.Password
}
Write-AppSettings `
    -ApiPath $apiPath `
    -WatchdogPath $watchdogPath `
    -PostgresBin $PostgresBinPath `
    -CertPath $certPath `
    -CertPassword $effectiveCertPassword `
    -JwtSecret $jwtSecret `
    -WatchdogSecret $watchdogSecret `
    -RlsContextSecret $rlsContextSecret

$apiExe = Join-Path $apiPath "AtlasBalance.API.exe"
$watchdogExe = Join-Path $watchdogPath "AtlasBalance.Watchdog.exe"
Install-OrReplaceService -Name $WatchdogServiceName -DisplayName "Atlas Balance - Watchdog" -Description "Backups y actualizaciones de Atlas Balance" -ExePath $watchdogExe
Install-OrReplaceService -Name $ApiServiceName -DisplayName "Atlas Balance - API" -Description "API y frontend de Atlas Balance" -ExePath $apiExe

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
Start-Sleep -Seconds 5
try {
    $curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
    if ($curl) {
        $statusCode = (& curl.exe -k -s -o NUL -w "%{http_code}" "$appUrl/api/health" 2>$null)
        if ($LASTEXITCODE -eq 0 -and $statusCode -eq "200") {
            Write-Host "Health check curl.exe HTTP $statusCode" -ForegroundColor Green
        } else {
            Write-Host "curl.exe no confirmo health OK (codigo $statusCode). Prueba manual: curl.exe -k -v $appUrl/api/health" -ForegroundColor Yellow
        }
    } else {
        $health = Invoke-WebRequest -Uri "$appUrl/api/health" -UseBasicParsing -TimeoutSec 20
        Write-Host "Health check HTTP $($health.StatusCode)" -ForegroundColor Green
    }
} catch {
    Write-Host "La instalacion termino, pero el health check automatico no confirmo la API: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "En Windows Server 2019 usa como prueba primaria: curl.exe -k -v $appUrl/api/health" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Atlas Balance $AppVersion instalado." -ForegroundColor Green
Write-Host "URL: $appUrl" -ForegroundColor Cyan
Write-Host "Credenciales iniciales: $InstallPath\config\INSTALL_CREDENTIALS_ONCE.txt" -ForegroundColor Yellow
Write-Host "Atajo creado: Atlas Balance" -ForegroundColor Cyan
