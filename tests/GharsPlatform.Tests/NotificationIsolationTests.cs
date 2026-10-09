using System.Net;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GharsPlatform.Tests;

/// <summary>
/// The live SignalR push reaches exactly the notification's audience. Each test connects real hub
/// clients for the intended recipients and for bystanders, sends a notification with a unique
/// title, and checks who received it — and that the inbox deliveries match.
/// </summary>
[Collection(AppCollection.Name)]
public class NotificationIsolationTests
{
    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(10);
    // After the intended recipients have their copy, bystanders get this long for one to show up.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1500);

    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public NotificationIsolationTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    private async Task<HttpResponseMessage> PostAdminNotificationAsync(string adminId, string title, NotificationTargetType target,
        string? userId = null, int? orgId = null, string? role = null)
    {
        var form = new Dictionary<string, string>
        {
            ["TitleEn"] = title,
            ["TitleAr"] = "عنوان " + title,
            ["MessageEn"] = "Private message body " + title,
            ["MessageAr"] = "نص خاص " + title,
            ["Type"] = ((byte)NotificationType.Info).ToString(),
            ["TargetType"] = ((byte)target).ToString(),
            ["LinkUrl"] = "/notifications"
        };
        if (userId is not null) form["TargetUserId"] = userId;
        if (orgId is not null) form["TargetOrganizationId"] = orgId.Value.ToString();
        if (role is not null) form["TargetRoleName"] = role;
        return await _f.ClientFor(adminId).PostAsync("/Admin/Notifications/Create", new FormUrlEncodedContent(form));
    }

    private Task<List<string>> DeliveredToAsync(string title)
        => _data.Db(db => db.NotificationDeliveries.Where(d => d.Notification!.TitleEn == title).Select(d => d.UserId).ToListAsync());

    [Fact]
    public async Task User_target_reaches_only_that_user()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var target = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var sameClub = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var dsc = await _data.UserAsync(RoleNames.DscAdmin);

        await using var t = await HubListener.ConnectAsync(_f, target);
        await using var b1 = await HubListener.ConnectAsync(_f, sameClub);
        await using var b2 = await HubListener.ConnectAsync(_f, dsc);

