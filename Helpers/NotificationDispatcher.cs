using GharsPlatform.Data;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// The one path by which an in-app notification reaches people: recipients are resolved on the
/// server, a delivery row is written for each (the inbox and read/unread state), and the live
/// SignalR push goes to exactly those users and nobody else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one path.</b> Every producer used to resolve its own recipients and then push the full
/// title, message and link to <c>Clients.All</c>, leaving it to the browser to ignore what was not
/// meant for it. Anyone signed in could read every notification as it was sent. The push now uses
/// <c>Clients.Users(...)</c> with the same list the deliveries were written for, so the live toast
/// and the inbox can never disagree about who was addressed.
/// </para>
/// <para>
/// <b>Recipient rules.</b>
/// <list type="bullet">
/// <item><b>User</b> — that account, if it exists and is not deactivated.</item>
/// <item><b>Organization</b> — accounts linked to the organization through
/// <see cref="OrganizationAdminLink"/> (the membership every workspace authorizes against) that
/// also hold the role that organization type's workspace requires, and are not deactivated. A
/// link left behind after a role change, or pointing at a deleted account, receives nothing.</item>
/// <item><b>Role</b> — active holders of that role.</item>
/// <item><b>DSC reviewers</b> — active DSC Admins and Super Admins (<see cref="SendToDscReviewersAsync"/>).</item>
/// <item><b>All</b> — every active account; used only when an administrator explicitly chooses it.</item>
/// </list>
/// "Deactivated" is the platform's existing convention: a lockout more than a year away.
/// </para>
/// <para>
/// <b>Language.</b> The push carries both languages and the browser shows the one the page is in,
/// which is the language the person is actually using; <see cref="ApplicationUser.PreferredLanguage"/>
/// is only a default and can differ from the page they have open.
/// </para>
/// </remarks>
public static class NotificationDispatcher
{
    public const string HubEvent = "notification";

    /// <summary>Sends a notification addressed by its own <see cref="Notification.TargetType"/> fields.</summary>
    /// <returns>The number of people it was delivered to.</returns>
    public static Task<int> SendAsync(AppDbContext db, IHubContext<NotificationsHub> hub, Notification n, ILogger? logger = null)
        => DispatchAsync(db, hub, n, dscReviewers: false, logger);

    /// <summary>Addresses the notification to an organization's authorized members and sends it.</summary>
    public static Task<int> SendToOrganizationAsync(AppDbContext db, IHubContext<NotificationsHub> hub, Notification n, int organizationId, ILogger? logger = null)
    {
        n.TargetType = NotificationTargetType.Organization;
        n.TargetOrganizationId = organizationId;
        return DispatchAsync(db, hub, n, dscReviewers: false, logger);
    }

    /// <summary>
    /// Addresses the notification to the DSC review queue: DSC Admins and Super Admins. Stored as a
    /// DSC Admin role target, as before; Super Admins are included so a site with no DSC Admin yet
    /// cannot lose a submission into a notification nobody receives.
    /// </summary>
    public static Task<int> SendToDscReviewersAsync(AppDbContext db, IHubContext<NotificationsHub> hub, Notification n, ILogger? logger = null)
    {
        n.TargetType = NotificationTargetType.Role;
        n.TargetRoleName = RoleNames.DscAdmin;
        return DispatchAsync(db, hub, n, dscReviewers: true, logger);
    }

