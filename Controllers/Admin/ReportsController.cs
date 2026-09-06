using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    public IActionResult Overview() => RedirectToAction(nameof(Analytics));

    public async Task<IActionResult> Attendance()
    {
        var since = DateTime.UtcNow.AddDays(-90);
        var sessions = await _db.AttendanceSessions.Include(x => x.Activity)
            .Where(x => x.SessionStartUtc >= since)
            .OrderByDescending(x => x.SessionStartUtc).Take(200).ToListAsync();
        return View(sessions);
    }

    public async Task<IActionResult> Bookings()
    {
        var list = await _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization)
            .OrderByDescending(x => x.CreatedAtUtc).Take(300).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Certificates()
    {
        var list = await _db.Certificates.Include(x => x.Activity)
            .OrderByDescending(x => x.IssuedAtUtc).Take(300).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Satisfaction()
    {
        var since = DateTime.UtcNow.AddDays(-180);
        var answers = await _db.SurveyAnswers.Include(x => x.SurveyQuestion).Include(x => x.SurveyResponse)
            .Where(x => x.StarsValue != null && x.SurveyResponse != null && x.SurveyResponse.SubmittedAtUtc >= since)
            .ToListAsync();
        var grouped = answers.Where(a => a.SurveyQuestion != null).GroupBy(a => a.SurveyQuestion!.Id)
            .Select(g => new SatisfactionRow
            {
                QuestionEn = g.First().SurveyQuestion!.QuestionEn,
                QuestionAr = g.First().SurveyQuestion!.QuestionAr,
                AvgStars = g.Average(x => (double)x.StarsValue!.Value),
                Count = g.Count()
            }).OrderByDescending(x => x.AvgStars).ToList();
        return View(grouped);
    }

    public IActionResult PointsRewards() => NotFound();

    public async Task<IActionResult> Analytics(
        int? seasonId,
        int? clubId,
        int? entityId,
        ActivityType? activityType,
        AgendaTargetCategory? category,
        KpiIndicator? kpiIndicator,
        KpiSubmissionStatus? kpiStatus,
        BookingStatus? bookingStatus,
        string? dateFrom,
        string? dateTo)
    {
        DateTime? from = DateTime.TryParse(dateFrom, out var df) ? df.Date : null;
        DateTime? to = DateTime.TryParse(dateTo, out var dt) ? dt.Date.AddDays(1).AddTicks(-1) : null;

        var bookingsQ = _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.PartnerOrganization).AsQueryable();
        if (clubId.HasValue) bookingsQ = bookingsQ.Where(x => x.OrganizationId == clubId);
        if (entityId.HasValue) bookingsQ = bookingsQ.Where(x => x.PartnerOrganizationId == entityId);
        if (activityType.HasValue) bookingsQ = bookingsQ.Where(x => x.Activity != null && x.Activity.Type == activityType);
        if (bookingStatus.HasValue) bookingsQ = bookingsQ.Where(x => x.Status == bookingStatus);
        if (from.HasValue) bookingsQ = bookingsQ.Where(x => x.CreatedAtUtc >= from.Value);
        if (to.HasValue) bookingsQ = bookingsQ.Where(x => x.CreatedAtUtc <= to.Value);
        var bookings = await bookingsQ.Take(5000).ToListAsync();

        var agendaQ = _db.AgendaEntries.Include(x => x.Season).Include(x => x.Organization).AsQueryable();
        if (seasonId.HasValue) agendaQ = agendaQ.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) agendaQ = agendaQ.Where(x => x.OrganizationId == clubId);
        if (activityType.HasValue) agendaQ = agendaQ.Where(x => x.ActivityType == activityType.Value);
        if (category.HasValue) agendaQ = agendaQ.Where(x => x.Category == category.Value);
        if (from.HasValue) agendaQ = agendaQ.Where(x => x.ActivityDate >= from.Value);
        if (to.HasValue) agendaQ = agendaQ.Where(x => x.ActivityDate <= to.Value);
        var agenda = await agendaQ.Take(5000).ToListAsync();

        var kpisQ = _db.KpiSubmissions.Include(x => x.Season).Include(x => x.Organization).AsQueryable();
        if (seasonId.HasValue) kpisQ = kpisQ.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) kpisQ = kpisQ.Where(x => x.OrganizationId == clubId);
        if (kpiStatus.HasValue) kpisQ = kpisQ.Where(x => x.Status == kpiStatus.Value);
        var kpis = await kpisQ.Take(5000).ToListAsync();

        var galleryQ = _db.GalleryItems.Include(x => x.Organization).Include(x => x.Season).Include(x => x.Activity).AsQueryable();
        if (seasonId.HasValue) galleryQ = galleryQ.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) galleryQ = galleryQ.Where(x => x.OrganizationId == clubId);
        if (activityType.HasValue) galleryQ = galleryQ.Where(x => x.Activity != null && x.Activity.Type == activityType.Value);
        if (from.HasValue) galleryQ = galleryQ.Where(x => x.MediaDate >= from.Value);
        if (to.HasValue) galleryQ = galleryQ.Where(x => x.MediaDate <= to.Value);
        var gallery = await galleryQ.Take(5000).ToListAsync();

        var libraryQ = _db.LibraryItems.AsQueryable();
        if (from.HasValue) libraryQ = libraryQ.Where(x => x.PublicationDate >= from.Value);
        if (to.HasValue) libraryQ = libraryQ.Where(x => x.PublicationDate <= to.Value);
        var library = await libraryQ.Take(5000).ToListAsync();

        var clubs = await _db.Organizations.Where(x => x.OrganizationType == OrganizationType.Club).OrderBy(x => x.NameEn).ToListAsync();
        var approvedKpiClubs = kpis.Where(x => x.Status == KpiSubmissionStatus.Approved).Select(x => x.OrganizationId).Distinct().Count();
        var coveragePercent = clubs.Count == 0 ? 0 : Math.Round((decimal)approvedKpiClubs * 100m / clubs.Count, 1);

        // Previous-season approved violations per club: the violations KPI is an annual REDUCTION,
        // not a raw count, so each row needs its club's prior-season figure.
        var allSeasons = await _db.Seasons.OrderBy(x => x.StartDate).ToListAsync();
        var previousSeasonOf = new Dictionary<int, int>();
        for (var i = 1; i < allSeasons.Count; i++) previousSeasonOf[allSeasons[i].Id] = allSeasons[i - 1].Id;
        var approvedByClubSeason = await _db.KpiSubmissions
            .Where(x => x.Status == KpiSubmissionStatus.Approved)
            .Select(x => new { x.OrganizationId, x.SeasonId, x.WarningsAndRedCards })
            .ToListAsync();
        var previousViolations = new Dictionary<int, int?>();
        foreach (var k in kpis)
        {
            int? prev = null;
            if (previousSeasonOf.TryGetValue(k.SeasonId, out var prevSeasonId))
            {
                prev = approvedByClubSeason
                    .FirstOrDefault(x => x.OrganizationId == k.OrganizationId && x.SeasonId == prevSeasonId)?.WarningsAndRedCards;
            }
            previousViolations[k.Id] = prev;
        }
        ViewBag.PreviousViolations = previousViolations;

        // Season-over-season progression (2026 baseline -> 2033 target) from approved submissions.
        var progression = allSeasons.Select(season =>
        {
            var rows = approvedByClubSeason.Where(x => x.SeasonId == season.Id).ToList();
            var full = _db.KpiSubmissions.Where(x => x.SeasonId == season.Id && x.Status == KpiSubmissionStatus.Approved);
            return new
            {
                SeasonId = season.Id,
                TitleEn = season.TitleEn,
                TitleAr = season.TitleAr,
                Year = season.StartDate.Year,
                Clubs = rows.Select(x => x.OrganizationId).Distinct().Count(),
                Participation = full.Any() ? Math.Round(full.Average(x => x.PlayerParticipationRate), 1) : (decimal?)null,
                Attendance = full.Any() ? Math.Round(full.Average(x => x.AttendanceRate), 1) : (decimal?)null,
                Ethics = full.Any() ? Math.Round(full.Average(x => x.EthicalValuesAdherenceRate), 1) : (decimal?)null,
                Diet = full.Any() ? Math.Round(full.Average(x => x.HealthyDietaryHabitsRate), 1) : (decimal?)null,
                Satisfaction = full.Any() ? Math.Round(full.Average(x => x.SatisfactionRate), 1) : (decimal?)null,
                PhysicalActivity = full.Any(x => x.PhysicalActivityComplianceRate != null)
                    ? Math.Round(full.Where(x => x.PhysicalActivityComplianceRate != null).Average(x => x.PhysicalActivityComplianceRate!.Value), 1)
                    : (decimal?)null,
                Participants = full.Any() ? full.Sum(x => x.NumberOfParticipants) : 0,
                Activities = full.Any() ? full.Sum(x => x.NumberOfLecturesActivities) : 0
            };
        }).ToList();
        ViewBag.Progression = progression;

        // Season summary by club, from actual delivered Agenda entries (end-of-season table).
        var summarySeasonId = seasonId ?? allSeasons.LastOrDefault(x => x.IsActive)?.Id ?? allSeasons.LastOrDefault()?.Id;
        var summaryEntries = await _db.AgendaEntries
            .Include(x => x.Organization)
            .Where(x => (summarySeasonId == null || x.SeasonId == summarySeasonId)
                        && (x.Status == AgendaEntryStatus.Submitted || x.Status == AgendaEntryStatus.Approved))
            .Select(x => new { x.OrganizationId, ClubEn = x.Organization!.NameEn, ClubAr = x.Organization.NameAr, x.NumberOfParticipants, x.LecturerName, x.DepartmentOrOrganization, x.Category })
            .ToListAsync();
        ViewBag.SeasonSummary = summaryEntries
            .GroupBy(x => new { x.OrganizationId, x.ClubEn, x.ClubAr })
            .Select(g => new
            {
                ClubEn = g.Key.ClubEn,
                ClubAr = g.Key.ClubAr,
                Activities = g.Count(),
                Participants = g.Sum(x => x.NumberOfParticipants),
                Lecturers = g.Where(x => !string.IsNullOrWhiteSpace(x.LecturerName)).Select(x => x.LecturerName!.Trim().ToLowerInvariant()).Distinct().Count(),
                Entities = g.Where(x => !string.IsNullOrWhiteSpace(x.DepartmentOrOrganization)).Select(x => x.DepartmentOrOrganization!.Trim().ToLowerInvariant()).Distinct().Count(),
                Categories = g.Select(x => x.Category).Distinct().Count()
            })
            .OrderByDescending(x => x.Activities)
            .ToList();
        ViewBag.SummarySeasonTitle = allSeasons.FirstOrDefault(x => x.Id == summarySeasonId)?.TitleEn;

        ViewBag.Bookings = bookings;
        ViewBag.Agenda = agenda;
        ViewBag.Kpis = kpis;
        ViewBag.Gallery = gallery;
        ViewBag.Library = library;
        ViewBag.TotalClubs = clubs.Count;
        ViewBag.CoveredClubs = approvedKpiClubs;
        ViewBag.CoveragePercent = coveragePercent;
        ViewBag.ClubsNotCovered = clubs.Where(c => !kpis.Any(k => k.OrganizationId == c.Id && k.Status == KpiSubmissionStatus.Approved)).ToList();
        ViewBag.Seasons = await _db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.Clubs = clubs;
        ViewBag.Entities = await _db.Organizations.Where(x => x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner).OrderBy(x => x.NameEn).ToListAsync();
        ViewBag.SelectedSeasonId = seasonId;
        ViewBag.SelectedClubId = clubId;
        ViewBag.SelectedEntityId = entityId;
        ViewBag.SelectedActivityType = activityType;
        ViewBag.SelectedCategory = category;
        ViewBag.SelectedKpiIndicator = kpiIndicator;
        ViewBag.SelectedKpiStatus = kpiStatus;
        ViewBag.SelectedBookingStatus = bookingStatus;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        return View();
    }

    public async Task<IActionResult> ExportAnalyticsCsv(int? seasonId, int? clubId)
    {
        var rowsQ = _db.KpiSubmissions.Include(x => x.Season).Include(x => x.Organization).Where(x => x.Status == KpiSubmissionStatus.Approved).AsQueryable();
        if (seasonId.HasValue) rowsQ = rowsQ.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) rowsQ = rowsQ.Where(x => x.OrganizationId == clubId);
        var rows = await rowsQ.OrderBy(x => x.Season!.StartDate).ThenBy(x => x.Organization!.NameEn).ToListAsync();
        // Targets come from the central KPI catalog so exports can never drift from dashboards/reports.
        var t = GharsKpiCatalog.All.ToDictionary(x => x.Key, x => x.TargetText);
        var sb = new StringBuilder();
        sb.AppendLine("Season,Club,Activities,Lecturers,ImplementingEntities,Participants,PlayerParticipationRate,PlayerParticipationTarget,AttendanceRate,AttendanceTarget,WarningsAndRedCards,ViolationsReductionTarget,EthicalValuesAdherenceRate,EthicalTarget,PhysicalActivityCompliancePercent,PhysicalActivityTarget,WeeklyTrainingMinutes,LifestyleDiseaseFreePercent,LifestyleDiseaseFreeTarget,HealthyDietaryHabitsRate,HealthyDietTarget,CommunityEventsCount,CommunityEventsTarget,SatisfactionRate,SatisfactionTarget,Status,IsBaselineYear");
        foreach (var r in rows)
        {
            var diseaseFree = GharsKpiCatalog.DiseaseFreePercent(r);
            // "No Data" is exported literally: a missing measurement must never read as 0.
            var diseaseFreeText = diseaseFree.HasValue ? diseaseFree.Value.ToString("0.#") : "No Data";
            var physicalText = r.PhysicalActivityComplianceRate.HasValue ? r.PhysicalActivityComplianceRate.Value.ToString("0.#") : "No Data";
            sb.AppendLine(string.Join(",",
                $"\"{r.Season?.TitleEn}\"", $"\"{r.Organization?.NameEn}\"",
                r.NumberOfLecturesActivities, r.NumberOfLecturers, r.NumberOfImplementingEntities, r.NumberOfParticipants,
                r.PlayerParticipationRate, $"\"{t[GharsKpiCatalog.PlayerParticipation.Key]}\"",
                r.AttendanceRate, $"\"{t[GharsKpiCatalog.AttendanceRate.Key]}\"",
                r.WarningsAndRedCards, $"\"{t[GharsKpiCatalog.ViolationsReduction.Key]}\"",
                r.EthicalValuesAdherenceRate, $"\"{t[GharsKpiCatalog.EthicalValues.Key]}\"",
                $"\"{physicalText}\"", $"\"{t[GharsKpiCatalog.PhysicalActivity.Key]}\"",
                r.WeeklyTrainingMinutes,
                $"\"{diseaseFreeText}\"", $"\"{t[GharsKpiCatalog.LifestyleDiseaseFree.Key]}\"",
                r.HealthyDietaryHabitsRate, $"\"{t[GharsKpiCatalog.HealthyDiet.Key]}\"",
                r.CommunityEventsCount, $"\"{t[GharsKpiCatalog.CommunityEvents.Key]}\"",
                r.SatisfactionRate, $"\"{t[GharsKpiCatalog.Satisfaction.Key]}\"",
                r.Status,
                r.Season?.StartDate.Year == GharsKpiCatalog.BaselineYear ? "Yes" : "No"));
        }
        // UTF-8 BOM so Excel renders the Arabic club/season names correctly.
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", "ghars-kpi-analytics.csv");
    }

    public async Task<IActionResult> ExportAnalyticsPdf(int? seasonId, int? clubId)
    {
        var rowsQ = _db.KpiSubmissions.Include(x => x.Season).Include(x => x.Organization).Where(x => x.Status == KpiSubmissionStatus.Approved).AsQueryable();
        if (seasonId.HasValue) rowsQ = rowsQ.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) rowsQ = rowsQ.Where(x => x.OrganizationId == clubId);
        var rows = await rowsQ.OrderByDescending(x => x.Id).Take(100).ToListAsync();

        QuestPDF.Settings.License = LicenseType.Community;
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Ghars KPI Analytics Report").FontSize(20).Bold().FontColor(Colors.Green.Darken3);
                        col.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
                page.Content().PaddingTop(15).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2); columns.RelativeColumn(2); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn();
                    });
                    table.Header(header =>
                    {
                        foreach (var h in new[] { "Season", "Club", "Participants", "Player %", "Attendance %", "Satisfaction %", "Status" })
                            header.Cell().Background(Colors.Green.Lighten4).Padding(4).Text(h).Bold().FontSize(9);
                    });
                    foreach (var r in rows)
                    {
                        table.Cell().Padding(4).Text(r.Season?.TitleEn ?? "").FontSize(8);
                        table.Cell().Padding(4).Text(r.Organization?.NameEn ?? "").FontSize(8);
                        table.Cell().Padding(4).Text(r.NumberOfParticipants.ToString()).FontSize(8);
                        table.Cell().Padding(4).Text($"{r.PlayerParticipationRate:0.#}").FontSize(8);
                        table.Cell().Padding(4).Text($"{r.AttendanceRate:0.#}").FontSize(8);
                        table.Cell().Padding(4).Text($"{r.SatisfactionRate:0.#}").FontSize(8);
                        table.Cell().Padding(4).Text(r.Status.ToString()).FontSize(8);
                    }
                });
                page.Footer().AlignCenter().Text("Ghars Platform - Ghars logo only branding").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();
        return File(bytes, "application/pdf", "ghars-kpi-analytics.pdf");
    }

    /// <summary>Kept for backward compatibility; delegates to the central KPI catalog (0 when no data).</summary>
    public static decimal CalculateDiseaseFreePercent(KpiSubmission r) => GharsKpiCatalog.DiseaseFreePercent(r) ?? 0m;

    public class SatisfactionRow
    {
        public string QuestionEn { get; set; } = "";
        public string QuestionAr { get; set; } = "";
        public double AvgStars { get; set; }
        public int Count { get; set; }
    }
}

public enum KpiIndicator
{
    ProgramCoverage = 1,
    PlayerParticipation = 2,
    AttendanceRate = 3,
    ViolationsReduction = 4,
    EthicalValuesAdherence = 5,
    PhysicalActivityMinutes = 6,
    LifestyleDiseaseFree = 7,
    HealthyDietaryHabits = 8,
    CommunityEvents = 9,
    SatisfactionHappiness = 10
}
