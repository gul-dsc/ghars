using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualReportAndGharsChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "ApprovalStatus",
                table: "GalleryItems",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ChannelCategory",
                table: "GalleryItems",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LibraryItemId",
                table: "GalleryItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "GalleryItems",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "GalleryItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByUserId",
                table: "GalleryItems",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "GalleryItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedByUserId",
                table: "GalleryItems",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GharsAnnualReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ProgramCoordinatorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ClubProposedLectures = table.Column<int>(type: "int", nullable: false),
                    CouncilProposedLectures = table.Column<int>(type: "int", nullable: false),
                    TotalLecturesDelivered = table.Column<int>(type: "int", nullable: false),
                    LecturersCount = table.Column<int>(type: "int", nullable: false),
                    ImplementingEntitiesCount = table.Column<int>(type: "int", nullable: false),
                    ParticipantsTotal = table.Column<int>(type: "int", nullable: false),
                    DeliveredActivitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SnapshotTakenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KeyResults = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Challenges = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DevelopmentProposals = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GharsAnnualReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GharsAnnualReports_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GharsAnnualReports_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GalleryItems_ApprovalStatus_SubmittedAtUtc",
                table: "GalleryItems",
                columns: new[] { "ApprovalStatus", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GalleryItems_LibraryItemId",
                table: "GalleryItems",
                column: "LibraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GharsAnnualReports_OrganizationId_SeasonId",
                table: "GharsAnnualReports",
                columns: new[] { "OrganizationId", "SeasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GharsAnnualReports_SeasonId",
                table: "GharsAnnualReports",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_GharsAnnualReports_Status_SubmittedAtUtc",
                table: "GharsAnnualReports",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_GalleryItems_LibraryItems_LibraryItemId",
                table: "GalleryItems",
                column: "LibraryItemId",
                principalTable: "LibraryItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GalleryItems_LibraryItems_LibraryItemId",
                table: "GalleryItems");

            migrationBuilder.DropTable(
                name: "GharsAnnualReports");

            migrationBuilder.DropIndex(
                name: "IX_GalleryItems_ApprovalStatus_SubmittedAtUtc",
                table: "GalleryItems");

            migrationBuilder.DropIndex(
                name: "IX_GalleryItems_LibraryItemId",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "ChannelCategory",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "LibraryItemId",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "GalleryItems");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "GalleryItems");
        }
    }
}
