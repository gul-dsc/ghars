using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    // Hand-written migration. Without these attributes EF does not recognise the class as part of the
    // chain, so it is skipped by `database update` and a fresh database never gets these changes.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260503173000_AddActivityPartnerOrganization")]
    public partial class AddActivityPartnerOrganization : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartnerOrganizationId",
                table: "Activities",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_PartnerOrganizationId",
                table: "Activities",
                column: "PartnerOrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_Organizations_PartnerOrganizationId",
                table: "Activities",
                column: "PartnerOrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Activities_Organizations_PartnerOrganizationId",
                table: "Activities");

            migrationBuilder.DropIndex(
                name: "IX_Activities_PartnerOrganizationId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PartnerOrganizationId",
                table: "Activities");
        }
    }
}
