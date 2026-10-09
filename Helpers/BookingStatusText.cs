using GharsPlatform.Models.Core;
using System.Globalization;

namespace GharsPlatform.Helpers;

/// <summary>
/// Display labels for <see cref="BookingStatus"/>, in one place so the club dashboard, the entity
/// dashboard, booking details and the admin screens cannot describe the same status differently.
///
/// These are labels only. No enum value, database value or transition is renamed. The rule they
/// follow: never call a booking confirmed or approved while the implementing entity's response is
/// still outstanding, and never tell the entity that it is waiting for itself.
///
/// <see cref="BookingStatus.Pending"/> and <see cref="BookingStatus.PendingPartnerApproval"/> are
/// the same value, so lists built from the enum must use <see cref="Distinct"/>, not
/// <c>Enum.GetValues</c>, or the label appears twice.
/// </summary>
public static class BookingStatusText
{
    /// <summary>Who is reading the label. The same status can need a different call to action.</summary>
    public enum Viewer { Neutral, Club, Entity }

    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    private static string T(string en, string ar) => IsAr ? ar : en;

    /// <summary>Every status once, in workflow order.</summary>
    public static readonly IReadOnlyList<BookingStatus> Distinct = new[]
    {
        BookingStatus.Pending,
        BookingStatus.PartnerProposedNewTime,
        BookingStatus.ClubRejectedProposedTimes,
        BookingStatus.Confirmed,
        BookingStatus.Approved,
        BookingStatus.Rejected,
        BookingStatus.Cancelled
    };

    public static string Label(BookingStatus status, Viewer viewer = Viewer.Neutral) => status switch
    {
        BookingStatus.Pending => viewer == Viewer.Entity
            ? T("New request. Your response needed", "طلب جديد بانتظار ردكم")
            : T("Awaiting entity response", "بانتظار رد الجهة المنفذة"),
        BookingStatus.PartnerProposedNewTime => viewer == Viewer.Club
            ? T("New times proposed. Your response needed", "أوقات مقترحة بانتظار ردكم")
            : T("Awaiting club response", "بانتظار رد النادي"),
        BookingStatus.ClubRejectedProposedTimes => viewer == Viewer.Entity
            ? T("Times declined. Send new options", "رُفضت الأوقات — أرسلوا خيارات جديدة")
            : T("Times declined. Awaiting new options", "رُفضت الأوقات بانتظار خيارات جديدة"),
        BookingStatus.Confirmed => T("Confirmed", "مؤكد"),
        BookingStatus.Approved => T("Approved by DSC", "معتمد من المجلس"),
        BookingStatus.Rejected => T("Rejected", "مرفوض"),
        BookingStatus.Cancelled => T("Cancelled", "ملغي"),
        _ => status.ToString()
    };

    /// <summary>
    /// Bootstrap badge colour. Open statuses are amber, settled ones green, closed ones grey or red;
    /// a request that is merely waiting is never shown in red.
    /// </summary>
    public static string BadgeClass(BookingStatus status) => status switch
    {
        BookingStatus.Confirmed or BookingStatus.Approved => "text-bg-success",
        BookingStatus.Rejected => "text-bg-danger",
        BookingStatus.Cancelled => "text-bg-secondary",
        _ => "text-bg-warning"
    };
}
