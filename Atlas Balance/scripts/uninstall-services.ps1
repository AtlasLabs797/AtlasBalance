# ══════════════════════════════════════════
# Atlas Balance — Desinstalar Windows Services
# Ejecutar como Administrador
# ══════════════════════════════════════════

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Desinstalación de Windows Services" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: Ejecutar como Administrador" -ForegroundColor Red
    exit 1
}

$confirm = Read-Host "¿Estás seguro? Esto detendrá y eliminará ambos servicios (s/n)"
if ($confirm -ne 's') {
    Write-Host "Cancelado." -ForegroundColor Yellow
    exit 0
}

foreach ($serviceName in @("AtlasBalance.API", "AtlasBalance.Watchdog")) {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Host "Deteniendo $serviceName..." -ForegroundColor Yellow
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $serviceName
        Write-Host "$serviceName eliminado" -ForegroundColor Green
    } else {
        Write-Host "$serviceName no encontrado" -ForegroundColor Gray
    }
}

Write-Host "`nServicios desinstalados. Los datos en PostgreSQL y archivos NO se han eliminado." -ForegroundColor Yellow
