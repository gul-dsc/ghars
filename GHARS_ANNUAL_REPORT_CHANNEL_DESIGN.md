# Ghars Annual Report & Ghars Channel — Design Note

**Date:** 2026-09-08
**Status:** design decisions taken before implementation, per the requirement to analyse before making
speculative schema changes.
**Business source for Part 1:** `docs/Ghars_Clubs Report Form.docx` — the official Arabic club report
form issued by Dubai Sports Council. It was read in full; its structure is reproduced in §1.1 below.

This note covers two approved requirements:

1. **Ghars Annual Report / التقرير السنوي لغرس** — make the supplied Word form electronic and linked
   to data Ghars already holds.
2. **Ghars Channel / قناة غرس** — rename the user-facing Media/Gallery section and extend it into a
   two-stream content channel.

Nothing else in the platform changes.

---

# PART 1 — GHARS ANNUAL REPORT

## 1.1 The source template, exactly as supplied

The document is Arabic-only, titled `تقرير برنامج "غرس"` with a `الموسم الرياضي` (sports season) line,
and contains four sections built entirely from tables:

| # | Arabic heading | English |
| --- | --- | --- |
| أولا | البيانات الأساسية | Basic information |
| ثانيا | المؤشرات الإجمالية لتنفيذ البرنامج | Overall program implementation indicators |
| ثالثا | تفاصيل المحاضرات والبرامج المنفذة | Details of implemented lectures / programs |
| رابعا | أبرز النتائج والملاحظات | Key results and notes |

**Section 1** has four labelled rows: `اسم النادي`, `المسؤول عن برنامج "غرس"`, `رقم التواصل`,
`البريد الإلكتروني`.

**Section 2** has six labelled rows: `عدد المحاضرات المقترحة من النادي`,
`عدد المحاضرات المقترحة من مجلس دبي الرياضي`, `إجمالي المحاضرات المنفذة`, `عدد المحاضرين`,
`عدد الجهات المنفذة / المشاركة`, `إجمالي عدد المشاركين`.

**Section 3** is a seven-column table — `م`, `عنوان المحاضرة / البرنامج`, `الجهة المنفذة`, `المحاضر`,
`التاريخ`, `الفئة المستهدفة`, `عدد المشاركين` — with twenty numbered blank rows. The twenty rows are a
paper artefact; the electronic version is not limited to twenty.

**Section 4** has three prompts, each followed by a single empty box:
`أبرز النتائج والأثر المحقق من تنفيذ البرنامج`, `أبرز التحديات التي واجهت النادي في التنفيذ`,
`مقترحات النادي لتطوير برنامج "غرس" خلال الموسم الرياضي`.

## 1.2 Entity decision

**A dedicated entity, `GharsAnnualReport`, not an extension of `KpiSubmission`.**

`KpiSubmission` is a fourteen-metric statistical return with its own approval state, its own evidence
documents, its own unique index on (Season, Organization) and its own meaning on the admin dashboard
and in Reports/Analytics. The Annual Report is a different business record: it is narrative, it carries
a contact snapshot, and it must freeze (§1.6) while KPI must not. Overloading `KpiSubmission` would
give one row two review lifecycles and two "approved" meanings.

This follows the precedent already set in this codebase by `OfferingApprovalStatus`, which was created
rather than widening `ActivityStatus` for exactly this reason.

```
GharsAnnualReport : AuditableEntity
  Id, SeasonId, OrganizationId
  Status                        AnnualReportStatus
  ProgramCoordinatorName, ContactNumber, ContactEmail      -- section 1 snapshot
  KeyResults, Challenges, DevelopmentProposals             -- section 4 narrative
  ClubProposedLectures, CouncilProposedLectures,
  TotalLecturesDelivered, LecturersCount,
  ImplementingEntitiesCount, ParticipantsTotal             -- section 2 snapshot
  DeliveredActivitiesJson                                  -- section 3 snapshot
  SnapshotTakenAtUtc
  SubmittedAtUtc, SubmittedByUserId
  ReviewedAtUtc, ReviewedByUserId, ReviewNotes
  CreatedAtUtc / CreatedByUserId / UpdatedAtUtc / UpdatedByUserId  (inherited)
```

Unique index on **(OrganizationId, SeasonId)** — one report per club per sports season, enforced in the
database as well as in validation, mirroring `KpiSubmission`'s existing unique index.

## 1.3 Workflow

Reuses the KPI review conventions rather than inventing a third style:

