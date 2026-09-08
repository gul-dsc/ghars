using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class BookingsController : Controllers.BaseController
{
    private readonly IHubContext<NotificationsHub> _hub;

    public BookingsController(AppDbContext db, IHubContext<NotificationsHub> hub) : base(db)
    {
        _hub = hub;
    }

    public async Task<IActionResult> Index(string? status = null, BookingSourceFilter? source = null)
    {
        IQueryable<BookingRequest> q = Db.BookingRequests
            .Include(x => x.Activity)
            .Include(x => x.Organization)
            // Custom requests carry no Activity, so the implementing entity is the only place their
            // destination is recorded. Without this the admin list could not name it.
            .Include(x => x.PartnerOrganization);

        if (Enum.TryParse<BookingStatus>(status ?? "", out var st))
            q = q.Where(x => x.Status == st);

        // Booking Source. Derived from ActivityId at query time rather than stored, so it cannot
        // disagree with the data.
        if (source == BookingSourceFilter.ExistingProgram) q = q.Where(x => x.ActivityId != null);
        if (source == BookingSourceFilter.CustomProgram) q = q.Where(x => x.ActivityId == null);

        ViewBag.Status = status;
        ViewBag.Source = source;
        var list = await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Details(int id)
    {
        var booking = await Db.BookingRequests
            .Include(x => x.Activity)
            .Include(x => x.Organization)
            .Include(x => x.PartnerOrganization)
            .Include(x => x.ProposedTimeOptions.OrderBy(o => o.ProposedStartUtc))
            .Include(x => x.AuditTrail.OrderByDescending(t => t.AtUtc))
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking is null) return NotFound();
        return View(booking);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Approve(int id)
    {
        var booking = await Db.BookingRequests
            .Include(x => x.Activity)
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Pending)
        {
            TempData["ToastWarning"] = "This booking is not pending.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var old = new { booking.Status, booking.DecisionAtUtc, booking.DecidedByUserId };

        booking.Status = BookingStatus.Approved;
        booking.DecisionAtUtc = DateTime.UtcNow;
        booking.DecidedByUserId = CurrentUserId;

        Db.BookingAuditTrails.Add(new BookingAuditTrail
        {
            BookingRequestId = booking.Id,
            Action = "Approved",
            OldValuesJson = JsonSerializer.Serialize(old),
            NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.DecisionAtUtc, booking.DecidedByUserId }),
            AtUtc = DateTime.UtcNow,
            ByUserId = CurrentUserId
        });

        await Db.SaveChangesAsync();
        await AuditAsync("Approve", nameof(BookingRequest), id.ToString(), old, booking);

        // Persist notification + broadcast (target org)
        await CreateAndDispatchNotificationAsync(
            titleEn: "Booking Approved",
            titleAr: "تمت الموافقة على الحجز",
            messageEn: $"Your booking request for '{booking.Activity?.TitleEn}' has been approved.",
            messageAr: $"تمت الموافقة على طلب الحجز للنشاط '{booking.Activity?.TitleAr}'.",
            targetType: NotificationTargetType.Organization,
            targetOrganizationId: booking.OrganizationId,
            linkUrl: Url.Action("Details", "Bookings", new { area = "Admin", id = booking.Id })
        );

        TempData["ToastSuccess"] = "Booking approved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var booking = await Db.BookingRequests
            .Include(x => x.Activity)
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Pending)
        {
            TempData["ToastWarning"] = "This booking is not pending.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var old = new { booking.Status, booking.DecisionAtUtc, booking.DecidedByUserId, booking.Notes };

        booking.Status = BookingStatus.Rejected;
        booking.DecisionAtUtc = DateTime.UtcNow;
        booking.DecidedByUserId = CurrentUserId;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var prefix = $"Rejected reason: {reason}";
            booking.Notes = string.IsNullOrWhiteSpace(booking.Notes) ? prefix : prefix + Environment.NewLine + booking.Notes;
        }

        Db.BookingAuditTrails.Add(new BookingAuditTrail
        {
            BookingRequestId = booking.Id,
            Action = "Rejected",
            OldValuesJson = JsonSerializer.Serialize(old),
            NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.DecisionAtUtc, booking.DecidedByUserId, booking.Notes }),
            AtUtc = DateTime.UtcNow,
            ByUserId = CurrentUserId
        });

        await Db.SaveChangesAsync();
        await AuditAsync("Reject", nameof(BookingRequest), id.ToString(), old, booking);

        await CreateAndDispatchNotificationAsync(
            titleEn: "Booking Rejected",
            titleAr: "تم رفض الحجز",
            messageEn: $"Your booking request for '{booking.Activity?.TitleEn}' has been rejected.",
            messageAr: $"تم رفض طلب الحجز للنشاط '{booking.Activity?.TitleAr}'.",
            targetType: NotificationTargetType.Organization,
            targetOrganizationId: booking.OrganizationId,
            linkUrl: Url.Action("Details", "Bookings", new { area = "Admin", id = booking.Id })
        );

        TempData["ToastWarning"] = "Booking rejected.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task CreateAndDispatchNotificationAsync(
        string titleEn,
        string titleAr,
        string messageEn,
        string messageAr,
        NotificationTargetType targetType,
        int? targetOrganizationId,
        string? linkUrl)
    {
        var n = new Notification
        {
            TitleEn = titleEn,
            TitleAr = titleAr,
            MessageEn = messageEn,
            MessageAr = messageAr,
            Type = NotificationType.Info,
            TargetType = targetType,
            TargetOrganizationId = targetOrganizationId,
            LinkUrl = linkUrl,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        Db.Notifications.Add(n);
        await Db.SaveChangesAsync();

        // Deliveries: resolve target users at write time so read status is per user
        var userIds = new List<string>();

        if (targetType == NotificationTargetType.Organization && targetOrganizationId.HasValue)
        {
            userIds = await Db.OrganizationAdminLinks
                .Where(x => x.OrganizationId == targetOrganizationId.Value)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync();
        }

        foreach (var uid in userIds)
        {
            Db.NotificationDeliveries.Add(new NotificationDelivery
            {
                NotificationId = n.Id,
                UserId = uid,
                DeliveredAtUtc = DateTime.UtcNow
            });
        }

        await Db.SaveChangesAsync();

        // SignalR broadcast (all connected users will receive, client filters by user deliveries)
        await _hub.Clients.All.SendAsync("notification", new
        {
            id = n.Id,
            titleEn = n.TitleEn,
            titleAr = n.TitleAr,
            messageEn = n.MessageEn,
            messageAr = n.MessageAr,
            type = n.Type.ToString(),
            linkUrl = n.LinkUrl,
            createdAtUtc = n.CreatedAtUtc
        });
    }
}
