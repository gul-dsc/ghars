using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class BookingRequest : AuditableEntity
{
    public int Id { get; set; }

    public string ReferenceNumber => $"GHR-BK-{CreatedAtUtc:yyyy}-{Id:D5}";

    // Nullable: direct entity-first booking requests (docs: "Ghars Lecture & Events Booking System")
    // are not anchored to a pre-published program/activity.
    public int? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    // Season selected by the club for direct requests; program-anchored bookings fall back to Activity.SeasonId.
    public int? SeasonId { get; set; }
    public Season? Season { get; set; }

    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int? PartnerOrganizationId { get; set; }
    public Organization? PartnerOrganization { get; set; }

    [Required, MaxLength(450)]
    public string RequestedByUserId { get; set; } = "";

    public ActivityType RequestedActivityType { get; set; } = ActivityType.Lecture;

    [MaxLength(250)]
    public string? Subject { get; set; }

    public DateTime? ProposedStartDateTime { get; set; }
    public DateTime? ProposedEndDateTime { get; set; }

    /// <summary>
    /// The implementing entity's published availability slot this request was made against, or
    /// <c>null</c> when the club proposed its own date and time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <b>Schedule Source</b>, and it is a different axis from Booking Source: an Existing
    /// Program and a Custom Program request can each arrive either from the partner's calendar or
    /// from a date the club typed. All four combinations are valid, and no third booking type was
    /// created for any of them.
    /// </para>
    /// <para>
    /// <c>null</c> for every booking that predates the calendar, and nothing is back-filled — in
    /// particular a historical booking is never matched to a slot because its date and time happen
    /// to coincide. Provenance is recorded at the moment of the choice or it is not claimed at all.
    /// </para>
    /// <para>
    /// The reference survives the slot being released and re-claimed by another club: it records
    /// what this club selected when it asked. The slot's own
    /// <see cref="PartnerAvailabilitySlot.HeldByBookingRequestId"/> is the live holder.
    /// </para>
    /// </remarks>
    public int? PartnerAvailabilitySlotId { get; set; }
    public PartnerAvailabilitySlot? PartnerAvailabilitySlot { get; set; }

    [MaxLength(250)]
    public string? TargetAudienceCsv { get; set; }

    [MaxLength(150)]
    public string? OtherTargetAudience { get; set; }

    [MaxLength(1000)]
    public string? AudienceDetails { get; set; }

    public int RequestedSeats { get; set; } = 1;

    [MaxLength(150)]
    public string? ContactPersonName { get; set; }

    [MaxLength(50)]
    public string? ContactPhone { get; set; }

    [MaxLength(150)]
    public string? ContactEmail { get; set; }

    [MaxLength(1000)]
    public string? SpecialRequirements { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public DateTime? ConfirmedStartUtc { get; set; }
    public DateTime? ConfirmedEndUtc { get; set; }
    public int? AcceptedProposedTimeOptionId { get; set; }

    [MaxLength(2000)]
    public string? PartnerResponseNotes { get; set; }

    // Confirmation details supplied by the implementing entity (docs: booking confirmation must
    // include the lecturer's name and contact details; logistics go into PartnerResponseNotes).
    [MaxLength(200)]
    public string? LecturerName { get; set; }

    [MaxLength(200)]
    public string? LecturerContact { get; set; }

    // Optional subject change proposed by the implementing entity alongside proposed times;
    // applied to Subject (with audit trail) only when the club accepts.
    [MaxLength(250)]
    public string? ProposedSubject { get; set; }

    public DateTime? DecisionAtUtc { get; set; }

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    public ICollection<BookingAuditTrail> AuditTrail { get; set; } = new List<BookingAuditTrail>();
    public ICollection<BookingProposedTimeOption> ProposedTimeOptions { get; set; } = new List<BookingProposedTimeOption>();
}
