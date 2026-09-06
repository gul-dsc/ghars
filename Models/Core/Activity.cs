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

    public ActivityStatus Status { get; set; } = ActivityStatus.Draft;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public ICollection<ActivitySpeaker> Speakers { get; set; } = new List<ActivitySpeaker>();
    public ICollection<BookingRequest> BookingRequests { get; set; } = new List<BookingRequest>();
}
