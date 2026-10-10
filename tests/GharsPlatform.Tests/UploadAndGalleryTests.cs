using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static GharsPlatform.Tests.EntityBookingAccessTests;

namespace GharsPlatform.Tests;

/// <summary>
/// Uploads are accepted only in their intended formats, judged by content as well as name; gallery
/// files are reachable only through the authorized endpoints, and only while their album or item is
/// visible.
/// </summary>
[Collection(AppCollection.Name)]
public class UploadAndGalleryTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n%%EOF\n");
    private static readonly byte[] Mp4 = new byte[] { 0, 0, 0, 0x18 }.Concat(Encoding.ASCII.GetBytes("ftypisom")).Concat(new byte[32]).ToArray();
    private static readonly byte[] Jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 }.Concat(Encoding.ASCII.GetBytes("JFIF")).Concat(new byte[32]).ToArray();
    private static readonly byte[] Html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
    private static readonly byte[] Svg = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");

    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public UploadAndGalleryTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    private static IFormFile File(byte[] bytes, string name, string contentType)
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", name) { Headers = new HeaderDictionary(), ContentType = contentType };

    // ---- validation rules -------------------------------------------------------------------

    [Fact]
    public void Content_must_match_the_extension()
    {
        Assert.Null(FileValidationHelper.Validate(File(Pdf, "a.pdf", "application/pdf"), FileValidationHelper.Pdf, false));
        Assert.NotNull(FileValidationHelper.Validate(File(Mp4, "video.pdf", "application/pdf"), FileValidationHelper.Pdf, false));
        Assert.NotNull(FileValidationHelper.Validate(File(Html, "photo.jpg", "image/jpeg"), FileValidationHelper.Image, false));
        Assert.NotNull(FileValidationHelper.Validate(File(Html, "photo.jpg", "image/jpeg"), FileValidationHelper.Media, false));
        Assert.Null(FileValidationHelper.Validate(File(Jpeg, "photo.jpg", "image/jpeg"), FileValidationHelper.Image, false));
        Assert.Null(FileValidationHelper.Validate(File(Mp4, "clip.mp4", "video/mp4"), FileValidationHelper.Media, false));
    }

    [Fact]
    public void Svg_is_no_longer_an_accepted_image()
        => Assert.NotNull(FileValidationHelper.Validate(File(Svg, "logo.svg", "image/svg+xml"), FileValidationHelper.Image, false));

    [Fact]
    public void Library_document_takes_pdf_only_and_library_video_takes_video_only()
    {
        Assert.Null(FileValidationHelper.Validate(File(Pdf, "booklet.pdf", "application/pdf"), FileValidationHelper.LibraryDocument, false));
        Assert.NotNull(FileValidationHelper.Validate(File(Mp4, "talk.mp4", "video/mp4"), FileValidationHelper.LibraryDocument, false));
        Assert.Null(FileValidationHelper.Validate(File(Mp4, "talk.mp4", "video/mp4"), FileValidationHelper.LibraryVideo, false));
        Assert.NotNull(FileValidationHelper.Validate(File(Pdf, "booklet.pdf", "application/pdf"), FileValidationHelper.LibraryVideo, false));
    }

    [Fact]
    public void Csv_must_be_text()
    {
        Assert.Null(FileValidationHelper.Validate(File(Encoding.UTF8.GetBytes("a,b\n1,2\n"), "data.csv", "text/csv"), FileValidationHelper.Evidence, false));
        Assert.NotNull(FileValidationHelper.Validate(File(new byte[] { 0x4D, 0x5A, 0, 0, 1 }, "data.csv", "text/csv"), FileValidationHelper.Evidence, false));
    }

    [Fact]
    public void Rejection_message_is_bilingual()
    {
        Assert.Contains("doesn't match", FileValidationHelper.Validate(File(Html, "photo.jpg", "image/jpeg"), FileValidationHelper.Image, false));
        Assert.Contains("لا يطابق", FileValidationHelper.Validate(File(Html, "photo.jpg", "image/jpeg"), FileValidationHelper.Image, true));
    }

    // ---- through the real forms ---------------------------------------------------------------

    private async Task<int> CategoryAsync() => await _data.Db(async db =>
    {
        var c = new LibraryCategory { NameEn = "Test " + TestData.Unique(), NameAr = "اختبار" };
        db.LibraryCategories.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    });

    private static MultipartFormDataContent Multipart(IEnumerable<(string, string)> fields, string fileField, byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        foreach (var (k, v) in fields) content.Add(new StringContent(v), k);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, fileField, fileName);
        return content;
    }

    [Fact]
    public async Task Library_refuses_a_video_for_a_booklet_and_accepts_it_for_an_awareness_video()
    {
        var admin = await _data.UserAsync(RoleNames.DscAdmin);
        var category = await CategoryAsync();
        var bookletTitle = "Booklet " + TestData.Unique();
        var videoTitle = "Video " + TestData.Unique();

        (string, string)[] Fields(string title, LibraryContentType type) => new[]
        {
            ("LibraryCategoryId", category.ToString()), ("TitleEn", title), ("TitleAr", title),
            ("ContentType", ((byte)type).ToString()), ("PublishingEntityEn", "DSC"), ("PublicationDate", "2026-01-01"),
            ("IsPublic", "true"), ("IsPublished", "true")
        };

        var refused = await _f.ClientFor(admin).PostAsync("/Admin/Library/CreateItem",
            Multipart(Fields(bookletTitle, LibraryContentType.EducationalBooklet), "File", Mp4, "talk.mp4", "video/mp4"));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.False(await _data.Db(db => db.LibraryItems.AnyAsync(i => i.TitleEn == bookletTitle)));

        var accepted = await _f.ClientFor(admin).PostAsync("/Admin/Library/CreateItem",
            Multipart(Fields(videoTitle, LibraryContentType.AwarenessVideo), "File", Mp4, "talk.mp4", "video/mp4"));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.True(await _data.Db(db => db.LibraryItems.AnyAsync(i => i.TitleEn == videoTitle)));
    }

    [Fact]
    public async Task Album_upload_refuses_a_renamed_html_page()
    {
        var super = await _data.UserAsync(RoleNames.SuperAdmin);
        var albumId = await AlbumAsync(isPublic: true);
        var caption = "html-" + TestData.Unique();

        var r = await _f.ClientFor(super).PostAsync("/Admin/Gallery/AddItem",
            Multipart(new[] { ("AlbumId", albumId.ToString()), ("MediaType", "1"), ("CaptionEn", caption), ("SortOrder", "1") },
                "File", Html, "photo.jpg", "image/jpeg"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(await _data.Db(db => db.MediaItems.AnyAsync(i => i.CaptionEn == caption)));
    }

    // ---- gallery file access ------------------------------------------------------------------

    private string Plant(string webPath, byte[] bytes)
    {
        var full = Path.Combine(_f.WebRoot, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, bytes);
        return full;
    }

    private async Task<int> AlbumAsync(bool isPublic, string? cover = null) => await _data.Db(async db =>
    {
        var a = new MediaAlbum { TitleEn = "Album " + TestData.Unique(), TitleAr = "ألبوم", IsPublic = isPublic, CoverImagePath = cover, AlbumDate = DateTime.UtcNow };
        db.MediaAlbums.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    });

    private async Task<int> AlbumItemAsync(int albumId, string path) => await _data.Db(async db =>
    {
        var i = new MediaItem { AlbumId = albumId, MediaType = MediaType.Image, FilePath = path, CaptionEn = "c", CaptionAr = "c", CreatedAtUtc = DateTime.UtcNow };
        db.MediaItems.Add(i);
        await db.SaveChangesAsync();
        return i.Id;
    });

    [Fact]
    public async Task Album_files_follow_the_albums_visibility_and_are_never_served_statically()
    {
        var dsc = await _data.UserAsync(RoleNames.DscAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var clubUser = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var publicPath = $"/uploads/gallery/items/{TestData.Unique()}.jpg";
        var privatePath = $"/uploads/gallery/items/{TestData.Unique()}.jpg";
        var coverPath = $"/uploads/gallery/covers/{TestData.Unique()}.jpg";
        Plant(publicPath, Jpeg); Plant(privatePath, Jpeg); Plant(coverPath, Jpeg);

        var publicAlbum = await AlbumAsync(isPublic: true, cover: coverPath);
        var privateAlbum = await AlbumAsync(isPublic: false, cover: coverPath);
        var publicItem = await AlbumItemAsync(publicAlbum, publicPath);
        var privateItem = await AlbumItemAsync(privateAlbum, privatePath);
        var anon = _f.ClientFor(null);

        // Never by direct URL.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(publicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(privatePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(coverPath)).StatusCode);

        // A public album stays public.
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/protected-files/album-media/{publicItem}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/protected-files/album-cover/{publicAlbum}")).StatusCode);

        // A private album's files: DSC only.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/protected-files/album-media/{privateItem}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _f.ClientFor(clubUser).GetAsync($"/protected-files/album-media/{privateItem}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/protected-files/album-cover/{privateAlbum}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(dsc).GetAsync($"/protected-files/album-media/{privateItem}")).StatusCode);

        // The public album page links its files through the endpoint, not the raw path.
        var page = await (await anon.GetAsync($"/gallery/album/{publicAlbum}")).Content.ReadAsStringAsync();
        Assert.Contains($"/protected-files/album-media/{publicItem}", page);
        Assert.DoesNotContain(publicPath, page);
    }

    [Fact]
    public async Task Hidden_channel_item_is_not_reachable_by_its_file_url()
    {
        var path = $"/uploads/gallery/items/{TestData.Unique()}.jpg";
        Plant(path, Jpeg);
        var seasonId = await _data.SeasonIdAsync();
        var hidden = await _data.Db(async db =>
        {
            var g = new GalleryItem { TitleEn = "Hidden " + TestData.Unique(), TitleAr = "مخفي", FilePath = path, SeasonId = seasonId, IsPublished = false, MediaDate = DateTime.UtcNow };
            db.GalleryItems.Add(g);
            await db.SaveChangesAsync();
            return g.Id;
        });

        var anon = _f.ClientFor(null);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/protected-files/gallery/{hidden}")).StatusCode);
    }
}
