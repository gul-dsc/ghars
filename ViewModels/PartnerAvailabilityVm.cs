using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GharsPlatform.Models.Core;

namespace GharsPlatform.ViewModels;

/// <summary>
/// One row of the "Add Availability" form: a start and an end on the date the form as a whole names.
/// </summary>
/// <remarks>
/// The partner adds <em>one date with several times</em> in a single submit, so the times are a list
/// of these rather than the form being posted three separate times. Blank rows are ignored rather
/// than rejected — the form renders spare rows, and an untouched spare is not an error.
/// </remarks>
public class AvailabilityTimeRowVm
{
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public bool IsEmpty => StartTime is null && EndTime is null;
}

/// <summary>
/// Add availability: one date, one or more time slots, and optional notes that apply to all of them.
/// </summary>
/// <remarks>
/// <b>There is no organization on this view model, and there never will be.</b> The owning entity is
/// resolved server-side from the authenticated user's organization link. A posted
/// <c>PartnerOrganizationId</c> would be the whole security model of this feature, handed to the
/// browser.
/// </remarks>
public class PartnerAvailabilityCreateVm : IValidatableObject
{
    public DateOnly? Date { get; set; }

    public List<AvailabilityTimeRowVm> Times { get; set; } = [new(), new(), new()];

    /// <summary>
    /// Optional. Null means the slots are offered for any eligible request, which is what makes them
    /// usable by a Custom Program request as well as a programme booking.
    /// </summary>
    public int? ActivityId { get; set; }

    [StringLength(500)]
    public string? NotesEn { get; set; }

    [StringLength(500)]
    public string? NotesAr { get; set; }

    [StringLength(300)]
    public string? LocationEn { get; set; }

    [StringLength(300)]
    public string? LocationAr { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var isAr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        static string T(bool ar, string en, string arText) => ar ? arText : en;

        if (!Date.HasValue)
        {
            yield return new ValidationResult(T(isAr, "Date is required.", "التاريخ مطلوب."), [nameof(Date)]);
            yield break;
        }

        var filled = Times.Where(x => !x.IsEmpty).ToList();
        if (filled.Count == 0)
        {
            yield return new ValidationResult(
                T(isAr, "Add at least one time slot.", "أضف فترة زمنية واحدة على الأقل."),
                [nameof(Times)]);
            yield break;
        }

        // A half-filled row is a mistake, not an empty row. Saying so beats silently dropping it.
        if (filled.Any(x => x.StartTime is null || x.EndTime is null))
        {
            yield return new ValidationResult(
                T(isAr, "Every time slot needs both a start and an end time.", "كل فترة زمنية تحتاج إلى وقت بدء ووقت انتهاء."),
                [nameof(Times)]);
            yield break;
        }

        // Overlap *within this submit*. The server also checks against what is already published;
        // this catches the case where the partner types two overlapping rows on the same form, which
        // the database would otherwise see as two separate inserts.
        var ordered = filled.OrderBy(x => x.StartTime!.Value).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].StartTime!.Value < ordered[i - 1].EndTime!.Value)
            {
                yield return new ValidationResult(
                    T(isAr, "Time slots on the same date cannot overlap.", "لا يمكن تداخل الفترات الزمنية في التاريخ نفسه."),
                    [nameof(Times)]);
                yield break;
            }
        }
    }
}

/// <summary>Edit one existing slot. Only a free future slot ever reaches this form.</summary>
public class PartnerAvailabilityEditVm
{
    public int Id { get; set; }

    public DateOnly? Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public int? ActivityId { get; set; }

    [StringLength(500)]
    public string? NotesEn { get; set; }

    [StringLength(500)]
    public string? NotesAr { get; set; }

    [StringLength(300)]
    public string? LocationEn { get; set; }

    [StringLength(300)]
    public string? LocationAr { get; set; }
}

/// <summary>
/// A slot as it is shown to a club or an anonymous visitor.
/// </summary>
/// <remarks>
/// A deliberate projection, not the entity. The JSON endpoint that feeds the custom-request form
/// serialises this, and it carries six facts and nothing else — no status, no holder, no audit
/// stamps, no internal note, and nothing about who else may have asked.
/// </remarks>
public sealed record AvailabilitySlotPublicVm(
    int Id,
    string Date,
    string StartTime,
    string EndTime,
    int? ActivityId,
    string? Note);

/// <summary>One date's worth of public slots, for the month and list views.</summary>
public sealed record AvailabilityDayVm(DateOnly Date, List<PartnerAvailabilitySlot> Slots);
