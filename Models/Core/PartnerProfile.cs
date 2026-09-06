using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class PartnerProfile : AuditableEntity
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [MaxLength(2000)]
    public string? OverviewEn { get; set; }

    [MaxLength(2000)]
    public string? OverviewAr { get; set; }

    public bool IsFeatured { get; set; } = false;

    public int FeatureSortOrder { get; set; } = 1;
}
