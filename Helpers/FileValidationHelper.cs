namespace GharsPlatform.Helpers;

/// <summary>
/// Central upload/URL validation used by Agenda media, KPI evidence, Library, Gallery and
/// external-survey report uploads. Keeps helper-style architecture (no service layer).
/// </summary>
public static class FileValidationHelper
{
    public sealed record ValidationProfile(string[] Extensions, string[] MimePrefixes, long MaxBytes);

    public static readonly ValidationProfile Image = new(
        new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" },
        new[] { "image/" },
        10 * 1024 * 1024);

    public static readonly ValidationProfile Pdf = new(
        new[] { ".pdf" },
        new[] { "application/pdf" },
        25 * 1024 * 1024);

    /// <summary>Photos + videos (Agenda supporting media, gallery uploads).</summary>
    public static readonly ValidationProfile Media = new(
        new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".webm", ".mov" },
        new[] { "image/", "video/" },
        200 * 1024 * 1024);

    /// <summary>Library main file: PDF preferred, video allowed.</summary>
    public static readonly ValidationProfile LibraryFile = new(
        new[] { ".pdf", ".mp4", ".webm" },
        new[] { "application/pdf", "video/" },
        200 * 1024 * 1024);

    /// <summary>KPI supporting evidence: reports, spreadsheets, tables, scans.</summary>
    public static readonly ValidationProfile Evidence = new(
        new[] { ".pdf", ".xls", ".xlsx", ".csv", ".doc", ".docx", ".png", ".jpg", ".jpeg" },
        new[] { "application/", "image/", "text/csv" },
        25 * 1024 * 1024);

    /// <summary>
    /// Validates an upload against a profile. Returns null when valid, otherwise a bilingual error message.
    /// </summary>
    public static string? Validate(IFormFile? file, ValidationProfile profile, bool isArabic)
    {
        if (file is null || file.Length == 0) return null; // absence is handled by callers' required rules

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !profile.Extensions.Contains(ext))
        {
            return isArabic
                ? $"نوع الملف غير مسموح ({ext}). الأنواع المسموحة: {string.Join(", ", profile.Extensions)}"
                : $"File type not allowed ({ext}). Allowed types: {string.Join(", ", profile.Extensions)}";
        }

        var contentType = file.ContentType ?? "";
        if (profile.MimePrefixes.Length > 0 && !profile.MimePrefixes.Any(p => contentType.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return isArabic
                ? "محتوى الملف لا يطابق النوع المسموح."
                : "The file content type does not match an allowed type.";
        }

        if (file.Length > profile.MaxBytes)
        {
            var maxMb = profile.MaxBytes / (1024 * 1024);
            return isArabic
                ? $"حجم الملف يتجاوز الحد الأقصى ({maxMb} ميجابايت)."
                : $"File exceeds the maximum size of {maxMb} MB.";
        }

        return null;
    }

    /// <summary>
    /// Saves a validated upload with a GUID-based safe filename (never trusts the original name)
    /// and returns the web-relative path.
    /// </summary>
    public static async Task<string> SaveAsync(IFormFile file, string webRootPath, string relativeFolder)
    {
        var folder = relativeFolder.Trim('/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.Combine(webRootPath, folder);
        Directory.CreateDirectory(root);
        var safeName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        var fullPath = Path.Combine(root, safeName);
        await using var fs = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fs);
        return "/" + relativeFolder.Trim('/') + "/" + safeName;
    }

    /// <summary>
    /// True only for absolute http/https URLs (blocks javascript:, data:, file: and relative schemes).
    /// Used for external survey links, library external URLs and gallery external media links.
    /// </summary>
    public static bool IsSafeHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
