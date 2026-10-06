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
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var name = E(user.FullName ?? user.Email);
        var body = EmailSender.Bilingual(
            $"<p>Dear {name},</p><p>Your account on the Ghars Platform is ready.</p>" +
            $"<p><b>Username:</b> {E(user.Email)}<br><b>Password:</b> <code>{E(password)}</code></p>" +
            $"<p>Sign in at <a href=\"{E(loginUrl)}\">{E(loginUrl)}</a>. You can choose your own password at any time with " +
            $"<a href=\"{E(forgotUrl)}\">Forgot password</a>.</p><p>Please keep these details private.</p>",
            $"<p>عزيزي/عزيزتي {name}،</p><p>تم تجهيز حسابك على منصة غرس.</p>" +
            $"<p><b>اسم المستخدم:</b> <span dir=\"ltr\">{E(user.Email)}</span><br><b>كلمة المرور:</b> <code dir=\"ltr\">{E(password)}</code></p>" +
            $"<p>يمكنك تسجيل الدخول عبر <a href=\"{E(loginUrl)}\">{E(loginUrl)}</a>، ويمكنك تغيير كلمة المرور في أي وقت من خلال " +
            $"<a href=\"{E(forgotUrl)}\">نسيت كلمة المرور</a>.</p><p>يرجى الحفاظ على سرية هذه البيانات.</p>");

        try
        {
            await _email.SendAsync(user.Email, "Ghars Platform - your login details | منصة غرس - بيانات الدخول", body);
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
                Roles = string.Join(", ", await _userManager.GetRolesAsync(u))
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
