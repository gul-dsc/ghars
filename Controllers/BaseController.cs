using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace GharsPlatform.Controllers;

public abstract class BaseController : Controller
{
    protected readonly AppDbContext Db;

    protected BaseController(AppDbContext db)
    {
        Db = db;
    }

    protected string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    protected async Task AuditAsync(string action, string entityName, string? entityId, object? oldValues = null, object? newValues = null)
    {
        try
        {
            Db.SystemAuditLogs.Add(new SystemAuditLog
            {
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                UserId = CurrentUserId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                OldValuesJson = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
                NewValuesJson = newValues is null ? null : JsonSerializer.Serialize(newValues),
                AtUtc = DateTime.UtcNow
            });
            await Db.SaveChangesAsync();
        }
        catch
        {
            // Non-blocking audit
        }
    }
}
