using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles=$"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class GalleryController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public GalleryController(AppDbContext db, IWebHostEnvironment env){_db=db;_env=env;}

    public async Task<IActionResult> Index(string? keyword, int? seasonId, int? organizationId, int? activityId, bool? isPublic)
    {
        var q = _db.MediaAlbums
            .Include(x=>x.Season)
            .Include(x=>x.Organization)
            .Include(x=>x.Activity)
            .Include(x=>x.Items)
            .AsQueryable();
        if(!string.IsNullOrWhiteSpace(keyword)) q = q.Where(x => x.TitleEn.Contains(keyword) || x.TitleAr.Contains(keyword) || (x.DescriptionEn!=null && x.DescriptionEn.Contains(keyword)) || (x.DescriptionAr!=null && x.DescriptionAr.Contains(keyword)));
        if(seasonId.HasValue) q = q.Where(x=>x.SeasonId==seasonId);
        if(organizationId.HasValue) q = q.Where(x=>x.OrganizationId==organizationId);
        if(activityId.HasValue) q = q.Where(x=>x.ActivityId==activityId);
        if(isPublic.HasValue) q = q.Where(x=>x.IsPublic==isPublic);
        await Lookups();
        return View(await q.OrderByDescending(x=>x.AlbumDate).Take(300).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var album = await _db.MediaAlbums
            .Include(x=>x.Season)
            .Include(x=>x.Organization)
            .Include(x=>x.Activity)
            .Include(x=>x.Items.OrderBy(i=>i.SortOrder))
            .FirstOrDefaultAsync(x=>x.Id==id);
        if(album==null) return NotFound();
        return View(album);
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Create()
    {
        await Lookups();
        return View(new AlbumVm{AlbumDate=DateTime.Today, IsPublic=true});
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Create(AlbumVm vm)
    {
        await Lookups();
        if(!ModelState.IsValid) return View(vm);
        var coverPath = await SaveFile(vm.CoverImage, "gallery/covers");
        var album = new MediaAlbum{
            TitleEn=vm.TitleEn,
            TitleAr=vm.TitleAr,
            DescriptionEn=vm.DescriptionEn,
            DescriptionAr=vm.DescriptionAr,
            CoverImagePath=coverPath,
            SeasonId=vm.SeasonId,
            OrganizationId=vm.OrganizationId,
            ActivityId=vm.ActivityId,
            AlbumDate=vm.AlbumDate,
            IsPublic=vm.IsPublic,
            CreatedAtUtc=DateTime.UtcNow,
            CreatedByUserId=User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        _db.MediaAlbums.Add(album);
        await _db.SaveChangesAsync();
        TempData["ToastSuccess"]="Gallery album created.";
        return RedirectToAction(nameof(Details), new { id = album.Id });
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(int id)
    {
        var album = await _db.MediaAlbums.FirstOrDefaultAsync(x=>x.Id==id);
        if(album==null) return NotFound();
        await Lookups();
        return View(new AlbumVm{
            TitleEn=album.TitleEn,
            TitleAr=album.TitleAr,
            DescriptionEn=album.DescriptionEn,
            DescriptionAr=album.DescriptionAr,
            SeasonId=album.SeasonId,
            OrganizationId=album.OrganizationId,
            ActivityId=album.ActivityId,
            AlbumDate=album.AlbumDate,
            IsPublic=album.IsPublic
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(int id, AlbumVm vm)
    {
        var album = await _db.MediaAlbums.FirstOrDefaultAsync(x=>x.Id==id);
        if(album==null) return NotFound();
        await Lookups();
        if(!ModelState.IsValid) return View(vm);
        var coverPath = await SaveFile(vm.CoverImage, "gallery/covers");
        album.TitleEn=vm.TitleEn;
        album.TitleAr=vm.TitleAr;
        album.DescriptionEn=vm.DescriptionEn;
        album.DescriptionAr=vm.DescriptionAr;
        if(!string.IsNullOrWhiteSpace(coverPath)) album.CoverImagePath=coverPath;
        album.SeasonId=vm.SeasonId;
        album.OrganizationId=vm.OrganizationId;
        album.ActivityId=vm.ActivityId;
        album.AlbumDate=vm.AlbumDate;
        album.IsPublic=vm.IsPublic;
        album.UpdatedAtUtc=DateTime.UtcNow;
        album.UpdatedByUserId=User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync();
        TempData["ToastSuccess"]="Gallery album updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> AddItem(int albumId)
    {
        var album = await _db.MediaAlbums.FirstOrDefaultAsync(x=>x.Id==albumId);
        if(album==null) return NotFound();
        ViewBag.Album = album;
        return View(new MediaItemVm{AlbumId=albumId, SortOrder=1});
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> AddItem(MediaItemVm vm)
    {
        var album = await _db.MediaAlbums.FirstOrDefaultAsync(x=>x.Id==vm.AlbumId);
        if(album==null) return NotFound();
        ViewBag.Album = album;
        if(!ModelState.IsValid) return View(vm);
        var filePath = await SaveFile(vm.File, "gallery/items");
        _db.MediaItems.Add(new MediaItem{
            AlbumId=vm.AlbumId,
            MediaType=vm.MediaType,
            FilePath=filePath,
            VideoUrl=vm.ExternalUrl,
            CaptionEn=vm.CaptionEn,
            CaptionAr=vm.CaptionAr,
            SortOrder=vm.SortOrder,
            CreatedAtUtc=DateTime.UtcNow,
            CreatedByUserId=User.FindFirstValue(ClaimTypes.NameIdentifier)
        });
        await _db.SaveChangesAsync();
        TempData["ToastSuccess"]="Media item added.";
        return RedirectToAction(nameof(Details), new { id = vm.AlbumId });
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> EditItem(int id)
    {
        var item = await _db.MediaItems.Include(x=>x.Album).FirstOrDefaultAsync(x=>x.Id==id);
        if(item==null) return NotFound();
        ViewBag.Album = item.Album;
        return View(new MediaItemVm{
            AlbumId=item.AlbumId,
            MediaType=item.MediaType,
            ExternalUrl=item.VideoUrl,
            CaptionEn=item.CaptionEn,
            CaptionAr=item.CaptionAr,
            SortOrder=item.SortOrder
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> EditItem(int id, MediaItemVm vm)
    {
        var item = await _db.MediaItems.Include(x=>x.Album).FirstOrDefaultAsync(x=>x.Id==id);
        if(item==null) return NotFound();
        ViewBag.Album = item.Album;
        if(!ModelState.IsValid) return View(vm);
        var filePath = await SaveFile(vm.File, "gallery/items");
        item.MediaType=vm.MediaType;
        if(!string.IsNullOrWhiteSpace(filePath)) item.FilePath=filePath;
        item.VideoUrl=vm.ExternalUrl;
        item.CaptionEn=vm.CaptionEn;
        item.CaptionAr=vm.CaptionAr;
        item.SortOrder=vm.SortOrder;
        item.UpdatedAtUtc=DateTime.UtcNow;
        item.UpdatedByUserId=User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync();
        TempData["ToastSuccess"]="Media item updated.";
        return RedirectToAction(nameof(Details), new { id = item.AlbumId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> DeleteItem(int id)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(x=>x.Id==id);
        if(item==null) return NotFound();
        var albumId = item.AlbumId;
        _db.MediaItems.Remove(item);
        await _db.SaveChangesAsync();
        TempData["ToastWarning"]="Media item deleted.";
        return RedirectToAction(nameof(Details), new { id = albumId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        var album = await _db.MediaAlbums.Include(x=>x.Items).FirstOrDefaultAsync(x=>x.Id==id);
        if(album==null) return NotFound();
        _db.MediaItems.RemoveRange(album.Items);
        _db.MediaAlbums.Remove(album);
        await _db.SaveChangesAsync();
        TempData["ToastWarning"]="Gallery album deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------------
    // Activity & press media (GalleryItem): DSC-uploaded official photos/videos and press coverage,
    // plus the club media aggregated automatically from Agenda entries.
    // ---------------------------------------------------------------------

    public async Task<IActionResult> MediaItems(int? seasonId, int? organizationId, GalleryMediaType? mediaType, string? source)
    {
        var q = _db.GalleryItems.Include(x => x.Organization).Include(x => x.Season).Include(x => x.AgendaEntry).AsQueryable();
        if (seasonId.HasValue) q = q.Where(x => x.SeasonId == seasonId);
        if (organizationId.HasValue) q = q.Where(x => x.OrganizationId == organizationId);
        if (mediaType.HasValue) q = q.Where(x => x.MediaType == mediaType);
        if (source == "agenda") q = q.Where(x => x.AgendaEntryId != null);
        if (source == "dsc") q = q.Where(x => x.AgendaEntryId == null);
        await Lookups();
        ViewBag.SelectedSeasonId = seasonId; ViewBag.SelectedOrganizationId = organizationId;
        ViewBag.SelectedMediaType = mediaType; ViewBag.SelectedSource = source;
        return View(await q.OrderByDescending(x => x.MediaDate).Take(300).ToListAsync());
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> CreateMediaItem()
    {
        await Lookups();
        var activeSeason = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        return View(new GalleryItemVm { MediaDate = DateTime.Today, IsPublished = true, SeasonId = activeSeason?.Id ?? 0, MediaType = GalleryMediaType.OfficialPhoto });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> CreateMediaItem(GalleryItemVm vm)
    {
        await Lookups();
        var isAr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

        var fileError = FileValidationHelper.Validate(vm.File, FileValidationHelper.Media, isAr);
        if (fileError != null) ModelState.AddModelError(nameof(vm.File), fileError);

        var hasFile = vm.File is { Length: > 0 };
        if (!hasFile && string.IsNullOrWhiteSpace(vm.ExternalUrl))
            ModelState.AddModelError(nameof(vm.File), isAr ? "يرجى رفع ملف أو إدخال رابط خارجي." : "Upload a file or provide an external link.");

        if (!string.IsNullOrWhiteSpace(vm.ExternalUrl) && !FileValidationHelper.IsSafeHttpUrl(vm.ExternalUrl))
            ModelState.AddModelError(nameof(vm.ExternalUrl), isAr ? "الرابط غير صالح. يجب أن يبدأ بـ http أو https." : "Invalid link. Only http/https URLs are allowed.");

        if (!await _db.Seasons.AnyAsync(x => x.Id == vm.SeasonId))
            ModelState.AddModelError(nameof(vm.SeasonId), isAr ? "الموسم الرياضي غير صالح." : "Invalid sports season.");

        if (!ModelState.IsValid) return View(vm);

        var path = hasFile ? await FileValidationHelper.SaveAsync(vm.File!, _env.WebRootPath, "uploads/gallery/items") : null;
        _db.GalleryItems.Add(new GalleryItem
        {
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            DescriptionEn = vm.DescriptionEn?.Trim(),
            DescriptionAr = vm.DescriptionAr?.Trim(),
            MediaType = vm.MediaType,
            FilePath = path,
            ExternalUrl = string.IsNullOrWhiteSpace(vm.ExternalUrl) ? null : vm.ExternalUrl.Trim(),
            OrganizationId = vm.OrganizationId,
            SeasonId = vm.SeasonId,
            MediaDate = vm.MediaDate,
            IsPublished = vm.IsPublished,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        });
        await _db.SaveChangesAsync();
        TempData["ToastSuccess"] = isAr ? "تمت إضافة الوسائط." : "Media item added.";
        return RedirectToAction(nameof(MediaItems));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> ToggleMediaItem(int id)
    {
        var item = await _db.GalleryItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        item.IsPublished = !item.IsPublished;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync();
        TempData["ToastSuccess"] = item.IsPublished ? "Media published." : "Media hidden.";
        return RedirectToAction(nameof(MediaItems));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> DeleteMediaItem(int id)
    {
        var item = await _db.GalleryItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        // Agenda-originated media is only unpublished here: the file belongs to the club's agenda record.
        if (item.AgendaEntryId.HasValue)
        {
            item.IsPublished = false;
            item.UpdatedAtUtc = DateTime.UtcNow;
            item.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            TempData["ToastWarning"] = "Agenda media hidden from the gallery (the club's agenda record is preserved).";
        }
        else
        {
            _db.GalleryItems.Remove(item);
            TempData["ToastWarning"] = "Media item deleted.";
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(MediaItems));
    }

    private async Task<string?> SaveFile(IFormFile? file, string folder)
    {
        if(file==null || file.Length==0) return null;
        var root = Path.Combine(_env.WebRootPath, "uploads", folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(root);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safe = $"{Guid.NewGuid():N}{ext}";
        await using var fs = new FileStream(Path.Combine(root, safe), FileMode.Create);
        await file.CopyToAsync(fs);
        return "/uploads/" + folder.Trim('/') + "/" + safe;
    }

    private async Task Lookups()
    {
        ViewBag.Seasons = await _db.Seasons.OrderByDescending(x=>x.StartDate).ToListAsync();
        ViewBag.Organizations = await _db.Organizations.OrderBy(x=>x.NameEn).ToListAsync();
        ViewBag.Activities = await _db.Activities.OrderByDescending(x=>x.StartDateTime).Take(300).ToListAsync();
    }

    public class AlbumVm
    {
        [Required] public string TitleEn { get; set; } = "";
        [Required] public string TitleAr { get; set; } = "";
        public string? DescriptionEn { get; set; }
        public string? DescriptionAr { get; set; }
        public IFormFile? CoverImage { get; set; }
        public int? SeasonId { get; set; }
        public int? OrganizationId { get; set; }
        public int? ActivityId { get; set; }
        public DateTime AlbumDate { get; set; } = DateTime.Today;
        public bool IsPublic { get; set; } = true;
    }

    public class GalleryItemVm
    {
        [Required, MaxLength(250)] public string TitleEn { get; set; } = "";
        [Required, MaxLength(250)] public string TitleAr { get; set; } = "";
        [MaxLength(2000)] public string? DescriptionEn { get; set; }
        [MaxLength(2000)] public string? DescriptionAr { get; set; }
        public GalleryMediaType MediaType { get; set; } = GalleryMediaType.OfficialPhoto;
        public IFormFile? File { get; set; }
        [MaxLength(700)] public string? ExternalUrl { get; set; }
        public int? OrganizationId { get; set; }
        [Required] public int SeasonId { get; set; }
        public DateTime MediaDate { get; set; } = DateTime.Today;
        public bool IsPublished { get; set; } = true;
    }

    public class MediaItemVm
    {
        [Required] public int AlbumId { get; set; }
        public MediaType MediaType { get; set; } = MediaType.Image;
        public IFormFile? File { get; set; }
        public string? ExternalUrl { get; set; }
        public string? CaptionEn { get; set; }
        public string? CaptionAr { get; set; }
        public int SortOrder { get; set; } = 1;
    }
}
