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
    [Migration("20260505103000_AddGalleryAlbumLinksAndPdf")]
    public partial class AddGalleryAlbumLinksAndPdf : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "ActivityId", table: "MediaAlbums", type: "int", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "AlbumDate", table: "MediaAlbums", type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()");
            migrationBuilder.AddColumn<int>(name: "OrganizationId", table: "MediaAlbums", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "SeasonId", table: "MediaAlbums", type: "int", nullable: true);

            migrationBuilder.CreateIndex(name: "IX_MediaAlbums_ActivityId", table: "MediaAlbums", column: "ActivityId");
            migrationBuilder.CreateIndex(name: "IX_MediaAlbums_OrganizationId", table: "MediaAlbums", column: "OrganizationId");
            migrationBuilder.CreateIndex(name: "IX_MediaAlbums_SeasonId", table: "MediaAlbums", column: "SeasonId");

            migrationBuilder.AddForeignKey(name: "FK_MediaAlbums_Activities_ActivityId", table: "MediaAlbums", column: "ActivityId", principalTable: "Activities", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            migrationBuilder.AddForeignKey(name: "FK_MediaAlbums_Organizations_OrganizationId", table: "MediaAlbums", column: "OrganizationId", principalTable: "Organizations", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            migrationBuilder.AddForeignKey(name: "FK_MediaAlbums_Seasons_SeasonId", table: "MediaAlbums", column: "SeasonId", principalTable: "Seasons", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_MediaAlbums_Activities_ActivityId", table: "MediaAlbums");
            migrationBuilder.DropForeignKey(name: "FK_MediaAlbums_Organizations_OrganizationId", table: "MediaAlbums");
            migrationBuilder.DropForeignKey(name: "FK_MediaAlbums_Seasons_SeasonId", table: "MediaAlbums");
            migrationBuilder.DropIndex(name: "IX_MediaAlbums_ActivityId", table: "MediaAlbums");
            migrationBuilder.DropIndex(name: "IX_MediaAlbums_OrganizationId", table: "MediaAlbums");
            migrationBuilder.DropIndex(name: "IX_MediaAlbums_SeasonId", table: "MediaAlbums");
            migrationBuilder.DropColumn(name: "ActivityId", table: "MediaAlbums");
            migrationBuilder.DropColumn(name: "AlbumDate", table: "MediaAlbums");
            migrationBuilder.DropColumn(name: "OrganizationId", table: "MediaAlbums");
            migrationBuilder.DropColumn(name: "SeasonId", table: "MediaAlbums");
        }
    }
}
