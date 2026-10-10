using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GharsPlatform.Tests.Infrastructure;

/// <summary>
/// Creates the records a test needs, directly in the test database. Every name carries a fresh
/// suffix so tests sharing one database never collide or depend on each other's rows.
/// </summary>
public sealed class TestData
{
    private readonly GharsAppFactory _factory;

    public TestData(GharsAppFactory factory) => _factory = factory;

    public static string Unique() => Guid.NewGuid().ToString("N")[..10];

    public async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task Db(Func<AppDbContext, Task> work)
    {
        using var scope = _factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<string> UserAsync(string role, bool deactivated = false, params int[] organizationIds)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"t-{Unique()}@tests.ghars.local";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = $"Test Person {Unique()}",
            EmailConfirmed = true,
            PreferredLanguage = "en",
            LockoutEnd = deactivated ? DateTimeOffset.UtcNow.AddYears(100) : null
        };
        Check(await users.CreateAsync(user));
        Check(await users.AddToRoleAsync(user, role));

        foreach (var orgId in organizationIds)
        {
            var type = await db.Organizations.Where(o => o.Id == orgId).Select(o => o.OrganizationType).FirstAsync();
            db.OrganizationAdminLinks.Add(new OrganizationAdminLink { OrganizationId = orgId, UserId = user.Id, RoleHint = type, CreatedAtUtc = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        return user.Id;
    }

    public Task LinkAsync(string userId, int organizationId) => Db(async db =>
    {
        var type = await db.Organizations.Where(o => o.Id == organizationId).Select(o => o.OrganizationType).FirstAsync();
        db.OrganizationAdminLinks.Add(new OrganizationAdminLink { OrganizationId = organizationId, UserId = userId, RoleHint = type, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    });

    public Task UnlinkAsync(string userId, int organizationId) => Db(async db =>
    {
        db.OrganizationAdminLinks.RemoveRange(await db.OrganizationAdminLinks.Where(l => l.UserId == userId && l.OrganizationId == organizationId).ToListAsync());
        await db.SaveChangesAsync();
    });

    public Task<int> OrganizationAsync(OrganizationType type) => Db(async db =>
    {
        var o = new Organization
        {
            OrganizationType = type,
            NameEn = $"Test Org {Unique()}",
            NameAr = $"جهة اختبار {Unique()}",
            Email = $"org-{Unique()}@tests.ghars.local",
            Phone = "000",
            Status = ApprovalStatus.Approved
        };
        db.Organizations.Add(o);
        await db.SaveChangesAsync();
        return o.Id;
    });

    public Task<int> SeasonIdAsync() => Db(db => db.Seasons.Where(s => s.IsActive).Select(s => s.Id).FirstAsync());

    public async Task<int> ActivityAsync(int? partnerOrganizationId, string createdByUserId)
    {
        var seasonId = await SeasonIdAsync();
        return await Db(async db =>
        {
            var a = new Activity
            {
                SeasonId = seasonId,
                PartnerOrganizationId = partnerOrganizationId,
                TitleEn = $"Test Program {Unique()}",
                TitleAr = $"برنامج اختبار {Unique()}",
                StartDateTime = DateTime.UtcNow.AddDays(10),
                EndDateTime = DateTime.UtcNow.AddDays(10).AddHours(2),
                LocationEn = "Test", LocationAr = "اختبار",
                Status = ActivityStatus.Published,
                CreatedByUserId = createdByUserId
            };
            db.Activities.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
    }

    public Task<int> BookingAsync(int clubOrganizationId, int? partnerOrganizationId, int? activityId, BookingStatus status, string requestedByUserId) => Db(async db =>
    {
        var b = new BookingRequest
        {
            OrganizationId = clubOrganizationId,
            PartnerOrganizationId = partnerOrganizationId,
            ActivityId = activityId,
            Subject = activityId is null ? $"Custom request {Unique()}" : null,
            RequestedByUserId = requestedByUserId,
            Status = status,
            ProposedStartDateTime = DateTime.UtcNow.AddDays(20),
            ProposedEndDateTime = DateTime.UtcNow.AddDays(20).AddHours(2),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = requestedByUserId
        };
        db.BookingRequests.Add(b);
        await db.SaveChangesAsync();
        return b.Id;
    });

    public Task<int> ProposedOptionAsync(int bookingId, string byUserId) => Db(async db =>
    {
        var o = new BookingProposedTimeOption
        {
            BookingRequestId = bookingId,
            ProposedStartUtc = DateTime.UtcNow.AddDays(30),
            ProposedEndUtc = DateTime.UtcNow.AddDays(30).AddHours(2),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = byUserId,
            IsActive = true
        };
        db.BookingProposedTimeOptions.Add(o);
        await db.SaveChangesAsync();
        return o.Id;
    });

    public Task<BookingRequest> BookingRowAsync(int id) => Db(db => db.BookingRequests.AsNoTracking().FirstAsync(x => x.Id == id));

    public Task<int> AuditCountAsync(int bookingId, string action) => Db(db => db.BookingAuditTrails.CountAsync(x => x.BookingRequestId == bookingId && x.Action == action));

    public static string Reference(BookingRequest b) => b.ReferenceNumber;

    private static void Check(IdentityResult r)
    {
        if (!r.Succeeded) throw new InvalidOperationException(string.Join("; ", r.Errors.Select(e => e.Description)));
    }
}
