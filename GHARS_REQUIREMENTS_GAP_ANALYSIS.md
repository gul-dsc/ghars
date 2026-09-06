# Ghars Platform — Requirements Gap Analysis

Date: 2026-09-06
Scope: All `.docx` documents under `/docs` compared against the current codebase (`handover.md` baseline).
Reviewer: Claude (AI agent), based on a full read of every controller, entity, view, migration and helper in the solution.

Documents reviewed (all 8 files in `/docs`):

| # | File | Language | Content |
|---|------|----------|---------|
| D1 | `About the "Ghars" Programحول برنامج  غرس  .docx` | AR+EN | Program introduction, vision, objectives, scope, implementation mechanisms |
| D2 | `Ghars Lecture & Events Booking System.docx` | AR+EN | Booking business purpose + step-by-step club/entity workflow |
| D3 | `التقارير Reports.docx` | AR+EN | Agenda (actual activity data entry) workflow + end-of-season reporting note |
| D4 | `Photo & Video Gallery.docx` | AR+EN | Automatic gallery aggregation from Agenda + DSC/press uploads |
| D5 | `Digital Library.docx` | AR+EN | Library content rules (PDF-first, cover+link fallback, publisher/date, pagination, Council-managed) |
| D6 | `Data Entry Reports & Statistics .docx` | AR+EN | Club KPI/statistics submission workflow + KPI table 2026–2033 with calculation methods |
| D7 | `Key Performance Indicators (KPIs).docx` | AR+EN | The 10 approved KPI targets |
| D8 | `Surveys.docx` | EN | One line: "Complete bilingual professional survey should be there with all the options and system for survey" |

Status legend: **COMPLETE** / **PARTIAL** / **MISSING** / **CONFLICT**

---

## D1 — About the Ghars Program

### R1.1 Approved introduction / strategic context (DSC Strategic Plan 2025–2033, Dubai Social Agenda 33)
- **Existing implementation**: `Views/Home/About.cshtml` and `Views/Home/Vision.cshtml` contain short, generic, **English-only** placeholder text. No mention of the Strategic Plan 2025–2033 or Social Agenda 33.
- **Status**: MISSING
- **Required change**: Replace About/Vision page content with the approved bilingual wording from D1 (verbatim AR + EN).
- **Module**: Public content. **Controller**: `HomeController` (no logic change). **Entity**: none. **View**: `Home/About.cshtml`, `Home/Vision.cshtml`. **DB impact**: none. **Security**: none. **Org scoping**: none. **AR/EN**: both languages must render with correct direction. **Dashboard/report impact**: none.
- **Notes**: Approved wording must be used verbatim; do not paraphrase.

### R1.2 Program vision, objectives (9), scope, target audiences (Players/Coaches/Administrators/Parents), implementation mechanisms (Educational Lectures / Digital Awareness / Workshops & Competitions)
- **Existing implementation**: Vision page has 4 invented bullet objectives; no mechanisms; no scope/audience statement.
- **Status**: MISSING
- **Required change**: Same views as R1.1 — render vision, 9 objectives, scope of application, and 3 implementation mechanisms bilingually.
- **Notes**: Target audiences already match the enums used elsewhere (`AgendaTargetCategory`, booking audiences) — no data change needed.

---

## D2 — Ghars Lecture & Events Booking System

### R2.1 Club selects Sports Season when creating a booking
- **Existing implementation**: `Public/BookingsController.Create` requires an existing published `Activity`; season comes implicitly from the activity and is shown read-only. There is no season selection and no way to book without a pre-published program.
- **Status**: PARTIAL (season is known, but only via activity anchoring)
- **Required change**: Support season selection (default = active season, inactive seasons excluded) for direct entity requests (see R2.2). Keep read-only season display for program-anchored bookings.
- **Module**: Bookings. **Controller**: `Public/BookingsController`. **Entity**: `BookingRequest` (+ nullable `SeasonId` FK). **View**: `Bookings/Create.cshtml`; VM `BookingCreateVm`. **DB impact**: new nullable column + FK (NoAction). **Security**: server-side validation of season. **AR/EN**: form labels already bilingual; new field must be too. **Dashboard/report impact**: season filters on dashboard already join through Activity; extended to use `booking.SeasonId ?? Activity.SeasonId`.

