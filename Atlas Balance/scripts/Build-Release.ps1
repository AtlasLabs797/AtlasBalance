param(
    [string]$Version = "V-01.06",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$CleanNpmInstall,
    [switch]$AllowUnsignedLocal
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$documentationRoot = Join-Path $workspaceRoot "Documentacion"
$frontendPath = Join-Path $repoRoot "frontend"
$apiProject = Join-Path $repoRoot "backend\src\AtlasBalance.API\AtlasBalance.API.csproj"
$watchdogProject = Join-Path $repoRoot "backend\src\AtlasBalance.Watchdog\AtlasBalance.Watchdog.csproj"
$apiWwwroot = Join-Path $repoRoot "backend\src\AtlasBalance.API\wwwroot"
$releaseRoot = Join-Path $repoRoot "Atlas Balance Release"
$packageName = "AtlasBalance-$Version-$Runtime"
$packageRoot = Join-Path $releaseRoot $packageName

function Copy-DirectoryContents {
    param([string]$Source, [string]$Target)

    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Target -Recurse -Force
}

function Write-JsonFile {
    param([object]$Value, [string]$Path)

    $json = $Value | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

if (-not (Test-Path $apiProject) -or -not (Test-Path $watchdogProject)) {
    throw "No se encontraron los proyectos .NET desde $repoRoot."
}

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

Push-Location $frontendPath
try {
    if (-not (Test-Path "package-lock.json")) {
        throw "package-lock.json es obligatorio para generar releases reproducibles."
    }

    & npm.cmd ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci fallo." }

    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build fallo." }
} finally {
    Pop-Location
}

& dotnet restore (Join-Path $repoRoot "backend\AtlasBalance.sln") --locked-mode
if ($LASTEXITCODE -ne 0) { throw "dotnet restore --locked-mode fallo." }

if (Test-Path -LiteralPath $apiWwwroot) {
    Remove-Item -LiteralPath $apiWwwroot -Recurse -Force -ErrorAction Stop
}
New-Item -ItemType Directory -Path $apiWwwroot -Force | Out-Null
if (Get-ChildItem -LiteralPath $apiWwwroot -Force | Select-Object -First 1) {
    throw "wwwroot no quedo limpio; abortando release para no empaquetar assets antiguos."
}
Copy-DirectoryContents -Source (Join-Path $frontendPath "dist") -Target $apiWwwroot

& dotnet publish $apiProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:InformationalVersion=$Version `
    -o (Join-Path $packageRoot "api")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish API fallo." }

& dotnet publish $watchdogProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:InformationalVersion=$Version `
    -o (Join-Path $packageRoot "watchdog")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Watchdog fallo." }

New-Item -ItemType Directory -Path (Join-Path $packageRoot "scripts") -Force | Out-Null
foreach ($script in @(
    "install.ps1",
    "update.ps1",
    "uninstall.ps1",
    "start.ps1",
    "Instalar-AtlasBalance.ps1",
    "Reset-AdminPassword.ps1",
    "Actualizar-AtlasBalance.ps1",
    "Launch-AtlasBalance.ps1",
    "install-cert-client.ps1",
    "uninstall-services.ps1"
)) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\$script") -Destination (Join-Path $packageRoot "scripts\$script") -Force
}

foreach ($cmd in @(
    "install.cmd",
    "update.cmd",
    "uninstall.cmd",
    "start.cmd",
    "Instalar Atlas Balance.cmd",
    "Actualizar Atlas Balance.cmd",
    "Atlas Balance.cmd"
)) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $cmd) -Destination (Join-Path $packageRoot $cmd) -Force
}

Copy-Item -LiteralPath (Join-Path $repoRoot "VERSION") -Destination (Join-Path $packageRoot "VERSION") -Force
$releaseReadme = Join-Path $repoRoot "README_RELEASE.md"
if (Test-Path $releaseReadme) {
    Copy-Item -LiteralPath $releaseReadme -Destination (Join-Path $packageRoot "README.md") -Force
}
$releaseGitignore = Join-Path $repoRoot "RELEASE.gitignore"
if (Test-Path $releaseGitignore) {
    Copy-Item -LiteralPath $releaseGitignore -Destination (Join-Path $packageRoot ".gitignore") -Force
}
$userDocumentation = Join-Path $documentationRoot "documentacion.md"
if (Test-Path $userDocumentation) {
    Copy-Item -LiteralPath $userDocumentation -Destination (Join-Path $packageRoot "documentacion.md") -Force
}

$manifest = [ordered]@{
    version = $Version
    message = "Atlas Balance $Version"
    source_path = "C:\AtlasBalance\updates\$Version\api"
}
Write-JsonFile -Value $manifest -Path (Join-Path $packageRoot "version.json")

$zipPath = Join-Path $releaseRoot "$packageName.zip"
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

$signaturePath = "$zipPath.sig"
Remove-Item -LiteralPath $signaturePath -Force -ErrorAction SilentlyContinue
if (-not [string]::IsNullOrWhiteSpace($env:ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM)) {
    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $privateKeyPem = $env:ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM -replace "\\n", "`n"
        $rsa.ImportFromPem($privateKeyPem)
        $zipBytes = [System.IO.File]::ReadAllBytes($zipPath)
        $signature = $rsa.SignData(
            $zipBytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        [System.IO.File]::WriteAllBytes($signaturePath, $signature)
        Write-Host "Firma generada: $signaturePath" -ForegroundColor Green
    } finally {
        $rsa.Dispose()
    }
} else {
    if (-not $AllowUnsignedLocal) {
        Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
        throw "ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM es obligatorio para un release publicable. Usa -AllowUnsignedLocal solo para pruebas locales."
    }

    Write-Warning "Release local sin firma generado por -AllowUnsignedLocal. No lo publiques."
}

Write-Host "Release generado: $packageRoot" -ForegroundColor Green
Write-Host "ZIP generado: $zipPath" -ForegroundColor Green
