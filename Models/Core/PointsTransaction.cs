using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class PointsTransaction
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public PointsTransactionType Type { get; set; } = PointsTransactionType.Earn;

    public int Points { get; set; }

    [MaxLength(300)]
    public string? ReasonEn { get; set; }

    [MaxLength(300)]
    public string? ReasonAr { get; set; }

    public PointsReferenceType ReferenceType { get; set; } = PointsReferenceType.Other;

    public int? ReferenceId { get; set; }

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? ByUserId { get; set; }
}
