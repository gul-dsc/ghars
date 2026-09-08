using GharsPlatform.Models.Core;
using System.Globalization;

namespace GharsPlatform.Helpers;

/// <summary>
/// The DSC review lifecycle for partner offerings, in one place so the controllers that enforce it
/// and the views that render it cannot drift apart.
///
///   Draft ──submit──▶ SubmittedForApproval ──approve──▶ Approved ──unpublish──▶ Unpublished
///     ▲                    │        │                                              │
///     │                    │        └──reject──▶ Rejected ──edit──┐                │
///     └────────edit────────┴──return──▶ ReturnedForCorrection ────┴──resubmit──────┘
///
/// A partner can never reach <see cref="OfferingApprovalStatus.Approved"/> on their own: approval is
/// the only transition that also sets <see cref="ActivityStatus.Published"/>, and only DSC can make
/// it.
/// </summary>
public static class OfferingWorkflow
{
    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public static string Label(OfferingApprovalStatus? s) => s switch
    {
        OfferingApprovalStatus.Draft => IsAr ? "مسودة" : "Draft",
        OfferingApprovalStatus.SubmittedForApproval => IsAr ? "قيد المراجعة" : "Submitted for approval",
        OfferingApprovalStatus.ReturnedForCorrection => IsAr ? "أُعيد للتعديل" : "Returned for correction",
        OfferingApprovalStatus.Approved => IsAr ? "معتمد" : "Approved",
        OfferingApprovalStatus.Rejected => IsAr ? "مرفوض" : "Rejected",
        OfferingApprovalStatus.Unpublished => IsAr ? "غير منشور" : "Unpublished",
        // DSC-created activities carry no approval state; they are not part of this workflow.
        _ => IsAr ? "غير خاضع للمراجعة" : "Not in review workflow"
    };

    public static string BadgeClass(OfferingApprovalStatus? s) => s switch
    {
        OfferingApprovalStatus.Approved => "text-bg-success",
        OfferingApprovalStatus.SubmittedForApproval => "text-bg-primary",
        OfferingApprovalStatus.ReturnedForCorrection => "text-bg-warning",
        OfferingApprovalStatus.Rejected => "text-bg-danger",
        OfferingApprovalStatus.Unpublished => "text-bg-secondary",
        OfferingApprovalStatus.Draft => "text-bg-secondary",
        _ => "text-bg-light"
    };

    /// <summary>
    /// A Bootstrap icon per state, so the badge never conveys its meaning by colour alone. Paired
    /// with <see cref="Label"/>, which always carries the text.
    /// </summary>
    public static string Icon(OfferingApprovalStatus? s) => s switch
    {
        OfferingApprovalStatus.Draft => "bi-pencil",
        OfferingApprovalStatus.SubmittedForApproval => "bi-hourglass-split",
        OfferingApprovalStatus.ReturnedForCorrection => "bi-arrow-counterclockwise",
        OfferingApprovalStatus.Approved => "bi-check-circle",
        OfferingApprovalStatus.Rejected => "bi-x-circle",
        OfferingApprovalStatus.Unpublished => "bi-eye-slash",
        _ => "bi-dash-circle"
    };

    /// <summary>
    /// What this state means for the partner, in one sentence. Shown wherever an action the partner
    /// might expect is absent, so an intentional lock does not read as a broken screen.
    /// </summary>
    public static string Explain(OfferingApprovalStatus? s) => s switch
    {
        OfferingApprovalStatus.Draft => IsAr
            ? "البرنامج غير مرئي للأندية. أرسلوه لاعتماد مجلس دبي الرياضي ليصبح متاحاً للحجز."
            : "This program is not visible to clubs. Submit it for DSC approval to make it bookable.",
        OfferingApprovalStatus.SubmittedForApproval => IsAr
            ? "البرنامج قيد مراجعة مجلس دبي الرياضي ولا يمكن تعديله حتى صدور القرار."
            : "This program is under DSC review and cannot be edited until a decision is made.",
        OfferingApprovalStatus.ReturnedForCorrection => IsAr
            ? "أعاد المراجع البرنامج للتعديل. يرجى مراجعة الملاحظات أدناه ثم إعادة الإرسال."
            : "The reviewer returned this program for correction. Address the notes below, then resubmit.",
        // The rule §5 asks to be made explicit, in the exact words used across every surface.
        OfferingApprovalStatus.Approved => IsAr
            ? "لتعديل برنامج معتمد، يجب إلغاء نشره أولاً. وبعد التعديل يلزم إعادة إرساله إلى مجلس دبي الرياضي للاعتماد."
            : "To change an approved program, unpublish it first. After editing, it must be submitted to DSC for approval again.",
        OfferingApprovalStatus.Rejected => IsAr
            ? "رُفض البرنامج. يمكنكم تعديله وإعادة إرساله بعد معالجة ملاحظات المراجع."
            : "This program was rejected. You may edit it and resubmit once the reviewer's notes are addressed.",
        OfferingApprovalStatus.Unpublished => IsAr
            ? "البرنامج مسحوب من كتالوج الأندية. عدّلوه ثم أعيدوا إرساله للاعتماد لإعادة إتاحته."
            : "This program is withdrawn from the club catalogue. Edit it and submit it for approval again to restore it.",
        _ => IsAr ? "هذا النشاط ليس ضمن سير اعتماد الجهات المنفذة." : "This activity is outside the implementing-entity approval workflow."
    };

