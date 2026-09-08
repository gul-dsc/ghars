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


    // ── The two club booking paths ───────────────────────────────────────────────────────────────
    //
    //   /bookings/create?activityId=N   Book an Existing Program. Anchored to a DSC-approved,
    //                                   published offering; title, type, partner and season all
    //                                   derive from it server-side.
    //
    //   /bookings/custom                Request a Custom Program. The club names the program it
    //                                   needs and addresses it to an implementing entity. No
    //                                   Activity row is created — ActivityId stays NULL.
    //
    // Both render the same form and post to the same action: one workflow, one set of statuses, one
    // partner inbox. Only the entry point and the fields on show differ.
    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpGet("/bookings/create")]
    public Task<IActionResult> Create(int? activityId, int? partnerId, ActivityType? type = null)
        => BuildCreateViewAsync(activityId, partnerId, type);

    /// <summary>
    /// The Custom Program Request entry point. It deliberately takes no activityId: this route can
    /// only ever produce a custom request, so a stray id in the query string cannot quietly turn it
    /// into a program booking.
    /// </summary>
    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpGet("/bookings/custom")]
    public Task<IActionResult> Custom(int? partnerId, ActivityType? type = null)
        => BuildCreateViewAsync(null, partnerId, type);

    private async Task<IActionResult> BuildCreateViewAsync(int? activityId, int? partnerId, ActivityType? type)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var orgs = await UserClubsAsync(userId);

        Activity? activity = null;
        if (activityId.HasValue && activityId.Value > 0)
        {
            activity = await BookableOfferings().FirstOrDefaultAsync(x => x.Id == activityId.Value);
            if (activity is null) return NotFound();
        }

        await PopulateCreateViewDataAsync(activity, orgs);

        // Named explicitly: /bookings/custom enters through the Custom action, and view resolution
        // would otherwise look for a Custom.cshtml that does not exist. Both paths use this form.
        if (activity is not null)
        {
            return View("Create", new BookingCreateVm
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
                // Carried over from the offering because both sides use the same audience
                // vocabulary. The club can change it — this is a starting point, and the posted
                // value is what is validated and stored.
                TargetAudiences = string.IsNullOrWhiteSpace(activity.TargetAudienceCsv)
                    ? []
                    : activity.TargetAudienceCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                OtherTargetAudience = activity.OtherTargetAudience,
                ExpectedParticipants = 1
            });
        }

        var activeSeason = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();

        // Requests arriving from the public Booking page are for a training session or a workshop, so
        // that is what this entry point defaults to. Any other type is still selectable in the form —
        // the value is only a starting point, and the posted value is what is persisted.
        var requestedType = type is ActivityType.TrainingProgram or ActivityType.Workshop
            ? type.Value
            : ActivityType.TrainingProgram;

        return View("Create", new BookingCreateVm
        {
            OrganizationId = orgs.FirstOrDefault()?.Id ?? 0,
            PartnerOrganizationId = partnerId,
            SeasonId = activeSeason?.Id,
            RequestedActivityType = requestedType,
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
            // Re-validated from the database on every post. The offering id arrives from the browser
            // and is never trusted: a draft, withdrawn, expired or foreign-entity offering fails
            // here exactly as it would if it had never been rendered.
            activity = await BookableOfferings().FirstOrDefaultAsync(x => x.Id == vm.ActivityId.Value);

            // The same answer the GET gives for an id that resolves to nothing bookable — a draft,
            // withdrawn, expired or foreign-entity offering, or one that never existed. A model
            // error would be worse than useless here: the id lives in a hidden field with nothing to
            // anchor a message to, so the club would get the form back with no visible explanation.
            if (activity is null) return NotFound();

            partnerOrgId = await ResolvePartnerOrganizationIdAsync(activity);
            seasonId = activity.SeasonId;

            // An offering nobody owns has nobody to review the request: it would be stored with no
            // PartnerOrganizationId, notify no one, and appear in no partner's inbox. Refused the
            // same way rather than accepted into a dead end.
            if (partnerOrgId is null) return NotFound();

            // The program identity comes from the offering, never from the post. A club booking a
            // published program cannot rewrite its title or type into something else — that is what
            // a Custom Program Request is for, and keeping the two apart is what makes the Existing
            // Program badge mean anything.
            vm.Subject = activity.TitleEn;
            vm.RequestedActivityType = NormalizeRequestedActivityType(activity.Type);
            ModelState.Remove(nameof(vm.Subject));
            ModelState.Remove(nameof(vm.RequestedActivityType));
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
        // The request exactly as the club wrote it. Recorded at submission because a partner may
        // later propose a different program name or time, and accepting that overwrites the live
        // row — without this snapshot the original ask would only survive as the "old" half of a
        // later entry, and would be lost entirely if the club never accepted anything.
        _db.BookingAuditTrails.Add(new BookingAuditTrail
        {
            BookingRequestId = booking.Id,
            Action = "ClubSubmitted",
            NewValuesJson = JsonSerializer.Serialize(new
            {
                Source = booking.ActivityId.HasValue ? "ExistingProgram" : "CustomProgram",
                booking.ActivityId,
                ProgramName = booking.Subject,
                booking.RequestedActivityType,
                booking.ProposedStartDateTime,
                booking.ProposedEndDateTime,
                booking.TargetAudienceCsv,
                booking.OtherTargetAudience,
                booking.RequestedSeats,
                booking.AudienceDetails,
                booking.Notes,
                booking.SpecialRequirements,
                booking.PartnerOrganizationId
            }),
            AtUtc = DateTime.UtcNow,
            ByUserId = userId
        });
        await _db.SaveChangesAsync();

        var subjectEn = booking.Subject ?? activity?.TitleEn ?? "Ghars activity";
        var subjectAr = booking.Subject ?? activity?.TitleAr ?? subjectEn;

        // Only the entity the request was addressed to is notified — the club's own organization and
        // every unrelated entity are left alone.
        if (partnerOrgId.HasValue)
        {
            var club = orgs.FirstOrDefault(x => x.Id == booking.OrganizationId);
            var clubEn = club?.NameEn ?? "A club";
            var clubAr = club?.NameAr ?? club?.NameEn ?? "أحد الأندية";

            // A custom request says so in the title: it is the club's own description of what it
            // needs, not one of the entity's published programs, and it reads differently to review.
            if (activity is null)
            {
                await CreateAndDispatchNotificationAsync("New Custom Program Request", "طلب برنامج مخصص جديد",
                    $"{clubEn} requested a custom program '{subjectEn}' ({booking.ReferenceNumber}). Your review is required.",
                    $"طلب {clubAr} برنامجاً مخصصاً '{subjectAr}' ({booking.ReferenceNumber}). مطلوب المراجعة.",
                    NotificationType.Warning, NotificationTargetType.Organization, partnerOrgId.Value,
                    Url.Action(nameof(Details), "Bookings", new { area = "", id = booking.Id }));
            }
            else
            {
                await CreateAndDispatchNotificationAsync("New Booking Request", "طلب حجز جديد",
                    $"{clubEn} submitted booking request {booking.ReferenceNumber} for '{subjectEn}'. Your review is required.",
                    $"قدّم {clubAr} طلب الحجز {booking.ReferenceNumber} للنشاط '{subjectAr}'. مطلوب المراجعة.",
                    NotificationType.Warning, NotificationTargetType.Organization, partnerOrgId.Value,
                    Url.Action(nameof(Details), "Bookings", new { area = "", id = booking.Id }));
            }
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
            // The offering's supporting documents, shown on the existing-program path. A custom
            // request has no Activity, so this join simply yields nothing for it.
            .Include(x => x.Activity).ThenInclude(x => x!.Attachments)
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


    /// <summary>
    /// Every condition an offering must satisfy before a club may request it: it is published, its
    /// owning implementing entity is approved and is actually an implementing entity, it belongs to
    /// an <em>active</em> season, and today falls inside any availability window the partner set.
    ///
    /// The season must be active because that is already the rule for a custom request, which
    /// rejects an inactive season outright. Without it the two entry points disagreed: a club could
    /// not name a closed season on the form, but could still reach an offering sitting in one by
    /// passing its id. Every offering published today belongs to the active season, so this closes
    /// the gap without withdrawing anything.
    ///
    /// Type is deliberately not restricted here. The club catalogue surfaces only Training and
    /// Workshop, but 27 published Course-type and 2 Lecture-type programs are bookable today through
    /// <c>/partners/{id}/learning-programs</c>, and filtering by type at this point would silently
    /// withdraw them. Creation is where the type restriction belongs, and that is enforced in
    /// <see cref="PartnerProgramsController"/>.
    /// </summary>
    private IQueryable<Activity> BookableOfferings()
    {
        var now = DateTime.UtcNow;
        return _db.Activities
            .Include(x => x.Season)
            .Include(x => x.PartnerOrganization)
            // The offering's supporting documents, so a club sees the programme outline on the same
            // screen where it decides to request it. Both callers resolve a single row by id, so this
            // costs one extra join and never fans out over a list.
            .Include(x => x.Attachments)
            .Where(x => x.Status == ActivityStatus.Published
                        && x.Season != null
                        && x.Season.IsActive
                        && (x.AvailableFromUtc == null || x.AvailableFromUtc <= now)
                        && (x.AvailableUntilUtc == null || x.AvailableUntilUtc >= now)
                        // An offering with no explicit owner is still reachable through the older
                        // learning-programs route, where ownership resolves from the creator's
                        // organization link; those keep working unchanged.
                        && (x.PartnerOrganizationId == null
                            || (x.PartnerOrganization != null
                                && x.PartnerOrganization.Status == ApprovalStatus.Approved
                                && (x.PartnerOrganization.OrganizationType == OrganizationType.GovernmentAuthority
                                    || x.PartnerOrganization.OrganizationType == OrganizationType.OtherPartner)
                                // DSC review gate. A partner offering reaches a club only once a
                                // reviewer has approved it; Draft, SubmittedForApproval,
                                // ReturnedForCorrection, Rejected and Unpublished are all excluded.
                                // Enforced here rather than in the view, so a hand-typed activityId
                                // fails the same way a hidden card does.
                                && x.ApprovalStatus == OfferingApprovalStatus.Approved)));
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
