using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CMSAPI.Application.Models;

// ── Ingest (1HMS-NLP-Router hms_router/telemetry.py -> POST agent-monitor/ingest/*) ─────────────────
public class IngestBatch<T>
{
    public List<T> Items { get; set; } = new();
}

public class DecisionIngestItem
{
    public Guid DecisionId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Agent { get; set; }
    public string? Caller { get; set; }
    public string? RequestId { get; set; }
    public string? Decision { get; set; }
    public string? RouteDecision { get; set; }
    public string? TopSpecialtyId { get; set; }
    public string? TopSpecialist { get; set; }
    public List<string>? CandidateIds { get; set; }
    public double? Confidence { get; set; }
    public double? MatchScore { get; set; }
    public bool IsEmergency { get; set; }
    public string? EmergencySource { get; set; }
    public string? ClarifyKind { get; set; }
    public bool NoMatch { get; set; }
    public string? Method { get; set; }
    public bool UsedReasoner { get; set; }
    public double? LatencyMs { get; set; }
    public string? ModelVersion { get; set; }
    public int? QueryLength { get; set; }
    public string? QueryHash { get; set; }
    public string? SessionRef { get; set; }
    public int? Turn { get; set; }
    public string? Degraded { get; set; }
    public string? ReviewText { get; set; }
    public string? Notes { get; set; }
}

public class FeedbackIngestItem
{
    public DateTime OccurredAt { get; set; }
    public string? Agent { get; set; }
    public string? Caller { get; set; }
    public string? Kind { get; set; }                 // wrong_specialist | new_symptom_phrase | correct_match
    public string? ShownSpecialist { get; set; }
    public string? SuggestedSpecialist { get; set; }
    public string? Text { get; set; }
}

public class ValidationMetricsIngest
{
    public int Rows { get; set; }
    public double Top1 { get; set; }
    public double Top3 { get; set; }
    public double Answered { get; set; }
    public double AnsweredAccuracy { get; set; }
    public double ConfidentlyWrong { get; set; }
    public double Abstain { get; set; }
    public double Clarify { get; set; }
    public double EmergencyFlagRate { get; set; }
}

public class ModelVersionIngestItem
{
    public string? ModelVersion { get; set; }
    public string? PreviousVersion { get; set; }
    public DateTime? BuiltAt { get; set; }
    public string? Trigger { get; set; }
    public bool Promoted { get; set; }
    public string? Verdict { get; set; }
    public int? TrainingRows { get; set; }
    public int? CmsRows { get; set; }
    public Dictionary<string, int>? Sources { get; set; }
    public ValidationMetricsIngest? Validation { get; set; }
    public ValidationMetricsIngest? PreviousValidation { get; set; }
    public List<string>? CmsExampleIds { get; set; }
    public double? Seconds { get; set; }
}

public class IngestResult
{
    public int Received { get; set; }
    public int Stored { get; set; }
    public int LearningItems { get; set; }
}

// ── Dashboard ───────────────────────────────────────────────────────────────────────────────────────
public class NameCount
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class AgentDailyRow
{
    public string Date { get; set; } = string.Empty;        // IST day
    public int Total { get; set; }
    public int Answer { get; set; }
    public int Clarify { get; set; }
    public int Abstain { get; set; }
    public int Emergency { get; set; }
    public int FollowUp { get; set; }
    public int Recommendation { get; set; }
    public double? AvgConfidence { get; set; }
    public double? AvgLatencyMs { get; set; }
}

