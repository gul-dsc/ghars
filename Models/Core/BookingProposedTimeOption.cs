using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class BookingProposedTimeOption
{
    public int Id { get; set; }

    public int BookingRequestId { get; set; }
    public BookingRequest? BookingRequest { get; set; }

    public DateTime ProposedStartUtc { get; set; }
    public DateTime ProposedEndUtc { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsSelected { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }
}
