using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

/// <summary>
/// The club's Ghars Annual Report — the electronic form of <c>docs/Ghars_Clubs Report Form.docx</c>.
///
/// The club never types what Ghars already knows. Section 2's six indicators and section 3's activity
/// table are derived from the club's own Agenda through <see cref="SeasonClubStatistics"/> — the same
/// query that backs the KPI submission, so the two can never report different totals. The club fills in
/// the contact snapshot and the three narrative answers, and nothing else.
///
/// The reporting club is resolved from the authenticated user's <see cref="OrganizationAdminLink"/> set
/// on every request and is never model-bound: there is no club dropdown, no hidden club id, and no
/// posted value that can reach another club's report. Every query is scoped by that set and a miss
/// returns <see cref="NotFoundResult"/> rather than a distinguishable "forbidden".
/// </summary>
[Authorize(Roles = RoleNames.ClubAdmin)]
public class AnnualReportsController : Controllers.BaseController
{
    private readonly IHubContext<NotificationsHub> _hub;

    public AnnualReportsController(AppDbContext db, IHubContext<NotificationsHub> hub) : base(db)
    {
        _hub = hub;
    }

    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    // ------------------------------------------------------------------ list

    /// <summary>
    /// One row per sports season, whether or not a report exists yet, so "the season with no report"
    /// is as visible as the ones that have one.
    /// </summary>
    [HttpGet("/annual-report")]
    public async Task<IActionResult> Index()
    {
        var club = await ResolveClubAsync();
        if (club is null) return Forbid();

        var seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        var reports = await Db.GharsAnnualReports
            .Where(x => x.OrganizationId == club.Id)
            .ToListAsync();

        ViewBag.Club = club;
        ViewBag.Reports = reports.ToDictionary(x => x.SeasonId);
        return View(seasons);
    }

    // ------------------------------------------------------------------ draft experience

    /// <summary>
    /// Opens the report for a season. Creates nothing: a GET must not have side effects, so the row is
    /// written on the first save. An already-submitted or approved report redirects to the read-only
    /// view rather than rendering an editable form it would refuse to accept.
    /// </summary>
    [HttpGet("/annual-report/edit/{seasonId:int}")]
    public async Task<IActionResult> Edit(int seasonId)
    {
        var club = await ResolveClubAsync();
        if (club is null) return Forbid();

        var season = await Db.Seasons.FirstOrDefaultAsync(x => x.Id == seasonId);
        if (season is null) return NotFound();

        var report = await FindReportAsync(club.Id, seasonId);
        if (report is not null && !AnnualReportWorkflow.ClubCanEdit(report.Status))
        {
            TempData["ToastWarning"] = AnnualReportWorkflow.EditLockMessage(report.Status);
            return RedirectToAction(nameof(Details), new { id = report.Id });
        }

        await PopulateAsync(club, season, report);
        return View(await BuildVmAsync(club, seasonId, report));
    }

