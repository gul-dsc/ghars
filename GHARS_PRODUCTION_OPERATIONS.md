# Ghars Platform — Production Operations

Operational runbook for the Ghars Platform (ASP.NET Core 8 + SQL Server).

Scope: protected file storage, backup and restore, deployment, folder permissions, the one-time file
migration utility, rollback and disaster recovery.

> **Status of automation.** This repository contains **no** CI pipeline, deployment script, Dockerfile,
> publish profile or backup job. Everything below is therefore written as an operational procedure to be
> implemented by whoever owns the target environment, not as documentation of existing tooling. Where a
> command is given it has been run and verified in development.

---

## 1. The rule that matters most

> **A restored database without restored protected files is an incomplete restore.**

The database stores only a **storage key** — `kpi/ab12cd….pdf` — never the file itself and never a server
path. The bytes live on disk under `protected-uploads/`. The two are useless apart:

* Database restored, files missing → KPI submissions, organization records and survey reports all exist and
  look healthy, but every evidence link returns **404**. The failure is silent by design: the authorized
  endpoint deliberately cannot distinguish "file is gone" from "you may not have it", so nothing in the UI
  announces that evidence has been lost.
* Files restored, database missing → the files are unreachable. Nothing maps a GUID filename back to a club,
  a submission or a permission.

**Back them up together, restore them together, and to the same point in time.**

---

## 2. Protected upload storage

### 2.1 Physical location

```
<ContentRoot>/protected-uploads/
├── kpi/          KPI supporting evidence uploaded by clubs
├── org/          Organization licences and supporting registration documents
├── programs/     Supporting documents attached to partner offerings
└── surveys/      Legacy externally-analysed survey reports (PDF), historical only
```

`<ContentRoot>` is the application's content root — the folder containing `GharsPlatform.dll` in a published
deployment. It is **outside `wwwroot`**, so the static-file middleware cannot reach it under any request
path. Filenames are GUIDs; the original filename is stored in the database, never on disk.

Defined in `Helpers/ProtectedFileStore.cs` (`RootFolderName`, category constants).

### 2.2 What lives there, and why it must be backed up

| Folder | Contents | Sensitivity | Recoverable from elsewhere? |
|---|---|---|---|
| `kpi/` | Club evidence for KPI submissions — medical aggregates, attendance records, participation reports | Organization-confidential; visible only to the owning club and DSC | **No.** Uploaded once by the club; there is no second copy |
| `org/` | Licences and registration documents | Legal documents supporting an approval decision | **No** |
| `programs/` | Programme outlines, brochures, session plans and trainer profiles attached to partner offerings | Visible to the owning entity and DSC while under review; readable by any signed-in user once the offering is approved and published. Never anonymous | **No.** Uploaded once by the implementing entity |
| `surveys/` | Analysed reports from the **legacy** externally-run surveys. No new file is written here: the official satisfaction survey is native, and its responses live in the database | Public *only after* DSC publishes them; confidential while under review | **No.** The external providers are no longer engaged, so a lost report is gone |

None of this content is reproducible by the platform. Losing it means asking every club to re-upload
evidence for approvals that have already been granted — and the evidence for an approval that has already
been made is precisely what an audit asks for.

Also note: `wwwroot/uploads/` (gallery, agenda media, library, certificates, organization logos, rewards)
is equally irreplaceable and is **not** covered by a database backup either. Back up **both** roots.

### 2.3 Required folder permissions

| Principal | `protected-uploads/` | Notes |
|---|---|---|
| Application pool identity / service account | **Read + Write + Create** | Needs write access to create category folders and store uploads |
| Backup service account | **Read** | |
| Everyone / IIS_IUSRS / anonymous | **No access** | |

Rules:

* The folder must **never** be published as an IIS virtual directory, an alias, or a static-file mapping.
* Do not place it inside `wwwroot`, and do not create a symlink or junction into `wwwroot`.
* Inherit-and-restrict: grant on the folder, do not grant on the parent.
* Verify after every deployment and every restore — a restore that recreates the folder with inherited
  permissions is the most likely way this protection is lost.

Example (Windows / IIS, adjust the identity to your app pool):

```powershell
$root = "C:\inetpub\ghars\protected-uploads"
icacls $root /inheritance:r
icacls $root /grant "IIS AppPool\GharsPlatform:(OI)(CI)(M)"
icacls $root /grant "Administrators:(OI)(CI)(F)"
```

**Verification after any change** — each of these must return 404, not the file:

