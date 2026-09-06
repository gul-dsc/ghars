using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class SurveyResponse
{
    public int Id { get; set; }

    public int SurveyId { get; set; }
    public Survey? Survey { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<SurveyAnswer> Answers { get; set; } = new List<SurveyAnswer>();
}
