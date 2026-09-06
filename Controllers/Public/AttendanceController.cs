using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

[Authorize]
public class AttendanceController : Controller
{
    private readonly AppDbContext _db;

    public AttendanceController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/attendance/scan")]
    public IActionResult Scan(string token)
    {
        return View(new AttendanceScanVm { Token = token });
    }

    [HttpPost("/attendance/scan")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan(AttendanceScanVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var session = await _db.AttendanceSessions
            .Include(x => x.Activity)
            .FirstOrDefaultAsync(x => x.QrToken == vm.Token);

        if (session is null)
        {
            ModelState.AddModelError("", "Invalid QR token.");
            return View(vm);
        }

        if (session.SessionEndUtc != null)
        {
            ModelState.AddModelError("", "This session is closed.");
            return View(vm);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();

        var exists = await _db.AttendanceRecords
            .AnyAsync(x => x.AttendanceSessionId == session.Id && x.UserId == userId);

        if (exists)
        {
            TempData["ToastInfo"] = "You are already checked in.";
            return RedirectToAction("Index", "Home", new { area = "" });
        }

        // Attempt to infer organization from OrganizationAdminLink
        var orgId = await _db.OrganizationAdminLinks
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.OrganizationId)
            .FirstOrDefaultAsync();

        _db.AttendanceRecords.Add(new AttendanceRecord
        {
            AttendanceSessionId = session.Id,
            UserId = userId,
            OrganizationId = orgId,
            CheckInUtc = DateTime.UtcNow,
            Method = AttendanceMethod.Qr
        });

        await _db.SaveChangesAsync();

        // Award points (attendance)
        await EnsureWalletAsync(userId);
        await AddPointsAsync(userId, 10, "Attendance points", "نقاط الحضور", PointsReferenceType.Attendance, session.Id);

        TempData["ToastSuccess"] = "Check-in successful. Thank you!";
        return RedirectToAction("Index", "Home", new { area = "" });
    }

    private async Task EnsureWalletAsync(string userId)
    {
        var wallet = await _db.UserPointsWallets.FirstOrDefaultAsync(x => x.UserId == userId);
        if (wallet is null)
        {
            _db.UserPointsWallets.Add(new UserPointsWallet
            {
                UserId = userId,
                Balance = 0,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
    }

    private async Task AddPointsAsync(string userId, int points, string reasonEn, string reasonAr, PointsReferenceType refType, int refId)
    {
        var wallet = await _db.UserPointsWallets.FirstAsync(x => x.UserId == userId);
        wallet.Balance += points;
        wallet.UpdatedAtUtc = DateTime.UtcNow;

        _db.PointsTransactions.Add(new PointsTransaction
        {
            UserId = userId,
            Type = PointsTransactionType.Earn,
            Points = points,
            ReasonEn = reasonEn,
            ReasonAr = reasonAr,
            ReferenceType = refType,
            ReferenceId = refId,
            AtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}
