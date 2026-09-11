using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Every <see cref="PartnerAvailabilitySlot"/> state transition, and every query that decides which
/// slots a club may see. Controllers call into here; none of them assigns
/// <see cref="PartnerAvailabilitySlot.Status"/> itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency.</b> Two clubs can click the same slot in the same second. Every transition here
/// is a single conditional <c>UPDATE … WHERE Id = @id AND Status = @expected</c>, issued through
/// <see cref="RelationalQueryableExtensions"/>' <c>ExecuteUpdateAsync</c>. SQL Server holds an
/// exclusive lock on the row for the statement, so of two concurrent claims exactly one observes the
/// expected status; the other affects <b>0 rows</b> and is told so. There is no read-then-write
/// window anywhere in this file.
/// </para>
/// <para>
/// A <c>rowversion</c> token was considered and rejected: <c>ExecuteUpdateAsync</c> ignores
/// concurrency tokens, so the two mechanisms would not compose, and <c>[Timestamp]</c> would raise
/// <c>DbUpdateConcurrencyException</c> on unrelated write paths. A compare-and-swap on the status is
/// strictly stronger, because the status is the value being contended.
/// </para>
/// <para>
/// <b>Time.</b> Everything future-facing goes through <see cref="GharsTime"/>. Slot dates and times
/// are Dubai wall-clock; the server clock is UTC; comparing one to the other directly would be wrong
/// by four hours, which for an 09:00 slot means it disappears at 05:00.
/// </para>
/// </remarks>
public static class PartnerAvailabilityWorkflow
{
    /// <summary>
    /// How far ahead a club is shown availability when the season runs longer than that. Bounded so
    /// the booking screens never load an unlimited date range; the season end is applied as well and
    /// whichever comes first wins, so a season closing in three weeks does not advertise slots past
    /// its own end.
    /// </summary>
    public const int ClubLookaheadDays = 90;

    // ─────────────────────────────────────────────────────────────────── queries

    /// <summary>
    /// Every slot a club or an anonymous visitor is allowed to see: published as
    /// <see cref="PartnerAvailabilityStatus.Available"/>, still in the future in Dubai time, in an
    /// active season, owned by an approved implementing entity, and — where the slot names one — tied
    /// to an offering that is still bookable.
    /// </summary>
    /// <remarks>
    /// Pending, Booked, Blocked and Cancelled slots are <em>absent</em> from this query rather than
    /// returned with a flag. A club must not be able to tell the difference between "the entity never
    /// offered this time" and "another club asked for it an hour ago".
    /// </remarks>
    public static IQueryable<PartnerAvailabilitySlot> ClubVisible(AppDbContext db)
    {
        var today = GharsTime.Today;
        var timeNow = GharsTime.TimeNow;
        var horizon = today.AddDays(ClubLookaheadDays);

        return db.PartnerAvailabilitySlots
            .Where(x => x.Status == PartnerAvailabilityStatus.Available
                        // Future only, to the minute: today's 09:00 slot stops being offered at 09:00,
                        // not at midnight.
                        && (x.Date > today || (x.Date == today && x.StartTime > timeNow))
                        && x.Date <= horizon
                        && x.Season != null && x.Season.IsActive
                        && x.Date <= x.Season.EndDate
                        && db.Organizations.ApprovedPartnerIds().Contains(x.PartnerOrganizationId)
                        // A slot offered for one specific programme is only meaningful while that
                        // programme is still bookable. Withdrawing an offering therefore withdraws the
                        // times attached to it, without the partner having to remember to.
                        && (x.ActivityId == null
                            || (x.Activity != null
                                && x.Activity.Status == ActivityStatus.Published
                                && x.Activity.ApprovalStatus == OfferingApprovalStatus.Approved)));
    }

    /// <summary>
    /// The slots a club may choose from for one particular request.
    /// </summary>
    /// <param name="activityId">
    /// The offering being booked, or <c>null</c> for a Custom Program request. A custom request has
    /// no programme, so only general slots apply to it; a programme booking may use a general slot or
    /// one offered specifically for that programme.
    /// </param>
    public static IQueryable<PartnerAvailabilitySlot> SelectableFor(AppDbContext db, int partnerOrganizationId, int? activityId)
    {
        var q = ClubVisible(db).Where(x => x.PartnerOrganizationId == partnerOrganizationId);

        return activityId.HasValue
            ? q.Where(x => x.ActivityId == null || x.ActivityId == activityId.Value)
            : q.Where(x => x.ActivityId == null);
    }

