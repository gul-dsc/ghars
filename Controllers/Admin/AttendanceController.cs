using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class AttendanceController : Controllers.BaseController
{
    private static bool IsAr() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public AttendanceController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Sessions()
    {
        var list = await Db.AttendanceSessions
            .Include(x => x.Activity)
            .OrderByDescending(x => x.SessionStartUtc)
            .ToListAsync();
        return View(list);
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> CreateSession(int? activityId = null)
    {
        ViewBag.Activities = await Db.Activities
            .Where(x => x.Status == ActivityStatus.Published)
            .OrderByDescending(x => x.StartDateTime)
            .ToListAsync();

        var vm = new CreateSessionVm { ActivityId = activityId ?? 0 };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> CreateSession(CreateSessionVm vm)
    {
        ViewBag.Activities = await Db.Activities
            .Where(x => x.Status == ActivityStatus.Published)
            .OrderByDescending(x => x.StartDateTime)
            .ToListAsync();

        if (!ModelState.IsValid) return View(vm);

        var session = new AttendanceSession
        {
            ActivityId = vm.ActivityId,
            SessionStartUtc = DateTime.UtcNow,
            Code = vm.Code?.Trim(),
            QrToken = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        Db.AttendanceSessions.Add(session);
        await Db.SaveChangesAsync();
        await AuditAsync("CreateSession", nameof(AttendanceSession), session.Id.ToString(), null, session);

        TempData["ToastSuccess"] = IsAr() ? "تم إنشاء جلسة الحضور." : "Attendance session created.";
        return RedirectToAction(nameof(SessionDetails), new { id = session.Id });
    }

    public async Task<IActionResult> SessionDetails(int id)
    {
        var s = await Db.AttendanceSessions
            .Include(x => x.Activity)
            .Include(x => x.AttendanceRecords)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (s is null) return NotFound();

        var scanUrl = Url.Action("Scan", "Attendance", new { area = "", token = s.QrToken }, Request.Scheme);
        ViewBag.ScanUrl = scanUrl;
        // Recipient name and email for display; certificates and attendance store only the user id.
        var recipientIds = s.AttendanceRecords.Select(x => x.UserId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        ViewBag.Recipients = await Db.Users.Where(u => recipientIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new[] { u.FullName ?? "", u.Email ?? "" });

        return View(s);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> AddRecord(int id, string email)
    {
        var session = await Db.AttendanceSessions.FirstOrDefaultAsync(x => x.Id == id);
        if (session is null) return NotFound();
        var user = await Db.Users.FirstOrDefaultAsync(x => x.Email == email || x.UserName == email);
        if (user is null)
        {
            TempData["ToastWarning"] = IsAr() ? "لا يوجد مستخدم بهذا البريد الإلكتروني." : "No user with that email address was found.";
            return RedirectToAction(nameof(SessionDetails), new { id });
        }
        if (await Db.AttendanceRecords.AnyAsync(x => x.AttendanceSessionId == id && x.UserId == user.Id))
        {
            TempData["ToastInfo"] = IsAr() ? "حضور هذا المستخدم مسجّل مسبقاً في هذه الجلسة." : "This user's attendance is already recorded for this session.";
            return RedirectToAction(nameof(SessionDetails), new { id });
        }
        Db.AttendanceRecords.Add(new AttendanceRecord
        {
            AttendanceSessionId = id,
            UserId = user.Id,
            OrganizationId = user.PrimaryOrganizationId,
            CheckInUtc = DateTime.UtcNow,
            Method = AttendanceMethod.Manual
        });
        await Db.SaveChangesAsync();
        TempData["ToastSuccess"] = IsAr() ? "تم تسجيل الحضور." : "Attendance recorded.";
        return RedirectToAction(nameof(SessionDetails), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> CloseSession(int id)
    {
        var s = await Db.AttendanceSessions.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        if (s.SessionEndUtc is null)
        {
            s.SessionEndUtc = DateTime.UtcNow;
            s.UpdatedAtUtc = DateTime.UtcNow;
            s.UpdatedByUserId = CurrentUserId;
            await Db.SaveChangesAsync();
            await AuditAsync("CloseSession", nameof(AttendanceSession), id.ToString(), null, new { s.SessionEndUtc });
            TempData["ToastSuccess"] = IsAr() ? "تم إغلاق جلسة الحضور." : "Attendance session closed.";
        }

        return RedirectToAction(nameof(SessionDetails), new { id });
    }

    public class CreateSessionVm
    {
        public int ActivityId { get; set; }

        public string? Code { get; set; }
    }
}
