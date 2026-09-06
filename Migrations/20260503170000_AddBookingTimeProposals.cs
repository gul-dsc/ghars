using System;
using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    // Hand-written migration. Without these attributes EF does not recognise the class as part of the
    // chain, so it is skipped by `database update` and a fresh database never gets these changes.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260503170000_AddBookingTimeProposals")]
    public partial class AddBookingTimeProposals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "PartnerOrganizationId", table: "BookingRequests", type: "int", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "ConfirmedStartUtc", table: "BookingRequests", type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "ConfirmedEndUtc", table: "BookingRequests", type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<int>(name: "AcceptedProposedTimeOptionId", table: "BookingRequests", type: "int", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PartnerResponseNotes", table: "BookingRequests", type: "nvarchar(2000)", maxLength: 2000, nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingProposedTimeOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    BookingRequestId = table.Column<int>(type: "int", nullable: false),
                    ProposedStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProposedEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingProposedTimeOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingProposedTimeOptions_BookingRequests_BookingRequestId",
                        column: x => x.BookingRequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_BookingRequests_PartnerOrganizationId", table: "BookingRequests", column: "PartnerOrganizationId");
            migrationBuilder.CreateIndex(name: "IX_BookingProposedTimeOptions_BookingRequestId_IsActive", table: "BookingProposedTimeOptions", columns: new[] { "BookingRequestId", "IsActive" });
            migrationBuilder.AddForeignKey(
                name: "FK_BookingRequests_Organizations_PartnerOrganizationId",
                table: "BookingRequests",
                column: "PartnerOrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_BookingRequests_Organizations_PartnerOrganizationId", table: "BookingRequests");
            migrationBuilder.DropTable(name: "BookingProposedTimeOptions");
            migrationBuilder.DropIndex(name: "IX_BookingRequests_PartnerOrganizationId", table: "BookingRequests");
            migrationBuilder.DropColumn(name: "PartnerOrganizationId", table: "BookingRequests");
            migrationBuilder.DropColumn(name: "ConfirmedStartUtc", table: "BookingRequests");
            migrationBuilder.DropColumn(name: "ConfirmedEndUtc", table: "BookingRequests");
            migrationBuilder.DropColumn(name: "AcceptedProposedTimeOptionId", table: "BookingRequests");
            migrationBuilder.DropColumn(name: "PartnerResponseNotes", table: "BookingRequests");
        }
    }
}
