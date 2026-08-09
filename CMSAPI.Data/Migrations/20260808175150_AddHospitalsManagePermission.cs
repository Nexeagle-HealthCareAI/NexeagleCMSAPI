using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMSAPI.Data.Migrations
{
    /// <inheritdoc />
    // NOTE: `dotnet ef migrations add` also auto-detected CmsOtps.CodeHash->Code and a new
    // EasyHmsSubscriptionPlans table as model drift relative to the last recorded migration
    // (20260703140938). Those are NOT part of this change — they were applied directly against
    // the database out-of-band at some point (this repo has no committed seed/migration history
    // for several existing changes; see CmsPermissions itself, which has never had a migration
    // either). Re-running those scaffolded operations against the real database would fail (drop
    // a column that's already gone, create a table that already exists), so Up()/Down() below are
    // hand-written to contain ONLY the actual change intended here — inserting one new
    // CmsPermissions row. The auto-updated CmsDbContextModelSnapshot.cs is left as generated,
    // since it correctly describes the current C# model either way.
    public partial class AddHospitalsManagePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM CmsPermissions WHERE [Key] = 'hospitals.manage')
BEGIN
    INSERT INTO CmsPermissions (PermissionId, [Key], PageKey, Action, DisplayName, Category, SortOrder)
    VALUES (NEWID(), 'hospitals.manage', 'hospitals', 'manage', 'Archive / Restore Hospitals', 'Hospitals', 100);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM CmsRolePermissions WHERE PermissionId IN (SELECT PermissionId FROM CmsPermissions WHERE [Key] = 'hospitals.manage');
DELETE FROM CmsUserPermissions WHERE PermissionId IN (SELECT PermissionId FROM CmsPermissions WHERE [Key] = 'hospitals.manage');
DELETE FROM CmsPermissions WHERE [Key] = 'hospitals.manage';
");
        }
    }
}