    /// <summary>
    /// Why an edit was refused. Shared by the partner controller (as a toast after a blocked GET or
    /// POST) and by the list and details views (as inline guidance), so the two cannot disagree
    /// about what the rule is.
    /// </summary>
    public static string EditLockMessage(OfferingApprovalStatus? s) => s switch
    {
        OfferingApprovalStatus.SubmittedForApproval or OfferingApprovalStatus.Approved => Explain(s),
        _ => IsAr ? "لا يمكن تعديل هذا البرنامج في حالته الحالية." : "This program cannot be edited in its current state."
    };

    /// <summary>
    /// Renders one recorded workflow transition for a human. The audit table stores an action name
    /// and a JSON payload; only the action is surfaced — the payload holds internal field values and
    /// is never shown to a normal user.
    /// </summary>
    public static string? AuditActionLabel(string action) => action switch
    {
        "OfferingDraftCreated" => IsAr ? "أُنشئت المسودة" : "Draft created",
        "OfferingUpdated" => IsAr ? "تم تعديل البرنامج" : "Program edited",
        "OfferingAttachmentsAdded" => IsAr ? "أُضيفت مستندات" : "Documents attached",
        "OfferingAttachmentsRemoved" => IsAr ? "أُزيلت مستندات" : "Documents removed",
        "OfferingSubmitted" => IsAr ? "أُرسل للاعتماد" : "Submitted for approval",
        "OfferingResubmitted" => IsAr ? "أُعيد الإرسال للاعتماد" : "Resubmitted for approval",
        "OfferingReturnedForCorrection" => IsAr ? "أُعيد للتعديل" : "Returned for correction",
        "OfferingApproved" => IsAr ? "تم الاعتماد" : "Approved",
        "OfferingRejected" => IsAr ? "تم الرفض" : "Rejected",
        "OfferingUnpublishedByPartner" => IsAr ? "أُلغي النشر من قبل الجهة" : "Unpublished by the entity",
        "OfferingUnpublishedByDsc" => IsAr ? "أُلغي النشر من قبل المجلس" : "Unpublished by DSC",
        "Publish" => IsAr ? "تم النشر" : "Published",
        // Anything else in the audit table belongs to a different workflow and is not this timeline's
        // to narrate. Callers drop nulls rather than printing a raw action name.
        _ => null
    };

    /// <summary>
    /// May the partner change the content? Approved offerings are locked on purpose: editing
    /// approved content in place would put unreviewed text in front of clubs. The partner unpublishes
    /// first, edits, then resubmits.
    /// </summary>
    public static bool PartnerCanEdit(OfferingApprovalStatus? s) => s is
        OfferingApprovalStatus.Draft or
        OfferingApprovalStatus.ReturnedForCorrection or
        OfferingApprovalStatus.Rejected or
        OfferingApprovalStatus.Unpublished;

    /// <summary>May the partner send it to DSC? Anything editable, plus nothing already in review.</summary>
    public static bool PartnerCanSubmit(OfferingApprovalStatus? s) => PartnerCanEdit(s);

    /// <summary>Only an approved (therefore publicly visible) offering can be withdrawn.</summary>
    public static bool CanUnpublish(OfferingApprovalStatus? s) => s == OfferingApprovalStatus.Approved;

    /// <summary>DSC may act only on something actually awaiting review.</summary>
    public static bool DscCanReview(OfferingApprovalStatus? s) => s == OfferingApprovalStatus.SubmittedForApproval;

    /// <summary>Whether this row is a partner offering at all, as opposed to a DSC-created activity.</summary>
    public static bool IsPartnerOffering(Activity a) => a.PartnerOrganizationId.HasValue;
}

/// <summary>
/// One recorded transition, projected out of <c>SystemAuditLog</c> for display. A named type rather
/// than an anonymous one because it crosses into a Razor view, and it carries the action name and
/// timestamp only — never the stored JSON payload.
/// </summary>
public sealed record OfferingHistoryEntry(string Action, DateTime AtUtc);
