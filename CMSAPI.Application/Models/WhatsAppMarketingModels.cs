namespace CMSAPI.Application.Models;

// WhatsApp marketing: what the CMS screens send and receive. Tables: 35_CMSDatabase_WhatsApp_Marketing.sql.

/// <summary>Who is asking, and what they may do (whatsapp.* permissions).</summary>
public class WaActor
{
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool CanReply { get; set; }
    public bool CanContacts { get; set; }
    public bool CanBroadcast { get; set; }
    public bool CanTemplates { get; set; }
    public bool CanApprove { get; set; }
    public bool CanSettings { get; set; }
    /// <summary>insights.contacts: may import Doctor Dekho patients' numbers.</summary>
    public bool CanPatientContacts { get; set; }
    /// <summary>whatsapp.connect: may enter the Meta keys and connect the webhook (WhatsApp → Setup).</summary>
    public bool CanConnect { get; set; }
}

// ── Contacts and chats ───────────────────────────────────────────────────────

public class WaChatDto
{
    public Guid ContactId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string DisplayPhone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ProfileName { get; set; }
    public string Source { get; set; } = string.Empty;
    public string OptIn { get; set; } = "Unknown";
    public List<string> Tags { get; set; } = new();
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public string ChatStatus { get; set; } = "Open";
    public Guid? AssignedUserId { get; set; }
    public string? AssignedName { get; set; }
    /// <summary>When the 24-hour reply window closes; null when it is closed.</summary>
    public DateTime? WindowClosesAt { get; set; }
}

