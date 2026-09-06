using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Core
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationContact> OrganizationContacts => Set<OrganizationContact>();
    public DbSet<OrganizationDocument> OrganizationDocuments => Set<OrganizationDocument>();
    public DbSet<OrganizationAdminLink> OrganizationAdminLinks => Set<OrganizationAdminLink>();

    public DbSet<SpeakerProfile> SpeakerProfiles => Set<SpeakerProfile>();

    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivitySpeaker> ActivitySpeakers => Set<ActivitySpeaker>();
    public DbSet<BookingRequest> BookingRequests => Set<BookingRequest>();
    public DbSet<BookingAuditTrail> BookingAuditTrails => Set<BookingAuditTrail>();
    public DbSet<BookingProposedTimeOption> BookingProposedTimeOptions => Set<BookingProposedTimeOption>();

    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    public DbSet<CertificateTemplate> CertificateTemplates => Set<CertificateTemplate>();
    public DbSet<Certificate> Certificates => Set<Certificate>();

    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<ExternalSurvey> ExternalSurveys => Set<ExternalSurvey>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyOption> SurveyOptions => Set<SurveyOption>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<SurveyAnswer> SurveyAnswers => Set<SurveyAnswer>();

    public DbSet<MediaAlbum> MediaAlbums => Set<MediaAlbum>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<SuccessStory> SuccessStories => Set<SuccessStory>();

    public DbSet<LibraryCategory> LibraryCategories => Set<LibraryCategory>();
    public DbSet<LibraryItem> LibraryItems => Set<LibraryItem>();
    public DbSet<UserPointsWallet> UserPointsWallets => Set<UserPointsWallet>();
    public DbSet<PointsTransaction> PointsTransactions => Set<PointsTransaction>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<RewardRedemption> RewardRedemptions => Set<RewardRedemption>();

    public DbSet<PartnerProfile> PartnerProfiles => Set<PartnerProfile>();

    public DbSet<NewsItem> NewsItems => Set<NewsItem>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<SystemAuditLog> SystemAuditLogs => Set<SystemAuditLog>();
    public DbSet<AgendaEntry> AgendaEntries => Set<AgendaEntry>();
    public DbSet<AgendaMedia> AgendaMedia => Set<AgendaMedia>();
    public DbSet<KpiSubmission> KpiSubmissions => Set<KpiSubmission>();
    public DbSet<KpiDocument> KpiDocuments => Set<KpiDocument>();
    public DbSet<GalleryItem> GalleryItems => Set<GalleryItem>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();


    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Global query filters (soft delete)
        builder.Entity<Organization>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<MediaAlbum>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<SuccessStory>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<LibraryItem>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Reward>().HasQueryFilter(x => !x.IsDeleted);

        builder.Entity<OrganizationAdminLink>()
            .HasIndex(x => new { x.OrganizationId, x.UserId })
            .IsUnique();

        builder.Entity<ActivitySpeaker>()
            .HasIndex(x => new { x.ActivityId, x.SpeakerUserId })
            .IsUnique();

        builder.Entity<Activity>()
            .HasOne(x => x.PartnerOrganization)
            .WithMany()
            .HasForeignKey(x => x.PartnerOrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<AttendanceRecord>()
            .HasIndex(x => new { x.AttendanceSessionId, x.UserId })
            .IsUnique();

        builder.Entity<SurveyResponse>()
            .HasIndex(x => new { x.SurveyId, x.UserId })
            .IsUnique();

        builder.Entity<Certificate>()
            .HasIndex(x => x.CertificateNo)
            .IsUnique();

        builder.Entity<Certificate>()
            .HasIndex(x => x.VerifyToken)
            .IsUnique();

        builder.Entity<NotificationDelivery>()
            .HasIndex(x => new { x.NotificationId, x.UserId })
            .IsUnique();

        builder.Entity<UserPointsWallet>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        builder.Entity<PartnerProfile>()
            .HasIndex(x => x.OrganizationId)
            .IsUnique();


        builder.Entity<BookingRequest>()
            .HasOne(x => x.PartnerOrganization)
            .WithMany()
            .HasForeignKey(x => x.PartnerOrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // ActivityId is optional now (direct entity-first requests). Deterministic NoAction:
        // ActivitiesController already cancels (not deletes) activities that have bookings.
        builder.Entity<BookingRequest>()
            .HasOne(x => x.Activity)
            .WithMany(x => x.BookingRequests)
            .HasForeignKey(x => x.ActivityId)
            .OnDelete(DeleteBehavior.NoAction);

        // Direct (entity-first) booking requests carry their own Season; NoAction avoids a second
        // cascade path (Season -> Activities -> BookingRequests already cascades).
        builder.Entity<BookingRequest>()
            .HasOne(x => x.Season)
            .WithMany()
            .HasForeignKey(x => x.SeasonId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<ExternalSurvey>()
            .HasOne(x => x.Season)
            .WithMany()
            .HasForeignKey(x => x.SeasonId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<KpiSubmission>()
            .Property(x => x.PhysicalActivityComplianceRate)
            .HasPrecision(18, 2);

        // Satisfaction evidence lineage. Optional (historical submissions predate it) and NoAction on
        // delete, consistent with the rest of the model: an approved KPI submission must survive the
        // removal of the survey record it cites, so the approved figure is never silently orphaned.
        builder.Entity<KpiSubmission>()
            .HasOne(x => x.SatisfactionExternalSurvey)
            .WithMany()
            .HasForeignKey(x => x.SatisfactionExternalSurveyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<KpiSubmission>()
            .HasIndex(x => x.SatisfactionExternalSurveyId);

        builder.Entity<BookingProposedTimeOption>()
            .HasOne(x => x.BookingRequest)
            .WithMany(x => x.ProposedTimeOptions)
            .HasForeignKey(x => x.BookingRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<BookingProposedTimeOption>()
            .HasIndex(x => new { x.BookingRequestId, x.IsActive });



        builder.Entity<AgendaEntry>()
            .HasOne(x => x.BookingRequest)
            .WithMany()
            .HasForeignKey(x => x.BookingRequestId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<AgendaEntry>()
            .HasIndex(x => new { x.SeasonId, x.OrganizationId, x.ActivityDate });

        builder.Entity<KpiSubmission>()
            .HasIndex(x => new { x.SeasonId, x.OrganizationId })
            .IsUnique();

        builder.Entity<GalleryItem>()
            .HasIndex(x => new { x.SeasonId, x.OrganizationId, x.MediaDate });

        // The admin queue is "newest unhandled first", which is the only way this table is ever read.
        builder.Entity<ContactMessage>()
            .HasIndex(x => new { x.Status, x.CreatedAtUtc });

        // --------------------------------------------------------------------
        // FIX: Prevent SQL Server "multiple cascade paths" for SurveyAnswers
        // SurveyAnswer links to BOTH SurveyResponse and SurveyQuestion.
        // If both cascade, SQL Server blocks migration. Use NO ACTION instead.
        // --------------------------------------------------------------------

        builder.Entity<SurveyAnswer>()
            .HasOne(x => x.SurveyResponse)
            .WithMany(x => x.Answers)
            .HasForeignKey(x => x.SurveyResponseId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<SurveyAnswer>()
            .HasOne(x => x.SurveyQuestion)
            .WithMany()
            .HasForeignKey(x => x.SurveyQuestionId)
            .OnDelete(DeleteBehavior.NoAction);

        // If SurveyAnswer has SelectedOptionId, keep it NO ACTION as well
        builder.Entity<SurveyAnswer>()
            .HasOne(x => x.SelectedOption)
            .WithMany()
            .HasForeignKey(x => x.SelectedOptionId)
            .OnDelete(DeleteBehavior.NoAction);
    }

}
