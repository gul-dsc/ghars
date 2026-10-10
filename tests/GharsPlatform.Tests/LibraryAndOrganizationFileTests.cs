using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Tests;

/// <summary>
/// Digital Library files are reachable only through the library endpoints, and only for items that
/// are published and public (DSC administrators excepted). Organization logos stay public while the
/// organization documents that share their legacy folder do not.
/// </summary>
[Collection(AppCollection.Name)]
public class LibraryAndOrganizationFileTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n%%EOF\n");
    private static readonly byte[] Mp4 = new byte[] { 0, 0, 0, 0x18 }.Concat(Encoding.ASCII.GetBytes("ftypisom")).Concat(Enumerable.Range(0, 4096).Select(i => (byte)i)).ToArray();
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[32]).ToArray();

    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public LibraryAndOrganizationFileTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    // ---- Digital Library ------------------------------------------------------------------

    /// <summary>Plants a file under wwwroot (legacy) or the protected root and returns the stored path.</summary>
    private string Plant(byte[] bytes, string ext, bool legacy)
    {
        var name = $"{Guid.NewGuid():N}{ext}";
        var stored = legacy ? $"/uploads/library/{name}" : $"library/{name}";
        var full = legacy
            ? Path.Combine(_f.WebRoot, "uploads", "library", name)
            : Path.Combine(_f.ContentRoot, "protected-uploads", "library", name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return stored;
    }

    private async Task<int> ItemAsync(bool published, bool isPublic, string? file, string? cover = null, LibraryContentType type = LibraryContentType.EducationalBooklet)
        => await _data.Db(async db =>
        {
            var category = new LibraryCategory { NameEn = "Lib " + TestData.Unique(), NameAr = "مكتبة" };
            db.LibraryCategories.Add(category);
            await db.SaveChangesAsync();
            var item = new LibraryItem
            {
                LibraryCategoryId = category.Id, TitleEn = "Item " + TestData.Unique(), TitleAr = "إصدار",
                FilePath = file, CoverImagePath = cover, ContentType = type,
                IsPublished = published, IsPublic = isPublic, CreatedAtUtc = DateTime.UtcNow
            };
            db.LibraryItems.Add(item);
            await db.SaveChangesAsync();
            return item.Id;
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Published_public_item_is_open_to_everyone_through_the_library_endpoints(bool legacy)
    {
        var file = Plant(Pdf, ".pdf", legacy);
        var cover = Plant(Png, ".png", legacy);
        var id = await ItemAsync(published: true, isPublic: true, file, cover);
        var anon = _f.ClientFor(null);

        var stream = await anon.GetAsync($"/Library/Stream/{id}");
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal("application/pdf", stream.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pdf, await stream.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/Library/Cover/{id}")).StatusCode);

        var viewer = await anon.GetAsync($"/Library/Viewer/{id}");
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        var html = await viewer.Content.ReadAsStringAsync();
        Assert.Contains($"/Library/Stream/{id}", html);
        Assert.DoesNotContain(Path.GetFileName(file), html);

        // The listing links covers through the endpoint and never prints a storage path.
        var listing = await (await anon.GetAsync("/Library")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(Path.GetFileName(cover), listing);
        Assert.DoesNotContain(Path.GetFileName(file), listing);
        Assert.DoesNotContain("protected-uploads", listing);
    }

    [Fact]
    public async Task Video_streams_with_range_requests_so_it_can_seek()
    {
        var id = await ItemAsync(published: true, isPublic: true, Plant(Mp4, ".mp4", legacy: false), type: LibraryContentType.AwarenessVideo);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/Library/Stream/{id}");
        request.Headers.Range = new RangeHeaderValue(100, 199);

        var response = await _f.ClientFor(null).SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(100, response.Content.Headers.ContentRange?.From);
        Assert.Equal(199, response.Content.Headers.ContentRange?.To);
        Assert.Equal(Mp4.Skip(100).Take(100).ToArray(), await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(false, true)]   // unpublished
    [InlineData(true, false)]   // published but not public
    [InlineData(false, false)]
    public async Task Unpublished_or_non_public_item_is_readable_only_by_dsc_administrators(bool published, bool isPublic)
    {
        var file = Plant(Pdf, ".pdf", legacy: true);
        var cover = Plant(Png, ".png", legacy: true);
        var id = await ItemAsync(published, isPublic, file, cover);
        var club = await _data.OrganizationAsync(OrganizationType.Club);

        var outsiders = new[]
        {
            _f.ClientFor(null),
            _f.ClientFor(await _data.UserAsync(RoleNames.Viewer)),
            _f.ClientFor(await _data.UserAsync(RoleNames.ClubAdmin, false, club))
        };
        foreach (var client in outsiders)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Library/Stream/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Library/Viewer/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Library/Cover/{id}")).StatusCode);
            // The old direct URL is closed as well.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(file)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(cover)).StatusCode);
        }

        foreach (var role in new[] { RoleNames.DscAdmin, RoleNames.SuperAdmin })
        {
            var admin = _f.ClientFor(await _data.UserAsync(role));
            var stream = await admin.GetAsync($"/Library/Stream/{id}");
            Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
            Assert.Equal("no-store", stream.Headers.CacheControl?.ToString());
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Library/Viewer/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Library/Cover/{id}")).StatusCode);
            // Not even administrators reach the file statically.
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(file)).StatusCode);
        }
    }

    [Fact]
    public async Task Deleted_item_is_not_served_to_anyone()
    {
        var id = await ItemAsync(published: true, isPublic: true, Plant(Pdf, ".pdf", legacy: false));
        await _data.Db(async db =>
        {
            var item = await db.LibraryItems.FirstAsync(x => x.Id == id);
            item.IsDeleted = true;
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.NotFound, (await _f.ClientFor(null).GetAsync($"/Library/Stream/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _f.ClientFor(await _data.UserAsync(RoleNames.SuperAdmin)).GetAsync($"/Library/Stream/{id}")).StatusCode);
    }

    [Fact]
    public async Task New_library_upload_is_stored_outside_wwwroot()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var category = await _data.Db(async db =>
        {
            var c = new LibraryCategory { NameEn = "Upload " + TestData.Unique(), NameAr = "رفع" };
            db.LibraryCategories.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });
        var title = "Upload " + TestData.Unique();

        var content = new MultipartFormDataContent();
        foreach (var (k, v) in new[]
                 {
                     ("LibraryCategoryId", category.ToString()), ("TitleEn", title), ("TitleAr", title),
                     ("ContentType", LibraryContentType.EducationalBooklet.ToString()),
                     ("PublishingEntityEn", "DSC"), ("PublicationDate", "2026-01-01"),
                     ("IsPublic", "true"), ("IsPublished", "true")
                 })
            content.Add(new StringContent(v), k);
        var pdf = new ByteArrayContent(Pdf);
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(pdf, "File", "booklet.pdf");
        var cover = new ByteArrayContent(Png);
        cover.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(cover, "CoverImage", "cover.png");

        var r = await _f.ClientFor(admin).PostAsync("/Admin/Library/CreateItem", content);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var item = await _data.Db(db => db.LibraryItems.AsNoTracking().FirstAsync(x => x.TitleEn == title));
        Assert.StartsWith("library/", item.FilePath);
        Assert.StartsWith("library/", item.CoverImagePath);
        Assert.True(File.Exists(Path.Combine(_f.ContentRoot, "protected-uploads", item.FilePath!.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(_f.WebRoot, "uploads", "library", Path.GetFileName(item.FilePath))));

        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(null).GetAsync($"/Library/Stream/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(null).GetAsync($"/Library/Cover/{item.Id}")).StatusCode);
    }

    // ---- Organization logos and documents -------------------------------------------------

    private string PlantOrg(byte[] bytes, string ext)
    {
        var name = $"{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(_f.WebRoot, "uploads", "org", name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return $"/uploads/org/{name}";
    }

    [Fact]
    public async Task Organization_logo_stays_public_but_legacy_documents_beside_it_do_not()
    {
        var org = await _data.OrganizationAsync(OrganizationType.Club);
        var other = await _data.OrganizationAsync(OrganizationType.Club);
        var logo = PlantOrg(Png, ".png");
        var licence = PlantOrg(Pdf, ".pdf");
        var stray = PlantOrg(Pdf, ".pdf");
        var docId = await _data.Db(async db =>
        {
            (await db.Organizations.FirstAsync(o => o.Id == org)).LogoPath = logo;
            var d = new OrganizationDocument { OrganizationId = org, FilePath = licence, OriginalFileName = "licence.pdf", DocumentType = OrganizationDocumentType.License };
            db.OrganizationDocuments.Add(d);
            await db.SaveChangesAsync();
            return d.Id;
        });

        var anon = _f.ClientFor(null);
        var logoResponse = await anon.GetAsync(logo);
        Assert.Equal(HttpStatusCode.OK, logoResponse.StatusCode);
        Assert.Equal(Png, await logoResponse.Content.ReadAsByteArrayAsync());

        var dsc = _f.ClientFor(await _data.UserAsync(RoleNames.DscAdmin));
        var owner = _f.ClientFor(await _data.UserAsync(RoleNames.ClubAdmin, false, org));
        var stranger = _f.ClientFor(await _data.UserAsync(RoleNames.ClubAdmin, false, other));

        // The legacy document and any unreferenced file are closed to everyone at their static URL.
        foreach (var client in new[] { anon, owner, stranger, dsc })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(licence)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(stray)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(licence.ToUpperInvariant())).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/uploads/org/..%2F..%2Fappsettings.json")).StatusCode);

        // The authorized endpoint still serves it to the organization and to DSC, and to nobody else.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/protected-files/organization/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/protected-files/organization/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/protected-files/organization/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dsc.GetAsync($"/protected-files/organization/{docId}")).StatusCode);
    }
}
