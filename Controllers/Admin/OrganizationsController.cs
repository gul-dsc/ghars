using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class OrganizationsController : Controllers.BaseController
{
    public OrganizationsController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index(string? status = null)
    {
        IQueryable<Organization> q = Db.Organizations;

        if (Enum.TryParse<ApprovalStatus>(status ?? "", out var st))
            q = q.Where(x => x.Status == st);

        var list = await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
        return View(list);
    }


    [Authorize(Roles = RoleNames.SuperAdmin)]
    public IActionResult Create()
    {
        return View(new OrganizationVm { Status = ApprovalStatus.Approved, OrganizationType = OrganizationType.Club });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Create(OrganizationVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var org = new Organization
        {
            OrganizationType = vm.OrganizationType,
            NameEn = vm.NameEn.Trim(),
            NameAr = vm.NameAr.Trim(),
            Email = vm.Email.Trim(),
            Phone = vm.Phone.Trim(),
            AddressEn = vm.AddressEn?.Trim(),
            AddressAr = vm.AddressAr?.Trim(),
            WebsiteUrl = vm.WebsiteUrl?.Trim(),
            LogoPath = vm.LogoPath?.Trim(),
            Status = vm.Status,
            ApprovedAtUtc = vm.Status == ApprovalStatus.Approved ? DateTime.UtcNow : null,
            ApprovedByUserId = vm.Status == ApprovalStatus.Approved ? CurrentUserId : null,
            Notes = vm.Notes?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        Db.Organizations.Add(org);
        await Db.SaveChangesAsync();
        if (org.OrganizationType == OrganizationType.GovernmentAuthority || org.OrganizationType == OrganizationType.OtherPartner || org.OrganizationType == OrganizationType.Partner)
        {
            if (!await Db.PartnerProfiles.AnyAsync(x => x.OrganizationId == org.Id))
            {
                Db.PartnerProfiles.Add(new PartnerProfile { OrganizationId = org.Id, OverviewEn = vm.Notes ?? org.NameEn, OverviewAr = vm.Notes ?? org.NameAr, IsFeatured = true, CreatedAtUtc = DateTime.UtcNow });
                await Db.SaveChangesAsync();
            }
        }
        await AuditAsync("Create", nameof(Organization), org.Id.ToString(), null, org);
        TempData["ToastSuccess"] = "Organization created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(int id)
    {
        var org = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == id);
        if (org is null) return NotFound();
        return View(new OrganizationVm
        {
            Id = org.Id,
            OrganizationType = org.OrganizationType,
            NameEn = org.NameEn,
            NameAr = org.NameAr,
            Email = org.Email,
            Phone = org.Phone,
            AddressEn = org.AddressEn,
            AddressAr = org.AddressAr,
            WebsiteUrl = org.WebsiteUrl,
            LogoPath = org.LogoPath,
            Status = org.Status,
            Notes = org.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Edit(OrganizationVm vm)
    {
        var org = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == vm.Id);
        if (org is null) return NotFound();
        if (!ModelState.IsValid) return View(vm);
        var old = new { org.OrganizationType, org.NameEn, org.NameAr, org.Email, org.Phone, org.Status };
        org.OrganizationType = vm.OrganizationType;
        org.NameEn = vm.NameEn.Trim();
        org.NameAr = vm.NameAr.Trim();
        org.Email = vm.Email.Trim();
        org.Phone = vm.Phone.Trim();
        org.AddressEn = vm.AddressEn?.Trim();
        org.AddressAr = vm.AddressAr?.Trim();
        org.WebsiteUrl = vm.WebsiteUrl?.Trim();
        org.LogoPath = vm.LogoPath?.Trim();
        org.Status = vm.Status;
        org.Notes = vm.Notes?.Trim();
        org.UpdatedAtUtc = DateTime.UtcNow;
        org.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync("Update", nameof(Organization), org.Id.ToString(), old, org);
        TempData["ToastSuccess"] = "Organization updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        var org = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == id);
        if (org is null) return NotFound();
        org.IsDeleted = true;
        org.UpdatedAtUtc = DateTime.UtcNow;
        org.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync("Delete", nameof(Organization), id.ToString(), org, null);
        TempData["ToastWarning"] = "Organization deleted.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var org = await Db.Organizations
            .Include(x => x.Contacts)
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (org is null) return NotFound();
        return View(org);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Approve(int id)
    {
        var org = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == id);
        if (org is null) return NotFound();

        var old = new { org.Status, org.ApprovedAtUtc, org.ApprovedByUserId };

        org.Status = ApprovalStatus.Approved;
        org.ApprovedAtUtc = DateTime.UtcNow;
        org.ApprovedByUserId = CurrentUserId;

        await Db.SaveChangesAsync();
        await AuditAsync("Approve", nameof(Organization), id.ToString(), old, new { org.Status, org.ApprovedAtUtc, org.ApprovedByUserId });

        TempData["ToastSuccess"] = "Organization approved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var org = await Db.Organizations.FirstOrDefaultAsync(x => x.Id == id);
        if (org is null) return NotFound();

        var old = new { org.Status, org.Notes };

        org.Status = ApprovalStatus.Rejected;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var prefix = $"Rejected: {reason}";
            org.Notes = string.IsNullOrWhiteSpace(org.Notes) ? prefix : prefix + Environment.NewLine + org.Notes;
        }
        org.UpdatedAtUtc = DateTime.UtcNow;
        org.UpdatedByUserId = CurrentUserId;

        await Db.SaveChangesAsync();
        await AuditAsync("Reject", nameof(Organization), id.ToString(), old, new { org.Status, org.Notes });

        TempData["ToastWarning"] = "Organization rejected.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public class OrganizationVm
    {
        public int Id { get; set; }
        [Required] public OrganizationType OrganizationType { get; set; } = OrganizationType.Club;
        [Required, MaxLength(250)] public string NameEn { get; set; } = "";
        [Required, MaxLength(250)] public string NameAr { get; set; } = "";
        [Required, EmailAddress, MaxLength(250)] public string Email { get; set; } = "";
        [Required, MaxLength(50)] public string Phone { get; set; } = "";
        [MaxLength(500)] public string? AddressEn { get; set; }
        [MaxLength(500)] public string? AddressAr { get; set; }
        [MaxLength(300)] public string? WebsiteUrl { get; set; }
        [MaxLength(400)] public string? LogoPath { get; set; }
        [Required] public ApprovalStatus Status { get; set; } = ApprovalStatus.Approved;
        [MaxLength(2000)] public string? Notes { get; set; }
    }
}
