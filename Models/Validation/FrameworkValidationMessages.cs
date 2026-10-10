using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace GharsPlatform.Models.Validation;

/// <summary>
/// Bilingual text for the messages the framework writes itself: the built-in DataAnnotations
/// attributes declared without an <c>ErrorMessage</c> (including MVC's implicit <c>[Required]</c> on
/// non-nullable value types), and the model-binding errors such as "The value '' is invalid.".
///
/// Only the message text changes. Which attributes apply, and what they accept, is untouched.
///
/// The attributes are pointed at the static properties below through
/// <see cref="ValidationAttribute.ErrorMessageResourceType"/>. The framework reads such a property
/// every time it formats the message, so the language follows the current request rather than
/// whichever request first built the validator cache.
///
/// The messages name no field, in either language: most models have no display names, so {0} would
/// print a property name such as "TitleEn". The message appears next to the field it belongs to.
/// </summary>
public static class FrameworkValidationMessages
{
    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    private static string T(string en, string ar) => IsAr ? ar : en;

    // Format arguments follow each attribute's own FormatErrorMessage: {0} is always the display name.
    public static string Required => T("This field is required.", "هذا الحقل مطلوب.");
    public static string StringLengthMax => T("Must be {1} characters or fewer.", "يجب ألا يتجاوز هذا الحقل {1} حرفاً.");
    public static string StringLengthRange => T("Must be between {2} and {1} characters.", "يجب أن يكون طول هذا الحقل بين {2} و{1} حرفاً.");
    public static string MaxLength => T("Must be {1} characters or fewer.", "يجب ألا يتجاوز هذا الحقل {1} حرفاً.");
    public static string MinLength => T("Must be at least {1} characters.", "يجب ألا يقل هذا الحقل عن {1} أحرف.");
    public static string Range => T("Must be between {1} and {2}.", "يجب أن تكون القيمة بين {1} و{2}.");
    public static string EmailAddress => T("Enter a valid email address.", "أدخلوا عنوان بريد إلكتروني صحيحاً.");
    public static string Phone => T("Enter a valid phone number.", "أدخلوا رقم هاتف صحيحاً.");
    public static string Url => T("Enter a valid web address.", "أدخلوا رابطاً صحيحاً.");
    public static string Compare => T("The values don't match.", "القيمتان غير متطابقتين.");

    /// <summary>The resource property for an attribute, or null to leave it alone.</summary>
    private static string? ResourceNameFor(ValidationAttribute attribute) => attribute switch
    {
        RequiredAttribute => nameof(Required),
        StringLengthAttribute s => s.MinimumLength > 0 ? nameof(StringLengthRange) : nameof(StringLengthMax),
        MaxLengthAttribute => nameof(MaxLength),
        MinLengthAttribute => nameof(MinLength),
        RangeAttribute => nameof(Range),
        EmailAddressAttribute => nameof(EmailAddress),
        PhoneAttribute => nameof(Phone),
        UrlAttribute => nameof(Url),
        CompareAttribute => nameof(Compare),
        _ => null
    };

    /// <summary>
    /// Registers both halves. Call from <c>AddControllersWithViews(options => ...)</c>; the metadata
    /// provider is appended so it runs after the one that adds the implicit <c>[Required]</c>.
    /// </summary>
    public static void Apply(MvcOptions options)
    {
        options.ModelMetadataDetailsProviders.Add(new MetadataProvider());

        var m = options.ModelBindingMessageProvider;
        m.SetValueIsInvalidAccessor(_ => T("Enter a valid value.", "أدخلوا قيمة صحيحة."));
        m.SetValueMustNotBeNullAccessor(_ => T("This field is required.", "هذا الحقل مطلوب."));
        m.SetAttemptedValueIsInvalidAccessor((_, _) => T("Enter a valid value.", "أدخلوا قيمة صحيحة."));
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => T("Enter a valid value.", "أدخلوا قيمة صحيحة."));
        m.SetUnknownValueIsInvalidAccessor(_ => T("Enter a valid value.", "أدخلوا قيمة صحيحة."));
        m.SetNonPropertyUnknownValueIsInvalidAccessor(() => T("Enter a valid value.", "أدخلوا قيمة صحيحة."));
        m.SetValueMustBeANumberAccessor(_ => T("Enter a number.", "أدخلوا رقماً."));
        m.SetNonPropertyValueMustBeANumberAccessor(() => T("Enter a number.", "أدخلوا رقماً."));
        m.SetMissingBindRequiredValueAccessor(_ => T("This field is required.", "هذا الحقل مطلوب."));
        m.SetMissingKeyOrValueAccessor(() => T("This field is required.", "هذا الحقل مطلوب."));
        m.SetMissingRequestBodyRequiredValueAccessor(() => T("A value is required.", "القيمة مطلوبة."));
    }

    private sealed class MetadataProvider : IValidationMetadataProvider
    {
        public void CreateValidationMetadata(ValidationMetadataProviderContext context)
        {
            var validators = context.ValidationMetadata.ValidatorMetadata;

            if (validators.OfType<BilingualRequiredAttribute>().Any())
            {
                // The field already has its own bilingual required message. Drop MVC's implicit
                // [Required] (added for non-nullable strings) so the field does not show two
                // messages, and stop the client provider adding a third.
                foreach (var implicitRequired in validators.OfType<RequiredAttribute>().ToList())
                    validators.Remove(implicitRequired);
                context.ValidationMetadata.IsRequired = false;
            }
            else if (context.Key.MetadataKind == ModelMetadataKind.Property
                     && context.Key.ModelType.IsValueType
                     && Nullable.GetUnderlyingType(context.Key.ModelType) is null
                     && !validators.OfType<RequiredAttribute>().Any())
            {
                // A non-nullable value type is always required. Left implicit, the client-side
                // provider creates its own RequiredAttribute with the framework's English text.
                // Declaring it here changes nothing about what is accepted (the value can never
                // be null) and lets the message below apply.
                validators.Add(new RequiredAttribute());
            }

            foreach (var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
            {
                // An explicit message, or one already resolved elsewhere, is the author's choice.
                if (!string.IsNullOrEmpty(attribute.ErrorMessage)
                    || attribute.ErrorMessageResourceType is not null
                    || !string.IsNullOrEmpty(attribute.ErrorMessageResourceName))
                    continue;

                var name = ResourceNameFor(attribute);
                if (name is null) continue;

                attribute.ErrorMessageResourceType = typeof(FrameworkValidationMessages);
                attribute.ErrorMessageResourceName = name;
            }
        }
    }
}
