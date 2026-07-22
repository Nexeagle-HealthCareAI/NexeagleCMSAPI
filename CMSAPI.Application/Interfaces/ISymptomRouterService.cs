using System;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface ISymptomRouterService
{
    Task<PagedResult<TrainingExampleItem>> GetTrainingExamplesAsync(
        int page, int limit, string? specialist, string? search);

    // Returns null (caller maps to 400) when Specialist isn't one of the 32 known labels.
    Task<TrainingExampleItem?> AddTrainingExampleAsync(UpsertTrainingExampleRequest request, string? createdBy);
    Task<TrainingExampleItem?> UpdateTrainingExampleAsync(Guid id, UpsertTrainingExampleRequest request);
    Task<bool> DeleteTrainingExampleAsync(Guid id);

    Task<PagedResult<FeedbackLogItem>> GetFeedbackLogAsync(
        int page, int limit, DateOnly? from, DateOnly? to, bool? correctionsOnly);

    Task<ModelInfoDto?> GetModelInfoAsync();
    Task<bool> TriggerRetrainAsync();
}
