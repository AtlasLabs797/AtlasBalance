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

function Invoke-ReleaseSigner {
    param([string]$ZipPath, [string]$SignaturePath)

    $signerRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("atlas-release-signer-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $signerRoot -Force | Out-Null
    try {
        $signerProject = Join-Path $signerRoot "AtlasReleaseSigner.csproj"
        $signerProgram = Join-Path $signerRoot "Program.cs"
        Set-Content -LiteralPath $signerProject -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
        Set-Content -LiteralPath $signerProgram -Encoding UTF8 -Value @'
using System.Security.Cryptography;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: AtlasReleaseSigner <zipPath> <signaturePath>");
    return 2;
}

var privateKeyPem = Environment.GetEnvironmentVariable("ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM");
if (string.IsNullOrWhiteSpace(privateKeyPem))
{
    Console.Error.WriteLine("ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM is required.");
    return 3;
}

using var rsa = RSA.Create();
rsa.ImportFromPem(privateKeyPem.Replace("\\n", "\n"));
var zipBytes = await File.ReadAllBytesAsync(args[0]);
var signature = rsa.SignData(
    zipBytes,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);
await File.WriteAllBytesAsync(args[1], signature);
return 0;
'@

        & dotnet run --project $signerProject --configuration Release -- $ZipPath $SignaturePath
        if ($LASTEXITCODE -ne 0) { throw "Firma RSA del release fallo." }
    } finally {
        Remove-Item -LiteralPath $signerRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
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

& dotnet restore $apiProject --locked-mode -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet restore API --locked-mode fallo." }

& dotnet restore $watchdogProject --locked-mode -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet restore Watchdog --locked-mode fallo." }

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
    --no-restore `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:InformationalVersion=$Version `
    -o (Join-Path $packageRoot "api")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish API fallo." }

& dotnet publish $watchdogProject `
    -c $Configuration `
    -r $Runtime `
    --no-restore `
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
    Invoke-ReleaseSigner -ZipPath $zipPath -SignaturePath $signaturePath
    Write-Host "Firma generada: $signaturePath" -ForegroundColor Green
} else {
    if (-not $AllowUnsignedLocal) {
        Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
        throw "ATLAS_RELEASE_SIGNING_PRIVATE_KEY_PEM es obligatorio para un release publicable. Usa -AllowUnsignedLocal solo para pruebas locales."
    }

    Write-Warning "Release local sin firma generado por -AllowUnsignedLocal. No lo publiques."
}

Write-Host "Release generado: $packageRoot" -ForegroundColor Green
Write-Host "ZIP generado: $zipPath" -ForegroundColor Green
