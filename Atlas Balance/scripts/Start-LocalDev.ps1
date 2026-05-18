#Requires -Version 5.1

param(
    [int]$TimeoutSeconds = 60,
    [switch]$SkipFrontend
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$workspaceRoot = Split-Path $root -Parent
$dotnetDir = Join-Path $workspaceRoot ".dotnet"
$pgBin = Join-Path $workspaceRoot "tools\pgsql\bin"
$pgData = Join-Path $workspaceRoot "tools\pgdata-user"
$pgLog = Join-Path $workspaceRoot "tools\postgres-user.log"
$backendPath = Join-Path $root "backend\src\AtlasBalance.API"
$projectPath = Join-Path $backendPath "AtlasBalance.API.csproj"
$localBuild = Join-Path $workspaceRoot "tools\dotnet-build\api"
$dllPath = Join-Path $localBuild "bin\Debug\net8.0\AtlasBalance.API.dll"
$localBuildObjPath = ((Join-Path $localBuild "obj") -replace "\\", "/") + "/"
$localBuildBinPath = ((Join-Path $localBuild "bin") -replace "\\", "/") + "/"
$frontendPath = Join-Path $root "frontend"
$logsPath = Join-Path $root "logs\dev"
$pidPath = Join-Path $logsPath "atlas-api-local-dev.pid"
$stdoutPath = Join-Path $logsPath "atlas-api-local-dev.out.log"
$stderrPath = Join-Path $logsPath "atlas-api-local-dev.err.log"

function Assert-PathExists {
    param(
        [string]$Path,
        [string]$Message
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw $Message
    }
}

function Test-PostgresReady {
    $pgReady = Join-Path $pgBin "pg_isready.exe"
    & $pgReady -h 127.0.0.1 -p 5433 -U postgres | Out-Null
    return $LASTEXITCODE -eq 0
}

function Test-HttpOk {
    param([string]$Url)

    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 400
    } catch {
        return $false
    }
}

function Get-ListeningPids {
    param([int]$Port)

    $connections = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($connections) {
        return @($connections | Select-Object -ExpandProperty OwningProcess -Unique)
    }

    return @()
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

        $cim = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
        $commandLine = if ($cim) { [string]$cim.CommandLine } else { "" }
        $isAtlasApi =
            $process.ProcessName -eq "AtlasBalance.API" -or
            $commandLine.IndexOf("AtlasBalance.API", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            ($process.ProcessName -eq "dotnet" -and $commandLine.IndexOf("AtlasBalance.API.dll", [StringComparison]::OrdinalIgnoreCase) -ge 0)

        if (-not $isAtlasApi) {
            throw "Port 5000 is already in use by PID $processId ($($process.ProcessName)). Refusing to stop an unrelated process."
        }

        Write-Host "[local-dev] Stopping old API PID $processId..." -ForegroundColor Yellow
        Stop-Process -Id $processId -Force
        Start-Sleep -Milliseconds 800
    }
}

Assert-PathExists (Join-Path $dotnetDir "dotnet.exe") "Local .NET SDK not found at $dotnetDir. Install it with dotnet-install.ps1 first."
Assert-PathExists (Join-Path $pgBin "pg_ctl.exe") "Portable PostgreSQL not found at $pgBin."
Assert-PathExists $pgData "Local PostgreSQL data directory not found at $pgData."
Assert-PathExists $projectPath "Backend project not found: $projectPath."
Assert-PathExists $frontendPath "Frontend directory not found: $frontendPath."

$env:PATH = "$dotnetDir;$pgBin;$env:PATH"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"

if (Test-PostgresReady) {
    Write-Host "[local-dev] PostgreSQL already responds at 127.0.0.1:5433" -ForegroundColor Green
} else {
    Write-Host "[local-dev] Starting portable PostgreSQL..." -ForegroundColor Cyan
    & (Join-Path $pgBin "pg_ctl.exe") -D $pgData -l $pgLog -o "-p 5433 -h 127.0.0.1" start
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL did not start. Check $pgLog."
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-PostgresReady) {
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not (Test-PostgresReady)) {
        throw "PostgreSQL did not become ready at 127.0.0.1:5433."
    }
}

