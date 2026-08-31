using System;
using System.Threading.Tasks;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces;

public interface ISalesLeadRepository
{
    Task<(List<CmsSalesLead> Items, int TotalCount)> GetLeadsPagedAsync(SalesLeadFilter filter);
    Task<CmsSalesLead?> GetByIdAsync(Guid leadId);
    Task<CmsSalesLead?> GetByMobileAsync(string mobile);
    Task<CmsSalesLead> CreateAsync(CmsSalesLead lead);
    Task<CmsSalesLead?> UpdateAsync(CmsSalesLead lead);
    Task<bool> DeleteAsync(Guid leadId);
    Task<CmsSalesLeadFollowUp> AddFollowUpAsync(CmsSalesLeadFollowUp followUp);
}
