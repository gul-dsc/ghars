# Ghars Platform — Implementation Plan

Date: 2026-09-06. Derived from `GHARS_REQUIREMENTS_GAP_ANALYSIS.md` (requirement IDs referenced below).
Order follows task §72 (org-scoping → booking/agenda → gallery → surveys → KPI → reports → library → content → audits).

Legend: DB = database impact, SEC = security impact, LOC = localization impact, RPT = reporting impact, MIG = migration required.

---

## Phase 1 — Shared foundations

### P1.1 KPI catalog (R7.1, task §44)
- **Current**: targets/formulas hardcoded in `Admin/DashboardController`, `Reports/Analytics.cshtml`, `Views/Kpi/Create.cshtml`, `ReportsController.ExportAnalyticsCsv`.
- **New**: `Models/Core/GharsKpiCatalog.cs` — static registry: `Key, NameEn, NameAr, Unit, Target, TargetText, Operator, Source (SystemDerived/ClubSubmitted/ExternalApproved), CalculationEn/Ar, BaselineYear=2026`. Consumers refactored to read from it.
- **Files**: new catalog file; edits to the 3 consumers. DB: none. SEC: none. LOC: AR names included. RPT: single source of truth. MIG: no. **Risk**: low. **Test**: build + visual check of dashboard/analytics targets unchanged (80/60/80/15/90/90/95/80/5–10/85).

### P1.2 Upload + URL validation helpers (RX.4, R8.2, R5.1)
- **New**: `Helpers/FileValidationHelper.cs` — per-profile allowed extensions/MIME/size (Image, Video, Pdf, Document/Evidence, Media) returning bilingual error; `IsSafeHttpUrl(string)` for http/https-only absolute URLs.
- Applied in: Agenda media upload, KPI evidence, Library create item, Gallery admin uploads (albums/items), External survey report, Library ExternalUrl, External survey URL.
- DB: none. SEC: prevents dangerous uploads/schemes. LOC: bilingual messages. MIG: no. **Risk**: rejecting previously-accepted odd files (acceptable). **Test**: attempt .exe upload → validation error; javascript: URL → rejected.

### P1.3 Entities + single migration (R2.2/2.5/2.6, R6.2, R8.2)
- `BookingRequest`: `SeasonId int?` (+ `Season?` nav, FK NoAction), `ActivityId` → `int?` (+ nav nullable), `LecturerName (200)`, `LecturerContact (200)`, `ProposedSubject (250)`.
- `KpiSubmission`: `PhysicalActivityComplianceRate decimal?`.
- New `ExternalSurvey`: Id, TitleEn/TitleAr (200, req), DescriptionEn/Ar (2000), ExternalUrl (700, req), SeasonId? (FK NoAction), IsActive, StartsAtUtc?, EndsAtUtc?, ReportPdfPath (500)?, IsReportPublished, ReportPublishedAtUtc?, audit base.
- `AppDbContext`: DbSet + FK configs (NoAction on Season FKs to avoid cascade cycles).
- **MIG**: one migration `GharsDocsAlignment2026` via `dotnet ef migrations add` (non-destructive: alter-to-nullable + additive columns + new table; works on existing DB; no data loss; no backfill needed — null Season falls back to `Activity.SeasonId` at read time).
- **Risk**: `ActivityId` nullability touches many readers — mitigated by Phase 3 sweep. **Test**: build, migration script review.

---

## Phase 2 — Organization-scoping hardening (RX.1, R2.8, R3.2, R6.1)

- **Current**: club dropdowns render for the user's own club(s); posted org id validated server-side.
- **New behavior**: helper `UserOrgResolver` (small static in `Helpers`) → `GetUserClubIdsAsync`. In Booking/Agenda/KPI create POST: if user has exactly one linked club, **server overwrites** posted `OrganizationId` with the resolved one; GET shows read-only club name (no dropdown). Multi-club users keep a dropdown restricted to their permitted clubs (still validated).
- **Files**: `Public/BookingsController`, `Public/AgendaController`, `Public/KpiController` + their Create views.
- DB: none. SEC: spoofed ClubId impossible for single-club users; already-validated for multi-club. LOC: reuse labels. RPT: none. MIG: no. **Risk**: low. **Test**: post foreign OrganizationId → server uses own club / rejects.

## Phase 3 — Booking alignment (R2.1–R2.7)

