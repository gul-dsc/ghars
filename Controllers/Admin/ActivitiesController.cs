using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Models.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class ActivitiesController : Controllers.BaseController
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly IWebHostEnvironment _env;

    public ActivitiesController(AppDbContext db, IHubContext<NotificationsHub> hub, IWebHostEnvironment env) : base(db)
    {
        _hub = hub;
        _env = env;
    }

    private static bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public async Task<IActionResult> Index(int? partnerId = null, int? seasonId = null, ActivityType? type = null,
                                           OfferingApprovalStatus? approval = null, bool pendingOnly = false)
    {
        // PartnerOrganization is included so the list identifies which implementing entity owns each
        // row. Partner-managed offerings appear here alongside DSC-created activities; admin
        // authority over them is unchanged and deliberately not narrowed by the ownership filter
        // that scopes the partner's own screen.
        IQueryable<Activity> q = Db.Activities.Include(x => x.Season).Include(x => x.PartnerOrganization);

        if (partnerId.HasValue) q = q.Where(x => x.PartnerOrganizationId == partnerId.Value);
        if (seasonId.HasValue) q = q.Where(x => x.SeasonId == seasonId.Value);
        if (type.HasValue) q = q.Where(x => x.Type == type.Value);
        if (approval.HasValue) q = q.Where(x => x.ApprovalStatus == approval.Value);
        if (pendingOnly) q = q.Where(x => x.ApprovalStatus == OfferingApprovalStatus.SubmittedForApproval);

        // A submission the reviewer has not acted on is more urgent than one they have, so the queue
        // is ordered by how long it has been waiting. Everything else keeps the existing order.
        var list = pendingOnly
            ? await q.OrderBy(x => x.SubmittedAtUtc).ToListAsync()
            : await q.OrderByDescending(x => x.StartDateTime).ToListAsync();

        // Who submitted each row, resolved once for the whole page rather than per row.
        ViewBag.SubmitterNames = await ResolveDisplayNamesAsync(list.Select(x => x.SubmittedByUserId));

        ViewBag.Partners = await Db.Organizations
            .Where(GharsOrganizations.IsApprovedPartner)
            .OrderBy(x => x.NameEn).ToListAsync();
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).ToListAsync();
        ViewBag.PendingCount = await Db.Activities.CountAsync(x => x.ApprovalStatus == OfferingApprovalStatus.SubmittedForApproval);
        ViewBag.PartnerId = partnerId;
        ViewBag.SeasonId = seasonId;
        ViewBag.Type = type;
        ViewBag.Approval = approval;
        ViewBag.PendingOnly = pendingOnly;
        return View(list);
    }

    public async Task<IActionResult> Details(int id)
    {
        var activity = await Db.Activities
            .Include(x => x.Season)
            .Include(x => x.PartnerOrganization)
            .Include(x => x.BookingRequests).ThenInclude(b => b.Organization)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (activity is null) return NotFound();

        ViewBag.People = await ResolveDisplayNamesAsync(new[] { activity.SubmittedByUserId, activity.ReviewedByUserId });

        // The partner's supporting documents, so the reviewer decides on the same material the
        // partner submitted rather than on the form fields alone.
        ViewBag.Attachments = await Db.ActivityAttachments
            .Where(x => x.ActivityId == activity.Id)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync();

        // The same workflow timeline the partner sees, read from the audit log rather than a second
        // store. Action name and timestamp only — the stored JSON payload stays internal.
        var key = activity.Id.ToString();
        ViewBag.History = await Db.SystemAuditLogs
            .Where(x => x.EntityName == nameof(Activity) && x.EntityId == key)
            .OrderByDescending(x => x.AtUtc)
            .Select(x => new OfferingHistoryEntry(x.Action, x.AtUtc))
            .ToListAsync();

        return View(activity);
    }

    /// <summary>Display names for the workflow stamps. Falls back to a role description rather than
    /// rendering a user id or an email address.</summary>
    private async Task<Dictionary<string, string>> ResolveDisplayNamesAsync(IEnumerable<string?> userIds)
    {
        var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await Db.Users.Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToListAsync();

        return users.ToDictionary(
            x => x.Id,
            x => string.IsNullOrWhiteSpace(x.FullName) ? (IsAr() ? "مستخدم مخوّل" : "Authorised user") : x.FullName!);
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Create()
    {
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).ToListAsync();
        return View(new ActivityVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Create(ActivityVm vm)
    {
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).ToListAsync();
        if (!ModelState.IsValid) return View(vm);

        if (vm.EndDateTime <= vm.StartDateTime)
        {
            ModelState.AddModelError(nameof(vm.EndDateTime), IsAr() ? "يجب أن يكون وقت الانتهاء بعد وقت البدء." : "End time must be after start time.");
            return View(vm);
        }

        var entity = new Activity
        {
            SeasonId = vm.SeasonId,
            Type = vm.Type,
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            DescriptionEn = vm.DescriptionEn?.Trim(),
            DescriptionAr = vm.DescriptionAr?.Trim(),
            CategoryEn = vm.CategoryEn?.Trim(),
            CategoryAr = vm.CategoryAr?.Trim(),
            StartDateTime = vm.StartDateTime,
            EndDateTime = vm.EndDateTime,
            LocationEn = vm.LocationEn.Trim(),
            LocationAr = vm.LocationAr.Trim(),
            Capacity = vm.Capacity,
            AllowWalkIn = vm.AllowWalkIn,
            Status = vm.Status,
            CreatedByUserId = CurrentUserId ?? ""
        };

        Db.Activities.Add(entity);
        await Db.SaveChangesAsync();
        await AuditAsync("Create", nameof(Activity), entity.Id.ToString(), null, entity);

        TempData["ToastSuccess"] = IsAr() ? "تم إنشاء البرنامج." : "Program created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(int id)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).ToListAsync();

        return View(new ActivityVm
        {
            Id = a.Id,
            SeasonId = a.SeasonId,
            Type = a.Type,
            TitleEn = a.TitleEn,
            TitleAr = a.TitleAr,
            DescriptionEn = a.DescriptionEn,
            DescriptionAr = a.DescriptionAr,
            CategoryEn = a.CategoryEn,
            CategoryAr = a.CategoryAr,
            StartDateTime = a.StartDateTime,
            EndDateTime = a.EndDateTime,
            LocationEn = a.LocationEn,
            LocationAr = a.LocationAr,
            Capacity = a.Capacity,
            AllowWalkIn = a.AllowWalkIn,
            Status = a.Status
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(ActivityVm vm)
    {
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).ToListAsync();

        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == vm.Id);
        if (a is null) return NotFound();

        if (!ModelState.IsValid) return View(vm);

        var old = new
        {
            a.SeasonId, a.Type, a.TitleEn, a.TitleAr, a.DescriptionEn, a.DescriptionAr,
            a.CategoryEn, a.CategoryAr, a.StartDateTime, a.EndDateTime, a.LocationEn, a.LocationAr,
            a.Capacity, a.AllowWalkIn, a.Status
        };

        a.SeasonId = vm.SeasonId;
        a.Type = vm.Type;
        a.TitleEn = vm.TitleEn.Trim();
        a.TitleAr = vm.TitleAr.Trim();
        a.DescriptionEn = vm.DescriptionEn?.Trim();
        a.DescriptionAr = vm.DescriptionAr?.Trim();
        a.CategoryEn = vm.CategoryEn?.Trim();
        a.CategoryAr = vm.CategoryAr?.Trim();
        a.StartDateTime = vm.StartDateTime;
        a.EndDateTime = vm.EndDateTime;
        a.LocationEn = vm.LocationEn.Trim();
        a.LocationAr = vm.LocationAr.Trim();
        a.Capacity = vm.Capacity;
        a.AllowWalkIn = vm.AllowWalkIn;
        a.Status = vm.Status;
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedByUserId = CurrentUserId;

        await Db.SaveChangesAsync();
        await AuditAsync("Update", nameof(Activity), a.Id.ToString(), old, a);

        TempData["ToastSuccess"] = IsAr() ? "تم تحديث البرنامج." : "Program updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Publish(int id)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        var old = new { a.Status, a.ApprovalStatus };
        a.Status = ActivityStatus.Published;
        // Keep the two fields consistent for a partner offering. Publishing one from here is an
        // approval by any other name, and leaving ApprovalStatus behind would produce a row that is
        // published but not approved — visible on the partner directory yet unbookable.
        if (OfferingWorkflow.IsPartnerOffering(a))
        {
            a.ApprovalStatus = OfferingApprovalStatus.Approved;
            a.ReviewedAtUtc = DateTime.UtcNow;
            a.ReviewedByUserId = CurrentUserId;
            a.ReviewNotes = null;
        }
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedByUserId = CurrentUserId;

        await Db.SaveChangesAsync();
        await AuditAsync("Publish", nameof(Activity), id.ToString(), old, new { a.Status, a.ApprovalStatus });

        TempData["ToastSuccess"] = IsAr() ? "تم نشر البرنامج." : "Program published.";
        return RedirectToAction(nameof(Index));
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        // Collected before the delete: the attachment rows cascade away with the activity, and once
        // they are gone nothing records where their files lived. A cancelled activity keeps both.
        var orphanedFiles = new List<string>();

        var cancelledInstead = await Db.BookingRequests.AnyAsync(x => x.ActivityId == id);
        if (cancelledInstead)
        {
            a.Status = ActivityStatus.Cancelled;
            a.UpdatedAtUtc = DateTime.UtcNow;
            a.UpdatedByUserId = CurrentUserId;
        }
        else
        {
            orphanedFiles = await Db.ActivityAttachments
                .Where(x => x.ActivityId == id)
                .Select(x => x.FilePath)
                .ToListAsync();
            Db.Activities.Remove(a);
        }
        await Db.SaveChangesAsync();

        // Only after the rows are actually gone, so a failed delete never leaves rows pointing at
        // files that no longer exist.
        foreach (var key in orphanedFiles) ProtectedFileStore.TryDelete(key, _env);

        await AuditAsync("Delete", nameof(Activity), id.ToString(), a, null);
        var name = IsAr() && !string.IsNullOrWhiteSpace(a.TitleAr) ? a.TitleAr : a.TitleEn;
        if (cancelledInstead)
            TempData["ToastWarning"] = IsAr()
                ? $"لم يُحذف البرنامج '{name}' لأن عليه حجوزات، وتم إلغاؤه بدلاً من ذلك."
                : $"'{name}' has bookings, so it was cancelled instead of deleted.";
        else
            TempData["ToastSuccess"] = IsAr() ? $"تم حذف البرنامج '{name}'." : $"'{name}' was deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ partner offering review
    //
    // These four actions are open to DSC Admin as well as Super Admin, unlike the create/edit/delete
    // actions above. That is deliberate and is what the workflow requires: reviewing partner
    // submissions is the DSC Admin's job, and restricting approval to Super Admin would leave the
    // queue unworkable. Content authoring stays Super Admin only.

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int id, string? notes) => ReviewAsync(id, OfferingApprovalStatus.Approved, notes);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ReturnForCorrection(int id, string? notes) => ReviewAsync(id, OfferingApprovalStatus.ReturnedForCorrection, notes);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(int id, string? notes) => ReviewAsync(id, OfferingApprovalStatus.Rejected, notes);

    /// <summary>
    /// Withdraw an approved offering from the club catalogue. Unlike the three review decisions this
    /// applies to an already-approved offering rather than one awaiting review, so it is handled
    /// separately.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnpublishOffering(int id, string? notes)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        if (!OfferingWorkflow.CanUnpublish(a.ApprovalStatus))
        {
            TempData["ToastWarning"] = IsAr() ? "يمكن سحب البرنامج المعتمد فقط." : "Only an approved program can be withdrawn.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var old = new { a.Status, a.ApprovalStatus };
        a.Status = ActivityStatus.Draft;
        a.ApprovalStatus = OfferingApprovalStatus.Unpublished;
        a.ReviewNotes = string.IsNullOrWhiteSpace(notes) ? a.ReviewNotes : notes.Trim();
        a.ReviewedAtUtc = DateTime.UtcNow;
        a.ReviewedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync("OfferingUnpublishedByDsc", nameof(Activity), a.Id.ToString(), old, new { a.Status, a.ApprovalStatus, a.ReviewNotes });

        await NotifyPartnerAsync(a, "Program withdrawn", "تم سحب البرنامج",
            $"'{a.TitleEn}' was withdrawn from the club catalogue by DSC.",
            $"تم سحب البرنامج '{a.TitleAr}' من كتالوج الأندية من قبل مجلس دبي الرياضي.",
            NotificationType.Warning);

        TempData["ToastSuccess"] = IsAr() ? "تم سحب البرنامج من كتالوج الأندية." : "Program withdrawn from the club catalogue.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// The one place a review decision is applied. Approval is the only transition that publishes,
    /// which is what keeps a partner from ever making their own content visible.
    /// </summary>
    private async Task<IActionResult> ReviewAsync(int id, OfferingApprovalStatus decision, string? notes)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        if (!OfferingWorkflow.IsPartnerOffering(a))
        {
            TempData["ToastWarning"] = IsAr() ? "هذا البرنامج ليس مقدّماً من جهة منفذة، فلا يمر بمراجعة الاعتماد." : "This program was not submitted by an implementing entity, so it has no review step.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!OfferingWorkflow.DscCanReview(a.ApprovalStatus))
        {
            TempData["ToastWarning"] = IsAr() ? "هذا البرنامج ليس بانتظار المراجعة." : "This program is not awaiting review.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // A return or a rejection has to tell the partner why, or it is not actionable.
        if (decision != OfferingApprovalStatus.Approved && string.IsNullOrWhiteSpace(notes))
        {
            TempData["ToastWarning"] = IsAr() ? "ملاحظات المراجع مطلوبة عند إعادة البرنامج أو رفضه." : "Reviewer notes are required when returning or rejecting a program.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var old = new { a.Status, a.ApprovalStatus, a.ReviewNotes };
        a.ApprovalStatus = decision;
        a.ReviewedAtUtc = DateTime.UtcNow;
        a.ReviewedByUserId = CurrentUserId;
        // Approval clears the note: it belonged to the correction round that has just ended.
        a.ReviewNotes = decision == OfferingApprovalStatus.Approved ? null : notes!.Trim();
        // Publication is set here and only here.
        a.Status = decision == OfferingApprovalStatus.Approved ? ActivityStatus.Published : ActivityStatus.Draft;

        await Db.SaveChangesAsync();

        var (auditAction, titleEn, titleAr, messageEn, messageAr, notifType) = decision switch
        {
            OfferingApprovalStatus.Approved => ("OfferingApproved", "Program approved", "تم اعتماد البرنامج",
                $"'{a.TitleEn}' was approved and is now visible to clubs.",
                $"تم اعتماد البرنامج '{a.TitleAr}' وهو الآن ظاهر للأندية.", NotificationType.Success),
            OfferingApprovalStatus.ReturnedForCorrection => ("OfferingReturnedForCorrection", "Program returned for correction", "أُعيد البرنامج للتعديل",
                $"'{a.TitleEn}' was returned for correction: {a.ReviewNotes}",
                $"أُعيد البرنامج '{a.TitleAr}' للتعديل: {a.ReviewNotes}", NotificationType.Warning),
            _ => ("OfferingRejected", "Program rejected", "تم رفض البرنامج",
                $"'{a.TitleEn}' was rejected: {a.ReviewNotes}",
                $"تم رفض البرنامج '{a.TitleAr}': {a.ReviewNotes}", NotificationType.Danger)
        };

        await AuditAsync(auditAction, nameof(Activity), a.Id.ToString(), old, new { a.Status, a.ApprovalStatus, a.ReviewNotes, a.ReviewedAtUtc });
        await NotifyPartnerAsync(a, titleEn, titleAr, messageEn, messageAr, notifType);

        TempData["ToastSuccess"] = (IsAr() ? titleAr : titleEn) + ".";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Organization-targeted notification to the owning implementing entity only. Deliveries resolve
    /// through that organization's admin links, so no unrelated organization is addressed.
    /// </summary>
    private async Task NotifyPartnerAsync(Activity a, string titleEn, string titleAr, string messageEn, string messageAr, NotificationType type)
    {
        if (!a.PartnerOrganizationId.HasValue) return;

        var n = new Notification
        {
            TitleEn = titleEn,
            TitleAr = titleAr,
            MessageEn = messageEn,
            MessageAr = messageAr,
            Type = type,
            LinkUrl = "/partner/programs",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };
        await NotificationDispatcher.SendToOrganizationAsync(Db, _hub, n, a.PartnerOrganizationId.Value);
    }

    public class ActivityVm
    {
        public int Id { get; set; }

        [Required]
        public int SeasonId { get; set; }

        [Required]
        public ActivityType Type { get; set; } = ActivityType.Lecture;

        [BilingualRequired(ErrorMessage = "Enter the English title.", Ar = "أدخلوا العنوان بالإنجليزية."), MaxLength(250)]
        public string TitleEn { get; set; } = "";

        [BilingualRequired(ErrorMessage = "Enter the Arabic title.", Ar = "أدخلوا العنوان بالعربية."), MaxLength(250)]
        public string TitleAr { get; set; } = "";

        [MaxLength(3000)]
        public string? DescriptionEn { get; set; }

        [MaxLength(3000)]
        public string? DescriptionAr { get; set; }

        [MaxLength(150)]
        public string? CategoryEn { get; set; }

        [MaxLength(150)]
        public string? CategoryAr { get; set; }

        [Required]
        public DateTime StartDateTime { get; set; } = DateTime.UtcNow.AddDays(7);

        [Required]
        public DateTime EndDateTime { get; set; } = DateTime.UtcNow.AddDays(7).AddHours(2);

        [BilingualRequired(ErrorMessage = "Enter the location in English.", Ar = "أدخلوا الموقع بالإنجليزية."), MaxLength(300)]
        public string LocationEn { get; set; } = "";

        [BilingualRequired(ErrorMessage = "Enter the location in Arabic.", Ar = "أدخلوا الموقع بالعربية."), MaxLength(300)]
        public string LocationAr { get; set; } = "";

        [Range(1, 5000)]
        public int Capacity { get; set; } = 50;

        public bool AllowWalkIn { get; set; } = false;

        [Required]
        public ActivityStatus Status { get; set; } = ActivityStatus.Draft;
    }
}
