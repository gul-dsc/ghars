using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

[Authorize]
public class NotificationsController : Controller
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) => _db = db;

    [HttpGet("/notifications")]
    public async Task<IActionResult> Inbox(string? state = null, NotificationType? type = null, DateTime? from = null, DateTime? to = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var q = _db.NotificationDeliveries.Include(x => x.Notification)
            .Where(x => x.UserId == userId && x.Notification != null);

        if (state == "unread") q = q.Where(x => x.ReadAtUtc == null);
        if (state == "read") q = q.Where(x => x.ReadAtUtc != null);
        if (type.HasValue) q = q.Where(x => x.Notification!.Type == type.Value);
        if (from.HasValue) q = q.Where(x => x.Notification!.CreatedAtUtc >= from.Value.Date);
        if (to.HasValue) q = q.Where(x => x.Notification!.CreatedAtUtc < to.Value.Date.AddDays(1));

        ViewBag.State = state;
        ViewBag.Type = type;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");

        var list = await q.OrderByDescending(x => x.Notification!.CreatedAtUtc).Take(300).ToListAsync();
        return View(list);
    }

    [HttpGet("/notifications/open/{id:int}")]
    public async Task<IActionResult> Open(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var d = await _db.NotificationDeliveries.Include(x => x.Notification)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (d is null || d.Notification is null) return NotFound();

        if (d.ReadAtUtc is null)
        {
            d.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        if (!string.IsNullOrWhiteSpace(d.Notification.LinkUrl))
            return LocalRedirect(d.Notification.LinkUrl);

        return RedirectToAction(nameof(Inbox));
    }

    [HttpPost("/notifications/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var d = await _db.NotificationDeliveries.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (d is null) return NotFound();
        if (d.ReadAtUtc is null)
        {
            d.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Inbox));
    }

    [HttpPost("/notifications/read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var list = await _db.NotificationDeliveries.Where(x => x.UserId == userId && x.ReadAtUtc == null).ToListAsync();
        foreach (var item in list) item.ReadAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Inbox));
    }
}
