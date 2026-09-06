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
using System.Text.Json;

namespace GharsPlatform.Controllers.Public;

[Authorize(Roles = RoleNames.ClubAdmin)]
public class AgendaController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public AgendaController(AppDbContext db, IWebHostEnvironment env) { _db = db; _env = env; }

    [HttpGet("/agenda")]
    public async Task<IActionResult> Index(int? seasonId, AgendaEntryStatus? status)
    {
        var orgIds = await UserOrgIds();
        var q = _db.AgendaEntries.Include(x=>x.Season).Include(x=>x.Organization).Include(x=>x.Media)
            .Where(x=>orgIds.Contains(x.OrganizationId));
        if (seasonId.HasValue) q = q.Where(x=>x.SeasonId==seasonId.Value);
        if (status.HasValue) q = q.Where(x=>x.Status==status.Value);
        ViewBag.Seasons = await _db.Seasons.OrderByDescending(x=>x.StartDate).ToListAsync();
        ViewBag.SelectedSeasonId = seasonId;
        ViewBag.SelectedStatus = status;
        return View(await q.OrderByDescending(x=>x.ActivityDate).Take(200).ToListAsync());
    }

    [HttpGet("/agenda/create")]
    public async Task<IActionResult> Create()
    {
        await LoadLookups();
        var clubs = ViewBag.Clubs as List<Organization> ?? new();
        var activeSeason = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        return View(new AgendaVm
        {
            ActivityDate = DateTime.Today,
            Status = AgendaEntryStatus.Submitted,
            OrganizationId = clubs.Count == 1 ? clubs[0].Id : 0,
            SeasonId = activeSeason?.Id ?? 0
        });
    }

    [HttpPost("/agenda/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AgendaVm vm)
    {
        await LoadLookups();
        await ApplyClubScopeAsync(vm);
        await ValidateMediaAsync(vm.MediaFiles);
        if (!ModelState.IsValid) return View(vm);

        var entry = new AgendaEntry{
            SeasonId=vm.SeasonId, OrganizationId=vm.OrganizationId, ActivityType=vm.ActivityType,
            SubjectEn=vm.SubjectEn.Trim(), SubjectAr=vm.SubjectAr.Trim(), ActivityDate=vm.ActivityDate,
            Category=vm.Category, OtherCategory=vm.OtherCategory?.Trim(), LecturerName=vm.LecturerName.Trim(),
            DepartmentOrOrganization=vm.DepartmentOrOrganization, NumberOfParticipants=vm.NumberOfParticipants,
            Status=vm.SaveAsDraft?AgendaEntryStatus.Draft:AgendaEntryStatus.Submitted, Notes=vm.Notes,
            CreatedAtUtc=DateTime.UtcNow, CreatedByUserId=User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        _db.AgendaEntries.Add(entry);
        await _db.SaveChangesAsync();
        await SaveAgendaMedia(entry, vm.MediaFiles);
        await AuditAsync(vm.SaveAsDraft ? "AgendaDraftCreated" : "AgendaSubmitted", entry.Id, null, new { entry.Status, entry.SubjectEn, entry.ActivityDate, entry.NumberOfParticipants });
        TempData["ToastSuccess"] = vm.SaveAsDraft
            ? IsAr() ? "تم حفظ النشاط كمسودة." : "Agenda saved as draft."
            : IsAr() ? "تم إرسال النشاط." : "Agenda entry submitted.";
        return RedirectToAction(nameof(Index));
    }

    // Actual-delivery data is completed here: confirmed bookings pre-create a DRAFT entry which the
    // club edits with the real date, lecturer, participant count and supporting media before submitting.
    [HttpGet("/agenda/edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var orgIds = await UserOrgIds();
        var entry = await _db.AgendaEntries.Include(x => x.Media)
            .FirstOrDefaultAsync(x => x.Id == id && orgIds.Contains(x.OrganizationId));
        if (entry is null) return NotFound();
        if (entry.Status is AgendaEntryStatus.Approved)
        {
            TempData["ToastWarning"] = IsAr() ? "لا يمكن تعديل نشاط معتمد." : "An approved agenda entry cannot be edited.";
            return RedirectToAction(nameof(Index));
        }

        await LoadLookups();
        ViewBag.ExistingMedia = entry.Media.ToList();
        return View(new AgendaVm
        {
            Id = entry.Id,
            SeasonId = entry.SeasonId,
            OrganizationId = entry.OrganizationId,
            ActivityType = entry.ActivityType,
            SubjectEn = entry.SubjectEn,
            SubjectAr = entry.SubjectAr,
            ActivityDate = entry.ActivityDate,
            Category = entry.Category,
            OtherCategory = entry.OtherCategory,
            LecturerName = entry.LecturerName,
            DepartmentOrOrganization = entry.DepartmentOrOrganization,
            NumberOfParticipants = entry.NumberOfParticipants,
            Notes = entry.Notes,
            Status = entry.Status
        });
    }

    [HttpPost("/agenda/edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AgendaVm vm)
    {
        var orgIds = await UserOrgIds();
        var entry = await _db.AgendaEntries.Include(x => x.Media)
            .FirstOrDefaultAsync(x => x.Id == id && orgIds.Contains(x.OrganizationId));
        if (entry is null) return NotFound();
        if (entry.Status is AgendaEntryStatus.Approved)
        {
            TempData["ToastWarning"] = IsAr() ? "لا يمكن تعديل نشاط معتمد." : "An approved agenda entry cannot be edited.";
            return RedirectToAction(nameof(Index));
        }

        await LoadLookups();
        await ApplyClubScopeAsync(vm);
        await ValidateMediaAsync(vm.MediaFiles);
        if (!ModelState.IsValid)
        {
            ViewBag.ExistingMedia = entry.Media.ToList();
            return View(vm);
        }

        var old = new { entry.SubjectEn, entry.ActivityDate, entry.LecturerName, entry.NumberOfParticipants, entry.Status };
        entry.SeasonId = vm.SeasonId;
        entry.ActivityType = vm.ActivityType;
        entry.SubjectEn = vm.SubjectEn.Trim();
        entry.SubjectAr = vm.SubjectAr.Trim();
        entry.ActivityDate = vm.ActivityDate;
        entry.Category = vm.Category;
        entry.OtherCategory = vm.OtherCategory?.Trim();
        entry.LecturerName = vm.LecturerName.Trim();
        entry.DepartmentOrOrganization = vm.DepartmentOrOrganization;
        entry.NumberOfParticipants = vm.NumberOfParticipants;
        entry.Notes = vm.Notes;
        entry.Status = vm.SaveAsDraft ? AgendaEntryStatus.Draft : AgendaEntryStatus.Submitted;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        entry.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync();
        await SaveAgendaMedia(entry, vm.MediaFiles);
        await AuditAsync(vm.SaveAsDraft ? "AgendaDraftUpdated" : "AgendaSubmitted", entry.Id, old, new { entry.SubjectEn, entry.ActivityDate, entry.LecturerName, entry.NumberOfParticipants, entry.Status });

        TempData["ToastSuccess"] = vm.SaveAsDraft
            ? IsAr() ? "تم تحديث المسودة." : "Draft updated."
            : IsAr() ? "تم إرسال النشاط للاعتماد." : "Agenda entry submitted for approval.";
        return RedirectToAction(nameof(Index));
    }

    private bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    /// <summary>
    /// The club is derived from the authenticated user's organization membership. A single-club user
    /// can never post a different club id; multi-club users are restricted to their own clubs.
    /// </summary>
    private async Task ApplyClubScopeAsync(AgendaVm vm)
    {
        var orgIds = await UserOrgIds();
        if (orgIds.Count == 1)
        {
            vm.OrganizationId = orgIds[0];
            ModelState.Remove(nameof(vm.OrganizationId));
        }
        else if (!orgIds.Contains(vm.OrganizationId))
        {
            ModelState.AddModelError(nameof(vm.OrganizationId), IsAr() ? "النادي غير صالح." : "Invalid club.");
        }

        if (vm.Category == AgendaTargetCategory.Others && string.IsNullOrWhiteSpace(vm.OtherCategory))
        {
            ModelState.AddModelError(nameof(vm.OtherCategory), IsAr() ? "يرجى تحديد الفئة الأخرى." : "Please specify the other category.");
        }

        if (!await _db.Seasons.AnyAsync(x => x.Id == vm.SeasonId))
        {
            ModelState.AddModelError(nameof(vm.SeasonId), IsAr() ? "الموسم الرياضي غير صالح." : "Invalid sports season.");
        }
    }

    private async Task ValidateMediaAsync(List<IFormFile>? files)
    {
        if (files == null) return;
        foreach (var file in files.Where(f => f.Length > 0))
        {
            var error = FileValidationHelper.Validate(file, FileValidationHelper.Media, IsAr());
            if (error != null) ModelState.AddModelError(nameof(AgendaVm.MediaFiles), error);
        }
        await Task.CompletedTask;
    }

    // Supporting media is stored once and referenced by both the agenda entry and the Ghars gallery
    // (docs: club media uploaded through the agenda is automatically aggregated into the gallery).
    private async Task SaveAgendaMedia(AgendaEntry entry, List<IFormFile>? files)
    {
        if (files == null || files.Count == 0) return;
        foreach (var file in files.Where(f=>f.Length>0))
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var path = await FileValidationHelper.SaveAsync(file, _env.WebRootPath, "uploads/agenda");
            var mediaType = ext is ".mp4" or ".webm" or ".mov" ? GalleryMediaType.Video : GalleryMediaType.Photo;
            _db.AgendaMedia.Add(new AgendaMedia{ AgendaEntryId=entry.Id, MediaType=mediaType, FilePath=path, TitleEn=entry.SubjectEn, TitleAr=entry.SubjectAr, IsPublished=true });
            _db.GalleryItems.Add(new GalleryItem{ TitleEn=entry.SubjectEn, TitleAr=entry.SubjectAr, DescriptionEn=entry.Notes, DescriptionAr=entry.Notes, MediaType=mediaType, FilePath=path, OrganizationId=entry.OrganizationId, AgendaEntryId=entry.Id, SeasonId=entry.SeasonId, MediaDate=entry.ActivityDate, IsPublished=true, CreatedAtUtc=DateTime.UtcNow, CreatedByUserId=entry.CreatedByUserId });
        }
        await _db.SaveChangesAsync();
    }

    private async Task AuditAsync(string action, int entryId, object? oldValues, object? newValues)
    {
        try
        {
            _db.SystemAuditLogs.Add(new SystemAuditLog
            {
                Action = action,
                EntityName = nameof(AgendaEntry),
                EntityId = entryId.ToString(),
                UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                OldValuesJson = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
                NewValuesJson = newValues is null ? null : JsonSerializer.Serialize(newValues),
                AtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch { /* non-blocking audit */ }
    }

    private async Task LoadLookups()
    {
        var orgIds = await UserOrgIds();
        ViewBag.Seasons = await _db.Seasons.OrderByDescending(x=>x.StartDate).ToListAsync();
        ViewBag.Clubs = await _db.Organizations.Where(x=>orgIds.Contains(x.Id)).OrderBy(x=>x.NameEn).ToListAsync();
    }
    private async Task<List<int>> UserOrgIds() => await _db.OrganizationAdminLinks.Where(x=>x.UserId==(User.FindFirstValue(ClaimTypes.NameIdentifier)??"")).Select(x=>x.OrganizationId).ToListAsync();

    public class AgendaVm
    {
        public int Id { get; set; }
        [Required] public int SeasonId { get; set; }
        public int OrganizationId { get; set; }
        public ActivityType ActivityType { get; set; } = ActivityType.Lecture;
        [Required, MaxLength(250)] public string SubjectEn { get; set; } = "";
        [Required, MaxLength(250)] public string SubjectAr { get; set; } = "";
        public DateTime ActivityDate { get; set; }
        public AgendaTargetCategory Category { get; set; } = AgendaTargetCategory.Players;
        [MaxLength(150)] public string? OtherCategory { get; set; }
        [Required, MaxLength(200)] public string LecturerName { get; set; } = "";
        [MaxLength(250)] public string? DepartmentOrOrganization { get; set; }
        [Range(0,100000)] public int NumberOfParticipants { get; set; }
        [MaxLength(2000)] public string? Notes { get; set; }
        public bool SaveAsDraft { get; set; }
        public AgendaEntryStatus Status { get; set; }
        public List<IFormFile>? MediaFiles { get; set; }
    }
}
