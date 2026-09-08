using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <summary>
    /// Makes the official Ghars participant satisfaction survey native.
    /// </summary>
    /// <remarks>
    /// <para>Additive apart from two deliberate widenings, both of which accept strictly more data than
    /// before and so cannot invalidate an existing row:</para>
    /// <list type="bullet">
    /// <item><c>Surveys.ActivityId</c> becomes nullable, because the official survey belongs to a season
    /// rather than to one activity.</item>
    /// <item><c>SurveyResponses.UserId</c> becomes nullable, because participants answer anonymously
    /// through a shared link. Its uniqueness index is rebuilt with a <c>WHERE UserId IS NOT NULL</c>
    /// filter — without that, SQL Server would treat the NULLs as equal and permit exactly one
    /// anonymous response per survey.</item>
    /// </list>
    /// <para>No table is dropped and no data is deleted. <c>ExternalSurveys</c> and
    /// <c>KpiSubmissions.SatisfactionExternalSurveyId</c> are untouched: they hold historical evidence
    /// for submissions approved under the previous external-survey model.</para>
    /// </remarks>
    public partial class AddNativeOfficialSatisfactionSurvey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Surveys_Activities_ActivityId",
                table: "Surveys");

            migrationBuilder.DropIndex(
                name: "IX_SurveyResponses_SurveyId_UserId",
                table: "SurveyResponses");

            migrationBuilder.AlterColumn<int>(
                name: "ActivityId",
                table: "Surveys",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "Surveys",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "Surveys",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicToken",
                table: "Surveys",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // SurveyPurpose.General = 1. The scaffolded default was 0, which is not a member of the
            // enum: every existing survey would have been left in a state that matches neither
            // classification and therefore appears in neither list.
            migrationBuilder.AddColumn<byte>(
                name: "Purpose",
                table: "Surveys",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            // Belt and braces for any row an earlier partial run may have left at 0.
            migrationBuilder.Sql("UPDATE dbo.Surveys SET Purpose = 1 WHERE Purpose = 0;");

            migrationBuilder.AddColumn<int>(
                name: "SeasonId",
                table: "Surveys",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "SurveyResponses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AddColumn<int>(
                name: "AgendaEntryId",
                table: "SurveyResponses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "SurveyResponses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_PublicToken",
                table: "Surveys",
                column: "PublicToken",
                unique: true,
                filter: "[PublicToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Surveys_OfficialSatisfaction_PerSeason",
                table: "Surveys",
                column: "SeasonId",
                unique: true,
                filter: "[Purpose] = 2 AND [SeasonId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_AgendaEntryId",
                table: "SurveyResponses",
                column: "AgendaEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_OrganizationId",
                table: "SurveyResponses",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_SurveyId_UserId",
                table: "SurveyResponses",
                columns: new[] { "SurveyId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SurveyResponses_AgendaEntries_AgendaEntryId",
                table: "SurveyResponses",
                column: "AgendaEntryId",
                principalTable: "AgendaEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SurveyResponses_Organizations_OrganizationId",
                table: "SurveyResponses",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Surveys_Activities_ActivityId",
                table: "Surveys",
                column: "ActivityId",
                principalTable: "Activities",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Surveys_Seasons_SeasonId",
                table: "Surveys",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SurveyResponses_AgendaEntries_AgendaEntryId",
                table: "SurveyResponses");

            migrationBuilder.DropForeignKey(
                name: "FK_SurveyResponses_Organizations_OrganizationId",
                table: "SurveyResponses");

            migrationBuilder.DropForeignKey(
                name: "FK_Surveys_Activities_ActivityId",
                table: "Surveys");

            migrationBuilder.DropForeignKey(
                name: "FK_Surveys_Seasons_SeasonId",
                table: "Surveys");

            migrationBuilder.DropIndex(
                name: "IX_Surveys_PublicToken",
                table: "Surveys");

            migrationBuilder.DropIndex(
                name: "UX_Surveys_OfficialSatisfaction_PerSeason",
                table: "Surveys");

            migrationBuilder.DropIndex(
                name: "IX_SurveyResponses_AgendaEntryId",
                table: "SurveyResponses");

            migrationBuilder.DropIndex(
                name: "IX_SurveyResponses_OrganizationId",
                table: "SurveyResponses");

            migrationBuilder.DropIndex(
                name: "IX_SurveyResponses_SurveyId_UserId",
                table: "SurveyResponses");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "PublicToken",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "SeasonId",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "AgendaEntryId",
                table: "SurveyResponses");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "SurveyResponses");

            migrationBuilder.AlterColumn<int>(
                name: "ActivityId",
                table: "Surveys",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "SurveyResponses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_SurveyId_UserId",
                table: "SurveyResponses",
                columns: new[] { "SurveyId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Surveys_Activities_ActivityId",
                table: "Surveys",
                column: "ActivityId",
                principalTable: "Activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
