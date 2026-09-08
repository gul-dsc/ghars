using GharsPlatform.Models.Core;
using System.Globalization;
using System.Linq.Expressions;

namespace GharsPlatform.Helpers;

/// <summary>
/// Ghars Channel (قناة غرس) vocabulary, moderation rules and — most importantly — the one definition of
/// what the public may see.
///
/// The channel carries two content streams with deliberately different moderation, because forcing
/// them into one workflow would break the club Agenda flow that already works:
///
///   A. CLUB ACTIVITY MEDIA — uploaded through an Agenda entry, published on arrival
///      (ApprovalStatus == null, IsPublished == true). DSC can hide it. No submission workflow.
///
///   B. PARTNER / GOVERNMENT CONTENT — submitted for DSC approval:
///      Draft ──submit──▶ SubmittedForApproval ──approve──▶ Approved ──unpublish──▶ Unpublished
///        ▲                    │        │                                              │
///        └────────edit────────┴─ return / reject ──────────────────────────────────────┘
///      A partner row is created unpublished; ONLY a DSC approval sets IsPublished.
/// </summary>
public static class ChannelWorkflow
{
    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public static string ChannelName => IsAr ? "قناة غرس" : "Ghars Channel";

    // ------------------------------------------------------------------ visibility
    //
    // THE rule. Both halves must agree, exactly as the partner offering catalogue requires Published
    // AND Approved. Expressed once as an expression tree so every query composes the same predicate and
    // no surface can implement half of it.

    /// <summary>
    /// What anyone other than the owning organization and DSC may see: a published item that is either
    /// outside the partner workflow (club media, DSC uploads) or has been approved by DSC.
    /// </summary>
    public static Expression<Func<GalleryItem, bool>> PubliclyVisible =>
        x => x.IsPublished && (x.ApprovalStatus == null || x.ApprovalStatus == ChannelApprovalStatus.Approved);

    /// <summary>The same rule for a materialised row — used by the file-serving endpoint.</summary>
    public static bool IsPubliclyVisible(GalleryItem x)
        => x.IsPublished && x.ApprovalStatus is null or ChannelApprovalStatus.Approved;

    // ------------------------------------------------------------------ classification

    /// <summary>
    /// The category to display for an item. Rows that predate the channel carry no stored category, so
    /// they are classified from the fields they do have rather than being back-filled with a guess.
    /// </summary>
    public static ChannelCategory ResolveCategory(GalleryItem x)
    {
        if (x.ChannelCategory.HasValue) return x.ChannelCategory.Value;
        if (x.AgendaEntryId.HasValue) return Models.Core.ChannelCategory.ClubActivity;
        if (x.MediaType is GalleryMediaType.PressCoverage or GalleryMediaType.NewspaperCoverage)
            return Models.Core.ChannelCategory.Press;
        return Models.Core.ChannelCategory.Official;
    }

    public static string CategoryLabel(ChannelCategory c) => c switch
    {
        Models.Core.ChannelCategory.ClubActivity => IsAr ? "أنشطة الأندية" : "Club activities",
        Models.Core.ChannelCategory.Awareness => IsAr ? "محتوى توعوي" : "Awareness",
        Models.Core.ChannelCategory.Educational => IsAr ? "محتوى تعليمي" : "Educational",
        Models.Core.ChannelCategory.Official => IsAr ? "محتوى رسمي" : "Official",
        Models.Core.ChannelCategory.Press => IsAr ? "تغطية صحفية" : "Press",
        _ => c.ToString()
    };

    public static string CategoryIcon(ChannelCategory c) => c switch
    {
        Models.Core.ChannelCategory.ClubActivity => "bi-trophy",
        Models.Core.ChannelCategory.Awareness => "bi-megaphone",
        Models.Core.ChannelCategory.Educational => "bi-mortarboard",
        Models.Core.ChannelCategory.Official => "bi-patch-check",
        Models.Core.ChannelCategory.Press => "bi-newspaper",
        _ => "bi-collection"
    };