### R2.2 Club selects the Implementing Entity/Authority from the available list
- **Existing implementation**: The club cannot select an entity directly. The entity is derived from the chosen activity (`Activity.PartnerOrganizationId`, with a creator-based fallback). Clubs can only book pre-published partner programs.
- **Status**: PARTIAL / MISSING (documented flow is entity-first; system is program-first)
- **Required change**: Add a **direct booking request** path: club chooses an approved implementing entity (GovernmentAuthority/OtherPartner, `Status == Approved` only), then enters activity details. `BookingRequest.ActivityId` becomes nullable; `PartnerOrganizationId` is set at creation. The existing program-first path is preserved (it is stronger — capacity, discovery, schedules).
- **DB impact**: `ActivityId` int → nullable; data preserved. **Security**: entity id validated server-side (must be approved partner-type org). **Report impact**: all queries/views that assume `Activity != null` must handle direct requests (most already null-check).
- **Notes**: This is the single largest booking gap. Only valid/approved entities are listed; clubs and unapproved orgs are never shown.

### R2.3 Booking fields: Activity Type (Lecture/Event), Subject, Proposed Date, Time, Target Group (+Others text), Expected Participants (>0), Additional Notes
- **Existing implementation**: All present in `BookingCreateVm`/`BookingRequest` (`RequestedActivityType`, `Subject`, `ProposedStartDateTime/ProposedEndDateTime`, `TargetAudienceCsv` + `OtherTargetAudience`, `RequestedSeats`, `Notes`) with the exact validation rules (end>start, >0 participants, Others⇒text) already enforced in `BookingCreateVm.Validate`.
- **Status**: COMPLETE
- **Notes**: The activity-type list in the form is a superset (Lecture/Event/Workshop/Training/Course). D2 mentions only Lecture/Event; per the reconciliation rule the richer list is preserved (see Conflicts C3). Existing proposed start/end time model is stronger than the doc's single "Time" and is kept.

### R2.4 Submit → automatic notification to the entity coordinator
- **Existing implementation**: On create, an org-targeted `Notification` + per-user `NotificationDelivery` rows are written for all `OrganizationAdminLink` users of the partner org + SignalR broadcast. Reference number (`GHR-BK-yyyy-#####`) and audit-trail entry exist.
- **Status**: COMPLETE
- **Coordinator decision (task §15)**: No dedicated coordinator model exists. **Decision: use the existing `OrganizationAdminLink` relationship as the coordinator target** (all linked partner users are notified). A parallel coordinator-assignment system is not justified by the documents. Documented here as the routing decision.

### R2.5 Entity response — Booking Confirmation including Lecturer Name, Contact Details, Logistical Requirements/Notes
- **Existing implementation**: Partner can Approve (status→Confirmed) with **no ability to record the lecturer name or contact details**. Only free-text `PartnerResponseNotes` exists (and it is only writable on the propose-times path).
- **Status**: PARTIAL
- **Required change**: Add `LecturerName`, `LecturerContact` to `BookingRequest`; partner approval form captures lecturer name, contact and logistics/notes; club sees them on the details page and in the confirmation notification. Confirmed lecturer flows into the auto-created Agenda draft (see R3.4).
- **DB impact**: 2 new nullable columns. **AR/EN**: bilingual labels. **Audit**: values included in the audit-trail JSON.

