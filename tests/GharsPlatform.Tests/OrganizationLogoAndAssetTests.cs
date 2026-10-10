using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Tests;

/// <summary>
/// An organization logo can only be a shipped site image or the organization's own uploaded image, so
/// a logo can never be used to publish a private document, a file outside the logo folder, or another
/// organization's upload. Also covers the self-hosted script/style dependencies and the report-only CSP.
/// </summary>
[Collection(AppCollection.Name)]
public class OrganizationLogoAndAssetTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n%%EOF\n");
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[32]).ToArray();
    private static readonly byte[] Html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");
    private static readonly byte[] Svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public OrganizationLogoAndAssetTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    private string Plant(string relativeFolder, byte[] bytes, string fileName)
    {
        var full = Path.Combine(_f.WebRoot, relativeFolder.Replace('/', Path.DirectorySeparatorChar), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return $"/{relativeFolder}/{fileName}";
    }

    private string PlantUpload(byte[] bytes, string ext) => Plant("uploads/org", bytes, $"{Guid.NewGuid():N}{ext}");

    private Task<string?> LogoOf(int org) => _data.Db(db => db.Organizations.Where(o => o.Id == org).Select(o => o.LogoPath).FirstAsync());

    private Task SetLogo(int org, string? path) => _data.Db(async db =>
    {
        (await db.Organizations.FirstAsync(o => o.Id == org)).LogoPath = path;
        await db.SaveChangesAsync();
    });

    private async Task<HttpClient> SuperAdmin() => _f.ClientFor(await _data.UserAsync(RoleNames.SuperAdmin));

    /// <summary>Posts Admin/Organizations/Edit for <paramref name="org"/> with its current values and the given logo.</summary>
    private async Task<HttpResponseMessage> EditAsync(HttpClient client, int org, string? logoPath, (byte[] Bytes, string Name, string Type)? upload = null)
    {
        var row = await _data.Db(db => db.Organizations.AsNoTracking().FirstAsync(o => o.Id == org));
        var form = new MultipartFormDataContent
        {
            { new StringContent(row.Id.ToString()), "Id" },
            { new StringContent(((byte)row.OrganizationType).ToString()), "OrganizationType" },
            { new StringContent(row.NameEn), "NameEn" },
            { new StringContent(row.NameAr), "NameAr" },
            { new StringContent(row.Email), "Email" },
            { new StringContent(row.Phone), "Phone" },
            { new StringContent(((byte)row.Status).ToString()), "Status" },
            { new StringContent(logoPath ?? ""), "LogoPath" },
        };
        if (upload is { } u)
        {
            var file = new ByteArrayContent(u.Bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(u.Type);
            form.Add(file, "LogoFile", u.Name);
        }
        return await client.PostAsync("/Admin/Organizations/Edit", form);
    }

    [Fact]
    public async Task Site_images_uploads_and_the_current_logo_can_be_chosen_and_are_served()
    {
        var org = await _data.OrganizationAsync(OrganizationType.Club);
        var siteImage = Plant("img/partners", Png, $"logo-{TestData.Unique()}.png");
        var admin = await SuperAdmin();
        var anon = _f.ClientFor(null);

        // A shipped site image.
        Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(admin, org, siteImage)).StatusCode);
        Assert.Equal(siteImage, await LogoOf(org));

        // A new upload: stored under /uploads/org with a random name and served to everyone.
        Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(admin, org, siteImage, (Png, "club logo.png", "image/png"))).StatusCode);
        var uploaded = await LogoOf(org);
        Assert.True(OrganizationLogo.IsUpload(uploaded), uploaded);
        var served = await anon.GetAsync(uploaded);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());

        // Saving the form again keeps the organization's own upload.
        Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(admin, org, uploaded)).StatusCode);
        Assert.Equal(uploaded, await LogoOf(org));

        // The edit form offers the current upload and the site images, and no free-text path.
        var form = WebUtility.HtmlDecode(await (await admin.GetAsync($"/Admin/Organizations/Edit/{org}")).Content.ReadAsStringAsync());
        Assert.Contains($"<option value=\"{uploaded}\" selected=\"selected\">", form);
        Assert.Contains($"<option value=\"{siteImage}\">", form);
        Assert.Contains("type=\"file\"", form);
        Assert.DoesNotContain("placeholder=\"/img/partners/default-partner.svg\"", form);

        // Choosing "no logo" clears it.
        Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(admin, org, "")).StatusCode);
        Assert.Null(await LogoOf(org));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(uploaded)).StatusCode);
    }

    [Fact]
    public async Task A_private_document_path_cannot_become_a_logo_or_be_served_as_one()
    {
        var org = await _data.OrganizationAsync(OrganizationType.Club);
        var licencePdf = PlantUpload(Pdf, ".pdf");
        var scanPng = PlantUpload(Png, ".png"); // a document that happens to be an image
        await _data.Db(async db =>
        {
            db.OrganizationDocuments.AddRange(
                new OrganizationDocument { OrganizationId = org, FilePath = licencePdf, OriginalFileName = "licence.pdf", DocumentType = OrganizationDocumentType.License },
                new OrganizationDocument { OrganizationId = org, FilePath = scanPng, OriginalFileName = "scan.png", DocumentType = OrganizationDocumentType.Other });
            await db.SaveChangesAsync();
        });
        var admin = await SuperAdmin();
        var anon = _f.ClientFor(null);

        foreach (var path in new[] { licencePdf, scanPng })
        {
            var response = await EditAsync(admin, org, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the form comes back with an error
            Assert.Contains("Choose a logo from the site images", await response.Content.ReadAsStringAsync());
            Assert.Null(await LogoOf(org));
        }

        // Even if a document path reaches the row by another route (old data, a direct SQL edit),
        // the static gate does not publish it and pages show the default image instead.
        foreach (var path in new[] { licencePdf, scanPng })
        {
            await SetLogo(org, path);
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(path)).StatusCode);
        }
        Assert.Equal(OrganizationLogo.DefaultImage, OrganizationLogo.SrcOrDefault(licencePdf));
    }

    [Theory]
    [InlineData("/uploads/org/../../appsettings.json")]
    [InlineData("/uploads/org/..%2F..%2Fappsettings.json")]
    [InlineData("/img/partners/../../appsettings.json")]
    [InlineData("/img/partners/..\\..\\web.config")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("\\\\server\\share\\logo.png")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("https://example.com/logo.png")]
    [InlineData("//example.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/img/partners/default-partner.svg?x=1")]
    [InlineData("/img/partners/missing-logo.png", true)]   // right shape, no such file
    [InlineData("/uploads/library/0123456789abcdef0123456789abcdef.png")]
    [InlineData("/uploads/org/0123456789abcdef0123456789abcdef.svg")]
    [InlineData("/uploads/org/0123456789abcdef0123456789abcdef.html")]
    [InlineData("/uploads/org/0123456789abcdef0123456789abcdef.png", true)] // right shape, not this organization's
    [InlineData("/protected-uploads/org/0123456789abcdef0123456789abcdef.png")]
    [InlineData("/appsettings.json")]
    public async Task Arbitrary_paths_urls_and_traversal_are_refused(string path, bool rightShape = false)
    {
        var org = await _data.OrganizationAsync(OrganizationType.Partner);
        var before = Plant("img/partners", Png, $"logo-{TestData.Unique()}.png");
        await SetLogo(org, before);

        var response = await EditAsync(await SuperAdmin(), org, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await LogoOf(org));
        if (!rightShape) Assert.Equal(OrganizationLogo.DefaultImage, OrganizationLogo.SrcOrDefault(path));
    }

    [Fact]
    public async Task Traversal_requests_under_the_logo_folder_answer_404()
    {
        var anon = _f.ClientFor(null);
        Plant("uploads/org", Encoding.ASCII.GetBytes("secret"), "notes.txt");
        foreach (var url in new[]
                 {
                     "/uploads/org/notes.txt",
                     "/uploads/org/..%2F..%2Fappsettings.json",
                     "/uploads/org/%2e%2e/%2e%2e/appsettings.json",
                     "/uploads/org%2F..%2F..%2Fappsettings.json",
                     "/uploads/org/",
                 })
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task An_organization_cannot_take_another_organizations_uploaded_logo()
    {
        var owner = await _data.OrganizationAsync(OrganizationType.Club);
        var other = await _data.OrganizationAsync(OrganizationType.Club);
        var ownersLogo = PlantUpload(Png, ".png");
        await SetLogo(owner, ownersLogo);
        var admin = await SuperAdmin();

        var response = await EditAsync(admin, other, ownersLogo);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await LogoOf(other));

        // Nor can a new organization be created with it.
        var create = new MultipartFormDataContent
        {
            { new StringContent(((byte)OrganizationType.Club).ToString()), "OrganizationType" },
            { new StringContent("Copycat " + TestData.Unique()), "NameEn" },
            { new StringContent("نسخة"), "NameAr" },
            { new StringContent($"copy-{TestData.Unique()}@tests.ghars.local"), "Email" },
            { new StringContent("000"), "Phone" },
            { new StringContent(((byte)ApprovalStatus.Approved).ToString()), "Status" },
            { new StringContent(ownersLogo), "LogoPath" },
        };
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/Admin/Organizations/Create", create)).StatusCode);
        Assert.Equal(1, await _data.Db(db => db.Organizations.CountAsync(o => o.LogoPath == ownersLogo)));

        // The owner still has it, and it is still public.
        Assert.Equal(ownersLogo, await LogoOf(owner));
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(null).GetAsync(ownersLogo)).StatusCode);
    }

    [Theory]
    [InlineData("logo.png", "image/png", "html")]
    [InlineData("logo.svg", "image/svg+xml", "svg")]
    [InlineData("logo.html", "text/html", "html")]
    [InlineData("logo.pdf", "application/pdf", "pdf")]
    public async Task Logo_uploads_must_be_raster_images(string fileName, string contentType, string kind)
    {
        var bytes = kind switch { "svg" => Svg, "pdf" => Pdf, _ => Html };
        var admin = await SuperAdmin();

        // Admin edit form.
        var org = await _data.OrganizationAsync(OrganizationType.Club);
        Assert.Equal(HttpStatusCode.OK, (await EditAsync(admin, org, "", (bytes, fileName, contentType))).StatusCode);
        Assert.Null(await LogoOf(org));

        // Organization registration form.
        var email = $"reg-{TestData.Unique()}@tests.ghars.local";
        var register = new MultipartFormDataContent
        {
            { new StringContent(((byte)OrganizationType.Club).ToString()), "OrganizationType" },
            { new StringContent("Registered " + TestData.Unique()), "NameEn" },
            { new StringContent("مسجل"), "NameAr" },
            { new StringContent(email), "Email" },
            { new StringContent("000"), "Phone" },
            { new StringContent("Contact"), "ContactFullName" },
            { new StringContent(email), "ContactEmail" },
            { new StringContent("000"), "ContactPhone" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        register.Add(file, "LogoFile", fileName);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/org/register", register)).StatusCode);
        Assert.False(await _data.Db(db => db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Email == email)));
    }

    [Fact]
    public async Task Registration_with_a_valid_logo_publishes_it()
    {
        var email = $"reg-{TestData.Unique()}@tests.ghars.local";
        var register = new MultipartFormDataContent
        {
            { new StringContent(((byte)OrganizationType.Club).ToString()), "OrganizationType" },
            { new StringContent("Registered " + TestData.Unique()), "NameEn" },
            { new StringContent("مسجل"), "NameAr" },
            { new StringContent(email), "Email" },
            { new StringContent("000"), "Phone" },
            { new StringContent("Contact"), "ContactFullName" },
            { new StringContent(email), "ContactEmail" },
            { new StringContent("000"), "ContactPhone" },
        };
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        register.Add(file, "LogoFile", "logo.PNG");
        Assert.Equal(HttpStatusCode.Redirect, (await (await SuperAdmin()).PostAsync("/org/register", register)).StatusCode);

        var logo = await _data.Db(db => db.Organizations.IgnoreQueryFilters().Where(o => o.Email == email).Select(o => o.LogoPath).FirstAsync());
        Assert.True(OrganizationLogo.IsUpload(logo), logo);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(null).GetAsync(logo)).StatusCode);
    }

    [Fact]
    public void Every_shipped_organization_logo_and_site_image_is_allowed()
    {
        foreach (var seed in GharsMasterData.All())
            Assert.True(OrganizationLogo.IsSiteImage(seed.LogoPath), $"{seed.NameEn}: {seed.LogoPath}");

        var img = Path.Combine(RepoRoot(), "wwwroot", "img");
        foreach (var folder in OrganizationLogo.SiteFolders)
            foreach (var file in Directory.EnumerateFiles(Path.Combine(img, folder)))
                Assert.True(OrganizationLogo.IsSiteImage($"/img/{folder}/{Path.GetFileName(file)}"), file);
    }

    // ---- third-party scripts and CSP ---------------------------------------------------------

    [Fact]
    public void Views_load_no_third_party_scripts_or_styles_and_every_local_library_file_exists()
    {
        var root = RepoRoot();
        var views = Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Areas"), "*.cshtml", SearchOption.AllDirectories))
            .ToList();
        var external = new Regex(@"<(script|link)\b[^>]*\b(src|href)\s*=\s*""(https?:)?//", RegexOptions.IgnoreCase);
        var local = new Regex(@"""~/lib/([^""?]+)""");
        var referenced = 0;
        foreach (var view in views)
        {
            var text = File.ReadAllText(view);
            Assert.False(external.IsMatch(text), $"{view} loads a script or stylesheet from another host");
            foreach (Match m in local.Matches(text))
            {
                referenced++;
                Assert.True(File.Exists(Path.Combine(root, "wwwroot", "lib", m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))), $"{view}: missing wwwroot/lib/{m.Groups[1].Value}");
            }
        }
        Assert.True(referenced >= 10);

        // Chart.js is pinned: every page uses the same tested build.
        var charts = views.SelectMany(v => Regex.Matches(File.ReadAllText(v), @"lib/chart\.js/[^""]+").Select(m => m.Value)).Distinct().ToList();
        Assert.Equal(new[] { "lib/chart.js/4.4.1/dist/chart.umd.js" }, charts);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Pages_use_the_local_libraries_and_send_a_report_only_policy(string culture)
    {
        var anon = _f.ClientFor(null, culture);
        var home = await anon.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("/lib/bootstrap/5.3.3/dist/css/bootstrap.min.css", html);
        Assert.Contains("/lib/microsoft-signalr/8.0.7/dist/browser/signalr.min.js", html);
        Assert.DoesNotContain("cdn.jsdelivr.net", html);

        Assert.True(home.Headers.TryGetValues("Content-Security-Policy-Report-Only", out var policy));
        Assert.Contains("script-src 'self' 'unsafe-inline'", policy!.Single());
        Assert.Contains("report-uri /csp-report", policy.Single());
        Assert.False(home.Headers.Contains("Content-Security-Policy"), "the policy must not be enforced yet");
        Assert.Equal("nosniff", home.Headers.GetValues("X-Content-Type-Options").Single());

        var admin = _f.ClientFor(await _data.UserAsync(RoleNames.DscAdmin), culture);
        var dashboard = await admin.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var adminHtml = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("/lib/chart.js/4.4.1/dist/chart.umd.js", adminHtml);
        Assert.Contains("/lib/sweetalert2/11.14.5/dist/sweetalert2.all.min.js", adminHtml);
        Assert.DoesNotContain("cdn.jsdelivr.net", adminHtml);
    }

    [Fact]
    public async Task Csp_reports_are_accepted_anonymously()
    {
        var content = new StringContent("{\"csp-report\":{\"violated-directive\":\"script-src\"}}", Encoding.UTF8, "application/csp-report");
        Assert.Equal(HttpStatusCode.NoContent, (await _f.ClientFor(null).PostAsync(SecurityHeaders.ReportPath, content)).StatusCode);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GharsPlatform.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
