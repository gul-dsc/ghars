using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// A future time an implementing entity is willing to receive a booking request for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a <see cref="CalendarEvent"/>.</b> That entity was assessed and rejected: it carries no
/// organization of any kind, its title is required in both languages, its visibility is a single
/// bool where this needs a five-state lifecycle, and widening
/// <see cref="CalendarEventType"/> would silently change what every existing comparison against it
/// includes. A calendar event is something that <em>is happening</em>; this is something that
/// <em>might be asked for</em>. See GHARS_PARTNER_AVAILABILITY_CALENDAR.md §2.
/// </para>
/// <para>
/// <b>Flat, one row per slot.</b> "20 September, three slots" is three rows, not a day row with
/// children. That is what makes multiple slots per date the default shape rather than a feature, and
/// it leaves every slot independently selectable, claimable and cancellable.
/// </para>
/// <para>
/// <b>Entirely optional.</b> An entity that never opens this screen is indistinguishable from before
/// the feature existed. Nothing in the booking workflow reads this table unless a club selected a
/// slot.
/// </para>
/// <para>
/// <b>Never a business statistic.</b> Slots are not programs, bookings, agenda entries or KPI
/// inputs. Publishing twenty of them changes no total anywhere in Ghars.
/// </para>
/// </remarks>
public class PartnerAvailabilitySlot : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>
    /// The owning implementing entity. Always written from the authenticated user's organization
    /// link and never model-bound, so no posted value can create or reach another entity's slot.
    /// </summary>
    public int PartnerOrganizationId { get; set; }
    public Organization? PartnerOrganization { get; set; }

    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    /// <summary>
    /// Optional. <c>null</c> means the slot is offered for any eligible request — which is what makes
    /// it usable by a Custom Program request, where there is no activity at all. When set, the slot
    /// is offered only for that one published offering, and the entity's ownership of it is
    /// re-validated on every write.
    /// </summary>
    public int? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    // ---------------------------------------------------------------- when
    //
    // DateOnly + TimeOnly, deliberately: these types cannot carry an offset, so there is no timezone
    // ambiguity to get wrong in the column. They are Dubai local wall-clock, which is also exactly
    // what the club types into the existing booking form — so a slot selection produces
    // ProposedStartDateTime through the same expression the manual path uses and the two kinds of
    // booking are stored identically. Comparisons against "now" go through GharsTime, never
    // DateTime.UtcNow. See GHARS_PARTNER_AVAILABILITY_CALENDAR.md §3.

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public PartnerAvailabilityStatus Status { get; set; } = PartnerAvailabilityStatus.Available;

    /// <summary>
    /// The booking request currently holding this slot, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// A plain column with <b>no foreign key</b>, on purpose. The constrained direction is
    /// <see cref="BookingRequest.PartnerAvailabilitySlotId"/>; adding one here too would make the
    /// pair circular. This is the <em>current</em> holder, which is different information from
    /// "every booking that ever referenced this slot" — a released slot keeps the old booking's
    /// reference for provenance, so deriving the holder from that direction would need a status
    /// filter and would be wrong the first time somebody forgot it.
    /// </remarks>
    public int? HeldByBookingRequestId { get; set; }

    /// <summary>
    /// Optional public note shown to clubs, e.g. "Workshops at our premises only". Bilingual because
    /// it is club-facing, and optional because most slots need nothing said about them.
    /// </summary>
    [MaxLength(500)]
    public string? NotesEn { get; set; }

    [MaxLength(500)]
    public string? NotesAr { get; set; }

    [MaxLength(300)]
    public string? LocationEn { get; set; }

    [MaxLength(300)]
    public string? LocationAr { get; set; }

    /// <summary>The slot's start as a Dubai wall-clock <see cref="DateTime"/>, for display and for
    /// writing a booking's proposed time. Not mapped — composed from the two stored columns.</summary>
    public DateTime StartDateTime => Date.ToDateTime(StartTime);

    /// <summary>The slot's end as a Dubai wall-clock <see cref="DateTime"/>. Not mapped.</summary>
    public DateTime EndDateTime => Date.ToDateTime(EndTime);
}
