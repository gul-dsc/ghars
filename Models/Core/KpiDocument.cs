using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class KpiDocument : AuditableEntity
{
    public int Id { get; set; }
    public int KpiSubmissionId { get; set; }
    public KpiSubmission? KpiSubmission { get; set; }
    [Required, MaxLength(500)] public string FilePath { get; set; } = "";
    [MaxLength(250)] public string? OriginalFileName { get; set; }
}