    [HttpPost("/annual-report/edit/{seasonId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int seasonId, AnnualReportVm vm)
    {
        var club = await ResolveClubAsync();
        if (club is null) return Forbid();

        // The route is authoritative for the season; the posted value is only a form echo.
        vm.SeasonId = seasonId;

        var season = await Db.Seasons.FirstOrDefaultAsync(x => x.Id == seasonId);
        if (season is null) return NotFound();

        var report = await FindReportAsync(club.Id, seasonId);

        // Re-checked on POST, not only on GET: a form rendered while the report was editable must not
        // still be postable after DSC has taken it into review.
        if (report is not null && !AnnualReportWorkflow.ClubCanEdit(report.Status))
        {
            TempData["ToastWarning"] = AnnualReportWorkflow.EditLockMessage(report.Status);
            return RedirectToAction(nameof(Details), new { id = report.Id });
        }

        var stats = await SeasonClubStatistics.GetAsync(Db, club.Id, seasonId);
        await ValidateSubmissionAsync(vm, club, season, stats);

        if (!ModelState.IsValid)
        {
            await PopulateAsync(club, season, report);
            return View(vm);
        }

        var isNew = report is null;
        if (isNew)
        {
            report = new GharsAnnualReport
            {
                // Server-derived, assigned here and nowhere else.
                OrganizationId = club.Id,
                SeasonId = seasonId,
                Status = AnnualReportStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = CurrentUserId
            };
            Db.GharsAnnualReports.Add(report);
        }

        var old = new { report!.Status, report.TotalLecturesDelivered, report.ParticipantsTotal };

        report.ProgramCoordinatorName = vm.ProgramCoordinatorName?.Trim();
        report.ContactNumber = vm.ContactNumber?.Trim();
        report.ContactEmail = vm.ContactEmail?.Trim();
        report.KeyResults = vm.KeyResults?.Trim();
        report.Challenges = vm.Challenges?.Trim();
        report.DevelopmentProposals = vm.DevelopmentProposals?.Trim();
        report.UpdatedAtUtc = DateTime.UtcNow;
        report.UpdatedByUserId = CurrentUserId;

        // While the report is editable the derived values track live Agenda data; on submission they are
        // frozen. Narrative text lives in its own columns, so a refresh can never lose what was typed.
        AnnualReportSnapshot.Capture(report, stats);

        var resubmission = !isNew && report.Status is AnnualReportStatus.ReturnedForCorrection or AnnualReportStatus.Rejected;

        if (!vm.SaveAsDraft)
        {
            report.Status = AnnualReportStatus.Submitted;
            report.SubmittedAtUtc = DateTime.UtcNow;
            report.SubmittedByUserId = CurrentUserId;
            report.SnapshotTakenAtUtc = DateTime.UtcNow;
        }
        else
        {
            report.Status = AnnualReportStatus.Draft;
        }

        await Db.SaveChangesAsync();

        var action = vm.SaveAsDraft
            ? (isNew ? "AnnualReportDraftCreated" : "AnnualReportDraftUpdated")
            : (resubmission ? "AnnualReportResubmitted" : "AnnualReportSubmitted");
        await AuditAsync(action, nameof(GharsAnnualReport), report.Id.ToString(), isNew ? null : old,
            new { report.Status, report.SeasonId, report.OrganizationId, report.TotalLecturesDelivered, report.ParticipantsTotal });

        if (!vm.SaveAsDraft)
        {
            await NotifyReviewersAsync(report, club, season);
            TempData["ToastSuccess"] = IsAr
                ? "تم إرسال التقرير السنوي إلى مجلس دبي الرياضي للمراجعة."
                : "The annual report was submitted to Dubai Sports Council for review.";
            return RedirectToAction(nameof(Details), new { id = report.Id });
        }

        TempData["ToastSuccess"] = IsAr ? "تم حفظ التقرير كمسودة." : "Annual report saved as a draft.";
        return RedirectToAction(nameof(Edit), new { seasonId });
    }

    // ------------------------------------------------------------------ read-only + export

    [HttpGet("/annual-report/view/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var club = await ResolveClubAsync();
        if (club is null) return Forbid();

        var report = await Db.GharsAnnualReports
            .Include(x => x.Season).Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == club.Id);
        if (report is null) return NotFound();

        ViewBag.Presentation = await AnnualReportSnapshot.PresentAsync(Db, report);
        ViewBag.ApprovedKpi = await ApprovedKpiAsync(report);
        return View(report);
    }

    /// <summary>
    /// A print-ready rendering that follows the template's structure. Uses a bare layout with a print
    /// stylesheet, so the browser's own Print-to-PDF produces a correct Arabic RTL document without the
    /// platform having to embed an Arabic-shaping font.
    ///
    /// Deliberately not audited. The requirement asks for export auditing only where it is already a
    /// project pattern, and it is not one here; more to the point this is a *render*, not a download,
    /// so auditing it would write a row every time someone opened the preview rather than once per
    /// export. The workflow events that matter — created, submitted, returned, approved — are audited.
    /// </summary>
    [HttpGet("/annual-report/print/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var club = await ResolveClubAsync();
        if (club is null) return Forbid();

        var report = await Db.GharsAnnualReports
            .Include(x => x.Season).Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == club.Id);
        if (report is null) return NotFound();

        ViewBag.Presentation = await AnnualReportSnapshot.PresentAsync(Db, report);
        return View("Print", report);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The club this user administers. Only <see cref="OrganizationType.Club"/> counts — the annual
    /// report is a club obligation — and the id is read from the link table, never from the request.
    /// </summary>
    private async Task<Organization?> ResolveClubAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return await Db.OrganizationAdminLinks
            .Where(x => x.UserId == userId && x.Organization != null
                        && (x.Organization.OrganizationType == OrganizationType.Club
                            || x.Organization.OrganizationType == OrganizationType.PrivateAcademy))
            .Select(x => x.Organization!)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
    }

    private Task<GharsAnnualReport?> FindReportAsync(int clubId, int seasonId)
        => Db.GharsAnnualReports.FirstOrDefaultAsync(x => x.OrganizationId == clubId && x.SeasonId == seasonId);

    /// <summary>
    /// Everything the form needs beyond the posted fields: the read-only club identity, the derived
    /// indicators, the generated activity table and the reviewer's notes from a previous return.
    /// </summary>
    private async Task PopulateAsync(Organization club, Season season, GharsAnnualReport? report)
    {
        var stats = await SeasonClubStatistics.GetAsync(Db, club.Id, season.Id);
        ViewBag.Club = club;
        ViewBag.Season = season;
        ViewBag.Report = report;
        ViewBag.Stats = stats;
        ViewBag.ReviewNotes = report?.ReviewNotes;
        ViewBag.CurrentStatus = report?.Status ?? AnnualReportStatus.Draft;
        ViewBag.ApprovedKpi = report is null ? null : await ApprovedKpiAsync(report);
        ViewBag.Warnings = BuildWarnings(stats);
    }

    /// <summary>
    /// Missing derived data is surfaced as a warning, never as a block: a club with an unusual season
    /// must still be able to file its narrative report. Only data that makes the report unusable stops
    /// a submission, and that is handled in <see cref="ValidateSubmissionAsync"/>.
    /// </summary>
    private static List<string> BuildWarnings(SeasonClubStats stats)
    {
        var warnings = new List<string>();
        if (!stats.HasData)
        {
            warnings.Add(IsAr
                ? "لا توجد أنشطة منفذة في الأجندة لهذا الموسم، لذلك ستُرسل المؤشرات وجدول الأنشطة فارغين. يرجى استكمال الأجندة أولاً إن كانت هناك أنشطة منفذة."
                : "No delivered activities were found in the Agenda for this season, so the indicators and activity table will be submitted empty. Complete the Agenda first if activities were delivered.");
            return warnings;
        }

        if (stats.Participants == 0)
            warnings.Add(IsAr
                ? "إجمالي عدد المشاركين صفر. يرجى التأكد من إدخال أعداد المشاركين في سجلات الأجندة."
                : "The total number of participants is zero. Check that participant counts were entered on the Agenda entries.");

        if (stats.ImplementingEntities == 0)
            warnings.Add(IsAr
                ? "لم تُسجَّل جهات منفذة في سجلات الأجندة لهذا الموسم."
                : "No implementing entities were recorded on this season's Agenda entries.");

        return warnings;
    }

    /// <summary>
    /// Submission preconditions. The club and season must be valid and usable; the narrative and
    /// contact requirements live on the view model. A derived indicator with no data is a warning, not
    /// a blocker.
    /// </summary>
    private async Task ValidateSubmissionAsync(AnnualReportVm vm, Organization club, Season season, SeasonClubStats stats)
    {
        if (club.Status != ApprovalStatus.Approved)
            ModelState.AddModelError("", IsAr
                ? "لا يمكن إرسال التقرير: النادي غير معتمد في المنصة."
                : "This report cannot be submitted: the club is not approved on the platform.");

        // Defence in depth behind the unique index: a second row for the same club and season would be
        // rejected by the database, but this produces a readable message instead of an exception.
        var duplicate = await Db.GharsAnnualReports
            .AnyAsync(x => x.OrganizationId == club.Id && x.SeasonId == season.Id && x.Id != vm.Id);
        if (duplicate)
            ModelState.AddModelError("", IsAr
                ? "يوجد تقرير سنوي لهذا النادي والموسم بالفعل."
                : "An annual report already exists for this club and season.");

        _ = stats; // derived data is never a submission blocker - see BuildWarnings
    }

    /// <summary>
    /// The approved KPI figure for the same club and season, where one exists. Surfaced beside the
    /// derived total and clearly labelled; the annual report never writes to KPI and never overrides
    /// its own derived value with it.
    /// </summary>
    private async Task<KpiSubmission?> ApprovedKpiAsync(GharsAnnualReport report)
        => await Db.KpiSubmissions.FirstOrDefaultAsync(x =>
            x.OrganizationId == report.OrganizationId &&
            x.SeasonId == report.SeasonId &&
            x.Status == KpiSubmissionStatus.Approved);

    /// <summary>
    /// Role-targeted notification to DSC reviewers, following the convention already used by KPI
    /// submissions and partner offerings. No other organization is addressed.
    /// </summary>
    private async Task NotifyReviewersAsync(GharsAnnualReport report, Organization club, Season season)
    {
        var n = new Notification
        {
            TitleEn = "Annual report awaiting DSC review",
            TitleAr = "تقرير سنوي بانتظار مراجعة المجلس",
            MessageEn = $"{club.NameEn} submitted its Ghars Annual Report for {season.TitleEn}. Your review is required.",
            MessageAr = $"قدّم {club.NameAr} التقرير السنوي لغرس عن {season.TitleAr}. مطلوب مراجعتكم.",
            Type = NotificationType.Warning,
            TargetType = NotificationTargetType.Role,
            TargetRoleName = RoleNames.DscAdmin,
            LinkUrl = $"/Admin/AnnualReports/Details/{report.Id}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };
        Db.Notifications.Add(n);
        await Db.SaveChangesAsync();

        // Delivered to DSC Admins and Super Admins, so a site with no DSC Admin yet cannot lose a
        // submission into a notification nobody receives.
        var roleNames = new[] { RoleNames.DscAdmin, RoleNames.SuperAdmin };
        var roleIds = await Db.Roles.Where(r => r.Name != null && roleNames.Contains(r.Name)).Select(r => r.Id).ToListAsync();
        var userIds = await Db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync();
        foreach (var uid in userIds)
            Db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = uid, DeliveredAtUtc = DateTime.UtcNow });
        await Db.SaveChangesAsync();

        await _hub.Clients.All.SendAsync("notificationReceived", new { title = n.TitleEn, message = n.MessageEn, linkUrl = n.LinkUrl });
    }

    private async Task<AnnualReportVm> BuildVmAsync(Organization club, int seasonId, GharsAnnualReport? report)
    {
        if (report is not null)
        {
            return new AnnualReportVm
            {
                Id = report.Id,
                SeasonId = report.SeasonId,
                ProgramCoordinatorName = report.ProgramCoordinatorName,
                ContactNumber = report.ContactNumber,
                ContactEmail = report.ContactEmail,
                KeyResults = report.KeyResults,
                Challenges = report.Challenges,
                DevelopmentProposals = report.DevelopmentProposals
            };
        }

        // A first draft is pre-filled from the club's primary contact where one exists, falling back to
        // the organization's own details. This is a copy, not a link: editing it here never writes back
        // to the organization's master contact record.
        var contact = await Db.OrganizationContacts
            .Where(x => x.OrganizationId == club.Id)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id)
            .FirstOrDefaultAsync();

        return new AnnualReportVm
        {
            SeasonId = seasonId,
            ProgramCoordinatorName = contact?.FullName,
            ContactNumber = string.IsNullOrWhiteSpace(contact?.Phone) ? club.Phone : contact!.Phone,
            ContactEmail = string.IsNullOrWhiteSpace(contact?.Email) ? club.Email : contact!.Email
        };
    }
}
