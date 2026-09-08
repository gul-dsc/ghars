using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GharsPlatform.Controllers;

/// <summary>
/// The only route through which protected documents are served. Every action resolves the *record*
/// from the database first, authorises the current user against it, and only then streams the bytes.
///
/// Authorisation is never based on knowing a GUID or path — a storage key is an identifier, not a
/// credential. Every failure (missing record, missing file, wrong organization, wrong role) returns a
/// bare 404 so the endpoint cannot be used to probe which document ids exist.
/// </summary>
[Authorize]
[Route("protected-files")]
public class ProtectedFilesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ProtectedFilesController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db; _env = env;
    }

    private bool IsDscAdmin => User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.DscAdmin);

    private async Task<bool> BelongsToUserOrgAsync(int organizationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return false;

        // Ownership is checked inside the query against the authenticated user's links — the posted id
        // is only ever a filter, never a source of authority.
        return await _db.OrganizationAdminLinks
            .AnyAsync(x => x.UserId == userId && x.OrganizationId == organizationId);
    }

    /// <summary>
    /// KPI supporting evidence. Club Admins may open evidence for their own club's submissions only;
    /// DSC Admin / Super Admin may open any club's evidence under their existing review authority.
    /// Partner Admin, Speaker and Viewer have no business requirement for KPI evidence and get 404.
    /// </summary>
    [HttpGet("kpi/{id:int}")]
    public async Task<IActionResult> KpiEvidence(int id)
    {
        var doc = await _db.KpiDocuments
            .Include(x => x.KpiSubmission)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (doc?.KpiSubmission is null) return NotFound();

        if (!IsDscAdmin)
        {
            if (!User.IsInRole(RoleNames.ClubAdmin) && !User.IsInRole(RoleNames.AcademyAdmin)) return NotFound();
            if (!await BelongsToUserOrgAsync(doc.KpiSubmission.OrganizationId)) return NotFound();
        }

        return Stream(doc.FilePath, doc.OriginalFileName);
    }

    /// <summary>
    /// Organization licence / supporting documents. Visible to the owning organization's linked admins
    /// and to DSC reviewers (who must inspect them to approve a registration).
    /// </summary>
    [HttpGet("organization/{id:int}")]
    public async Task<IActionResult> OrganizationDocument(int id)
    {
        var doc = await _db.OrganizationDocuments.FirstOrDefaultAsync(x => x.Id == id);
        if (doc is null) return NotFound();

        if (!IsDscAdmin && !await BelongsToUserOrgAsync(doc.OrganizationId)) return NotFound();

        return Stream(doc.FilePath, doc.OriginalFileName);
    }

    /// <summary>
    /// Official (external) survey analysis report — CONDITIONAL: readable by any signed-in user once DSC
    /// has published it, and by DSC reviewers only while it is still unpublished/under review.
    /// </summary>
    [HttpGet("survey-report/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> SurveyReport(int id)
    {
        var survey = await _db.ExternalSurveys.FirstOrDefaultAsync(x => x.Id == id);
        if (survey is null || string.IsNullOrWhiteSpace(survey.ReportPdfPath)) return NotFound();

        var published = survey.IsReportPublished && survey.IsActive;
        if (!published && !IsDscAdmin) return NotFound();

        return Stream(survey.ReportPdfPath, $"{survey.TitleEn}.pdf", inline: true);
    }

    /// <summary>
    /// Ghars Channel media, held as gallery rows. CONDITIONAL: a visible item is public — the channel is
    /// a public showcase and must keep working anonymously.
    ///
    /// "Visible" is <see cref="ChannelWorkflow.IsPubliclyVisible"/> and nothing else, so this endpoint
    /// enforces exactly the rule the listing pages apply: published, AND either outside the partner
    /// review workflow or approved by DSC. A partner's draft, submitted, returned, rejected or withdrawn
    /// item is therefore not served to the public even when its URL is known — knowing a storage key was
    /// never a credential.
    ///
    /// Anything not publicly visible is readable solely by the owning organization's linked admins (so a
    /// partner can still preview its own draft, and a club can still see media DSC has hidden) and by
    /// DSC reviewers.
    ///
    /// Files are neither moved nor duplicated: the gallery row stays the single record of the media, and
    /// static access to /uploads/agenda and /uploads/channel is denied in Program.cs so this endpoint is
    /// the only way in.
    /// </summary>
    [HttpGet("gallery/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GalleryMedia(int id)
    {
        var item = await _db.GalleryItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null || string.IsNullOrWhiteSpace(item.FilePath)) return NotFound();

        if (!ChannelWorkflow.IsPubliclyVisible(item))
        {
            if (User.Identity?.IsAuthenticated != true) return NotFound();

            // Media with no owning organization (DSC/press uploads) is DSC-only while withheld.
            if (!IsDscAdmin)
            {
                if (item.OrganizationId is null) return NotFound();
                if (!await BelongsToUserOrgAsync(item.OrganizationId.Value)) return NotFound();
            }
        }

        return Stream(item.FilePath, null, inline: true);
    }

    /// <summary>
    /// Streams a stored file after the caller has already been authorised. Missing files return 404
    /// rather than an error page, so a broken row never leaks its storage key or server path.
    /// </summary>
    private IActionResult Stream(string? storedPath, string? originalFileName, bool inline = false)
    {
        var full = ProtectedFileStore.ResolvePhysicalPath(storedPath, _env);
        if (full is null || !System.IO.File.Exists(full)) return NotFound();

        var downloadName = ProtectedFileStore.SafeDownloadName(originalFileName, storedPath!);
        var contentType = ProtectedFileStore.ContentTypeFor(storedPath);

        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);

        // The declared content type is authoritative: without this a browser may sniff an uploaded file
        // into something executable in the site's own origin.
        Response.Headers.XContentTypeOptions = "nosniff";

        if (inline && ProtectedFileStore.IsInlineSafe(contentType))
        {
            Response.Headers.ContentDisposition = $"inline; filename=\"{downloadName}\"";
            return File(stream, contentType);
        }

        return File(stream, contentType, downloadName);
    }
}
