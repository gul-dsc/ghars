using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class RewardRedemption
{
    public int Id { get; set; }

    public int RewardId { get; set; }
    public Reward? Reward { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;

    public RedemptionStatus Status { get; set; } = RedemptionStatus.Requested;

    public DateTime? DecisionAtUtc { get; set; }

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
