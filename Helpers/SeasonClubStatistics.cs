using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// The single definition of "what a club delivered in a season", shared by the KPI submission and the
/// Ghars Annual Report.
///
/// Both surfaces report the same six indicators, and before this existed each would have carried its
/// own query — which is exactly how an Annual Report comes to say 12 activities while the season KPI
/// says 11. Every caller goes through <see cref="GetAsync"/>; nothing re-derives these numbers inline.
///
/// AUTHORITATIVE SOURCE: <see cref="AgendaEntry"/>. The agenda is the record of what actually happened.
/// Published partner offerings, unconfirmed bookings, draft agenda rows and the Activity catalogue are
/// all deliberately excluded — none of them is evidence that anything was delivered.
/// </summary>
public static class SeasonClubStatistics
{
    /// <summary>
    /// Agenda states that count as delivered. Matches the rule the KPI derivation has always used:
    /// a club submits an entry once the activity has happened, and DSC may then approve it.
    /// </summary>
    public static bool IsDelivered(AgendaEntryStatus status)
        => status is AgendaEntryStatus.Submitted or AgendaEntryStatus.Approved;

    /// <summary>
    /// Season/club statistics derived from delivered Agenda entries.
    ///
    /// <paramref name="organizationId"/> must already have been resolved from the caller's
    /// organization links — this helper filters, it does not authorise.
    /// </summary>
    public static async Task<SeasonClubStats> GetAsync(AppDbContext db, int organizationId, int seasonId)
    {
        if (organizationId <= 0 || seasonId <= 0) return SeasonClubStats.Empty;

        var club = await db.Organizations
            .Where(x => x.Id == organizationId)
            .Select(x => new { x.NameEn, x.NameAr })
            .FirstOrDefaultAsync();

        // One query, projected to exactly the fields the indicators need. The booking is joined so the
        // origin of each activity (club-proposed vs proposed through the DSC catalogue) can be
        // determined from data rather than guessed - see the design note §1.7.
        var rows = await db.AgendaEntries
            .Where(x => x.OrganizationId == organizationId
                        && x.SeasonId == seasonId
                        && (x.Status == AgendaEntryStatus.Submitted || x.Status == AgendaEntryStatus.Approved))
            .OrderBy(x => x.ActivityDate).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.SubjectEn,
                x.SubjectAr,
                x.LecturerName,
                x.DepartmentOrOrganization,
                x.ActivityDate,
                x.Category,
                x.OtherCategory,
                x.NumberOfParticipants,
                x.BookingRequestId,
                // Null when there is no booking at all; null-valued when the booking is a custom
                // (entity-first) request that the club wrote itself.
                BookingActivityId = x.BookingRequestId == null
                    ? null
                    : db.BookingRequests.Where(b => b.Id == x.BookingRequestId).Select(b => b.ActivityId).FirstOrDefault()
            })
            .ToListAsync();

        if (rows.Count == 0) return SeasonClubStats.Empty;

        // An entity name that is just the club itself is not an implementing entity. Verified against
        // the development data before being added here: no existing row matches, so this changes no
        // stored KPI value and only prevents a wrong count in future data.
        var clubNames = new[] { club?.NameEn, club?.NameAr }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize)
            .ToHashSet();

        var detail = rows.Select(x => new DeliveredActivityRow(
            AgendaEntryId: x.Id,
            TitleEn: x.SubjectEn,
            TitleAr: x.SubjectAr,
            ImplementingEntity: string.IsNullOrWhiteSpace(x.DepartmentOrOrganization) ? null : x.DepartmentOrOrganization.Trim(),
            Lecturer: string.IsNullOrWhiteSpace(x.LecturerName) ? null : x.LecturerName.Trim(),
            ActivityDate: x.ActivityDate,
            Category: x.Category,
            OtherCategory: x.OtherCategory,
            Participants: x.NumberOfParticipants,
            // The catalogue is DSC-managed: a DSC-created activity, or a partner offering that only
            // became bookable because DSC approved and published it. Anything else originated with the
            // club - a custom request it wrote, or an activity it recorded directly.
            CouncilProposed: x.BookingActivityId != null)).ToList();

        var lecturers = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.LecturerName))
            .Select(x => Normalize(x.LecturerName))
            .Distinct()
            .Count();

        var entities = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.DepartmentOrOrganization))
            .Select(x => Normalize(x.DepartmentOrOrganization))
            .Where(x => !clubNames.Contains(x))
            .Distinct()
            .Count();

        return new SeasonClubStats(
            HasData: true,
            DeliveredActivities: rows.Count,
            Participants: rows.Sum(x => x.NumberOfParticipants),
            Lecturers: lecturers,
            ImplementingEntities: entities,
            CouncilProposed: detail.Count(x => x.CouncilProposed),
            ClubProposed: detail.Count(x => !x.CouncilProposed),
            Delivered: detail);
    }

    /// <summary>
    /// Uniqueness for lecturers and implementing entities is by normalised free-text name: both fields
    /// are free text on <see cref="AgendaEntry"/> and there is no master-person or master-entity
    /// relationship to prefer. The same lecturer across three activities therefore counts once, which
    /// is what "number of lecturers" asks for.
    /// </summary>
    private static string Normalize(string? value)
        => string.Join(' ', (value ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Derived season statistics for one club. <see cref="HasData"/> is false when the club has no
/// delivered agenda entries at all — callers must not treat that as "zero of everything", because the
/// KPI form falls back to the club's own manual entry in that case.
/// </summary>
public sealed record SeasonClubStats(
    bool HasData,
    int DeliveredActivities,
    int Participants,
    int Lecturers,
    int ImplementingEntities,
    int CouncilProposed,
    int ClubProposed,
    IReadOnlyList<DeliveredActivityRow> Delivered)
{
    public static readonly SeasonClubStats Empty =
        new(false, 0, 0, 0, 0, 0, 0, Array.Empty<DeliveredActivityRow>());
}

/// <summary>
/// One delivered activity, in the shape the Annual Report's section 3 needs. Also the serialisation
/// shape of the report's frozen snapshot, which is why it is a named record with plain properties:
/// <see cref="AgendaEntryId"/> keeps the link back to the live entry for authorised readers, without
/// the snapshot depending on that entry still existing or still saying the same thing.
/// </summary>
public sealed record DeliveredActivityRow(
    int AgendaEntryId,
    string TitleEn,
    string TitleAr,
    string? ImplementingEntity,
    string? Lecturer,
    DateTime ActivityDate,
    AgendaTargetCategory Category,
    string? OtherCategory,
    int Participants,
    bool CouncilProposed);
