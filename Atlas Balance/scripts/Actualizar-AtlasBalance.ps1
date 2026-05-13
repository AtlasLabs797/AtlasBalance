param(
    [string]$InstallPath = "C:\AtlasBalance",
    [switch]$SkipBackup
)

$ErrorActionPreference = "Stop"
$ApiServiceName = "AtlasBalance.API"
$WatchdogServiceName = "AtlasBalance.Watchdog"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
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
    }
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

    $connection = Parse-ConnectionString -ConnectionString $ApiConfig.ConnectionStrings.DefaultConnection
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
            throw "pg_dump devolvio codigo $LASTEXITCODE"
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

Sync-DirectoryPreserveConfig -Source $apiSource -Target $apiTarget
Sync-DirectoryPreserveConfig -Source $watchdogSource -Target $watchdogTarget

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

$appUrl = if ($runtime -and $runtime.AppUrl) { [string]$runtime.AppUrl } else { "" }
if ([string]::IsNullOrWhiteSpace($appUrl)) {
    $appUrl = "https://localhost"
}

Start-Sleep -Seconds 5
$healthOk = $false
$curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
if ($curl) {
    $statusCode = (& curl.exe -k -s -o NUL -w "%{http_code}" "$appUrl/api/health" 2>$null)
    $healthOk = ($LASTEXITCODE -eq 0 -and $statusCode -eq "200")
    if (-not $healthOk -and -not $appUrl.Equals("https://localhost", [StringComparison]::OrdinalIgnoreCase)) {
        $statusCode = (& curl.exe -k -s -o NUL -w "%{http_code}" "https://localhost/api/health" 2>$null)
        $healthOk = ($LASTEXITCODE -eq 0 -and $statusCode -eq "200")
    }
} else {
    try {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        $health = Invoke-WebRequest -Uri "$appUrl/api/health" -UseBasicParsing -TimeoutSec 20
        $healthOk = ($health.StatusCode -eq 200)
    } catch {
        $healthOk = $false
    }
}

if (-not $healthOk) {
    throw "La actualizacion copio los binarios, pero la API no respondio al health check. Revisa servicios y logs. Rollback de binarios disponible en $rollbackRoot."
}

Write-Host ""
Write-Host "Atlas Balance actualizado a $newVersion." -ForegroundColor Green
Write-Host "Copia rollback de binarios: $rollbackRoot" -ForegroundColor Cyan
Write-Host "La base de datos no se reemplazo; las migraciones se aplican al arrancar la API." -ForegroundColor Cyan
Write-Host "Health check OK: $appUrl/api/health" -ForegroundColor Green
