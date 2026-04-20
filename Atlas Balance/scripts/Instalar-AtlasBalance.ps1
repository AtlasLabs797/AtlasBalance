param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$ServerName = $env:COMPUTERNAME,
    [int]$ApiPort = 443,
    [int]$WatchdogPort = 5001,
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432,
    [string]$DbName = "atlas_balance",
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
$AppVersion = "V-01.02"
$ApiServiceName = "AtlasBalance.API"
$WatchdogServiceName = "AtlasBalance.Watchdog"
$ManagedPostgres = $false
$GeneratedPostgresAdminPassword = ""

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

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
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
        "C:\Program Files\PostgreSQL\14\bin"
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
    & winget.exe install --id $PackageId -e --accept-source-agreements --accept-package-agreements --silent --override $override
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
            $args += @("-t", "-A", "-c", $Sql)
        } else {
            $args += @("-c", $Sql)
        }

        $output = & $PsqlExe @args 2>&1
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

    $roleName = Escape-SqlLiteral $DbUser
    $rolePassword = Escape-SqlLiteral $DbPassword
    $dbNameLiteral = Escape-SqlLiteral $DbName

    $roleExists = Invoke-Psql -PsqlExe $psql -Scalar -Sql "SELECT 1 FROM pg_roles WHERE rolname = '$roleName';"
    if ($roleExists -eq "1") {
        Invoke-Psql -PsqlExe $psql -Sql "ALTER ROLE `"$DbUser`" WITH LOGIN PASSWORD '$rolePassword';" | Out-Null
    } else {
        Invoke-Psql -PsqlExe $psql -Sql "CREATE ROLE `"$DbUser`" WITH LOGIN PASSWORD '$rolePassword';" | Out-Null
    }

    $dbExists = Invoke-Psql -PsqlExe $psql -Scalar -Sql "SELECT 1 FROM pg_database WHERE datname = '$dbNameLiteral';"
    if ($dbExists -ne "1") {
        Invoke-Psql -PsqlExe $psql -Sql "CREATE DATABASE `"$DbName`" OWNER `"$DbUser`" ENCODING 'UTF8';" | Out-Null
    }

    Invoke-Psql -PsqlExe $psql -Database $DbName -Sql "GRANT ALL PRIVILEGES ON DATABASE `"$DbName`" TO `"$DbUser`";" | Out-Null
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
        return @{ Path = $pfxPath; Password = $Password; PublicCer = $cerPath }
    }

    $dnsNames = @($DnsName, "localhost", $env:COMPUTERNAME) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
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
        [string]$WatchdogSecret
    )

    $stateFile = Join-Path $InstallPath "watchdog-state.json"
    $updateRoot = Join-Path $InstallPath "updates"
    $backupPath = Join-Path $InstallPath "backups"
    $exportPath = Join-Path $InstallPath "exports"
    $apiTarget = Join-Path $InstallPath "api"
    $dataProtectionKeysPath = Join-Path $env:ProgramData "AtlasBalance\keys"
    $connection = "Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbUser;Password=$DbPassword"
    $url = "https://0.0.0.0:$ApiPort"

    $apiConfig = [ordered]@{
        ConnectionStrings = [ordered]@{
            DefaultConnection = $connection
        }
        JwtSettings = [ordered]@{
            Secret = $JwtSecret
            AccessTokenExpMinutes = 60
            RefreshTokenExpDays = 7
        }
        SeedAdmin = [ordered]@{
            Email = $AdminEmail
            Password = $AdminPassword
        }
        WatchdogSettings = [ordered]@{
            BaseUrl = "http://localhost:$WatchdogPort"
            SharedSecret = $WatchdogSecret
            PostgresBinPath = $PostgresBin
            StateFilePath = $stateFile
            DockerPostgresContainer = "atlas_balance_db"
            UpdateSourceRoot = $updateRoot
            UpdateTargetPath = $apiTarget
        }
        GitHubSettings = [ordered]@{
            UpdateToken = ""
        }
        DataProtection = [ordered]@{
            KeysPath = $dataProtectionKeysPath
        }
        Kestrel = [ordered]@{
            Endpoints = [ordered]@{
                Https = [ordered]@{
                    Url = $url
                    Certificate = [ordered]@{
                        Path = $CertPath
                        Password = $CertPassword
                    }
                }
            }
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
        AllowedHosts = "$ServerName;localhost"
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
            DbUser = $DbUser
            DbPassword = $DbPassword
            DockerPostgresContainer = "atlas_balance_db"
            UpdateSourceRoot = $updateRoot
            UpdateTargetPath = $apiTarget
        }
        Serilog = [ordered]@{
            MinimumLevel = [ordered]@{
                Default = "Information"
            }
        }
    }

    Write-JsonFile -Value $apiConfig -Path (Join-Path $ApiPath "appsettings.Production.json")
    Write-JsonFile -Value $watchdogConfig -Path (Join-Path $WatchdogPath "appsettings.Production.json")
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

