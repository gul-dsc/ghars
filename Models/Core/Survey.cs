using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// A Ghars survey. One engine serves both an ordinary activity survey and the official participant
/// satisfaction survey; <see cref="Purpose"/> is what tells them apart.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActivityId"/> and <see cref="SeasonId"/> are both optional because the two kinds anchor
/// differently: an activity survey belongs to one activity, while the official satisfaction survey
/// belongs to a whole season and is reused across every activity delivered in it.
/// </para>
/// <para>
/// The survey is completed inside Ghars. There is no external URL here by design — see
/// <see cref="ExternalSurvey"/>, which is retained for historical records only.
/// </para>
/// </remarks>
public class Survey : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>
    /// The activity this survey belongs to, when it belongs to one. Null for the official
    /// satisfaction survey, which is season-wide.
    /// </summary>
    public int? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>
    /// The sports season this survey reports against. Required in practice for
    /// <see cref="SurveyPurpose.OfficialSatisfaction"/>, and the axis every satisfaction figure is
    /// grouped by. Nullable so existing activity surveys are not invented a season they never had.
    /// </summary>
    public int? SeasonId { get; set; }
    public Season? Season { get; set; }

    public SurveyPurpose Purpose { get; set; } = SurveyPurpose.General;

    [Required, MaxLength(200)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(200)]
    public string TitleAr { get; set; } = "";

    [MaxLength(2000)]
    public string? DescriptionEn { get; set; }

    [MaxLength(2000)]
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Opaque token for the shareable participant link (<c>/surveys/take/{token}</c>) and its QR code.
    /// </summary>
    /// <remarks>
    /// The token identifies the survey without exposing a guessable sequential id, and it is what a
    /// club prints on a QR code after a lecture. It grants no privilege beyond opening a survey that
    /// is already open to the public, so it is a convenience, not a credential — the open/closed
    /// check still runs on every request.
    /// </remarks>
    [MaxLength(64)]
    public string? PublicToken { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? OpensAtUtc { get; set; }
    public DateTime? ClosesAtUtc { get; set; }

    public ICollection<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();

    /// <summary>Whether the survey accepts responses at <paramref name="nowUtc"/>.</summary>
    public bool IsOpenAt(DateTime nowUtc)
        => IsActive
           && (OpensAtUtc is null || OpensAtUtc <= nowUtc)
           && (ClosesAtUtc is null || ClosesAtUtc >= nowUtc);
}
