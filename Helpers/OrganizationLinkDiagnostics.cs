using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Read-only report on organization membership and booking ownership, with an explicitly confirmed
/// cleanup of orphan links. Never runs at startup.
/// </summary>
/// <remarks>
/// <code>
///   dotnet GharsPlatform.dll org-link-diagnostics                       report only
///   dotnet GharsPlatform.dll org-link-diagnostics --cleanup             also list the orphan links that would be removed
///   dotnet GharsPlatform.dll org-link-diagnostics --cleanup --confirm   remove them (one transaction)
/// </code>
/// <c>--confirm</c> refuses in the Production environment unless <c>--production</c> is also given.
/// Output carries record ids and counts only — no names or email addresses.
/// <para>
/// Only <b>orphan links</b> (pointing at an account that no longer exists) are ever removed. The other
/// findings are for a person to review: a link whose holder lacks the organization's workspace role
/// grants no access and receives no organization notifications, and a booking with no recorded
/// implementing organization can no longer be opened by any entity now that program authorship is
/// not an access path.
/// </para>
/// </remarks>
public static class OrganizationLinkDiagnostics
{
    public sealed record Report(
        List<int> OrphanLinkIds,
        List<int> RoleMismatchLinkIds,
        List<int> DeactivatedLinkIds,
        List<int> BookingsWithoutEntity,
        List<int> BookingsWithConflictingEntity);

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var env = services.GetRequiredService<IWebHostEnvironment>();
        var cleanup = args.Contains("--cleanup");
        var confirm = args.Contains("--confirm");

        if (confirm && !cleanup)
        {
            Console.Error.WriteLine("--confirm only applies together with --cleanup.");
            return 2;
        }
        if (confirm && env.IsProduction() && !args.Contains("--production"))
        {
            Console.Error.WriteLine("Refusing to remove links in the Production environment without --production.");
            return 2;
        }

        var report = await BuildReportAsync(db);

        Console.WriteLine();
        Console.WriteLine("Organization link diagnostics (read-only unless --cleanup --confirm)");
        Console.WriteLine(new string('-', 80));
        Print("Orphan links (account no longer exists)", report.OrphanLinkIds, "link");
        Print("Links whose holder lacks the organization's workspace role", report.RoleMismatchLinkIds, "link");
        Print("Links held by deactivated accounts", report.DeactivatedLinkIds, "link");
        Print("Bookings with no implementing organization (no entity can open them)", report.BookingsWithoutEntity, "booking");
        Print("Bookings whose recorded entity differs from the program's entity", report.BookingsWithConflictingEntity, "booking");
        Console.WriteLine(new string('-', 80));

        if (!cleanup) return 0;

        if (!confirm)
        {
            Console.WriteLine($"DRY RUN: --cleanup --confirm would remove {report.OrphanLinkIds.Count} orphan link(s). Nothing was changed.");
            return 0;
        }

        var removed = await RemoveOrphanLinksAsync(db);
        Console.WriteLine($"Removed {removed} orphan link(s).");
        return 0;
    }

    public static async Task<Report> BuildReportAsync(AppDbContext db)
    {
        var orphan = await db.OrganizationAdminLinks
            .Where(l => !db.Users.Any(u => u.Id == l.UserId))
            .OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();

        var links = await db.OrganizationAdminLinks
            .Where(l => db.Users.Any(u => u.Id == l.UserId) && l.Organization != null)
            .Select(l => new { l.Id, l.UserId, l.Organization!.OrganizationType })
            .ToListAsync();
        var userRoles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name }).ToListAsync();
        var rolesByUser = userRoles.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToHashSet());
        var mismatch = links
            .Where(l => !rolesByUser.TryGetValue(l.UserId, out var roles)
                        || !NotificationDispatcher.MemberRolesFor(l.OrganizationType).Any(roles.Contains))
            .Select(l => l.Id).OrderBy(x => x).ToList();

        var deactivatedAfter = DateTimeOffset.UtcNow.AddYears(1);
        var deactivated = await db.OrganizationAdminLinks
            .Where(l => db.Users.Any(u => u.Id == l.UserId && u.LockoutEnd != null && u.LockoutEnd > deactivatedAfter))
            .OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();

        var withoutEntity = await db.BookingRequests
            .Where(b => b.PartnerOrganizationId == null && (b.Activity == null || b.Activity.PartnerOrganizationId == null))
            .OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();

        var conflicting = await db.BookingRequests
            .Where(b => b.PartnerOrganizationId != null && b.Activity != null && b.Activity.PartnerOrganizationId != null
                        && b.Activity.PartnerOrganizationId != b.PartnerOrganizationId)
            .OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();

        return new Report(orphan, mismatch, deactivated, withoutEntity, conflicting);
    }

    /// <summary>Removes links whose account no longer exists, in one transaction. Returns the count.</summary>
    public static async Task<int> RemoveOrphanLinksAsync(AppDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var orphans = await db.OrganizationAdminLinks.Where(l => !db.Users.Any(u => u.Id == l.UserId)).ToListAsync();
        db.OrganizationAdminLinks.RemoveRange(orphans);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return orphans.Count;
    }

    private static void Print(string title, List<int> ids, string noun)
    {
        Console.WriteLine($"{ids.Count,6}  {title}");
        if (ids.Count > 0)
            Console.WriteLine($"        {noun} ids: {string.Join(", ", ids.Take(50))}{(ids.Count > 50 ? $" … (+{ids.Count - 50} more)" : "")}");
    }
}
