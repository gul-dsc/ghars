using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class AgendaMedia : AuditableEntity
{
    public int Id { get; set; }
    public int AgendaEntryId { get; set; }
    public AgendaEntry? AgendaEntry { get; set; }
    public GalleryMediaType MediaType { get; set; } = GalleryMediaType.Photo;
    [MaxLength(500)] public string? FilePath { get; set; }
    [MaxLength(700)] public string? ExternalUrl { get; set; }
    [MaxLength(250)] public string? TitleEn { get; set; }
    [MaxLength(250)] public string? TitleAr { get; set; }
    public bool IsPublished { get; set; } = true;
}
