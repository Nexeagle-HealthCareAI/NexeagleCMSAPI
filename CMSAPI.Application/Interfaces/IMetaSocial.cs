namespace CMSAPI.Application.Interfaces;

/// <summary>
/// Meta's Graph API for CMS → Social: the Facebook Page, its Instagram account and the ad account. One
/// implementation talks to graph.facebook.com (CMSAPI.Data.Social.GraphMetaSocial); tests use a fake.
/// Every call takes the account's keys, so several accounts (NexEagle, Doctor Dekho) can be served.
/// </summary>
public interface IMetaSocial
{
    // Setup
    Task<MetaAssets> DiscoverAsync(string token, CancellationToken ct = default);
    Task<List<MetaAppSubscription>> AppSubscriptionsAsync(string appId, string appSecret, CancellationToken ct = default);
    Task SubscribeAppAsync(string appId, string appSecret, string obj, string callbackUrl, string fields, string verifyToken, CancellationToken ct = default);
    Task SubscribePageAsync(SocialCreds c, string fields, CancellationToken ct = default);
    Task<List<string>> PageSubscribedFieldsAsync(SocialCreds c, CancellationToken ct = default);

    // Posts and numbers
    Task<List<MetaPostItem>> FacebookPostsAsync(SocialCreds c, DateTime? since, int max, CancellationToken ct = default);
    Task<List<MetaPostItem>> InstagramMediaAsync(SocialCreds c, DateTime? since, int max, CancellationToken ct = default);
    /// <summary>A post's numbers by metric name; metrics Meta no longer knows are left out, not an error.</summary>
    Task<Dictionary<string, long>> InsightsAsync(SocialCreds c, string channel, string metaId, string mediaType, CancellationToken ct = default);
    Task<(int? Facebook, int? Instagram)> FollowersAsync(SocialCreds c, CancellationToken ct = default);

    // Comments
    Task<List<MetaCommentItem>> CommentsAsync(SocialCreds c, string channel, string postMetaId, CancellationToken ct = default);
    Task<string> ReplyToCommentAsync(SocialCreds c, string channel, string commentMetaId, string text, CancellationToken ct = default);
    Task HideCommentAsync(SocialCreds c, string channel, string commentMetaId, bool hide, CancellationToken ct = default);
    /// <summary>One private message answering a comment (Instagram: within 7 days of it).</summary>
    Task<string> PrivateReplyAsync(SocialCreds c, string commentMetaId, string text, CancellationToken ct = default);

    // Direct messages (Messenger: channel "messenger"; Instagram: "instagram")
    Task<string> SendMessageAsync(SocialCreds c, string channel, string recipientId, string text, bool humanAgent, CancellationToken ct = default);
    Task<string> SendAttachmentAsync(SocialCreds c, string channel, string recipientId, byte[] data, string mime, string fileName, bool humanAgent, CancellationToken ct = default);
    Task<MetaPerson> PersonAsync(SocialCreds c, string channel, string externalId, CancellationToken ct = default);

    // Leads
    Task<MetaLeadItem> LeadAsync(SocialCreds c, string leadgenId, CancellationToken ct = default);
    Task<List<MetaLeadForm>> LeadFormsAsync(SocialCreds c, CancellationToken ct = default);
    Task<List<MetaLeadItem>> FormLeadsAsync(SocialCreds c, string formId, DateTime since, CancellationToken ct = default);

    // Ads
    Task<List<MetaAdRow>> AdInsightsAsync(SocialCreds c, string level, DateTime fromDay, DateTime toDay, CancellationToken ct = default);
    Task<Dictionary<string, string>> AdStatusesAsync(SocialCreds c, string level, CancellationToken ct = default);

