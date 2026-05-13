#Requires -Version 5.1
<#
    Starts the Atlas Balance API for local development with a real healthcheck.
    It deliberately avoids apphost.exe by running the compiled DLL.
#>

param(
    [int]$TimeoutSeconds = 60,
    [switch]$NoBuild,
    [switch]$KeepExisting
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$backendPath = Join-Path $root "backend\src\AtlasBalance.API"
$projectPath = Join-Path $backendPath "AtlasBalance.API.csproj"
$dllPath = Join-Path $backendPath "bin\Debug\net8.0\AtlasBalance.API.dll"
$logsPath = Join-Path $root "logs\dev"
$pidPath = Join-Path $logsPath "atlas-api-dev.pid"
$stdoutPath = Join-Path $logsPath "atlas-api-dev.out.log"
$stderrPath = Join-Path $logsPath "atlas-api-dev.err.log"
$healthUrl = "http://localhost:5000/api/health"

function Repair-ProcessEnvironment {
    $proxyNames = @(
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "GIT_HTTP_PROXY",
        "GIT_HTTPS_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy"
    )

    foreach ($name in $proxyNames) {
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }

    $processPath = [Environment]::GetEnvironmentVariable("Path", "Process")
    if ([string]::IsNullOrWhiteSpace($processPath)) {
        $pathParts = @(
            [Environment]::GetEnvironmentVariable("Path", "Machine"),
            [Environment]::GetEnvironmentVariable("Path", "User")
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $processPath = [string]::Join(";", $pathParts)
    }

    [Environment]::SetEnvironmentVariable("PATH", $null, "Process")
    [Environment]::SetEnvironmentVariable("Path", $processPath, "Process")
    [Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development", "Process")
    [Environment]::SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development", "Process")
}

function Get-ListeningPids {
    param([int]$Port)

    $connections = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($connections) {
        return @($connections | Select-Object -ExpandProperty OwningProcess -Unique)
    }

    $lines = netstat -ano | Select-String -Pattern (":$Port\s")
    return @(
        $lines |
            ForEach-Object {
                $parts = ($_ -replace "^\s+", "") -split "\s+"
                if ($parts.Length -ge 5 -and $parts[3] -eq "LISTENING") {
                    [int]$parts[4]
                }
            } |
            Select-Object -Unique
    )
}

function Stop-ExistingApi {
    $candidatePids = @()

    if (Test-Path $pidPath) {
        $savedPid = Get-Content -LiteralPath $pidPath -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($savedPid -match "^\d+$") {
            $candidatePids += [int]$savedPid
        }
    }

    $candidatePids += Get-ListeningPids -Port 5000
    $candidatePids = @($candidatePids | Select-Object -Unique)

    foreach ($processId in $candidatePids) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            continue
        }

        $cim = $null
        try {
            $cim = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
        } catch {
            $cim = $null
        }
        $commandLine = if ($cim) { [string]$cim.CommandLine } else { "" }
        $isAtlasApi =
            $process.ProcessName -eq "AtlasBalance.API" -or
            $commandLine.IndexOf("AtlasBalance.API", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            ($process.ProcessName -eq "dotnet" -and (Test-Path $pidPath) -and ((Get-Content -LiteralPath $pidPath -ErrorAction SilentlyContinue | Select-Object -First 1) -eq [string]$processId))

        if (-not $isAtlasApi) {
            throw "Port 5000 is already in use by PID $processId ($($process.ProcessName)). Refusing to kill an unrelated process."
        }

        Write-Host "[backend] Stopping old API PID $processId..." -ForegroundColor Yellow
        Stop-Process -Id $processId -Force
        Start-Sleep -Milliseconds 800
    }
}

function Test-Health {
    try {
        $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 3
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

Repair-ProcessEnvironment
New-Item -ItemType Directory -Force -Path $logsPath | Out-Null

if (-not (Test-Path $projectPath)) {
    throw "Backend project not found: $projectPath"
}

if ($KeepExisting -and (Test-Health)) {
    Write-Host "[backend] API already healthy at $healthUrl" -ForegroundColor Green
    exit 0
}

Stop-ExistingApi

if (-not $NoBuild) {
    Write-Host "[backend] Building API..." -ForegroundColor Cyan
    Push-Location $backendPath
    try {
        & dotnet build $projectPath -p:UseAppHost=false --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

if (-not (Test-Path $dllPath)) {
    throw "Compiled API DLL not found: $dllPath"
}

Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue

Write-Host "[backend] Starting API..." -ForegroundColor Cyan
$process = Start-Process -FilePath "dotnet" `
    -ArgumentList @("bin\Debug\net8.0\AtlasBalance.API.dll") `
    -WorkingDirectory $backendPath `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru

Set-Content -LiteralPath $pidPath -Value $process.Id -Encoding ASCII

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ($process.HasExited) {
        Write-Host "[backend] API exited before healthcheck. ExitCode=$($process.ExitCode)" -ForegroundColor Red
        Get-Content -Tail 80 -LiteralPath $stdoutPath -ErrorAction SilentlyContinue
        Get-Content -Tail 80 -LiteralPath $stderrPath -ErrorAction SilentlyContinue
        exit 1
    }

    if (Test-Health) {
        Write-Host "[backend] API healthy at $healthUrl (PID $($process.Id))." -ForegroundColor Green
        exit 0
    }

    Start-Sleep -Seconds 1
}

Write-Host "[backend] API did not become healthy within $TimeoutSeconds seconds." -ForegroundColor Red
Get-Content -Tail 80 -LiteralPath $stdoutPath -ErrorAction SilentlyContinue
Get-Content -Tail 80 -LiteralPath $stderrPath -ErrorAction SilentlyContinue
exit 1
