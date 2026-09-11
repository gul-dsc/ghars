using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GharsPlatform.Controllers.Public;

/// <summary>
/// "My Calendar" — an implementing entity's own, entirely optional record of future times it is
/// willing to receive requests for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optional means optional.</b> An entity that never opens this screen keeps working exactly as
/// it did before the feature existed: it can publish programmes, receive both kinds of request and
/// confirm bookings without a single slot in this table. Nothing here is a prerequisite for
/// anything.
/// </para>
/// <para>
/// <b>Ownership is server-derived.</b> Every query is scoped by
/// <see cref="PartnerOrganizationIdsAsync"/>, the owning entity is written from that and never
/// model-bound, and a miss returns <see cref="NotFoundResult"/> rather than a distinguishable
/// "forbidden" — the same non-disclosure convention <see cref="PartnerProgramsController"/> uses.
/// </para>
/// <para>
/// <b>No status is assigned here.</b> Every transition goes through
/// <see cref="PartnerAvailabilityWorkflow"/>, which performs it as a conditional update so a
/// concurrent club claim cannot be overwritten.
/// </para>
/// </remarks>
[Authorize(Roles = RoleNames.PartnerAdmin)]
public class PartnerCalendarController : Controllers.BaseController
{
    public PartnerCalendarController(AppDbContext db) : base(db) { }

    private bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    private string T(string en, string ar) => IsAr ? ar : en;

    // ─────────────────────────────────────────────────────────────────────── month

    [HttpGet("/partner/calendar")]
    public async Task<IActionResult> Index(int? year = null, int? month = null, string? view = null)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();
        var orgId = orgIds[0];

        // The displayed month, in Dubai time. An out-of-range value from the query string falls back
        // to the current month rather than throwing.
        var today = GharsTime.Today;
        var y = year is >= 2000 and <= 2100 ? year.Value : today.Year;
        var m = month is >= 1 and <= 12 ? month.Value : today.Month;
        var monthStart = new DateOnly(y, m, 1);

        // One query for the displayed month only — never the whole table. The grid also shows the
        // leading and trailing days of the adjacent months, so the range is widened to cover them.
        var gridStart = monthStart.AddDays(-(int)monthStart.DayOfWeek);
        var gridEnd = gridStart.AddDays(41);