function Write-RuntimeAndCredentials {
    param([string]$AppUrl)

    $runtime = [ordered]@{
        Version = $AppVersion
        AppUrl = $AppUrl
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

    $credentialsPath = Join-Path $InstallPath "INSTALL_CREDENTIALS_ONCE.txt"
    $lines = @(
        "Atlas Balance $AppVersion",
        "URL: $AppUrl",
        "Admin inicial: $AdminEmail",
        "Password admin inicial: $AdminPassword",
        "Base de datos: $DbName",
        "Usuario DB app: $DbUser",
        "Password DB app: $DbPassword",
        "PostgreSQL gestionado por Atlas: $ManagedPostgres",
        "",
        "Guarda esto en un gestor de passwords y borra este archivo.",
        "Si dejas este archivo tirado en el servidor, la instalacion no esta segura."
    )
    if ($ManagedPostgres) {
        $lines = @(
            $lines[0..6],
            "Servicio PostgreSQL: $PostgresServiceName",
            "Puerto PostgreSQL: $DbPort",
            "Password superusuario PostgreSQL: $GeneratedPostgresAdminPassword",
            $lines[7..($lines.Count - 1)]
        ) | ForEach-Object { $_ }
    }
    Set-Content -LiteralPath $credentialsPath -Value $lines -Encoding UTF8
    & icacls.exe $credentialsPath /inheritance:r /grant:r "*S-1-5-32-544:F" "*S-1-5-18:F" | Out-Null
}

if (-not (Test-IsAdmin)) {
    throw "Ejecuta este instalador como Administrador."
}

$packageRoot = Split-Path -Parent $PSScriptRoot
$apiSource = Join-Path $packageRoot "api"
$watchdogSource = Join-Path $packageRoot "watchdog"

if (-not (Test-Path (Join-Path $apiSource "GestionCaja.API.exe")) -or
    -not (Test-Path (Join-Path $watchdogSource "GestionCaja.Watchdog.exe"))) {
    throw "No se encontraron los binarios publicados. Ejecuta primero scripts\Build-Release.ps1 y usa el paquete generado."
}

if ([string]::IsNullOrWhiteSpace($DbPassword)) { $DbPassword = New-RandomSecret 40 }
if ([string]::IsNullOrWhiteSpace($AdminPassword)) { $AdminPassword = New-RandomSecret 24 }
if ([string]::IsNullOrWhiteSpace($PostgresInstallPath)) { $PostgresInstallPath = Join-Path $InstallPath "postgresql\16" }
if ([string]::IsNullOrWhiteSpace($PostgresDataPath)) { $PostgresDataPath = Join-Path $InstallPath "postgres-data" }
$jwtSecret = New-RandomSecret 64
$watchdogSecret = New-RandomSecret 64
$certPassword = New-RandomSecret 40

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
foreach ($dir in @("api", "watchdog", "scripts", "backups", "exports", "logs", "certs", "updates")) {
    New-Item -ItemType Directory -Path (Join-Path $InstallPath $dir) -Force | Out-Null
}

if (-not $SkipDatabaseSetup) {
    if ($InstallDependencies -and [string]::IsNullOrWhiteSpace($PostgresAdminPassword)) {
        $DbHost = "localhost"
        $DbPort = Resolve-PostgresPort -PreferredPort $DbPort
        $generatedSuperPassword = New-RandomSecret 40
        [void](Try-InstallPostgres `
            -PackageId $PostgresPackageId `
            -Port $DbPort `
            -SuperPassword $generatedSuperPassword `
            -PrefixPath $PostgresInstallPath `
            -DataPath $PostgresDataPath `
            -ServiceName $PostgresServiceName)
        $PostgresBinPath = Find-PostgresBin -PreferredPath (Join-Path $PostgresInstallPath "bin")
    }

    if ([string]::IsNullOrWhiteSpace($PostgresBinPath)) {
        $PostgresBinPath = Find-PostgresBin -PreferredPath $PostgresBinPath
    }
    if ([string]::IsNullOrWhiteSpace($PostgresBinPath) -and $InstallDependencies) {
        throw "No se pudo preparar PostgreSQL automaticamente. Instala PostgreSQL 16+ o pasa -PostgresAdminPassword para usar una instancia existente."
    }
    if ([string]::IsNullOrWhiteSpace($PostgresBinPath)) {
        throw "No se encontro PostgreSQL 16+. Ejecuta install.cmd para instalacion automatica o indica -PostgresBinPath."
    }

    Ensure-Database -PostgresBin $PostgresBinPath
}

$apiPath = Join-Path $InstallPath "api"
$watchdogPath = Join-Path $InstallPath "watchdog"
Sync-DirectoryPreserveConfig -Source $apiSource -Target $apiPath
Sync-DirectoryPreserveConfig -Source $watchdogSource -Target $watchdogPath

Copy-Item -LiteralPath (Join-Path $packageRoot "Atlas Balance.cmd") -Destination (Join-Path $InstallPath "Atlas Balance.cmd") -Force
Copy-Item -LiteralPath (Join-Path $packageRoot "scripts\Launch-AtlasBalance.ps1") -Destination (Join-Path $InstallPath "scripts\Launch-AtlasBalance.ps1") -Force

$cert = New-AtlasCertificate -CertDirectory (Join-Path $InstallPath "certs") -DnsName $ServerName -Password $certPassword
Write-AppSettings `
    -ApiPath $apiPath `
    -WatchdogPath $watchdogPath `
    -PostgresBin $PostgresBinPath `
    -CertPath $cert.Path `
    -CertPassword $cert.Password `
    -JwtSecret $jwtSecret `
    -WatchdogSecret $watchdogSecret

$apiExe = Join-Path $apiPath "GestionCaja.API.exe"
$watchdogExe = Join-Path $watchdogPath "GestionCaja.Watchdog.exe"
Install-OrReplaceService -Name $WatchdogServiceName -DisplayName "Atlas Balance - Watchdog" -Description "Backups y actualizaciones de Atlas Balance" -ExePath $watchdogExe
Install-OrReplaceService -Name $ApiServiceName -DisplayName "Atlas Balance - API" -Description "API y frontend de Atlas Balance" -ExePath $apiExe

$firewallName = "Atlas Balance HTTPS $ApiPort"
if (-not (Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $firewallName -Direction Inbound -Protocol TCP -LocalPort $ApiPort -Action Allow | Out-Null
}

Start-Service -Name $WatchdogServiceName
Start-Service -Name $ApiServiceName

$appUrl = if ($ApiPort -eq 443) { "https://$ServerName" } else { "https://$ServerName`:$ApiPort" }
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

[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
Start-Sleep -Seconds 5
try {
    $health = Invoke-WebRequest -Uri "$appUrl/api/health" -UseBasicParsing -TimeoutSec 20
    Write-Host "Health check HTTP $($health.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "La instalacion termino, pero el health check aun no responde: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Atlas Balance $AppVersion instalado." -ForegroundColor Green
Write-Host "URL: $appUrl" -ForegroundColor Cyan
Write-Host "Credenciales iniciales: $InstallPath\INSTALL_CREDENTIALS_ONCE.txt" -ForegroundColor Yellow
Write-Host "Atajo creado: Atlas Balance" -ForegroundColor Cyan
