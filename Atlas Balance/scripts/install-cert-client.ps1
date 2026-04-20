# ══════════════════════════════════════════
# Atlas Balance — Instalar CA en cliente
# Ejecutar como Administrador en cada PC cliente
# ══════════════════════════════════════════

param(
    [string]$ServerIP = "",
    [string]$Domain = "localhost"
)

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Instalación de certificado CA" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: Ejecutar como Administrador" -ForegroundColor Red
    exit 1
}

# Install mkcert CA
if (Get-Command mkcert -ErrorAction SilentlyContinue) {
    Write-Host "Instalando CA de mkcert..." -ForegroundColor Yellow
    mkcert -install
    Write-Host "CA instalada correctamente" -ForegroundColor Green
} else {
    Write-Host "AVISO: mkcert no encontrado. Instalar manualmente el certificado CA del servidor." -ForegroundColor Yellow
    Write-Host "El certificado está en: \\SERVIDOR\AtlasBalance\certs\" -ForegroundColor Yellow
}

# Add hosts entry if custom domain
if ($Domain -ne "localhost" -and $ServerIP -ne "") {
    $hostsPath = "C:\Windows\System32\drivers\etc\hosts"
    $hostsEntry = "$ServerIP    $Domain"
    $hostsContent = Get-Content $hostsPath -Raw

    if ($hostsContent -notmatch [regex]::Escape($Domain)) {
        Add-Content -Path $hostsPath -Value "`n$hostsEntry"
        Write-Host "Añadido a hosts: $hostsEntry" -ForegroundColor Green
    } else {
        Write-Host "Entrada ya existe en hosts para $Domain" -ForegroundColor Yellow
    }
} elseif ($Domain -ne "localhost") {
    Write-Host "`nPara completar la configuración, añadir manualmente a C:\Windows\System32\drivers\etc\hosts:" -ForegroundColor Yellow
    Write-Host "  IP_DEL_SERVIDOR    $Domain" -ForegroundColor White
}

Write-Host "`nListo. Abrir https://$Domain en el navegador." -ForegroundColor Green
