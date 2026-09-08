<#
.SYNOPSIS
    Creates (or drops) a throwaway Ghars database built from the migration chain.

.DESCRIPTION
    A verification pass that does not need the populated development data should not run against
    it. A scratch database starts empty, so nothing a test does can damage anything that matters,
    and cleanup becomes "drop the database" rather than a delete plan.

    Use the development database only for tests that genuinely need its data or configuration:
    the seeded organizations, the role/organization links, the demo accounts, existing approved
    offerings. Everything else — schema behaviour, validation rules, migrations, controller
    routing — runs perfectly well on an empty schema.

    The scratch database is created by applying the migration chain, so it matches production
    schema exactly and no migration is invented for testing purposes.

.PARAMETER Label
    Short suffix identifying the scratch database, e.g. 'booking' -> GharsPlatformDb_Scratch_booking.

.PARAMETER Drop
    Drop the scratch database instead of creating it. Scratch databases must not be left behind.

.EXAMPLE
    $env:GHARS_TEST_ENVIRONMENT = 'Development'
    .\New-GharsScratchDatabase.ps1 -Label booking
    # ... run tests against the printed connection string ...
    .\New-GharsScratchDatabase.ps1 -Label booking -Drop
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_]{1,32}$')][string]$Label,
    [string]$Server = '.',
    [switch]$Drop,
    [switch]$AllowRemoteServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\GharsTestSafety.ps1"

$dbName = "GharsPlatformDb_Scratch_$Label"

# The guard accepts GharsPlatformDb_Scratch* by design; the name is still checked here so a typo
# can never send a DROP at the real development database.
Assert-GharsDevelopmentEnvironment -Server $Server -Database $dbName -AllowRemoteServer:$AllowRemoteServer
if ($dbName -notlike 'GharsPlatformDb_Scratch_*') { throw "Refusing to act on '$dbName' — not a scratch database name." }

$conn = "Server=$Server;Database=$dbName;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

if ($Drop) {
    $sql = "IF DB_ID('$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END"
    & sqlcmd -S $Server -d master -E -b -Q $sql | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to drop $dbName." }
    Write-Host "Dropped $dbName." -ForegroundColor Green
    return
}

$exists = & sqlcmd -S $Server -d master -E -h -1 -W -b -Q "SELECT CASE WHEN DB_ID('$dbName') IS NULL THEN 0 ELSE 1 END;"
if ("$exists".Trim() -eq '1') {
    throw "$dbName already exists. Drop it first (-Drop) rather than reusing a database of unknown state."
}

Write-Host "Applying migrations to $dbName ..." -ForegroundColor Cyan
$previous = $env:ConnectionStrings__DefaultConnection
try {
    $env:ConnectionStrings__DefaultConnection = $conn
    & dotnet ef database update --project "$projectRoot"
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed for $dbName." }
} finally {
    $env:ConnectionStrings__DefaultConnection = $previous
}

Write-Host ""
Write-Host "Scratch database ready." -ForegroundColor Green
Write-Host "  Name       : $dbName"
Write-Host "  Connection : $conn"
Write-Host ""
Write-Host "Point the application at it with:" -ForegroundColor Cyan
Write-Host "  `$env:ConnectionStrings__DefaultConnection = '$conn'"
Write-Host ""
Write-Host "Drop it when the run finishes:" -ForegroundColor Yellow
Write-Host "  .\New-GharsScratchDatabase.ps1 -Label $Label -Drop"
