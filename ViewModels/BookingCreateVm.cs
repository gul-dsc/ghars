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

    // Null for direct entity-first requests (no pre-published program is selected).
    public int? ActivityId { get; set; }

    // Direct-request fields (required when no activity anchors the request).
    public int? PartnerOrganizationId { get; set; }
    public int? SeasonId { get; set; }

    public int OrganizationId { get; set; }
    public ActivityType RequestedActivityType { get; set; } = ActivityType.Lecture;
    public string? Subject { get; set; }
    public DateOnly? ProposedDate { get; set; }
    public TimeOnly? ProposedStartTime { get; set; }
    public TimeOnly? ProposedEndTime { get; set; }
    public List<string> TargetAudiences { get; set; } = [];
    public string? OtherTargetAudience { get; set; }
    public int ExpectedParticipants { get; set; } = 1;
    public string? AudienceDetails { get; set; }
    public string? Notes { get; set; }
    public string? ContactPersonName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
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

        if (string.IsNullOrWhiteSpace(Subject))
            yield return new ValidationResult(T(isAr, "Subject is required.", "الموضوع مطلوب."), [nameof(Subject)]);

        if (!ProposedDate.HasValue)
            yield return new ValidationResult(T(isAr, "Proposed date is required.", "التاريخ المقترح مطلوب."), [nameof(ProposedDate)]);

        if (!ProposedStartTime.HasValue)
            yield return new ValidationResult(T(isAr, "Proposed start time is required.", "وقت البدء المقترح مطلوب."), [nameof(ProposedStartTime)]);

        if (!ProposedEndTime.HasValue)
            yield return new ValidationResult(T(isAr, "Proposed end time is required.", "وقت الانتهاء المقترح مطلوب."), [nameof(ProposedEndTime)]);

        if (ProposedDate.HasValue && ProposedStartTime.HasValue && ProposedEndTime.HasValue)
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
