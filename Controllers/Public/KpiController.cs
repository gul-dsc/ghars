using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace GharsPlatform.Controllers.Public;

/// <summary>
/// Club data-entry for Ghars reports and statistics.
/// Workflow: Draft -> Submitted -> DSC review -> Approved / Returned (MoreInfoRequired) / Rejected.
/// Indicators the platform already knows reliably (activities, participants, lecturers, implementing
/// entities) are derived from the club's Agenda for the season; the rest are club-submitted.
/// </summary>
[Authorize(Roles = RoleNames.ClubAdmin)]
public class KpiController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IHubContext<NotificationsHub> _hub;

    public KpiController(AppDbContext db, IWebHostEnvironment env, IHubContext<NotificationsHub> hub)
    {
        _db = db; _env = env; _hub = hub;
    }

    private static bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    [HttpGet("/kpi")]
    public async Task<IActionResult> Index()
    {
        var ids = await UserOrgIds();
        ViewBag.Seasons = await _db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        return View(await _db.KpiSubmissions
            .Include(x => x.Season).Include(x => x.Organization).Include(x => x.Documents)
            .Where(x => ids.Contains(x.OrganizationId))
            .OrderByDescending(x => x.Id)
            .ToListAsync());
    }

    [HttpGet("/kpi/create")]
    public async Task<IActionResult> Create(int? seasonId)
    {
        await Lookups();
        var orgIds = await UserOrgIds();
        var activeSeason = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        var vm = new KpiVm
        {
            OrganizationId = orgIds.Count == 1 ? orgIds[0] : 0,
            SeasonId = seasonId ?? activeSeason?.Id ?? 0
        };
        await ApplyDerivedValuesAsync(vm);
        return View(vm);
    }

    [HttpPost("/kpi/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(KpiVm vm)
    {
        await Lookups();
        await ValidateAsync(vm, existingId: null);
        if (!ModelState.IsValid)
        {
            await ApplyDerivedValuesAsync(vm);
            return View(vm);
        }

        var entity = new KpiSubmission
        {
            SeasonId = vm.SeasonId,
            OrganizationId = vm.OrganizationId,
            Status = vm.SaveAsDraft ? KpiSubmissionStatus.Draft : KpiSubmissionStatus.Submitted,
            SubmittedAtUtc = vm.SaveAsDraft ? null : DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        ApplyVmToEntity(vm, entity);
        await ApplyDerivedValuesToEntityAsync(entity);

        _db.KpiSubmissions.Add(entity);
        await _db.SaveChangesAsync();
        await SaveDocs(entity, vm.Documents);
        await AuditAsync(vm.SaveAsDraft ? "KpiDraftCreated" : "KpiSubmitted", entity.Id, null, new { entity.Status, entity.SeasonId, entity.OrganizationId });

        if (entity.Status == KpiSubmissionStatus.Submitted) await NotifyAdmins(entity.Id);

        TempData["ToastSuccess"] = vm.SaveAsDraft
            ? IsAr() ? "تم حفظ البيانات كمسودة." : "KPI data saved as draft."
            : IsAr() ? "تم إرسال البيانات للاعتماد." : "KPI data submitted for approval.";
        return RedirectToAction(nameof(Index));
    }

    // Clubs may edit their own draft, or a submission returned for correction (MoreInfoRequired).
    [HttpGet("/kpi/edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var orgIds = await UserOrgIds();
        var entity = await _db.KpiSubmissions.Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id && orgIds.Contains(x.OrganizationId));
        if (entity is null) return NotFound();
        if (!IsEditable(entity.Status))
        {
            TempData["ToastWarning"] = IsAr() ? "لا يمكن تعديل هذا الإرسال في حالته الحالية." : "This submission cannot be edited in its current state.";
            return RedirectToAction(nameof(Index));
        }

        await Lookups();
        ViewBag.ExistingDocuments = entity.Documents.ToList();
        ViewBag.ReviewNotes = entity.ReviewNotes;
        ViewBag.CurrentStatus = entity.Status;
        var vm = MapToVm(entity);
        await ApplyDerivedValuesAsync(vm);
        return View(vm);
    }

    [HttpPost("/kpi/edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, KpiVm vm)
    {
        var orgIds = await UserOrgIds();
        var entity = await _db.KpiSubmissions.Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id && orgIds.Contains(x.OrganizationId));
        if (entity is null) return NotFound();
        if (!IsEditable(entity.Status))
        {
            TempData["ToastWarning"] = IsAr() ? "لا يمكن تعديل هذا الإرسال في حالته الحالية." : "This submission cannot be edited in its current state.";
            return RedirectToAction(nameof(Index));
        }

        await Lookups();
        await ValidateAsync(vm, existingId: entity.Id);
        if (!ModelState.IsValid)
        {
            ViewBag.ExistingDocuments = entity.Documents.ToList();
            ViewBag.ReviewNotes = entity.ReviewNotes;
            ViewBag.CurrentStatus = entity.Status;
            await ApplyDerivedValuesAsync(vm);
            return View(vm);
        }

        var old = new { entity.Status, entity.NumberOfParticipants, entity.SatisfactionRate };
        ApplyVmToEntity(vm, entity);
        await ApplyDerivedValuesToEntityAsync(entity);
        entity.Status = vm.SaveAsDraft ? KpiSubmissionStatus.Draft : KpiSubmissionStatus.Submitted;
        entity.SubmittedAtUtc = vm.SaveAsDraft ? entity.SubmittedAtUtc : DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync();
        await SaveDocs(entity, vm.Documents);
        await AuditAsync(vm.SaveAsDraft ? "KpiDraftUpdated" : "KpiSubmitted", entity.Id, old, new { entity.Status, entity.NumberOfParticipants, entity.SatisfactionRate });

        if (entity.Status == KpiSubmissionStatus.Submitted) await NotifyAdmins(entity.Id);

        TempData["ToastSuccess"] = vm.SaveAsDraft
            ? IsAr() ? "تم تحديث المسودة." : "Draft updated."
            : IsAr() ? "تم إرسال البيانات للاعتماد." : "KPI data submitted for approval.";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsEditable(KpiSubmissionStatus status)
        => status is KpiSubmissionStatus.Draft or KpiSubmissionStatus.MoreInfoRequired or KpiSubmissionStatus.Rejected;

    private async Task ValidateAsync(KpiVm vm, int? existingId)
    {
        var orgIds = await UserOrgIds();
        var isAr = IsAr();

        // Club identity always comes from the authenticated user's organization membership.
        if (orgIds.Count == 1)
        {
            vm.OrganizationId = orgIds[0];
            ModelState.Remove(nameof(vm.OrganizationId));
        }
        else if (!orgIds.Contains(vm.OrganizationId))
        {
            ModelState.AddModelError(nameof(vm.OrganizationId), isAr ? "النادي غير صالح." : "Invalid club.");
        }

        if (!await _db.Seasons.AnyAsync(x => x.Id == vm.SeasonId))
            ModelState.AddModelError(nameof(vm.SeasonId), isAr ? "الموسم الرياضي غير صالح." : "Invalid sports season.");

        var duplicate = await _db.KpiSubmissions.AnyAsync(x =>
            x.SeasonId == vm.SeasonId && x.OrganizationId == vm.OrganizationId && (existingId == null || x.Id != existingId));
        if (duplicate)
            ModelState.AddModelError("", isAr
                ? "توجد بيانات مؤشرات لهذا النادي والموسم. يرجى تعديل الإرسال الحالي."
                : "KPI data already exists for this club and season. Please edit the existing submission.");

        foreach (var percent in new (string Field, decimal? Value)[]
        {
            (nameof(vm.PlayerParticipationRate), vm.PlayerParticipationRate),
            (nameof(vm.AttendanceRate), vm.AttendanceRate),
            (nameof(vm.EthicalValuesAdherenceRate), vm.EthicalValuesAdherenceRate),
            (nameof(vm.HealthyDietaryHabitsRate), vm.HealthyDietaryHabitsRate),
            (nameof(vm.SatisfactionRate), vm.SatisfactionRate),
            (nameof(vm.PhysicalActivityComplianceRate), vm.PhysicalActivityComplianceRate)
        })
        {
            if (percent.Value is < 0 or > 100)
                ModelState.AddModelError(percent.Field, isAr ? "يجب أن تكون النسبة بين 0 و100." : "Percentage must be between 0 and 100.");
        }

        if (vm.Documents != null)
        {
            foreach (var file in vm.Documents.Where(f => f.Length > 0))
            {
                var error = FileValidationHelper.Validate(file, FileValidationHelper.Evidence, isAr);
                if (error != null) ModelState.AddModelError(nameof(vm.Documents), error);
            }
        }
    }

    /// <summary>
    /// Agenda-derived indicator values for the selected club/season (docs: the number of lectures is
    /// extracted automatically from the Agenda). Only Submitted/Approved entries count as delivered.
    ///
    /// The query itself lives in <see cref="SeasonClubStatistics"/> because the Ghars Annual Report
    /// reports the same four indicators: two copies of this logic is precisely how the two surfaces
    /// would come to disagree about how many activities a club delivered.
    /// </summary>
    private Task<SeasonClubStats> GetDerivedAsync(int organizationId, int seasonId)
        => SeasonClubStatistics.GetAsync(_db, organizationId, seasonId);

    private async Task ApplyDerivedValuesAsync(KpiVm vm)
    {
        var derived = await GetDerivedAsync(vm.OrganizationId, vm.SeasonId);
        ViewBag.HasDerivedData = derived.HasData;
        if (!derived.HasData) return;
        vm.NumberOfLecturesActivities = derived.DeliveredActivities;
        vm.NumberOfParticipants = derived.Participants;
        vm.NumberOfLecturers = derived.Lecturers;
        vm.NumberOfImplementingEntities = derived.ImplementingEntities;
    }

    private async Task ApplyDerivedValuesToEntityAsync(KpiSubmission entity)
    {
        var derived = await GetDerivedAsync(entity.OrganizationId, entity.SeasonId);
        if (!derived.HasData) return; // no authoritative agenda data: keep the club's manual entry
        entity.NumberOfLecturesActivities = derived.DeliveredActivities;
        entity.NumberOfParticipants = derived.Participants;
        entity.NumberOfLecturers = derived.Lecturers;
        entity.NumberOfImplementingEntities = derived.ImplementingEntities;
    }

    private static void ApplyVmToEntity(KpiVm vm, KpiSubmission e)
    {
        e.SeasonId = vm.SeasonId;
        e.OrganizationId = vm.OrganizationId;
        e.NumberOfLecturesActivities = vm.NumberOfLecturesActivities;
        e.NumberOfLecturers = vm.NumberOfLecturers;
        e.NumberOfImplementingEntities = vm.NumberOfImplementingEntities;
        e.NumberOfParticipants = vm.NumberOfParticipants;
        e.PlayerParticipationRate = vm.PlayerParticipationRate;
        e.WarningsAndRedCards = vm.WarningsAndRedCards;
        e.WeeklyTrainingMinutes = vm.WeeklyTrainingMinutes;
        e.PhysicalActivityComplianceRate = vm.PhysicalActivityComplianceRate;
        e.DiabetesCases = vm.DiabetesCases;
        e.HeartConditionCases = vm.HeartConditionCases;
        e.HypertensionCases = vm.HypertensionCases;
        e.OtherLifestyleConditionCases = vm.OtherLifestyleConditionCases;
        e.SatisfactionRate = vm.SatisfactionRate;
        e.AttendanceRate = vm.AttendanceRate;
        e.EthicalValuesAdherenceRate = vm.EthicalValuesAdherenceRate;
        e.HealthyDietaryHabitsRate = vm.HealthyDietaryHabitsRate;
        e.CommunityEventsCount = vm.CommunityEventsCount;
    }

    private static KpiVm MapToVm(KpiSubmission e) => new()
    {
        Id = e.Id,
        SeasonId = e.SeasonId,
        OrganizationId = e.OrganizationId,
        NumberOfLecturesActivities = e.NumberOfLecturesActivities,
        NumberOfLecturers = e.NumberOfLecturers,
        NumberOfImplementingEntities = e.NumberOfImplementingEntities,
        NumberOfParticipants = e.NumberOfParticipants,
        PlayerParticipationRate = e.PlayerParticipationRate,
        WarningsAndRedCards = e.WarningsAndRedCards,
        WeeklyTrainingMinutes = e.WeeklyTrainingMinutes,
        PhysicalActivityComplianceRate = e.PhysicalActivityComplianceRate,
        DiabetesCases = e.DiabetesCases,
        HeartConditionCases = e.HeartConditionCases,
        HypertensionCases = e.HypertensionCases,
        OtherLifestyleConditionCases = e.OtherLifestyleConditionCases,
        SatisfactionRate = e.SatisfactionRate,
        AttendanceRate = e.AttendanceRate,
        EthicalValuesAdherenceRate = e.EthicalValuesAdherenceRate,
        HealthyDietaryHabitsRate = e.HealthyDietaryHabitsRate,
        CommunityEventsCount = e.CommunityEventsCount
    };

    private async Task SaveDocs(KpiSubmission e, List<IFormFile>? files)
    {
        if (files == null) return;
        foreach (var f in files.Where(x => x.Length > 0))
        {
            // KPI evidence is PROTECTED: stored outside wwwroot and served only via /protected-files/kpi/{id}.
            var path = await ProtectedFileStore.SaveAsync(f, _env, ProtectedFileStore.KpiEvidence);
            _db.KpiDocuments.Add(new KpiDocument
            {
                KpiSubmissionId = e.Id,
                FilePath = path,
                OriginalFileName = Path.GetFileName(f.FileName),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = e.CreatedByUserId
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task NotifyAdmins(int id)
    {
        var n = new Notification
        {
            TitleEn = "KPI submitted for approval",
            TitleAr = "تم تقديم مؤشرات الأداء للاعتماد",
            MessageEn = "A club submitted KPI data for review.",
            MessageAr = "قدم نادٍ بيانات مؤشرات الأداء للمراجعة.",
            Type = NotificationType.Warning,
            TargetType = NotificationTargetType.Role,
            TargetRoleName = RoleNames.DscAdmin,
            LinkUrl = $"/Admin/Kpi/Details/{id}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();

        // Resolve deliveries so DSC reviewers see it in their inbox (not only the live toast).
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.DscAdmin);
        if (role != null)
        {
            var userIds = await _db.UserRoles.Where(ur => ur.RoleId == role.Id).Select(ur => ur.UserId).Distinct().ToListAsync();
            foreach (var uid in userIds)
                _db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = uid, DeliveredAtUtc = DateTime.UtcNow });
            await _db.SaveChangesAsync();
        }

        await _hub.Clients.All.SendAsync("notificationReceived", new { title = n.TitleEn, message = n.MessageEn, linkUrl = n.LinkUrl });
    }

    private async Task AuditAsync(string action, int id, object? oldValues, object? newValues)
    {
        try
        {
            _db.SystemAuditLogs.Add(new SystemAuditLog
            {
                Action = action,
                EntityName = nameof(KpiSubmission),
                EntityId = id.ToString(),
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

    private async Task Lookups()
    {
        var ids = await UserOrgIds();
        ViewBag.Seasons = await _db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.Clubs = await _db.Organizations.Where(x => ids.Contains(x.Id)).OrderBy(x => x.NameEn).ToListAsync();
    }

    private async Task<List<int>> UserOrgIds()
        => await _db.OrganizationAdminLinks
            .Where(x => x.UserId == (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ""))
            .Select(x => x.OrganizationId)
            .ToListAsync();

    public class KpiVm
    {
        public int Id { get; set; }
        [Required] public int SeasonId { get; set; }
        public int OrganizationId { get; set; }

        // System-derived from Agenda when data exists (read-only in the UI).
        public int NumberOfLecturesActivities { get; set; }
        public int NumberOfLecturers { get; set; }
        public int NumberOfImplementingEntities { get; set; }
        public int NumberOfParticipants { get; set; }

        // Club-submitted indicators.
        public decimal PlayerParticipationRate { get; set; }
        public decimal AttendanceRate { get; set; }
        public int WarningsAndRedCards { get; set; }
        public int WeeklyTrainingMinutes { get; set; }
        public decimal? PhysicalActivityComplianceRate { get; set; }
        public int DiabetesCases { get; set; }
        public int HeartConditionCases { get; set; }
        public int HypertensionCases { get; set; }
        public int OtherLifestyleConditionCases { get; set; }
        public decimal SatisfactionRate { get; set; }
        public decimal EthicalValuesAdherenceRate { get; set; }
        public decimal HealthyDietaryHabitsRate { get; set; }
        public int CommunityEventsCount { get; set; }

        public bool SaveAsDraft { get; set; }
        public List<IFormFile>? Documents { get; set; }
    }
}
