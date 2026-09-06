using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// Official external survey (developed and analyzed by Dubai Digital Authority).
/// DSC staff maintain the survey link; participants open it externally; the analyzed
/// PDF report is uploaded afterwards and published alongside the link.
/// Coexists with the internal Ghars survey engine (Survey/SurveyQuestion/...).
/// </summary>
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

    // Analyzed results report (PDF) uploaded by DSC after Dubai Digital Authority analysis.
    [MaxLength(500)]
    public string? ReportPdfPath { get; set; }

    public bool IsReportPublished { get; set; }

    public DateTime? ReportPublishedAtUtc { get; set; }
}
