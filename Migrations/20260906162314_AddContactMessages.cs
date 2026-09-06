using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddContactMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OrganizationName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Topic = table.Column<byte>(type: "tinyint", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    AdminNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HandledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HandledByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SubmittedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SubmittedFromIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubmittedCulture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactMessages_Status_CreatedAtUtc",
                table: "ContactMessages",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactMessages");
        }
    }
}
