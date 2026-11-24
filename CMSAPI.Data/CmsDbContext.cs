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
