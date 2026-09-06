using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Globalization;

namespace GharsPlatform.Controllers.Public;

[Authorize]
public class BookingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationsHub> _hub;

    public BookingsController(AppDbContext db, IHubContext<NotificationsHub> hub)
    {
        _db = db;
        _hub = hub;
    }


    // Supports both entry points:
    //  - activityId > 0 : booking anchored to a published partner program (existing, stronger flow)
    //  - partnerId / none: direct entity-first request per the approved booking document
    //    (club selects implementing entity + season, then describes the requested activity)
    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpGet("/bookings/create")]
    public async Task<IActionResult> Create(int? activityId, int? partnerId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var orgs = await UserClubsAsync(userId);

        Activity? activity = null;
        if (activityId.HasValue && activityId.Value > 0)
        {
            activity = await _db.Activities
                .Include(x => x.Season)
                .Include(x => x.PartnerOrganization)
                .FirstOrDefaultAsync(x => x.Id == activityId.Value && x.Status == ActivityStatus.Published);
            if (activity is null) return NotFound();
        }

        await PopulateCreateViewDataAsync(activity, orgs);

        if (activity is not null)
        {
            return View(new BookingCreateVm
            {
                ActivityId = activity.Id,
                OrganizationId = orgs.FirstOrDefault()?.Id ?? 0,
                PartnerOrganizationId = await ResolvePartnerOrganizationIdAsync(activity),
                SeasonId = activity.SeasonId,
                RequestedActivityType = NormalizeRequestedActivityType(activity.Type),
                Subject = activity.TitleEn,
                ProposedDate = DateOnly.FromDateTime(activity.StartDateTime),
                ProposedStartTime = TimeOnly.FromDateTime(activity.StartDateTime),
                ProposedEndTime = TimeOnly.FromDateTime(activity.EndDateTime),
                ExpectedParticipants = 1
            });
        }

        var activeSeason = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        return View(new BookingCreateVm
        {
            OrganizationId = orgs.FirstOrDefault()?.Id ?? 0,
            PartnerOrganizationId = partnerId,
            SeasonId = activeSeason?.Id,
            ExpectedParticipants = 1
        });
    }

    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpPost("/bookings/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BookingCreateVm vm)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var orgs = await UserClubsAsync(userId);
        var orgIds = orgs.Select(x => x.Id).ToList();
        var isAr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

        // Organization-scoping rule: the club is derived server-side from the authenticated user.
        // A single-club user can never book for another club regardless of the posted value.
        if (orgs.Count == 1)
        {
            vm.OrganizationId = orgs[0].Id;
            ModelState.Remove(nameof(vm.OrganizationId));
        }
        else if (!orgIds.Contains(vm.OrganizationId))
        {
            ModelState.AddModelError(nameof(vm.OrganizationId), isAr ? "لا يمكنكم الحجز لهذا النادي." : "You cannot book for this club.");
        }

        Activity? activity = null;
        int? partnerOrgId = null;
        int? seasonId = null;

        if (vm.ActivityId.HasValue && vm.ActivityId.Value > 0)
        {
            activity = await _db.Activities
                .Include(x => x.Season)
                .Include(x => x.PartnerOrganization)
                .FirstOrDefaultAsync(x => x.Id == vm.ActivityId.Value && x.Status == ActivityStatus.Published);
            if (activity is null)
            {
                ModelState.AddModelError(nameof(vm.ActivityId), isAr ? "البرنامج غير متاح للحجز حالياً." : "Program is not available for booking.");
            }
            else
            {
                partnerOrgId = await ResolvePartnerOrganizationIdAsync(activity);
                seasonId = activity.SeasonId;
            }
        }
        else
        {
            // Direct request: entity and season are validated server-side.
            var partner = await _db.Organizations.FirstOrDefaultAsync(x =>
                x.Id == vm.PartnerOrganizationId &&
                x.Status == ApprovalStatus.Approved &&
                (x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner));
            if (partner is null)
                ModelState.AddModelError(nameof(vm.PartnerOrganizationId), isAr ? "الجهة المنفذة غير صالحة." : "The selected implementing entity is not valid.");
            else
                partnerOrgId = partner.Id;

            var season = await _db.Seasons.FirstOrDefaultAsync(x => x.Id == vm.SeasonId && x.IsActive);
            if (season is null)
                ModelState.AddModelError(nameof(vm.SeasonId), isAr ? "الموسم الرياضي غير صالح أو غير نشط." : "The selected sports season is not valid or not active.");
            else
                seasonId = season.Id;
        }

        if (!ModelState.IsValid)
        {
            await PopulateCreateViewDataAsync(activity, orgs);
            return View(vm);
        }

        var proposedStart = vm.ProposedDate!.Value.ToDateTime(vm.ProposedStartTime!.Value);
        var proposedEnd = vm.ProposedDate.Value.ToDateTime(vm.ProposedEndTime!.Value);

        var booking = new BookingRequest
        {
            ActivityId = activity?.Id,
            SeasonId = seasonId,
            OrganizationId = vm.OrganizationId,
            PartnerOrganizationId = partnerOrgId,
            RequestedByUserId = userId,
            RequestedActivityType = vm.RequestedActivityType,
            Subject = vm.Subject?.Trim(),
            ProposedStartDateTime = proposedStart,
            ProposedEndDateTime = proposedEnd,
            TargetAudienceCsv = string.Join(",", vm.TargetAudiences.Distinct()),
            OtherTargetAudience = vm.OtherTargetAudience?.Trim(),
            AudienceDetails = vm.AudienceDetails?.Trim(),
            RequestedSeats = vm.ExpectedParticipants,
            ContactPersonName = vm.ContactPersonName?.Trim(),
            ContactPhone = vm.ContactPhone?.Trim(),
            ContactEmail = vm.ContactEmail?.Trim(),
            SpecialRequirements = vm.SpecialRequirements?.Trim(),
            Notes = vm.Notes,
            Status = BookingStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        };
        _db.BookingRequests.Add(booking);
        await _db.SaveChangesAsync();
        _db.BookingAuditTrails.Add(new BookingAuditTrail { BookingRequestId = booking.Id, Action = "ClubSubmitted", AtUtc = DateTime.UtcNow, ByUserId = userId });
        await _db.SaveChangesAsync();

        var subjectEn = booking.Subject ?? activity?.TitleEn ?? "Ghars activity";
        var subjectAr = booking.Subject ?? activity?.TitleAr ?? subjectEn;
        if (partnerOrgId.HasValue)
        {
            await CreateAndDispatchNotificationAsync("New Booking Request", "طلب حجز جديد",
                $"A club submitted booking request {booking.ReferenceNumber} for '{subjectEn}'. Your review is required.",
                $"قدم نادٍ طلب الحجز {booking.ReferenceNumber} للنشاط '{subjectAr}'. مطلوب المراجعة.",
                NotificationType.Warning, NotificationTargetType.Organization, partnerOrgId.Value,
                Url.Action(nameof(Details), "Bookings", new { area = "", id = booking.Id }));
        }

        TempData["ToastSuccess"] = isAr
            ? $"تم إرسال طلب الحجز بنجاح. رقم المرجع: {booking.ReferenceNumber}"
            : $"Booking request submitted successfully. Reference number: {booking.ReferenceNumber}";
        return RedirectToAction(nameof(Details), new { id = booking.Id });
    }

    [HttpGet("/bookings/details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var orgIds = await UserOrganizationIds(userId);
        var booking = await _db.BookingRequests
            .Include(x => x.Activity).ThenInclude(x => x!.Season)
            .Include(x => x.Season)
            .Include(x => x.Organization)
            .Include(x => x.PartnerOrganization)
            .Include(x => x.ProposedTimeOptions.OrderBy(o => o.ProposedStartUtc))
            .Include(x => x.AuditTrail.OrderByDescending(t => t.AtUtc))
            .FirstOrDefaultAsync(x => x.Id == id && (orgIds.Contains(x.OrganizationId) || (x.PartnerOrganizationId.HasValue && orgIds.Contains(x.PartnerOrganizationId.Value))));

        if (booking is null) return NotFound();
        ViewBag.IsClubSide = orgIds.Contains(booking.OrganizationId);
        ViewBag.IsPartnerSide = booking.PartnerOrganizationId.HasValue && orgIds.Contains(booking.PartnerOrganizationId.Value);
        return View(booking);
    }

    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpPost("/bookings/accept-proposed-time")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptProposedTime(int bookingId, int optionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var orgIds = await UserOrganizationIds(userId);
        var booking = await _db.BookingRequests
            .Include(x => x.Activity)
            .Include(x => x.Organization)
            .Include(x => x.PartnerOrganization)
            .Include(x => x.ProposedTimeOptions)
            .FirstOrDefaultAsync(x => x.Id == bookingId && orgIds.Contains(x.OrganizationId));
        if (booking is null) return NotFound();

        var option = booking.ProposedTimeOptions.FirstOrDefault(x => x.Id == optionId && x.IsActive);
        if (option is null)
        {
            TempData["ToastWarning"] = "Selected option is no longer available.";
            return RedirectToAction(nameof(Details), new { id = bookingId });
        }

        var old = new { booking.Status, booking.ConfirmedStartUtc, booking.ConfirmedEndUtc, booking.AcceptedProposedTimeOptionId, booking.Subject };
        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedStartUtc = option.ProposedStartUtc;
        booking.ConfirmedEndUtc = option.ProposedEndUtc;
        booking.AcceptedProposedTimeOptionId = option.Id;
        booking.DecisionAtUtc = DateTime.UtcNow;
        booking.DecidedByUserId = userId;

        // Accepting the partner's modification also accepts the proposed subject change (if any),
        // with the original value preserved in the audit trail (no silent overwrite).
        if (!string.IsNullOrWhiteSpace(booking.ProposedSubject))
        {
            booking.Subject = booking.ProposedSubject.Trim();
            booking.ProposedSubject = null;
        }

        foreach (var item in booking.ProposedTimeOptions)
        {
            item.IsSelected = item.Id == option.Id;
            item.IsActive = item.Id == option.Id;
        }
        _db.BookingAuditTrails.Add(new BookingAuditTrail
        {
            BookingRequestId = booking.Id,
            Action = "ClubAcceptedProposedTime",
            OldValuesJson = JsonSerializer.Serialize(old),
            NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.ConfirmedStartUtc, booking.ConfirmedEndUtc, booking.AcceptedProposedTimeOptionId, booking.Subject }),
            AtUtc = DateTime.UtcNow,
            ByUserId = userId
        });
        await _db.SaveChangesAsync();
        await BookingAgendaHelper.EnsureDraftAgendaEntryAsync(_db, booking, userId);

        if (booking.PartnerOrganizationId.HasValue)
        {
            await CreateAndDispatchNotificationAsync("Club Accepted Proposed Time", "وافق النادي على الوقت المقترح",
                $"{booking.Organization?.NameEn} accepted a proposed time for booking {booking.ReferenceNumber}.",
                $"وافق النادي على وقت مقترح لطلب الحجز {booking.ReferenceNumber}.",
                NotificationType.Success, NotificationTargetType.Organization, booking.PartnerOrganizationId.Value,
                Url.Action(nameof(Details), "Bookings", new { area = "", id = booking.Id }));
        }

        TempData["ToastSuccess"] = "Proposed time accepted and booking confirmed.";
        return RedirectToAction(nameof(Details), new { id = bookingId });
    }

    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpPost("/bookings/reject-proposed-times")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectProposedTimes(int bookingId, string? reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var orgIds = await UserOrganizationIds(userId);
        var booking = await _db.BookingRequests
            .Include(x => x.Activity).Include(x => x.Organization).Include(x => x.ProposedTimeOptions)
            .FirstOrDefaultAsync(x => x.Id == bookingId && orgIds.Contains(x.OrganizationId));
        if (booking is null) return NotFound();

        var old = new { booking.Status, booking.Notes, booking.ProposedSubject };
        booking.Status = BookingStatus.ClubRejectedProposedTimes;
        booking.ProposedSubject = null;
        if (!string.IsNullOrWhiteSpace(reason))
            booking.Notes = (booking.Notes ?? "") + Environment.NewLine + "Club rejected proposed times: " + reason;
        foreach (var option in booking.ProposedTimeOptions) option.IsActive = false;
        _db.BookingAuditTrails.Add(new BookingAuditTrail
        {
            BookingRequestId = booking.Id,
            Action = "ClubRejectedProposedTimes",
            OldValuesJson = JsonSerializer.Serialize(old),
            NewValuesJson = JsonSerializer.Serialize(new { booking.Status, booking.Notes }),
            AtUtc = DateTime.UtcNow,
            ByUserId = userId
        });
        await _db.SaveChangesAsync();

        if (booking.PartnerOrganizationId.HasValue)
        {
            await CreateAndDispatchNotificationAsync("Club Rejected Proposed Times", "رفض النادي الأوقات المقترحة",
                $"{booking.Organization?.NameEn} rejected all proposed times for booking {booking.ReferenceNumber}.",
                $"رفض النادي جميع الأوقات المقترحة لطلب الحجز {booking.ReferenceNumber}.",
                NotificationType.Warning, NotificationTargetType.Organization, booking.PartnerOrganizationId.Value,
                Url.Action(nameof(Details), "Bookings", new { area = "", id = booking.Id }));
        }

        TempData["ToastWarning"] = "All proposed times rejected.";
        return RedirectToAction(nameof(Details), new { id = bookingId });
    }


    private async Task<List<Organization>> UserClubsAsync(string userId)
        => await _db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.UserId == userId && x.Organization != null && x.Organization.OrganizationType == OrganizationType.Club && x.Organization.Status == ApprovalStatus.Approved)
            .Select(x => x.Organization!)
            .ToListAsync();

    private async Task<List<int>> UserOrganizationIds(string userId)
        => await _db.OrganizationAdminLinks.Where(x => x.UserId == userId).Select(x => x.OrganizationId).ToListAsync();

    private async Task PopulateCreateViewDataAsync(Activity? activity, List<Organization> orgs)
    {
        ViewBag.Activity = activity;
        ViewBag.Organizations = orgs;
        ViewBag.PartnerOrganization = activity is null ? null : await ResolvePartnerOrganizationAsync(activity);
        // Approved implementing entities + active seasons for direct entity-first requests.
        ViewBag.Entities = await _db.Organizations
            .Where(x => x.Status == ApprovalStatus.Approved && (x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner))
            .OrderBy(x => x.NameEn)
            .ToListAsync();
        ViewBag.Seasons = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).ToListAsync();
    }

    private async Task<Organization?> ResolvePartnerOrganizationAsync(Activity activity)
    {
        if (activity.PartnerOrganization is not null) return activity.PartnerOrganization;
        var partnerOrgId = await ResolvePartnerOrganizationIdAsync(activity);
        if (!partnerOrgId.HasValue) return null;
        return await _db.Organizations.FirstOrDefaultAsync(x => x.Id == partnerOrgId.Value);
    }

    private async Task<int?> ResolvePartnerOrganizationIdAsync(Activity activity)
    {
        if (activity.PartnerOrganizationId.HasValue) return activity.PartnerOrganizationId;

        return await _db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.UserId == activity.CreatedByUserId && x.Organization != null && (x.Organization.OrganizationType == OrganizationType.OtherPartner || x.Organization.OrganizationType == OrganizationType.GovernmentAuthority))
            .Select(x => (int?)x.OrganizationId)
            .FirstOrDefaultAsync();
    }

    private static ActivityType NormalizeRequestedActivityType(ActivityType type)
        => type == ActivityType.Activity ? ActivityType.Event : type;

    private async Task CreateAndDispatchNotificationAsync(string titleEn, string titleAr, string messageEn, string messageAr, NotificationType type, NotificationTargetType targetType, int targetOrganizationId, string? linkUrl)
    {
        var n = new Notification
        {
            TitleEn = titleEn,
            TitleAr = titleAr,
            MessageEn = messageEn,
            MessageAr = messageAr,
            Type = type,
            TargetType = targetType,
            TargetOrganizationId = targetOrganizationId,
            LinkUrl = linkUrl,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        var users = await _db.OrganizationAdminLinks.Where(x => x.OrganizationId == targetOrganizationId).Select(x => x.UserId).Distinct().ToListAsync();
        foreach (var uid in users)
            _db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = uid, DeliveredAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("notificationReceived", new { title = titleEn, message = messageEn, linkUrl });
    }
}
