using GharsPlatform.Models.Core;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace GharsPlatform.ViewModels;

/// <summary>
/// Create/edit form for a partner-managed offering.
///
/// Note what is deliberately absent: there is no PartnerOrganizationId. Ownership is resolved from
/// the authenticated user in the controller and never model-bound, so no posted value can move an
/// offering between implementing entities.
///
/// Validation lives in <see cref="Validate"/> rather than in message-carrying DataAnnotations,
/// matching <see cref="BookingCreateVm"/>. That is not only for consistency: a framework attribute
/// resolves its message once, when the validator cache is first built, which bakes in whichever
/// language served the first request. IValidatableObject runs per request and cannot do that.
/// </summary>
public class PartnerProgramVm : IValidatableObject
{
    /// <summary>The audience vocabulary is shared verbatim with <see cref="BookingCreateVm"/> so a
    /// selection made here can be carried into a booking request without any mapping.</summary>
    public static readonly string[] AllowedAudiences =
    [
        "Players",
        "Coaches",
        "Staff",
        "Administrators",
        "Parents",
        "Others"
    ];

    /// <summary>Only these two types may be created or edited through the partner surface. Lecture,
    /// Course, Event and Activity remain valid elsewhere and on historical rows.</summary>
    public static readonly ActivityType[] AllowedTypes =
    [
        ActivityType.TrainingProgram,
        ActivityType.Workshop
    ];

    public int Id { get; set; }

    // Nullable for the same reason as Capacity: a non-nullable value type makes MVC emit an English
    // data-val-required message that no request culture can change.
    public int? SeasonId { get; set; }

    public ActivityType? Type { get; set; } = ActivityType.TrainingProgram;

    public string? TitleEn { get; set; }

    public string? TitleAr { get; set; }

    public string? DescriptionEn { get; set; }

    public string? DescriptionAr { get; set; }

    public List<string> TargetAudiences { get; set; } = [];

    public string? OtherTargetAudience { get; set; }

    // Indicative session slot. Clubs may propose a different date when they request the offering;
    // this is what the booking form pre-fills.
    public DateOnly? SessionDate { get; set; }
    public TimeOnly? SessionStartTime { get; set; }
    public TimeOnly? SessionEndTime { get; set; }

    // Nullable, and with no [Range]: a non-nullable int here makes MVC emit
    // data-val-required="The Capacity field is required." and a matching data-val-range, both in
    // English regardless of the request culture, and both reachable by clearing the number input.
    public int? Capacity { get; set; } = 30;

    public string? LocationEn { get; set; }

    public string? LocationAr { get; set; }

    // Optional booking window. Null at either end means no bound in that direction.
    public DateOnly? AvailableFrom { get; set; }
    public DateOnly? AvailableUntil { get; set; }

    /// <summary>True when the offering already has booking requests, which locks the fields that
    /// would rewrite the meaning of those bookings. Display-only: the controller re-derives it.</summary>
    public bool HasBookings { get; set; }

    // ------------------------------------------------------------------ supporting documents
    //
    // Attachments are optional at every stage, including submission. A programme that needs no
    // brochure is not an incomplete programme, and blocking submission over a missing file would
    // invent a rule the business never asked for.

    /// <summary>Files chosen on this post. Type, MIME and size are validated in the controller
    /// against <see cref="Helpers.FileValidationHelper.ProgramAttachment"/>, which is where every
    /// other upload in this project is checked — the rules belong with the storage profile, not
    /// duplicated into a view model.</summary>
    public List<IFormFile>? Attachments { get; set; }

    /// <summary>Ids of already-stored attachments the partner ticked for removal. Every id is
    /// re-scoped to this offering in the controller before anything is deleted.</summary>
    public List<int> RemoveAttachmentIds { get; set; } = [];

    /// <summary>What is already attached, for rendering only. Never bound from the request: the
    /// controller reloads it from the database on every GET and on every invalid POST.</summary>
    public List<ActivityAttachment> ExistingAttachments { get; set; } = [];

    /// <summary>How many documents one offering may carry. A limit the form states plainly rather
    /// than a silent truncation on the server.</summary>
    public const int MaxAttachments = 10;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var isAr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        static string T(bool ar, string en, string arText) => ar ? arText : en;

        if (!SeasonId.HasValue || SeasonId.Value <= 0)
            yield return new ValidationResult(T(isAr, "Sports season is required.", "الموسم الرياضي مطلوب."), [nameof(SeasonId)]);

        if (!Type.HasValue || !AllowedTypes.Contains(Type.Value))
            yield return new ValidationResult(T(isAr, "Choose either a training programme or a workshop.", "يرجى اختيار برنامج تدريبي أو ورشة عمل."), [nameof(Type)]);

