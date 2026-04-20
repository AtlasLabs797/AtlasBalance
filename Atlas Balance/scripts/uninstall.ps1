param(
    [string]$InstallPath = "C:\AtlasBalance",
    [switch]$KeepDatabase,
    [switch]$KeepProgramData
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-Argument {
    param([string]$Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '"', '\"') + '"'
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

function Stop-AndDeleteService {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return
    }

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $service) {
        return
    }

    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
        try {
            $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(45))
        } catch {
            Write-Host "No se pudo confirmar parada de $Name; se intentara eliminar igual." -ForegroundColor Yellow
        }
    }

    sc.exe delete $Name | Out-Null
}

function Remove-ShortcutIfExists {
    param([string]$Root)

    if ([string]::IsNullOrWhiteSpace($Root)) {
        return
    }

    $shortcutPath = Join-Path $Root "Atlas Balance.lnk"
    Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
}

function Remove-DirectoryStrict {
    param(
        [string]$Path,
        [string]$ExpectedMarker,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return
    }

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootPath = [IO.Path]::GetPathRoot($fullPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

    if ($fullPath.Length -lt 10 -or [string]::Equals($fullPath, $rootPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ruta insegura para borrar ${Description}: $fullPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedMarker) -and -not (Test-Path (Join-Path $fullPath $ExpectedMarker))) {
        throw "No borro $Description porque falta el marcador esperado '$ExpectedMarker' en $fullPath."
    }

    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

function Invoke-PostgresUninstaller {
    param([string]$InstallRoot)

    if ([string]::IsNullOrWhiteSpace($InstallRoot) -or -not (Test-Path $InstallRoot)) {
        return
    }

    $uninstaller = Get-ChildItem -LiteralPath $InstallRoot -Filter "uninstall-postgresql.exe" -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $uninstaller) {
        return
    }

    Start-Process -FilePath $uninstaller.FullName -ArgumentList "--mode unattended" -Wait
}

if (-not (Test-IsAdmin)) {
    $scriptPath = $MyInvocation.MyCommand.Path
    $argumentList = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Quote-Argument $scriptPath),
        "-InstallPath", (Quote-Argument $InstallPath)
    )
    if ($KeepDatabase) { $argumentList += "-KeepDatabase" }
    if ($KeepProgramData) { $argumentList += "-KeepProgramData" }

    Start-Process -FilePath "powershell.exe" -ArgumentList ($argumentList -join " ") -Verb RunAs | Out-Null
    exit 0
}

$resolvedInstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path $resolvedInstallPath)) {
    Write-Host "No hay instalacion en $resolvedInstallPath." -ForegroundColor Yellow
    exit 0
}

$runtime = Read-RuntimeConfig -BasePath $resolvedInstallPath
$apiService = if ($runtime -and $runtime.ApiServiceName) { [string]$runtime.ApiServiceName } else { "AtlasBalance.API" }
$watchdogService = if ($runtime -and $runtime.WatchdogServiceName) { [string]$runtime.WatchdogServiceName } else { "AtlasBalance.Watchdog" }
$managedPostgres = $runtime -and $runtime.ManagedPostgres
$postgresService = if ($managedPostgres -and $runtime.PostgresServiceName) { [string]$runtime.PostgresServiceName } else { "AtlasBalance.PostgreSQL" }

Stop-AndDeleteService -Name $apiService
Stop-AndDeleteService -Name $watchdogService

if ($managedPostgres -and -not $KeepDatabase) {
    Stop-AndDeleteService -Name $postgresService
    Invoke-PostgresUninstaller -InstallRoot (Join-Path $resolvedInstallPath "postgresql")
}

Get-NetFirewallRule -DisplayName "Atlas Balance HTTPS*" -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue

Remove-ShortcutIfExists -Root ([Environment]::GetFolderPath("Desktop"))
Remove-ShortcutIfExists -Root ([Environment]::GetFolderPath("CommonDesktopDirectory"))
Remove-ShortcutIfExists -Root ([Environment]::GetFolderPath("CommonStartMenu"))

if (-not $KeepProgramData) {
    $programDataPath = Join-Path $env:ProgramData "AtlasBalance"
    if (Test-Path $programDataPath) {
        Remove-DirectoryStrict -Path $programDataPath -ExpectedMarker "keys" -Description "ProgramData AtlasBalance"
    }
}

if ($KeepDatabase -and $managedPostgres) {
    Write-Host "Se conservaron base de datos y PostgreSQL gestionado por -KeepDatabase." -ForegroundColor Yellow
} else {
    Remove-DirectoryStrict -Path $resolvedInstallPath -ExpectedMarker "atlas-balance.runtime.json" -Description "instalacion Atlas Balance"
}

Write-Host "Atlas Balance desinstalado." -ForegroundColor Green