```
Draft ──submit──▶ Submitted ──approve──▶ Approved     (final; read-only)
  ▲                   │
  │                   ├──return──▶ ReturnedForCorrection ──┐
  └───────edit────────┤                                     ├── club edits, resubmits
                      └──reject──▶ Rejected ────────────────┘
```

`AnnualReportStatus` mirrors `KpiSubmissionStatus`'s numeric layout (Draft=1, Submitted=2, Approved=3,
Rejected=4) and names the returned state `ReturnedForCorrection=5` rather than KPI's
`MoreInfoRequired=5`, because the requirement names it that way and because the offering workflow
already uses that word. A separate enum, for the same reason the entity is separate.

Editable states: Draft, ReturnedForCorrection, Rejected. Approved and Submitted are locked to the club.

## 1.4 Club identity

Server-derived from `OrganizationAdminLink`, exactly as `Public/KpiController` and
`Public/AgendaController` already do. There is **no** club dropdown, no hidden club id and no editable
`OrganizationId` on the club-facing form; the club name renders read-only. Every query is scoped by the
authenticated user's links and a miss returns `NotFound()`, matching the platform's non-disclosing
convention. DSC Admin and Super Admin filter by club on the management screens only.

## 1.5 Data-source mapping (§ template → electronic)

| Template field | Class | Source |
| --- | --- | --- |
| اسم النادي — club name | SYSTEM-DERIVED | `Organization` resolved from `OrganizationAdminLink`; read-only |
| المسؤول عن البرنامج — coordinator | MIXED / CONFIRMED | pre-filled from the club's primary `OrganizationContact`, editable, stored as a season snapshot |
| رقم التواصل — phone | MIXED / CONFIRMED | same |
| البريد الإلكتروني — email | MIXED / CONFIRMED | same |
| عدد المحاضرات المقترحة من النادي | SYSTEM-DERIVED | delivered agenda entries **not** sourced from the DSC catalogue (§1.7) |
| عدد المحاضرات المقترحة من المجلس | SYSTEM-DERIVED | delivered agenda entries sourced from the DSC catalogue (§1.7) |
| إجمالي المحاضرات المنفذة | SYSTEM-DERIVED | count of delivered agenda entries |
| عدد المحاضرين | SYSTEM-DERIVED | distinct normalised `AgendaEntry.LecturerName` |
| عدد الجهات المنفذة | SYSTEM-DERIVED | distinct normalised `AgendaEntry.DepartmentOrOrganization`, club itself excluded |
| إجمالي عدد المشاركين | SYSTEM-DERIVED | `SUM(AgendaEntry.NumberOfParticipants)` |
| Section 3 detail table | SYSTEM-GENERATED | one row per delivered agenda entry; read-only |
| Section 4 narrative ×3 | CLUB-SUBMITTED | free text |

**"Delivered" means `AgendaEntryStatus.Submitted` or `Approved`** — the definition already used by
`Public/KpiController.GetDerivedAsync`. Draft and Rejected agenda rows are excluded, and so are
published partner offerings, unconfirmed bookings and the Activity catalogue: none of them is evidence
that anything was delivered.

## 1.6 Snapshot / data freeze

| State | Derived values |
| --- | --- |
| Draft | recomputed from live Agenda on every open and every save |
| Submitted | frozen at the moment of submission |
| ReturnedForCorrection | unfrozen — the club is editing again, so it refreshes |
| Rejected | unfrozen, for the same reason |
| Approved | frozen permanently; never recomputed |

The snapshot is taken in one place (`CaptureSnapshot`) called on every submit, so create-and-submit and
edit-and-submit cannot diverge. It stores the six section-2 totals in columns and the full section-3
detail table as JSON in `DeliveredActivitiesJson`.

**Why JSON in one column rather than child rows.** The requirement is to preserve exactly what was
submitted, not to make the detail queryable — nothing reports across annual-report detail rows, and
the live Agenda remains the queryable source. A child table would add an entity, a migration and a
delete path for no analytical gain, and would read as a second copy of the agenda dataset, which the
requirement explicitly warns against. Each snapshot row keeps its `AgendaEntryId`, so an authorised
user still gets a link back to the live entry (§1.8) without the report depending on it.

Editing an old agenda row therefore cannot change an approved report. Narrative text is stored in its
own columns and is never touched by a snapshot refresh, so refreshing derived data cannot lose what the
club typed.

## 1.7 Club-proposed vs Council-proposed

Ghars does record enough to determine this, through the booking link that already exists on
`AgendaEntry.BookingRequestId`:

| Situation | Classified as | Why |
| --- | --- | --- |
| entry has a booking whose `ActivityId` is set | **Council** | the club selected it from the DSC-managed catalogue — either a DSC-created activity, or a partner offering that only became visible because DSC approved and published it |
| entry has a booking with no `ActivityId` | **Club** | a Request Custom Booking, whose subject the club wrote itself |
| entry has no booking at all | **Club** | the club recorded its own activity directly in the Agenda |

This is derived, not guessed, and it is **not applied silently**: the rule is stated on the report
screen and the origin of every single delivered activity is shown in its own column in the section-3
table, so the club can see exactly which activities produced each of the two numbers.

**Historical limitation, documented as required:** agenda rows created before the booking→agenda link
existed carry no `BookingRequestId` and are therefore counted as club-proposed. On the current
development database, 8 of 10 delivered entries have no booking link. This is the correct default —
an activity with no catalogue booking behind it was not proposed by the Council — but it cannot
distinguish "the club's own initiative" from "a legacy row whose origin was never recorded", and it is
labelled as such in the UI rather than presented as certain.

## 1.8 Centralised statistics (no duplicate calculations)

A new helper, `Helpers/SeasonClubStatistics.cs`, holds **the single delivered-agenda query** and is
called by both `Public/KpiController` and the Annual Report. `GetDerivedAsync` in the KPI controller is
replaced by a call into it, so the two surfaces cannot drift into reporting 12 and 11 for the same
club and season.

One deliberate refinement is made inside that shared helper: an implementing entity whose name equals
the club's own name is not counted as an implementing entity. This was verified against the
development database first — no agenda row currently has `DepartmentOrOrganization` equal to its club's
`NameEn` or `NameAr` — so it changes no existing KPI value and only prevents a wrong count in future
data.

The uniqueness of both lecturers and implementing entities is based on the **normalised free-text name**
(trimmed, case-insensitive). `AgendaEntry.LecturerName` and `DepartmentOrOrganization` are free text;
there is no master-person or master-entity relationship to prefer. The same lecturer appearing in three
activities counts once, which is what "number of lecturers" asks for.

## 1.9 Relationship to KPI

One-directional: the Annual Report **consumes** the same derivation KPI uses. It never writes to
`KpiSubmission`, and `KpiSubmission` never reads the Annual Report, so there is no circular dependency.

Where an **Approved** KPI submission exists for the same club and season and its stored figure differs
from the agenda-derived figure, the Annual Report shows the approved KPI number beside the derived one,
labelled with its source and status, rather than silently overriding either. (This will occur on
demo data, where several KPI rows were seeded with figures that were never agenda-derived.)

## 1.10 Export

An HTML print view (`/annual-report/print/{id}`, and the DSC equivalent) that follows the template's
structure — title, sports season, then the four sections in order — using a print stylesheet, plus a
QuestPDF export via the existing `CertificatePdfBuilder` dependency. The output is readable and
correctly ordered in Arabic and English; it is deliberately not a pixel copy of the Word file.

## 1.11 Security

Club ownership is checked **inside every query** through the user's `OrganizationAdminLink` set, never
by trusting a route or form value. Cross-club access — view, edit, submit, print, export — returns
`NotFound()`, consistent with `ProtectedFilesController` and the partner offering controllers. DSC Admin
and Super Admin keep cross-club review access.

---

# PART 2 — GHARS CHANNEL

## 2.1 Rename scope

`Media` / `Gallery` becomes **Ghars Channel / قناة غرس** in user-facing text only: public navigation,
page headings, empty states, admin sidebar labels and dashboard shortcuts. Controllers, tables, DbSets
and view folders keep their names — renaming them would be churn with migration risk and no business
value. The existing `/gallery` routes keep working; `/channel` is added as the primary route so
existing links and bookmarks do not break.

## 2.2 Model reuse

`GalleryItem` is already the aggregation point for the channel: `AgendaController.SaveAgendaMedia`
writes an `AgendaMedia` row and a `GalleryItem` row from one uploaded file, and both the public gallery
and the admin gallery already read `GalleryItem`. Creating a parallel `GharsChannelItem` would fork
that. `GalleryItem` is therefore **extended**, with columns that are all nullable and additive:

