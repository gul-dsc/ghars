# Ghars Platform — Implementation Report

Date: 2026-09-06
Companion documents: `GHARS_REQUIREMENTS_GAP_ANALYSIS.md`, `GHARS_IMPLEMENTATION_PLAN.md`

---

## 1. Executive Summary

The Ghars Platform was aligned with the eight approved business documents in `/docs` without rebuilding any module. All existing functionality was preserved; nothing mature was removed.

The eight substantive changes delivered:

1. **Organization scoping hardened** — Club Admins no longer see (or post) their own club anywhere. The club is resolved server-side from `OrganizationAdminLinks`; a posted foreign club id is ignored. Verified by live spoofing test.
2. **Booking system aligned with the approved workflow** — clubs can now submit **direct entity-first requests** (select season + implementing entity, then describe the activity), the documented flow, while the stronger existing program-booking flow is retained. Confirmation now captures **lecturer name, contact details and logistical requirements**; entities can propose a **subject** change alongside alternative times.
3. **Agenda is now genuine delivery evidence** — a confirmed booking pre-creates a **Draft** agenda entry (previously it was auto-marked Submitted, i.e. auto-reported as delivered). Clubs complete the actual date, lecturer, real participant count and media, then submit. A new club Edit action makes this possible.
4. **Gallery aggregation completed** — agenda media already wrote `GalleryItem` rows but nothing ever displayed them. The public gallery now renders them with Agenda/DSC/Press source badges, and DSC staff have a new admin screen for official photos, videos and press coverage. Files are referenced, never duplicated.
5. **Official external survey workflow added** — DSC publishes the Dubai Digital Authority survey link and later uploads/publishes the analysed PDF report; clubs see "Open survey" plus a "View report" button that appears only once a report is published. The internal survey engine was **kept** (dependency analysis) and completed: it is now bilingual and supports MCQ end-to-end.
6. **KPI/statistics became a real workflow** — Draft → Submit → DSC Review → Approved / Returned / Rejected, with club edit-and-resubmit, reviewer notes shown to the club, evidence upload validation, notifications and audit logging.
7. **KPI calculations corrected and centralised** — a single `GharsKpiCatalog` now supplies every target/name/formula (previously duplicated across four files with two defective calculations). The violations KPI is now an **annual reduction vs the previous season** (was a raw count compared to 15) and physical activity uses a new **% of players ≥150 min/week** field (was a club-wide minutes total compared to 150). Missing data renders as **"No Data"**, never 0%.
8. **Reporting and public content** — new Season Summary by Club and 2026→2033 KPI progression tables; About/Vision pages now carry the approved bilingual programme wording (strategic context, vision, 9 objectives, scope, 3 implementation mechanisms).

Build: **0 errors, 1 warning** (`dotnet build --no-incremental`). The single warning is pre-existing and
was deliberately left in place — see §17 for the build output and §21.2 for the full investigation and the
decision not to change it. The migration was applied to the existing populated database and the full
application was exercised live as Club Admin, Partner Admin and DSC Admin.

---

## 2. Documents Reviewed

All eight `.docx` files under `/docs` were extracted and read in full (Arabic and English sections):

| # | File | Reviewed |
|---|------|----------|
| 1 | `About the "Ghars" Programحول برنامج  غرس  .docx` | ✔ |
| 2 | `Ghars Lecture & Events Booking System.docx` | ✔ |
| 3 | `التقارير Reports.docx` (Agenda) | ✔ |
| 4 | `Photo & Video Gallery.docx` | ✔ |
| 5 | `Digital Library.docx` | ✔ |
| 6 | `Data Entry Reports & Statistics .docx` | ✔ |
| 7 | `Key Performance Indicators (KPIs).docx` | ✔ |
| 8 | `Surveys.docx` | ✔ |

`handover.md` was also reviewed in full, along with every controller, entity, view and migration in the solution.

---

## 3. Requirements Compliance

