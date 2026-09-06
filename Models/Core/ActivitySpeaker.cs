using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class ActivitySpeaker
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    [Required, MaxLength(450)]
    public string SpeakerUserId { get; set; } = "";
}
