using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IHospitalOperationsRepository
{
    // toDateExclusive: the day AFTER the last day to include (caller adds 1 day to the
    // inclusive end date) -- matches the >= from / < to convention every date-range query in
    // this codebase already uses.
    Task<List<HospitalOperationsSummaryItem>> GetSummaryAsync(DateTime fromDate, DateTime toDateExclusive);
}
