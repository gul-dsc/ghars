using System.Net;
using System.Text;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GharsPlatform.Tests;

/// <summary>
/// <c>migrate-protected-files</c>: a dry run changes nothing; --commit copies, verifies and repoints
/// with a manifest and keeps the originals; --rollback restores the old paths and removes the copies;
/// --purge deletes only originals that nothing still uses. Logos are never touched.
/// </summary>
[Collection(AppCollection.Name)]
public class ProtectedFileMigrationTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n%%EOF\n");
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[32]).ToArray();

    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public ProtectedFileMigrationTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    private (string Stored, string Full) Plant(string folder, byte[] bytes, string ext)
    {
        var name = $"{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(_f.WebRoot, "uploads", folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return ($"/uploads/{folder}/{name}", full);
    }

    private async Task<int> RunAsync(params string[] args)
    {
        using var scope = _f.Services.CreateScope();
        return await ProtectedFileMigrator.RunAsync(scope.ServiceProvider, new[] { "migrate-protected-files" }.Concat(args).ToArray());
    }

    private string LatestManifest()
        => new DirectoryInfo(Path.Combine(_f.ContentRoot, "protected-uploads", "_migrations"))
            .GetFiles("protected-files-*.csv").OrderByDescending(f => f.Name).First().FullName;

    private string ProtectedFull(string key) => ProtectedFileStore.ResolvePhysicalPath(key, _f.Services.GetRequiredService<IWebHostEnvironment>())!;

    [Fact]
    public async Task Dry_run_commit_rollback_and_purge_for_organization_documents_and_library_files()
    {
        var org = await _data.OrganizationAsync(OrganizationType.Club);
        var logo = Plant("org", Png, ".png");
        var licence = Plant("org", Pdf, ".pdf");
        var missing = Plant("org", Pdf, ".pdf");
        File.Delete(missing.Full);
        var book = Plant("library", Pdf, ".pdf");
        var cover = Plant("library", Png, ".png");
        // A library PDF that a gallery album also uses: it moves for the library row, but its original
        // must survive purge because the album still names it.
        var sharedPdf = Plant("library", Pdf, ".pdf");

        var (docId, missingDocId, itemId, sharedItemId) = await _data.Db(async db =>
        {
            (await db.Organizations.FirstAsync(o => o.Id == org)).LogoPath = logo.Stored;
            var doc = new OrganizationDocument { OrganizationId = org, FilePath = licence.Stored, OriginalFileName = "licence.pdf" };
            var gone = new OrganizationDocument { OrganizationId = org, FilePath = missing.Stored, OriginalFileName = "gone.pdf" };
            var category = new LibraryCategory { NameEn = "Mig " + TestData.Unique(), NameAr = "ترحيل" };
            db.AddRange(doc, gone, category);
            await db.SaveChangesAsync();
            var item = new LibraryItem { LibraryCategoryId = category.Id, TitleEn = "Mig", TitleAr = "ترحيل", FilePath = book.Stored, CoverImagePath = cover.Stored, IsPublished = false, IsPublic = true };
            var shared = new LibraryItem { LibraryCategoryId = category.Id, TitleEn = "Shared", TitleAr = "مشترك", FilePath = sharedPdf.Stored, CoverImagePath = "/img/brand/ghars-logo.png", IsPublished = true, IsPublic = true };
            var album = new MediaAlbum { TitleEn = "Album", TitleAr = "ألبوم", IsPublic = true, AlbumDate = DateTime.UtcNow };
            album.Items.Add(new MediaItem { MediaType = MediaType.Pdf, FilePath = sharedPdf.Stored, CaptionEn = "c", CaptionAr = "c", CreatedAtUtc = DateTime.UtcNow });
            db.AddRange(item, shared, album);
            await db.SaveChangesAsync();
            return (doc.Id, gone.Id, item.Id, shared.Id);
        });

        async Task<(string Doc, string Missing, string Book, string Cover, string Shared, string SharedCover, string? Logo)> Paths()
            => await _data.Db(async db => (
                (await db.OrganizationDocuments.FirstAsync(x => x.Id == docId)).FilePath,
                (await db.OrganizationDocuments.FirstAsync(x => x.Id == missingDocId)).FilePath,
                (await db.LibraryItems.FirstAsync(x => x.Id == itemId)).FilePath!,
                (await db.LibraryItems.FirstAsync(x => x.Id == itemId)).CoverImagePath!,
                (await db.LibraryItems.FirstAsync(x => x.Id == sharedItemId)).FilePath!,
                (await db.LibraryItems.FirstAsync(x => x.Id == sharedItemId)).CoverImagePath!,
                (await db.Organizations.FirstAsync(x => x.Id == org)).LogoPath));

        var dsc = _f.ClientFor(await _data.UserAsync(RoleNames.DscAdmin));
        var anon = _f.ClientFor(null);

        // ---- dry run: nothing changes --------------------------------------------------------
        var before = await Paths();
        await RunAsync();
        Assert.Equal(before, await Paths());

        // ---- commit -------------------------------------------------------------------------
        await RunAsync("--commit");
        var after = await Paths();
        Assert.StartsWith("org/", after.Doc);
        Assert.StartsWith("library/", after.Book);
        Assert.StartsWith("library/", after.Cover);
        Assert.StartsWith("library/", after.Shared);
        Assert.Equal(missing.Stored, after.Missing);                // missing file: row left alone
        Assert.Equal("/img/brand/ghars-logo.png", after.SharedCover); // site image: not an upload
        Assert.Equal(logo.Stored, after.Logo);                       // logos are never moved
        Assert.DoesNotContain(Path.GetFileNameWithoutExtension(licence.Full), after.Doc); // new random name
        Assert.True(File.Exists(licence.Full) && File.Exists(book.Full), "originals are kept until purge");
        Assert.Equal(Pdf, File.ReadAllBytes(ProtectedFull(after.Doc)));

        var manifest = ProtectedFileMigrator.ReadManifest(LatestManifest());
        Assert.Contains(manifest, m => m.Entity == "OrganizationDocument" && m.Id == docId && m.OldPath == licence.Stored && m.NewKey == after.Doc);
        Assert.Contains(manifest, m => m.Entity == "LibraryItem" && m.Field == "CoverImagePath" && m.Id == itemId);
        Assert.DoesNotContain(manifest, m => m.OldPath == logo.Stored);

        // The application serves the migrated files, with the same rules as before.
        Assert.Equal(HttpStatusCode.OK, (await dsc.GetAsync($"/protected-files/organization/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dsc.GetAsync($"/Library/Stream/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/Library/Stream/{itemId}")).StatusCode); // still unpublished
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/Library/Stream/{sharedItemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(licence.Stored)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(logo.Stored)).StatusCode);

        // ---- rollback -----------------------------------------------------------------------
        await RunAsync("--rollback", LatestManifest());
        var rolledBack = await Paths();
        Assert.Equal(before, rolledBack);
        Assert.False(File.Exists(ProtectedFull(after.Doc)), "rollback removes the copies");
        Assert.False(File.Exists(ProtectedFull(after.Book)));
        Assert.Equal(HttpStatusCode.OK, (await dsc.GetAsync($"/protected-files/organization/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/Library/Stream/{sharedItemId}")).StatusCode);

        // ---- commit again, then purge -------------------------------------------------------
        await RunAsync("--commit");
        await RunAsync("--purge", LatestManifest());
        Assert.False(File.Exists(licence.Full));
        Assert.False(File.Exists(book.Full));
        Assert.False(File.Exists(cover.Full));
        Assert.True(File.Exists(sharedPdf.Full), "an original still named by an album is kept");
        Assert.True(File.Exists(logo.Full), "logos are never purged");
        Assert.Equal(HttpStatusCode.OK, (await dsc.GetAsync($"/protected-files/organization/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dsc.GetAsync($"/Library/Stream/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(logo.Stored)).StatusCode);
    }

    [Fact]
    public async Task Rollback_leaves_a_row_on_its_copy_when_the_original_has_changed()
    {
        var org = await _data.OrganizationAsync(OrganizationType.Club);
        var licence = Plant("org", Pdf, ".pdf");
        var docId = await _data.Db(async db =>
        {
            var d = new OrganizationDocument { OrganizationId = org, FilePath = licence.Stored, OriginalFileName = "l.pdf" };
            db.OrganizationDocuments.Add(d);
            await db.SaveChangesAsync();
            return d.Id;
        });

        await RunAsync("--commit");
        var key = await _data.Db(db => db.OrganizationDocuments.Where(x => x.Id == docId).Select(x => x.FilePath).FirstAsync());
        File.WriteAllText(licence.Full, "tampered");

        await RunAsync("--rollback", LatestManifest());
        Assert.Equal(key, await _data.Db(db => db.OrganizationDocuments.Where(x => x.Id == docId).Select(x => x.FilePath).FirstAsync()));
        Assert.True(File.Exists(ProtectedFull(key)));
    }

    [Fact]
    public async Task Rollback_and_purge_refuse_without_a_manifest()
    {
        Assert.Equal(2, await RunAsync("--rollback"));
        Assert.Equal(2, await RunAsync("--purge"));
        Assert.Equal(2, await RunAsync("--commit", "--purge", "x.csv"));
    }
}
