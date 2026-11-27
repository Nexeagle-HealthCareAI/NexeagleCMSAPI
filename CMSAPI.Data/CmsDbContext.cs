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
    public DbSet<CmsUserPhoto> CmsUserPhotos => Set<CmsUserPhoto>();
    public DbSet<CmsRefreshToken> CmsRefreshTokens => Set<CmsRefreshToken>();
    public DbSet<CmsOtp> CmsOtps => Set<CmsOtp>();
    public DbSet<CmsPartner> CmsPartners => Set<CmsPartner>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<SubscriptionAuditLog> SubscriptionAuditLogs => Set<SubscriptionAuditLog>();
    public DbSet<ModuleCharge> ModuleCharges => Set<ModuleCharge>();
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
    // WhatsApp marketing (35_CMSDatabase_WhatsApp_Marketing.sql)
    public DbSet<WaContact> WaContacts => Set<WaContact>();
    public DbSet<WaMessage> WaMessages => Set<WaMessage>();
    public DbSet<WaNote> WaNotes => Set<WaNote>();
    public DbSet<WaTemplate> WaTemplates => Set<WaTemplate>();
    public DbSet<WaBroadcast> WaBroadcasts => Set<WaBroadcast>();
    public DbSet<WaBroadcastRecipient> WaBroadcastRecipients => Set<WaBroadcastRecipient>();
    public DbSet<WaRate> WaRates => Set<WaRate>();
    public DbSet<WaSettings> WaSettings => Set<WaSettings>();
    public DbSet<WaConnection> WaConnections => Set<WaConnection>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<SocialAccountDaily> SocialAccountDaily => Set<SocialAccountDaily>();
    public DbSet<SocialPost> SocialPosts => Set<SocialPost>();
    public DbSet<SocialPostStat> SocialPostStats => Set<SocialPostStat>();
    public DbSet<SocialDraft> SocialDrafts => Set<SocialDraft>();
    public DbSet<SocialQueueSlot> SocialQueueSlots => Set<SocialQueueSlot>();
    public DbSet<SocialComment> SocialComments => Set<SocialComment>();
    public DbSet<SocialContact> SocialContacts => Set<SocialContact>();
    public DbSet<SocialMessage> SocialMessages => Set<SocialMessage>();
    public DbSet<SocialNote> SocialNotes => Set<SocialNote>();
    public DbSet<SocialLead> SocialLeads => Set<SocialLead>();
    public DbSet<SocialLeadForm> SocialLeadForms => Set<SocialLeadForm>();
    public DbSet<SocialAdStat> SocialAdStats => Set<SocialAdStat>();
    public DbSet<WorkProject> WorkProjects => Set<WorkProject>();
    public DbSet<WorkStatus> WorkStatuses => Set<WorkStatus>();
    public DbSet<WorkProjectMember> WorkProjectMembers => Set<WorkProjectMember>();
    public DbSet<WorkProjectAccessLog> WorkProjectAccessLogs => Set<WorkProjectAccessLog>();
    public DbSet<WorkPresence> WorkPresences => Set<WorkPresence>();
    public DbSet<WorkSessionLog> WorkSessionLogs => Set<WorkSessionLog>();
    public DbSet<DocComment> DocComments => Set<DocComment>();
    public DbSet<DocCommentMention> DocCommentMentions => Set<DocCommentMention>();
    public DbSet<DocNotification> DocNotifications => Set<DocNotification>();
    public DbSet<WorkChatChannel> WorkChatChannels => Set<WorkChatChannel>();
    public DbSet<WorkChatMember> WorkChatMembers => Set<WorkChatMember>();
    public DbSet<WorkChatMessage> WorkChatMessages => Set<WorkChatMessage>();
    public DbSet<WorkChatMention> WorkChatMentions => Set<WorkChatMention>();
    public DbSet<WorkNotification> WorkNotifications => Set<WorkNotification>();
    public DbSet<WorkSprint> WorkSprints => Set<WorkSprint>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkItemComment> WorkItemComments => Set<WorkItemComment>();
    public DbSet<WorkItemMention> WorkItemMentions => Set<WorkItemMention>();
    public DbSet<WorkItemNotification> WorkItemNotifications => Set<WorkItemNotification>();
    public DbSet<WorkItemHistory> WorkItemHistories => Set<WorkItemHistory>();
    public DbSet<DocFolder> DocFolders => Set<DocFolder>();
    public DbSet<DocFolderAccess> DocFolderAccesses => Set<DocFolderAccess>();
    public DbSet<WorkDocument> WorkDocuments => Set<WorkDocument>();
    public DbSet<DocVersion> DocVersions => Set<DocVersion>();
    public DbSet<DocAccessLog> DocAccessLogs => Set<DocAccessLog>();
    public DbSet<WorkItemDocument> WorkItemDocuments => Set<WorkItemDocument>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        // ── Daily Work Board, Docs tab (27_CMSDatabase_WorkDocs.sql) ──
        b.Entity<DocFolder>(e =>
        {
            e.ToTable("DocFolders");
            e.HasKey(x => x.FolderId);
            e.Property(x => x.FolderId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Color).HasMaxLength(20);
        });

        b.Entity<DocFolderAccess>(e =>
        {
            e.ToTable("DocFolderAccess");
            e.HasKey(x => new { x.FolderId, x.UserId });
        });

        b.Entity<WorkDocument>(e =>
        {
            e.ToTable("Documents");
            e.HasKey(x => x.DocumentId);
            e.Property(x => x.DocumentId).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.Property(x => x.StorageKey).HasMaxLength(400);
            e.Property(x => x.Tags).HasMaxLength(400);
        });

        b.Entity<DocVersion>(e =>
        {
            e.ToTable("DocVersions");
            e.HasKey(x => x.VersionId);
            e.Property(x => x.VersionId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.Property(x => x.StorageKey).HasMaxLength(400);
        });

        b.Entity<DocAccessLog>(e =>
        {
            e.ToTable("DocAccessLog");
            e.HasKey(x => x.LogId);
            e.Property(x => x.LogId).ValueGeneratedNever();
            e.Property(x => x.Action).HasMaxLength(30).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(300);
        });

        b.Entity<WorkItemDocument>(e =>
        {
            e.ToTable("WorkItemDocuments");
            e.HasKey(x => new { x.WorkItemId, x.DocumentId });
        });

        // ── Daily Work Board (26_CMSDatabase_WorkBoard.sql) ──
        b.Entity<WorkProject>(e =>
        {
            e.ToTable("WorkProjects");
            e.HasKey(x => x.ProjectId);
            e.Property(x => x.ProjectId).ValueGeneratedNever();
            e.Property(x => x.Key).HasMaxLength(10).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
        });

        // Board members (28_CMSDatabase_WorkBoardAccess.sql)
        b.Entity<WorkProjectMember>(e =>
        {
            e.ToTable("WorkProjectMembers");
            e.HasKey(x => new { x.ProjectId, x.UserId });
        });

        b.Entity<WorkProjectAccessLog>(e =>
        {
            e.ToTable("WorkProjectAccessLog");
            e.HasKey(x => x.LogId);
            e.Property(x => x.LogId).ValueGeneratedNever();
            e.Property(x => x.Action).HasMaxLength(20).IsRequired();
        });

        // Comments on documents (31_CMSDatabase_DocComments.sql)
        b.Entity<DocComment>(e =>
        {
            e.ToTable("DocComments");
            e.HasKey(x => x.CommentId);
            e.Property(x => x.CommentId).ValueGeneratedNever();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Quote).HasMaxLength(500);
        });

        b.Entity<DocCommentMention>(e =>
        {
            e.ToTable("DocCommentMentions");
            e.HasKey(x => new { x.CommentId, x.UserId });
        });

        // Tagging in work items (33_CMSDatabase_WorkItemMentions.sql)
        b.Entity<WorkItemMention>(e =>
        {
            e.ToTable("WorkItemMentions");
            e.HasKey(x => x.MentionId);
            e.Property(x => x.MentionId).ValueGeneratedNever();
            e.Property(x => x.Place).HasMaxLength(12).IsRequired();
        });

        b.Entity<WorkItemNotification>(e =>
        {
            e.ToTable("WorkItemNotifications");
            e.HasKey(x => x.NotificationId);
            e.Property(x => x.NotificationId).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(12).IsRequired();
            e.Property(x => x.Place).HasMaxLength(12).IsRequired();
            e.Property(x => x.Excerpt).HasMaxLength(300);
        });

        b.Entity<DocNotification>(e =>
        {
            e.ToTable("DocNotifications");
            e.HasKey(x => x.NotificationId);
            e.Property(x => x.NotificationId).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(12).IsRequired();
        });

        // Team chat (30_CMSDatabase_WorkChat.sql)
        b.Entity<WorkChatChannel>(e =>
        {
            e.ToTable("WorkChatChannels");
            e.HasKey(x => x.ChannelId);
            e.Property(x => x.ChannelId).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.DirectKey).HasMaxLength(80);
        });

        b.Entity<WorkChatMember>(e =>
        {
            e.ToTable("WorkChatMembers");
            e.HasKey(x => new { x.ChannelId, x.UserId });
        });

        b.Entity<WorkChatMessage>(e =>
        {
            e.ToTable("WorkChatMessages");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).ValueGeneratedNever();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        });

        b.Entity<WorkChatMention>(e =>
        {
            e.ToTable("WorkChatMentions");
            e.HasKey(x => new { x.MessageId, x.UserId });
        });

        b.Entity<WorkNotification>(e =>
        {
            e.ToTable("WorkNotifications");
            e.HasKey(x => x.NotificationId);
            e.Property(x => x.NotificationId).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(12).IsRequired();
        });

        // Activity (29_CMSDatabase_WorkActivity.sql)
        b.Entity<WorkPresence>(e =>
        {
            e.ToTable("WorkPresence");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.LastIp).HasMaxLength(64);
            e.Property(x => x.PreviousIp).HasMaxLength(64);
            e.Property(x => x.LastUserAgent).HasMaxLength(300);
        });

        b.Entity<WorkSessionLog>(e =>
        {
            e.ToTable("WorkSessionLog");
            e.HasKey(x => x.LogId);
            e.Property(x => x.LogId).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
        });

        b.Entity<WorkStatus>(e =>
        {
            e.ToTable("WorkStatuses");
            e.HasKey(x => x.StatusId);
            e.Property(x => x.StatusId).ValueGeneratedNever();
            e.Property(x => x.Key).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.Property(x => x.Category).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.Key }).IsUnique();
        });

        b.Entity<WorkSprint>(e =>
        {
            e.ToTable("WorkSprints");
            e.HasKey(x => x.SprintId);
            e.Property(x => x.SprintId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Goal).HasMaxLength(500);
            e.Property(x => x.State).HasMaxLength(20).IsRequired();
            e.Property(x => x.StartDate).HasColumnType("date");
            e.Property(x => x.EndDate).HasColumnType("date");
        });

        b.Entity<WorkItem>(e =>
        {
            e.ToTable("WorkItems");
            e.HasKey(x => x.WorkItemId);
            e.Property(x => x.WorkItemId).ValueGeneratedNever();
            e.Property(x => x.ItemType).HasMaxLength(20).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasMaxLength(40).IsRequired();
            e.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            e.Property(x => x.StoryPoints).HasColumnType("decimal(5,1)");
            e.Property(x => x.Labels).HasMaxLength(400);
            e.Property(x => x.StartDate).HasColumnType("date");
            e.Property(x => x.DueDate).HasColumnType("date");
            e.Property(x => x.BlockedReason).HasMaxLength(300);
            e.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
        });

        b.Entity<WorkItemComment>(e =>
        {
            e.ToTable("WorkItemComments");
            e.HasKey(x => x.CommentId);
            e.Property(x => x.CommentId).ValueGeneratedNever();
            e.Property(x => x.Body).IsRequired();
        });

        b.Entity<WorkItemHistory>(e =>
        {
            e.ToTable("WorkItemHistory");
            e.HasKey(x => x.HistoryId);
            e.Property(x => x.HistoryId).ValueGeneratedNever();
            e.Property(x => x.Field).HasMaxLength(40).IsRequired();
            e.Property(x => x.OldValue).HasMaxLength(400);
            e.Property(x => x.NewValue).HasMaxLength(400);
        });

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
            e.Property(x => x.JobTitle).HasMaxLength(100);
        });

        b.Entity<CmsUserPhoto>(e =>
        {
            e.ToTable("CmsUserPhotos");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.ContentType).HasMaxLength(40).IsRequired();
            e.Property(x => x.Data).IsRequired();
            e.HasOne<CmsUser>().WithOne().HasForeignKey<CmsUserPhoto>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
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

        b.Entity<ModuleCharge>(e =>
        {
            e.ToTable("ModuleCharges");
            e.HasKey(x => x.ModuleChargeId);
            e.Property(x => x.ModuleChargeId).ValueGeneratedNever();
            e.Property(x => x.ApplicationName).HasMaxLength(50).IsRequired();
            e.Property(x => x.ModuleKey).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.ApplicationName, x.ModuleKey }).IsUnique();
        });

        b.Entity<SubscriptionAuditLog>(e =>
        {
            e.ToTable("SubscriptionAuditLogs");
            e.HasKey(x => x.AuditId);
            e.Property(x => x.AuditId).ValueGeneratedNever();
            e.Property(x => x.Platform).HasMaxLength(50).IsRequired();
            e.Property(x => x.Action).HasMaxLength(50).IsRequired();
            e.Property(x => x.ActorEmail).HasMaxLength(256);
            e.Property(x => x.OldValue).HasMaxLength(1000);
            e.Property(x => x.NewValue).HasMaxLength(1000);
            e.HasIndex(x => new { x.Platform, x.HospitalId });
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

        // WhatsApp marketing (35_CMSDatabase_WhatsApp_Marketing.sql)
        b.Entity<WaContact>(e =>
        {
            e.ToTable("WaContacts");
            e.HasKey(x => x.ContactId);
            e.Property(x => x.ContactId).ValueGeneratedNever();
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.ProfileName).HasMaxLength(150);
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.Property(x => x.OptIn).HasMaxLength(10).IsRequired();
            e.Property(x => x.OptInSource).HasMaxLength(200);
            e.Property(x => x.OptOutReason).HasMaxLength(200);
            e.Property(x => x.Tags).HasMaxLength(400);
            e.Property(x => x.LastMessagePreview).HasMaxLength(200);
            e.Property(x => x.ChatStatus).HasMaxLength(10).IsRequired();
        });
        b.Entity<WaMessage>(e =>
        {
            e.ToTable("WaMessages");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).ValueGeneratedNever();
            e.Property(x => x.Direction).HasMaxLength(3).IsRequired();
            e.Property(x => x.WamId).HasMaxLength(200);
            e.HasIndex(x => x.WamId).IsUnique().HasFilter("[WamId] IS NOT NULL");
            e.Property(x => x.Type).HasMaxLength(20).IsRequired();
            e.Property(x => x.MediaId).HasMaxLength(100);
            e.Property(x => x.MediaMime).HasMaxLength(100);
            e.Property(x => x.MediaName).HasMaxLength(260);
            e.Property(x => x.TemplateName).HasMaxLength(512);
            e.Property(x => x.TemplateLanguage).HasMaxLength(15);
            e.Property(x => x.ReplyToWamId).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(12).IsRequired();
            e.Property(x => x.ErrorText).HasMaxLength(400);
            e.Property(x => x.PricingCategory).HasMaxLength(30);
            e.Property(x => x.Cost).HasPrecision(12, 4);
            e.Property(x => x.Currency).HasMaxLength(3);
        });
        b.Entity<WaNote>(e =>
        {
            e.ToTable("WaNotes");
            e.HasKey(x => x.NoteId);
            e.Property(x => x.NoteId).ValueGeneratedNever();
            e.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        });
        b.Entity<WaTemplate>(e =>
        {
            e.ToTable("WaTemplates");
            e.HasKey(x => x.TemplateId);
            e.Property(x => x.TemplateId).ValueGeneratedNever();
            e.Property(x => x.MetaTemplateId).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(512).IsRequired();
            e.Property(x => x.Language).HasMaxLength(15).IsRequired();
            e.HasIndex(x => new { x.Name, x.Language }).IsUnique();
            e.Property(x => x.Category).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.RejectedReason).HasMaxLength(400);
            e.Property(x => x.QualityScore).HasMaxLength(20);
            e.Property(x => x.ParameterFormat).HasMaxLength(12).IsRequired();
            e.Property(x => x.Components).IsRequired();
            e.Property(x => x.HeaderMediaKey).HasMaxLength(300);
        });
        b.Entity<WaBroadcast>(e =>
        {
            e.ToTable("WaBroadcasts");
            e.HasKey(x => x.BroadcastId);
            e.Property(x => x.BroadcastId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.HeaderMediaUrl).HasMaxLength(1000);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.PausedReason).HasMaxLength(200);
            e.Property(x => x.EstimatedCost).HasPrecision(12, 4);
            e.Property(x => x.ActualCost).HasPrecision(12, 4);
            e.Property(x => x.Currency).HasMaxLength(3);
        });
        b.Entity<WaBroadcastRecipient>(e =>
        {
            e.ToTable("WaBroadcastRecipients");
            e.HasKey(x => new { x.BroadcastId, x.ContactId });
            e.Property(x => x.Status).HasMaxLength(12).IsRequired();
            e.Property(x => x.Error).HasMaxLength(400);
            e.Property(x => x.EstimatedCost).HasPrecision(12, 4);
        });
        b.Entity<WaRate>(e =>
        {
            e.ToTable("WaRates");
            e.HasKey(x => new { x.Country, x.Category });
            e.Property(x => x.Country).HasMaxLength(2);
            e.Property(x => x.Category).HasMaxLength(30);
            e.Property(x => x.Rate).HasPrecision(12, 4);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        });
        // ── Social (script 36) ──
        b.Entity<SocialAccount>(e =>
        {
            e.ToTable("SocialAccounts");
            e.HasKey(x => x.AccountId);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.AppId).HasMaxLength(40);
            e.Property(x => x.AppSecretEnc).HasMaxLength(400);
            e.Property(x => x.TokenHint).HasMaxLength(8);
            e.Property(x => x.VerifyTokenEnc).HasMaxLength(400);
            e.Property(x => x.PageId).HasMaxLength(40);
            e.Property(x => x.PageName).HasMaxLength(200);
            e.Property(x => x.IgUserId).HasMaxLength(40);
            e.Property(x => x.IgUsername).HasMaxLength(100);
            e.Property(x => x.AdAccountId).HasMaxLength(40);
            e.Property(x => x.AdAccountName).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.WebhookUrl).HasMaxLength(400);
            e.Property(x => x.SyncError).HasMaxLength(400);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<SocialAccountDaily>(e =>
        {
            e.ToTable("SocialAccountDaily");
            e.HasKey(x => new { x.AccountId, x.Day });
            e.Property(x => x.Day).HasColumnType("date");
        });
        b.Entity<SocialPost>(e =>
        {
            e.ToTable("SocialPosts");
            e.HasKey(x => x.PostId);
            e.Property(x => x.Channel).HasMaxLength(12);
            e.Property(x => x.MetaId).HasMaxLength(80);
            e.Property(x => x.Type).HasMaxLength(20);
            e.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
            e.Property(x => x.Permalink).HasMaxLength(500);
            e.HasIndex(x => new { x.Channel, x.MetaId }).IsUnique();
        });
        b.Entity<SocialPostStat>(e =>
        {
            e.ToTable("SocialPostStats");
            e.HasKey(x => new { x.PostId, x.Day });
            e.Property(x => x.Day).HasColumnType("date");
        });
        b.Entity<SocialDraft>(e =>
        {
            e.ToTable("SocialDrafts");
            e.HasKey(x => x.DraftId);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Link).HasMaxLength(1000);
            e.Property(x => x.WhenMode).HasMaxLength(12);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ChecklistJson).HasMaxLength(1000);
            e.Property(x => x.ApprovedHash).HasMaxLength(64);
            e.Property(x => x.SentBackNote).HasMaxLength(1000);
            e.Property(x => x.FacebookPostId).HasMaxLength(80);
            e.Property(x => x.InstagramMediaId).HasMaxLength(80);
            e.Property(x => x.Error).HasMaxLength(1000);
        });
        b.Entity<SocialQueueSlot>(e =>
        {
            e.ToTable("SocialQueueSlots");
            e.HasKey(x => x.SlotId);
            e.Property(x => x.TimeOfDay).HasColumnType("time(0)");
        });
        b.Entity<SocialComment>(e =>
        {
            e.ToTable("SocialComments");
            e.HasKey(x => x.CommentId);
            e.Property(x => x.Channel).HasMaxLength(12);
            e.Property(x => x.MetaId).HasMaxLength(80);
            e.Property(x => x.PostMetaId).HasMaxLength(80);
            e.Property(x => x.ParentMetaId).HasMaxLength(80);
            e.Property(x => x.AuthorId).HasMaxLength(80);
            e.Property(x => x.AuthorName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(10);
            e.HasIndex(x => new { x.Channel, x.MetaId }).IsUnique();
        });
        b.Entity<SocialContact>(e =>
        {
            e.ToTable("SocialContacts");
            e.HasKey(x => x.ContactId);
            e.Property(x => x.Channel).HasMaxLength(12);
            e.Property(x => x.ExternalId).HasMaxLength(80);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.LastMessagePreview).HasMaxLength(200);
            e.Property(x => x.ChatStatus).HasMaxLength(10);
            e.HasIndex(x => new { x.AccountId, x.Channel, x.ExternalId }).IsUnique();
        });
        b.Entity<SocialMessage>(e =>
        {
            e.ToTable("SocialMessages");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.Direction).HasMaxLength(3);
            e.Property(x => x.MetaId).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => x.MetaId).IsUnique().HasFilter("[MetaId] IS NOT NULL");
        });
        b.Entity<SocialNote>(e =>
        {
            e.ToTable("SocialNotes");
            e.HasKey(x => x.NoteId);
            e.Property(x => x.Text).HasMaxLength(2000);
        });
        b.Entity<SocialLead>(e =>
        {
            e.ToTable("SocialLeads");
            e.HasKey(x => x.SocialLeadId);
            e.Property(x => x.Source).HasMaxLength(20);
            e.Property(x => x.MetaLeadId).HasMaxLength(80);
            e.Property(x => x.FormId).HasMaxLength(80);
            e.Property(x => x.FormName).HasMaxLength(200);
            e.Property(x => x.AdId).HasMaxLength(80);
            e.Property(x => x.AdName).HasMaxLength(300);
            e.Property(x => x.CampaignId).HasMaxLength(80);
            e.Property(x => x.CampaignName).HasMaxLength(300);
            e.Property(x => x.Platform).HasMaxLength(20);
            e.HasIndex(x => x.MetaLeadId).IsUnique().HasFilter("[MetaLeadId] IS NOT NULL");
        });
        b.Entity<SocialLeadForm>(e =>
        {
            e.ToTable("SocialLeadForms");
            e.HasKey(x => x.FormId);
            e.Property(x => x.FormId).HasMaxLength(80);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
        });
        b.Entity<SocialAdStat>(e =>
        {
            e.ToTable("SocialAdStats");
            e.HasKey(x => new { x.AccountId, x.Day, x.Level, x.ObjectId });
            e.Property(x => x.Day).HasColumnType("date");
            e.Property(x => x.Level).HasMaxLength(10);
            e.Property(x => x.ObjectId).HasMaxLength(80);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.ParentName).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.Spend).HasPrecision(14, 2);
        });

        b.Entity<WaConnection>(e =>
        {
            e.ToTable("WaConnection");
            e.HasKey(x => x.ConnectionId);
            e.Property(x => x.ConnectionId).ValueGeneratedNever();
            e.Property(x => x.PhoneNumberId).HasMaxLength(40);
            e.Property(x => x.WabaId).HasMaxLength(40);
            e.Property(x => x.AppId).HasMaxLength(40);
            e.Property(x => x.AppSecretEnc).HasMaxLength(400);
            e.Property(x => x.VerifyTokenEnc).HasMaxLength(400);
            e.Property(x => x.AccessTokenHint).HasMaxLength(8);
            e.Property(x => x.WebhookUrl).HasMaxLength(400);
        });
        b.Entity<WaSettings>(e =>
        {
            e.ToTable("WaSettings");
            e.HasKey(x => x.SettingsId);
            e.Property(x => x.SettingsId).ValueGeneratedNever();
            e.Property(x => x.MonthlyBudget).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        });

        b.Entity<CmsWhatsappTemplate>(e =>
        {
            e.ToTable("CmsWhatsappTemplates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        WorkBoardRelationships(b);
    }

    // The Daily Work Board's foreign keys, as the SQL scripts 26 to 31 declare them. EF needs to know
    // them for one reason: when a parent row and its child rows are saved together (a work item and
    // its "created" history line, a chat message and its mentions, a document and its first version)
    // it inserts in whatever order it likes unless it knows which row depends on which. Without
    // these, SQL Server refused the save with error 547 whenever the child happened to go first.
    // Add a line here for every new foreign key between two tables that can be written together.
    // WorkBoardModelTests checks the list.
    private static void WorkBoardRelationships(ModelBuilder b)
    {
        // 26_CMSDatabase_WorkBoard.sql
        Link<WorkStatus, WorkProject>(b, x => x.ProjectId);
        Link<WorkSprint, WorkProject>(b, x => x.ProjectId);
        Link<WorkItem, WorkProject>(b, x => x.ProjectId);
        Link<WorkItem, WorkItem>(b, x => x.ParentId);
        Link<WorkItem, WorkSprint>(b, x => x.SprintId);
        Link<WorkItemComment, WorkItem>(b, x => x.WorkItemId);
        Link<WorkItemHistory, WorkItem>(b, x => x.WorkItemId);
        // 27_CMSDatabase_WorkDocs.sql
        Link<DocFolder, DocFolder>(b, x => x.ParentId);
        Link<DocFolderAccess, DocFolder>(b, x => x.FolderId);
        Link<WorkDocument, DocFolder>(b, x => x.FolderId);
        Link<DocVersion, WorkDocument>(b, x => x.DocumentId);
        Link<WorkItemDocument, WorkItem>(b, x => x.WorkItemId);
        Link<WorkItemDocument, WorkDocument>(b, x => x.DocumentId);
        // 28_CMSDatabase_WorkBoardAccess.sql
        Link<WorkProjectMember, WorkProject>(b, x => x.ProjectId);
        Link<WorkProjectAccessLog, WorkProject>(b, x => x.ProjectId);
        // 30_CMSDatabase_WorkChat.sql
        Link<WorkChatChannel, WorkProject>(b, x => x.ProjectId);
        Link<WorkChatMember, WorkChatChannel>(b, x => x.ChannelId);
        Link<WorkChatMessage, WorkChatChannel>(b, x => x.ChannelId);
        Link<WorkChatMessage, WorkItem>(b, x => x.WorkItemId);
        Link<WorkChatMention, WorkChatMessage>(b, x => x.MessageId);
        Link<WorkNotification, WorkChatChannel>(b, x => x.ChannelId);
        Link<WorkNotification, WorkChatMessage>(b, x => x.MessageId);
        // 31_CMSDatabase_DocComments.sql
        Link<DocComment, WorkDocument>(b, x => x.DocumentId);
        Link<DocComment, DocComment>(b, x => x.ParentId);
        Link<DocCommentMention, DocComment>(b, x => x.CommentId);
        Link<DocNotification, WorkDocument>(b, x => x.DocumentId);
        Link<DocNotification, DocComment>(b, x => x.CommentId);
        // 33_CMSDatabase_WorkItemMentions.sql
        Link<WorkItemMention, WorkItem>(b, x => x.WorkItemId);
        Link<WorkItemMention, WorkItemComment>(b, x => x.CommentId);
        Link<WorkItemNotification, WorkItem>(b, x => x.WorkItemId);
        Link<WorkItemNotification, WorkItemComment>(b, x => x.CommentId);
    }

    // Child rows point at the parent's key. Nothing is deleted on the parent's behalf: these tables
    // hide rows instead of removing them, and the database's own rules stay in charge.
    private static void Link<TChild, TParent>(ModelBuilder b, System.Linq.Expressions.Expression<Func<TChild, object?>> foreignKey)
        where TChild : class
        where TParent : class
    {
        b.Entity<TChild>().HasOne<TParent>().WithMany().HasForeignKey(foreignKey).OnDelete(DeleteBehavior.ClientNoAction);
    }
}
