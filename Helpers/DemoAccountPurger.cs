using GharsPlatform.Data;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Removes the per-organization demo accounts that demo seeding used to create, leaving the two it
/// creates now — <c>democlub@ghars.local</c> and <c>demoentity@ghars.local</c> — and the demo
/// administrators.
/// </summary>
/// <remarks>
/// <para>
/// Demo seeding once created one account per approved organization: seventeen
/// <c>partner-&lt;name&gt;@ghars.local</c>, seven <c>club-&lt;name&gt;@ghars.local</c>, and
/// <c>club1@ghars.local</c>. <see cref="DbSeeder"/> no longer makes them, but a database seeded before
/// that change still holds them, and a seeder cannot remove what it has stopped creating. That is what
/// this command is for.
/// </para>
/// <para><b>Why the accounts are worth removing rather than leaving.</b></para>
/// <para>
/// They share a password published in this repository's git history, and they are named after real
/// clubs and implementing entities — so an account that any organization would reasonably take for its
/// own is exactly the account whose password anybody can read. On a Development database that is
/// clutter. On any database reachable from outside one, it is a way in.
/// </para>
/// <para>The safeties, and why each one is here:</para>
/// <list type="bullet">
/// <item>Only addresses on <see cref="DbSeeder.DemoEmailDomain"/> are ever considered. The domain is
/// not configurable and is not read from an argument: a typo in a command line must not be able to
/// widen this command's reach to a real account.</item>
/// <item><see cref="DbSeeder.RetainedDemoEmails"/> — the demo administrators and the two organization
/// accounts — are excluded by address, so the account a developer signs in with survives.</item>
/// <item>An account holding an administrative role is refused even if it is on the demo domain and not
/// on the retain list, because losing the last administrator locks everybody out of the admin screens
/// and there is no self-service way back in.</item>
/// <item>Nothing is written without <c>--commit</c>. The first run prints exactly what it would delete,
/// and what each account authored.</item>
/// <item><c>--production</c> is required whenever the target is not a local SQL Server, regardless of
/// the hosting environment.</item>
/// </list>
/// <para>
/// <b>What is deleted, and what is not.</b> The account row and its
/// <see cref="Models.Core.OrganizationAdminLink"/> rows go. Content the account authored — bookings,
/// activities, uploads — stays, because deleting a demo booking would change the reporting figures
/// this data exists to demonstrate. The authored rows keep a user id that no longer resolves, which is
/// what already happens when an administrator deletes a user from <c>Admin -&gt; Users</c>. The counts
/// are printed before anything is written so that this is a decision rather than a surprise.
/// </para>
/// <code>
/// dotnet run -- purge-demo-accounts
/// dotnet run -- purge-demo-accounts --commit
/// </code>
/// </remarks>
public static class DemoAccountPurger
{
    /// <summary>Roles that make an account too important to delete on a name match alone.</summary>
    private static readonly string[] AdministrativeRoles = { RoleNames.SuperAdmin, RoleNames.DscAdmin };

