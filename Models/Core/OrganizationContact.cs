using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class OrganizationContact : AuditableEntity
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [Required, MaxLength(200)]
    public string FullName { get; set; } = "";

    [MaxLength(150)]
    public string? PositionTitleEn { get; set; }

    [MaxLength(150)]
    public string? PositionTitleAr { get; set; }

    [Required, MaxLength(250)]
    public string Email { get; set; } = "";

    [Required, MaxLength(50)]
    public string Phone { get; set; } = "";

    public bool IsPrimary { get; set; }
}
