using System.Globalization;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// MVC + Localization (Views + DataAnnotations)
builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

// EF Core + SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Identity
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // Strong defaults (tune as required)
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 10;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// SignalR
builder.Services.AddSignalR();

// Localization configuration
var supportedCultures = new[]
{
    new CultureInfo("en"),
    new CultureInfo("ar")
};

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // Cookie first, then query string (?culture=ar)
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new CookieRequestCultureProvider(),
        new QueryStringRequestCultureProvider()
    };
});

// Authorization policies (roles)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", p => p.RequireRole(RoleNames.SuperAdmin, RoleNames.DscAdmin));
});

var app = builder.Build();

// One-time operational utility for relocating protected uploads out of wwwroot. Runs instead of the
// web host (and without seeding) so it can never fire as a side effect of a normal start.
//   dotnet run -- migrate-protected-files [--commit | --purge]
if (args.Length > 0 && string.Equals(args[0], "migrate-protected-files", StringComparison.OrdinalIgnoreCase))
{
    using var migrationScope = app.Services.CreateScope();
    return await ProtectedFileMigrator.RunAsync(
        migrationScope.ServiceProvider,
        commit: args.Contains("--commit"),
        purge: args.Contains("--purge"));
}

// Explicit developer recovery for a forgotten demo password. Like the migrator above it runs instead
// of the web host, and DbSeeder refuses it outside Development.
//   dotnet run -- reset-demo-passwords
if (args.Length > 0 && string.Equals(args[0], "reset-demo-passwords", StringComparison.OrdinalIgnoreCase))
{
    using var resetScope = app.Services.CreateScope();
    return await DbSeeder.ResetDevelopmentDemoPasswordsAsync(resetScope.ServiceProvider, app.Environment);
}

// Migrate and seed at startup. Structural data (roles, the active season) and the configured bootstrap
// administrator run in every environment; demo/sample data is Development-only. Nothing here changes
// an existing user's password — see Data/DbSeeder.cs.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await DbSeeder.SeedAsync(services, app.Environment);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Defence in depth: these folders are served exclusively through ProtectedFilesController, so deny
// static access even for rows that have not yet been migrated out of wwwroot.
//   /uploads/kpi, /uploads/surveys — only ever held protected content (KPI evidence, official reports).
//   /uploads/agenda — club activity media. Publication is decided per gallery row, so the file must be
//     fetched through /protected-files/gallery/{id}; served statically, hiding a photo would not hide it.
// /uploads/org is deliberately absent: it also holds public organization logos. Its documents are
// removed from disk by the --purge phase of the migration instead.
string[] deniedStaticPrefixes = { "/uploads/kpi", "/uploads/surveys", "/uploads/agenda" };
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (deniedStaticPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseRequestLocalization(app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value);

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<NotificationsHub>("/hubs/notifications");

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

return 0;
