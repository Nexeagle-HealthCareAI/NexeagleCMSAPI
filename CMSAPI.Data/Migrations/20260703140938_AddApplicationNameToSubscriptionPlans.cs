using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMSAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationNameToSubscriptionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CmsOtps",
                columns: table => new
                {
                    OtpId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeliveryTarget = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DeliveryMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "login"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsOtps", x => x.OtpId);
                });

            migrationBuilder.CreateTable(
                name: "CmsPartners",
                columns: table => new
                {
                    PartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Age = table.Column<int>(type: "int", nullable: false),
                    Sex = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HighestQualification = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CurrentProfession = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pincode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartnerCode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    DashboardToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsPartners", x => x.PartnerId);
                });

            migrationBuilder.CreateTable(
                name: "CmsPermissions",
                columns: table => new
                {
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsPermissions", x => x.PermissionId);
                });

            migrationBuilder.CreateTable(
                name: "CmsRoles",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsSystemDefined = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsRoles", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "CmsUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    FailedLoginAttempts = table.Column<int>(type: "int", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsUsers", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ApplicationName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.PlanId);
                });

            migrationBuilder.CreateTable(
                name: "CmsRolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsRolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_CmsRolePermissions_CmsPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "CmsPermissions",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CmsRolePermissions_CmsRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "CmsRoles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CmsRefreshTokens",
                columns: table => new
                {
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsRefreshTokens", x => x.TokenId);
                    table.ForeignKey(
                        name: "FK_CmsRefreshTokens_CmsUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "CmsUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CmsUserPermissions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsUserPermissions", x => new { x.UserId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_CmsUserPermissions_CmsPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "CmsPermissions",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CmsUserPermissions_CmsUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "CmsUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CmsUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_CmsUserRoles_CmsRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "CmsRoles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CmsUserRoles_CmsUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "CmsUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CmsOtps_UserId",
                table: "CmsOtps",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPartners_DashboardToken",
                table: "CmsPartners",
                column: "DashboardToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsPartners_PartnerCode",
                table: "CmsPartners",
                column: "PartnerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsPermissions_Key",
                table: "CmsPermissions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsRefreshTokens_TokenHash",
                table: "CmsRefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsRefreshTokens_UserId",
                table: "CmsRefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsRolePermissions_PermissionId",
                table: "CmsRolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsRoles_Name",
                table: "CmsRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsUserPermissions_PermissionId",
                table: "CmsUserPermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsUserRoles_RoleId",
                table: "CmsUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsUsers_Email",
                table: "CmsUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmsUsers_PhoneNumber",
                table: "CmsUsers",
                column: "PhoneNumber",
                filter: "[PhoneNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CmsOtps");

            migrationBuilder.DropTable(
                name: "CmsPartners");

            migrationBuilder.DropTable(
                name: "CmsRefreshTokens");

            migrationBuilder.DropTable(
                name: "CmsRolePermissions");

            migrationBuilder.DropTable(
                name: "CmsUserPermissions");

            migrationBuilder.DropTable(
                name: "CmsUserRoles");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "CmsPermissions");

            migrationBuilder.DropTable(
                name: "CmsRoles");

            migrationBuilder.DropTable(
                name: "CmsUsers");
        }
    }
}
