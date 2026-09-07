using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
/// Note the ownership test used here is stricter than the one
/// <see cref="PartnerDashboardController"/> uses for reading: that one also matches on
/// CreatedByUserId, which is a reasonable fallback for display but would be a weak basis for edit
/// rights. Management requires the explicit foreign key.
/// </summary>
[Authorize(Roles = RoleNames.PartnerAdmin)]
public class PartnerProgramsController : Controllers.BaseController
{
    public PartnerProgramsController(AppDbContext db) : base(db) { }

    private bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    [HttpGet("/partner/programs")]
    public async Task<IActionResult> Index(ActivityType? type = null, ActivityStatus? status = null, string? q = null)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var query = OwnedActivities(orgIds);
        if (type.HasValue) query = query.Where(x => x.Type == type.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
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

        ViewBag.Organization = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == orgIds[0]);
        ViewBag.Type = type;
        ViewBag.Status = status;
        ViewBag.Query = q;
        return View(programs);
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

        if (!ModelState.IsValid)
        {
            await PopulateFormAsync(orgIds);
            return View(vm);
        }

        var publish = string.Equals(action, "publish", StringComparison.OrdinalIgnoreCase);
        var entity = new Activity
        {
            // Ownership is server-derived. This is the only place it is ever assigned.
            PartnerOrganizationId = orgIds[0],
            SeasonId = vm.SeasonId!.Value,
            Type = vm.Type!.Value,
            Status = publish ? ActivityStatus.Published : ActivityStatus.Draft,
            CreatedByUserId = CurrentUserId ?? "",
            CreatedAtUtc = DateTime.UtcNow
        };
        ApplyEditableFields(entity, vm);

        Db.Activities.Add(entity);
        await Db.SaveChangesAsync();
        await AuditAsync(publish ? "CreateAndPublish" : "Create", nameof(Activity), entity.Id.ToString(), null,
            new { entity.PartnerOrganizationId, entity.SeasonId, entity.Type, entity.Status, entity.TitleEn, entity.TitleAr });

        TempData["ToastSuccess"] = publish
            ? (IsAr ? "تم إنشاء البرنامج ونشره." : "Program created and published.")
            : (IsAr ? "تم حفظ البرنامج كمسودة." : "Program saved as a draft.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/partner/programs/edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        await PopulateFormAsync(orgIds);
        ViewBag.Activity = a;
        return View(ToVm(a, await HasBookingsAsync(a.Id)));
    }

    [HttpPost("/partner/programs/edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PartnerProgramVm vm, string? action)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

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

        if (!ModelState.IsValid)
        {
            await PopulateFormAsync(orgIds);
            ViewBag.Activity = a;
            return View(vm);
        }

        var old = new { a.SeasonId, a.Type, a.TitleEn, a.TitleAr, a.DescriptionEn, a.DescriptionAr, a.Capacity, a.StartDateTime, a.EndDateTime, a.TargetAudienceCsv, a.AvailableFromUtc, a.AvailableUntilUtc, a.Status };

        if (!hasBookings)
        {
            a.SeasonId = vm.SeasonId!.Value;
            a.Type = vm.Type!.Value;
        }
        ApplyEditableFields(a, vm);
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedByUserId = CurrentUserId;

        if (string.Equals(action, "publish", StringComparison.OrdinalIgnoreCase)) a.Status = ActivityStatus.Published;
        else if (string.Equals(action, "unpublish", StringComparison.OrdinalIgnoreCase)) a.Status = ActivityStatus.Draft;

        await Db.SaveChangesAsync();
        await AuditAsync("Update", nameof(Activity), a.Id.ToString(), old,
            new { a.SeasonId, a.Type, a.TitleEn, a.TitleAr, a.DescriptionEn, a.DescriptionAr, a.Capacity, a.StartDateTime, a.EndDateTime, a.TargetAudienceCsv, a.AvailableFromUtc, a.AvailableUntilUtc, a.Status });

        TempData["ToastSuccess"] = IsAr ? "تم تحديث البرنامج." : "Program updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/partner/programs/publish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Publish(int id) => SetStatusAsync(id, ActivityStatus.Published);

    [HttpPost("/partner/programs/unpublish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Unpublish(int id) => SetStatusAsync(id, ActivityStatus.Draft);

    /// <summary>
    /// Publish and unpublish are the only status transitions offered. There is no delete: an
    /// offering may already anchor booking requests, attendance sessions and certificates, and
    /// removing it would break that history. Unpublishing withdraws it from the club catalogue,
    /// which is what "no longer offered" actually means here.
    /// </summary>
    private async Task<IActionResult> SetStatusAsync(int id, ActivityStatus status)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var a = await OwnedActivities(orgIds).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        var old = new { a.Status };
        a.Status = status;
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync(status == ActivityStatus.Published ? "Publish" : "Unpublish", nameof(Activity), a.Id.ToString(), old, new { a.Status });

        TempData["ToastSuccess"] = status == ActivityStatus.Published
            ? (IsAr ? "تم نشر البرنامج." : "Program published.")
            : (IsAr ? "تم إلغاء نشر البرنامج." : "Program unpublished.");
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ helpers

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

    /// <summary>Writes every field a partner may change. Ownership, status and audit stamps are
    /// handled by the caller so that this can never be the thing that reassigns an offering.</summary>
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
