param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"

function Require-Path([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "No se encuentra el archivo: $Path" }
}

function Normalize-AtlasVersion([string]$Value) {
    if ($Value -notmatch '^V-(\d{2})[-.](\d{2})$') { throw "Version Atlas invalida: '$Value'" }
    return "V-$($matches[1]).$($matches[2])"
}

$productRoot = Join-Path $RepoRoot "Atlas Balance"
$versionPath = Join-Path $productRoot "VERSION"
$propsPath = Join-Path $productRoot "Directory.Build.props"
$packagePath = Join-Path $productRoot "frontend\package.json"
$lockPath = Join-Path $productRoot "frontend\package-lock.json"
$seedPath = Join-Path $productRoot "backend\src\AtlasBalance.API\Data\SeedData.cs"
$releasePath = Join-Path $RepoRoot ".github\workflows\release.yml"
$buildReleasePath = Join-Path $productRoot "scripts\Build-Release.ps1"
$installerPath = Join-Path $productRoot "scripts\Instalar-AtlasBalance.ps1"
$installPath = Join-Path $productRoot "scripts\install.ps1"

@($versionPath, $propsPath, $packagePath, $lockPath, $seedPath, $releasePath, $buildReleasePath, $installerPath, $installPath) |
    ForEach-Object { Require-Path $_ }

$version = Normalize-AtlasVersion ((Get-Content -LiteralPath $versionPath -Raw).Trim())
$expected = if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { $version } else { Normalize-AtlasVersion $ExpectedVersion }
$semantic = if ($expected -match '^V-(\d{2})\.(\d{2})$') { "$( [int]$matches[1] ).$( [int]$matches[2] ).0" } else { throw "Version inesperada" }
$fileSemantic = "$semantic.0"
$tagVersion = $expected.Replace('.', '-')

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$package = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
$lockContent = Get-Content -LiteralPath $lockPath -Raw
$seed = Get-Content -LiteralPath $seedPath -Raw
$release = Get-Content -LiteralPath $releasePath -Raw
$buildRelease = Get-Content -LiteralPath $buildReleasePath -Raw
$installer = Get-Content -LiteralPath $installerPath -Raw
$install = Get-Content -LiteralPath $installPath -Raw

$releaseDefaultMatch = [regex]::Match($release, '(?ms)^\s{6}version:\s*.*?^\s{8}default:\s*"([^"]+)"')
$seedMatch = [regex]::Match($seed, '\["app_version"\]\s*=\s*\("([^"]+)"')
$buildMatch = [regex]::Match($buildRelease, '\[string\]\$Version\s*=\s*"([^"]+)"')
$installerMatch = [regex]::Match($installer, '\$AppVersion\s*=\s*"([^"]+)"')
$installMatch = [regex]::Match($install, 'AtlasBalance-(V-\d{2}[-.]\d{2})-win-x64\.zip')
$lockVersionMatch = [regex]::Match($lockContent, '(?m)^\s{2}"version":\s*"([^"]+)"')
$lockRootVersionMatch = [regex]::Match($lockContent, '(?s)"packages"\s*:\s*\{\s*""\s*:\s*\{.*?"version"\s*:\s*"([^"]+)"')
if (-not $releaseDefaultMatch.Success -or -not $seedMatch.Success -or -not $buildMatch.Success -or -not $installerMatch.Success -or -not $installMatch.Success -or -not $lockVersionMatch.Success -or -not $lockRootVersionMatch.Success) {
    throw "No se pudieron extraer todas las fuentes de version."
}

$values = [ordered]@{
    'VERSION' = $version
    'InformationalVersion' = [string]$props.Project.PropertyGroup.InformationalVersion
    'package.appVersion' = [string]$package.appVersion
    'SeedData.app_version' = $seedMatch.Groups[1].Value
    'release.default' = Normalize-AtlasVersion $releaseDefaultMatch.Groups[1].Value
    'Build-Release.default' = Normalize-AtlasVersion $buildMatch.Groups[1].Value
    'Instalar.default' = Normalize-AtlasVersion $installerMatch.Groups[1].Value
    'install.default' = Normalize-AtlasVersion $installMatch.Groups[1].Value
}
$semanticValues = [ordered]@{
    'Directory.Build.props Version' = [string]$props.Project.PropertyGroup.Version
    'Directory.Build.props AssemblyVersion' = [string]$props.Project.PropertyGroup.AssemblyVersion
    'Directory.Build.props FileVersion' = [string]$props.Project.PropertyGroup.FileVersion
    'package.json version' = [string]$package.version
    'package-lock.json version' = $lockVersionMatch.Groups[1].Value
    'package-lock root version' = $lockRootVersionMatch.Groups[1].Value
}

$issues = New-Object System.Collections.Generic.List[string]
foreach ($entry in $values.GetEnumerator()) {
    if ((Normalize-AtlasVersion ([string]$entry.Value)) -ne $expected) { $issues.Add("$($entry.Key) = '$($entry.Value)' (esperado '$expected')") }
}
foreach ($entry in $semanticValues.GetEnumerator()) {
    $expectedValue = if ($entry.Key -match 'AssemblyVersion|FileVersion') { $fileSemantic } else { $semantic }
    if ([string]$entry.Value -ne $expectedValue) { $issues.Add("$($entry.Key) = '$($entry.Value)' (esperado '$expectedValue')") }
}
if ($releaseDefaultMatch.Groups[1].Value -ne $tagVersion) { $issues.Add("release.default debe usar '$tagVersion'") }

if ($issues.Count -gt 0) {
    throw "Fuentes de version desalineadas:`n - $($issues -join "`n - ")"
}

Write-Host "Alineacion de version OK: $expected ($semantic)."
