using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class SystemAuditLog
{
    public int Id { get; set; }

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UserId { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = "";

    [Required, MaxLength(100)]
    public string EntityName { get; set; } = "";

    [MaxLength(64)]
    public string? EntityId { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(600)]
    public string? UserAgent { get; set; }

    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
}
