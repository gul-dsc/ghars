using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerAvailabilityCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartnerAvailabilitySlotId",
                table: "BookingRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PartnerAvailabilitySlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartnerOrganizationId = table.Column<int>(type: "int", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    HeldByBookingRequestId = table.Column<int>(type: "int", nullable: true),
                    NotesEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NotesAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LocationEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LocationAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerAvailabilitySlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerAvailabilitySlots_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PartnerAvailabilitySlots_Organizations_PartnerOrganizationId",
                        column: x => x.PartnerOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PartnerAvailabilitySlots_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_PartnerAvailabilitySlotId",
                table: "BookingRequests",
                column: "PartnerAvailabilitySlotId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerAvailabilitySlots_ActivityId",
                table: "PartnerAvailabilitySlots",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerAvailabilitySlots_Partner_Date_Status",
                table: "PartnerAvailabilitySlots",
                columns: new[] { "PartnerOrganizationId", "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerAvailabilitySlots_Season_Date_Status",
                table: "PartnerAvailabilitySlots",
                columns: new[] { "SeasonId", "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_PartnerAvailabilitySlots_ActiveUnique",
                table: "PartnerAvailabilitySlots",
                columns: new[] { "PartnerOrganizationId", "Date", "StartTime", "EndTime" },
                unique: true,
                filter: "[Status] <> 5");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingRequests_PartnerAvailabilitySlots_PartnerAvailabilitySlotId",
                table: "BookingRequests",
                column: "PartnerAvailabilitySlotId",
                principalTable: "PartnerAvailabilitySlots",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingRequests_PartnerAvailabilitySlots_PartnerAvailabilitySlotId",
                table: "BookingRequests");

            migrationBuilder.DropTable(
                name: "PartnerAvailabilitySlots");

            migrationBuilder.DropIndex(
                name: "IX_BookingRequests_PartnerAvailabilitySlotId",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "PartnerAvailabilitySlotId",
                table: "BookingRequests");
        }
    }
}
