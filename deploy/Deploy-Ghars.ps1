#requires -Version 5.1
<#
.SYNOPSIS
    Deploys a published Ghars Platform build onto the local IIS site.

.DESCRIPTION
    Runs on the web server itself - as a step on a self-hosted Azure Pipelines agent,
    or by hand during an incident. Every action is local: no remoting, no Web Deploy
    endpoint, no inbound port to open.

    The order of operations is not arbitrary. GHARS_PRODUCTION_OPERATIONS.md 6.1
    names the two things a deployment of this application can destroy, and the
    script is built around them.

    1. protected-uploads\ and wwwroot\uploads\ hold KPI evidence, organization
       licences, programme attachments, certificates and gallery media. None of it is
       reproducible by the platform and none of it is in a database backup. They are
       excluded from the mirror - and app_offline.htm is excluded with them, because
       otherwise /MIR would delete the very file holding the site down and bring the
       application back up in the middle of the copy.

    2. The application applies EF migrations at startup, so the schema changes the
       moment the app pool comes back whether or not anyone asked for it. The
       database backup therefore runs before anything is touched, and the migration
       is applied explicitly while the site is offline - a failure then stops the
       deployment instead of leaving a started site on a half-migrated database.

    Omitting -SqlServer/-Database/-BackupRoot skips the backup; omitting
    -MigrationScript skips the explicit migration (startup will still apply it).
    Both are announced, never silent.

.EXAMPLE
    .\Deploy-Ghars.ps1 -Source C:\drop\site -SitePath C:\inetpub\ghars -AppPool GharsPlatform `
        -SqlServer . -Database GharsPlatformDb -BackupRoot D:\GharsBackups\predeploy `
        -MigrationScript C:\drop\migrations\ghars-migrations.sql -HealthCheckUrl http://localhost:89/
