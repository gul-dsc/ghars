using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace GharsPlatform.ViewModels;

/// <summary>
/// The club-editable part of a Ghars Annual Report: the section 1 contact snapshot and the three
/// section 4 narrative answers. Everything in section 2 and section 3 is system-derived and is
/// deliberately absent — a value that is not posted cannot be tampered with.
///
/// There is no ClubId/OrganizationId here either: the club is resolved server-side from the
/// authenticated user's organization links on every request.
///
/// VALIDATION CONVENTION (see the project notes): every property is nullable and carries no
/// message-bearing DataAnnotation. A framework attribute resolves its message once, when the validator
/// cache is first built, baking in whichever language served the first request; and MVC synthesises
/// English data-val-* rules for non-nullable value types that no request culture can override. All
/// rules therefore live in <see cref="Validate"/>, which is evaluated per request in the caller's
/// language.
/// </summary>
public class AnnualReportVm : IValidatableObject
{
    public int Id { get; set; }
    public int SeasonId { get; set; }

    /// <summary>Section 1 — pre-filled from the club's primary contact, editable, stored per season.</summary>
    public string? ProgramCoordinatorName { get; set; }
    public string? ContactNumber { get; set; }
    public string? ContactEmail { get; set; }

    /// <summary>Section 4 — stored in the language the club typed and displayed as entered.</summary>
    public string? KeyResults { get; set; }
    public string? Challenges { get; set; }
    public string? DevelopmentProposals { get; set; }

    /// <summary>True when "Save draft" was pressed rather than "Submit". Relaxes the rules below.</summary>
    public bool SaveAsDraft { get; set; }

    private const int NarrativeMaxLength = 4000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        string T(string en, string arabic) => ar ? arabic : en;

        if (SeasonId <= 0)
            yield return new ValidationResult(T("Select a valid sports season.", "يرجى اختيار موسم رياضي صالح."), new[] { nameof(SeasonId) });

        // Length rules apply to a draft as well: they protect the column, not the workflow.
        foreach (var (value, field, max, label) in new (string?, string, int, string)[]
        {
            (ProgramCoordinatorName, nameof(ProgramCoordinatorName), 200, T("Person responsible for the Ghars Program", "المسؤول عن برنامج غرس")),
            (ContactNumber, nameof(ContactNumber), 50, T("Contact number", "رقم التواصل")),
            (ContactEmail, nameof(ContactEmail), 250, T("Email", "البريد الإلكتروني")),
            (KeyResults, nameof(KeyResults), NarrativeMaxLength, T("Key results", "أبرز النتائج")),
            (Challenges, nameof(Challenges), NarrativeMaxLength, T("Challenges", "التحديات")),
            (DevelopmentProposals, nameof(DevelopmentProposals), NarrativeMaxLength, T("Development proposals", "مقترحات التطوير"))
        })
        {
            if (value is not null && value.Length > max)
                yield return new ValidationResult(
                    T($"{label} must be {max} characters or fewer.", $"يجب ألا يتجاوز حقل \"{label}\" {max} حرفاً."),
                    new[] { field });
        }

        if (!string.IsNullOrWhiteSpace(ContactEmail) && !IsPlausibleEmail(ContactEmail))
            yield return new ValidationResult(T("Enter a valid email address.", "يرجى إدخال بريد إلكتروني صالح."), new[] { nameof(ContactEmail) });

        // A draft is a work in progress: nothing below is required until the club submits.
        if (SaveAsDraft) yield break;

        if (string.IsNullOrWhiteSpace(ProgramCoordinatorName))
            yield return new ValidationResult(
                T("Enter the person responsible for the Ghars Program before submitting.", "يرجى إدخال المسؤول عن برنامج غرس قبل الإرسال."),
                new[] { nameof(ProgramCoordinatorName) });

        if (string.IsNullOrWhiteSpace(ContactNumber))
            yield return new ValidationResult(
                T("Enter a contact number before submitting.", "يرجى إدخال رقم التواصل قبل الإرسال."),
                new[] { nameof(ContactNumber) });

        if (string.IsNullOrWhiteSpace(ContactEmail))
            yield return new ValidationResult(
                T("Enter an email address before submitting.", "يرجى إدخال البريد الإلكتروني قبل الإرسال."),
                new[] { nameof(ContactEmail) });

        // The template asks all three narrative questions, so all three are required to submit.
        if (string.IsNullOrWhiteSpace(KeyResults))
            yield return new ValidationResult(
                T("Describe the key results and impact achieved before submitting.", "يرجى ذكر أبرز النتائج والأثر المحقق قبل الإرسال."),
                new[] { nameof(KeyResults) });

        if (string.IsNullOrWhiteSpace(Challenges))
            yield return new ValidationResult(
                T("Describe the key challenges faced before submitting.", "يرجى ذكر أبرز التحديات قبل الإرسال."),
                new[] { nameof(Challenges) });

        if (string.IsNullOrWhiteSpace(DevelopmentProposals))
            yield return new ValidationResult(
                T("Add the club's proposals for developing the Ghars Program before submitting.", "يرجى إضافة مقترحات النادي لتطوير برنامج غرس قبل الإرسال."),
                new[] { nameof(DevelopmentProposals) });
    }

    /// <summary>
    /// Deliberately permissive: this is a contact detail typed by a club administrator, not a login,
    /// and a stricter pattern would reject valid addresses without protecting anything.
    /// </summary>
    private static bool IsPlausibleEmail(string value)
    {
        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@');
        return at > 0
               && at < trimmed.Length - 1
               && trimmed.IndexOf('@', at + 1) < 0
               && trimmed.LastIndexOf('.') > at + 1
               && !trimmed.Contains(' ');
    }
}
