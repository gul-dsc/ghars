<#
.SYNOPSIS
    Removes exactly the rows a verification pass created, and nothing else.

.DESCRIPTION
    Reads a fixture manifest written during a test run (see fixture-manifest.mjs) and deletes the
    rows it names, together with their dependants, in foreign-key-safe order.

    The rules this script exists to enforce:

      * Fixtures are identified by Id. Never by subject text, program name, notification wording,
        organization name, status, or "created in the last N hours".
      * Every row in the delete plan must have an Id above the baseline high-water mark for its
        table. A row that existed before the run cannot be deleted, and the script aborts rather
        than skipping it — an unexpected id means the manifest is wrong, and a wrong manifest is
        not something to work around silently.
      * Explicitly protected rows abort the run outright.
      * A dependant that is NOT part of the manifest blocks its parent's deletion. Cascading into
        rows nobody claimed is how a teardown turns into a data loss.
      * Dry run is the default. Deleting requires -Execute.

    This is not a general-purpose database cleaner. It understands a fixed set of fixture kinds
    with fixed dependency chains, and will refuse anything else.

.PARAMETER Manifest
    Path to the run manifest produced by the test suite.

.PARAMETER Execute
    Actually delete. Without this the script prints the plan and stops.

.EXAMPLE
    $env:GHARS_TEST_ENVIRONMENT = 'Development'
    .\Remove-GharsFixtures.ps1 -Manifest runs\2026-09-08-dual-booking.json
    .\Remove-GharsFixtures.ps1 -Manifest runs\2026-09-08-dual-booking.json -Execute
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Manifest,
    [string]$Server,
    [string]$Database,
    [switch]$Execute,
    [switch]$AllowRemoteServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\GharsTestSafety.ps1"

# ---------------------------------------------------------------------------- manifest + baseline

$run = Read-GharsJson -Path $Manifest
if ($run.schema -ne 'ghars-test-manifest/1') {
    throw "Unrecognised manifest schema '$($run.schema)'. Expected 'ghars-test-manifest/1'."
}
if (-not $run.baseline) {
    throw "Manifest has no embedded baseline. Run New-GharsBaseline.ps1 before the test run — cleanup without a high-water mark is exactly what this tooling exists to prevent."
}

if (-not $Server)   { $Server   = $run.baseline.server }
if (-not $Database) { $Database = $run.baseline.database }
Assert-GharsDevelopmentEnvironment -Server $Server -Database $Database -AllowRemoteServer:$AllowRemoteServer

$wm = @{}
foreach ($p in $run.baseline.watermarks.PSObject.Properties) { $wm[$p.Name] = [int]$p.Value }

$protected = @{}
foreach ($p in $run.baseline.protectedRows) { $protected["$($p.table):$($p.id)"] = $true }

