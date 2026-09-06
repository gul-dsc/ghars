using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Identity;

public class ApplicationUser : Microsoft.AspNetCore.Identity.IdentityUser
{
    [MaxLength(200)]
    public string? FullName { get; set; }

    /// <summary>
    /// "en" or "ar" — used for default UI preference and certificate language.
    /// Actual request culture is still controlled by localization middleware (cookie/query).
    /// </summary>
    [MaxLength(10)]
    public string? PreferredLanguage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Optional: link user to an organization for convenience
    public int? PrimaryOrganizationId { get; set; }
}
