using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Operator-run move of certificate PDFs issued before protected storage, from
/// <c>wwwroot/uploads/certificates/{verifyToken}.pdf</c> to the protected root under a new random
/// name. Never runs at startup.
/// </summary>
/// <remarks>
/// <code>
///   dotnet GharsPlatform.dll migrate-certificate-files                         dry run: reports, writes nothing
///   dotnet GharsPlatform.dll migrate-certificate-files --commit                copy, verify, repoint; originals kept
///   dotnet GharsPlatform.dll migrate-certificate-files --rollback &lt;manifest&gt;   point rows back at the originals
///   dotnet GharsPlatform.dll migrate-certificate-files --purge &lt;manifest&gt;      delete originals once verified
/// </code>
/// Add <c>--production</c> to allow --commit, --rollback or --purge when the environment is
/// Production; without it they refuse.
/// <para>
/// <b>Every certificate row is covered</b>, issued and revoked alike: a revoked certificate still
/// names its participant.
/// </para>
/// <para>
/// <b>--commit</b> copies each file to <c>protected-uploads/certificates/{guid}.pdf</c>, compares
/// SHA-256 of the copy against the original, and only then repoints the row. All rows are
/// repointed in one transaction; if that fails, the copies are removed and nothing changes. The
/// originals stay where they are. A manifest (certificate id, old path, new key, hash, size) is
/// written under the protected root for rollback and purge.
/// </para>
/// <para>
/// <b>Missing files</b> are reported and the row is left unchanged. <b>Shared files</b> (several rows
/// naming one file, as the development seed does) get one copy per row, so each row stays
/// independent; the original is purged only when every row that named it has moved.
/// </para>
/// <para>
/// <b>--rollback</b> restores the old path on each manifest row that still points at the new key,
/// provided the original is present and unchanged, then removes the copy. <b>--purge</b> deletes an
/// original only if every manifest row that used it now points at a copy whose hash still matches.
/// </para>
/// </remarks>
public static class CertificateFileMigrator
{
    private const string LegacyPrefix = "/uploads/certificates/";

    public sealed record ManifestRow(int CertificateId, string OldPath, string NewKey, string Sha256, long Size);

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
        var rows = await db.Certificates
            .Where(x => x.PdfPath != null && x.PdfPath.StartsWith(LegacyPrefix))
            .OrderBy(x => x.Id)
            .ToListAsync();
        var already = await db.Certificates.CountAsync(x => x.PdfPath != null && !x.PdfPath.StartsWith("/"));
        var empty = await db.Certificates.CountAsync(x => x.PdfPath == null || x.PdfPath == "");
        var shared = rows.GroupBy(x => x.PdfPath!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Console.WriteLine();
        Console.WriteLine($"Certificate file migration — {(commit ? "COMMIT" : "DRY RUN (nothing is written)")}");
        Console.WriteLine($"Legacy rows: {rows.Count}   already protected: {already}   no file recorded: {empty}");
        Console.WriteLine(new string('-', 90));

        var manifest = new List<ManifestRow>();
        var copies = new List<string>();
        var errors = 0;

        foreach (var cert in rows)
        {
            var source = ProtectedFileStore.ResolvePhysicalPath(cert.PdfPath, env);
            var note = shared.TryGetValue(cert.PdfPath!, out var n) ? $" (file shared by {n} rows)" : "";

            if (source is null) { Console.WriteLine($"#{cert.Id,-6} ERROR: path cannot be resolved — row unchanged"); errors++; continue; }
            if (!File.Exists(source)) { Console.WriteLine($"#{cert.Id,-6} MISSING FILE — row unchanged"); continue; }

            var sourceHash = Sha256Of(source);
            var size = new FileInfo(source).Length;

            if (!commit)
            {
                Console.WriteLine($"#{cert.Id,-6} WOULD MIGRATE  {size,10:N0} bytes{note}");
                continue;
            }

            var key = $"{ProtectedFileStore.Certificates}/{Guid.NewGuid():N}.pdf";
            var target = ProtectedFileStore.ResolvePhysicalPath(key, env)!;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);

            if (!string.Equals(Sha256Of(target), sourceHash, StringComparison.Ordinal))
            {
                File.Delete(target);
                Console.WriteLine($"#{cert.Id,-6} ERROR: copy failed verification — row unchanged");
                errors++;
                continue;
            }

            copies.Add(target);
            manifest.Add(new ManifestRow(cert.Id, cert.PdfPath!, key, sourceHash, size));
            cert.PdfPath = key;
            Console.WriteLine($"#{cert.Id,-6} COPIED + VERIFIED{note}");
        }

