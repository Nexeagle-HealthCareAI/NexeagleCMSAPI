namespace CMSAPI.Domain.Entities;

// CMS → Marketing → Keywords: searches collected daily for Doctor Dekho and nexeagle.com, topics and the
// content plan. Tables from CMSDatabase script 38. Plan: claude/keyword-content-engine-plan.md.

public class KeywordSettings
{
    public int SettingsId { get; set; } = 1;
    public string? SeedsJson { get; set; }
    public string? CompetitorSitesJson { get; set; }
    public string? CitiesJson { get; set; }
    public string? WeightsJson { get; set; }
    public int MinVisitors { get; set; } = 5;
    public int PlannerCursor { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class KeywordTopic
{
    public Guid TopicId { get; set; }
    public string Site { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TargetPage { get; set; }
    public string? Notes { get; set; }
    public bool IsSuggested { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Keyword
{
    public Guid KeywordId { get; set; }
    public string Site { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Normalized { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public bool LanguageByPerson { get; set; }
    public string Intent { get; set; } = "other";
    public bool IntentByPerson { get; set; }
    public string? City { get; set; }
    public Guid? TopicId { get; set; }
    public bool TopicByPerson { get; set; }
    public string Status { get; set; } = "active";
    public string Sources { get; set; } = string.Empty;
    public long? AvgMonthlySearches { get; set; }
    public string? Competition { get; set; }
    public int? CompetitionIndex { get; set; }
    public decimal? LowBid { get; set; }
    public decimal? HighBid { get; set; }
    public string? MonthlyJson { get; set; }
    public DateTime? VolumeAt { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public class KeywordObservation
{
    public long ObservationId { get; set; }
    public Guid KeywordId { get; set; }
    public DateOnly Day { get; set; }
    public string Source { get; set; } = string.Empty;
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public decimal? Position { get; set; }
    public int Visitors { get; set; }
    public decimal? Cost { get; set; }
    public decimal? Conversions { get; set; }
    public string? Page { get; set; }
}

public class KeywordContent
{
    public Guid ContentId { get; set; }
    public string Site { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string Kind { get; set; } = "page";
    public string Title { get; set; } = string.Empty;
    public string Stage { get; set; } = "idea";
    public string Languages { get; set; } = "en";
    public string? BriefJson { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Url { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class KeywordSourceRun
{
    public string SourceKey { get; set; } = string.Empty;
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastOkAt { get; set; }
    public int LastCount { get; set; }
    public string? LastDetail { get; set; }
    public string? LastError { get; set; }
}
