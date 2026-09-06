using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class NewsItem : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(200)]
    public string TitleAr { get; set; } = "";

    [MaxLength(600)]
    public string? Url { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }

    public int Priority { get; set; } = 0;
}
