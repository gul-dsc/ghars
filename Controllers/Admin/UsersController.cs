using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Models.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = RoleNames.SuperAdmin)]
public class UsersController : Controller
{
    private static bool IsAr() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

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
        if (ok) TempData["ToastSuccess"] = IsAr() ? $"تم إرسال بيانات الدخول إلى {user.Email}." : $"Login details sent to {user.Email}.";
        else TempData["ToastWarning"] = error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCredentialsBulk(List<string> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            TempData["ToastWarning"] = IsAr() ? "اختاروا مستخدماً واحداً على الأقل." : "Select at least one user.";
            return RedirectToAction(nameof(Index));
        }
        if (!_email.IsConfigured)
        {
            TempData["ToastWarning"] = IsAr() ? "لا يمكن إرسال بيانات الدخول بالبريد حالياً لأن إرسال البريد الإلكتروني غير مُعدّ على الخادم." : "Login details can't be emailed yet because email sending isn't set up on this server.";
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
        TempData["ToastSuccess"] = IsAr() ? $"تم إرسال بيانات الدخول إلى {sent} من المستخدمين." : $"Login details sent to {sent} user(s).";
        if (failed.Count > 0) TempData["ToastWarning"] = (IsAr() ? "لم تُرسل إلى: " : "Not sent: ") + string.Join(", ", failed);
        return RedirectToAction(nameof(Index));
    }

    private const string CredentialsSentAction = "LoginDetailsSent";

