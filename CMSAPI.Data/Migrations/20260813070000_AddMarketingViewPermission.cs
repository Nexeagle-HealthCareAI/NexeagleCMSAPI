using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMSAPI.Data.Migrations
{
    /// <inheritdoc />
    // Same shape as 20260808175150_AddHospitalsManagePermission -- one new CmsPermissions row,
    // hand-written (not `dotnet ef migrations add` output) for the same reason that one was:
    // this repo's migration history doesn't reflect several already-applied out-of-band
    // changes, so a real scaffold would try to redo/undo things that already exist.
    //
    // UNLIKE that migration, this one also grants the permission to the Administrator role
    // directly (CmsRolePermissions) -- hospitals.manage shipped granted to nobody, which meant
    // the Archive Hospital feature silently didn't work for anyone (including existing admins)
    // until a per-user override was added by hand and the affected user re-logged-in. Granting
    // Administrator by default here avoids repeating that.
    public partial class AddMarketingViewPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM CmsPermissions WHERE [Key] = 'marketing.view')
BEGIN
    INSERT INTO CmsPermissions (PermissionId, [Key], PageKey, Action, DisplayName, Category, SortOrder)
    VALUES (NEWID(), 'marketing.view', 'marketing', 'view', 'View Marketing Tab', 'Marketing', 110);
END

INSERT INTO CmsRolePermissions (RoleId, PermissionId)
SELECT 'B0000001-0000-0000-0000-000000000001', p.PermissionId
FROM CmsPermissions p
WHERE p.[Key] = 'marketing.view'
  AND NOT EXISTS (
      SELECT 1 FROM CmsRolePermissions rp
      WHERE rp.RoleId = 'B0000001-0000-0000-0000-000000000001' AND rp.PermissionId = p.PermissionId
  );
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM CmsRolePermissions WHERE PermissionId IN (SELECT PermissionId FROM CmsPermissions WHERE [Key] = 'marketing.view');
DELETE FROM CmsUserPermissions WHERE PermissionId IN (SELECT PermissionId FROM CmsPermissions WHERE [Key] = 'marketing.view');
DELETE FROM CmsPermissions WHERE [Key] = 'marketing.view';
");
        }
    }
}