    /// <summary>
    /// Which of the given entities have any availability a club could select, as one grouped query.
    /// </summary>
    /// <remarks>
    /// The booking catalogue renders dozens of cards. This is what stops it asking the database once
    /// per card — the caller gets a set and each card does a lookup.
    /// </remarks>
    public static async Task<HashSet<int>> PartnersWithAvailabilityAsync(AppDbContext db, IEnumerable<int> partnerIds, CancellationToken ct = default)
    {
        var ids = partnerIds as IList<int> ?? partnerIds.ToList();
        if (ids.Count == 0) return [];

        var found = await ClubVisible(db)
            .Where(x => ids.Contains(x.PartnerOrganizationId))
            .Select(x => x.PartnerOrganizationId)
            .Distinct()
            .ToListAsync(ct);

        return found.ToHashSet();
    }

    // ─────────────────────────────────────────────────────────── state transitions

    /// <summary>
    /// Claim an available slot for a booking request: <c>Available → Pending</c>.
    /// </summary>
    /// <returns>
    /// <c>true</c> when this caller won the slot. <c>false</c> when it was already taken, blocked,
    /// cancelled or booked — in which case nothing was changed and the caller must abandon the
    /// booking rather than continue without the slot.
    /// </returns>
    public static Task<bool> TryClaimAsync(AppDbContext db, int slotId, int bookingRequestId, string? userId, CancellationToken ct = default)
        => TransitionAsync(db, slotId, PartnerAvailabilityStatus.Available, PartnerAvailabilityStatus.Pending, bookingRequestId, userId, ct);

    /// <summary>
    /// Confirm a held slot: <c>Pending → Booked</c>. Terminal.
    /// </summary>
    public static Task<bool> TryMarkBookedAsync(AppDbContext db, int slotId, int bookingRequestId, string? userId, CancellationToken ct = default)
        => TransitionAsync(db, slotId, PartnerAvailabilityStatus.Pending, PartnerAvailabilityStatus.Booked, bookingRequestId, userId, ct);

    /// <summary>
    /// Hand a held slot back: <c>Pending → Available</c>, and the holder reference is cleared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Guarded on <c>Pending</c>, which is what enforces two separate rules at once: a
    /// <c>Booked</c> slot is never released by an unrelated state change, and a slot the entity has
    /// since blocked or cancelled is not silently re-opened.
    /// </para>
    /// <para>
    /// A slot whose time has already passed is released too. It becomes Available but stays invisible
    /// everywhere, because every club-facing query filters to the future — which is the truth of it:
    /// nothing holds it, and nothing can ever book it. The alternative was an <c>Expired</c> state
    /// that only a scheduled job could ever write, and this application has no scheduler.
    /// </para>
    /// </remarks>
    public static Task<bool> TryReleaseAsync(AppDbContext db, int slotId, string? userId, CancellationToken ct = default)
        => TransitionAsync(db, slotId, PartnerAvailabilityStatus.Pending, PartnerAvailabilityStatus.Available, null, userId, ct);

    /// <summary>Withdraw a free slot temporarily: <c>Available → Blocked</c>.</summary>
    public static Task<bool> TryBlockAsync(AppDbContext db, int slotId, string? userId, CancellationToken ct = default)
        => TransitionAsync(db, slotId, PartnerAvailabilityStatus.Available, PartnerAvailabilityStatus.Blocked, null, userId, ct);

    /// <summary>Re-open a blocked slot: <c>Blocked → Available</c>.</summary>
    public static Task<bool> TryUnblockAsync(AppDbContext db, int slotId, string? userId, CancellationToken ct = default)
        => TransitionAsync(db, slotId, PartnerAvailabilityStatus.Blocked, PartnerAvailabilityStatus.Available, null, userId, ct);

    /// <summary>
    /// Release whichever slot a booking is holding, if it is holding one.
    /// </summary>
    /// <remarks>
    /// The convenience form used by the four booking outcomes that free a slot: the entity rejected
    /// the request, the entity proposed a different time instead, DSC rejected it, or the club
    /// withdrew it. Safe to call on a booking that never had a slot, and safe to call twice — the
    /// status guard makes it a no-op the second time.
    /// </remarks>
    public static async Task ReleaseForBookingAsync(AppDbContext db, BookingRequest booking, string? userId, CancellationToken ct = default)
    {
        if (booking.PartnerAvailabilitySlotId is not int slotId) return;
        await TryReleaseAsync(db, slotId, userId, ct);
    }

    /// <summary>Mark whichever slot a booking is holding as booked, if it is holding one.</summary>
    public static async Task MarkBookedForBookingAsync(AppDbContext db, BookingRequest booking, string? userId, CancellationToken ct = default)
    {
        if (booking.PartnerAvailabilitySlotId is not int slotId) return;
        await TryMarkBookedAsync(db, slotId, booking.Id, userId, ct);
    }

