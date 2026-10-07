using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = RoleNames.SuperAdmin)]
public class UsersController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    private readonly EmailSender _email;

    public UsersController(AppDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, EmailSender email)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _email = email;
    }

    // Passwords are stored hashed, so the current one cannot be sent. Sending credentials therefore sets a
    // NEW random password and emails it with the username; any password handed out earlier stops working.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCredentials(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        var (ok, error) = await SendCredentialsToAsync(user);
        if (ok) TempData["ToastSuccess"] = $"Login details sent to {user.Email}.";
        else TempData["ToastWarning"] = error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCredentialsBulk(List<string> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            TempData["ToastWarning"] = "Select at least one user.";
            return RedirectToAction(nameof(Index));
        }
        if (!_email.IsConfigured)
        {
            TempData["ToastWarning"] = "Email is not configured on this server yet.";
            return RedirectToAction(nameof(Index));
        }

        int sent = 0;
        var failed = new List<string>();
        foreach (var id in ids.Distinct())
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) continue;
            var (ok, _) = await SendCredentialsToAsync(user);
            if (ok) sent++; else failed.Add(user.Email ?? id);
        }
        TempData["ToastSuccess"] = $"Login details sent to {sent} user(s).";
        if (failed.Count > 0) TempData["ToastWarning"] = "Not sent: " + string.Join(", ", failed);
        return RedirectToAction(nameof(Index));
    }

    private const string CredentialsSentAction = "LoginDetailsSent";

    private async Task<(bool ok, string? error)> SendCredentialsToAsync(ApplicationUser user)
    {
        if (!_email.IsConfigured) return (false, "Email is not configured on this server yet.");
        if (string.IsNullOrWhiteSpace(user.Email)) return (false, "This user has no email address.");
        if (user.Email.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return (false, $"{user.Email} is a test address and cannot receive email.");
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow.AddYears(1))
            return (false, $"{user.Email} is deactivated. Activate the account first.");

        var password = PlatformUserSeeder.GeneratePassword();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await _userManager.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
            return (false, $"Could not set a password for {user.Email}: " + string.Join("; ", reset.Errors.Select(e => e.Description)));

        var loginUrl = Url.Action("Login", "Account", new { area = "" }, Request.Scheme)!;
        var forgotUrl = Url.Action("ForgotPassword", "Account", new { area = "" }, Request.Scheme)!;
        var E = EmailSender.Encode;
        var name = E(user.FullName ?? user.Email);
        var siteUrl = $"{Request.Scheme}://{Request.Host}";
        var link = "color:#2D9B6C;font-weight:600;";
        var body = EmailSender.Layout(siteUrl, "Your Ghars Platform login details | بيانات الدخول إلى منصة غرس",
            EmailSender.Section(false, "Welcome to the Ghars Platform",
                $"<p style=\"margin:0 0 10px\">Dear {name},</p><p style=\"margin:0\">Your account is ready. Use the details below to sign in.</p>",
                loginUrl, "Sign in to Ghars",
                new[] { ("Username", E(user.Email)), ("Password", E(password)) }) +
            $"<p style=\"font-family:'Segoe UI',Tahoma,Arial,sans-serif;font-size:13px;line-height:1.6;color:#6b7280;margin:14px 0 0\">For your security, please choose your own password after signing in using <a href=\"{E(forgotUrl)}\" style=\"{link}\">Forgot password</a>, and keep these details private.</p>",
            EmailSender.Section(true, "مرحباً بك في منصة غرس",
                $"<p style=\"margin:0 0 10px\">عزيزي/عزيزتي {name}،</p><p style=\"margin:0\">تم تجهيز حسابك. استخدم البيانات التالية لتسجيل الدخول.</p>",
                loginUrl, "تسجيل الدخول إلى غرس",
                new[] { ("اسم المستخدم", E(user.Email)), ("كلمة المرور", E(password)) }) +
            $"<p dir=\"rtl\" style=\"text-align:right;font-family:'Segoe UI',Tahoma,Arial,sans-serif;font-size:13px;line-height:1.6;color:#6b7280;margin:14px 0 0\">حفاظاً على أمان حسابك، يرجى اختيار كلمة مرور خاصة بك بعد تسجيل الدخول من خلال <a href=\"{E(forgotUrl)}\" style=\"{link}\">نسيت كلمة المرور</a>، والحفاظ على سرية هذه البيانات.</p>");

        try
        {
            await _email.SendAsync(user.Email, "Ghars Platform - your login details | منصة غرس - بيانات الدخول", body);
            // Recorded so the users list can show when (and by whom) login details were last sent.
            _db.SystemAuditLogs.Add(new SystemAuditLog
            {
                UserId = _userManager.GetUserId(User),
                Action = CredentialsSentAction,
                EntityName = "User",
                EntityId = user.Id,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { user.Email })
            });
            await _db.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            // The password was already changed; the user can still recover through Forgot password.
            return (false, $"Password was reset but the email to {user.Email} failed: {ex.Message}");
        }
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.OrderBy(x => x.Email).ToListAsync();
        var sends = (await _db.SystemAuditLogs.AsNoTracking()
                .Where(x => x.Action == CredentialsSentAction && x.EntityName == "User" && x.EntityId != null)
                .Select(x => new { x.EntityId, x.AtUtc, x.UserId })
                .ToListAsync())
            .GroupBy(x => x.EntityId!)
            .ToDictionary(g => g.Key, g => (Last: g.OrderByDescending(x => x.AtUtc).First(), Count: g.Count()));
        var senderIds = sends.Values.Select(v => v.Last.UserId).Where(x => x != null).Distinct().ToList();
        var senderNames = await _userManager.Users.Where(x => senderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName ?? x.Email ?? "");
        var rows = new List<UserRowVm>();
        foreach (var u in users)
        {
            rows.Add(new UserRowVm
            {
                Id = u.Id,
                Email = u.Email ?? "",
                FullName = u.FullName ?? "",
                PrimaryOrganizationId = u.PrimaryOrganizationId,
                IsLocked = u.LockoutEnd.HasValue && u.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow,
                Roles = string.Join(", ", await _userManager.GetRolesAsync(u)),
                LoginSentAtUtc = sends.TryGetValue(u.Id, out var s) ? s.Last.AtUtc : null,
                LoginSentCount = sends.TryGetValue(u.Id, out var s2) ? s2.Count : 0,
                LoginSentBy = sends.TryGetValue(u.Id, out var s3) && s3.Last.UserId != null
                    && senderNames.TryGetValue(s3.Last.UserId, out var n) ? n : null
            });
        }
        return View(rows);
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Create()
    {
        await LoadLookupsAsync();
        return View(new UserEditVm { PreferredLanguage = "en", RoleName = RoleNames.Viewer, IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Create(UserEditVm vm)
    {
        await LoadLookupsAsync();
        if (!ModelState.IsValid) return View(vm);
        if (!await _roleManager.RoleExistsAsync(vm.RoleName))
        {
            ModelState.AddModelError(nameof(vm.RoleName), "Selected role does not exist.");
            return View(vm);
        }
        var user = new ApplicationUser
        {
            UserName = vm.Email.Trim(),
            Email = vm.Email.Trim(),
            FullName = vm.FullName.Trim(),
            PreferredLanguage = vm.PreferredLanguage,
            EmailConfirmed = true,
            PrimaryOrganizationId = vm.PrimaryOrganizationId,
            CreatedAtUtc = DateTime.UtcNow
        };
        var created = await _userManager.CreateAsync(user, vm.Password!);
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        await _userManager.AddToRoleAsync(user, vm.RoleName);
        TempData["ToastSuccess"] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        await LoadLookupsAsync();
        var roles = await _userManager.GetRolesAsync(user);
        return View(new UserEditVm
        {
            Id = user.Id,
            Email = user.Email ?? "",
            FullName = user.FullName ?? "",
            PreferredLanguage = user.PreferredLanguage ?? "en",
            PrimaryOrganizationId = user.PrimaryOrganizationId,
            RoleName = roles.FirstOrDefault() ?? RoleNames.Viewer,
            IsActive = !(user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(UserEditVm vm)
    {
        await LoadLookupsAsync();
        var user = await _userManager.FindByIdAsync(vm.Id ?? "");
        if (user == null) return NotFound();
        ModelState.Remove(nameof(vm.Password));
        if (!ModelState.IsValid) return View(vm);
        user.Email = vm.Email.Trim();
        user.UserName = vm.Email.Trim();
        user.FullName = vm.FullName.Trim();
        user.PreferredLanguage = vm.PreferredLanguage;
        user.PrimaryOrganizationId = vm.PrimaryOrganizationId;
        user.LockoutEnd = vm.IsActive ? null : DateTimeOffset.UtcNow.AddYears(100);
        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            foreach (var e in updated.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Any()) await _userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!string.IsNullOrWhiteSpace(vm.RoleName)) await _userManager.AddToRoleAsync(user, vm.RoleName);
        if (!string.IsNullOrWhiteSpace(vm.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            await _userManager.ResetPasswordAsync(user, token, vm.Password);
        }
        TempData["ToastSuccess"] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        await _userManager.UpdateAsync(user);
        TempData["ToastWarning"] = "User deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        await _userManager.DeleteAsync(user);
        TempData["ToastWarning"] = "User deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadLookupsAsync()
    {
        ViewBag.Roles = await _roleManager.Roles.OrderBy(x => x.Name).Select(x => x.Name!).ToListAsync();
        ViewBag.Organizations = await _db.Organizations.OrderBy(x => x.NameEn).ToListAsync();
    }

    public class UserRowVm
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Roles { get; set; } = "";
        public int? PrimaryOrganizationId { get; set; }
        public bool IsLocked { get; set; }
        public DateTime? LoginSentAtUtc { get; set; }
        public int LoginSentCount { get; set; }
        public string? LoginSentBy { get; set; }
    }

    public class UserEditVm
    {
        public string? Id { get; set; }
        [Required, MaxLength(200)] public string FullName { get; set; } = "";
        [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
        [DataType(DataType.Password)] public string? Password { get; set; }
        [Required] public string RoleName { get; set; } = RoleNames.Viewer;
        public int? PrimaryOrganizationId { get; set; }
        [Required] public string PreferredLanguage { get; set; } = "en";
        public bool IsActive { get; set; } = true;
    }
}
