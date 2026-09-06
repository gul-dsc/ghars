using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class CertificatesController : Controllers.BaseController
{
    private readonly IWebHostEnvironment _env;

    public CertificatesController(AppDbContext db, IWebHostEnvironment env) : base(db)
    {
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var list = await Db.Certificates
            .Include(x => x.Activity)
            .OrderByDescending(x => x.IssuedAtUtc)
            .Take(300)
            .ToListAsync();
        return View(list);
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Issue()
    {
        ViewBag.Sessions = await Db.AttendanceSessions
            .Include(x => x.Activity)
            .OrderByDescending(x => x.SessionStartUtc)
            .Take(100)
            .ToListAsync();
        return View(new IssueVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Issue(IssueVm vm)
    {
        ViewBag.Sessions = await Db.AttendanceSessions
            .Include(x => x.Activity)
            .OrderByDescending(x => x.SessionStartUtc)
            .Take(100)
            .ToListAsync();

        if (!ModelState.IsValid) return View(vm);

        var session = await Db.AttendanceSessions
            .Include(x => x.Activity)
            .FirstOrDefaultAsync(x => x.Id == vm.AttendanceSessionId);

        if (session is null) return NotFound();

        var attendees = await Db.AttendanceRecords
            .Where(x => x.AttendanceSessionId == session.Id)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync();

        if (attendees.Count == 0)
        {
            TempData["ToastWarning"] = "No attendees found for this session.";
            return RedirectToAction(nameof(Issue));
        }

        var issuedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var uploadRoot = Path.Combine(_env.WebRootPath, "uploads", "certificates");
        Directory.CreateDirectory(uploadRoot);

        var already = await Db.Certificates
            .Where(x => x.ActivityId == session.ActivityId && attendees.Contains(x.UserId) && x.Status == CertificateStatus.Issued)
            .Select(x => x.UserId)
            .ToListAsync();

        var toIssue = attendees.Except(already).ToList();

        int count = 0;

        foreach (var userId in toIssue)
        {
            var certNo = $"GHR-{DateTime.UtcNow:yyyyMMdd}-{session.ActivityId:D4}-{Guid.NewGuid():N}".Substring(0, 30);
            var verifyToken = Guid.NewGuid().ToString("N");
            var verifyUrl = Url.Action("Certificate", "Verify", new { area = "", token = verifyToken }, Request.Scheme) ?? "";

            // Participant name
            var user = await Db.Users.FirstOrDefaultAsync(x => x.Id == userId);
            var participantName = user?.FullName ?? user?.Email ?? "Participant";

            var isRtl = string.Equals(vm.Language, "ar", StringComparison.OrdinalIgnoreCase);
            var activityTitle = isRtl ? (session.Activity?.TitleAr ?? "") : (session.Activity?.TitleEn ?? "");

            var qrBytes = QrCodeHelper.GeneratePng(verifyUrl, 10);

            var pdfBytes = CertificatePdfBuilder.Build(new CertificatePdfBuilder.CertificateRenderData(
                CertificateNo: certNo,
                ParticipantName: participantName,
                ActivityTitle: activityTitle,
                IssuedDateText: DateTime.UtcNow.ToString("yyyy-MM-dd"),
                VerifyUrl: verifyUrl,
                QrPngBytes: qrBytes,
                IsRtl: isRtl
            ));

            var pdfFile = $"{verifyToken}.pdf";
            var pdfPathFull = Path.Combine(uploadRoot, pdfFile);
            await System.IO.File.WriteAllBytesAsync(pdfPathFull, pdfBytes);

            var cert = new Certificate
            {
                ActivityId = session.ActivityId,
                UserId = userId,
                IssuedByUserId = issuedBy,
                IssuedAtUtc = DateTime.UtcNow,
                CertificateNo = certNo,
                VerifyToken = verifyToken,
                PdfPath = "/uploads/certificates/" + pdfFile,
                Status = CertificateStatus.Issued
            };

            Db.Certificates.Add(cert);
            count++;
        }

        await Db.SaveChangesAsync();
        await AuditAsync("IssueCertificates", nameof(Certificate), session.ActivityId.ToString(), null, new { SessionId = session.Id, Issued = count });

        TempData["ToastSuccess"] = $"Issued {count} certificates. Skipped {already.Count} existing.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Download(int id)
    {
        var cert = await Db.Certificates.FirstOrDefaultAsync(x => x.Id == id);
        if (cert is null) return NotFound();
        if (string.IsNullOrWhiteSpace(cert.PdfPath)) return NotFound();

        var full = Path.Combine(_env.WebRootPath, cert.PdfPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!System.IO.File.Exists(full)) return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(full);
        return File(bytes, "application/pdf", $"{cert.CertificateNo}.pdf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Revoke(int id, string? reason)
    {
        var cert = await Db.Certificates.FirstOrDefaultAsync(x => x.Id == id);
        if (cert is null) return NotFound();

        if (cert.Status == CertificateStatus.Revoked)
        {
            TempData["ToastInfo"] = "Already revoked.";
            return RedirectToAction(nameof(Index));
        }

        var old = new { cert.Status, cert.RevokedAtUtc, cert.RevokedByUserId, cert.RevokeReason };

        cert.Status = CertificateStatus.Revoked;
        cert.RevokedAtUtc = DateTime.UtcNow;
        cert.RevokedByUserId = CurrentUserId;
        cert.RevokeReason = reason;

        await Db.SaveChangesAsync();
        await AuditAsync("Revoke", nameof(Certificate), id.ToString(), old, cert);

        TempData["ToastWarning"] = "Certificate revoked.";
        return RedirectToAction(nameof(Index));
    }

    public class IssueVm
    {
        public int AttendanceSessionId { get; set; }

        public string Language { get; set; } = "en";
    }
}
