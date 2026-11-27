namespace CMSAPI.Application.Models;

// CMS → Social: what the API sends and receives (SocialController).

/// <summary>Who is acting, from the sign-in claims.</summary>
public class SocialActor
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool CanReply { get; set; }
    public bool CanCompose { get; set; }
    public bool CanPublish { get; set; }
    public bool CanAds { get; set; }
    public bool CanConnect { get; set; }
}

// ── Accounts and setup ───────────────────────────────────────────────────────

public class SocialAccountDto
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PageId { get; set; }
    public string? PageName { get; set; }
    public string? IgUserId { get; set; }
    public string? IgUsername { get; set; }
    public string? AdAccountId { get; set; }
    public string? AdAccountName { get; set; }
    public string Currency { get; set; } = "INR";
    /// <summary>Token and Page chosen: posts can be read.</summary>
    public bool Connected { get; set; }
}

public class SocialSetupDto : SocialAccountDto
{
    public bool CanEdit { get; set; }
    public string? AppId { get; set; }
    public bool HasAppSecret { get; set; }
    public bool HasToken { get; set; }
    public string? TokenHint { get; set; }
    /// <summary>Shown only to people who may connect.</summary>
    public string? VerifyToken { get; set; }
    public bool Unreadable { get; set; }
    public bool CanEncrypt { get; set; }
    public string SuggestedWebhookUrl { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public DateTime? WebhooksConnectedAt { get; set; }
    public DateTime? LastWebhookAt { get; set; }
    public DateTime? PostsSyncedAt { get; set; }
    public DateTime? AdsSyncedAt { get; set; }
    public DateTime? LeadsCheckedAt { get; set; }
    public string? SyncError { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class SocialAccountCreate { public string Name { get; set; } = string.Empty; }

public class SocialKeysSave
{
    public string? Name { get; set; }
    public string? AppId { get; set; }
    /// <summary>Null or empty: keep the stored one.</summary>
    public string? AppSecret { get; set; }
    /// <summary>Null or empty: keep the stored one.</summary>
    public string? Token { get; set; }
    public bool ClearToken { get; set; }
}

public class SocialAssetsChoice
{
    public string? PageId { get; set; }
    public string? AdAccountId { get; set; }
}

public class SocialCheck
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>ok, warn, fail, todo</summary>
    public string State { get; set; } = "todo";
    public string? Detail { get; set; }
}

public class SocialPermissionDto
{
    public string For { get; set; } = string.Empty;
    public string Permissions { get; set; } = string.Empty;
    /// <summary>granted, missing, unknown</summary>
    public string State { get; set; } = "unknown";
}

public class SocialChecks
{
    public List<SocialCheck> Items { get; set; } = new();
    public List<SocialPermissionDto> Permissions { get; set; } = new();
    public bool Ready { get; set; }
}

public class SocialWebhookRequest
{
    public string Url { get; set; } = string.Empty;
    /// <summary>The app's Page webhooks go somewhere else now (e.g. another system): move them here anyway.</summary>
    public bool Replace { get; set; }
}

// ── Posts ────────────────────────────────────────────────────────────────────

public class SocialPostQuery
{
    public Guid AccountId { get; set; }
    public string? Channel { get; set; }
    public string? Type { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Search { get; set; }
    /// <summary>newest (default), views, interactions</summary>
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class SocialPostDto
{
    public Guid PostId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string MetaId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Permalink { get; set; }
    public DateTime PublishedAt { get; set; }
    public bool FromCms { get; set; }
    public string? PublishedBy { get; set; }
    public int? Views { get; set; }
    public int? Reach { get; set; }
    public int? Likes { get; set; }
    public int? Comments { get; set; }
    public int? Shares { get; set; }
    public int? Saves { get; set; }
    public int? Clicks { get; set; }
    public DateTime? StatsAt { get; set; }
}

public class SocialPostSummary
{
    public int Posts { get; set; }
    public int FacebookPosts { get; set; }
    public int InstagramPosts { get; set; }
    public long Views { get; set; }
    public long? PreviousViews { get; set; }
    public long Interactions { get; set; }
    public int? NewFacebookFollowers { get; set; }
    public int? NewInstagramFollowers { get; set; }
}

public class SocialPostPage
{
    public List<SocialPostDto> Items { get; set; } = new();
    public int Total { get; set; }
    public SocialPostSummary Summary { get; set; } = new();
    public DateTime? SyncedAt { get; set; }
}

public class SocialDayStat
{
    public DateTime Day { get; set; }
    public int? Views { get; set; }
    public int? Likes { get; set; }
    public int? Comments { get; set; }
}

public class SocialPostDetail
{
    public SocialPostDto Post { get; set; } = new();
    public List<SocialDayStat> Days { get; set; } = new();
    public List<SocialCommentDto> Comments { get; set; } = new();
    /// <summary>Every number Meta gave, by its metric name.</summary>
    public Dictionary<string, long> Metrics { get; set; } = new();
}

// ── Compose ──────────────────────────────────────────────────────────────────

public class SocialMediaRef
{
    public string Key { get; set; } = string.Empty;
    public string Mime { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
}

public class SocialDraftSave
{
    public Guid AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool ToFacebook { get; set; }
    public bool ToInstagram { get; set; }
    public string Kind { get; set; } = "Photo";
    public string? Caption { get; set; }
    public string? FacebookCaption { get; set; }
    public string? InstagramCaption { get; set; }
    public string? Link { get; set; }
    public List<SocialMediaRef> Media { get; set; } = new();
    /// <summary>approved, schedule, queue</summary>
    public string WhenMode { get; set; } = "approved";
    public DateTime? ScheduledAt { get; set; }
    public Dictionary<string, bool> Checklist { get; set; } = new();
    public Guid? ApproverUserId { get; set; }
}

public class SocialDraftDto
{
    public Guid DraftId { get; set; }
    public Guid AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool ToFacebook { get; set; }
    public bool ToInstagram { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public string? FacebookCaption { get; set; }
    public string? InstagramCaption { get; set; }
    public string? Link { get; set; }
    public List<SocialMediaRef> Media { get; set; } = new();
    public string WhenMode { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, bool> Checklist { get; set; } = new();
    public Guid? ApproverUserId { get; set; }
    public string? ApproverName { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? SentBackNote { get; set; }
    public string? FacebookPostId { get; set; }
    public string? InstagramMediaId { get; set; }
    public string? Error { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SocialSendBackRequest { public string Note { get; set; } = string.Empty; }

public class SocialCalendarItem
{
    public Guid? DraftId { get; set; }
    public Guid? PostId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>facebook, instagram</summary>
    public List<string> Channels { get; set; } = new();
    public DateTime At { get; set; }
    /// <summary>Published, Scheduled, AwaitingApproval, Failed, Publishing, Draft, SentBack</summary>
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class SocialSlotDto
{
    public Guid? SlotId { get; set; }
    public int DayOfWeek { get; set; }
    /// <summary>"18:30", India time</summary>
    public string Time { get; set; } = "18:30";
    public bool ToFacebook { get; set; }
    public bool ToInstagram { get; set; }
}

public class SocialApprover { public Guid UserId { get; set; } public string FullName { get; set; } = string.Empty; }

// ── Inbox ────────────────────────────────────────────────────────────────────

public class SocialConversationDto
{
    /// <summary>dm or comment</summary>
    public string Kind { get; set; } = "dm";
    public Guid Id { get; set; }
    /// <summary>messenger, instagram (direct messages); facebook, instagram (comments)</summary>
    public string Channel { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Preview { get; set; }
    public DateTime? LastAt { get; set; }
    public int Unread { get; set; }
    public string Status { get; set; } = "Open";
    public Guid? AssignedUserId { get; set; }
    public string? AssignedName { get; set; }
    /// <summary>Free replies allowed until then (24 hours after their last message).</summary>
    public DateTime? WindowClosesAt { get; set; }
    /// <summary>A human agent may still reply until then (7 days after their last message).</summary>
    public DateTime? HumanAgentUntil { get; set; }
    public Guid? LeadId { get; set; }
}

public class SocialConversationQuery
{
    public Guid AccountId { get; set; }
    /// <summary>messenger, instagram, comments, or empty for all</summary>
    public string? Channel { get; set; }
    /// <summary>open, mine, unassigned, unread, closed, all</summary>
    public string? View { get; set; }
    public string? Search { get; set; }
}

public class SocialAttachment { public string Type { get; set; } = "file"; public string? Url { get; set; } public string? Name { get; set; } }

public class SocialMessageDto
{
    public Guid MessageId { get; set; }
    public string Direction { get; set; } = "In";
    public string? Text { get; set; }
    public List<SocialAttachment> Attachments { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string? SentBy { get; set; }
    public bool HumanAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SocialNoteDto { public Guid NoteId { get; set; } public string Author { get; set; } = string.Empty; public string Text { get; set; } = string.Empty; public DateTime CreatedAt { get; set; } }

public class SocialThreadDto
{
    public SocialConversationDto Conversation { get; set; } = new();
    public List<SocialMessageDto> Messages { get; set; } = new();
    public List<SocialNoteDto> Notes { get; set; } = new();
    public bool WindowOpen { get; set; }
    public bool HumanAgentOpen { get; set; }
}

public class SocialCommentDto
{
    public Guid CommentId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string MetaId { get; set; } = string.Empty;
    public string PostMetaId { get; set; } = string.Empty;
    public string? PostCaption { get; set; }
    public string? PostPermalink { get; set; }
    public Guid? PostId { get; set; }
    public string? ParentMetaId { get; set; }
    public string? AuthorName { get; set; }
    public string? Text { get; set; }
    public bool FromUs { get; set; }
    public bool Hidden { get; set; }
    public string Status { get; set; } = "Open";
    public string? RepliedBy { get; set; }
    public DateTime? PrivateReplyAt { get; set; }
    /// <summary>A private message reply is still possible (Instagram: within 7 days; once).</summary>
    public bool CanPrivateReply { get; set; }
    public Guid? LeadId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SocialCommentDto> Replies { get; set; } = new();
}

public class SocialTextRequest { public string Text { get; set; } = string.Empty; }
public class SocialAssignRequest { public Guid? UserId { get; set; } }
public class SocialStatusRequest { public string Status { get; set; } = "Open"; }
public class SocialCommentReply { public string Text { get; set; } = string.Empty; public bool Private { get; set; } }
public class SocialHideRequest { public bool Hidden { get; set; } = true; }

public class SocialMakeLead
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    /// <summary>What they are interested in (free text: a product name).</summary>
    public string? InterestedIn { get; set; }
    public string? Notes { get; set; }
}

// ── Leads ────────────────────────────────────────────────────────────────────

public class SocialLeadQuery
{
    public Guid AccountId { get; set; }
    /// <summary>form, messenger, instagram, comment</summary>
    public string? Source { get; set; }
    public string? FormId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class SocialLeadAnswer { public string Name { get; set; } = string.Empty; public string Value { get; set; } = string.Empty; }

public class SocialLeadDto
{
    public Guid SocialLeadId { get; set; }
    public Guid LeadId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? FormName { get; set; }
    public string? AdName { get; set; }
    public string? CampaignName { get; set; }
    public string? Platform { get; set; }
    public bool? IsOrganic { get; set; }
    public List<SocialLeadAnswer> Answers { get; set; } = new();
    public DateTime ReceivedAt { get; set; }
    public string? Stage { get; set; }
    public string? Owner { get; set; }
    public bool InCrm { get; set; } = true;
}

public class SocialLeadSummary
{
    public int Last7Days { get; set; }
    public int FromForms7 { get; set; }
    public int FromChats7 { get; set; }
    public int NotContacted { get; set; }
    public DateTime? OldestNotContactedAt { get; set; }
    public int Customers { get; set; }
}

public class SocialLeadPage
{
    public List<SocialLeadDto> Items { get; set; } = new();
    public int Total { get; set; }
    public SocialLeadSummary Summary { get; set; } = new();
    public List<SocialLeadFormDto> Forms { get; set; } = new();
}

public class SocialLeadFormDto
{
    public string FormId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? MetaCount { get; set; }
    public int? CrmCount { get; set; }
    public int Missing { get; set; }
    public DateTime? CheckedAt { get; set; }
}

// ── Ads ──────────────────────────────────────────────────────────────────────

public class SocialAdsQuery
{
    public Guid AccountId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    /// <summary>campaign, adset, ad</summary>
    public string Level { get; set; } = "campaign";
}

public class SocialAdRowDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public string? Status { get; set; }
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public int Leads { get; set; }
    public int Conversations { get; set; }
    public decimal? CostPerLead { get; set; }
}

public class SocialAdDay { public DateTime Day { get; set; } public decimal Spend { get; set; } public int Leads { get; set; } }

public class SocialAdsDto
{
    public string Currency { get; set; } = "INR";
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public int Leads { get; set; }
    public int Conversations { get; set; }
    public decimal? CostPerLead { get; set; }
    public decimal? PreviousCostPerLead { get; set; }
    /// <summary>CRM leads from lead forms in the period that reached "Closed Won".</summary>
    public int Customers { get; set; }
    public List<SocialAdDay> Days { get; set; } = new();
    public List<SocialAdRowDto> Rows { get; set; } = new();
    public DateTime? SyncedAt { get; set; }
}
