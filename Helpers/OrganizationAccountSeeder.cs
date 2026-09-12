using System.Security.Cryptography;
using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Creates the sign-in account each approved club and implementing entity needs, one per organization,
/// with a freshly generated password per account.
/// </summary>
/// <remarks>
/// <para>
/// A production database arrives with organizations but no organization accounts: the roster is loaded
/// by <see cref="OrganizationReconciler"/>, while the accounts that exist in Development come from
/// <see cref="Data.DbSeeder"/>, which never runs outside Development. Until this command is run, the
/// approved organizations exist as records that nobody can sign in as.
/// </para>
/// <para>
/// The Development demo accounts are emphatically not the thing to copy across. They live at
/// <c>@ghars.local</c> and share four passwords that are permanently published in this repository git
/// history. This command exists so that the production accounts are new accounts with new secrets,
/// rather than the demo set moved somewhere it can do harm.
/// </para>
/// <para><b>Why the organization link matters more than it looks.</b></para>
/// <para>
/// Every scoped surface in the application — bookings, agenda, attendance, KPI, gallery, annual
/// reports, protected file downloads — answers "which organization is this user?" from
/// <see cref="OrganizationAdminLink"/>, and so does <see cref="WorkspaceContext"/>.
/// <c>ApplicationUser.PrimaryOrganizationId</c> is a convenience field that almost nothing reads. An
/// account created through <c>Admin -&gt; Users</c> gets the field but not the link, so it signs in
/// successfully and then sees an empty workspace. This command always writes both, and repairs an
/// existing account that is missing its link.
/// </para>
/// <para>Run it, like the other operational commands, instead of the web host:</para>
/// <code>
/// dotnet run -- seed-organization-accounts --domain dubaisc.ae --production
/// dotnet run -- seed-organization-accounts --domain dubaisc.ae --production --commit
/// </code>
/// <para>The safeties, and why each one is here:</para>
/// <list type="bullet">
/// <item>Nothing is written without <c>--commit</c>. The first run prints the exact plan, the same way
/// the reconciler does, because "create two dozen accounts" is not a sentence to run on trust.</item>
/// <item><c>--production</c> is required whenever the target is not a local SQL Server, regardless of
/// the hosting environment. This command is expected to be run from a developer machine against a
/// remote database, where <c>ASPNETCORE_ENVIRONMENT</c> would otherwise say Development and quietly
/// waive the confirmation.</item>
/// <item>The domain is required and never defaulted. A demo domain — anything under <c>.local</c> —
/// is refused outright, so the published demo credentials cannot be recreated by accident.</item>
/// <item>An existing account is never given a new password. An organization that already has a linked
/// account is skipped entirely; one whose account exists but is missing the link or the role has those
/// added and its password left alone.</item>
/// <item>Each password is generated independently from a CSPRNG. There is no shared password, and none
/// of them is derived from the organization name.</item>
/// </list>
/// <para>
/// The passwords are printed once, at the end of a committed run, and are stored nowhere. There is no
/// way to recover one afterwards; a lost password is reset from <c>Admin -&gt; Users</c>.
/// </para>
/// </remarks>
public static class OrganizationAccountSeeder
{
    /// <summary>What one organization needs done to it, decided before anything is written.</summary>
    private enum PlannedAction
    {
        /// <summary>No account exists for this organization: create one.</summary>
        Create,

        /// <summary>An account exists but is missing its link or role: repair those, keep the password.</summary>
        Repair,

        /// <summary>Already has a linked account: leave it entirely alone.</summary>
        Skip
    }

