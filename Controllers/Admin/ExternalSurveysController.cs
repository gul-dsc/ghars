using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GharsPlatform.Controllers.Admin;

/// <summary>
/// LEGACY. Administration of surveys that were run on an external platform, with an analysed PDF
/// report published afterwards.
/// </summary>
/// <remarks>
/// The official participant satisfaction survey is native now — see
/// <see cref="SurveysController"/> and <see cref="Models.Core.SurveyPurpose.OfficialSatisfaction"/>.
/// This controller is retained so historical rows and their published reports stay readable, and so a
/// KPI submission that cites one still resolves. Nothing new is created here: the Create action and
/// its view have been removed, not merely hidden, so the route cannot be reached by typing it either.
/// </remarks>
[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class ExternalSurveysController : Controllers.BaseController
{
    private readonly IWebHostEnvironment _env;

    public ExternalSurveysController(AppDbContext db, IWebHostEnvironment env) : base(db)
    {
        _env = env;
    }

    private static bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public async Task<IActionResult> Index()
    {
        var list = await Db.ExternalSurveys.Include(x => x.Season)
            .OrderByDescending(x => x.Id)
            .ToListAsync();
        return View(list);
    }

    // There is deliberately no Create action. The official participant satisfaction survey is native,
    // so a new external survey has nothing to be: it would appear to participants nowhere, feed no
    // indicator, and only invite the assumption that the old workflow is still live. Editing an
    // existing row remains available so a historical record can be corrected or its report published.

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await Db.ExternalSurveys.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound();
        await LoadLookupsAsync();
        ViewBag.ExistingReport = entity.ReportPdfPath;
        return View(new ExternalSurveyVm
        {
            Id = entity.Id,
            TitleEn = entity.TitleEn,
            TitleAr = entity.TitleAr,
            DescriptionEn = entity.DescriptionEn,
            DescriptionAr = entity.DescriptionAr,
            ExternalUrl = entity.ExternalUrl,
            SeasonId = entity.SeasonId,
            IsActive = entity.IsActive,
            StartsAtUtc = entity.StartsAtUtc,
            EndsAtUtc = entity.EndsAtUtc,
            IsReportPublished = entity.IsReportPublished
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ExternalSurveyVm vm)
    {
        var entity = await Db.ExternalSurveys.FirstOrDefaultAsync(x => x.Id == vm.Id);
        if (entity is null) return NotFound();
        await LoadLookupsAsync();
        ViewBag.ExistingReport = entity.ReportPdfPath;
        ValidateUrlAndReport(vm);
        if (!ModelState.IsValid) return View(vm);

        var old = new { entity.TitleEn, entity.ExternalUrl, entity.IsActive, entity.IsReportPublished };
        entity.TitleEn = vm.TitleEn.Trim();
        entity.TitleAr = vm.TitleAr.Trim();
        entity.DescriptionEn = vm.DescriptionEn?.Trim();
        entity.DescriptionAr = vm.DescriptionAr?.Trim();
        entity.ExternalUrl = vm.ExternalUrl.Trim();
        entity.SeasonId = vm.SeasonId;
        entity.IsActive = vm.IsActive;
        entity.StartsAtUtc = vm.StartsAtUtc;
        entity.EndsAtUtc = vm.EndsAtUtc;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = CurrentUserId;

        if (vm.ReportPdf is { Length: > 0 })
            // Analysis reports are CONDITIONAL: stored outside wwwroot and released through
            // /protected-files/survey-report/{id} only once DSC publishes them.
            entity.ReportPdfPath = await ProtectedFileStore.SaveAsync(vm.ReportPdf, _env, ProtectedFileStore.SurveyReports);

        // The report can only be published once a file exists.
        var wasPublished = entity.IsReportPublished;
        entity.IsReportPublished = vm.IsReportPublished && !string.IsNullOrWhiteSpace(entity.ReportPdfPath);
        if (entity.IsReportPublished && !wasPublished) entity.ReportPublishedAtUtc = DateTime.UtcNow;
        if (!entity.IsReportPublished) entity.ReportPublishedAtUtc = null;

        await Db.SaveChangesAsync();
        await AuditAsync("Update", nameof(ExternalSurvey), entity.Id.ToString(), old, new { entity.TitleEn, entity.ExternalUrl, entity.IsActive, entity.IsReportPublished });

        TempData["ToastSuccess"] = IsAr() ? "تم تحديث الاستبيان الرسمي." : "Official survey updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleReport(int id)
    {
        var entity = await Db.ExternalSurveys.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(entity.ReportPdfPath))
        {
            TempData["ToastWarning"] = IsAr() ? "لا يوجد تقرير مرفوع لنشره." : "There is no uploaded report to publish.";
            return RedirectToAction(nameof(Index));
        }

        var old = new { entity.IsReportPublished };
        entity.IsReportPublished = !entity.IsReportPublished;
        entity.ReportPublishedAtUtc = entity.IsReportPublished ? DateTime.UtcNow : null;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync("PublishReport", nameof(ExternalSurvey), entity.Id.ToString(), old, new { entity.IsReportPublished });

        TempData["ToastSuccess"] = entity.IsReportPublished
            ? IsAr() ? "تم نشر تقرير الاستبيان." : "Survey report published."
            : IsAr() ? "تم إخفاء تقرير الاستبيان." : "Survey report hidden.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await Db.ExternalSurveys.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound();
        Db.ExternalSurveys.Remove(entity);
        await Db.SaveChangesAsync();
        await AuditAsync("Delete", nameof(ExternalSurvey), id.ToString(), entity, null);
        TempData["ToastWarning"] = IsAr() ? "تم حذف الاستبيان الرسمي." : "Official survey deleted.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateUrlAndReport(ExternalSurveyVm vm)
    {
        var isAr = IsAr();
        if (!FileValidationHelper.IsSafeHttpUrl(vm.ExternalUrl))
        {
            ModelState.AddModelError(nameof(vm.ExternalUrl), isAr
                ? "رابط الاستبيان غير صالح. يجب أن يبدأ بـ http أو https."
                : "Invalid survey link. Only http/https URLs are allowed.");
        }

        var reportError = FileValidationHelper.Validate(vm.ReportPdf, FileValidationHelper.Pdf, isAr);
        if (reportError != null) ModelState.AddModelError(nameof(vm.ReportPdf), reportError);

        if (vm.StartsAtUtc.HasValue && vm.EndsAtUtc.HasValue && vm.EndsAtUtc <= vm.StartsAtUtc)
        {
            ModelState.AddModelError(nameof(vm.EndsAtUtc), isAr
                ? "يجب أن يكون تاريخ الانتهاء بعد تاريخ البدء."
                : "The end date must be after the start date.");
        }
    }

    private async Task LoadLookupsAsync()
    {
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
    }

    public class ExternalSurveyVm
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string TitleEn { get; set; } = "";
        [Required, MaxLength(200)] public string TitleAr { get; set; } = "";
        [MaxLength(2000)] public string? DescriptionEn { get; set; }
        [MaxLength(2000)] public string? DescriptionAr { get; set; }
        [Required, MaxLength(700)] public string ExternalUrl { get; set; } = "";
        public int? SeasonId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
        public IFormFile? ReportPdf { get; set; }
        public bool IsReportPublished { get; set; }
    }
}
