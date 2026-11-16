param([string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path)

$ErrorActionPreference = "Stop"
$scanner = Join-Path $PSScriptRoot "Test-AtlasSecrets.ps1"
$fixtureRoot = Join-Path $PSScriptRoot ".scanner-fixtures"
$excludedRoot = Join-Path $fixtureRoot "bin"
$bait = 'sk_atlas_balance_' + ('A' * 32)
$hostExecutable = (Get-Process -Id $PID).Path

function Invoke-Scanner {
    param([string]$Root = $RepoRoot)

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $hostExecutable -NoProfile -File $scanner -Root $Root *> $null
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureRoot "positive.txt") -Value $bait -Encoding UTF8
    if ((Invoke-Scanner) -eq 0) { throw "El scanner no detecto el fixture positivo." }

    Remove-Item -LiteralPath (Join-Path $fixtureRoot "positive.txt") -Force
    New-Item -ItemType Directory -Path $excludedRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $excludedRoot "excluded.txt") -Value $bait -Encoding UTF8
    if ((Invoke-Scanner) -ne 0) { throw "El scanner analizo un fixture dentro de bin/." }

    # Casos de universo git vs. recorrido completo.
    $git = Get-Command git -ErrorAction SilentlyContinue
    $tempBase = Join-Path ([IO.Path]::GetTempPath()) ("atlas-scanner-" + [guid]::NewGuid().ToString("N"))
    $configRel = Join-Path (Join-Path "Atlas Balance" "src") "appsettings.Development.json"

    # (3) Directorio sin git con secreto -> se reporta (recorrido completo).
    $plainRoot = Join-Path $tempBase "plain"
    New-Item -ItemType Directory -Path (Join-Path $plainRoot "Atlas Balance") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $plainRoot "Atlas Balance/leak.txt") -Value $bait -Encoding UTF8
    if ((Invoke-Scanner -Root $plainRoot) -eq 0) { throw "El scanner no detecto un secreto en un directorio sin git." }

    if ($null -ne $git) {
        $gitRoot = Join-Path $tempBase "repo"
        New-Item -ItemType Directory -Path (Join-Path $gitRoot "Atlas Balance/src") -Force | Out-Null
        & git -C $gitRoot init --quiet
        if ($LASTEXITCODE -ne 0) { throw "git init fallo en el fixture." }
        Set-Content -LiteralPath (Join-Path $gitRoot ".gitignore") -Value "appsettings.Development.json" -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $gitRoot $configRel) -Value $bait -Encoding UTF8

        # (1) Repo git con fichero ignorado que contiene un secreto -> no se reporta.
        if ((Invoke-Scanner -Root $gitRoot) -ne 0) { throw "El scanner reporto un fichero ignorado por git." }

        # (2) Fichero untracked no ignorado con secreto -> se reporta.
        Set-Content -LiteralPath (Join-Path $gitRoot "Atlas Balance/src/leak.txt") -Value $bait -Encoding UTF8
        if ((Invoke-Scanner -Root $gitRoot) -eq 0) { throw "El scanner no detecto un fichero untracked no ignorado." }
    }
    else {
        Write-Host "git no disponible: casos de repo git omitidos."
    }

    Write-Host "Fixtures del scanner OK."
}
finally {
    if ($null -ne $tempBase -and (Test-Path -LiteralPath $tempBase)) { Remove-Item -LiteralPath $tempBase -Recurse -Force }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
