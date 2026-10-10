using System.Text;

namespace GharsPlatform.Helpers;

/// <summary>
/// Content Security Policy for the whole site, plus the endpoint browsers send violation reports to.
/// </summary>
/// <remarks>
/// <para>
/// The policy is sent in <b>report-only</b> mode by default (<c>Security:Csp:Mode</c> = <c>ReportOnly</c>):
/// browsers enforce nothing and report what they would have blocked to <see cref="ReportPath"/>, which
/// logs it. Set the mode to <c>Enforce</c> only after the reports from staging and production have been
/// reviewed and are empty. <c>Off</c> sends no policy.
/// </para>
/// <para>
/// Every script, stylesheet and font is served from this site (wwwroot/lib, see its README), so no CDN
/// host is listed. Pages still use inline <c>&lt;script&gt;</c> blocks, inline event handlers and
/// <c>style</c> attributes, so <c>'unsafe-inline'</c> remains for scripts and styles; moving those to
/// files or nonces is what would let it be removed. Images and media may be external because gallery
/// items can link to outside https sources; frames allow YouTube for gallery videos.
/// </para>
/// </remarks>
public static class SecurityHeaders
{
    public const string ReportPath = "/csp-report";

    public static readonly string Policy = string.Join("; ",
        "default-src 'self'",
        "script-src 'self' 'unsafe-inline'",
        "style-src 'self' 'unsafe-inline'",
        "font-src 'self' data:",
        "img-src 'self' data: blob: https:",
        "media-src 'self' blob: https:",
        "connect-src 'self' wss: ws:",
        "frame-src 'self' https://www.youtube.com https://www.youtube-nocookie.com",
        "frame-ancestors 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "report-uri " + ReportPath);

    public enum CspMode { ReportOnly, Enforce, Off }

    public static CspMode ModeFrom(IConfiguration configuration)
        => Enum.TryParse<CspMode>(configuration["Security:Csp:Mode"], ignoreCase: true, out var mode) ? mode : CspMode.ReportOnly;

    /// <summary>Adds the policy header and <c>X-Content-Type-Options: nosniff</c> to every response.</summary>
    public static IApplicationBuilder UseGharsSecurityHeaders(this IApplicationBuilder app, CspMode mode)
    {
        var header = mode switch
        {
            CspMode.Enforce => "Content-Security-Policy",
            CspMode.ReportOnly => "Content-Security-Policy-Report-Only",
            _ => null
        };

        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (header is not null && !headers.ContainsKey(header)) headers[header] = Policy;
                headers.XContentTypeOptions = "nosniff";
                return Task.CompletedTask;
            });
            await next();
        });
    }

    /// <summary>
    /// Accepts violation reports (<c>application/csp-report</c> or <c>application/reports+json</c>) and
    /// writes them to the log as a warning. Anonymous by necessity; the body is capped and never stored.
    /// </summary>
    public static RouteHandlerBuilder MapCspReports(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost(ReportPath, async (HttpContext context, ILoggerFactory loggers) =>
        {
            const int maxBytes = 8 * 1024;
            var buffer = new byte[maxBytes];
            var read = 0;
            int n;
            while (read < maxBytes && (n = await context.Request.Body.ReadAsync(buffer.AsMemory(read, maxBytes - read))) > 0)
                read += n;

            var body = Encoding.UTF8.GetString(buffer, 0, read).Replace('\r', ' ').Replace('\n', ' ');
            loggers.CreateLogger("CspReport").LogWarning("CSP violation report: {Report}", body);
            return Results.NoContent();
        }).AllowAnonymous().DisableAntiforgery();
    }
}
