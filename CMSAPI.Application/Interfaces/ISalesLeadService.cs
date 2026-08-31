using System;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface ISalesLeadService
{
    Task<SalesLeadListResult> GetLeadsAsync(SalesLeadFilter filter);
    Task<SalesLeadDetail?> GetLeadDetailAsync(Guid leadId);
    Task<SalesLeadDetail?> GetLeadByMobileAsync(string mobile);
    Task<SalesLeadDetail> CreateLeadAsync(CreateSalesLeadRequest request, Guid currentUserId, string currentUserName);
    Task<SalesLeadDetail?> UpdateLeadAsync(Guid leadId, UpdateSalesLeadRequest request);
    Task<bool> DeleteLeadAsync(Guid leadId);
    Task<SalesLeadFollowUpDto?> AddFollowUpAsync(Guid leadId, AddFollowUpRequest request, Guid currentUserId, string currentUserName);
}
