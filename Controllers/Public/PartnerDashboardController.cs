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

/// <summary>
/// The implementing entity's booking inbox and its three decisions. Access follows
/// <see cref="BookingWorkflow"/>: a booking is the entity's when its implementing organization is one
/// the user is linked to now. The dashboard list, the details page and Confirm / Reject / Propose
/// all use that one rule, and each decision checks the booking's status on the server.
/// </summary>
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
        var partnerOrgIds = await BookingWorkflow.EntityOrganizationIdsAsync(_db, userId);
        var partnerId = partnerOrgIds.FirstOrDefault();
        var partner = await _db.Organizations.FirstOrDefaultAsync(x => x.Id == partnerId);

        // The entity's own programs: those recorded against its organization. Who typed them in is
        // not ownership.
        var activityQuery = _db.Activities
            .Where(x => x.PartnerOrganizationId.HasValue && partnerOrgIds.Contains(x.PartnerOrganizationId.Value))
            .AsQueryable();
        if (programType.HasValue) activityQuery = activityQuery.Where(x => x.Type == programType.Value);
        if (!string.IsNullOrWhiteSpace(q)) activityQuery = activityQuery.Where(x => x.TitleEn.Contains(q) || x.TitleAr.Contains(q));
        if (from.HasValue) activityQuery = activityQuery.Where(x => x.StartDateTime >= from.Value.Date);
        if (to.HasValue) activityQuery = activityQuery.Where(x => x.StartDateTime < to.Value.Date.AddDays(1));

        // An account with no entity link sees an empty inbox, never every booking on the platform.
        var bookingQuery = BookingWorkflow.ForEntities(
            _db.BookingRequests.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.ProposedTimeOptions),
            partnerOrgIds);
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
        // "My Programs" uses — so the numbers here and the list behind the CTA always agree.
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
        var booking = await FindEntityBookingAsync(userId, bookingId,
            q => q.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.ProposedTimeOptions));
        if (booking is null) return NotFound();

        if (!BookingWorkflow.IsAllowed(BookingDecision.EntityProposeTimes, booking.Status))
            return NotAllowed(booking);

        var valid = new List<(DateTime Start, DateTime End, string? Note)>();
        for (var i = 0; i < starts.Length; i++)
        {
            var end = i < ends.Length ? ends[i] : default;
            if (starts[i] == default || end == default || end <= starts[i]) continue;
            valid.Add((starts[i], end, notes != null && i < notes.Length ? notes[i] : null));
        }
        if (valid.Count == 0)
        {
            TempData["ToastWarning"] = T("Add at least one valid proposed start and end time.", "أضيفوا وقتاً مقترحاً واحداً صالحاً على الأقل (بداية ونهاية).");
            return RedirectToAction("Details", "Bookings", new { area = "", id = bookingId });
        }

        // Every free-text field here maps to a bounded column. Refused with a message rather than
        // left to fail as a SQL truncation error, and refused rather than silently trimmed so the
        // entity knows its wording did not reach the club intact.
        if (proposedSubject?.Trim().Length > 250 || partnerComments?.Length > 2000 || valid.Any(x => x.Note?.Length > 1000))
        {
            TempData["ToastWarning"] = T("One of the values you entered is too long.", "إحدى القيم المدخلة طويلة جداً.");
            return RedirectToAction("Details", "Bookings", new { area = "", id = bookingId });
        }

        var old = new { booking.Status, booking.PartnerResponseNotes, booking.ProposedSubject };

        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var claim = await BookingWorkflow.TryClaimAsync(_db, booking, BookingDecision.EntityProposeTimes, userId);
            if (claim != BookingClaimResult.Claimed) return Refused(claim, booking);

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
            booking.PartnerResponseNotes = partnerComments;
            // Optional subject modification (docs: entity may propose date / time / subject changes).
            // Applied to the booking subject only when the club accepts; audit keeps both values.
            booking.ProposedSubject = string.IsNullOrWhiteSpace(proposedSubject) ? null : proposedSubject.Trim();
            booking.PartnerOrganizationId ??= BookingWorkflow.ImplementingOrganizationId(booking);
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
            await tx.CommitAsync();
        }

        // AFTER the save, like every other slot transition: the entity is offering a different
        // time, so it is no longer holding the one the club picked from its calendar — the slot
        // goes back to Available and another club may take it. Releasing before the save would
        // free the slot even if the proposal itself failed to persist.
        //
        // BookingRequest.PartnerAvailabilitySlotId is deliberately NOT cleared: it records what this
        // club selected when it asked, which stays true whatever happens next. And no slot is
        // fabricated for the proposed times — those live in the existing BookingProposedTimeOption
        // workflow, which is unchanged.
        await PartnerAvailabilityWorkflow.ReleaseForBookingAsync(_db, booking, userId);

        await CreateAndDispatchNotificationAsync("New times proposed", "أوقات جديدة مقترحة",
            $"The implementing entity proposed new times for booking {booking.ReferenceNumber}. Please respond.",
            $"اقترحت الجهة المنفذة أوقاتاً جديدة لطلب الحجز {booking.ReferenceNumber}. يرجى الرد على الطلب.",
            NotificationType.Warning, NotificationTargetType.Organization, booking.OrganizationId,
            Url.Action("Details", "Bookings", new { area = "", id = booking.Id }));

        TempData["ToastSuccess"] = T("Proposed times sent to the club.", "تم إرسال الأوقات المقترحة إلى النادي.");
        return RedirectToAction("Details", "Bookings", new { area = "", id = bookingId });
    }

    [HttpPost("/partner/bookings/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? lecturerName, string? lecturerContact, string? logistics)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var booking = await FindEntityBookingAsync(userId, id,
            q => q.Include(x => x.Activity).Include(x => x.Organization).Include(x => x.PartnerOrganization));
        if (booking is null) return NotFound();

        if (!BookingWorkflow.IsAllowed(BookingDecision.EntityConfirm, booking.Status))
            return NotAllowed(booking);

        // Docs: confirmation must include the lecturer's name (contact + logistics accompany it).
        // The same requirement applies to both booking paths — a custom program is confirmed with
        // exactly the operational detail an existing-program booking is.
        if (string.IsNullOrWhiteSpace(lecturerName))
        {
            TempData["ToastWarning"] = T("Lecturer name is required to confirm the booking.", "اسم المحاضر مطلوب لتأكيد الحجز.");
            return RedirectToAction("Details", "Bookings", new { area = "", id });
        }

        if (lecturerName.Trim().Length > 200 || lecturerContact?.Trim().Length > 200 || logistics?.Length > 2000)
        {
            TempData["ToastWarning"] = T("One of the values you entered is too long.", "إحدى القيم المدخلة طويلة جداً.");
            return RedirectToAction("Details", "Bookings", new { area = "", id });
        }

        var old = new { booking.Status, booking.LecturerName, booking.LecturerContact, booking.PartnerResponseNotes };

        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var claim = await BookingWorkflow.TryClaimAsync(_db, booking, BookingDecision.EntityConfirm, userId);
            if (claim != BookingClaimResult.Claimed) return Refused(claim, booking);

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
            booking.PartnerOrganizationId ??= BookingWorkflow.ImplementingOrganizationId(booking);
            _db.BookingAuditTrails.Add(new BookingAuditTrail { BookingRequestId = booking.Id, Action = "PartnerApproved", OldValuesJson = JsonSerializer.Serialize(old), NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.LecturerName, booking.LecturerContact, booking.PartnerResponseNotes }), AtUtc = DateTime.UtcNow, ByUserId = userId });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // The confirmed times above already ARE the slot's times: a calendar booking's
        // ProposedStartDateTime was written from the slot and nothing since has changed it, so
        // confirmation needs no special case. All that is left is to retire the slot.
        await PartnerAvailabilityWorkflow.MarkBookedForBookingAsync(_db, booking, userId);
        await BookingAgendaHelper.EnsureDraftAgendaEntryAsync(_db, booking, userId);
        await CreateAndDispatchNotificationAsync("Booking Confirmed", "تم تأكيد الحجز",
            $"Your booking request {booking.ReferenceNumber} was confirmed. Lecturer: {booking.LecturerName}{(string.IsNullOrWhiteSpace(booking.LecturerContact) ? "" : $" ({booking.LecturerContact})")}.",
            $"تم تأكيد طلب الحجز {booking.ReferenceNumber}. المحاضر: {booking.LecturerName}{(string.IsNullOrWhiteSpace(booking.LecturerContact) ? "" : $" ({booking.LecturerContact})")}.",
            NotificationType.Success, NotificationTargetType.Organization, booking.OrganizationId, Url.Action("Details", "Bookings", new { area = "", id = booking.Id }));
        TempData["ToastSuccess"] = T("Booking confirmed. The club has been notified.", "تم تأكيد الحجز وإبلاغ النادي.");
        return RedirectToAction("Details", "Bookings", new { area = "", id });
    }

    [HttpPost("/partner/bookings/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var booking = await FindEntityBookingAsync(userId, id,
            q => q.Include(x => x.Activity).Include(x => x.Organization));
        if (booking is null) return NotFound();

        if (!BookingWorkflow.IsAllowed(BookingDecision.EntityReject, booking.Status))
            return NotAllowed(booking);

        var old = new { booking.Status, booking.Notes };

        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var claim = await BookingWorkflow.TryClaimAsync(_db, booking, BookingDecision.EntityReject, userId);
            if (claim != BookingClaimResult.Claimed) return Refused(claim, booking);

            booking.DecisionAtUtc = DateTime.UtcNow;
            booking.DecidedByUserId = userId;
            if (!string.IsNullOrWhiteSpace(reason)) booking.Notes = (booking.Notes ?? "") + Environment.NewLine + "Partner rejection reason: " + reason;
            booking.PartnerOrganizationId ??= BookingWorkflow.ImplementingOrganizationId(booking);
            _db.BookingAuditTrails.Add(new BookingAuditTrail { BookingRequestId = booking.Id, Action = "PartnerRejected", OldValuesJson = JsonSerializer.Serialize(old), NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.Notes }), AtUtc = DateTime.UtcNow, ByUserId = userId });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // Rejecting frees the time again. Guarded on the slot still being Pending, so a slot the
        // entity has since blocked or cancelled is not silently re-opened, and a booked one is
        // never released by an unrelated decision.
        await PartnerAvailabilityWorkflow.ReleaseForBookingAsync(_db, booking, userId);
        // The reason is still stored only in Notes (decision D3: no new field); here it is simply
        // repeated in the notification so the club does not have to open the booking to see it.
        // Shortened so a long reason can never push the message past its 2000-character column.
        var reasonText = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reasonText?.Length > 500) reasonText = reasonText[..500] + "…";
        await CreateAndDispatchNotificationAsync("Booking request rejected", "تم رفض طلب الحجز",
            $"Your booking request {booking.ReferenceNumber} was rejected by the implementing entity.{(reasonText is null ? "" : $" Reason for rejection: {reasonText}")}",
            $"رفضت الجهة المنفذة طلب الحجز {booking.ReferenceNumber}.{(reasonText is null ? "" : $" سبب الرفض: {reasonText}")}",
            NotificationType.Danger, NotificationTargetType.Organization, booking.OrganizationId, Url.Action("Details", "Bookings", new { area = "", id = booking.Id }));
        TempData["ToastSuccess"] = T("Booking request rejected. The club has been notified.", "تم رفض طلب الحجز وإبلاغ النادي.");
        return RedirectToAction("Details", "Bookings", new { area = "", id });
    }


    private static string T(string en, string ar) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? ar : en;

    /// <summary>The booking, if it belongs to one of the user's implementing entities now; otherwise null.</summary>
    private async Task<BookingRequest?> FindEntityBookingAsync(string userId, int bookingId, Func<IQueryable<BookingRequest>, IQueryable<BookingRequest>> include)
    {
        var entityOrgIds = await BookingWorkflow.EntityOrganizationIdsAsync(_db, userId);
        if (entityOrgIds.Count == 0) return null;
        return await BookingWorkflow.ForEntities(include(_db.BookingRequests), entityOrgIds)
            .FirstOrDefaultAsync(x => x.Id == bookingId);
    }

    /// <summary>The booking's status does not allow this decision; nothing was changed.</summary>
    private IActionResult NotAllowed(BookingRequest booking)
    {
        var label = BookingStatusText.Label(booking.Status, BookingStatusText.Viewer.Entity);
        TempData["ToastWarning"] = T(
            $"This request can no longer be answered this way: its status is now \"{label}\". Nothing was changed.",
            $"لم يعد بالإمكان تنفيذ هذا الإجراء على الطلب لأن حالته الآن: «{label}». لم يتم تغيير أي شيء.");
        return RedirectToAction("Details", "Bookings", new { area = "", id = booking.Id });
    }

    /// <summary>The claim lost: either the status no longer allows it, or another change got there first.</summary>
    private IActionResult Refused(BookingClaimResult claim, BookingRequest booking)
    {
        if (claim == BookingClaimResult.NotAllowed) return NotAllowed(booking);
        TempData["ToastWarning"] = T(
            "This booking was updated a moment ago, so your action was not applied. Nothing was changed. Check its current status and try again if needed.",
            "تم تحديث هذا الحجز قبل لحظات، لذلك لم يُنفَّذ إجراؤكم ولم يتم تغيير أي شيء. راجعوا حالته الحالية وحاولوا مرة أخرى عند الحاجة.");
        return RedirectToAction("Details", "Bookings", new { area = "", id = booking.Id });
    }

    private async Task CreateAndDispatchNotificationAsync(string titleEn, string titleAr, string messageEn, string messageAr, NotificationType type, NotificationTargetType targetType, int targetOrganizationId, string? linkUrl)
    {
        var n = new Notification { TitleEn = titleEn, TitleAr = titleAr, MessageEn = messageEn, MessageAr = messageAr, Type = type, TargetType = targetType, TargetOrganizationId = targetOrganizationId, LinkUrl = linkUrl, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) };
        // Recipients resolved on the server; the live push reaches only them.
        await NotificationDispatcher.SendAsync(_db, _hub, n);
    }
}
