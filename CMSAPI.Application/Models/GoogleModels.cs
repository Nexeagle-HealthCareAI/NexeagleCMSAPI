using CMSAPI.Application.Interfaces;

namespace CMSAPI.Application.Models;

// CMS → Google: what GoogleController sends and receives (web: src/features/google/types.ts).

public class GoogleActor
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool CanAds { get; set; }
    public bool CanConnect { get; set; }
}

// ── accounts and setup ───────────────────────────────────────────────────────

public class GoogleAccountDto
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Site { get; set; }
    public string? Ga4PropertyName { get; set; }
    public string? AdsAccountName { get; set; }
    public string? SearchConsoleSite { get; set; }
    public string Currency { get; set; } = "INR";
    public bool SignedIn { get; set; }
    public bool HasAnalytics { get; set; }
    public bool HasAds { get; set; }
}

public class GoogleAccountCreate
{
    public string Name { get; set; } = string.Empty;
    public string? Site { get; set; }
}

public class GoogleOutcome
{
    /// <summary>CRM stage: "Contacted", "Demo Scheduled", "Demo Done", "Negotiation", "Closed Won".</summary>
    public string Stage { get; set; } = string.Empty;
    /// <summary>A Google Ads conversion action of type "import from clicks" (UPLOAD_CLICKS).</summary>
    public string ConversionActionId { get; set; } = string.Empty;
    public string? ConversionActionName { get; set; }
    /// <summary>Value sent with it; for Closed Won the lead's deal value when it has one.</summary>
    public decimal? Value { get; set; }
}

public class GoogleSetupDto : GoogleAccountDto
{
    public bool CanEdit { get; set; }
    public bool CanEncrypt { get; set; }
    public bool Unreadable { get; set; }
    public string? ClientId { get; set; }
    public bool HasClientSecret { get; set; }
    public string? SignedInEmail { get; set; }
    public DateTime? SignedInAt { get; set; }
    public string? Ga4PropertyId { get; set; }
    public string? AdsCustomerId { get; set; }
    public string? AdsLoginCustomerId { get; set; }
    public string RedirectUrl { get; set; } = string.Empty;
    public string? MeasurementId { get; set; }
    public string? AdsConversionId { get; set; }
    public Dictionary<string, string> EventLabels { get; set; } = new();
    /// <summary>Site actions the website sends (name → label in the CMS).</summary>
    public List<GoogleSiteEvent> SiteEvents { get; set; } = new();
    public string LeadWebhookUrl { get; set; } = string.Empty;
    /// <summary>The key to paste into each Google lead form; only for people who may connect.</summary>
    public string? LeadKey { get; set; }
    public DateTime? LastLeadAt { get; set; }
    public List<GoogleOutcome> Outcomes { get; set; } = new();
    public DateTime? LastOutcomeAt { get; set; }
    public string? LastOutcomeError { get; set; }
    public string? LastReadError { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class GoogleSiteEvent
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>Counted as a lead (key event) in the CMS's numbers.</summary>
    public bool IsLead { get; set; }
}

public class GoogleClientSave
{
    public string? Name { get; set; }
    public string? Site { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public bool SignOut { get; set; }
}

public class GoogleChoice
{
    public string? Ga4PropertyId { get; set; }
    public string? AdsCustomerId { get; set; }
    public string? SearchConsoleSite { get; set; }
}

public class GoogleTagSave
{
    public string? MeasurementId { get; set; }
    public string? AdsConversionId { get; set; }
    public Dictionary<string, string>? EventLabels { get; set; }
}

public class GoogleSignInRequest
{
    /// <summary>The CMS page to come back to after Google's sign-in.</summary>
    public string ReturnUrl { get; set; } = string.Empty;
}

public class GoogleDiscovery
{
    public string? SignedInEmail { get; set; }
    public List<GaProperty> Properties { get; set; } = new();
    public List<GoogleAdsAccountDto> AdsAccounts { get; set; } = new();
    public List<string> SearchConsoleSites { get; set; } = new();
    public List<GoogleConversionActionDto> ConversionActions { get; set; } = new();
    /// <summary>Per part: why it could not be listed (e.g. "Google Ads API access not approved yet").</summary>
    public Dictionary<string, string> Problems { get; set; } = new();
}

public class GoogleAdsAccountDto
{
    public string CustomerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string? LoginCustomerId { get; set; }
}

public class GoogleConversionActionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class GoogleCheck
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>ok | warn | fail | todo</summary>
    public string State { get; set; } = "todo";
    public string? Detail { get; set; }
}

public class GoogleChecks
{
    public bool Ready { get; set; }
    public List<GoogleCheck> Items { get; set; } = new();
}

// ── Analytics ────────────────────────────────────────────────────────────────

public class GoogleRange
{
    public Guid AccountId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}

public class GoogleOverviewDto
{
    public int Users { get; set; }
    public int? PreviousUsers { get; set; }
    public int Sessions { get; set; }
    public int EngagedSessions { get; set; }
    public int KeyEvents { get; set; }
    /// <summary>The CMS's own count from the site's banner-free beacon (Site Visits), when known.</summary>
    public int? OwnVisits { get; set; }
    public List<GoogleDayDto> Days { get; set; } = new();
    public List<GoogleChannelDto> Channels { get; set; } = new();
    public List<GoogleCampaignDto> Campaigns { get; set; } = new();
    public List<GoogleEventDto> Events { get; set; } = new();
    public DateTime ReadAt { get; set; }
}

public class GoogleDayDto
{
    public DateOnly Day { get; set; }
    public int Sessions { get; set; }
    public int KeyEvents { get; set; }
}

public class GoogleChannelDto
{
    public string Name { get; set; } = string.Empty;
    public int Sessions { get; set; }
    public int KeyEvents { get; set; }
}

public class GoogleCampaignDto
{
    public string Name { get; set; } = string.Empty;
    public string SourceMedium { get; set; } = string.Empty;
    public int Sessions { get; set; }
    public int KeyEvents { get; set; }
}

public class GoogleEventDto
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool IsLead { get; set; }
}

