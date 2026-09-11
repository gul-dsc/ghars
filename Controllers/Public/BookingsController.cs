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
    public Task<IActionResult> Create(int? activityId, int? partnerId, ActivityType? type = null, int? slotId = null)
        => BuildCreateViewAsync(activityId, partnerId, type, slotId);

    /// <summary>
    /// The Custom Program Request entry point. It deliberately takes no activityId: this route can
    /// only ever produce a custom request, so a stray id in the query string cannot quietly turn it
    /// into a program booking.
    /// </summary>
    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpGet("/bookings/custom")]
    public Task<IActionResult> Custom(int? partnerId, ActivityType? type = null, int? slotId = null)
        => BuildCreateViewAsync(null, partnerId, type, slotId);

    /// <param name="slotId">
    /// A slot the club already chose, on the entity's availability page or before signing in. It is
    /// a <em>pre-selection</em> only: it is re-validated here against the same query that decides
    /// what may be offered, and re-validated again from scratch on post. An id that no longer
    /// qualifies is dropped silently and the form opens on the manual date and time fields, which
    /// is the right outcome — the club can still ask.
    /// </param>
    private async Task<IActionResult> BuildCreateViewAsync(int? activityId, int? partnerId, ActivityType? type, int? slotId = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var orgs = await UserClubsAsync(userId);

        Activity? activity = null;
        if (activityId.HasValue && activityId.Value > 0)
        {
            activity = await BookableOfferings().FirstOrDefaultAsync(x => x.Id == activityId.Value);
            if (activity is null) return NotFound();
        }

        await PopulateCreateViewDataAsync(activity, orgs,
            partnerOrganizationId: activity is not null ? await ResolvePartnerOrganizationIdAsync(activity) : partnerId,
            activityId: activity?.Id);

        // Named explicitly: /bookings/custom enters through the Custom action, and view resolution
        // would otherwise look for a Custom.cshtml that does not exist. Both paths use this form.
        if (activity is not null)
        {
            var activityPartnerId = await ResolvePartnerOrganizationIdAsync(activity);
            var preselected = await ResolvePreselectedSlotAsync(slotId, activityPartnerId, activity.Id);

            return View("Create", new BookingCreateVm
            {
                ActivityId = activity.Id,
                OrganizationId = orgs.FirstOrDefault()?.Id ?? 0,
                PartnerOrganizationId = activityPartnerId,
                SeasonId = activity.SeasonId,
                RequestedActivityType = NormalizeRequestedActivityType(activity.Type),
                Subject = activity.TitleEn,
                PartnerAvailabilitySlotId = preselected?.Id,
                // From the slot when one was chosen; otherwise the offering's indicative session
                // times, exactly as before.
                ProposedDate = preselected?.Date ?? DateOnly.FromDateTime(activity.StartDateTime),
                ProposedStartTime = preselected?.StartTime ?? TimeOnly.FromDateTime(activity.StartDateTime),
                ProposedEndTime = preselected?.EndTime ?? TimeOnly.FromDateTime(activity.EndDateTime),
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

        var customSlot = await ResolvePreselectedSlotAsync(slotId, partnerId, activityId: null);

        return View("Create", new BookingCreateVm
        {
            OrganizationId = orgs.FirstOrDefault()?.Id ?? 0,
            PartnerOrganizationId = partnerId,
            // The slot's own season wins when one was chosen, so the form opens consistent with what
            // the server will accept.
            SeasonId = customSlot?.SeasonId ?? activeSeason?.Id,
            RequestedActivityType = requestedType,
            PartnerAvailabilitySlotId = customSlot?.Id,
            ProposedDate = customSlot?.Date,
            ProposedStartTime = customSlot?.StartTime,
            ProposedEndTime = customSlot?.EndTime,
            ExpectedParticipants = 1
        });
    }

    /// <summary>
    /// Re-reads a pre-selected slot through the one query that decides what a club may be offered,
    /// so a hand-typed <c>slotId</c> cannot put another entity's — or an already-taken — time onto
    /// the form. Returns <c>null</c> for anything that does not qualify, and the form then simply
    /// opens without a selection.
    /// </summary>
    private async Task<PartnerAvailabilitySlot?> ResolvePreselectedSlotAsync(int? slotId, int? partnerOrganizationId, int? activityId)
    {
        if (slotId is not > 0 || partnerOrganizationId is not > 0) return null;

        return await PartnerAvailabilityWorkflow
            .SelectableFor(_db, partnerOrganizationId.Value, activityId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == slotId.Value);
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
            // Direct request: entity and season are validated server-side against the one eligibility
            // definition, so a hand-posted id for a deactivated, dummy or club organization is refused
            // even though no screen offers it.
            var partner = await _db.Organizations
                .ApprovedPartners()
                .FirstOrDefaultAsync(x => x.Id == vm.PartnerOrganizationId);
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

        // ── Schedule Source: the club selected one of the entity's published availability slots ──
        //
        // Nothing about the posted schedule is trusted. The slot is re-read through exactly the
        // query that decided what to show — so it must still be Available, still in the future in
        // Dubai time, still in an active season, still owned by an approved entity, and still
        // applicable to this request — and the date and both times are then overwritten FROM the
        // slot. A post pairing a real slot id with a different time is therefore stored as the
        // slot, never as the post.
        //
        // This is only a pre-check. The slot is not claimed here; it is claimed atomically below,
        // because between this read and the insert another club can take it.
        PartnerAvailabilitySlot? slot = null;
        if (vm.PartnerAvailabilitySlotId is > 0)
        {
            var slotGoneEn = "This time slot is no longer available. Please choose another time.";
            var slotGoneAr = "لم تعد هذه الفترة الزمنية متاحة. يرجى اختيار وقت آخر.";

            slot = partnerOrgId.HasValue
                ? await PartnerAvailabilityWorkflow.SelectableFor(_db, partnerOrgId.Value, activity?.Id)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == vm.PartnerAvailabilitySlotId.Value)
                : null;

            // Covers every way the selection can be wrong at once, and answers them all the same
            // way: a slot that has since been claimed, blocked, cancelled or passed; one belonging
            // to a different entity than the club selected; one offered only for another programme;
            // and one that never existed. Distinguishing them would tell a club things about
            // another club's requests.
            if (slot is null || slot.SeasonId != seasonId)
            {
                slot = null;
                // Cleared so the re-rendered form comes back usable: the manual date and time
                // fields unlock, the message explains why, and the club can ask for another time
                // without starting again.
                vm.PartnerAvailabilitySlotId = null;
                ModelState.AddModelError(nameof(vm.PartnerAvailabilitySlotId), isAr ? slotGoneAr : slotGoneEn);
            }
            else
            {
                vm.ProposedDate = slot.Date;
                vm.ProposedStartTime = slot.StartTime;
                vm.ProposedEndTime = slot.EndTime;
                ModelState.Remove(nameof(vm.ProposedDate));
                ModelState.Remove(nameof(vm.ProposedStartTime));
                ModelState.Remove(nameof(vm.ProposedEndTime));
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateCreateViewDataAsync(activity, orgs, partnerOrgId, activity?.Id);
            return View(vm);
        }

        // Identical on both schedule sources: a slot's Dubai wall-clock date and times are combined
        // exactly as a club's typed values are, so a calendar booking and a hand-typed booking are
        // stored the same way and every consumer downstream is unaffected by which one it was.
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
            PartnerAvailabilitySlotId = slot?.Id,
            Status = BookingStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        };
        // A transaction ONLY when a slot is involved. Without one, this is byte-for-byte the write
        // path that existed before the calendar, so a manual booking is unaffected by this feature
        // even in how it is committed.
        await using var tx = slot is null ? null : await _db.Database.BeginTransactionAsync();

        _db.BookingRequests.Add(booking);
        await _db.SaveChangesAsync();

        if (slot is not null)
        {
            // The race, resolved. This is a single UPDATE guarded on the slot still being Available;
            // SQL Server serialises two concurrent attempts on the row, so exactly one affects a row
            // and the other affects none. The booking is inserted first so the claim can record which
            // request holds the slot, and the rollback is what makes that ordering safe: the loser's
            // booking never reaches the database at all.
            //
            // Two clubs clicking the same slot in the same second therefore produce exactly one
            // booking request and exactly one held slot.
            if (!await PartnerAvailabilityWorkflow.TryClaimAsync(_db, slot.Id, booking.Id, userId))
            {
                await tx!.RollbackAsync();
                _db.Entry(booking).State = EntityState.Detached;

                vm.PartnerAvailabilitySlotId = null;
                ModelState.AddModelError(nameof(vm.PartnerAvailabilitySlotId), isAr
                    ? "لم تعد هذه الفترة الزمنية متاحة. يرجى اختيار وقت آخر."
                    : "This time slot is no longer available. Please choose another time.");
                await PopulateCreateViewDataAsync(activity, orgs, partnerOrgId, activity?.Id);
                return View(vm);
            }
        }

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
                booking.PartnerOrganizationId,
                // Schedule Source, recorded at submission so provenance survives every later change
                // of time. Null means the club proposed the time itself.
                ScheduleSource = booking.PartnerAvailabilitySlotId.HasValue ? "PartnerCalendar" : "ClubProposed",
                booking.PartnerAvailabilitySlotId
            }),
            AtUtc = DateTime.UtcNow,
            ByUserId = userId
        });
        await _db.SaveChangesAsync();

        if (tx is not null) await tx.CommitAsync();

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
            .Include(x => x.PartnerAvailabilitySlot)
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
        // Defensive and idempotent, and AFTER the save: the slot must never be freed by a request
        // whose own state change did not survive. The entity's proposal already released whichever
        // slot the club had selected, so this normally moves nothing — the status guard inside makes
        // a second call a no-op — but it means the request cannot end in a dead state while still
        // holding a time.
        await PartnerAvailabilityWorkflow.ReleaseForBookingAsync(_db, booking, userId);

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
    /// One implementing entity's published availability, as a browsable calendar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Anonymous visitors may read it.</b> That matches the policy of the public booking
    /// catalogue this page is reached from — hiding an entity's open times behind a login would
    /// make the catalogue less useful than it is today, and the times themselves are exactly as
    /// public as the programmes beside them. Creating a request still requires a signed-in club
    /// admin, and the sign-in link carries the chosen slot through so the visitor lands back on the
    /// booking form with it selected.
    /// </para>
    /// <para>
    /// Only Available future slots appear. Pending, Booked, Blocked and Cancelled ones are absent
    /// rather than greyed out: a placeholder would tell a reader that somebody else asked, and who
    /// asked is nobody else's business.
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("/booking/partner/{partnerId:int}/calendar")]
    public async Task<IActionResult> PartnerCalendar(int partnerId, int? year = null, int? month = null, int? activityId = null)
    {
        var partner = await _db.Organizations.ApprovedPartners().FirstOrDefaultAsync(x => x.Id == partnerId);
        if (partner is null) return NotFound();

        // A programme filter is only honoured if that programme really belongs to this entity and is
        // really bookable, so a hand-typed id cannot make the page speak for someone else's offering.
        Activity? activity = null;
        if (activityId is > 0)
        {
            activity = await BookableOfferings().FirstOrDefaultAsync(x => x.Id == activityId.Value);
            if (activity is not null && await ResolvePartnerOrganizationIdAsync(activity) != partnerId) activity = null;
        }

        var today = GharsTime.Today;
        var y = year is >= 2000 and <= 2100 ? year.Value : today.Year;
        var m = month is >= 1 and <= 12 ? month.Value : today.Month;
        var monthStart = new DateOnly(y, m, 1);
        var gridStart = monthStart.AddDays(-(int)monthStart.DayOfWeek);
        var gridEnd = gridStart.AddDays(41);

        // Bounded to the rendered grid — six weeks, never the whole table.
        var slots = await PartnerAvailabilityWorkflow.SelectableFor(_db, partnerId, activity?.Id)
            .AsNoTracking()
            .Include(x => x.Activity)
            .Where(x => x.Date >= gridStart && x.Date <= gridEnd)
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime)
            .ToListAsync();

        // Whether this entity has anything at all coming up, so an empty month can say "nothing this
        // month" rather than "this entity publishes no availability" — two different facts.
        ViewBag.HasAnyUpcoming = await PartnerAvailabilityWorkflow.SelectableFor(_db, partnerId, activity?.Id).AnyAsync();
        ViewBag.Partner = partner;
        ViewBag.Activity = activity;
        ViewBag.MonthStart = monthStart;
        ViewBag.GridStart = gridStart;
        ViewBag.Today = today;
        ViewBag.Season = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
        return View(slots);
    }

    /// <summary>
    /// The published availability of one implementing entity, for the Custom Program request form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A club picks an entity from a dropdown and the form asks for that entity's times — one
    /// request for one entity, rather than the page pre-loading every entity's calendar.
    /// </para>
    /// <para>
    /// <b>A projection, not the entity.</b> Six fields leave this endpoint: id, date, start, end,
    /// the optional programme id and the public note in the reader's language. No status, no
    /// holder, no audit stamps, no internal note — and because
    /// <see cref="PartnerAvailabilityWorkflow.SelectableFor"/> returns only Available future slots
    /// of an approved entity, a slot another club is holding is simply absent rather than listed as
    /// unavailable. A club cannot learn from this endpoint that another club asked for something.
    /// </para>
    /// <para>
    /// Only general slots are returned: <c>activityId: null</c>. A custom request has no programme,
    /// so availability an entity pinned to one specific published programme does not apply to it.
    /// </para>
    /// </remarks>
    [Authorize(Roles = RoleNames.ClubAdmin)]
    [HttpGet("/bookings/partner-availability")]
    public async Task<IActionResult> PartnerAvailability(int partnerId)
    {
        if (partnerId <= 0) return Json(Array.Empty<AvailabilitySlotPublicVm>());

        var isAr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

        // Narrowed in SQL to the five columns that leave the endpoint, then formatted in memory:
        // DateOnly.ToString(format) and TimeOnly.ToString(format) have no SQL translation, so doing
        // this inside the query would throw at runtime rather than at compile time.
        var rows = await PartnerAvailabilityWorkflow.SelectableFor(_db, partnerId, activityId: null)
            .AsNoTracking()
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime)
            .Select(x => new { x.Id, x.Date, x.StartTime, x.EndTime, x.ActivityId, x.NotesEn, x.NotesAr })
            .ToListAsync();

        var slots = rows.Select(x => new AvailabilitySlotPublicVm(
            x.Id,
            x.Date.ToString("yyyy-MM-dd"),
            x.StartTime.ToString(@"HH\:mm"),
            x.EndTime.ToString(@"HH\:mm"),
            x.ActivityId,
            isAr ? (x.NotesAr ?? x.NotesEn) : (x.NotesEn ?? x.NotesAr))).ToList();

        return Json(slots);
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
                            || (_db.Organizations.ApprovedPartnerIds().Contains(x.PartnerOrganizationId.Value)
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

    private async Task PopulateCreateViewDataAsync(Activity? activity, List<Organization> orgs, int? partnerOrganizationId = null, int? activityId = null)
    {
        ViewBag.Activity = activity;
        ViewBag.Organizations = orgs;
        ViewBag.PartnerOrganization = activity is null ? null : await ResolvePartnerOrganizationAsync(activity);
        // Approved implementing entities + active seasons for direct entity-first requests.
        ViewBag.Entities = await _db.Organizations.ApprovedPartners().ToListAsync();
        ViewBag.Seasons = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).ToListAsync();

        // The entity's published availability, when the entity is already known — which on the
        // existing-program path it always is. On the custom path the club has not chosen an entity
        // yet, so nothing is loaded here and the form fetches slots for whichever entity it picks.
        //
        // ONE query, for ONE entity. Empty is the normal case and is not an error: the calendar is
        // optional, and a club whose entity keeps none simply sees the manual date and time fields
        // it has always seen.
        ViewBag.AvailabilitySlots = partnerOrganizationId is > 0
            ? await PartnerAvailabilityWorkflow.SelectableFor(_db, partnerOrganizationId.Value, activityId)
                .AsNoTracking()
                .OrderBy(x => x.Date).ThenBy(x => x.StartTime)
                .ToListAsync()
            : new List<PartnerAvailabilitySlot>();
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
