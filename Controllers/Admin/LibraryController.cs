using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class LibraryController : Controllers.BaseController
{
    private readonly IWebHostEnvironment _env;

    public LibraryController(AppDbContext db, IWebHostEnvironment env) : base(db)
    {
        _env = env;
    }

    public async Task<IActionResult> Categories()
    {
        var list = await Db.LibraryCategories.OrderBy(x => x.SortOrder).ToListAsync();
        return View(list);
    }

    public IActionResult CreateCategory() => View(new CategoryVm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(CategoryVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        Db.LibraryCategories.Add(new LibraryCategory
        {
            NameEn = vm.NameEn.Trim(),
            NameAr = vm.NameAr.Trim(),
            SortOrder = vm.SortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        });

        await Db.SaveChangesAsync();
        TempData["ToastSuccess"] = "Category created.";
        return RedirectToAction(nameof(Categories));
    }

    public async Task<IActionResult> Items()
    {
        var items = await Db.LibraryItems.Include(x => x.LibraryCategory).OrderByDescending(x => x.Id).Take(200).ToListAsync();
        return View(items);
    }

    public async Task<IActionResult> CreateItem()
    {
        ViewBag.Categories = await Db.LibraryCategories.OrderBy(x => x.SortOrder).ToListAsync();
        return View(new ItemVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateItem(ItemVm vm)
    {
        ViewBag.Categories = await Db.LibraryCategories.OrderBy(x => x.SortOrder).ToListAsync();
        var isAr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

        // Docs: materials are uploaded as PDF; where that is not feasible a cover image plus an
        // external link is required. Publishing entity and publication date are mandatory metadata.
        if ((vm.File is null || vm.File.Length == 0) && (vm.CoverImage is null || vm.CoverImage.Length == 0 || string.IsNullOrWhiteSpace(vm.ExternalUrl)))
        {
            ModelState.AddModelError(nameof(vm.File), isAr
                ? "ارفع ملف PDF، أو ارفع صورة غلاف مع رابط خارجي."
                : "Upload a PDF, or upload a cover image and provide an external link.");
        }

        if (string.IsNullOrWhiteSpace(vm.PublishingEntityEn) && string.IsNullOrWhiteSpace(vm.PublishingEntityAr))
            ModelState.AddModelError(nameof(vm.PublishingEntityEn), isAr ? "اسم الجهة الناشرة مطلوب." : "The publishing entity is required.");

        if (!vm.PublicationDate.HasValue)
            ModelState.AddModelError(nameof(vm.PublicationDate), isAr ? "تاريخ النشر مطلوب." : "The publication date is required.");

        if (!string.IsNullOrWhiteSpace(vm.ExternalUrl) && !FileValidationHelper.IsSafeHttpUrl(vm.ExternalUrl))
            ModelState.AddModelError(nameof(vm.ExternalUrl), isAr ? "الرابط غير صالح. يجب أن يبدأ بـ http أو https." : "Invalid link. Only http/https URLs are allowed.");

        var fileError = FileValidationHelper.Validate(vm.File, FileValidationHelper.LibraryFile, isAr);
        if (fileError != null) ModelState.AddModelError(nameof(vm.File), fileError);
        var coverError = FileValidationHelper.Validate(vm.CoverImage, FileValidationHelper.Image, isAr);
        if (coverError != null) ModelState.AddModelError(nameof(vm.CoverImage), coverError);

        if (!ModelState.IsValid) return View(vm);

        var filePath = vm.File is { Length: > 0 } ? await FileValidationHelper.SaveAsync(vm.File, _env.WebRootPath, "uploads/library") : null;
        var coverPath = vm.CoverImage is { Length: > 0 } ? await FileValidationHelper.SaveAsync(vm.CoverImage, _env.WebRootPath, "uploads/library") : null;

        Db.LibraryItems.Add(new LibraryItem
        {
            LibraryCategoryId = vm.LibraryCategoryId,
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            DescriptionEn = vm.DescriptionEn?.Trim(),
            DescriptionAr = vm.DescriptionAr?.Trim(),
            FilePath = filePath,
            CoverImagePath = coverPath,
            ExternalUrl = vm.ExternalUrl,
            PublishingEntityEn = vm.PublishingEntityEn,
            PublishingEntityAr = vm.PublishingEntityAr,
            PublicationDate = vm.PublicationDate,
            ContentType = vm.ContentType,
            PointsCost = vm.PointsCost,
            IsPublic = vm.IsPublic,
            IsPublished = vm.IsPublished,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        });

        await Db.SaveChangesAsync();
        TempData["ToastSuccess"] = "Library item created.";
        return RedirectToAction(nameof(Items));
    }

    private static async Task<string> SaveFileAsync(IFormFile file, string rootFolder)
    {
        var ext = Path.GetExtension(file.FileName);
        var safeName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(rootFolder, safeName);

        await using var fs = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fs);

        return "/uploads/library/" + safeName;
    }

    public class CategoryVm
    {
        [Required, MaxLength(150)]
        public string NameEn { get; set; } = "";

        [Required, MaxLength(150)]
        public string NameAr { get; set; } = "";

        public int SortOrder { get; set; } = 1;
    }

    public class ItemVm
    {
        [Required]
        public int LibraryCategoryId { get; set; }

        [Required, MaxLength(250)]
        public string TitleEn { get; set; } = "";

        [Required, MaxLength(250)]
        public string TitleAr { get; set; } = "";

        [MaxLength(2000)]
        public string? DescriptionEn { get; set; }

        [MaxLength(2000)]
        public string? DescriptionAr { get; set; }

        public int PointsCost { get; set; } = 0;

        public bool IsPublic { get; set; } = true;

        public bool IsPublished { get; set; } = true;

        public LibraryContentType ContentType { get; set; } = LibraryContentType.EducationalBooklet;

        [MaxLength(250)] public string? PublishingEntityEn { get; set; }
        [MaxLength(250)] public string? PublishingEntityAr { get; set; }
        public DateTime? PublicationDate { get; set; } = DateTime.Today;
        [MaxLength(700)] public string? ExternalUrl { get; set; }
        public IFormFile? File { get; set; }
        public IFormFile? CoverImage { get; set; }
    }
}
