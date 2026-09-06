using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class KpiSubmission : AuditableEntity
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int NumberOfLecturesActivities { get; set; }
    public int NumberOfLecturers { get; set; }
    public int NumberOfImplementingEntities { get; set; }
    public int NumberOfParticipants { get; set; }
    public decimal PlayerParticipationRate { get; set; }
    public int WarningsAndRedCards { get; set; }
    public int WeeklyTrainingMinutes { get; set; }
    // % of players achieving >= 150 minutes of physical activity per week (KPI target >= 90%).
    // Nullable so "not reported" is never rendered as 0% (missing data is not zero).
    public decimal? PhysicalActivityComplianceRate { get; set; }
    public int DiabetesCases { get; set; }
    public int HeartConditionCases { get; set; }
    public int HypertensionCases { get; set; }
    public int OtherLifestyleConditionCases { get; set; }
    public decimal SatisfactionRate { get; set; }

    // Evidence lineage for SatisfactionRate: which Official Program Survey (the external survey run and
    // analysed outside Ghars) supports the approved figure. Optional, so historical rows keep working and
    // are never backfilled by guesswork. This records *provenance only* - it never computes or overwrites
    // SatisfactionRate, and internal Ghars surveys are deliberately not linkable here.
    public int? SatisfactionExternalSurveyId { get; set; }
    public ExternalSurvey? SatisfactionExternalSurvey { get; set; }

    public decimal AttendanceRate { get; set; }
    public decimal EthicalValuesAdherenceRate { get; set; }
    public decimal HealthyDietaryHabitsRate { get; set; }
    public int CommunityEventsCount { get; set; }
    public KpiSubmissionStatus Status { get; set; } = KpiSubmissionStatus.Draft;
    [MaxLength(2000)] public string? ReviewNotes { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(450)] public string? ReviewedByUserId { get; set; }
    public ICollection<KpiDocument> Documents { get; set; } = new List<KpiDocument>();
}
