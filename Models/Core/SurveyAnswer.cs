namespace GharsPlatform.Models.Core;

public class SurveyAnswer
{
    public int Id { get; set; }

    public int SurveyResponseId { get; set; }
    public SurveyResponse? SurveyResponse { get; set; }

    public int SurveyQuestionId { get; set; }
    public SurveyQuestion? SurveyQuestion { get; set; }

    public byte? StarsValue { get; set; }
    public bool? BoolValue { get; set; }
    public string? TextValue { get; set; }
    public int? SelectedOptionId { get; set; }
    public SurveyOption? SelectedOption { get; set; }
}