- **Direct entity-first requests**: `/bookings/create` GET accepts `activityId?` or `partnerId?`; with neither, form shows approved-entity dropdown (GovernmentAuthority/OtherPartner + Approved only) + season dropdown (active default; inactive seasons excluded). POST validates entity + season server-side; saves `SeasonId`, `PartnerOrganizationId`, `ActivityId=null`.
- `BookingCreateVm`: + `PartnerOrganizationId int?`, `SeasonId int?`; conditional validation (no activity ⇒ entity+season required).
- **Confirmation details**: partner Approve form gains LecturerName (required), LecturerContact, logistics/notes → stored on booking, included in notification message + details page.
- **Subject modification**: ProposeTimes gains optional `proposedSubject` → `booking.ProposedSubject`; club Accept applies it to `Subject` with old/new in audit JSON; details page shows pending proposed subject.
- **Agenda helper consolidation**: new `Helpers/BookingAgendaHelper.CreateDraftFromBookingAsync` replaces the duplicated `EnsureAgendaEntryAsync` in both controllers; creates **Draft** entries; lecturer = `booking.LecturerName`; season = `booking.SeasonId ?? Activity.SeasonId`.
- **Null-activity sweep**: views (`Bookings/Details`, `ClubDashboard/Index`, `PartnerDashboard/Index`, admin booking views, dashboards) display `Subject` when `Activity == null`; queries guarding `x.Activity != null` reviewed; season filters use `(b.SeasonId ?? b.Activity.SeasonId)` where translatable, else left as-is (activity-anchored only) with note.
- Club dashboard gains "New booking request" button (direct flow entry).
- DB: from P1.3. SEC: entity/season validation server-side. LOC: all new labels bilingual. RPT: dashboard booking-season filter extended. MIG: from P1.3. **Risk**: medium (widest blast radius) — mitigated by null-checks + build + view sweep. **Test**: create direct booking; partner approves with lecturer; club sees details; propose subject+times; club accepts; audit rows present.

## Phase 4 — Agenda alignment (R3.2–R3.4)

- `AgendaVm`: conditional validation Others⇒`OtherCategory` required.
- **Edit action** (`/agenda/edit/{id}`): own-club entries with status Draft or Submitted (not Approved/Rejected); pre-filled form incl. new media upload; Save as Draft / Submit buttons; org ownership enforced in query (IDOR-safe); `SystemAuditLog` entry on edit/submit.
- Auto-draft from booking per Phase 3 helper (Draft status).
- Agenda index: show status badges incl. Draft prominently + Edit button for editable rows.
- DB: none. SEC: ownership filters on Edit. LOC: bilingual. RPT: pending-agenda counts now reflect true submissions. MIG: no. **Risk**: low-medium (dashboard counters shift by design). **Test**: confirm booking → draft agenda appears → edit + submit → DSC sees Submitted.

## Phase 5 — Gallery integration (R4.1–R4.3)

- Public `Gallery/Index`: second section "Activity & Program Media" rendering published `GalleryItem`s (existing season/club/date filters applied), with source badges (Agenda / DSC / Press) derived from `AgendaEntryId`/`MediaType`; lightbox reuse from album details pattern.
- Admin `GalleryController`: `MediaItems` list + `CreateMediaItem` (upload w/ validation or external URL; type = OfficialPhoto/OfficialVideo/PressCoverage/NewspaperCoverage/Photo/Video), `ToggleItemPublish`, `DeleteMediaItemRecord` (record-only delete; underlying agenda files never deleted from here when agenda-owned).
- No file duplication (references only — already true).
- DB: none. SEC: admin-role gated; club items remain club-attributed. LOC: bilingual public labels. RPT: dashboard gallery counts unchanged (albums) + GalleryItems already counted in analytics. MIG: no. **Risk**: low. **Test**: upload agenda media → visible in public gallery; DSC adds press item → visible with badge.

## Phase 6 — Surveys (R8.1, R8.2, C1)

- **Internal engine completion**: Take view bilingual (`T()`, AR question text, RTL) + renders MCQ options (radio per option, `SelectedOptionId` captured); POST handles `Mcq`; admin Create gains dynamic question builder (arrays: QuestionEn[], QuestionAr[], Type[], Required[], Options pipe-separated for MCQ) — default 4 questions still offered as a starting template.
- **External surveys**: new `Admin/ExternalSurveysController` (SuperAdmin+DscAdmin; CRUD + report PDF upload + report publish toggle; URL validated http/https; audit log on publish). New public `/surveys` page (`Public/SurveysController.Index`) listing active external surveys (within availability window): Open Survey (new tab, `rel=noopener`), Report button only when `IsReportPublished && ReportPdfPath != null`. Nav links (public layout authenticated menu + admin sidebar). Labeled "Official Survey" vs internal "Ghars Survey".
- Seeder: one sample external survey (guarded insert).
- DB: table from P1.3. SEC: URL scheme + PDF validation; role gating. LOC: fully bilingual. RPT: none direct. MIG: from P1.3. **Risk**: low. **Test**: admin creates survey + uploads report; club sees Open + Report; no report ⇒ no button.

