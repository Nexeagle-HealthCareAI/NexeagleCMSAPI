namespace CMSAPI.Domain.Entities;

// CMS → Social (Facebook Page, Instagram, ads). Tables from CMSDatabase script 36.

public class SocialAccount
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? AppSecretEnc { get; set; }
    public string? TokenEnc { get; set; }
    public string? TokenHint { get; set; }
    public string? VerifyTokenEnc { get; set; }
    public string? PageId { get; set; }
    public string? PageName { get; set; }
    public string? IgUserId { get; set; }
    public string? IgUsername { get; set; }
    public string? AdAccountId { get; set; }
    public string? AdAccountName { get; set; }
    public string? Currency { get; set; }
    public string? WebhookUrl { get; set; }
    public DateTime? WebhooksConnectedAt { get; set; }
    public DateTime? LastWebhookAt { get; set; }
    public DateTime? PostsSyncedAt { get; set; }
    public DateTime? AdsSyncedAt { get; set; }
    public DateTime? LeadsCheckedAt { get; set; }
    public string? SyncError { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SocialAccountDaily
{
    public Guid AccountId { get; set; }
    public DateTime Day { get; set; }
    public int? FbFollowers { get; set; }
    public int? IgFollowers { get; set; }
}

public class SocialPost
{
    public Guid PostId { get; set; }
    public Guid AccountId { get; set; }
    public string Channel { get; set; } = "facebook";
    public string MetaId { get; set; } = string.Empty;
    public string Type { get; set; } = "Photo";
    public string? Caption { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Permalink { get; set; }
    public DateTime PublishedAt { get; set; }
    public Guid? DraftId { get; set; }
    public int? Views { get; set; }
    public int? Reach { get; set; }
    public int? Likes { get; set; }
    public int? Comments { get; set; }
    public int? Shares { get; set; }
    public int? Saves { get; set; }
    public int? Clicks { get; set; }
    public string? StatsJson { get; set; }
    public DateTime? StatsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SocialPostStat
{
    public Guid PostId { get; set; }
    public DateTime Day { get; set; }
    public int? Views { get; set; }
    public int? Reach { get; set; }
    public int? Likes { get; set; }
    public int? Comments { get; set; }
    public int? Shares { get; set; }
    public int? Saves { get; set; }
}

public class SocialDraft
{
    public Guid DraftId { get; set; }
    public Guid AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool ToFacebook { get; set; }
    public bool ToInstagram { get; set; }
    public string Kind { get; set; } = "Photo";
    public string? Caption { get; set; }
    public string? FacebookCaption { get; set; }
    public string? InstagramCaption { get; set; }
    public string? Link { get; set; }
    public string? MediaJson { get; set; }
    public string WhenMode { get; set; } = "approved";
    public DateTime? ScheduledAt { get; set; }
    public string Status { get; set; } = "Draft";
    public string? ChecklistJson { get; set; }
    public Guid? ApproverUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedHash { get; set; }
    public string? SentBackNote { get; set; }
    public string? FacebookPostId { get; set; }
    public string? InstagramMediaId { get; set; }
    public string? PublishStateJson { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SocialQueueSlot
{
    public Guid SlotId { get; set; }
    public Guid AccountId { get; set; }
    public byte DayOfWeek { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public bool ToFacebook { get; set; }
    public bool ToInstagram { get; set; }
}

public class SocialComment
{
    public Guid CommentId { get; set; }
    public Guid AccountId { get; set; }
    public string Channel { get; set; } = "facebook";
    public string MetaId { get; set; } = string.Empty;
    public string PostMetaId { get; set; } = string.Empty;
    public string? ParentMetaId { get; set; }
    public string? AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string? Text { get; set; }
    public bool FromUs { get; set; }
    public bool Hidden { get; set; }
    public bool Deleted { get; set; }
    public string Status { get; set; } = "Open";
    public Guid? RepliedByUserId { get; set; }
    public DateTime? PrivateReplyAt { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SocialContact
{
    public Guid ContactId { get; set; }
    public Guid AccountId { get; set; }
    public string Channel { get; set; } = "messenger";
    public string ExternalId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Username { get; set; }
    public DateTime? LastInboundAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public string ChatStatus { get; set; } = "Open";
    public Guid? AssignedUserId { get; set; }
    public Guid? LeadId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SocialMessage
{
    public Guid MessageId { get; set; }
    public Guid ContactId { get; set; }
    public string Direction { get; set; } = "In";
    public string? MetaId { get; set; }
    public string? Text { get; set; }
    public string? AttachmentsJson { get; set; }
    public string Status { get; set; } = "received";
    public string? Error { get; set; }
    public bool HumanAgent { get; set; }
    public Guid? SentByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SocialNote
{
    public Guid NoteId { get; set; }
    public Guid ContactId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class SocialLead
{
    public Guid SocialLeadId { get; set; }
    public Guid AccountId { get; set; }
    public Guid LeadId { get; set; }
    public string Source { get; set; } = "form";
    public string? MetaLeadId { get; set; }
    public string? FormId { get; set; }
    public string? FormName { get; set; }
    public string? AdId { get; set; }
    public string? AdName { get; set; }
    public string? CampaignId { get; set; }
    public string? CampaignName { get; set; }
    public string? Platform { get; set; }
    public bool? IsOrganic { get; set; }
    public string? AnswersJson { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? CommentId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public class SocialLeadForm
{
    public string FormId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? MetaCount { get; set; }
    public int? CrmCount { get; set; }
    public string? MissingJson { get; set; }
    public DateTime? CheckedAt { get; set; }
}

public class SocialAdStat
{
    public Guid AccountId { get; set; }
    public DateTime Day { get; set; }
    public string Level { get; set; } = "campaign";
    public string ObjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public string? Status { get; set; }
    public decimal Spend { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public int Leads { get; set; }
    public int Conversations { get; set; }
}