    private sealed record Plan(
        Organization Organization,
        PlannedAction Action,
        string Email,
        string Role,
        ApplicationUser? ExistingUser,
        string Note);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        string? domain,
        bool commit,
        bool allowProduction)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("OrganizationAccountSeeder");
        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var connection = db.Database.GetDbConnection();
        var isRemote = !DatabaseTarget.IsLocalServer(connection.DataSource);

        // The environment says how the process was configured; the connection string says what is about
        // to be written to. When they disagree the connection string is the one that matters — running
        // this locally against a remote server is the expected way to use it.
        if ((isRemote || !environment.IsDevelopment()) && !allowProduction)
        {
            logger.LogError(
                "seed-organization-accounts refused: the target is {Server}/{Database} in the {Environment} " +
                "environment. Re-run with --production to confirm you mean to create accounts there.",
                connection.DataSource, connection.Database, environment.EnvironmentName);
            return 1;
        }

        if (!TryValidateDomain(domain, logger, out var cleanDomain)) return 1;

        // Roles are seeded at startup, in every environment. If they are absent the application has
        // never successfully started against this database, and AddToRoleAsync would fail per account
        // — leaving behind users who can sign in but hold no authority at all.
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var required in new[] { RoleNames.ClubAdmin, RoleNames.PartnerAdmin })
        {
            if (await roleManager.RoleExistsAsync(required)) continue;

            logger.LogError(
                "seed-organization-accounts refused: the {Role} role does not exist in this database. Start the " +
                "application once against it first — roles are seeded at startup in every environment.",
                required);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Environment : {environment.EnvironmentName}");
        Console.WriteLine($"Server      : {connection.DataSource}");
        Console.WriteLine($"Database    : {connection.Database}");
        Console.WriteLine($"Domain      : {cleanDomain}");
        Console.WriteLine(commit
            ? "Mode        : COMMIT — accounts will be created"
            : "Mode        : DRY RUN — nothing will be written (add --commit to apply)");

        var clubs = await db.Organizations.ApprovedClubs().ToListAsync();
        var partners = await db.Organizations.ApprovedPartners().ToListAsync();

        // Which organizations already have somebody attached, asked once rather than per organization.
        var linkedOrgIds = (await db.OrganizationAdminLinks.Select(x => x.OrganizationId).Distinct().ToListAsync())
            .ToHashSet();

        var plans = new List<Plan>();
        foreach (var club in clubs)
            plans.Add(await PlanForAsync(userManager, club, RoleNames.ClubAdmin, "club", cleanDomain, linkedOrgIds));
        foreach (var partner in partners)
            plans.Add(await PlanForAsync(userManager, partner, RoleNames.PartnerAdmin, "partner", cleanDomain, linkedOrgIds));

        if (plans.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No approved clubs or implementing entities exist in this database.");
            Console.WriteLine("Load the roster first: reconcile-organizations --production --commit");
            return 1;
        }

        // Two organizations whose names differ only in punctuation would slug to the same address, and
        // the second CreateAsync would fail halfway through a committed run. Caught before anything is
        // written, while the fix is still just "give one of them a different address".
        var duplicates = plans
            .Where(x => x.Action == PlannedAction.Create)
            .GroupBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count > 0)
        {
            Console.WriteLine();
            foreach (var group in duplicates)
            {
                logger.LogError(
                    "Refused: {Email} would be shared by {Count} organizations ({Names}). Rename one, or create " +
                    "these accounts individually in Admin -> Users.",
                    group.Key, group.Count(), string.Join(", ", group.Select(x => x.Organization.NameEn)));
            }

            return 1;
        }

        PrintSection("Create", plans.Where(x => x.Action == PlannedAction.Create).ToList());
        PrintSection("Repair", plans.Where(x => x.Action == PlannedAction.Repair).ToList());
        PrintSection("Leave alone", plans.Where(x => x.Action == PlannedAction.Skip).ToList());

        if (!commit)
        {
            Console.WriteLine();
            Console.WriteLine("Nothing was written. Re-run with --commit to apply this plan.");
            return 0;
        }

        var created = new List<(string Organization, string Email, string Password)>();
        var repaired = 0;
        var failed = 0;

        foreach (var plan in plans)
        {
            switch (plan.Action)
            {
                case PlannedAction.Create:
                {
                    var password = GeneratePassword();
                    var user = new ApplicationUser
                    {
                        UserName = plan.Email,
                        Email = plan.Email,
                        FullName = $"{plan.Organization.NameEn} {(plan.Role == RoleNames.ClubAdmin ? "Club" : "Partner")} Admin",
                        PreferredLanguage = "en",
                        EmailConfirmed = true,
                        PrimaryOrganizationId = plan.Organization.Id,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    var result = await userManager.CreateAsync(user, password);
                    if (!result.Succeeded)
                    {
                        // Descriptions only: the generated password is never echoed, even on failure.
                        logger.LogError(
                            "Could not create {Email} for {Organization}: {Errors}",
                            plan.Email, plan.Organization.NameEn,
                            string.Join(", ", result.Errors.Select(e => e.Description)));
                        failed++;
                        continue;
                    }

                    // An account that exists but holds no role signs in and is refused by every
                    // [Authorize] in the application, which reads as a broken deployment rather than
                    // as a half-finished account. Say so here instead.
                    var roleAdded = await userManager.AddToRoleAsync(user, plan.Role);
                    if (!roleAdded.Succeeded)
                    {
                        logger.LogError(
                            "{Email} was created but could not be given the {Role} role: {Errors}. Assign it in " +
                            "Admin -> Users before handing the account over.",
                            plan.Email, plan.Role, string.Join(", ", roleAdded.Errors.Select(e => e.Description)));
                        failed++;
                    }

                    EnsureLink(db, plan.Organization, user.Id);
                    created.Add((plan.Organization.NameEn, plan.Email, password));
                    break;
                }

                case PlannedAction.Repair:
                {
                    var user = plan.ExistingUser!;
                    if (!await userManager.IsInRoleAsync(user, plan.Role))
                        await userManager.AddToRoleAsync(user, plan.Role);

                    EnsureLink(db, plan.Organization, user.Id);

                    if (user.PrimaryOrganizationId != plan.Organization.Id)
                    {
                        user.PrimaryOrganizationId = plan.Organization.Id;
                        await userManager.UpdateAsync(user);
                    }

                    Console.WriteLine($"   repaired  {plan.Organization.NameEn} -> {plan.Email} (password unchanged)");
                    repaired++;
                    break;
                }
            }
        }

        await db.SaveChangesAsync();

        if (created.Count > 0)
        {
            var width = Math.Max(created.Max(x => x.Email.Length), 5);
            Console.WriteLine();
            Console.WriteLine("New accounts. These passwords are shown once and are stored nowhere —");
            Console.WriteLine("record them now, then hand each organization its own line.");
            Console.WriteLine();
            Console.WriteLine($"{"Email".PadRight(width)}  Password          Organization");
            Console.WriteLine($"{new string('-', width)}  ----------------  ------------");
            foreach (var row in created)
                Console.WriteLine($"{row.Email.PadRight(width)}  {row.Password}  {row.Organization}");
        }

        Console.WriteLine();
        Console.WriteLine($"Created {created.Count}, repaired {repaired}, failed {failed}.");

        if (created.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Tell each organization to change its password after first sign-in. There is no");
            Console.WriteLine("self-service reset in this application: a lost password is reset from Admin -> Users.");
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Decides what one organization needs, writing nothing. Split out so that the dry run and the
    /// committed run reach their conclusions through identical code — a plan that is printed and then
    /// recomputed differently is worse than no plan at all.
    /// </summary>
    private static async Task<Plan> PlanForAsync(
        UserManager<ApplicationUser> userManager,
        Organization organization,
        string role,
        string prefix,
        string domain,
        HashSet<int> linkedOrgIds)
    {
        var email = BuildEmail(prefix, organization.NameEn, domain);
        var existing = await userManager.Users.FirstOrDefaultAsync(x => x.Email == email);

        if (linkedOrgIds.Contains(organization.Id))
        {
            // Somebody is already attached to this organization. Whether that is this address or a real
            // person's account created by hand, it is not this command's business to touch it.
            return new Plan(organization, PlannedAction.Skip, email, role, existing, "already has a linked account");
        }

        if (existing is not null)
        {
            // The Admin -> Users case: created through the UI, which writes PrimaryOrganizationId but
            // no link, so it signs in to an empty workspace.
            return new Plan(organization, PlannedAction.Repair, email, role, existing, "account exists, link missing");
        }

        return new Plan(organization, PlannedAction.Create, email, role, null, string.Empty);
    }

    private static void EnsureLink(AppDbContext db, Organization organization, string userId)
    {
        if (db.OrganizationAdminLinks.Local.Any(x => x.OrganizationId == organization.Id && x.UserId == userId))
            return;

        if (db.OrganizationAdminLinks.Any(x => x.OrganizationId == organization.Id && x.UserId == userId))
            return;

        db.OrganizationAdminLinks.Add(new OrganizationAdminLink
        {
            OrganizationId = organization.Id,
            UserId = userId,
            RoleHint = organization.OrganizationType,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        });
    }

    private static void PrintSection(string title, IReadOnlyList<Plan> plans)
    {
        Console.WriteLine();
        Console.WriteLine($"{title} ({plans.Count})");
        if (plans.Count == 0)
        {
            Console.WriteLine("   (none)");
            return;
        }

        var width = Math.Min(plans.Max(x => x.Organization.NameEn.Length), 44);
        foreach (var plan in plans)
        {
            var name = plan.Organization.NameEn.Length > width
                ? plan.Organization.NameEn[..width]
                : plan.Organization.NameEn.PadRight(width);
            var note = string.IsNullOrEmpty(plan.Note) ? string.Empty : $"   [{plan.Note}]";
            Console.WriteLine($"   {name}  {plan.Email}{note}");
        }
    }

    /// <summary>
    /// <c>club-al-nasr-club@domain</c> / <c>partner-dubai-police@domain</c>, matching the addressing
    /// used in Development so that the two environments stay readable against each other.
    /// </summary>
    private static string BuildEmail(string prefix, string name, string domain)
    {
        var local = $"{prefix}-{MakeSlug(name)}";

        // RFC 5321 caps a local part at 64 octets, and a couple of the entity names come close enough
        // that a silent overrun is a real possibility rather than a theoretical one.
        if (local.Length > 64) local = local[..64].TrimEnd('-');

        return $"{local}@{domain}";
    }

    private static string MakeSlug(string source)
    {
        var chars = source.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var normalized = new string(chars).Trim('-');
        while (normalized.Contains("--")) normalized = normalized.Replace("--", "-");
        return normalized;
    }

    private static bool TryValidateDomain(string? domain, ILogger logger, out string cleanDomain)
    {
        cleanDomain = (domain ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(cleanDomain))
        {
            logger.LogError(
                "seed-organization-accounts refused: no domain was given. Pass --domain <domain>, for example " +
                "--domain dubaisc.ae. There is deliberately no default: the address is what each organization " +
                "will sign in with.");
            return false;
        }

        if (cleanDomain.Contains('@') || cleanDomain.Contains(' ') || !cleanDomain.Contains('.'))
        {
            logger.LogError("seed-organization-accounts refused: {Domain} is not a domain name.", cleanDomain);
            return false;
        }

        // The demo accounts live at @ghars.local and their passwords are published in this repository
        // git history. Recreating that domain is never the intent, so it is refused rather than warned
        // about.
        if (cleanDomain.EndsWith(".local", StringComparison.Ordinal) || cleanDomain == "localhost")
        {
            logger.LogError(
                "seed-organization-accounts refused: {Domain} is a demo domain. Accounts under .local are the " +
                "Development demo set, whose passwords are public. Use a real domain.",
                cleanDomain);
            return false;
        }

        return true;
    }

    // Deliberately excludes the character pairs a human cannot tell apart in a console font — 0/O and
    // 1/l/I — because every one of these passwords is going to be read off a screen and retyped.
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    // Symbols restricted to those that survive a PowerShell command line, a CSV cell and an email body
    // without quoting or escaping. Identity only requires that one be present, not which one.
    private const string Symbols = "!#%*+-=?";

    private const int PasswordLength = 16;

    /// <summary>
    /// A random password that satisfies the configured Identity rules by construction: one character
    /// from each required class, the remainder from all of them, then shuffled so that the guaranteed
    /// characters do not always land in the same positions.
    /// </summary>
    private static string GeneratePassword()
    {
        const string all = Lower + Upper + Digits + Symbols;

        var chars = new List<char>(PasswordLength)
        {
            Pick(Lower),
            Pick(Upper),
            Pick(Digits),
            Pick(Symbols)
        };

        while (chars.Count < PasswordLength) chars.Add(Pick(all));

        // Fisher-Yates with a CSPRNG. Without it the first four positions would always be
        // lower/upper/digit/symbol in that order, which is a quarter of the password given away.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars.ToArray());
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    /// <summary>Reads <c>--domain &lt;domain&gt;</c> from the command line.</summary>
    public static string? ReadDomainArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--domain", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
