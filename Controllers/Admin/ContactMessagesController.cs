using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

/// <summary>
/// The queue for enquiries submitted through the public Contact page. Because this platform sends no
/// email, this screen is the only place an enquiry is ever read — the notification only links here.
/// </summary>
[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class ContactMessagesController : Controllers.BaseController
{
    public ContactMessagesController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index(string? status = null)
    {
        IQueryable<ContactMessage> q = Db.ContactMessages;

        if (Enum.TryParse<ContactMessageStatus>(status ?? "", out var st))
            q = q.Where(x => x.Status == st);

        ViewBag.Status = status;
        ViewBag.NewCount = await Db.ContactMessages.CountAsync(x => x.Status == ContactMessageStatus.New);

        var list = await q.OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Details(int id)
    {
        var message = await Db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id);
        if (message is null) return NotFound();
        return View(message);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, ContactMessageStatus status, string? adminNotes)
    {
        var message = await Db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id);
        if (message is null) return NotFound();

        var before = new { message.Status, message.AdminNotes };

        message.Status = status;
        message.AdminNotes = string.IsNullOrWhiteSpace(adminNotes) ? null : adminNotes.Trim();
        message.UpdatedAtUtc = DateTime.UtcNow;
        message.UpdatedByUserId = CurrentUserId;

        // Stamp who closed it, and clear the stamp if it is reopened, so "handled by" never outlives
        // the handled state.
        if (status is ContactMessageStatus.Resolved or ContactMessageStatus.Spam)
        {
            message.HandledAtUtc = DateTime.UtcNow;
            message.HandledByUserId = CurrentUserId;
        }
        else
        {
            message.HandledAtUtc = null;
            message.HandledByUserId = null;
        }

        await Db.SaveChangesAsync();
        await AuditAsync("ContactMessage.SetStatus", nameof(ContactMessage), id.ToString(),
            before, new { message.Status, message.AdminNotes });

        TempData["ToastSuccess"] = "Message updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> Delete(int id)
    {
        var message = await Db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id);
        if (message is null) return NotFound();

        // ContactMessage is not soft-deletable: it holds a member of the public's personal data with
        // no ongoing business purpose once it is dealt with, so deletion here is a real deletion.
        await AuditAsync("ContactMessage.Delete", nameof(ContactMessage), id.ToString(),
            new { message.FullName, message.Email, message.Subject, message.Status }, null);

        Db.ContactMessages.Remove(message);
        await Db.SaveChangesAsync();

        TempData["ToastSuccess"] = "Message deleted.";
        return RedirectToAction(nameof(Index));
    }
}