| Column | Purpose |
| --- | --- |
| `ChannelCategory` | ClubActivity / Awareness / Educational / Official / Press — the §38 filters |
| `ApprovalStatus` | `ChannelApprovalStatus?`; **null** = not in the partner workflow (every existing row) |
| `SubmittedAtUtc`, `SubmittedByUserId` | partner submission stamps |
| `ReviewedAtUtc`, `ReviewedByUserId`, `ReviewNotes` | DSC review stamps |
| `LibraryItemId` | optional pointer to a Digital Library item instead of a duplicate upload |

`MediaAlbum` / `MediaItem` (curated albums) and `AgendaMedia` are untouched.

## 2.3 Two streams, two moderation models

The requirement explicitly warns against forcing both streams through one workflow.

**Stream A — club activity media.** Unchanged. A club uploads through its Agenda entry; the file is
stored once and surfaces in the Channel automatically with `IsPublished = true` and
`ApprovalStatus = null`. `IsPublished = false` continues to mean *a DSC takedown*, not a draft. DSC
keeps Hide, and Delete on agenda-sourced rows continues to unpublish rather than delete, preserving the
club's agenda record. No second submission workflow is imposed on a club photo.

A second club entry point is added — "contribute media" from the Channel — but it **requires an agenda
entry to be selected** and writes exactly the same rows through a shared helper, so it cannot create
disconnected club media with no activity context and cannot diverge from the Agenda path.

**Stream B — partner / government awareness content.** New, and gated:

```
Draft ──submit──▶ SubmittedForApproval ──approve──▶ Approved (published)
  ▲                    │       │                        │
  └──── edit ──────────┴─ return / reject ──────────────┴── unpublish
```

A partner row is created with `IsPublished = false`; only a DSC approval sets it true. Visibility to
anyone else therefore requires **both** fields to agree — `IsPublished == true` **and**
(`ApprovalStatus == null || ApprovalStatus == Approved`) — the same two-field rule the offering
catalogue already uses, centralised in `ChannelWorkflow` so no query can implement half of it.

## 2.4 Distinction from the Digital Library

| | Ghars Channel | Digital Library |
| --- | --- | --- |
| Content | photos, videos, awareness clips, activity coverage, campaign material | booklets, publications, structured educational documents |
| Shape | visual media | documents (PDF) |
| Uploads accepted | images and video only | PDF and video |

**The Channel does not accept PDF or Office uploads.** Document-shaped content belongs in the Digital
Library, which already manages it with categories, points and download counting. To avoid a partner
having to choose between duplicating a booklet and not surfacing it, a Channel item may instead carry
`LibraryItemId` and render as a link into the Library. Nothing is copied.

This also keeps the Channel's file handling narrow: one validation profile
(`FileValidationHelper.Media` — images and video, existing size limits, MIME check, GUID filenames) and
no new document-serving path in a public media surface.

## 2.5 Roles

No new role. The requirement's "government entity" is already modelled: `OrganizationType.GovernmentAuthority`
and `OrganizationType.OtherPartner` are both administered by **Partner Admin**, and
`PartnerProgramsController.PartnerOrganizationIdsAsync` already treats them identically. There is no
permission gap that a `GovernmentEntityAdmin` role would close, so none is introduced.

Partner content management lives at `/partner/channel` — **"My Ghars Channel Content" / "محتواي في قناة غرس"** —
deliberately separate from `/partner/programs` ("My Programs"). A program is a bookable offering; channel
content is published material. Mixing them would put two unrelated lifecycles on one screen.

## 2.6 File access

Reuses the existing conditional endpoint rather than regressing to static hiding. Partner channel
uploads are written to `wwwroot/uploads/channel`, which is added to `Program.cs`'s denied static
prefixes alongside `/uploads/agenda`, so the bytes are reachable **only** through
`/protected-files/gallery/{id}`. That endpoint's existing publication check is extended with the
approval check of §2.3, so a Draft, Submitted, Returned, Rejected, Unpublished or Hidden item's file is
not served to the public even when its URL is known. The owning organization's admins and DSC reviewers
keep access, so a partner can still preview their own draft.

## 2.7 Reporting isolation

Channel uploads are evidence, not delivery. Nothing in this work adds a Channel count to delivered
activity counts, KPI values, booking counts or Agenda counts, and the Annual Report's totals and detail
table come from `AgendaEntry` alone (§1.5) — never from `GalleryItem`.

---

## Migration

One additive, non-destructive migration covering:

* new table `GharsAnnualReports` + unique index `(OrganizationId, SeasonId)`;
* eight nullable columns on `GalleryItems`, plus an index supporting the channel's approval queue.

No column is dropped, renamed or made non-nullable, and no historical migration is edited.
