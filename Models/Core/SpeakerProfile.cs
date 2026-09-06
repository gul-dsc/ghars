using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class SpeakerProfile : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    [MaxLength(2000)]
    public string? BioEn { get; set; }

    [MaxLength(2000)]
    public string? BioAr { get; set; }

    [MaxLength(300)]
    public string? SpecialtyEn { get; set; }

    [MaxLength(300)]
    public string? SpecialtyAr { get; set; }

    [MaxLength(400)]
    public string? PhotoPath { get; set; }

    public bool IsVerified { get; set; } = false;
}
