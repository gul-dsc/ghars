using System.Text.RegularExpressions;
using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Rules for an organization's public logo. A logo is the only organization file anyone may fetch
/// without signing in, so <c>Organization.LogoPath</c> may hold exactly two shapes:
/// <list type="bullet">
/// <item>a site image shipped with the application: <c>/img/clubs|partners|brand/&lt;name&gt;.&lt;image&gt;</c>;</item>
/// <item>an uploaded logo: <c>/uploads/org/&lt;32 hex&gt;.&lt;raster image&gt;</c>, as written by <see cref="SaveUploadAsync"/>.</item>
/// </list>
/// Anything else — a URL, a drive or UNC path, a traversal sequence, a query string, a PDF, an SVG
/// upload — is refused when saved, never served by the <c>/uploads/org</c> gate in Program.cs, and
/// replaced by the default image when rendered.
/// </summary>
/// <remarks>
/// Uploaded logos sit in the same folder as licence and supporting documents uploaded before protected
/// storage, so a free-text path could otherwise point a logo at a private document and publish it. An
/// administrator therefore cannot type an upload path: an organization keeps the upload it already has,
/// or receives a new one through the upload field. A path that names an organization document is
/// refused even if it has the right shape.
/// </remarks>
public static class OrganizationLogo
{
    public const string DefaultImage = "/img/partners/default-partner.svg";
    public const string UploadFolder = "/uploads/org/";

    /// <summary>The folders under wwwroot/img whose images may be chosen as a logo.</summary>
    public static readonly string[] SiteFolders = { "clubs", "partners", "brand" };

    private static readonly Regex SitePattern = new(
        @"^/img/(clubs|partners|brand)/[A-Za-z0-9][A-Za-z0-9_-]*(\.[A-Za-z0-9_-]+)*\.(png|jpe?g|gif|webp|svg)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // SVG is not accepted for uploads: it can carry script and would be served from the site's origin.
    private static readonly Regex UploadPattern = new(
        @"^/uploads/org/[0-9a-fA-F]{32}\.(png|jpe?g|gif|webp)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsSiteImage(string? path) => path is not null && SitePattern.IsMatch(path);

    public static bool IsUpload(string? path) => path is not null && UploadPattern.IsMatch(path);

    /// <summary>True when the path has one of the two permitted shapes. Says nothing about ownership.</summary>
    public static bool IsWellFormed(string? path) => IsSiteImage(path) || IsUpload(path);

    /// <summary>The image to put in an <c>img</c> tag: the logo when it is well formed, otherwise the default.</summary>
    public static string SrcOrDefault(string? path, string fallback = DefaultImage)
        => IsWellFormed(path) ? path! : fallback;

    /// <summary>Site images an administrator may choose from, as stored paths, sorted.</summary>
    public static IReadOnlyList<string> SiteImages(IWebHostEnvironment env)
    {
        var list = new List<string>();
        foreach (var folder in SiteFolders)
        {
            var dir = Path.Combine(env.WebRootPath, "img", folder);
            if (!Directory.Exists(dir)) continue;
            list.AddRange(Directory.EnumerateFiles(dir)
                .Select(f => $"/img/{folder}/{Path.GetFileName(f)}")
                .Where(IsSiteImage));
        }
        return list.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Whether <paramref name="path"/> may be served publicly as a logo: an upload of the right shape
    /// that is the current logo of an organization and is not also recorded as an organization document.
    /// </summary>
    public static async Task<bool> IsServableUploadAsync(AppDbContext db, string path)
    {
        if (!IsUpload(path)) return false;
        return await db.Organizations.AnyAsync(o => o.LogoPath == path)
               && !await db.OrganizationDocuments.IgnoreQueryFilters().AnyAsync(d => d.FilePath == path);
    }

    /// <summary>
    /// Checks a logo path chosen for organization <paramref name="organizationId"/> (0 for a new one).
    /// Returns null when it may be saved, otherwise a bilingual error message.
    /// </summary>
    public static async Task<string?> ValidateChoiceAsync(AppDbContext db, IWebHostEnvironment env, int organizationId, string? path, bool isArabic)
    {
        if (string.IsNullOrEmpty(path)) return null;

        if (IsSiteImage(path))
        {
            var physical = Path.Combine(env.WebRootPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(physical) ? null : Error(isArabic);
        }

        if (IsUpload(path))
        {
            // An upload may only be kept by the organization that already has it, never copied from
            // another organization or picked out of the folder by name.
            var current = organizationId > 0
                ? await db.Organizations.IgnoreQueryFilters().Where(o => o.Id == organizationId).Select(o => o.LogoPath).FirstOrDefaultAsync()
                : null;
            var isOwn = string.Equals(current, path, StringComparison.OrdinalIgnoreCase);
            var isDocument = await db.OrganizationDocuments.IgnoreQueryFilters().AnyAsync(d => d.FilePath == path);
            return isOwn && !isDocument ? null : Error(isArabic);
        }

        return Error(isArabic);
    }

    /// <summary>Validates an uploaded logo file. Returns null when acceptable, otherwise a bilingual error.</summary>
    public static string? ValidateUpload(IFormFile? file, bool isArabic)
        => FileValidationHelper.Validate(file, FileValidationHelper.Image, isArabic);

    /// <summary>Saves a validated logo under wwwroot/uploads/org with a random name and returns its path.</summary>
    public static Task<string> SaveUploadAsync(IFormFile file, IWebHostEnvironment env)
        => FileValidationHelper.SaveAsync(file, env.WebRootPath, UploadFolder.Trim('/'));

    private static string Error(bool isArabic) => isArabic
        ? "اختاروا شعاراً من صور الموقع أو ارفعوا صورة جديدة (PNG أو JPG أو GIF أو WebP)."
        : "Choose a logo from the site images or upload a new image (PNG, JPG, GIF or WebP).";
}