New-Item -ItemType Directory -Force -Path $logsPath, $localBuild | Out-Null
Stop-ExistingApi

Write-Host "[local-dev] Restoring backend into local build folder..." -ForegroundColor Cyan
& (Join-Path $dotnetDir "dotnet.exe") restore $projectPath "-p:BaseIntermediateOutputPath=$localBuildObjPath"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

Write-Host "[local-dev] Building backend into local build folder..." -ForegroundColor Cyan
& (Join-Path $dotnetDir "dotnet.exe") build $projectPath --no-restore -p:UseAppHost=false -p:GenerateAssemblyInfo=false -p:GenerateTargetFrameworkAttribute=false "-p:BaseIntermediateOutputPath=$localBuildObjPath" "-p:BaseOutputPath=$localBuildBinPath"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $dllPath)) {
    throw "Compiled API DLL not found: $dllPath"
}

Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue

Write-Host "[local-dev] Starting backend with healthcheck..." -ForegroundColor Cyan
$apiProcess = Start-Process -FilePath (Join-Path $dotnetDir "dotnet.exe") `
    -ArgumentList @("`"$dllPath`"") `
    -WorkingDirectory $backendPath `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru

Set-Content -LiteralPath $pidPath -Value $apiProcess.Id -Encoding ASCII

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ($apiProcess.HasExited) {
        Write-Host "[local-dev] API exited before healthcheck. ExitCode=$($apiProcess.ExitCode)" -ForegroundColor Red
        Get-Content -Tail 80 -LiteralPath $stdoutPath -ErrorAction SilentlyContinue
        Get-Content -Tail 80 -LiteralPath $stderrPath -ErrorAction SilentlyContinue
        exit 1
    }

    if (Test-HttpOk "http://localhost:5000/api/health") {
        Write-Host "[local-dev] API healthy at http://localhost:5000/api/health (PID $($apiProcess.Id))." -ForegroundColor Green
        break
    }

    Start-Sleep -Seconds 1
}

if (-not (Test-HttpOk "http://localhost:5000/api/health")) {
    throw "Backend did not become healthy. Check logs under $logsPath."
}

if (-not $SkipFrontend) {
    if (Test-HttpOk "http://localhost:5173") {
        Write-Host "[local-dev] Frontend already responds at http://localhost:5173" -ForegroundColor Green
    } else {
        Write-Host "[local-dev] Starting frontend..." -ForegroundColor Cyan
        New-Item -ItemType Directory -Force -Path $logsPath | Out-Null
        Start-Process powershell.exe -WorkingDirectory $frontendPath -ArgumentList @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-Command", "npm.cmd run dev -- --host 127.0.0.1 --port 5173 --strictPort"
        ) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logsPath "atlas-frontend-dev.out.log") -RedirectStandardError (Join-Path $logsPath "atlas-frontend-dev.err.log") | Out-Null

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            if (Test-HttpOk "http://localhost:5173") {
                break
            }
            Start-Sleep -Seconds 1
        }

        if (-not (Test-HttpOk "http://localhost:5173")) {
            throw "Frontend did not become healthy at http://localhost:5173. Check logs under $logsPath."
        }
    }
}

if (-not (Test-HttpOk "http://localhost:5000/api/health")) {
    throw "Backend healthcheck failed after startup."
}

Write-Host "`n[local-dev] Atlas Balance development stack is ready." -ForegroundColor Green
Write-Host "  Frontend : http://localhost:5173"
Write-Host "  Backend  : http://localhost:5000"
Write-Host "  Health   : http://localhost:5000/api/health"
Write-Host "  DB       : 127.0.0.1:5433`n"
