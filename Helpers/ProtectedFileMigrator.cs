using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// One-time, operator-driven migration of already-uploaded protected documents from
/// <c>wwwroot/uploads/...</c> into the protected store outside the web root.
///
/// Deliberately NOT run at application startup: it rewrites database paths and touches files, so it
/// must be an explicit, observable action with a dry run first. Invoke via:
///
///   dotnet run -- migrate-protected-files            (report only — changes nothing)
///   dotnet run -- migrate-protected-files --commit   (copy + repoint the database; originals kept)
///   dotnet run -- migrate-protected-files --purge    (delete the verified legacy originals)
///
/// The three phases exist so the copy can be verified before anything is deleted. --commit *copies*
/// rather than moves, so rollback is simply restoring the previous FilePath values while every legacy
/// file is still in place.
/// </summary>
public static class ProtectedFileMigrator
{
    public sealed record Row(string Entity, int Id, string OldPath, string? NewPath, string Outcome);

    public static async Task<int> RunAsync(IServiceProvider services, bool commit, bool purge)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var env = services.GetRequiredService<IWebHostEnvironment>();
        var rows = new List<Row>();

        // ---- KPI evidence -------------------------------------------------------------------
        foreach (var doc in await db.KpiDocuments.ToListAsync())
        {
            var r = Process(doc.FilePath, ProtectedFileStore.KpiEvidence, env, commit, purge,
                            newPath => doc.FilePath = newPath);
            rows.Add(r with { Entity = "KpiDocument", Id = doc.Id });
        }

        // ---- Organization licence / supporting documents ------------------------------------
        // Note: organization *logos* live in the same legacy folder and are intentionally public —
        // they are not touched here, and /uploads/org therefore stays publicly served.
        foreach (var doc in await db.OrganizationDocuments.ToListAsync())
        {
            var r = Process(doc.FilePath, ProtectedFileStore.OrganizationDocuments, env, commit, purge,
                            newPath => doc.FilePath = newPath);
            rows.Add(r with { Entity = "OrganizationDocument", Id = doc.Id });
        }

        // ---- Official survey analysis reports -----------------------------------------------
        foreach (var survey in await db.ExternalSurveys.Where(x => x.ReportPdfPath != null).ToListAsync())
        {
            var r = Process(survey.ReportPdfPath, ProtectedFileStore.SurveyReports, env, commit, purge,
                            newPath => survey.ReportPdfPath = newPath);
            rows.Add(r with { Entity = "ExternalSurvey", Id = survey.Id });
        }

        if (commit) await db.SaveChangesAsync();

        Report(rows, commit, purge);
        return rows.Any(x => x.Outcome == "ERROR") ? 1 : 0;
    }

    private static Row Process(string? storedPath, string category, IWebHostEnvironment env,
                               bool commit, bool purge, Action<string> repoint)
    {
        var empty = new Row("", 0, storedPath ?? "", null, "");

        if (string.IsNullOrWhiteSpace(storedPath))
            return empty with { Outcome = "SKIPPED (empty path)" };

        // Already migrated: nothing to copy. Under --purge this is the row whose legacy twin we drop.
        if (ProtectedFileStore.IsProtectedKey(storedPath))
        {
            if (!purge) return empty with { Outcome = "ALREADY PROTECTED" };

            var legacyTwin = "/uploads/" + storedPath;
            var legacyFull = ProtectedFileStore.ResolvePhysicalPath(legacyTwin, env);
            if (legacyFull is null || !File.Exists(legacyFull))
                return empty with { Outcome = "ALREADY PROTECTED (no legacy copy)" };

            var protectedFull = ProtectedFileStore.ResolvePhysicalPath(storedPath, env);
            if (protectedFull is null || !File.Exists(protectedFull))
                return empty with { Outcome = "ERROR: protected copy missing — legacy file kept" };

            File.Delete(legacyFull);
            return empty with { Outcome = "PURGED legacy copy" };
        }

        if (purge) return empty with { Outcome = "SKIPPED (not migrated yet)" };

        var sourceFull = ProtectedFileStore.ResolvePhysicalPath(storedPath, env);
        if (sourceFull is null)
            return empty with { Outcome = "ERROR: path could not be resolved" };

        // A row pointing at a file that is already gone is reported, never silently repointed —
        // repointing it would only move the broken link to a new location.
        if (!File.Exists(sourceFull))
            return empty with { Outcome = "MISSING FILE (row left unchanged)" };

        var newKey = $"{category}/{Path.GetFileName(sourceFull)}";

        if (!commit) return empty with { NewPath = newKey, Outcome = "WOULD MIGRATE" };

        var targetFolder = Path.Combine(ProtectedFileStore.Root(env), category);
        Directory.CreateDirectory(targetFolder);
        var targetFull = Path.Combine(targetFolder, Path.GetFileName(sourceFull));

        File.Copy(sourceFull, targetFull, overwrite: true);
        if (new FileInfo(targetFull).Length != new FileInfo(sourceFull).Length)
            return empty with { Outcome = "ERROR: copy size mismatch — row left unchanged" };

        repoint(newKey);
        return empty with { NewPath = newKey, Outcome = "MIGRATED (legacy copy kept)" };
    }

    private static void Report(List<Row> rows, bool commit, bool purge)
    {
        var mode = purge ? "PURGE" : commit ? "COMMIT" : "DRY RUN (no changes written)";
        Console.WriteLine();
        Console.WriteLine($"Protected file migration — mode: {mode}");
        Console.WriteLine(new string('-', 96));

        if (rows.Count == 0)
        {
            Console.WriteLine("No protected document rows found.");
            return;
        }

        foreach (var r in rows)
            Console.WriteLine($"{r.Entity,-22} #{r.Id,-5} {r.Outcome,-40} {r.OldPath} {(r.NewPath is null ? "" : "-> " + r.NewPath)}");

        Console.WriteLine(new string('-', 96));
        foreach (var g in rows.GroupBy(x => x.Outcome).OrderBy(x => x.Key))
            Console.WriteLine($"{g.Count(),5}  {g.Key}");

        if (!commit && !purge)
            Console.WriteLine("\nRe-run with --commit to copy the files and repoint the database.");
        else if (commit)
            Console.WriteLine("\nVerify the application serves these documents, then re-run with --purge to delete the legacy copies.");
    }
}
