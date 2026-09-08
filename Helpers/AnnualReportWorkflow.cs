using GharsPlatform.Models.Core;
using System.Globalization;

namespace GharsPlatform.Helpers;

/// <summary>
/// The Ghars Annual Report lifecycle and its bilingual vocabulary, in one place so the controllers
/// that enforce it and the views that render it cannot drift apart. Follows the same shape as
/// <see cref="OfferingWorkflow"/>, which is the established convention in this codebase.
///
///   Draft ──submit──▶ Submitted ──approve──▶ Approved  (final, read-only, never recomputed)
///     ▲                   │
///     │                   ├──return──▶ ReturnedForCorrection ─┐
///     └──────edit─────────┤                                    ├── club edits and resubmits
///                         └──reject──▶ Rejected ───────────────┘
///
/// The Arabic terms are taken from the source template, <c>docs/Ghars_Clubs Report Form.docx</c>.
/// </summary>
public static class AnnualReportWorkflow
{
    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    // ------------------------------------------------------------------ template vocabulary
    //
    // Held here rather than repeated in each view so the club form, the DSC review screen and the
    // print/PDF output all name the report's sections with the same official words.

    public static string ReportTitle => IsAr ? "التقرير السنوي لغرس" : "Ghars Annual Report";
    public static string ProgramReportTitle => IsAr ? "تقرير برنامج \"غرس\"" : "\"Ghars\" Program Report";
    public static string SportsSeason => IsAr ? "الموسم الرياضي" : "Sports season";
    public static string Section1 => IsAr ? "أولاً: البيانات الأساسية" : "1. Basic information";
    public static string Section2 => IsAr ? "ثانياً: المؤشرات الإجمالية لتنفيذ البرنامج" : "2. Overall program implementation indicators";
    public static string Section3 => IsAr ? "ثالثاً: تفاصيل المحاضرات والبرامج المنفذة" : "3. Details of implemented lectures / programs";
    public static string Section4 => IsAr ? "رابعاً: أبرز النتائج والملاحظات" : "4. Key results and notes";

    public static string ClubName => IsAr ? "اسم النادي" : "Club name";
    public static string Coordinator => IsAr ? "المسؤول عن برنامج \"غرس\"" : "Person responsible for the Ghars Program";
    public static string ContactNumber => IsAr ? "رقم التواصل" : "Contact number";
    public static string ContactEmail => IsAr ? "البريد الإلكتروني" : "Email";

    public static string ClubProposed => IsAr ? "عدد المحاضرات المقترحة من النادي" : "Lectures proposed by the club";
    public static string CouncilProposed => IsAr ? "عدد المحاضرات المقترحة من مجلس دبي الرياضي" : "Lectures proposed by Dubai Sports Council";
    public static string TotalDelivered => IsAr ? "إجمالي المحاضرات المنفذة" : "Total lectures implemented";
    public static string LecturersCount => IsAr ? "عدد المحاضرين" : "Number of lecturers";
    public static string EntitiesCount => IsAr ? "عدد الجهات المنفذة / المشاركة" : "Implementing / participating entities";
    public static string ParticipantsTotal => IsAr ? "إجمالي عدد المشاركين" : "Total number of participants";

    public static string KeyResultsPrompt => IsAr
        ? "أبرز النتائج والأثر المحقق من تنفيذ البرنامج"
        : "Key results and impact achieved from implementing the program";
    public static string ChallengesPrompt => IsAr
        ? "أبرز التحديات التي واجهت النادي في التنفيذ"
        : "Key challenges faced by the club during implementation";
    public static string ProposalsPrompt => IsAr
        ? "مقترحات النادي لتطوير برنامج \"غرس\" خلال الموسم الرياضي"
        : "Club proposals for developing the Ghars Program during the sports season";

    // Section 3 column headings, in template order.
    public static string ColIndex => IsAr ? "م" : "#";
    public static string ColTitle => IsAr ? "عنوان المحاضرة / البرنامج" : "Lecture / program title";
    public static string ColEntity => IsAr ? "الجهة المنفذة" : "Implementing entity";
    public static string ColLecturer => IsAr ? "المحاضر" : "Lecturer";
    public static string ColDate => IsAr ? "التاريخ" : "Date";
    public static string ColAudience => IsAr ? "الفئة المستهدفة" : "Target group";
    public static string ColParticipants => IsAr ? "عدد المشاركين" : "Number of participants";
    public static string ColOrigin => IsAr ? "مصدر الاقتراح" : "Proposed by";

