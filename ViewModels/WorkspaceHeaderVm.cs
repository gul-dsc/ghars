using Microsoft.AspNetCore.Html;

namespace GharsPlatform.ViewModels;

/// <summary>How much room the organization workspace header is given on a page.</summary>
public enum WorkspaceHeaderVariant
{
    /// <summary>
    /// The identity panel that opens a dashboard: a large logo, the organization name as the page's
    /// leading heading, and the season. One per workspace, on the dashboard only.
    /// </summary>
    Hero = 1,

    /// <summary>
    /// A single line of context above a working screen: small logo, organization name, and the name
    /// of the screen. Keeps a subordinate page connected to its workspace without spending the top
    /// third of the viewport on saying so again.
    /// </summary>
    Compact = 2
}

/// <summary>
/// The presentation options for <c>_OrganizationWorkspaceHeader.cshtml</c>.
/// </summary>
/// <remarks>
/// Deliberately carries no organization and no id. The partial resolves the organization from the
/// authenticated principal through <see cref="Helpers.WorkspaceContext"/>, so no caller — and no
/// query string — can choose whose identity a page displays.
/// </remarks>
public sealed class WorkspaceHeaderVm
{
    public WorkspaceHeaderVariant Variant { get; init; } = WorkspaceHeaderVariant.Compact;

    /// <summary>The screen's own name, shown after the workspace label on a compact header.</summary>
    public string? PageTitleEn { get; init; }

    public string? PageTitleAr { get; init; }

    /// <summary>One line of context under the heading. Optional.</summary>
    public string? SubtitleEn { get; init; }

    public string? SubtitleAr { get; init; }

    /// <summary>Show the active sports season chip when there is one.</summary>
    public bool ShowSeason { get; init; } = true;

    /// <summary>
    /// Optional buttons rendered inside a hero panel, opposite the identity. Supplied as a Razor
    /// template — <c>Actions = @&lt;a class="btn btn-light" href="..."&gt;…&lt;/a&gt;</c> — so a page
    /// keeps its own actions in its own view instead of the header growing a parameter per screen.
    /// </summary>
    public Func<object?, IHtmlContent>? Actions { get; init; }
}
