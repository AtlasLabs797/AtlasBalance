param(
    [string]$Version = "V-02.07",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$CleanNpmInstall,
    [switch]$AllowUnsignedLocal
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^V-\d{2}[-.]\d{2}$') {
    throw "Version invalida '$Version'. Usa formato V-02-03 o V-02.03."
}

if ($Runtime -ne "win-x64") {
    throw "Runtime invalido '$Runtime'. La release publicada solo permite win-x64."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$documentationRoot = Join-Path $workspaceRoot "Documentacion"
$frontendPath = Join-Path $repoRoot "frontend"
$apiProject = Join-Path $repoRoot "backend\src\AtlasBalance.API\AtlasBalance.API.csproj"
$watchdogProject = Join-Path $repoRoot "backend\src\AtlasBalance.Watchdog\AtlasBalance.Watchdog.csproj"
$releaseRoot = Join-Path $repoRoot "Atlas Balance Release"
$packageName = "AtlasBalance-$Version-$Runtime"
$packageRoot = Join-Path $releaseRoot $packageName
$frontendBuildDist = Join-Path $releaseRoot ".frontend-dist-$Version-$Runtime"
$secretScanner = Join-Path $PSScriptRoot "Test-AtlasSecrets.ps1"

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

function Resolve-DotnetPath {
    $localDotnet = Join-Path $workspaceRoot ".dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $localDotnet) {
        return $localDotnet
    }

    $command = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "No se encontro dotnet. Instala el SDK local en $localDotnet o agrega dotnet al PATH."
}

function Invoke-ReleaseSigner {
    param([string]$ZipPath, [string]$SignaturePath)

    $signerRoot = Join-Path $releaseRoot ("release-signer-" + [guid]::NewGuid().ToString("N"))
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

        & $dotnetPath run --project $signerProject --configuration Release -- $ZipPath $SignaturePath
        if ($LASTEXITCODE -ne 0) { throw "Firma RSA del release fallo." }
    } finally {
        Remove-Item -LiteralPath $signerRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $apiProject) -or -not (Test-Path $watchdogProject)) {
    throw "No se encontraron los proyectos .NET desde $repoRoot."
}

$dotnetPath = Resolve-DotnetPath

if (Test-Path -LiteralPath $secretScanner) {
    & $secretScanner -Root $workspaceRoot
    if (-not $?) { throw "Scanner de secretos Atlas fallo." }
}

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $frontendBuildDist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

Push-Location $frontendPath
try {
    if (-not (Test-Path "package-lock.json")) {
        throw "package-lock.json es obligatorio para generar releases reproducibles."
    }

    $nodeModulesPath = Join-Path $frontendPath "node_modules"
    $typescriptBin = Join-Path $frontendPath "node_modules\.bin\tsc.cmd"
    if ($CleanNpmInstall -or -not (Test-Path -LiteralPath $nodeModulesPath)) {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci fallo." }
    } elseif (-not (Test-Path -LiteralPath $typescriptBin)) {
        Write-Host "node_modules incompleto detectado; reparando dependencias sin limpieza destructiva." -ForegroundColor Yellow
        & npm.cmd install --ignore-scripts --no-audit --fund=false
        if ($LASTEXITCODE -ne 0) { throw "npm install fallo." }
    } else {
        Write-Host "node_modules existente detectado; omitiendo npm ci. Usa -CleanNpmInstall para reinstalacion limpia." -ForegroundColor Yellow
    }

    & npm.cmd exec tsc -- --noEmit
    if ($LASTEXITCODE -ne 0) { throw "tsc --noEmit fallo." }

    & npm.cmd exec vite -- build --outDir $frontendBuildDist --emptyOutDir true
    if ($LASTEXITCODE -ne 0) { throw "vite build fallo." }
} finally {
    Pop-Location
}

& $dotnetPath restore $apiProject --locked-mode -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet restore API --locked-mode fallo." }

& $dotnetPath restore $watchdogProject --locked-mode -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet restore Watchdog --locked-mode fallo." }

& $dotnetPath publish $apiProject `
    -c $Configuration `
    -r $Runtime `
    --no-restore `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:InformationalVersion=$Version `
    -o (Join-Path $packageRoot "api")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish API fallo." }

$publishedWwwroot = Join-Path $packageRoot "api\wwwroot"
if (Test-Path -LiteralPath $publishedWwwroot) {
    Remove-Item -LiteralPath $publishedWwwroot -Recurse -Force -ErrorAction Stop
}
New-Item -ItemType Directory -Path $publishedWwwroot -Force | Out-Null
Copy-DirectoryContents -Source $frontendBuildDist -Target $publishedWwwroot

# Excluir sourcemaps del wwwroot publicado. Vite los genera con sourcemap:'hidden',
# que solo omite el comentario sourceMappingURL del bundle: el .map se sigue sirviendo
# a quien pida la ruta. Publicarlos expone todo el codigo fuente TypeScript original.
# Se borran de forma explicita porque Copy-Item -Exclude no filtra de forma fiable en
# copias recursivas. Sin SilentlyContinue: si esto falla, la release debe fallar.
$mapFiles = @(Get-ChildItem -Path $publishedWwwroot -Filter "*.map" -Recurse -File -ErrorAction Stop)
if ($mapFiles.Count -gt 0) {
    $mapFiles | Remove-Item -Force -ErrorAction Stop
    Write-Host "Se excluyeron $($mapFiles.Count) ficheros .map del wwwroot publicado." -ForegroundColor Yellow
}

$mapLeftovers = @(Get-ChildItem -Path $publishedWwwroot -Filter "*.map" -Recurse -File -ErrorAction Stop)
if ($mapLeftovers.Count -gt 0) {
    throw "Quedan $($mapLeftovers.Count) ficheros .map en $publishedWwwroot tras la limpieza."
}

Remove-Item -LiteralPath $frontendBuildDist -Recurse -Force -ErrorAction SilentlyContinue

& $dotnetPath publish $watchdogProject `
    -c $Configuration `
    -r $Runtime `
    --no-restore `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:InformationalVersion=$Version `
    -o (Join-Path $packageRoot "watchdog")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Watchdog fallo." }

# Los PDB y los lockfiles de NuGet son artefactos de depuracion/build y no
# forman parte del paquete de produccion. Se eliminan despues de publicar ambos
# proyectos y se comprueba que no quede ninguno antes de comprimir el release.
$developmentArtifacts = @(
    Get-ChildItem -Path $packageRoot -Filter "*.pdb" -Recurse -File -ErrorAction Stop
    Get-ChildItem -Path $packageRoot -Filter "packages.lock.json" -Recurse -File -ErrorAction Stop
)
if ($developmentArtifacts.Count -gt 0) {
    $developmentArtifacts | Remove-Item -Force -ErrorAction Stop
    Write-Host "Se excluyeron $($developmentArtifacts.Count) artefactos de depuracion/build del paquete publicado." -ForegroundColor Yellow
}

$developmentLeftovers = @(
    Get-ChildItem -Path $packageRoot -Filter "*.pdb" -Recurse -File -ErrorAction Stop
    Get-ChildItem -Path $packageRoot -Filter "packages.lock.json" -Recurse -File -ErrorAction Stop
)
if ($developmentLeftovers.Count -gt 0) {
    throw "Quedan $($developmentLeftovers.Count) artefactos de depuracion/build en $packageRoot tras la limpieza."
}

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
    "uninstall-services.ps1",
    "Caddyfile.example"
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
    source_path = "C:\AtlasBalance\updates\$Version"
    target_path = "C:\AtlasBalance"
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
