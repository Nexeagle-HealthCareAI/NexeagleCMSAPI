using System.Text.Json;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

// WhatsApp marketing on the CMS's own number. Every method checks what the actor may do and answers
// WorkResult: 403 not allowed, 404 not found, 400/409 not possible now (the message says why),
// 502 Meta refused (Meta's reason), 503 the number is not connected here.

/// <summary>The inbox: chats, messages, replies, notes, assigning.</summary>
public interface IWaInboxService
{
    Task<WorkResult<List<WaChatDto>>> ChatsAsync(WaActor actor, WaChatQuery query);
    /// <summary>The whole chat; marks it read.</summary>
    Task<WorkResult<WaThreadDto>> ThreadAsync(WaActor actor, Guid contactId);
    /// <summary>Messages and notes newer than `after`, for the open chat to refresh.</summary>
    Task<WorkResult<WaThreadDto>> ThreadSinceAsync(WaActor actor, Guid contactId, DateTime after);
    Task<WorkResult<WaMessageDto>> SendTextAsync(WaActor actor, Guid contactId, WaSendTextRequest request);
    Task<WorkResult<WaMessageDto>> SendMediaAsync(WaActor actor, Guid contactId, byte[] data, string mime, string fileName, string? caption);
    Task<WorkResult<WaMessageDto>> SendTemplateAsync(WaActor actor, Guid contactId, WaSendTemplateRequest request);
    Task<WorkResult<WaNoteDto>> AddNoteAsync(WaActor actor, Guid contactId, WaNoteRequest request);
    Task<WorkResult<WaChatDto>> AssignAsync(WaActor actor, Guid contactId, WaAssignRequest request);
    Task<WorkResult<WaChatDto>> SetStatusAsync(WaActor actor, Guid contactId, WaChatStatusRequest request);
    /// <summary>A picture/video/document in a message, fetched from Meta.</summary>
    Task<WorkResult<WaMediaFile>> MediaAsync(WaActor actor, Guid messageId);
    /// <summary>Unread chats, for the menu badge.</summary>
    Task<int> UnreadAsync();
}

/// <summary>Messages and delivery statuses Meta sends to the marketing number's webhook.</summary>
public interface IWaWebhookService
{
    /// <summary>Stores what the payload carries. Safe to call twice with the same payload.</summary>
    Task HandleAsync(JsonElement payload);
}

public interface IWaContactService
{
    Task<WorkResult<WaContactPage>> ListAsync(WaActor actor, WaContactQuery query);
    Task<WorkResult<WaContactDto>> GetAsync(WaActor actor, Guid contactId);
    Task<WorkResult<WaContactDto>> CreateAsync(WaActor actor, WaSaveContactRequest request);
    Task<WorkResult<WaContactDto>> UpdateAsync(WaActor actor, Guid contactId, WaSaveContactRequest request);
    Task<WorkResult<WaImportResult>> ImportAsync(WaActor actor, WaImportRequest request);
    Task<WorkResult<WaImportResult>> ImportLeadsAsync(WaActor actor, WaImportLeadsRequest request);
    Task<WorkResult<WaImportResult>> ImportDoctorDekhoAsync(WaActor actor, WaImportDoctorDekhoRequest request);
}

public interface IWaTemplateService
{
    /// <summary>The templates; with refresh, read again from Meta first.</summary>
    Task<WorkResult<List<WaTemplateDto>>> ListAsync(WaActor actor, bool refresh);
    /// <summary>Sends a new template to Meta for approval. sample: the picture/video/document for a media header.</summary>
    Task<WorkResult<WaTemplateDto>> CreateAsync(WaActor actor, WaSaveTemplateRequest request, byte[]? sample, string? sampleMime, string? sampleName);
    Task<WorkResult<bool>> DeleteAsync(WaActor actor, Guid templateId);
    /// <summary>Reads every template's status from Meta (also run by the worker while some are pending).</summary>
    Task<int> SyncAsync();
}

public interface IWaBroadcastService
{
    Task<WorkResult<List<WaBroadcastDto>>> ListAsync(WaActor actor);
    Task<WorkResult<WaBroadcastDto>> GetAsync(WaActor actor, Guid broadcastId);
    Task<WorkResult<List<WaRecipientDto>>> RecipientsAsync(WaActor actor, Guid broadcastId, string? status);
    /// <summary>Who it would go to, what it would cost and whether that passes the budget.</summary>
    Task<WorkResult<WaBroadcastPreview>> PreviewAsync(WaActor actor, WaBroadcastRequest request);
    /// <summary>Creates it: Scheduled (or waiting for approval when over the budget).</summary>
    Task<WorkResult<WaBroadcastDto>> CreateAsync(WaActor actor, WaBroadcastRequest request);
    Task<WorkResult<WaBroadcastDto>> ApproveAsync(WaActor actor, Guid broadcastId);
    Task<WorkResult<WaBroadcastDto>> PauseAsync(WaActor actor, Guid broadcastId);
    Task<WorkResult<WaBroadcastDto>> ResumeAsync(WaActor actor, Guid broadcastId);
    Task<WorkResult<WaBroadcastDto>> CancelAsync(WaActor actor, Guid broadcastId);
    /// <summary>The worker: sends up to `max` due messages. Returns how many went.</summary>
    Task<int> SendDueAsync(int max, CancellationToken ct);
}

public interface IWaAccountService
{
    Task<WorkResult<WaNumberDto>> NumberAsync(WaActor actor);
    Task<WorkResult<WaProfileDto>> ProfileAsync(WaActor actor);
    Task<WorkResult<WaProfileDto>> SaveProfileAsync(WaActor actor, WaProfileDto profile, byte[]? picture, string? pictureMime);
    Task<WorkResult<WaSettingsDto>> SettingsAsync(WaActor actor);
    Task<WorkResult<WaSettingsDto>> SaveSettingsAsync(WaActor actor, WaSaveSettingsRequest request);
    Task<WorkResult<WaSpendSummary>> SpendAsync(WaActor actor, DateOnly from, DateOnly to);
    Task<WorkResult<List<WaMetaSpendDay>>> MetaSpendAsync(WaActor actor, DateOnly from, DateOnly to);
}