```
GET https://<host>/protected-uploads/kpi/<any-known-filename>
GET https://<host>/uploads/kpi/<any-known-filename>
GET https://<host>/uploads/agenda/<any-known-filename>
GET https://<host>/uploads/channel/<any-known-filename>
```

`wwwroot/uploads/agenda` and `wwwroot/uploads/channel` hold Ghars Channel media. They live under
`wwwroot` but are denied to the static-file middleware in `Program.cs`, because whether a given item is
visible is decided per database row — club media can be hidden by DSC, and implementing-entity content
is invisible until DSC approves it. Both are served only through `/protected-files/gallery/{id}`, which
re-checks that visibility on every request. If either folder ever becomes statically reachable, hiding
or withdrawing an item stops actually withdrawing the file.

---

## 3. Backup

### 3.1 What to back up

| Item | Contents |
|---|---|
| SQL Server database `GharsPlatformDb` | All records, including storage keys |
| `<ContentRoot>/protected-uploads/` | Protected files |
| `<ContentRoot>/wwwroot/uploads/` | Public media, library files, certificates |
| `appsettings.Production.json` | Connection string and environment configuration |

### 3.2 Frequency

| Item | Full | Incremental / differential | Retention |
|---|---|---|---|
| Database | Weekly | Daily differential + transaction log every 15–30 min | 90 days minimum |
| `protected-uploads/` | Weekly | **Daily** | 90 days minimum, aligned with the database |
| `wwwroot/uploads/` | Weekly | Daily | 90 days |

The file and database schedules must be **aligned**. A daily file backup against a 15-minute log chain
means a point-in-time database restore can reference files that the file backup does not yet contain. The
practical mitigation is to quiesce or snapshot both together for the weekly full, and accept that
intra-day recovery may need the most recent daily file set.

Because filenames are GUIDs and files are never modified in place, file backups are **append-only in
practice**: an incremental backup captures new uploads and nothing else. Deletions only occur through
`--purge` (see §5).

### 3.3 Example file backup

```powershell
# Daily incremental — mirrors new uploads, keeps history via the dated destination.
$src  = "C:\inetpub\ghars\protected-uploads"
$dest = "\\backup-server\ghars\protected-uploads\$(Get-Date -Format yyyy-MM-dd)"
robocopy $src $dest /E /COPY:DAT /R:2 /W:5 /LOG+:C:\logs\ghars-protected-backup.log
```

Confirm the backup is non-empty and the file count is monotonically increasing. A protected-uploads backup
that silently starts producing zero files is indistinguishable from "no uploads happened this week" unless
someone checks.

---

## 4. Restore

Restore the database and the file roots **as a set**.

1. **Stop the application** (stop the site or the app pool). Do not restore files under a running app.
2. **Restore the database** to the chosen point in time.
3. **Restore `protected-uploads/`** from the backup closest to — and not after — that point in time.
4. **Restore `wwwroot/uploads/`** on the same basis.
5. **Reapply folder permissions** (§2.3). A restore commonly resets ACLs to inherited.
6. **Apply any pending migrations**: `dotnet ef database update` (see §6).
7. **Start the application.**
8. **Verify** — §4.1.

### 4.1 Post-restore verification

| Check | Expected |
|---|---|
| `GET /` | 200 |
| Sign in as a club admin, open a KPI submission with evidence, click through to a document | File downloads |
| Sign in as DSC Admin, open another club's KPI evidence | File downloads |
| Sign in as club A, request club B's evidence id directly | **404** |
| Anonymous `GET /protected-files/kpi/{id}` | Redirect to login |
| Anonymous `GET /uploads/kpi/<filename>` | **404** |
| Anonymous `GET /uploads/agenda/<filename>` | **404** |
| Public gallery `/gallery` renders its images | 200, images visible |

**Reconciling the database against the disk** — run this to find rows whose file did not come back. It is
the only reliable way to detect a partial restore, because the application reports a missing file as 404:

```sql
SELECT 'KpiDocument' AS Source, Id, FilePath FROM KpiDocuments
UNION ALL SELECT 'OrganizationDocument', Id, FilePath FROM OrganizationDocuments
UNION ALL SELECT 'ExternalSurvey', Id, ReportPdfPath FROM ExternalSurveys WHERE ReportPdfPath IS NOT NULL;
```

