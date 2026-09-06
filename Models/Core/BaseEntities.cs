using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public abstract class AuditableEntity
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }
}

public abstract class SoftDeletableEntity : AuditableEntity
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    [MaxLength(450)]
    public string? DeletedByUserId { get; set; }
}
