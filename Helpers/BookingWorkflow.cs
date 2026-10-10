using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>A decision someone can take on a booking request.</summary>
public enum BookingDecision
{
    /// <summary>The implementing entity confirms the booking (with the lecturer's details).</summary>
    EntityConfirm,

    /// <summary>The implementing entity rejects the request.</summary>
    EntityReject,

    /// <summary>The implementing entity offers different times (replacing any earlier offer).</summary>
    EntityProposeTimes,

    /// <summary>The club accepts one of the entity's proposed times.</summary>
    ClubAcceptProposedTime,

    /// <summary>The club declines every proposed time.</summary>
    ClubRejectProposedTimes,

    /// <summary>DSC approves the request directly (Super Admin).</summary>
    DscApprove,

    /// <summary>DSC rejects the request directly (Super Admin).</summary>
    DscReject
}

/// <summary>Outcome of trying to take a booking decision.</summary>
public enum BookingClaimResult
{
    /// <summary>The booking moved to the new status; the caller saves the rest of its changes.</summary>
    Claimed,

    /// <summary>The booking's status does not allow this decision. Nothing was changed.</summary>
    NotAllowed,

    /// <summary>
    /// Someone else changed the booking after it was loaded (a second tab, a double click, the other
    /// party deciding at the same moment). Nothing was changed.
    /// </summary>
    Stale
}

/// <summary>
/// The booking workflow's two server-side rules: who may act on a booking for the implementing
/// entity, and which decisions each status allows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Entity access.</b> A booking belongs to the implementing organization recorded on it
/// (<see cref="BookingRequest.PartnerOrganizationId"/>). Only older rows that never recorded it fall
/// back to the program's own organization (<see cref="Activity.PartnerOrganizationId"/>). The user
/// must be linked to that organization now. Who created the program is never a source of access:
/// writing an offering does not make someone a representative of whichever entity it is booked
/// with. The same rule backs the dashboard list, the details page and every decision, so anything
/// the dashboard shows also opens.
/// </para>
/// <para>
/// <b>Transitions.</b> Each decision lists the statuses it may start from. The status change is
/// taken with a conditional UPDATE that only matches the row if its status and
/// <see cref="AuditableEntity.UpdatedAtUtc"/> are still what the caller loaded, so two decisions
/// racing for the same booking cannot both win, and a repeated submission finds the booking
/// already decided. No schema change: <c>UpdatedAtUtc</c> already exists and is stamped on every
/// claim.
/// </para>
/// </remarks>
public static class BookingWorkflow
{
    private static readonly IReadOnlyDictionary<BookingDecision, (BookingStatus[] From, BookingStatus To)> Rules =
        new Dictionary<BookingDecision, (BookingStatus[], BookingStatus)>
        {
            [BookingDecision.EntityConfirm] = (new[] { BookingStatus.Pending, BookingStatus.ClubRejectedProposedTimes }, BookingStatus.Confirmed),
            [BookingDecision.EntityReject] = (new[] { BookingStatus.Pending, BookingStatus.ClubRejectedProposedTimes }, BookingStatus.Rejected),
            // A pending proposal may be replaced before the club answers it.
            [BookingDecision.EntityProposeTimes] = (new[] { BookingStatus.Pending, BookingStatus.PartnerProposedNewTime, BookingStatus.ClubRejectedProposedTimes }, BookingStatus.PartnerProposedNewTime),
            [BookingDecision.ClubAcceptProposedTime] = (new[] { BookingStatus.PartnerProposedNewTime }, BookingStatus.Confirmed),
            [BookingDecision.ClubRejectProposedTimes] = (new[] { BookingStatus.PartnerProposedNewTime }, BookingStatus.ClubRejectedProposedTimes),
            [BookingDecision.DscApprove] = (new[] { BookingStatus.Pending }, BookingStatus.Approved),
            [BookingDecision.DscReject] = (new[] { BookingStatus.Pending }, BookingStatus.Rejected)
        };