public class AgentOverview
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Agent { get; set; }
    public string? Caller { get; set; }

    public int Total { get; set; }
    public int Answered { get; set; }
    public int Clarified { get; set; }
    public int Abstained { get; set; }
    public int Emergencies { get; set; }
    public int FollowUps { get; set; }
    public int Recommendations { get; set; }
    public int ReasonerUsed { get; set; }
    public int Degraded { get; set; }
    public double? AvgConfidence { get; set; }
    public double? AvgLatencyMs { get; set; }
    public double? P95LatencyMs { get; set; }
    public DateTime? LastDecisionAt { get; set; }

    public List<NameCount> ByAgent { get; set; } = new();
    public List<NameCount> ByCaller { get; set; } = new();
    public List<NameCount> TopSpecialties { get; set; } = new();
    public List<NameCount> ConfidenceBuckets { get; set; } = new();    // "0.0-0.1" ... "0.9-1.0"
    public List<NameCount> ModelVersionsInUse { get; set; } = new();
    public List<AgentDailyRow> Daily { get; set; } = new();
}

public class AgentDecisionItem
{
    public Guid DecisionId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Agent { get; set; } = string.Empty;
    public string? Caller { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? TopSpecialtyId { get; set; }
    public string? TopSpecialist { get; set; }
    public string? CandidateIds { get; set; }
    public double? Confidence { get; set; }
    public double? MatchScore { get; set; }
    public bool IsEmergency { get; set; }
    public bool UsedReasoner { get; set; }
    public double? LatencyMs { get; set; }
    public string? ModelVersion { get; set; }
    public string? ReviewText { get; set; }
    public string? Notes { get; set; }
}

// ── Learning loop ───────────────────────────────────────────────────────────────────────────────────
public class LearningItemDto
{
    public Guid ItemId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int Occurrences { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Agent { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string? PredictedSpecialtyId { get; set; }
    public string? ActualSpecialtyId { get; set; }
    public double? Confidence { get; set; }
    public string? ModelVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ApprovedSpecialtyId { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ModelVersionLearned { get; set; }
    public DateTime? LearnedAt { get; set; }
}

public class ReviewLearningItemRequest
{
    /// <summary>approve | reject</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Router specialtyId (e.g. "cardiology"); required to approve.</summary>
    public string? SpecialtyId { get; set; }
    public string? Note { get; set; }
}

public class WeekCount
{
    public string WeekStart { get; set; } = string.Empty;   // Monday, IST
    public int Found { get; set; }
    public int Reviewed { get; set; }
    public int Learned { get; set; }
}

public class LearningSummary
{
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    /// <summary>Approved, not yet inside a model in service.</summary>
    public int AwaitingRebuild { get; set; }
    /// <summary>Approved and included in a promoted model.</summary>
    public int Learned { get; set; }
    public List<NameCount> PendingBySource { get; set; } = new();
    public List<WeekCount> Weekly { get; set; } = new();
    public DateTime? LastScanAt { get; set; }
    public int BookingCorrectionsFoundLastScan { get; set; }
}

public class ModelVersionDto
{
    public Guid Id { get; set; }
    public string? ModelVersion { get; set; }
    public string? PreviousVersion { get; set; }
    public DateTime BuiltAt { get; set; }
    public string? TriggerReason { get; set; }
    public bool Promoted { get; set; }
    public string? Verdict { get; set; }
    public int? TrainingRows { get; set; }
    public int? CmsRows { get; set; }
    public int? LearnedItems { get; set; }
    public int? ValidationRows { get; set; }
    public double? Top1 { get; set; }
    public double? Top3 { get; set; }
    public double? Answered { get; set; }
    public double? AnsweredAccuracy { get; set; }
    public double? ConfidentlyWrong { get; set; }
    public double? Abstain { get; set; }
    public double? Clarify { get; set; }
    public double? EmergencyFlagRate { get; set; }
    public double? PrevTop1 { get; set; }
    public double? PrevConfidentlyWrong { get; set; }
}

public class AgentServiceStatus
{
    public bool Reachable { get; set; }
    public string? ModelVersion { get; set; }
    public DateTime? ModelBuiltAt { get; set; }
    public int? TrainingRows { get; set; }
    public string? Reasoner { get; set; }
    public JsonElement? Telemetry { get; set; }
    public JsonElement? Rebuild { get; set; }
    public JsonElement? Validation { get; set; }
    public DateTime? LastDecisionReceivedAt { get; set; }
    public string? Error { get; set; }
}
