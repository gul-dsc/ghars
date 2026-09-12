using System.Text;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Sets an existing administrator's password from the server console. The recovery path for the one
/// situation the application cannot fix from inside itself: the only administrator's password is
/// unknown, so nobody can reach <c>Admin -&gt; Users</c> to reset it.
/// </summary>
/// <remarks>
/// <para>
/// Invoked instead of the web host, like the other operational commands:
/// </para>
/// <code>
/// dotnet GharsPlatform.dll set-admin-password --email someone@example.com --production
/// </code>
/// <para>
/// The bootstrap administrator in <see cref="Data.DbSeeder"/> deliberately cannot do this. It skips
/// entirely once any administrator exists, and even when it runs against an address that already has
/// an account it grants the role and leaves the password alone — bootstrap must never be a way to
/// seize an existing account. That is the right rule, and it is exactly why this command has to exist
/// separately, where the operator states the target by name.
/// </para>
/// <para>The safeties, and why each one is here:</para>
/// <list type="bullet">
/// <item><c>--production</c> is required outside Development, so changing a live credential is
/// always a deliberate act rather than a command recalled from shell history.</item>
/// <item>Only an account already holding Super Admin or DSC Admin can be targeted. A console command
/// that could rewrite any user's password would be a way to sign in as a club and act as them; the
/// blast radius is capped at the accounts an administrator could already reset from the admin screens
/// anyway. Everyone else is reset through <c>Admin -&gt; Users</c>, by a signed-in human.</item>
/// <item>The account must already exist. This command never creates one — that is bootstrap's job,
/// and a typo in an address should fail loudly rather than quietly mint a second administrator.</item>
/// <item>The password is never a command-line argument. It comes from the console, unechoed and typed
/// twice, or from <c>GHARS_ADMIN_PASSWORD</c> for an unattended run. Arguments are visible in the
/// process list and persist in shell history; neither is a good home for a live credential.</item>
/// <item>It goes through <see cref="UserManager{TUser}"/>, so the configured password rules are
/// enforced, the hash matches what sign-in expects, and the security stamp is rotated — which signs
/// out every existing session for that account, as a password change should.</item>
/// </list>
/// <para>
/// Lockout is cleared as part of the same operation. Five failed attempts lock an account for fifteen
/// minutes, and a locked account rejects even the correct password — so setting a new one without
/// clearing the lock would look exactly like the command having failed.
/// </para>
/// </remarks>
public static class AdminPasswordSetter
{
    /// <summary>Unattended source for the new password. Read once and never logged.</summary>
    private const string PasswordEnv = "GHARS_ADMIN_PASSWORD";

