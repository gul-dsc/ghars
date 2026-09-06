using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class NotificationsController : Controllers.BaseController
{
    private readonly IHubContext<NotificationsHub> _hub;

    public NotificationsController(AppDbContext db, IHubContext<NotificationsHub> hub) : base(db)
    {
        _hub = hub;
    }

    public async Task<IActionResult> Index()
    {
        var list = await Db.Notifications.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Organizations = await Db.Organizations.OrderBy(x => x.NameEn).ToListAsync();
        return View(new NotificationVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NotificationVm vm)
    {
        ViewBag.Organizations = await Db.Organizations.OrderBy(x => x.NameEn).ToListAsync();

        if (!ModelState.IsValid) return View(vm);

        var n = new Notification
        {
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            MessageEn = vm.MessageEn.Trim(),
            MessageAr = vm.MessageAr.Trim(),
            Type = vm.Type,
            TargetType = vm.TargetType,
            TargetRoleName = vm.TargetRoleName?.Trim(),
            TargetOrganizationId = vm.TargetOrganizationId,
            TargetUserId = vm.TargetUserId?.Trim(),
            LinkUrl = vm.LinkUrl?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        Db.Notifications.Add(n);
        await Db.SaveChangesAsync();

        // Resolve deliveries (snapshot)
        var userIds = new List<string>();

        if (n.TargetType == NotificationTargetType.All)
        {
            userIds = await Db.Users.Select(x => x.Id).ToListAsync();
        }
        else if (n.TargetType == NotificationTargetType.Organization && n.TargetOrganizationId.HasValue)
        {
            userIds = await Db.OrganizationAdminLinks
                .Where(x => x.OrganizationId == n.TargetOrganizationId.Value)
                .Select(x => x.UserId).Distinct().ToListAsync();
        }
        else if (n.TargetType == NotificationTargetType.User && !string.IsNullOrWhiteSpace(n.TargetUserId))
        {
            userIds = new List<string> { n.TargetUserId! };
        }
        else if (n.TargetType == NotificationTargetType.Role && !string.IsNullOrWhiteSpace(n.TargetRoleName))
        {
            // Identity role users (join via AspNetUserRoles)
            var role = await Db.Roles.FirstOrDefaultAsync(r => r.Name == n.TargetRoleName);
            if (role != null)
            {
                userIds = await Db.UserRoles.Where(ur => ur.RoleId == role.Id).Select(ur => ur.UserId).Distinct().ToListAsync();
            }
        }

        foreach (var uid in userIds)
        {
            Db.NotificationDeliveries.Add(new NotificationDelivery
            {
                NotificationId = n.Id,
                UserId = uid,
                DeliveredAtUtc = DateTime.UtcNow
            });
        }

        await Db.SaveChangesAsync();

        // Push to connected clients
        await _hub.Clients.All.SendAsync("notification", new
        {
            id = n.Id,
            titleEn = n.TitleEn,
            titleAr = n.TitleAr,
            messageEn = n.MessageEn,
            messageAr = n.MessageAr,
            type = n.Type.ToString(),
            linkUrl = n.LinkUrl,
            createdAtUtc = n.CreatedAtUtc
        });

        TempData["ToastSuccess"] = $"Notification sent to {userIds.Count} users.";
        return RedirectToAction(nameof(Index));
    }

    public class NotificationVm
    {
        [Required, MaxLength(150)]
        public string TitleEn { get; set; } = "";

        [Required, MaxLength(150)]
        public string TitleAr { get; set; } = "";

        [Required, MaxLength(2000)]
        public string MessageEn { get; set; } = "";

        [Required, MaxLength(2000)]
        public string MessageAr { get; set; } = "";

        public NotificationType Type { get; set; } = NotificationType.Info;

        public NotificationTargetType TargetType { get; set; } = NotificationTargetType.All;

        public string? TargetRoleName { get; set; }

        public int? TargetOrganizationId { get; set; }

        public string? TargetUserId { get; set; }

        public string? LinkUrl { get; set; }
    }
}
