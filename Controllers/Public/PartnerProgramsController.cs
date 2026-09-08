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
/// "My Programs" — an implementing entity's own catalogue of bookable Training and Workshop
/// offerings. This is content management and is deliberately kept separate from
/// <see cref="PartnerDashboardController"/>, which remains the operational inbox for the booking
/// requests those offerings attract.
///
/// An offering is an <see cref="Activity"/> owned by the partner organization. Ownership is
/// <see cref="Activity.PartnerOrganizationId"/> and nothing else: it is written from the
/// authenticated user's organization link and is never model-bound, so no posted or query-string
/// value can move an offering between entities or reach another entity's rows. Every query in this
/// controller is scoped by <see cref="PartnerOrganizationIdsAsync"/> and a miss returns
/// <see cref="NotFoundResult"/> rather than a distinguishable "forbidden".
///
/// Nothing here can make an offering visible to clubs. Publication is the exclusive result of a DSC
/// approval in <c>Areas/Admin</c> — see <see cref="OfferingWorkflow"/> for the lifecycle.
/// </summary>
[Authorize(Roles = RoleNames.PartnerAdmin)]
public class PartnerProgramsController : Controllers.BaseController
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly IWebHostEnvironment _env;

    public PartnerProgramsController(AppDbContext db, IHubContext<NotificationsHub> hub, IWebHostEnvironment env) : base(db)
    {
        _hub = hub;
        _env = env;
    }

    private bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    [HttpGet("/partner/programs")]
    public async Task<IActionResult> Index(ActivityType? type = null, OfferingApprovalStatus? status = null, string? q = null)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var query = OwnedActivities(orgIds);
        if (type.HasValue) query = query.Where(x => x.Type == type.Value);
        if (status.HasValue) query = query.Where(x => x.ApprovalStatus == status.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.TitleEn.Contains(q) || x.TitleAr.Contains(q));

        var programs = await query
            .Include(x => x.Season)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        // One grouped query for every count, rather than a count per row.
        var ids = programs.Select(x => x.Id).ToList();
        ViewBag.BookingCounts = await Db.BookingRequests
            .Where(x => x.ActivityId != null && ids.Contains(x.ActivityId.Value))
            .GroupBy(x => x.ActivityId!.Value)
            .Select(g => new { ActivityId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ActivityId, x => x.Count);

        // Operational summary. Counted over every owned program rather than the filtered page, so the
        // header keeps telling the partner what is outstanding while they are looking at one slice —
        // and computed from ApprovalStatus, the same field the list badges and the filter use, so the
        // two can never disagree. One grouped query, not a count per state.
        var byState = await OwnedActivities(orgIds)
            .GroupBy(x => x.ApprovalStatus)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync();
        // The total counts every owned program; the per-state tiles cover only rows that carry a
        // workflow state, which for a partner-owned row is all of them.
        ViewBag.TotalPrograms = byState.Sum(x => x.Count);
        ViewBag.StateCounts = byState.Where(x => x.State.HasValue)
            .ToDictionary(x => x.State!.Value, x => x.Count);

        ViewBag.Organization = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == orgIds[0]);
        ViewBag.Type = type;
        ViewBag.Status = status;
        ViewBag.Query = q;
        return View(programs);
    }

    /// <summary>
    /// One offering, with the review history behind it. Read-only: every state change still goes
    /// through the actions below, which re-derive ownership and re-check the transition rules.
    /// </summary>
    [HttpGet("/partner/programs/details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds)
            .Include(x => x.Season)
            .Include(x => x.PartnerOrganization)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        ViewBag.Attachments = await AttachmentsAsync(a.Id);
        ViewBag.BookingCount = await Db.BookingRequests.CountAsync(x => x.ActivityId == a.Id);
        ViewBag.People = await ResolveDisplayNamesAsync(a.SubmittedByUserId, a.ReviewedByUserId);

        // The workflow timeline, read from the audit log this workflow already writes. Only the
        // action and its timestamp are surfaced; the stored JSON payload holds internal field values
        // and is deliberately not shown.
        var key = a.Id.ToString();
        ViewBag.History = await Db.SystemAuditLogs
            .Where(x => x.EntityName == nameof(Activity) && x.EntityId == key)
            .OrderByDescending(x => x.AtUtc)
            .Select(x => new OfferingHistoryEntry(x.Action, x.AtUtc))
            .ToListAsync();

        return View(a);
    }

    [HttpGet("/partner/programs/create")]
    public async Task<IActionResult> Create()
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        await PopulateFormAsync(orgIds);
        var season = await Db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        return View(new PartnerProgramVm
        {
            SeasonId = season?.Id ?? 0,
            SessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            SessionStartTime = new TimeOnly(10, 0),
            SessionEndTime = new TimeOnly(12, 0)
        });
    }

    [HttpPost("/partner/programs/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PartnerProgramVm vm, string? action)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        await ValidateSeasonAsync(vm);
        ValidateAttachments(vm, alreadyStored: 0);

        if (!ModelState.IsValid)
        {
            await PopulateFormAsync(orgIds);
            return View(vm);
        }

        var submit = string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase);
        var entity = new Activity
        {
            // Ownership is server-derived. This is the only place it is ever assigned.
            PartnerOrganizationId = orgIds[0],
            SeasonId = vm.SeasonId!.Value,
            Type = vm.Type!.Value,
            // A new offering is never published, whichever button was pressed. Publication happens
            // only on DSC approval.
            Status = ActivityStatus.Draft,
            ApprovalStatus = OfferingApprovalStatus.Draft,
            CreatedByUserId = CurrentUserId ?? "",
            CreatedAtUtc = DateTime.UtcNow
        };
        ApplyEditableFields(entity, vm);

        Db.Activities.Add(entity);
        await Db.SaveChangesAsync();
        await AuditAsync("OfferingDraftCreated", nameof(Activity), entity.Id.ToString(), null,
            new { entity.PartnerOrganizationId, entity.SeasonId, entity.Type, entity.Status, entity.ApprovalStatus, entity.TitleEn });

        // Stored before any submission is recorded, so a reviewer opening the queue never sees a
        // programme whose documents have not landed yet.
        await SaveAttachmentsAsync(entity, vm.Attachments);

        if (submit)
        {
            await MarkSubmittedAsync(entity, "OfferingSubmitted");
            TempData["ToastSuccess"] = IsAr ? "تم إرسال البرنامج إلى مجلس دبي الرياضي للاعتماد." : "Program submitted to DSC for approval.";
        }
        else
        {
            TempData["ToastSuccess"] = IsAr ? "تم حفظ البرنامج كمسودة." : "Program saved as a draft.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/partner/programs/edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        if (!OfferingWorkflow.PartnerCanEdit(a.ApprovalStatus))
        {
            TempData["ToastWarning"] = EditLockMessage(a.ApprovalStatus);
            return RedirectToAction(nameof(Index));
        }

        await PopulateFormAsync(orgIds);
        ViewBag.Activity = a;
        var vm = ToVm(a, await HasBookingsAsync(a.Id));
        vm.ExistingAttachments = await AttachmentsAsync(a.Id);
        return View(vm);
    }

    [HttpPost("/partner/programs/edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PartnerProgramVm vm, string? action)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        // Re-checked on POST, not just on GET: a form rendered while the offering was editable must
        // not still be postable after DSC has taken it into review.
        if (!OfferingWorkflow.PartnerCanEdit(a.ApprovalStatus))
        {
            TempData["ToastWarning"] = EditLockMessage(a.ApprovalStatus);
            return RedirectToAction(nameof(Index));
        }

        var hasBookings = await HasBookingsAsync(a.Id);
        vm.Id = a.Id;
        vm.HasBookings = hasBookings;

        if (hasBookings)
        {
            // Clubs have already requested this offering. Season and type are what those requests
            // were made against, so changing them now would silently rewrite booking history; the
            // descriptive fields stay editable. Enforced here, not merely disabled in the form.
            vm.SeasonId = a.SeasonId;
            vm.Type = a.Type;
            ModelState.Remove(nameof(vm.SeasonId));
            ModelState.Remove(nameof(vm.Type));
        }
        else
        {
            await ValidateSeasonAsync(vm);
        }

        // Only attachments that actually belong to this offering may be removed, and only those count
        // towards the limit. An id from another entity's programme is dropped here rather than being
        // allowed to reach the delete.
        var stored = await AttachmentsAsync(a.Id);
        var removing = stored.Where(x => vm.RemoveAttachmentIds.Contains(x.Id)).ToList();
        ValidateAttachments(vm, alreadyStored: stored.Count - removing.Count);

        if (!ModelState.IsValid)
        {
            await PopulateFormAsync(orgIds);
            ViewBag.Activity = a;
            vm.ExistingAttachments = stored;
            return View(vm);
        }

        var old = new { a.SeasonId, a.Type, a.TitleEn, a.TitleAr, a.DescriptionEn, a.DescriptionAr, a.Capacity, a.StartDateTime, a.EndDateTime, a.TargetAudienceCsv, a.AvailableFromUtc, a.AvailableUntilUtc, a.Status, a.ApprovalStatus };

        if (!hasBookings)
        {
            a.SeasonId = vm.SeasonId!.Value;
            a.Type = vm.Type!.Value;
        }
        ApplyEditableFields(a, vm);
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedByUserId = CurrentUserId;

        await Db.SaveChangesAsync();
        await AuditAsync("OfferingUpdated", nameof(Activity), a.Id.ToString(), old,
            new { a.SeasonId, a.Type, a.TitleEn, a.TitleAr, a.DescriptionEn, a.DescriptionAr, a.Capacity, a.StartDateTime, a.EndDateTime, a.TargetAudienceCsv, a.AvailableFromUtc, a.AvailableUntilUtc, a.Status, a.ApprovalStatus });

        await RemoveAttachmentsAsync(a, removing);
        await SaveAttachmentsAsync(a, vm.Attachments);

        if (string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase))
        {
            var resubmission = old.ApprovalStatus != OfferingApprovalStatus.Draft;
            await MarkSubmittedAsync(a, resubmission ? "OfferingResubmitted" : "OfferingSubmitted");
            TempData["ToastSuccess"] = IsAr ? "تم إرسال البرنامج إلى مجلس دبي الرياضي للاعتماد." : "Program submitted to DSC for approval.";
        }
        else
        {
            TempData["ToastSuccess"] = IsAr ? "تم تحديث البرنامج." : "Program updated.";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Submit straight from the list, without opening the form.</summary>
    [HttpPost("/partner/programs/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        if (!OfferingWorkflow.PartnerCanSubmit(a.ApprovalStatus))
        {
            TempData["ToastWarning"] = IsAr ? "لا يمكن إرسال هذا البرنامج في حالته الحالية." : "This program cannot be submitted in its current state.";
            return RedirectToAction(nameof(Index));
        }

        var resubmission = a.ApprovalStatus != OfferingApprovalStatus.Draft;
        await MarkSubmittedAsync(a, resubmission ? "OfferingResubmitted" : "OfferingSubmitted");
        TempData["ToastSuccess"] = IsAr ? "تم إرسال البرنامج إلى مجلس دبي الرياضي للاعتماد." : "Program submitted to DSC for approval.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Withdraw an approved offering from the club catalogue. The partner may do this to their own
    /// content because it only ever removes visibility — it can never create it. Making it visible
    /// again requires a fresh submission and a fresh DSC approval.
    ///
    /// There is no delete: an offering may already anchor booking requests, attendance sessions and
    /// certificates, and existing bookings keep pointing at the row after it is withdrawn.
    /// </summary>
    [HttpPost("/partner/programs/unpublish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        if (!OfferingWorkflow.CanUnpublish(a.ApprovalStatus))
        {
            TempData["ToastWarning"] = IsAr ? "لا يمكن إلغاء نشر برنامج غير معتمد." : "Only an approved program can be unpublished.";
            return RedirectToAction(nameof(Index));
        }

        var old = new { a.Status, a.ApprovalStatus };
        a.Status = ActivityStatus.Draft;
        a.ApprovalStatus = OfferingApprovalStatus.Unpublished;
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync("OfferingUnpublishedByPartner", nameof(Activity), a.Id.ToString(), old, new { a.Status, a.ApprovalStatus });

        TempData["ToastSuccess"] = IsAr ? "تم سحب البرنامج من كتالوج الأندية." : "Program withdrawn from the club catalogue.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ supporting documents
    //
    // Attachments follow the KPI evidence pattern exactly: validated against a named profile, written
    // to the protected store outside wwwroot under a GUID name, and recorded as a row that carries the
    // storage key. Nothing here ever produces a publicly fetchable URL —
    // /protected-files/program-attachment/{id} re-derives who may read each file from the offering's
    // own state, so a document is exactly as visible as the programme it describes.

    private Task<List<ActivityAttachment>> AttachmentsAsync(int activityId)
        => Db.ActivityAttachments
            .Where(x => x.ActivityId == activityId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync();

    /// <summary>
    /// Checks the files on this post before anything is written. Absence is never an error: an
    /// offering has never required a document, and refusing to submit a programme over a missing
    /// brochure would be a rule nobody asked for.
    /// </summary>
    private void ValidateAttachments(PartnerProgramVm vm, int alreadyStored)
    {
        var files = vm.Attachments?.Where(f => f is { Length: > 0 }).ToList() ?? [];
        if (files.Count == 0) return;

        foreach (var file in files)
        {
            var error = FileValidationHelper.Validate(file, FileValidationHelper.ProgramAttachment, IsAr);
            if (error != null) ModelState.AddModelError(nameof(vm.Attachments), error);
        }

        if (alreadyStored + files.Count > PartnerProgramVm.MaxAttachments)
        {
            ModelState.AddModelError(nameof(vm.Attachments), IsAr
                ? $"لا يمكن إرفاق أكثر من {PartnerProgramVm.MaxAttachments} مستندات لكل برنامج."
                : $"A program may carry at most {PartnerProgramVm.MaxAttachments} documents.");
        }
    }

    /// <summary>
    /// Stores validated uploads against an offering the caller has already proved they own. Writes the
    /// file first and the row second, so a failed write can never leave a row pointing at nothing.
    /// </summary>
    private async Task SaveAttachmentsAsync(Activity activity, List<IFormFile>? files)
    {
        var accepted = files?.Where(f => f is { Length: > 0 }).ToList() ?? [];
        if (accepted.Count == 0) return;

        foreach (var file in accepted)
        {
            var key = await ProtectedFileStore.SaveAsync(file, _env, ProtectedFileStore.ProgramAttachments);
            Db.ActivityAttachments.Add(new ActivityAttachment
            {
                ActivityId = activity.Id,
                FilePath = key,
                // Display metadata only. The name on disk is a GUID, and this value is sanitised again
                // by ProtectedFileStore.SafeDownloadName before it reaches a response header.
                OriginalFileName = Path.GetFileName(file.FileName),
                FileSizeBytes = file.Length,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = CurrentUserId
            });
        }

        await Db.SaveChangesAsync();
        await AuditAsync("OfferingAttachmentsAdded", nameof(Activity), activity.Id.ToString(), null,
            new { Count = accepted.Count, Files = accepted.Select(f => Path.GetFileName(f.FileName)).ToArray() });
    }

    /// <summary>
    /// Removes attachments the caller has already been proved to own. The row goes first and the file
    /// second: an orphaned file is recoverable housekeeping, whereas a row left pointing at a deleted
    /// file is a broken download nobody can explain.
    /// </summary>
    private async Task RemoveAttachmentsAsync(Activity activity, List<ActivityAttachment> removing)
    {
        if (removing.Count == 0) return;

        var names = removing.Select(x => x.OriginalFileName).ToArray();
        var keys = removing.Select(x => x.FilePath).ToList();

        Db.ActivityAttachments.RemoveRange(removing);
        await Db.SaveChangesAsync();

        foreach (var key in keys) ProtectedFileStore.TryDelete(key, _env);

        await AuditAsync("OfferingAttachmentsRemoved", nameof(Activity), activity.Id.ToString(),
            new { Count = removing.Count, Files = names }, null);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Display names for the people named on an offering's workflow stamps. Only <c>FullName</c> is
    /// returned: the stored value is a user id, which is internal, and the account's email address is
    /// a contact detail the partner has no reason to receive. A reviewer with no name on file becomes
    /// a role description rather than an identifier.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveDisplayNamesAsync(params string?[] userIds)
    {
        var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await Db.Users.Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToListAsync();

        return users.ToDictionary(
            x => x.Id,
            x => string.IsNullOrWhiteSpace(x.FullName)
                ? (IsAr ? "مستخدم معتمد" : "Authorised user")
                : x.FullName!);
    }

    private string EditLockMessage(OfferingApprovalStatus? s) => OfferingWorkflow.EditLockMessage(s);

    /// <summary>
    /// The single place a submission is recorded, so create-and-submit, edit-and-submit and
    /// submit-from-the-list cannot diverge. Never touches <see cref="Activity.Status"/>: a submitted
    /// offering is still invisible to clubs.
    /// </summary>
    private async Task MarkSubmittedAsync(Activity a, string auditAction)
    {
        var old = new { a.ApprovalStatus, a.SubmittedAtUtc, a.Status };
        a.ApprovalStatus = OfferingApprovalStatus.SubmittedForApproval;
        a.SubmittedAtUtc = DateTime.UtcNow;
        a.SubmittedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync(auditAction, nameof(Activity), a.Id.ToString(), old, new { a.ApprovalStatus, a.SubmittedAtUtc, a.Status });

        // Both names are read, so the Arabic notification names the entity in Arabic. The message is
        // written once and stored in both languages; the recipient's culture decides which is shown.
        var entity = await Db.Organizations.Where(x => x.Id == a.PartnerOrganizationId)
            .Select(x => new { x.NameEn, x.NameAr }).FirstOrDefaultAsync();
        var nameEn = string.IsNullOrWhiteSpace(entity?.NameEn) ? "An implementing entity" : entity!.NameEn;
        var nameAr = string.IsNullOrWhiteSpace(entity?.NameAr) ? "جهة منفذة" : entity!.NameAr!;
        var kindEn = a.Type == ActivityType.Workshop ? "workshop" : "training program";
        var kindAr = a.Type == ActivityType.Workshop ? "ورشة عمل" : "برنامجاً تدريبياً";

        await NotifyReviewersAsync(
            "Program awaiting DSC review", "برنامج بانتظار مراجعة المجلس",
            $"{nameEn} submitted the {kindEn} '{a.TitleEn}' for approval. Your review is required.",
            $"قدمت {nameAr} {kindAr} بعنوان '{a.TitleAr}' للاعتماد. مطلوب مراجعتكم.",
            $"/Admin/Activities/Details/{a.Id}");
    }

    /// <summary>
    /// Role-targeted notification to the reviewers, following the convention already used for KPI
    /// submissions and contact enquiries: delivered to DSC Admins and Super Admins so a site with no
    /// DSC Admin yet cannot lose a submission into a notification nobody receives. No other
    /// organization is addressed.
    /// </summary>
    private async Task NotifyReviewersAsync(string titleEn, string titleAr, string messageEn, string messageAr, string linkUrl)
    {
        var n = new Notification
        {
            TitleEn = titleEn,
            TitleAr = titleAr,
            MessageEn = messageEn,
            MessageAr = messageAr,
            Type = NotificationType.Warning,
            TargetType = NotificationTargetType.Role,
            TargetRoleName = RoleNames.DscAdmin,
            LinkUrl = linkUrl,
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

        await _hub.Clients.All.SendAsync("notificationReceived", new { title = titleEn, message = messageEn, linkUrl });
    }

    /// <summary>The implementing entities this user administers. Copied in shape from
    /// <see cref="PartnerDashboardController"/> so both surfaces agree on who a partner is.</summary>
    private async Task<List<int>> PartnerOrganizationIdsAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return await Db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.UserId == userId && x.Organization != null &&
                        (x.Organization.OrganizationType == OrganizationType.OtherPartner || x.Organization.OrganizationType == OrganizationType.GovernmentAuthority))
            .Select(x => x.OrganizationId)
            .ToListAsync();
    }

    private IQueryable<Activity> OwnedActivities(List<int> orgIds)
        => Db.Activities.Where(x => x.PartnerOrganizationId.HasValue && orgIds.Contains(x.PartnerOrganizationId.Value));

    private Task<bool> HasBookingsAsync(int activityId)
        => Db.BookingRequests.AnyAsync(x => x.ActivityId == activityId);

    private async Task ValidateSeasonAsync(PartnerProgramVm vm)
    {
        if (!vm.SeasonId.HasValue || vm.SeasonId.Value <= 0) return;
        if (!await Db.Seasons.AnyAsync(x => x.Id == vm.SeasonId!.Value && x.IsActive))
            ModelState.AddModelError(nameof(vm.SeasonId), IsAr ? "الموسم الرياضي غير صالح أو غير نشط." : "The selected sports season is not valid or not active.");
    }

    private async Task PopulateFormAsync(List<int> orgIds)
    {
        ViewBag.Seasons = await Db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.Organization = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == orgIds[0]);
    }

    /// <summary>Writes every field a partner may change. Ownership, publication state, approval state
    /// and audit stamps are handled by the caller so that this can never be the thing that reassigns
    /// an offering or makes it visible.</summary>
    private static void ApplyEditableFields(Activity entity, PartnerProgramVm vm)
    {
        entity.TitleEn = vm.TitleEn!.Trim();
        entity.TitleAr = vm.TitleAr!.Trim();
        entity.DescriptionEn = vm.DescriptionEn?.Trim();
        entity.DescriptionAr = vm.DescriptionAr?.Trim();
        entity.CategoryEn = "Partner Program";
        entity.CategoryAr = "برنامج جهة منفذة";
        entity.StartDateTime = vm.SessionDate!.Value.ToDateTime(vm.SessionStartTime!.Value);
        entity.EndDateTime = vm.SessionDate.Value.ToDateTime(vm.SessionEndTime!.Value);
        entity.LocationEn = vm.LocationEn?.Trim() ?? "";
        entity.LocationAr = vm.LocationAr?.Trim() ?? "";
        entity.Capacity = vm.Capacity!.Value;
        entity.TargetAudienceCsv = string.Join(",", vm.TargetAudiences.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        entity.OtherTargetAudience = vm.TargetAudiences.Contains("Others") ? vm.OtherTargetAudience?.Trim() : null;
        entity.AvailableFromUtc = vm.AvailableFrom?.ToDateTime(TimeOnly.MinValue);
        entity.AvailableUntilUtc = vm.AvailableUntil?.ToDateTime(new TimeOnly(23, 59));
    }

    private static PartnerProgramVm ToVm(Activity a, bool hasBookings) => new()
    {
        Id = a.Id,
        SeasonId = a.SeasonId,
        Type = a.Type,
        TitleEn = a.TitleEn,
        TitleAr = a.TitleAr,
        DescriptionEn = a.DescriptionEn,
        DescriptionAr = a.DescriptionAr,
        TargetAudiences = string.IsNullOrWhiteSpace(a.TargetAudienceCsv)
            ? []
            : a.TargetAudienceCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        OtherTargetAudience = a.OtherTargetAudience,
        SessionDate = DateOnly.FromDateTime(a.StartDateTime),
        SessionStartTime = TimeOnly.FromDateTime(a.StartDateTime),
        SessionEndTime = TimeOnly.FromDateTime(a.EndDateTime),
        Capacity = a.Capacity,
        LocationEn = a.LocationEn,
        LocationAr = a.LocationAr,
        AvailableFrom = a.AvailableFromUtc.HasValue ? DateOnly.FromDateTime(a.AvailableFromUtc.Value) : null,
        AvailableUntil = a.AvailableUntilUtc.HasValue ? DateOnly.FromDateTime(a.AvailableUntilUtc.Value) : null,
        HasBookings = hasBookings
    };
}
