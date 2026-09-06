using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class OrganizationAdminLink
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    // Convenient hint for UI (actual authority is from AspNetRoles)
    public OrganizationType RoleHint { get; set; } = OrganizationType.Club;
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByUserId { get; set; }

}