    /// <summary>
    /// Who produced the item, for the source badge. Derived from data already on the row; internal ids
    /// are never exposed — the badge names the kind of contributor and the card names the organization.
    /// </summary>
    public static string SourceLabel(GalleryItem x, OrganizationType? ownerType)
    {
        if (x.AgendaEntryId.HasValue) return IsAr ? "نادٍ" : "Club";
        if (x.ApprovalStatus.HasValue)
        {
            return ownerType == OrganizationType.GovernmentAuthority
                ? (IsAr ? "جهة حكومية" : "Government entity")
                : (IsAr ? "جهة منفذة" : "Implementing entity");
        }
        if (x.MediaType is GalleryMediaType.PressCoverage or GalleryMediaType.NewspaperCoverage)
            return IsAr ? "تغطية صحفية" : "Press";
        return IsAr ? "مجلس دبي الرياضي" : "Dubai Sports Council";
    }

    public static string SourceBadgeClass(GalleryItem x)
    {
        if (x.AgendaEntryId.HasValue) return "text-bg-success";
        if (x.ApprovalStatus.HasValue) return "text-bg-info";
        if (x.MediaType is GalleryMediaType.PressCoverage or GalleryMediaType.NewspaperCoverage) return "text-bg-warning";
        return "text-bg-primary";
    }

    public static bool IsVideo(GalleryItem x)
        => x.MediaType is GalleryMediaType.Video or GalleryMediaType.OfficialVideo;

    // ------------------------------------------------------------------ partner review lifecycle

    public static string StatusLabel(ChannelApprovalStatus? s) => s switch
    {
        ChannelApprovalStatus.Draft => IsAr ? "مسودة" : "Draft",
        ChannelApprovalStatus.SubmittedForApproval => IsAr ? "قيد المراجعة" : "Under review",
        ChannelApprovalStatus.ReturnedForCorrection => IsAr ? "أُعيد للتعديل" : "Returned for correction",
        ChannelApprovalStatus.Approved => IsAr ? "معتمد ومنشور" : "Approved and published",
        ChannelApprovalStatus.Rejected => IsAr ? "مرفوض" : "Rejected",
        ChannelApprovalStatus.Unpublished => IsAr ? "مسحوب" : "Withdrawn",
        _ => IsAr ? "غير خاضع للمراجعة" : "Not in review workflow"
    };

