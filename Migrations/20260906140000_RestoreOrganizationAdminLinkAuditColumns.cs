using System;
using GharsPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GharsPlatform.Migrations
{
    /// <summary>
    /// Repairs a gap in the migration chain.
    ///
    /// OrganizationAdminLink inherits CreatedAtUtc/CreatedByUserId from AuditableEntity and the model
    /// snapshot has always declared both columns, but the migration that originally created them
    /// ("20260505103249_new one") is recorded in the development database's __EFMigrationsHistory while
    /// its source file is absent from the repository. The result was that a database built purely from
    /// source control lacked the two columns, and because OrganizationAdminLinks backs the organization
    /// scoping check on nearly every authenticated request, that database failed at runtime rather than
    /// at deployment time.
    ///
    /// The model already matches the snapshot, so `migrations add` would produce an empty migration.
    /// This one is therefore hand-written, and each statement is guarded so it is a no-op on databases
    /// that already carry the columns.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260906140000_RestoreOrganizationAdminLinkAuditColumns")]
    public partial class RestoreOrganizationAdminLinkAuditColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('OrganizationAdminLinks', 'CreatedAtUtc') IS NULL
    ALTER TABLE [OrganizationAdminLinks]
        ADD [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_OrganizationAdminLinks_CreatedAtUtc] DEFAULT '0001-01-01T00:00:00.0000000';");

            migrationBuilder.Sql(@"
IF COL_LENGTH('OrganizationAdminLinks', 'CreatedByUserId') IS NULL
    ALTER TABLE [OrganizationAdminLinks] ADD [CreatedByUserId] nvarchar(max) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_OrganizationAdminLinks_CreatedAtUtc')
    ALTER TABLE [OrganizationAdminLinks] DROP CONSTRAINT [DF_OrganizationAdminLinks_CreatedAtUtc];");

            migrationBuilder.Sql(@"
IF COL_LENGTH('OrganizationAdminLinks', 'CreatedAtUtc') IS NOT NULL
    ALTER TABLE [OrganizationAdminLinks] DROP COLUMN [CreatedAtUtc];");

            migrationBuilder.Sql(@"
IF COL_LENGTH('OrganizationAdminLinks', 'CreatedByUserId') IS NOT NULL
    ALTER TABLE [OrganizationAdminLinks] DROP COLUMN [CreatedByUserId];");
        }
    }
}
