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

namespace GharsPlatform.Controllers.Admin;

/// <summary>
/// DSC review of club Ghars Annual Reports: approve, return for correction or reject, with review
/// notes, notification and audit trail.
///
/// Deliberately the same interaction as the KPI review screen and the partner offering review screen —
/// a filtered queue, a details page, and one Review action that records the decision — rather than a
/// third style. Approval freezes the report: nothing here ever recomputes a submitted report's derived
/// values.
/// </summary>
[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class AnnualReportsController : Controllers.BaseController
{
    private readonly IHubContext<NotificationsHub> _hub;

    public AnnualReportsController(AppDbContext db, IHubContext<NotificationsHub> hub) : base(db)
    {
        _hub = hub;
    }

    private static bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public async Task<IActionResult> Index(AnnualReportStatus? status, int? seasonId, int? clubId)
    {
        var q = Db.GharsAnnualReports.Include(x => x.Season).Include(x => x.Organization).AsQueryable();
        if (status.HasValue) q = q.Where(x => x.Status == status);
        if (seasonId.HasValue) q = q.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) q = q.Where(x => x.OrganizationId == clubId);

        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.Clubs = await Db.Organizations
            .Where(x => x.OrganizationType == OrganizationType.Club || x.OrganizationType == OrganizationType.PrivateAcademy)
            .OrderBy(x => x.NameEn).ToListAsync();
        ViewBag.SelectedStatus = status;
        ViewBag.SelectedSeasonId = seasonId;
        ViewBag.SelectedClubId = clubId;

        // Queue counters, from the same field the list badges and the filter use.
        ViewBag.StatusCounts = await Db.GharsAnnualReports
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        return View(await q
            .OrderBy(x => x.Status == AnnualReportStatus.Submitted ? 0 : 1)
            .ThenByDescending(x => x.SubmittedAtUtc ?? x.CreatedAtUtc)
            .Take(300)
            .ToListAsync());
    }

    [HttpGet("/Admin/AnnualReports/Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var report = await LoadAsync(id);
        if (report is null) return NotFound();

        ViewBag.Presentation = await AnnualReportSnapshot.PresentAsync(Db, report);
        ViewBag.ApprovedKpi = await ApprovedKpiAsync(report);
        ViewBag.People = await ResolveDisplayNamesAsync(report.SubmittedByUserId, report.ReviewedByUserId);

        var key = report.Id.ToString();
        ViewBag.History = await Db.SystemAuditLogs
            .Where(x => x.EntityName == nameof(GharsAnnualReport) && x.EntityId == key)
            .OrderByDescending(x => x.AtUtc)
            .Select(x => new OfferingHistoryEntry(x.Action, x.AtUtc))
            .ToListAsync();

        return View(report);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, AnnualReportStatus status, string? notes)
    {
        var report = await LoadAsync(id);
        if (report is null) return NotFound();

        if (!AnnualReportWorkflow.IsReviewDecision(status))
        {
            TempData["ToastWarning"] = IsAr() ? "قرار المراجعة غير صالح." : "Invalid review decision.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!AnnualReportWorkflow.DscCanReview(report.Status))
        {
            TempData["ToastWarning"] = IsAr()
                ? "لا يمكن اتخاذ قرار على تقرير ليس قيد المراجعة."
                : "A decision can only be recorded on a report that is under review.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Returning or rejecting hands the report back to the club, which makes its derived values live
        // again on the next save. Approving freezes it permanently: the snapshot taken at submission is
        // never recomputed, which is what makes an approved report an official historical record.
        var old = new { report.Status, report.ReviewNotes };
        report.Status = status;
        report.ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        report.ReviewedAtUtc = DateTime.UtcNow;
        report.ReviewedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();

        var action = status switch
        {
            AnnualReportStatus.Approved => "AnnualReportApproved",
            AnnualReportStatus.ReturnedForCorrection => "AnnualReportReturnedForCorrection",
            _ => "AnnualReportRejected"
        };
        await AuditAsync(action, nameof(GharsAnnualReport), report.Id.ToString(), old, new { report.Status, report.ReviewNotes });

        await NotifyClubAsync(report, status, notes);

        TempData["ToastSuccess"] = IsAr() ? "تم تسجيل قرار المراجعة." : "Review decision recorded.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Print-ready rendering of any club's report, under the existing cross-club review authority.
    /// Not audited, for the reason given on the club-facing action: this is a render, not a download.
    /// </summary>
    [HttpGet("/Admin/AnnualReports/Print/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var report = await LoadAsync(id);
        if (report is null) return NotFound();

        ViewBag.Presentation = await AnnualReportSnapshot.PresentAsync(Db, report);

        // The club-facing print view is identical in content; reusing it keeps one template for the
        // official document rather than two that can drift.
        return View("~/Views/AnnualReports/Print.cshtml", report);
    }

    // ------------------------------------------------------------------ helpers

    private Task<GharsAnnualReport?> LoadAsync(int id)
        => Db.GharsAnnualReports.Include(x => x.Season).Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id);

    private async Task<KpiSubmission?> ApprovedKpiAsync(GharsAnnualReport report)
        => await Db.KpiSubmissions.FirstOrDefaultAsync(x =>
            x.OrganizationId == report.OrganizationId &&
            x.SeasonId == report.SeasonId &&
            x.Status == KpiSubmissionStatus.Approved);

    private async Task<Dictionary<string, string>> ResolveDisplayNamesAsync(params string?[] userIds)
    {
        var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await Db.Users.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.FullName }).ToListAsync();
        return users.ToDictionary(
            x => x.Id,
            x => string.IsNullOrWhiteSpace(x.FullName) ? (IsAr() ? "مستخدم معتمد" : "Authorised user") : x.FullName!);
    }

    /// <summary>
    /// Organization-targeted notification: only the reporting club is addressed, never another club and
    /// never an unrelated entity.
    /// </summary>
    private async Task NotifyClubAsync(GharsAnnualReport report, AnnualReportStatus status, string? notes)
    {
        var (titleEn, titleAr, messageEn, messageAr) = status switch
        {
            AnnualReportStatus.Approved => (
                "Annual report approved", "تم اعتماد التقرير السنوي",
                "Your Ghars Annual Report was approved and is now an official record for the season.",
                "تم اعتماد التقرير السنوي لغرس وأصبح سجلاً رسمياً للموسم."),
            AnnualReportStatus.ReturnedForCorrection => (
                "Annual report returned for correction", "إعادة التقرير السنوي للتصحيح",
                "Your Ghars Annual Report was returned for correction. Please review the notes and resubmit.",
                "تمت إعادة التقرير السنوي لغرس للتصحيح. يرجى مراجعة الملاحظات وإعادة الإرسال."),
            _ => (
                "Annual report rejected", "تم رفض التقرير السنوي",
                "Your Ghars Annual Report was rejected. Please review the notes.",
                "تم رفض التقرير السنوي لغرس. يرجى مراجعة الملاحظات.")
        };

        var n = new Notification
        {
            TitleEn = titleEn,
            TitleAr = titleAr,
            MessageEn = string.IsNullOrWhiteSpace(notes) ? messageEn : $"{messageEn} — {notes}",
            MessageAr = string.IsNullOrWhiteSpace(notes) ? messageAr : $"{messageAr} — {notes}",
            Type = status == AnnualReportStatus.Approved ? NotificationType.Success : NotificationType.Warning,
            TargetType = NotificationTargetType.Organization,
            TargetOrganizationId = report.OrganizationId,
            LinkUrl = $"/annual-report/view/{report.Id}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };
        Db.Notifications.Add(n);
        await Db.SaveChangesAsync();

        var users = await Db.OrganizationAdminLinks
            .Where(x => x.OrganizationId == report.OrganizationId)
            .Select(x => x.UserId).Distinct().ToListAsync();
        foreach (var u in users)
            Db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = u, DeliveredAtUtc = DateTime.UtcNow });
        await Db.SaveChangesAsync();

        await _hub.Clients.All.SendAsync("notificationReceived", new { title = n.TitleEn, message = n.MessageEn, linkUrl = n.LinkUrl });
    }
}
