using GharsPlatform.Models.Core;
using System.Globalization;

namespace GharsPlatform.Helpers;

/// <summary>
/// The two ways a club reaches a booking, in one place so the six surfaces that display the
/// distinction — the club catalogue, the request form, booking details, both dashboards and the
/// admin lists — cannot describe it differently.
///
///   Existing Program        ActivityId is set. The club picked a DSC-approved, published offering
///                           from the catalogue; title, type, partner and season all derive from it.
///
///   Custom Program Request  ActivityId is NULL. The club described what it needs and addressed it
///                           to an implementing entity. No Activity row is created for it, at request
///                           time or on confirmation — an unapproved request must never appear as an
///                           available program.
///
/// <see cref="BookingRequest.ActivityId"/> is the only discriminator, and nothing ever fabricates an
/// Activity to fill the gap. That is what keeps the two paths distinguishable for the whole life of
/// the request, including in the audit trail after a partner proposes changes.
/// </summary>
public static class BookingSource
{
    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    /// <summary>True when the club wrote this request itself rather than picking a published program.</summary>
    public static bool IsCustom(BookingRequest booking) => !booking.ActivityId.HasValue;

    public static string Label(BookingRequest booking) => IsCustom(booking)
        ? (IsAr ? "طلب برنامج مخصص" : "Custom Program Request")
        : (IsAr ? "برنامج قائم" : "Existing Program");

    /// <summary>
    /// The shorter form, for a dashboard column where the row already reads as a request.
    /// </summary>
    public static string ShortLabel(BookingRequest booking) => IsCustom(booking)
        ? (IsAr ? "برنامج مخصص" : "Custom Program")
        : (IsAr ? "برنامج قائم" : "Existing Program");

    public static string BadgeClass(BookingRequest booking) => IsCustom(booking)
        ? "text-bg-info"
        : "text-bg-light border";

    /// <summary>
    /// An icon per source, so the badge never carries its meaning by colour alone. Always paired with
    /// <see cref="Label"/> or <see cref="ShortLabel"/>, which carry the text.
    /// </summary>
    public static string Icon(BookingRequest booking) => IsCustom(booking)
        ? "bi-pencil-square"
        : "bi-collection";

    /// <summary>
    /// What to call this booking in a list. An existing-program request shows its offering's title in
    /// the reader's language; a custom request shows the program the club asked for. The final
    /// fallback exists only for legacy rows written before a subject was required — it is never the
    /// normal case, and a custom request created today always has a name.
    /// </summary>
    public static string Title(BookingRequest booking)
    {
        if (booking.Activity is not null)
        {
            var offering = IsAr
                ? (booking.Activity.TitleAr ?? booking.Activity.TitleEn)
                : (booking.Activity.TitleEn ?? booking.Activity.TitleAr);
            if (!string.IsNullOrWhiteSpace(offering)) return offering;
        }

        return string.IsNullOrWhiteSpace(booking.Subject)
            ? (IsAr ? "طلب برنامج مخصص" : "Custom Program Request")
            : booking.Subject!;
    }
}

/// <summary>
/// The Booking Source filter offered on the partner and admin booking lists. Deliberately not a
/// stored field: it is derived from ActivityId at query time, so it cannot fall out of step with the
/// data the way a duplicated column would.
/// </summary>
public enum BookingSourceFilter : byte
{
    ExistingProgram = 1,
    CustomProgram = 2
}
