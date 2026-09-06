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
