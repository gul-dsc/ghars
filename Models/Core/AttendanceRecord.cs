using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class AttendanceRecord
{
    public int Id { get; set; }

    public int AttendanceSessionId { get; set; }
    public AttendanceSession? AttendanceSession { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public DateTime CheckInUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CheckOutUtc { get; set; }

    public AttendanceMethod Method { get; set; } = AttendanceMethod.Qr;
}
