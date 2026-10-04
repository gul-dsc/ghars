using System.Security.Cryptography;
using System.Text;
using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Creates the named people each club and implementing entity nominated to use the platform, from a
/// manifest file, one account per person.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OrganizationAccountSeeder"/> creates one generic account per organization —
/// <c>club-al-nasr-club@domain</c> — which is the right thing when an organization has nominated
/// nobody. This command is for when it has: the nomination form asks each organization for a primary
/// and a substitute user, by name, with their own official work address, and those are the addresses
/// those people expect to sign in with. An account per person is also the only version of this that
/// leaves an audit trail worth having, because a booking approved by <c>club-hatta-club@</c> records
/// which organization acted and a booking approved by a named address records who.
/// </para>
/// <para><b>Why a file rather than a list in this repository.</b></para>
/// <para>
/// The manifest is personal data: real names, job titles and work addresses of identifiable people.
/// This repository is public, so the manifest is passed in at run time and is deliberately not
/// committed — see <c>.gitignore</c>. Nothing about the people provisioned by this command is
/// recoverable from source control, which is the intent.
/// </para>
/// <para><b>Manifest format.</b> UTF-8 CSV, with a header row. A byte order mark is tolerated, because
/// Excel writes one and the organization names are matched in Arabic as well as English:</para>
/// <code>
/// Organization,FullName,Email,Language
/// Al Nasr Club,Example Name,first.last@example.ae,ar
/// نادي حتا,Example Name,first.last@example.ae,ar
/// </code>
/// <para>
/// The addresses above are placeholders. Real ones are never written into this repository, which is
/// public — see the paragraph above on where the manifest lives.
/// </para>
/// <list type="bullet">
/// <item><c>Organization</c> — matched against <see cref="Organization.NameEn"/> or
/// <see cref="Organization.NameAr"/>, trimmed, case-insensitive. It must already be an approved club
/// or implementing entity; this command never creates an organization. Load the roster first with
/// <c>reconcile-organizations</c>.</item>
/// <item><c>FullName</c> — the person's name as it should appear in the application.</item>
/// <item><c>Email</c> — the address they sign in with.</item>
/// <item><c>Language</c> — <c>en</c> or <c>ar</c>, optional, defaulting to <c>ar</c>: the nomination
/// forms are Arabic and so are the people on them.</item>
/// </list>
/// <para>
/// The role is not in the file. It follows from the organization —
/// <see cref="RoleNames.ClubAdmin"/> for a club, <see cref="RoleNames.PartnerAdmin"/> for an
/// implementing entity — because a file that could name a role could name <see cref="RoleNames.SuperAdmin"/>.
/// The nomination form's four permission boxes (coordinator, programmes, reports, Ghars channel) have
/// no equivalent in the application, which has one role per organization type and no finer grain; every
/// nominee therefore gets their organization's full role. That is worth knowing before handing these
/// accounts out, and it is not something this command can soften.
/// </para>
/// <para>The safeties are the same as the other provisioning commands:</para>
/// <list type="bullet">
/// <item>The whole file is validated before anything is written. One unknown organization or malformed
/// address fails the run, rather than creating the accounts above it and stopping.</item>
/// <item>Nothing is written without <c>--commit</c>.</item>
/// <item><c>--production</c> is required whenever the target is not a local SQL Server.</item>
/// <item>An existing account is never given a new password. It has its role, link and organization
/// repaired if those are missing, and its password left alone.</item>
/// <item>Each password is generated independently from a CSPRNG, printed once at the end of a
/// committed run, and stored nowhere.</item>
/// </list>
/// <code>
/// dotnet run -- seed-platform-users --file ghars-users.csv
/// dotnet run -- seed-platform-users --file ghars-users.csv --production --commit
/// </code>
/// </remarks>
public static class PlatformUserSeeder
{
    private enum PlannedAction
    {
        /// <summary>No account exists at this address: create one.</summary>
        Create,

        /// <summary>An account exists: repair its role, link and organization, keep its password.</summary>
        Repair,

        /// <summary>An account exists and is already correct.</summary>
        Skip
    }

    private sealed record Row(int LineNumber, string Organization, string FullName, string Email, string Language);

