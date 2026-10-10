using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Models.Validation;
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

    private static bool IsAr() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public async Task<IActionResult> Index()
    {
        var list = await Db.Notifications.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync();
        return View(list);
    }

    // Lookups for the target pickers. They post the same values the form always did: the role NAME
    // (matched against AspNetRoles.Name) and the user ID.
    private async Task LoadTargetsAsync()
    {
        ViewBag.Organizations = await Db.Organizations.OrderBy(x => x.NameEn).ToListAsync();
        ViewBag.Roles = await Db.Roles.Where(r => r.Name != null).OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync();
        ViewBag.Users = await Db.Users.OrderBy(u => u.FullName ?? u.Email)
            .Select(u => new[] { u.Id, u.FullName ?? "", u.Email ?? "" }).ToListAsync();
    }

    public async Task<IActionResult> Create()
    {
        await LoadTargetsAsync();
        return View(new NotificationVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NotificationVm vm)
    {
        await LoadTargetsAsync();

        // The target must name something that exists; an empty or unknown target used to send to
        // nobody while the page still reported success.
        switch (vm.TargetType)
        {
            case NotificationTargetType.Organization when vm.TargetOrganizationId is null || !await Db.Organizations.AnyAsync(o => o.Id == vm.TargetOrganizationId):
                ModelState.AddModelError(nameof(vm.TargetOrganizationId), IsAr() ? "اختاروا الجهة المستهدفة." : "Choose the organization to notify.");
                break;
            case NotificationTargetType.Role when string.IsNullOrWhiteSpace(vm.TargetRoleName) || !await Db.Roles.AnyAsync(r => r.Name == vm.TargetRoleName):
                ModelState.AddModelError(nameof(vm.TargetRoleName), IsAr() ? "اختاروا الدور المستهدف." : "Choose the role to notify.");
                break;
            case NotificationTargetType.User when string.IsNullOrWhiteSpace(vm.TargetUserId) || !await Db.Users.AnyAsync(u => u.Id == vm.TargetUserId):
                ModelState.AddModelError(nameof(vm.TargetUserId), IsAr() ? "اختاروا المستخدم المستهدف." : "Choose the user to notify.");
                break;
        }

        // Opened through LocalRedirect from the inbox, so only a path on this site can work.
        if (!string.IsNullOrWhiteSpace(vm.LinkUrl) && !Url.IsLocalUrl(vm.LinkUrl.Trim()))
            ModelState.AddModelError(nameof(vm.LinkUrl), IsAr() ? "يجب أن يكون الرابط صفحة داخل المنصة ويبدأ بـ /." : "The link must be a page on this platform, starting with /.");

        if (!ModelState.IsValid) return View(vm);

        var n = new Notification
        {
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            MessageEn = vm.MessageEn.Trim(),
            MessageAr = vm.MessageAr.Trim(),
            Type = vm.Type,
            TargetType = vm.TargetType,
            // Only the field that matches the target type is kept, so the stored row says who it was for.
            TargetRoleName = vm.TargetType == NotificationTargetType.Role ? vm.TargetRoleName?.Trim() : null,
            TargetOrganizationId = vm.TargetType == NotificationTargetType.Organization ? vm.TargetOrganizationId : null,
            TargetUserId = vm.TargetType == NotificationTargetType.User ? vm.TargetUserId?.Trim() : null,
            LinkUrl = string.IsNullOrWhiteSpace(vm.LinkUrl) ? null : vm.LinkUrl.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        // Recipients are resolved on the server and the live push goes to them alone; the count
        // reported below is the number of deliveries actually written.
        var sent = await NotificationDispatcher.SendAsync(Db, _hub, n);

        if (sent == 0)
            TempData["ToastWarning"] = IsAr() ? "تم حفظ الإشعار، لكن لا يوجد مستخدم نشط ضمن الفئة المستهدفة." : "The notification was saved, but no active user matches this target.";
        else
            TempData["ToastSuccess"] = IsAr() ? $"تم إرسال الإشعار إلى {sent} من المستخدمين." : $"Notification sent to {sent} user(s).";
        return RedirectToAction(nameof(Index));
    }

    public class NotificationVm
    {
        [BilingualRequired(ErrorMessage = "Enter the title in English.", Ar = "أدخلوا العنوان بالإنجليزية."), MaxLength(150)]
        public string TitleEn { get; set; } = "";

        [BilingualRequired(ErrorMessage = "Enter the title in Arabic.", Ar = "أدخلوا العنوان بالعربية."), MaxLength(150)]
        public string TitleAr { get; set; } = "";

        [BilingualRequired(ErrorMessage = "Enter the message in English.", Ar = "أدخلوا الرسالة بالإنجليزية."), MaxLength(2000)]
        public string MessageEn { get; set; } = "";

        [BilingualRequired(ErrorMessage = "Enter the message in Arabic.", Ar = "أدخلوا الرسالة بالعربية."), MaxLength(2000)]
        public string MessageAr { get; set; } = "";

        public NotificationType Type { get; set; } = NotificationType.Info;

        public NotificationTargetType TargetType { get; set; } = NotificationTargetType.All;

        public string? TargetRoleName { get; set; }

        public int? TargetOrganizationId { get; set; }

        public string? TargetUserId { get; set; }

        public string? LinkUrl { get; set; }
    }
}