#>
[CmdletBinding()]
param(
    # Published output to deploy - the folder containing GharsPlatform.dll and web.config.
    [Parameter(Mandatory)][string] $Source,

    # Live site root (the IIS physical path).
    [Parameter(Mandatory)][string] $SitePath,

    [Parameter(Mandatory)][string] $AppPool,

    # e.g. "IIS AppPool\GharsPlatform". When supplied, the protected-uploads ACL from
    # GHARS_PRODUCTION_OPERATIONS.md 2.3 is re-applied after the copy - a restore or a
    # recreated folder is the most likely way that protection is quietly lost.
    [string] $AppPoolIdentity,

    [string] $SqlServer,
    [string] $Database,
    [string] $BackupRoot,

    # Idempotent script produced by "dotnet ef migrations script --idempotent".
    [string] $MigrationScript,

    # Base URL smoke-tested after the site comes back, e.g. http://localhost:89/
    [string] $HealthCheckUrl,

    # Rollback copies. Kept OUTSIDE $SitePath so the mirror cannot reach them.
    [string] $ReleaseHistoryRoot,
    [int]    $KeepReleases = 5,

    [int]    $AppPoolTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:stepNo = 0

function Step([string] $Message) {
    $script:stepNo++
    Write-Host ''
    Write-Host ("=== [{0}] {1}" -f $script:stepNo, $Message) -ForegroundColor Cyan
}

function Fail([string] $Message) { throw $Message }

function Invoke-SqlcmdCli {
    param([string[]] $ExtraArgs, [string] $What)

    # -b  return a non-zero exit code on error, so a failed statement fails the deployment
    # -I  QUOTED_IDENTIFIER ON. Required: this schema uses filtered indexes, and creating one
    #     without it fails at runtime rather than at parse time.
    # -C  trust the server certificate (local instance, self-signed)
    $sqlArgs = @('-S', $SqlServer, '-E', '-b', '-I', '-C') + $ExtraArgs
    & sqlcmd @sqlArgs
    if ($LASTEXITCODE -ne 0) { Fail "$What failed (sqlcmd exit $LASTEXITCODE)." }
}

function Invoke-Robocopy {
    param([string] $From, [string] $To, [string[]] $Options, [string] $What)

    & robocopy $From $To @Options | Out-Host
    # Robocopy returns a bit field: 0-7 are success (files copied, extras removed, and so on),
    # 8 and above are genuine failures. Treating any non-zero as failure is the classic mistake
    # and would fail every successful deployment.
    if ($LASTEXITCODE -ge 8) { Fail "$What failed (robocopy exit $LASTEXITCODE)." }
    Write-Host ("    robocopy exit {0} (0-7 = success)" -f $LASTEXITCODE)
}

# ---------------------------------------------------------------------------
Step 'Validate inputs'

if (-not (Test-Path (Join-Path $Source 'GharsPlatform.dll'))) {
    Fail "No GharsPlatform.dll under '$Source'. Point -Source at the dotnet publish output, not the repository."
}
if (-not (Test-Path (Join-Path $Source 'web.config'))) {
    Fail "No web.config under '$Source'. dotnet publish generates it for the ASP.NET Core Module; without it IIS cannot start the app."
}
if (-not (Test-Path $SitePath)) { Fail "Site path '$SitePath' does not exist. Create the IIS site first." }

Import-Module WebAdministration -ErrorAction Stop
if (-not (Test-Path "IIS:\AppPools\$AppPool")) { Fail "Application pool '$AppPool' does not exist." }

$doBackup  = $SqlServer -and $Database -and $BackupRoot
$doMigrate = $MigrationScript -and (Test-Path $MigrationScript)

Write-Host "    source      : $Source"
Write-Host "    destination : $SitePath"
Write-Host "    app pool    : $AppPool"
Write-Host ("    db backup   : {0}" -f $(if ($doBackup)  { "$SqlServer / $Database -> $BackupRoot" } else { 'SKIPPED (no -SqlServer/-Database/-BackupRoot)' }))
Write-Host ("    migration   : {0}" -f $(if ($doMigrate) { $MigrationScript } else { 'SKIPPED - startup will still migrate on first request' }))

# ---------------------------------------------------------------------------
Step 'Back up the database (before anything can migrate it)'

if ($doBackup) {
    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    $backupFile = Join-Path $BackupRoot "$Database-predeploy-$stamp.bak"
    $sql = "BACKUP DATABASE [$Database] TO DISK = N'$backupFile' WITH INIT, CHECKSUM, COMPRESSION, STATS = 10;"
    Invoke-SqlcmdCli -ExtraArgs @('-Q', $sql) -What 'Database backup'
    Write-Host "    backup: $backupFile"
}
else {
    Write-Warning 'Database backup skipped. Startup will apply any pending migration with no restore point.'
}

# ---------------------------------------------------------------------------
Step 'Snapshot the current build for rollback'

if ($ReleaseHistoryRoot) {
    $snapshot = Join-Path $ReleaseHistoryRoot "rollback-$stamp"
    # Binaries and views only. The upload roots are business data, not part of a release, and
    # copying them here would duplicate gigabytes on every deployment.
    Invoke-Robocopy -From $SitePath -To $snapshot -What 'Rollback snapshot' -Options @(
        '/E', '/R:2', '/W:2', '/NFL', '/NDL', '/NJH', '/NJS', '/NP',
        '/XD', (Join-Path $SitePath 'protected-uploads'),
        (Join-Path $SitePath 'wwwroot\uploads'),
        (Join-Path $SitePath 'logs')
    )
    Write-Host "    snapshot: $snapshot"

    $old = Get-ChildItem $ReleaseHistoryRoot -Directory -Filter 'rollback-*' |
        Sort-Object Name -Descending | Select-Object -Skip $KeepReleases
    foreach ($d in $old) { Remove-Item $d.FullName -Recurse -Force; Write-Host "    pruned: $($d.Name)" }
}
else {
    Write-Warning 'No -ReleaseHistoryRoot: rolling back would mean rebuilding the previous commit.'
}

# ---------------------------------------------------------------------------
Step 'Take the site offline'

# app_offline.htm first, so requests in flight get a page rather than a reset connection; then
# stop the pool, which is what actually releases the lock on GharsPlatform.dll.
$offline = Join-Path $SitePath 'app_offline.htm'
$offlineHtml = @'
<!doctype html>
<html><head><meta charset="utf-8"><title>Ghars Platform</title></head>
<body style="font-family:Segoe UI,Tahoma,sans-serif;text-align:center;padding:4rem">
<h1>Update in progress</h1><p>The platform will be back shortly.</p>
<h1 dir="rtl">جارٍ التحديث</h1><p dir="rtl">ستعود المنصة للعمل خلال لحظات.</p>
</body></html>
'@
Set-Content -Path $offline -Value $offlineHtml -Encoding UTF8

if ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Stopped') {
    Stop-WebAppPool -Name $AppPool
}

$deadline = (Get-Date).AddSeconds($AppPoolTimeoutSeconds)
while ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Stopped') {
    if ((Get-Date) -gt $deadline) { Fail "Application pool '$AppPool' did not stop within $AppPoolTimeoutSeconds seconds." }
    Start-Sleep -Seconds 2
}
Write-Host '    app pool stopped'

