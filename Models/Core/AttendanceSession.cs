using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class AttendanceSession : AuditableEntity
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public DateTime SessionStartUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SessionEndUtc { get; set; }

    [MaxLength(20)]
    public string? Code { get; set; }

    [Required, MaxLength(64)]
    public string QrToken { get; set; } = Guid.NewGuid().ToString("N");

    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
}
