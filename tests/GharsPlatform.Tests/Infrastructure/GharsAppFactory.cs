using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GharsPlatform.Tests.Infrastructure;

/// <summary>
/// Runs the real application (Program.cs, its middleware, controllers, views and SignalR hub) in
/// memory against a throwaway SQL Server database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Database.</b> A new <c>GharsPlatformDb_Scratch_t…</c> database on the local server, built by the
/// application's own startup (the migration chain), and dropped when the fixture is disposed. The
/// server defaults to "." and can be pointed at another LOCAL instance with
/// <c>GHARS_TEST_SQL_SERVER</c>; anything that does not look local is refused, so a test run can never
/// reach a shared or production server.
/// </para>
/// <para>
/// <b>Files.</b> The content root and web root are a temporary folder, so certificate PDFs, protected
/// uploads and planted legacy files never touch the repository's wwwroot or protected-uploads.
/// Views are precompiled, so they do not need the real content root.
/// </para>
/// <para>
/// <b>Identity.</b> Requests carry an <c>X-Test-User</c> header; <see cref="TestAuthHandler"/> signs in as
/// that user with the roles the database holds for them. Antiforgery validation is replaced by a
/// no-op: these tests are about authorization and workflow state, and posting forms without it keeps
/// them independent of page markup.
/// </para>
/// <para>
/// <b>Environment.</b> "Testing": DbSeeder creates only the structural data (roles, active season),
/// no demo accounts, and no configuration file from the repository is loaded.
/// </para>
/// </remarks>
public class GharsAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"GharsPlatformDb_Scratch_t{Guid.NewGuid():N}"[..44];

    public string SqlServer { get; } = Environment.GetEnvironmentVariable("GHARS_TEST_SQL_SERVER") is { Length: > 0 } s ? s : ".";

    public string ContentRoot { get; } = Path.Combine(Path.GetTempPath(), "ghars-tests", Guid.NewGuid().ToString("N"));

    public string WebRoot => Path.Combine(ContentRoot, "wwwroot");

    public string ConnectionString =>
        $"Server={SqlServer};Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public GharsAppFactory()
    {
        if (!Regex.IsMatch(DatabaseName, "^GharsPlatformDb_Scratch_t[0-9a-f]+$"))
            throw new InvalidOperationException("Refusing to use a database that is not a scratch database.");

        var host = SqlServer.Split('\\')[0].Trim().ToLowerInvariant();
        var local = host is "." or "localhost" or "(local)" or "127.0.0.1" or "(localdb)" || host.StartsWith("(localdb)")
                    || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        if (!local)
            throw new InvalidOperationException($"Refusing to run tests against non-local SQL Server '{SqlServer}'.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(WebRoot);
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(ContentRoot);
        builder.UseWebRoot(WebRoot);
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
            services.AddSingleton<IAntiforgery, NoAntiforgery>();
        });
    }

    public async Task InitializeAsync()
    {
        // Building the server runs startup: migrations into the new database, then structural seed.
        _ = Server;
        await Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        SqlConnection.ClearAllPools();
        await using (var conn = new SqlConnection($"Server={SqlServer};Database=master;Trusted_Connection=True;TrustServerCertificate=True"))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            // The name was generated above and checked against the scratch pattern.
            cmd.CommandText = $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END";
            await cmd.ExecuteNonQueryAsync();
        }

        try { Directory.Delete(ContentRoot, recursive: true); } catch { /* temp folder; best effort */ }
    }

    /// <summary>An HTTP client signed in as <paramref name="userId"/> (anonymous when null).</summary>
    public HttpClient ClientFor(string? userId, string culture = "en")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        if (userId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.Header, userId);
        client.DefaultRequestHeaders.Add("Cookie", $".AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture}");
        return client;
    }

    private sealed class NoAntiforgery : IAntiforgery
    {
        private static readonly AntiforgeryTokenSet Tokens = new("test", "test", "__RequestVerificationToken", "RequestVerificationToken");
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => Tokens;
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => Tokens;
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }
}

/// <summary>A second, separate database, for tests that depend on the whole Super Admin roster.</summary>
public sealed class IsolatedGharsAppFactory : GharsAppFactory
{
}
