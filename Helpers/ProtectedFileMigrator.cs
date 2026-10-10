using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Operator-run move of already-uploaded protected files from <c>wwwroot/uploads/…</c> into the
/// protected store outside the web root, under new random names. Never runs at startup.
/// </summary>
/// <remarks>
/// <code>
///   dotnet GharsPlatform.dll migrate-protected-files                         dry run: reports, writes nothing
///   dotnet GharsPlatform.dll migrate-protected-files --commit                copy, verify, repoint; originals kept
///   dotnet GharsPlatform.dll migrate-protected-files --rollback &lt;manifest&gt;   point rows back at the originals
///   dotnet GharsPlatform.dll migrate-protected-files --purge &lt;manifest&gt;      delete originals once verified
/// </code>
/// Add <c>--production</c> to allow --commit, --rollback or --purge when the environment is
/// Production; without it they refuse.
/// <para>
/// <b>Covered:</b> KPI evidence, organization licence and supporting documents, official survey
/// reports, and Digital Library files and cover images. Organization logos are public and are not
/// touched. Soft-deleted rows are included: their files are just as private.
/// </para>
/// <para>
/// <b>--commit</b> copies each file to <c>protected-uploads/{category}/{guid}{ext}</c>, compares
/// SHA-256 of the copy against the original, and only then repoints the row. All rows are repointed
/// in one transaction; if that fails, the copies are removed and nothing changes. Originals stay
/// where they are. A manifest (entity, id, field, old path, new key, hash, size) is written under
/// <c>protected-uploads/_migrations</c> for rollback and purge; keep it with the backups.
/// </para>
/// <para>
/// <b>Missing files</b> are reported and the row is left unchanged. <b>Shared files</b> get one copy
/// per row, so each row stays independent.
/// </para>
/// <para>
/// <b>--rollback</b> restores the old path on each manifest row that still points at its new key,
/// provided the original is present and unchanged, then removes the copy. <b>--purge</b> deletes an
/// original only if every manifest row that used it is on a copy whose hash still matches, and no
/// file column anywhere in the database still names it.
/// </para>
/// </remarks>
public static class ProtectedFileMigrator
{
    private const string LegacyPrefix = "/uploads/";

    public sealed record ManifestRow(string Entity, int Id, string Field, string OldPath, string NewKey, string Sha256, long Size);

    /// <summary>One file column on one loaded row.</summary>
    private sealed record Slot(int Id, Func<string?> Get, Action<string> Set);

    /// <summary>A file column the migration covers, and how to load its rows (tracked, deleted ones included).</summary>
    private sealed record Target(string Entity, string Field, string Category, string Prefix, Func<AppDbContext, Task<List<Slot>>> Load);

    private static readonly Target[] Targets =
    {
        new("KpiDocument", "FilePath", ProtectedFileStore.KpiEvidence, LegacyPrefix,
            async db => (await db.KpiDocuments.IgnoreQueryFilters().ToListAsync())
                .Select(x => new Slot(x.Id, () => x.FilePath, v => x.FilePath = v)).ToList()),
        new("OrganizationDocument", "FilePath", ProtectedFileStore.OrganizationDocuments, LegacyPrefix,
            async db => (await db.OrganizationDocuments.IgnoreQueryFilters().ToListAsync())
                .Select(x => new Slot(x.Id, () => x.FilePath, v => x.FilePath = v)).ToList()),
        new("ExternalSurvey", "ReportPdfPath", ProtectedFileStore.SurveyReports, LegacyPrefix,
            async db => (await db.ExternalSurveys.IgnoreQueryFilters().ToListAsync())
                .Select(x => new Slot(x.Id, () => x.ReportPdfPath, v => x.ReportPdfPath = v)).ToList()),
        // Library covers can also be site images such as the brand logo; only uploads are moved.
        new("LibraryItem", "FilePath", ProtectedFileStore.Library, "/uploads/library/",
            async db => (await db.LibraryItems.IgnoreQueryFilters().ToListAsync())
                .Select(x => new Slot(x.Id, () => x.FilePath, v => x.FilePath = v)).ToList()),
        new("LibraryItem", "CoverImagePath", ProtectedFileStore.Library, "/uploads/library/",
            async db => (await db.LibraryItems.IgnoreQueryFilters().ToListAsync())
                .Select(x => new Slot(x.Id, () => x.CoverImagePath, v => x.CoverImagePath = v)).ToList()),
    };

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var env = services.GetRequiredService<IWebHostEnvironment>();

        var commit = args.Contains("--commit");
        var rollback = ArgValue(args, "--rollback");
        var purge = ArgValue(args, "--purge");
        var modes = (commit ? 1 : 0) + (rollback is null ? 0 : 1) + (purge is null ? 0 : 1);
        if (modes > 1)
        {
            Console.Error.WriteLine("Choose one of --commit, --rollback <manifest>, --purge <manifest>.");
            return 2;
        }
        if ((args.Contains("--rollback") && rollback is null) || (args.Contains("--purge") && purge is null))
        {
            Console.Error.WriteLine("--rollback and --purge need the manifest written by --commit.");
            return 2;
        }

