using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Public;

public class GalleryController : Controller
{
    private readonly AppDbContext _db;
    public GalleryController(AppDbContext db){_db=db;}

    [HttpGet("/gallery")]
    public async Task<IActionResult> Index(int? seasonId, int? clubId, int? activityId, DateTime? from, DateTime? to)
    {
        var q = _db.MediaAlbums
            .Include(x=>x.Season)
            .Include(x=>x.Organization)
            .Include(x=>x.Activity)
            .Include(x=>x.Items)
            .Where(x=>x.IsPublic)
            .AsQueryable();
        if(seasonId.HasValue) q=q.Where(x=>x.SeasonId==seasonId);
        if(clubId.HasValue) q=q.Where(x=>x.OrganizationId==clubId);
        if(activityId.HasValue) q=q.Where(x=>x.ActivityId==activityId);
        if(from.HasValue) q=q.Where(x=>x.AlbumDate>=from.Value);
        if(to.HasValue) q=q.Where(x=>x.AlbumDate<=to.Value);
        // Media uploaded by clubs through the Agenda (and official/press media added by DSC staff) is
        // aggregated here automatically - clubs never upload the same content twice.
        var itemsQuery = _db.GalleryItems
            .Include(x=>x.Organization)
            .Include(x=>x.Season)
            .Include(x=>x.Activity)
            .Where(x=>x.IsPublished)
            .AsQueryable();
        if(seasonId.HasValue) itemsQuery=itemsQuery.Where(x=>x.SeasonId==seasonId);
        if(clubId.HasValue) itemsQuery=itemsQuery.Where(x=>x.OrganizationId==clubId);
        if(activityId.HasValue) itemsQuery=itemsQuery.Where(x=>x.ActivityId==activityId);
        if(from.HasValue) itemsQuery=itemsQuery.Where(x=>x.MediaDate>=from.Value);
        if(to.HasValue) itemsQuery=itemsQuery.Where(x=>x.MediaDate<=to.Value);

        ViewBag.MediaItems = await itemsQuery.OrderByDescending(x=>x.MediaDate).Take(200).ToListAsync();
        ViewBag.Seasons=await _db.Seasons.OrderByDescending(x=>x.StartDate).ToListAsync();
        ViewBag.Clubs=await _db.Organizations.Where(x=>x.OrganizationType==OrganizationType.Club).OrderBy(x=>x.NameEn).ToListAsync();
        ViewBag.SelectedSeasonId=seasonId; ViewBag.SelectedClubId=clubId;
        ViewBag.From=from?.ToString("yyyy-MM-dd"); ViewBag.To=to?.ToString("yyyy-MM-dd");
        return View(await q.OrderByDescending(x=>x.AlbumDate).Take(200).ToListAsync());
    }

    [HttpGet("/gallery/album/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var album = await _db.MediaAlbums
            .Include(x=>x.Season)
            .Include(x=>x.Organization)
            .Include(x=>x.Activity)
            .Include(x=>x.Items.OrderBy(i=>i.SortOrder))
            .FirstOrDefaultAsync(x=>x.Id==id && x.IsPublic);
        if(album==null) return NotFound();
        return View(album);
    }
}