Every returned key that does **not** start with `/` must exist at
`<ContentRoot>/protected-uploads/<key>`; every key that **does** start with `/` is a legacy path and must
exist under `<ContentRoot>/wwwroot/<key>`. Any mismatch is data loss and should be escalated before the
platform is returned to service — clubs can re-upload evidence, but only if they are told.

---

## 5. The protected-file migration utility

One-time relocation of legacy files from `wwwroot/uploads` into `protected-uploads/`. It is an **operator
command** and never runs as part of a normal startup — it replaces the web host and skips seeding entirely.

```bash
dotnet GharsPlatform.dll migrate-protected-files              # 1. dry run — reports, changes nothing
dotnet GharsPlatform.dll migrate-protected-files --commit     # 2. copies files, repoints DB, keeps originals
dotnet GharsPlatform.dll migrate-protected-files --purge      # 3. deletes the legacy originals
```

Run the phases as three separate, verified steps:

1. **Dry run.** Review the report. `MISSING FILE` rows are pre-existing broken references — investigate
   them before continuing; the migrator leaves those rows untouched rather than repointing them at a path
   that also does not exist.
2. **`--commit`.** Copies each file, verifies the copy's length, then updates the database. The legacy file
   is **kept**. The application works correctly in this state — both path shapes resolve.
3. **Verify.** Open real evidence for several clubs through the UI.
4. **`--purge`.** Only after step 3. Deletes a legacy file only once the protected copy is confirmed
   present.

> Take a database and file backup **before `--commit`** and again **before `--purge`**.

### Rollback

| Situation | Action |
|---|---|
| After `--commit`, before `--purge` | Restore the previous `FilePath` / `ReportPdfPath` values from backup. The legacy files are still on disk, so the application returns to its prior behaviour immediately. **No file recovery needed.** |
| After `--purge` | The legacy copies are gone. Roll back by restoring both the database *and* `protected-uploads/` from backup. This is the reason `--purge` is a separate, deliberate step. |

`/uploads/kpi`, `/uploads/surveys` and `/uploads/agenda` are refused by the static deny-list in
`Program.cs` regardless of migration state, so an un-migrated row is still not publicly downloadable.

---

## 6. Deployment

### 6.1 Packaging

`dotnet publish` output does **not** include `protected-uploads/` — it is runtime data, not build output.
This is correct, and it is also the failure mode to guard against: a deployment that replaces the
application directory wholesale will delete it.

| Item | Deployment handling |
|---|---|
| Application binaries and views | Replace |
| `protected-uploads/` | **Preserve — never delete, never overwrite** |
| `wwwroot/uploads/` | **Preserve — never delete, never overwrite** |
| `appsettings.Production.json` | Preserve (or inject from secret storage) |

If your deployment performs a clean directory replacement (`robocopy /MIR`, "delete existing files" in a
Web Deploy profile, a container image swap), you must either exclude both upload roots explicitly or move
them outside the deployment directory and point the application at that location.

**Strongly recommended for containers and any immutable-infrastructure deployment:** place
`protected-uploads/` on a mounted volume or file share outside the application directory, so it cannot be
destroyed by a redeploy.

### 6.2 First deployment

The sequence for a brand-new installation, where the database does not yet exist and nobody can sign
in. A step-by-step operator version with checkboxes is in
[`GHARS_PRODUCTION_DEPLOYMENT_CHECKLIST.md`](GHARS_PRODUCTION_DEPLOYMENT_CHECKLIST.md).

1. **Create the database** (empty) on the target SQL Server instance, or restore it if you are
   migrating an existing one.
2. **Apply EF migrations** — `dotnet ef database update`, or a generated idempotent script. See §6.3,
   including the one-time history repair for databases created before 2026-09-06.
3. **Configure the production connection string** through the hosting environment's protected
   configuration (`ConnectionStrings__DefaultConnection`, IIS configuration, container secret, or an
   `appsettings.Production.json` kept outside Git). Never commit it.
4. **Configure protected-upload storage** — create `protected-uploads/`, apply the permissions in
   §2.3, and confirm it is outside any directory a redeploy will mirror-delete (§6.1).
5. **Set the bootstrap administrator values**, but only if the database is new and has no
   administrator:

   ```
   GHARS_BOOTSTRAP_ADMIN_EMAIL=<the real administrator's address>
   GHARS_BOOTSTRAP_ADMIN_PASSWORD=<a strong one-time password>
   ```

   If the database already has a Super Admin or DSC Admin, skip this — the values would be ignored
   anyway.
