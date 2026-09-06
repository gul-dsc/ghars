using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class Organization : SoftDeletableEntity
{
    public int Id { get; set; }

    public OrganizationType OrganizationType { get; set; }

    [Required, MaxLength(250)]
    public string NameEn { get; set; } = "";

    [Required, MaxLength(250)]
    public string NameAr { get; set; } = "";

    [MaxLength(100)]
    public string? TradeLicenseNo { get; set; }

    [Required, MaxLength(250)]
    public string Email { get; set; } = "";

    [Required, MaxLength(50)]
    public string Phone { get; set; } = "";

    [MaxLength(500)]
    public string? AddressEn { get; set; }

    [MaxLength(500)]
    public string? AddressAr { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(300)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(400)]
    public string? LogoPath { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public DateTime? ApprovedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ApprovedByUserId { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public ICollection<OrganizationContact> Contacts { get; set; } = new List<OrganizationContact>();
    public ICollection<OrganizationDocument> Documents { get; set; } = new List<OrganizationDocument>();
}
