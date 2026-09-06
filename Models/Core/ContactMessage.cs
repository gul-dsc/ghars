using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// An enquiry submitted through the public Contact page.
///
/// This platform has no outbound email service, so an enquiry cannot be "sent" anywhere. It is
/// persisted here and announced to DSC reviewers through the existing in-app notification system —
/// the same channel KPI submissions and bookings use. That makes the record, not a mail server, the
/// thing that must not be lost: an enquiry is never dropped just because nobody was signed in.
///
/// Bound from <c>HomeController.ContactVm</c> rather than directly, so a visitor cannot post
/// <see cref="Status"/>, <see cref="AdminNotes"/> or the handled/audit fields.
/// </summary>
public class ContactMessage : AuditableEntity
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, MaxLength(250)]
    public string Email { get; set; } = "";

    [MaxLength(50)]
    public string? Phone { get; set; }

    /// <summary>Free text: the sender may not be a registered organization in this system.</summary>
    [MaxLength(250)]
    public string? OrganizationName { get; set; }

    public ContactTopic Topic { get; set; } = ContactTopic.GeneralEnquiry;

    [Required, MaxLength(200)]
    public string Subject { get; set; } = "";

    [Required, MaxLength(4000)]
    public string Message { get; set; } = "";

    public ContactMessageStatus Status { get; set; } = ContactMessageStatus.New;

    [MaxLength(2000)]
    public string? AdminNotes { get; set; }

    public DateTime? HandledAtUtc { get; set; }

    [MaxLength(450)]
    public string? HandledByUserId { get; set; }

    /// <summary>
    /// Set when a signed-in user submits the form, so a club's enquiry can be tied back to its account.
    /// Null for anonymous visitors, which is the normal case.
    /// </summary>
    [MaxLength(450)]
    public string? SubmittedByUserId { get; set; }

    /// <summary>
    /// Retained for abuse handling only — the same justification and field width as
    /// <see cref="SystemAuditLog.IpAddress"/>. It is shown to DSC reviewers, never published.
    /// </summary>
    [MaxLength(64)]
    public string? SubmittedFromIp { get; set; }

    /// <summary>The UI language the enquiry was written in, so the reply is drafted in that language.</summary>
    [MaxLength(10)]
    public string? SubmittedCulture { get; set; }
}

/// <summary>
/// Bilingual labels for the contact enums. Shared by the public form and the admin queue so a topic
/// never reads one way to the sender and another way to the reviewer.
/// </summary>
public static class ContactLabels
{
    public static readonly IReadOnlyList<ContactTopic> Topics = new[]
    {
        ContactTopic.GeneralEnquiry,
        ContactTopic.LectureOrEventBooking,
        ContactTopic.ClubOrAcademyRegistration,
        ContactTopic.PartnershipOrImplementingEntity,
        ContactTopic.KpiOrReporting,
        ContactTopic.TechnicalSupport,
        ContactTopic.Other
    };

    public static string Topic(ContactTopic topic, bool ar) => topic switch
    {
        ContactTopic.LectureOrEventBooking => ar ? "حجز محاضرة أو فعالية" : "Lecture or event booking",
        ContactTopic.ClubOrAcademyRegistration => ar ? "تسجيل نادٍ أو أكاديمية" : "Club or academy registration",
        ContactTopic.PartnershipOrImplementingEntity => ar ? "شراكة أو جهة منفذة" : "Partnership / implementing entity",
        ContactTopic.KpiOrReporting => ar ? "مؤشرات الأداء والتقارير" : "KPIs and reporting",
        ContactTopic.TechnicalSupport => ar ? "الدعم الفني" : "Technical support",
        ContactTopic.Other => ar ? "أخرى" : "Other",
        _ => ar ? "استفسار عام" : "General enquiry"
    };

    public static string Status(ContactMessageStatus status, bool ar) => status switch
    {
        ContactMessageStatus.InProgress => ar ? "قيد المعالجة" : "In progress",
        ContactMessageStatus.Resolved => ar ? "تم الرد" : "Resolved",
        ContactMessageStatus.Spam => ar ? "رسالة مزعجة" : "Spam",
        _ => ar ? "جديدة" : "New"
    };

    /// <summary>Bootstrap contextual suffix for the status badge in the admin queue.</summary>
    public static string StatusBadge(ContactMessageStatus status) => status switch
    {
        ContactMessageStatus.InProgress => "warning",
        ContactMessageStatus.Resolved => "success",
        ContactMessageStatus.Spam => "secondary",
        _ => "danger"
    };
}