    /// <summary>Sets the password of an existing administrator account.</summary>
    /// <returns>A process exit code: 0 on success, 1 on any refusal or failure.</returns>
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        string? email,
        bool allowProduction)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminPasswordSetter");

        var db = services.GetRequiredService<Data.AppDbContext>();
        var connection = db.Database.GetDbConnection();

        // The environment says how the process was configured; the connection string says whose
        // credential is about to be rewritten. A working copy run against a remote database reports
        // Development — launchSettings.json forces it — so the environment alone would waive the
        // confirmation on exactly the run that most needs it. Read without opening a connection, so
        // the refusal happens before the database is touched at all.
        var isDevelopment = environment.IsDevelopment();
        var isRemote = !DatabaseTarget.IsLocalServer(connection.DataSource);

        if ((isRemote || !isDevelopment) && !allowProduction)
        {
            logger.LogError(
                "set-admin-password refused: the target is {Server}/{Database} in the {Environment} environment. " +
                "Re-run with --production to confirm you mean to change that administrator's credential.",
                connection.DataSource, connection.Database, environment.EnvironmentName);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogError(
                "set-admin-password refused: no account was named. Pass --email <address>. The address must " +
                "already have an account; this command never creates one.");
            return 1;
        }

        email = email.Trim();

        // Against anything but a local development database this rewrites a live credential, so name
        // the target before touching it — the same reason the reconciler prints its banner. See
        // Helpers/OrganizationReconciler.cs.
        if (isRemote || !isDevelopment)
        {
            Console.WriteLine();
            Console.WriteLine($"Environment : {environment.EnvironmentName}");
            Console.WriteLine($"Server      : {connection.DataSource}");
            Console.WriteLine($"Database    : {connection.Database}");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.Users.FirstOrDefaultAsync(x => x.Email == email);
        if (user is null)
        {
            logger.LogError(
                "set-admin-password refused: no account exists for {Email}. This command never creates one — " +
                "check the address, or use the bootstrap administrator settings for a database that has no " +
                "administrator at all.",
                email);
            await ListAdministratorsAsync(userManager);
            return 1;
        }

        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Contains(RoleNames.SuperAdmin) && !roles.Contains(RoleNames.DscAdmin))
        {
            logger.LogError(
                "set-admin-password refused: {Email} holds no administrative role ({Roles}). This command only " +
                "targets Super Admin and DSC Admin accounts; every other account is reset from Admin -> Users " +
                "by a signed-in administrator.",
                email, roles.Count == 0 ? "none" : string.Join(", ", roles));
            await ListAdministratorsAsync(userManager);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Account     : {user.Email}");
        Console.WriteLine($"Name        : {user.FullName}");
        Console.WriteLine($"Roles       : {string.Join(", ", roles)}");
        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
            Console.WriteLine($"Lockout     : locked until {user.LockoutEnd:u} — will be cleared");
        if (user.AccessFailedCount > 0)
            Console.WriteLine($"Failed signs: {user.AccessFailedCount} — will be reset to 0");
        Console.WriteLine();

        var password = Environment.GetEnvironmentVariable(PasswordEnv);
        if (!string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine($"Using the password from {PasswordEnv}.");
        }
        else
        {
            password = ReadPasswordFromConsole();
            if (password is null)
            {
                logger.LogError(
                    "set-admin-password refused: no password was supplied. Type one at the prompt, or set " +
                    "{PasswordEnv} for an unattended run.",
                    PasswordEnv);
                return 1;
            }
        }

        // Through UserManager rather than by writing a hash: it applies the configured password rules,
        // produces the hash format sign-in actually verifies, and rotates the security stamp.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            // Descriptions only. Identity never echoes the password and neither does this.
            logger.LogError(
                "The password for {Email} was NOT changed: {Errors}",
                email, string.Join(", ", result.Errors.Select(e => e.Description)));
            return 1;
        }

        // Order matters: a new password on a still-locked account is rejected at sign-in, which reads
        // as this command having silently failed.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        Console.WriteLine();
        Console.WriteLine($"Password set for {user.Email}. Any existing session for this account is now signed out.");
        if (!isDevelopment)
        {
            Console.WriteLine();
            Console.WriteLine($"NOTE: if you set {PasswordEnv}, remove it from this session now — a live");
            Console.WriteLine("      credential should not persist in an environment or a process listing.");
        }

        return 0;
    }

    /// <summary>
    /// Lists the accounts this command would accept. Printed on a refusal because the natural next
    /// question is "then which account?", and the operator is already at a console with database access.
    /// </summary>
    private static async Task ListAdministratorsAsync(UserManager<ApplicationUser> userManager)
    {
        var admins = (await userManager.GetUsersInRoleAsync(RoleNames.SuperAdmin))
            .Concat(await userManager.GetUsersInRoleAsync(RoleNames.DscAdmin))
            .Select(x => x.Email)
            .Where(x => x is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine();
        if (admins.Count == 0)
        {
            Console.WriteLine("This database has no administrator at all. Use the bootstrap administrator");
            Console.WriteLine("settings (GHARS_BOOTSTRAP_ADMIN_EMAIL / GHARS_BOOTSTRAP_ADMIN_PASSWORD) instead.");
            return;
        }

        Console.WriteLine($"Administrator accounts on this database ({admins.Count}):");
        foreach (var admin in admins) Console.WriteLine($"   {admin}");
    }

    /// <summary>
    /// Reads a password from the console without echoing it, twice, and returns it only if both
    /// entries match. Returns null when there is no console to type into, or on a mismatch — a
    /// mistyped password here would lock the operator out just as effectively as the forgotten one.
    /// </summary>
    private static string? ReadPasswordFromConsole()
    {
        if (Console.IsInputRedirected) return null;

        var first = ReadHidden("New password (not shown): ");
        if (string.IsNullOrEmpty(first)) return null;

        var second = ReadHidden("Confirm password       : ");
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            Console.WriteLine();
            Console.WriteLine("The two entries did not match. Nothing was changed.");
            return null;
        }

        return first;
    }

    /// <summary>Reads one line from the console with no echo, honouring backspace.</summary>
    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var buffer = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }

            // Control characters would otherwise land in the password as unprintable bytes the
            // operator cannot see and could never retype.
            if (char.IsControl(key.KeyChar)) continue;

            buffer.Append(key.KeyChar);
        }

        Console.WriteLine();
        return buffer.ToString();
    }

    /// <summary>
    /// Reads <c>--email &lt;address&gt;</c> from the command line. Kept here rather than in
    /// <c>Program.cs</c> so the argument shape lives beside the command that defines it.
    /// </summary>
    public static string? ReadEmailArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--email", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