        var title = "user-" + TestData.Unique();
        var response = await PostAdminNotificationAsync(admin, title, NotificationTargetType.User, userId: target);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await t.WaitForAsync(title, Arrival), "the target did not receive the notification");
        await Task.Delay(Settle);
        Assert.False(b1.Has(title), "another member of the same club received a user-targeted notification");
        Assert.False(b2.Has(title), "an administrator who was not the target received it");
        Assert.Equal(new[] { target }, await DeliveredToAsync(title));

        // Both languages travel to the recipient; the page shows the one it is in.
        var payload = t.Received.First(p => HubListener.Title(p) == title);
        Assert.Equal("عنوان " + title, payload.GetProperty("titleAr").GetString());
    }

    [Fact]
    public async Task Organization_target_reaches_only_current_authorized_members()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var otherClub = await _data.OrganizationAsync(OrganizationType.Club);
        var member = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var viewerLinked = await _data.UserAsync(RoleNames.Viewer, false, club);          // link but no workspace role
        var deactivated = await _data.UserAsync(RoleNames.ClubAdmin, true, club);        // deactivated account
        var removed = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        await _data.UnlinkAsync(removed, club);                                           // membership removed
        var outsider = await _data.UserAsync(RoleNames.ClubAdmin, false, otherClub);

        await using var m = await HubListener.ConnectAsync(_f, member);
        await using var v = await HubListener.ConnectAsync(_f, viewerLinked);
        await using var d = await HubListener.ConnectAsync(_f, deactivated);
        await using var r = await HubListener.ConnectAsync(_f, removed);
        await using var o = await HubListener.ConnectAsync(_f, outsider);

        var title = "org-" + TestData.Unique();
        await PostAdminNotificationAsync(admin, title, NotificationTargetType.Organization, orgId: club);

        Assert.True(await m.WaitForAsync(title, Arrival));
        await Task.Delay(Settle);
        Assert.False(v.Has(title), "a linked account without the club role received it");
        Assert.False(d.Has(title), "a deactivated account received it");
        Assert.False(r.Has(title), "a user whose membership was removed received it");
        Assert.False(o.Has(title), "a member of another organization received it");
        Assert.Equal(new[] { member }, await DeliveredToAsync(title));
    }

    [Fact]
    public async Task Role_target_reaches_only_holders_of_that_role()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var speaker = await _data.UserAsync(RoleNames.Speaker);
        var viewer = await _data.UserAsync(RoleNames.Viewer);

        await using var s = await HubListener.ConnectAsync(_f, speaker);
        await using var v = await HubListener.ConnectAsync(_f, viewer);

        var title = "role-" + TestData.Unique();
        await PostAdminNotificationAsync(admin, title, NotificationTargetType.Role, role: RoleNames.Speaker);

        Assert.True(await s.WaitForAsync(title, Arrival));
        await Task.Delay(Settle);
        Assert.False(v.Has(title));

        var delivered = await DeliveredToAsync(title);
        var activeSpeakers = await _data.Db(db => NotificationDispatcher.ActiveUsers(db)
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.Speaker)))
            .Select(u => u.Id).ToListAsync());
        Assert.Equal(activeSpeakers.OrderBy(x => x), delivered.OrderBy(x => x));
        Assert.DoesNotContain(viewer, delivered);
    }

    [Fact]
    public async Task Dsc_reviewer_notifications_reach_only_dsc_admins_and_super_admins()
    {
        var dsc = await _data.UserAsync(RoleNames.DscAdmin);
        var super = await _data.UserAsync(RoleNames.SuperAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var clubUser = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var partner = await _data.UserAsync(RoleNames.PartnerAdmin);

        await using var a = await HubListener.ConnectAsync(_f, dsc);
        await using var b = await HubListener.ConnectAsync(_f, super);
        await using var c = await HubListener.ConnectAsync(_f, clubUser);
        await using var p = await HubListener.ConnectAsync(_f, partner);

        // The public contact form is a real DSC-reviewer producer, and its message carries the
        // sender's name — exactly what must not reach other signed-in users.
        var subject = "contact-" + TestData.Unique();
        var response = await _f.ClientFor(null).PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Private Sender",
            ["Email"] = "sender@tests.ghars.local",
            ["Subject"] = subject,
            ["Message"] = "A private enquiry used by the notification isolation test.",
            ["Topic"] = "1"
        }));
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);

        bool Got(HubListener l) => l.Received.Any(x => x.GetProperty("messageEn").GetString()!.Contains(subject));
        var until = DateTime.UtcNow + Arrival;
        while (DateTime.UtcNow < until && !(Got(a) && Got(b))) await Task.Delay(50);
        Assert.True(Got(a), "the DSC Admin did not receive the enquiry");
        Assert.True(Got(b), "the Super Admin did not receive the enquiry");
        await Task.Delay(Settle);
        Assert.False(Got(c), "a club user received a contact enquiry");
        Assert.False(Got(p), "an entity user received a contact enquiry");
    }

    [Fact]
    public async Task All_target_reaches_every_active_user_and_no_deactivated_one()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var active = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var viewer = await _data.UserAsync(RoleNames.Viewer);
        var deactivated = await _data.UserAsync(RoleNames.Viewer, true);

        await using var a = await HubListener.ConnectAsync(_f, active);
        await using var v = await HubListener.ConnectAsync(_f, viewer);
        await using var d = await HubListener.ConnectAsync(_f, deactivated);

        var title = "all-" + TestData.Unique();
        await PostAdminNotificationAsync(admin, title, NotificationTargetType.All);

        Assert.True(await a.WaitForAsync(title, Arrival));
        Assert.True(await v.WaitForAsync(title, Arrival));
        await Task.Delay(Settle);
        Assert.False(d.Has(title), "a deactivated account received a general notification");

        var delivered = await DeliveredToAsync(title);
        var activeCount = await _data.Db(db => NotificationDispatcher.ActiveUsers(db).CountAsync());
        Assert.Equal(activeCount, delivered.Count);
        Assert.DoesNotContain(deactivated, delivered);
    }

    [Fact]
    public async Task Organization_notification_from_a_workflow_does_not_leak_to_other_organizations()
    {
        // KPI review decision, sent through the dispatcher the way every producer now does.
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var otherClub = await _data.OrganizationAsync(OrganizationType.Club);
        var member = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var outsider = await _data.UserAsync(RoleNames.ClubAdmin, false, otherClub);

        await using var m = await HubListener.ConnectAsync(_f, member);
        await using var o = await HubListener.ConnectAsync(_f, outsider);

        var title = "workflow-" + TestData.Unique();
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GharsPlatform.Data.AppDbContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationsHub>>();
            var sent = await NotificationDispatcher.SendToOrganizationAsync(db, hub,
                new Notification { TitleEn = title, TitleAr = title, MessageEn = "m", MessageAr = "m" }, club);
            Assert.Equal(1, sent);
        }

        Assert.True(await m.WaitForAsync(title, Arrival));
        await Task.Delay(Settle);
        Assert.False(o.Has(title));
    }

    [Fact]
    public async Task Admin_form_refuses_a_missing_target_and_an_external_link()
    {
        var admin = await _data.UserAsync(RoleNames.SuperAdmin);
        var title = "invalid-" + TestData.Unique();

        var noUser = await PostAdminNotificationAsync(admin, title, NotificationTargetType.User, userId: "no-such-user");
        Assert.Equal(HttpStatusCode.OK, noUser.StatusCode); // form shown again with the error

        var client = _f.ClientFor(admin);
        var external = await client.PostAsync("/Admin/Notifications/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TitleEn"] = title, ["TitleAr"] = title, ["MessageEn"] = "m", ["MessageAr"] = "m",
            ["Type"] = "1", ["TargetType"] = ((byte)NotificationTargetType.All).ToString(),
            ["LinkUrl"] = "https://example.org/phish"
        }));
        Assert.Equal(HttpStatusCode.OK, external.StatusCode);
        Assert.Empty(await DeliveredToAsync(title));
        Assert.Equal(0, await _data.Db(db => db.Notifications.CountAsync(n => n.TitleEn == title)));
    }
}
