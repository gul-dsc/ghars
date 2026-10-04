using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Creates the two organization demo accounts, <c>democlub@ghars.local</c> and
/// <c>demoentity@ghars.local</c>, on a database where demo seeding never runs — production.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DbSeeder"/> creates these two only in Development, with the shared demo password. A
/// deployed database therefore has neither, and nothing else can make them correctly: the
/// <c>Admin -&gt; Users</c> screen writes no <see cref="OrganizationAdminLink"/>, and the link is what
/// every scoped screen reads. <see cref="PlatformUserSeeder"/> refuses <c>.local</c> addresses on
/// purpose. This command exists so the Council can test the club and entity workspaces on the live
/// site without borrowing a real nominee's account.
/// </para>
/// <para><b>Each password is new and random — never the demo password.</b> The shared demo password is
/// published in this repository's git history, so it must not reach a deployed environment. A created
/// account gets a password from <see cref="PlatformUserSeeder.GeneratePassword"/>, printed once at the
/// end of a committed run and stored nowhere.</para>
/// <para><b>These are real accounts on a real organization.</b> The demo club is scoped to
/// <see cref="DbSeeder.DemoClubOrganizationNameEn"/> and the demo entity to
/// <see cref="DbSeeder.DemoEntityOrganizationNameEn"/>, the same as in Development. On production
/// that means a test booking is a real booking: the organization's own users see it, and it is
/// counted in the reports until it is cancelled.</para>
/// <list type="bullet">
/// <item>Nothing is written without <c>--commit</c>.</item>
/// <item><c>--production</c> is required whenever the target is not a local SQL Server.</item>
/// <item>An existing account is never given a new password. Its role, link and organization are
/// repaired if missing, and the run says the password was left alone.</item>
/// </list>
/// <code>
/// dotnet run -- seed-demo-org-accounts --production
/// dotnet run -- seed-demo-org-accounts --production --commit
/// </code>
/// </remarks>
public static class DemoOrgAccountSeeder
{
    private sealed record Target(string Email, string FullName, string Role, Organization Organization);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        bool commit,
        bool allowProduction)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DemoOrgAccountSeeder");
        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        var connection = db.Database.GetDbConnection();
        var isRemote = !DatabaseTarget.IsLocalServer(connection.DataSource);

        if ((isRemote || !environment.IsDevelopment()) && !allowProduction)
        {
            logger.LogError(
                "seed-demo-org-accounts refused: the target is {Server}/{Database} in the {Environment} " +
                "environment. Re-run with --production to confirm you mean to create accounts there.",
                connection.DataSource, connection.Database, environment.EnvironmentName);
            return 1;
        }

        foreach (var required in new[] { RoleNames.ClubAdmin, RoleNames.PartnerAdmin })
        {
            if (await roleManager.RoleExistsAsync(required)) continue;

            logger.LogError(
                "seed-demo-org-accounts refused: the {Role} role does not exist in this database. Start the " +
                "application once against it first — roles are seeded at startup in every environment.",
                required);
            return 1;
        }

        var club = await db.Organizations.ApprovedClubs()
            .FirstOrDefaultAsync(x => x.NameEn == DbSeeder.DemoClubOrganizationNameEn);
        var entity = await db.Organizations.ApprovedPartners()
            .FirstOrDefaultAsync(x => x.NameEn == DbSeeder.DemoEntityOrganizationNameEn);

        // Named organizations only, unlike DbSeeder's fallback to the first in the list: on production a
        // demo account landing on whichever club sorts first is a test account inside a real club's
        // workspace that nobody chose.
        if (club is null || entity is null)
        {
            logger.LogError(
                "seed-demo-org-accounts refused: {Missing} is not an approved organization in this database. " +
                "Load the roster first: reconcile-organizations --production --commit",
                club is null ? DbSeeder.DemoClubOrganizationNameEn : DbSeeder.DemoEntityOrganizationNameEn);
            return 1;
        }

        var targets = new[]
        {
            new Target(DbSeeder.DemoClubEmail, "Demo Club Admin", RoleNames.ClubAdmin, club),
            new Target(DbSeeder.DemoEntityEmail, "Demo Entity Admin", RoleNames.PartnerAdmin, entity)
        };

        Console.WriteLine();
        Console.WriteLine($"Environment : {environment.EnvironmentName}");
        Console.WriteLine($"Server      : {connection.DataSource}");
        Console.WriteLine($"Database    : {connection.Database}");
        Console.WriteLine(commit
            ? "Mode        : COMMIT — accounts will be created or repaired"
            : "Mode        : DRY RUN — nothing will be written (add --commit to apply)");
        Console.WriteLine();

        var created = new List<(string Email, string Password, string Organization)>();
        var failed = 0;

        foreach (var target in targets)
        {
            var existing = await userManager.Users.FirstOrDefaultAsync(x => x.Email == target.Email);

            if (existing is null)
            {
                Console.WriteLine($"   create    {target.Email} -> {target.Organization.NameEn} ({target.Role})");
                if (!commit) continue;

                var password = PlatformUserSeeder.GeneratePassword();
                var user = new ApplicationUser
                {
                    UserName = target.Email,
                    Email = target.Email,
                    FullName = target.FullName,
                    PreferredLanguage = "en",
                    EmailConfirmed = true,
                    PrimaryOrganizationId = target.Organization.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    // Descriptions only: the generated password is never echoed, even on failure.
                    logger.LogError("Could not create {Email}: {Errors}", target.Email,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                    failed++;
                    continue;
                }

                if (!(await userManager.AddToRoleAsync(user, target.Role)).Succeeded)
                {
                    logger.LogError("{Email} was created but could not be given the {Role} role.", target.Email, target.Role);
                    failed++;
                }

                await EnsureLinkAsync(db, target.Organization, user.Id);
                created.Add((target.Email, password, target.Organization.NameEn));
                continue;
            }

            var hasRole = await userManager.IsInRoleAsync(existing, target.Role);
            var hasLink = await db.OrganizationAdminLinks
                .AnyAsync(x => x.OrganizationId == target.Organization.Id && x.UserId == existing.Id);
            var hasOrganization = existing.PrimaryOrganizationId == target.Organization.Id;

            if (hasRole && hasLink && hasOrganization)
            {
                Console.WriteLine($"   exists    {target.Email} -> {target.Organization.NameEn} (already correct, password unchanged)");
                continue;
            }

            Console.WriteLine($"   repair    {target.Email} -> {target.Organization.NameEn} (password unchanged)");
            if (!commit) continue;

            if (!hasRole) await userManager.AddToRoleAsync(existing, target.Role);
            if (!hasOrganization)
            {
                existing.PrimaryOrganizationId = target.Organization.Id;
                await userManager.UpdateAsync(existing);
            }
            await EnsureLinkAsync(db, target.Organization, existing.Id);
        }

        if (!commit)
        {
            Console.WriteLine();
            Console.WriteLine("Nothing was written. Re-run with --commit to apply this plan.");
            return 0;
        }

        if (created.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("New accounts. These passwords are shown once and are stored nowhere.");
            Console.WriteLine();
            foreach (var row in created)
                Console.WriteLine($"{row.Email.PadRight(24)}  {row.Password}  {row.Organization}");
        }

        Console.WriteLine();
        Console.WriteLine($"Created {created.Count}, failed {failed}.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task EnsureLinkAsync(AppDbContext db, Organization organization, string userId)
    {
        if (await db.OrganizationAdminLinks.AnyAsync(x => x.OrganizationId == organization.Id && x.UserId == userId))
            return;

        db.OrganizationAdminLinks.Add(new OrganizationAdminLink
        {
            OrganizationId = organization.Id,
            UserId = userId,
            RoleHint = organization.OrganizationType,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        });
        await db.SaveChangesAsync();
    }
}
