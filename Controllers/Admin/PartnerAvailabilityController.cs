using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

/// <summary>
/// DSC oversight of partner availability calendars, across every implementing entity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only, deliberately.</b> DSC can see what every entity has published and in what state;
/// it cannot add, edit or withdraw availability on an entity's behalf. Nobody has asked for that,
/// and read-only is the half of the decision that is reversible — the write side can be added later
/// without having to undo anything. This is the same posture the Ghars Channel and offering review
/// queues take towards content they oversee but do not author.
/// </para>
/// <para>
/// Availability is not a business metric. Nothing on this screen feeds KPI, the annual report or
/// any booking total, and the counts it shows are operational.
/// </para>
/// </remarks>
[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class PartnerAvailabilityController : Controllers.BaseController
{
    public PartnerAvailabilityController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index(
        int? partnerId = null,
        int? seasonId = null,
        PartnerAvailabilityStatus? status = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        // A bounded default rather than the whole table: the month around today, which is what an
        // unfiltered visit is actually asking to see.
        var today = GharsTime.Today;
        var start = from ?? today.AddDays(-7);
        var end = to ?? today.AddDays(60);
        if (end < start) (start, end) = (end, start);

        var query = Db.PartnerAvailabilitySlots
            .AsNoTracking()
            .Include(x => x.PartnerOrganization)
            .Include(x => x.Season)
            .Include(x => x.Activity)
            .Where(x => x.Date >= start && x.Date <= end);

        if (partnerId is > 0) query = query.Where(x => x.PartnerOrganizationId == partnerId.Value);
        if (seasonId is > 0) query = query.Where(x => x.SeasonId == seasonId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var slots = await query
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime).ThenBy(x => x.PartnerOrganizationId)
            .Take(1000)
            .ToListAsync();

        // One grouped query for the state summary over the same window and filters, rather than a
        // count per state.
        ViewBag.StateCounts = (await query
                .GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.Status, x => x.Count);

        // Which clubs hold the pending slots — one grouped query for the whole page.
        var heldIds = slots.Where(x => x.HeldByBookingRequestId.HasValue)
            .Select(x => x.HeldByBookingRequestId!.Value).Distinct().ToList();
        ViewBag.Holders = heldIds.Count == 0
            ? new Dictionary<int, string>()
            : (await Db.BookingRequests
                    .Where(x => heldIds.Contains(x.Id))
                    .Select(x => new { x.Id, Club = x.Organization!.NameEn })
                    .ToListAsync())
                .ToDictionary(x => x.Id, x => x.Club);

        ViewBag.Partners = await Db.Organizations.ApprovedPartners().ToListAsync();
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.PartnerId = partnerId;
        ViewBag.SeasonId = seasonId;
        ViewBag.Status = status;
        ViewBag.From = start;
        ViewBag.To = end;
        return View(slots);
    }
}
