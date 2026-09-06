using System.Security.Claims;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Public;

public class OrganizationController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public OrganizationController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.DscAdmin)]
    [HttpGet("/Organization/Create")]
    public IActionResult Create()
    {
        return View("Register", new OrganizationRegistrationVm());
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.DscAdmin)]
    [HttpPost("/Organization/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrganizationRegistrationVm vm)
    {
        return await SaveOrganizationAsync(vm);
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.DscAdmin)]
    [HttpGet("/org/register")]
    public IActionResult Register()
    {
        return View(new OrganizationRegistrationVm());
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.DscAdmin)]
    [HttpPost("/org/register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(OrganizationRegistrationVm vm)
    {
        return await SaveOrganizationAsync(vm);
    }

    [Authorize]
    [HttpGet("/Organization/MyOrganizations")]
    public async Task<IActionResult> MyOrganizations()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var organizations = await _db.OrganizationAdminLinks
            .Where(x => x.UserId == userId)
            .Include(x => x.Organization)
            .Select(x => x.Organization!)
            .OrderBy(x => x.NameEn)
            .ToListAsync();

        return View(organizations);
    }

    [HttpGet("/org/register/success")]
    public IActionResult RegisterSuccess() => View();

    private async Task<IActionResult> SaveOrganizationAsync(OrganizationRegistrationVm vm)
    {
        if (!ModelState.IsValid) return View("Register", vm);

        var exists = await _db.Organizations.IgnoreQueryFilters()
            .AnyAsync(x => x.Email == vm.Email && !x.IsDeleted);
        if (exists)
        {
            ModelState.AddModelError(nameof(vm.Email), "An organization with this email already exists.");
            return View("Register", vm);
        }

        var org = new Organization
        {
            OrganizationType = vm.OrganizationType,
            NameEn = vm.NameEn.Trim(),
            NameAr = vm.NameAr.Trim(),
            TradeLicenseNo = vm.TradeLicenseNo?.Trim(),
            Email = vm.Email.Trim(),
            Phone = vm.Phone.Trim(),
            AddressEn = vm.AddressEn?.Trim(),
            AddressAr = vm.AddressAr?.Trim(),
            City = vm.City?.Trim(),
            WebsiteUrl = vm.WebsiteUrl?.Trim(),
            Notes = vm.Notes?.Trim(),
            Status = ApprovalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };

        // The logo is PUBLIC (shown on public organization listings) and stays under wwwroot.
        // Licence and supporting documents below are PROTECTED and never touch the web root.
        var logoRoot = Path.Combine(_env.WebRootPath, "uploads", "org");
        Directory.CreateDirectory(logoRoot);

        if (vm.LogoFile is not null && vm.LogoFile.Length > 0)
            org.LogoPath = await SaveFileAsync(vm.LogoFile, logoRoot);

        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        _db.OrganizationContacts.Add(new OrganizationContact
        {
            OrganizationId = org.Id,
            FullName = vm.ContactFullName.Trim(),
            PositionTitleEn = vm.ContactPositionEn?.Trim(),
            PositionTitleAr = vm.ContactPositionAr?.Trim(),
            Email = vm.ContactEmail.Trim(),
            Phone = vm.ContactPhone.Trim(),
            IsPrimary = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        });

        if (vm.LicenseFile is not null && vm.LicenseFile.Length > 0)
        {
            _db.OrganizationDocuments.Add(new OrganizationDocument
            {
                OrganizationId = org.Id,
                DocumentType = OrganizationDocumentType.License,
                FilePath = await ProtectedFileStore.SaveAsync(vm.LicenseFile, _env, ProtectedFileStore.OrganizationDocuments),
                OriginalFileName = vm.LicenseFile.FileName,
                UploadedAtUtc = DateTime.UtcNow,
                UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            });
        }

        if (vm.SupportingFile is not null && vm.SupportingFile.Length > 0)
        {
            _db.OrganizationDocuments.Add(new OrganizationDocument
            {
                OrganizationId = org.Id,
                DocumentType = OrganizationDocumentType.Other,
                FilePath = await ProtectedFileStore.SaveAsync(vm.SupportingFile, _env, ProtectedFileStore.OrganizationDocuments),
                OriginalFileName = vm.SupportingFile.FileName,
                UploadedAtUtc = DateTime.UtcNow,
                UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            });
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!_db.OrganizationAdminLinks.Any(x => x.OrganizationId == org.Id && x.UserId == userId))
            {
                _db.OrganizationAdminLinks.Add(new OrganizationAdminLink
                {
                    OrganizationId = org.Id,
                    UserId = userId,
                    RoleHint = vm.OrganizationType switch
                    {
                        OrganizationType.Club => OrganizationType.Club,
                        OrganizationType.PrivateAcademy => OrganizationType.PrivateAcademy,
                        _ => OrganizationType.OtherPartner
                    },
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = userId
                });
            }

            var roleName = vm.OrganizationType switch
            {
                OrganizationType.Club => RoleNames.ClubAdmin,
                OrganizationType.PrivateAcademy => RoleNames.AcademyAdmin,
                _ => RoleNames.PartnerAdmin
            };

            // role assignment is optional here; if user manager is not injected, admin can assign later
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(RegisterSuccess));
    }

    private static async Task<string> SaveFileAsync(IFormFile file, string rootFolder)
    {
        var ext = Path.GetExtension(file.FileName);
        var safeName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(rootFolder, safeName);

        await using var fs = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fs);

        return "/uploads/org/" + safeName;
    }
}
