using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class Certificate
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(450)]
    public string IssuedByUserId { get; set; } = "";

    [Required, MaxLength(30)]
    public string CertificateNo { get; set; } = "";

    [Required, MaxLength(64)]
    public string VerifyToken { get; set; } = Guid.NewGuid().ToString("N");

    [MaxLength(500)]
    public string? PdfPath { get; set; }

    [MaxLength(500)]
    public string? QrImagePath { get; set; }

    public CertificateStatus Status { get; set; } = CertificateStatus.Issued;

    public DateTime? RevokedAtUtc { get; set; }

    [MaxLength(450)]
    public string? RevokedByUserId { get; set; }

    [MaxLength(2000)]
    public string? RevokeReason { get; set; }
}
