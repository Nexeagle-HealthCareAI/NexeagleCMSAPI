using CMSAPI.Domain.Entities;
using CMSAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data;

/// <summary>
/// EF Core DbContext for CMSDatabase (identity + RBAC + CRM).
///
/// ⚠ SCHEMA OWNERSHIP: The schema is exclusively owned by the CMSDatabase SQL
/// repository (CMSDatabase/schema/*.sql). EF is used purely as a runtime ORM
/// mapper — it NEVER owns or manages the database schema.
///
/// ❌ DO NOT run `dotnet ef migrations add` on this context.
/// ❌ DO NOT call context.Database.Migrate() or EnsureCreated() at startup.
///
/// To make a schema change, add a new numbered SQL script to CMSDatabase/schema/
/// and apply it to the server manually or via the deployment pipeline.
///
/// GUID PKs are assigned in application code, hence ValueGeneratedNever on all
/// identity-key properties.
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
    public DbSet<EasyHmsSubscriptionPlan> EasyHmsSubscriptionPlans => Set<EasyHmsSubscriptionPlan>();
    public DbSet<ReferralCodeType> ReferralCodeTypes => Set<ReferralCodeType>();
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<MigrationBatch> MigrationBatches => Set<MigrationBatch>();
    public DbSet<MigrationBatchRow> MigrationBatchRows => Set<MigrationBatchRow>();
    public DbSet<MigrationDoctorMap> MigrationDoctorMaps => Set<MigrationDoctorMap>();
    public DbSet<CmsSalesLead> CmsSalesLeads => Set<CmsSalesLead>();
    public DbSet<CmsSalesLeadFollowUp> CmsSalesLeadFollowUps => Set<CmsSalesLeadFollowUp>();
    public DbSet<CmsCampaign> CmsCampaigns => Set<CmsCampaign>();
    public DbSet<CmsSocialPost> CmsSocialPosts => Set<CmsSocialPost>();
    public DbSet<CmsWhatsappTemplate> CmsWhatsappTemplates => Set<CmsWhatsappTemplate>();
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
            e.Property(x => x.Code).HasMaxLength(6).IsRequired();
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

        b.Entity<EasyHmsSubscriptionPlan>(e =>
        {
            e.ToTable("EasyHmsSubscriptionPlans");
            e.HasKey(x => x.PlanId);
            e.Property(x => x.PlanId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        b.Entity<ReferralCodeType>(e =>
        {
            e.ToTable("ReferralCodeTypes");
            e.HasKey(x => x.ReferralCodeTypeId);
            e.Property(x => x.ReferralCodeTypeId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.RewardKind).HasMaxLength(20).IsRequired();
            e.Property(x => x.RewardValue).HasColumnType("decimal(10,2)");
        });

        b.Entity<ReferralCode>(e =>
        {
            e.ToTable("ReferralCodes");
            e.HasKey(x => x.ReferralCodeId);
            e.Property(x => x.ReferralCodeId).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.ReferralCodeType)
                .WithMany()
                .HasForeignKey(x => x.ReferralCodeTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MigrationBatch>(e =>
        {
            e.ToTable("MigrationBatches");
            e.HasKey(x => x.BatchId);
            e.Property(x => x.BatchId).ValueGeneratedNever();
            e.Property(x => x.DataType).HasMaxLength(30).IsRequired();
            e.Property(x => x.SourceFileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
        });

        b.Entity<MigrationBatchRow>(e =>
        {
            e.ToTable("MigrationBatchRows");
            e.HasKey(x => x.RowId);
            e.Property(x => x.RowId).ValueGeneratedNever();
            e.Property(x => x.RawDataJson).IsRequired();
            e.Property(x => x.ResolvedPatientId).HasMaxLength(20);
            e.Property(x => x.RowStatus).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Batch)
                .WithMany()
                .HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MigrationDoctorMap>(e =>
        {
            e.ToTable("MigrationDoctorMap");
            e.HasKey(x => x.MapId);
            e.Property(x => x.MapId).ValueGeneratedNever();
            e.Property(x => x.SourceDoctorName).HasMaxLength(200).IsRequired();
            e.Property(x => x.SourceDepartment).HasMaxLength(200);
            e.Property(x => x.MappedDoctorName).HasMaxLength(200);
            e.HasOne(x => x.Batch)
                .WithMany()
                .HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CmsSalesLead>(e =>
        {
            e.ToTable("CmsSalesLeads");
            e.HasKey(x => x.LeadId);
            e.Property(x => x.LeadId).ValueGeneratedNever();
            e.Property(x => x.HospitalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContactName).HasMaxLength(150);
            e.Property(x => x.Mobile).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.State).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(50).IsRequired();
            e.Property(x => x.Stage).HasMaxLength(50).IsRequired();
            e.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.AssignedTo)
                .WithMany()
                .HasForeignKey(x => x.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.FollowUps)
                .WithOne(f => f.Lead)
                .HasForeignKey(f => f.LeadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CmsSalesLeadFollowUp>(e =>
        {
            e.ToTable("CmsSalesLeadFollowUps");
            e.HasKey(x => x.FollowUpId);
            e.Property(x => x.FollowUpId).ValueGeneratedNever();
            e.Property(x => x.ActivityType).HasMaxLength(50).IsRequired();
            e.Property(x => x.AuthorName).HasMaxLength(150);
            e.Property(x => x.Notes).IsRequired();
        });

        b.Entity<CmsCampaign>(e =>
        {
            e.ToTable("CmsCampaigns");
            e.HasKey(x => x.CampaignId);
            e.Property(x => x.CampaignId).ValueGeneratedNever();
        });

        b.Entity<CmsSocialPost>(e =>
        {
            e.ToTable("CmsSocialPosts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        b.Entity<CmsWhatsappTemplate>(e =>
        {
            e.ToTable("CmsWhatsappTemplates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });
    }
}