| Document | Status | Notes |
|---|---|---|
| About the Ghars Program | **Complete** | Approved AR/EN wording for intro, vision, 9 objectives, scope, audiences and 3 mechanisms |
| Booking System | **Complete** | Entity-first flow, all documented fields, coordinator routing, confirmation with lecturer details, date/time/**subject** modification, club accept/reject |
| Agenda (Reports doc) | **Complete** | All fields, Others-requires-text, media, draft-from-booking, actual-delivery submission, end-of-season summary table |
| Photo & Video Gallery | **Complete** | Automatic agenda aggregation now visible publicly; DSC/press uploads; source indicators; no file duplication |
| Digital Library | **Complete** | PDF-first with cover+link fallback enforced; publisher and publication date now mandatory; pagination; bilingual; Council-managed |
| Data Entry Reports & Statistics | **Complete** | Season + auto club, all indicators, evidence upload, Draft/Submit/Review/Approve, KPI table by club & season, 2026 baseline |
| Key Performance Indicators | **Complete** | All 10 approved targets centralised; two defective calculations fixed |
| Surveys | **Complete** (+ conflict noted) | Internal engine completed & bilingual with MCQ **and** official external survey + report workflow added — see §18 |

**Outstanding:** none blocking. Four business decisions are listed in §18.

---

## 4. Security / Organization Scoping

Verified live against the running application:

| Check | Result |
|---|---|
| Club Admin club is server-derived | **Confirmed** — no `<select name="OrganizationId">` and no hidden club input on Booking, Agenda or KPI create forms; club shown read-only |
| Club id spoofing | **Blocked** — POST `/agenda/create` with foreign `OrganizationId=31` while logged in as club 30 stored the record under **club 30** |
| Partner organization is server-derived | **Confirmed** — no entity selector exists anywhere; partner org always resolved from `OrganizationAdminLinks` |
| IDOR — foreign booking | `/bookings/details/2` (other club) → **404** |
| IDOR — foreign agenda | `/agenda/edit/3` (other club) → **404** |
| IDOR — foreign KPI | `/kpi/edit/4` (other club) → **404** |
| Own records | `/bookings/details/7` → **200** |
| Role boundaries | Club → `/Admin/Dashboard` **302**; Club → `/partner` **302**; Partner → `/club` **302** |
| DSC cross-org access | **Intentional and preserved** — club/entity/season filters retained on all management screens |

Every new endpoint (`/agenda/edit`, `/kpi/edit`, admin gallery media, external surveys) filters by the caller's organization inside the query itself, not in the UI.

---

## 5. Booking Changes

**Club flow.** `/bookings/create` accepts either `activityId` (existing program booking) or no parameter (new direct request). For a direct request the club selects an **active** season and an **approved** implementing entity (government authority / other partner only — clubs and unapproved organizations are never listed). Season and entity are re-validated server-side on POST. All documented fields are captured with the existing validation rules (end > start, participants > 0, "Others" requires text, contact person required). A reference number (`GHR-BK-yyyy-#####`) is issued and shown.

**Implementing-entity flow.** Confirmation now requires a **lecturer name** and captures **contact details** and **logistical requirements**; a confirmation attempt without a lecturer is rejected and the booking stays Pending (verified). The entity can alternatively propose modifications: multiple alternative time options (existing, stronger than the document) **plus an optional new subject**.

**Club response.** Accepting a proposed option confirms the booking and applies the proposed subject; the original subject is preserved in `BookingAuditTrail`. Rejecting all options returns the request to the entity.

**Routing decision (documented per brief §15).** No dedicated coordinator entity exists. Requests are routed to **all users linked to the implementing entity via `OrganizationAdminLink`** — reusing the existing relationship rather than inventing a parallel assignment system.

**Notifications** fire on submit, confirm (including lecturer name), reject, propose and club response — persisted per user plus SignalR broadcast. **Audit**: every transition writes a `BookingAuditTrail` row with old/new JSON.

**Status model unchanged** — no status was renamed or removed, protecting dashboards, reports, filters and counters.

---

## 6. Agenda Changes

- **Actual activity recording**: agenda entries represent delivered activities. Auto-created entries from confirmed bookings are now **Draft**, not Submitted — a confirmed booking is a plan, not evidence of delivery.
- **New Edit action** (`/agenda/edit/{id}`) lets a club complete the draft with the final date, lecturer, **actual** participant count and supporting media, then submit. Approved entries are locked.
- **Automatic club context**: single-club users see the club read-only; the server forces the id.
- **"Others" category** now requires descriptive text (server-side).
- **Media**: uploads are validated (type/MIME/size) and stored once, referenced by both `AgendaMedia` and `GalleryItem`.
- **Booking → Agenda**: pre-populates club, season, activity type, subject, entity, date, target group, participants and the confirmed lecturer. The duplicated creation logic in two controllers was consolidated into `Helpers/BookingAgendaHelper`.
- **Audit**: create/update/submit write `SystemAuditLog` entries.

Verified end to end: booking 15 confirmed → agenda draft 12 created with lecturer "Major Ahmed Al Mansoori" → club edited to the real date (2026-04-16) and actual participants (38 vs 45 planned) → submitted.

---

## 7. Gallery Changes

- The public gallery now renders published `GalleryItem` records — the agenda media that was previously written but never displayed — alongside curated albums, with the same season/club/date filters applied to both.
- **Source indicators** are derived from existing data (no schema change): *Agenda* when `AgendaEntryId` is set, *Press* for press/newspaper media types, otherwise *DSC*.
- **New admin screen** (`/Admin/Gallery/MediaItems` + `CreateMediaItem`) for Council staff to add official photos, official videos, press coverage and newspaper coverage by upload or external link, with publish/hide and filtering by season, club, media type and source.
- **No duplicate files.** Agenda media is referenced, not copied. Removing an agenda-originated item from the gallery only unpublishes it — the club's agenda record and file are preserved.

---

## 8. Survey Changes

**Official external survey (new).** `ExternalSurvey` entity + DSC admin module: bilingual title/description, https-only external URL, season, active flag, availability window, analysed-report PDF upload and a publish toggle. Members see the survey at `/surveys` with an "Open survey" button; the **"View report" button appears only when a report is uploaded *and* published** (verified). Report publication is audit-logged.

**Internal engine retained and completed.** Dependency analysis showed the internal engine feeds points, satisfaction analytics, admin results and seeded history — deleting it would break reports and historical data. Instead it was finished:
- The participant page is now **fully bilingual** (Arabic questions, labels, buttons, RTL).
- **MCQ is now supported end to end** — options render as radio inputs and the selection is persisted to `SurveyAnswer.SelectedOptionId` (validated against the question).

The two are clearly separated in the UI: "Official surveys" vs "Ghars activity surveys", and in the admin sidebar: "Official Surveys" vs "Activity Surveys".

**Security**: `javascript:` URL rejected with a validation message and nothing written to the database (verified); report uploads restricted to PDF.

---

## 9. KPI / Statistics Changes

Full workflow implemented and verified live:

| Step | Result |
|---|---|
| Club submits (season auto-defaults, club read-only) | HTTP 302, status **Submitted** |
| Server override of derived fields | Club posted 1/1/1/1 → stored **4 activities, 245 participants, 4 lecturers, 3 entities** from Agenda |
| DSC returns for correction with notes | status **MoreInfoRequired**, notes stored |
| Club edits the returned submission | `/kpi/edit/15` → 200, reviewer notes displayed |
| Club resubmits | status **Submitted** |
| DSC approves | status **Approved** |
| Audit | `KpiSubmitted`, `KpiReviewed`, `KpiSubmitted`, `KpiReviewed` |

- **Evidence upload** validated (pdf/xls/xlsx/csv/doc/docx/png/jpg, ≤25 MB), stored with GUID filenames, listed for the reviewer.
- **Returned** is surfaced to users as "Returned for correction" while the underlying `MoreInfoRequired` enum value is unchanged (protects dashboards/reports).
- Notifications on submit (to DSC reviewers, with inbox deliveries) and on decision (to the club).

**Automatic vs manual** (per brief §37):

| Indicator | Source | Behaviour |
|---|---|---|
| Lectures/activities, Participants, Lecturers, Implementing entities | **SYSTEM-DERIVED** | Computed from the club's Submitted/Approved agenda entries; read-only in the form; server recomputes on save. Falls back to manual entry when the club has no agenda data (stated on screen). |
| Participation, Attendance, Warnings/red cards, Training minutes, Physical-activity %, Lifestyle conditions, Ethics, Diet, Community events | **CLUB-SUBMITTED** | No authoritative platform source exists; captured with evidence. |
| Satisfaction | **APPROVED EXTERNAL SOURCE** | Backed by the official survey report; the internal star score is shown separately and labelled. |

**Privacy**: only aggregated condition counts are stored — no individual medical records.

---

## 10. KPI Calculations

All definitions live in `Models/Core/GharsKpiCatalog.cs` (name EN/AR, unit, target, comparison direction, source, formula, baseline year).

| KPI | Formula | Source | Target | Missing data |
|---|---|---|---|---|
| Program coverage | clubs with approved data ÷ total clubs × 100 | System | ≥ 80% | 0 clubs → shown as 0 coverage (denominator known) |
| Player participation | participating ÷ registered × 100 | Club | ≥ 60% | No Data |
| Attendance | participating ÷ registered × 100 | Club | ≥ 80% | No Data (falls back to operational check-in rate on the dashboard, labelled) |
| **Violations reduction** | **(prev season − current) ÷ prev season × 100** | Club | ≥ 15% annual reduction | **No Data** when no prior season or prior = 0 |
| Ethical values | adhering players ÷ total × 100 | Club | ≥ 90% | No Data |
| **Physical activity** | **players ≥150 min/week ÷ total × 100** | Club | ≥ 90% | **No Data** when not measured (nullable field) |
| Lifestyle disease-free | (participants − affected) ÷ participants × 100 | Club (aggregated) | ≥ 95% | **No Data** when participants = 0 |
| Healthy diet | adhering ÷ total × 100 | Club | ≥ 80% | No Data |
| Community events | events per club per season | Club | 5–10 annually | No Data |
| Satisfaction | official survey result | External approved | ≥ 85% | No Data |

Two defects fixed: violations was comparing a raw count against 15; physical activity was comparing club-wide total minutes against 150. Missing data is never rendered as 0% — verified in the UI and in the CSV export (13 "No Data" cells).

---

## 11. Reporting Changes

- KPI matrix now driven by the catalog, with per-row annual violations reduction and No-Data badges.
- **New: Season Summary by Club** — activities, participants, distinct lecturers, distinct implementing entities and target categories from delivered agenda entries (the document's end-of-season table).
- **New: KPI progression 2026 (baseline) → 2033 (target)** — per-season averages of approved submissions with an explicit note that blank cells mean no approved data, not zero.
- Filters retained and working: season, club, entity, activity type, target category, booking status, KPI indicator, KPI status, date range.
- CSV export rebuilt: catalog targets, derived counts, No-Data literals, and a UTF-8 BOM so Excel renders Arabic names correctly.
- Dashboard and reports now read the same catalog and the same approved-submission definitions, removing drift.

---

## 12. Digital Library Changes

- Publishing entity (EN or AR) and publication date are now **required**; external URLs must be http/https; file and cover uploads validated by type/MIME/size.
- Public page fully localized (title, categories, content types, publisher, buttons, pagination) and shows Arabic titles/descriptions in Arabic mode.
- Pagination preserves the selected category (previously it dropped the filter).
- Council-only management preserved; partner entities supply materials but cannot self-publish.

---

## 13. Database Changes

**Migration:** `20260905223140_GharsDocsAlignment2026` — applied successfully to the existing populated database (verified in `__EFMigrationsHistory`).

| Table | Change | Type |
|---|---|---|
| `BookingRequests` | `ActivityId` int → **int NULL** | Widening, no data loss |
| `BookingRequests` | `+ SeasonId int NULL` (FK → Seasons, NoAction) + index | Additive |
| `BookingRequests` | `+ LecturerName nvarchar(200) NULL` | Additive |
| `BookingRequests` | `+ LecturerContact nvarchar(200) NULL` | Additive |
| `BookingRequests` | `+ ProposedSubject nvarchar(250) NULL` | Additive |
| `BookingRequests` | FK to Activities re-created as NoAction | Behavioural (activities with bookings are cancelled, not deleted) |
| `KpiSubmissions` | `+ PhysicalActivityComplianceRate decimal(18,2) NULL` | Additive; NULL = No Data |
| `ExternalSurveys` | **New table** (+ FK → Seasons NoAction, index) | Additive |

No drops, no destructive alters, **no backfill required** (null season falls back to `Activity.SeasonId` at read time). Existing rows verified intact after migration.

---

## 14. Files Changed

**New (9):** `Models/Core/GharsKpiCatalog.cs`, `Models/Core/ExternalSurvey.cs`, `Helpers/FileValidationHelper.cs`, `Helpers/BookingAgendaHelper.cs`, `Controllers/Admin/ExternalSurveysController.cs`, `Views/Agenda/Edit.cshtml`, `Views/Kpi/Edit.cshtml`, `Views/Kpi/_KpiForm.cshtml`, `Views/Surveys/Index.cshtml`, plus admin views `ExternalSurveys/{Index,Create,Edit}.cshtml` and `Gallery/{MediaItems,CreateMediaItem}.cshtml`.

**Controllers (13):** Public `BookingsController`, `PartnerDashboardController`, `ClubDashboardController`, `AgendaController`, `KpiController`, `SurveysController`, `GalleryController`; Admin `DashboardController`, `ReportsController`, `KpiController`, `GalleryController`, `LibraryController`, `ExternalSurveysController`.

**Entities / data (5):** `BookingRequest`, `KpiSubmission`, `ExternalSurvey`, `AppDbContext`, `DbSeeder`.

**ViewModels (1):** `BookingCreateVm`.

**Views (20):** `Bookings/{Create,Details}`, `Agenda/{Index,Create,Edit}`, `Kpi/{Index,Create,Edit,_KpiForm}`, `Surveys/{Index,Take}`, `Gallery/Index`, `Library/Index`, `Home/{About,Vision}`, `ClubDashboard/Index`, `PartnerDashboard/Index`, `Shared/_Layout`, Admin `Dashboard/Index`, `Reports/Analytics`, `Kpi/{Index,Details}`, `Shared/_AdminLayout`.

**Migrations (3):** the new migration, its designer and the updated model snapshot.

No JavaScript, CSS or configuration files required changes (existing design language preserved).

---

## 15. Localization

Every new or modified screen is bilingual with RTL support, using the project's existing inline `T(en, ar)` convention (no competing framework introduced). Verified live in Arabic:

- About page: `dir="rtl"`, strategic plan, Social Agenda, scope, all three mechanisms, audience chips — all present.
- Vision page: vision statement, objectives, KPI names and targets (≥ 95%, ≥ 85%, 5–10) — all present.
- Library page: title, categories, "All" — all present.
- Booking, Agenda, KPI, Surveys, Gallery and the new admin screens use bilingual labels, statuses, buttons, validation messages, filters, empty states and table headings.

(Note: Razor HTML-encodes Arabic from `@T(...)` into numeric character references such as `&#x645;`, which render identically in the browser — confirmed by decoding the responses.)

---

## 16. Security Review

| Area | Result |
|---|---|
| Authentication | Unchanged (ASP.NET Identity, cookie auth) |
| Authorization | Role attributes preserved; new admin endpoints gated to Super Admin / DSC Admin; destructive actions Super-Admin-only |
| Organization filtering | All scoped queries filter by the caller's `OrganizationAdminLinks`; single-club users' club is forced server-side |
| IDOR prevention | Verified 404 on foreign booking, agenda and KPI records |
| Upload validation | Centralised extension + MIME + size checks on agenda media, KPI evidence, library file/cover, gallery media and survey reports; GUID filenames (original names never trusted) |
| External URL validation | http/https only for survey links, library links and gallery external media; `javascript:` rejected (verified, nothing persisted) |
| Anti-forgery | All new POST endpoints use `[ValidateAntiForgeryToken]` |
| Outbound links | `rel="noopener noreferrer"` on all new external links |
| Audit | Booking transitions, agenda create/edit/submit, KPI submit/review, survey report publication |

**Known pre-existing limitation (not introduced here):** all uploads live under `wwwroot/uploads` and are therefore reachable by direct URL if the path is known (mitigated by GUID filenames). Moving evidence to authorised streaming is a platform-wide storage change and is recorded as a follow-up recommendation.

---

## 17. Build Results

```
dotnet build --no-incremental
Build succeeded.
    1 Warning(s)
    0 Error(s)
```

The single warning is **pre-existing and untouched by this work**:
`Models/Core/Activity.cs(51,19): warning CS0108: 'Activity.CreatedByUserId' hides inherited member
'AuditableEntity.CreatedByUserId'`. It was not silenced, because adding `new` would mask a genuine
question about which property EF maps — worth resolving deliberately rather than as a side effect.
(An earlier incremental build reported 0 warnings simply because `Activity.cs` was not recompiled.)

**Runtime verification** (application started against SQL Server, migration applied, seeding completed):

| Area | Result |
|---|---|
| Migration | `20260905223140_GharsDocsAlignment2026` applied to the existing populated DB; all columns/table verified |
| Public pages | `/`, `/Home/About`, `/Home/Vision`, `/Account/Login`, `/gallery`, `/Library` → 200; `/surveys` → 302 (auth) |
| Club Admin | `/club`, `/agenda`, `/agenda/create`, `/kpi`, `/kpi/create`, `/bookings/create`, `/surveys`, `/gallery` → 200 |
| Partner Admin | `/partner`, `/gallery`, `/surveys` → 200; `/club` → 302 |
| DSC Admin | `/Admin/Dashboard`, `/Admin/Kpi`, `/Admin/Reports/Analytics`, `/Admin/ExternalSurveys`, `/Admin/Gallery/MediaItems` → 200 |
| End-to-end booking | Direct request created (ActivityId NULL, Season 1, Partner 17, club 30) → partner confirmed with lecturer → Draft agenda auto-created → club submitted actual data |
| KPI round trip | Submit → Return → Correct → Resubmit → Approve, with audit rows |
| Security | Spoofed club id ignored; foreign records 404; `javascript:` URL rejected |
| Exports | CSV 200 with UTF-8 BOM and "No Data" literals |

No automated test project exists in the solution; verification was performed against the running application as described.

---

## 18. Outstanding Business Decisions

These are documented in `GHARS_REQUIREMENTS_GAP_ANALYSIS.md` §Conflicts and were **not** silently resolved:

1. **Surveys — internal vs external (C1).** `Surveys.docx` asks for a complete in-platform survey system; the approved operational flow is an external Dubai Digital Authority survey with an uploaded PDF analysis. Both are implemented and clearly separated. *Decision needed:* should the internal engine remain participant-facing long term, or become DSC-internal once the official survey is live?

2. **Attendance rate definition (C5).** *Still open — deliberately unchanged.* The current implementation and every available data source are set out in full in **§19.1** so the definition can be chosen from evidence. No calculation was altered.

3. **Satisfaction source (C6).** *Still open — deliberately unchanged.* Sources, storage and linkage are set out in **§19.2**. One correction was made: the dashboard no longer silently substitutes the internal survey score for the official figure (see §19.2).

4. **Agenda "Others (specify)" club option (C4).** **Decided — will not be implemented.** A Club Admin remains permanently scoped to the organization linked to their authenticated account; a free-text organization field would bypass that scoping. An activity for an organization not yet in Ghars is handled by registering it through the Organizations module (an administrative process), not by typing a name into a club form.

Informational (no decision required): the entity-first booking flow was **added alongside** the existing program-booking flow rather than replacing it (C2), and the activity-type list remains a superset of the document's Lecture/Event (C3) — document silence was not treated as a removal instruction.

**Follow-up now completed:** protected uploads have been moved out of `wwwroot` behind an authorised streaming endpoint — see **§20**.

---

## 19. KPI Data-Source Analysis — Attendance Rate & Satisfaction Rate

**No calculation in this section was changed.** This is a factual record of what the code does today and
what data the platform actually holds, so the two open business definitions can be settled from evidence.
One defect was corrected (§19.2, "Correction applied"); it removes a silent substitution rather than
choosing a definition.

### 19.1 Attendance Rate

| Aspect | Current state |
|---|---|
| **Catalogue definition** | `GharsKpiCatalog.AttendanceRate` — target ≥ 80%, `KpiSource.ClubSubmitted`, stated formula "Participating players ÷ total registered players in the academy/sector × 100" |
| **Current formula in code** | **None.** No numerator or denominator is computed anywhere. The club types a finished percentage into `KpiSubmission.AttendanceRate` (`decimal`, non-nullable) on the KPI form |
| **Current numerator** | Does not exist |
| **Current denominator** | Does not exist |
| **Validation applied** | Range 0–100 only (`KpiController.ValidateAsync`) |
| **Where the value surfaces** | Admin dashboard tile "Attendance Rate" (`AvgAttendance` = mean of *approved* submissions); `Reports/Analytics` average + per-club target table; `Admin/Kpi/Details`; club `Kpi/Index` |

**A second, unrelated attendance number also exists.** `DashboardController` computes an *operational*
check-in rate:

```
attendanceRate = AttendanceRecords (filtered) ÷ Σ Activity.Capacity × 100
```

It is exposed as `ViewBag.OperationalAttendanceRate` and is used on the dashboard tile **only as a
fallback** when no approved KPI submission exists (the tile's hint text switches to "Check-ins vs
capacity"). The two numbers measure different things and are not reconciled.

**Can `AttendanceRecord` / QR check-in data provide an authoritative KPI?** Partially — not today.

* *Available:* `AttendanceRecord` (`AttendanceSessionId`, `UserId`, `OrganizationId`, `CheckInUtc`,
  `Method` — QR or Manual), unique-indexed on `(AttendanceSessionId, UserId)`, so distinct real
  attendances per session are trustworthy. `AttendanceSession` carries `ActivityId`, start/end and a
  `QrToken`.
* *Limitation 1 — coverage.* A session exists only where a DSC admin created one for an `Activity`.
  Entity-first bookings (`BookingRequest.ActivityId == null`) and agenda-only deliveries have no session
  and therefore contribute zero check-ins. A KPI built on this today would **understate** every club that
  works outside the program-activity flow.
* *Limitation 2 — denominator.* The only denominators available are `Activity.Capacity` (a planned seat
  count, not registered players) or `AgendaEntry.NumberOfParticipants` (actual attendees — which makes
  the ratio circular).

**Are Agenda participant counts used anywhere?** Yes, but never for attendance:
`AgendaEntry.NumberOfParticipants` feeds the dashboard participant total, the growth-vs-2026 baseline
comparison, and the system-derived `NumberOfParticipants` on KPI submissions.

**Do registered-player totals exist?** **No.** There is no field anywhere in the schema for registered
players, squad size or club membership (`Organization` has no such property, and no separate player
entity exists). The documented denominator is therefore not capturable today — which is precisely why
the figure is hand-entered.

**Impact if the formula changes.** Affected surfaces: the dashboard tile and its 2033 target chart;
`Reports/Analytics` (average card, per-club target/actual table, baseline-vs-current comparison); the
composite `ComplianceScore` (attendance is one of its four components); the club-ranking score in
`DashboardController`; the KPI CSV export; and `Admin/Kpi/Details`. Historical approved submissions hold
hand-entered percentages, so any switch to a derived value creates a **discontinuity in the trend line**
unless prior seasons are recomputed or flagged. If a registered-player denominator is chosen, a new
club-submitted field plus a migration is required.

### 19.2 Satisfaction / Happiness Rate

| Aspect | Current state |
|---|---|
| **Catalogue definition** | `GharsKpiCatalog.Satisfaction` — target ≥ 85%, `KpiSource.ExternalApproved`, "Results of the official participant satisfaction survey (analyzed report)" |
| **Current source** | `KpiSubmission.SatisfactionRate` (`decimal`, non-nullable) — hand-entered on the club KPI form, then DSC-approved |
| **Does it come from internal Ghars surveys?** | **No.** No code path writes survey results into `SatisfactionRate` |
| **Does it come from `KpiSubmission`?** | **Yes — exclusively.** That is the only feed into the official figure |
| **Can an external/DDA result be stored as a structured approved value?** | **No.** `ExternalSurvey` has `TitleEn/Ar`, `DescriptionEn/Ar`, `ExternalUrl`, `SeasonId`, `IsActive`, `StartsAtUtc/EndsAtUtc`, `ReportPdfPath`, `IsReportPublished`, `ReportPublishedAtUtc` — **no numeric result field**. The only route from the DDA report to the KPI is a person reading the PDF and typing the percentage |
| **How the PDF report is linked** | `ExternalSurvey.ReportPdfPath`, released through `/protected-files/survey-report/{id}` once `IsReportPublished && IsActive`. There is **no foreign key between `ExternalSurvey` and `KpiSubmission`** — nothing records which report a given satisfaction number came from |
| **Internal survey data that does exist** | `SurveyAnswer.StarsValue` (1–5) → `internalStarScore = mean ÷ 5 × 100`, displayed as its own labelled row ("Ghars internal survey score") |
| **Where the value surfaces** | Dashboard "Happiness Score" tile; `Reports/Analytics` (per-club table, baseline-vs-current chart, composite score ÷ 6); club-ranking score; KPI CSV export |

**Correction applied (defect, not a definition change).** The dashboard previously fell back to the
internal star score when no approved KPI existed, while the tile still read *"Official survey results"* —
so an internal activity-feedback average could be presented as the official Ghars satisfaction figure.
The fallback was removed: with no approved data the tile now shows **"No Data"**, and the internal score
remains visible on its own clearly-labelled row. This satisfies the rule that an internal survey result
must never automatically become the official KPI value.

**Impact if the source changes.** Adding a structured external result (e.g. `ExternalSurvey.ResultPercent`
plus approval fields, with a foreign key from the submission) would let the KPI be traced to its report
and remove manual transcription, but requires a migration, a DSC entry screen, and a rule for which value
wins when both a club entry and an external result exist for the same season. Existing approved rows
would keep their hand-entered values.

### 19.3 Summary for the decision-makers

| Question | Evidence-based answer |
|---|---|
| Is authoritative attendance data available today? | No — partial check-in coverage, and no registered-player denominator exists anywhere |
| Is an authoritative satisfaction value storable today? | No — `ExternalSurvey` holds a link and a PDF, but no numeric result |
| What is measured today for both? | A hand-entered percentage, reviewed and approved by DSC |
| What changed in this pass? | Only the removal of the internal-score fallback (§19.2). Both formulas are untouched |

---

## 20. Protected File Storage & Authorized Access

### 20.1 Upload inventory and classification

Every upload site in the codebase was audited before anything moved.

| # | Upload type | Written by | Legacy location | Class | Action taken |
|---|---|---|---|---|---|
| 1 | **KPI supporting evidence** | `Kpi.SaveDocs` | `wwwroot/uploads/kpi` | **PROTECTED** | Moved out of web root; served via `/protected-files/kpi/{id}` |
| 2 | **Organization licence / supporting documents** | `Organization.SaveOrganizationAsync` | `wwwroot/uploads/org` | **PROTECTED** | Moved out of web root; served via `/protected-files/organization/{id}` |
| 3 | **Official survey analysis report** | `ExternalSurveys.Create/Edit` | `wwwroot/uploads/surveys` | **CONDITIONAL** | Moved out of web root; released via `/protected-files/survey-report/{id}` only once published |
| 4 | Organization logo | `Organization.SaveFileAsync` | `wwwroot/uploads/org` | **PUBLIC** | Left in place — rendered on public organization listings |
| 5 | Agenda supporting media | `Agenda.SaveAgendaMedia` | `wwwroot/uploads/agenda` | **CONDITIONAL** | Left in place — publication is already gated by `GalleryItem.IsPublished`; the file itself is club activity photography intended for the public gallery |
| 6 | Gallery media items | `Admin/Gallery.CreateMediaItem` | `wwwroot/uploads/gallery/items` | **PUBLIC** | Left in place — the public gallery is its purpose |
| 7 | Digital Library files and covers | `Admin/Library.Create/Edit` | `wwwroot/uploads/library` | **CONDITIONAL** | Left in place — `IsPublished` gates listing; content is intentionally public educational material |
| 8 | Certificates (PDF) | `Admin/Certificates.Issue` | `wwwroot/uploads/certificates` | **CONDITIONAL** | Left in place — see residual risk R1 |
| 9 | Reward images | seeded/admin | `wwwroot/uploads/rewards` | **PUBLIC** | Left in place |

Items 5–9 were deliberately **not** moved: the objective is authorization, not hiding every file, and
relocating public content would break the gallery, library and public organization pages.

### 20.2 Storage architecture

* Protected files live in `{ContentRoot}/protected-uploads/{category}/{guid}{ext}` — outside `wwwroot`,
  so the static-file middleware cannot reach them at any URL.
* The database stores a **storage key** (`kpi/ab12….pdf`), never a physical server path. No server path
  is ever rendered to a user; links carry only a record id.
* Two path shapes coexist and both resolve correctly, so the application works before *and* after
  migration: a leading `/` means a legacy `wwwroot` path, its absence means a managed protected key.
  `ProtectedFileStore.ResolvePhysicalPath` is the single resolver and rejects anything containing `..`
  or resolving outside its own root.
* Uploads keep GUID filenames (the original name is stored separately and only used as the download
  name, sanitised of path and header-breaking characters).
* **Defence in depth:** middleware registered *before* `UseStaticFiles` returns 404 for any request under
  `/uploads/kpi` or `/uploads/surveys`, so unmigrated legacy files are unreachable even mid-migration.
  `/uploads/org` is deliberately excluded because public logos share that folder; its documents are
  removed from disk by the migration's purge phase instead.

### 20.3 Authorization rules

All three actions resolve the **record** first, authorise against it, then stream. Authorization is never
based on knowing a GUID or path. Every failure — unknown id, missing file, wrong organization, wrong role
— returns a bare **404**, so the endpoint cannot be used to probe which documents exist.

| Endpoint | Club Admin / Academy Admin | DSC Admin / Super Admin | Partner Admin / Speaker / Viewer | Anonymous |
|---|---|---|---|---|
| `/protected-files/kpi/{id}` | Own club's submissions only | All clubs (existing review authority) | 404 | Redirected to login |
| `/protected-files/organization/{id}` | Own organization only | All organizations | 404 unless linked to that organization | Redirected to login |
| `/protected-files/survey-report/{id}` | Only when published | Always (including under review) | Only when published | Only when published |

Organization ownership is checked *inside the query* against the authenticated user's
`OrganizationAdminLink` rows — the requested id is only ever a filter, never a source of authority.

### 20.4 Migration of existing files

Migration is an explicit operator action, never automatic at startup, because it rewrites database paths.

```
dotnet run -- migrate-protected-files            # dry run — reports, changes nothing
dotnet run -- migrate-protected-files --commit   # copies files, repoints the DB, keeps originals
dotnet run -- migrate-protected-files --purge    # deletes the verified legacy originals
```

* **Copy, not move.** `--commit` copies and verifies file length before repointing the row, so every
  legacy file is still in place while the new location is verified.
* **Rollback.** Between `--commit` and `--purge`, rollback is restoring the previous `FilePath` values;
  no file has been destroyed. After `--purge` the protected copy is authoritative.
* **Missing files.** A row whose file is already gone is reported as `MISSING FILE` and **left
  unchanged** — repointing it would only relocate a broken link.
* **Idempotent.** Already-migrated rows report `ALREADY PROTECTED` and are skipped.
* The command runs without the seeder, so it cannot have side effects on user accounts or demo data.

### 20.5 Live security test results

Run against the application on `http://localhost:5080` with a populated SQL Server database, using
purpose-built fixtures (Club A = Shabab Al Ahli / org 30, Club B = Al Nasr / org 31). All fixtures were
removed afterwards.

**Access matrix** (`200` = served, `404` = denied without disclosure):

| Request | Anonymous | Club A (org 30) | Club B (org 31) | Partner Admin | DSC Admin |
|---|---|---|---|---|---|
| KPI evidence — Club A owns | → login | **200** | **404** | **404** | **200** |
| KPI evidence — Club B owns | → login | **404** | **200** | **404** | **200** |
| KPI evidence — row whose file is gone | → login | 404 | 404 | 404 | 404 |
| KPI evidence — non-existent id | → login | 404 | 404 | 404 | 404 |
| Organization document — Club A owns | → login | **200** | **404** | **404** | **200** |
| Survey report — **unpublished** | 404 | 404 | 404 | 404 | **200** |
| Survey report — **published** | **200** | 200 | 200 | 200 | 200 |
| Legacy static URL — Club A evidence | 404 | 404 | 404 | 404 | 404 |
| Legacy static URL — Club B evidence | 404 | 404 | 404 | 404 | 404 |
| Legacy static URL — survey report | 404 | 404 | 404 | 404 | 404 |
| Legacy static URL — organization licence | 404 | 404 | 404 | 404 | 404 |
| Public static asset (`/css/ghars-public-theme.css`) | 200 | 200 | 200 | 200 | 200 |
| Public pages (`/gallery`, `/library`) | 200 | 200 | 200 | 200 | 200 |

Confirmed by direct id manipulation in both directions: Club A → Club B's evidence = 404, Club B → Club
A's evidence = 404. Response bodies were inspected for marker content, so a `200` proves the correct
bytes were served and a `404` proves nothing leaked.

**Path traversal.** Two poisoned rows were inserted directly into the database and requested as DSC
Admin (the highest privilege):

| Stored path | Result |
|---|---|
| `../../appsettings.json` | **404 — blocked** |
| `kpi/../../appsettings.json` | **404 — blocked** |

Neither returned the connection string.

**End-to-end upload.** A new evidence file was uploaded through the real Club Admin KPI form. It was
written to `protected-uploads/kpi/eec97d52….pdf` with a GUID name, the database stored the managed key
`kpi/eec97d52….pdf`, and `wwwroot/uploads/kpi` remained empty. Access to the new document: Club A **200**,
Club B **404**, DSC **200**.

**Migration run.** Dry run reported 4 × `WOULD MIGRATE` and wrote nothing. `--commit` produced
4 × `MIGRATED` + 1 × `MISSING FILE (row left unchanged)`. `--purge` produced 4 × `PURGED legacy copy`
+ 1 × `SKIPPED (not migrated yet)`, after which the legacy organization-licence URL changed from 200 to
404 — the one gap that the middleware deny-list cannot cover.

### 20.6 Regression check

| Module | Result |
|---|---|
| Booking create (`/bookings/create`) | 200 |
| Club dashboard (`/club`) | 200 |
| Agenda (`/agenda`) | 200 |
| KPI list / edit (`/kpi`, `/kpi/edit/15`) | 200 / 200 |
| KPI submission with evidence | Saved, redirected to `/kpi`, file stored outside web root |
| Gallery (`/gallery`) | 200 |
| Digital Library (`/library`, `/Admin/Library/Items`) | 200 / 200 |
| Surveys (`/surveys`) | 200 |
| Admin dashboard / KPI review / KPI details | 200 / 200 / 200 |
| Admin external surveys / organizations / analytics / gallery | 200 / 200 / 200 / 200 |
| Historical file references | Legacy `/uploads/...` values still resolve through the same endpoint pre-migration |

**Defect found and fixed during this pass:** `DbSeeder` crashed at startup with
`Nullable object must have a value` once entity-first bookings (`ActivityId == null`) existed in the
database — a latent regression from making `BookingRequest.ActivityId` nullable. Attendance sessions and
certificates are activity-anchored, so those bookings are now filtered out of that seeding block rather
than dereferenced.

### 20.7 Files changed

| File | Change |
|---|---|
| `Helpers/ProtectedFileStore.cs` | **New** — storage root, save, resolve (with traversal guard), content type, safe download name |
| `Helpers/ProtectedFileMigrator.cs` | **New** — three-phase operator migration utility |
| `Controllers/ProtectedFilesController.cs` | **New** — the three authorized endpoints |
| `Program.cs` | CLI branch for the migration command; static deny-list middleware before `UseStaticFiles`; explicit exit code |
| `Controllers/Public/KpiController.cs` | Evidence saved via `ProtectedFileStore` |
| `Controllers/Public/OrganizationController.cs` | Documents protected; logo explicitly kept public |
| `Controllers/Admin/ExternalSurveysController.cs` | Report PDFs saved via `ProtectedFileStore` |
| `Controllers/Admin/DashboardController.cs` | Removed the internal-score fallback for official satisfaction (§19.2) |
| `Data/DbSeeder.cs` | Null-activity bookings excluded from attendance/certificate seeding |
| `Views/Kpi/_KpiForm.cshtml`, `Areas/Admin/Views/Kpi/Details.cshtml` | Evidence links → `/protected-files/kpi/{id}` |
| `Areas/Admin/Views/Organizations/Details.cshtml` | Document links → `/protected-files/organization/{id}` |
| `Views/Surveys/Index.cshtml`, `Areas/Admin/Views/ExternalSurveys/Index.cshtml` | Report links → `/protected-files/survey-report/{id}`; survey sections renamed "Official Program Survey" / "Ghars Internal Surveys" with explanatory text |
| `Areas/Admin/Views/Dashboard/Index.cshtml` | Internal survey row relabelled "Ghars internal survey score" |

### 20.8 Remaining risks

* **R1 — Certificates.** Certificate PDFs remain publicly readable at `/uploads/certificates/{verifyToken}.pdf`
  and contain a participant's name. The token doubles as the public QR-verification token, so possession
  is by design — but the *file* is reachable without the verification page. Recommended (not done here,
  as it changes an intentional public-verification flow): serve certificates through an endpoint that
  accepts the verify token and returns a rendered verification view.
* **R2 — Agenda media.** ~~Files are reachable by direct URL even when the derived gallery item is
  unpublished.~~ **Closed — see §21.5.** Agenda media is now served only through
  `/protected-files/gallery/{id}`, which re-checks publication on every request.
* **R3 — `/uploads/org`.** Cannot be added to the static deny-list while public logos share the folder.
  Protection relies on the migration's `--purge` phase physically removing document files. If logos are
  ever relocated to a dedicated folder, the whole path can be denied.
* **R4 — No antivirus scanning.** Uploads are validated by extension, MIME prefix and size only. Files
  are never executed or served from the web root, which limits the impact, but scanning should be added
  before large-scale external registration intake.
* **R5 — Backups.** `protected-uploads/` sits outside `wwwroot` and must be added to whatever backup and
  deployment routine currently covers `wwwroot/uploads`, or migrated documents will not be backed up.
  **Documented — see §21.3 and `GHARS_PRODUCTION_OPERATIONS.md`.** The repository contains no backup or
  deployment automation to amend, so this is an operational instruction, not a code change.

---

## 21. Production Hardening Pass

A narrow pass over an already-complete implementation: correct the reported build result, investigate
(rather than silence) the one compiler warning, close the unpublished-media gap, give the satisfaction KPI
an evidence trail, review certificate exposure, and write down the operational requirements that protected
storage created. No completed module was redesigned.

### 21.1 Corrected build result

`dotnet build --no-incremental`, run clean at the start and again at the end of this pass:

```
Build succeeded.
    1 Warning(s)
    0 Error(s)
```

The executive summary (§1) previously claimed **0 warnings**, contradicting §17. It now states
**0 errors, 1 warning** and points here. The warning is unchanged from before this pass:

```
Models/Core/Activity.cs(51,19): warning CS0108: 'Activity.CreatedByUserId' hides inherited member
'AuditableEntity.CreatedByUserId'. Use the new keyword if hiding was intended.
```

### 21.2 The `Activity.CreatedByUserId` warning — investigated, deliberately not changed

| Question | Finding |
|---|---|
| **Why does the duplicate exist?** | `AuditableEntity.CreatedByUserId` is `string?` (optional). `Activity` re-declares it as `[Required] string` because an activity's creator is not decorative: it *resolves ownership*. Partner dashboards, the public programme list and the club dashboard all authorise or filter with `partnerUserIds.Contains(x.CreatedByUserId)`. A null creator there would be an authorisation ambiguity, so the derived type deliberately strengthens the contract. |
| **Is the base property mapped by EF Core?** | **No.** It is shadowed and never reaches the model. |
| **Is the derived property mapped?** | **Yes** — it is the only `CreatedByUserId` EF sees for this entity. |
| **What is the actual database column?** | `Activities.CreatedByUserId nvarchar(450) NOT NULL` — confirmed against the live database via `INFORMATION_SCHEMA.COLUMNS` (`IS_NULLABLE = NO`). |
| **Are both properties represented in the EF model?** | **No — exactly one.** `AppDbContextModelSnapshot` declares a single `CreatedByUserId` with `.IsRequired().HasMaxLength(450)`. There is no duplicate column and no shadow property. |
| **Do migrations/snapshot reference one or both?** | One. Every migration that touches the column treats it as a single required column. |
| **Would removing one property cause a schema change?** | Removing the **derived** property makes the C# type `string?`, and EF would then emit a **nullable** column — a schema change — unless `IsRequired()` is restored in `OnModelCreating`. With that fluent configuration the snapshot is byte-identical and **no migration would be required**. |
| **Does code read/write the derived property?** | Yes, in 10 places, 8 of which are ownership filters in `PartnerDashboardController`, `HomeController` and `ClubDashboardController`. |
| **Would existing data be affected?** | **No.** One column, one value, populated on every row. Nothing to migrate either way. |

**Decision: leave the code unchanged and carry the warning.** Both available fixes were rejected:

* Adding `new` would silence the compiler while *preserving* the actual hazard (below) and asserting the
  hiding is fine — which is the thing the warning is asking us to prove, not assume.
* Removing the derived property plus fluent `IsRequired()` is architecturally the correct fix and needs no
  migration, but it turns the C# type nullable and so requires editing eight **ownership-resolution
  queries** to suppress nullability warnings. Editing authorisation filters to remove a cosmetic warning is
  a poor trade in a hardening pass whose purpose is to reduce risk.

**Recorded risk (R6).** Because the base property is shadowed rather than overridden, `((AuditableEntity)activity).CreatedByUserId = userId` compiles, sets the *unmapped* base property, and is silently discarded by EF. Nothing in the codebase does this today — there is no `SaveChanges` override and no audit interceptor — but adding a generic audit stamper over `AuditableEntity` is a common and natural next step, and it would silently fail to stamp `Activities` **and** could leave ownership filters unable to match. Anyone adding one must first apply the fluent-configuration fix above. This is the reason the warning was left visible rather than suppressed.

### 21.3 Backup and deployment status for `protected-uploads/`

**Audited:** the repository contains **no** CI pipeline, deployment script, Dockerfile, publish profile or
backup automation of any kind. The only configuration files are `appsettings.json` and
`Properties/launchSettings.json`. There was therefore nothing to amend, and none of the following exist to
be checked: backup jobs, restore procedures, deployment packaging, server-migration steps, DR runbooks.

Because inventing infrastructure would be worse than documenting the requirement, this pass creates
**`GHARS_PRODUCTION_OPERATIONS.md`** instead, covering physical location, contents, backup frequency,
restore procedure, folder permissions, deployment, the migration command and rollback.

The single most important point, stated there and repeated here: **a restored database without restored
protected files is an incomplete restore.** The database holds only storage keys (`kpi/ab12….pdf`); the
bytes live in `protected-uploads/`. Restoring one without the other yields KPI submissions and
organization records whose evidence returns 404 — silently, because a missing file is indistinguishable
from an unauthorised request by design.

### 21.4 Attendance KPI — source clarified, calculation untouched

No attendance calculation was changed. The official value remains **club-submitted, DSC-approved**.

| | Official Attendance KPI | Operational attendance |
|---|---|---|
| Source | `KpiSubmission.AttendanceRate`, typed by the club | `AttendanceRecord` rows from QR / manual check-in |
| Authority | Reviewed and approved by DSC | None — operational visibility only |
| Target | ≥ 80% | n/a |
| Automated? | **No.** Not derived from any platform data | Counted directly |
| Coverage | Every club that submits KPI data | **Partial** — sessions are anchored to an `Activity`, so entity-first bookings (`ActivityId == null`) have none |
| Denominator | Registered players — **not available anywhere in the schema** | n/a |

These two are not merged, and the operational figure must never silently replace the official one. Agenda
participant counts remain excluded as a denominator: they count attendees of a single agenda item, which is
not the registered-player population the KPI is defined against.

To stop the UI implying computation, indicator provenance is now rendered from the existing
`KpiDefinition.Source` value that the catalog already carried but no screen displayed:

* `GharsKpiCatalog.SourceLabel(KpiSource, bool ar)` returns **"Club Submitted / DSC Approved"** /
  «إدخال النادي / اعتماد مجلس دبي الرياضي», **"Official Program Survey / DSC Approved"** and
  **"System Calculated"**.
* The DSC review table shows that label under every indicator, beside the approved formula — the formula is
  the indicator's *definition*, the badge states who supplies the number.
* The club entry form now reads *"Club submitted, DSC approved. Not calculated from check-in records."*

### 21.5 Unpublished Agenda media — gap confirmed and closed

**Intended meaning, established from the code rather than assumed.** Agenda media is created with
`IsPublished = true` unconditionally (`AgendaController.SaveAgendaMedia`), so no "awaiting publication"
state is ever produced by the club workflow. `IsPublished` becomes false in exactly two places, both
administrative: `Admin/Gallery.ToggleMediaItem` ("Media hidden") and `Admin/Gallery.DeleteMediaItem`, which
for agenda-sourced rows **only unpublishes** and reports *"Agenda media hidden from the gallery."*

So the flag means **DSC takedown**, not "not yet released" — the stronger case. A DSC admin pressing
*Delete* on unsuitable club photography was told the media was hidden while the file remained downloadable
at its static URL indefinitely. That is publication control that does not control publication, so it was
fixed.

**Design — no file moved, no file duplicated:**

* New `GET /protected-files/gallery/{id}` resolves the `GalleryItem`, then: published → anyone;
  unpublished → the owning organization's linked admins and DSC/Super Admin only; everyone else 404.
* `/uploads/agenda` added to the static deny-list ahead of `UseStaticFiles`.
* The public gallery and the admin media list now link by **record id**; the file itself stays where it is
  and is resolved through `ProtectedFileStore.ResolvePhysicalPath`'s existing legacy-path branch.

**No migration, no schema change, and no risk to published media** — which is why this approach was chosen
over relocating the files. The residual weakness is that the bytes still sit under `wwwroot` and protection
depends on the deny-list middleware staying ahead of `UseStaticFiles`; relocating them into
`protected-uploads/agenda` via the existing migrator remains available as a later, optional step.

### 21.6 Satisfaction KPI — evidence lineage

The approved satisfaction figure and the official report that justifies it had no connection: nothing
recorded which analysed report a given percentage came from.

* `KpiSubmission.SatisfactionExternalSurveyId` → `ExternalSurvey`, **nullable**, `DeleteBehavior.NoAction`,
  indexed. Named for its role rather than its type, so it can never be mistaken for the internal `Survey`
  entity.
* It carries **provenance only**. `SatisfactionRate` is never computed, overwritten or defaulted from it —
  verified live below. Internal Ghars surveys are not linkable, per §15.
* The DSC reviewer selects the supporting survey on the KPI review screen. The choice is validated
  server-side against the submission's season (active surveys for that season, or programme-wide surveys
  with no season); an invalid selection is refused with a message and **leaves the existing link
  untouched** rather than clearing it.
* The review screen shows satisfaction rate, target ≥85%, source, linked survey title and a report link —
  or **"No linked official survey report"** / «لا يوجد تقرير استبيان رسمي مرتبط».
* The change is recorded in the audit trail alongside status and notes.

**Historical rows are untouched.** All 14 existing submissions carry `NULL` and nothing was backfilled. A
later backfill is only defensible if a season maps to exactly one official survey; today season 1 has one
survey and season 2 has none, which is too thin to infer from — a reviewer should attach reports
deliberately.

### 21.7 Certificate public access — reviewed, not changed

| | Finding |
|---|---|
| Public verification page | `GET /verify/certificate/{token}` — anonymous, no rate limiting |
| Public PDF route | `wwwroot/uploads/certificates/{token}.pdf` — **static**, anonymous |
| Verification page reveals | Certificate number, status, issue date, activity title, revocation reason. **No participant name** |
| PDF reveals | **Participant's full name**, activity title, issue date, certificate number, QR code |
| Is the token unpredictable? | Yes — `Guid.NewGuid()` (v4, ~122 bits). Not enumerable or guessable |
| Same token for both? | **Yes.** The PDF filename *is* the verification token |
| Does the PDF reveal more than the page? | **Yes — the participant's name.** This is the finding |

**Not changed, per instruction.** The exposure is narrow: possession of the token is required, and the
token is only distributed with the certificate itself. But the design means a token shared for the
legitimate purpose of *verifying* a certificate also hands over a PDF containing personal data, and the
issuer cannot offer one without the other.

Recommended architecture (needs a business decision, not a code decision):

1. Move certificate PDFs into `protected-uploads/certificates/`.
2. Serve them from `/verify/certificate/{token}/document`, which resolves the certificate by token exactly
   as the verification page already does.
3. Keep the verification page anonymous and unchanged.

That preserves public QR verification exactly while making PDF delivery a deliberate, revocable step
(a revoked certificate could stop returning its document). **Business decision required:** should the
certificate PDF remain retrievable by anyone holding the verification token, or should verification and
document retrieval be separated? Until that is answered, behaviour is unchanged — see R1.

### 21.8 Database migration

`Migrations/20260906084642_AddKpiSatisfactionSurveyLineage.cs` — the only schema change in this pass.

```csharp
migrationBuilder.AddColumn<int>("SatisfactionExternalSurveyId", "KpiSubmissions", "int", nullable: true);
migrationBuilder.CreateIndex("IX_KpiSubmissions_SatisfactionExternalSurveyId", "KpiSubmissions", ...);
migrationBuilder.AddForeignKey("FK_KpiSubmissions_ExternalSurveys_SatisfactionExternalSurveyId", ...);
```

Additive, nullable, indexed, non-destructive, `NO_ACTION` on delete and update, safe on a populated
database, and reversible via `Down`. No existing migration was edited.

Applied to the development database and verified against it:

| Check | Result |
|---|---|
| Column | `SatisfactionExternalSurveyId`, `int`, `IS_NULLABLE = YES` |
| Foreign key | `FK_KpiSubmissions_ExternalSurveys_SatisfactionExternalSurveyId`, delete `NO_ACTION`, update `NO_ACTION` |
| Index | `IX_KpiSubmissions_SatisfactionExternalSurveyId` present |
| Rows before / after | 14 / 14 |
| Approved rows before / after | 14 / 14 |
| Historical rows with a lineage value | 0 — nothing backfilled |

> Operational note: `dotnet ef database update --no-build` will silently apply *nothing* if the assembly
> predates the migration. Build first, and confirm the `Applying migration '…'` line actually appears.

### 21.9 Security regression results

Verified against the running application with controlled fixtures — two clubs, five actors, real files.
Every check below was actually executed and its observed result recorded; cells marked "—" were not
probed and are not being claimed. `nosniff` was present on every streamed response.

**KPI evidence** (doc 7 = Club A / org 30, doc 8 = Club B / org 31):

| Request | Club A | Club B | Partner | Anonymous | DSC |
|---|---|---|---|---|---|
| `/protected-files/kpi/7` | **200** | **404** | 404 | 302 → login | 200 |
| `/protected-files/kpi/8` | **404** | **200** | 404 | — | 200 |

Club A's own evidence returned `Content-Type: application/pdf` with
`Content-Disposition: attachment; filename="ZZ Club A evidence.pdf"`.

**Agenda / gallery media** (item 9, owned by org 30) — the unpublished row is the case this pass fixed, so
it was probed for every actor:

| State | Anonymous | Club A (owner) | Club B | Partner | DSC |
|---|---|---|---|---|---|
| Published | **200** | — | **200** | — | — |
| Unpublished | **404** | **200** | **404** | **404** | **200** |

A takedown now genuinely withdraws the file, while published gallery media stays fully public.

**Survey report** (`ExternalSurvey` 1): unpublished → anonymous 404, Club A 404, **DSC 200**; published →
anonymous **200**, served inline as `application/pdf`. Intentional and unchanged.

**Static bypass** — every legacy direct URL is refused, including for DSC:

| URL | Result |
|---|---|
| `/uploads/kpi/ZZfixtureClubA.pdf` | 404 |
| `/uploads/surveys/ZZfixtureReport.pdf` | 404 |
| `/uploads/agenda/de0dc770….pdf` (anonymous) | 404 |
| `/uploads/agenda/de0dc770….pdf` (**DSC Admin**) | 404 |

**Cross-club record access** — re-verified in this pass rather than assumed, since the KPI review action
changed. Club A = org 30, Club B = org 31:

| Request | Club A | Club B |
|---|---|---|
| `/agenda/edit/1` (Club A's agenda) | **200** | **404** |
| `/agenda/edit/3` (Club B's agenda) | **404** | **200** |
| `/bookings/details/7` (Club A's booking) | **200** | **404** |
| `/bookings/details/2` (Club B's booking) | **404** | **200** |

**Organization spoofing** — Club A (org 30) posted `/agenda/create` with `OrganizationId=31`, Club B's id.
The entry was created with **`OrganizationId = 30`**: the posted value was discarded and the organization
taken from the authenticated user's link, as required by §16. The probe row was deleted afterwards.

**Path traversal** (§18) — malicious values written *directly into the database* and requested **as DSC
Admin**, i.e. past every authorisation check, testing `ProtectedFileStore` containment alone:

| Stored `FilePath` | Result |
|---|---|
| `../../appsettings.json` | **404** — no connection string returned |
| `kpi/../../appsettings.json` | **404** — no connection string returned |

`ResolvePhysicalPath` validation was not weakened; the `..` rejection and root-containment check are intact.

**Missing-file behaviour** (§4), tested on both a fixture row and a genuine dangling row (gallery item 9
references a seeded file that has never existed on disk):

* Database record present, file absent → **404**, empty body.
* Response body checked for leakage of `D:\`, `wwwroot`, `protected-uploads`, `\uploads`, the assembly name
  and the stored filename — **none present**. Body length 0.
* Application did not error: `/gallery` and `/Admin/Gallery/MediaItems` both continued to return 200 with
  the broken item in place.

**No regression in normal operation:** `/gallery`, `/library`, `/css/…`, `/img/brand/ghars-logo.png` and
the public library PDF all 200 anonymously; `/kpi`, `/agenda`, `/club` 200 as Club Admin;
`/Admin/Kpi/Details/15`, `/Admin/Gallery/MediaItems`, `/Admin/Dashboard` 200 as DSC Admin.

One expectation of ours was wrong, not the application: `/surveys` returns 302 for anonymous users because
`SurveysController` carries `[Authorize]`. That is pre-existing and correct. Note the consequence — a
*published* official report is reachable anonymously at `/protected-files/survey-report/{id}`, while the
page that links to it requires sign-in.

**Satisfaction lineage, end to end:**

| Action | Result |
|---|---|
| DSC links survey 1 (season 1) to submission 15 (season 1) | Stored — `SatisfactionExternalSurveyId = 1` |
| DSC links survey 1 (season 1) to submission 14 (**season 2**) | **Refused** with a warning; value stays `NULL` |
| `SatisfactionRate` after both | **87.00 and 94.00 — unchanged**, confirming lineage never moves the value |
| Review screen, linked | Survey title and "Open report" shown |
| Review screen, unlinked | "No linked official survey report" shown |

**Fixtures fully removed afterwards.** `KpiDocuments` back to 0 rows; the survey report path, publication
flag and timestamp reset to `NULL`/`0`; submission 15's lineage cleared and its review note restored;
gallery item 9 republished; all five fixture files deleted from both roots. Post-cleanup counts: 14
submissions, 14 approved, 0 with lineage. *(One unavoidable trace: the review POSTs updated submission 15's
`ReviewedAtUtc`/`ReviewedByUserId` to the test time and the DSC test account — a seeded demo row on the
development database.)*

### 21.10 Files changed in this pass

| File | Change |
|---|---|
| `Models/Core/KpiSubmission.cs` | Nullable `SatisfactionExternalSurveyId` + navigation |
| `Models/Core/GharsKpiCatalog.cs` | `SourceLabel(KpiSource, bool ar)` bilingual provenance labels |
| `Data/AppDbContext.cs` | Lineage FK (`NoAction`) and index |
| `Migrations/20260906084642_AddKpiSatisfactionSurveyLineage.*` | New additive migration |
| `Controllers/Admin/KpiController.cs` | Loads/validates the supporting survey; records it in the audit trail |
| `Controllers/ProtectedFilesController.cs` | New `gallery/{id}` endpoint; `nosniff` on all streamed responses; inline allow-list |
| `Helpers/ProtectedFileStore.cs` | Video content types; `IsInlineSafe` allow-list |
| `Program.cs` | `/uploads/agenda` added to the static deny-list |
| `Areas/Admin/Views/Kpi/Details.cshtml` | Source badges, satisfaction evidence card, supporting-survey selector |
| `Views/Kpi/_KpiForm.cshtml` | Attendance and satisfaction provenance wording |
| `Views/Gallery/Index.cshtml`, `Areas/Admin/Views/Gallery/MediaItems.cshtml` | Media linked by record id |
| `GHARS_IMPLEMENTATION_REPORT.md` | §1 build result corrected; R2/R5 updated; this section |
| `GHARS_PRODUCTION_OPERATIONS.md` | **New** — backup, restore, deployment, permissions, rollback |

### 21.11 Remaining business decisions

Genuinely open, and none of them ours to settle:

1. **Authoritative attendance definition.** Registered-player totals exist nowhere in the schema. Either
   add that field (making the documented formula computable), or formally confirm attendance stays
   club-attested. Until then the KPI is a declared figure, now labelled as one.
2. **Satisfaction source.** `ExternalSurvey` still has no numeric result field, so the percentage is
   transcribed from a PDF by hand. Lineage now records *which* PDF; storing the analysed value as
   structured data remains a separate decision.
3. **Certificate PDF accessibility** — §21.7. Should holding a verification token also grant the PDF?
4. **Internal vs official survey ownership** — unchanged from §18; both engines stay, results stay separate.
5. **Backfilling satisfaction lineage** for the 14 historical rows — only if DSC can state which report
   backs each, and only deliberately.

Also carried forward, not changed here: EF reports `decimal` precision warnings for the `KpiSubmission`
rate columns (they default to `decimal(18,2)`, which is adequate for percentages). Setting explicit
precision would require a migration touching approved KPI values, so it was left alone.

---

## 22. Repository Baseline Pass

Dated 2026-09-06. This pass placed the completed platform under source control and tagged a
production candidate. It made no feature changes. Full details, including the commit SHA and tag, are
in [`GHARS_REPOSITORY_BASELINE.md`](GHARS_REPOSITORY_BASELINE.md); this section records the two
findings that changed the code.

### 22.1 Test residue cleared

The hardening pass reported that KPI submission 15 carried `ReviewedAtUtc` and `ReviewedByUserId`
values written by the DSC test account. The audit trail settled what was actually residue:

| Field | Finding | Action |
| --- | --- | --- |
| `ReviewedByUserId` | `SystemAuditLogs` id 6 shows the *previous* review was also by `dscadmin@ghars.local`. The value never changed. | None needed |
| `ReviewNotes` | Restored to `"Approved."` during the hardening pass already; audit id 8 confirms the test value `"ZZ lineage test"` was reverted. | None needed |
| `SatisfactionExternalSurveyId` | Already back to `NULL`. | None needed |
| `ReviewedAtUtc` | `2026-09-06 08:58:18` — genuinely the test write. | Restored to `2026-09-05 23:34:05.0369974`, the timestamp recorded for the preceding review in `SystemAuditLogs` id 6 |

The restored timestamp is *sourced, not invented*: it is the audit row written by the same request that
last legitimately set the field. It is accurate to that request, differing only by the few milliseconds
between the entity write and the audit write. No value was guessed.

Submission 15 is purely seeded demo data — a seeded club (Shabab Al Ahli), a seeded season, created by
`DbSeeder`, and touched only by `@ghars.local` accounts from `127.0.0.1`/`::1`. It was **not** deleted
and re-seeded: that would have destroyed audit-referenced history to make the database look tidier.

Two rows named `"testing course"` (GalleryItem 9, AgendaEntry 10) were examined and **left alone** —
both are dated 2026-05-13, months before any verification work, so they are the owner's own data.
GalleryItem 9 points at `/uploads/agenda/de0dc770….pdf`, which is absent from disk; that dangling
reference predates this work and is noted, not "fixed".

The `SystemAuditLogs` rows recording the test activity were deliberately **kept**. An audit trail that
gets edited to look clean is worth nothing.

### 22.2 The migration chain could not build a database — fixed

This is the significant finding of the pass. It was found by testing rather than by reading: the
repository's migrations were applied to a scratch database, which **failed**.

```
Applying migration '20260905223140_GharsDocsAlignment2026'.
SqlException: Cannot find the object "KpiSubmissions" because it does not exist or you do not have permissions.
```

Root cause, in two parts:

1. **Four migrations were invisible to EF.** `AddBookingTimeProposals`, `AddActivityPartnerOrganization`,
   `AddGharsAgendaKpiGalleryLibraryEnhancements` and `AddGalleryAlbumLinksAndPdf` are hand-written and
   have no `.Designer.cs`, so they carried neither `[DbContext]` nor `[Migration]`. `dotnet ef migrations
   list` reported only four migrations where the folder held eight. One of the ignored files creates
   `AgendaEntries`, `KpiSubmissions`, `AgendaMedia`, `GalleryItems` and `KpiDocuments` — the core Ghars
   tables. A database built from source control simply did not have them.
2. **One applied migration has no source file.** `__EFMigrationsHistory` contains
   `20260505103249_new one `, which is absent from the repository. Its unique contribution was
   `OrganizationAdminLinks.CreatedAtUtc` and `.CreatedByUserId`. The model snapshot declares both, so
   `has-pending-model-changes` correctly reported *no* pending changes — the model matched the snapshot.
   The defect was that no migration produced the snapshot's schema, which that check does not detect.

The second point mattered most: `OrganizationAdminLinks` backs the organization-scoping check on
virtually every authenticated request. A deployment would have installed cleanly and then failed at
runtime on nearly every page, rather than failing visibly at deployment time.

Fixes, both minimal and additive:

- `[DbContext(typeof(AppDbContext))]` and `[Migration("…")]` attributes added to the four orphaned
  files. No DDL was altered and no existing migration's behaviour was changed.
- A new hand-written migration, `20260906140000_RestoreOrganizationAdminLinkAuditColumns`, adds the two
  missing columns. It is hand-written because the model already matches the snapshot, so
  `migrations add` would have produced an empty migration. Every statement is guarded
  (`IF COL_LENGTH(...) IS NULL`), making it a no-op where the columns already exist.

Verification — a fresh database was built from the repository alone and compared to the working
development database:

| Check | Result |
| --- | --- |
| Migrations applied to empty database | All 9, no errors |
| Tables | 49 vs 49 |
| Columns | 549 vs 549 |
| Column-level differences (name, type, nullability, length) | **0** |
| Development database data after applying the repair | 14/14 KPI submissions, 36 organizations, 11 agenda entries, 9 gallery items, 15 bookings, 8 certificates — unchanged |

The scratch database was then dropped.

**Existing databases need one manual step**, because their history predates the attribute fix and EF
would otherwise try to re-apply already-applied migrations. Recorded in
`GHARS_PRODUCTION_OPERATIONS.md`; it was applied to the development database in this pass:

```sql
INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
SELECT v.MigrationId, '8.0.8' FROM (VALUES
  ('20260503170000_AddBookingTimeProposals'),
  ('20260503173000_AddActivityPartnerOrganization'),
  ('20260504120000_AddGharsAgendaKpiGalleryLibraryEnhancements'),
  ('20260505103000_AddGalleryAlbumLinksAndPdf')) v(MigrationId)
WHERE NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory h WHERE h.MigrationId = v.MigrationId);
```

### 22.3 Seeding is not environment-gated — flagged, not changed

`Program.cs` calls `DbSeeder.SeedAsync` unconditionally, and `EnsureSeedPasswordAsync` **resets every
seeded account's password to its hard-coded constant on every application start**. The demo accounts
and their passwords are therefore created in whatever environment the application runs, production
included, and an administrator who changes one of those passwords will find it reset at the next
restart.

This was **not** changed here. Gating the seeder is a deployment decision with a real failure mode —
a production database with seeding disabled and no bootstrap path has no administrator at all — and
this pass was explicitly not a development cycle. The risk is now documented at its source, in
`SEED_CREDENTIALS.md`, and referenced from the README. It belongs on the go-live checklist.

> **Resolved in §23.** The gate was added in the following pass, together with the bootstrap path
> that made it safe.

---

## 23. Startup Seeding Hardening

Follow-up on the item flagged in §22.3. The gate could not be added on its own, because a production
database with seeding disabled and no bootstrap path has no administrator at all. This section
records the seed-data classification that made a safe split possible, then the change.

### 23.1 A finding that changed the risk rating

The repository is **public**:

```
gh repo view gul-dsc/ghars --json isPrivate,visibility
{"isPrivate":false,"visibility":"PUBLIC"}
```

The baseline pass assumed a private repository when it judged the hard-coded demo passwords
acceptable to commit. They are not. Five passwords, the account-name pattern that goes with them
(`superadmin@`, `dscadmin@`, `club-<slug>@`, `partner-<slug>@ghars.local`) and — before this change —
a seeder that created those accounts in **every** environment were together published on the open
internet. Anyone who found a reachable Ghars instance had a credential list for it.

The literals have been removed from `Data/DbSeeder.cs` and `SEED_CREDENTIALS.md`. They remain in the
public history of commits `4f1e77b` and `bb3772a` and cannot be removed without a force push, so
**they must be treated as permanently compromised**: any environment ever seeded with them needs
those accounts' passwords changed. After this change, no environment is seeded with them again.

### 23.2 Seed data classification

Every operation in `DbSeeder`, classified before any behaviour was modified.

| # | Seed operation | Entities | Class | Runs in Production |
| --- | --- | --- | --- | --- |
| 1 | Apply pending migrations | `__EFMigrationsHistory` | Structural | Yes |
| 2 | Identity roles | `AspNetRoles` × 7 | Structural | Yes |
| 3 | Active season | `Seasons` | Structural | Yes |
| 4 | Bootstrap administrator | `AspNetUsers`, `AspNetUserRoles` | Bootstrap | Only when configured **and** no admin exists |
| 5 | Fixed admin accounts (`superadmin@`, `dscadmin@`, `admin1-3@`) | `AspNetUsers` | Demo | No |
| 6 | 29 government entities + partner profiles | `Organizations`, `PartnerProfiles` | Demo | No |
| 7 | 7 clubs | `Organizations` | Demo | No |
| 8 | Partner and club admin users + org links | `AspNetUsers`, `OrganizationAdminLinks` | Demo | No |
| 9 | Learning programmes | `Activities` | Demo | No |
| 10 | Library categories and items | `LibraryCategories`, `LibraryItems` | Demo | No |
| 11 | Bookings, proposed times, audit trails | `BookingRequests`, `BookingProposedTimeOptions`, `BookingAuditTrails` | Demo | No |
| 12 | Attendance sessions and records | `AttendanceSessions`, `AttendanceRecords` | Demo | No |
| 13 | Certificates | `Certificates` | Demo | No |
| 14 | KPI submissions (2026 + 2027) | `KpiSubmissions` | Demo | No |
| 15 | Agenda entries | `AgendaEntries` | Demo | No |
| 16 | Gallery items, albums, media | `GalleryItems`, `MediaAlbums`, `MediaItems` | Demo | No |
| 17 | Official survey sample | `ExternalSurveys` | Demo | No |
| 18 | Notifications | `Notifications`, `NotificationDeliveries` | Demo | No |

Two classifications deserve their reasoning recorded.

**The season is structural, not demo.** Every controller resolves it with `FirstOrDefaultAsync`, so a
season-less database does not crash. But KPI, agenda, gallery and booking submission all require a
valid `SeasonId`, and `BookingsController` accepts only a season with `IsActive`. A production
database with no season starts cleanly and then rejects every club submission. It is reference data
the application needs in order to function, it carries no credentials, and it is idempotent.

**The 29 government entities are demo, despite being real organizations.** They arrive with 29
partner-admin accounts sharing one password, and that is the whole of the risk being removed here.
Real organizations are created by DSC through the admin UI, where each gets its own account. A fresh
production install therefore starts with no organizations — correct for a new installation, not a
regression.

### 23.3 The change

`SeedAsync` now takes `IHostEnvironment` and splits three ways:

| Method | Runs | Contains |
| --- | --- | --- |
| `SeedRequiredDataAsync` | Every environment | Migrations, roles, active season |
| `BootstrapAdministratorAsync` | Every environment, guarded | The first administrator, from configuration only |
| `SeedDevelopmentDemoDataAsync` | `Development` only | Rows 5–18 above |

**`EnsureSeedPasswordAsync` is deleted.** It called `ResetPasswordAsync` on every seeded account on
every application start. A password must not change because a process restarted. Demo passwords are
now set once, at account creation, and never touched again. Recovering a forgotten demo password is
an explicit operator action — `dotnet run -- reset-demo-passwords` — which refuses to run outside
`Development` and only ever touches `@ghars.local` accounts.

**No hard-coded passwords remain.** The demo password comes from `Ghars:Seed:DemoPassword`; when it
is absent, demo accounts are not created and everything depending on them is skipped, with one
warning naming the `dotnet user-secrets set` command that fixes it. Existing databases are
unaffected, because their demo accounts already exist.

### 23.4 Bootstrap administrator

The first administrator of a fresh production database is created from configuration:

| Configuration key | Environment variable |
| --- | --- |
| `Ghars:Bootstrap:AdminEmail` | `GHARS_BOOTSTRAP_ADMIN_EMAIL` |
| `Ghars:Bootstrap:AdminPassword` | `GHARS_BOOTSTRAP_ADMIN_PASSWORD` |
| `Ghars:Bootstrap:AdminFullName` | `GHARS_BOOTSTRAP_ADMIN_FULL_NAME` |

Guarantees, each enforced in `BootstrapAdministratorAsync`:

1. It runs only when **no** user holds `Super Admin` or `DSC Admin`. An existing administrator ends
   the check before configuration is read.
2. There is no default and no fallback value. Absent configuration creates nothing.
3. The password is never logged, never echoed in an error, and never written to a committed file.
   Identity validation failures are logged as descriptions only.
4. If the email names an existing account, the account is granted the role and its **password is left
   alone** — the bootstrap path cannot be used to take over an existing user's credentials.
5. When no administrator exists and no configuration is supplied, the application logs a critical
   message naming the variables to set, and starts normally. It does not invent an account, and it
   does not refuse to boot.

---

## 24. Public Contact Page

`Views/Home/Contact.cshtml` was a five-line stub. It now carries a working enquiry form.

**Where an enquiry goes.** The platform has no outbound email — no `IEmailSender`, no SMTP
configuration. Rather than a `mailto:` that leaves no record, an enquiry is persisted to
`ContactMessages` and announced through the in-app notification system already used by KPI
submissions and bookings. The record is written **before** the notification, so an enquiry survives
even if nobody is signed in to receive it.

Notifications are delivered to `DSC Admin` **and** `Super Admin`. `TargetRoleName` records DSC Admin
as the nominal audience, but an installation that has not yet created a DSC Admin must not lose
enquiries into a notification with no recipients.

| Piece | Location |
| --- | --- |
| Entity + bilingual labels | `Models/Core/ContactMessage.cs` |
| Displayed contact details | `Models/Core/ContactDetails.cs` |
| Public form + notification fan-out | `Controllers/Public/HomeController.cs` |
| Admin queue | `Controllers/Admin/ContactMessagesController.cs`, `Areas/Admin/Views/ContactMessages/` |
| Migration | `20260906162314_AddContactMessages` (one table, one index) |

**Contact details are configuration-driven with no defaults** (`Ghars:Contact:*`, each with a
`GHARS_CONTACT_*` environment fallback). Dubai Sports Council is a real government body; publishing
an invented address for it would send people to a mailbox nobody reads. An unset value is simply not
rendered, and the form works regardless. The seeded "Dubai Sports Council" organization row is not a
source for these — it is created only by the Development demo seed and carries placeholder values.

**Spam protection**, the first in the application, because this is its only anonymous POST endpoint: a
hidden honeypot field (a filled honeypot returns the normal thank-you and persists nothing, so a bot
gets no signal to retry differently) plus a `contact-form` rate limit of 5 submissions per 10 minutes
per client IP.

---

## 25. Public UI Refinement — Navigation, Booking Entry Point, Logo

A presentation-layer pass. No booking, KPI, agenda, gallery, survey or reporting logic was reopened,
and no persisted enum value was renamed or deleted.

### 25.1 Logo and header

The Ghars mark is **portrait** (443 × 563), so height buys only about 0.79× that in width and the
header's height is what the logo actually costs. Both were raised together rather than scaling the
image alone:

| Breakpoint | Navbar min-height | Logo height |
| --- | --- | --- |
| ≥ 1400px | 124px (was 88) | **96px** (was 64) |
| 1200–1399px | 110px | 84px |
| < 1200px (menu collapsed) | auto | 88px |
| < 992px | auto | 76px |
| < 576px | auto | 64px |

Below `navbar-expand-xl` the menu collapses, so the header row is only the brand and the toggler —
the logo keeps its full size there because there is nothing left for it to crowd. Nav padding and
gaps were tightened slightly so ten signed-in items still fit one row at 1440px without wrapping.

Measured in a real browser at every breakpoint: the logo never overflows the header box and no page
scrolls horizontally.

### 25.2 Partners → Booking

The public "Partners" navigation link became **Booking** (`الحجز`), and it leads to a genuine booking
entry point rather than a renamed directory:

- `HomeController.Booking` renders approved implementing entities as a **logo grid**.
- `HomeController.Partners` now **redirects** to it, so existing links and bookmarks keep working.
- `Views/Home/Partners.cshtml` was removed; the description/metadata cards it rendered are gone.

The entity list is queried from `Organization` with exactly the filter
`BookingsController.PopulateCreateViewDataAsync` uses (`Status == Approved` and type
`GovernmentAuthority` or `OtherPartner`). Sourcing it from `PartnerProfiles` instead would let a
bookable entity disappear from the page simply because it has no profile row.

### 25.3 Logo-only display

Every tile reserves the same logo area and uses `object-fit: contain`, so a wide or a tall mark is
letterboxed rather than stretched or cropped. An entity with no logo falls back to
`default-partner.svg` rather than rendering broken media. Grid: 4 columns desktop, 3 at < 1200px,
2 at < 768px, 1 below 360px. The call to action sits at the bottom of every tile and is **always
visible** — never hover-only, which would leave it unreachable on touch devices.

### 25.4 Request Booking

For a signed-in `Club Admin` each tile offers **Request Booking** / **طلب حجز**, linking to the
existing `/bookings/create?partnerId={id}`. No parallel booking implementation was created; the
`partnerId` entry point already existed on `BookingsController.Create`.

Behaviour by role, so no role is shown a call to action it cannot use:

| Role | Booking page |
| --- | --- |
| Anonymous | Entities shown; "Sign in to request", carrying `returnUrl` back to the page |
| Club Admin | **Request Booking** on every tile |
| Partner Admin | No club call to action; pointed at their partner dashboard |
| DSC / Super Admin | Pointed at admin booking management |

### 25.5 Training and Workshop

`ActivityType` already carried `Workshop` and `TrainingProgram`; **nothing was renamed or removed**.
This entry point emphasises them instead: the activity-type selector groups Training and Workshop
first under "Requested from the implementing entity", with Lecture, Event and Course retained under
"Other activity types", and a direct request now defaults to `TrainingProgram` rather than `Lecture`.

### 25.6 Course removed from Learning categories

The `Course` filter tab is gone from the learning-programmes view in both languages. The enum value,
the database records and the `?type=Course` URL are all untouched — existing Course programmes still
load, still render with their own label, and remain reachable under "All". No record was reclassified.

### 25.7 Navigation

**Contact is now the last item** in both languages, on desktop and in the collapsed mobile menu. In
RTL the list direction flips, so "last" correctly reads as leftmost.

**"My Organizations" was removed from user-facing navigation** — the main nav and the profile
dropdown. This is a menu change only: `OrganizationController.MyOrganizations`, organization
membership, admin links, org scoping and `/Admin/Organizations` are all untouched and still respond.

### 25.8 Arabic corrections

| Fixed | Was |
| --- | --- |
| Navbar role buttons | `Partner` / `Club` were hardcoded English → `لوحة الشريك` / `لوحة النادي` |
| Navbar login button | hardcoded `Login` → `تسجيل الدخول` |
| Login page | entirely English → bilingual |
| Dubai Islamic Economy Development Centre | Arabic name was half-untranslated (`دبي Islamic Economy Development Centre`) → `مركز دبي لتطوير الاقتصاد الإسلامي` |
| Partner detail heading | `شريك غرس` → `جهة منفذة`, matching the booking vocabulary |
| Contact page cross-link | pointed at the removed partners directory → the booking page |

The seed correction affects newly seeded databases only: entities are matched on `NameEn`, so no
existing row is rewritten and no duplicate is created.

### 25.9 Security — unchanged

The club remains **server-derived**. `BookingsController.Create` resolves the user's clubs from
`OrganizationAdminLinks`; a single-club user is assigned their club and the posted value is discarded
(`ModelState.Remove`), and a multi-club user's posted id must be one of their own or the request is
rejected. There is no hidden trusted club id. Verified in a browser: the booking form reached from a
tile shows the club as read-only text with **no dropdown**.

The implementing entity may be chosen on the public page, but it is re-validated server-side on POST
against `Status == Approved` and the permitted organization types before it is persisted. A
`partnerId` in the query string is a preselection, never an authorization.

---

## 26. Contact Migration Verification (2026-09-06)

A verification pass to bring the development database up to the committed code and confirm the
result at runtime. One defect was found and fixed; nothing else changed.

### 26.1 Migration state

`20260906162314_AddContactMessages` was **already applied** when this pass began. `DbSeeder`
calls `Database.MigrateAsync()` during startup, so the running application had applied it itself.
`dotnet ef database update` (a full build, not `--no-build`) confirmed *"No migrations were applied.
The database is already up to date."*, and `has-pending-model-changes` reported no changes.

`__EFMigrationsHistory` holds 11 rows for 10 migration files. The extra row is
`20260505103249_new one `, whose source file is absent — the known artifact of the migration-chain
repair recorded in §22.2, not a fault.

### 26.2 Schema verified against the migration

Verified from `INFORMATION_SCHEMA` and `sys.indexes` rather than trusting migration history:

| Check | Result |
| --- | --- |
| Columns | 19, matching the migration's declared types, lengths and nullability exactly |
| Identity | `Id` only |
| Primary key | `PK_ContactMessages`, clustered on `Id` |
| Index | `IX_ContactMessages_Status_CreatedAtUtc`, non-clustered, key order `(Status, CreatedAtUtc)` |
| Foreign keys | none, matching the migration — `SubmittedByUserId` is a free string, not a FK |

### 26.3 Runtime results

Application started from current `main` in Development. No seeder, migration or missing-column
errors; the only warning is the expected hardened-seeder notice that no demo password is configured.

| Area | English | Arabic |
| --- | --- | --- |
| Nav order | Home · About Us · Vision & Objectives · Booking · **Contact last** | same order, `dir="rtl"`, Contact last |
| Contact page | renders, all labels and topics localized | renders RTL, all labels and topics in Arabic |
| Valid submission | persisted + success confirmation | persisted + success confirmation |
| Required/format validation | rejected with field messages | rejected with field messages |

Stored rows were inspected directly: anonymous submissions correctly store `NULL` for
`SubmittedByUserId`/`CreatedByUserId`, `Status = New (1)`, the submitting culture and client IP,
and genuine UTC timestamps. Arabic content stored without corruption.

**Security.** Antiforgery-less POST → `400`. Sixth POST in the window → `429`. A filled honeypot
returned the normal thank-you and persisted **nothing**. The stored
`<script>alert('xss')</script>` payload renders as text in the admin detail view — no raw markup in
the DOM and no dialog fired. Length limits match the model.

### 26.4 Defect found and fixed

Every contact validation message rendered in **English on the Arabic interface** — the only place in
the codebase with hardcoded English `ErrorMessage` strings. Fixed in commit `42fd080` with
`Models/Validation/BilingualValidationAttributes.cs`; see that commit for why the attributes derive
from `ValidationAttribute` (a framework-derived attribute has its message baked once, in whichever
language served the first request) and implement `IClientModelValidator` themselves. Verified in both
language orderings against a fresh application.

`MaxLength` messages still use the framework English default. They are unreachable through the UI,
which caps input with the `maxlength` attribute, and surface only on a hand-crafted post.

### 26.5 Regression checks

| Role | Verified |
| --- | --- |
| Anonymous | logo 96/76/64 px at desktop/tablet/mobile, aspect preserved, no overlap or horizontal overflow; `Booking` present, no `Partners`, no `My Organization`; no Course tab; 29 entity logos, `object-fit: contain`, none distorted, all actions visible without hover; login `returnUrl` honoured |
| Club Admin | club rendered read-only — **no `OrganizationId` control exists in the DOM**; entity preselected from the tile; Training/Workshop grouped first |
| Partner Admin | `/partner` dashboard intact; booking tiles show a non-actionable *Clubs only*; `/bookings/create` → `AccessDenied` |
| DSC Admin | `/Admin/Organizations`, `/Admin/Bookings`, `/Admin/ContactMessages` all reachable; organization management intact |

**Ownership proven, not assumed.** An authenticated Club Admin of organization 30 posted
`OrganizationId=31` (a different club). The persisted booking recorded organization **30** — the
server discarded the posted value. Posting a club id as the implementing entity was rejected with
*"The selected implementing entity is not valid."* and persisted nothing.

### 26.6 Test data removed

All rows created by this pass were deleted: 4 contact messages, 1 booking request, 1 booking audit
trail, 5 notifications and 21 notification deliveries. The 15 pre-existing bookings and 14
pre-existing notifications were left untouched, and no orphaned deliveries remain.

---

## 27. Partner Programs / Offerings Workflow (2026-09-07)

### 27.1 Business purpose

Implementing entities previously had no way to say what they offer. Clubs saw a grid of entity logos
and had to describe from scratch whatever they wanted, and the 59 seeded "learning programs" were
reachable only through `/partners/{id}/learning-programs`, a page nothing linked to prominently.

This pass gives the partner a catalogue to manage and the club a catalogue to browse:

> Partner Admin creates and publishes Training/Workshop offerings -> Club Admin browses published
> offerings grouped by implementing entity -> Club requests a booking -> Partner processes the
> request through the existing Partner Dashboard.

The direct/custom booking flow is untouched and remains available as a clearly secondary action.

### 27.2 Data model decision — `Activity` reused, no new entity

`Activity` was already almost exactly the required shape, so no `PartnerOffering` table was created:

| Requirement | Already present |
| --- | --- |
| Owning implementing entity | `Activity.PartnerOrganizationId` (nullable FK, added by its own migration `20260503173000_AddActivityPartnerOrganization`) |
| Publication state | `Activity.Status` — `ActivityStatus.Draft` / `Published` / `Closed` / `Cancelled` |
| Offering types | `ActivityType.TrainingProgram` (3) and `ActivityType.Workshop` (2) |
| Season | `Activity.SeasonId` |
| Bilingual title/description | `TitleEn/Ar`, `DescriptionEn/Ar` |
| Capacity, location | `Capacity`, `LocationEn/Ar` |
| Booking relationship | `BookingRequest.ActivityId`, already nullable, whose own comment anticipates program-anchored bookings |
| Audit stamps | `AuditableEntity` base |

The database confirmed the fit rather than the code alone: all 59 existing activity rows already
carry a `PartnerOrganizationId` and were created by `partner-*@ghars.local` users. Partner-created
offerings are not a new concept in this schema — they are what the table already holds.

**Ownership is `PartnerOrganizationId` and nothing else.** The distinction demanded by the
requirement to keep partner and system records apart already exists and needed no new discriminator
column: the admin `ActivitiesController.Create`/`Edit` never assign `PartnerOrganizationId`, so a
DSC-created activity has it `NULL` and is invisible to every partner query.

Note the partner management surface uses a **stricter** ownership test than the partner *dashboard*
does for reading. `PartnerDashboardController` and `/partners/{id}/learning-programs` also match on
`CreatedByUserId`, a reasonable fallback for display; `PartnerProgramsController` requires the
explicit foreign key, because a creator-based match is too weak a basis for edit and publish rights.

### 27.3 Schema change — `20260907162752_AddActivityOfferingFields`

Four nullable columns and one index. Additive, safe on the populated database, no historical row
altered and no back-fill required:

| Column | Type | Why it could not be reused |
| --- | --- | --- |
| `TargetAudienceCsv` | `nvarchar(250) NULL` | No audience concept existed on `Activity`. Uses the identical vocabulary and CSV convention as `BookingRequest.TargetAudienceCsv`, so the value carries into a booking with no mapping. |
| `OtherTargetAudience` | `nvarchar(150) NULL` | Required when "Others" is selected, mirroring `BookingRequest`. |
| `AvailableFromUtc` | `datetime2 NULL` | `StartDateTime`/`EndDateTime` are a real scheduled slot for 59 existing rows and feed attendance and the admin dashboard. Overloading them as an availability window would have changed the meaning of existing data. |
| `AvailableUntilUtc` | `datetime2 NULL` | As above. |

The migration also replaces `IX_Activities_PartnerOrganizationId` with
`IX_Activities_PartnerOrganizationId_Status_Type`, which leads with the same column and therefore
still serves the foreign key while also covering both new queries (one entity's rows; published rows
of a type across all entities). `Down` restores the original index.

Deliberately **not** added: delivery mode (no such concept exists anywhere in the application, and
inventing an enum and a column for it was not justified) and a duration column (duration is derived
from the session start and end and displayed on the club card).

Verified against `INFORMATION_SCHEMA` after applying, not from migration history: four columns with
the declared types and nullability, the composite index present, and all 59 rows intact.

> The first attempt at this migration was scaffolded with `dotnet ef migrations add --no-build` and
> produced an **empty** `Up`/`Down` — the stale-assembly trap recorded in section 26. It was removed
> and regenerated after a real build.

### 27.4 Partner "My Programs"

`Controllers/Public/PartnerProgramsController.cs`, `[Authorize(Roles = PartnerAdmin)]`, routes
`/partner/programs`, `/partner/programs/create`, `/partner/programs/edit/{id}`, and POST
`/partner/programs/publish` and `/unpublish`. It inherits `BaseController` to reuse the existing
`AuditAsync` helper, so Create, Update, Publish and Unpublish write `SystemAuditLog` rows exactly as
the admin Activities screen does. No new audit framework.

- Ownership is written once, from `OrganizationAdminLink`, and is never model-bound.
  `PartnerProgramVm` has no `PartnerOrganizationId` property at all — the posted form carries no
  organization id, so there is nothing to tamper with.
- Creation and editing are restricted to `TrainingProgram` and `Workshop`. `Lecture`, `Course`,
  `Event` and `Activity` remain valid enum values and are untouched on historical rows.
- Every query is scoped by organization and a miss returns `NotFound()`, so a foreign id is
  indistinguishable from a non-existent one.
- **There is no delete.** An offering may already anchor bookings, attendance sessions and
  certificates. Unpublishing withdraws it from the club catalogue, which is what "no longer offered"
  actually means.
- **Once bookings exist, season and type are locked** (the descriptive fields stay editable), because
  changing them would silently rewrite what those bookings were made against. The lock is enforced in
  the controller by overwriting the posted values, not merely by disabling the inputs — verified by
  posting a changed season and type directly and confirming the stored row was unchanged.

Booking counts on the list come from a single grouped query over `BookingRequest.ActivityId`, not a
count per row.

### 27.5 Club booking catalogue

`/Home/Booking` now lists published offerings grouped under each implementing entity's logo and name,
with filters for entity, type, season and title, and the existing entity logos retained as the group
headers. Visibility requires: `Status == Published`, an owning entity that is approved and is an
implementing entity, type Training or Workshop, and today inside any availability window.

Custom booking remains, deliberately secondary: a single "Can't find what you need? -> Request Custom
Booking" strip below the catalogue, and inside the empty state.

Anonymous visitors browse the catalogue and get "Sign in to request", with `returnUrl` pointing at
`/bookings/create?activityId=...` so sign-in continues into the selected offering.

Partner Admins are **redirected** from this page to `/partner` rather than shown disabled "Clubs
only" tiles, and the club-oriented Booking link is hidden from their navigation entirely. Their two
links are Partner Dashboard and My Programs.

### 27.6 Requesting a booking from an offering

No new booking path was written: `/bookings/create?activityId=N` already existed and already stored
`BookingRequest.ActivityId`, derived the partner organization server-side and pre-filled subject,
season, type and times. Two changes were made to it:

1. Target audience is now carried from the offering into the form, since both sides use the same
   vocabulary. The club can change it, and the posted value is what is validated and stored.
2. Validation was tightened into a shared `BookableOfferings()` filter used by both GET and POST, so
   an id arriving from the browser is re-checked against publication state, entity approval, season
   and availability window on every request.

Type is deliberately **not** restricted on the booking path. The catalogue surfaces only Training and
Workshop, but 27 published `Course` and 2 `Lecture` programs are bookable today through
`/partners/{id}/learning-programs`, and filtering by type here would have silently withdrawn them.
Type restriction belongs at creation, and that is where it is enforced.

### 27.7 Security verification

Partner A is Dubai Police (organization 17); Partner B is Dubai Sports Council (organization 19).

| Attempt | Result |
| --- | --- |
| B `GET /partner/programs/edit/{A draft}` and `{A published}` | `404` |
| B `POST /partner/programs/publish` with A's draft id | `404` |
| B `POST /partner/programs/unpublish` with A's published id | `404` |
| B `POST /partner/programs/edit/{A id}` with a hijacked title | `404`, A's row unchanged |
| B's own listing | Contains none of A's programs |
| Club Admin `GET /partner/programs`, `/create`, `/edit/{id}` | `AccessDenied` on all three |
| Club Admin `GET /bookings/create?activityId={draft}` | `404` — an unpublished offering is not bookable |
| Partner creating type `Course` | Rejected: "Choose either a training programme or a workshop." |
| Partner creating against the inactive season | Rejected: "not valid or not active" |

Ownership was confirmed in the database, not inferred: every offering created through the partner
form persisted with `PartnerOrganizationId = 17`, a value the form never posted.

DSC authority is unchanged — `/Admin/Activities` still lists every row regardless of owner, and now
also shows an Implementing Entity column so partner-managed offerings are identifiable.

### 27.8 End-to-end result

Partner created a Training draft and a published Workshop -> the draft stayed invisible to clubs and
the Workshop appeared under "Dubai Police" in the catalogue -> the club requested it -> booking 17
persisted with `ActivityId=63`, `SeasonId=1`, club 30 (server-derived), entity 17 (from the
offering), status Pending, audience `Coaches` -> the partner saw it on the dashboard and confirmed it
with a lecturer name -> the existing booking-confirmation flow created its Draft agenda entry as
before.

### 27.9 Reporting impact

Baseline was measured before any test data and again after cleanup; the two match exactly.

| Metric | Before | With a published offering and one booking | Verdict |
| --- | --- | --- | --- |
| Activities | 59 | 65 | Catalogue rows, as expected |
| Completed activities (`EndDateTime < now`) | 59 | **59** | Publishing an offering did **not** increase delivered activity |
| Agenda entries Submitted/Approved — the source reports use for delivered activity | 10 | **10** | Unchanged |
| KPI submissions | 14 | **14** | Unchanged |
| Booking requests | 15 | 16 | Increased only when a `BookingRequest` was created |

`ReportsController` already derives delivered activity from `AgendaEntry` rows with status Submitted
or Approved, never from `Activity` — so published offerings cannot reach a delivered-activity report.
No KPI definition was changed.

Two honest caveats, documented rather than silently fixed:

- The admin dashboard's `totalCapacity` sums `Activity.Capacity` across all activities and is the
  denominator of its attendance-rate tile, so publishing offerings dilutes that percentage. This is
  pre-existing: all 59 rows are already partner offerings, so the tile already means "capacity of the
  catalogue". Changing it would alter an existing metric definition, which this task explicitly
  forbids.
- `completedActivities` counts activities whose end date has passed, which for a catalogue row means
  "its indicative session date is in the past" rather than "it was delivered". Also pre-existing, and
  unchanged by this work.

### 27.10 A seeder behaviour worth knowing

After the end-to-end test, `AttendanceRecords` rose by 2 and `Certificates` by 2 for the new
offering. This is **not** done by any code in this feature. `DbSeeder.cs:692` seeds attendance
sessions, records and certificates for approved or confirmed bookings that have an `ActivityId`, so
confirming the test booking caused the next Development startup to generate demo attendance for it.
It is Development-only under the hardened seeder and cannot occur in production. Nothing in the
offering workflow creates attendance sessions, certificates, or agenda entries.

### 27.11 Bilingual verification

Verified with the `.AspNetCore.Culture` cookie, in both languages, across My Programs, the create
form, the club catalogue and the booking form: RTL applied, Arabic titles preferred over English when
present, all labels, badges, filters, buttons, empty states and validation messages translated, and
no English UI string left in the Arabic renders.

One defect was found and fixed during this verification, the same class as section 26.4: MVC emits
English `data-val-*` messages for non-nullable value types and for `[MaxLength]`/`[Range]`
annotations, and no request culture can change them. `PartnerProgramVm` therefore carries **no
message-bearing DataAnnotations at all** — `SeasonId`, `Type` and `Capacity` are nullable and every
rule lives in `IValidatableObject`, which is evaluated per request. Confirmed by inspecting the
rendered Arabic form: zero `data-val-*` attributes remain, and the server-side summary is entirely
Arabic.

### 27.12 Open policy question

No DSC approval workflow existed at the time of this section, and none was invented here. **This was
subsequently resolved: see section 28, which implements the approval workflow. A Partner Admin can no
longer publish directly.** The description below records the state as of 2026-09-07 before that
change.

A Partner Admin published their own Training and Workshop offerings under their own organization
directly. DSC retained full oversight and could edit or unpublish anything through `/Admin/Activities`.

### 27.13 Test data removed

All rows created by this pass were deleted: 6 activities, 1 booking request, 2 booking audit trails,
1 agenda entry, 1 attendance session, 2 attendance records, 2 certificates, 2 notifications, 3
notification deliveries and 7 system audit log rows. Every metric returned to its exact pre-test
value and no orphaned rows remain.

---

## 28. DSC Approval Workflow for Partner Offerings (2026-09-07)

Section 27 left an open policy question: a Partner Admin could publish their own offering directly,
with no DSC gate. This section closes it. **A partner can no longer make anything visible to clubs.**

### 28.1 Lifecycle

```
  Draft ──submit──▶ SubmittedForApproval ──approve──▶ Approved ──unpublish──▶ Unpublished
    ▲                    │        │                                              │
    │                    │        └──reject──▶ Rejected ──edit──┐                │
    └────────edit────────┴──return──▶ ReturnedForCorrection ────┴──resubmit──────┘
```

| State | Visible to clubs | Partner may edit | Partner may submit |
| --- | --- | --- | --- |
| `Draft` | no | yes | yes |
| `SubmittedForApproval` | no | **no** | no |
| `ReturnedForCorrection` | no | yes | yes (resubmit) |
| `Rejected` | no | yes | yes (resubmit) |
| `Approved` | **yes** | **no** | no |
| `Unpublished` | no | yes | yes (resubmit) |

Two locks are worth stating explicitly:

- **Submitted is locked** so a partner cannot alter content out from under a reviewer mid-review.
- **Approved is locked** so approved text cannot be silently rewritten after review. To change an
  approved offering the partner unpublishes it, edits, and resubmits. Both locks are enforced on GET
  *and* on POST, so a form rendered while the offering was editable cannot be posted after the state
  moves.

### 28.2 Data model

A separate `OfferingApprovalStatus` enum on a **new nullable column**, rather than extra members on
`ActivityStatus`. `Status == Published` is compared in a dozen unrelated places — surveys, attendance,
the public home page, the admin dashboard — and widening that enum would silently change what those
comparisons exclude. The column is `NULL` for DSC-created activities, which have no owning entity and
are outside this workflow entirely.

Visibility to a club now requires **both** `ApprovalStatus == Approved` and
`ActivityStatus.Published`, and only a DSC approval ever sets that pair.

Migration `20260907172613_AddOfferingApprovalWorkflow`, additive:

| Column | Type |
| --- | --- |
| `ApprovalStatus` | `tinyint NULL` |
| `SubmittedAtUtc` | `datetime2 NULL` |
| `SubmittedByUserId` | `nvarchar(450) NULL` |
| `ReviewedAtUtc` | `datetime2 NULL` |
| `ReviewedByUserId` | `nvarchar(450) NULL` |
| `ReviewNotes` | `nvarchar(2000) NULL` |

plus `IX_Activities_ApprovalStatus_SubmittedAtUtc` for the review queue. Nothing was duplicated —
created/updated stamps continue to come from the `AuditableEntity` base.

`Helpers/OfferingWorkflow.cs` holds the state labels, badge classes and the transition predicates
(`PartnerCanEdit`, `PartnerCanSubmit`, `CanUnpublish`, `DscCanReview`) in one place, so the
controllers that enforce the rules and the views that render them cannot drift apart.

### 28.3 Legacy backfill — the exact rule

Derived from each row's existing `ActivityStatus` and nothing else. No historical row was inspected,
guessed at, or rejected:

| Existing row | Backfilled to | Effect |
| --- | --- | --- |
| partner-owned, `Published` | `Approved` | **grandfathered — stays visible to clubs** |
| partner-owned, `Draft` | `Draft` | stays invisible, stays editable |
| partner-owned, `Closed` | `Unpublished` | already withdrawn, stays withdrawn |
| partner-owned, `Cancelled` | `Unpublished` | as above |
| DSC-created (no owning entity) | left `NULL` | outside the workflow |

`Unpublished` rather than `Rejected` for Closed/Cancelled, because those rows were *withdrawn*, not
refused — nothing in their history records a reviewer refusing them. Each statement is guarded by
`ApprovalStatus IS NULL` so a re-run cannot overwrite a state a reviewer has since set.

**Result on the development database: all 59 existing partner offerings grandfathered to `Approved`,
none removed, none rejected, and the club catalogue was unaffected.**

### 28.4 Partner actions

Create as draft, edit a draft, submit for approval, edit and resubmit a returned or rejected
offering, read the reviewer's notes, and unpublish an approved offering. The partner may unpublish
their own content because that action only ever *removes* visibility — it can never create it, and
restoring visibility needs a fresh submission and a fresh approval.

The old "Publish" button is gone from the partner surface. Create now offers **Save Draft** and
**Submit for Approval**; a new offering is created with `Status = Draft` whichever button is pressed.

### 28.5 DSC actions

Implemented inside the existing `Areas/Admin` Activities screen rather than as a second admin module.
The Index gained filters for implementing entity, season, type and approval status, plus an
**Awaiting approval** shortcut with a live count; the Details page gained a DSC Review panel with
Approve, Return for correction, Reject and Unpublish.

Notes are **required** for a return or a rejection, enforced server-side — a decision the partner
cannot act on is not a decision. Approval clears the note, since it belonged to the correction round
that just ended; the note is retained after a resubmission so the reviewer keeps context, but the
partner's list only shows it while it is still actionable.

These four actions are open to **DSC Admin as well as Super Admin**, unlike the create/edit/delete
actions beside them. That is a deliberate departure from the "admin mutations are Super Admin only"
convention: reviewing partner submissions is the DSC Admin's job and restricting it to Super Admin
would leave the queue unworkable. Content authoring stays Super Admin only.

One consistency hole was closed at the same time: the pre-existing admin **Publish** action set
`Status` without touching `ApprovalStatus`, which would have produced a row that is published but not
approved — listed on the partner directory yet unbookable. It now also approves a partner offering,
with the reviewer stamps recorded.

### 28.6 Club visibility

Enforced in the shared server-side query, not the UI. `BookingsController.BookableOfferings()` — used
by both the booking GET and POST — and the catalogue query both now require
`ApprovalStatus == Approved` for any row with an owning entity. Draft, SubmittedForApproval,
ReturnedForCorrection, Rejected and Unpublished offerings are excluded from the catalogue *and* from
a hand-typed `activityId`, which returns 404 either way.

DSC-created activities (`PartnerOrganizationId IS NULL`) are unaffected and keep working through
`Status` alone, so the older `/partners/{id}/learning-programs` route behaves exactly as before.

### 28.7 Notifications

Reused, not rebuilt. Submission raises a **role-targeted** notification delivered to DSC Admins and
Super Admins, following the convention already used for KPI submissions and contact enquiries so a
site with no DSC Admin cannot lose a submission. Approve, return, reject and unpublish raise an
**organization-targeted** notification whose deliveries resolve through the owning entity's admin
links only.

Verified in the database: the submission notification produced 5 deliveries, all to admin accounts;
the three partner-facing notifications produced 1 delivery each, all to
`partner-dubai-police@ghars.local`. No unrelated organization was addressed.

### 28.8 Audit

`SystemAuditLog` via the existing `BaseController.AuditAsync`. A full run recorded, in order:

```
OfferingDraftCreated            partner-dubai-police@ghars.local
OfferingSubmitted               partner-dubai-police@ghars.local
OfferingReturnedForCorrection   dscadmin@ghars.local
OfferingUpdated                 partner-dubai-police@ghars.local
OfferingResubmitted             partner-dubai-police@ghars.local
OfferingApproved                dscadmin@ghars.local
OfferingUnpublishedByDsc        dscadmin@ghars.local
```

`OfferingRejected` and `OfferingUnpublishedByPartner` are written by the same helpers and were
exercised separately. No historical audit record was modified.

### 28.9 Runtime verification

The full 14-step lifecycle, each state read back from the database rather than inferred from the UI:

| Step | Result |
| --- | --- |
| 1. Partner creates Draft | `approval=Draft, activityStatus=Draft` |
| 2. Club cannot see it | absent from catalogue; direct `activityId` → **404** |
| 3. Partner submits | `approval=Submitted, activityStatus=Draft`, submit stamp set; edit now **blocked** |
| 4. Club still cannot see it | absent; direct `activityId` → **404** |
| 5. DSC sees submission | appears in the Awaiting-approval queue; review panel renders |
| 6. DSC returns with notes | note-less return **refused**; with notes → `approval=Returned`, notes stored, review stamp set |
| 7. Partner edits and resubmits | notes shown on the form; → `approval=Submitted` |
| 8. DSC approves | `approval=Approved, activityStatus=Published`, notes cleared |
| 9. Club now sees it | in catalogue; booking form 200 |
| 10. Club books it | booking 18 stored with `ActivityId=66` |
| 11. Partner receives it | visible on the Partner Dashboard |
| 12. DSC unpublishes | `approval=Unpublished, activityStatus=Draft` |
| 13. Club can no longer see or book it | absent; direct `activityId` → **404** |
| 14. Historical booking intact | row `18 \| 66 \| 30 \| 17 \| Pending` unchanged; club can still open it |

Additional checks: rejecting works and a rejected offering is unreachable by direct id; DSC cannot
re-review an already-decided offering (an Approve posted against a Rejected row left it Rejected); and
an approved offering cannot be edited — a POST attempting to rename one to "SNEAKY EDIT" left the
stored title untouched.

**IDOR.** Partner B against Partner A's offering: edit → 404, submit → 404, unpublish → 404, and A's
rows never appear in B's listing. Ownership remains server-derived from `OrganizationAdminLink`; the
view model still has no `PartnerOrganizationId` property.

### 28.10 Reporting

Unchanged, and verified with the workflow exercised end to end:

| Metric | Baseline | With the workflow exercised |
| --- | --- | --- |
| Agenda entries Submitted/Approved (delivered activity) | 10 | **10** |
| KPI submissions | 14 | **14** |
| Completed activities | 59 | **59** |
| Booking requests | 15 | 16 (only because a booking was created) |

Approving an offering moves no delivered-activity number. The distinction holds: **offering =
available, BookingRequest = requested, Agenda = delivered.** No KPI definition was touched.

### 28.11 Arabic

All six workflow states, the reviewer-notes block, the submit/resubmit/unpublish buttons, the
approval-status filter and the approval explanation carry Arabic text, verified under RTL with the
culture cookie. The Arabic create form emits **zero** `data-val-*` attributes, so no English framework
validation message can surface — the rule established in sections 26.4 and 27.11 still holds.

### 28.12 Migration result

Applied without `--no-build` at either step. The chain was first built from **empty** into a scratch
database (`GharsScratchApprovalTest`, since dropped) to prove the whole sequence still constructs a
database, then applied to the development database. Schema verified directly from
`INFORMATION_SCHEMA` and `sys.indexes` afterwards: six nullable columns with the declared types and
the new index present, 59 rows intact.

### 28.13 Test data removed

3 activities, 1 booking request, 1 booking audit trail, 8 notifications, 20 notification deliveries
and 11 system audit log rows. Every metric returned to its exact pre-test value — 59 activities all
`Approved`, 0 with a null approval state, agenda 11, bookings 15, KPI 14, attendance 8, certificates
8 — with no orphaned rows.

---

## 29. Partner Offering UX & Release Verification (2026-09-07)

Sections 27 and 28 built the partner offering catalogue and the DSC approval gate. This section makes
that lifecycle legible and verifies the result for release. **No business workflow changed.** The
approval rule from §28 stands exactly as it was: `Approved → Unpublish → Edit → Resubmit → DSC Review
→ Approve`, with approved content locked against direct editing. No migration was required.

### 29.1 Status UX on My Programs

Every program now states its lifecycle position in words, an icon and a colour — never colour alone,
and never a raw enum name. `OfferingWorkflow` gained `Icon()` alongside the existing `Label()` and
`BadgeClass()`, so the six states render identically wherever they appear.

A compact summary strip sits above the list: **Total Programs**, **Draft**, **Awaiting DSC Approval**,
**Approved**. The counts are computed in the controller from `ApprovalStatus` — the same field the
badges and the filter use, so the header and the list can never disagree — in a single grouped query
over *all* owned programs rather than the filtered page, so an outstanding item stays visible while
the partner is looking at one slice. Each tile links to that filter.

Actions are contextual and offer only what the server will accept:

| State | Offered |
| --- | --- |
| `Draft` | View, Edit, Submit for approval |
| `SubmittedForApproval` | View + "Awaiting DSC review" — no Edit, no Submit |
| `ReturnedForCorrection` | View, Edit, Resubmit, reviewer notes |
| `Approved` | View, Unpublish, request count — no Edit |
| `Rejected` | View, Edit, Resubmit, reviewer notes |
| `Unpublished` | View, Edit, Submit for approval |

The buttons are a convenience, never the rule: `PartnerProgramsController` re-checks the same
`OfferingWorkflow` predicates on every GET *and* every POST.

### 29.2 The approved edit lock, explained rather than hidden

§28's two locks were correct but silent — a missing Edit button reads as a broken screen. Both are now
stated in the exact same words everywhere they surface, from a single `OfferingWorkflow.Explain()`:

> To change an approved program, unpublish it first. After editing, it must be submitted to DSC for
> approval again.
>
> لتعديل برنامج معتمد، يجب إلغاء نشره أولاً. وبعد التعديل يلزم إعادة إرساله إلى مجلس دبي الرياضي للاعتماد.

`OfferingWorkflow.EditLockMessage()` now returns that same sentence, so the toast shown after a blocked
POST and the inline guidance on the list and details pages are one string, not three that can drift.

### 29.3 Workflow history and review notes

A new read-only **Program Details** page (`/partner/programs/details/{id}`) shows created, last
updated, submitted, submitted by, reviewed, reviewed by, approval status and review notes.

Two things it deliberately does not show: **internal identifiers** — `SubmittedByUserId` and
`ReviewedByUserId` are resolved to `FullName`, falling back to a role description rather than a user id
or an email address — and **raw audit payloads**. The workflow timeline is read from the
`SystemAuditLog` rows §28 already writes, projected through `OfferingWorkflow.AuditActionLabel()`;
only recognised transitions are narrated and the stored JSON is never rendered. Verified: no GUID and
no `OldValuesJson` appears in the response.

Reviewer notes appear **above** the program information on a returned or rejected offering, in a
coloured panel headed *DSC Review Notes / ملاحظات مراجعة المجلس* — verified by string position in the
rendered page. No panel is rendered when there is nothing to say, and the note continues to be shown
only while it is still actionable (§28.5).

### 29.4 DSC review improvements

Still the existing Admin Activities screen — no second module. With `pendingOnly=true` the page renders
a dedicated **queue**, ordered oldest submission first, carrying program (EN + AR), implementing
entity, type, season, submitted date, submitted by and status. Those eight fields are paired into five
columns rather than eight: the admin shell already scrolls sideways on a phone and a wider table makes
that worse for no gain (see §29.8).

The Details page is now split into the three things a reviewer does in order — **Program Information**
(including target audience, capacity and availability, which the reviewer previously could not see),
**Submission Information** (by whom, when, current state) and **Review Decision**. Notes remain
required for a return or a rejection and optional for an approval, enforced server-side as before;
confirmations were added to all three decisions and to Unpublish, using the `confirm()` pattern the
rest of the admin area already uses.

Both surfaces became bilingual, following the `T()` convention already used by ten other admin views.
Approval states there now render through `OfferingWorkflow.Label()` instead of the raw enum.

### 29.5 Partner Dashboard

Unchanged in purpose — it remains the booking inbox. It gained one compact strip: **My Programs**
with Awaiting DSC Approval / Approved / Returned and a **Manage My Programs** button. The counts are
scoped by `PartnerOrganizationId` only, the same definition My Programs uses, so the strip and the page
it links to always agree; the dashboard's own broader activity query (which also matches by creator)
would have over-counted.

### 29.6 Club catalogue and the custom-booking fallback

Grouping by implementing entity is unchanged. Cards gained capacity and a full availability window
(from / until / both), and the entity filter now lists **only entities that actually have a bookable
offering** — computed before the entity filter is applied, so choosing one entity does not empty its own
dropdown. Every eligible entity remains reachable through Request Custom Booking, which keeps its own
complete list. Empty entity sections never rendered and still do not.

Custom booking is presented as a legitimate fallback rather than an error path:

> Can't find the training or workshop you need? — لم تجدوا التدريب أو الورشة التي تحتاجونها؟

The empty state now reads "No programs match your filters right now" instead of implying nothing
exists, and still offers the fallback.

### 29.7 Offering-based vs custom request

`ActivityId` is the discriminator, and no placeholder Activity is ever created for a custom request.
A booking with no linked program is now labelled **Custom Booking Request / طلب حجز مخصص** rather than
falling back to a blank or to "Direct request", on the booking details page (as a badge in the summary
strip and as an explicit *Request source* field) and in both Partner Dashboard tables — visible in the
pending-action list, so the partner knows which kind of request they are deciding on before they open
it. Booking details also now names the implementing entity and shows the requested type for custom
requests, where it previously showed an empty program type.

The booking history table now translates its stored action names (`ClubSubmitted` → "Club submitted the
request" / "قدّم النادي الطلب") instead of printing them raw. No audit system was added and no JSON is
exposed.

### 29.8 Season validity — the one behavioural fix

`BookableOfferings()` required an offering to have a season but not an *active* one, while a custom
request rejects an inactive season outright. The two entry points therefore disagreed: a club could not
name a closed season on the form, but could still reach an offering sitting in one by passing its id.
`x.Season.IsActive` was added to the shared query and to the catalogue query.

Checked against the data before changing it: all 59 published offerings belong to the single active
season (Season 2026-2027), and the only other season holds no activities. **The gap closes without
withdrawing anything** — the catalogue still lists 30 offerings across 29 entities, unchanged.

### 29.9 Accessibility and mobile

Form labels on the partner program form now carry `for`, bound to the control only when one exists (a
locked field renders as read-only text, and a `for` pointing at nothing helps no reader). The same was
done for the catalogue, admin activity and partner dashboard filters. Status is never conveyed by
colour alone. Decorative icons carry `aria-hidden`. Organization logos carry the entity name as alt
text. Heading hierarchy descends without skipping on every page checked, and each has exactly one
`<h1>` outside the admin shell.

One real defect fixed: the partner form's validation summary rendered an **empty red alert box** on a
valid form, because the tag helper emits its container either way. It is now rendered only when there
is something to report.

Measured in Chrome at 390 × 844 in both languages, using `documentElement.scrollWidth`:

| Page | Page-level horizontal overflow |
| --- | --- |
| Partner My Programs / Create / Edit / Details, Partner Dashboard | **0 px** |
| Club booking catalogue | **0 px** |
| DSC review details | 0 px (en) |
| DSC review queue | 220 px |
| Admin Activities list | 251 px |
| *Admin Bookings (untouched, for comparison)* | *277 px* |
| *Admin Organizations (untouched, for comparison)* | *189 px* |

Wide tables scroll inside their own `.table-responsive` container, which is this project's pattern and
is not page-level overflow. The residual overflow on admin pages comes from the admin shell's fixed
sidebar and is **pre-existing** — untouched admin pages show the same thing. Both Activities pages are
now *narrower* than the untouched Bookings page; before the column pairing they were 484 px and 430 px.

Two pre-existing items were found and deliberately left alone, since fixing them means changing the
admin shell rather than this workflow: the admin sidebar does not collapse at mobile width, and
`wwwroot/js/admin.js` injects a client-side filter bar whose six controls have no `id`/`for` pairing —
present identically on admin pages this pass never touched.

### 29.10 Security regression

Repeated in full against fixtures held in all six states.

**A club cannot reach a non-approved offering**, by catalogue or by hand-typed id:

| Offering state | Catalogue | `GET /bookings/create?activityId=` | `POST` with that id |
| --- | --- | --- | --- |
| Draft | hidden | **404** | 0 rows created |
| SubmittedForApproval | hidden | **404** | 0 rows created |
| ReturnedForCorrection | hidden | **404** | 0 rows created |
| Rejected | hidden | **404** | 0 rows created |
| Unpublished | hidden | **404** | 0 rows created |

**Partner B against Partner A**: view details → 404, open edit → 404, POST edit → 404 with the title
unchanged, submit → 404 and still Draft, unpublish an approved offering → 404 and still Approved. A's
rows never appear in B's listing.

**Club and anonymous**: every partner program route returns Access Denied for a club and the login
redirect for an anonymous visitor; a club POST to the submit endpoint left the offering Draft.
**DSC retains review authority**; a Partner Admin posting to `/Admin/Activities/Approve` is redirected
to Access Denied with the state unchanged. Notes remain required — a return with empty notes and a
reject with whitespace-only notes were both refused, leaving the offering Submitted.

### 29.11 Reporting regression

Eight test programs were created spanning all six states, including one that reached Approved:

| Metric | Baseline | With eight programs across all states | After cleanup |
| --- | --- | --- | --- |
| Agenda entries Submitted/Approved (delivered) | 10 | **10** | 10 |
| KPI submissions | 14 | **14** | 14 |
| Booking requests | 15 | 16 (one real booking) | 15 |
| Attendance records | 8 | 10 (Development seeder only — see below) | 8 |

Creating, submitting and approving offerings moved **no** delivered-activity number and **no** KPI
number. The distinction from §28.10 holds: offering = available, BookingRequest = requested, Agenda =
delivered. No KPI definition was touched. The two pre-existing conflations recorded in §27.10
(`totalCapacity` feeding the attendance-rate denominator; `completedActivities` counting catalogue
rows) are unchanged and still not silently corrected.

**Development seeder, accounted for explicitly.** `DbSeeder.cs:692` manufactures attendance data for
any confirmed booking that has an `ActivityId`. Metrics were snapshotted *after* startup so the
baseline already included its effect, and the one restart mid-run was measured on both sides: it added
exactly 1 attendance session, 2 records and 2 certificates, all attached to the confirmed test booking,
and moved delivered-agenda and KPI by 0. The seeder was not modified.

### 29.12 Test data removed

8 activities, 1 booking request, 1 agenda entry, 2 booking audit trails, 18 notifications, 55
notification deliveries, 27 system audit log rows, plus the seeder's 1 attendance session, 2 records
and 2 certificates. Rows were selected from the test activity ids and what hangs off them — never by
date or by "recent".

Every metric returned to its exact pre-test value: **59 activities, all Approved, 0 with a null
approval state, agenda 11 (10 delivered), bookings 15, KPI 14, attendance sessions 6, records 8,
certificates 8, notifications 14, deliveries 25, audit logs 9.** Six referential checks all returned
zero orphans, and no QA-stamped row remains.

### 29.13 Build and migration

`dotnet build --no-incremental` → **0 errors, 1 warning** — the retained `CS0108` on
`Activity.CreatedByUserId`, which is deliberate and was not suppressed. An intermediate build raised
four `CS8714` warnings from a nullable dictionary key; those were fixed rather than left.

**No migration was required.** Every change in this section is presentation or query-filter only; no
entity, no column and no index changed. Nothing was scaffolded.


---

## 30. Admin Shell Accessibility & Mobile Hardening (2026-09-08)

Two platform-wide defects recorded at the end of §29 as pre-existing and out of scope there. This
section fixes both in the shared admin shell, so every page in the Admin area benefits rather than
one. **No business behaviour changed**: no controller, no C# file, no authorization attribute, no
query and no migration. The ten changed files are the admin layout, four admin views, three
stylesheets and `admin.js`.

Inspecting the shell first corrected two beliefs recorded in §29.9.

**The sidebar did collapse.** `ghars-theme.css` already turned it into a fixed off-canvas panel below
992px. What it lacked was everything around that: no backdrop, no Escape, no focus handling, no
`aria-controls`, no `aria-expanded`, an English-only `aria-label` on an icon-only button, and no way
to dismiss it other than the toggle.

**The overflow was never the sidebar.** Two rules in the same stylesheet listed
`.table-responsive` twice, and the second reset it to `overflow: visible`:

```css
body.admin-shell .admin-page .table-responsive { overflow-x: auto; }
body.admin-shell .admin-page .card,
body.admin-shell .admin-page .table,
body.admin-shell .admin-page .table-responsive { overflow: visible; }   /* wins -- same specificity, later */
```

So wide tables were not scrolling inside their wrapper; they spilled onto the page. And because
`body.admin-shell` sets `overflow-x: hidden`, which propagates to the viewport, the spilled columns
were **clipped with no scrollbar** — unreachable rather than merely awkward. This is why KPI
(520px) and Reports (485px) overflowed at 390px, and why both still overflowed at 1024px, a width
where the sidebar is a normal static column and cannot be the cause.

### 30.1 Mobile sidebar

The sidebar is now Bootstrap's own responsive off-canvas rather than a hand-rolled imitation:
`class="admin-sidebar offcanvas-lg offcanvas-start"`, toggled by `data-bs-toggle="offcanvas"`.
Bootstrap therefore owns the backdrop, Escape, focus trapping, focus restoration, body scroll lock
and the transition. The hand-written transform/`.open` block was deleted.

Bootstrap does two things above the breakpoint that had to be reconciled: it forces
`background-color: transparent !important` on the element and turns `.offcanvas-body` into a flex
row. Both are overridden for `.admin-sidebar` so the desktop column keeps its charcoal background
and its original block flow. The desktop layout rules (`width: 280px`, `position: sticky`,
`height: 100vh`) are now scoped to `@media (min-width: 992px)`, since below that Bootstrap positions
the element.

Also removed: a `@media (max-width: 992px)` block that shrank the sidebar to an 86px icon rail. It
never applied — a later, more specific `body.admin-shell .admin-sidebar` rule overrode its width —
and it would have conflicted with the drawer.

### 30.2 Toggle and dismissal

A real `<button>`, keyboard operable by default, carrying `aria-controls="adminSidebar"` and an
`aria-expanded` that is kept in sync from the `show.bs.offcanvas` / `hidden.bs.offcanvas` events —
Bootstrap does not maintain it for offcanvas. It no longer relies on the icon: the icon is
`aria-hidden` and the accessible name comes from visible text, **Menu / القائمة**. The drawer has an
`.offcanvas-header` with a titled heading and a close button labelled *Close menu / إغلاق القائمة*,
both hidden automatically from `lg` up.

The only other script added closes the drawer when the viewport grows past the breakpoint; without
it a drawer opened on a phone and then widened leaves a backdrop and a locked body behind.

Keyboard focus is now visible on the shell's own controls (`3px` teal outline on `:focus-visible`
for sidebar and topbar links and buttons).

### 30.3 RTL

The drawer opens from the side the language reads from. Rather than mirroring Bootstrap's transforms
in `admin.rtl.css` — which needs a matching override for the shown state or the drawer never appears
— the layout picks the class: `offcanvas-end` under RTL, `offcanvas-start` otherwise. Bootstrap's own
rules then anchor and animate it correctly with no custom CSS at all.

Measured at 390px in Arabic, the open drawer occupies x 110–390, i.e. flush to the right edge and
fully on screen; in English, x 0–280. The toggle sits in the topbar's leading group, so it too moves
to the right under RTL, on the same side as the drawer.

`admin.rtl.css` previously held a border rule for `.sidebar`, a class the admin layout does not use,
so it never applied. It was **not** restored: the LTR sidebar has no matching border, and adding one
only in Arabic would make the two languages differ for no reason.

### 30.4 Filter bar accessibility (`admin.js`)

The injected filter panel already rendered visible `<label>` text, but no label was bound to any
control — six unnamed form controls on every admin list page.

Every control now has a deterministic unique id (`ghars-filter-<n>-<key>`, from a module-level
counter so a second initialisation cannot duplicate an id) and a `<label for>` that matches exactly
one control.

The labels themselves were also wrong, not just unbound. The panel is generic and cannot know a
page's business vocabulary, yet it labelled column 0 "Main entity" and column 2 "Type / Category"
whatever those columns actually held — on Bookings, "Main entity" was a dropdown of numeric row IDs.
Each column filter is now named from **the table's own `<thead>` cell**, which is metadata already
present in the markup and already localized by the view. A column is dropped entirely when it cannot
be filtered honestly:

| Dropped when | Why |
| --- | --- |
| header cell is blank | no accessible name available (action columns) |
| fewer than two distinct values | selecting the only value filters nothing |
| every value is numeric | an id or count column is not a filter |

The visible effect: Bookings offers *Search, Status, Activity, From, To* instead of *Keyword, Status,
Type / Category, Main entity, From, To*; the ID dropdown is gone. Library, whose columns are ID / Name
/ Sort, correctly offers only *Search, From, To*. The approval queue drops its Status filter because
every queued item has the same status.

This also satisfies the Arabic requirement through the existing localization approach rather than a
new one: on a bilingual admin view the headers are Arabic, so the filter labels are Arabic
(`الموسم` on KPI Review). The static labels use the `label(en, ar)` helper already in the file.

Two smaller fixes in the same file: the panel is now inserted **outside** the table's horizontal
scroll container — with scrolling restored it would otherwise have scrolled away and lost width — and
the dead sidebar toggle handler was removed, since it queried `.sidebar` and never ran.

### 30.5 Page-level filter forms

The shell fix left 24 unnamed controls per language on four pages whose own filter forms had
`<label>` elements with no `for`. Those were bound mechanically — ids and `for` attributes only, no
text or layout change — on Admin Dashboard (7), KPI Review (3) and Reports Analytics (10). Admin
Gallery (4) had no labels at all, only a placeholder, so it received `visually-hidden` labels, which
give an accessible name without altering the row.

The Activities filter row's Filter/Reset pair was given `flex-wrap`; it was 4px wider than its
column at 1024px.

### 30.6 A second, subtler overflow source

With table scrolling restored, Admin Activities still overflowed by 105px at 390px, and nothing
inside `.admin-page` appeared to be over-wide. The cause was
`<span class="visually-hidden">Actions</span>` in a table header. Bootstrap declares
`.visually-hidden:not(caption) { position: absolute !important }`, and `.table-responsive` was
`position: static`, so the span's containing block was not the scroll wrapper and it escaped the
clip — a 1px element sitting at x = 494 pushing the page 105px wide.

`position: relative` on the wrapper makes it a containing block, so absolutely positioned descendants
are clipped with everything else. This protects any future screen-reader label inside a wide table.

### 30.7 Verification

Chrome, 1440 / 1024 / 768 / 390px, English and Arabic.

**Page-level horizontal overflow** (`documentElement.scrollWidth − clientWidth`):

| | 1440 | 1024 | 768 | 390 |
| --- | --- | --- | --- | --- |
| Worst admin page, before | 0px | 166px | 142px | **520px** |
| Worst admin page, after | 0px | 0px | 0px | **0px** |

Sixteen admin pages — Dashboard, Activities, Approval queue, Bookings, KPI, Reports, Organizations,
External Surveys, Gallery, Library, Attendance, Certificates, Surveys, News, Notifications, Contact
Messages — at four widths in both languages: 128 page loads, every one HTTP 200 with **0px** overflow,
**0** unnamed controls, **0** duplicate ids and no badge without text. Wide tables scroll inside their
own wrapper, as before; no admin table was converted to cards.

**Accessible names**, at 1440px across ten list pages in both languages: **156 unnamed controls
before, 0 after**; duplicate ids 0 before and after; every `label[for]` resolves to exactly one
control.

**Drawer behaviour**, 23 checks per language at 390px, all passing: hidden at load with no backdrop
and no overflow; Tab reaches the toggle and it shows a visible focus ring; Enter opens it;
`aria-expanded` flips to `true`; the backdrop appears; the drawer is fully on screen, on the reading
side; opening adds no page overflow; focus moves into the drawer; all 16 nav links stay keyboard
reachable and the active item stays marked; Escape closes it and returns focus to the toggle; the
backdrop click and the in-drawer close button both close it; and resizing to desktop leaves no
backdrop and no scroll lock.

**Desktop regression.** The pre-change build was rebuilt from a stash and measured, then compared
element by element against the post-change build — sidebar, brand, logo, nav container, active link,
footer, content, topbar and page boxes plus all 16 nav-link rectangles, on three pages at 1440px and
1024px in both languages. **132 of 132 measurements identical, 0 differing.** The desktop admin
layout is unchanged.

**Public site.** All shell rules are scoped to `body.admin-shell`, `.admin-sidebar` or
`.admin-topbar`; public pages were checked at 1440px and 390px in both languages and are unaffected.

**Security.** Presentation only: `git diff --name-only` lists four admin views, the admin layout,
three stylesheets and `admin.js`. **No C# file changed** — no controller, no `[Authorize]` attribute,
no role check, no organization filter, no server-side query. Sidebar visibility is not authorization,
and the Users link remains rendered only for Super Admin as before.

`dotnet build --no-incremental` → **0 errors, 1 warning**, the retained `CS0108` on
`Activity.CreatedByUserId`, not suppressed. **No migration required** and none created.

---

## 31. Ghars Annual Report & Ghars Channel (2026-09-08)

Two approved business requirements, implemented together because both touch the same media/reporting
surfaces. The design decisions were taken and written down *before* any schema change, in
[`GHARS_ANNUAL_REPORT_CHANNEL_DESIGN.md`](GHARS_ANNUAL_REPORT_CHANNEL_DESIGN.md); this section records
what was built and how it was verified.

**Business source for Part 1:** `docs/Ghars_Clubs Report Form.docx`, the official Arabic club report
form. It was read in full before implementation; its four sections and exact terminology are reproduced
in §1.1 of the design note and are used verbatim in the electronic form, the review screen and the
printed output.

---

### 31.1 Annual Report — architecture

A dedicated entity, `GharsAnnualReport`, rather than an extension of `KpiSubmission`. The two records
have different lifecycles: a KPI submission stays live, while an annual report must **freeze** on
approval. Overloading one row with two "approved" meanings would have made both harder to reason
about. This follows the precedent set by `OfferingApprovalStatus`, which was created rather than
widening `ActivityStatus` for the same reason.

`AnnualReportStatus` mirrors `KpiSubmissionStatus`'s numeric layout (Draft=1, Submitted=2, Approved=3,
Rejected=4) and names the returned state `ReturnedForCorrection=5`. A **unique index on
(OrganizationId, SeasonId)** enforces one report per club per season in the database, not only in
validation — the same guarantee `KpiSubmission` already has.

**Club identity is server-derived.** Resolved from `OrganizationAdminLink` on every request. There is
no club dropdown, no hidden club id and no editable `OrganizationId` anywhere on the club-facing form;
the club name renders read-only. Verified in the browser: 0 club selectors and 0 hidden organization
fields on the form.

### 31.2 Template mapping

| Template field (Arabic) | Class | Source |
| --- | --- | --- |
| اسم النادي | SYSTEM-DERIVED | `Organization` via `OrganizationAdminLink`; read-only |
| المسؤول عن برنامج "غرس" | MIXED / CONFIRMED | pre-filled from the club's primary `OrganizationContact`, editable, stored as a season snapshot |
| رقم التواصل | MIXED / CONFIRMED | same, falling back to the organization's own phone |
| البريد الإلكتروني | MIXED / CONFIRMED | same, falling back to the organization's own email |
| عدد المحاضرات المقترحة من النادي | SYSTEM-DERIVED | delivered agenda entries not sourced from the DSC catalogue |
| عدد المحاضرات المقترحة من مجلس دبي الرياضي | SYSTEM-DERIVED | delivered agenda entries booked from the DSC catalogue |
| إجمالي المحاضرات المنفذة | SYSTEM-DERIVED | count of delivered agenda entries |
| عدد المحاضرين | SYSTEM-DERIVED | distinct normalised `AgendaEntry.LecturerName` |
| عدد الجهات المنفذة / المشاركة | SYSTEM-DERIVED | distinct normalised `DepartmentOrOrganization`, club itself excluded |
| إجمالي عدد المشاركين | SYSTEM-DERIVED | `SUM(AgendaEntry.NumberOfParticipants)` |
| تفاصيل المحاضرات والبرامج المنفذة | SYSTEM-GENERATED | one row per delivered agenda entry; read-only |
| أبرز النتائج / التحديات / المقترحات | CLUB-SUBMITTED | free text, stored in the language typed |

The paper form stops at twenty detail rows; the electronic version does not.

### 31.3 Derived statistics — one query, two surfaces

`Helpers/SeasonClubStatistics.cs` now holds **the** delivered-agenda query, and both
`Public/KpiController` and the Annual Report call it. `GetDerivedAsync` in the KPI controller was
replaced by a call into the helper. Two copies of this logic is precisely how an annual report comes to
report 12 activities while the season KPI reports 11.

"Delivered" means `AgendaEntryStatus.Submitted` or `Approved` — the rule the KPI derivation has always
used. Published partner offerings, unconfirmed bookings, draft agenda rows and the Activity catalogue
are all excluded: none of them is evidence that anything was delivered.

One refinement was made inside the shared helper: an implementing entity whose name equals the club's
own name is not counted as an implementing entity. This was **verified against the data before being
added** — no agenda row has `DepartmentOrOrganization` equal to its club's `NameEn` or `NameAr` — so it
changes no stored KPI value and only prevents a wrong count in future data.

Lecturer and entity uniqueness is by **normalised free-text name** (trimmed, case-insensitive). Both
fields are free text on `AgendaEntry` and there is no master-person or master-entity relationship to
prefer; the same lecturer across three activities therefore counts once, which is what "number of
lecturers" asks for.

**Club-proposed vs Council-proposed** is derived from the booking link that already exists on
`AgendaEntry.BookingRequestId`:

| Situation | Counts as | Why |
| --- | --- | --- |
| booking with an `ActivityId` | **Council** | selected from the DSC-managed catalogue — a DSC-created activity, or a partner offering that only became visible because DSC approved it |
| booking with no `ActivityId` | **Club** | a Request Custom Booking, whose subject the club wrote |
| no booking at all | **Club** | an activity the club recorded directly in the Agenda |

This is **not applied silently**: the rule is stated on the report screen and the origin of every
single delivered activity appears in its own column of the section-3 table, so the club can see exactly
which activities produced each number.

*Historical limitation, stated in the UI as well as here:* agenda rows created before bookings were
linked to the Agenda carry no `BookingRequestId` and are therefore counted as club-proposed. On the
development database that is 8 of 10 delivered entries. It is the correct default — an activity with no
catalogue booking behind it was not proposed by the Council — but it cannot distinguish "the club's own
initiative" from "a legacy row whose origin was never recorded".

### 31.4 Snapshot / data freeze

| State | Derived values |
| --- | --- |
| Draft / ReturnedForCorrection / Rejected | recomputed from live Agenda on every open and save |
| Submitted / Approved | the snapshot taken at submission; never recomputed |

The snapshot is captured in one place (`AnnualReportSnapshot.Capture`, called from a single point in
the club controller) so create-and-submit and edit-and-submit cannot freeze different things. It stores
the six section-2 totals in columns and the section-3 table as JSON, each row keeping its
`AgendaEntryId` so an authorised reader still gets a link back to the live entry.

**Why JSON in one column rather than child rows.** The requirement is to preserve exactly what was
submitted, not to make the detail queryable — nothing reports across annual-report detail rows, and the
live Agenda remains the queryable source. A child table would have added an entity, a delete path and a
second copy of the agenda dataset for no analytical gain.

**Verified.** With report 3 approved, the live Agenda was changed (entry 1: 45 → 999 participants, plus
a new delivered entry), taking the live figures to 5 activities / 1,276 participants. The approved
report continued to report **4 activities / 245 participants** with its original detail rows, including
the original 45. The Agenda was then restored. Narrative text lives in its own columns and is never
touched by a snapshot refresh, so refreshing derived data cannot lose what the club typed.

### 31.5 Relationship to KPI

One-directional. The Annual Report **consumes** the same derivation KPI uses; it never writes to
`KpiSubmission`, and `KpiSubmission` never reads the Annual Report — no circular dependency.

Where an **Approved** KPI submission exists for the same club and season and its stored figure differs
from the agenda-derived figure, the report shows both, each labelled with its source and status, rather
than silently overriding either. (This occurs on demo data, where several KPI rows were seeded with
figures that were never agenda-derived.)

### 31.6 Workflows

**Club:** `/annual-report` lists every season with its status; `/annual-report/edit/{seasonId}` opens
the form. A GET creates nothing — the row is written on first save, so opening a report has no side
effects. Save Draft → reopen → Submit. Editable states are Draft, ReturnedForCorrection and Rejected;
the rule is re-checked on POST, not only on GET, so a form rendered while the report was editable
cannot still be posted after DSC has taken it into review.

**Submission validation.** Club approved, season valid, contact details present, all three narrative
answers present, no duplicate report. Missing *derived* data is a **warning, never a blocker** — a club
with an unusual season can still file its narrative report, and the screen says what is missing.

**DSC:** `/Admin/AnnualReports` is a filtered queue (season, club, status) with submitted reports
first, showing club, season, submitted date, status and reviewer. `/Admin/AnnualReports/Details/{id}`
renders the same report body the club sees, plus Approve / Return for correction / Reject with notes,
the submission provenance and a history timeline read from the audit log. Decisions are refused unless
the report is actually under review. Deliberately the same interaction as the KPI and partner-offering
review screens rather than a third style.

### 31.7 Export

An HTML print view (`/annual-report/print/{id}` and the DSC equivalent) with `Layout = null`, Ghars
branding, a print stylesheet and page-break rules. It follows the template's structure — title, sports
season, then the four sections in order — and is deliberately not a pixel copy of the Word file.

Export is the browser's own Print-to-PDF, which shapes Arabic correctly using the reader's system
fonts. Generating the PDF server-side with QuestPDF would have meant embedding an Arabic-shaping font
for this one document; the print path gives a correct bilingual result with no such dependency.

The print action is **not** audited. The requirement asks for export auditing only where it is already
a project pattern, and it is not one here; more to the point a print view is a *render*, not a
download, so auditing it would write a row every time someone opened the preview. (An earlier iteration
did audit it and produced 12 rows from a handful of test renders — the reason it was removed.)

### 31.8 Ghars Channel — architecture

`Media`/`Gallery` becomes **Ghars Channel / قناة غرس** in user-facing text only. Controllers, tables,
DbSets and view folders keep their names; `/gallery` keeps working and `/channel` is added as the
primary route, so existing links and bookmarks do not break.

`GalleryItem` is **extended, not forked**. It was already the aggregation point — `AgendaController`
writes an `AgendaMedia` row and a `GalleryItem` row from one uploaded file, and both the public and
admin gallery already read it. Eight nullable columns were added: `ChannelCategory`, `ApprovalStatus`,
the submit/review stamps, `ReviewNotes` and `LibraryItemId`. Every pre-existing row stays valid with
`ApprovalStatus = null`, which means "outside the implementing-entity review workflow".

`MediaAlbum` / `MediaItem` (curated albums) and `AgendaMedia` are untouched.

### 31.9 Two streams, two moderation models

**Club activity media — unchanged.** Uploaded through an Agenda entry, stored once, surfacing in the
channel automatically with `IsPublished = true` and `ApprovalStatus = null`. `IsPublished = false`
still means *a DSC takedown*, not a draft. No second submission workflow is imposed on a club photo.

A second club entry point was added — "Add activity media" in the channel — but it **requires an agenda
entry** and writes exactly the same rows through `Helpers/AgendaMediaPublisher.cs`, which both paths now
call. It cannot create disconnected club media with no activity context.

**Partner / government content — gated.** Draft → SubmittedForApproval → Approved, with Returned,
Rejected and Unpublished. A partner row is created with `IsPublished = false`; **only a DSC approval
sets it true**. Visibility therefore requires *both* fields to agree —
`IsPublished && (ApprovalStatus == null || ApprovalStatus == Approved)` — the same two-field rule the
offering catalogue uses, centralised as `ChannelWorkflow.PubliclyVisible` so no query can implement
half of it.

Partner content management lives at `/partner/channel` — **"My Ghars Channel Content" / "محتواي في قناة
غرس"** — deliberately separate from `/partner/programs`. A program is a bookable offering; channel
content is published material.

**No new role.** `OrganizationType.GovernmentAuthority` and `OtherPartner` are both administered by
Partner Admin, exactly as `PartnerProgramsController` already treats them. There is no permission gap a
`GovernmentEntityAdmin` role would close.

### 31.10 Distinction from the Digital Library

| | Ghars Channel | Digital Library |
| --- | --- | --- |
| Content | photos, videos, awareness clips, activity coverage | booklets, publications, structured documents |
| Uploads accepted | images and video only | PDF and video |

**The channel accepts no documents.** Verified in the browser: the upload control offers
`.jpg,.jpeg,.png,.gif,.webp,.mp4,.webm,.mov` and nothing else. A partner whose material is a booklet
links their existing Library publication via `LibraryItemId` instead — nothing is copied, and the link
is validated to be a published item issued by that entity. The channel page also carries an explicit
pointer to the Digital Library so the two are not confused.

Partner vocabulary is constrained to what is theirs to claim: media types **Photo, Video** (not
"official photo" or "press coverage") and categories **Awareness, Educational** (not "club activity" or
"press"). Both verified in the rendered form.

### 31.11 File access

Partner uploads go to `wwwroot/uploads/channel`, added to the denied static prefixes in `Program.cs`
alongside `/uploads/agenda`. `/protected-files/gallery/{id}` now enforces
`ChannelWorkflow.IsPubliclyVisible` — exactly the rule the listing pages apply — instead of checking
`IsPublished` alone.

Verified end to end: a partner **draft**, a **submitted** item and a **withdrawn** item all return
`404` anonymously while their id is known; the owning entity gets `200` for its own draft; an
**approved** item returns `200` anonymously; a club photo **hidden by DSC** returns `404` anonymously
but `200` to the owning club; and `GET /uploads/channel/` returns `404`.

### 31.12 Reporting isolation

Channel uploads are evidence, not delivery. After the full end-to-end run — which created club media,
partner content and a complete report lifecycle — every business metric was **exactly at its
pre-test baseline**: agenda 11 / 10 delivered, bookings 15, KPI 14, attendance 6 sessions / 8 records,
certificates 8, activities 59. The Annual Report's totals and detail come from `AgendaEntry` alone.

### 31.13 Notifications and audit

Existing infrastructure, existing conventions. Club submits → role-targeted notification to DSC Admin
**and Super Admin** (so a site with no DSC Admin cannot lose a submission). DSC decides →
organization-targeted notification to the reporting club only. Partner submits channel content → DSC
reviewers; DSC decides → that entity only. No unrelated organization is addressed, and a club photo
upload generates nothing.

Audited via `SystemAuditLog`: report created, draft updated, submitted, resubmitted, returned,
approved, rejected; channel content created, updated, submitted, resubmitted, approved, returned,
rejected, withdrawn, hidden and republished by DSC, and club media added.

### 31.14 Security

Every query is scoped by the caller's `OrganizationAdminLink` set, and a miss returns `NotFound()` —
non-disclosing, consistent with the rest of the platform.

| Test | Result |
| --- | --- |
| Club B views Club A's report | 404 |
| Club B prints Club A's report | 404 |
| Club B opens "its own" season report | 200, scoped to Al Nasr Club |
| Anonymous opens a report | 302 to login |
| DSC cross-club review | 200 (retained) |
| Partner B views Partner A's content | 404 |
| Partner B edits Partner A's content | 404 |
| Partner B **forges a POST** to submit Partner A's content | 404 |
| Partner B **forges a POST** to withdraw Partner A's content | 404 |

The forged-POST cases matter: hiding an action in the UI is not authorisation, so both were checked at
the endpoint.

### 31.15 Verification

| Suite | Result |
| --- | --- |
| Annual Report lifecycle (the 14 required steps) | **44 checks, all passing** |
| Cross-club security + channel end-to-end (club and partner) | **41 checks, all passing** |
| Snapshot freeze against a post-approval Agenda change | **identical before and after** |
| Regression / Arabic / mobile / accessibility sweep | **244 page loads, 0 failures** |

The sweep covers 30 pages across anonymous, club, partner and DSC roles at **1440 / 1024 / 768 / 390px
in English and Arabic**, checking HTTP 200, zero page-level horizontal overflow, zero unnamed form
controls, zero duplicate ids, no badge without text, every image carrying `alt`, every data table
carrying a header cell, every button having an accessible name, and `dir` matching the language.

All six approved Arabic template terms — `التقرير السنوي لغرس`, `الموسم الرياضي`, `البيانات الأساسية`,
`المؤشرات الإجمالية لتنفيذ البرنامج`, `تفاصيل المحاضرات والبرامج المنفذة`, `أبرز النتائج والملاحظات` —
were asserted present on the rendered Arabic report.

Three defects found by the sweep were fixed rather than explained away:

1. **The injected admin filter bar treated the annual report as a list.** `wwwroot/js/admin.js` adds a
   keyword/column filter above the first table on any admin page; on the review screen that offered to
   *hide rows of an official report*. Tables can now opt out with `data-filterable="false"`, and the
   filter picks the first table that has not, so no other admin page changes.
2. **4px page overflow at 1024px in Arabic** on the review screen — a contact email is one long
   unbreakable token in a flex row, and a flex item cannot shrink below its content. Fixed with
   `text-break` on the value side.
3. **Toast dismiss buttons had no accessible name** (`Views/Shared/_Toasts.cshtml`) — icon-only buttons
   announced as "button". Given a bilingual `aria-label`; the icons are now `aria-hidden`.

Four pages carry pre-existing unbound filter labels that this work did not introduce and did not touch
— Club Dashboard (6 controls), Agenda (2), KPI create (19), Custom booking (2). Verified unchanged
before and after (`Views/Kpi`, `Views/Agenda` and `Views/Bookings` are untouched by this change set);
they are recorded here rather than silently absorbed.

### 31.16 Migration

**One migration, additive and non-destructive:**
`20260908074525_AddAnnualReportAndGharsChannel`.

* New table `GharsAnnualReports` (27 columns), unique index on `(OrganizationId, SeasonId)`, review-queue
  index on `(Status, SubmittedAtUtc)`, `NoAction` on both parent FKs so an approved report is never
  cascade-deleted.
* Eight nullable columns on `GalleryItems`, plus `IX_GalleryItems_ApprovalStatus_SubmittedAtUtc` and the
  `LibraryItemId` FK (`NoAction`).

No column is dropped, renamed or made non-nullable, and no historical migration was edited. Verified
from `INFORMATION_SCHEMA` after applying: all 27 columns present with the intended nullability, all
indexes created, and **all 9 pre-existing gallery rows preserved with `ApprovalStatus = NULL` and
`IsPublished = 1`** — the intended backwards-compatible default.

`dotnet build --no-incremental` → **0 errors, 1 warning**, the retained `CS0108` on
`Activity.CreatedByUserId`, not suppressed.

---

## 32. Program Attachments & Header Navigation (2026-09-08)

Two reported gaps, unrelated to each other: an implementing entity had no way to attach documents to a
program it was creating, and after signing in some header controls could not be reached.

### 32.1 Supporting documents on a partner offering

**The gap.** `New Program` collected title, description, audience, capacity, schedule and availability,
but nothing else. A partner with a programme outline, brochure, session plan or trainer profile had
nowhere to put it, so the material reached DSC — and the clubs deciding whether to request the
programme — outside the platform or not at all.

**The record.** A new `ActivityAttachment` entity, shaped after `KpiDocument` because it is the same
kind of record: a row that owns a stored file. It hangs off `Activity` rather than off a booking,
because the same document answers the same question for every club that considers the programme.

There is deliberately **no bilingual title**. The label a reader sees is the uploaded file's own name,
which the partner already controls and already writes in whichever language the document is in.
Requiring a translated caption per attachment would be entry work with no business rule behind it.
`OriginalFileName` is display metadata only — the name on disk is always a GUID.

**Storage.** `ProtectedFileStore` gains a `programs` category, so the files live at
`<ContentRoot>/protected-uploads/programs/` — outside `wwwroot`, unreachable by the static-file
middleware. These rows are born protected: none ever carried a legacy `/uploads/...` path, so
`ProtectedFileMigrator` has nothing to migrate and deliberately does not walk the category.

**Accepted types.** A dedicated `FileValidationHelper.ProgramAttachment` profile rather than a reuse of
`Evidence`: `.pdf .doc .docx .ppt .pptx .xls .xlsx .png .jpg .jpeg`, 25 MB each, 10 per programme.
Presentations are included because a programme outline routinely is one. The two profiles answer to
different business rules, and changing what counts as KPI evidence must not silently change what a
partner may publish alongside a bookable programme.

**Visibility — the rule, in one place.** `/protected-files/program-attachment/{id}` re-derives who may
read each file from the offering's own state:

| Reader | While Draft / Submitted / Returned / Rejected / Unpublished | Once Approved + Published |
|---|---|---|
| The owning implementing entity | Yes — it can check what it uploaded | Yes |
| DSC Admin / Super Admin | Yes — the approval decision rests on this material | Yes |
| Any other signed-in user (a club) | **404** | Yes |
| Anonymous | **404** (never served) | **404** (never served) |

The availability window is deliberately *not* part of this test, unlike
`BookingsController.BookableOfferings()`. That window governs whether a club may still *request* the
programme; a club that already booked it must not lose the session plan the day the window closes.
Publication and approval are the confidentiality question, and those are what this checks.

**Lifecycle.** Attachments are optional at every stage, including submission — a programme that needs
no brochure is not an incomplete programme, so nothing here can block sending it for approval. They may
be added or removed exactly while the rest of the form is editable (`OfferingWorkflow.PartnerCanEdit`),
re-checked on POST and not merely hidden in the view. Removal is a checkbox that takes effect on save,
not an instant delete, so a partner who changes their mind simply does not save. Every removal id is
re-scoped to the offering before anything is deleted.

Write order is deliberate in both directions: on upload the **file** is written before the row, so a
failed write cannot leave a row pointing at nothing; on removal the **row** goes before the file,
because an orphaned file is recoverable housekeeping whereas a row pointing at a deleted file is a
broken download nobody can explain. Admin hard-delete of an activity collects the storage keys *before*
the cascade removes the rows, then deletes the files — a cascade reaches rows, never the file system.

Both add and remove are recorded through the existing audit trail
(`OfferingAttachmentsAdded` / `OfferingAttachmentsRemoved`) and narrated by `OfferingWorkflow`, so they
appear in the workflow history the partner and DSC already read. No new audit system.

**Where they surface.** One shared partial, `Views/Shared/_ProgramAttachments.cshtml`, renders the list
on the partner's details page, the DSC review screen and the club's booking form, so the three cannot
drift into describing the same file differently. From `Areas/Admin` it is referenced by full path — a
bare partial name resolves against the area's own Views folder and is not found.

> Razor gotcha worth keeping: inside a view, a bare `Path` is `RazorPage.Path` (the view's own
> location), which shadows `System.IO.Path` and does not compile. The partial spells the type in full.

**Verification — 33 checks, 0 failures.** The form carries `enctype="multipart/form-data"` (without it
the browser posts file names and no content, and the upload fails silently); a `.txt` is refused with a
bilingual message naming the type and nothing is created; a draft's documents return 200 for the owner
and DSC and 404 for another entity, for a club and for an anonymous caller; the download is the actual
uploaded bytes with `nosniff` and the original name in `Content-Disposition`; after approval the club
can read them and sees them on the booking form; removing one leaves the other downloadable, returns
404 for the removed id, and **deletes the file from disk** (verified by comparing the folder against the
table).

### 32.2 Header navigation — links that were clipped, not merely cramped

**The symptom, measured.** A signed-in partner had ten top-level links and a club eleven, beside a 96px
brand logo and four action controls. `.navbar-nav` is a flex row that defaults to `flex-wrap: nowrap`
and `.ghars-main-nav .nav-link` is `white-space: nowrap`, so the row could not shrink and overflowed its
container — and because `html, body { overflow-x: hidden }`, the excess was **clipped with no
scrollbar**.

Reproducing the old geometry in the live page and measuring per-element bounding boxes:

| Width | Partner — controls off-screen | Club — controls off-screen |
|---|---|---|
| 1920 | 0 | 0 |
| 1600 | 1 (user menu) | 0 |
| 1440 | 2 (bell, user menu) | 1 (user menu) |
| 1366 | 1 (user menu) | 1 (user menu) |
| 1280 | 3 (Partner button, bell, user menu) | 1 (user menu) |
| 1200 | 4 (language switch, Partner button, bell, user menu) | 1 (user menu) |

What fell off was the **right-hand action strip**, not the nav links — at 1366px, a very ordinary laptop
width, the user menu was unreachable and with it **Logout**.

This is worth recording as a measurement lesson: `documentElement.scrollWidth - clientWidth` reported
**0px** throughout, because `overflow-x: hidden` clips the overflow away. The page-overflow metric that
found the admin-shell defects (§30) cannot see this one. Only a per-element bounding-box check against
`window.innerWidth` catches a control pushed out of a clipped container.

**The fix, in two parts.**

1. *Structure.* The links that belong to the signed-in person — their own dashboard, their own
   programmes, their own club's returns — collapse into one **My Workspace** dropdown, grouped under a
   heading naming whose work each group is (`Implementing Entity`, `My Club`, `Administration`). The
   public spine (Home, About Us, Vision & Objectives, Booking, Contact) stays at the top level, so the
   navigation no longer reshapes itself when someone signs in.

   **Ghars content stays at the top level** — Ghars Channel, Digital Library and Surveys are published
   by the Ghars team for everyone, not work belonging to the person signed in, and filing them under a
   personal-workspace heading would misdescribe them.

   No visibility rule moved and no destination was added or removed: the same links appear under
   exactly the same role conditions. A divider is emitted only *between* two groups, so a user holding
   one role does not get a rule dangling under the last item.

2. *Safety net.* `.ghars-main-nav` gains `flex-wrap: wrap` and `min-width: 0`, so any future overflow
   wraps onto a second line **in view** instead of being swallowed. A narrow band rule
   (`1400–1599.98px`) tightens link padding only, leaving the logo and header height alone, so a club's
   nine items fit on one row down to 1366px.

**Verification — 239 checks, 0 failures.** Four roles (anonymous, partner, club, DSC) × two languages ×
nine widths from 1920 to 390: **zero page overflow and zero header controls outside the viewport**,
against 1–4 clipped controls before. Every destination that used to be a top-level link is still
reachable from the header for the same role, the dropdown opens, and its toggle maintains
`aria-expanded`. A separate sweep loaded 74 pages across all roles and both languages with no error
page and no off-screen control.

Below 1400px the nav wraps to two rows at 1280 and 1200 rather than clipping — the header height does
not grow, and nothing is hidden. That is the intended behaviour: at those widths there genuinely is not
room for nine links plus the action strip, and wrapping is the honest outcome.

### 32.3 Migration and build

`20260908092255_AddActivityAttachments` — purely additive: one new table `ActivityAttachments` with a
cascade FK to `Activities` and an index on `ActivityId`. Nothing existing is dropped, renamed or made
non-nullable, and no historical migration was edited. Verified from `INFORMATION_SCHEMA` after applying:
9 columns with the intended nullability, primary key and index present, and all 59 existing activities
untouched.

> Applying it hit the trap the README documents: `dotnet ef database update --no-build` reported
> `Done.` while applying **nothing**, because the compiled assembly predated the migration. Build first.

`dotnet build` → **0 errors, 1 warning** — the retained `CS0108` on `Activity.CreatedByUserId`, not
suppressed. Test data was removed afterwards and every business metric returned to baseline: 59
activities, 0 attachments, 11 agenda entries, 14 KPI submissions, 9 gallery items, 9 audit rows, and no
orphaned files in `protected-uploads/programs/`.

`BookingRequests` stands at 16 rather than the 15 recorded earlier in this document. The extra row is
booking 20 against activity 35, created during this work by a person exercising the booking flow — the
test harness only ever renders `/bookings/create` and never posts it. It is ordinary data and was
deliberately left in place.
