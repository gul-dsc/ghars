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
public class NewsController : Controllers.BaseController
{
    public NewsController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var list = await Db.NewsItems.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Id).ToListAsync();
        return View(list);
    }

    public IActionResult Create() => View(new NewsVm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NewsVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var n = new NewsItem
        {
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            Url = vm.Url?.Trim(),
            IsActive = vm.IsActive,
            StartsAtUtc = vm.StartsAtUtc,
            EndsAtUtc = vm.EndsAtUtc,
            Priority = vm.Priority,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        Db.NewsItems.Add(n);
        await Db.SaveChangesAsync();
        TempData["ToastSuccess"] = "News item created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var n = await Db.NewsItems.FirstOrDefaultAsync(x => x.Id == id);
        if (n is null) return NotFound();
        n.IsActive = !n.IsActive;
        n.UpdatedAtUtc = DateTime.UtcNow;
        n.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public class NewsVm
    {
        [Required, MaxLength(200)]
        public string TitleEn { get; set; } = "";

        [Required, MaxLength(200)]
        public string TitleAr { get; set; } = "";

        [MaxLength(600)]
        public string? Url { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }

        public int Priority { get; set; } = 0;
    }
}
