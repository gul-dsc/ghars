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
/// "My Ghars Channel Content" — an implementing entity's own awareness and educational material.
///
/// Deliberately separate from <see cref="PartnerProgramsController"/> ("My Programs"): a program is a
/// bookable offering with a season, a capacity and booking requests behind it; channel content is
/// published material. Putting two unrelated lifecycles on one screen would make both harder to reason
/// about.
///
/// Ownership is <see cref="GalleryItem.OrganizationId"/> written from the authenticated user's
/// organization link and never model-bound, so no posted or query-string value can move an item
/// between entities or reach another entity's rows. Every query is scoped by
/// <see cref="PartnerOrganizationIdsAsync"/> and a miss returns <see cref="NotFoundResult"/>.
///
/// Nothing here can publish. <see cref="GalleryItem.IsPublished"/> is set only by a DSC approval in
/// <c>Areas/Admin</c> — see <see cref="ChannelWorkflow"/> for the lifecycle.
/// </summary>
[Authorize(Roles = RoleNames.PartnerAdmin)]
public class ChannelContentController : Controllers.BaseController
{
    private readonly IWebHostEnvironment _env;
    private readonly IHubContext<NotificationsHub> _hub;

    public ChannelContentController(AppDbContext db, IWebHostEnvironment env, IHubContext<NotificationsHub> hub) : base(db)
    {
        _env = env; _hub = hub;
    }

    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    /// <summary>Partner channel uploads live here, which Program.cs denies to the static file middleware,
    /// so the bytes are reachable only through the publication-checked endpoint.</summary>
    private const string UploadFolder = "uploads/channel";

