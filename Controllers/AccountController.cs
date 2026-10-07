using System.ComponentModel.DataAnnotations;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GharsPlatform.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return View(vm);

        var result = await _signInManager.PasswordSignInAsync(vm.Email, vm.Password, vm.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToAction("Index", "Home", new { area = "" });
        }

        ModelState.AddModelError("", "Invalid login attempt.");
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
    public IActionResult Register() => RedirectToAction("Create", "Users", new { area = "Admin" });

    [HttpPost]
    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
    [ValidateAntiForgeryToken]
    public IActionResult Register(RegisterVm vm) => RedirectToAction("Create", "Users", new { area = "Admin" });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
    public IActionResult AccessDenied() => View();

    public IActionResult ForgotPassword() => View(new ForgotPasswordVm());

    // Always answers the same way whether or not the address exists, so the page cannot be used to
    // discover who has an account.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordVm vm, [FromServices] EmailSender email, [FromServices] ILogger<AccountController> logger)
    {
        if (!ModelState.IsValid) return View(vm);
        if (!email.IsConfigured)
        {
            ModelState.AddModelError("", "Password reset by email is not available yet. Please contact the system administrator.");
            return View(vm);
        }

        var user = await _userManager.FindByEmailAsync(vm.Email.Trim());
        if (user != null && !string.IsNullOrWhiteSpace(user.Email) && !IsDeactivated(user))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = Url.Action(nameof(ResetPassword), "Account", new { area = "", email = user.Email, token }, Request.Scheme)!;
            var E = EmailSender.Encode;
            var siteUrl = $"{Request.Scheme}://{Request.Host}";
            var body = EmailSender.Layout(siteUrl, "Reset your Ghars Platform password | إعادة تعيين كلمة المرور",
                EmailSender.Section(false, "Reset your password",
                    $"<p style=\"margin:0 0 10px\">We received a request to reset the password for <b dir=\"ltr\">{E(user.Email)}</b>.</p>" +
                    "<p style=\"margin:0\">Click the button below to choose a new password. The link works once and expires in 24 hours.</p>",
                    link, "Choose a new password") +
                "<p style=\"font-family:'Segoe UI',Tahoma,Arial,sans-serif;font-size:13px;line-height:1.6;color:#6b7280;margin:14px 0 0\">Didn't ask for this? You can safely ignore this email; your password has not changed.</p>",
                EmailSender.Section(true, "إعادة تعيين كلمة المرور",
                    $"<p style=\"margin:0 0 10px\">تلقينا طلباً لإعادة تعيين كلمة المرور للحساب <b dir=\"ltr\">{E(user.Email)}</b>.</p>" +
                    "<p style=\"margin:0\">اضغط على الزر أدناه لاختيار كلمة مرور جديدة. يعمل الرابط مرة واحدة وتنتهي صلاحيته خلال 24 ساعة.</p>",
                    link, "اختر كلمة مرور جديدة") +
                "<p dir=\"rtl\" style=\"text-align:right;font-family:'Segoe UI',Tahoma,Arial,sans-serif;font-size:13px;line-height:1.6;color:#6b7280;margin:14px 0 0\">لم تطلب ذلك؟ يمكنك تجاهل هذه الرسالة بأمان؛ لم تتغير كلمة المرور.</p>");
            try
            {
                await email.SendAsync(user.Email, "Ghars Platform - reset your password | منصة غرس - إعادة تعيين كلمة المرور", body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Password reset email failed for user {UserId}", user.Id);
            }
        }
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    public IActionResult ForgotPasswordConfirmation() => View();

    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(ForgotPassword));
        return View(new ResetPasswordVm { Email = email, Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await _userManager.FindByEmailAsync(vm.Email);
        if (user != null && !IsDeactivated(user))
        {
            var result = await _userManager.ResetPasswordAsync(user, vm.Token, vm.Password);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                    ModelState.AddModelError("", e.Code == "InvalidToken"
                        ? "This link is invalid or has expired. Request a new one."
                        : e.Description);
                return View(vm);
            }
            // A successful reset also clears a failed-login lockout.
            await _userManager.ResetAccessFailedCountAsync(user);
            if (user.LockoutEnd.HasValue)
                await _userManager.SetLockoutEndDateAsync(user, null);
        }
        TempData["ToastSuccess"] = "Your password has been changed. Please sign in.";
        return RedirectToAction(nameof(Login));
    }

    // Admin -> Users "Deactivate" locks an account out for 100 years; a failed-login lockout lasts minutes.
    // Only the first means the account is switched off, and a password reset must not switch it back on.
    private static bool IsDeactivated(ApplicationUser user) =>
        user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow.AddYears(1);

    public class ForgotPasswordVm
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }

    public class ResetPasswordVm
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Token { get; set; } = "";

        [Required, DataType(DataType.Password), MinLength(10)]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = "";
    }

    public class LoginVm
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    public class RegisterVm
    {
        [Required, MaxLength(200)]
        public string FullName { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string PreferredLanguage { get; set; } = "en";

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = "";
    }
}
