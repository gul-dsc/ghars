using System.Security.Claims;
using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>Which organization's workspace the signed-in user is working inside.</summary>
public enum WorkspaceKind
{
    /// <summary>A Ghars club.</summary>
    Club = 1,

    /// <summary>A Ghars implementing entity.</summary>
    Partner = 2
}

/// <summary>
/// The organization whose workspace the current request belongs to, with the sports season it is
/// being worked in.
/// </summary>
public sealed record OrganizationWorkspace(Organization Organization, WorkspaceKind Kind, Season? Season);

/// <summary>
/// Resolves the signed-in user's own organization, once per request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a service and not a view helper.</b> The workspace header appears on a dozen screens. If
/// each view resolved the organization itself, the identity shown would be a dozen separate answers
/// to the same question — and the first one to accept an id from the query string would be a
/// spoofing hole in what is meant to be an identity marker. There is one resolution path, it starts
/// from <see cref="ClaimTypes.NameIdentifier"/> on the authenticated principal, and nothing in it
/// reads the request.
/// </para>
/// <para>
/// <b>Presentation only.</b> Nothing here authorizes anything. Every controller keeps its own
/// <c>[Authorize]</c> attributes and its own organization scoping; this exists so a page can say
/// whose workspace it is. A stale or missing link renders no header rather than a wrong one.
/// </para>
/// <para>
/// <b>One query per request.</b> Scoped, and the answer is cached after the first call — including
/// the null answer, so an anonymous visitor on a page that asks twice still costs nothing. The
/// booking catalogue asks once for a club strip while rendering dozens of cards.
/// </para>
/// </remarks>
public sealed class WorkspaceContext
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    private OrganizationWorkspace? _workspace;
    private bool _resolved;

    public WorkspaceContext(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    /// <summary>
    /// The current user's organization workspace, or <c>null</c> for anonymous visitors,
    /// administrators, and any account with no organization link of a matching type.
    /// </summary>
    public async Task<OrganizationWorkspace?> CurrentAsync()
    {
        if (_resolved) return _workspace;
        _resolved = true;

        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        var isClub = user.IsInRole(RoleNames.ClubAdmin);
        var isPartner = user.IsInRole(RoleNames.PartnerAdmin);

        // An administrator has no workspace of their own: they oversee every organization and belong
        // to none of them. Saying "Dubai Sports Council Workspace" over an admin screen would be a
        // different claim than the one this header makes.
        if (!isClub && !isPartner) return null;

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return null;

        // Held by both roles — vanishingly rare, but the club surfaces are the ones a club admin
        // reaches, so the club link wins rather than the lower row id.
        var kind = isClub ? WorkspaceKind.Club : WorkspaceKind.Partner;

        var linked = _db.OrganizationAdminLinks
            .Where(x => x.UserId == userId && x.Organization != null)
            .Select(x => x.Organization!);

        var organization = await (kind == WorkspaceKind.Club
                ? linked.Where(x => x.OrganizationType == OrganizationType.Club
                                    || x.OrganizationType == OrganizationType.PrivateAcademy)
                : linked.Where(x => x.OrganizationType == OrganizationType.GovernmentAuthority
                                    || x.OrganizationType == OrganizationType.OtherPartner))
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();

        if (organization is null) return null;

        var season = await _db.Seasons
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync();

        _workspace = new OrganizationWorkspace(organization, kind, season);
        return _workspace;
    }
}
