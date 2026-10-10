using System.Net;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GharsPlatform.Tests.EntityBookingAccessTests;

namespace GharsPlatform.Tests;

/// <summary>
/// Certificate PDFs are stored outside the web root under names unrelated to the public
/// verification token, can be downloaded only by DSC administrators, and the public verification
/// page confirms validity without naming the participant or offering the PDF.
/// </summary>
[Collection(AppCollection.Name)]
public class CertificateAccessTests
{
    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public CertificateAccessTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    private async Task<(Certificate Cert, string ParticipantName, string Super)> IssueAsync()
    {
        var super = await _data.UserAsync(RoleNames.SuperAdmin);
        var participant = await _data.UserAsync(RoleNames.Viewer);
        var name = await _data.Db(db => db.Users.Where(u => u.Id == participant).Select(u => u.FullName!).FirstAsync());
        var activityId = await _data.ActivityAsync(null, super);
        var sessionId = await _data.Db(async db =>
        {
            var s = new AttendanceSession { ActivityId = activityId, SessionStartUtc = DateTime.UtcNow };
            db.AttendanceSessions.Add(s);
            await db.SaveChangesAsync();
            db.AttendanceRecords.Add(new AttendanceRecord { AttendanceSessionId = s.Id, UserId = participant, CheckInUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return s.Id;
        });

        var r = await _f.ClientFor(super).PostAsync("/Admin/Certificates/Issue",
            Form(("AttendanceSessionId", sessionId.ToString()), ("Language", "en")));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var cert = await _data.Db(db => db.Certificates.AsNoTracking().FirstAsync(c => c.ActivityId == activityId && c.UserId == participant));
        return (cert, name, super);
    }

    [Fact]
    public async Task New_certificate_is_stored_outside_wwwroot_under_a_name_unrelated_to_its_token()
    {
        var (cert, _, _) = await IssueAsync();

        Assert.StartsWith("certificates/", cert.PdfPath);
        Assert.DoesNotContain(cert.VerifyToken, cert.PdfPath);
        var full = Path.Combine(_f.ContentRoot, "protected-uploads", cert.PdfPath!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full));
        Assert.False(Directory.Exists(Path.Combine(_f.WebRoot, "uploads", "certificates"))
                     && Directory.GetFiles(Path.Combine(_f.WebRoot, "uploads", "certificates")).Any(f => f.Contains(cert.VerifyToken)));
    }

    [Fact]
    public async Task Download_is_limited_to_dsc_administrators()
    {
        var (cert, _, super) = await IssueAsync();
        var dsc = await _data.UserAsync(RoleNames.DscAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var clubUser = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var participant = cert.UserId;
        var url = $"/Admin/Certificates/Download/{cert.Id}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.ClientFor(null).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.ClientFor(clubUser).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.ClientFor(participant).GetAsync(url)).StatusCode);

        foreach (var admin in new[] { super, dsc })
        {
            var ok = await _f.ClientFor(admin).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);
            var bytes = await ok.Content.ReadAsByteArrayAsync();
            Assert.True(FileSignatures.Matches(bytes, ".pdf"));
        }
    }

    [Fact]
    public async Task Public_verification_confirms_validity_without_the_name_or_the_pdf()
    {
        var (cert, name, super) = await IssueAsync();
        var anon = _f.ClientFor(null);

        var page = await anon.GetAsync($"/verify/certificate/{cert.VerifyToken}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains(cert.CertificateNo, html);
        Assert.DoesNotContain(name, html);
        Assert.DoesNotContain(".pdf", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Certificates/Download", html, StringComparison.OrdinalIgnoreCase);

        // The token does not lead to the file, at the old public location or anywhere else.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/uploads/certificates/{cert.VerifyToken}.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/protected-uploads/{cert.PdfPath}")).StatusCode);

        // Revoked: still verifiable as revoked, still nameless, still not downloadable by the public.
        await _f.ClientFor(super).PostAsync($"/Admin/Certificates/Revoke/{cert.Id}", Form(("reason", "issued in error")));
        var revoked = await (await anon.GetAsync($"/verify/certificate/{cert.VerifyToken}")).Content.ReadAsStringAsync();
        Assert.Contains("revoked", revoked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(name, revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/Admin/Certificates/Download/{cert.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(super).GetAsync($"/Admin/Certificates/Download/{cert.Id}")).StatusCode);
    }

    [Fact]
    public async Task Public_verification_never_shows_the_revocation_reason_but_dsc_administrators_see_it()
    {
        var (cert, _, super) = await IssueAsync();
        var dsc = await _data.UserAsync(RoleNames.DscAdmin);
        var reason = $"Disciplinary note {TestData.Unique()}";

        await _f.ClientFor(super).PostAsync($"/Admin/Certificates/Revoke/{cert.Id}", Form(("reason", reason)));
        Assert.Equal(reason, await _data.Db(db => db.Certificates.Where(c => c.Id == cert.Id).Select(c => c.RevokeReason).FirstAsync()));

        foreach (var (client, revokedText) in new[]
                 {
                     (_f.ClientFor(null, "en"), "has been revoked"),
                     (_f.ClientFor(null, "ar"), "تم إلغاء هذه الشهادة"),
                     // A signed-in non-administrator gets the same public page.
                     (_f.ClientFor(await _data.UserAsync(RoleNames.Viewer)), "has been revoked")
                 })
        {
            var page = await client.GetAsync($"/verify/certificate/{cert.VerifyToken}");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            Assert.Contains(revokedText, html);
            Assert.Contains(cert.CertificateNo, html);
            Assert.DoesNotContain(reason, html);
            Assert.DoesNotContain("Reason:", html);
            Assert.DoesNotContain("السبب:", html);
        }

        // DSC administrators still see the reason, on the admin certificate list.
        var admin = WebUtility.HtmlDecode(await (await _f.ClientFor(dsc).GetAsync("/Admin/Certificates")).Content.ReadAsStringAsync());
        Assert.Contains(reason, admin);
    }

    [Fact]
    public void Verification_view_model_carries_no_private_certificate_fields()
    {
        var names = typeof(GharsPlatform.ViewModels.CertificateVerificationVm).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Equal(new[] { "ActivityTitleAr", "ActivityTitleEn", "CertificateNo", "IsValid", "IssuedAtUtc" }, names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Existing_verification_links_keep_working_through_migration_and_rollback()
    {
        var (legacyId, token, _) = await LegacyCertificateAsync();
        var anon = _f.ClientFor(null);
        var url = $"/verify/certificate/{token}";

        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(url)).StatusCode);
        await RunMigratorAsync("--commit");
        Assert.StartsWith("certificates/", await _data.Db(db => db.Certificates.Where(c => c.Id == legacyId).Select(c => c.PdfPath).FirstAsync()));
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(url)).StatusCode);
        await RunMigratorAsync("--rollback", LatestManifest());
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/verify/certificate/not-a-real-token")).StatusCode);
    }

    [Fact]
    public async Task Legacy_file_is_not_served_statically_but_still_downloads_for_admins()
    {
        var (legacyId, token, _) = await LegacyCertificateAsync();
        var super = await _data.UserAsync(RoleNames.SuperAdmin);

        Assert.Equal(HttpStatusCode.NotFound, (await _f.ClientFor(null).GetAsync($"/uploads/certificates/{token}.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _f.ClientFor(super).GetAsync($"/uploads/certificates/{token}.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(super).GetAsync($"/Admin/Certificates/Download/{legacyId}")).StatusCode);
    }

    [Fact]
    public async Task Migration_dry_run_commit_rollback_and_purge()
    {
        var (issued, _, issuedFile) = await LegacyCertificateAsync();
        var (revoked, _, revokedFile) = await LegacyCertificateAsync(revoked: true);
        var (missing, _, missingFile) = await LegacyCertificateAsync();
        File.Delete(missingFile);
        var super = await _data.UserAsync(RoleNames.SuperAdmin);

        async Task<string?> PathOf(int id) => await _data.Db(db => db.Certificates.Where(c => c.Id == id).Select(c => c.PdfPath).FirstAsync());
        var issuedLegacy = await PathOf(issued);

        // Dry run writes nothing.
        Assert.Equal(0, await RunMigratorAsync());
        Assert.Equal(issuedLegacy, await PathOf(issued));

        // Commit: issued and revoked rows move; the row whose file is missing is left as it was.
        await RunMigratorAsync("--commit");
        var issuedKey = await PathOf(issued);
        Assert.StartsWith("certificates/", issuedKey);
        Assert.StartsWith("certificates/", await PathOf(revoked));
        Assert.StartsWith("/uploads/certificates/", await PathOf(missing));
        Assert.True(File.Exists(issuedFile), "the original must be kept until purge");
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(super).GetAsync($"/Admin/Certificates/Download/{issued}")).StatusCode);

        var manifest = LatestManifest();
        Assert.Contains(CertificateFileMigrator.ReadManifest(manifest), m => m.CertificateId == issued);

        // Rollback: rows point at the originals again and the copies are removed.
        await RunMigratorAsync("--rollback", manifest);
        Assert.Equal(issuedLegacy, await PathOf(issued));
        Assert.False(File.Exists(ProtectedFileStore.ResolvePhysicalPath(issuedKey, Env())));

        // Commit again, then purge: originals go only now, and downloads still work.
        await RunMigratorAsync("--commit");
        manifest = LatestManifest();
        await RunMigratorAsync("--purge", manifest);
        Assert.False(File.Exists(issuedFile));
        Assert.False(File.Exists(revokedFile));
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(super).GetAsync($"/Admin/Certificates/Download/{issued}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(super).GetAsync($"/Admin/Certificates/Download/{revoked}")).StatusCode);
    }

    [Fact]
    public async Task Migration_gives_each_row_sharing_a_file_its_own_copy()
    {
        var (first, _, file) = await LegacyCertificateAsync();
        var legacyPath = await _data.Db(db => db.Certificates.Where(c => c.Id == first).Select(c => c.PdfPath!).FirstAsync());
        var (second, _, _) = await LegacyCertificateAsync(sharedPath: legacyPath);

        await RunMigratorAsync("--commit");
        var keys = await _data.Db(db => db.Certificates.Where(c => c.Id == first || c.Id == second).Select(c => c.PdfPath!).ToListAsync());
        Assert.All(keys, k => Assert.StartsWith("certificates/", k));
        Assert.NotEqual(keys[0], keys[1]);

        await RunMigratorAsync("--purge", LatestManifest());
        Assert.False(File.Exists(file));
    }

    // ---- helpers ----------------------------------------------------------------------------

    private IWebHostEnvironment Env() => _f.Services.GetRequiredService<IWebHostEnvironment>();

    private async Task<int> RunMigratorAsync(params string[] args)
    {
        using var scope = _f.Services.CreateScope();
        return await CertificateFileMigrator.RunAsync(scope.ServiceProvider, new[] { "migrate-certificate-files" }.Concat(args).ToArray());
    }

    private string LatestManifest()
        => new DirectoryInfo(Path.Combine(_f.ContentRoot, "protected-uploads", "_migrations"))
            .GetFiles("certificates-*.csv").OrderByDescending(f => f.Name).ThenByDescending(f => f.LastWriteTimeUtc).First().FullName;

    /// <summary>A certificate the way it was issued before this change: a PDF under wwwroot named by its token.</summary>
    private async Task<(int Id, string Token, string File)> LegacyCertificateAsync(bool revoked = false, string? sharedPath = null)
    {
        var super = await _data.UserAsync(RoleNames.SuperAdmin);
        var participant = await _data.UserAsync(RoleNames.Viewer);
        var activityId = await _data.ActivityAsync(null, super);
        var token = Guid.NewGuid().ToString("N");
        var path = sharedPath ?? $"/uploads/certificates/{token}.pdf";
        var full = Path.Combine(_f.WebRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (sharedPath is null) await File.WriteAllTextAsync(full, $"%PDF-1.4\n% legacy certificate {token}\n%%EOF\n");

        var id = await _data.Db(async db =>
        {
            var c = new Certificate
            {
                ActivityId = activityId, UserId = participant, IssuedByUserId = super,
                CertificateNo = $"GHR-T-{TestData.Unique()}", VerifyToken = token, PdfPath = path,
                Status = revoked ? CertificateStatus.Revoked : CertificateStatus.Issued,
                RevokedAtUtc = revoked ? DateTime.UtcNow : null
            };
            db.Certificates.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });
        return (id, token, full);
    }
}
