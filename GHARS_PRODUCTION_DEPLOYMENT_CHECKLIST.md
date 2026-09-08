# Ghars Platform — Production Deployment Checklist

Work through this during a deployment. Background and the reasoning behind each step are in
[`GHARS_PRODUCTION_OPERATIONS.md`](GHARS_PRODUCTION_OPERATIONS.md); this page is the operator's copy.

**Deployment record** — fill in as you go:

| Field | Value |
| --- | --- |
| Date | |
| Deployed by | |
| Commit SHA | |
| Tag | |
| Target environment | |
| New installation or upgrade? | |

---

## Before deployment

- [ ] The commit and tag being deployed are identified and exist on the remote
- [ ] `git status` is clean and the deployed tree matches that commit
- [ ] Production connection string is configured outside source control
      (`ConnectionStrings__DefaultConnection`, IIS configuration, or a container secret)
- [ ] Database backup taken and its restorability confirmed, not just its existence
- [ ] `protected-uploads/` backed up
- [ ] `wwwroot/uploads/` backed up
- [ ] Folder permissions verified against `GHARS_PRODUCTION_OPERATIONS.md` §2.3
- [ ] Bootstrap administrator values ready **if and only if** this is a new database with no
      administrator (`GHARS_BOOTSTRAP_ADMIN_EMAIL`, `GHARS_BOOTSTRAP_ADMIN_PASSWORD`)
- [ ] No development secrets present in production configuration — no `Ghars:Seed:DemoPassword`, no
      development connection string, no `appsettings.Development.json` in the deployed output
- [ ] Public contact details configured if they are to be shown (`Ghars:Contact:*` — see `README.md`).
      Unset is safe: the panel is hidden and the enquiry form still works
- [ ] `ASPNETCORE_ENVIRONMENT` is set to `Production`

## Database

- [ ] Pending migrations reviewed — you know what each one changes
- [ ] **One-time history repair applied if required.** A database created before 2026-09-06 is
      missing four migration history rows and `database update` will fail without them. The tell is a
      row named `20260505103249_new one ` in `__EFMigrationsHistory`; the `INSERT` is in
      `GHARS_PRODUCTION_OPERATIONS.md` §6.3. Fresh databases need nothing
- [ ] `dotnet build` run **before** `dotnet ef database update` — `--no-build` against a stale
      assembly reports `Done.` while applying nothing
- [ ] `dotnet ef database update` executed successfully
- [ ] Migration history verified: `SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId`
- [ ] Schema sanity checked — the tables and columns the release expects are present
- [ ] **Ghars Annual Report / Ghars Channel release only** —
      `20260908074525_AddAnnualReportAndGharsChannel` applied. It is additive and non-destructive:
      one new table (`GharsAnnualReports`, with a unique index on `OrganizationId, SeasonId`) and eight
      nullable columns on `GalleryItems`. No existing column is dropped, renamed or made non-nullable,
      so existing gallery rows keep working unchanged — a null `ApprovalStatus` means "outside the
      implementing-entity review workflow", which is every pre-existing row
- [ ] **Program attachments release only** — `20260908092255_AddActivityAttachments` applied. Purely
      additive: one new table (`ActivityAttachments`) with a cascade FK to `Activities` and an index on
      `ActivityId`. No existing table or column is touched, and every existing offering simply has no
      attachments. Its files live in the new `protected-uploads/programs/` folder — create it with the
      same permissions as the other categories (§2.3 of `GHARS_PRODUCTION_OPERATIONS.md`)

## Files

- [ ] `protected-uploads/` preserved through the deployment, not replaced
- [ ] The deployment process does **not** mirror-delete (`robocopy /MIR`, "remove additional files",
      image swap) either upload root
- [ ] `wwwroot/uploads/` handled: runtime uploads preserved, static assets updated
- [ ] Backup path confirmed and reachable from the new deployment

## Application

- [ ] Application starts and the log reports `Hosting environment: Production`
- [ ] Log shows `Demo/sample seeding skipped: environment is Production, not Development.`
- [ ] Required roles seeded — `SELECT COUNT(*) FROM AspNetRoles` returns 7
- [ ] An active season exists — `SELECT COUNT(*) FROM Seasons WHERE IsActive = 1` returns at least 1
- [ ] Administrator sign-in works
- [ ] Bootstrap password changed after first sign-in, and
      `GHARS_BOOTSTRAP_ADMIN_PASSWORD` removed from the deployment environment
- [ ] Restart the application, then sign in again — **the password still works.** Startup must never
      reset a password
- [ ] No demo accounts exist — `SELECT COUNT(*) FROM AspNetUsers WHERE Email LIKE '%@ghars.local'`
      returns 0. Anything else means the application was started in `Development`; those credentials
      are public, so delete the accounts and rotate whatever they could reach

## Smoke tests

- [ ] Public home page renders, in both English and Arabic
- [ ] DSC Admin sign-in and dashboard
- [ ] Club sign-in, scoped to that club's own data
- [ ] Partner sign-in, scoped to that entity's own programmes
- [ ] Booking: create a request, respond as the partner, confirm
- [ ] Agenda: list and create an entry
- [ ] KPI: submit and review a submission
- [ ] Protected file download through `/protected-files/...` as an authorized user
- [ ] Gallery renders; published media is visible
- [ ] Surveys page loads, internal and official
- [ ] Digital library loads and a document opens
- [ ] Reports and dashboards render with data
- [ ] Contact page: submit an enquiry, confirm it appears under **Admin → Contact Messages** and that
      a notification reached a DSC Admin. Delete the test enquiry afterwards

## Security checks

- [ ] Cross-organization access blocked: signed in as one club, requesting another organization's
      record returns 404/403, not data
- [ ] Posting a foreign `OrganizationId` is ignored — identity is taken from the signed-in user
- [ ] Protected evidence is inaccessible anonymously
- [ ] Path-traversal payloads on file endpoints return 404 and disclose no path or configuration
- [ ] Unpublished gallery media is inaccessible to anonymous and other-organization users
- [ ] Static access to `/uploads/kpi`, `/uploads/surveys` and `/uploads/agenda` is denied
- [ ] External URL fields reject unsafe schemes (`javascript:`, `data:`, `file:`)
- [ ] HTTPS enforced and HSTS active

## After deployment

- [ ] Backup job includes `protected-uploads/` — verify by inspecting the job, not by assuming
- [ ] Application logs reviewed for errors and for unexpected seeding messages
- [ ] First post-deployment database backup taken and verified
- [ ] Deployed commit SHA recorded in the table at the top of this page
- [ ] Deployed tag recorded
- [ ] Migration state recorded — the last `MigrationId` in `__EFMigrationsHistory`

---

> **A restored database without its matching `protected-uploads/` is an incomplete restore.** Missing
> files return an ordinary `404`, so the loss is silent. The reconciliation query that detects it is
> in `GHARS_PRODUCTION_OPERATIONS.md` §4.1.
