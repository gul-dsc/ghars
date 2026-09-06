using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class Notification
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(150)]
    public string TitleAr { get; set; } = "";

    [Required, MaxLength(2000)]
    public string MessageEn { get; set; } = "";

    [Required, MaxLength(2000)]
    public string MessageAr { get; set; } = "";

    public NotificationType Type { get; set; } = NotificationType.Info;

    public NotificationTargetType TargetType { get; set; } = NotificationTargetType.All;

    [MaxLength(100)]
    public string? TargetRoleName { get; set; }

    public int? TargetOrganizationId { get; set; }

    [MaxLength(450)]
    public string? TargetUserId { get; set; }

    [MaxLength(600)]
    public string? LinkUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
}
