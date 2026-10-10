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
    private static bool IsAr() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

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
            TempData["ToastWarning"] = IsAr()
                ? $"يمكن اعتماد الحجز أو رفضه هنا فقط عندما يكون بانتظار رد الجهة المنفذة. حالته الآن: {BookingStatusText.Label(booking.Status)}."
                : $"Only a booking awaiting the entity's response can be approved or rejected here. Its status is now: {BookingStatusText.Label(booking.Status)}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var old = new { booking.Status, booking.DecisionAtUtc, booking.DecidedByUserId };

        // Taken with a conditional update, so a second click or a decision the entity made a moment
        // ago cannot be overwritten.
        await using var tx = await Db.Database.BeginTransactionAsync();
        var claim = await BookingWorkflow.TryClaimAsync(Db, booking, BookingDecision.DscApprove, CurrentUserId);
        if (claim != BookingClaimResult.Claimed) return DecisionRefused(claim, booking);

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
        await tx.CommitAsync();
        // A DSC approval is still an approval: if the club picked one of the entity's published
        // times, that time is now taken and must stop being offered. Guarded on the slot being
        // Pending, so this is a no-op for every booking that carries no slot.
        await PartnerAvailabilityWorkflow.MarkBookedForBookingAsync(Db, booking, CurrentUserId);
        await AuditAsync("Approve", nameof(BookingRequest), id.ToString(), old, booking);

        // Persist notification + broadcast (target org)
        await CreateAndDispatchNotificationAsync(
            titleEn: "Booking approved by DSC",
            titleAr: "اعتمد المجلس طلب الحجز",
            messageEn: $"Your booking request for '{NotificationTitleEn(booking)}' has been approved by DSC.",
            messageAr: $"اعتمد المجلس طلب الحجز الخاص بكم للبرنامج '{NotificationTitleAr(booking)}'.",
            targetType: NotificationTargetType.Organization,
            targetOrganizationId: booking.OrganizationId,
            // The club's own booking page: the admin details page is not open to a club.
            linkUrl: Url.Action("Details", "Bookings", new { area = "", id = booking.Id })
        );

        TempData["ToastSuccess"] = IsAr() ? "تم اعتماد الحجز وإبلاغ النادي." : "Booking approved. The club has been notified.";
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
            TempData["ToastWarning"] = IsAr()
                ? $"يمكن اعتماد الحجز أو رفضه هنا فقط عندما يكون بانتظار رد الجهة المنفذة. حالته الآن: {BookingStatusText.Label(booking.Status)}."
                : $"Only a booking awaiting the entity's response can be approved or rejected here. Its status is now: {BookingStatusText.Label(booking.Status)}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var old = new { booking.Status, booking.DecisionAtUtc, booking.DecidedByUserId, booking.Notes };

        await using var tx = await Db.Database.BeginTransactionAsync();
        var claim = await BookingWorkflow.TryClaimAsync(Db, booking, BookingDecision.DscReject, CurrentUserId);
        if (claim != BookingClaimResult.Claimed) return DecisionRefused(claim, booking);

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
        await tx.CommitAsync();
        // Rejected by DSC frees the time again, exactly as a rejection by the entity does. After the
        // save, so a rejection that did not persist cannot release the slot underneath it.
        await PartnerAvailabilityWorkflow.ReleaseForBookingAsync(Db, booking, CurrentUserId);
        await AuditAsync("Reject", nameof(BookingRequest), id.ToString(), old, booking);

        await CreateAndDispatchNotificationAsync(
            titleEn: "Booking request rejected by DSC",
            titleAr: "رفض المجلس طلب الحجز",
            messageEn: $"Your booking request for '{NotificationTitleEn(booking)}' has been rejected by DSC.",
            messageAr: $"رفض المجلس طلب الحجز الخاص بكم للبرنامج '{NotificationTitleAr(booking)}'.",
            targetType: NotificationTargetType.Organization,
            targetOrganizationId: booking.OrganizationId,
            // The club's own booking page: the admin details page is not open to a club.
            linkUrl: Url.Action("Details", "Bookings", new { area = "", id = booking.Id })
        );

        TempData["ToastWarning"] = IsAr() ? "تم رفض طلب الحجز وإبلاغ النادي." : "Booking request rejected. The club has been notified.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>The decision lost its claim: the status changed or another decision got there first.</summary>
    private IActionResult DecisionRefused(BookingClaimResult claim, BookingRequest booking)
    {
        TempData["ToastWarning"] = claim == BookingClaimResult.NotAllowed
            ? (IsAr()
                ? $"يمكن اعتماد الحجز أو رفضه هنا فقط عندما يكون بانتظار رد الجهة المنفذة. حالته الآن: {BookingStatusText.Label(booking.Status)}."
                : $"Only a booking awaiting the entity's response can be approved or rejected here. Its status is now: {BookingStatusText.Label(booking.Status)}.")
            : (IsAr()
                ? "تم تحديث هذا الحجز قبل لحظات، لذلك لم يُنفَّذ الإجراء ولم يتم تغيير أي شيء. راجعوا حالته الحالية."
                : "This booking was updated a moment ago, so the action was not applied. Nothing was changed. Check its current status.");
        return RedirectToAction(nameof(Details), new { id = booking.Id });
    }

    // The program name for a notification, in each language regardless of who is reading now. Same
    // rule as BookingSource.Title: a custom request has no Activity, so it is named by its subject.
    private static string NotificationTitleEn(BookingRequest b) =>
        !string.IsNullOrWhiteSpace(b.Activity?.TitleEn) ? b.Activity!.TitleEn
        : !string.IsNullOrWhiteSpace(b.Activity?.TitleAr) ? b.Activity!.TitleAr!
        : !string.IsNullOrWhiteSpace(b.Subject) ? b.Subject! : "Custom program request";

    private static string NotificationTitleAr(BookingRequest b) =>
        !string.IsNullOrWhiteSpace(b.Activity?.TitleAr) ? b.Activity!.TitleAr!
        : !string.IsNullOrWhiteSpace(b.Activity?.TitleEn) ? b.Activity!.TitleEn
        : !string.IsNullOrWhiteSpace(b.Subject) ? b.Subject! : "طلب برنامج مخصص";

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

        // Recipients resolved on the server; the live push reaches only them.
        await NotificationDispatcher.SendAsync(Db, _hub, n);
    }
}