$fixtures = @($run.fixtures)
if (-not $fixtures) {
    Write-Host "Manifest records no fixtures. Nothing to remove." -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "Run      : $($run.runId)" -ForegroundColor Cyan
Write-Host "Database : $Server/$Database"
Write-Host "Baseline : $($run.baseline.capturedAtUtc)"
Write-Host "Fixtures : $($fixtures.Count)"
Write-Host ""

# ---------------------------------------------------------------------------- helpers

function Get-Ids {
    param([string]$Query)
    $out = Invoke-GharsQuery -Query $Query -Server $Server -Database $Database
    @($out | Where-Object { $_ -match '^\s*-?\d+\s*$' } | ForEach-Object { [int]$_.Trim() })
}

function Join-Ids { param([int[]]$Ids) ($Ids | ForEach-Object { $_ }) -join ',' }

$plan = [ordered]@{}   # Table -> [int[]]
function Add-ToPlan {
    param([string]$Table, [int[]]$Ids)
    if (-not $Ids -or $Ids.Count -eq 0) { return }
    if (-not $plan.Contains($Table)) { $plan[$Table] = @() }
    $plan[$Table] = @($plan[$Table] + $Ids | Sort-Object -Unique)
}

$blockers = @()
function Add-Blocker { param([string]$Message) $script:blockers += $Message }

# ---------------------------------------------------------------------------- expand fixtures

$byKind = @{}
foreach ($f in $fixtures) {
    if (-not $byKind.ContainsKey($f.kind)) { $byKind[$f.kind] = @() }
    $byKind[$f.kind] += [int]$f.id
}

$known = @('booking', 'activity', 'organization', 'contactMessage', 'notification', 'agendaEntry')
foreach ($k in $byKind.Keys) {
    if ($k -notin $known) {
        throw "Unknown fixture kind '$k'. Known kinds: $($known -join ', '). This tool deletes only shapes it understands."
    }
}

$bookingIds  = @(if ($byKind.ContainsKey('booking'))  { $byKind['booking'] })
$activityIds = @(if ($byKind.ContainsKey('activity')) { $byKind['activity'] })
$orgIds      = @(if ($byKind.ContainsKey('organization')) { $byKind['organization'] })

# --- bookings ---------------------------------------------------------------
foreach ($b in $bookingIds) {
    $agenda = Get-Ids "SELECT Id FROM dbo.AgendaEntries WHERE BookingRequestId = $b;"
    if ($agenda) {
        Add-ToPlan 'AgendaMedia' (Get-Ids "SELECT Id FROM dbo.AgendaMedia WHERE AgendaEntryId IN ($(Join-Ids $agenda));")
        $gal = Get-Ids "SELECT Id FROM dbo.GalleryItems WHERE AgendaEntryId IN ($(Join-Ids $agenda));"
        if ($gal) { Add-Blocker "Gallery items $(Join-Ids $gal) reference agenda entries of booking $b and are not in the manifest." }
        Add-ToPlan 'AgendaEntries' $agenda
    }
    # Exact LinkUrl equality — never a LIKE. '/bookings/details/20' cannot match '/bookings/details/28'.
    Add-ToPlan 'Notifications' (Get-Ids "SELECT Id FROM dbo.Notifications WHERE LinkUrl = '/bookings/details/$b' AND Id > $($wm['Notifications']);")
    Add-ToPlan 'BookingAuditTrails' (Get-Ids "SELECT Id FROM dbo.BookingAuditTrails WHERE BookingRequestId = $b;")
    Add-ToPlan 'BookingProposedTimeOptions' (Get-Ids "SELECT Id FROM dbo.BookingProposedTimeOptions WHERE BookingRequestId = $b;")
    Add-ToPlan 'BookingRequests' @($b)
}

# --- activities -------------------------------------------------------------
foreach ($a in $activityIds) {
    $stray = Get-Ids "SELECT Id FROM dbo.BookingRequests WHERE ActivityId = $a;"
    $unclaimed = @($stray | Where-Object { $_ -notin $bookingIds })
    if ($unclaimed) { Add-Blocker "Bookings $(Join-Ids $unclaimed) reference activity $a and are not in the manifest." }

    foreach ($t in @('GalleryItems', 'MediaAlbums')) {
        $ref = Get-Ids "SELECT Id FROM dbo.$t WHERE ActivityId = $a;"
        if ($ref) { Add-Blocker "$t $(Join-Ids $ref) reference activity $a and are not in the manifest." }
    }

    $sessions = Get-Ids "SELECT Id FROM dbo.AttendanceSessions WHERE ActivityId = $a;"
    if ($sessions) {
        Add-ToPlan 'AttendanceRecords' (Get-Ids "SELECT Id FROM dbo.AttendanceRecords WHERE AttendanceSessionId IN ($(Join-Ids $sessions));")
        Add-ToPlan 'AttendanceSessions' $sessions
    }
    Add-ToPlan 'Certificates' (Get-Ids "SELECT Id FROM dbo.Certificates WHERE ActivityId = $a;")
    Add-ToPlan 'Surveys' (Get-Ids "SELECT Id FROM dbo.Surveys WHERE ActivityId = $a;")
    Add-ToPlan 'ActivityAttachments' (Get-Ids "SELECT Id FROM dbo.ActivityAttachments WHERE ActivityId = $a;")
    Add-ToPlan 'ActivitySpeakers' (Get-Ids "SELECT Id FROM dbo.ActivitySpeakers WHERE ActivityId = $a;")
    Add-ToPlan 'Notifications' (Get-Ids "SELECT Id FROM dbo.Notifications WHERE LinkUrl = '/Admin/Activities/Details/$a' AND Id > $($wm['Notifications']);")
    Add-ToPlan 'SystemAuditLogs' (Get-Ids "SELECT Id FROM dbo.SystemAuditLogs WHERE EntityName = 'Activity' AND TRY_CAST(EntityId AS int) = $a AND Id > $($wm['SystemAuditLogs']);")
    Add-ToPlan 'Activities' @($a)
}

# --- organizations ----------------------------------------------------------
foreach ($o in $orgIds) {
    $refBookings = Get-Ids "SELECT Id FROM dbo.BookingRequests WHERE OrganizationId = $o OR PartnerOrganizationId = $o;"
    $unclaimed = @($refBookings | Where-Object { $_ -notin $bookingIds })
    if ($unclaimed) { Add-Blocker "Bookings $(Join-Ids $unclaimed) reference organization $o and are not in the manifest." }

    $refActivities = Get-Ids "SELECT Id FROM dbo.Activities WHERE PartnerOrganizationId = $o;"
    $unclaimedAct = @($refActivities | Where-Object { $_ -notin $activityIds })
    if ($unclaimedAct) { Add-Blocker "Activities $(Join-Ids $unclaimedAct) reference organization $o and are not in the manifest." }

    foreach ($pair in @(@('AgendaEntries', 'OrganizationId'), @('KpiSubmissions', 'OrganizationId'), @('GharsAnnualReports', 'OrganizationId'))) {
        $ref = Get-Ids "SELECT Id FROM dbo.$($pair[0]) WHERE $($pair[1]) = $o;"
        $left = @($ref | Where-Object { -not ($plan.Contains($pair[0]) -and $_ -in $plan[$pair[0]]) })
        if ($left) { Add-Blocker "$($pair[0]) $(Join-Ids $left) reference organization $o and are not in the manifest." }
    }

    foreach ($t in @('OrganizationAdminLinks', 'OrganizationContacts', 'OrganizationDocuments', 'PartnerProfiles')) {
        Add-ToPlan $t (Get-Ids "SELECT Id FROM dbo.$t WHERE OrganizationId = $o;")
    }
    Add-ToPlan 'Organizations' @($o)
}

# --- contact messages, standalone notifications and agenda entries ----------
foreach ($c in @(if ($byKind.ContainsKey('contactMessage')) { $byKind['contactMessage'] })) {
    Add-ToPlan 'Notifications' (Get-Ids "SELECT Id FROM dbo.Notifications WHERE LinkUrl = '/Admin/ContactMessages/Details/$c' AND Id > $($wm['Notifications']);")
    Add-ToPlan 'ContactMessages' @($c)
}
foreach ($n in @(if ($byKind.ContainsKey('notification')) { $byKind['notification'] })) {
    Add-ToPlan 'Notifications' @($n)
}
foreach ($e in @(if ($byKind.ContainsKey('agendaEntry')) { $byKind['agendaEntry'] })) {
    Add-ToPlan 'AgendaMedia' (Get-Ids "SELECT Id FROM dbo.AgendaMedia WHERE AgendaEntryId = $e;")
    Add-ToPlan 'AgendaEntries' @($e)
}

# Deliveries follow whatever notifications ended up in the plan.
if ($plan.Contains('Notifications')) {
    Add-ToPlan 'NotificationDeliveries' (Get-Ids "SELECT Id FROM dbo.NotificationDeliveries WHERE NotificationId IN ($(Join-Ids $plan['Notifications']));")
}

# ---------------------------------------------------------------------------- guards

$violations = @()

foreach ($table in $plan.Keys) {
    foreach ($id in $plan[$table]) {
        if ($protected.ContainsKey("${table}:$id")) {
            $violations += "PROTECTED  ${table}:$id is on the protected list and must never be deleted."
        }
        if ($wm.ContainsKey($table) -and $id -le $wm[$table]) {
            $violations += "PRE-EXISTING  ${table}:$id is at or below the baseline high-water mark ($($wm[$table])) — it existed before this run."
        }
    }
}

# ---------------------------------------------------------------------------- report the plan

Write-Host "Delete plan (exact ids):" -ForegroundColor Cyan
$total = 0
foreach ($table in Get-GharsFixtureTables) {
    if (-not $plan.Contains($table)) { continue }
    $ids = $plan[$table]
    $total += $ids.Count
    Write-Host ("  {0,-28} {1,3}  [{2}]" -f $table, $ids.Count, (Join-Ids $ids))
}
$unordered = @($plan.Keys | Where-Object { $_ -notin (Get-GharsFixtureTables) })
foreach ($table in $unordered) {
    Write-Host ("  {0,-28} {1,3}  [{2}]  (no declared delete order)" -f $table, $plan[$table].Count, (Join-Ids $plan[$table])) -ForegroundColor Yellow
    $violations += "UNORDERED  $table has no position in the declared dependency order; refusing to guess."
}
Write-Host ("  {0,-28} {1,3}" -f 'TOTAL', $total)

if ($run.PSObject.Properties.Name -contains 'modified' -and $run.modified) {
    Write-Host ""
    Write-Host "Values to restore:" -ForegroundColor Cyan
    foreach ($m in $run.modified) {
        Write-Host ("  {0}:{1}.{2} -> {3}" -f $m.table, $m.id, $m.column, ($m.original ?? 'NULL'))
    }
}

if ($blockers) {
    Write-Host ""
    Write-Host "Blocked by rows outside the manifest:" -ForegroundColor Red
    $blockers | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}
if ($violations) {
    Write-Host ""
    Write-Host "Guard violations:" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}
if ($blockers -or $violations) {
    throw "Aborting without deleting anything. Fix the manifest, or remove the offending rows deliberately by hand."
}

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry run — nothing deleted. Re-run with -Execute to apply." -ForegroundColor Yellow
    return
}

