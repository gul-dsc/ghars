using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class SurveyOption
{
    public int Id { get; set; }

    public int SurveyQuestionId { get; set; }
    public SurveyQuestion? SurveyQuestion { get; set; }

    [Required, MaxLength(250)]
    public string OptionEn { get; set; } = "";

    [Required, MaxLength(250)]
    public string OptionAr { get; set; } = "";

    public int SortOrder { get; set; } = 1;
}
