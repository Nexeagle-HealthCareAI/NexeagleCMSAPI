using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IJobsService
{
    Task<List<JobSettingItem>> GetJobsAsync();
    Task<UpdateJobActiveResult> SetJobActiveAsync(string jobName, bool isActive, string? updatedBy);
    Task<List<NightJobRunItem>> GetRecentRunsAsync(int take);
    Task<RunJobNowResult> RunJobNowAsync(string jobName, string? triggeredBy);
}
