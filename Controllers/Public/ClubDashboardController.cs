using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

[Authorize(Roles = RoleNames.ClubAdmin)]
public class ClubDashboardController : Controller
{
    private readonly AppDbContext _db;
    public ClubDashboardController(AppDbContext db) => _db = db;

    [HttpGet("/club")]
    public async Task<IActionResult> Index(int? partnerId = null, ActivityType? programType = null, BookingStatus? status = null, DateTime? from = null, DateTime? to = null, string? q = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var clubIds = await _db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.UserId == userId && x.Organization != null && x.Organization.OrganizationType == OrganizationType.Club)
            .Select(x => x.OrganizationId).ToListAsync();
        var club = await _db.Organizations.FirstOrDefaultAsync(x => clubIds.Contains(x.Id));

        var bookingQuery = _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.PartnerOrganization).Include(x => x.ProposedTimeOptions).AsQueryable();
        if (clubIds.Count > 0) bookingQuery = bookingQuery.Where(x => clubIds.Contains(x.OrganizationId));
        if (partnerId.HasValue) bookingQuery = bookingQuery.Where(x => x.PartnerOrganizationId == partnerId.Value);
        if (status.HasValue) bookingQuery = bookingQuery.Where(x => x.Status == status.Value);
        // Direct (entity-first) requests carry their own requested type; program bookings use the activity type.
        if (programType.HasValue) bookingQuery = bookingQuery.Where(x => x.Activity != null ? x.Activity.Type == programType.Value : x.RequestedActivityType == programType.Value);
        if (from.HasValue) bookingQuery = bookingQuery.Where(x => x.CreatedAtUtc >= from.Value.Date);
        if (to.HasValue) bookingQuery = bookingQuery.Where(x => x.CreatedAtUtc < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q)) bookingQuery = bookingQuery.Where(x => (x.Activity != null && (x.Activity.TitleEn.Contains(q) || x.Activity.TitleAr.Contains(q))) || (x.Subject != null && x.Subject.Contains(q)));

        var bookings = await bookingQuery.OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync();
        var partnerUserIds = await _db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.Organization != null && (x.Organization.OrganizationType == OrganizationType.OtherPartner || x.Organization.OrganizationType == OrganizationType.GovernmentAuthority))
            .Select(x => x.UserId)
            .ToListAsync();
        var programs = await _db.Activities
            .Where(x => x.Status == ActivityStatus.Published && x.StartDateTime >= DateTime.UtcNow && (x.PartnerOrganizationId.HasValue || partnerUserIds.Contains(x.CreatedByUserId)))
            .OrderBy(x => x.StartDateTime)
            .Take(50)
            .ToListAsync();
        var unread = await _db.NotificationDeliveries.Include(x => x.Notification).Where(x => x.UserId == userId && x.ReadAtUtc == null).OrderByDescending(x => x.Notification!.CreatedAtUtc).Take(5).ToListAsync();

        // Ghars Annual Report status for the current season - a status and a shortcut, not the report's
        // contents: the dashboard points at the obligation, the report screen is where it is done.
        var activeSeason = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        ViewBag.AnnualReportSeason = activeSeason;
        ViewBag.AnnualReport = activeSeason is null || club is null
            ? null
            : await _db.GharsAnnualReports.FirstOrDefaultAsync(x => x.OrganizationId == club.Id && x.SeasonId == activeSeason.Id);

        ViewBag.Club = club;
        ViewBag.Bookings = bookings;
        ViewBag.Programs = programs;
        ViewBag.Notifications = unread;
        ViewBag.Partners = await _db.Organizations.Where(x => x.Status == ApprovalStatus.Approved && (x.OrganizationType == OrganizationType.OtherPartner || x.OrganizationType == OrganizationType.GovernmentAuthority)).OrderBy(x => x.NameEn).ToListAsync();
        ViewBag.PartnerId = partnerId; ViewBag.ProgramType = programType; ViewBag.Status = status; ViewBag.From = from?.ToString("yyyy-MM-dd"); ViewBag.To = to?.ToString("yyyy-MM-dd"); ViewBag.Query = q;
        return View();
    }
}
