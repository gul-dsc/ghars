using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    public partial class AddDetailedClubBookingWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudienceDetails",
                table: "BookingRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "BookingRequests",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPersonName",
                table: "BookingRequests",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "BookingRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherTargetAudience",
                table: "BookingRequests",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposedEndDateTime",
                table: "BookingRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposedStartDateTime",
                table: "BookingRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "RequestedActivityType",
                table: "BookingRequests",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "SpecialRequirements",
                table: "BookingRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "BookingRequests",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetAudienceCsv",
                table: "BookingRequests",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudienceDetails",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "ContactPersonName",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "OtherTargetAudience",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "ProposedEndDateTime",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "ProposedStartDateTime",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "RequestedActivityType",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "SpecialRequirements",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "TargetAudienceCsv",
                table: "BookingRequests");
        }
    }
}
