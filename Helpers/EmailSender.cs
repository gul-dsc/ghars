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

    /// <summary>Email every in-app notification too. Set Smtp__NotificationsEnabled=false to stop it.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Base for links in notification emails; when empty, the address of the triggering request.</summary>
    public string? SiteUrl { get; set; }

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

    // Ghars palette (wwwroot/css): green #2D9B6C, deep #1B5E32, sprout #AED581, charcoal #353535.
    // Email clients ignore <style> and CSS variables, so everything is inline and table-based.
    private const string Font = "'Segoe UI',Tahoma,Arial,sans-serif";

    public static string Encode(string s) => System.Net.WebUtility.HtmlEncode(s);

    /// <summary>One language section: heading, paragraphs, optional detail box, optional button.</summary>
    public static string Section(bool rtl, string heading, string bodyHtml, string? buttonUrl = null,
        string? buttonText = null, IEnumerable<(string Label, string Value)>? details = null)
    {
        var align = rtl ? "right" : "left";
        var sb = new System.Text.StringBuilder();
        sb.Append($"<div dir=\"{(rtl ? "rtl" : "ltr")}\" style=\"text-align:{align};font-family:{Font};\">");
        sb.Append($"<h2 style=\"margin:0 0 14px;font-size:20px;line-height:1.4;color:#1B5E32;font-weight:700;\">{heading}</h2>");
        sb.Append($"<div style=\"font-size:15px;line-height:1.7;color:#353535;\">{bodyHtml}</div>");
        if (details != null)
        {
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:18px 0;background:#F1F8EC;border:1px solid #D7EBC6;border-radius:10px;\">");
            foreach (var (label, value) in details)
                sb.Append($"<tr><td style=\"padding:12px 16px;font-size:13px;color:#6b7280;width:38%;text-align:{align};border-bottom:1px solid #E3F0D8;\">{label}</td>" +
                          $"<td dir=\"ltr\" style=\"padding:12px 16px;font-size:15px;color:#1c1f23;font-weight:600;font-family:Consolas,'Courier New',monospace;text-align:{align};border-bottom:1px solid #E3F0D8;word-break:break-all;\">{value}</td></tr>");
            sb.Append("</table>");
        }
        if (buttonUrl != null && buttonText != null)
            sb.Append($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" align=\"{align}\" style=\"margin:20px 0 6px;\"><tr>" +
                      $"<td style=\"background:#2D9B6C;border-radius:8px;\"><a href=\"{Encode(buttonUrl)}\" style=\"display:inline-block;padding:13px 28px;font-family:{Font};font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;\">{buttonText}</a></td>" +
                      "</tr></table><div style=\"clear:both\"></div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>The branded frame: logo header, English then Arabic card body, footer.</summary>
    public static string Layout(string siteUrl, string preheader, string englishSection, string arabicSection)
    {
        var site = siteUrl.TrimEnd('/');
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>" +
            "<body style=\"margin:0;padding:0;background:#f6f7f8;\">" +
            $"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{Encode(preheader)}</div>" +
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f6f7f8;\"><tr><td align=\"center\" style=\"padding:28px 12px;\">" +
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border-radius:14px;overflow:hidden;border:1px solid #e6e8eb;\">" +
            // header
            "<tr><td style=\"background:#ffffff;padding:22px 28px 18px;text-align:center;border-bottom:1px solid #eef0f2;\">" +
            $"<img src=\"{site}/img/brand/ghars-logo.png\" alt=\"Ghars | غرس\" height=\"56\" style=\"height:56px;width:auto;border:0;display:inline-block;\">" +
            "</td></tr>" +
            "<tr><td style=\"height:5px;line-height:5px;font-size:0;background:#2D9B6C;background-image:linear-gradient(90deg,#1B5E32,#2D9B6C 55%,#AED581);\">&nbsp;</td></tr>" +
            // English
            $"<tr><td style=\"padding:30px 32px 10px;\">{englishSection}</td></tr>" +
            "<tr><td style=\"padding:8px 32px;\"><div style=\"border-top:1px dashed #d9dee3;font-size:0;line-height:0;\">&nbsp;</div></td></tr>" +
            // Arabic
            $"<tr><td style=\"padding:14px 32px 30px;\">{arabicSection}</td></tr>" +
            // footer
            $"<tr><td style=\"background:#353535;padding:20px 28px;text-align:center;font-family:{Font};font-size:12px;line-height:1.7;color:#d1d5db;\">" +
            "Ghars Platform &middot; Dubai Sports Council<br>" +
            "<span dir=\"rtl\">منصة غرس &middot; مجلس دبي الرياضي</span><br>" +
            $"<a href=\"{site}\" style=\"color:#AED581;text-decoration:none;\">{Encode(site.Replace("https://", ""))}</a>" +
            "<div style=\"margin-top:8px;color:#9ca3af;font-size:11px;\">This is an automated message, please do not reply. &middot; <span dir=\"rtl\">رسالة آلية، يرجى عدم الرد.</span></div>" +
            "</td></tr></table></td></tr></table></body></html>";
    }
}
