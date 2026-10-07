using System.Threading.Channels;
using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace GharsPlatform.Helpers;

/// <summary>One bell notification to email to one user.</summary>
public record NotificationEmailJob(int NotificationId, string UserId, string SiteUrl);

/// <summary>
/// Hands every in-app notification to email as well. Each controller that notifies somebody adds one
/// NotificationDelivery row per recipient, so watching those rows covers every place at once and emails
/// exactly the people the bell shows it to. The work is queued after the save commits and sent by
/// <see cref="NotificationEmailWorker"/>, so a slow or broken mail server never delays a request.
/// </summary>
public class NotificationEmailInterceptor : SaveChangesInterceptor
{
    private readonly Channel<NotificationEmailJob> _queue;
    private readonly IHttpContextAccessor _http;
    private readonly SmtpOptions _smtp;
    // Keyed by context: the rows seen before a save, waiting for it to commit.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, List<NotificationDelivery>> _pending = new();

    public NotificationEmailInterceptor(Channel<NotificationEmailJob> queue, IHttpContextAccessor http, IOptions<SmtpOptions> smtp)
    {
        _queue = queue;
        _http = http;
        _smtp = smtp.Value;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Enqueue(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Enqueue(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context != null) _pending.Remove(eventData.Context);
    }

    private void Capture(DbContext? context)
    {
        if (context == null) return;
        _pending.Remove(context);
        // Only web requests send: seeding and command-line tools have no HttpContext.
        if (!_smtp.NotificationsEnabled || !_smtp.IsConfigured || _http.HttpContext == null) return;
        var added = context.ChangeTracker.Entries<NotificationDelivery>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        if (added.Count > 0) _pending.AddOrUpdate(context, added);
    }

    private void Enqueue(DbContext? context)
    {
        if (context == null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        var req = _http.HttpContext?.Request;
        var site = !string.IsNullOrWhiteSpace(_smtp.SiteUrl) ? _smtp.SiteUrl!
                 : req != null ? $"{req.Scheme}://{req.Host}" : "";
        foreach (var d in pending)
            if (d.NotificationId > 0) _queue.Writer.TryWrite(new NotificationEmailJob(d.NotificationId, d.UserId, site));
    }
}

public class NotificationEmailWorker : BackgroundService
{
    private readonly Channel<NotificationEmailJob> _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NotificationEmailWorker> _logger;

    public NotificationEmailWorker(Channel<NotificationEmailJob> queue, IServiceScopeFactory scopes, ILogger<NotificationEmailWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await SendAsync(scope.ServiceProvider, job);
            }
            catch (Exception ex)
            {
                // Never retried and never rethrown: the bell notification already exists either way.
                _logger.LogError(ex, "Notification email {NotificationId} to user {UserId} failed", job.NotificationId, job.UserId);
            }
        }
    }

    private static async Task SendAsync(IServiceProvider sp, NotificationEmailJob job)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var email = sp.GetRequiredService<EmailSender>();
        if (!email.IsConfigured) return;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == job.UserId);
        if (user == null || string.IsNullOrWhiteSpace(user.Email)) return;
        if (user.Email.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return;
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow.AddYears(1)) return; // deactivated

        var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.NotificationId);
        if (n == null) return;

        var site = job.SiteUrl.TrimEnd('/');
        string? link = null;
        if (!string.IsNullOrWhiteSpace(n.LinkUrl) && !string.IsNullOrEmpty(site))
            link = n.LinkUrl.StartsWith("/") ? site + n.LinkUrl : n.LinkUrl.StartsWith("http") ? n.LinkUrl : null;
        link ??= string.IsNullOrEmpty(site) ? null : site + "/notifications";

        var E = EmailSender.Encode;
        string Para(string s) => string.Join("", E(s).Split('\n').Select(l => $"<p style=\"margin:0 0 10px\">{l}</p>"));
        var body = EmailSender.Layout(site, n.TitleEn + " | " + n.TitleAr,
            EmailSender.Section(false, E(n.TitleEn), Para(n.MessageEn), link, "Open in Ghars"),
            EmailSender.Section(true, E(n.TitleAr), Para(n.MessageAr), link, "فتح في غرس"));

        // The user's preferred language goes first in the subject.
        var subject = user.PreferredLanguage == "ar"
            ? $"منصة غرس - {n.TitleAr} | {n.TitleEn}"
            : $"Ghars Platform - {n.TitleEn} | {n.TitleAr}";
        await email.SendAsync(user.Email, subject, body);
    }
}