    private static async Task<int> DispatchAsync(AppDbContext db, IHubContext<NotificationsHub> hub, Notification n, bool dscReviewers, ILogger? logger)
    {
        if (n.Id == 0)
        {
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
        }

        var userIds = dscReviewers
            ? await ActiveHoldersOfAsync(db, RoleNames.DscAdmin, RoleNames.SuperAdmin)
            : await ResolveRecipientsAsync(db, n);

        var now = DateTime.UtcNow;
        foreach (var uid in userIds)
            db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = uid, DeliveredAtUtc = now });
        await db.SaveChangesAsync();

        if (userIds.Count > 0)
        {
            try
            {
                await hub.Clients.Users(userIds).SendAsync(HubEvent, Payload(n));
            }
            catch (Exception ex)
            {
                // The deliveries are saved, so the inbox already has it; only the live toast is lost.
                logger?.LogWarning(ex, "Live push for notification {NotificationId} failed.", n.Id);
            }
        }

        return userIds.Count;
    }

    /// <summary>The users a notification is for, by the rules in the class remarks.</summary>
    public static async Task<List<string>> ResolveRecipientsAsync(AppDbContext db, Notification n)
    {
        switch (n.TargetType)
        {
            case NotificationTargetType.User:
                if (string.IsNullOrWhiteSpace(n.TargetUserId)) return new();
                return await ActiveUsers(db).Where(u => u.Id == n.TargetUserId).Select(u => u.Id).ToListAsync();

            case NotificationTargetType.Organization:
                return n.TargetOrganizationId is int orgId ? await OrganizationMembersAsync(db, orgId) : new();

            case NotificationTargetType.Role:
                return string.IsNullOrWhiteSpace(n.TargetRoleName) ? new() : await ActiveHoldersOfAsync(db, n.TargetRoleName);

            case NotificationTargetType.All:
                return await ActiveUsers(db).Select(u => u.Id).ToListAsync();

            default:
                return new();
        }
    }

    /// <summary>
    /// Accounts currently authorized to represent an organization: linked to it, holding the role its
    /// workspace requires, and not deactivated.
    /// </summary>
    public static async Task<List<string>> OrganizationMembersAsync(AppDbContext db, int organizationId)
    {
        var type = await db.Organizations.Where(o => o.Id == organizationId)
            .Select(o => (OrganizationType?)o.OrganizationType).FirstOrDefaultAsync();
        if (type is null) return new();

        var roleNames = MemberRolesFor(type.Value);
        var roleIds = db.Roles.Where(r => r.Name != null && roleNames.Contains(r.Name)).Select(r => r.Id);

        return await db.OrganizationAdminLinks
            .Where(l => l.OrganizationId == organizationId)
            .Join(ActiveUsers(db), l => l.UserId, u => u.Id, (l, u) => u.Id)
            .Where(uid => db.UserRoles.Any(ur => ur.UserId == uid && roleIds.Contains(ur.RoleId)))
            .Distinct()
            .ToListAsync();
    }

    /// <summary>The roles whose holders work inside an organization of this type.</summary>
    public static string[] MemberRolesFor(OrganizationType type) => type switch
    {
        OrganizationType.Club or OrganizationType.PrivateAcademy => new[] { RoleNames.ClubAdmin, RoleNames.AcademyAdmin },
        OrganizationType.GovernmentAuthority or OrganizationType.OtherPartner => new[] { RoleNames.PartnerAdmin },
        _ => new[] { RoleNames.SuperAdmin, RoleNames.DscAdmin }
    };

    private static async Task<List<string>> ActiveHoldersOfAsync(AppDbContext db, params string[] roleNames)
    {
        var roleIds = db.Roles.Where(r => r.Name != null && roleNames.Contains(r.Name)).Select(r => r.Id);
        return await ActiveUsers(db)
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)))
            .Select(u => u.Id)
            .ToListAsync();
    }

    /// <summary>Accounts that are not deactivated (the platform marks deactivation with a lockout years away).</summary>
    public static IQueryable<ApplicationUser> ActiveUsers(AppDbContext db)
    {
        var deactivatedAfter = DateTimeOffset.UtcNow.AddYears(1);
        return db.Users.Where(u => u.LockoutEnd == null || u.LockoutEnd <= deactivatedAfter);
    }

    public static object Payload(Notification n) => new
    {
        id = n.Id,
        titleEn = n.TitleEn,
        titleAr = n.TitleAr,
        messageEn = n.MessageEn,
        messageAr = n.MessageAr,
        type = n.Type.ToString(),
        linkUrl = n.LinkUrl,
        createdAtUtc = n.CreatedAtUtc
    };
}
