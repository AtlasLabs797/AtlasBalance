param(
    [string]$InstallPath = "",
    [string]$Url = ""
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-Elevated {
    param([string]$ScriptPath, [string]$InstallPathValue, [string]$UrlValue)

    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$ScriptPath`"",
        "-InstallPath", "`"$InstallPathValue`""
    )
    if (-not [string]::IsNullOrWhiteSpace($UrlValue)) {
        $args += @("-Url", "`"$UrlValue`"")
    }

    Start-Process -FilePath "powershell.exe" -ArgumentList $args -Verb RunAs | Out-Null
}

function Read-RuntimeConfig {
    param([string]$BasePath)

    $configPath = Join-Path $BasePath "atlas-balance.runtime.json"
    if (-not (Test-Path $configPath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Ensure-ServiceRunning {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return $false
    }

    if ($service.Status -ne "Running") {
        Start-Service -Name $Name
        $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
    }

    return $true
}

function Resolve-ManagedPostgresServiceName {
    param([object]$Runtime)

    if ($runtime -and
        $runtime.ManagedPostgres -and
        -not [string]::IsNullOrWhiteSpace([string]$runtime.PostgresServiceName)) {
        return [string]$runtime.PostgresServiceName
    }

    return ""
}

function Start-DevelopmentMode {
    param([string]$RepoRoot)

    $backendPath = Join-Path $RepoRoot "backend\src\GestionCaja.API"
    $frontendPath = Join-Path $RepoRoot "frontend"

    if (-not (Test-Path (Join-Path $backendPath "GestionCaja.API.csproj")) -or
        -not (Test-Path (Join-Path $frontendPath "package.json"))) {
        return $false
    }

    Start-Process -FilePath "powershell.exe" -WorkingDirectory $backendPath -ArgumentList @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-Command", "dotnet run"
    ) | Out-Null

    Start-Process -FilePath "powershell.exe" -WorkingDirectory $frontendPath -ArgumentList @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-Command", "npm.cmd run dev"
    ) | Out-Null

    Start-Sleep -Seconds 5
    Start-Process "http://localhost:5173" | Out-Null
    return $true
}

$scriptPath = $MyInvocation.MyCommand.Path
$scriptDir = Split-Path -Parent $scriptPath
$defaultInstallPath = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($InstallPath)) {
    $InstallPath = $defaultInstallPath
}

$runtime = Read-RuntimeConfig -BasePath $InstallPath
$apiService = if ($runtime -and $runtime.ApiServiceName) { [string]$runtime.ApiServiceName } else { "AtlasBalance.API" }
$watchdogService = if ($runtime -and $runtime.WatchdogServiceName) { [string]$runtime.WatchdogServiceName } else { "AtlasBalance.Watchdog" }
$postgresService = Resolve-ManagedPostgresServiceName -Runtime $runtime
$appUrl = if (-not [string]::IsNullOrWhiteSpace($Url)) { $Url } elseif ($runtime -and $runtime.AppUrl) { [string]$runtime.AppUrl } else { "https://localhost" }

$api = Get-Service -Name $apiService -ErrorAction SilentlyContinue
$watchdog = Get-Service -Name $watchdogService -ErrorAction SilentlyContinue
$postgres = if ([string]::IsNullOrWhiteSpace($postgresService)) { $null } else { Get-Service -Name $postgresService -ErrorAction SilentlyContinue }

if ($api -or $watchdog -or $postgres) {
    $needsAdmin =
        ($postgres -and $postgres.Status -ne "Running") -or
        ($watchdog -and $watchdog.Status -ne "Running") -or
        ($api -and $api.Status -ne "Running")
    if ($needsAdmin -and -not (Test-IsAdmin)) {
        Start-Elevated -ScriptPath $scriptPath -InstallPathValue $InstallPath -UrlValue $appUrl
        exit 0
    }

    if ($postgres) {
        [void](Ensure-ServiceRunning -Name $postgresService)
        Start-Sleep -Seconds 2
    }
    if ($watchdog) {
        [void](Ensure-ServiceRunning -Name $watchdogService)
    }
    if ($api) {
        [void](Ensure-ServiceRunning -Name $apiService)
    }

    Start-Sleep -Seconds 2
    Start-Process $appUrl | Out-Null
    exit 0
}

$repoRoot = Split-Path -Parent $scriptDir
if (Start-DevelopmentMode -RepoRoot $repoRoot) {
    exit 0
}

Write-Host "Atlas Balance no esta instalado como servicio y no se encontro un entorno de desarrollo valido." -ForegroundColor Red
Write-Host "Ejecuta primero el instalador o abre el proyecto desde la carpeta correcta." -ForegroundColor Yellow
exit 1