### R2.6 Entity response — Proposed Modification of date / time / **subject**
- **Existing implementation**: Multiple proposed time options fully supported (`BookingProposedTimeOption`, stronger than doc). **Subject modification is not supported.**
- **Status**: PARTIAL
- **Required change**: Add `ProposedSubject` to `BookingRequest`; partner may include a subject change when proposing modifications; when the club accepts, the subject is applied with before/after values retained in `BookingAuditTrail` (no silent overwrite).
- **Notes**: The multi-option proposed-time workflow is preserved (do not reduce to the doc's simpler single modification).

### R2.7 Club receives notification (Confirmed/Modification) and can approve or reschedule
- **Existing implementation**: Notifications on approve/reject/propose exist; club can accept one proposed option or reject all (`ClubRejectedProposedTimes`), and audit history is kept. "Reschedule" is achievable by the partner re-proposing after a rejection, or the club submitting a new request.
- **Status**: COMPLETE (with the R2.6 subject addition)

### R2.8 Club Admin must not select their own club (task §5/§7 applied to D2's "Select the Club" step)
- **Existing implementation**: The create form shows a club dropdown only when the user is linked to >1 club; with exactly one club, a hidden input + read-only display is used. The posted `OrganizationId` **is validated server-side** against `OrganizationAdminLinks` (`orgIds.Contains`). 
- **Status**: PARTIAL
- **Required change**: When the user has exactly one linked club, the server must force that org id (ignore the posted value entirely), and the read-only club name is displayed. Multi-club users may still pick among **their own** permitted clubs (still server-validated). Same treatment in Agenda and KPI forms (R3.2, R6.1).
- **Security impact**: Removes reliance on the posted hidden field for the common single-club case; IDOR posture unchanged (already validated) but hardened.

---

## D3 — Reports / Agenda (actual delivered activities)

### R3.1 Agenda data-entry fields (season, club, type, subject, date, category+Others, lecturer, department/entity, participants, supporting media, submit, add-new)
- **Existing implementation**: `Public/AgendaController` + `AgendaEntry`/`AgendaMedia` implement every field, including Others-category text, media upload, Draft/Submitted statuses.
- **Status**: COMPLETE (field coverage), PARTIAL (workflow — see below)
- **Notes**: "Others (specify)" for **category** exists (`OtherCategory`). D3's club list also shows "Others (specify)" for club — not applicable: clubs are registered organizations; a free-text club contradicts org-scoping and is intentionally not implemented (see Conflicts C4).

### R3.2 Club auto-derived — no club dropdown for Club Admin
- **Existing implementation**: Dropdown lists only the user's own linked clubs and the POST validates membership; but a dropdown still renders even for single-club users.
- **Status**: PARTIAL
- **Required change**: Single-club users get read-only club context; server forces the org id. Multi-club users select among permitted clubs only.

### R3.3 Others category requires descriptive text
- **Existing implementation**: `OtherCategory` field exists but is **not conditionally required** when Category=Others in `AgendaVm`.
- **Status**: PARTIAL
- **Required change**: Server-side validation: `Category == Others ⇒ OtherCategory required` (mirroring the booking form rule). Shared audience/category semantics kept via existing enums (`AgendaTargetCategory`).

### R3.4 Booking → Agenda relationship (pre-populate; Agenda = actual delivery evidence; do not auto-mark delivered)
- **Existing implementation**: On confirmation, `EnsureAgendaEntryAsync` (duplicated in `Public/BookingsController` and `PartnerDashboardController`) auto-creates an AgendaEntry **with `Status = Submitted`** and a junk lecturer value (`PartnerResponseNotes ?? "Confirmed by implementing entity"`).
- **Status**: CONFLICT with the "Agenda is actual delivery evidence" principle — a confirmed booking currently *is* auto-reported as a submitted delivered activity.
- **Required change**: Auto-created entries become **Draft** (pre-populated from the booking: club, season, type, subject, entity, date, target group, participants, confirmed lecturer). The club then edits the draft with actual data (final date, actual participants, media) and submits it. Requires a new **Agenda Edit** action for the club's own non-approved entries (no edit exists today). Extract the duplicated creation logic into one shared helper.
- **Dashboard/report impact**: Dashboard "pending approvals" counts `AgendaEntryStatus.Submitted`; converting auto-entries to Draft reduces noise (intended). Participants sums used by dashboard come from agenda — Draft entries are still included in some sums today; totals will be based on Submitted+Approved after this change is verified against dashboard queries.

### R3.5 End-of-season comprehensive table + digital summary (totals of sessions, participants, lecturers, entities, categories; season-over-season progression)
- **Existing implementation**: `Admin/Reports/Analytics` shows agenda counts by club and category charts, but no per-club season summary table with distinct lecturer/entity counts and no season-over-season progression view.
- **Status**: PARTIAL
- **Required change**: Add a "Season Summary by Club" table (activities, participants, distinct lecturers, distinct implementing entities, category breakdown) and a season-comparison block to Reports.
- **Module**: Reports. **Controller**: `Admin/ReportsController`. **View**: `Reports/Analytics.cshtml`.

---

## D4 — Photo & Video Gallery

### R4.1 Agenda media automatically appears in the Gallery (no double upload)
- **Existing implementation**: `AgendaController.SaveAgendaMedia` already creates a `GalleryItem` row (referencing the same physical file — no duplication) for every uploaded agenda file. **However the public gallery page (`Public/GalleryController.Index`) lists only `MediaAlbum`s — `GalleryItem` rows are never displayed anywhere public.** So the aggregation requirement is effectively broken end-to-end.
- **Status**: PARTIAL (data flow exists; presentation missing)
- **Required change**: Public Gallery page renders published `GalleryItem`s (agenda media) alongside curated albums, with existing season/club/date filters applied to both. Files are referenced, not copied (already the case).
- **View**: `Views/Gallery/Index.cshtml`. **Controller**: `Public/GalleryController`. **DB impact**: none.

### R4.2 DSC staff can upload official photos, official videos, press/newspaper coverage
- **Existing implementation**: `GalleryMediaType` enum already includes Photo, Video, PressCoverage, NewspaperCoverage, OfficialPhoto, OfficialVideo — but **no admin UI writes `GalleryItem` rows**; the admin Gallery module manages only albums (`MediaAlbum`/`MediaItem`).
- **Status**: MISSING (UI), COMPLETE (data model)
- **Required change**: Admin Gallery gains an "Activity & Press Media" management screen: list/create (upload or external URL)/publish-toggle/delete `GalleryItem`s with media-type selection. Album functionality untouched.
- **Controller**: `Admin/GalleryController`. **Views**: new `Admin/Gallery/MediaItems.cshtml` + `CreateMediaItem.cshtml`. **Security**: SuperAdmin/DscAdmin per existing gallery pattern.

### R4.3 Source/origin indicator (Agenda / DSC / Press)
- **Existing implementation**: Derivable — `AgendaEntryId != null` ⇒ Agenda-originated; `MediaType` Press/Newspaper ⇒ Press; otherwise DSC. No schema change required.
- **Status**: PARTIAL
- **Required change**: Show a source badge on gallery cards using the derivation above (no DB redesign).

---

## D5 — Digital Library

### R5.1 PDF-first; else cover image + external link; publisher + publication date required
- **Existing implementation**: `LibraryItem` has `FilePath`, `CoverImagePath`, `ExternalUrl`, `PublishingEntityEn/Ar`, `PublicationDate`, `ContentType`; admin `CreateItem` enforces "PDF or (cover + external link)". Publisher/date are optional in the VM though the doc says each item "must include" them.
- **Status**: PARTIAL
- **Required change**: Make Publishing Entity (EN or AR) and Publication Date required at item creation; validate `ExternalUrl` as http/https only.
- **Controller**: `Admin/LibraryController`. **DB impact**: none (validation only, existing rows untouched).

### R5.2 Pagination, fixed items per page, browsable
- **Existing implementation**: Public library index paginates at 9/page with category + content-type filtering, bilingual-capable cards (currently mostly EN text in the view).
- **Status**: COMPLETE (function) / PARTIAL (the public library view renders EN-only labels/titles)
- **Required change**: Localize the public Library index labels and show AR titles when culture=ar.

### R5.3 Council uploads/manages all content; partner entities supply materials but do not self-publish
- **Existing implementation**: Only SuperAdmin/DscAdmin can access the admin library module; partners have no publish path.
- **Status**: COMPLETE

---

## D6 — Data Entry Reports & Statistics

### R6.1 Club user selects season; club auto-derived; enters indicator data
- **Existing implementation**: `Public/KpiController.Create` has season + club dropdowns (club limited to own orgs, server-validated) and all documented indicator fields.
- **Status**: PARTIAL (club dropdown shown to single-club users — same fix as R2.8/R3.2)
- **Required change**: Read-only club context for single-club users; force server-side org id.

### R6.2 Indicators (lectures, lecturers, entities, participants, participation rate, warnings+red cards, weekly training minutes, lifestyle-disease players, satisfaction)
- **Existing implementation**: All exist on `KpiSubmission` (incl. `DiabetesCases`, `HeartConditionCases`, `HypertensionCases`, `OtherLifestyleConditionCases`) plus extra fields for ethics/diet/community events (from D7).
- **Status**: COMPLETE (fields) — with two refinements:
  - D6 lists **vision** conditions ("سكري/ضغط/قلب/نظر"); mapped to `OtherLifestyleConditionCases` with an updated label ("other incl. vision conditions"). Aggregated counts only — no individual medical records (privacy per task §49).
  - D6/D7 require **% of players achieving ≥150 min/week (target ≥90%)**; the model stores only a club-wide `WeeklyTrainingMinutes` number. **New field `PhysicalActivityComplianceRate` (%) is required** (CLUB-SUBMITTED; player-level data does not exist in the platform — documented limitation, no fabricated derivation).
- **DB impact**: 1 new decimal column (nullable/backfilled 0-safe → stored as nullable to honor "missing ≠ zero").

### R6.3 System-derived vs club-submitted classification (task §37; D6 states lectures count "extracted automatically from the Agenda")
- **Existing implementation**: Everything is manually typed by the club; nothing is derived.
- **Status**: MISSING
- **Required change + classification decision** (documented per task §38–§39):

| Indicator | Source classification | Derivation |
|---|---|---|
| Number of Lectures/Activities | **SYSTEM-DERIVED** | Count of club's Agenda entries (Submitted+Approved) in season — D6 explicitly says auto-extracted from Agenda |
| Number of Participants | **SYSTEM-DERIVED** (fallback: club) | Sum of Agenda `NumberOfParticipants` |
| Number of Lecturers | **SYSTEM-DERIVED** (fallback: club) | Distinct Agenda `LecturerName` |
| Number of Implementing Entities | **SYSTEM-DERIVED** (fallback: club) | Distinct Agenda `DepartmentOrOrganization` |
| Player Participation Rate | CLUB-SUBMITTED | No registered-player registry exists in Ghars |
| Attendance Rate | CLUB-SUBMITTED | See Conflicts C5 (doc formula ≠ platform check-in data) |
| Warnings & Red Cards | CLUB-SUBMITTED | External sports-competition data |
| Weekly training minutes / Physical-activity compliance % | CLUB-SUBMITTED | No player-level training log |
| Lifestyle-disease counts | CLUB-SUBMITTED | Club medical reports (aggregated only) |
| Ethical values %, Healthy diet %, Community events | CLUB-SUBMITTED | No authoritative platform source |
| Satisfaction Rate | APPROVED EXTERNAL SOURCE / CLUB-SUBMITTED | Official value comes from survey results (D8/external report), entered on approval evidence |

  Derived values are computed server-side at save/submit time when the club has Agenda data for that season (authoritative), otherwise the club's manual entry is kept (fallback documented on-screen).

### R6.4 Supporting documents upload (annual reports, Excel, tables)
- **Existing implementation**: `KpiDocument` upload exists (any extension, no validation). Files land in public `/uploads/kpi`.
- **Status**: PARTIAL
- **Required change**: Validate extension/MIME/size (pdf, xls(x), csv, doc(x), png/jpg; ≤20 MB). Public-path exposure of evidence is a pre-existing platform-wide limitation (all uploads are under `wwwroot`) — recorded as a risk, not fixed globally in this pass (see Risks).

### R6.5 Workflow Draft → Submitted → DSC Review → Approved (+ Returned/Rejected), club can edit draft, see status + reviewer feedback
- **Existing implementation**: Statuses exist (`Draft, Submitted, Approved, Rejected, MoreInfoRequired`); DSC review with notes + notification exists. **Clubs cannot edit a draft, cannot submit a saved draft, and cannot see reviewer notes** (index shows status only). Duplicate-check blocks a second submission per season+club (unique index) but the club is stuck if returned.
- **Status**: PARTIAL
- **Required change**: Club Edit action for own `Draft`/`MoreInfoRequired` (= "Returned") submissions; Submit action; reviewer notes displayed to club; audit entries (SystemAuditLog) for submit/review state changes. `MoreInfoRequired` is surfaced with the business label "Returned for correction" (no enum rename — protects dashboards/reports per task §19 logic).

### R6.6 Upon approval data appears in KPI table by club and season; 2026 baseline for later-year comparison; KPI table 2026–2033
- **Existing implementation**: Analytics/dashboard consume Approved submissions; 2026-baseline logic exists; the season model (one row per season+club) already supports the 2026–2033 progression (a season row per year). But there is **no year-by-year KPI progression table** and targets are hardcoded in 3 different files.
- **Status**: PARTIAL
- **Required change**: (a) central KPI catalog (R7.1); (b) year-progression table (2026 baseline → 2033 target) per indicator in Reports, driven by approved submissions per season.

---

## D7 — Key Performance Indicators

### R7.1 The 10 approved targets, defined centrally
| KPI | Approved target (D6/D7) | Current implementation |
|---|---|---|
| Program coverage | ≥ 80% of clubs (+ ≥60% of players) | hardcoded 80 in dashboard/analytics |
| Player participation | ≥ 60% | hardcoded 60 |
| Attendance | ≥ 80% | hardcoded 80 |
| Violations/disciplinary | ≥ 15% **annual reduction** | WRONG: compared raw count vs "15" |
| Ethical values | ≥ 90% | hardcoded 90 |
| Physical activity ≥150 min/wk | **≥ 90% of players** | WRONG: compares club total minutes vs 150 |
| Lifestyle disease-free | ≥ 95% | derived % (aggregate) vs 95 — OK |
| Healthy dietary habits | ≥ 80% | hardcoded 80 |
| Community events | 5–10 per club annually | hardcoded "5-10"/5 |
| Satisfaction/happiness | ≥ 85% | hardcoded 85 |
- **Existing implementation**: Targets & formulas duplicated across `Admin/DashboardController`, `Admin/ReportsController` + `Reports/Analytics.cshtml`, `Views/Kpi/Create.cshtml`.
- **Status**: PARTIAL, with two calculation defects (violations, physical activity)
- **Required change**: Introduce a single code-level KPI catalog (name EN/AR, unit, target, operator, source, formula description, baseline year, active) consumed by dashboard, analytics and the club KPI page. Fix the violations KPI to an annual-reduction calculation vs the previous season (explicit No-Data when no prior season / prior = 0). Use the new `PhysicalActivityComplianceRate` for the physical-activity KPI.

### R7.2 Missing data must not display as zero (task §54)
- **Existing implementation**: Several aggregates render 0/0% when no data (e.g., satisfaction with no answers → 0; disease-free with 0 participants → 0).
- **Status**: PARTIAL
- **Required change**: No-data states shown as "No Data" (bilingual) in the analytics KPI matrix and dashboard KPI cards where the underlying set is empty; divide-by-zero guarded (some guards already exist).

---

## D8 — Surveys

### R8.1 "Complete bilingual professional survey ... with all the options and system for survey"
- **Existing implementation**: Internal engine exists (`Survey/SurveyQuestion/SurveyOption/SurveyResponse/SurveyAnswer`, one-response-per-user, points award, admin results). BUT: the participant Take page is **English-only**; **MCQ questions cannot be created or answered** (options exist in the model, never rendered, `SelectedOptionId` never set); admin create only seeds 4 fixed default questions.
- **Status**: PARTIAL
- **Required change**: (a) bilingual Take page (question AR/EN, buttons, validation); (b) MCQ support end-to-end (admin question builder incl. options, participant rendering, answer capture via `SelectedOptionId`); (c) admin create supports adding custom questions of all 4 types (Stars/YesNo/Text/MCQ).
- **DB impact**: none (model already supports everything).

### R8.2 External official survey workflow (task brief §30–33: Dubai Digital Authority link + uploaded PDF analysis report)
- **Existing implementation**: Nothing — no external-survey concept.
- **Status**: MISSING
- **Required change**: New `ExternalSurvey` entity + DSC admin management (title EN/AR, description, https-only URL, season, active window, report PDF upload, report publish state) + member-facing Surveys page (Open Survey; Report button only when a published report exists). The internal engine is **retained** (dependency analysis: it feeds points, satisfaction analytics, admin results, seeded data — deletion would break reports and history). The two are clearly labeled "Official Survey" vs "Ghars Survey".
- **DB impact**: 1 new table. **Security**: URL scheme validation, PDF-only report upload validation.

---

## Cross-cutting requirements (task brief)

### RX.1 Organization scoping / no self-org selection (task §5–§7, §68–69)
- Audit result (all org-scoped entry points):
  - Booking create (club): validated, dropdown only for multi-club users → harden per R2.8.
  - Agenda create (club): validated → harden per R3.2.
  - KPI create (club): validated → harden per R6.1.
  - Partner dashboard/actions: partner org(s) always resolved from `OrganizationAdminLinks` server-side; **no entity selector exists anywhere** — COMPLETE.
  - Booking details/accept/reject (club+partner): all queries filter by the caller's org ids — IDOR-safe (COMPLETE).
  - Agenda/KPI/notifications/gallery listings: org-filtered (COMPLETE). New Edit endpoints must apply the same filters.
- **Status**: PARTIAL → to be hardened as described. No IDOR vulnerabilities were found in existing scoped queries; the fixes are defense-in-depth + UX alignment.

### RX.2 Localization
- New/modified screens must be bilingual. Known EN-only screens being touched: Survey Take, public Library labels, About/Vision. Admin-area pages follow the existing admin convention (bilingual where already bilingual; new admin views will use the `T(en, ar)` pattern).

### RX.3 Auditability
- Booking actions audited (existing). Gaps: KPI submit/review and external-survey report publication get `SystemAuditLog` entries; agenda auto-draft creation is recorded via booking audit trail (existing "PartnerApproved/ClubAcceptedProposedTime" entries) — agenda edit actions get audit entries.

### RX.4 File upload security
- Existing uploads accept any extension with original-extension passthrough (GUID filenames — good). Add centralized validation helper (extension + MIME + size) applied to: agenda media, KPI evidence, library file/cover, gallery admin uploads, survey report PDF. Storage stays under `wwwroot/uploads` (existing architecture); protected-storage rework is out of scope and recorded as a risk.

---

## Requirement Conflicts / Business Decisions Required

### C1 — Surveys: document vs task instruction
- **Document (D8, updated 2026-09-06)**: "Complete bilingual professional survey should be there with all the options and system for survey" → implies a full internal survey system.
- **Task brief (§30–§32)**: official surveys are external (Dubai Digital Authority) — Ghars stores only the link + uploaded PDF analysis report.
- **Current implementation**: internal engine only, partially bilingual, no MCQ.
- **Recommended interpretation (implemented)**: do both — complete/bilingualize the internal engine (satisfies D8) *and* add the external official-survey workflow (satisfies the approved business flow in the brief). They are clearly labeled and coexist; the official satisfaction KPI is sourced from the official report, not the internal engine.
- **Impact**: low risk; nothing removed.
- **Business decision required**: **Yes** — confirm whether the internal engine remains participant-facing long-term or becomes DSC-internal once the official external survey is live.

### C2 — Booking entry point: entity-first (doc) vs program-first (system)
- **Document (D2)**: club selects entity then describes the activity freely.
- **Current implementation**: club books a pre-published partner program (stronger: capacity, discovery, partner planning).
- **Recommended interpretation (implemented)**: support both — direct entity-first requests (doc-compliant) while preserving program bookings.
- **Business decision required**: **No** (both paths preserved; nothing removed). Flagged for awareness.

### C3 — Activity types: doc lists Lecture/Event only; system supports Lecture/Event/Workshop/Training/Course
- **Recommended interpretation**: keep the superset (documents elsewhere — D1 mechanisms, seeded programs — use workshops/courses). Doc silence ≠ removal.
- **Business decision required**: **No**.

### C4 — Agenda doc offers "Club: … / Others (specify)"
- A free-text "other club" contradicts organization scoping and approval workflow (clubs must be registered orgs). Not implemented; new clubs are registered via the Organizations module.
- **Business decision required**: **Yes** (low priority) — confirm free-text club entry is not desired.

### C5 — Attendance Rate formula
- **Document (D6)**: "participating players ÷ total registered players" — which duplicates the participation-rate formula with a different target (80% vs 60%), and differs from the platform's QR check-in data and from the current dashboard's checked-in/capacity calculation.
- **Recommended interpretation (implemented)**: attendance rate remains a CLUB-SUBMITTED percentage per the D6 data-entry flow (club enters it); platform check-in analytics remain as supplementary operational data. Dashboard "attendance" card is labeled to reflect KPI-submission-based attendance where KPI data exists.
- **Business decision required**: **Yes** — confirm the authoritative attendance definition (registered-players based vs event-capacity based).

### C6 — Satisfaction source
- D6 says "results of participant satisfaction surveys"; the official flow makes Dubai Digital Authority the analyzer. The internal engine also produces star ratings.
- **Recommended interpretation (implemented)**: official KPI value = the club-submitted/approved `SatisfactionRate` (backed by the official survey report); internal star analytics remain visible but labeled as internal indicators.
- **Business decision required**: **Yes** — confirm.

---

## Key risks noted (not regressions introduced by this work)

1. **All uploads are publicly reachable** under `wwwroot/uploads` (KPI evidence, org documents, certificates). Serving them through authorized endpoints requires a storage rework beyond this scope. Mitigated by GUID filenames only. Recommend a follow-up hardening task.
2. **Booking `ActivityId` becomes nullable** — every consumer was reviewed; views/queries null-check `Activity`, and season resolution falls back to the new `BookingRequest.SeasonId`. Regression risk concentrated in dashboards/reports filters; addressed in implementation and re-checked at build.
3. **Auto-agenda entries switch from Submitted to Draft** — dashboards counting "Agenda submissions" pending review will show fewer pending items (this is the intended business meaning).
4. **Seed data** continues to run on startup; new columns are nullable/additive so existing databases migrate safely.
