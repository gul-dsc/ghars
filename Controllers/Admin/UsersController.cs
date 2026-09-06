using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
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

    public UsersController(AppDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
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
