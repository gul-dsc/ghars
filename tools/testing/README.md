# Runtime test fixture safety

Tooling for verification passes that exercise the running application against the development
database. It exists so that a test run can create rows and then remove **exactly those rows**,
with no possibility of catching legitimate data in the net.

Nothing here is part of the application. No file in this folder is compiled into `GharsPlatform`,
referenced by it, or shipped with it.

## Why

Notifications carry no foreign key to the booking or activity they describe — their only link is
the `LinkUrl` string. Cleanup scripts therefore used to identify them with `LIKE` patterns. During
the dual-booking verification pass, the pattern `LIKE '%/bookings/details/2[0-9]'` was written to
match fixture bookings 28 and 29 and also matched **booking 20**, a legitimate row. Two real
notification rows were deleted and had to be reconstructed from the booking and its audit trail.

The lesson is not "write better patterns". It is that fixture identity must be recorded when the
fixture is created, and cleanup must work from that record alone.

## The rules

1. **Exact ids, always.** Capture the id the application returns at the moment it returns it.
   Never re-find a fixture afterwards by subject text, program name, notification wording,
   organization name, status, date range, or `LIKE`.
2. **Snapshot a baseline first.** The high-water mark (`MAX(Id)`) per table is what proves an id
   was created by this run. Cleanup refuses to delete any row at or below it.
3. **Baseline counts are a signal, not a mechanism.** Compare them afterwards to check cleanup was
   complete. Never delete rows because a count is higher than baseline.
4. **Protected rows are absolute.** Booking 20 and everything else in `protectedRows` aborts a run
   that would touch it.
5. **Unclaimed dependants block.** If a row references a fixture and is not itself in the manifest,
   cleanup stops rather than cascading into it.
6. **Prefer a scratch database.** Use the populated development database only when the test needs
   its seeded data. Drop scratch databases afterwards.
7. **Development only.** Every script refuses to run unless the environment says Development, and
   refuses an unset environment rather than assuming.
8. **Dry run first.** `Remove-GharsFixtures.ps1` prints the exact ids it intends to delete and does
   nothing until `-Execute`.

## Files

| File | What it does |
|---|---|
| `New-GharsBaseline.ps1` | Records counts and `MAX(Id)` high-water marks before a run. |
| `fixture-manifest.mjs` | Node helper the test suites use to record created ids as they happen. |
| `Remove-GharsFixtures.ps1` | Deletes exactly the manifest's rows, dependency-ordered, guarded, dry-run by default. |
| `Compare-GharsBaseline.ps1` | Verification signal: reports drift and confirms protected rows survive. |
| `New-GharsScratchDatabase.ps1` | Creates/drops a throwaway database from the migration chain. |
| `GharsTestSafety.ps1` | Shared guards, query helpers and the declared delete order. |
| `runs/` | Baselines and manifests. Git-ignored. |

## A run, end to end

```powershell
$env:GHARS_TEST_ENVIRONMENT = 'Development'

# 1. Baseline, before anything runs.
.\tools\testing\New-GharsBaseline.ps1
```

```js
// 2. The test records what it creates, as it creates it.
import { createRun } from '../../tools/testing/fixture-manifest.mjs';

const run = createRun('dual-booking');

const res = await postBooking(club, fields);
run.recordBookingFromRedirect(res.headers['location'], 'existing-program E2E');

// A row the test had to change rather than create: capture the value BEFORE changing it.
run.recordModification('Activities', 35, 'IsPublished', true);

run.finish();
```

```powershell
# 3. Dry run: read the plan before anything is deleted.
.\tools\testing\Remove-GharsFixtures.ps1 -Manifest .\tools\testing\runs\<runId>.json

# 4. Apply it.
.\tools\testing\Remove-GharsFixtures.ps1 -Manifest .\tools\testing\runs\<runId>.json -Execute

# 5. Confirm the database is back where it started.
.\tools\testing\Compare-GharsBaseline.ps1
```

## Fixture kinds

`Remove-GharsFixtures.ps1` understands these and refuses anything else — it is deliberately not a
general-purpose cleaner:

| Kind | Root table | Removed with it |
|---|---|---|
| `booking` | `BookingRequests` | agenda entries and their media, audit trail, proposed time options, notifications whose `LinkUrl` is exactly `/bookings/details/<id>`, their deliveries |
| `activity` | `Activities` | attachments, speakers, surveys, certificates, attendance sessions and records, `/Admin/Activities/Details/<id>` notifications, `Activity` system audit rows |
| `organization` | `Organizations` | admin links, contacts, documents, partner profile |
| `contactMessage` | `ContactMessages` | `/Admin/ContactMessages/Details/<id>` notifications and deliveries |
| `notification` | `Notifications` | its deliveries |
| `agendaEntry` | `AgendaEntries` | its media |

### Notifications without an id in the link

A notification linking to `/partner/programs` names no row, so it cannot be matched exactly. Record
its id explicitly instead, immediately after the action that raised it:

```sql
SELECT Id FROM dbo.Notifications WHERE Id > <baseline watermark> ORDER BY Id;
```

then `run.record('notification', id)`. Do not fall back to matching on message text.

## Audit trails

`BookingAuditTrails` rows are evidence. They are removed only as part of a booking that the
manifest names — a booking that by definition did not exist before the run. A legitimate booking's
audit trail is never touched, and history is never rewritten to make counts look tidier. If cleanup
leaves a discrepancy, the discrepancy is reported, not edited away.

## Seeder side effects

Development seeding can create attendance and certificate rows when the application restarts. Those
are the application's own doing, not test fixtures, and they must not be swept up in a teardown.
`Compare-GharsBaseline.ps1` reports those tables separately for that reason. If a restart happened
during a run, note it — the drift is explained by the restart, not by the tests.

## Modified rows

If a test must change an existing row, snapshot the exact original value first with
`recordModification`, and let cleanup restore it. The restore is verified by reading the value back
and comparing. Reconstructing a value afterwards from what you believe it used to be is not a
restoration, and the difference only becomes visible when it is already too late.
