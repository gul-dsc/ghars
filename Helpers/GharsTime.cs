namespace GharsPlatform.Helpers;

/// <summary>
/// The Ghars business calendar timezone: <b>Asia/Dubai</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Before the availability calendar, this codebase had no timezone handling
/// at all — not one reference to <see cref="TimeZoneInfo"/> anywhere. Scheduling values are entered
/// by a club as a date plus two times, combined with <c>ToDateTime(...)</c> and stored as-is, so
/// every scheduling column in the database holds a <b>Dubai wall-clock</b> value. (That includes
/// <c>BookingRequest.ConfirmedStartUtc</c>, whose name says otherwise. It is consistent across every
/// row, reinterpreting it would move every historical booking by four hours, and correcting it is
/// not something a calendar feature gets to do.)
/// </para>
/// <para>
/// Availability slots follow that same convention deliberately, and store <see cref="DateOnly"/> and
/// <see cref="TimeOnly"/> — types that cannot carry an offset, so the column has no ambiguity in it
/// to get wrong. Selecting a slot therefore produces a proposed start through exactly the same
/// expression the manual path uses, and calendar bookings and hand-typed bookings are stored
/// identically.
/// </para>
/// <para>
/// <b>The one comparison that needs a zone</b> is "is this slot still in the future", because the
/// server clock reports UTC. That is what this type is for. Scheduling code calls
/// <see cref="Now"/>/<see cref="Today"/>; it must not call <c>DateTime.UtcNow</c> and compare the
/// result to a local date. Audit stamps (<c>CreatedAtUtc</c> and friends) are genuine UTC instants
/// and keep using <c>DateTime.UtcNow</c> — those are records of when something happened, not
/// business calendar values.
/// </para>
/// </remarks>
public static class GharsTime
{
    /// <summary>
    /// The Dubai timezone, resolved once.
    /// </summary>
    /// <remarks>
    /// Three-step resolution on purpose. .NET accepts IANA ids on Windows only when ICU is
    /// available, so <c>Asia/Dubai</c> is tried first and the Windows id second — the IIS host will
    /// certainly have the latter. The final fallback is a fixed <c>UTC+04:00</c>, which is not an
    /// approximation: Dubai has never observed daylight saving, so the offset is the whole truth
    /// about this zone.
    /// </remarks>
    public static TimeZoneInfo Zone { get; } = Resolve();

    /// <summary>The current Dubai wall-clock time.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Today's date in Dubai.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>The current time of day in Dubai.</summary>
    public static TimeOnly TimeNow => TimeOnly.FromDateTime(Now);

    /// <summary>
    /// Whether a Dubai-local date and time is still in the future. This is the guard behind every
    /// "future slots only" rule in the availability calendar.
    /// </summary>
    public static bool IsFuture(DateOnly date, TimeOnly time) => date.ToDateTime(time) > Now;

    /// <summary>Whether a Dubai-local date and time has already passed.</summary>
    public static bool IsPast(DateOnly date, TimeOnly time) => !IsFuture(date, time);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Dubai", "Arabian Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Ghars-Dubai", TimeSpan.FromHours(4), "Gulf Standard Time", "Gulf Standard Time");
    }
}
