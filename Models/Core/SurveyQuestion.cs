using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class SurveyQuestion
{
    public int Id { get; set; }

    public int SurveyId { get; set; }
    public Survey? Survey { get; set; }

    [Required, MaxLength(500)]
    public string QuestionEn { get; set; } = "";

    [Required, MaxLength(500)]
    public string QuestionAr { get; set; } = "";

    public SurveyQuestionType QuestionType { get; set; } = SurveyQuestionType.Stars;

    public bool IsRequired { get; set; } = true;

    public int SortOrder { get; set; } = 1;

    public ICollection<SurveyOption> Options { get; set; } = new List<SurveyOption>();
}