        if (commit && manifest.Count > 0)
        {
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
            Console.WriteLine(new string('-', 90));
            Console.WriteLine($"Repointed {manifest.Count} row(s). Originals kept. Manifest: {manifestPath}");
            Console.WriteLine("Check downloads in Admin > Certificates, then run --purge with this manifest. To undo, run --rollback with it.");
        }
        else if (!commit)
        {
            Console.WriteLine(new string('-', 90));
            Console.WriteLine("Nothing was changed. Re-run with --commit to copy, verify and repoint.");
        }

        return errors > 0 ? 1 : 0;
    }

    private static async Task<int> RollbackAsync(AppDbContext db, IWebHostEnvironment env, string manifestPath)
    {
        var manifest = ReadManifest(manifestPath);
        var ids = manifest.Select(m => m.CertificateId).ToList();
        var certs = await db.Certificates.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var restored = new List<ManifestRow>();

        foreach (var m in manifest)
        {
            if (!certs.TryGetValue(m.CertificateId, out var cert)) { Console.WriteLine($"#{m.CertificateId,-6} SKIPPED: certificate no longer exists"); continue; }
            if (cert.PdfPath != m.NewKey) { Console.WriteLine($"#{m.CertificateId,-6} SKIPPED: row no longer points at the migrated copy"); continue; }

            var original = ProtectedFileStore.ResolvePhysicalPath(m.OldPath, env);
            if (original is null || !File.Exists(original) || Sha256Of(original) != m.Sha256)
            {
                Console.WriteLine($"#{m.CertificateId,-6} SKIPPED: original missing or changed — row left on the protected copy");
                continue;
            }

            cert.PdfPath = m.OldPath;
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
            Console.WriteLine($"#{m.CertificateId,-6} ROLLED BACK to {m.OldPath}");
        }
        Console.WriteLine($"Rolled back {restored.Count} of {manifest.Count} row(s).");
        return 0;
    }

    private static async Task<int> PurgeAsync(AppDbContext db, IWebHostEnvironment env, string manifestPath)
    {
        var manifest = ReadManifest(manifestPath);
        var ids = manifest.Select(m => m.CertificateId).ToList();
        var current = await db.Certificates.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.PdfPath);
        var deleted = 0;

        foreach (var group in manifest.GroupBy(m => m.OldPath, StringComparer.OrdinalIgnoreCase))
        {
            // An original goes only when every row that used it is on a verified copy, and no row
            // outside the manifest still names it.
            var allMoved = group.All(m => current.TryGetValue(m.CertificateId, out var p) && p == m.NewKey
                && ProtectedFileStore.ResolvePhysicalPath(m.NewKey, env) is string copy
                && File.Exists(copy) && Sha256Of(copy) == m.Sha256);
            var stillNamed = await db.Certificates.AnyAsync(x => x.PdfPath == group.Key);

            if (!allMoved || stillNamed)
            {
                Console.WriteLine($"KEPT    {group.Key} — {(stillNamed ? "still referenced by a certificate" : "a migrated copy is missing, changed or not in use")}");
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

    private static string WriteManifest(IWebHostEnvironment env, List<ManifestRow> rows)
    {
        var folder = Path.Combine(ProtectedFileStore.Root(env), "_migrations");
        Directory.CreateDirectory(folder);
        // Never overwrite an earlier manifest: each one is the rollback record of its own run.
        var path = Path.Combine(folder, $"certificates-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..44] + ".csv");
        var sb = new StringBuilder("CertificateId,OldPath,NewKey,Sha256,Size\n");
        foreach (var r in rows)
            sb.Append(CultureInfo.InvariantCulture, $"{r.CertificateId},{r.OldPath},{r.NewKey},{r.Sha256},{r.Size}\n");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public static List<ManifestRow> ReadManifest(string path)
        => File.ReadAllLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .Select(p => new ManifestRow(int.Parse(p[0], CultureInfo.InvariantCulture), p[1], p[2], p[3], long.Parse(p[4], CultureInfo.InvariantCulture)))
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
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
