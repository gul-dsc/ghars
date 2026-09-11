using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Brings a database in line with the approved Ghars roster in <see cref="GharsMasterData"/>.
/// </summary>
/// <remarks>
/// <para>
/// Run explicitly, never at startup:
/// <code>
/// dotnet run -- reconcile-organizations            # dry run: prints the plan, changes nothing
/// dotnet run -- reconcile-organizations --commit   # applies it
/// </code>
/// </para>
/// <para>
/// <b>Outside Development it also needs <c>--production</c></b>, on the dry run as well as the
/// commit, so a refusal can never be mistaken for "the plan was empty". In that mode the command is
/// <i>additive only</i>: it creates and updates the rosters' organizations but leaves anything not on
/// the list untouched, because on a live system a row missing from this hard-coded roster is more
/// likely one the DSC added deliberately than a stale fixture. <c>--allow-deactivate</c> asks for the
/// other half explicitly. Rows outside the roster are listed in the plan either way.
/// </para>
/// <para>
/// It creates <b>no user accounts</b>, and the organizations it creates carry placeholder contact
/// details — the roster holds names and logos, not telephone numbers. Both are finished in the admin
/// screens.
/// </para>
/// A routine that rewrote the organization table on every launch would be a standing hazard: one bad
/// roster edit and every environment loses its master data at the next restart, with no moment at
/// which a human read the plan. This is a command a person runs, having seen exactly what it will do.
/// </para>
/// <para>
/// <b>It never deletes an organization.</b> Rows outside the roster are set to
/// <see cref="ApprovalStatus.Suspended"/>, which removes them from every selector, catalogue, filter
/// and report while leaving their bookings, agenda entries, KPI submissions, activities and audit
/// history exactly where they are. Nothing is matched by pattern, date or fuzzy text: the roster is
/// matched on the full organization name and the plan is printed, row by row, before anything moves.
/// </para>
/// </remarks>
public static class OrganizationReconciler
{
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        bool commit,
        bool allowProduction = false,
        bool allowDeactivate = false)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("OrganizationReconciler");

        var isDevelopment = environment.IsDevelopment();
        if (!isDevelopment && !allowProduction)
        {
            logger.LogError(
                "reconcile-organizations refused: the environment is {Environment}, not Development. " +
                "Re-run with --production to confirm you mean to change this database's master data. " +
                "A dry run outside Development still needs the flag, so the refusal cannot be mistaken " +
                "for an empty plan.",
                environment.EnvironmentName);
            return 1;
        }

        var db = services.GetRequiredService<AppDbContext>();

        // Outside Development this writes live master data, so name the target before touching it.
        // An operator who has several environments configured should be able to see, in the same
        // output as the plan, which database the plan is about to be applied to.
        if (!isDevelopment)
        {
            var connection = db.Database.GetDbConnection();
            Console.WriteLine();
            Console.WriteLine($"Environment : {environment.EnvironmentName}");
            Console.WriteLine($"Server      : {connection.DataSource}");
            Console.WriteLine($"Database    : {connection.Database}");
        }

        // This command runs instead of the web host, so the startup migration has not happened. Apply
        // it here rather than reading a schema that may predate the roster's columns.
        await db.Database.MigrateAsync();

        // IgnoreQueryFilters so a previously soft-deleted row is seen and reconciled rather than
        // silently skipped and then re-created as a duplicate.
        var existing = await db.Organizations.IgnoreQueryFilters().ToListAsync();
        var byName = existing.ToDictionary(x => x.NameEn, StringComparer.OrdinalIgnoreCase);

        var keep = new List<(Organization Row, GharsMasterData.OrganizationSeed Seed, List<string> Changes)>();
        var create = new List<GharsMasterData.OrganizationSeed>();
        var deactivate = new List<(Organization Row, int Dependants)>();

        foreach (var seed in GharsMasterData.All())
        {
            if (!byName.TryGetValue(seed.NameEn, out var row))
            {
                create.Add(seed);
                continue;
            }

            var changes = new List<string>();
            if (row.OrganizationType != seed.Type) changes.Add($"type {row.OrganizationType} -> {seed.Type}");
            if (row.Status != ApprovalStatus.Approved) changes.Add($"status {row.Status} -> Approved");
            if (row.IsDeleted) changes.Add("restore (was soft-deleted)");
            if (!string.Equals(row.LogoPath, seed.LogoPath, StringComparison.Ordinal)) changes.Add($"logo -> {seed.LogoPath}");
            if (!string.Equals(row.NameAr, seed.NameAr, StringComparison.Ordinal)) changes.Add("arabic name");
            keep.Add((row, seed, changes));
        }

        var approved = GharsMasterData.ApprovedNames();
        foreach (var row in existing.Where(x => !approved.Contains(x.NameEn)))
        {
            if (row.Status == ApprovalStatus.Suspended) continue;   // already reconciled
            deactivate.Add((row, await CountDependantsAsync(db, row.Id)));
        }

        // Creating what the roster lists is additive and reversible. Suspending what it omits is the
        // half that removes a live organization from every selector, catalogue and report — and
        // outside Development a row missing from this hard-coded list is far more likely to be one
        // the DSC added through the admin screens than a stale fixture. So in production the roster
        // only ever *adds*, unless the operator asks for the other half by name. The rows are still
        // listed either way: suppressing the action must not suppress the information.
        var suppressDeactivation = !isDevelopment && !allowDeactivate;

        Print(keep, create, deactivate, commit, suppressDeactivation);

        if (!commit)
        {
            Console.WriteLine();
            Console.WriteLine("Dry run. Nothing was changed. Re-run with --commit to apply this plan.");
            return 0;
        }

        foreach (var (row, seed, _) in keep)
        {
            row.OrganizationType = seed.Type;
            row.NameAr = seed.NameAr;
            row.LogoPath = seed.LogoPath;
            row.Status = ApprovalStatus.Approved;
            row.IsDeleted = false;
            row.DeletedAtUtc = null;
            row.DeletedByUserId = null;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var seed in create)
        {
            db.Organizations.Add(new Organization
            {
                OrganizationType = seed.Type,
                NameEn = seed.NameEn,
                NameAr = seed.NameAr,
                Email = SeedEmail(seed.NameEn),
                Phone = "0000000000",
                AddressEn = "Dubai, United Arab Emirates",
                AddressAr = "دبي، الإمارات العربية المتحدة",
                LogoPath = seed.LogoPath,
                Status = ApprovalStatus.Approved,
                ApprovedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Notes = "Approved Ghars organization"
            });
        }

        if (!suppressDeactivation)
        {
            foreach (var (row, _) in deactivate)
            {
                // Suspended, not deleted and not soft-deleted. Every dependent row stays readable and the
                // organization can be reinstated by setting its status back to Approved.
                row.Status = ApprovalStatus.Suspended;
                row.UpdatedAtUtc = DateTime.UtcNow;
                row.Notes = string.IsNullOrWhiteSpace(row.Notes)
                    ? "Not part of the approved Ghars roster; retained for historical integrity."
                    : row.Notes;
            }
        }

        await db.SaveChangesAsync();

        var deactivated = suppressDeactivation ? 0 : deactivate.Count;
        Console.WriteLine();
        Console.WriteLine($"Applied: {keep.Count} retained, {create.Count} created, {deactivated} deactivated. No organization was deleted.");

        if (suppressDeactivation && deactivate.Count > 0)
            Console.WriteLine($"Left alone: {deactivate.Count} organization(s) outside the roster. Pass --allow-deactivate to suspend them.");

        // A created row carries placeholder contact details by design — the roster holds names and
        // logos, not telephone numbers. In Development nobody reads them; on a live system they are
        // visible on the organization's own screens until a human replaces them.
        if (!isDevelopment && create.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"NOTE: the {create.Count} created organization(s) carry placeholder contact details");
            Console.WriteLine("      (email @ghars.seed.local, phone 0000000000, address \"Dubai, United Arab Emirates\").");
            Console.WriteLine("      Complete them in Admin -> Organizations, and create each organization's own");
            Console.WriteLine("      administrator in Admin -> Users. This command creates no user accounts.");
        }

        return 0;
    }

    /// <summary>
    /// How many rows across the platform reference this organization. Reported so the operator can see
    /// that deactivating is the only safe option, and would notice if a row were unexpectedly isolated.
    /// </summary>
    private static async Task<int> CountDependantsAsync(AppDbContext db, int organizationId)
        => await db.Activities.CountAsync(x => x.PartnerOrganizationId == organizationId)
         + await db.BookingRequests.CountAsync(x => x.OrganizationId == organizationId || x.PartnerOrganizationId == organizationId)
         + await db.AgendaEntries.CountAsync(x => x.OrganizationId == organizationId)
         + await db.KpiSubmissions.CountAsync(x => x.OrganizationId == organizationId)
         + await db.GalleryItems.CountAsync(x => x.OrganizationId == organizationId)
         + await db.MediaAlbums.CountAsync(x => x.OrganizationId == organizationId)
         + await db.OrganizationAdminLinks.CountAsync(x => x.OrganizationId == organizationId)
         + await db.Users.CountAsync(x => x.PrimaryOrganizationId == organizationId);

    private static void Print(
        List<(Organization Row, GharsMasterData.OrganizationSeed Seed, List<string> Changes)> keep,
        List<GharsMasterData.OrganizationSeed> create,
        List<(Organization Row, int Dependants)> deactivate,
        bool commit,
        bool suppressDeactivation)
    {
        Console.WriteLine();
        Console.WriteLine(commit ? "=== Organization reconciliation (APPLYING) ===" : "=== Organization reconciliation (DRY RUN) ===");

        Console.WriteLine();
        Console.WriteLine($"-- Retain ({keep.Count})");
        foreach (var (row, seed, changes) in keep.OrderBy(x => x.Seed.Type).ThenBy(x => x.Seed.NameEn))
        {
            var note = changes.Count == 0 ? "no change" : string.Join("; ", changes);
            Console.WriteLine($"   [{row.Id,4}] {seed.Type,-20} {seed.NameEn,-58} {note}");
        }

        Console.WriteLine();
        Console.WriteLine($"-- Create ({create.Count})");
        foreach (var seed in create.OrderBy(x => x.Type).ThenBy(x => x.NameEn))
            Console.WriteLine($"   [ new] {seed.Type,-20} {seed.NameEn,-58} {seed.LogoPath}");

        Console.WriteLine();
        Console.WriteLine(suppressDeactivation
            ? $"-- Outside the roster ({deactivate.Count}) — NOT changed; pass --allow-deactivate to suspend them"
            : $"-- Deactivate ({deactivate.Count}) — status set to Suspended, rows retained");
        foreach (var (row, dependants) in deactivate.OrderBy(x => x.Row.OrganizationType).ThenBy(x => x.Row.NameEn))
            Console.WriteLine($"   [{row.Id,4}] {row.OrganizationType,-20} {row.NameEn,-58} {dependants} dependent row(s)");

        Console.WriteLine();
        Console.WriteLine("-- Logo mapping");
        foreach (var (row, seed, _) in keep.OrderBy(x => x.Seed.Type).ThenBy(x => x.Seed.NameEn))
            Console.WriteLine($"   {seed.NameEn,-58} {seed.LogoPath}");
        foreach (var seed in create.OrderBy(x => x.Type).ThenBy(x => x.NameEn))
            Console.WriteLine($"   {seed.NameEn,-58} {seed.LogoPath}");
    }

    private static string SeedEmail(string source)
    {
        var chars = source.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return $"{slug}@ghars.seed.local";
    }
}
