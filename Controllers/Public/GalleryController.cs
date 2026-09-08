using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

/// <summary>
/// Ghars Channel (قناة غرس) — the programme's central content area, formerly presented as the Gallery.
///
/// The rename is user-facing only: this controller, its views and the tables behind them keep their
/// names, and <c>/gallery</c> keeps working so existing links and bookmarks do not break. <c>/channel</c>
/// is the primary route.
///
/// The channel carries two streams from one table: club activity media aggregated automatically from
/// the Agenda, and awareness/educational content contributed by implementing entities and government
/// partners. Visibility is <see cref="ChannelWorkflow.PubliclyVisible"/> and nothing else — a published
/// item that is either outside the partner workflow or approved by DSC.
/// </summary>
public class GalleryController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public GalleryController(AppDbContext db, IWebHostEnvironment env) { _db = db; _env = env; }

    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    [HttpGet("/channel")]
    [HttpGet("/gallery")]
    public async Task<IActionResult> Index(int? seasonId, int? clubId, int? activityId, DateTime? from, DateTime? to, string? category)
    {
        var q = _db.MediaAlbums
            .Include(x => x.Season)
            .Include(x => x.Organization)
            .Include(x => x.Activity)
            .Include(x => x.Items)
            .Where(x => x.IsPublic)
            .AsQueryable();
        if (seasonId.HasValue) q = q.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) q = q.Where(x => x.OrganizationId == clubId);
        if (activityId.HasValue) q = q.Where(x => x.ActivityId == activityId);
        if (from.HasValue) q = q.Where(x => x.AlbumDate >= from.Value);
        if (to.HasValue) q = q.Where(x => x.AlbumDate <= to.Value);

        // The channel feed. Media uploaded by clubs through the Agenda arrives here automatically, and
        // partner content appears only once DSC has approved it - both halves of the visibility rule are
        // applied by the same shared predicate, so no surface can implement only one of them.
        var itemsQuery = _db.GalleryItems
            .Include(x => x.Organization)
            .Include(x => x.Season)
            .Include(x => x.Activity)
            .Include(x => x.LibraryItem)
            .Where(ChannelWorkflow.PubliclyVisible);
        if (seasonId.HasValue) itemsQuery = itemsQuery.Where(x => x.SeasonId == seasonId);
        if (clubId.HasValue) itemsQuery = itemsQuery.Where(x => x.OrganizationId == clubId);
        if (activityId.HasValue) itemsQuery = itemsQuery.Where(x => x.ActivityId == activityId);
        if (from.HasValue) itemsQuery = itemsQuery.Where(x => x.MediaDate >= from.Value);
        if (to.HasValue) itemsQuery = itemsQuery.Where(x => x.MediaDate <= to.Value);

        var items = await itemsQuery.OrderByDescending(x => x.MediaDate).Take(300).ToListAsync();

        // Category filtering happens after materialisation because rows that predate the channel carry
        // no stored category and are classified from their existing fields. Filtering in SQL would drop
        // every one of them.
        var selected = ParseCategory(category);
        if (selected.HasValue)
            items = items.Where(x => ChannelWorkflow.ResolveCategory(x) == selected.Value).ToList();

        ViewBag.MediaItems = items;
        ViewBag.CategoryCounts = items
            .GroupBy(ChannelWorkflow.ResolveCategory)
            .ToDictionary(g => g.Key, g => g.Count());
        ViewBag.SelectedCategory = selected;
        ViewBag.Seasons = await _db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.Clubs = await _db.Organizations.ApprovedClubs().ToListAsync();
        ViewBag.SelectedSeasonId = seasonId; ViewBag.SelectedClubId = clubId;
        ViewBag.From = from?.ToString("yyyy-MM-dd"); ViewBag.To = to?.ToString("yyyy-MM-dd");
        ViewBag.CanContribute = User.IsInRole(RoleNames.ClubAdmin);
        return View(await q.OrderByDescending(x => x.AlbumDate).Take(200).ToListAsync());
    }

    [HttpGet("/channel/album/{id:int}")]
    [HttpGet("/gallery/album/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var album = await _db.MediaAlbums
            .Include(x => x.Season)
            .Include(x => x.Organization)
            .Include(x => x.Activity)
            .Include(x => x.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id && x.IsPublic);
        if (album == null) return NotFound();
        return View(album);
    }

    // ------------------------------------------------------------------ club contribution
    //
    // A second entry point for the club media stream, tied to a delivered activity exactly like the
    // Agenda one. There is deliberately no way to add club media with no activity behind it: the
    // channel is a record of what the club did, not a free photo album.

    [HttpGet("/channel/contribute")]
    [Authorize(Roles = RoleNames.ClubAdmin)]
    public async Task<IActionResult> Contribute(int? agendaEntryId)
    {
        var orgIds = await ClubOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        await PopulateContributeAsync(orgIds);
        ViewBag.SelectedAgendaEntryId = agendaEntryId;
        return View();
    }

    [HttpPost("/channel/contribute")]
    [Authorize(Roles = RoleNames.ClubAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contribute(int agendaEntryId, List<IFormFile>? mediaFiles, string? captionEn, string? captionAr)
    {
        var orgIds = await ClubOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        // Ownership is checked inside the query: the posted id is a filter, never a source of authority.
        var entry = await _db.AgendaEntries
            .FirstOrDefaultAsync(x => x.Id == agendaEntryId && orgIds.Contains(x.OrganizationId));
        if (entry is null) return NotFound();

        if (!SeasonClubStatistics.IsDelivered(entry.Status))
            ModelState.AddModelError(nameof(agendaEntryId), IsAr
                ? "يمكن إضافة الوسائط فقط لنشاط تم تنفيذه وإرساله."
                : "Media can only be added to an activity that has been delivered and submitted.");

        if (mediaFiles is null || mediaFiles.All(f => f.Length == 0))
            ModelState.AddModelError(nameof(mediaFiles), IsAr ? "يرجى اختيار ملف واحد على الأقل." : "Select at least one file.");

        foreach (var file in (mediaFiles ?? new List<IFormFile>()).Where(f => f.Length > 0))
        {
            var error = FileValidationHelper.Validate(file, FileValidationHelper.Media, IsAr);
            if (error != null) ModelState.AddModelError(nameof(mediaFiles), error);
        }

        if (!ModelState.IsValid)
        {
            await PopulateContributeAsync(orgIds);
            ViewBag.SelectedAgendaEntryId = agendaEntryId;
            return View();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var stored = await AgendaMediaPublisher.PublishAsync(_db, _env, entry, mediaFiles, userId, captionEn, captionAr);
        await AuditAsync("ChannelClubMediaAdded", entry.Id, new { AgendaEntryId = entry.Id, Files = stored });

        TempData["ToastSuccess"] = IsAr
            ? $"تمت إضافة {stored} من الوسائط إلى قناة غرس."
            : $"{stored} media file(s) added to the Ghars Channel.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<List<int>> ClubOrganizationIdsAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return await _db.OrganizationAdminLinks
            .Where(x => x.UserId == userId)
            .Select(x => x.OrganizationId)
            .ToListAsync();
    }

    /// <summary>
    /// The club's own delivered activities, which are the only things media may be attached to. Scoped
    /// by the caller's organization links, so the dropdown can never offer another club's activity.
    /// </summary>
    private async Task PopulateContributeAsync(List<int> orgIds)
    {
        ViewBag.AgendaEntries = await _db.AgendaEntries
            .Include(x => x.Season)
            .Where(x => orgIds.Contains(x.OrganizationId)
                        && (x.Status == AgendaEntryStatus.Submitted || x.Status == AgendaEntryStatus.Approved))
            .OrderByDescending(x => x.ActivityDate)
            .Take(200)
            .ToListAsync();
    }

    private static ChannelCategory? ParseCategory(string? value)
        => Enum.TryParse<ChannelCategory>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private async Task AuditAsync(string action, int entityId, object? newValues)
    {
        try
        {
            _db.SystemAuditLogs.Add(new SystemAuditLog
            {
                Action = action,
                EntityName = nameof(AgendaEntry),
                EntityId = entityId.ToString(),
                UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                NewValuesJson = newValues is null ? null : System.Text.Json.JsonSerializer.Serialize(newValues),
                AtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch { /* non-blocking audit */ }
    }
}
