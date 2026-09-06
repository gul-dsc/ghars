# Ghars Platform — Repository Baseline

Verified production-candidate baseline for the Ghars Platform.

**Date:** 2026-09-06

---

## 1. Repository

| Item | Value |
| --- | --- |
| Remote | `git@github.com:gul-dsc/ghars.git` |
| Default branch | `main` |
| Baseline commit | `4f1e77b57335a62e2c5f5df2806c5b1358ea9848` |
| Commit message | `release: Ghars 2026 production candidate` |
| Tag | `ghars-docs-alignment-2026-production-candidate` |
| Tracked files | 248 |

The remote repository was **empty** when this baseline was pushed — `git ls-remote` returned no refs,
so the initial `README` commit described in the setup notes never landed. There was therefore no
history to reconcile, no merge or rebase was required, and **no force push was used**.

The tag is annotated and points at the commit that adds this document, whose parent is the baseline
commit above.

## 2. Build

```
dotnet build --no-incremental
```

| Result | Value |
| --- | --- |
| Errors | **0** |
| Warnings | **1** |

The single warning is expected and deliberately retained:

```
Models/Core/Activity.cs(51,19): warning CS0108: 'Activity.CreatedByUserId' hides inherited member
'AuditableEntity.CreatedByUserId'. Use the new keyword if hiding was intended.
```

It is **not** noise. Only the derived property is mapped by EF Core, so the base property is shadowed
and `((AuditableEntity)activity).CreatedByUserId = x` compiles while being silently discarded. Adding
`new` would hide that hazard rather than remove it. Nothing in the code does this today, but a future
generic audit interceptor over `AuditableEntity` would silently fail to stamp `Activities`. The
reasoning and the correct fix are in §21.2 of `GHARS_IMPLEMENTATION_REPORT.md`.

EF Core also reports `decimal` precision warnings for the `KpiSubmission` rate columns at design time.
They default to `decimal(18,2)`, which is adequate for percentages; changing them would require a
migration touching approved KPI values.

## 3. Migration state

| Check | Result |
| --- | --- |
| Migrations in repository | 9 |
| Recognised by `dotnet ef migrations list` | 9 |
| `has-pending-model-changes` | No changes since the last migration |
| Applied to development database | 10 rows (the 9 above plus the legacy `20260505103249_new one `) |
| Fresh database built from repository alone | Succeeds — all 9 apply in order |
| Schema diff, fresh vs. development database | **0 differences** (49 tables, 549 columns both sides) |

Latest migrations:

```
20260218043146_FixSurveyCascadePaths
20260503170000_AddBookingTimeProposals
20260503173000_AddActivityPartnerOrganization
20260504120000_AddGharsAgendaKpiGalleryLibraryEnhancements
20260505103000_AddGalleryAlbumLinksAndPdf
20260511090534_AddDetailedClubBookingWorkflow
20260905223140_GharsDocsAlignment2026
20260906084642_AddKpiSatisfactionSurveyLineage
20260906140000_RestoreOrganizationAdminLinkAuditColumns
```

> **Databases created before 2026-09-06 need one manual step** before `dotnet ef database update`.
> Four migrations were previously invisible to EF Core and their history rows are missing. The
> `INSERT` that records them, and how to tell whether a database needs it, are in
> `GHARS_PRODUCTION_OPERATIONS.md` §6.3. Fresh databases need nothing.

## 4. What is deliberately excluded from source control

Runtime and operational data is not source and is never committed. It is protected by backup and
restore procedures instead — see `GHARS_PRODUCTION_OPERATIONS.md`.

| Excluded | Why |
| --- | --- |
| `protected-uploads/` | KPI evidence, organization licences, official survey reports — live business data. |
| `wwwroot/uploads/` | Runtime uploads, including generated certificate PDFs that carry participant names. |
| `bin/`, `obj/`, `.vs/`, `*.user` | Build output and IDE state; reproducible. |
| `*.mdf`, `*.ldf`, `*.bak`, `*.db` | The database itself. The repository holds migrations and seed code, not data. |
| `appsettings.Development.json`, `appsettings.Production.json`, `.env`, `secrets.json` | Local and deployment configuration that may carry credentials. |
| `*.log`, `.remember/` | Generated logs and local tooling state. |

One deliberate exception: `wwwroot/uploads/library/sample-ghars-values.pdf` is committed because
`DbSeeder` references it by a fixed path, so omitting it would leave a freshly seeded database with a
broken link.

**A restored database without the matching restored protected files is an incomplete restore.** The
file endpoints report a missing file as an ordinary `404`, so the loss is silent.

## 5. Security verification carried into this baseline

Verified during the production-hardening pass (§21.9 of `GHARS_IMPLEMENTATION_REPORT.md`) and unchanged
by the baseline work:

- Cross-club isolation on KPI evidence, agenda and bookings, in both directions.
- A club posting another organization's `OrganizationId` has it discarded; identity is server-derived.
- Path-traversal payloads return `404` even for a DSC Admin, with no configuration content disclosed.
- Missing protected files return an **empty-bodied** `404` that leaks no server path or filename.
- Unpublishing gallery media withdraws the file from anonymous, other-club and partner access, while
  published media stays public.
- Static access to `/uploads/kpi`, `/uploads/surveys` and `/uploads/agenda` is denied.

No secrets were found in the committed tree. `appsettings.json` contains only a Windows-authentication
development connection string with no credentials.

## 6. Known open items

### Security item requiring a decision before go-live

**Seeding is not environment-gated.** `Program.cs` calls `DbSeeder.SeedAsync` unconditionally, and
`EnsureSeedPasswordAsync` resets every seeded account's password to a hard-coded constant on **every**
application start. The demo accounts are therefore created in whatever environment the application
runs, and an administrator who changes one of those passwords will find it reset at the next restart.

This was flagged rather than changed: gating the seeder without providing a bootstrap path would leave
a fresh production database with no administrator. Details in `SEED_CREDENTIALS.md`.

### Business decisions, unchanged and still open

1. **Attendance denominator / source** — no registered-player field exists in the schema, so the
   official KPI remains club-submitted and DSC-approved, now labelled as such.
2. **Satisfaction numeric source** — `ExternalSurvey` has no numeric result field; the percentage is
   transcribed by hand. Lineage now records which report supports it.
3. **Certificate PDF public access** — the PDF filename is the verification token, and the PDF carries
   the participant's name while the verification page does not.
4. **Internal vs Official Survey long-term ownership** — both engines remain, results stay separate.
5. **Historical satisfaction-lineage backfill** — the 14 historical rows remain `NULL`; no backfill by
   guesswork.

### Minor, pre-existing

`GalleryItem` 9 references `/uploads/agenda/de0dc770….pdf`, which is absent from disk. The row dates
from 2026-05-13 and the dangling reference predates this work.

## 7. Related documentation

| Document | Contents |
| --- | --- |
| `README.md` | Developer setup, configuration, roles. |
| `handover.md` | Architecture and conventions. |
| `GHARS_REQUIREMENTS_GAP_ANALYSIS.md` | Approved requirements vs. implementation. |
| `GHARS_IMPLEMENTATION_PLAN.md` | The plan derived from that analysis. |
| `GHARS_IMPLEMENTATION_REPORT.md` | Implementation, hardening (§21) and baseline (§22) detail. |
| `GHARS_PRODUCTION_OPERATIONS.md` | Backup, restore, deployment, permissions, disaster recovery. |
| `docs/` | The approved bilingual programme documents. |
