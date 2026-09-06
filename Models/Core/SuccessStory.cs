using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class SuccessStory : SoftDeletableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(250)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(250)]
    public string TitleAr { get; set; } = "";

    [Required]
    public string StoryEn { get; set; } = "";

    [Required]
    public string StoryAr { get; set; } = "";

    [MaxLength(400)]
    public string? HeroImagePath { get; set; }

    public bool IsPublished { get; set; } = false;

    public DateTime? PublishedAtUtc { get; set; }
}
