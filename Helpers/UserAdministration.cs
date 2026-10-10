using System.Globalization;
using GharsPlatform.Data;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// The safety rules behind Admin &gt; Users: who may lose Super Admin access, and how an account is
/// removed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Super Admin continuity.</b> An active Super Admin (holds the role, not deactivated) may not be
/// demoted, deactivated or deleted when they are the last one; and nobody may do any of those
/// three to their own account — another Super Admin has to. Other roles, and other Super Admins
/// while at least one remains, are managed exactly as before.
/// </para>
/// <para>
/// <b>Concurrency.</b> Two administrators acting at once could each see "one other Super Admin
/// remains" and each remove the other. Every change that can reduce the roster therefore takes an
/// exclusive SQL Server application lock inside its transaction before counting, so the second
/// change counts after the first has committed.
/// </para>
/// <para>
/// <b>Deletion.</b> <see cref="Models.Core.OrganizationAdminLink"/> has no foreign key to the user,
/// so it is removed explicitly in the same transaction as the account: both go, or neither does.
/// Everything else that names a user (bookings, certificates, attendance, audit logs, notification
/// deliveries) stores the id as plain text with no foreign key, so it is kept as history.
/// </para>
/// </remarks>
public static class UserAdministration
{
    private const string RosterLock = "ghars-super-admin-roster";

    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
    private static string T(string en, string ar) => IsAr ? ar : en;

    /// <summary>The platform's deactivation convention: a lockout more than a year away.</summary>
    public static bool IsDeactivated(ApplicationUser user)
        => user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow.AddYears(1);

    /// <summary>
    /// Takes the roster lock for the current transaction. Call after BeginTransaction and before
    /// <see cref="CheckSuperAdminContinuityAsync"/>; the lock is released when the transaction ends.
    /// </summary>
    public static async Task LockSuperAdminRosterAsync(AppDbContext db)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The Super Admin roster lock must be taken inside a transaction.");

        await db.Database.ExecuteSqlRawAsync(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; " +
            "IF @r < 0 THROW 51000, 'Could not obtain the Super Admin roster lock.', 1;", RosterLock);
    }

    /// <summary>Active Super Admins other than <paramref name="exceptUserId"/>, read from the database.</summary>
    public static async Task<int> OtherActiveSuperAdminsAsync(AppDbContext db, string exceptUserId)
    {
        var roleId = await db.Roles.Where(r => r.Name == RoleNames.SuperAdmin).Select(r => r.Id).FirstOrDefaultAsync();
        if (roleId is null) return 0;
        return await NotificationDispatcher.ActiveUsers(db)
            .Where(u => u.Id != exceptUserId && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId))
            .CountAsync();
    }

    /// <summary>
    /// Whether a change to <paramref name="target"/> may go ahead. Returns null when it may, or the
    /// bilingual reason it may not.
    /// </summary>
    /// <param name="keepsSuperAdmin">The target still holds Super Admin after the change.</param>
    /// <param name="staysActive">The target is still active (and still exists) after the change.</param>
    public static async Task<string?> CheckSuperAdminContinuityAsync(
        AppDbContext db, UserManager<ApplicationUser> userManager, string? actingUserId,
        ApplicationUser target, bool keepsSuperAdmin, bool staysActive)
    {
        var isActiveSuperAdmin = !IsDeactivated(target) && await userManager.IsInRoleAsync(target, RoleNames.SuperAdmin);
        if (!isActiveSuperAdmin || (keepsSuperAdmin && staysActive)) return null;

        if (target.Id == actingUserId)
            return T("You can't remove your own Super Admin access, deactivate or delete your own account. Ask another Super Admin to make this change.",
                     "لا يمكنكم إزالة صلاحية المشرف العام من حسابكم أو تعطيل حسابكم أو حذفه. اطلبوا ذلك من مشرف عام آخر.");

        if (await OtherActiveSuperAdminsAsync(db, target.Id) == 0)
            return T("This is the last active Super Admin. Assign the Super Admin role to another active account first.",
                     "هذا هو المشرف العام النشط الوحيد. امنحوا دور المشرف العام لحساب نشط آخر أولاً.");

        return null;
    }

    /// <summary>
    /// Removes the account and its organization links inside the caller's transaction. The caller
    /// commits on success; on failure it rolls back and nothing has changed.
    /// </summary>
    public static async Task<IdentityResult> DeleteUserWithLinksAsync(AppDbContext db, UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Delete a user inside a transaction so the account and its links go together.");

        db.OrganizationAdminLinks.RemoveRange(await db.OrganizationAdminLinks.Where(x => x.UserId == user.Id).ToListAsync());
        await db.SaveChangesAsync();
        return await userManager.DeleteAsync(user);
    }
}
