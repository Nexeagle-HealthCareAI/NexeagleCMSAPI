using System;

namespace CMSAPI.Domain.Entities;

// AI Agents dashboard tables (1HMSDatabase db/schema/migrations/agent_monitoring_2026_10.sql), written by
// AgentMonitorController's ingest endpoints from 1HMS-NLP-Router's telemetry, read by the CMS "AI Agents" tab.

/// <summary>One decision by an NLP-router agent (/route-symptom or /chat).</summary>
public class AgentDecision
{
    public Guid DecisionId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Agent { get; set; } = string.Empty;          // route | chat
    public string? Caller { get; set; }
    public string? RequestId { get; set; }
    public string Decision { get; set; } = string.Empty;       // answer | clarify | abstain | emergency | follow_up | recommendation
    public string? RouteDecision { get; set; }
    public string? TopSpecialtyId { get; set; }
    public string? TopSpecialist { get; set; }
    public string? CandidateIds { get; set; }
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
    public DateTime ReceivedAt { get; set; }
}

/// <summary>Review-queue item: a mistake or doubtful answer for a person to correct.</summary>
public class AgentLearningItem
{
    public Guid ItemId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int Occurrences { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Agent { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string? PredictedSpecialtyId { get; set; }
    public string? ActualSpecialtyId { get; set; }
    public double? Confidence { get; set; }
    public string? ModelVersion { get; set; }
    public string Status { get; set; } = "pending";
    public string? ApprovedSpecialtyId { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? TrainingExampleId { get; set; }
    public string? ModelVersionLearned { get; set; }
    public DateTime? LearnedAt { get; set; }
}

/// <summary>One rebuild of the route model, promoted or not.</summary>
public class AgentModelVersion
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
    public string? DetailsJson { get; set; }
}
