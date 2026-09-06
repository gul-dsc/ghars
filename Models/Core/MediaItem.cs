using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class MediaItem : AuditableEntity
{
    public int Id { get; set; }

    public int AlbumId { get; set; }
    public MediaAlbum? Album { get; set; }

    public MediaType MediaType { get; set; } = MediaType.Image;

    [MaxLength(500)]
    public string? FilePath { get; set; }

    // Used for external video links or external PDF/publication URLs.
    [MaxLength(600)]
    public string? VideoUrl { get; set; }

    [MaxLength(300)]
    public string? CaptionEn { get; set; }

    [MaxLength(300)]
    public string? CaptionAr { get; set; }

    public int SortOrder { get; set; } = 1;
}