public class GooglePageDto
{
    public string Path { get; set; } = string.Empty;
    public int Sessions { get; set; }
    public int EngagedSessions { get; set; }
    public double AvgSeconds { get; set; }
    public int KeyEvents { get; set; }
    public int Contacts { get; set; }
}

public class GooglePagesDto
{
    public string? SiteUrl { get; set; }
    public List<GooglePageDto> Pages { get; set; } = new();
    public DateTime ReadAt { get; set; }
}

public class GooglePageDetailDto
{
    public string Path { get; set; } = string.Empty;
    public List<GoogleChannelDto> Sources { get; set; } = new();
    public List<GscRow> Queries { get; set; } = new();
    public bool SearchConsoleConnected { get; set; }
}

// ── Ads ──────────────────────────────────────────────────────────────────────

public class GoogleAdsQuery : GoogleRange
{
    /// <summary>campaign | adgroup | keyword | search</summary>
    public string Level { get; set; } = "campaign";
}

public class GoogleAdsRowDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Sub { get; set; }
    public string? Status { get; set; }
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    /// <summary>Conversions as Google counts them.</summary>
    public double Conversions { get; set; }
    /// <summary>Leads in the CRM from this campaign (campaign level only).</summary>
    public int? CrmLeads { get; set; }
    public decimal? CostPerLead { get; set; }
}

public class GoogleAdsDayDto
{
    public DateOnly Day { get; set; }
    public decimal Spend { get; set; }
}

public class GooglePlatformDto
{
    public string Platform { get; set; } = string.Empty;   // google | meta
    public decimal Spend { get; set; }
    public int Leads { get; set; }
    public decimal? CostPerLead { get; set; }
    public int ReachedDemo { get; set; }
    public int Customers { get; set; }
    public decimal? CostPerCustomer { get; set; }
}

public class GoogleAdsDto
{
    public string Currency { get; set; } = "INR";
    public decimal Spend { get; set; }
    public decimal? DailyBudgets { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public double Conversions { get; set; }
    public int CrmLeads { get; set; }
    public int WebLeads { get; set; }
    public int FormLeads { get; set; }
    public decimal? CostPerLead { get; set; }
    public decimal? PreviousCostPerLead { get; set; }
    public int Customers { get; set; }
    public List<GoogleAdsDayDto> Days { get; set; } = new();
    public List<GoogleAdsRowDto> Rows { get; set; } = new();
    public List<GooglePlatformDto> Platforms { get; set; } = new();
    public DateTime ReadAt { get; set; }
}

// ── Leads ────────────────────────────────────────────────────────────────────

public class GoogleLeadQuery
{
    public Guid AccountId { get; set; }
    /// <summary>"" | web | form</summary>
    public string? Source { get; set; }
    public string? Campaign { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GoogleLeadAnswer
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class GoogleLeadDto
{
    public Guid GoogleLeadId { get; set; }
    public Guid LeadId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? FormName { get; set; }
    public string? CampaignName { get; set; }
    public string? Keyword { get; set; }
    public bool FromGoogleAd { get; set; }
    public string? UtmSource { get; set; }
    public List<GoogleLeadAnswer> Answers { get; set; } = new();
    public DateTime ReceivedAt { get; set; }
    public string? Stage { get; set; }
    public string? Owner { get; set; }
    public bool InCrm { get; set; }
    /// <summary>The latest result sent to Google for this lead, e.g. "Demo Done".</summary>
    public string? SentStage { get; set; }
    public DateTime? SentAt { get; set; }
    public string? SendError { get; set; }
}

public class GoogleLeadSummary
{
    public int Last7Days { get; set; }
    public int Web7 { get; set; }
    public int Forms7 { get; set; }
    public int NotContacted { get; set; }
    public DateTime? OldestNotContactedAt { get; set; }
    public int ReachedDemoThisMonth { get; set; }
    public int LeadsThisMonth { get; set; }
    public int SentThisMonth { get; set; }
}

public class GoogleLeadPage
{
    public List<GoogleLeadDto> Items { get; set; } = new();
    public int Total { get; set; }
    public GoogleLeadSummary Summary { get; set; } = new();
    public List<string> Campaigns { get; set; } = new();
}

/// <summary>A website form (nexeagle.com contact form), sent by the site with the Google click ID.</summary>
public class GoogleWebsiteLead
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Organisation { get; set; }
    public string? City { get; set; }
    public string? Message { get; set; }
    public string? Page { get; set; }
    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? LandingPage { get; set; }
    /// <summary>Honeypot: a hidden field people never fill; bots do.</summary>
    public string? Website { get; set; }
}

/// <summary>What a website reads to load the Google tag.</summary>
public class GoogleSiteTagDto
{
    public string? MeasurementId { get; set; }
    public string? AdsConversionId { get; set; }
    public Dictionary<string, string> EventLabels { get; set; } = new();
}