6. **Set `ASPNETCORE_ENVIRONMENT=Production`** and start the application.
7. **Verify the initial administrator**: sign in with the bootstrap address and immediately change the
   password from the account page.
8. **Remove `GHARS_BOOTSTRAP_ADMIN_PASSWORD` from the deployment environment** and restart. Leaving it
   set does no harm — the bootstrap path is inert once an administrator exists — but a one-time
   password should not persist in the environment, a process listing, or a deployment pipeline's
   stored variables. It must never be committed to source control.
9. **Verify no demo data was created.** Production seeding creates the seven roles, one active season
   and nothing else:

   ```sql
   SELECT COUNT(*) FROM AspNetUsers WHERE Email LIKE '%@ghars.local';  -- expect 0
   SELECT COUNT(*) FROM Organizations;                                 -- expect 0 on a new install
   SELECT COUNT(*) FROM AspNetRoles;                                   -- expect 7
   SELECT COUNT(*) FROM Seasons WHERE IsActive = 1;                    -- expect 1
   ```

   Any `@ghars.local` account in production means the application was started with
   `ASPNETCORE_ENVIRONMENT=Development`. Those accounts and their passwords are public — see
   `SEED_CREDENTIALS.md`. Delete them and rotate anything they could have reached.

10. **Load the approved organization roster.** A production database is created empty, so until this
    runs there are no clubs and no implementing entities: the booking catalogue reads *Existing
    Programs (0)*, nobody can be given a club or partner account, and every entity-facing feature —
    including the availability calendar — has nothing to show. Run it from the deployed folder, where
    the connection string is:

    ```powershell
    cd C:\inetpub\ghars
    dotnet GharsPlatform.dll reconcile-organizations --production            # dry run: read the plan
    dotnet GharsPlatform.dll reconcile-organizations --production --commit   # apply it
    ```

    It prints the environment, server and database name above the plan — check them before
    committing. Expect 25 created on a new install: 7 clubs, 17 implementing entities and Dubai Sports
    Council itself. Outside `Development` the command is **additive only**; organizations not on the
    roster are listed but left alone unless you also pass `--allow-deactivate`.

    ```sql
    SELECT COUNT(*) FROM Organizations WHERE Status = 2;   -- expect 25 on a new install
    ```

11. **Finish the organizations by hand.** The roster carries names, types and logos — not contact
    details, and no user accounts. In **Admin → Organizations**, replace each created row's
    placeholder email (`…@ghars.seed.local`), phone (`0000000000`) and address. Then in **Admin →
    Users**, create each organization's own administrator. Only after an implementing entity has an
    administrator can that entity publish availability or receive booking requests.

The startup log states which path ran. On a correct production start you will see
`Demo/sample seeding skipped: environment is Production, not Development.`

> Application startup never resets an existing user's password, in any environment. An administrator
> who changes their password keeps it across restarts.

### 6.2.1 Deployment checklist (existing installation)

1. Back up the database and both upload roots.
2. Publish/copy the application, preserving the upload roots.
3. Apply migrations (`dotnet ef database update`, or a generated idempotent script for controlled
   environments).
4. Confirm folder permissions (§2.3).
5. Smoke-test using §4.1.

### 6.3 Migrations

```bash
dotnet ef database update                       # applies pending migrations
dotnet ef migrations script --idempotent -o ghars.sql   # for DBA-controlled environments
```

> **Build first.** `dotnet ef database update --no-build` against a stale assembly reports `Done.` while
> applying nothing. Confirm the `Applying migration '…'` line appears in the output.

#### One-time step for databases created before 2026-09-06

Four migrations (`AddBookingTimeProposals`, `AddActivityPartnerOrganization`,
`AddGharsAgendaKpiGalleryLibraryEnhancements`, `AddGalleryAlbumLinksAndPdf`) were previously invisible
to EF Core because they lacked `[Migration]` attributes. Their changes were applied to existing
databases under the now-missing migration `20260505103249_new one `, so those databases have the
schema but not the history rows.

Now that EF recognises the four files, it will try to re-apply them to any such database and fail
because the objects already exist. Record them as applied **before** running `database update`:

```sql
INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
SELECT v.MigrationId, '8.0.8' FROM (VALUES
  ('20260503170000_AddBookingTimeProposals'),
  ('20260503173000_AddActivityPartnerOrganization'),
  ('20260504120000_AddGharsAgendaKpiGalleryLibraryEnhancements'),
  ('20260505103000_AddGalleryAlbumLinksAndPdf')) v(MigrationId)
WHERE NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory h WHERE h.MigrationId = v.MigrationId);
```

