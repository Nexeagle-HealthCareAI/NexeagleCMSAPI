namespace CMSAPI.Domain.Entities;

// WhatsApp marketing on the CMS's own number (35_CMSDatabase_WhatsApp_Marketing.sql).

/// <summary>Someone the marketing number may message, and the state of the chat with them.</summary>
public class WaContact
{
    public Guid ContactId { get; set; }
    /// <summary>Digits with the country code and no "+", as Meta sends wa_id: 919876543210.</summary>
    public string Phone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ProfileName { get; set; }
    public string Source { get; set; } = "Manual";
    public Guid? LeadId { get; set; }
    public string OptIn { get; set; } = "Unknown";
    public string? OptInSource { get; set; }
    public Guid? OptInByUserId { get; set; }
    public DateTime? OptInAt { get; set; }
    public DateTime? OptOutAt { get; set; }
    public string? OptOutReason { get; set; }
    public string? Tags { get; set; }
    public string? Attributes { get; set; }
    public DateTime? LastInboundAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public string ChatStatus { get; set; } = "Open";
    public Guid? AssignedUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WaMessage
{
    public Guid MessageId { get; set; }
    public Guid ContactId { get; set; }
    public string Direction { get; set; } = "Out";
    public string? WamId { get; set; }
    public string Type { get; set; } = "text";
    public string? Body { get; set; }
    public string? MediaId { get; set; }
    public string? MediaMime { get; set; }
    public string? MediaName { get; set; }
    public string? TemplateName { get; set; }
    public string? TemplateLanguage { get; set; }
    public Guid? BroadcastId { get; set; }
    public string? ReplyToWamId { get; set; }
    public Guid? SentByUserId { get; set; }
    public string Status { get; set; } = "queued";
    public DateTime? StatusAt { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
    public string? PricingCategory { get; set; }
    public bool? Billable { get; set; }
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "INR";
    public DateTime CreatedAt { get; set; }
}

public class WaNote
{
    public Guid NoteId { get; set; }
    public Guid ContactId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class WaTemplate
{
    public Guid TemplateId { get; set; }
    public string? MetaTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Category { get; set; } = "MARKETING";
    public string Status { get; set; } = "DRAFT";
    public string? RejectedReason { get; set; }
    public string? QualityScore { get; set; }
    public string ParameterFormat { get; set; } = "POSITIONAL";
    public string Components { get; set; } = "[]";
    public string? HeaderMediaKey { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SyncedAt { get; set; }
}

public class WaBroadcast
{
    public Guid BroadcastId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TemplateId { get; set; }
    public string? Variables { get; set; }
    public string? HeaderMediaUrl { get; set; }
    public string? Audience { get; set; }
    public string Status { get; set; } = "Draft";
    public string? PausedReason { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int Recipients { get; set; }
    public int Sent { get; set; }
    public int Delivered { get; set; }
    public int Read { get; set; }
    public int Failed { get; set; }
    public int Replied { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string Currency { get; set; } = "INR";
    public Guid? CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WaBroadcastRecipient
{
    public Guid BroadcastId { get; set; }
    public Guid ContactId { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? MessageId { get; set; }
    public string? Error { get; set; }
    public decimal EstimatedCost { get; set; }
    public DateTime? SentAt { get; set; }
}

public class WaRate
{
    public string Country { get; set; } = "IN";
    public string Category { get; set; } = "marketing";
    public decimal Rate { get; set; }
    public string Currency { get; set; } = "INR";
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WaSettings
{
    public int SettingsId { get; set; } = 1;
    public decimal? MonthlyBudget { get; set; }
    public string Currency { get; set; } = "INR";
    public int SendPerSecond { get; set; } = 20;
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>The Meta connection, one row (entered in WhatsApp → Setup). Secrets are encrypted by the API.</summary>
public class WaConnection
{
    public int ConnectionId { get; set; } = 1;
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }
    public string? AppId { get; set; }
    public string? AccessTokenEnc { get; set; }
    public string? AppSecretEnc { get; set; }
    public string? VerifyTokenEnc { get; set; }
    public string? AccessTokenHint { get; set; }
    public string? WebhookUrl { get; set; }
    public DateTime? WebhookConnectedAt { get; set; }
    public DateTime? LastWebhookAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
