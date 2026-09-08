using GharsPlatform.Models.Core;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace GharsPlatform.ViewModels;

/// <summary>
/// One piece of awareness or educational content an implementing entity contributes to the Ghars
/// Channel.
///
/// There is no OrganizationId: the owning entity is resolved server-side from the authenticated user's
/// organization links, so no posted value can publish content on another entity's behalf. There is no
/// publication or approval field either — only a DSC approval makes channel content visible.
///
/// Validation convention as elsewhere in this project: nullable properties, no message-bearing
/// DataAnnotations, every rule in <see cref="Validate"/> so it is evaluated per request in the
/// caller's language.
/// </summary>
public class ChannelContentVm : IValidatableObject
{
    public int Id { get; set; }

    public int SeasonId { get; set; }

    public string? TitleEn { get; set; }
    public string? TitleAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? DescriptionAr { get; set; }

    public GalleryMediaType MediaType { get; set; } = GalleryMediaType.Video;
    public ChannelCategory ChannelCategory { get; set; } = Models.Core.ChannelCategory.Awareness;

    /// <summary>Photo or video only. The channel takes no documents — those belong in the Digital Library.</summary>
    public IFormFile? File { get; set; }

    /// <summary>An approved external video link, validated as http/https only.</summary>
    public string? ExternalUrl { get; set; }

    /// <summary>
    /// Instead of uploading a booklet, a partner may point at their existing Digital Library
    /// publication. Nothing is copied; the channel card links through to the library.
    /// </summary>
    public int? LibraryItemId { get; set; }

    public DateTime MediaDate { get; set; } = DateTime.Today;

    /// <summary>True when an item already holds a stored file, so the upload is optional on edit.</summary>
    public bool HasExistingFile { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        string T(string en, string arabic) => ar ? arabic : en;

        if (SeasonId <= 0)
            yield return new ValidationResult(T("Select a valid sports season.", "يرجى اختيار موسم رياضي صالح."), new[] { nameof(SeasonId) });

        if (string.IsNullOrWhiteSpace(TitleEn))
            yield return new ValidationResult(T("Enter the English title.", "يرجى إدخال العنوان بالإنجليزية."), new[] { nameof(TitleEn) });

        if (string.IsNullOrWhiteSpace(TitleAr))
            yield return new ValidationResult(T("Enter the Arabic title.", "يرجى إدخال العنوان بالعربية."), new[] { nameof(TitleAr) });

        foreach (var (value, field, max, label) in new (string?, string, int, string)[]
        {
            (TitleEn, nameof(TitleEn), 250, T("English title", "العنوان بالإنجليزية")),
            (TitleAr, nameof(TitleAr), 250, T("Arabic title", "العنوان بالعربية")),
            (DescriptionEn, nameof(DescriptionEn), 2000, T("English description", "الوصف بالإنجليزية")),
            (DescriptionAr, nameof(DescriptionAr), 2000, T("Arabic description", "الوصف بالعربية")),
            (ExternalUrl, nameof(ExternalUrl), 700, T("External link", "الرابط الخارجي"))
        })
        {
            if (value is not null && value.Length > max)
                yield return new ValidationResult(
                    T($"{label} must be {max} characters or fewer.", $"يجب ألا يتجاوز حقل \"{label}\" {max} حرفاً."),
                    new[] { field });
        }

        if (!Helpers.ChannelWorkflow.PartnerMediaTypes.Contains(MediaType))
            yield return new ValidationResult(
                T("Choose a photo or a video.", "يرجى اختيار صورة أو فيديو."),
                new[] { nameof(MediaType) });

        if (!Helpers.ChannelWorkflow.PartnerCategories.Contains(ChannelCategory))
            yield return new ValidationResult(
                T("Choose an awareness or educational category.", "يرجى اختيار تصنيف توعوي أو تعليمي."),
                new[] { nameof(ChannelCategory) });

        // Exactly one way of carrying the content must be present: an uploaded file, an approved
        // external link, or a Digital Library publication to point at.
        var hasUpload = File is { Length: > 0 } || HasExistingFile;
        var hasLink = !string.IsNullOrWhiteSpace(ExternalUrl);
        var hasLibrary = LibraryItemId.HasValue && LibraryItemId.Value > 0;

        if (!hasUpload && !hasLink && !hasLibrary)
            yield return new ValidationResult(
                T("Upload a photo or video, add an external video link, or link a Digital Library publication.",
                  "يرجى رفع صورة أو فيديو، أو إضافة رابط فيديو خارجي، أو الربط بإصدار من المكتبة الرقمية."),
                new[] { nameof(File) });

        if (hasLink && !Helpers.FileValidationHelper.IsSafeHttpUrl(ExternalUrl))
            yield return new ValidationResult(
                T("The link must be a valid http or https address.", "يجب أن يكون الرابط بصيغة http أو https صالحة."),
                new[] { nameof(ExternalUrl) });

        if (MediaDate == default)
            yield return new ValidationResult(T("Enter a valid date.", "يرجى إدخال تاريخ صالح."), new[] { nameof(MediaDate) });
    }
}
