using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class Activity : AuditableEntity
{
    public int Id { get; set; }

    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    public int? PartnerOrganizationId { get; set; }
    public Organization? PartnerOrganization { get; set; }

    public ActivityType Type { get; set; } = ActivityType.Lecture;

    [Required, MaxLength(250)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(250)]
    public string TitleAr { get; set; } = "";

    [MaxLength(3000)]
    public string? DescriptionEn { get; set; }

    [MaxLength(3000)]
    public string? DescriptionAr { get; set; }

    [MaxLength(150)]
    public string? CategoryEn { get; set; }

    [MaxLength(150)]
    public string? CategoryAr { get; set; }

    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }

    [MaxLength(300)]
    public string LocationEn { get; set; } = "";

    [MaxLength(300)]
    public string LocationAr { get; set; } = "";

    public int Capacity { get; set; } = 50;

    public bool AllowWalkIn { get; set; } = false;

    // ------------------------------------------------------------------ Partner offering fields
    //
    // An Activity owned by an implementing entity (PartnerOrganizationId set) doubles as a bookable
    // offering in the club catalogue. These four columns are what that role needs and the rest of
    // the entity did not already provide. All are nullable, so every pre-existing row stays valid
    // and nothing has to be back-filled.

    /// <summary>
    /// Comma-separated audiences this offering is aimed at, using the same vocabulary and storage
    /// convention as <see cref="BookingRequest.TargetAudienceCsv"/> so the value can be carried
    /// straight into a booking request without translation.
    /// </summary>
    [MaxLength(250)]
    public string? TargetAudienceCsv { get; set; }

    [MaxLength(150)]
    public string? OtherTargetAudience { get; set; }

    /// <summary>
    /// Optional window during which clubs may request this offering. This is deliberately separate
    /// from <see cref="StartDateTime"/>/<see cref="EndDateTime"/>, which are an indicative session
    /// slot: those already carry scheduling meaning for attendance and the admin dashboard, so
    /// overloading them as an availability window would change the meaning of 59 existing rows.
    /// Null at either end means "no bound in that direction".
    /// </summary>
    public DateTime? AvailableFromUtc { get; set; }

    public DateTime? AvailableUntilUtc { get; set; }

    // ------------------------------------------------------------------ DSC review workflow
    //
    // Null for DSC-created activities: they have no owning implementing entity and do not pass
    // through partner review. For partner offerings this is the authority on whether clubs may see
    // the row, alongside Status.

    public OfferingApprovalStatus? ApprovalStatus { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    [MaxLength(450)]
    public string? SubmittedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    /// <summary>The reviewer's note from the most recent return or rejection, shown to the partner
    /// so they know what to correct. Cleared on approval.</summary>
    [MaxLength(2000)]
    public string? ReviewNotes { get; set; }

    public ActivityStatus Status { get; set; } = ActivityStatus.Draft;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public ICollection<ActivitySpeaker> Speakers { get; set; } = new List<ActivitySpeaker>();
    public ICollection<BookingRequest> BookingRequests { get; set; } = new List<BookingRequest>();
}
