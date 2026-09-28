using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Services;

public class JobsService : IJobsService
{
    private readonly IJobsRepository _repo;

    public JobsService(IJobsRepository repo)
    {
        _repo = repo;
    }

    public Task<List<JobSettingItem>> GetJobsAsync() => _repo.GetJobsAsync();

    public Task<UpdateJobActiveResult> SetJobActiveAsync(string jobName, bool isActive, string? updatedBy) =>
        _repo.SetJobActiveAsync(jobName, isActive, updatedBy);

    public Task<List<NightJobRunItem>> GetRecentRunsAsync(int take) => _repo.GetRecentRunsAsync(take);

    public Task<RunJobNowResult> RunJobNowAsync(string jobName, string? triggeredBy) =>
        _repo.RunJobNowAsync(jobName, triggeredBy);
}
