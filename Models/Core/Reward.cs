using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class Reward : SoftDeletableEntity
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

    public int PointsRequired { get; set; } = 50;

    [MaxLength(500)]
    public string? ImagePath { get; set; }

    public bool IsActive { get; set; } = true;
}
