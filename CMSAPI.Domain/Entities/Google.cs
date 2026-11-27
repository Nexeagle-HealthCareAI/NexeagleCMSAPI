namespace CMSAPI.Domain.Entities;

// CMS → Google (Analytics, Google Ads, Search Console, lead forms). Tables from CMSDatabase script 37.

public class GoogleAccount
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Site { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecretEnc { get; set; }
    public string? RefreshTokenEnc { get; set; }
    public string? SignedInEmail { get; set; }
    public DateTime? SignedInAt { get; set; }
    public string? Ga4PropertyId { get; set; }
    public string? Ga4PropertyName { get; set; }
    public string? AdsCustomerId { get; set; }
    public string? AdsLoginCustomerId { get; set; }
    public string? AdsAccountName { get; set; }
    public string? Currency { get; set; }
    public string? TimeZone { get; set; }
    public string? SearchConsoleSite { get; set; }
    public string? MeasurementId { get; set; }
    public string? AdsConversionId { get; set; }
    public string? EventLabelsJson { get; set; }
    public string? LeadKeyEnc { get; set; }
    public DateTime? LastLeadAt { get; set; }
    public string? OutcomesJson { get; set; }
    public DateTime? LastOutcomeAt { get; set; }
    public string? LastOutcomeError { get; set; }
    public string? LastReadError { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GoogleLead
{
    public Guid GoogleLeadId { get; set; }
    public Guid AccountId { get; set; }
    public Guid LeadId { get; set; }
    public string Source { get; set; } = "web";
    public string? ExternalLeadId { get; set; }
    public string? FormId { get; set; }
    public string? FormName { get; set; }
    public string? CampaignId { get; set; }
    public string? CampaignName { get; set; }
    public string? AdGroupId { get; set; }
    public string? Keyword { get; set; }
    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? LandingPage { get; set; }
    public string? AnswersJson { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public class GoogleConversionUpload
{
    public Guid UploadId { get; set; }
    public Guid AccountId { get; set; }
    public Guid GoogleLeadId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string ConversionActionId { get; set; } = string.Empty;
    public string Status { get; set; } = "sent";
    public string? RequestId { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; } = 1;
    public DateTime SentAt { get; set; }
}
