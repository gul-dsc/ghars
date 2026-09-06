using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class BookingAuditTrail
{
    public int Id { get; set; }

    public int BookingRequestId { get; set; }
    public BookingRequest? BookingRequest { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = "";

    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? ByUserId { get; set; }
}