    [HttpGet("/partner/channel")]
    public async Task<IActionResult> Index(ChannelApprovalStatus? status, ChannelCategory? category, string? q)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var query = OwnedContent(orgIds);
        if (status.HasValue) query = query.Where(x => x.ApprovalStatus == status.Value);
        if (category.HasValue) query = query.Where(x => x.ChannelCategory == category.Value);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.TitleEn.Contains(q) || x.TitleAr.Contains(q));

        var items = await query
            .Include(x => x.Season)
            .Include(x => x.LibraryItem)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        // Counted over everything the entity owns, not the filtered page, so the header keeps saying
        // what is outstanding while the user is looking at one slice. One grouped query.
        var byState = await OwnedContent(orgIds)
            .GroupBy(x => x.ApprovalStatus)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync();
        ViewBag.TotalItems = byState.Sum(x => x.Count);
        ViewBag.StateCounts = byState.Where(x => x.State.HasValue).ToDictionary(x => x.State!.Value, x => x.Count);

        ViewBag.Organization = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == orgIds[0]);
        ViewBag.Status = status;
        ViewBag.Category = category;
        ViewBag.Query = q;
        return View(items);
    }

    [HttpGet("/partner/channel/details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var item = await OwnedContent(orgIds)
            .Include(x => x.Season).Include(x => x.Organization).Include(x => x.LibraryItem)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();

        var key = item.Id.ToString();
        ViewBag.History = await Db.SystemAuditLogs
            .Where(x => x.EntityName == nameof(GalleryItem) && x.EntityId == key)
            .OrderByDescending(x => x.AtUtc)
            .Select(x => new OfferingHistoryEntry(x.Action, x.AtUtc))
            .ToListAsync();

        return View(item);
    }

    [HttpGet("/partner/channel/create")]
    public async Task<IActionResult> Create()
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        await PopulateAsync(orgIds);
        var season = await Db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        return View(new ChannelContentVm { SeasonId = season?.Id ?? 0, MediaDate = DateTime.Today });
    }

    [HttpPost("/partner/channel/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ChannelContentVm vm, string? action)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        await ValidateServerSideAsync(vm, orgIds);
        if (!ModelState.IsValid)
        {
            await PopulateAsync(orgIds);
            return View(vm);
        }

        var path = vm.File is { Length: > 0 }
            ? await FileValidationHelper.SaveAsync(vm.File, _env.WebRootPath, UploadFolder)
            : null;

        var item = new GalleryItem
        {
            // Ownership is server-derived. This is the only place it is ever assigned.
            OrganizationId = orgIds[0],
            SeasonId = vm.SeasonId,
            // New content is never visible, whichever button was pressed. Publication happens only on
            // DSC approval.
            IsPublished = false,
            ApprovalStatus = ChannelApprovalStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };
        ApplyEditableFields(item, vm, path);

        Db.GalleryItems.Add(item);
        await Db.SaveChangesAsync();
        await AuditAsync("ChannelContentDraftCreated", nameof(GalleryItem), item.Id.ToString(), null,
            new { item.OrganizationId, item.SeasonId, item.MediaType, item.ChannelCategory, item.TitleEn, item.ApprovalStatus, item.IsPublished });

        if (string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase))
        {
            await MarkSubmittedAsync(item, "ChannelContentSubmitted");
            TempData["ToastSuccess"] = IsAr ? "تم إرسال المحتوى إلى مجلس دبي الرياضي للاعتماد." : "Content submitted to DSC for approval.";
        }
        else
        {
            TempData["ToastSuccess"] = IsAr ? "تم حفظ المحتوى كمسودة." : "Content saved as a draft.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/partner/channel/edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var item = await OwnedContent(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();

        if (!ChannelWorkflow.PartnerCanEdit(item.ApprovalStatus))
        {
            TempData["ToastWarning"] = ChannelWorkflow.EditLockMessage(item.ApprovalStatus);
            return RedirectToAction(nameof(Index));
        }

        await PopulateAsync(orgIds);
        ViewBag.Item = item;
        return View(ToVm(item));
    }

    [HttpPost("/partner/channel/edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ChannelContentVm vm, string? action)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var item = await OwnedContent(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();

        // Re-checked on POST: a form rendered while the item was editable must not still be postable
        // after DSC has taken it into review.
        if (!ChannelWorkflow.PartnerCanEdit(item.ApprovalStatus))
        {
            TempData["ToastWarning"] = ChannelWorkflow.EditLockMessage(item.ApprovalStatus);
            return RedirectToAction(nameof(Index));
        }

        vm.Id = item.Id;
        vm.HasExistingFile = !string.IsNullOrWhiteSpace(item.FilePath);
        await ValidateServerSideAsync(vm, orgIds);
        if (!ModelState.IsValid)
        {
            await PopulateAsync(orgIds);
            ViewBag.Item = item;
            return View(vm);
        }

        var old = new { item.TitleEn, item.TitleAr, item.MediaType, item.ChannelCategory, item.SeasonId, item.ExternalUrl, item.LibraryItemId, item.ApprovalStatus, item.IsPublished };

        var newPath = vm.File is { Length: > 0 }
            ? await FileValidationHelper.SaveAsync(vm.File, _env.WebRootPath, UploadFolder)
            : null;
        var replaced = newPath is not null ? item.FilePath : null;

        ApplyEditableFields(item, vm, newPath ?? item.FilePath);
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();

        // A replaced upload leaves an orphan file behind otherwise. Only ever the file this entity just
        // replaced on its own row.
        if (!string.IsNullOrWhiteSpace(replaced)) TryDeleteUpload(replaced);

        await AuditAsync("ChannelContentUpdated", nameof(GalleryItem), item.Id.ToString(), old,
            new { item.TitleEn, item.TitleAr, item.MediaType, item.ChannelCategory, item.SeasonId, item.ExternalUrl, item.LibraryItemId, item.ApprovalStatus, item.IsPublished });

        if (string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase))
        {
            var resubmission = old.ApprovalStatus != ChannelApprovalStatus.Draft;
            await MarkSubmittedAsync(item, resubmission ? "ChannelContentResubmitted" : "ChannelContentSubmitted");
            TempData["ToastSuccess"] = IsAr ? "تم إرسال المحتوى إلى مجلس دبي الرياضي للاعتماد." : "Content submitted to DSC for approval.";
        }
        else
        {
            TempData["ToastSuccess"] = IsAr ? "تم تحديث المحتوى." : "Content updated.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/partner/channel/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var item = await OwnedContent(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();

        if (!ChannelWorkflow.PartnerCanSubmit(item.ApprovalStatus))
        {
            TempData["ToastWarning"] = IsAr ? "لا يمكن إرسال هذا المحتوى في حالته الحالية." : "This content cannot be submitted in its current state.";
            return RedirectToAction(nameof(Index));
        }

        var resubmission = item.ApprovalStatus != ChannelApprovalStatus.Draft;
        await MarkSubmittedAsync(item, resubmission ? "ChannelContentResubmitted" : "ChannelContentSubmitted");
        TempData["ToastSuccess"] = IsAr ? "تم إرسال المحتوى إلى مجلس دبي الرياضي للاعتماد." : "Content submitted to DSC for approval.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Withdraw approved content from the channel. Safe for the owner to do because it only ever
    /// removes visibility — it can never create it. Restoring it requires a fresh DSC approval.
    /// </summary>
    [HttpPost("/partner/channel/withdraw")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var item = await OwnedContent(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();

        if (!ChannelWorkflow.PartnerCanWithdraw(item.ApprovalStatus))
        {
            TempData["ToastWarning"] = IsAr ? "لا يمكن سحب محتوى غير معتمد." : "Only approved content can be withdrawn.";
            return RedirectToAction(nameof(Index));
        }

        var old = new { item.IsPublished, item.ApprovalStatus };
        item.IsPublished = false;
        item.ApprovalStatus = ChannelApprovalStatus.Unpublished;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync("ChannelContentWithdrawnByPartner", nameof(GalleryItem), item.Id.ToString(), old, new { item.IsPublished, item.ApprovalStatus });

        TempData["ToastSuccess"] = IsAr ? "تم سحب المحتوى من قناة غرس." : "Content withdrawn from the Ghars Channel.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Only rows this workflow owns. <c>ApprovalStatus != null</c> is essential: without it a partner
    /// whose organization also appears on club agenda media or a DSC upload could edit those rows.
    /// </summary>
    private IQueryable<GalleryItem> OwnedContent(List<int> orgIds)
        => Db.GalleryItems.Where(x => x.ApprovalStatus != null
                                      && x.OrganizationId != null
                                      && orgIds.Contains(x.OrganizationId.Value));

    /// <summary>The implementing entities this user administers — the same shape used by My Programs,
    /// so both partner surfaces agree on who a partner is. Government authorities included.</summary>
    private async Task<List<int>> PartnerOrganizationIdsAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return await Db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.UserId == userId && x.Organization != null &&
                        (x.Organization.OrganizationType == OrganizationType.OtherPartner ||
                         x.Organization.OrganizationType == OrganizationType.GovernmentAuthority))
            .Select(x => x.OrganizationId)
            .ToListAsync();
    }

    private async Task ValidateServerSideAsync(ChannelContentVm vm, List<int> orgIds)
    {
        // The upload profile is images and video only. The channel deliberately accepts no documents:
        // booklets and publications belong in the Digital Library, which a partner can link instead.
        var fileError = FileValidationHelper.Validate(vm.File, FileValidationHelper.Media, IsAr);
        if (fileError != null) ModelState.AddModelError(nameof(vm.File), fileError);

        if (!await Db.Seasons.AnyAsync(x => x.Id == vm.SeasonId))
            ModelState.AddModelError(nameof(vm.SeasonId), IsAr ? "الموسم الرياضي غير صالح." : "Invalid sports season.");

        // A linked publication must be one this entity actually published, and must be live: linking an
        // arbitrary id would let the channel surface another organization's library item.
        if (vm.LibraryItemId is > 0)
        {
            var org = await Db.Organizations.Where(x => orgIds.Contains(x.Id))
                .Select(x => new { x.NameEn, x.NameAr }).FirstOrDefaultAsync();
            var valid = await Db.LibraryItems.AnyAsync(x =>
                x.Id == vm.LibraryItemId.Value && x.IsPublished &&
                (x.PublishingEntityEn == org!.NameEn || x.PublishingEntityAr == org.NameAr));
            if (!valid)
                ModelState.AddModelError(nameof(vm.LibraryItemId), IsAr
                    ? "يمكن الربط فقط بإصدار منشور في المكتبة الرقمية باسم جهتكم."
                    : "You can only link a published Digital Library item issued by your own entity.");
        }
    }

    /// <summary>Writes every field a partner may change. Ownership, publication and approval state are
    /// handled by the caller, so this can never be the thing that publishes content.</summary>
    private static void ApplyEditableFields(GalleryItem item, ChannelContentVm vm, string? filePath)
    {
        item.TitleEn = vm.TitleEn!.Trim();
        item.TitleAr = vm.TitleAr!.Trim();
        item.DescriptionEn = vm.DescriptionEn?.Trim();
        item.DescriptionAr = vm.DescriptionAr?.Trim();
        item.MediaType = vm.MediaType;
        item.ChannelCategory = vm.ChannelCategory;
        item.SeasonId = vm.SeasonId;
        item.MediaDate = vm.MediaDate;
        item.FilePath = filePath;
        item.ExternalUrl = string.IsNullOrWhiteSpace(vm.ExternalUrl) ? null : vm.ExternalUrl.Trim();
        item.LibraryItemId = vm.LibraryItemId is > 0 ? vm.LibraryItemId : null;
    }

    private static ChannelContentVm ToVm(GalleryItem item) => new()
    {
        Id = item.Id,
        SeasonId = item.SeasonId,
        TitleEn = item.TitleEn,
        TitleAr = item.TitleAr,
        DescriptionEn = item.DescriptionEn,
        DescriptionAr = item.DescriptionAr,
        MediaType = item.MediaType,
        ChannelCategory = item.ChannelCategory ?? Models.Core.ChannelCategory.Awareness,
        ExternalUrl = item.ExternalUrl,
        LibraryItemId = item.LibraryItemId,
        MediaDate = item.MediaDate,
        HasExistingFile = !string.IsNullOrWhiteSpace(item.FilePath)
    };

    /// <summary>
    /// The single place a submission is recorded, so create-and-submit, edit-and-submit and
    /// submit-from-the-list cannot diverge. Never touches IsPublished: submitted content is still
    /// invisible.
    /// </summary>
    private async Task MarkSubmittedAsync(GalleryItem item, string auditAction)
    {
        var old = new { item.ApprovalStatus, item.SubmittedAtUtc, item.IsPublished };
        item.ApprovalStatus = ChannelApprovalStatus.SubmittedForApproval;
        item.SubmittedAtUtc = DateTime.UtcNow;
        item.SubmittedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync(auditAction, nameof(GalleryItem), item.Id.ToString(), old, new { item.ApprovalStatus, item.SubmittedAtUtc, item.IsPublished });

        var entity = await Db.Organizations.Where(x => x.Id == item.OrganizationId)
            .Select(x => new { x.NameEn, x.NameAr }).FirstOrDefaultAsync();
        var nameEn = string.IsNullOrWhiteSpace(entity?.NameEn) ? "An implementing entity" : entity!.NameEn;
        var nameAr = string.IsNullOrWhiteSpace(entity?.NameAr) ? "جهة منفذة" : entity!.NameAr!;

        var n = new Notification
        {
            TitleEn = "Ghars Channel content awaiting review",
            TitleAr = "محتوى في قناة غرس بانتظار المراجعة",
            MessageEn = $"{nameEn} submitted '{item.TitleEn}' to the Ghars Channel for approval. Your review is required.",
            MessageAr = $"قدّمت {nameAr} '{item.TitleAr}' إلى قناة غرس للاعتماد. مطلوب مراجعتكم.",
            Type = NotificationType.Warning,
            TargetType = NotificationTargetType.Role,
            TargetRoleName = RoleNames.DscAdmin,
            LinkUrl = $"/Admin/Gallery/ChannelReview/{item.Id}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };
        Db.Notifications.Add(n);
        await Db.SaveChangesAsync();

        var roleNames = new[] { RoleNames.DscAdmin, RoleNames.SuperAdmin };
        var roleIds = await Db.Roles.Where(r => r.Name != null && roleNames.Contains(r.Name)).Select(r => r.Id).ToListAsync();
        var userIds = await Db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync();
        foreach (var uid in userIds)
            Db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = uid, DeliveredAtUtc = DateTime.UtcNow });
        await Db.SaveChangesAsync();

        await _hub.Clients.All.SendAsync("notificationReceived", new { title = n.TitleEn, message = n.MessageEn, linkUrl = n.LinkUrl });
    }

    private void TryDeleteUpload(string webRelativePath)
    {
        try
        {
            if (!webRelativePath.StartsWith('/') || webRelativePath.Contains("..", StringComparison.Ordinal)) return;
            var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, webRelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(Path.Combine(_env.WebRootPath, UploadFolder.Replace('/', Path.DirectorySeparatorChar)));
            // Only ever inside the channel upload folder, whatever the stored value claims.
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
        }
        catch { /* a leftover file is not worth failing the request over */ }
    }

    private async Task PopulateAsync(List<int> orgIds)
    {
        var org = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == orgIds[0]);
        ViewBag.Organization = org;
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        // Only this entity's own published library items may be linked.
        ViewBag.LibraryItems = org is null
            ? new List<LibraryItem>()
            : await Db.LibraryItems
                .Where(x => x.IsPublished && (x.PublishingEntityEn == org.NameEn || x.PublishingEntityAr == org.NameAr))
                .OrderBy(x => x.TitleEn).ToListAsync();
    }
}
