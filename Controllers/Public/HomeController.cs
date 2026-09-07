using GharsPlatform.Data;
using GharsPlatform.Hubs;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Models.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly IConfiguration _configuration;

    public HomeController(AppDbContext db, IHubContext<NotificationsHub> hub, IConfiguration configuration)
    {
        _db = db;
        _hub = hub;
        _configuration = configuration;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;

        var news = await _db.NewsItems
            .Where(x => x.IsActive &&
                        (x.StartsAtUtc == null || x.StartsAtUtc <= now) &&
                        (x.EndsAtUtc == null || x.EndsAtUtc >= now))
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        var season = await _db.Seasons
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        var upcomingActivities = await _db.Activities
            .Where(x => x.Status == ActivityStatus.Published && x.StartDateTime >= DateTime.UtcNow.AddDays(-1))
            .OrderBy(x => x.StartDateTime)
            .Take(6)
            .ToListAsync();

        var stories = await _db.SuccessStories
            .Where(x => x.IsPublished)
            .OrderByDescending(x => x.PublishedAtUtc)
            .Take(4)
            .ToListAsync();

        ViewBag.News = news;
        ViewBag.Season = season;
        ViewBag.UpcomingActivities = upcomingActivities;
        ViewBag.Stories = stories;

        return View();
    }

    public IActionResult About() => View();
    public IActionResult Vision() => View();

    /// <summary>
    /// The public booking entry point: approved implementing entities shown as logos, each offering
    /// "Request Booking" into the existing <c>/bookings/create</c> workflow.
    ///
    /// The entity list is queried from <see cref="Organization"/> with exactly the filter
    /// <c>BookingsController.PopulateCreateViewDataAsync</c> uses, so an entity offered here is always
    /// one the booking form will accept — sourcing it from <c>PartnerProfiles</c> instead would let a
    /// bookable entity go missing simply because it has no profile row.
    ///
    /// Anonymous visitors see the same entities; only the call to action differs, because the entity
    /// list is public information and hiding it would make the page useless before sign-in.
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Booking(int? entityId = null, ActivityType? type = null, int? seasonId = null, string? q = null)
    {
        // This page is a club's request catalogue. For an implementing entity every action on it is
        // one they cannot take, so they are sent to their own workflow rather than shown a wall of
        // disabled buttons.
        if (User.IsInRole(RoleNames.PartnerAdmin) && !User.IsInRole(RoleNames.ClubAdmin))
            return RedirectToAction("Index", "PartnerDashboard");

        var entities = await _db.Organizations
            .Where(x => x.Status == ApprovalStatus.Approved &&
                        (x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner))
            .OrderBy(x => x.NameEn)
            .ToListAsync();

        var entityIds = entities.Select(x => x.Id).ToList();
        var now = DateTime.UtcNow;

        // The catalogue shows only what a club may actually request: published offerings of a
        // bookable type, owned by an approved implementing entity, inside their availability window.
        // The same conditions are re-checked server-side when the booking is posted — this query is
        // for display and is not what authorises anything.
        var offerings = _db.Activities
            .Include(x => x.Season)
            .Where(x => x.Status == ActivityStatus.Published
                        && x.ApprovalStatus == OfferingApprovalStatus.Approved
                        && x.PartnerOrganizationId != null
                        && entityIds.Contains(x.PartnerOrganizationId.Value)
                        && (x.Type == ActivityType.TrainingProgram || x.Type == ActivityType.Workshop)
                        // Same season rule the booking gate applies, so the catalogue never shows a
                        // card that would be refused on submission.
                        && x.Season != null && x.Season.IsActive
                        && (x.AvailableFromUtc == null || x.AvailableFromUtc <= now)
                        && (x.AvailableUntilUtc == null || x.AvailableUntilUtc >= now));

        // Which entities have anything to show, computed before the entity filter narrows the list —
        // otherwise choosing one entity would empty its own dropdown. Only these appear in the
        // catalogue filter; every eligible entity remains reachable through Request Custom Booking.
        var entityIdsWithOfferings = await offerings
            .Select(x => x.PartnerOrganizationId!.Value)
            .Distinct()
            .ToListAsync();

        if (entityId.HasValue) offerings = offerings.Where(x => x.PartnerOrganizationId == entityId.Value);
        if (type is ActivityType.TrainingProgram or ActivityType.Workshop) offerings = offerings.Where(x => x.Type == type!.Value);
        if (seasonId.HasValue) offerings = offerings.Where(x => x.SeasonId == seasonId.Value);
        if (!string.IsNullOrWhiteSpace(q)) offerings = offerings.Where(x => x.TitleEn.Contains(q) || x.TitleAr.Contains(q));

        var list = await offerings.OrderBy(x => x.TitleEn).ToListAsync();

        ViewBag.Offerings = list
            .GroupBy(x => x.PartnerOrganizationId!.Value)
            .OrderBy(g => entities.First(e => e.Id == g.Key).NameEn)
            .ToDictionary(g => g.Key, g => g.ToList());
        ViewBag.Seasons = await _db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).ToListAsync();
        ViewBag.FilterEntities = entities.Where(x => entityIdsWithOfferings.Contains(x.Id)).ToList();
        ViewBag.EntityId = entityId;
        ViewBag.Type = type;
        ViewBag.SeasonId = seasonId;
        ViewBag.Query = q;
        ViewBag.TotalOfferings = list.Count;

        // The full eligible list stays the model: it resolves each group's name and logo, and it is
        // what the custom-booking fallback offers.
        return View(entities);
    }

    /// <summary>
    /// The former public partner directory. The booking page replaced it, so this keeps existing
    /// links and bookmarks working instead of 404ing. Administrative organization management is a
    /// different screen (<c>/Admin/Organizations</c>) and is unaffected.
    /// </summary>
    public IActionResult Partners() => RedirectToAction(nameof(Booking));

    [HttpGet("/partners/{id:int}")]
    public async Task<IActionResult> PartnerDetails(int id)
    {
        var partner = await _db.PartnerProfiles
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.OrganizationId == id && x.Organization != null && x.Organization.Status == ApprovalStatus.Approved);
        if (partner is null) return NotFound();

        var partnerUserIds = await _db.OrganizationAdminLinks
            .Where(x => x.OrganizationId == id)
            .Select(x => x.UserId)
            .ToListAsync();
        ViewBag.ProgramCount = await _db.Activities.CountAsync(x => partnerUserIds.Contains(x.CreatedByUserId) && x.Status == ActivityStatus.Published);
        return View(partner);
    }

    [HttpGet("/partners/{id:int}/learning-programs")]
    public async Task<IActionResult> LearningPrograms(int id, ActivityType? type = null)
    {
        var partner = await _db.PartnerProfiles
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.OrganizationId == id && x.Organization != null && x.Organization.Status == ApprovalStatus.Approved);
        if (partner is null) return NotFound();

        var partnerUserIds = await _db.OrganizationAdminLinks
            .Where(x => x.OrganizationId == id)
            .Select(x => x.UserId)
            .ToListAsync();

        var q = _db.Activities
            .Include(x => x.BookingRequests)
            .Where(x => x.Status == ActivityStatus.Published && (x.PartnerOrganizationId == id || partnerUserIds.Contains(x.CreatedByUserId)));
        if (type.HasValue) q = q.Where(x => x.Type == type.Value);

        var programs = await q.OrderBy(x => x.StartDateTime).ToListAsync();
        ViewBag.Partner = partner;
        ViewBag.Type = type;
        return View(programs);
    }

    // ------------------------------------------------------------------ Contact

    /// <summary>
    /// The required string properties are declared nullable on purpose. With nullable reference types
    /// enabled, MVC synthesises an implicit <c>[Required]</c> for a non-nullable reference property and
    /// that framework message — always English — pre-empts the bilingual one below.
    /// </summary>
    public class ContactVm
    {
        [BilingualRequired(ErrorMessage = "Your name is required.", Ar = "الاسم مطلوب."), MaxLength(150)]
        [Display(Name = "Full name")]
        public string? FullName { get; set; }

        [BilingualRequired(ErrorMessage = "An email address is required.", Ar = "البريد الإلكتروني مطلوب.")]
        [BilingualEmailAddress(ErrorMessage = "Enter a valid email address.", Ar = "يرجى إدخال بريد إلكتروني صحيح.")]
        [MaxLength(250)]
        public string? Email { get; set; }

        [MaxLength(50)]
        [Display(Name = "Phone")]
        public string? Phone { get; set; }

        [MaxLength(250)]
        [Display(Name = "Club / organization")]
        public string? OrganizationName { get; set; }

        public ContactTopic Topic { get; set; } = ContactTopic.GeneralEnquiry;

        [BilingualRequired(ErrorMessage = "A subject is required.", Ar = "عنوان الرسالة مطلوب."), MaxLength(200)]
        public string? Subject { get; set; }

        [BilingualRequired(ErrorMessage = "A message is required.", Ar = "نص الرسالة مطلوب.")]
        [MaxLength(4000)]
        [BilingualMinLength(10, ErrorMessage = "Please describe your enquiry in a little more detail.",
            Ar = "يرجى تقديم تفاصيل أوفى عن استفسارك.")]
        public string? Message { get; set; }

        /// <summary>
        /// Honeypot. Hidden from people by CSS and left empty by them; bots fill every field they find.
        /// A non-empty value is accepted with the normal thank-you and silently discarded, so a bot
        /// gets no signal telling it to try again differently.
        /// </summary>
        public string? Website { get; set; }
    }

    [HttpGet]
    public IActionResult Contact()
    {
        ViewBag.ContactDetails = ContactDetails.FromConfiguration(_configuration);
        return View(new ContactVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("contact-form")]
    public async Task<IActionResult> Contact(ContactVm vm)
    {
        ViewBag.ContactDetails = ContactDetails.FromConfiguration(_configuration);

        if (!string.IsNullOrWhiteSpace(vm.Website))
        {
            // Honeypot tripped. Behave exactly as success, but persist nothing and notify nobody.
            TempData["ToastSuccess"] = "Thank you. Your message has been received.";
            return RedirectToAction(nameof(Contact));
        }

        if (!ModelState.IsValid) return View(vm);

        // Non-null past this point: ModelState.IsValid means every BilingualRequired check passed.
        var entity = new ContactMessage
        {
            FullName = vm.FullName!.Trim(),
            Email = vm.Email!.Trim(),
            Phone = string.IsNullOrWhiteSpace(vm.Phone) ? null : vm.Phone.Trim(),
            OrganizationName = string.IsNullOrWhiteSpace(vm.OrganizationName) ? null : vm.OrganizationName.Trim(),
            Topic = vm.Topic,
            Subject = vm.Subject!.Trim(),
            Message = vm.Message!.Trim(),
            Status = ContactMessageStatus.New,
            SubmittedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            SubmittedFromIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            SubmittedCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };

        _db.ContactMessages.Add(entity);
        await _db.SaveChangesAsync();

        await NotifyReviewersAsync(entity);

        TempData["ToastSuccess"] = "Thank you. Your message has been received and the Ghars team has been notified.";
        return RedirectToAction(nameof(Contact));
    }

    /// <summary>
    /// Announce a new enquiry to the people who handle it. There is no outbound email in this platform,
    /// so this in-app notification is the whole delivery mechanism — it goes to DSC Admins and Super
    /// Admins, the two roles with access to the admin queue the notification links to.
    /// </summary>
    private async Task NotifyReviewersAsync(ContactMessage entity)
    {
        var topicEn = ContactLabels.Topic(entity.Topic, ar: false);
        var topicAr = ContactLabels.Topic(entity.Topic, ar: true);

        var n = new Notification
        {
            TitleEn = "New contact enquiry",
            TitleAr = "استفسار جديد عبر نموذج التواصل",
            MessageEn = $"{entity.FullName} ({topicEn}): {entity.Subject}",
            MessageAr = $"{entity.FullName} ({topicAr}): {entity.Subject}",
            Type = NotificationType.Info,
            TargetType = NotificationTargetType.Role,
            TargetRoleName = RoleNames.DscAdmin,
            // Site-relative: NotificationsController.Open passes this to LocalRedirect.
            LinkUrl = $"/Admin/ContactMessages/Details/{entity.Id}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = entity.SubmittedByUserId
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();

        // Deliver to both admin roles. TargetRoleName above records DSC Admin as the nominal audience,
        // but Super Admins can open the queue too and a site with no DSC Admin yet must not lose the
        // enquiry into a notification nobody receives.
        var roleNames = new[] { RoleNames.DscAdmin, RoleNames.SuperAdmin };
        var roleIds = await _db.Roles.Where(r => r.Name != null && roleNames.Contains(r.Name))
            .Select(r => r.Id).ToListAsync();
        var userIds = await _db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId).Distinct().ToListAsync();

        foreach (var uid in userIds)
            _db.NotificationDeliveries.Add(new NotificationDelivery
            {
                NotificationId = n.Id,
                UserId = uid,
                DeliveredAtUtc = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        // "notification" with the rich payload is what admin.js renders as a toast.
        await _hub.Clients.All.SendAsync("notification", new
        {
            id = n.Id,
            titleEn = n.TitleEn,
            titleAr = n.TitleAr,
            messageEn = n.MessageEn,
            messageAr = n.MessageAr,
            type = n.Type.ToString(),
            linkUrl = n.LinkUrl,
            createdAtUtc = n.CreatedAtUtc
        });
    }

    public IActionResult Error() => View();
}
