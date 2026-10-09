using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Public
{
    [Authorize]
    public class LibraryController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public LibraryController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Index(int? categoryId, LibraryContentType? contentType, int page = 1)
        {
            var categories = await _db.LibraryCategories
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.NameEn)
                .ToListAsync();

            var query = _db.LibraryItems
                .Where(x => !x.IsDeleted && x.IsPublished && x.IsPublic)
                .AsQueryable();

            if (categoryId.HasValue)
            {
                query = query.Where(x => x.LibraryCategoryId == categoryId.Value);
            }
            if (contentType.HasValue)
            {
                query = query.Where(x => x.ContentType == contentType.Value);
            }

            const int pageSize = 9;
            page = Math.Max(1, page);
            var total = await query.CountAsync();
            var items = await query
                .Include(x => x.LibraryCategory)
                .OrderByDescending(x => x.PublicationDate ?? x.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.ContentType = contentType;
            ViewBag.Page = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(items);
        }

        /// <summary>
        /// Who may open an item's file, cover or viewer. Every failure is a bare 404 so ids cannot be
        /// probed.
        ///   • DSC Admin / Super Admin — any item that is not deleted, published or not, so the library
        ///     can be checked before and after publication;
        ///   • anyone else, signed in or not — only an item that is both published and public, which
        ///     is exactly what the public listing shows.
        /// </summary>
        private async Task<LibraryItem?> ReadableItemAsync(int id)
        {
            var item = await _db.LibraryItems
                .Include(x => x.LibraryCategory)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (item is null) return null;

            var isDscAdmin = User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.DscAdmin);
            return isDscAdmin || (item.IsPublished && item.IsPublic) ? item : null;
        }

        /// <summary>The library page's viewer modal. Links to the file by item id only, never by path.</summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Viewer(int id)
        {
            var item = await ReadableItemAsync(id);
            if (item is null || string.IsNullOrWhiteSpace(item.FilePath))
                return NotFound();

            return PartialView("_LibraryViewerModal", item);
        }

        /// <summary>
        /// Streams an item's PDF or video, with range requests so a video can seek. Handles both a
        /// protected storage key and the legacy <c>/uploads/library/…</c> path of an item not yet
        /// migrated; static access to that folder is denied in Program.cs.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Stream(int id)
        {
            var item = await ReadableItemAsync(id);
            if (item is null) return NotFound();

            var fullPath = ProtectedFileStore.ResolvePhysicalPath(item.FilePath, _env);
            if (fullPath is null || !System.IO.File.Exists(fullPath))
                return NotFound();

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = extension switch
            {
                ".pdf" => "application/pdf",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".ogg" => "video/ogg",
                _ => "application/octet-stream"
            };

            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            Response.Headers["Content-Disposition"] = contentType == "application/octet-stream" ? "attachment" : "inline";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!(item.IsPublished && item.IsPublic)) Response.Headers.CacheControl = "no-store";

            return File(stream, contentType, enableRangeProcessing: true);
        }

        /// <summary>An item's uploaded cover image, under the same rule as its file.</summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Cover(int id)
        {
            var item = await ReadableItemAsync(id);
            if (item is null || !IsStoredFile(item.CoverImagePath)) return NotFound();

            var fullPath = ProtectedFileStore.ResolvePhysicalPath(item.CoverImagePath, _env);
            if (fullPath is null || !System.IO.File.Exists(fullPath)) return NotFound();

            var contentType = ProtectedFileStore.ContentTypeFor(fullPath);
            if (!contentType.StartsWith("image/", StringComparison.Ordinal) || !ProtectedFileStore.IsInlineSafe(contentType))
                return NotFound();

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!(item.IsPublished && item.IsPublic)) Response.Headers.CacheControl = "no-store";
            return PhysicalFile(fullPath, contentType);
        }

        /// <summary>
        /// Where a page loads an item's cover from: an uploaded cover (protected key or legacy
        /// /uploads path) goes through <see cref="Cover"/>; a site image such as the brand logo is
        /// used as stored.
        /// </summary>
        public static string? CoverUrl(LibraryItem item)
            => string.IsNullOrWhiteSpace(item.CoverImagePath) ? null
                : IsStoredFile(item.CoverImagePath) ? $"/Library/Cover/{item.Id}"
                : item.CoverImagePath;

        private static bool IsStoredFile(string? path)
            => ProtectedFileStore.IsProtectedKey(path) || GalleryMediaUrls.IsStoredUpload(path);
    }
}
