using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>Where a satisfaction percentage came from.</summary>
public enum SatisfactionSource
{
    /// <summary>Neither source had data. Render as "No data" — never as 0%.</summary>
    None = 0,

    /// <summary>The native official Ghars satisfaction survey for that season.</summary>
    NativeOfficialSurvey = 1,

    /// <summary>The average of club-submitted, DSC-approved KPI satisfaction rates.</summary>
    ClubSubmittedKpi = 2
}

/// <summary>
/// A satisfaction figure together with the evidence behind it.
/// </summary>
/// <remarks>
/// The source travels with the number on purpose. A percentage with no provenance invites a reader to
/// assume the strongest possible one, and the difference between "1,240 participants answered" and
/// "seven clubs typed a number into a form" is the difference between a measurement and an estimate.
/// Every screen that shows <see cref="Percent"/> must show what produced it.
/// </remarks>
public sealed record SatisfactionResult(
    decimal? Percent,
    SatisfactionSource Source,
    int ResponseCount,
    int RatingAnswerCount,
    int? SurveyId)
{
    public static readonly SatisfactionResult NoData = new(null, SatisfactionSource.None, 0, 0, null);

    public bool HasValue => Percent.HasValue;

    /// <summary>Bilingual provenance label, for display beside the figure.</summary>
    public string SourceLabel(bool ar) => Source switch
    {
        SatisfactionSource.NativeOfficialSurvey => ar
            ? $"الاستبيان الرسمي لرضا المشاركين ({ResponseCount} استجابة)"
            : $"Official Participant Satisfaction Survey ({ResponseCount} responses)",
        SatisfactionSource.ClubSubmittedKpi => ar
            ? "إدخال النادي / اعتماد مجلس دبي الرياضي"
            : "Club Submitted / DSC Approved",
        _ => ar ? "لا توجد بيانات" : "No data"
    };
}

/// <summary>
/// The one place the Ghars participant-satisfaction percentage is calculated.
/// </summary>
/// <remarks>
/// <para>
/// Survey results, the KPI screens, the executive dashboard and the annual report all read from here,
/// so they cannot drift into quoting different percentages for the same season — which is what
/// happens the moment two of them each do their own averaging.
/// </para>
/// <para><b>Formula.</b> Every <see cref="SurveyQuestionType.Stars"/> answer in the season's official
/// survey is a 1–5 rating. The figure is <c>mean(stars) ÷ 5 × 100</c>, rounded to one decimal. That is
/// the conversion the platform already used for star scores; it is restated here rather than
/// redefined. Answers to yes/no, multiple-choice and free-text questions are reported on the results
/// page but never enter the percentage — mixing instruments would produce a number that means
/// nothing.
/// </para>
/// <para><b>Precedence.</b> The native survey wins when it has at least
/// <see cref="MinimumResponses"/> responses. Below that the sample is too small to publish as an
/// official figure, so the approved club-submitted average stands instead. The club-submitted value
/// in <see cref="KpiSubmission.SatisfactionRate"/> is never overwritten by any of this: it remains
/// exactly what the club submitted and DSC approved, and stays available as the fallback and as the
/// historical record.
/// </para>
/// </remarks>
public static class SatisfactionCalculator
{
    /// <summary>
    /// Responses required before the native survey is published as the official season figure.
    /// </summary>
    /// <remarks>
    /// Ten is a deliberately modest floor: high enough that one enthusiastic or one disgruntled
    /// respondent cannot move the headline percentage by tens of points, low enough that a season
    /// reaches it early. It is a publication threshold, not a statistical claim — below it the
    /// responses are still collected, still shown on the results page, and still counted; they simply
    /// do not yet displace the approved club figure.
    /// </remarks>
    public const int MinimumResponses = 10;

    /// <summary>The official satisfaction survey for a season, if one exists.</summary>
    public static Task<Survey?> OfficialSurveyAsync(AppDbContext db, int seasonId)
        => db.Surveys.FirstOrDefaultAsync(x =>
            x.Purpose == SurveyPurpose.OfficialSatisfaction && x.SeasonId == seasonId);

    /// <summary>
    /// The satisfaction figure for one season, native survey first and approved club submissions as
    /// the fallback.
    /// </summary>
    public static async Task<SatisfactionResult> ForSeasonAsync(AppDbContext db, int? seasonId)
    {
        if (seasonId is null) return await ClubSubmittedAsync(db, null);

        var native = await FromOfficialSurveyAsync(db, seasonId.Value);
        if (native.Source == SatisfactionSource.NativeOfficialSurvey) return native;

        // Not enough native responses yet. Fall back, but carry the response count forward so the
        // caller can still say how close the season is to switching over.
        var fallback = await ClubSubmittedAsync(db, seasonId);
        return fallback with { ResponseCount = native.ResponseCount, SurveyId = native.SurveyId };
    }

    /// <summary>
    /// The native figure alone, with no fallback. Returns <see cref="SatisfactionSource.None"/> when
    /// there is no official survey, no rating answers, or fewer than <see cref="MinimumResponses"/>
    /// responses — with the counts filled in either way.
    /// </summary>
    public static async Task<SatisfactionResult> FromOfficialSurveyAsync(AppDbContext db, int seasonId, int? organizationId = null)
    {
        var survey = await OfficialSurveyAsync(db, seasonId);
        if (survey is null) return SatisfactionResult.NoData;

        var responses = db.SurveyResponses.Where(x => x.SurveyId == survey.Id);
        if (organizationId.HasValue) responses = responses.Where(x => x.OrganizationId == organizationId.Value);

        var responseCount = await responses.CountAsync();

        var stars = await db.SurveyAnswers
            .Where(a => a.StarsValue != null
                        && a.SurveyQuestion != null
                        && a.SurveyQuestion.SurveyId == survey.Id
                        && a.SurveyQuestion.QuestionType == SurveyQuestionType.Stars
                        && responses.Any(r => r.Id == a.SurveyResponseId))
            .Select(a => (int)a.StarsValue!)
            .ToListAsync();

        if (responseCount < MinimumResponses || stars.Count == 0)
            return new SatisfactionResult(null, SatisfactionSource.None, responseCount, stars.Count, survey.Id);

        var percent = Math.Round((decimal)stars.Average() / 5m * 100m, 1);
        return new SatisfactionResult(percent, SatisfactionSource.NativeOfficialSurvey, responseCount, stars.Count, survey.Id);
    }

    /// <summary>The approved club-submitted average, unchanged from how it has always been computed.</summary>
    public static async Task<SatisfactionResult> ClubSubmittedAsync(AppDbContext db, int? seasonId)
    {
        var q = db.KpiSubmissions.Where(x => x.Status == KpiSubmissionStatus.Approved);
        if (seasonId.HasValue) q = q.Where(x => x.SeasonId == seasonId.Value);

        var rates = await q.Select(x => x.SatisfactionRate).ToListAsync();
        if (rates.Count == 0) return SatisfactionResult.NoData;

        return new SatisfactionResult(
            Math.Round(rates.Average(), 1), SatisfactionSource.ClubSubmittedKpi, 0, 0, null);
    }

    /// <summary>
    /// The satisfaction percentage for an already-loaded set of star ratings. Used by the results
    /// screens, which have the answers in hand and must not produce a second, differently-rounded
    /// version of the same number.
    /// </summary>
    public static decimal? PercentFromStars(IReadOnlyCollection<int> stars)
        => stars.Count == 0 ? null : Math.Round((decimal)stars.Average() / 5m * 100m, 1);
}
