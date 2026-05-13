#Requires -Version 5.1
<#
    Starts the local development stack and refuses to pretend success
    if the API is not healthy.
#>

param(
    [int]$TimeoutSeconds = 60,
    [switch]$SkipDocker,
    [switch]$SkipFrontend
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$backendLauncher = Join-Path $PSScriptRoot "Start-BackendDev.ps1"
$frontendPath = Join-Path $root "frontend"

function Clear-ProxyEnvironment {
    foreach ($name in @("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "GIT_HTTP_PROXY", "GIT_HTTPS_PROXY", "http_proxy", "https_proxy", "all_proxy")) {
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }
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

Clear-ProxyEnvironment

if (-not $SkipDocker) {
    Write-Host "`n[dev] Starting PostgreSQL..." -ForegroundColor Cyan
    Push-Location $root
    try {
        docker compose up -d
    } catch {
        Write-Host "[dev] Docker could not be started. If PostgreSQL is already listening on 5433, continuing." -ForegroundColor Yellow
    } finally {
        Pop-Location
    }
}

Write-Host "[dev] Starting backend with healthcheck..." -ForegroundColor Cyan
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $backendLauncher -TimeoutSeconds $TimeoutSeconds
if ($LASTEXITCODE -ne 0) {
    throw "Backend did not become healthy. Check logs under logs\dev."
}

if (-not $SkipFrontend) {
    if (Test-HttpOk "http://localhost:5173") {
        Write-Host "[dev] Frontend already responds at http://localhost:5173" -ForegroundColor Green
    } else {
        Write-Host "[dev] Starting frontend..." -ForegroundColor Cyan
        Start-Process powershell.exe -WorkingDirectory $frontendPath -ArgumentList @(
            "-NoProfile",
            "-NoExit",
            "-ExecutionPolicy", "Bypass",
            "-Command", "npm.cmd run dev -- --host 127.0.0.1 --port 5173 --strictPort"
        ) -WindowStyle Normal | Out-Null

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            if (Test-HttpOk "http://localhost:5173") {
                break
            }
            Start-Sleep -Seconds 1
        }

        if (-not (Test-HttpOk "http://localhost:5173")) {
            throw "Frontend did not become healthy at http://localhost:5173."
        }
    }
}

if (-not (Test-HttpOk "http://localhost:5000/api/health")) {
    throw "Backend healthcheck failed after startup."
}

Write-Host "`n[dev] Atlas Balance development stack is ready." -ForegroundColor Green
Write-Host "  Frontend : http://localhost:5173"
Write-Host "  Backend  : http://localhost:5000"
Write-Host "  Health   : http://localhost:5000/api/health"
Write-Host "  DB       : localhost:5433`n"
