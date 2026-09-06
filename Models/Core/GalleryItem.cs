using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class GalleryItem : AuditableEntity
{
    public int Id { get; set; }
    [Required, MaxLength(250)] public string TitleEn { get; set; } = "";
    [Required, MaxLength(250)] public string TitleAr { get; set; } = "";
    [MaxLength(2000)] public string? DescriptionEn { get; set; }
    [MaxLength(2000)] public string? DescriptionAr { get; set; }
    public GalleryMediaType MediaType { get; set; } = GalleryMediaType.Photo;
    [MaxLength(500)] public string? FilePath { get; set; }
    [MaxLength(700)] public string? ExternalUrl { get; set; }
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int? ActivityId { get; set; }
    public Activity? Activity { get; set; }
    public int? AgendaEntryId { get; set; }
    public AgendaEntry? AgendaEntry { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public DateTime MediaDate { get; set; } = DateTime.UtcNow;
    public bool IsPublished { get; set; } = true;
}
