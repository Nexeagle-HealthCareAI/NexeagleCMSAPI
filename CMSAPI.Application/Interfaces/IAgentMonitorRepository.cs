using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IAgentMonitorRepository
{
    Task<IngestResult> IngestDecisionsAsync(List<DecisionIngestItem> items);
    Task<IngestResult> IngestFeedbackAsync(List<FeedbackIngestItem> items);
    Task<int> IngestModelVersionAsync(ModelVersionIngestItem item);
    Task<int> UpsertBookingCorrectionsAsync(IEnumerable<FeedbackLogItem> corrections);
    Task PruneDecisionsAsync(DateTime cutoffUtc);

    Task<AgentOverview> GetOverviewAsync(DateOnly? from, DateOnly? to, string? agent, string? caller);
    Task<PagedResult<AgentDecisionItem>> GetDecisionsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? agent, string? caller, string? decision);
    Task<DateTime?> LastDecisionReceivedAtAsync();

    Task<LearningSummary> GetLearningSummaryAsync();
    Task<PagedResult<LearningItemDto>> GetLearningItemsAsync(int page, int limit, string? status, string? source, string? search);
    Task<LearningItemDto?> ReviewAsync(Guid id, ReviewLearningItemRequest request, string? reviewer);
    Task<List<ModelVersionDto>> GetModelVersionsAsync(int limit);
}

public interface IAgentMonitorService
{
    Task<IngestResult> IngestDecisionsAsync(List<DecisionIngestItem> items);
    Task<IngestResult> IngestFeedbackAsync(List<FeedbackIngestItem> items);
    Task<IngestResult> IngestModelVersionsAsync(List<ModelVersionIngestItem> items);

    Task<AgentOverview> GetOverviewAsync(DateOnly? from, DateOnly? to, string? agent, string? caller);
    Task<PagedResult<AgentDecisionItem>> GetDecisionsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? agent, string? caller, string? decision);

    Task<LearningSummary> GetLearningSummaryAsync();
    Task<int> ScanBookingCorrectionsAsync(bool force);
    Task<PagedResult<LearningItemDto>> GetLearningItemsAsync(int page, int limit, string? status, string? source, string? search);
    Task<LearningItemDto?> ReviewAsync(Guid id, ReviewLearningItemRequest request, string? reviewer);

    Task<List<ModelVersionDto>> GetModelVersionsAsync(int limit);
    Task<AgentServiceStatus> GetServiceStatusAsync();
    /// <summary>(started, http status from the router, router message/status json)</summary>
    Task<(bool Started, int Status, string Body)> TriggerRebuildAsync();
}
