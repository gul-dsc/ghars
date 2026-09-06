using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class UserPointsWallet : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public int Balance { get; set; } = 0;
}