# ---------------------------------------------------------------------------- execute

function ConvertTo-SqlLiteral {
    param($Value)
    if ($null -eq $Value) { return 'NULL' }
    if ($Value -is [bool]) { return $(if ($Value) { '1' } else { '0' }) }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) { return "$Value" }
    "N'" + ($Value -replace "'", "''") + "'"
}

$sql = [System.Text.StringBuilder]::new()
[void]$sql.AppendLine('SET NOCOUNT ON;')
[void]$sql.AppendLine('SET XACT_ABORT ON;')
[void]$sql.AppendLine('BEGIN TRANSACTION;')
foreach ($table in Get-GharsFixtureTables) {
    if (-not $plan.Contains($table)) { continue }
    [void]$sql.AppendLine("DELETE FROM dbo.[$table] WHERE Id IN ($(Join-Ids $plan[$table]));")
    [void]$sql.AppendLine("IF @@ROWCOUNT <> $($plan[$table].Count) BEGIN ROLLBACK TRANSACTION; THROW 50001, 'Row count mismatch on $table', 1; END;")
}
if ($run.PSObject.Properties.Name -contains 'modified') {
    foreach ($m in $run.modified) {
        [void]$sql.AppendLine("UPDATE dbo.[$($m.table)] SET [$($m.column)] = $(ConvertTo-SqlLiteral $m.original) WHERE Id = $([int]$m.id);")
    }
}
[void]$sql.AppendLine('COMMIT TRANSACTION;')