    private async Task<(bool ok, string? error)> SendCredentialsToAsync(ApplicationUser user)
    {
        if (!_email.IsConfigured) return (false, IsAr() ? "لا يمكن إرسال بيانات الدخول بالبريد حالياً لأن إرسال البريد الإلكتروني غير مُعدّ على الخادم." : "Login details can't be emailed yet because email sending isn't set up on this server.");
        if (string.IsNullOrWhiteSpace(user.Email)) return (false, IsAr() ? "لا يوجد بريد إلكتروني لهذا المستخدم." : "This user has no email address.");
        if (user.Email.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return (false, IsAr() ? $"{user.Email} عنوان اختباري ولا يمكنه استقبال البريد." : $"{user.Email} is a test address and cannot receive email.");
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow.AddYears(1))
            return (false, IsAr() ? $"الحساب {user.Email} معطّل. فعّلوا الحساب أولاً." : $"{user.Email} is deactivated. Activate the account first.");

        var password = PlatformUserSeeder.GeneratePassword();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await _userManager.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
            return (false, (IsAr() ? $"تعذّر تعيين كلمة مرور لـ {user.Email}: " : $"Could not set a password for {user.Email}: ") + string.Join("; ", reset.Errors.Select(e => e.Description)));

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
            return (false, IsAr() ? $"تمت إعادة تعيين كلمة المرور، لكن تعذّر إرسال البريد إلى {user.Email}: {ex.Message}" : $"Password was reset but the email to {user.Email} failed: {ex.Message}");
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
        ViewBag.PlaceholderEmails = (await PlaceholderAccountsAsync()).Select(x => x.Email!).OrderBy(x => x).ToList();
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
        // Required here, not only by the browser: a request without a password used to fail inside
        // Identity with an unrelated message, or create an account nobody could sign in to.
        if (string.IsNullOrWhiteSpace(vm.Password))
            ModelState.AddModelError(nameof(vm.Password), IsAr() ? "أدخلوا كلمة مرور للحساب الجديد." : "Enter a password for the new account.");
        if (!ModelState.IsValid) return View(vm);
        if (!await _roleManager.RoleExistsAsync(vm.RoleName))
        {
            ModelState.AddModelError(nameof(vm.RoleName), IsAr() ? "الدور المحدد غير موجود." : "Selected role does not exist.");
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
        // Checked against the configured password rules before anything is written.
        if (await AddPasswordErrorsAsync(user, vm.Password!)) return View(vm);

        // The account and its role are created together, or not at all.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var created = await _userManager.CreateAsync(user, vm.Password!);
        var result = created.Succeeded ? await _userManager.AddToRoleAsync(user, vm.RoleName) : created;
        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        await tx.CommitAsync();
        TempData["ToastSuccess"] = IsAr() ? "تم إنشاء المستخدم." : "User created.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Runs every configured password validator and adds its (bilingual) errors to the Password
    /// field. True when the password was refused. The password itself is never logged or echoed.
    /// </summary>
    private async Task<bool> AddPasswordErrorsAsync(ApplicationUser user, string password)
    {
        var refused = false;
        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user, password);
            if (result.Succeeded) continue;
            refused = true;
            foreach (var e in result.Errors) ModelState.AddModelError(nameof(UserEditVm.Password), e.Description);
        }
        return refused;
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
        if (!await _roleManager.RoleExistsAsync(vm.RoleName))
        {
            ModelState.AddModelError(nameof(vm.RoleName), IsAr() ? "الدور المحدد غير موجود." : "Selected role does not exist.");
            return View(vm);
        }

        // All or nothing: a new password that breaks the rules, or any step that fails below, leaves
        // the account exactly as it was, and the page says what was refused. Nothing is reported as
        // updated unless every requested change was saved.
        var newPassword = string.IsNullOrWhiteSpace(vm.Password) ? null : vm.Password;
        if (newPassword is not null && await AddPasswordErrorsAsync(user, newPassword)) return View(vm);

        await using var tx = await _db.Database.BeginTransactionAsync();
        await UserAdministration.LockSuperAdminRosterAsync(_db);
        await _db.Entry(user).ReloadAsync();

        var refusal = await UserAdministration.CheckSuperAdminContinuityAsync(_db, _userManager, _userManager.GetUserId(User), user,
            keepsSuperAdmin: vm.RoleName == RoleNames.SuperAdmin, staysActive: vm.IsActive);
        if (refusal is not null)
        {
            ModelState.AddModelError("", refusal);
            return View(vm);
        }

        user.Email = vm.Email.Trim();
        user.UserName = vm.Email.Trim();
        user.FullName = vm.FullName.Trim();
        user.PreferredLanguage = vm.PreferredLanguage;
        user.PrimaryOrganizationId = vm.PrimaryOrganizationId;
        user.LockoutEnd = vm.IsActive ? null : DateTimeOffset.UtcNow.AddYears(100);

        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded)
        {
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!(currentRoles.Count == 1 && currentRoles[0] == vm.RoleName))
            {
                if (currentRoles.Any()) result = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (result.Succeeded) result = await _userManager.AddToRoleAsync(user, vm.RoleName);
            }
        }
        if (result.Succeeded && newPassword is not null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        }

        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            ModelState.AddModelError("", IsAr() ? "لم يتم حفظ أي تغيير على الحساب:" : "No changes were saved to this account:");
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }

        await tx.CommitAsync();
        TempData["ToastSuccess"] = newPassword is null
            ? (IsAr() ? "تم تحديث المستخدم." : "User updated.")
            : (IsAr() ? "تم تحديث المستخدم وتعيين كلمة المرور الجديدة." : "User updated and the new password set.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        await using var tx = await _db.Database.BeginTransactionAsync();
        await UserAdministration.LockSuperAdminRosterAsync(_db);
        await _db.Entry(user).ReloadAsync();
        var refusal = await UserAdministration.CheckSuperAdminContinuityAsync(_db, _userManager, _userManager.GetUserId(User), user,
            keepsSuperAdmin: true, staysActive: false);
        if (refusal is not null)
        {
            TempData["ToastWarning"] = refusal;
            return RedirectToAction(nameof(Index));
        }

        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            TempData["ToastWarning"] = (IsAr() ? "تعذّر تعطيل المستخدم: " : "The user could not be deactivated: ") + string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }
        await tx.CommitAsync();
        TempData["ToastWarning"] = IsAr() ? "تم تعطيل المستخدم." : "User deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        var email = user.Email;

        await using var tx = await _db.Database.BeginTransactionAsync();
        await UserAdministration.LockSuperAdminRosterAsync(_db);
        await _db.Entry(user).ReloadAsync();
        var refusal = await UserAdministration.CheckSuperAdminContinuityAsync(_db, _userManager, _userManager.GetUserId(User), user,
            keepsSuperAdmin: false, staysActive: false);
        if (refusal is not null)
        {
            TempData["ToastWarning"] = refusal;
            return RedirectToAction(nameof(Index));
        }

        if (await DeleteWithLinksAsync(user, tx))
            TempData["ToastWarning"] = IsAr() ? $"تم حذف المستخدم {email}." : $"User {email} deleted.";
        else
            TempData["ToastWarning"] = IsAr() ? $"تعذّر حذف المستخدم {email}، ولم يتم تغيير أي شيء." : $"User {email} could not be deleted. Nothing was changed.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The one-per-organization placeholder accounts made by seed-organization-accounts
    /// (club-&lt;name&gt;@domain / partner-&lt;name&gt;@domain) before the real nominees were known.
    /// Admin-role accounts are never matched.
    /// </summary>
    private async Task<List<ApplicationUser>> PlaceholderAccountsAsync()
    {
        var candidates = await _userManager.Users
            .Where(x => x.Email != null && (x.Email.StartsWith("club-") || x.Email.StartsWith("partner-")))
            .ToListAsync();
        var result = new List<ApplicationUser>();
        foreach (var u in candidates)
        {
            var roles = await _userManager.GetRolesAsync(u);
            if (!roles.Contains(RoleNames.SuperAdmin) && !roles.Contains(RoleNames.DscAdmin)) result.Add(u);
        }
        return result;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePlaceholderAccounts()
    {
        int deleted = 0;
        foreach (var user in await PlaceholderAccountsAsync())
        {
            // One transaction per account: a failure keeps that account and its links intact and
            // does not undo the ones already removed.
            await using var tx = await _db.Database.BeginTransactionAsync();
            if (await DeleteWithLinksAsync(user, tx)) deleted++;
        }
        TempData["ToastSuccess"] = IsAr() ? $"تم حذف {deleted} من حسابات الإعداد غير المستخدمة." : $"Removed {deleted} unused setup account(s).";
        return RedirectToAction(nameof(Index));
    }

    // OrganizationAdminLinks is not an Identity FK, so it is removed with the account in one
    // transaction: both go, or neither does. Records that name the user as history are kept.
    private async Task<bool> DeleteWithLinksAsync(ApplicationUser user, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
    {
        try
        {
            var result = await UserAdministration.DeleteUserWithLinksAsync(_db, _userManager, user);
            if (result.Succeeded)
            {
                await tx.CommitAsync();
                return true;
            }
        }
        catch (DbUpdateException)
        {
            // Rolled back below; the account and its links stay as they were.
        }
        await tx.RollbackAsync();
        _db.ChangeTracker.Clear();
        return false;
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
        [BilingualRequired(ErrorMessage = "Enter the full name.", Ar = "أدخلوا الاسم الكامل."), MaxLength(200)] public string FullName { get; set; } = "";
        [BilingualRequired(ErrorMessage = "Enter the email address.", Ar = "أدخلوا البريد الإلكتروني."), BilingualEmailAddress(ErrorMessage = "Enter a valid email address.", Ar = "أدخلوا بريداً إلكترونياً صالحاً."), MaxLength(256)] public string Email { get; set; } = "";
        [DataType(DataType.Password)] public string? Password { get; set; }
        [Required] public string RoleName { get; set; } = RoleNames.Viewer;
        public int? PrimaryOrganizationId { get; set; }
        [Required] public string PreferredLanguage { get; set; } = "en";
        public bool IsActive { get; set; } = true;
    }
}