    public static string StatusBadgeClass(ChannelApprovalStatus? s) => s switch
    {
        ChannelApprovalStatus.Approved => "text-bg-success",
        ChannelApprovalStatus.SubmittedForApproval => "text-bg-primary",
        ChannelApprovalStatus.ReturnedForCorrection => "text-bg-warning",
        ChannelApprovalStatus.Rejected => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    public static string StatusIcon(ChannelApprovalStatus? s) => s switch
    {
        ChannelApprovalStatus.Draft => "bi-pencil",
        ChannelApprovalStatus.SubmittedForApproval => "bi-hourglass-split",
        ChannelApprovalStatus.ReturnedForCorrection => "bi-arrow-counterclockwise",
        ChannelApprovalStatus.Approved => "bi-check-circle",
        ChannelApprovalStatus.Rejected => "bi-x-circle",
        ChannelApprovalStatus.Unpublished => "bi-eye-slash",
        _ => "bi-dash-circle"
    };

    public static string Explain(ChannelApprovalStatus? s) => s switch
    {
        ChannelApprovalStatus.Draft => IsAr
            ? "المحتوى غير مرئي في القناة. أرسلوه لاعتماد مجلس دبي الرياضي ليصبح متاحاً."
            : "This content is not visible in the channel. Submit it for DSC approval to publish it.",
        ChannelApprovalStatus.SubmittedForApproval => IsAr
            ? "المحتوى قيد مراجعة مجلس دبي الرياضي ولا يمكن تعديله حتى صدور القرار."
            : "This content is under DSC review and cannot be edited until a decision is made.",
        ChannelApprovalStatus.ReturnedForCorrection => IsAr
            ? "أعاد المراجع المحتوى للتعديل. يرجى مراجعة الملاحظات ثم إعادة الإرسال."
            : "The reviewer returned this content for correction. Address the notes, then resubmit.",
        ChannelApprovalStatus.Approved => IsAr
            ? "المحتوى معتمد ومنشور في القناة. لتعديله يجب سحبه أولاً ثم إعادة إرساله للاعتماد."
            : "This content is approved and published in the channel. To change it, withdraw it first, then submit it for approval again.",
        ChannelApprovalStatus.Rejected => IsAr
            ? "رُفض المحتوى. يمكن تعديله وإعادة إرساله بعد معالجة ملاحظات المراجع."
            : "This content was rejected. You may edit it and resubmit once the reviewer's notes are addressed.",
        ChannelApprovalStatus.Unpublished => IsAr
            ? "المحتوى مسحوب من القناة. عدّلوه ثم أعيدوا إرساله للاعتماد لإعادة إتاحته."
            : "This content is withdrawn from the channel. Edit it and submit it for approval again to restore it.",
        _ => ""
    };

    /// <summary>Approved content is locked: editing it in place would publish unreviewed material.</summary>
    public static bool PartnerCanEdit(ChannelApprovalStatus? s) => s is
        ChannelApprovalStatus.Draft or
        ChannelApprovalStatus.ReturnedForCorrection or
        ChannelApprovalStatus.Rejected or
        ChannelApprovalStatus.Unpublished;

    public static bool PartnerCanSubmit(ChannelApprovalStatus? s) => PartnerCanEdit(s);

    /// <summary>Only approved (therefore visible) content can be withdrawn by its owner.</summary>
    public static bool PartnerCanWithdraw(ChannelApprovalStatus? s) => s == ChannelApprovalStatus.Approved;

    public static bool DscCanReview(ChannelApprovalStatus? s) => s == ChannelApprovalStatus.SubmittedForApproval;

    public static string EditLockMessage(ChannelApprovalStatus? s) => s switch
    {
        ChannelApprovalStatus.SubmittedForApproval or ChannelApprovalStatus.Approved => Explain(s),
        _ => IsAr ? "لا يمكن تعديل هذا المحتوى في حالته الحالية." : "This content cannot be edited in its current state."
    };

    /// <summary>
    /// Media types a partner may choose. Press and official types are DSC vocabulary and are
    /// deliberately absent: a partner labelling its own upload "official photo" would misrepresent it.
    /// </summary>
    public static readonly GalleryMediaType[] PartnerMediaTypes =
        { GalleryMediaType.Photo, GalleryMediaType.Video };

    /// <summary>Categories a partner may choose. Club activity and press are not theirs to claim.</summary>
    public static readonly ChannelCategory[] PartnerCategories =
        { Models.Core.ChannelCategory.Awareness, Models.Core.ChannelCategory.Educational };

    public static string? AuditActionLabel(string action) => action switch
    {
        "ChannelContentDraftCreated" => IsAr ? "أُنشئت المسودة" : "Draft created",
        "ChannelContentUpdated" => IsAr ? "تم تعديل المحتوى" : "Content edited",
        "ChannelContentSubmitted" => IsAr ? "أُرسل للاعتماد" : "Submitted for approval",
        "ChannelContentResubmitted" => IsAr ? "أُعيد الإرسال للاعتماد" : "Resubmitted for approval",
        "ChannelContentApproved" => IsAr ? "تم الاعتماد والنشر" : "Approved and published",
        "ChannelContentReturnedForCorrection" => IsAr ? "أُعيد للتعديل" : "Returned for correction",
        "ChannelContentRejected" => IsAr ? "تم الرفض" : "Rejected",
        "ChannelContentWithdrawnByPartner" => IsAr ? "سُحب من قبل الجهة" : "Withdrawn by the entity",
        "ChannelContentHiddenByDsc" => IsAr ? "أُخفي من قبل المجلس" : "Hidden by DSC",
        "ChannelContentPublishedByDsc" => IsAr ? "أُعيد نشره من قبل المجلس" : "Republished by DSC",
        "ChannelClubMediaAdded" => IsAr ? "أضاف النادي وسائط" : "Club media added",
        _ => null
    };
}
