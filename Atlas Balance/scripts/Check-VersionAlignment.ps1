param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

function Resolve-Value {
    param(
        [string]$Path,
        [string]$Pattern
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Error "No se encuentra el archivo: $Path"
        exit 2
    }
    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, $Pattern)
    if (-not $match.Success) {
        Write-Error "No se pudo extraer el patron '$Pattern' de $Path"
        exit 2
    }
    return $match.Groups[1].Value.Trim()
}

$versionFile       = Join-Path $RepoRoot "Atlas Balance\VERSION"
$buildPropsPath    = Join-Path $RepoRoot "Atlas Balance\Directory.Build.props"
$packageJsonPath   = Join-Path $RepoRoot "Atlas Balance\frontend\package.json"
$seedDataPath      = Join-Path $RepoRoot "Atlas Balance\backend\src\AtlasBalance.API\Data\SeedData.cs"
$releaseWorkflow   = Join-Path $RepoRoot ".github\workflows\release.yml"

$versionFileValue     = (Get-Content -LiteralPath $versionFile -Raw).Trim()
$informationalVersion = Resolve-Value -Path $buildPropsPath -Pattern '<InformationalVersion>([^<]+)</InformationalVersion>'
$appVersion           = Resolve-Value -Path $packageJsonPath -Pattern '"appVersion"\s*:\s*"([^"]+)"'
$seedAppVersion       = Resolve-Value -Path $seedDataPath -Pattern '\["app_version"\]\s*=\s*\("([^"]+)"'

# V-02.06 (PR F5): default del input `version` en el workflow de release.
# Recorremos linea por linea para quedarnos con el `default:` que vive
# dentro del input `version` (no el de `runtime` u otros).
$releaseDefault = $null
$expectingVersionDefault = $false
foreach ($rawLine in (Get-Content -LiteralPath $releaseWorkflow)) {
    $line = $rawLine.Trim()
    if ($line -match '^version:\s*$') {
        $expectingVersionDefault = $true
        continue
    }
    if ($expectingVersionDefault -and $line -match 'default:\s*"([^"]+)"') {
        $releaseDefault = $matches[1].Trim()
        break
    }
}
if (-not $releaseDefault) {
    Write-Error "No se encontro el default del input `version` en $releaseWorkflow"
    exit 2
}

$expected = "V-02.06"
$expectedTag = "V-02-06"  # nomenclatura usada por los nombres de paquete en GitHub Releases.
$issues = New-Object System.Collections.Generic.List[string]

if ($versionFileValue -ne $expected) {
    $issues.Add("Atlas Balance/VERSION = '$versionFileValue' (esperado '$expected')")
}
if ($informationalVersion -ne $expected) {
    $issues.Add("Directory.Build.props InformationalVersion = '$informationalVersion' (esperado '$expected')")
}
if ($appVersion -ne $expected) {
    $issues.Add("frontend/package.json appVersion = '$appVersion' (esperado '$expected')")
}
if ($seedAppVersion -ne $expected) {
    $issues.Add("SeedData.cs app_version = '$seedAppVersion' (esperado '$expected')")
}
if ($releaseDefault -ne $expectedTag) {
    $issues.Add("release.yml default version = '$releaseDefault' (esperado '$expectedTag')")
}

# Comprobacion cruzada de coherencia: si alguien cambia `V-02.06` por `V-02-07`
# en una de las fuentes, debe cambiarlo en todas (incluido el tag del paquete).
$expectedBase = "0206"
$actualBases = @($versionFileValue, $informationalVersion, $appVersion, $seedAppVersion, $releaseDefault) |
    ForEach-Object { ($_ -replace 'V-', '' -replace '[-.]', '') }
$coherent = ($actualBases | Sort-Object -Unique) -join(',')
if ($coherent -ne $expectedBase) {
    $issues.Add("Las bases de version no convergen en $expectedBase. Valores unicos: $coherent")
}

if ($issues.Count -gt 0) {
    Write-Host "Verificacion de alineacion de version:"
    Write-Host "  VERSION ........................ $versionFileValue"
    Write-Host "  InformationalVersion ........... $informationalVersion"
    Write-Host "  appVersion (frontend) .......... $appVersion"
    Write-Host "  SeedData app_version ........... $seedAppVersion"
    Write-Host "  release.yml default ............ $releaseDefault"
    $msg = "Las siguientes fuentes no coinciden con ${expected}:`n - " + ($issues -join "`n - ")
    Write-Error $msg
    exit 1
}

Write-Host "Alineacion de version OK: todas las fuentes declaran '${expected}'."
