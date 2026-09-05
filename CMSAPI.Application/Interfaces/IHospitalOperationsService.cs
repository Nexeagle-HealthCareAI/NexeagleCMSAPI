using System;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IHospitalOperationsService
{
    Task<HospitalOperationsSummaryResponse> GetSummaryAsync(DateTime fromDate, DateTime toDate);
}
