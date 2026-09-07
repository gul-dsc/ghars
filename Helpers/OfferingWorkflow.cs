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
