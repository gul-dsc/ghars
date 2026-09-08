using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GharsPlatform.Helpers;

/// <summary>
/// The freeze rule for a Ghars Annual Report, in one place.
///
///   Draft / ReturnedForCorrection / Rejected  →  derived values track live Agenda data
///   Submitted / Approved                      →  derived values are the snapshot taken at submission
///
/// This is what stops an approved report from silently changing because someone edited an old agenda
/// row two seasons later. It is also why <see cref="Capture"/> is called from exactly one place in the
/// club controller: create-and-submit and edit-and-submit must not be able to freeze different things.
/// </summary>
public static class AnnualReportSnapshot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes the derived indicators and the delivered-activity table onto the report. Does nothing
    /// once the report has left the club's hands, so a stray call can never rewrite an official record.
    /// </summary>
    public static void Capture(GharsAnnualReport report, SeasonClubStats stats)
    {
        if (!AnnualReportWorkflow.DerivedValuesAreLive(report.Status)) return;

        report.TotalLecturesDelivered = stats.DeliveredActivities;
        report.ParticipantsTotal = stats.Participants;
        report.LecturersCount = stats.Lecturers;
        report.ImplementingEntitiesCount = stats.ImplementingEntities;
        report.ClubProposedLectures = stats.ClubProposed;
        report.CouncilProposedLectures = stats.CouncilProposed;
        report.DeliveredActivitiesJson = JsonSerializer.Serialize(stats.Delivered, Json);
    }

    /// <summary>
    /// What to render for this report: live figures while it is still editable, the frozen snapshot
    /// once it has been submitted.
    /// </summary>
    public static async Task<AnnualReportPresentation> PresentAsync(AppDbContext db, GharsAnnualReport report)
    {
        var live = AnnualReportWorkflow.DerivedValuesAreLive(report.Status);

        var stats = live
            ? await SeasonClubStatistics.GetAsync(db, report.OrganizationId, report.SeasonId)
            : FromSnapshot(report);

        // Section 3 rows link back to the live Agenda entry for authorised readers, but only where that
        // entry still exists: a frozen snapshot must render correctly even after its source row is gone.
        var ids = stats.Delivered.Select(x => x.AgendaEntryId).Distinct().ToList();
        var stillPresent = ids.Count == 0
            ? new HashSet<int>()
            : (await db.AgendaEntries
                .Where(x => ids.Contains(x.Id) && x.OrganizationId == report.OrganizationId)
                .Select(x => x.Id)
                .ToListAsync()).ToHashSet();

        return new AnnualReportPresentation(stats, IsFrozen: !live, report.SnapshotTakenAtUtc, stillPresent);
    }

    /// <summary>Rebuilds the stats shape from the frozen columns and JSON, so views take one code path.</summary>
    private static SeasonClubStats FromSnapshot(GharsAnnualReport report)
    {
        IReadOnlyList<DeliveredActivityRow> rows = Array.Empty<DeliveredActivityRow>();
        if (!string.IsNullOrWhiteSpace(report.DeliveredActivitiesJson))
        {
            try
            {
                rows = JsonSerializer.Deserialize<List<DeliveredActivityRow>>(report.DeliveredActivitiesJson, Json)
                       ?? new List<DeliveredActivityRow>();
            }
            catch (JsonException)
            {
                // An unreadable snapshot must not take the page down. The stored totals are separate
                // columns and remain authoritative; the table renders empty and says so.
                rows = Array.Empty<DeliveredActivityRow>();
            }
        }

        return new SeasonClubStats(
            HasData: report.TotalLecturesDelivered > 0 || rows.Count > 0,
            DeliveredActivities: report.TotalLecturesDelivered,
            Participants: report.ParticipantsTotal,
            Lecturers: report.LecturersCount,
            ImplementingEntities: report.ImplementingEntitiesCount,
            CouncilProposed: report.CouncilProposedLectures,
            ClubProposed: report.ClubProposedLectures,
            Delivered: rows);
    }
}

/// <summary>
/// One report's derived content, ready to render, plus whether it is frozen. Views ask this rather
/// than deciding for themselves which source to read, so the club form, the DSC review screen and the
/// print output cannot disagree about what the report says.
/// </summary>
public sealed record AnnualReportPresentation(
    SeasonClubStats Stats,
    bool IsFrozen,
    DateTime? SnapshotTakenAtUtc,
    HashSet<int> LinkableAgendaEntryIds)
{
    public bool CanLinkTo(int agendaEntryId) => LinkableAgendaEntryIds.Contains(agendaEntryId);
}
