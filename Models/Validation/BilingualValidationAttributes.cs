using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace GharsPlatform.Models.Validation;

/// <summary>
/// Bilingual DataAnnotations attributes for public forms.
///
/// The platform's usual idiom is an inline <c>T(en, ar)</c> helper, but that only reaches text the
/// server renders: <see cref="IValidatableObject"/> (see <c>BookingCreateVm</c>) produces no
/// client-side messages at all, and a plain <c>[Required(ErrorMessage = "...")]</c> bakes one
/// language in at compile time. These attributes resolve the message per request from
/// <see cref="CultureInfo.CurrentUICulture"/>, so the Arabic interface reads Arabic whether the
/// message arrives from jQuery unobtrusive validation or from a server round trip.
///
/// Each attribute implements <see cref="IClientModelValidator"/> itself rather than relying on a
/// registered <c>IValidationAttributeAdapterProvider</c>: the built-in provider matches adapters by
/// exact attribute type, so a subclass would emit no <c>data-val-*</c> attributes and would silently
/// lose client-side validation.
/// </summary>
internal static class BilingualMessage
{
    public static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public static string Resolve(string english, string? arabic) =>
        IsArabic && !string.IsNullOrWhiteSpace(arabic) ? arabic! : english;

    /// <summary>
    /// Builds the failure result with the resolved message. The framework-derived attributes below
    /// do not reliably route server-side validation through <c>FormatErrorMessage</c>, so the message
    /// is attached here explicitly rather than left to the base implementation.
    /// </summary>
    public static ValidationResult Failure(ValidationAttribute attribute, ValidationContext context)
    {
        var name = context.DisplayName ?? context.MemberName ?? "";
        return new ValidationResult(
            attribute.FormatErrorMessage(name),
            context.MemberName is null ? null : new[] { context.MemberName });
    }

    /// <summary>Adds a data- attribute without overwriting one already present.</summary>
    public static void Merge(IDictionary<string, string> attributes, string key, string value)
    {
        if (!attributes.ContainsKey(key)) attributes.Add(key, value);
    }
}

/// <summary>
/// Required, with an Arabic message via <see cref="Ar"/>.
///
/// Deliberately derives from <see cref="ValidationAttribute"/> rather than <see cref="RequiredAttribute"/>:
/// attributes are singletons, and MVC resolves the message for a framework-recognised attribute once,
/// when the validator cache is first built. That bakes in whichever language served the first request.
/// A plain <see cref="ValidationAttribute"/> has no such adapter, so the message is resolved per request.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class BilingualRequiredAttribute : ValidationAttribute, IClientModelValidator
{
    public string? Ar { get; set; }

    public override bool IsValid(object? value) => value switch
    {
        null => false,
        string s => s.Trim().Length > 0,
        _ => true
    };

    public override string FormatErrorMessage(string name) =>
        BilingualMessage.Resolve(base.FormatErrorMessage(name), Ar);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        IsValid(value) ? ValidationResult.Success : BilingualMessage.Failure(this, validationContext);

    public void AddValidation(ClientModelValidationContext context)
    {
        var message = FormatErrorMessage(context.ModelMetadata.GetDisplayName() ?? context.ModelMetadata.Name ?? "");
        BilingualMessage.Merge(context.Attributes, "data-val", "true");
        BilingualMessage.Merge(context.Attributes, "data-val-required", message);
    }
}

/// <summary>
/// Email address, with an Arabic message via <see cref="Ar"/>. <see cref="EmailAddressAttribute"/>
/// is sealed, so the framework's own instance does the validating and this only owns the message.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class BilingualEmailAddressAttribute : ValidationAttribute, IClientModelValidator
{
    private static readonly EmailAddressAttribute Inner = new();

    public string? Ar { get; set; }

    public override bool IsValid(object? value) => Inner.IsValid(value);

    public override string FormatErrorMessage(string name) =>
        BilingualMessage.Resolve(base.FormatErrorMessage(name), Ar);

    public void AddValidation(ClientModelValidationContext context)
    {
        var message = FormatErrorMessage(context.ModelMetadata.GetDisplayName() ?? context.ModelMetadata.Name ?? "");
        BilingualMessage.Merge(context.Attributes, "data-val", "true");
        BilingualMessage.Merge(context.Attributes, "data-val-email", message);
    }
}

/// <summary>
/// Minimum length, with an Arabic message via <see cref="Ar"/>. Derives from
/// <see cref="ValidationAttribute"/> for the same reason as <see cref="BilingualRequiredAttribute"/>.
/// A null or absent value is left to the required check, matching <see cref="MinLengthAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class BilingualMinLengthAttribute : ValidationAttribute, IClientModelValidator
{
    public BilingualMinLengthAttribute(int length) => Length = length;

    public int Length { get; }

    public string? Ar { get; set; }

    public override bool IsValid(object? value) => value switch
    {
        null => true,
        string s => s.Length >= Length,
        System.Collections.ICollection c => c.Count >= Length,
        _ => true
    };

    public override string FormatErrorMessage(string name) =>
        BilingualMessage.Resolve(base.FormatErrorMessage(name), Ar);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        IsValid(value) ? ValidationResult.Success : BilingualMessage.Failure(this, validationContext);

    public void AddValidation(ClientModelValidationContext context)
    {
        var message = FormatErrorMessage(context.ModelMetadata.GetDisplayName() ?? context.ModelMetadata.Name ?? "");
        BilingualMessage.Merge(context.Attributes, "data-val", "true");
        BilingualMessage.Merge(context.Attributes, "data-val-minlength", message);
        BilingualMessage.Merge(context.Attributes, "data-val-minlength-min", Length.ToString(CultureInfo.InvariantCulture));
    }
}
