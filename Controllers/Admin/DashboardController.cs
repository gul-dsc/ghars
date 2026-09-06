using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int? seasonId, int? clubId, int? partnerId, ActivityType? activityType, DateTime? from, DateTime? to, string? kpiCategory)
    {
        var now = DateTime.UtcNow;
        var fromDate = from?.Date;
        var toDate = to?.Date.AddDays(1);

        var activeSeason = await _db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
        var selectedSeasonId = seasonId ?? activeSeason?.Id;

        var seasons = await _db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        var clubs = await _db.Organizations.Where(x => x.OrganizationType == OrganizationType.Club || x.OrganizationType == OrganizationType.PrivateAcademy).OrderBy(x => x.NameEn).ToListAsync();
        var partners = await _db.Organizations.Where(x => x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner).OrderBy(x => x.NameEn).ToListAsync();

        IQueryable<Activity> activities = _db.Activities.Include(x => x.PartnerOrganization).Include(x => x.Season);
        if (selectedSeasonId.HasValue) activities = activities.Where(x => x.SeasonId == selectedSeasonId.Value);
        if (partnerId.HasValue) activities = activities.Where(x => x.PartnerOrganizationId == partnerId.Value);
        if (activityType.HasValue) activities = activities.Where(x => x.Type == activityType.Value);
        if (fromDate.HasValue) activities = activities.Where(x => x.StartDateTime >= fromDate.Value);
        if (toDate.HasValue) activities = activities.Where(x => x.StartDateTime < toDate.Value);

        IQueryable<BookingRequest> bookings = _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.PartnerOrganization);
        if (selectedSeasonId.HasValue) bookings = bookings.Where(x => x.Activity != null && x.Activity.SeasonId == selectedSeasonId.Value);
        if (clubId.HasValue) bookings = bookings.Where(x => x.OrganizationId == clubId.Value);
        if (partnerId.HasValue) bookings = bookings.Where(x => x.PartnerOrganizationId == partnerId.Value || (x.Activity != null && x.Activity.PartnerOrganizationId == partnerId.Value));
        if (activityType.HasValue) bookings = bookings.Where(x => x.Activity != null && x.Activity.Type == activityType.Value);
        if (fromDate.HasValue) bookings = bookings.Where(x => x.CreatedAtUtc >= fromDate.Value);
        if (toDate.HasValue) bookings = bookings.Where(x => x.CreatedAtUtc < toDate.Value);

        IQueryable<AgendaEntry> agenda = _db.AgendaEntries.Include(x => x.Organization).Include(x => x.Season);
        if (selectedSeasonId.HasValue) agenda = agenda.Where(x => x.SeasonId == selectedSeasonId.Value);
        if (clubId.HasValue) agenda = agenda.Where(x => x.OrganizationId == clubId.Value);
        if (activityType.HasValue) agenda = agenda.Where(x => x.ActivityType == activityType.Value);
        if (fromDate.HasValue) agenda = agenda.Where(x => x.ActivityDate >= fromDate.Value);
        if (toDate.HasValue) agenda = agenda.Where(x => x.ActivityDate < toDate.Value);

        IQueryable<KpiSubmission> kpis = _db.KpiSubmissions.Include(x => x.Organization).Include(x => x.Season);
        if (selectedSeasonId.HasValue) kpis = kpis.Where(x => x.SeasonId == selectedSeasonId.Value);
        if (clubId.HasValue) kpis = kpis.Where(x => x.OrganizationId == clubId.Value);

        IQueryable<AttendanceRecord> attendance = _db.AttendanceRecords.Include(x => x.AttendanceSession).ThenInclude(x => x!.Activity).Include(x => x.Organization);
        if (selectedSeasonId.HasValue) attendance = attendance.Where(x => x.AttendanceSession != null && x.AttendanceSession.Activity != null && x.AttendanceSession.Activity.SeasonId == selectedSeasonId.Value);
        if (clubId.HasValue) attendance = attendance.Where(x => x.OrganizationId == clubId.Value);
        if (activityType.HasValue) attendance = attendance.Where(x => x.AttendanceSession != null && x.AttendanceSession.Activity != null && x.AttendanceSession.Activity.Type == activityType.Value);
        if (fromDate.HasValue) attendance = attendance.Where(x => x.CheckInUtc >= fromDate.Value);
        if (toDate.HasValue) attendance = attendance.Where(x => x.CheckInUtc < toDate.Value);

        IQueryable<MediaAlbum> albums = _db.MediaAlbums.Include(x => x.Items).Include(x => x.Organization).Include(x => x.Activity);
        if (selectedSeasonId.HasValue) albums = albums.Where(x => x.SeasonId == selectedSeasonId.Value || x.SeasonId == null);
        if (clubId.HasValue) albums = albums.Where(x => x.OrganizationId == clubId.Value);
        if (activityType.HasValue) albums = albums.Where(x => x.Activity != null && x.Activity.Type == activityType.Value);
        if (fromDate.HasValue) albums = albums.Where(x => x.AlbumDate >= fromDate.Value);
        if (toDate.HasValue) albums = albums.Where(x => x.AlbumDate < toDate.Value);

        IQueryable<LibraryItem> library = _db.LibraryItems.Include(x => x.LibraryCategory);
        if (fromDate.HasValue) library = library.Where(x => x.PublicationDate == null || x.PublicationDate >= fromDate.Value);
        if (toDate.HasValue) library = library.Where(x => x.PublicationDate == null || x.PublicationDate < toDate.Value);

        var totalClubs = await _db.Organizations.CountAsync(x => x.OrganizationType == OrganizationType.Club || x.OrganizationType == OrganizationType.PrivateAcademy);
        var coveredClubIds = await agenda.Select(x => x.OrganizationId).Distinct().ToListAsync();
        var programCoverage = totalClubs == 0 ? 0 : Math.Round((decimal)coveredClubIds.Count / totalClubs * 100, 1);

        var totalPartners = await _db.Organizations.CountAsync(x => x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner);
        var totalPrograms = await activities.CountAsync();
        var totalBookings = await bookings.CountAsync();
        var pendingApprovals = await bookings.CountAsync(x => x.Status == BookingStatus.Pending || x.Status == BookingStatus.PendingPartnerApproval || x.Status == BookingStatus.PartnerProposedNewTime)
            + await kpis.CountAsync(x => x.Status == KpiSubmissionStatus.Submitted)
            + await agenda.CountAsync(x => x.Status == AgendaEntryStatus.Submitted);
        var confirmedBookings = await bookings.CountAsync(x => x.Status == BookingStatus.Approved || x.Status == BookingStatus.Confirmed);
        var rejectedBookings = await bookings.CountAsync(x => x.Status == BookingStatus.Rejected || x.Status == BookingStatus.ClubRejectedProposedTimes);
        var totalParticipants = await agenda.SumAsync(x => (int?)x.NumberOfParticipants) ?? 0;
        var totalCapacity = await activities.SumAsync(x => (int?)x.Capacity) ?? 0;
        var attendanceCount = await attendance.CountAsync();
        var attendanceRate = totalCapacity == 0 ? 0 : Math.Round((decimal)attendanceCount / totalCapacity * 100, 1);
        var certificatesIssued = await _db.Certificates.CountAsync(x => x.Status == CertificateStatus.Issued);
        var libraryCount = await library.CountAsync(x => x.IsPublished);
        var galleryUploadCount = await albums.SelectMany(x => x.Items).CountAsync();
        var unreadNotifications = await _db.NotificationDeliveries.CountAsync(x => x.ReadAtUtc == null);
        var activeUsers = await _db.Users.CountAsync(x => !x.LockoutEnd.HasValue || x.LockoutEnd < now);
        var upcomingEvents = await activities.CountAsync(x => x.Status == ActivityStatus.Published && x.StartDateTime >= now);
        var completedActivities = await activities.CountAsync(x => x.EndDateTime < now);

        var approvedKpis = await kpis.Where(x => x.Status == KpiSubmissionStatus.Approved).ToListAsync();

        // Official satisfaction comes ONLY from approved club submissions (backed by the official survey
        // report). The internal Ghars survey star rating is reported separately and must never stand in
        // for the official KPI value - with no approved data the tile shows "No Data", not the internal
        // score, which measures different respondents against a different instrument.
        var starValues = await _db.SurveyAnswers.Where(x => x.StarsValue != null).Select(x => (int)x.StarsValue!).ToListAsync();
        decimal? internalStarScore = starValues.Count == 0 ? null : Math.Round((decimal)starValues.Average() / 5 * 100, 1);
        decimal? satisfactionScore = approvedKpis.Count == 0 ? null : Math.Round(approvedKpis.Average(x => x.SatisfactionRate), 1);

        decimal? avgPlayerParticipation = approvedKpis.Count == 0 ? null : Math.Round(approvedKpis.Average(x => x.PlayerParticipationRate), 1);
        decimal? avgEthics = approvedKpis.Count == 0 ? null : Math.Round(approvedKpis.Average(x => x.EthicalValuesAdherenceRate), 1);
        decimal? avgDiet = approvedKpis.Count == 0 ? null : Math.Round(approvedKpis.Average(x => x.HealthyDietaryHabitsRate), 1);
        decimal? avgAttendance = approvedKpis.Count == 0 ? null : Math.Round(approvedKpis.Average(x => x.AttendanceRate), 1);
        var physicalRows = approvedKpis.Where(x => x.PhysicalActivityComplianceRate.HasValue).ToList();
        decimal? avgPhysicalActivity = physicalRows.Count == 0 ? null : Math.Round(physicalRows.Average(x => x.PhysicalActivityComplianceRate!.Value), 1);

        // Annual violations reduction: current season vs the immediately preceding season (approved only).
        decimal? violationsReduction = null;
        if (selectedSeasonId.HasValue)
        {
            var currentSeason = seasons.FirstOrDefault(x => x.Id == selectedSeasonId.Value);
            var previousSeason = currentSeason == null ? null : seasons.Where(x => x.StartDate < currentSeason.StartDate).OrderByDescending(x => x.StartDate).FirstOrDefault();
            if (previousSeason != null)
            {
                var currentViolations = approvedKpis.Sum(x => x.WarningsAndRedCards);
                var previousViolations = await _db.KpiSubmissions
                    .Where(x => x.SeasonId == previousSeason.Id && x.Status == KpiSubmissionStatus.Approved)
                    .SumAsync(x => (int?)x.WarningsAndRedCards);
                violationsReduction = GharsKpiCatalog.ViolationsReductionPercent(previousViolations, currentViolations);
            }
        }

        var complianceComponents = new[] { avgPlayerParticipation, avgEthics, avgDiet, avgAttendance }.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        decimal? complianceScore = complianceComponents.Count == 0 ? null : Math.Round(complianceComponents.Average(), 1);

        var lastSeason = seasons.Skip(1).FirstOrDefault();
        var currentSeasonParticipants = selectedSeasonId.HasValue ? await _db.AgendaEntries.Where(x => x.SeasonId == selectedSeasonId.Value).SumAsync(x => (int?)x.NumberOfParticipants) ?? 0 : totalParticipants;
        var baselineSeason = seasons.FirstOrDefault(x => x.TitleEn.Contains("2026") || x.StartDate.Year == 2026 || x.EndDate.Year == 2026);
        var baselineParticipants = baselineSeason == null ? 0 : await _db.AgendaEntries.Where(x => x.SeasonId == baselineSeason.Id).SumAsync(x => (int?)x.NumberOfParticipants) ?? 0;
        var growth = baselineParticipants == 0 ? 0 : Math.Round(((decimal)currentSeasonParticipants - baselineParticipants) / baselineParticipants * 100, 1);

        ViewBag.IsSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        ViewBag.ActiveSeason = activeSeason;
        ViewBag.Filters = new { seasonId = selectedSeasonId, clubId, partnerId, activityType, from, to, kpiCategory };
        ViewBag.Seasons = seasons.Select(x => new SelectListItem(x.TitleEn, x.Id.ToString(), selectedSeasonId == x.Id)).ToList();
        ViewBag.Clubs = clubs.Select(x => new SelectListItem(x.NameEn, x.Id.ToString(), clubId == x.Id)).ToList();
        ViewBag.Partners = partners.Select(x => new SelectListItem(x.NameEn, x.Id.ToString(), partnerId == x.Id)).ToList();

        ViewBag.TotalClubs = totalClubs;
        ViewBag.TotalPartners = totalPartners;
        ViewBag.TotalPrograms = totalPrograms;
        ViewBag.TotalParticipants = totalParticipants;
        ViewBag.TotalBookings = totalBookings;
        ViewBag.PendingApprovals = pendingApprovals;
        ViewBag.AttendanceRate = attendanceRate;
        ViewBag.SatisfactionScore = satisfactionScore;
        ViewBag.ProgramCoverage = programCoverage;
        ViewBag.ParticipationGrowth = growth;
        ViewBag.GenderDistribution = "Not captured";
        ViewBag.ActiveUsers = activeUsers;
        ViewBag.UpcomingEvents = upcomingEvents;
        ViewBag.CompletedActivities = completedActivities;
        ViewBag.CertificatesIssued = certificatesIssued;
        ViewBag.LibraryCount = libraryCount;
        ViewBag.GalleryUploadCount = galleryUploadCount;
        ViewBag.UnreadNotifications = unreadNotifications;
        ViewBag.ConfirmedBookings = confirmedBookings;
        ViewBag.RejectedBookings = rejectedBookings;
        ViewBag.ComplianceScore = complianceScore;
        ViewBag.AvgPlayerParticipation = avgPlayerParticipation;
        ViewBag.AvgEthics = avgEthics;
        ViewBag.AvgDiet = avgDiet;
        ViewBag.AvgAttendance = avgAttendance;
        ViewBag.AvgPhysicalActivity = avgPhysicalActivity;
        ViewBag.ViolationsReduction = violationsReduction;
        ViewBag.InternalStarScore = internalStarScore;
        // Operational check-in rate kept distinct from the KPI-submitted attendance figure.
        ViewBag.OperationalAttendanceRate = attendanceRate;

        var months = Enumerable.Range(0, 12).Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(-11 + i)).ToList();
        var monthLabels = months.Select(m => m.ToString("MMM yyyy")).ToList();
        var bookingTrend = new List<int>();
        var attendanceTrend = new List<int>();
        var uploadTrend = new List<int>();
        foreach (var m in months)
        {
            var start = m;
            var end = m.AddMonths(1);
            bookingTrend.Add(await bookings.CountAsync(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end));
            attendanceTrend.Add(await attendance.CountAsync(x => x.CheckInUtc >= start && x.CheckInUtc < end));
            uploadTrend.Add(await albums.CountAsync(x => x.AlbumDate >= start && x.AlbumDate < end));
        }

        var bookingStatus = await bookings.GroupBy(x => x.Status).Select(g => new { Label = g.Key.ToString(), Value = g.Count() }).ToListAsync();
        var programType = await activities.GroupBy(x => x.Type).Select(g => new { Label = g.Key.ToString(), Value = g.Count() }).ToListAsync();
        var agendaCategory = await agenda.GroupBy(x => x.Category).Select(g => new { Label = g.Key.ToString(), Value = g.Sum(x => x.NumberOfParticipants) }).ToListAsync();
        var mediaDistribution = await _db.MediaItems.GroupBy(x => x.MediaType).Select(g => new { Label = g.Key.ToString(), Value = g.Count() }).ToListAsync();
        var libraryDistribution = await library.GroupBy(x => x.ContentType).Select(g => new { Label = g.Key.ToString(), Value = g.Count() }).ToListAsync();

        // Targets and labels come from the central Ghars KPI catalog (never hardcoded per view).
        var isAr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        string KpiLabel(KpiDefinition k) => isAr ? k.NameAr : k.NameEn;
        var kpiTargets = new[]
        {
            new { Label = KpiLabel(GharsKpiCatalog.ProgramCoverage), Target = GharsKpiCatalog.ProgramCoverage.Target ?? 0m, Actual = programCoverage },
            new { Label = KpiLabel(GharsKpiCatalog.PlayerParticipation), Target = GharsKpiCatalog.PlayerParticipation.Target ?? 0m, Actual = avgPlayerParticipation ?? 0m },
            new { Label = KpiLabel(GharsKpiCatalog.AttendanceRate), Target = GharsKpiCatalog.AttendanceRate.Target ?? 0m, Actual = avgAttendance ?? 0m },
            new { Label = KpiLabel(GharsKpiCatalog.EthicalValues), Target = GharsKpiCatalog.EthicalValues.Target ?? 0m, Actual = avgEthics ?? 0m },
            new { Label = KpiLabel(GharsKpiCatalog.PhysicalActivity), Target = GharsKpiCatalog.PhysicalActivity.Target ?? 0m, Actual = avgPhysicalActivity ?? 0m },
            new { Label = KpiLabel(GharsKpiCatalog.HealthyDiet), Target = GharsKpiCatalog.HealthyDiet.Target ?? 0m, Actual = avgDiet ?? 0m },
            new { Label = KpiLabel(GharsKpiCatalog.Satisfaction), Target = GharsKpiCatalog.Satisfaction.Target ?? 0m, Actual = satisfactionScore ?? 0m }
        };

        var clubComparison = await kpis.Where(x => x.Organization != null)
            .Select(x => new { Club = x.Organization!.NameEn, Score = (x.PlayerParticipationRate + x.AttendanceRate + x.EthicalValuesAdherenceRate + x.HealthyDietaryHabitsRate + x.SatisfactionRate) / 5 })
            .OrderByDescending(x => x.Score)
            .Take(8)
            .ToListAsync();

        var partnerContribution = await activities.Where(x => x.PartnerOrganization != null)
            .GroupBy(x => x.PartnerOrganization!.NameEn)
            .Select(g => new { Partner = g.Key, Programs = g.Count(), Capacity = g.Sum(x => x.Capacity) })
            .OrderByDescending(x => x.Programs)
            .Take(8)
            .ToListAsync();

        var recentActivity = await bookings.OrderByDescending(x => x.CreatedAtUtc).Take(6)
            .Select(x => new { Title = x.Activity != null ? x.Activity.TitleEn : "Booking", Club = x.Organization != null ? x.Organization.NameEn : "Club", Status = x.Status.ToString(), Date = x.CreatedAtUtc.ToString("yyyy-MM-dd") })
            .ToListAsync();

        var upcomingList = await activities.Where(x => x.Status == ActivityStatus.Published && x.StartDateTime >= now)
            .OrderBy(x => x.StartDateTime).Take(6)
            .Select(x => new { Title = x.TitleEn, Entity = x.PartnerOrganization != null ? x.PartnerOrganization.NameEn : "Ghars", Type = x.Type.ToString(), Date = x.StartDateTime.ToString("yyyy-MM-dd HH:mm") })
            .ToListAsync();

        var approvals = new[]
        {
            new { Label = "Booking approvals", Value = await bookings.CountAsync(x => x.Status == BookingStatus.Pending || x.Status == BookingStatus.PendingPartnerApproval) },
            new { Label = "Proposed-time responses", Value = await bookings.CountAsync(x => x.Status == BookingStatus.PartnerProposedNewTime) },
            new { Label = "KPI submissions", Value = await kpis.CountAsync(x => x.Status == KpiSubmissionStatus.Submitted) },
            new { Label = "Agenda submissions", Value = await agenda.CountAsync(x => x.Status == AgendaEntryStatus.Submitted) }
        };

        var heatmap = await agenda.GroupBy(x => new { x.Organization!.NameEn, x.ActivityType })
            .Select(g => new { Club = g.Key.NameEn, Type = g.Key.ActivityType.ToString(), Count = g.Count() })
            .Take(30)
            .ToListAsync();

        ViewBag.MonthLabelsJson = JsonSerializer.Serialize(monthLabels);
        ViewBag.BookingTrendJson = JsonSerializer.Serialize(bookingTrend);
        ViewBag.AttendanceTrendJson = JsonSerializer.Serialize(attendanceTrend);
        ViewBag.UploadTrendJson = JsonSerializer.Serialize(uploadTrend);
        ViewBag.BookingStatusJson = JsonSerializer.Serialize(bookingStatus);
        ViewBag.ProgramTypeJson = JsonSerializer.Serialize(programType);
        ViewBag.AgendaCategoryJson = JsonSerializer.Serialize(agendaCategory);
        ViewBag.MediaDistributionJson = JsonSerializer.Serialize(mediaDistribution);
        ViewBag.LibraryDistributionJson = JsonSerializer.Serialize(libraryDistribution);
        ViewBag.KpiTargetsJson = JsonSerializer.Serialize(kpiTargets);
        ViewBag.ClubComparisonJson = JsonSerializer.Serialize(clubComparison);
        ViewBag.PartnerContributionJson = JsonSerializer.Serialize(partnerContribution);
        ViewBag.RecentActivity = recentActivity;
        ViewBag.UpcomingList = upcomingList;
        ViewBag.Approvals = approvals;
        ViewBag.Heatmap = heatmap;

        var coverageTarget = GharsKpiCatalog.ProgramCoverage.Target ?? 80m;
        var insights = new List<string>();
        insights.Add(growth >= 0
            ? $"Participation is {growth:0.#}% above the {GharsKpiCatalog.BaselineYear} baseline."
            : $"Participation is {Math.Abs(growth):0.#}% below the {GharsKpiCatalog.BaselineYear} baseline.");
        insights.Add(programCoverage >= coverageTarget
            ? "Program coverage is on target."
            : $"Program coverage is below the {coverageTarget:0}% target and needs intervention.");
        if (violationsReduction.HasValue)
        {
            var violationsTarget = GharsKpiCatalog.ViolationsReduction.Target ?? 15m;
            insights.Add(violationsReduction.Value >= violationsTarget
                ? $"Violations fell {violationsReduction.Value:0.#}% year on year, meeting the {violationsTarget:0}% reduction target."
                : $"Violations changed by {violationsReduction.Value:0.#}% year on year, short of the {violationsTarget:0}% reduction target.");
        }
        if (!avgPhysicalActivity.HasValue) insights.Add("Physical-activity compliance has not been reported by clubs for this selection.");
        insights.Add(pendingApprovals > 10 ? "Pending approvals exceed the operational threshold." : "Pending approvals are within a manageable range.");
        if (clubComparison.Any()) insights.Add($"{clubComparison.First().Club} currently leads club KPI performance.");
        if (upcomingEvents > 0) insights.Add($"{upcomingEvents} upcoming published activities are scheduled.");
        ViewBag.Insights = insights;

        return View();
    }
}
