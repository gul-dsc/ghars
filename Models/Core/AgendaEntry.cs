using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class AgendaEntry : AuditableEntity
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int? BookingRequestId { get; set; }
    public BookingRequest? BookingRequest { get; set; }
    public ActivityType ActivityType { get; set; } = ActivityType.Lecture;
    [Required, MaxLength(250)] public string SubjectEn { get; set; } = "";
    [Required, MaxLength(250)] public string SubjectAr { get; set; } = "";
    public DateTime ActivityDate { get; set; }
    public AgendaTargetCategory Category { get; set; } = AgendaTargetCategory.Players;
    [MaxLength(150)] public string? OtherCategory { get; set; }
    [Required, MaxLength(200)] public string LecturerName { get; set; } = "";
    [MaxLength(250)] public string? DepartmentOrOrganization { get; set; }
    public int NumberOfParticipants { get; set; }
    public AgendaEntryStatus Status { get; set; } = AgendaEntryStatus.Draft;
    [MaxLength(2000)] public string? Notes { get; set; }
    public ICollection<AgendaMedia> Media { get; set; } = new List<AgendaMedia>();
}
