using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class MediaAlbum : SoftDeletableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(200)]
    public string TitleAr { get; set; } = "";

    [MaxLength(2000)]
    public string? DescriptionEn { get; set; }

    [MaxLength(2000)]
    public string? DescriptionAr { get; set; }

    [MaxLength(400)]
    public string? CoverImagePath { get; set; }

    public int? SeasonId { get; set; }
    public Season? Season { get; set; }

    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public DateTime AlbumDate { get; set; } = DateTime.UtcNow;

    public bool IsPublic { get; set; } = true;

    public ICollection<MediaItem> Items { get; set; } = new List<MediaItem>();
}
