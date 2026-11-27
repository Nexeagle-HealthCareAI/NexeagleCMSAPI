using System.Text.Json;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

// CMS → Social services (implemented in CMSAPI.Data.Social). Design: claude/social-facebook-instagram-design.md

public interface ISocialSetupService
{
    Task<List<SocialAccountDto>> AccountsAsync();
    Task<WorkResult<SocialSetupDto>> GetAsync(SocialActor actor, Guid accountId, string suggestedWebhookUrl);
    Task<WorkResult<SocialSetupDto>> CreateAsync(SocialActor actor, SocialAccountCreate request, string suggestedWebhookUrl);
    Task<WorkResult<SocialSetupDto>> SaveKeysAsync(SocialActor actor, Guid accountId, SocialKeysSave request, string suggestedWebhookUrl);
    Task<WorkResult<MetaAssets>> DiscoverAsync(SocialActor actor, Guid accountId);
    Task<WorkResult<SocialSetupDto>> ChooseAsync(SocialActor actor, Guid accountId, SocialAssetsChoice choice, string suggestedWebhookUrl);
    Task<WorkResult<SocialChecks>> CheckAsync(SocialActor actor, Guid accountId, string suggestedWebhookUrl);
    Task<WorkResult<SocialChecks>> ConnectWebhooksAsync(SocialActor actor, Guid accountId, SocialWebhookRequest request, string suggestedWebhookUrl);
}

public interface ISocialPostsService
{
    Task<WorkResult<SocialPostPage>> ListAsync(SocialPostQuery query);
    Task<WorkResult<SocialPostDetail>> DetailAsync(Guid postId);
}

public interface ISocialComposeService
{
    Task<WorkResult<SocialMediaRef>> UploadAsync(SocialActor actor, Stream content, string fileName, string mime, long size);
    /// <summary>The file, for the CMS (signed-in) to show.</summary>
    Task<(Stream Content, string Mime)?> MediaAsync(string key);
    Task<WorkResult<List<SocialDraftDto>>> ListAsync(Guid accountId, string? status);
    Task<WorkResult<SocialDraftDto>> GetAsync(Guid draftId);
    Task<WorkResult<SocialDraftDto>> SaveAsync(SocialActor actor, Guid? draftId, SocialDraftSave request);
    Task<WorkResult<SocialDraftDto>> SubmitAsync(SocialActor actor, Guid draftId);
    Task<WorkResult<SocialDraftDto>> ApproveAsync(SocialActor actor, Guid draftId);
    Task<WorkResult<SocialDraftDto>> SendBackAsync(SocialActor actor, Guid draftId, string note);
    Task<WorkResult<SocialDraftDto>> CancelAsync(SocialActor actor, Guid draftId);
    Task<WorkResult<SocialDraftDto>> RetryAsync(SocialActor actor, Guid draftId);
    Task<WorkResult<List<SocialCalendarItem>>> CalendarAsync(Guid accountId, DateTime from, DateTime to);
    Task<List<SocialSlotDto>> SlotsAsync(Guid accountId);
    Task<WorkResult<List<SocialSlotDto>>> SaveSlotsAsync(SocialActor actor, Guid accountId, List<SocialSlotDto> slots);
    Task<List<SocialApprover>> ApproversAsync();
    /// <summary>Worker step: publish what is due, move Instagram containers along. Returns how many changed.</summary>
    Task<int> PublishDueAsync(CancellationToken ct);
}

public interface ISocialInboxService
{
    Task<WorkResult<List<SocialConversationDto>>> ConversationsAsync(SocialActor actor, SocialConversationQuery query);
    Task<WorkResult<SocialThreadDto>> ThreadAsync(SocialActor actor, Guid contactId);
    Task<WorkResult<SocialMessageDto>> SendAsync(SocialActor actor, Guid contactId, string? text, Stream? file, string? fileName, string? mime);
    Task<WorkResult<SocialNoteDto>> NoteAsync(SocialActor actor, Guid contactId, string text);
    Task<WorkResult<SocialConversationDto>> AssignAsync(SocialActor actor, Guid contactId, Guid? userId);
    Task<WorkResult<SocialConversationDto>> StatusAsync(SocialActor actor, Guid contactId, string status);
    Task<WorkResult<SocialCommentDto>> CommentAsync(SocialActor actor, Guid commentId);
    Task<WorkResult<SocialCommentDto>> ReplyToCommentAsync(SocialActor actor, Guid commentId, SocialCommentReply request);
    Task<WorkResult<SocialCommentDto>> HideCommentAsync(SocialActor actor, Guid commentId, bool hidden);
    Task<WorkResult<SocialCommentDto>> CommentDoneAsync(SocialActor actor, Guid commentId, bool done);
    Task<int> UnreadAsync(Guid? accountId);
}

public interface ISocialLeadsService
{
    Task<WorkResult<SocialLeadPage>> ListAsync(SocialLeadQuery query);
    /// <summary>A lead from a lead-form webhook (or the missed-lead check): fetched from Meta, made once.</summary>
    Task<WorkResult<SocialLeadDto>> IngestFormLeadAsync(Guid accountId, string leadgenId, CancellationToken ct = default);
    Task<WorkResult<SocialLeadDto>> FromConversationAsync(SocialActor actor, Guid contactId, SocialMakeLead request);
    Task<WorkResult<SocialLeadDto>> FromCommentAsync(SocialActor actor, Guid commentId, SocialMakeLead request);
    /// <summary>Compares each lead form's last 30 days at Meta with the CRM.</summary>
    Task<WorkResult<List<SocialLeadFormDto>>> CheckFormsAsync(Guid accountId, CancellationToken ct = default);
    Task<WorkResult<int>> ImportMissingAsync(SocialActor actor, Guid accountId, string formId, CancellationToken ct = default);
}

public interface ISocialAdsService
{
    Task<WorkResult<SocialAdsDto>> SummaryAsync(SocialAdsQuery query);
}

public interface ISocialWebhookService
{
    Task HandleAsync(JsonElement payload, CancellationToken ct = default);
}

/// <summary>Reads posts, numbers, comments, followers and ads from Meta into the database (the worker).</summary>
public interface ISocialSyncService
{
    /// <summary>Everything due for one account. `full` reads every past post (first connection).</summary>
    Task SyncAccountAsync(Guid accountId, bool full, CancellationToken ct);
}
