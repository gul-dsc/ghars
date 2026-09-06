using GharsPlatform.Data;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class RewardsController : Controllers.BaseController
{
    public RewardsController(AppDbContext db, IWebHostEnvironment env) : base(db) { }

    public IActionResult Index() => NotFound();
    public IActionResult Create() => NotFound();
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(RewardVm vm) => NotFound();
    public IActionResult Redemptions() => NotFound();
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Decide(int id, string action) => NotFound();

    public class RewardVm
    {
        public string TitleEn { get; set; } = "";
        public string TitleAr { get; set; } = "";
        public string? DescriptionEn { get; set; }
        public string? DescriptionAr { get; set; }
        public int PointsRequired { get; set; }
        public bool IsActive { get; set; }
        public IFormFile? Image { get; set; }
    }
}