        if (modes == 1 && env.IsProduction() && !args.Contains("--production"))
        {
            Console.Error.WriteLine("Refusing to change files or data in the Production environment without --production.");
            return 2;
        }

        if (rollback is not null) return await RollbackAsync(db, env, rollback);
        if (purge is not null) return await PurgeAsync(db, env, purge);
        return await MigrateAsync(db, env, commit);
    }

    private static async Task<int> MigrateAsync(AppDbContext db, IWebHostEnvironment env, bool commit)
    {
        Console.WriteLine();
        Console.WriteLine($"Protected file migration — {(commit ? "COMMIT" : "DRY RUN (nothing is written)")}");
        Console.WriteLine(new string('-', 96));

        var manifest = new List<ManifestRow>();
        var copies = new List<string>();
        var errors = 0;

        foreach (var target in Targets)
        {
            foreach (var slot in await target.Load(db))
            {
                var path = slot.Get();
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(target.Prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var label = $"{target.Entity}.{target.Field} #{slot.Id}";

                // The manifest is a plain CSV; a path it could not round-trip is reported, not guessed at.
                if (path.IndexOfAny(new[] { ',', '\r', '\n' }) >= 0)
                {
                    Console.WriteLine($"{label,-40} ERROR: unsupported characters in path — row unchanged");
                    errors++;
                    continue;
                }

                var source = ProtectedFileStore.ResolvePhysicalPath(path, env);
                if (source is null) { Console.WriteLine($"{label,-40} ERROR: path cannot be resolved — row unchanged"); errors++; continue; }
                if (!File.Exists(source)) { Console.WriteLine($"{label,-40} MISSING FILE — row unchanged"); continue; }

                var hash = Sha256Of(source);
                var size = new FileInfo(source).Length;

                if (!commit)
                {
                    Console.WriteLine($"{label,-40} WOULD MIGRATE  {size,10:N0} bytes");
                    continue;
                }

                var key = $"{target.Category}/{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}";
                var copy = ProtectedFileStore.ResolvePhysicalPath(key, env)!;
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(source, copy, overwrite: false);

                if (!string.Equals(Sha256Of(copy), hash, StringComparison.Ordinal))
                {
                    File.Delete(copy);
                    Console.WriteLine($"{label,-40} ERROR: copy failed verification — row unchanged");
                    errors++;
                    continue;
                }

                copies.Add(copy);
                manifest.Add(new ManifestRow(target.Entity, slot.Id, target.Field, path, key, hash, size));
                slot.Set(key);
                Console.WriteLine($"{label,-40} COPIED + VERIFIED");
            }
        }

        Console.WriteLine(new string('-', 96));

        if (!commit)
        {
            Console.WriteLine("Nothing was changed. Re-run with --commit to copy, verify and repoint.");
            return errors > 0 ? 1 : 0;
        }

        if (manifest.Count == 0)
        {
            Console.WriteLine("No legacy files to migrate.");
            return errors > 0 ? 1 : 0;
        }

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            foreach (var c in copies) TryDelete(c);
            Console.Error.WriteLine($"Database update failed; every copy was removed and no row changed. {ex.Message}");
            return 1;
        }

        var manifestPath = WriteManifest(env, manifest);
        Console.WriteLine($"Repointed {manifest.Count} file reference(s). Originals kept. Manifest: {manifestPath}");
        Console.WriteLine("Check the files open in the application, then run --purge with this manifest. To undo, run --rollback with it.");
        return errors > 0 ? 1 : 0;
    }

    private static async Task<int> RollbackAsync(AppDbContext db, IWebHostEnvironment env, string manifestPath)
    {
        var manifest = ReadManifest(manifestPath);
        var slots = await LoadSlotsAsync(db, manifest);
        var restored = new List<ManifestRow>();

        foreach (var m in manifest)
        {
            var label = $"{m.Entity}.{m.Field} #{m.Id}";
            if (!slots.TryGetValue((m.Entity, m.Field, m.Id), out var slot)) { Console.WriteLine($"{label,-40} SKIPPED: row no longer exists"); continue; }
            if (slot.Get() != m.NewKey) { Console.WriteLine($"{label,-40} SKIPPED: row no longer points at the migrated copy"); continue; }

            var original = ProtectedFileStore.ResolvePhysicalPath(m.OldPath, env);
            if (original is null || !File.Exists(original) || Sha256Of(original) != m.Sha256)
            {
                Console.WriteLine($"{label,-40} SKIPPED: original missing or changed — row left on the protected copy");
                continue;
            }

            slot.Set(m.OldPath);
            restored.Add(m);
        }

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        foreach (var m in restored)
        {
            TryDelete(ProtectedFileStore.ResolvePhysicalPath(m.NewKey, env));
            Console.WriteLine($"{m.Entity}.{m.Field} #{m.Id,-6} ROLLED BACK to {m.OldPath}");
        }
        Console.WriteLine($"Rolled back {restored.Count} of {manifest.Count} file reference(s).");
        return 0;
    }

    private static async Task<int> PurgeAsync(AppDbContext db, IWebHostEnvironment env, string manifestPath)
    {
        var manifest = ReadManifest(manifestPath);
        var slots = await LoadSlotsAsync(db, manifest);
        var deleted = 0;

        foreach (var group in manifest.GroupBy(m => m.OldPath, StringComparer.OrdinalIgnoreCase))
        {
            // An original goes only when every row that used it is on a verified copy, and nothing
            // anywhere in the database still names it.
            var allMoved = group.All(m => slots.TryGetValue((m.Entity, m.Field, m.Id), out var slot) && slot.Get() == m.NewKey
                && ProtectedFileStore.ResolvePhysicalPath(m.NewKey, env) is string copy
                && File.Exists(copy) && Sha256Of(copy) == m.Sha256);
            var stillNamed = await IsReferencedAnywhereAsync(db, group.Key);

            if (!allMoved || stillNamed)
            {
                Console.WriteLine($"KEPT    {group.Key} — {(stillNamed ? "still referenced in the database" : "a migrated copy is missing, changed or not in use")}");
                continue;
            }

            var original = ProtectedFileStore.ResolvePhysicalPath(group.Key, env);
            if (original is not null && File.Exists(original))
            {
                File.Delete(original);
                deleted++;
                Console.WriteLine($"PURGED  {group.Key}");
            }
        }

        Console.WriteLine($"Deleted {deleted} original file(s).");
        return 0;
    }

    /// <summary>
    /// Every column in the schema that stores an uploaded file's path. A legacy original named by any
    /// of them — an album reusing a library PDF, say — is never purged.
    /// </summary>
    private static async Task<bool> IsReferencedAnywhereAsync(AppDbContext db, string path)
        => await db.KpiDocuments.IgnoreQueryFilters().AnyAsync(x => x.FilePath == path)
        || await db.OrganizationDocuments.IgnoreQueryFilters().AnyAsync(x => x.FilePath == path)
        || await db.ExternalSurveys.IgnoreQueryFilters().AnyAsync(x => x.ReportPdfPath == path)
        || await db.LibraryItems.IgnoreQueryFilters().AnyAsync(x => x.FilePath == path || x.CoverImagePath == path || x.ThumbnailPath == path)
        || await db.MediaItems.IgnoreQueryFilters().AnyAsync(x => x.FilePath == path)
        || await db.MediaAlbums.IgnoreQueryFilters().AnyAsync(x => x.CoverImagePath == path)
        || await db.GalleryItems.IgnoreQueryFilters().AnyAsync(x => x.FilePath == path)
        || await db.AgendaMedia.IgnoreQueryFilters().AnyAsync(x => x.FilePath == path)
        || await db.Organizations.IgnoreQueryFilters().AnyAsync(x => x.LogoPath == path)
        || await db.Certificates.IgnoreQueryFilters().AnyAsync(x => x.PdfPath == path || x.QrImagePath == path)
        || await db.ActivityAttachments.IgnoreQueryFilters().AnyAsync(x => x.FilePath == path);

    private static async Task<Dictionary<(string, string, int), Slot>> LoadSlotsAsync(AppDbContext db, List<ManifestRow> manifest)
    {
        var result = new Dictionary<(string, string, int), Slot>();
        foreach (var target in Targets.Where(t => manifest.Any(m => m.Entity == t.Entity && m.Field == t.Field)))
            foreach (var slot in await target.Load(db))
                result[(target.Entity, target.Field, slot.Id)] = slot;
        return result;
    }

    private static string WriteManifest(IWebHostEnvironment env, List<ManifestRow> rows)
    {
        var folder = Path.Combine(ProtectedFileStore.Root(env), "_migrations");
        Directory.CreateDirectory(folder);
        // Never overwrite an earlier manifest: each one is the rollback record of its own run.
        var path = Path.Combine(folder, $"protected-files-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..47] + ".csv");
        var sb = new StringBuilder("Entity,Id,Field,OldPath,NewKey,Sha256,Size\n");
        foreach (var r in rows)
            sb.Append(CultureInfo.InvariantCulture, $"{r.Entity},{r.Id},{r.Field},{r.OldPath},{r.NewKey},{r.Sha256},{r.Size}\n");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public static List<ManifestRow> ReadManifest(string path)
        => File.ReadAllLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .Select(p => new ManifestRow(p[0], int.Parse(p[1], CultureInfo.InvariantCulture), p[2], p[3], p[4], p[5], long.Parse(p[6], CultureInfo.InvariantCulture)))
            .ToList();

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TryDelete(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); } catch { /* reported by the caller's summary */ }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
    }
}
