<#
.SYNOPSIS
    Compares the current database against a recorded baseline.

.DESCRIPTION
    Reports which tables have drifted from the baseline captured by New-GharsBaseline.ps1, and
    confirms that every protected row is still present.

    This is a VERIFICATION SIGNAL, not a cleanup mechanism. It deliberately has no delete path.
    A table sitting above its baseline count means "go and find out why" — it never means "remove
    the difference". Deleting rows because a count looks too high is how legitimate data gets lost:
    the excess may be seeder output from an application restart, someone else's work on a shared
    development database, or a fixture that a manifest failed to record.

    Development seeding in particular can create attendance and certificate rows when the
    application restarts. Those are seeder side effects, not test fixtures, and they belong to the
    application rather than to any test run. -ExpectSeederDrift lists the tables where that is
    normal so they are reported separately instead of as failures.

.EXAMPLE
    .\Compare-GharsBaseline.ps1 -Baseline runs\baseline.json
#>
[CmdletBinding()]
param(
    [string]$Baseline,
    [string]$Server,
    [string]$Database,
    [string[]]$ExpectSeederDrift = @('AttendanceRecords', 'AttendanceSessions', 'Certificates'),
    [switch]$AllowRemoteServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\GharsTestSafety.ps1"

if (-not $Baseline) { $Baseline = Join-Path $PSScriptRoot 'runs\baseline.json' }
$base = Read-GharsJson -Path $Baseline
if ($base.schema -ne 'ghars-test-baseline/1') { throw "Unrecognised baseline schema '$($base.schema)'." }

if (-not $Server)   { $Server   = $base.server }
if (-not $Database) { $Database = $base.database }
Assert-GharsDevelopmentEnvironment -Server $Server -Database $Database -AllowRemoteServer:$AllowRemoteServer

$tables = Get-GharsExistingTables -Server $Server -Database $Database
$selects = foreach ($t in $tables) { "SELECT '$($t.Name)', COUNT(*) FROM dbo.[$($t.Name)]" }
$rows = Invoke-GharsQuery -Query ($selects -join "`nUNION ALL ") -Server $Server -Database $Database `
                          -Columns @('Table', 'Count')

$now = @{}
foreach ($r in $rows) { $now[$r.Table] = [int]$r.Count }

Write-Host ""
Write-Host "Baseline : $($base.capturedAtUtc)  ($Server/$Database)" -ForegroundColor Cyan
Write-Host ""

$drift = @(); $seederDrift = @()
foreach ($p in $base.counts.PSObject.Properties | Sort-Object Name) {
    $table = $p.Name
    if (-not $now.ContainsKey($table)) { continue }
    $delta = $now[$table] - [int]$p.Value
    if ($delta -eq 0) { continue }
    $line = "{0,-28} {1,5} -> {2,-5} ({3:+#;-#;0})" -f $table, [int]$p.Value, $now[$table], $delta
    if ($table -in $ExpectSeederDrift) { $seederDrift += $line } else { $drift += $line }
}

if ($drift) {
    Write-Host "Drift from baseline:" -ForegroundColor Yellow
    $drift | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "  Investigate each of these. Do NOT delete rows to make the numbers match." -ForegroundColor Yellow
} else {
    Write-Host "All tracked tables match baseline." -ForegroundColor Green
}

if ($seederDrift) {
    Write-Host ""
    Write-Host "Expected seeder drift (application restart, not test fixtures):" -ForegroundColor DarkGray
    $seederDrift | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
}

Write-Host ""
$missing = 0
foreach ($p in $base.protectedRows) {
    $n = Invoke-GharsQuery -Server $Server -Database $Database -Query "SELECT COUNT(*) FROM dbo.[$($p.table)] WHERE Id = $($p.id);"
    $ok = ([int]($n[0]) -eq 1)
    if (-not $ok) { $missing++ }
    Write-Host ("Protected {0}:{1} ... {2}" -f $p.table, $p.id, $(if ($ok) { 'present' } else { 'MISSING' })) `
        -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

if ($missing -gt 0) { throw "$missing protected row(s) missing. Something deleted data it should not have." }
exit $(if ($drift) { 1 } else { 0 })
