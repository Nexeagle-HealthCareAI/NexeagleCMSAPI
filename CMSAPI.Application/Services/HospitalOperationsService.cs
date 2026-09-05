using System;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Services;

public class HospitalOperationsService : IHospitalOperationsService
{
    private readonly IHospitalOperationsRepository _repo;

    public HospitalOperationsService(IHospitalOperationsRepository repo)
    {
        _repo = repo;
    }

    public async Task<HospitalOperationsSummaryResponse> GetSummaryAsync(DateTime fromDate, DateTime toDate)
    {
        // Inclusive end date from the caller (a calendar day picker), normalized to the
        // exclusive >= from / < to+1day convention the repository's raw SQL uses.
        var from = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);

        if (toExclusive <= from)
            return new HospitalOperationsSummaryResponse { Success = false, Message = "toDate must not be before fromDate.", FromDate = from, ToDate = toDate.Date };

        var items = await _repo.GetSummaryAsync(from, toExclusive);

        return new HospitalOperationsSummaryResponse
        {
            Success = true,
            FromDate = from,
            ToDate = toDate.Date,
            Hospitals = items,
        };
    }
}