    private sealed record Doomed(
        ApplicationUser User,
        int LinkCount,
        int ActivityCount,
        int BookingCount);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        bool commit,
        bool allowProduction)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DemoAccountPurger");
        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var connection = db.Database.GetDbConnection();
        var isRemote = !DatabaseTarget.IsLocalServer(connection.DataSource);

        // The environment says how the process was configured; the connection string says what is about
        // to be written to. When they disagree the connection string is the one that matters.
        if ((isRemote || !environment.IsDevelopment()) && !allowProduction)
        {
            logger.LogError(
                "purge-demo-accounts refused: the target is {Server}/{Database} in the {Environment} " +
                "environment. Re-run with --production to confirm you mean to delete accounts there.",
                connection.DataSource, connection.Database, environment.EnvironmentName);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Environment : {environment.EnvironmentName}");
        Console.WriteLine($"Server      : {connection.DataSource}");
        Console.WriteLine($"Database    : {connection.Database}");
        Console.WriteLine($"Scope       : {DbSeeder.DemoEmailDomain} accounts only");
        Console.WriteLine(commit
            ? "Mode        : COMMIT — accounts will be deleted"
            : "Mode        : DRY RUN — nothing will be written (add --commit to apply)");

        // EndsWith on the demo domain, evaluated in the database, is the whole scope of this command.
        var demoUsers = await userManager.Users
            .Where(x => x.Email != null && x.Email.EndsWith(DbSeeder.DemoEmailDomain))
            .OrderBy(x => x.Email)
            .ToListAsync();

        var retained = DbSeeder.RetainedDemoEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keep = new List<ApplicationUser>();
        var candidates = new List<ApplicationUser>();

        foreach (var user in demoUsers)
        {
            if (retained.Contains(user.Email!)) { keep.Add(user); continue; }

            // Defence in depth: the retain list is the intended guard, and this catches an
            // administrator account somebody created under a name the list does not know about.
            var roles = await userManager.GetRolesAsync(user);
            if (roles.Any(r => AdministrativeRoles.Contains(r)))
            {
                keep.Add(user);
                logger.LogWarning(
                    "{Email} is on the demo domain but holds {Role}; it will not be deleted. Remove it from " +
                    "Admin -> Users if it really is surplus.",
                    user.Email, string.Join(", ", roles));
                continue;
            }

            candidates.Add(user);
        }

        var doomed = new List<Doomed>();
        foreach (var user in candidates)
        {
            doomed.Add(new Doomed(
                user,
                await db.OrganizationAdminLinks.CountAsync(x => x.UserId == user.Id),
                await db.Activities.CountAsync(x => x.CreatedByUserId == user.Id),
                await db.BookingRequests.CountAsync(x => x.RequestedByUserId == user.Id)));
        }

        Console.WriteLine();
        Console.WriteLine($"Keep ({keep.Count})");
        if (keep.Count == 0) Console.WriteLine("   (none)");
        foreach (var user in keep) Console.WriteLine($"   {user.Email}");

        Console.WriteLine();
        Console.WriteLine($"Delete ({doomed.Count})");
        if (doomed.Count == 0)
        {
            Console.WriteLine("   (none)");
            Console.WriteLine();
            Console.WriteLine("Nothing to do: this database holds no surplus demo accounts.");
            return 0;
        }

        var width = Math.Max(doomed.Max(x => x.User.Email!.Length), 5);
        Console.WriteLine($"   {"Email".PadRight(width)}  links  activities  bookings");
        Console.WriteLine($"   {new string('-', width)}  -----  ----------  --------");
        foreach (var row in doomed)
        {
            Console.WriteLine(
                $"   {row.User.Email!.PadRight(width)}  {row.LinkCount,5}  {row.ActivityCount,10}  {row.BookingCount,8}");
        }

        var authored = doomed.Sum(x => x.ActivityCount) + doomed.Sum(x => x.BookingCount);
        if (authored > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{authored} rows were authored by these accounts and are NOT deleted. They keep a user id");
            Console.WriteLine("that will no longer resolve, exactly as when a user is deleted from Admin -> Users.");
        }

        if (!commit)
        {
            Console.WriteLine();
            Console.WriteLine("Nothing was written. Re-run with --commit to apply this plan.");
            return 0;
        }

        var deleted = 0;
        var failed = 0;

        foreach (var row in doomed)
        {
            // The link first, and in the same SaveChanges as the delete would be ideal — but the link
            // table is not an Identity FK, so UserManager.DeleteAsync leaves it behind. An orphan link
            // still counts as "this organization has somebody attached", which is enough to make
            // seed-organization-accounts skip creating the real account for it.
            var links = await db.OrganizationAdminLinks.Where(x => x.UserId == row.User.Id).ToListAsync();
            db.OrganizationAdminLinks.RemoveRange(links);
            await db.SaveChangesAsync();

            var result = await userManager.DeleteAsync(row.User);
            if (!result.Succeeded)
            {
                logger.LogError(
                    "Could not delete {Email}: {Errors}",
                    row.User.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
                failed++;
                continue;
            }

            deleted++;
        }

        Console.WriteLine();
        Console.WriteLine($"Deleted {deleted}, failed {failed}.");

        if (deleted > 0)
        {
            Console.WriteLine();
            Console.WriteLine("The organizations those accounts were attached to now have no account. That is the");
            Console.WriteLine("intended state: real accounts come from seed-organization-accounts or");
            Console.WriteLine("seed-platform-users, against a real domain.");
        }

        return failed == 0 ? 0 : 1;
    }
}
