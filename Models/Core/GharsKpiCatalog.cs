namespace GharsPlatform.Models.Core;

/// <summary>
/// Central registry of the approved Ghars KPI definitions (docs: "Key Performance Indicators (KPIs).docx"
/// and "Data Entry Reports &amp; Statistics.docx"). Dashboards, reports and data-entry screens must read
/// targets/names/formulas from here instead of hardcoding them (2026 = baseline year, 2033 = target year).
/// </summary>
public enum KpiSource : byte
{
    SystemDerived = 1,
    ClubSubmitted = 2,

    /// <summary>
    /// LEGACY. An instrument run and analysed outside Ghars. No indicator uses this any more; it is
    /// kept so historical records and any stored value still resolve to a label.
    /// </summary>
    ExternalApproved = 3,

    /// <summary>
    /// Computed from responses to the official Ghars participant satisfaction survey, collected inside
    /// the platform. Falls back to the approved club-submitted average until a season has enough
    /// responses — see <see cref="Helpers.SatisfactionCalculator"/>.
    /// </summary>
    OfficialSurvey = 4
}

public sealed record KpiDefinition(
    string Key,
    string NameEn,
    string NameAr,
    string Unit,
    decimal? Target,
    string TargetText,
    // true  => actual must be >= target to be achieved
    // false => actual must be <= target to be achieved
    bool HigherIsBetter,
    KpiSource Source,
    string CalculationEn,
    string CalculationAr);

public static class GharsKpiCatalog
{
    public const int BaselineYear = 2026;
    public const int TargetYear = 2033;

    public static readonly KpiDefinition ProgramCoverage = new(
        "ProgramCoverage", "Program Coverage", "تغطية البرنامج", "%", 80m, "≥ 80%", true,
        KpiSource.SystemDerived,
        "Clubs covered by approved Ghars data ÷ total clubs × 100",
        "الأندية المشمولة ببيانات غرس المعتمدة ÷ إجمالي الأندية × 100");

    public static readonly KpiDefinition PlayerParticipation = new(
        "PlayerParticipation", "Player Participation Rate", "نسبة مشاركة اللاعبين", "%", 60m, "≥ 60%", true,
        KpiSource.ClubSubmitted,
        "Participating players ÷ total registered players × 100",
        "عدد اللاعبين المشاركين ÷ إجمالي اللاعبين المسجلين × 100");

    public static readonly KpiDefinition AttendanceRate = new(
        "AttendanceRate", "Attendance Rate", "نسبة الحضور", "%", 80m, "≥ 80%", true,
        KpiSource.ClubSubmitted,
        "Participating players ÷ total registered players in the academy/sector × 100",
        "عدد اللاعبين المشاركين ÷ إجمالي اللاعبين المسجلين في الأكاديمية/القطاع × 100");

    public static readonly KpiDefinition ViolationsReduction = new(
        "ViolationsReduction", "Reduction in Violations & Disciplinary Actions", "خفض المخالفات والعقوبات", "%", 15m, "≥ 15% annual reduction", true,
        KpiSource.ClubSubmitted,
        "((Previous season violations − current season violations) ÷ previous season violations) × 100",
        "((مخالفات الموسم السابق − مخالفات الموسم الحالي) ÷ مخالفات الموسم السابق) × 100");

    public static readonly KpiDefinition EthicalValues = new(
        "EthicalValues", "Ethical Values Adherence", "الالتزام بالقيم الأخلاقية", "%", 90m, "≥ 90%", true,
        KpiSource.ClubSubmitted,
        "Players demonstrating adherence to ethical values and positive conduct ÷ total players × 100",
        "اللاعبون الملتزمون بالقيم الأخلاقية والسلوك الإيجابي ÷ إجمالي اللاعبين × 100");

    public static readonly KpiDefinition PhysicalActivity = new(
        "PhysicalActivity", "Physical Activity ≥ 150 min/week", "النشاط البدني ≥ 150 دقيقة أسبوعياً", "%", 90m, "≥ 90% of players", true,
        KpiSource.ClubSubmitted,
        "Players achieving ≥ 150 minutes of physical activity per week ÷ total players × 100",
        "اللاعبون المحققون 150 دقيقة نشاط بدني أسبوعياً فأكثر ÷ إجمالي اللاعبين × 100");

