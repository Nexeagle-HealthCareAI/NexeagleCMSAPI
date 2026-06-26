using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data;

/// <summary>
/// EF context for CMSDatabase (identity + RBAC). The schema is owned by the
/// CMSDatabase SQL repo; EF is a runtime mapper only (no migrations). GUID keys
/// are assigned in application code, hence ValueGeneratedNever.
/// </summary>
public class CmsDbContext : DbContext
{
    public CmsDbContext(DbContextOptions<CmsDbContext> options) : base(options) { }

    public DbSet<CmsUser> CmsUsers => Set<CmsUser>();
    public DbSet<CmsRole> CmsRoles => Set<CmsRole>();
    public DbSet<CmsPermission> CmsPermissions => Set<CmsPermission>();
    public DbSet<CmsUserRole> CmsUserRoles => Set<CmsUserRole>();
    public DbSet<CmsRolePermission> CmsRolePermissions => Set<CmsRolePermission>();
    public DbSet<CmsUserPermission> CmsUserPermissions => Set<CmsUserPermission>();
    public DbSet<CmsRefreshToken> CmsRefreshTokens => Set<CmsRefreshToken>();
    public DbSet<CmsOtp> CmsOtps => Set<CmsOtp>();
    public DbSet<CmsPartner> CmsPartners => Set<CmsPartner>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CmsPartner>(e =>
        {
            e.ToTable("CmsPartners");
            e.HasKey(x => x.PartnerId);
            e.Property(x => x.PartnerId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.HighestQualification).HasMaxLength(100).IsRequired();
            e.Property(x => x.CurrentProfession).HasMaxLength(100).IsRequired();
            e.Property(x => x.PartnerCode).HasMaxLength(6).IsRequired();
            e.HasIndex(x => x.PartnerCode).IsUnique();
            e.Property(x => x.DashboardToken).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.DashboardToken).IsUnique();
        });

        b.Entity<CmsUser>(e =>
        {
            e.ToTable("CmsUsers");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(20);
            e.HasIndex(x => x.PhoneNumber).HasFilter("[PhoneNumber] IS NOT NULL");
            e.Property(x => x.LastLoginIp).HasMaxLength(64);
        });

        b.Entity<CmsRole>(e =>
        {
            e.ToTable("CmsRoles");
            e.HasKey(x => x.RoleId);
            e.Property(x => x.RoleId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(255);
        });

        b.Entity<CmsPermission>(e =>
        {
            e.ToTable("CmsPermissions");
            e.HasKey(x => x.PermissionId);
            e.Property(x => x.PermissionId).ValueGeneratedNever();
            e.Property(x => x.Key).HasColumnName("Key").HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.PageKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.Action).HasMaxLength(50).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100);
        });

        b.Entity<CmsUserRole>(e =>
        {
            e.ToTable("CmsUserRoles");
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId);
        });

        b.Entity<CmsRolePermission>(e =>
        {
            e.ToTable("CmsRolePermissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId);
            e.HasOne(x => x.Permission).WithMany(p => p.RolePermissions).HasForeignKey(x => x.PermissionId);
        });

        b.Entity<CmsUserPermission>(e =>
        {
            e.ToTable("CmsUserPermissions");
            e.HasKey(x => new { x.UserId, x.PermissionId });
            e.Property(x => x.Effect).HasMaxLength(10).IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.UserPermissions).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Permission).WithMany(p => p.UserPermissions).HasForeignKey(x => x.PermissionId);
        });

        b.Entity<CmsRefreshToken>(e =>
        {
            e.ToTable("CmsRefreshTokens");
            e.HasKey(x => x.TokenId);
            e.Property(x => x.TokenId).ValueGeneratedNever();
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<CmsOtp>(e =>
        {
            e.ToTable("CmsOtps");
            e.HasKey(x => x.OtpId);
            e.Property(x => x.OtpId).ValueGeneratedNever();
            e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.DeliveryTarget).HasMaxLength(256).IsRequired();
            e.Property(x => x.DeliveryMethod).HasMaxLength(10).IsRequired();
            e.Property(x => x.Purpose).HasMaxLength(20).IsRequired().HasDefaultValue("login");
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<SubscriptionPlan>(e =>
        {
            e.ToTable("SubscriptionPlans");
            e.HasKey(x => x.PlanId);
            e.Property(x => x.PlanId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });
    }
}