## Phase 7 — KPI submission workflow (R6.1–R6.5)

- Club `KpiController`: 
  - Create GET: derive from Agenda (club+season, Submitted+Approved): activities count, participants sum, distinct lecturers, distinct entities → pre-filled + flagged "System-derived from Agenda"; POST recomputes server-side and **overrides** those 4 fields when agenda data exists (>0 entries), else keeps manual entry.
  - New `Edit`/`Submit` for own Draft/MoreInfoRequired rows (ownership-filtered); reviewer notes shown on index; status labels bilingual ("Returned for correction" for MoreInfoRequired).
  - New field `PhysicalActivityComplianceRate` on form (% players ≥150 min/wk); lifestyle-conditions label updated to include vision under "other".
  - Evidence upload validated via P1.2; `SystemAuditLog` on create/edit/submit.
- Admin `KpiController.Review`: add Returned label, audit log entry; (functionality otherwise exists).
- DB: column from P1.3. SEC: ownership on edit/submit. LOC: bilingual. RPT: new field consumed by Phase 8. MIG: from P1.3. **Risk**: low-medium (unique index season+club interplay with edit — edit works on the same row, no new row). **Test**: draft → edit → submit → DSC returns → club edits → resubmit → approve.

## Phase 8 — KPI calculations, reports, dashboards (R7.1, R7.2, R6.6, R3.5)

- `Reports/Analytics`: KPI matrix consumes `GharsKpiCatalog`; **violations KPI** becomes annual reduction vs same club's previous-season submission (`(prev−curr)/prev×100`, target ≥15%; "No Data" when no prior/prior=0); **physical activity** uses `PhysicalActivityComplianceRate` (target ≥90%), showing "No Data" when null; No-Data badges replace misleading 0%.
- New Analytics sections: (a) **Season Summary by Club** (from Agenda: activities, participants, distinct lecturers, distinct entities); (b) **KPI year progression 2026(baseline)→2033(target)** per indicator from Approved submissions per season.
- `Admin/DashboardController`: KpiTargets from catalog; physical-activity/violations cards corrected; satisfaction/compliance cards show No Data when empty; attendance card labeled per C5 decision (KPI-submitted attendance when available, else operational check-in rate).
- `Views/Kpi/Create` targets block reads catalog.
- CSV export updated for new field/labels.
- DB: none beyond P1.3. SEC: none. LOC: bilingual labels via catalog. RPT: core. MIG: no. **Risk**: medium (dashboard is management-facing) — verify numbers side-by-side. **Test**: analytics matrix shows reduction % + No Data states; dashboard/analytics targets consistent.

## Phase 9 — Digital Library (R5.1, R5.2)

- Admin `CreateItem`: require Publishing Entity (EN or AR) + Publication Date; validate ExternalUrl http/https; file/cover validated via P1.2.
- Public `Library/Index`: bilingual labels + AR titles/descriptions when culture=ar.
- DB/SEC/LOC as above. MIG: no. **Risk**: low. **Test**: create item w/o publisher → validation; AR page shows AR titles.

## Phase 10 — About Ghars content (R1.1, R1.2)

- `Home/About.cshtml`, `Home/Vision.cshtml`: approved bilingual wording verbatim (intro/strategic context; vision; 9 objectives; scope/audiences; 3 mechanisms). `T()` pattern + RTL-safe markup.
- **Risk**: none. **Test**: view both cultures.

## Phase 11 — Audits & final sweep (task §81)

- Grep sweep: hardcoded targets remnants, org-id-from-form without validation, `FindAsync` without scope, EN-only strings in touched views, duplicate KPI formulas.
- `dotnet build`; fix all errors; run app compile of Razor (build includes RazorCompileOnBuild default).
- Produce `GHARS_IMPLEMENTATION_REPORT.md`.

---

## Explicitly out of scope (documented, not regressions)
- Protected file storage outside `wwwroot` (platform-wide rework; risk-logged).
- Player-level registries (registration, medical, training logs) — no authoritative source; KPI stays aggregate per docs.
- Removing internal survey engine (kept per dependency analysis).
- Rewards module re-enablement (disabled by design; untouched).

## Testing approach summary
Static + build verification for all phases; scenario walkthroughs per role recorded in the final report (§75–77 of the brief) against the seeded database where a local SQL Server is available; Arabic spot-checks on all touched views (§78).
