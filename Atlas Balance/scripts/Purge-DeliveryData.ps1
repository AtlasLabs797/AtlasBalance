param(
    [switch]$ConfirmDeliveryPurge,
    [string]$ContainerName = "atlas_balance_db",
    [string]$Database = "atlas_balance",
    [string]$DbUser = "postgres",
    [string]$SqlPath = ""
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmDeliveryPurge -and $env:ATLAS_CONFIRM_DELIVERY_PURGE -ne "BORRAR_DATOS") {
    throw "Purga cancelada. Ejecuta con -ConfirmDeliveryPurge o define ATLAS_CONFIRM_DELIVERY_PURGE=BORRAR_DATOS."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($SqlPath)) {
    $SqlPath = Join-Path $scriptRoot "purge-delivery-data.sql"
}

if (-not (Test-Path -LiteralPath $SqlPath)) {
    throw "No se encontro el SQL de purga: $SqlPath"
}

$containerSqlPath = "/tmp/atlas-balance-purge-delivery-data.sql"

Write-Host "Copiando SQL de purga al contenedor $ContainerName..."
& docker cp $SqlPath "${ContainerName}:$containerSqlPath"
if ($LASTEXITCODE -ne 0) { throw "docker cp fallo." }

Write-Host "Ejecutando purga de datos sensibles en $Database..."
& docker exec $ContainerName psql -U $DbUser -d $Database -v ON_ERROR_STOP=1 -f $containerSqlPath
if ($LASTEXITCODE -ne 0) { throw "psql fallo durante la purga." }

Write-Host "Purga finalizada. La base queda sin usuarios, titulares, cuentas, extractos, tokens ni auditorias operativas."
