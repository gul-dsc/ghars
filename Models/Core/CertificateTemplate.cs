using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class CertificateTemplate : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string NameEn { get; set; } = "";

    [Required, MaxLength(150)]
    public string NameAr { get; set; } = "";

    [MaxLength(400)]
    public string? BackgroundImagePath { get; set; }

    // Optional: HTML template if you want hybrid rendering later.
    public string? HtmlTemplate { get; set; }

    public bool IsActive { get; set; } = true;
}
