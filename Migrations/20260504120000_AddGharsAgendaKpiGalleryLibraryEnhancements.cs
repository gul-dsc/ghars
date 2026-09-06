using System;
using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    // Hand-written migration. Without these attributes EF does not recognise the class as part of the
    // chain, so it is skipped by `database update` and a fresh database never gets these changes.
    // This one creates AgendaEntries, KpiSubmissions, AgendaMedia, GalleryItems and KpiDocuments, so
    // skipping it leaves a new database without the core Ghars tables.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260504120000_AddGharsAgendaKpiGalleryLibraryEnhancements")]
    public partial class AddGharsAgendaKpiGalleryLibraryEnhancements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "CoverImagePath", table: "LibraryItems", type: "nvarchar(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ExternalUrl", table: "LibraryItems", type: "nvarchar(700)", maxLength: 700, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PublishingEntityEn", table: "LibraryItems", type: "nvarchar(250)", maxLength: 250, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PublishingEntityAr", table: "LibraryItems", type: "nvarchar(250)", maxLength: 250, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "PublicationDate", table: "LibraryItems", type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<byte>(name: "ContentType", table: "LibraryItems", type: "tinyint", nullable: false, defaultValue: (byte)3);
            migrationBuilder.AddColumn<bool>(name: "IsPublished", table: "LibraryItems", type: "bit", nullable: false, defaultValue: true);
            migrationBuilder.AlterColumn<string>(name: "FilePath", table: "LibraryItems", type: "nvarchar(500)", maxLength: 500, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500);

            migrationBuilder.CreateTable(
                name: "AgendaEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    BookingRequestId = table.Column<int>(type: "int", nullable: true),
                    ActivityType = table.Column<byte>(type: "tinyint", nullable: false),
                    SubjectEn = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SubjectAr = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ActivityDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    OtherCategory = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    LecturerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DepartmentOrOrganization = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    NumberOfParticipants = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgendaEntries", x => x.Id);
                    table.ForeignKey("FK_AgendaEntries_BookingRequests_BookingRequestId", x => x.BookingRequestId, "BookingRequests", "Id");
                    table.ForeignKey("FK_AgendaEntries_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_AgendaEntries_Seasons_SeasonId", x => x.SeasonId, "Seasons", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KpiSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    NumberOfLecturesActivities = table.Column<int>(type: "int", nullable: false),
                    NumberOfLecturers = table.Column<int>(type: "int", nullable: false),
                    NumberOfImplementingEntities = table.Column<int>(type: "int", nullable: false),
                    NumberOfParticipants = table.Column<int>(type: "int", nullable: false),
                    PlayerParticipationRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WarningsAndRedCards = table.Column<int>(type: "int", nullable: false),
                    WeeklyTrainingMinutes = table.Column<int>(type: "int", nullable: false),
                    DiabetesCases = table.Column<int>(type: "int", nullable: false),
                    HeartConditionCases = table.Column<int>(type: "int", nullable: false),
                    HypertensionCases = table.Column<int>(type: "int", nullable: false),
                    OtherLifestyleConditionCases = table.Column<int>(type: "int", nullable: false),
                    SatisfactionRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AttendanceRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EthicalValuesAdherenceRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    HealthyDietaryHabitsRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CommunityEventsCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ReviewNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiSubmissions", x => x.Id);
                    table.ForeignKey("FK_KpiSubmissions_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_KpiSubmissions_Seasons_SeasonId", x => x.SeasonId, "Seasons", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgendaMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    AgendaEntryId = table.Column<int>(type: "int", nullable: false),
                    MediaType = table.Column<byte>(type: "tinyint", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExternalUrl = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: true),
                    TitleEn = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    TitleAr = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgendaMedia", x => x.Id);
                    table.ForeignKey("FK_AgendaMedia_AgendaEntries_AgendaEntryId", x => x.AgendaEntryId, "AgendaEntries", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GalleryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    TitleEn = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MediaType = table.Column<byte>(type: "tinyint", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExternalUrl = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: true),
                    OrganizationId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    AgendaEntryId = table.Column<int>(type: "int", nullable: true),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    MediaDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GalleryItems", x => x.Id);
                    table.ForeignKey("FK_GalleryItems_Activities_ActivityId", x => x.ActivityId, "Activities", "Id");
                    table.ForeignKey("FK_GalleryItems_AgendaEntries_AgendaEntryId", x => x.AgendaEntryId, "AgendaEntries", "Id");
                    table.ForeignKey("FK_GalleryItems_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id");
                    table.ForeignKey("FK_GalleryItems_Seasons_SeasonId", x => x.SeasonId, "Seasons", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KpiDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    KpiSubmissionId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiDocuments", x => x.Id);
                    table.ForeignKey("FK_KpiDocuments_KpiSubmissions_KpiSubmissionId", x => x.KpiSubmissionId, "KpiSubmissions", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_AgendaEntries_BookingRequestId", "AgendaEntries", "BookingRequestId");
            migrationBuilder.CreateIndex("IX_AgendaEntries_OrganizationId", "AgendaEntries", "OrganizationId");
            migrationBuilder.CreateIndex("IX_AgendaEntries_SeasonId_OrganizationId_ActivityDate", "AgendaEntries", new[] { "SeasonId", "OrganizationId", "ActivityDate" });
            migrationBuilder.CreateIndex("IX_AgendaMedia_AgendaEntryId", "AgendaMedia", "AgendaEntryId");
            migrationBuilder.CreateIndex("IX_GalleryItems_ActivityId", "GalleryItems", "ActivityId");
            migrationBuilder.CreateIndex("IX_GalleryItems_AgendaEntryId", "GalleryItems", "AgendaEntryId");
            migrationBuilder.CreateIndex("IX_GalleryItems_OrganizationId", "GalleryItems", "OrganizationId");
            migrationBuilder.CreateIndex("IX_GalleryItems_SeasonId_OrganizationId_MediaDate", "GalleryItems", new[] { "SeasonId", "OrganizationId", "MediaDate" });
            migrationBuilder.CreateIndex("IX_KpiDocuments_KpiSubmissionId", "KpiDocuments", "KpiSubmissionId");
            migrationBuilder.CreateIndex("IX_KpiSubmissions_OrganizationId", "KpiSubmissions", "OrganizationId");
            migrationBuilder.CreateIndex("IX_KpiSubmissions_SeasonId_OrganizationId", "KpiSubmissions", new[] { "SeasonId", "OrganizationId" }, unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("AgendaMedia");
            migrationBuilder.DropTable("GalleryItems");
            migrationBuilder.DropTable("KpiDocuments");
            migrationBuilder.DropTable("AgendaEntries");
            migrationBuilder.DropTable("KpiSubmissions");
            migrationBuilder.DropColumn("CoverImagePath", "LibraryItems");
            migrationBuilder.DropColumn("ExternalUrl", "LibraryItems");
            migrationBuilder.DropColumn("PublishingEntityEn", "LibraryItems");
            migrationBuilder.DropColumn("PublishingEntityAr", "LibraryItems");
            migrationBuilder.DropColumn("PublicationDate", "LibraryItems");
            migrationBuilder.DropColumn("ContentType", "LibraryItems");
            migrationBuilder.DropColumn("IsPublished", "LibraryItems");
            migrationBuilder.AlterColumn<string>(name: "FilePath", table: "LibraryItems", type: "nvarchar(500)", maxLength: 500, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500, oldNullable: true);
        }
    }
}
