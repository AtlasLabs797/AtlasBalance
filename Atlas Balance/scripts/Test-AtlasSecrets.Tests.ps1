param([string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path)

$ErrorActionPreference = "Stop"
$scanner = Join-Path $PSScriptRoot "Test-AtlasSecrets.ps1"
$fixtureRoot = Join-Path $PSScriptRoot ".scanner-fixtures"
$excludedRoot = Join-Path $fixtureRoot "bin"
$bait = 'sk_atlas_balance_' + ('A' * 32)
$hostExecutable = (Get-Process -Id $PID).Path

function Invoke-Scanner {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $hostExecutable -NoProfile -File $scanner -Root $RepoRoot *> $null
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

    Write-Host "Fixtures del scanner OK."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
