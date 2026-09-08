using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// One participant's submission. Stored entirely inside Ghars — no external result import exists or
/// is needed.
/// </summary>
public class SurveyResponse
{
    public int Id { get; set; }

    public int SurveyId { get; set; }
    public Survey? Survey { get; set; }

    /// <summary>
    /// The signed-in participant, when there is one. Null for an anonymous submission through the
    /// shareable link.
    /// </summary>
    /// <remarks>
    /// Participants in a club lecture are mostly young players with no Ghars account, so requiring
    /// sign-in would collect almost nothing. Anonymity here is deliberate: nothing identifying is
    /// stored, and the uniqueness index that prevents a signed-in participant answering twice is
    /// filtered to <c>UserId IS NOT NULL</c> so it cannot also cap anonymous responses at one.
    /// </remarks>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>
    /// The club this response belongs to, when the link carried that context. Server-derived from the
    /// agenda entry — never accepted from the request — so a club cannot attribute responses to
    /// another club.
    /// </summary>
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>
    /// The delivered agenda activity this response is feedback on, when the club distributed a
    /// context-specific link. Optional: a season-wide response with no activity context is valid.
    /// </summary>
    public int? AgendaEntryId { get; set; }
    public AgendaEntry? AgendaEntry { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<SurveyAnswer> Answers { get; set; } = new List<SurveyAnswer>();
}
