using GharsPlatform.Data;
using GharsPlatform.Models.Core;
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

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Viewer(int id)
        {
            var item = await _db.LibraryItems
                .Include(x => x.LibraryCategory)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && x.IsPublished);

            if (item == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(item.FilePath))
                return NotFound();

            return PartialView("_LibraryViewerModal", item);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Stream(int id)
        {
            var item = await _db.LibraryItems
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && x.IsPublished);

            if (item == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(item.FilePath))
                return NotFound();

            var relativePath = item.FilePath.TrimStart('~', '/').Replace("/", Path.DirectorySeparatorChar.ToString());
            var fullPath = Path.Combine(_env.WebRootPath, relativePath);

            if (!System.IO.File.Exists(fullPath))
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

            Response.Headers["Content-Disposition"] = "inline";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            return File(stream, contentType, enableRangeProcessing: true);
        }
    }
}