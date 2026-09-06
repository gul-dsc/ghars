using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class NotificationDelivery
{
    public int Id { get; set; }

    public int NotificationId { get; set; }
    public Notification? Notification { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
