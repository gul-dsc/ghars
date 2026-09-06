using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <inheritdoc />
    public partial class GharsDocsAlignment2026 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingRequests_Activities_ActivityId",
                table: "BookingRequests");

            migrationBuilder.AddColumn<decimal>(
                name: "PhysicalActivityComplianceRate",
                table: "KpiSubmissions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ActivityId",
                table: "BookingRequests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "LecturerContact",
                table: "BookingRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LecturerName",
                table: "BookingRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedSubject",
                table: "BookingRequests",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeasonId",
                table: "BookingRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalSurveys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TitleEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExternalUrl = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReportPdfPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsReportPublished = table.Column<bool>(type: "bit", nullable: false),
                    ReportPublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSurveys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalSurveys_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_SeasonId",
                table: "BookingRequests",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSurveys_SeasonId",
                table: "ExternalSurveys",
                column: "SeasonId");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingRequests_Activities_ActivityId",
                table: "BookingRequests",
                column: "ActivityId",
                principalTable: "Activities",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingRequests_Seasons_SeasonId",
                table: "BookingRequests",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingRequests_Activities_ActivityId",
                table: "BookingRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_BookingRequests_Seasons_SeasonId",
                table: "BookingRequests");

            migrationBuilder.DropTable(
                name: "ExternalSurveys");

            migrationBuilder.DropIndex(
                name: "IX_BookingRequests_SeasonId",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "PhysicalActivityComplianceRate",
                table: "KpiSubmissions");

            migrationBuilder.DropColumn(
                name: "LecturerContact",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "LecturerName",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "ProposedSubject",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "SeasonId",
                table: "BookingRequests");

            migrationBuilder.AlterColumn<int>(
                name: "ActivityId",
                table: "BookingRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BookingRequests_Activities_ActivityId",
                table: "BookingRequests",
                column: "ActivityId",
                principalTable: "Activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
