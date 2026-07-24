using System;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface ISymptomRouterRepository
{
    Task<PagedResult<TrainingExampleItem>> GetTrainingExamplesAsync(
        int page, int limit, string? specialist, string? search);

    Task<TrainingExampleItem> AddTrainingExampleAsync(UpsertTrainingExampleRequest request, string? createdBy);
    Task<TrainingExampleItem?> UpdateTrainingExampleAsync(Guid id, UpsertTrainingExampleRequest request);
    Task<bool> DeleteTrainingExampleAsync(Guid id);

    Task<PagedResult<FeedbackLogItem>> GetFeedbackLogAsync(
        int page, int limit, DateOnly? from, DateOnly? to, bool? correctionsOnly);
}