# ---------------------------------------------------------------------------
# From here until the site comes back, every failure must leave the platform behind the
# maintenance page rather than half-deployed. See the catch block.
try {
    # -----------------------------------------------------------------------
    Step 'Apply database migrations'

    if ($doMigrate) {
        Invoke-SqlcmdCli -ExtraArgs @('-d', $Database, '-i', $MigrationScript) -What 'Migration script'
        Write-Host '    migrations applied (idempotent - a no-op when already current)'
    }
    else {
        Write-Host '    skipped'
    }

    # -----------------------------------------------------------------------
    Step 'Mirror the new build into the site'

    # /MIR removes files deleted between releases - a stale view or an orphaned DLL is a real
    # source of "the fix did not deploy". The excluded paths are the ones /MIR must never see.
    Invoke-Robocopy -From $Source -To $SitePath -What 'Site mirror' -Options @(
        '/MIR', '/R:3', '/W:5', '/MT:8', '/NFL', '/NDL', '/NJH', '/NJS', '/NP',
        '/XD', (Join-Path $SitePath 'protected-uploads'),
        (Join-Path $SitePath 'wwwroot\uploads'),
        (Join-Path $SitePath 'logs'),
        '/XF', (Join-Path $SitePath 'appsettings.Production.json'),
        $offline
    )

    # The published tree carries one file under wwwroot\uploads: the sample library PDF the seeder
    # references by a fixed path. The exclusion above means it would never arrive on a new server,
    # so seed anything missing without ever overwriting what is already there.
    # /XC /XN /XO = skip changed, newer and older files, i.e. copy only what does not exist.
    $srcUploads = Join-Path $Source 'wwwroot\uploads'
    if (Test-Path $srcUploads) {
        Invoke-Robocopy -From $srcUploads -To (Join-Path $SitePath 'wwwroot\uploads') -What 'Seed missing static uploads' -Options @(
            '/E', '/XC', '/XN', '/XO', '/R:2', '/W:2', '/NFL', '/NDL', '/NJH', '/NJS', '/NP'
        )
    }

    foreach ($c in @('kpi', 'org', 'surveys', 'programs')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $SitePath "protected-uploads\$c") | Out-Null
    }

    if ($AppPoolIdentity) {
        Write-Host '    re-asserting protected-uploads permissions'
        $protectedRoot = Join-Path $SitePath 'protected-uploads'
        foreach ($argset in @(
                @('/inheritance:r'),
                @('/grant', "${AppPoolIdentity}:(OI)(CI)(M)"),
                @('/grant', 'Administrators:(OI)(CI)(F)'))) {
            & icacls $protectedRoot @argset | Out-Host
            if ($LASTEXITCODE -ne 0) { Fail "icacls $argset failed on '$protectedRoot' (exit $LASTEXITCODE)." }
        }
    }
}
catch {
    # Deliberately leave app_offline.htm in place and the pool stopped. A copy or a migration that
    # failed part-way would otherwise come back as a running application built from half a release
    # - which looks healthy to a load balancer and is not. The maintenance page is honest, and
    # rolling back is a decision for whoever reads this message.
    Write-Host ''
    Write-Host '!!! DEPLOYMENT FAILED - the site is being held offline on purpose.' -ForegroundColor Red
    Write-Host '    app_offline.htm is in place and the application pool is stopped.'
    if ($ReleaseHistoryRoot) {
        Write-Host "    Roll back by re-running this script with -Source '$snapshot' and no -MigrationScript."
    }
    Write-Host '    If the migration step ran, restore the pre-deploy backup as well - a code'
    Write-Host '    rollback does not undo a schema change.'
    throw
}

# ---------------------------------------------------------------------------
Step 'Bring the site back online'

if ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Started') {
    Start-WebAppPool -Name $AppPool
}
Remove-Item $offline -Force -ErrorAction SilentlyContinue
Write-Host '    app pool started, app_offline.htm removed'

# ---------------------------------------------------------------------------
Step 'Smoke test'

if ($HealthCheckUrl) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    # Loopback against a certificate issued for the public host name. Scoped to this process
    # only; it does not change machine trust.
    [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

    $base = $HealthCheckUrl.TrimEnd('/')

    $ok = $false
    foreach ($attempt in 1..12) {
        try {
            $r = Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 30
            if ($r.StatusCode -eq 200) { $ok = $true; break }
        }
        catch {
            Write-Host "    attempt $attempt - not ready yet"
        }
        Start-Sleep -Seconds 5
    }
    if (-not $ok) { Fail "Home page did not return 200 from $base/ after warm-up. Check the Windows Application event log and the ASP.NET Core Module stdout log." }
    Write-Host '    GET / -> 200'

    # GHARS_PRODUCTION_OPERATIONS.md 2.3: these must be unreachable as static content. A 200 here
    # means protected evidence is being served straight off disk, so the deployment has failed
    # even though the site is up.
    foreach ($path in @('/protected-uploads/kpi/probe.pdf', '/uploads/kpi/probe.pdf', '/uploads/agenda/probe.jpg', '/uploads/channel/probe.jpg')) {
        $code = 0
        try { $code = (Invoke-WebRequest -Uri "$base$path" -UseBasicParsing -TimeoutSec 20).StatusCode }
        catch {
            if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode } else { throw }
        }
        if ($code -ne 404) { Fail "SECURITY: $path returned $code, expected 404. Protected content is statically reachable." }
        Write-Host "    GET $path -> 404"
    }
}
else {
    Write-Warning 'No -HealthCheckUrl: the deployment is unverified.'
}

Write-Host ''
Write-Host "=== Deployment complete ($stamp) ===" -ForegroundColor Green
