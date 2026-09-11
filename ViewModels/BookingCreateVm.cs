using GharsPlatform.Models.Core;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace GharsPlatform.ViewModels;

public class BookingCreateVm : IValidatableObject
{
    private static readonly string[] AllowedAudiences =
    [
        "Players",
        "Coaches",
        "Staff",
        "Administrators",
        "Parents",
        "Others"
    ];

    // Null for a Custom Program Request; set when the club is booking an existing published program.
    public int? ActivityId { get; set; }

    // Custom-request fields (required when no activity anchors the request).
    public int? PartnerOrganizationId { get; set; }
    public int? SeasonId { get; set; }

    public int OrganizationId { get; set; }
    public ActivityType RequestedActivityType { get; set; } = ActivityType.Lecture;

    // The requested program name. Subject is the canonical field on BookingRequest and is what both
    // paths write to, so there is no second ProgramName/RequestedProgramName to keep in step.
    //
    // Every length below mirrors the MaxLength on the matching BookingRequest column. Without them an
    // over-long value reaches SQL Server and fails as a truncation exception — a 500 where the user
    // should simply be told the field is too long.
    [StringLength(250)]
    public string? Subject { get; set; }

    public DateOnly? ProposedDate { get; set; }
    public TimeOnly? ProposedStartTime { get; set; }
    public TimeOnly? ProposedEndTime { get; set; }

    /// <summary>
    /// The implementing entity's published availability slot the club chose, or <c>null</c> when it
    /// is proposing its own date and time.
    /// </summary>
    /// <remarks>
    /// Optional on every path, always. The partner calendar is an aid, not a gate: even where an
    /// entity has published availability the manual fields stay usable, which is what keeps a
    /// partner with no calendar — and a club that wants a different time — working exactly as
    /// before.
    ///
    /// When it is set, the three fields above are <b>not</b> trusted. The server re-reads the slot,
    /// re-checks every condition, and overwrites the date and times from it, so a post that pairs a
    /// real slot id with a different time is stored as the slot, never as the post. The validation
    /// below skips them for the same reason: they are derived, so requiring them of the browser
    /// would be asking for a value that is about to be thrown away.
    /// </remarks>
    public int? PartnerAvailabilitySlotId { get; set; }
    public List<string> TargetAudiences { get; set; } = [];

    [StringLength(150)]
    public string? OtherTargetAudience { get; set; }

    public int ExpectedParticipants { get; set; } = 1;

    [StringLength(1000)]
    public string? AudienceDetails { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [StringLength(150)]
    public string? ContactPersonName { get; set; }

    [StringLength(50)]
    public string? ContactPhone { get; set; }

    [StringLength(150)]
    public string? ContactEmail { get; set; }

    [StringLength(1000)]
    public string? SpecialRequirements { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var isAr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        static string T(bool ar, string en, string arText) => ar ? arText : en;

        if (OrganizationId <= 0)
            yield return new ValidationResult(T(isAr, "Club is required.", "النادي مطلوب."), [nameof(OrganizationId)]);

        if (!ActivityId.HasValue || ActivityId <= 0)
        {
            if (!PartnerOrganizationId.HasValue || PartnerOrganizationId <= 0)
                yield return new ValidationResult(T(isAr, "Implementing entity is required.", "الجهة المنفذة مطلوبة."), [nameof(PartnerOrganizationId)]);

            if (!SeasonId.HasValue || SeasonId <= 0)
                yield return new ValidationResult(T(isAr, "Sports season is required.", "الموسم الرياضي مطلوب."), [nameof(SeasonId)]);
        }

        // For a Custom Program Request this is the program the club is asking for, so it is named as
        // such. On the existing-program path the value is derived from the offering server-side and
        // this branch is unreachable.
        if (string.IsNullOrWhiteSpace(Subject))
            yield return new ValidationResult(T(isAr, "Program name is required.", "اسم البرنامج مطلوب."), [nameof(Subject)]);

        // A selected availability slot supplies the date and both times server-side, so they are not
        // required of the form. Everything below this point still applies.
        var scheduleFromSlot = PartnerAvailabilitySlotId is > 0;

        if (!scheduleFromSlot && !ProposedDate.HasValue)
            yield return new ValidationResult(T(isAr, "Proposed date is required.", "التاريخ المقترح مطلوب."), [nameof(ProposedDate)]);

        if (!scheduleFromSlot && !ProposedStartTime.HasValue)
            yield return new ValidationResult(T(isAr, "Proposed start time is required.", "وقت البدء المقترح مطلوب."), [nameof(ProposedStartTime)]);

        if (!scheduleFromSlot && !ProposedEndTime.HasValue)
            yield return new ValidationResult(T(isAr, "Proposed end time is required.", "وقت الانتهاء المقترح مطلوب."), [nameof(ProposedEndTime)]);

        if (!scheduleFromSlot && ProposedDate.HasValue && ProposedStartTime.HasValue && ProposedEndTime.HasValue)
        {
            var start = ProposedDate.Value.ToDateTime(ProposedStartTime.Value);
            var end = ProposedDate.Value.ToDateTime(ProposedEndTime.Value);
            if (end <= start)
            {
                yield return new ValidationResult(
                    T(isAr, "Proposed end time must be after the proposed start time.", "يجب أن يكون وقت الانتهاء بعد وقت البدء المقترح."),
                    [nameof(ProposedEndTime)]);
            }
        }

        if (TargetAudiences.Count == 0)
            yield return new ValidationResult(T(isAr, "Select at least one target audience.", "يرجى اختيار فئة مستهدفة واحدة على الأقل."), [nameof(TargetAudiences)]);

        if (TargetAudiences.Any(x => !AllowedAudiences.Contains(x)))
            yield return new ValidationResult(T(isAr, "One or more target audience selections are invalid.", "أحد خيارات الفئة المستهدفة غير صالح."), [nameof(TargetAudiences)]);

        if (TargetAudiences.Contains("Others") && string.IsNullOrWhiteSpace(OtherTargetAudience))
            yield return new ValidationResult(T(isAr, "Specify the other target audience.", "يرجى تحديد الفئة المستهدفة الأخرى."), [nameof(OtherTargetAudience)]);

        if (ExpectedParticipants <= 0)
            yield return new ValidationResult(T(isAr, "Expected number of participants must be greater than zero.", "يجب أن يكون عدد المشاركين المتوقع أكبر من صفر."), [nameof(ExpectedParticipants)]);

        if (string.IsNullOrWhiteSpace(ContactPersonName))
            yield return new ValidationResult(T(isAr, "Contact person name is required.", "اسم الشخص المسؤول مطلوب."), [nameof(ContactPersonName)]);

        if (!string.IsNullOrWhiteSpace(ContactEmail))
        {
            var email = new EmailAddressAttribute();
            if (!email.IsValid(ContactEmail))
                yield return new ValidationResult(T(isAr, "Enter a valid contact email address.", "يرجى إدخال بريد إلكتروني صحيح للتواصل."), [nameof(ContactEmail)]);
        }
    }
}
