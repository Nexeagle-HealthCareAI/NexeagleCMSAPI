using System;

namespace CMSAPI.Application.Models;

// ── Training data editor (CMS "NLP" tab) ────────────────────────────────────
public class TrainingExampleItem
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Specialist { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class UpsertTrainingExampleRequest
{
    public string Text { get; set; } = string.Empty;
    public string Specialist { get; set; } = string.Empty;
    public string? Type { get; set; }
}

// ── Feedback log (search -> booking correlation over AnalyticsEvents) ───────
public class FeedbackLogItem
{
    public DateTime OccurredAt { get; set; }
    public string? SessionId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string? PredictedSpecialtyId { get; set; }
    public string? Method { get; set; }
    public double? Confidence { get; set; }
    public bool HasBooking { get; set; }
    public string? ActualBookedSpecialtyId { get; set; }
    public bool WasCorrection { get; set; }
}

// ── Model info (proxied from the NLP service's own /model-info) ────────────
// The router in service today (hms_router) reports modelVersion, trainedAt (Unix seconds),
// trainingRows, emergencyRows and sources (rows per training file); it does not publish validation
// metrics. The older classifier's LastRetrainedAt / TrainingRowCount / ValidationMetrics are kept
// for compatibility and filled from the new fields where possible (SymptomRouterService).
public class ModelInfoDto
{
    public string? ModelVersion { get; set; }
    public DateTime? LastRetrainedAt { get; set; }
    public int? TrainingRowCount { get; set; }
    public int? ValidationRowCount { get; set; }
    public ValidationMetricsDto? ValidationMetrics { get; set; }

    public double? TrainedAt { get; set; }
    public int? TrainingRows { get; set; }
    public int? EmergencyRows { get; set; }
    public Dictionary<string, int>? Sources { get; set; }
    public string? Reasoner { get; set; }
}

public class ValidationMetricsDto
{
    public double Top1Accuracy { get; set; }
    public double TopKAccuracy { get; set; }
    public double ConfidentlyWrongRate { get; set; }
}
