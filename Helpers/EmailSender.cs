using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace GharsPlatform.Helpers;

/// <summary>
/// SMTP settings, bound from the "Smtp" configuration section. On the server they are set as IIS
/// environment variables (Smtp__Host, Smtp__Password, ...) - never in appsettings.json, which is public.
/// </summary>
public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Ghars Platform";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public class EmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IOptions<SmtpOptions> options, ILogger<EmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>Sends one HTML message. Throws if SMTP is not configured or the server refuses it.</summary>
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("Email is not configured. Set Smtp__Host and Smtp__FromAddress on the server.");

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress!, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        message.To.Add(to);

        using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl };
        if (!string.IsNullOrWhiteSpace(_options.UserName))
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);

        await client.SendMailAsync(message);
        // The address only - never the body, which can hold a password.
        _logger.LogInformation("Email '{Subject}' sent to {To}", subject, to);
    }

    /// <summary>A bilingual body: the English block, then the Arabic block right-to-left.</summary>
    public static string Bilingual(string englishHtml, string arabicHtml) =>
        "<div style=\"font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:14px;color:#1f2d27\">" +
        englishHtml +
        "<hr style=\"border:none;border-top:1px solid #ddd;margin:24px 0\">" +
        "<div dir=\"rtl\" style=\"text-align:right\">" + arabicHtml + "</div></div>";
}
