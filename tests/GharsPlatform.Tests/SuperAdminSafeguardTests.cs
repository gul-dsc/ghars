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
/// The platform always keeps at least one active Super Admin, nobody can remove their own Super Admin
/// access, and two administrators acting at the same moment cannot remove the last one between them.
/// Runs on its own database because every assertion depends on the whole roster.
/// </summary>
[Collection(IsolatedCollection.Name)]
public class SuperAdminSafeguardTests
{
    private readonly IsolatedGharsAppFactory _f;
    private readonly TestData _data;

    public SuperAdminSafeguardTests(IsolatedGharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    /// <summary>Deactivates every Super Admin left by earlier tests, so each test sets its own roster.</summary>
    private Task ClearRosterAsync() => _data.Db(async db =>
    {
        var roleId = await db.Roles.Where(r => r.Name == RoleNames.SuperAdmin).Select(r => r.Id).FirstAsync();
        var ids = await db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync();
        foreach (var u in await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync())
            u.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        await db.SaveChangesAsync();
    });

    private async Task<bool> IsActiveSuperAdminAsync(string userId)
    {
        using var scope = _f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await users.FindByIdAsync(userId);
        return u is not null && !UserAdministration.IsDeactivated(u) && await users.IsInRoleAsync(u, RoleNames.SuperAdmin);
    }

    private async Task<HttpResponseMessage> EditAsync(string actor, string target, string role, bool active = true, string? password = null)
    {
        var email = await _data.Db(db => db.Users.Where(u => u.Id == target).Select(u => u.Email!).FirstAsync());
        var fields = new List<(string, string)>
        {
            ("Id", target), ("FullName", "Edited Name"), ("Email", email), ("RoleName", role),
            ("PreferredLanguage", "en"), ("IsActive", active ? "true" : "false")
        };
        if (password is not null) fields.Add(("Password", password));
        return await _f.ClientFor(actor).PostAsync("/Admin/Users/Edit", Form(fields.ToArray()));
    }

    [Fact]
    public async Task A_super_admin_cannot_demote_deactivate_or_delete_their_own_account()
    {
        await ClearRosterAsync();
        var me = await _data.UserAsync(RoleNames.SuperAdmin);
        await _data.UserAsync(RoleNames.SuperAdmin); // another one exists, so this is about self-protection

        var demote = await EditAsync(me, me, RoleNames.DscAdmin);
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        Assert.Contains("can't remove your own Super Admin access", System.Net.WebUtility.HtmlDecode(await demote.Content.ReadAsStringAsync()));

        await _f.ClientFor(me).PostAsync($"/Admin/Users/Deactivate/{me}", Form());
        await _f.ClientFor(me).PostAsync($"/Admin/Users/Delete/{me}", Form());
        var selfInactive = await EditAsync(me, me, RoleNames.SuperAdmin, active: false);
        Assert.Equal(HttpStatusCode.OK, selfInactive.StatusCode);

        Assert.True(await IsActiveSuperAdminAsync(me));
    }

    [Fact]
    public async Task The_last_active_super_admin_cannot_be_demoted_deactivated_or_deleted()
    {
        await ClearRosterAsync();
        var last = await _data.UserAsync(RoleNames.SuperAdmin);
        // A Super Admin who has just been deactivated but whose session is still open.
        var stale = await _data.UserAsync(RoleNames.SuperAdmin, deactivated: true);

        var demote = await EditAsync(stale, last, RoleNames.Viewer);
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        Assert.Contains("last active Super Admin", System.Net.WebUtility.HtmlDecode(await demote.Content.ReadAsStringAsync()));
        await _f.ClientFor(stale).PostAsync($"/Admin/Users/Deactivate/{last}", Form());
        await _f.ClientFor(stale).PostAsync($"/Admin/Users/Delete/{last}", Form());

        Assert.True(await IsActiveSuperAdminAsync(last));
    }

    [Fact]
    public async Task Arabic_refusal_is_in_arabic()
    {
        await ClearRosterAsync();
        var me = await _data.UserAsync(RoleNames.SuperAdmin);
        var email = await _data.Db(db => db.Users.Where(u => u.Id == me).Select(u => u.Email!).FirstAsync());
        var r = await _f.ClientFor(me, "ar").PostAsync("/Admin/Users/Edit", Form(("Id", me), ("FullName", "x"), ("Email", email),
            ("RoleName", RoleNames.Viewer), ("PreferredLanguage", "ar"), ("IsActive", "true")));
        var html = System.Net.WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains("لا يمكنكم إزالة صلاحية المشرف العام من حسابكم", html);
    }

    [Fact]
    public async Task Another_super_admin_can_be_demoted_while_one_remains()
    {
        await ClearRosterAsync();
        var a = await _data.UserAsync(RoleNames.SuperAdmin);
        var b = await _data.UserAsync(RoleNames.SuperAdmin);

        var r = await EditAsync(a, b, RoleNames.DscAdmin);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.False(await IsActiveSuperAdminAsync(b));
        Assert.True(await IsActiveSuperAdminAsync(a));
    }

    [Fact]
    public async Task Two_super_admins_removing_each_other_at_once_leave_one_in_place()
    {
        for (var round = 0; round < 5; round++)
        {
            await ClearRosterAsync();
            var a = await _data.UserAsync(RoleNames.SuperAdmin);
            var b = await _data.UserAsync(RoleNames.SuperAdmin);

            await Task.WhenAll(EditAsync(a, b, RoleNames.Viewer), EditAsync(b, a, RoleNames.Viewer));

            var remaining = (await IsActiveSuperAdminAsync(a) ? 1 : 0) + (await IsActiveSuperAdminAsync(b) ? 1 : 0);
            Assert.Equal(1, remaining);
        }
    }

    [Fact]
    public async Task Concurrent_deactivation_and_deletion_leave_one_in_place()
    {
        for (var round = 0; round < 5; round++)
        {
            await ClearRosterAsync();
            var a = await _data.UserAsync(RoleNames.SuperAdmin);
            var b = await _data.UserAsync(RoleNames.SuperAdmin);

            await Task.WhenAll(
                _f.ClientFor(a).PostAsync($"/Admin/Users/Deactivate/{b}", Form()),
                _f.ClientFor(b).PostAsync($"/Admin/Users/Delete/{a}", Form()));

            var remaining = (await IsActiveSuperAdminAsync(a) ? 1 : 0) + (await IsActiveSuperAdminAsync(b) ? 1 : 0);
            Assert.Equal(1, remaining);
        }
    }

    [Fact]
    public async Task Other_roles_are_managed_normally()
    {
        await ClearRosterAsync();
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var clubUser = await _data.UserAsync(RoleNames.ClubAdmin, false, club);

        Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(admin, clubUser, RoleNames.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await _f.ClientFor(admin).PostAsync($"/Admin/Users/Deactivate/{clubUser}", Form())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await _f.ClientFor(admin).PostAsync($"/Admin/Users/Delete/{clubUser}", Form())).StatusCode);
        Assert.Null(await _data.Db(db => db.Users.FirstOrDefaultAsync(u => u.Id == clubUser)));
    }

    [Fact]
    public async Task Continuity_rule_refuses_the_last_super_admin_for_any_actor()
    {
        await ClearRosterAsync();
        var last = await _data.UserAsync(RoleNames.SuperAdmin);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var target = await users.FindByIdAsync(last);

        Assert.NotNull(await UserAdministration.CheckSuperAdminContinuityAsync(db, users, "someone-else", target!, keepsSuperAdmin: false, staysActive: true));
        Assert.NotNull(await UserAdministration.CheckSuperAdminContinuityAsync(db, users, "someone-else", target!, keepsSuperAdmin: true, staysActive: false));
        Assert.Null(await UserAdministration.CheckSuperAdminContinuityAsync(db, users, "someone-else", target!, keepsSuperAdmin: true, staysActive: true));
    }
}
