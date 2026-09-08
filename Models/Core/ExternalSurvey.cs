using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// LEGACY. A survey run on a third-party platform, with its analysed PDF report uploaded afterwards.
/// </summary>
/// <remarks>
/// <para>
/// This was once how the official participant satisfaction survey worked. It is not any more: the
/// official survey is now native, lives in <see cref="Survey"/> with
/// <see cref="SurveyPurpose.OfficialSatisfaction"/>, and is completed inside Ghars.
/// </para>
/// <para>
/// The entity and its rows are kept because they are historical evidence — a report a third party
/// analysed and DSC published is a record of what happened, and deleting it would erase that. It no
/// longer feeds satisfaction reporting, no longer appears to participants, and nothing new should be
/// created here. See <see cref="KpiSubmission.SatisfactionExternalSurveyId"/>, which stays readable
/// for the submissions that cite it.
/// </para>
/// </remarks>
public class ExternalSurvey : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string TitleEn { get; set; } = "";

    [Required, MaxLength(200)]
    public string TitleAr { get; set; } = "";

    [MaxLength(2000)]
    public string? DescriptionEn { get; set; }

    [MaxLength(2000)]
    public string? DescriptionAr { get; set; }

    // Absolute http/https URL only (validated at write time).
    [Required, MaxLength(700)]
    public string ExternalUrl { get; set; } = "";

    public int? SeasonId { get; set; }
    public Season? Season { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }

    // Analysed results report (PDF) uploaded by DSC after the external provider returned its analysis.
    [MaxLength(500)]
    public string? ReportPdfPath { get; set; }

    public bool IsReportPublished { get; set; }

    public DateTime? ReportPublishedAtUtc { get; set; }
}