    // Publishing
    Task<string> PublishFacebookAsync(SocialCreds c, FacebookPublish post, CancellationToken ct = default);
    Task<string> CreateInstagramContainerAsync(SocialCreds c, InstagramContainer container, CancellationToken ct = default);
    /// <summary>IN_PROGRESS, FINISHED, ERROR, EXPIRED or PUBLISHED.</summary>
    Task<string> InstagramContainerStatusAsync(SocialCreds c, string containerId, CancellationToken ct = default);
    Task<string> PublishInstagramAsync(SocialCreds c, string containerId, CancellationToken ct = default);
    Task<(int Used, int Total)> InstagramPublishingLimitAsync(SocialCreds c, CancellationToken ct = default);
    Task<string?> PermalinkAsync(SocialCreds c, string channel, string metaId, CancellationToken ct = default);
}

/// <summary>An account's keys and assets, decrypted for one use. Never logged or returned.</summary>
public record SocialCreds(string Token, string? AppId, string? AppSecret, string? PageId, string? IgUserId, string? AdAccountId);

public class MetaSocialException : Exception
{
    public int? Code { get; }
    public int? Subcode { get; }
    public int HttpStatus { get; }
    public MetaSocialException(string message, int? code = null, int? subcode = null, int httpStatus = 502) : base(message)
    {
        Code = code;
        Subcode = subcode;
        HttpStatus = httpStatus;
    }
}

public class MetaAssets
{
    public string? UserName { get; set; }
    public List<MetaPage> Pages { get; set; } = new();
    public List<MetaAdAccount> AdAccounts { get; set; } = new();
    /// <summary>Permissions the token has (granted).</summary>
    public List<string> Permissions { get; set; } = new();
}

public class MetaPage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? InstagramId { get; set; }
    public string? InstagramUsername { get; set; }
}

public class MetaAdAccount
{
    /// <summary>"act_…"</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public int? Status { get; set; }
}

public class MetaAppSubscription
{
    public string Object { get; set; } = string.Empty;
    public string? CallbackUrl { get; set; }
    public List<string> Fields { get; set; } = new();
    public bool Active { get; set; }
}

public class MetaPostItem
{
    public string Id { get; set; } = string.Empty;
    /// <summary>Photo, Video, Reel, Carousel, Story, Link, Text</summary>
    public string Type { get; set; } = "Photo";
    /// <summary>Meta's own media type (IMAGE, VIDEO, CAROUSEL_ALBUM…), used to pick insight metrics.</summary>
    public string MediaType { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Permalink { get; set; }
    public DateTime PublishedAt { get; set; }
    public int? Likes { get; set; }
    public int? Comments { get; set; }
    public int? Shares { get; set; }
}

public class MetaCommentItem
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string? Text { get; set; }
    public bool Hidden { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MetaPerson
{
    public string? Name { get; set; }
    public string? Username { get; set; }
}

public class MetaLeadItem
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? FormId { get; set; }
    public string? AdId { get; set; }
    public string? AdName { get; set; }
    public string? CampaignId { get; set; }
    public string? CampaignName { get; set; }
    public string? Platform { get; set; }
    public bool? IsOrganic { get; set; }
    public List<KeyValuePair<string, string>> Answers { get; set; } = new();
}

public class MetaLeadForm
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class MetaAdRow
{
    public DateTime Day { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public decimal Spend { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public int Leads { get; set; }
    public int Conversations { get; set; }
}

public class FacebookPublish
{
    /// <summary>Photo, Carousel, Video, Reel, Text, Link</summary>
    public string Kind { get; set; } = "Photo";
    public string? Message { get; set; }
    public string? Link { get; set; }
    public List<MediaFile> Media { get; set; } = new();
}

public class MediaFile
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string Mime { get; set; } = "image/jpeg";
    public string Name { get; set; } = "file";
}

public class InstagramContainer
{
    /// <summary>IMAGE, VIDEO (carousel item), REELS, STORIES, CAROUSEL</summary>
    public string MediaType { get; set; } = "IMAGE";
    public string? ImageUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string? Caption { get; set; }
    public bool IsCarouselItem { get; set; }
    public List<string> Children { get; set; } = new();
}
