using System.ComponentModel.DataAnnotations;
using GharsPlatform.Models.Core;

namespace GharsPlatform.ViewModels;

public class OrganizationRegistrationVm
{
    [Required]
    public OrganizationType OrganizationType { get; set; } = OrganizationType.Club;

    [Required, MaxLength(250)]
    public string NameEn { get; set; } = "";

    [Required, MaxLength(250)]
    public string NameAr { get; set; } = "";

    [MaxLength(100)]
    public string? TradeLicenseNo { get; set; }

    [Required, EmailAddress, MaxLength(250)]
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

    // Primary contact
    [Required, MaxLength(200)]
    public string ContactFullName { get; set; } = "";

    [MaxLength(150)]
    public string? ContactPositionEn { get; set; }

    [MaxLength(150)]
    public string? ContactPositionAr { get; set; }

    [Required, EmailAddress, MaxLength(250)]
    public string ContactEmail { get; set; } = "";

    [Required, MaxLength(50)]
    public string ContactPhone { get; set; } = "";

    // Uploads
    public IFormFile? LogoFile { get; set; }
    public IFormFile? LicenseFile { get; set; }
    public IFormFile? SupportingFile { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
