using System.Net;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GharsPlatform.Tests.EntityBookingAccessTests;

namespace GharsPlatform.Tests;

/// <summary>
/// Admin &gt; Users: deleting an account takes its organization links with it in one transaction and
/// keeps history; orphan links can be found and removed only on explicit confirmation; passwords are
/// checked on the server with bilingual messages; and a refused edit saves nothing.
/// </summary>
[Collection(AppCollection.Name)]
public class UserAccountTests
{
    private const string GoodPassword = "Ghars-Test-2026!x";

    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public UserAccountTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    private static string Html(string s) => WebUtility.HtmlDecode(s);

    // ---- deletion ---------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_user_removes_the_account_and_its_links_and_keeps_history()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var club2 = await _data.OrganizationAsync(OrganizationType.Club);
        var user = await _data.UserAsync(RoleNames.ClubAdmin, false, club, club2);
        var booking = await _data.BookingAsync(club, null, null, BookingStatus.Pending, user);
        await _data.Db(async db =>
        {
            db.SystemAuditLogs.Add(new SystemAuditLog { UserId = user, Action = "TestAction", EntityName = "Test", AtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var r = await _f.ClientFor(admin).PostAsync($"/Admin/Users/Delete/{user}", Form());
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        Assert.Null(await _data.Db(db => db.Users.FirstOrDefaultAsync(u => u.Id == user)));
        Assert.Equal(0, await _data.Db(db => db.OrganizationAdminLinks.CountAsync(l => l.UserId == user)));
        // History is kept: the booking the person made and the audit entry naming them.
        Assert.Equal(user, (await _data.BookingRowAsync(booking)).RequestedByUserId);
        Assert.Equal(1, await _data.Db(db => db.SystemAuditLogs.CountAsync(a => a.UserId == user && a.Action == "TestAction")));
    }

    [Fact]
    public async Task A_failed_deletion_leaves_the_account_and_its_links_unchanged()
    {
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var user = await _data.UserAsync(RoleNames.ClubAdmin, false, club);

        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var loaded = await users.FindByIdAsync(user);

        // Someone else changes the account after it was loaded, so the delete itself is refused
        // (concurrency stamp) after the links have already been removed inside the transaction.
        await _data.Db(async other =>
        {
            var u = await other.Users.FirstAsync(x => x.Id == user);
            u.FullName = "Changed elsewhere";
            u.ConcurrencyStamp = Guid.NewGuid().ToString();
            await other.SaveChangesAsync();
        });

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var result = await UserAdministration.DeleteUserWithLinksAsync(db, users, loaded!);
            Assert.False(result.Succeeded);
            await tx.RollbackAsync();
        }

        Assert.NotNull(await _data.Db(d => d.Users.FirstOrDefaultAsync(u => u.Id == user)));
        Assert.Equal(1, await _data.Db(d => d.OrganizationAdminLinks.CountAsync(l => l.UserId == user && l.OrganizationId == club)));
    }

    [Fact]
    public async Task Orphan_links_are_reported_and_removed_only_on_confirmation()
    {
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var kept = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var orphanId = await _data.Db(async db =>
        {
            var l = new OrganizationAdminLink { OrganizationId = club, UserId = "deleted-" + TestData.Unique(), RoleHint = OrganizationType.Club, CreatedAtUtc = DateTime.UtcNow };
            db.OrganizationAdminLinks.Add(l);
            await db.SaveChangesAsync();
            return l.Id;
        });

        var report = await _data.Db(OrganizationLinkDiagnostics.BuildReportAsync);
        Assert.Contains(orphanId, report.OrphanLinkIds);

        // Report only, and --cleanup without --confirm: nothing removed.
        using (var scope = _f.Services.CreateScope())
            Assert.Equal(0, await OrganizationLinkDiagnostics.RunAsync(scope.ServiceProvider, new[] { "org-link-diagnostics", "--cleanup" }));
        Assert.Equal(1, await _data.Db(db => db.OrganizationAdminLinks.CountAsync(l => l.Id == orphanId)));

        using (var scope = _f.Services.CreateScope())
            Assert.Equal(0, await OrganizationLinkDiagnostics.RunAsync(scope.ServiceProvider, new[] { "org-link-diagnostics", "--cleanup", "--confirm" }));
        Assert.Equal(0, await _data.Db(db => db.OrganizationAdminLinks.CountAsync(l => l.Id == orphanId)));
        Assert.Equal(1, await _data.Db(db => db.OrganizationAdminLinks.CountAsync(l => l.UserId == kept)));
    }

    [Fact]
    public async Task Diagnostics_report_links_without_the_workspace_role_and_bookings_without_an_entity()
    {
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var viewer = await _data.UserAsync(RoleNames.Viewer, false, club);
        var clubUser = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var orphanBooking = await _data.BookingAsync(club, null, null, BookingStatus.Pending, clubUser);

        var report = await _data.Db(OrganizationLinkDiagnostics.BuildReportAsync);
        var viewerLink = await _data.Db(db => db.OrganizationAdminLinks.Where(l => l.UserId == viewer).Select(l => l.Id).FirstAsync());
        var clubLink = await _data.Db(db => db.OrganizationAdminLinks.Where(l => l.UserId == clubUser).Select(l => l.Id).FirstAsync());
        Assert.Contains(viewerLink, report.RoleMismatchLinkIds);
        Assert.DoesNotContain(clubLink, report.RoleMismatchLinkIds);
        Assert.Contains(orphanBooking, report.BookingsWithoutEntity);
    }

    // ---- passwords --------------------------------------------------------------------------

    private async Task<HttpResponseMessage> CreateAsync(string admin, string email, string? password, string culture = "en")
    {
        var fields = new List<(string, string)>
        {
            ("FullName", "New Person"), ("Email", email), ("RoleName", RoleNames.Viewer), ("PreferredLanguage", "en"), ("IsActive", "true")
        };
        if (password is not null) fields.Add(("Password", password));
        return await _f.ClientFor(admin, culture).PostAsync("/Admin/Users/Create", Form(fields.ToArray()));
    }

    private Task<bool> ExistsAsync(string email) => _data.Db(db => db.Users.AnyAsync(u => u.Email == email));

    [Fact]
    public async Task Create_refuses_a_missing_password()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var email = $"new-{TestData.Unique()}@tests.ghars.local";
        var r = await CreateAsync(admin, email, null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Enter a password for the new account.", Html(await r.Content.ReadAsStringAsync()));
        Assert.False(await ExistsAsync(email));
    }

    [Theory]
    [InlineData("short1!A", "at least 10 characters")]
    [InlineData("alllowercase123!", "uppercase letter")]
    [InlineData("NoDigitsHere!!", "number")]
    [InlineData("NoSymbols12345", "symbol")]
    public async Task Create_enforces_the_configured_password_rules(string password, string expected)
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var email = $"new-{TestData.Unique()}@tests.ghars.local";
        var r = await CreateAsync(admin, email, password);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(expected, Html(await r.Content.ReadAsStringAsync()));
        Assert.False(await ExistsAsync(email));
    }

    [Fact]
    public async Task Password_rule_messages_are_arabic_in_arabic()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var email = $"new-{TestData.Unique()}@tests.ghars.local";
        var r = await CreateAsync(admin, email, "short", "ar");
        var html = Html(await r.Content.ReadAsStringAsync());
        Assert.Contains("يجب ألا تقل كلمة المرور عن 10 أحرف.", html);
        Assert.DoesNotContain("Passwords must be at least", html);
    }