The statement is safe to re-run and is a no-op on a database created after this date.

**Databases created from scratch need none of this** — `dotnet ef database update` applies the whole
chain in order.

Expected shape of a database built this way, re-verified 2026-09-11 after
`20260911151258_AddPartnerAvailabilityCalendar`, against both a scratch database built from the
whole chain and the working development database:

| | From scratch | Development database |
|---|---|---|
| `__EFMigrationsHistory` rows | **16** | **17** |
| Tables (`sys.tables`) | **53** | 53 |
| Columns | **647** | 647 |

The history counts differ by one and that is correct, not drift: the development database is a
pre-existing one and still carries the phantom row `20260505103249_new one ` described above. The
schema itself is identical — a fresh database matching on tables and columns is the check that
matters.

> Keep this table current whenever the chain grows, or the next person verifying a new database
> will think a correct one is wrong. Its history:
>
> | Measured at | Migrations (scratch) | Tables | Columns |
> |---|---|---|---|
> | `20260906140000_RestoreOrganizationAdminLinkAuditColumns` | 9 | 49 | 549 |
> | `20260908170721_AddNativeOfficialSatisfactionSurvey` | 15 | 52 | 629 |
> | `20260911151258_AddPartnerAvailabilityCalendar` | 16 | 53 | 647 |
>
> The last step is the partner availability calendar: one new table
> (`PartnerAvailabilitySlots`, 17 columns) and one new nullable column
> (`BookingRequests.PartnerAvailabilitySlotId`).

Check which state a database is in with:

```sql
SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;
```

If `20260505103249_new one ` appears, it is a pre-existing database and needs the insert above.

### 6.4 Server migration (moving to a new host)

1. Back up database + both upload roots on the old host.
2. Restore the database on the new host.
3. Copy `protected-uploads/` and `wwwroot/uploads/` preserving directory structure.
4. Set folder permissions for the **new** application identity (§2.3).
5. Update the connection string.
6. Apply pending migrations.
7. Run the full §4.1 verification, including the negative checks.
8. Keep the old host intact until verification passes.

---

## 7. Disaster recovery

| Consideration | Position |
|---|---|
| RPO | Set by the file backup interval, not the database log chain. Daily file backups ⇒ up to 24h of uploaded evidence at risk, even with 15-minute log shipping. Tighten the file schedule if that is unacceptable. |
| RTO | Restore database + both file roots + reapply ACLs + verify. Rehearse it; do not discover the ACL step during an incident. |
| Off-site copy | Required for both the database and `protected-uploads/`. A backup on the same host protects against nothing that matters. |
| Encryption at rest | `protected-uploads/` holds organization-confidential documents. Encrypt the volume and the backup media. |
| Restore rehearsal | At least annually, into an isolated environment, ending with the §4.1 matrix **including the negative checks** — a restore that accidentally makes protected files publicly readable is a failed restore, not a successful one. |
| Ransomware | Keep at least one immutable/offline copy. File storage is append-only in normal operation, so mass modification or deletion of `protected-uploads/` is a reliable alarm condition. |
| Antivirus | Uploads are validated by extension, MIME prefix and size only, and are never executed or served from the web root. Server-side scanning of both upload roots is recommended before large-scale external intake. |

### Monitoring worth having

* Free disk space on the volume holding `protected-uploads/`.
* Backup job success **and non-zero file count**.
* File count in `protected-uploads/` trending upward, never sharply down (a drop means `--purge`,
  a bad deployment, or an incident).
* Unexpected 404 rates on `/protected-files/*`, which is what a partial restore looks like from outside.

---

## 8. Quick reference

| Task | Command |
|---|---|
| Apply migrations | `dotnet ef database update` |
| Generate migration script | `dotnet ef migrations script --idempotent -o ghars.sql` |
| Migrate protected files (dry run) | `dotnet GharsPlatform.dll migrate-protected-files` |
| Migrate protected files (commit) | `dotnet GharsPlatform.dll migrate-protected-files --commit` |
| Purge legacy copies | `dotnet GharsPlatform.dll migrate-protected-files --purge` |
| Verify protection | `GET /uploads/kpi/<file>` → **404** |

**Related:** `GHARS_IMPLEMENTATION_REPORT.md` §20 (protected storage architecture and authorization rules)
and §21 (production hardening pass, security regression results, remaining risks).