        if (string.IsNullOrWhiteSpace(TitleEn))
            yield return new ValidationResult(T(isAr, "The English title is required.", "العنوان بالإنجليزية مطلوب."), [nameof(TitleEn)]);
        else if (TitleEn.Trim().Length > 250)
            yield return new ValidationResult(T(isAr, "The English title must be 250 characters or fewer.", "يجب ألا يتجاوز العنوان بالإنجليزية 250 حرفاً."), [nameof(TitleEn)]);

        if (string.IsNullOrWhiteSpace(TitleAr))
            yield return new ValidationResult(T(isAr, "The Arabic title is required.", "العنوان بالعربية مطلوب."), [nameof(TitleAr)]);
        else if (TitleAr.Trim().Length > 250)
            yield return new ValidationResult(T(isAr, "The Arabic title must be 250 characters or fewer.", "يجب ألا يتجاوز العنوان بالعربية 250 حرفاً."), [nameof(TitleAr)]);

        if (string.IsNullOrWhiteSpace(DescriptionEn))
            yield return new ValidationResult(T(isAr, "The English description is required.", "الوصف بالإنجليزية مطلوب."), [nameof(DescriptionEn)]);
        else if (DescriptionEn.Trim().Length > 3000)
            yield return new ValidationResult(T(isAr, "The English description must be 3000 characters or fewer.", "يجب ألا يتجاوز الوصف بالإنجليزية 3000 حرف."), [nameof(DescriptionEn)]);

        if (string.IsNullOrWhiteSpace(DescriptionAr))
            yield return new ValidationResult(T(isAr, "The Arabic description is required.", "الوصف بالعربية مطلوب."), [nameof(DescriptionAr)]);
        else if (DescriptionAr.Trim().Length > 3000)
            yield return new ValidationResult(T(isAr, "The Arabic description must be 3000 characters or fewer.", "يجب ألا يتجاوز الوصف بالعربية 3000 حرف."), [nameof(DescriptionAr)]);

        if (OtherTargetAudience is { Length: > 150 })
            yield return new ValidationResult(T(isAr, "The other-audience description must be 150 characters or fewer.", "يجب ألا يتجاوز وصف الفئة الأخرى 150 حرفاً."), [nameof(OtherTargetAudience)]);

        if (LocationEn is { Length: > 300 } || LocationAr is { Length: > 300 })
            yield return new ValidationResult(T(isAr, "The location must be 300 characters or fewer.", "يجب ألا يتجاوز الموقع 300 حرف."), [nameof(LocationEn)]);

        if (!Capacity.HasValue)
            yield return new ValidationResult(T(isAr, "Capacity is required.", "السعة مطلوبة."), [nameof(Capacity)]);
        else if (Capacity.Value < 1 || Capacity.Value > 5000)
            yield return new ValidationResult(T(isAr, "Capacity must be between 1 and 5000.", "يجب أن تكون السعة بين 1 و5000."), [nameof(Capacity)]);

        var audiences = TargetAudiences.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (audiences.Count == 0)
            yield return new ValidationResult(T(isAr, "Select at least one target audience.", "يرجى اختيار فئة مستهدفة واحدة على الأقل."), [nameof(TargetAudiences)]);

        if (audiences.Any(x => !AllowedAudiences.Contains(x)))
            yield return new ValidationResult(T(isAr, "An unrecognised target audience was submitted.", "تم إرسال فئة مستهدفة غير معروفة."), [nameof(TargetAudiences)]);

        if (audiences.Contains("Others") && string.IsNullOrWhiteSpace(OtherTargetAudience))
            yield return new ValidationResult(T(isAr, "Describe the other target audience.", "يرجى وصف الفئة المستهدفة الأخرى."), [nameof(OtherTargetAudience)]);

        if (!SessionDate.HasValue)
            yield return new ValidationResult(T(isAr, "An indicative session date is required.", "تاريخ الجلسة الاسترشادي مطلوب."), [nameof(SessionDate)]);

        if (!SessionStartTime.HasValue)
            yield return new ValidationResult(T(isAr, "A start time is required.", "وقت البداية مطلوب."), [nameof(SessionStartTime)]);

        if (!SessionEndTime.HasValue)
            yield return new ValidationResult(T(isAr, "An end time is required.", "وقت النهاية مطلوب."), [nameof(SessionEndTime)]);

        if (SessionStartTime.HasValue && SessionEndTime.HasValue && SessionEndTime.Value <= SessionStartTime.Value)
            yield return new ValidationResult(T(isAr, "The end time must be after the start time.", "يجب أن يكون وقت النهاية بعد وقت البداية."), [nameof(SessionEndTime)]);

        if (AvailableFrom.HasValue && AvailableUntil.HasValue && AvailableUntil.Value < AvailableFrom.Value)
            yield return new ValidationResult(T(isAr, "The availability end date must not precede its start date.", "يجب ألا يسبق تاريخ نهاية الإتاحة تاريخ بدايتها."), [nameof(AvailableUntil)]);
    }
}