public class WaContactDto : WaChatDto
{
    public Guid? LeadId { get; set; }
    public string? OptInSource { get; set; }
    public string? OptInBy { get; set; }
    public DateTime? OptInAt { get; set; }
    public DateTime? OptOutAt { get; set; }
    public string? OptOutReason { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public DateTime? LastInboundAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WaMessageDto
{
    public Guid MessageId { get; set; }
    public string Direction { get; set; } = "Out";
    public string Type { get; set; } = "text";
    public string? Body { get; set; }
    public bool HasMedia { get; set; }
    public string? MediaMime { get; set; }
    public string? MediaName { get; set; }
    public string? TemplateName { get; set; }
    public Guid? BroadcastId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StatusAt { get; set; }
    public string? ErrorText { get; set; }
    public string? SentBy { get; set; }
    public decimal Cost { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WaNoteDto
{
    public Guid NoteId { get; set; }
    public string Author { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class WaThreadDto
{
    public WaContactDto Contact { get; set; } = new();
    public List<WaMessageDto> Messages { get; set; } = new();
    public List<WaNoteDto> Notes { get; set; } = new();
    public bool WindowOpen { get; set; }
}

public class WaChatQuery
{
    /// <summary>open / closed / mine / unassigned / unread / all</summary>
    public string? View { get; set; }
    public string? Search { get; set; }
    public int Take { get; set; } = 50;
    /// <summary>Only chats with a message before this (for loading more).</summary>
    public DateTime? Before { get; set; }
}

public class WaSendTextRequest { public string Text { get; set; } = string.Empty; }
public class WaSendTemplateRequest
{
    public Guid TemplateId { get; set; }
    /// <summary>Variable → value, e.g. {"1":"Asha"} or {"first_name":"Asha"}.</summary>
    public Dictionary<string, string> Values { get; set; } = new();
    /// <summary>A https link to the picture/video/document for a media header; empty uses the template's sample.</summary>
    public string? HeaderMediaUrl { get; set; }
}
public class WaAssignRequest { public Guid? UserId { get; set; } }
public class WaNoteRequest { public string Text { get; set; } = string.Empty; }
public class WaChatStatusRequest { public string Status { get; set; } = "Open"; }

public class WaContactQuery
{
    public string? Search { get; set; }
    public string? OptIn { get; set; }
    public string? Source { get; set; }
    public string? Tag { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class WaContactPage
{
    public List<WaContactDto> Items { get; set; } = new();
    public int Total { get; set; }
    public List<string> AllTags { get; set; } = new();
}

public class WaSaveContactRequest
{
    public string Phone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public List<string>? Tags { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
    /// <summary>OptedIn / OptedOut / Unknown; null leaves it.</summary>
    public string? OptIn { get; set; }
    /// <summary>How they agreed — required when marking someone opted in.</summary>
    public string? OptInSource { get; set; }
}

public class WaImportRow
{
    public string Phone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public List<string>? Tags { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
    /// <summary>From an opt-in column: true / false / null (not given).</summary>
    public bool? OptedIn { get; set; }
}

public class WaImportRequest
{
    public List<WaImportRow> Rows { get; set; } = new();
    /// <summary>The person importing confirms everyone in the list agreed to WhatsApp messages.</summary>
    public bool AllOptedIn { get; set; }
    public string? OptInSource { get; set; }
    /// <summary>Tags added to everyone imported.</summary>
    public List<string>? Tags { get; set; }
}

public class WaImportLeadsRequest
{
    /// <summary>These leads; empty = every lead with a mobile number (optionally of these stages).</summary>
    public List<Guid>? LeadIds { get; set; }
    public List<string>? Stages { get; set; }
    public bool AllOptedIn { get; set; }
    public string? OptInSource { get; set; }
    public List<string>? Tags { get; set; }
}

public class WaImportDoctorDekhoRequest
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public bool AllOptedIn { get; set; }
    public string? OptInSource { get; set; }
    public List<string>? Tags { get; set; }
}

public class WaImportResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Problems { get; set; } = new();
}

// ── Templates ────────────────────────────────────────────────────────────────

public class WaButton
{
    /// <summary>QUICK_REPLY / URL / PHONE_NUMBER / COPY_CODE</summary>
    public string Type { get; set; } = "QUICK_REPLY";
    public string? Text { get; set; }
    public string? Url { get; set; }
    /// <summary>Example for a {{1}} at the end of the URL.</summary>
    public string? UrlExample { get; set; }
    public string? Phone { get; set; }
    /// <summary>Example code for COPY_CODE.</summary>
    public string? Example { get; set; }
}

public class WaTemplateDto
{
    public Guid TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RejectedReason { get; set; }
    public string? QualityScore { get; set; }
    public string ParameterFormat { get; set; } = "POSITIONAL";
    /// <summary>NONE / TEXT / IMAGE / VIDEO / DOCUMENT / LOCATION</summary>
    public string HeaderFormat { get; set; } = "NONE";
    public string? HeaderText { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? Footer { get; set; }
    public List<WaButton> Buttons { get; set; } = new();
    /// <summary>Variables to fill when sending: header first, then body, then a URL button's.</summary>
    public List<string> HeaderVariables { get; set; } = new();
    public List<string> BodyVariables { get; set; } = new();
    public bool UrlButtonVariable { get; set; }
    public bool HasSampleMedia { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WaSaveTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Category { get; set; } = "MARKETING";
    /// <summary>NONE / TEXT / IMAGE / VIDEO / DOCUMENT</summary>
    public string HeaderFormat { get; set; } = "NONE";
    public string? HeaderText { get; set; }
    public string? HeaderExample { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>Variable → example value; Meta requires one for each.</summary>
    public Dictionary<string, string> BodyExamples { get; set; } = new();
    public string? Footer { get; set; }
    public List<WaButton> Buttons { get; set; } = new();
}

// ── Broadcasts ───────────────────────────────────────────────────────────────

public class WaAudience
{
    /// <summary>Contacts with any of these tags (empty: any tag).</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>Lead / DoctorDekho / Import / Inbound / Manual (empty: any).</summary>
    public List<string> Sources { get; set; } = new();
    /// <summary>Exactly these people (instead of the filters).</summary>
    public List<Guid> ContactIds { get; set; } = new();
}

/// <summary>How one template variable is filled for each person.</summary>
public class WaVariableValue
{
    /// <summary>name / phone / attribute / text</summary>
    public string From { get; set; } = "text";
    /// <summary>The attribute's name when From = attribute.</summary>
    public string? Attribute { get; set; }
    /// <summary>The text when From = text; the fallback when a person's field is empty.</summary>
    public string? Text { get; set; }
}

public class WaBroadcastRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid TemplateId { get; set; }
    /// <summary>Variable ("1", "first_name", "url") → how to fill it.</summary>
    public Dictionary<string, WaVariableValue> Variables { get; set; } = new();
    public string? HeaderMediaUrl { get; set; }
    public WaAudience Audience { get; set; } = new();
    /// <summary>Send at this time (UTC); null sends as soon as it is allowed.</summary>
    public DateTime? ScheduledAt { get; set; }
}

public class WaBroadcastPreview
{
    /// <summary>Contacts the audience matches.</summary>
    public int Matched { get; set; }
    /// <summary>Who will get it: opted in.</summary>
    public int WillSend { get; set; }
    public int NotOptedIn { get; set; }
    public int OptedOut { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Currency { get; set; } = "INR";
    public decimal SpentThisMonth { get; set; }
    /// <summary>Estimates of broadcasts approved or sending that have not been spent yet.</summary>
    public decimal Committed { get; set; }
    public decimal? MonthlyBudget { get; set; }
    public bool OverBudget { get; set; }
    /// <summary>Three people's messages filled in, to check the variables.</summary>
    public List<string> Samples { get; set; } = new();
}

public class WaBroadcastDto
{
    public Guid BroadcastId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PausedReason { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int Recipients { get; set; }
    public int Pending { get; set; }
    public int Sent { get; set; }
    public int Delivered { get; set; }
    public int Read { get; set; }
    public int Failed { get; set; }
    public int Replied { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string Currency { get; set; } = "INR";
    public string? CreatedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WaRecipientDto
{
    public Guid ContactId { get; set; }
    public string DisplayPhone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime? SentAt { get; set; }
}

// ── Account, money ───────────────────────────────────────────────────────────

public class WaProfileDto
{
    public string? About { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? Email { get; set; }
    public List<string> Websites { get; set; } = new();
    public string? Vertical { get; set; }
    public string? PictureUrl { get; set; }
}

public class WaNumberDto
{
    public bool Connected { get; set; }
    public string? Problem { get; set; }
    public string? DisplayPhone { get; set; }
    public string? VerifiedName { get; set; }
    public string? NameStatus { get; set; }
    /// <summary>GREEN / YELLOW / RED / UNKNOWN</summary>
    public string? Quality { get; set; }
    public string? MessagingLimit { get; set; }
    public string? Throughput { get; set; }
}

public class WaRateDto
{
    public string Country { get; set; } = "IN";
    public string Category { get; set; } = "marketing";
    public decimal Rate { get; set; }
    public string Currency { get; set; } = "INR";
    public DateTime UpdatedAt { get; set; }
}

public class WaSettingsDto
{
    public decimal? MonthlyBudget { get; set; }
    public string Currency { get; set; } = "INR";
    public int SendPerSecond { get; set; } = 20;
    public List<WaRateDto> Rates { get; set; } = new();
}

public class WaSaveSettingsRequest
{
    /// <summary>Null or 0: no limit.</summary>
    public decimal? MonthlyBudget { get; set; }
    public int? SendPerSecond { get; set; }
    public List<WaRateDto>? Rates { get; set; }
}

public class WaSpendDay
{
    public DateOnly Date { get; set; }
    public decimal Marketing { get; set; }
    public decimal Utility { get; set; }
    public decimal Authentication { get; set; }
    public decimal Service { get; set; }
    public decimal Total { get; set; }
    public int Charged { get; set; }
    public int Free { get; set; }
}

public class WaSpendBroadcast
{
    public Guid BroadcastId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public int Delivered { get; set; }
}

public class WaSpendSummary
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string Currency { get; set; } = "INR";
    public decimal Total { get; set; }
    public decimal SpentThisMonth { get; set; }
    public decimal Committed { get; set; }
    public decimal? MonthlyBudget { get; set; }
    public List<WaSpendDay> Days { get; set; } = new();
    public Dictionary<string, decimal> ByCategory { get; set; } = new();
    public List<WaSpendBroadcast> ByBroadcast { get; set; } = new();
}

public class WaMetaSpendDay
{
    public DateOnly Date { get; set; }
    public string Category { get; set; } = string.Empty;
    public long Volume { get; set; }
    public decimal Cost { get; set; }
}

// ── Setup (the Meta connection) ──────────────────────────────────────────────

public class WaSetupDto
{
    public bool CanEdit { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }
    public string? AppId { get; set; }
    public bool HasAccessToken { get; set; }
    /// <summary>Last 4 characters of the token in use, to recognise it.</summary>
    public string? AccessTokenHint { get; set; }
    /// <summary>"cms" (entered here), "server" (server settings) or "none".</summary>
    public string TokenSource { get; set; } = "none";
    public bool HasAppSecret { get; set; }
    /// <summary>Shown only to people who may connect: it is typed into Meta when connecting by hand.</summary>
    public string? VerifyToken { get; set; }
    public bool HasVerifyToken { get; set; }
    /// <summary>The stored secrets cannot be decrypted (the server's key changed): enter them again.</summary>
    public bool Unreadable { get; set; }
    /// <summary>False when the server has no key to encrypt secrets with.</summary>
    public bool CanEncrypt { get; set; }
    public string SuggestedWebhookUrl { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public DateTime? WebhookConnectedAt { get; set; }
    public DateTime? LastWebhookAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class WaSetupSaveRequest
{
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }
    public string? AppId { get; set; }
    /// <summary>Null or empty: keep the stored one.</summary>
    public string? AccessToken { get; set; }
    /// <summary>Null or empty: keep the stored one.</summary>
    public string? AppSecret { get; set; }
    public bool ClearAccessToken { get; set; }
    public bool ClearAppSecret { get; set; }
}

public class WaSetupCheck
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>ok, warn, fail, todo</summary>
    public string State { get; set; } = "todo";
    public string? Detail { get; set; }
}

public class WaSetupChecks
{
    public List<WaSetupCheck> Items { get; set; } = new();
    /// <summary>Everything needed to send and receive is in place.</summary>
    public bool Ready { get; set; }
}

public class WaConnectWebhookRequest { public string Url { get; set; } = string.Empty; }
public class WaTestMessageRequest { public string Phone { get; set; } = string.Empty; }
