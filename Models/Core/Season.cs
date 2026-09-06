using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class Season : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(200)]
    public string TitleAr { get; set; } = "";

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<CalendarEvent> CalendarEvents { get; set; } = new List<CalendarEvent>();
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}
