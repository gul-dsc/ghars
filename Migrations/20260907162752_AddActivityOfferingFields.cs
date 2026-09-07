using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityOfferingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Activities_PartnerOrganizationId",
                table: "Activities");

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableFromUtc",
                table: "Activities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableUntilUtc",
                table: "Activities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherTargetAudience",
                table: "Activities",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetAudienceCsv",
                table: "Activities",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_PartnerOrganizationId_Status_Type",
                table: "Activities",
                columns: new[] { "PartnerOrganizationId", "Status", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Activities_PartnerOrganizationId_Status_Type",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "AvailableFromUtc",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "AvailableUntilUtc",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "OtherTargetAudience",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "TargetAudienceCsv",
                table: "Activities");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_PartnerOrganizationId",
                table: "Activities",
                column: "PartnerOrganizationId");
        }
    }
}