    // ------------------------------------------------------------------ status vocabulary

    public static string Label(AnnualReportStatus s) => s switch
    {
        AnnualReportStatus.Draft => IsAr ? "مسودة" : "Draft",
        AnnualReportStatus.Submitted => IsAr ? "قيد مراجعة المجلس" : "Under DSC review",
        AnnualReportStatus.Approved => IsAr ? "معتمد" : "Approved",
        AnnualReportStatus.Rejected => IsAr ? "مرفوض" : "Rejected",
        AnnualReportStatus.ReturnedForCorrection => IsAr ? "أُعيد للتصحيح" : "Returned for correction",
        _ => s.ToString()
    };

    public static string BadgeClass(AnnualReportStatus s) => s switch
    {
        AnnualReportStatus.Approved => "text-bg-success",
        AnnualReportStatus.Submitted => "text-bg-primary",
        AnnualReportStatus.ReturnedForCorrection => "text-bg-warning",
        AnnualReportStatus.Rejected => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    /// <summary>An icon per state, so a badge never carries its meaning by colour alone.</summary>
    public static string Icon(AnnualReportStatus s) => s switch
    {
        AnnualReportStatus.Draft => "bi-pencil",
        AnnualReportStatus.Submitted => "bi-hourglass-split",
        AnnualReportStatus.ReturnedForCorrection => "bi-arrow-counterclockwise",
        AnnualReportStatus.Approved => "bi-check-circle",
        AnnualReportStatus.Rejected => "bi-x-circle",
        _ => "bi-dash-circle"
    };

    /// <summary>What this state means for the club, shown wherever an expected action is absent.</summary>
    public static string Explain(AnnualReportStatus s) => s switch
    {
        AnnualReportStatus.Draft => IsAr
            ? "لم يُرسل التقرير بعد. تُحدَّث المؤشرات وجدول الأنشطة تلقائياً من الأجندة حتى الإرسال."
            : "This report has not been submitted yet. Its indicators and activity table refresh automatically from the Agenda until it is submitted.",
        AnnualReportStatus.Submitted => IsAr
            ? "التقرير قيد مراجعة مجلس دبي الرياضي ولا يمكن تعديله. القيم المعروضة مجمّدة كما أُرسلت."
            : "This report is under DSC review and cannot be edited. The values shown are frozen exactly as submitted.",
        AnnualReportStatus.ReturnedForCorrection => IsAr
            ? "أعاد المراجع التقرير للتصحيح. يرجى مراجعة الملاحظات ثم إعادة الإرسال."
            : "The reviewer returned this report for correction. Review the notes, then resubmit.",
        AnnualReportStatus.Approved => IsAr
            ? "تقرير معتمد ومحفوظ كسجل رسمي. لا تتغير قيمه إذا عُدِّلت الأجندة لاحقاً."
            : "This report is approved and kept as an official record. Its values do not change if the Agenda is edited later.",
        AnnualReportStatus.Rejected => IsAr
            ? "رُفض التقرير. يمكن تعديله وإعادة إرساله بعد معالجة ملاحظات المراجع."
            : "This report was rejected. It can be edited and resubmitted once the reviewer's notes are addressed.",
        _ => ""
    };

    // ------------------------------------------------------------------ transition rules

    /// <summary>
    /// May the club change the report? Approved and Submitted are locked: an approved report is an
    /// official historical record, and a submitted one is in front of a reviewer.
    /// </summary>
    public static bool ClubCanEdit(AnnualReportStatus s) => s is
        AnnualReportStatus.Draft or
        AnnualReportStatus.ReturnedForCorrection or
        AnnualReportStatus.Rejected;

    /// <summary>Anything editable can be sent to DSC.</summary>
    public static bool ClubCanSubmit(AnnualReportStatus s) => ClubCanEdit(s);

    /// <summary>
    /// Whether the derived indicators may still be refreshed from live Agenda data. False from
    /// submission onwards — that is what makes an approved report stable.
    /// </summary>
    public static bool DerivedValuesAreLive(AnnualReportStatus s) => ClubCanEdit(s);

    /// <summary>DSC may only decide on something actually awaiting review.</summary>
    public static bool DscCanReview(AnnualReportStatus s) => s == AnnualReportStatus.Submitted;

    /// <summary>The three decisions a reviewer may record.</summary>
    public static bool IsReviewDecision(AnnualReportStatus s) => s is
        AnnualReportStatus.Approved or
        AnnualReportStatus.ReturnedForCorrection or
        AnnualReportStatus.Rejected;

    public static string EditLockMessage(AnnualReportStatus s) => s switch
    {
        AnnualReportStatus.Submitted or AnnualReportStatus.Approved => Explain(s),
        _ => IsAr ? "لا يمكن تعديل هذا التقرير في حالته الحالية." : "This report cannot be edited in its current state."
    };

    /// <summary>The target-group cell of section 3, in the reader's language.</summary>
    public static string AudienceLabel(AgendaTargetCategory category, string? other) => category switch
    {
        AgendaTargetCategory.Players => IsAr ? "اللاعبون" : "Players",
        AgendaTargetCategory.Coaches => IsAr ? "المدربون" : "Coaches",
        AgendaTargetCategory.Administrators => IsAr ? "الإداريون" : "Administrators",
        AgendaTargetCategory.Parents => IsAr ? "أولياء الأمور" : "Parents",
        AgendaTargetCategory.Others => string.IsNullOrWhiteSpace(other) ? (IsAr ? "فئات أخرى" : "Others") : other,
        _ => category.ToString()
    };

    /// <summary>Where an activity came from, for the origin column of section 3.</summary>
    public static string OriginLabel(bool councilProposed) => councilProposed
        ? (IsAr ? "مجلس دبي الرياضي" : "Dubai Sports Council")
        : (IsAr ? "النادي" : "The club");

    /// <summary>
    /// The rule behind the two "proposed by" counts, stated to the club rather than applied silently.
    /// </summary>
    public static string OriginRuleExplanation => IsAr
        ? "يُحتسب النشاط ضمن مقترحات المجلس إذا نتج عن حجز من كتالوج البرامج المعتمد من مجلس دبي الرياضي، وضمن مقترحات النادي إذا كان طلب حجز خاصاً كتبه النادي أو نشاطاً سجّله مباشرة في الأجندة. يظهر مصدر كل نشاط في الجدول أدناه."
        : "An activity counts as Council-proposed when it came from a booking against the DSC-approved program catalogue, and as club-proposed when it was a custom booking request the club wrote or an activity the club recorded directly in the Agenda. The origin of every activity is shown in the table below.";

    /// <summary>
    /// The limitation that comes with that rule. Shown alongside it so a historical figure is never
    /// presented as more certain than it is.
    /// </summary>
    public static string OriginHistoricalNote => IsAr
        ? "ملاحظة: الأنشطة المسجّلة قبل ربط الحجوزات بالأجندة لا تحمل حجزاً، وتُحتسب ضمن مقترحات النادي."
        : "Note: activities recorded before bookings were linked to the Agenda carry no booking and are therefore counted as club-proposed.";

    /// <summary>Renders one recorded transition from the audit log for a human reader.</summary>
    public static string? AuditActionLabel(string action) => action switch
    {
        "AnnualReportDraftCreated" => IsAr ? "أُنشئت المسودة" : "Draft created",
        "AnnualReportDraftUpdated" => IsAr ? "حُدِّثت المسودة" : "Draft updated",
        "AnnualReportSubmitted" => IsAr ? "أُرسل للاعتماد" : "Submitted for approval",
        "AnnualReportResubmitted" => IsAr ? "أُعيد الإرسال للاعتماد" : "Resubmitted for approval",
        "AnnualReportApproved" => IsAr ? "تم الاعتماد" : "Approved",
        "AnnualReportReturnedForCorrection" => IsAr ? "أُعيد للتصحيح" : "Returned for correction",
        "AnnualReportRejected" => IsAr ? "تم الرفض" : "Rejected",
        _ => null
    };
}
