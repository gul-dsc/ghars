using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class ActivitiesController : Controllers.BaseController
{
    public ActivitiesController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var list = await Db.Activities.Include(x => x.Season)
            .OrderByDescending(x => x.StartDateTime)
            .ToListAsync();
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
        return View(activity);
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
            ModelState.AddModelError(nameof(vm.EndDateTime), "End time must be after start time.");
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

        TempData["ToastSuccess"] = "Activity created.";
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

        TempData["ToastSuccess"] = "Activity updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Publish(int id)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        var old = new { a.Status };
        a.Status = ActivityStatus.Published;
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedByUserId = CurrentUserId;

        await Db.SaveChangesAsync();
        await AuditAsync("Publish", nameof(Activity), id.ToString(), old, new { a.Status });

        TempData["ToastSuccess"] = "Activity published.";
        return RedirectToAction(nameof(Index));
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();
        if (await Db.BookingRequests.AnyAsync(x => x.ActivityId == id))
        {
            a.Status = ActivityStatus.Cancelled;
            a.UpdatedAtUtc = DateTime.UtcNow;
            a.UpdatedByUserId = CurrentUserId;
        }
        else
        {
            Db.Activities.Remove(a);
        }
        await Db.SaveChangesAsync();
        await AuditAsync("Delete", nameof(Activity), id.ToString(), a, null);
        TempData["ToastWarning"] = "Activity deleted or cancelled when linked records exist.";
        return RedirectToAction(nameof(Index));
    }

    public class ActivityVm
    {
        public int Id { get; set; }

        [Required]
        public int SeasonId { get; set; }

        [Required]
        public ActivityType Type { get; set; } = ActivityType.Lecture;

        [Required, MaxLength(250)]
        public string TitleEn { get; set; } = "";

        [Required, MaxLength(250)]
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

        [Required, MaxLength(300)]
        public string LocationEn { get; set; } = "";

        [Required, MaxLength(300)]
        public string LocationAr { get; set; } = "";

        [Range(1, 5000)]
        public int Capacity { get; set; } = 50;

        public bool AllowWalkIn { get; set; } = false;

        [Required]
        public ActivityStatus Status { get; set; } = ActivityStatus.Draft;
    }
}
