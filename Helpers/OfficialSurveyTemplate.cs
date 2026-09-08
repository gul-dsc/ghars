using GharsPlatform.Models.Core;

namespace GharsPlatform.Helpers;

/// <summary>
/// The starting question set a new Ghars survey is created with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance.</b> These are the four questions the platform's survey engine has always created by
/// default — overall satisfaction, quality, recommendation and free-text comments. No approved Ghars
/// question list exists in the project documents, so nothing new has been invented here: the existing
/// set is carried forward, with the official survey's wording widened from a single activity to the
/// programme and season it now covers. Only the first two, the 1–5 rating questions, feed the
/// satisfaction percentage.
/// </para>
/// <para>
/// This is a starting point, not a fixed instrument. DSC can add, edit, reorder and remove questions
/// afterwards; if the council later approves a different official question set, it is entered through
/// the authoring screen and no code changes.
/// </para>
/// </remarks>
public static class OfficialSurveyTemplate
{
    public const string TitleEn = "Ghars Program Participant Satisfaction Survey";
    public const string TitleAr = "استبيان قياس رضا المشاركين في برنامج غرس";

    public const string DescriptionEn =
        "Your feedback helps Dubai Sports Council improve the Ghars Program. The survey takes about a " +
        "minute, and your answers are anonymous.";

    public const string DescriptionAr =
        "تساعدنا ملاحظاتك في تطوير برنامج غرس بمجلس دبي الرياضي. لا يستغرق الاستبيان سوى دقيقة تقريباً، " +
        "وإجاباتك مجهولة الهوية.";

    /// <summary>Questions for the official, season-wide satisfaction survey.</summary>
    public static IEnumerable<SurveyQuestion> Questions(int surveyId) => new[]
    {
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Overall satisfaction with the Ghars Program activity (1-5)",
            QuestionAr = "الرضا العام عن نشاط برنامج غرس (١-٥)",
            QuestionType = SurveyQuestionType.Stars,
            IsRequired = true,
            SortOrder = 1
        },
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Quality of the content presented (1-5)",
            QuestionAr = "جودة المحتوى المقدم (١-٥)",
            QuestionType = SurveyQuestionType.Stars,
            IsRequired = true,
            SortOrder = 2
        },
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Would you recommend it to others?",
            QuestionAr = "هل توصي بها للآخرين؟",
            QuestionType = SurveyQuestionType.YesNo,
            IsRequired = true,
            SortOrder = 3
        },
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Comments / Suggestions",
            QuestionAr = "ملاحظات / اقتراحات",
            QuestionType = SurveyQuestionType.Text,
            IsRequired = false,
            SortOrder = 4
        }
    };

    /// <summary>Questions for an ordinary activity survey — the original wording, unchanged.</summary>
    public static IEnumerable<SurveyQuestion> ActivityQuestions(int surveyId) => new[]
    {
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Overall satisfaction (1-5)",
            QuestionAr = "الرضا العام (١-٥)",
            QuestionType = SurveyQuestionType.Stars,
            IsRequired = true,
            SortOrder = 1
        },
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Content quality (1-5)",
            QuestionAr = "جودة المحتوى (١-٥)",
            QuestionType = SurveyQuestionType.Stars,
            IsRequired = true,
            SortOrder = 2
        },
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Would you recommend it to others?",
            QuestionAr = "هل توصي بها للآخرين؟",
            QuestionType = SurveyQuestionType.YesNo,
            IsRequired = true,
            SortOrder = 3
        },
        new SurveyQuestion
        {
            SurveyId = surveyId,
            QuestionEn = "Comments / Suggestions",
            QuestionAr = "ملاحظات / اقتراحات",
            QuestionType = SurveyQuestionType.Text,
            IsRequired = false,
            SortOrder = 4
        }
    };
}
