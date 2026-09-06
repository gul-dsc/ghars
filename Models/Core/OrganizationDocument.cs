using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class OrganizationDocument
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public OrganizationDocumentType DocumentType { get; set; } = OrganizationDocumentType.Other;

    [Required, MaxLength(500)]
    public string FilePath { get; set; } = "";

    [Required, MaxLength(255)]
    public string OriginalFileName { get; set; } = "";

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UploadedByUserId { get; set; }
}
