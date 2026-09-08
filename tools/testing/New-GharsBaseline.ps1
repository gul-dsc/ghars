<#
.SYNOPSIS
    Records the pre-test baseline for a runtime verification pass.

.DESCRIPTION
    Captures, for every table a verification pass can write to:

      * the row count, and
      * the current MAX(Id) — the high-water mark.

    The high-water mark is the important half. Any row a test creates afterwards necessarily has
    an Id above it, so cleanup can refuse, mechanically, to delete anything that existed before the
    run. That single rule is what turns "delete the rows the test made" from a text-matching
    exercise into an arithmetic one.

    The counts are a verification signal only. Comparing them afterwards tells you whether cleanup
    was complete; it must never be used to decide WHAT to delete. A count that is higher than
    baseline is a prompt to go and look, not a licence to delete the difference.

.PARAMETER Path
    Where to write the baseline. Defaults to runs\baseline.json beside this script.

.PARAMETER ProtectedId
    Rows that must never be deleted by the cleanup tooling regardless of anything else, given as
    'Table:Id'. Booking 20 is protected by default: it is legitimate development data that a
    previous cleanup pass damaged, and it is worth naming explicitly as well as relying on the
    high-water mark.

.EXAMPLE
    $env:GHARS_TEST_ENVIRONMENT = 'Development'
    .\New-GharsBaseline.ps1
#>
[CmdletBinding()]
param(
    [string]$Server = '.',
    [string]$Database = 'GharsPlatformDb',
    [string]$Path,
    [string[]]$ProtectedId = @('BookingRequests:20'),
    [switch]$AllowRemoteServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\GharsTestSafety.ps1"

Assert-GharsDevelopmentEnvironment -Server $Server -Database $Database -AllowRemoteServer:$AllowRemoteServer

if (-not $Path) {
    $runs = Join-Path $PSScriptRoot 'runs'
    if (-not (Test-Path -LiteralPath $runs)) { New-Item -ItemType Directory -Path $runs | Out-Null }
    $Path = Join-Path $runs 'baseline.json'
}

$tables = Get-GharsExistingTables -Server $Server -Database $Database
if (-not $tables) { throw "No known fixture tables found in $Database. Is this the right database?" }

# One UNION ALL rather than a query per table, so the whole baseline is a single point in time.
$selects = foreach ($t in $tables) {
    if ($t.HasId -eq '1') {
        "SELECT '$($t.Name)', COUNT(*), ISNULL(MAX(Id), 0) FROM dbo.[$($t.Name)]"
    } else {
        "SELECT '$($t.Name)', COUNT(*), -1 FROM dbo.[$($t.Name)]"
    }
}
$rows = Invoke-GharsQuery -Query ($selects -join "`nUNION ALL ") -Server $Server -Database $Database `
                          -Columns @('Table', 'Count', 'MaxId')

$watermarks = [ordered]@{}
$counts     = [ordered]@{}
foreach ($r in $rows | Sort-Object Table) {
    $counts[$r.Table] = [int]$r.Count
    if ([int]$r.MaxId -ge 0) { $watermarks[$r.Table] = [int]$r.MaxId }
}

# Confirm every protected row exists now, so a later "it is still there" check means something.
$protected = @()
foreach ($p in $ProtectedId) {
    $parts = $p -split ':', 2
    if ($parts.Count -ne 2) { throw "ProtectedId must be 'Table:Id'; got '$p'." }
    $tableName = $parts[0]; $rowId = [int]$parts[1]
    $exists = Invoke-GharsQuery -Server $Server -Database $Database `
        -Query "SELECT COUNT(*) FROM dbo.[$tableName] WHERE Id = $rowId;"
    if ([int]($exists[0]) -ne 1) { throw "Protected row $p does not exist — refusing to write a baseline that claims it does." }
    $protected += [ordered]@{ table = $tableName; id = $rowId }
}

$baseline = [ordered]@{
    schema        = 'ghars-test-baseline/1'
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    server        = $Server
    database      = $Database
    watermarks    = $watermarks
    counts        = $counts
    protectedRows = $protected
}

$baseline | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding UTF8

Write-Host "Baseline written to $Path" -ForegroundColor Green
Write-Host ("  {0} tables, captured {1}" -f $counts.Count, $baseline.capturedAtUtc)
Write-Host ("  high-water marks: BookingRequests={0} Activities={1} Notifications={2}" -f `
    $watermarks['BookingRequests'], $watermarks['Activities'], $watermarks['Notifications'])
Write-Host ("  protected: {0}" -f (($protected | ForEach-Object { "$($_.table):$($_.id)" }) -join ', '))