        var slots = await Db.PartnerAvailabilitySlots
            .AsNoTracking()
            .Include(x => x.Activity)
            .Where(x => x.PartnerOrganizationId == orgId
                        && x.Date >= gridStart && x.Date <= gridEnd
                        && x.Status != PartnerAvailabilityStatus.Cancelled)
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime)
            .ToListAsync();

        // Who is holding the pending slots, as ONE grouped query for the whole month rather than one
        // per slot. The entity sees the requesting club's name on its own calendar; no other club and
        // no anonymous visitor ever does.
        var heldIds = slots.Where(x => x.HeldByBookingRequestId.HasValue)
            .Select(x => x.HeldByBookingRequestId!.Value).Distinct().ToList();
        ViewBag.Holders = heldIds.Count == 0
            ? new Dictionary<int, (string Name, int BookingId)>()
            : (await Db.BookingRequests
                .Where(x => heldIds.Contains(x.Id))
                .Select(x => new { x.Id, NameEn = x.Organization!.NameEn, NameAr = x.Organization!.NameAr })
                .ToListAsync())
              .ToDictionary(x => x.Id, x => (Name: IsAr ? (x.NameAr ?? x.NameEn) : (x.NameEn ?? x.NameAr), BookingId: x.Id));

        await PopulateShellAsync(orgId);

        // Three operational counts, over the active season rather than the displayed month, so the
        // header keeps saying what is outstanding while the entity pages through months. These feed
        // nothing: availability is not a Ghars statistic and appears in no KPI, report or total.
        var season = await ActiveSeasonAsync();
        if (season is not null)
        {
            var counts = await Db.PartnerAvailabilitySlots
                .Where(x => x.PartnerOrganizationId == orgId && x.SeasonId == season.Id)
                .GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var upcoming = await Db.PartnerAvailabilitySlots
                .CountAsync(x => x.PartnerOrganizationId == orgId
                                 && x.SeasonId == season.Id
                                 && x.Status == PartnerAvailabilityStatus.Available
                                 && x.Date >= today);

            ViewBag.CountUpcoming = upcoming;
            ViewBag.CountPending = counts.FirstOrDefault(x => x.Status == PartnerAvailabilityStatus.Pending)?.Count ?? 0;
            ViewBag.CountBooked = counts.FirstOrDefault(x => x.Status == PartnerAvailabilityStatus.Booked)?.Count ?? 0;
        }

        ViewBag.MonthStart = monthStart;
        ViewBag.GridStart = gridStart;
        ViewBag.Today = today;
        // The month grid is unusable at phone widths, so the list is the default there. The choice is
        // still the reader's: both views are always reachable, and neither is a fallback.
        ViewBag.View = view == "month" ? "month" : (view == "list" ? "list" : "auto");
        ViewBag.HasAnySlots = slots.Count > 0
                              || await Db.PartnerAvailabilitySlots.AnyAsync(x => x.PartnerOrganizationId == orgId
                                                                                 && x.Status != PartnerAvailabilityStatus.Cancelled);
        return View(slots);
    }

    // ─────────────────────────────────────────────────────────────────────── create

    /// <summary>
    /// Add one date with one or more time slots, in a single submit.
    /// </summary>
    /// <remarks>
    /// Deliberately one operation rather than three trips through a form: an entity publishing
    /// 09:00–10:00, 11:00–12:00 and 14:00–15:30 on a Saturday is doing one thing, and making it
    /// three would be the reason nobody keeps the calendar up to date.
    ///
    /// Partial success is allowed and reported: if one of three rows collides with something already
    /// published, the other two are still created and the response says which was skipped and why.
    /// Refusing all three because of one collision would lose work the entity had already typed.
    /// </remarks>
    [HttpPost("/partner/calendar/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PartnerAvailabilityCreateVm vm)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();
        var orgId = orgIds[0];

        var season = await ActiveSeasonAsync();
        if (season is null)
        {
            TempData["ToastWarning"] = T("There is no active sports season, so availability cannot be published yet.",
                                         "لا يوجد موسم رياضي نشط، لذا لا يمكن نشر الإتاحة حالياً.");
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            TempData["ToastWarning"] = FirstError() ?? T("Check the availability details and try again.", "يرجى مراجعة بيانات الإتاحة والمحاولة مرة أخرى.");
            return RedirectToAction(nameof(Index));
        }

        var date = vm.Date!.Value;

        // A slot may only be offered inside the season it belongs to. Without this an entity could
        // publish availability for a date the season does not cover, which no club could ever book.
        if (date < season.StartDate || date > season.EndDate)
        {
            TempData["ToastWarning"] = T("That date falls outside the active sports season.", "هذا التاريخ خارج نطاق الموسم الرياضي النشط.");
            return RedirectToAction(nameof(Index), new { year = date.Year, month = date.Month });
        }

        // The optional programme link, re-validated against the database. An entity cannot offer
        // times for somebody else's programme, or for one DSC has not approved and published.
        if (vm.ActivityId.HasValue && !await IsLinkableActivityAsync(vm.ActivityId.Value, orgId))
        {
            TempData["ToastWarning"] = T("That program cannot be linked to availability.", "لا يمكن ربط هذا البرنامج بالإتاحة.");
            return RedirectToAction(nameof(Index), new { year = date.Year, month = date.Month });
        }

        var created = 0;
        var skipped = new List<string>();

        foreach (var row in vm.Times.Where(x => !x.IsEmpty).OrderBy(x => x.StartTime))
        {
            var start = row.StartTime!.Value;
            var end = row.EndTime!.Value;
            var label = $"{start:HH\\:mm}–{end:HH\\:mm}";

            if (PartnerAvailabilityWorkflow.ValidateShape(date, start, end) is { } problem)
            {
                skipped.Add($"{label}: {(IsAr ? problem.Ar : problem.En)}");
                continue;
            }

            if (await PartnerAvailabilityWorkflow.OverlapsAsync(Db, orgId, date, start, end))
            {
                skipped.Add($"{label}: {T("overlaps availability you already published.", "يتداخل مع إتاحة منشورة مسبقاً.")}");
                continue;
            }

            Db.PartnerAvailabilitySlots.Add(new PartnerAvailabilitySlot
            {
                PartnerOrganizationId = orgId,
                SeasonId = season.Id,
                ActivityId = vm.ActivityId,
                Date = date,
                StartTime = start,
                EndTime = end,
                Status = PartnerAvailabilityStatus.Available,
                NotesEn = vm.NotesEn?.Trim(),
                NotesAr = vm.NotesAr?.Trim(),
                LocationEn = vm.LocationEn?.Trim(),
                LocationAr = vm.LocationAr?.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = CurrentUserId
            });
            created++;
        }

        if (created > 0)
        {
            await Db.SaveChangesAsync();
            await AuditAsync("AvailabilityPublished", nameof(PartnerAvailabilitySlot), null,
                newValues: new { PartnerOrganizationId = orgId, Date = date.ToString("yyyy-MM-dd"), Slots = created, vm.ActivityId });
            TempData["ToastSuccess"] = T($"{created} time slot(s) published for {date:yyyy-MM-dd}.",
                                         $"تم نشر {created} فترة زمنية بتاريخ {date:yyyy-MM-dd}.");
        }

        if (skipped.Count > 0)
        {
            TempData["ToastWarning"] = T("Not added: ", "لم تُضف: ") + string.Join(" · ", skipped);
        }

        return RedirectToAction(nameof(Index), new { year = date.Year, month = date.Month });
    }

    // ───────────────────────────────────────────────────────────────────────── edit

    /// <summary>
    /// Change a free future slot.
    /// </summary>
    /// <remarks>
    /// Only an <see cref="PartnerAvailabilityStatus.Available"/> or
    /// <see cref="PartnerAvailabilityStatus.Blocked"/> future slot can be edited. A pending slot is
    /// referenced by a club's live request, so moving it silently would change what that club asked
    /// for — the entity proposes a different time through the existing booking workflow instead,
    /// where the club gets to accept or refuse. A booked slot is history.
    /// </remarks>
    [HttpPost("/partner/calendar/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PartnerAvailabilityEditVm vm)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();
        var orgId = orgIds[0];

        // Read untracked: the write below is a conditional UPDATE, not a tracked save, so a tracked
        // copy would only be a stale object waiting to be used by mistake.
        var slot = await OwnedSlots(orgId).AsNoTracking().FirstOrDefaultAsync(x => x.Id == vm.Id);
        if (slot is null) return NotFound();

        if (slot.Status is PartnerAvailabilityStatus.Pending or PartnerAvailabilityStatus.Booked)
        {
            TempData["ToastWarning"] = T("A time slot a club has requested cannot be edited here. Respond through the booking request instead.",
                                         "لا يمكن تعديل فترة زمنية طلبها أحد الأندية من هنا. يرجى الرد عبر طلب الحجز.");
            return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
        }

        if (slot.Status == PartnerAvailabilityStatus.Cancelled) return NotFound();

        if (!vm.Date.HasValue || !vm.StartTime.HasValue || !vm.EndTime.HasValue)
        {
            TempData["ToastWarning"] = T("Date, start time and end time are all required.", "التاريخ ووقت البدء ووقت الانتهاء كلها مطلوبة.");
            return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
        }

        var date = vm.Date.Value;
        var start = vm.StartTime.Value;
        var end = vm.EndTime.Value;

        if (PartnerAvailabilityWorkflow.ValidateShape(date, start, end) is { } problem)
        {
            TempData["ToastWarning"] = IsAr ? problem.Ar : problem.En;
            return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
        }

        var season = await Db.Seasons.FirstOrDefaultAsync(x => x.Id == slot.SeasonId);
        if (season is null || date < season.StartDate || date > season.EndDate)
        {
            TempData["ToastWarning"] = T("That date falls outside the sports season this slot belongs to.", "هذا التاريخ خارج نطاق الموسم الرياضي التابع له هذا الوقت.");
            return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
        }

        if (await PartnerAvailabilityWorkflow.OverlapsAsync(Db, orgId, date, start, end, excludeSlotId: slot.Id))
        {
            TempData["ToastWarning"] = T("That time overlaps availability you already published.", "هذا الوقت يتداخل مع إتاحة منشورة مسبقاً.");
            return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
        }

        if (vm.ActivityId.HasValue && !await IsLinkableActivityAsync(vm.ActivityId.Value, orgId))
        {
            TempData["ToastWarning"] = T("That program cannot be linked to availability.", "لا يمكن ربط هذا البرنامج بالإتاحة.");
            return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
        }

        // A club can claim this slot between the page rendering and this post landing. The write is
        // therefore one conditional UPDATE guarded on the status the slot was read in — the same
        // compare-and-swap the workflow helper uses. If the claim won the race this affects zero
        // rows and the entity is told why, rather than the claim being silently overwritten.
        var expected = slot.Status;
        var notesEn = vm.NotesEn?.Trim();
        var notesAr = vm.NotesAr?.Trim();
        var locationEn = vm.LocationEn?.Trim();
        var locationAr = vm.LocationAr?.Trim();
        var stamp = DateTime.UtcNow;
        var userId = CurrentUserId;

        var affected = await Db.PartnerAvailabilitySlots
            .Where(x => x.Id == slot.Id && x.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Date, date)
                .SetProperty(x => x.StartTime, start)
                .SetProperty(x => x.EndTime, end)
                .SetProperty(x => x.ActivityId, vm.ActivityId)
                .SetProperty(x => x.NotesEn, notesEn)
                .SetProperty(x => x.NotesAr, notesAr)
                .SetProperty(x => x.LocationEn, locationEn)
                .SetProperty(x => x.LocationAr, locationAr)
                .SetProperty(x => x.UpdatedAtUtc, stamp)
                .SetProperty(x => x.UpdatedByUserId, userId));

        if (affected == 0)
        {
            TempData["ToastWarning"] = T("That time slot has just been requested by a club and can no longer be edited.",
                                         "تم طلب هذه الفترة الزمنية للتو من أحد الأندية ولم يعد بالإمكان تعديلها.");
            return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
        }

        await AuditAsync("AvailabilityUpdated", nameof(PartnerAvailabilitySlot), slot.Id.ToString(),
            oldValues: new { Date = slot.Date.ToString("yyyy-MM-dd"), StartTime = slot.StartTime.ToString("HH\\:mm"), EndTime = slot.EndTime.ToString("HH\\:mm"), slot.ActivityId },
            newValues: new { Date = date.ToString("yyyy-MM-dd"), StartTime = start.ToString("HH\\:mm"), EndTime = end.ToString("HH\\:mm"), vm.ActivityId });

        TempData["ToastSuccess"] = T("Availability updated.", "تم تحديث الإتاحة.");
        return RedirectToAction(nameof(Index), new { year = date.Year, month = date.Month });
    }

    // ───────────────────────────────────────────────────────────── block / unblock

    [HttpPost("/partner/calendar/block")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Block(int id) => ToggleAsync(id, block: true);

    [HttpPost("/partner/calendar/unblock")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Unblock(int id) => ToggleAsync(id, block: false);

    private async Task<IActionResult> ToggleAsync(int id, bool block)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var slot = await OwnedSlots(orgIds[0]).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (slot is null) return NotFound();

        var moved = block
            ? await PartnerAvailabilityWorkflow.TryBlockAsync(Db, slot.Id, CurrentUserId)
            : await PartnerAvailabilityWorkflow.TryUnblockAsync(Db, slot.Id, CurrentUserId);

        if (moved)
        {
            await AuditAsync(block ? "AvailabilityBlocked" : "AvailabilityUnblocked", nameof(PartnerAvailabilitySlot), slot.Id.ToString());
            TempData["ToastSuccess"] = block
                ? T("Time slot marked unavailable.", "تم تعليم الفترة الزمنية كغير متاحة.")
                : T("Time slot is available again.", "أصبحت الفترة الزمنية متاحة مرة أخرى.");
        }
        else
        {
            TempData["ToastWarning"] = T("That time slot has changed since this page was loaded.", "تغيّرت حالة هذه الفترة الزمنية منذ تحميل الصفحة.");
        }

        return RedirectToAction(nameof(Index), new { year = slot.Date.Year, month = slot.Date.Month });
    }

    // ─────────────────────────────────────────────────────────────────────── remove

    /// <summary>
    /// Withdraw a slot.
    /// </summary>
    /// <remarks>
    /// Three outcomes, and which one applies is decided by the data rather than by the caller:
    ///
    ///   Pending / Booked      refused. A club's request references it; that is resolved through the
    ///                         booking workflow, not by deleting the evidence underneath it.
    ///   Ever referenced       cancelled, not deleted. A booking still points at it for provenance,
    ///                         so the row stays and the status records the withdrawal.
    ///   Never referenced      deleted. A slot published by mistake and never seen by anyone leaves
    ///                         no history worth keeping — and the audit log records the deletion.
    /// </remarks>
    [HttpPost("/partner/calendar/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        var orgIds = await PartnerOrganizationIdsAsync();
        if (orgIds.Count == 0) return Forbid();

        var slot = await OwnedSlots(orgIds[0]).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (slot is null) return NotFound();

        var month = new { year = slot.Date.Year, month = slot.Date.Month };

        if (slot.Status is PartnerAvailabilityStatus.Pending or PartnerAvailabilityStatus.Booked)
        {
            TempData["ToastWarning"] = T("A time slot a club has requested or you have confirmed cannot be removed. Handle it through the booking request.",
                                         "لا يمكن حذف فترة زمنية طلبها نادٍ أو سبق تأكيدها. يرجى معالجتها عبر طلب الحجز.");
            return RedirectToAction(nameof(Index), month);
        }

        var referenced = await Db.BookingRequests.AnyAsync(x => x.PartnerAvailabilitySlotId == slot.Id);
        var stamp = DateTime.UtcNow;
        var userId = CurrentUserId;
        var snapshot = new
        {
            Date = slot.Date.ToString("yyyy-MM-dd"),
            StartTime = slot.StartTime.ToString("HH\\:mm"),
            EndTime = slot.EndTime.ToString("HH\\:mm"),
            slot.ActivityId,
            slot.SeasonId
        };

        // Both branches are guarded on the slot still being free. The guard is what makes them
        // race-free against a club claiming the slot at the same moment: the claim's UPDATE and this
        // statement contend for the same row lock, so whichever runs second matches nothing — either
        // the withdrawal changes nothing and says so, or the claim finds the slot gone and rolls its
        // booking back. Neither can leave a booking pointing at a slot that was removed underneath it.
        if (referenced)
        {
            var cancelled = await Db.PartnerAvailabilitySlots
                .Where(x => x.Id == slot.Id
                            && (x.Status == PartnerAvailabilityStatus.Available || x.Status == PartnerAvailabilityStatus.Blocked))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, PartnerAvailabilityStatus.Cancelled)
                    .SetProperty(x => x.HeldByBookingRequestId, (int?)null)
                    .SetProperty(x => x.UpdatedAtUtc, stamp)
                    .SetProperty(x => x.UpdatedByUserId, userId));

            if (cancelled == 0)
            {
                TempData["ToastWarning"] = T("That time slot has changed since this page was loaded.", "تغيّرت حالة هذه الفترة الزمنية منذ تحميل الصفحة.");
                return RedirectToAction(nameof(Index), month);
            }

            await AuditAsync("AvailabilityCancelled", nameof(PartnerAvailabilitySlot), slot.Id.ToString(),
                oldValues: snapshot, newValues: new { Retained = "referenced by a booking request" });
            TempData["ToastSuccess"] = T("Time slot cancelled. It is kept in your history because a booking request refers to it.",
                                         "تم إلغاء الفترة الزمنية. وقد تم الاحتفاظ بها في السجل لارتباطها بطلب حجز.");
            return RedirectToAction(nameof(Index), month);
        }

        var deleted = await Db.PartnerAvailabilitySlots
            .Where(x => x.Id == slot.Id
                        && (x.Status == PartnerAvailabilityStatus.Available || x.Status == PartnerAvailabilityStatus.Blocked))
            .ExecuteDeleteAsync();

        if (deleted == 0)
        {
            TempData["ToastWarning"] = T("That time slot has changed since this page was loaded.", "تغيّرت حالة هذه الفترة الزمنية منذ تحميل الصفحة.");
            return RedirectToAction(nameof(Index), month);
        }

        await AuditAsync("AvailabilityDeleted", nameof(PartnerAvailabilitySlot), id.ToString(), oldValues: snapshot);
        TempData["ToastSuccess"] = T("Time slot removed.", "تم حذف الفترة الزمنية.");
        return RedirectToAction(nameof(Index), month);
    }

    // ─────────────────────────────────────────────────────────────────────── helpers

    private async Task<List<int>> PartnerOrganizationIdsAsync()
    {
        var userId = CurrentUserId ?? "";
        return await Db.OrganizationAdminLinks.Include(x => x.Organization)
            .Where(x => x.UserId == userId && x.Organization != null &&
                        (x.Organization.OrganizationType == OrganizationType.OtherPartner
                         || x.Organization.OrganizationType == OrganizationType.GovernmentAuthority))
            .Select(x => x.OrganizationId)
            .ToListAsync();
    }

    private IQueryable<PartnerAvailabilitySlot> OwnedSlots(int orgId)
        => Db.PartnerAvailabilitySlots.Where(x => x.PartnerOrganizationId == orgId);

    private Task<Season?> ActiveSeasonAsync()
        => Db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();

    /// <summary>
    /// Whether this entity may attach availability to that offering: it owns it, DSC approved it, it
    /// is published, and its season is active. Re-read from the database on every write, so a
    /// hand-posted id for another entity's programme fails exactly as a hidden option would.
    /// </summary>
    private Task<bool> IsLinkableActivityAsync(int activityId, int orgId)
        => Db.Activities.AnyAsync(x => x.Id == activityId
                                       && x.PartnerOrganizationId == orgId
                                       && x.Status == ActivityStatus.Published
                                       && x.ApprovalStatus == OfferingApprovalStatus.Approved
                                       && x.Season != null && x.Season.IsActive);

    private async Task PopulateShellAsync(int orgId)
    {
        ViewBag.Organization = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == orgId);
        ViewBag.Season = await ActiveSeasonAsync();
        // The offerings a slot may be pinned to. Same conditions IsLinkableActivityAsync enforces, so
        // the dropdown never offers something the post would refuse.
        ViewBag.LinkableActivities = await Db.Activities
            .Where(x => x.PartnerOrganizationId == orgId
                        && x.Status == ActivityStatus.Published
                        && x.ApprovalStatus == OfferingApprovalStatus.Approved
                        && x.Season != null && x.Season.IsActive)
            .OrderBy(x => x.TitleEn)
            .ToListAsync();
    }

    private string? FirstError()
        => ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
}
