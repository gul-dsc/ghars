<#
    Shared guards and helpers for the Ghars runtime test-fixture tooling.

    Dot-source this from the scripts in this folder:

        . "$PSScriptRoot\GharsTestSafety.ps1"

    Nothing in here touches business behaviour. It exists so that a verification pass can create
    rows in the development database and remove exactly those rows again, without ever matching
    on subject text, program name, notification wording, status, or "rows created recently".

    Why that matters: Notifications carry no foreign key to the booking or activity they describe
    (their only link is the LinkUrl string), so cleanup used to reach for LIKE patterns. A pattern
    of '%/bookings/details/2[0-9]' matches booking 20 as readily as booking 28, and on one pass it
    did exactly that. Everything here is built to make that class of mistake impossible.
#>

Set-StrictMode -Version Latest

# Databases this tooling is ever allowed to write to. A name not on this list is refused outright.
$script:GharsAllowedDatabases = @('GharsPlatformDb')

# Server names considered local. Anything else needs -AllowRemoteServer and a very good reason.
$script:GharsLocalServers = @('.', '(local)', 'localhost', '127.0.0.1', '(localdb)\mssqllocaldb')

<#
    The environment guard.

    Deliberately strict in the safe direction: an UNSET environment variable is refused, not
    assumed to be Development. A machine that has never been told it is a development box is
    exactly the machine where a destructive script should decline to run.
#>
function Assert-GharsDevelopmentEnvironment {
    [CmdletBinding()]
    param(
        [string]$Server = '.',
        [string]$Database = 'GharsPlatformDb',
        [switch]$AllowRemoteServer
    )

    $envName = if ($env:GHARS_TEST_ENVIRONMENT) { $env:GHARS_TEST_ENVIRONMENT }
               elseif ($env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT }
               else { '' }

    if ($envName -ne 'Development') {
        throw ("Refusing to run: environment is '$envName'. This tooling only runs against a " +
               "development database. Set GHARS_TEST_ENVIRONMENT=Development (or " +
               "ASPNETCORE_ENVIRONMENT=Development) if this really is a development machine.")
    }

    if ($Database -notin $script:GharsAllowedDatabases -and $Database -notlike 'GharsPlatformDb_Scratch*') {
        throw ("Refusing to run: database '$Database' is not an allowed development database. " +
               "Allowed: " + ($script:GharsAllowedDatabases -join ', ') + ", or GharsPlatformDb_Scratch*.")
    }

    if (-not $AllowRemoteServer -and $Server.ToLowerInvariant() -notin $script:GharsLocalServers) {
        throw ("Refusing to run: server '$Server' is not a local instance. Pass -AllowRemoteServer " +
               "only if you are certain this is not a production host.")
    }

    Write-Verbose "Environment guard passed: $envName on $Server/$Database"
}

<#
    Run a query and return its rows as objects.

    sqlcmd is used rather than Invoke-Sqlcmd because it is present on this machine without the
    SqlServer PowerShell module. SET NOCOUNT ON is prepended so row-count chatter never reaches
    the parser.
#>
function Invoke-GharsQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$Server = '.',
        [string]$Database = 'GharsPlatformDb',
        [string[]]$Columns
    )

    # -I sets QUOTED_IDENTIFIER ON. sqlcmd defaults it OFF, and SQL Server refuses any INSERT, UPDATE
    # or DELETE against a table carrying a filtered index while it is OFF — which Surveys and
    # SurveyResponses now do. Without this a cleanup fails with a message about indexed views and
    # spatial indexes that says nothing about the actual cause.
    $full = "SET NOCOUNT ON;`n$Query"
    $raw = & sqlcmd -S $Server -d $Database -E -h -1 -W -s '|' -b -I -Q $full 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed (exit $LASTEXITCODE): $($raw -join [Environment]::NewLine)"
    }

    $lines = @($raw | ForEach-Object { "$_" } | Where-Object { $_.Trim().Length -gt 0 })

    # The leading comma matters. Without it PowerShell unrolls a one-element array into a bare
    # string, and a caller reading $result[0] to get a scalar would get the first CHARACTER of it
    # instead — turning a count of 12 into 1, silently and only sometimes.
    if (-not $Columns) { return ,$lines }

    $rows = @($lines | ForEach-Object {
        $parts = $_ -split '\|'
        if ($parts.Count -ne $Columns.Count) { return }
        $row = [ordered]@{}
        for ($i = 0; $i -lt $Columns.Count; $i++) { $row[$Columns[$i]] = $parts[$i].Trim() }
        [pscustomobject]$row
    })
    return ,$rows
}

<# Run a statement batch that returns no rows. Used only by the cleanup script, inside a transaction. #>
function Invoke-GharsStatement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Sql,
        [string]$Server = '.',
        [string]$Database = 'GharsPlatformDb'
    )

    # -I as above: required for any write to a table with a filtered index.
    $raw = & sqlcmd -S $Server -d $Database -E -W -b -I -Q $Sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed (exit $LASTEXITCODE): $($raw -join [Environment]::NewLine)"
    }
    $raw | ForEach-Object { "$_" } | Where-Object { $_.Trim().Length -gt 0 }
}

<#
    The tables a verification pass can plausibly write to, in the order a full teardown must
    follow: children before parents. Cleanup derives its order from this list rather than from
    the order fixtures happen to appear in a manifest.
#>
function Get-GharsFixtureTables {
    @(
        'NotificationDeliveries'
        'Notifications'
        'AgendaMedia'
        'AgendaEntries'
        'BookingAuditTrails'
        'BookingProposedTimeOptions'
        'BookingRequests'
        'AttendanceRecords'
        'AttendanceSessions'
        'Certificates'
        'Surveys'
        'ActivityAttachments'
        'ActivitySpeakers'
        'GalleryItems'
        'MediaAlbums'
        'Activities'
        'OrganizationAdminLinks'
        'OrganizationContacts'
        'OrganizationDocuments'
        'PartnerProfiles'
        'KpiSubmissions'
        'Organizations'
        'ContactMessages'
        'SystemAuditLogs'
    )
}

<# Which of those tables actually exist in this database, and which of them have an Id column. #>
function Get-GharsExistingTables {
    [CmdletBinding()]
    param(
        [string]$Server = '.',
        [string]$Database = 'GharsPlatformDb'
    )

    $names = (Get-GharsFixtureTables | ForEach-Object { "'$_'" }) -join ','
    $q = @"
SELECT t.name,
       CASE WHEN EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = 'Id')
            THEN 1 ELSE 0 END
FROM sys.tables t
WHERE t.name IN ($names);
"@
    Invoke-GharsQuery -Query $q -Server $Server -Database $Database -Columns @('Name', 'HasId')
}

<# Read a manifest or baseline JSON file, failing with a useful message rather than a null. #>
function Read-GharsJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File not found: $Path"
    }
    try {
        Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        throw "Could not parse JSON at ${Path}: $($_.Exception.Message)"
    }
}