    private sealed record Plan(
        Row Row,
        Organization Organization,
        string Role,
        PlannedAction Action,
        ApplicationUser? ExistingUser,
        string Note);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        string? file,
        bool commit,
        bool allowProduction)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PlatformUserSeeder");
        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        if (string.IsNullOrWhiteSpace(file))
        {
            logger.LogError(
                "seed-platform-users refused: no manifest was given. Pass --file <path> to a UTF-8 CSV with the " +
                "header Organization,FullName,Email,Language.");
            return 1;
        }

        if (!File.Exists(file))
        {
            logger.LogError("seed-platform-users refused: {File} does not exist.", Path.GetFullPath(file));
            return 1;
        }

        var connection = db.Database.GetDbConnection();
        var isRemote = !DatabaseTarget.IsLocalServer(connection.DataSource);

        if ((isRemote || !environment.IsDevelopment()) && !allowProduction)
        {
            logger.LogError(
                "seed-platform-users refused: the target is {Server}/{Database} in the {Environment} environment. " +
                "Re-run with --production to confirm you mean to create accounts there.",
                connection.DataSource, connection.Database, environment.EnvironmentName);
            return 1;
        }

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var required in new[] { RoleNames.ClubAdmin, RoleNames.PartnerAdmin })
        {
            if (await roleManager.RoleExistsAsync(required)) continue;

            logger.LogError(
                "seed-platform-users refused: the {Role} role does not exist in this database. Start the " +
                "application once against it first — roles are seeded at startup in every environment.",
                required);
            return 1;
        }

        if (!TryReadManifest(file, logger, out var rows)) return 1;
        if (rows.Count == 0)
        {
            logger.LogError("seed-platform-users refused: {File} has a header but no rows.", Path.GetFullPath(file));
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Environment : {environment.EnvironmentName}");
        Console.WriteLine($"Server      : {connection.DataSource}");
        Console.WriteLine($"Database    : {connection.Database}");
        Console.WriteLine($"Manifest    : {Path.GetFullPath(file)} ({rows.Count} people)");
        Console.WriteLine(commit
            ? "Mode        : COMMIT — accounts will be created"
            : "Mode        : DRY RUN — nothing will be written (add --commit to apply)");

        // Approved clubs and implementing entities, indexed by both names. Anything else in the file is
        // an error rather than a row to skip: a misspelled organization that silently provisioned
        // nobody would read as this command having worked.
        var organizations = await db.Organizations
            .Where(GharsOrganizations.IsApprovedClub)
            .Concat(db.Organizations.Where(GharsOrganizations.IsApprovedPartner))
            .ToListAsync();

        var byName = new Dictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);
        foreach (var organization in organizations)
        {
            byName.TryAdd(organization.NameEn.Trim(), organization);
            if (!string.IsNullOrWhiteSpace(organization.NameAr)) byName.TryAdd(organization.NameAr.Trim(), organization);
        }

        var problems = new List<string>();

        var duplicated = rows
            .GroupBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in duplicated)
        {
            problems.Add(
                $"{group.Key} appears on {group.Count()} rows (lines {string.Join(", ", group.Select(x => x.LineNumber))}). " +
                "One account cannot belong to two people.");
        }

        var plans = new List<Plan>();
        foreach (var row in rows)
        {
            if (!byName.TryGetValue(row.Organization, out var organization))
            {
                problems.Add(
                    $"line {row.LineNumber}: no approved club or implementing entity is named \"{row.Organization}\". " +
                    "Check the spelling against the roster, or load the roster first with reconcile-organizations.");
                continue;
            }

            if (!IsAcceptableEmail(row.Email))
            {
                problems.Add($"line {row.LineNumber}: \"{row.Email}\" is not a usable sign-in address.");
                continue;
            }

            var role = organization.OrganizationType == OrganizationType.Club
                ? RoleNames.ClubAdmin
                : RoleNames.PartnerAdmin;

            plans.Add(await PlanForAsync(db, userManager, row, organization, role));
        }

        if (problems.Count > 0)
        {
            Console.WriteLine();
            foreach (var problem in problems) logger.LogError("{Problem}", problem);
            Console.WriteLine();
            Console.WriteLine($"Refused: {problems.Count} problem(s) in the manifest. Nothing was written.");
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

        var created = new List<(string Person, string Organization, string Email, string Password)>();
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
                        UserName = plan.Row.Email,
                        Email = plan.Row.Email,
                        FullName = plan.Row.FullName,
                        PreferredLanguage = plan.Row.Language,
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
                            plan.Row.Email, plan.Organization.NameEn,
                            string.Join(", ", result.Errors.Select(e => e.Description)));
                        failed++;
                        continue;
                    }

                    var roleAdded = await userManager.AddToRoleAsync(user, plan.Role);
                    if (!roleAdded.Succeeded)
                    {
                        logger.LogError(
                            "{Email} was created but could not be given the {Role} role: {Errors}. Assign it in " +
                            "Admin -> Users before handing the account over.",
                            plan.Row.Email, plan.Role, string.Join(", ", roleAdded.Errors.Select(e => e.Description)));
                        failed++;
                    }

                    EnsureLink(db, plan.Organization, user.Id);
                    created.Add((plan.Row.FullName, plan.Organization.NameEn, plan.Row.Email, password));
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

                    Console.WriteLine($"   repaired  {plan.Row.Email} -> {plan.Organization.NameEn} (password unchanged)");
                    repaired++;
                    break;
                }
            }
        }

        await db.SaveChangesAsync();

        if (created.Count > 0)
        {
            var emailWidth = Math.Max(created.Max(x => x.Email.Length), 5);
            var nameWidth = Math.Max(created.Max(x => x.Person.Length), 6);
            Console.WriteLine();
            Console.WriteLine("New accounts. These passwords are shown once and are stored nowhere —");
            Console.WriteLine("record them now, then hand each person their own line.");
            Console.WriteLine();
            Console.WriteLine($"{"Person".PadRight(nameWidth)}  {"Email".PadRight(emailWidth)}  Password          Organization");
            Console.WriteLine($"{new string('-', nameWidth)}  {new string('-', emailWidth)}  ----------------  ------------");
            foreach (var row in created)
                Console.WriteLine($"{row.Person.PadRight(nameWidth)}  {row.Email.PadRight(emailWidth)}  {row.Password}  {row.Organization}");
        }

        Console.WriteLine();
        Console.WriteLine($"Created {created.Count}, repaired {repaired}, failed {failed}.");

        if (created.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Tell each person to change their password after first sign-in. There is no self-service");
            Console.WriteLine("reset in this application: a lost password is reset from Admin -> Users.");
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Decides what one person needs, writing nothing. Split out so that the dry run and the committed
    /// run reach their conclusions through identical code.
    /// </summary>
    private static async Task<Plan> PlanForAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        Row row,
        Organization organization,
        string role)
    {
        var existing = await userManager.Users.FirstOrDefaultAsync(x => x.Email == row.Email);
        if (existing is null) return new Plan(row, organization, role, PlannedAction.Create, null, string.Empty);

        var hasRole = await userManager.IsInRoleAsync(existing, role);
        var hasLink = await db.OrganizationAdminLinks.AnyAsync(x => x.OrganizationId == organization.Id && x.UserId == existing.Id);
        var hasOrganization = existing.PrimaryOrganizationId == organization.Id;

        if (hasRole && hasLink && hasOrganization)
            return new Plan(row, organization, role, PlannedAction.Skip, existing, "already correct");

        var missing = new List<string>();
        if (!hasRole) missing.Add("role");
        if (!hasLink) missing.Add("link");
        if (!hasOrganization) missing.Add("organization");

        return new Plan(row, organization, role, PlannedAction.Repair, existing, $"missing {string.Join(" + ", missing)}");
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

        var width = Math.Min(plans.Max(x => x.Row.Email.Length), 44);
        foreach (var plan in plans)
        {
            var email = plan.Row.Email.Length > width ? plan.Row.Email[..width] : plan.Row.Email.PadRight(width);
            var note = string.IsNullOrEmpty(plan.Note) ? string.Empty : $"   [{plan.Note}]";
            Console.WriteLine($"   {email}  {plan.Organization.NameEn} ({plan.Role}){note}");
        }
    }

    /// <summary>
    /// Reads the manifest. Returns false having logged every problem it found, rather than the first:
    /// fixing a hand-written file one error per run is its own kind of unpleasant.
    /// </summary>
    private static bool TryReadManifest(string path, ILogger logger, out List<Row> rows)
    {
        rows = new List<Row>();
        var problems = new List<string>();

        // Excel writes a BOM, and an organization name that starts with an invisible U+FEFF matches
        // nothing at all. Encoding.UTF8 with detection strips it.
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var seenHeader = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;

            var fields = ParseCsvLine(line);

            if (!seenHeader)
            {
                seenHeader = true;
                if (fields.Count > 0 && fields[0].Trim().Equals("Organization", StringComparison.OrdinalIgnoreCase))
                    continue;

                problems.Add(
                    $"line {lineNumber}: the first row must be the header " +
                    "Organization,FullName,Email,Language");
                continue;
            }

            if (fields.Count < 3)
            {
                problems.Add($"line {lineNumber}: expected at least Organization,FullName,Email — found {fields.Count} field(s).");
                continue;
            }

            var organization = fields[0].Trim();
            var fullName = fields[1].Trim();
            var email = fields[2].Trim();
            var language = fields.Count > 3 ? fields[3].Trim().ToLowerInvariant() : string.Empty;

            if (organization.Length == 0) problems.Add($"line {lineNumber}: Organization is empty.");
            if (fullName.Length == 0) problems.Add($"line {lineNumber}: FullName is empty.");
            if (email.Length == 0) problems.Add($"line {lineNumber}: Email is empty.");
            if (language.Length == 0) language = "ar";
            if (language is not ("en" or "ar"))
            {
                problems.Add($"line {lineNumber}: Language must be en or ar, not \"{language}\".");
                continue;
            }

            if (organization.Length == 0 || fullName.Length == 0 || email.Length == 0) continue;

            rows.Add(new Row(lineNumber, organization, fullName, email, language));
        }

        if (!seenHeader) problems.Add("the file is empty.");

        if (problems.Count == 0) return true;

        foreach (var problem in problems) logger.LogError("{Problem}", problem);
        Console.WriteLine();
        Console.WriteLine($"Refused: {problems.Count} problem(s) reading the manifest. Nothing was written.");
        return false;
    }

    /// <summary>
    /// One CSV line, honouring double quotes and the doubled-quote escape. Enough for a hand-written
    /// file and for what Excel writes; there is no attempt at embedded newlines, which a manifest of
    /// names and addresses has no use for.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (quoted)
            {
                if (ch != '"') { field.Append(ch); continue; }

                if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; continue; }

                quoted = false;
                continue;
            }

            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': fields.Add(field.ToString()); field.Clear(); break;
                default: field.Append(ch); break;
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    /// <summary>
    /// Deliberately not a full RFC 5322 check. It rejects what would break sign-in or what is obviously
    /// a mistake, and refuses the demo domain for the same reason the other seeders do: the passwords
    /// of every <c>.local</c> account are published in this repository's git history.
    /// </summary>
    private static bool IsAcceptableEmail(string email)
    {
        if (email.Any(char.IsWhiteSpace)) return false;

        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@')) return false;

        var domain = email[(at + 1)..];
        if (domain.Length < 3 || !domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.')) return false;
        if (domain.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;

        return email.Length <= 254 && at <= 64;
    }

    // Deliberately excludes the character pairs a human cannot tell apart in a console font — 0/O and
    // 1/l/I — because every one of these passwords is going to be read off a screen and retyped.
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    // Symbols restricted to those that survive a PowerShell command line, a CSV cell and an email body
    // without quoting or escaping.
    private const string Symbols = "!#%*+-=?";

    private const int PasswordLength = 16;

    /// <summary>
    /// A random password that satisfies the configured Identity rules by construction: one character
    /// from each required class, the remainder from all of them, then shuffled.
    /// </summary>
    internal static string GeneratePassword()
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

    /// <summary>Reads <c>--file &lt;path&gt;</c> from the command line.</summary>
    public static string? ReadFileArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--file", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
