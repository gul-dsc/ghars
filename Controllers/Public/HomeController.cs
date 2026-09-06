using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;

        var news = await _db.NewsItems
            .Where(x => x.IsActive &&
                        (x.StartsAtUtc == null || x.StartsAtUtc <= now) &&
                        (x.EndsAtUtc == null || x.EndsAtUtc >= now))
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        var season = await _db.Seasons
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        var upcomingActivities = await _db.Activities
            .Where(x => x.Status == ActivityStatus.Published && x.StartDateTime >= DateTime.UtcNow.AddDays(-1))
            .OrderBy(x => x.StartDateTime)
            .Take(6)
            .ToListAsync();

        var featuredPartners = await _db.PartnerProfiles
            .Include(x => x.Organization)
            .Where(x => x.IsFeatured && x.Organization != null && x.Organization.Status == ApprovalStatus.Approved)
            .OrderBy(x => x.FeatureSortOrder)
            .Take(8)
            .ToListAsync();

        var stories = await _db.SuccessStories
            .Where(x => x.IsPublished)
            .OrderByDescending(x => x.PublishedAtUtc)
            .Take(4)
            .ToListAsync();

        ViewBag.News = news;
        ViewBag.Season = season;
        ViewBag.UpcomingActivities = upcomingActivities;
        ViewBag.FeaturedPartners = featuredPartners;
        ViewBag.Stories = stories;

        return View();
    }

    public IActionResult About() => View();
    public IActionResult Vision() => View();

    public async Task<IActionResult> Partners()
    {
        var partners = await _db.PartnerProfiles
            .Include(x => x.Organization)
            .Where(x => x.Organization != null && x.Organization.Status == ApprovalStatus.Approved &&
                        (x.Organization.OrganizationType == OrganizationType.GovernmentAuthority || x.Organization.OrganizationType == OrganizationType.OtherPartner))
            .OrderBy(x => x.FeatureSortOrder)
            .ThenBy(x => x.Organization!.NameEn)
            .ToListAsync();

        return View(partners);
    }

    [HttpGet("/partners/{id:int}")]
    public async Task<IActionResult> PartnerDetails(int id)
    {
        var partner = await _db.PartnerProfiles
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.OrganizationId == id && x.Organization != null && x.Organization.Status == ApprovalStatus.Approved);
        if (partner is null) return NotFound();

        var partnerUserIds = await _db.OrganizationAdminLinks
            .Where(x => x.OrganizationId == id)
            .Select(x => x.UserId)
            .ToListAsync();
        ViewBag.ProgramCount = await _db.Activities.CountAsync(x => partnerUserIds.Contains(x.CreatedByUserId) && x.Status == ActivityStatus.Published);
        return View(partner);
    }

    [HttpGet("/partners/{id:int}/learning-programs")]
    public async Task<IActionResult> LearningPrograms(int id, ActivityType? type = null)
    {
        var partner = await _db.PartnerProfiles
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.OrganizationId == id && x.Organization != null && x.Organization.Status == ApprovalStatus.Approved);
        if (partner is null) return NotFound();

        var partnerUserIds = await _db.OrganizationAdminLinks
            .Where(x => x.OrganizationId == id)
            .Select(x => x.UserId)
            .ToListAsync();

        var q = _db.Activities
            .Include(x => x.BookingRequests)
            .Where(x => x.Status == ActivityStatus.Published && (x.PartnerOrganizationId == id || partnerUserIds.Contains(x.CreatedByUserId)));
        if (type.HasValue) q = q.Where(x => x.Type == type.Value);

        var programs = await q.OrderBy(x => x.StartDateTime).ToListAsync();
        ViewBag.Partner = partner;
        ViewBag.Type = type;
        return View(programs);
    }

    public IActionResult Contact() => View();
    public IActionResult Error() => View();
}