    public static readonly KpiDefinition LifestyleDiseaseFree = new(
        "LifestyleDiseaseFree", "Players Free from Lifestyle Diseases", "الخلو من أمراض نمط الحياة", "%", 95m, "≥ 95%", true,
        KpiSource.ClubSubmitted,
        "Players without lifestyle conditions (diabetes/heart/hypertension/vision/other) ÷ total players × 100 (club medical reports, aggregated)",
        "اللاعبون غير المصابين (سكري/قلب/ضغط/نظر/أخرى) ÷ إجمالي اللاعبين × 100 (وفق تقارير طبيب النادي، بشكل مجمّع)");

    public static readonly KpiDefinition HealthyDiet = new(
        "HealthyDiet", "Healthy Dietary Habits", "العادات الغذائية الصحية", "%", 80m, "≥ 80%", true,
        KpiSource.ClubSubmitted,
        "Players adhering to healthy dietary habits ÷ total players × 100",
        "اللاعبون الملتزمون بعادات غذائية سليمة ÷ إجمالي اللاعبين × 100");

    public static readonly KpiDefinition CommunityEvents = new(
        "CommunityEvents", "Community Events", "الفعاليات المجتمعية", "No.", 5m, "5–10 per club annually", true,
        KpiSource.ClubSubmitted,
        "Community events organized by the club during the season (target range 5–10)",
        "الفعاليات المجتمعية المنظمة من النادي خلال الموسم (النطاق المستهدف 5–10)");

    public static readonly KpiDefinition Satisfaction = new(
        "Satisfaction", "Participant Satisfaction / Happiness", "رضا وسعادة المشاركين", "%", 85m, "≥ 85%", true,
        KpiSource.OfficialSurvey,
        "Mean of the 1-5 ratings in the official Ghars participant satisfaction survey ÷ 5 × 100",
        "متوسط التقييمات من ١ إلى ٥ في استبيان رضا المشاركين الرسمي لغرس ÷ ٥ × ١٠٠");

    public static readonly IReadOnlyList<KpiDefinition> All = new[]
    {
        ProgramCoverage, PlayerParticipation, AttendanceRate, ViolationsReduction, EthicalValues,
        PhysicalActivity, LifestyleDiseaseFree, HealthyDiet, CommunityEvents, Satisfaction
    };

    public static KpiDefinition? Find(string key) => All.FirstOrDefault(x => x.Key == key);

    /// <summary>
    /// Bilingual provenance label for an indicator. The <see cref="KpiDefinition.CalculationEn"/> text is
    /// the approved *definition* of an indicator, not a promise that the platform computes it — screens
    /// must show this label alongside it so a club-entered figure is never read as system-calculated.
    /// </summary>
    public static string SourceLabel(KpiSource source, bool ar) => source switch
    {
        KpiSource.SystemDerived => ar ? "محتسب آلياً من بيانات المنصة" : "System Calculated",
        KpiSource.OfficialSurvey => ar ? "الاستبيان الرسمي لرضا المشاركين داخل غرس" : "Official Ghars Satisfaction Survey",
        KpiSource.ExternalApproved => ar ? "مصدر خارجي (سجل تاريخي)" : "External source (historical)",
        _ => ar ? "إدخال النادي / اعتماد مجلس دبي الرياضي" : "Club Submitted / DSC Approved"
    };

    /// <summary>
    /// Aggregated lifestyle-disease-free percentage for a submission. Returns null (No Data) when the
    /// participant denominator is missing/zero — missing data must never be rendered as 0% or 100%.
    /// </summary>
    public static decimal? DiseaseFreePercent(KpiSubmission r)
    {
        if (r.NumberOfParticipants <= 0) return null;
        var affected = r.DiabetesCases + r.HeartConditionCases + r.HypertensionCases + r.OtherLifestyleConditionCases;
        var free = Math.Max(0, r.NumberOfParticipants - affected);
        return Math.Round((decimal)free * 100m / r.NumberOfParticipants, 1);
    }

    /// <summary>
    /// Annual reduction in violations vs the previous period. Null when previous data is missing or zero
    /// (an explicit No-Data state, never a misleading percentage).
    /// </summary>
    public static decimal? ViolationsReductionPercent(int? previousViolations, int currentViolations)
    {
        if (!previousViolations.HasValue || previousViolations.Value <= 0) return null;
        return Math.Round((decimal)(previousViolations.Value - currentViolations) * 100m / previousViolations.Value, 1);
    }
}