    /// <summary>True when <paramref name="decision"/> may be taken on a booking in <paramref name="status"/>.</summary>
    public static bool IsAllowed(BookingDecision decision, BookingStatus status) => Rules[decision].From.Contains(status);

    /// <summary>The status a booking moves to when <paramref name="decision"/> is taken.</summary>
    public static BookingStatus TargetStatus(BookingDecision decision) => Rules[decision].To;

    /// <summary>
    /// Moves the booking to the decision's status, only if it is still in the state the caller loaded.
    /// On success the tracked entity is updated to match, so the caller's following
    /// <c>SaveChangesAsync</c> writes its remaining fields without touching the status again.
    /// Call inside a transaction that also covers that save, so a failure undoes the claim.
    /// </summary>
    public static async Task<BookingClaimResult> TryClaimAsync(AppDbContext db, BookingRequest booking, BookingDecision decision, string? userId)
    {
        if (!IsAllowed(decision, booking.Status)) return BookingClaimResult.NotAllowed;

        var from = booking.Status;
        var loadedStamp = booking.UpdatedAtUtc;
        var to = TargetStatus(decision);
        var stamp = DateTime.UtcNow;

        var rows = await db.BookingRequests
            .Where(x => x.Id == booking.Id && x.Status == from && x.UpdatedAtUtc == loadedStamp)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, to)
                .SetProperty(x => x.UpdatedAtUtc, stamp)
                .SetProperty(x => x.UpdatedByUserId, userId));

        if (rows == 0) return BookingClaimResult.Stale;

        // Bring the tracked entity in line with the row, and mark these values as already saved.
        var entry = db.Entry(booking);
        booking.Status = to;
        booking.UpdatedAtUtc = stamp;
        booking.UpdatedByUserId = userId;
        entry.Property(x => x.Status).OriginalValue = to;
        entry.Property(x => x.UpdatedAtUtc).OriginalValue = stamp;
        entry.Property(x => x.UpdatedByUserId).OriginalValue = userId;
        entry.Property(x => x.Status).IsModified = false;
        entry.Property(x => x.UpdatedAtUtc).IsModified = false;
        entry.Property(x => x.UpdatedByUserId).IsModified = false;

        return BookingClaimResult.Claimed;
    }

    /// <summary>
    /// The implementing entities the user is currently linked to (government authorities and other
    /// partners). Ordered, so a caller that must pick one does so the same way every time.
    /// </summary>
    public static Task<List<int>> EntityOrganizationIdsAsync(AppDbContext db, string userId)
        => db.OrganizationAdminLinks
            .Where(x => x.UserId == userId && x.Organization != null
                        && (x.Organization.OrganizationType == OrganizationType.OtherPartner
                            || x.Organization.OrganizationType == OrganizationType.GovernmentAuthority))
            .Select(x => x.OrganizationId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

    /// <summary>
    /// Bookings whose implementing organization is one of <paramref name="entityOrgIds"/>. An empty
    /// list matches nothing.
    /// </summary>
    public static IQueryable<BookingRequest> ForEntities(IQueryable<BookingRequest> query, IReadOnlyCollection<int> entityOrgIds)
        => query.Where(x =>
            (x.PartnerOrganizationId != null && entityOrgIds.Contains(x.PartnerOrganizationId.Value))
            || (x.PartnerOrganizationId == null && x.Activity != null && x.Activity.PartnerOrganizationId != null
                && entityOrgIds.Contains(x.Activity.PartnerOrganizationId.Value)));

    /// <summary>The implementing organization a booking belongs to, by the same rule as <see cref="ForEntities"/>.</summary>
    public static int? ImplementingOrganizationId(BookingRequest booking)
        => booking.PartnerOrganizationId ?? booking.Activity?.PartnerOrganizationId;

    /// <summary>True when the booking belongs to one of the user's implementing entities.</summary>
    public static bool BelongsToEntities(BookingRequest booking, IReadOnlyCollection<int> entityOrgIds)
        => ImplementingOrganizationId(booking) is int id && entityOrgIds.Contains(id);
}
