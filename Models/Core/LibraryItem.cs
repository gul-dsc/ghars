using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class LibraryItem : SoftDeletableEntity
{
    public int Id { get; set; }

    public int LibraryCategoryId { get; set; }
    public LibraryCategory? LibraryCategory { get; set; }

    [Required, MaxLength(250)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(250)]
    public string TitleAr { get; set; } = "";

    [MaxLength(2000)]
    public string? DescriptionEn { get; set; }

    [MaxLength(2000)]
    public string? DescriptionAr { get; set; }

    [MaxLength(500)]
    public string? FilePath { get; set; }

    [MaxLength(500)]
    public string? CoverImagePath { get; set; }

    [MaxLength(700)]
    public string? ExternalUrl { get; set; }

    [MaxLength(250)]
    public string? PublishingEntityEn { get; set; }

    [MaxLength(250)]
    public string? PublishingEntityAr { get; set; }

    public DateTime? PublicationDate { get; set; }

    public LibraryContentType ContentType { get; set; } = LibraryContentType.EducationalBooklet;

    [MaxLength(500)]
    public string? ThumbnailPath { get; set; }

    public int PointsCost { get; set; } = 0;

    public bool IsPublic { get; set; } = true;

    public bool IsPublished { get; set; } = true;

    public int DownloadCount { get; set; } = 0;
}
