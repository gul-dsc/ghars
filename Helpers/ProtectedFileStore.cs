namespace GharsPlatform.Helpers;

/// <summary>
/// Storage for files that must NOT be served as static content.
///
/// Protected files live in <c>{ContentRoot}/protected-uploads/{category}/{guid}{ext}</c> — i.e. outside
/// <c>wwwroot</c>, so the static-file middleware can never reach them. The database stores only a
/// *storage key* ("kpi/ab12….pdf"), never a physical server path.
///
/// Two path shapes therefore coexist in the database and both must keep working:
///   • legacy  "/uploads/kpi/ab12….pdf"  → resolved under wwwroot (pre-migration rows)
///   • managed "kpi/ab12….pdf"           → resolved under the protected root (post-migration rows)
/// The leading slash is the discriminator. Callers should always go through
/// <see cref="ResolvePhysicalPath"/> rather than combining paths themselves.
/// </summary>
public static class ProtectedFileStore
{
    public const string RootFolderName = "protected-uploads";

    /// <summary>Categories = subfolders. Keep in sync with <see cref="AllCategories"/>.</summary>
    public const string KpiEvidence = "kpi";
    public const string OrganizationDocuments = "org";
    public const string SurveyReports = "surveys";

    public static readonly string[] AllCategories = { KpiEvidence, OrganizationDocuments, SurveyReports };

    public static string Root(IWebHostEnvironment env) => Path.Combine(env.ContentRootPath, RootFolderName);

    /// <summary>
    /// True when the stored value is a managed storage key (protected root) rather than a legacy
    /// web-relative path under wwwroot.
    /// </summary>
    public static bool IsProtectedKey(string? storedPath)
        => !string.IsNullOrWhiteSpace(storedPath) && !storedPath.StartsWith('/');

    /// <summary>
    /// Saves an already-validated upload into the protected root under a GUID filename and returns the
    /// storage key to persist. The original filename is never used on disk.
    /// </summary>
    public static async Task<string> SaveAsync(IFormFile file, IWebHostEnvironment env, string category)
    {
        var safeCategory = NormalizeCategory(category);
        var folder = Path.Combine(Root(env), safeCategory);
        Directory.CreateDirectory(folder);

        var name = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        await using var fs = new FileStream(Path.Combine(folder, name), FileMode.Create);
        await file.CopyToAsync(fs);

        return $"{safeCategory}/{name}";
    }

    /// <summary>
    /// Resolves a stored path to a physical file, or null when the value is unusable or escapes its root.
    /// Handles both managed keys and legacy wwwroot paths. Never returns a path outside the two roots.
    /// </summary>
    public static string? ResolvePhysicalPath(string? storedPath, IWebHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        var trimmed = storedPath.Trim().Replace('\\', '/');
        if (trimmed.Contains("..", StringComparison.Ordinal)) return null;

        var isLegacy = trimmed.StartsWith('/');
        var root = isLegacy ? env.WebRootPath : Root(env);
        if (string.IsNullOrEmpty(root)) return null;

        var relative = trimmed.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative));

        // Containment check: reject anything that resolved outside its own root.
        var rootFull = Path.GetFullPath(root);
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;

        return full;
    }

    /// <summary>Deletes a stored file, ignoring missing files. Returns true when something was removed.</summary>
    public static bool TryDelete(string? storedPath, IWebHostEnvironment env)
    {
        var full = ResolvePhysicalPath(storedPath, env);
        if (full is null || !File.Exists(full)) return false;
        File.Delete(full);
        return true;
    }

    /// <summary>Conservative content type for streaming. Unknown types download rather than render inline.</summary>
    public static string ContentTypeFor(string? fileName) => Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".csv" => "text/csv",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// Types that may be rendered in the browser instead of downloaded. Deliberately a short allow-list of
    /// inert formats: anything that could carry script (HTML, SVG, unknown types) is never served inline.
    /// </summary>
    private static readonly HashSet<string> InlineSafeContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/gif", "image/webp",
        "video/mp4", "video/webm", "video/quicktime"
    };

    public static bool IsInlineSafe(string contentType) => InlineSafeContentTypes.Contains(contentType);

    /// <summary>
    /// A display filename that is safe to place in a Content-Disposition header: the original name
    /// stripped of any path information and header-breaking characters, with the stored extension.
    /// </summary>
    public static string SafeDownloadName(string? originalFileName, string storedPath)
    {
        var extension = Path.GetExtension(storedPath);
        var candidate = Path.GetFileName(originalFileName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(candidate))
            return $"document{extension}";

        var cleaned = new string(candidate.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/' or ';' or ':')).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return $"document{extension}";

        if (!string.IsNullOrEmpty(extension) && !cleaned.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            cleaned += extension;

        return cleaned.Length > 120 ? cleaned[^120..] : cleaned;
    }

    private static string NormalizeCategory(string category)
    {
        var value = (category ?? "").Trim().Trim('/').ToLowerInvariant();
        return AllCategories.Contains(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(category), $"Unknown protected file category '{category}'.");
    }
}
