using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// One club's official Ghars Annual Report for one sports season — the electronic form of
/// <c>docs/Ghars_Clubs Report Form.docx</c>.
///
/// A distinct business record rather than an extension of <see cref="KpiSubmission"/>: it is narrative,
/// it carries a season-specific contact snapshot, and it must freeze on approval while KPI must not.
///
/// The six section-2 totals and the section-3 detail are *snapshots*. While the report is editable they
/// are recomputed from the club's Agenda on every open and save; on submission they are frozen, so an
/// approved report cannot change because someone later edits an old agenda row. See
/// <c>GHARS_ANNUAL_REPORT_CHANNEL_DESIGN.md</c> §1.6.
/// </summary>
public class GharsAnnualReport : AuditableEntity
{
    public int Id { get; set; }

    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    /// <summary>
    /// The reporting club. Always resolved server-side from the authenticated user's
    /// <see cref="OrganizationAdminLink"/> set — never model-bound from a club-facing form.
    /// </summary>
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public AnnualReportStatus Status { get; set; } = AnnualReportStatus.Draft;

    // ------------------------------------------------------------------ Section 1: basic information
    //
    // The club name is not stored: it is read from Organization, so a renamed club is never
    // misreported. The three contact fields ARE stored, as a season-specific snapshot — the report
    // records who was responsible during that season, and must not change when the organization's
    // master contact record is later updated.

    [MaxLength(200)] public string? ProgramCoordinatorName { get; set; }
    [MaxLength(50)] public string? ContactNumber { get; set; }
    [MaxLength(250)] public string? ContactEmail { get; set; }

    // ------------------------------------------------------------------ Section 2: overall indicators
    //
    // All six are system-derived from delivered Agenda entries via SeasonClubStatistics — the same
    // query that backs the KPI submission, so the two can never report different totals.

    public int ClubProposedLectures { get; set; }
    public int CouncilProposedLectures { get; set; }
    public int TotalLecturesDelivered { get; set; }
    public int LecturersCount { get; set; }
    public int ImplementingEntitiesCount { get; set; }
    public int ParticipantsTotal { get; set; }

    /// <summary>
    /// Section 3, frozen at submission: the delivered activity list serialised as JSON
    /// (<see cref="Helpers.DeliveredActivityRow"/>). Held as one column rather than child rows because
    /// nothing reports across annual-report detail — the live Agenda remains the queryable source —
    /// and each row keeps its AgendaEntryId so an authorised reader still gets a link back.
    /// </summary>
    public string? DeliveredActivitiesJson { get; set; }

    /// <summary>When the derived values above were captured. Null while the report has never been submitted.</summary>
    public DateTime? SnapshotTakenAtUtc { get; set; }

    // ------------------------------------------------------------------ Section 4: narrative
    //
    // Stored in the language the club typed and displayed as entered: the source template is Arabic and
    // asks for one answer per prompt, not a bilingual pair.

    [MaxLength(4000)] public string? KeyResults { get; set; }
    [MaxLength(4000)] public string? Challenges { get; set; }
    [MaxLength(4000)] public string? DevelopmentProposals { get; set; }

    // ------------------------------------------------------------------ Workflow stamps

    public DateTime? SubmittedAtUtc { get; set; }
    [MaxLength(450)] public string? SubmittedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(450)] public string? ReviewedByUserId { get; set; }

    [MaxLength(2000)] public string? ReviewNotes { get; set; }
}
