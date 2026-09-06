using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class Survey : AuditableEntity
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    [Required, MaxLength(200)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(200)]
    public string TitleAr { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime? OpensAtUtc { get; set; }
    public DateTime? ClosesAtUtc { get; set; }

    public ICollection<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();
}
