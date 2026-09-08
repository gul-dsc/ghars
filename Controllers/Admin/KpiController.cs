using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace GharsPlatform.Controllers.Admin;

/// <summary>
/// DSC review of club KPI/statistics submissions: approve, return for correction
/// (MoreInfoRequired) or reject, with review notes, notification and audit trail.
/// </summary>
[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class KpiController : Controllers.BaseController
{
    private readonly IHubContext<NotificationsHub> _hub;

    public KpiController(AppDbContext db, IHubContext<NotificationsHub> hub) : base(db)
    {
        _hub = hub;
    }

    private static bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public async Task<IActionResult> Index(KpiSubmissionStatus? status, int? seasonId, int? clubId)
    {
        var q = Db.KpiSubmissions.Include(x => x.Season).Include(x => x.Organization).Include(x => x.Documents).AsQueryable();
        if (status.HasValue) q = q.Where(x => x.Status == status);
        if (seasonId.HasValue) q = q.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) q = q.Where(x => x.OrganizationId == clubId);

        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.Clubs = await Db.Organizations.ApprovedClubsAndAcademies().ToListAsync();
        ViewBag.SelectedStatus = status; ViewBag.SelectedSeasonId = seasonId; ViewBag.SelectedClubId = clubId;
        return View(await q.OrderByDescending(x => x.Id).Take(300).ToListAsync());
    }

    [HttpGet("/Admin/Kpi/Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var e = await Db.KpiSubmissions
            .Include(x => x.Season).Include(x => x.Organization).Include(x => x.Documents)
            .Include(x => x.SatisfactionExternalSurvey)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();

        // The satisfaction figure for this season, from the native official survey where it has enough
        // responses and from approved club submissions otherwise. Shown to the reviewer as context;
        // it never alters the submitted SatisfactionRate.
        ViewBag.SeasonSatisfaction = await SatisfactionCalculator.ForSeasonAsync(Db, e.SeasonId);
        ViewBag.NativeSatisfaction = await SatisfactionCalculator.FromOfficialSurveyAsync(Db, e.SeasonId);
        ViewBag.MinimumResponses = SatisfactionCalculator.MinimumResponses;

        // Previous season submission for the same club drives the violations-reduction KPI.
        var currentSeason = await Db.Seasons.FirstOrDefaultAsync(x => x.Id == e.SeasonId);
        if (currentSeason != null)
        {
            var previousSeason = await Db.Seasons
                .Where(x => x.StartDate < currentSeason.StartDate)
                .OrderByDescending(x => x.StartDate)
                .FirstOrDefaultAsync();
            if (previousSeason != null)
            {
                ViewBag.PreviousViolations = await Db.KpiSubmissions
                    .Where(x => x.OrganizationId == e.OrganizationId && x.SeasonId == previousSeason.Id && x.Status == KpiSubmissionStatus.Approved)
                    .Select(x => (int?)x.WarningsAndRedCards)
                    .FirstOrDefaultAsync();
                ViewBag.PreviousSeasonTitle = IsAr() ? previousSeason.TitleAr : previousSeason.TitleEn;
            }
        }
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, KpiSubmissionStatus status, string? notes)
    {
        var e = await Db.KpiSubmissions.Include(x => x.Organization).FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();

        if (status is not (KpiSubmissionStatus.Approved or KpiSubmissionStatus.Rejected or KpiSubmissionStatus.MoreInfoRequired))
        {
            TempData["ToastWarning"] = IsAr() ? "قرار المراجعة غير صالح." : "Invalid review decision.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // SatisfactionExternalSurveyId is deliberately NOT written here any more. The official
        // satisfaction survey is native now (see SatisfactionCalculator), so no new submission is ever
        // linked to a third-party survey — and the links historical submissions already carry are left
        // exactly as they are, because they record what evidence supported a figure DSC approved at
        // the time. Reviewing an old submission again must not erase that.
        var old = new { e.Status, e.ReviewNotes };
        e.Status = status;
        e.ReviewNotes = notes;
        e.ReviewedAtUtc = DateTime.UtcNow;
        e.ReviewedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync("KpiReviewed", nameof(KpiSubmission), id.ToString(), old, new { e.Status, e.ReviewNotes });

        var (titleEn, titleAr, messageEn, messageAr) = status switch
        {
            KpiSubmissionStatus.Approved => (
                "KPI data approved", "تم اعتماد بيانات المؤشرات",
                "Your KPI submission was approved and now appears in the Ghars KPI table.",
                "تم اعتماد بيانات مؤشرات الأداء وتظهر الآن في جدول مؤشرات غرس."),
            KpiSubmissionStatus.MoreInfoRequired => (
                "KPI data returned for correction", "إعادة بيانات المؤشرات للتصحيح",
                "Your KPI submission was returned for correction. Please review the notes and resubmit.",
                "تمت إعادة بيانات مؤشرات الأداء للتصحيح. يرجى مراجعة الملاحظات وإعادة الإرسال."),
            _ => (
                "KPI data rejected", "تم رفض بيانات المؤشرات",
                "Your KPI submission was rejected. Please review the notes.",
                "تم رفض بيانات مؤشرات الأداء. يرجى مراجعة الملاحظات.")
        };

        var n = new Notification
        {
            TitleEn = titleEn,
            TitleAr = titleAr,
            MessageEn = string.IsNullOrWhiteSpace(notes) ? messageEn : $"{messageEn} — {notes}",
            MessageAr = string.IsNullOrWhiteSpace(notes) ? messageAr : $"{messageAr} — {notes}",
            Type = status == KpiSubmissionStatus.Approved ? NotificationType.Success : NotificationType.Warning,
            TargetType = NotificationTargetType.Organization,
            TargetOrganizationId = e.OrganizationId,
            LinkUrl = "/kpi",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };
        Db.Notifications.Add(n);
        await Db.SaveChangesAsync();

        var users = await Db.OrganizationAdminLinks.Where(x => x.OrganizationId == e.OrganizationId).Select(x => x.UserId).Distinct().ToListAsync();
        foreach (var u in users)
            Db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = u, DeliveredAtUtc = DateTime.UtcNow });
        await Db.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("notificationReceived", new { title = n.TitleEn, message = n.MessageEn, linkUrl = n.LinkUrl });

        TempData["ToastSuccess"] = IsAr() ? "تم تسجيل قرار المراجعة." : "Review decision recorded.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
