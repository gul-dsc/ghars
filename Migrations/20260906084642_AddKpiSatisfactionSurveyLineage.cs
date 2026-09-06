using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddKpiSatisfactionSurveyLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SatisfactionExternalSurveyId",
                table: "KpiSubmissions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KpiSubmissions_SatisfactionExternalSurveyId",
                table: "KpiSubmissions",
                column: "SatisfactionExternalSurveyId");

            migrationBuilder.AddForeignKey(
                name: "FK_KpiSubmissions_ExternalSurveys_SatisfactionExternalSurveyId",
                table: "KpiSubmissions",
                column: "SatisfactionExternalSurveyId",
                principalTable: "ExternalSurveys",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KpiSubmissions_ExternalSurveys_SatisfactionExternalSurveyId",
                table: "KpiSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_KpiSubmissions_SatisfactionExternalSurveyId",
                table: "KpiSubmissions");

            migrationBuilder.DropColumn(
                name: "SatisfactionExternalSurveyId",
                table: "KpiSubmissions");
        }
    }
}
