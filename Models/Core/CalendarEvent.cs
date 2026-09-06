using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class CalendarEvent : AuditableEntity
{
    public int Id { get; set; }

    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    [Required, MaxLength(200)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(200)]
    public string TitleAr { get; set; } = "";

    [MaxLength(2000)]
    public string? DescriptionEn { get; set; }

    [MaxLength(2000)]
    public string? DescriptionAr { get; set; }

    public DateOnly EventDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    [MaxLength(300)]
    public string? LocationEn { get; set; }

    [MaxLength(300)]
    public string? LocationAr { get; set; }

    public CalendarEventType EventType { get; set; } = CalendarEventType.Other;

    public bool IsPublic { get; set; } = true;
}