    [Fact]
    public async Task Create_with_a_valid_password_creates_a_working_account_with_its_role()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var email = $"new-{TestData.Unique()}@tests.ghars.local";
        Assert.Equal(HttpStatusCode.Redirect, (await CreateAsync(admin, email, GoodPassword)).StatusCode);

        using var scope = _f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await users.FindByEmailAsync(email);
        Assert.NotNull(u);
        Assert.True(await users.CheckPasswordAsync(u!, GoodPassword));
        Assert.True(await users.IsInRoleAsync(u!, RoleNames.Viewer));
    }

    private async Task<HttpResponseMessage> EditAsync(string admin, string userId, string fullName, string? password)
    {
        var email = await _data.Db(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).FirstAsync());
        var fields = new List<(string, string)>
        {
            ("Id", userId), ("FullName", fullName), ("Email", email), ("RoleName", RoleNames.Viewer), ("PreferredLanguage", "en"), ("IsActive", "true")
        };
        if (password is not null) fields.Add(("Password", password));
        return await _f.ClientFor(admin).PostAsync("/Admin/Users/Edit", Form(fields.ToArray()));
    }

    [Fact]
    public async Task A_refused_password_change_saves_nothing_and_does_not_report_success()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var user = await _data.UserAsync(RoleNames.Viewer);
        var before = await _data.Db(db => db.Users.Where(u => u.Id == user).Select(u => new { u.FullName, u.PasswordHash, u.SecurityStamp }).FirstAsync());

        var r = await EditAsync(admin, user, "Name That Must Not Be Saved", "weak");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = Html(await r.Content.ReadAsStringAsync());
        Assert.Contains("at least 10 characters", html);
        Assert.DoesNotContain("User updated", html);

        var after = await _data.Db(db => db.Users.Where(u => u.Id == user).Select(u => new { u.FullName, u.PasswordHash, u.SecurityStamp }).FirstAsync());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_valid_password_change_is_saved_with_the_profile()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var user = await _data.UserAsync(RoleNames.Viewer);

        Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(admin, user, "Renamed Person", GoodPassword)).StatusCode);

        using var scope = _f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await users.FindByIdAsync(user);
        Assert.Equal("Renamed Person", u!.FullName);
        Assert.True(await users.CheckPasswordAsync(u, GoodPassword));
    }

    [Fact]
    public async Task A_profile_change_without_a_password_is_saved()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var user = await _data.UserAsync(RoleNames.Viewer);
        Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(admin, user, "Only The Name", null)).StatusCode);
        Assert.Equal("Only The Name", await _data.Db(db => db.Users.Where(u => u.Id == user).Select(u => u.FullName).FirstAsync()));
    }
}