Write-Host ""
Write-Host "Executing..." -ForegroundColor Yellow
Invoke-GharsStatement -Sql $sql.ToString() -Server $Server -Database $Database | ForEach-Object { Write-Host "  $_" }

# ---------------------------------------------------------------------------- verify

Write-Host ""
Write-Host "Verification:" -ForegroundColor Cyan

foreach ($p in $run.baseline.protectedRows) {
    $n = Invoke-GharsQuery -Server $Server -Database $Database -Query "SELECT COUNT(*) FROM dbo.[$($p.table)] WHERE Id = $($p.id);"
    $ok = ([int]($n[0]) -eq 1)
    Write-Host ("  protected {0}:{1} present ... {2}" -f $p.table, $p.id, $(if ($ok) { 'yes' } else { 'MISSING' })) `
        -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

if ($run.PSObject.Properties.Name -contains 'modified') {
    foreach ($m in $run.modified) {
        $cur = Invoke-GharsQuery -Server $Server -Database $Database -Query "SELECT CAST([$($m.column)] AS nvarchar(100)) FROM dbo.[$($m.table)] WHERE Id = $([int]$m.id);"
        $expected = if ($null -eq $m.original) { 'NULL' } elseif ($m.original -is [bool]) { $(if ($m.original) { '1' } else { '0' }) } else { "$($m.original)" }
        $actual = if ($cur) { "$($cur[0])".Trim() } else { 'NULL' }
        $ok = ($actual -eq $expected)
        Write-Host ("  restored {0}:{1}.{2} = {3} (expected {4}) ... {5}" -f $m.table, $m.id, $m.column, $actual, $expected, $(if ($ok) { 'ok' } else { 'MISMATCH' })) `
            -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    }
}

Write-Host ""
Write-Host "Now run Compare-GharsBaseline.ps1 to confirm the database is back at baseline." -ForegroundColor Cyan