    /// <summary>
    /// The one statement every transition is built from: change status only if it is still what the
    /// caller expected. Returns whether a row actually moved.
    /// </summary>
    private static async Task<bool> TransitionAsync(
        AppDbContext db,
        int slotId,
        PartnerAvailabilityStatus from,
        PartnerAvailabilityStatus to,
        int? heldBy,
        string? userId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var affected = await db.PartnerAvailabilitySlots
            .Where(x => x.Id == slotId && x.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, to)
                .SetProperty(x => x.HeldByBookingRequestId, heldBy)
                .SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.UpdatedByUserId, userId), ct);

        return affected == 1;
    }

    // ───────────────────────────────────────────────────────────────── validation

    /// <summary>
    /// The shape rules for a single slot, independent of what else the entity has published.
    /// </summary>
    /// <returns><c>null</c> when the slot is valid, otherwise the bilingual reason it is not.</returns>
    public static (string En, string Ar)? ValidateShape(DateOnly date, TimeOnly start, TimeOnly end, bool mustBeFuture = true)
    {
        // One calendar day per slot. Overnight availability is not supported in this first version,
        // and accepting end <= start as "it wraps past midnight" would make every duration
        // calculation and every overlap check ambiguous.
        if (end <= start)
            return ("End time must be after the start time.", "يجب أن يكون وقت الانتهاء بعد وقت البدء.");

        if ((end - start) < TimeSpan.FromMinutes(15))
            return ("A time slot must be at least 15 minutes long.", "يجب ألا تقل مدة الفترة الزمنية عن 15 دقيقة.");

        if (mustBeFuture && !GharsTime.IsFuture(date, start))
            return ("Availability can only be added for a future date and time.", "لا يمكن إضافة الإتاحة إلا لتاريخ ووقت مستقبليين.");

        return null;
    }

    /// <summary>
    /// Whether a proposed slot overlaps one the entity already has on that date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adjacent slots are fine — <c>09:00–10:00</c> and <c>10:00–11:00</c> do not overlap, because
    /// the comparison is strict at both ends. Genuine overlap (<c>09:00–11:00</c> against
    /// <c>10:30–12:00</c>) is refused.
    /// </para>
    /// <para>
    /// Cancelled slots are ignored: withdrawing a time and later re-publishing it must work. So is
    /// <paramref name="excludeSlotId"/>, so that editing a slot does not find that it overlaps
    /// itself.
    /// </para>
    /// <para>
    /// SQL Server has no range-exclusion constraint, so this rule cannot be an index. The database
    /// backstop is narrower and covers what an index <em>can</em> express — the filtered unique index
    /// on (partner, date, start, end) that refuses exact duplicates.
    /// </para>
    /// </remarks>
    public static Task<bool> OverlapsAsync(
        AppDbContext db,
        int partnerOrganizationId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        int? excludeSlotId = null,
        CancellationToken ct = default)
        => db.PartnerAvailabilitySlots.AnyAsync(x =>
                x.PartnerOrganizationId == partnerOrganizationId
                && x.Date == date
                && x.Status != PartnerAvailabilityStatus.Cancelled
                && (excludeSlotId == null || x.Id != excludeSlotId)
                && start < x.EndTime
                && x.StartTime < end,
            ct);

    // ────────────────────────────────────────────────────────────────── labels

    /// <summary>
    /// The bilingual label, icon and badge class for a status. Status is never conveyed by colour
    /// alone anywhere in this feature: the icon and the text always travel with it.
    /// </summary>
    public static (string En, string Ar, string Icon, string BadgeClass) Describe(PartnerAvailabilityStatus status) => status switch
    {
        PartnerAvailabilityStatus.Available => ("Available", "متاح", "bi-check-circle", "text-bg-success"),
        PartnerAvailabilityStatus.Pending => ("Pending request", "طلب قيد المراجعة", "bi-hourglass-split", "text-bg-warning"),
        PartnerAvailabilityStatus.Booked => ("Booked", "محجوز", "bi-calendar-check", "text-bg-primary"),
        PartnerAvailabilityStatus.Blocked => ("Unavailable", "غير متاح", "bi-slash-circle", "text-bg-secondary"),
        PartnerAvailabilityStatus.Cancelled => ("Cancelled", "ملغى", "bi-x-circle", "text-bg-light border"),
        _ => (status.ToString(), status.ToString(), "bi-question-circle", "text-bg-light border")
    };
}
