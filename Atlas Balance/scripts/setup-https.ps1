param(
    [string]$Domain = "localhost",
    [string]$CertPath = "C:\AtlasBalance\certs"
)

$ErrorActionPreference = "Stop"

Write-Host "Atlas Balance - configuracion HTTPS" -ForegroundColor Cyan

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Ejecuta este script como Administrador."
}

if (-not (Get-Command "mkcert.exe" -ErrorAction SilentlyContinue)) {
    throw "mkcert.exe no esta instalado. Usa el instalador principal salvo que necesites certificados mkcert para desarrollo."
}

New-Item -ItemType Directory -Path $CertPath -Force | Out-Null

Write-Host "Instalando CA raiz de mkcert..." -ForegroundColor Yellow
& mkcert.exe -install
if ($LASTEXITCODE -ne 0) {
    throw "mkcert -install fallo."
}

Write-Host "Generando certificados para: $Domain localhost 127.0.0.1" -ForegroundColor Yellow
Push-Location $CertPath
try {
    & mkcert.exe $Domain localhost 127.0.0.1
    if ($LASTEXITCODE -ne 0) {
        throw "mkcert fallo al generar certificados."
    }
} finally {
    Pop-Location
}

if ($Domain -ne "localhost") {
    $hostsPath = "C:\Windows\System32\drivers\etc\hosts"
    $hostsEntry = "127.0.0.1    $Domain"
    $hostsContent = Get-Content -LiteralPath $hostsPath -Raw

    if ($hostsContent -notmatch [regex]::Escape($Domain)) {
        Add-Content -LiteralPath $hostsPath -Value "`n$hostsEntry"
        Write-Host "Entrada agregada a hosts: $hostsEntry" -ForegroundColor Green
    } else {
        Write-Host "Entrada hosts existente para $Domain" -ForegroundColor Yellow
    }
}

Write-Host "HTTPS configurado. Certificados en: $CertPath" -ForegroundColor Green
Write-Host "Para produccion real, usa un certificado emitido por la autoridad interna o publica correspondiente." -ForegroundColor Yellow
