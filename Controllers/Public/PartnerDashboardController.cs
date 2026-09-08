using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace GharsPlatform.Controllers.Public;

[Authorize(Roles = RoleNames.PartnerAdmin)]
public class PartnerDashboardController : Controller
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationsHub> _hub;

    public PartnerDashboardController(AppDbContext db, IHubContext<NotificationsHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpGet("/partner")]
    public async Task<IActionResult> Index(DateTime? from = null, DateTime? to = null, ActivityType? programType = null, BookingStatus? status = null, int? clubId = null, string? q = null, string? capacity = null, BookingSourceFilter? source = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var partnerOrgIds = await PartnerOrganizationIds(userId);
        var partnerId = partnerOrgIds.FirstOrDefault();
        var partner = await _db.Organizations.FirstOrDefaultAsync(x => x.Id == partnerId);

        var partnerUserIds = await _db.OrganizationAdminLinks
            .Where(x => partnerOrgIds.Contains(x.OrganizationId))
            .Select(x => x.UserId)
            .ToListAsync();

        var activityQuery = _db.Activities
            .Where(x => (x.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.PartnerOrganizationId.Value)) || partnerUserIds.Contains(x.CreatedByUserId))
            .AsQueryable();
        if (programType.HasValue) activityQuery = activityQuery.Where(x => x.Type == programType.Value);
        if (!string.IsNullOrWhiteSpace(q)) activityQuery = activityQuery.Where(x => x.TitleEn.Contains(q) || x.TitleAr.Contains(q));
        if (from.HasValue) activityQuery = activityQuery.Where(x => x.StartDateTime >= from.Value.Date);
        if (to.HasValue) activityQuery = activityQuery.Where(x => x.StartDateTime < to.Value.Date.AddDays(1));

        var bookingQuery = _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.ProposedTimeOptions).AsQueryable();
        if (partnerOrgIds.Count > 0) bookingQuery = bookingQuery.Where(x => (x.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.PartnerOrganizationId.Value)) || (x.Activity != null && ((x.Activity.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.Activity.PartnerOrganizationId.Value)) || partnerUserIds.Contains(x.Activity.CreatedByUserId))));
        if (status.HasValue) bookingQuery = bookingQuery.Where(x => x.Status == status.Value);
        if (clubId.HasValue) bookingQuery = bookingQuery.Where(x => x.OrganizationId == clubId.Value);
        // Booking Source. Both kinds live in this one inbox — the filter separates them on demand
        // rather than splitting the partner's work across two screens.
        if (source == BookingSourceFilter.ExistingProgram) bookingQuery = bookingQuery.Where(x => x.ActivityId != null);
        if (source == BookingSourceFilter.CustomProgram) bookingQuery = bookingQuery.Where(x => x.ActivityId == null);
        if (!string.IsNullOrWhiteSpace(q)) bookingQuery = bookingQuery.Where(x => (x.Activity != null && (x.Activity.TitleEn.Contains(q) || x.Activity.TitleAr.Contains(q))) || (x.Subject != null && x.Subject.Contains(q)));
        if (from.HasValue) bookingQuery = bookingQuery.Where(x => x.CreatedAtUtc >= from.Value.Date);
        if (to.HasValue) bookingQuery = bookingQuery.Where(x => x.CreatedAtUtc < to.Value.Date.AddDays(1));

        var activities = await activityQuery.OrderBy(x => x.StartDateTime).Take(50).ToListAsync();
        if (capacity == "low") activities = activities.Where(x => x.Capacity <= 10).ToList();
        if (capacity == "none") activities = activities.Where(x => x.Capacity <= 0).ToList();

        var bookings = await bookingQuery.OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync();
        var unread = await _db.NotificationDeliveries.Include(x => x.Notification).Where(x => x.UserId == userId && x.ReadAtUtc == null).OrderByDescending(x => x.Notification!.CreatedAtUtc).Take(5).ToListAsync();

        // Compact My Programs summary. Scoped by PartnerOrganizationId only — the same definition
        // "My Programs" uses — so the numbers here and the list behind the CTA always agree. The
        // broader activityQuery above also matches rows by creator and would over-count.
        ViewBag.ProgramStates = (await _db.Activities
                .Where(x => x.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.PartnerOrganizationId.Value)
                            && x.ApprovalStatus != null)
                .GroupBy(x => x.ApprovalStatus!.Value)
                .Select(g => new { State = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.State, x => x.Count);

        ViewBag.Partner = partner;
        ViewBag.Activities = activities;
        ViewBag.Bookings = bookings;
        ViewBag.Notifications = unread;
        ViewBag.Clubs = await _db.Organizations.ApprovedClubs().ToListAsync();
        ViewBag.From = from?.ToString("yyyy-MM-dd"); ViewBag.To = to?.ToString("yyyy-MM-dd"); ViewBag.ProgramType = programType; ViewBag.Status = status; ViewBag.ClubId = clubId; ViewBag.Query = q; ViewBag.Capacity = capacity; ViewBag.Source = source;
        return View();
    }

    [HttpPost("/partner/bookings/propose-times")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProposeTimes(int bookingId, DateTime[] starts, DateTime[] ends, string[]? notes, string? partnerComments, string? proposedSubject)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var partnerOrgIds = await PartnerOrganizationIds(userId);
        var partnerUserIds = await PartnerUserIds(partnerOrgIds);
        var booking = await _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.ProposedTimeOptions)
            .FirstOrDefaultAsync(x => x.Id == bookingId && ((x.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.PartnerOrganizationId.Value)) || (x.Activity != null && ((x.Activity.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.Activity.PartnerOrganizationId.Value)) || partnerUserIds.Contains(x.Activity.CreatedByUserId)))));
        if (booking is null) return NotFound();

        var valid = new List<(DateTime Start, DateTime End, string? Note)>();
        for (var i = 0; i < starts.Length; i++)
        {
            var end = i < ends.Length ? ends[i] : default;
            if (starts[i] == default || end == default || end <= starts[i]) continue;
            valid.Add((starts[i], end, notes != null && i < notes.Length ? notes[i] : null));
        }
        if (valid.Count == 0)
        {
            TempData["ToastWarning"] = "Add at least one valid proposed start and end time.";
            return RedirectToAction("Details", "Bookings", new { area = "", id = bookingId });
        }

        // Every free-text field here maps to a bounded column. Refused with a message rather than
        // left to fail as a SQL truncation error, and refused rather than silently trimmed so the
        // entity knows its wording did not reach the club intact.
        if (proposedSubject?.Trim().Length > 250 || partnerComments?.Length > 2000 || valid.Any(x => x.Note?.Length > 1000))
        {
            TempData["ToastWarning"] = "One of the values you entered is too long. | إحدى القيم المدخلة طويلة جداً.";
            return RedirectToAction("Details", "Bookings", new { area = "", id = bookingId });
        }

        var old = new { booking.Status, booking.PartnerResponseNotes, booking.ProposedSubject };
        foreach (var current in booking.ProposedTimeOptions) current.IsActive = false;
        foreach (var item in valid)
        {
            _db.BookingProposedTimeOptions.Add(new BookingProposedTimeOption
            {
                BookingRequestId = booking.Id,
                ProposedStartUtc = item.Start,
                ProposedEndUtc = item.End,
                Note = item.Note,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = userId,
                IsActive = true
            });
        }
        booking.Status = BookingStatus.PartnerProposedNewTime;
        booking.PartnerResponseNotes = partnerComments;
        // Optional subject modification (docs: entity may propose date / time / subject changes).
        // Applied to the booking subject only when the club accepts; audit keeps both values.
        booking.ProposedSubject = string.IsNullOrWhiteSpace(proposedSubject) ? null : proposedSubject.Trim();
        if (booking.PartnerOrganizationId is null && partnerOrgIds.Count > 0) booking.PartnerOrganizationId = partnerOrgIds[0];
        _db.BookingAuditTrails.Add(new BookingAuditTrail
        {
            BookingRequestId = booking.Id,
            Action = "PartnerProposedNewTime",
            OldValuesJson = JsonSerializer.Serialize(old),
            NewValuesJson = JsonSerializer.Serialize(new { booking.Status, Count = valid.Count, booking.PartnerResponseNotes, booking.ProposedSubject }),
            AtUtc = DateTime.UtcNow,
            ByUserId = userId
        });
        await _db.SaveChangesAsync();

        await CreateAndDispatchNotificationAsync("Partner Proposed New Time", "اقترح الشريك أوقاتاً جديدة",
            $"New time options were proposed for booking {booking.ReferenceNumber}. Your response is required.",
            $"تم اقتراح أوقات جديدة لطلب الحجز {booking.ReferenceNumber}. مطلوب ردكم.",
            NotificationType.Warning, NotificationTargetType.Organization, booking.OrganizationId,
            Url.Action("Details", "Bookings", new { area = "", id = booking.Id }));

        TempData["ToastSuccess"] = "Proposed time options sent to the club.";
        return RedirectToAction("Details", "Bookings", new { area = "", id = bookingId });
    }

    [HttpPost("/partner/bookings/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? lecturerName, string? lecturerContact, string? logistics)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var partnerOrgIds = await PartnerOrganizationIds(userId);
        var partnerUserIds = await PartnerUserIds(partnerOrgIds);
        var booking = await _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.PartnerOrganization)
            .FirstOrDefaultAsync(x => x.Id == id && ((x.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.PartnerOrganizationId.Value)) || (x.Activity != null && ((x.Activity.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.Activity.PartnerOrganizationId.Value)) || partnerUserIds.Contains(x.Activity.CreatedByUserId)))));
        if (booking is null) return NotFound();

        // Docs: confirmation must include the lecturer's name (contact + logistics accompany it).
        // The same requirement applies to both booking paths — a custom program is confirmed with
        // exactly the operational detail an existing-program booking is.
        if (string.IsNullOrWhiteSpace(lecturerName))
        {
            TempData["ToastWarning"] = "Lecturer name is required to confirm the booking. | اسم المحاضر مطلوب لتأكيد الحجز.";
            return RedirectToAction("Details", "Bookings", new { area = "", id });
        }

        if (lecturerName.Trim().Length > 200 || lecturerContact?.Trim().Length > 200 || logistics?.Length > 2000)
        {
            TempData["ToastWarning"] = "One of the values you entered is too long. | إحدى القيم المدخلة طويلة جداً.";
            return RedirectToAction("Details", "Bookings", new { area = "", id });
        }

        var old = new { booking.Status, booking.LecturerName, booking.LecturerContact, booking.PartnerResponseNotes };
        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedStartUtc = booking.ProposedStartDateTime ?? booking.Activity?.StartDateTime;
        booking.ConfirmedEndUtc = booking.ProposedEndDateTime ?? booking.Activity?.EndDateTime;
        booking.DecisionAtUtc = DateTime.UtcNow;
        booking.DecidedByUserId = userId;
        booking.LecturerName = lecturerName.Trim();
        booking.LecturerContact = lecturerContact?.Trim();
        if (!string.IsNullOrWhiteSpace(logistics))
        {
            booking.PartnerResponseNotes = string.IsNullOrWhiteSpace(booking.PartnerResponseNotes)
                ? logistics.Trim()
                : booking.PartnerResponseNotes + Environment.NewLine + logistics.Trim();
        }
        if (booking.PartnerOrganizationId is null && partnerOrgIds.Count > 0) booking.PartnerOrganizationId = partnerOrgIds[0];
        _db.BookingAuditTrails.Add(new BookingAuditTrail { BookingRequestId = booking.Id, Action = "PartnerApproved", OldValuesJson = JsonSerializer.Serialize(old), NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.LecturerName, booking.LecturerContact, booking.PartnerResponseNotes }), AtUtc = DateTime.UtcNow, ByUserId = userId });
        await _db.SaveChangesAsync();
        await BookingAgendaHelper.EnsureDraftAgendaEntryAsync(_db, booking, userId);
        await CreateAndDispatchNotificationAsync("Booking Confirmed", "تم تأكيد الحجز",
            $"Your booking request {booking.ReferenceNumber} was confirmed. Lecturer: {booking.LecturerName}{(string.IsNullOrWhiteSpace(booking.LecturerContact) ? "" : $" ({booking.LecturerContact})")}.",
            $"تم تأكيد طلب الحجز {booking.ReferenceNumber}. المحاضر: {booking.LecturerName}{(string.IsNullOrWhiteSpace(booking.LecturerContact) ? "" : $" ({booking.LecturerContact})")}.",
            NotificationType.Success, NotificationTargetType.Organization, booking.OrganizationId, Url.Action("Details", "Bookings", new { area = "", id = booking.Id }));
        return RedirectToAction("Details", "Bookings", new { area = "", id });
    }

    [HttpPost("/partner/bookings/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var partnerOrgIds = await PartnerOrganizationIds(userId);
        var partnerUserIds = await PartnerUserIds(partnerOrgIds);
        var booking = await _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id && ((x.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.PartnerOrganizationId.Value)) || (x.Activity != null && ((x.Activity.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.Activity.PartnerOrganizationId.Value)) || partnerUserIds.Contains(x.Activity.CreatedByUserId)))));
        if (booking is null) return NotFound();
        var old = new { booking.Status, booking.Notes };
        booking.Status = BookingStatus.Rejected;
        booking.DecisionAtUtc = DateTime.UtcNow;
        booking.DecidedByUserId = userId;
        if (!string.IsNullOrWhiteSpace(reason)) booking.Notes = (booking.Notes ?? "") + Environment.NewLine + "Partner rejection reason: " + reason;
        if (booking.PartnerOrganizationId is null && partnerOrgIds.Count > 0) booking.PartnerOrganizationId = partnerOrgIds[0];
        _db.BookingAuditTrails.Add(new BookingAuditTrail { BookingRequestId = booking.Id, Action = "PartnerRejected", OldValuesJson = JsonSerializer.Serialize(old), NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.Notes }), AtUtc = DateTime.UtcNow, ByUserId = userId });
        await _db.SaveChangesAsync();
        await CreateAndDispatchNotificationAsync("Booking Rejected", "تم رفض الحجز", $"Your booking request {booking.ReferenceNumber} was rejected.", $"تم رفض طلب الحجز {booking.ReferenceNumber}.", NotificationType.Danger, NotificationTargetType.Organization, booking.OrganizationId, Url.Action("Details", "Bookings", new { area = "", id = booking.Id }));
        return RedirectToAction("Details", "Bookings", new { area = "", id });
    }


    private async Task<List<string>> PartnerUserIds(List<int> partnerOrgIds)
        => await _db.OrganizationAdminLinks.Where(x => partnerOrgIds.Contains(x.OrganizationId)).Select(x => x.UserId).ToListAsync();

    private async Task<List<int>> PartnerOrganizationIds(string userId)
        => await _db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.UserId == userId && x.Organization != null && (x.Organization.OrganizationType == OrganizationType.OtherPartner || x.Organization.OrganizationType == OrganizationType.GovernmentAuthority))
            .Select(x => x.OrganizationId).ToListAsync();

    private async Task CreateAndDispatchNotificationAsync(string titleEn, string titleAr, string messageEn, string messageAr, NotificationType type, NotificationTargetType targetType, int targetOrganizationId, string? linkUrl)
    {
        var n = new Notification { TitleEn = titleEn, TitleAr = titleAr, MessageEn = messageEn, MessageAr = messageAr, Type = type, TargetType = targetType, TargetOrganizationId = targetOrganizationId, LinkUrl = linkUrl, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        var users = await _db.OrganizationAdminLinks.Where(x => x.OrganizationId == targetOrganizationId).Select(x => x.UserId).Distinct().ToListAsync();
        foreach (var uid in users) _db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = uid, DeliveredAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("notificationReceived", new { title = titleEn, message = messageEn, linkUrl });
    }
}
