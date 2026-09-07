using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferingApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "ApprovalStatus",
                table: "Activities",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "Activities",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "Activities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByUserId",
                table: "Activities",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "Activities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedByUserId",
                table: "Activities",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ApprovalStatus_SubmittedAtUtc",
                table: "Activities",
                columns: new[] { "ApprovalStatus", "SubmittedAtUtc" });

            // ---------------------------------------------------------------- legacy backfill
            //
            // Every existing partner offering predates this workflow, so each needs a state that
            // preserves what it means today. The rule is derived from the row's existing
            // ActivityStatus and nothing else — no historical row is inspected, guessed at, or
            // rejected:
            //
            //   partner-owned + Published (2) -> Approved (4)     grandfathered; stays visible
            //   partner-owned + Draft (1)     -> Draft (1)        stays invisible and editable
            //   partner-owned + Closed (3)    -> Unpublished (6)  already withdrawn; stays withdrawn
            //   partner-owned + Cancelled (4) -> Unpublished (6)  as above
            //   DSC-created (no owning entity) -> left NULL       outside this workflow entirely
            //
            // Unpublished is used rather than Rejected for Closed/Cancelled because those rows were
            // withdrawn, not refused: nothing in their history says a reviewer ever refused them.
            //
            // Guarded by IS NULL so re-running against a partly-migrated database cannot overwrite a
            // state a reviewer has since set.
            migrationBuilder.Sql(@"
UPDATE dbo.Activities SET ApprovalStatus = 4
 WHERE ApprovalStatus IS NULL AND PartnerOrganizationId IS NOT NULL AND Status = 2;

UPDATE dbo.Activities SET ApprovalStatus = 1
 WHERE ApprovalStatus IS NULL AND PartnerOrganizationId IS NOT NULL AND Status = 1;

UPDATE dbo.Activities SET ApprovalStatus = 6
 WHERE ApprovalStatus IS NULL AND PartnerOrganizationId IS NOT NULL AND Status IN (3, 4);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Activities_ApprovalStatus_SubmittedAtUtc",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "Activities");
        }
    }
}
