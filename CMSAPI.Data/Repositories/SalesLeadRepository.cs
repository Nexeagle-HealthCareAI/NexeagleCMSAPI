using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories;

public class SalesLeadRepository : ISalesLeadRepository
{
    private readonly CmsDbContext _db;

    public SalesLeadRepository(CmsDbContext db)
    {
        _db = db;
    }

    public async Task<(List<CmsSalesLead> Items, int TotalCount)> GetLeadsPagedAsync(SalesLeadFilter filter)
    {
        var query = _db.CmsSalesLeads
            .Include(l => l.AssignedTo)
            .Include(l => l.FollowUps)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Stage))
            query = query.Where(l => l.Stage == filter.Stage);

        if (!string.IsNullOrWhiteSpace(filter.Priority))
            query = query.Where(l => l.Priority == filter.Priority);

        if (filter.AssignedToUserId.HasValue)
            query = query.Where(l => l.AssignedToUserId == filter.AssignedToUserId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(l =>
                l.HospitalName.ToLower().Contains(s) ||
                (l.City != null && l.City.ToLower().Contains(s)) ||
                (l.ContactName != null && l.ContactName.ToLower().Contains(s)));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(l => l.UpdatedAt)
            .Skip((filter.Page - 1) * filter.Limit)
            .Take(filter.Limit)
            .ToListAsync();

        return (items, total);
    }

    public async Task<CmsSalesLead?> GetByIdAsync(Guid leadId)
    {
        return await _db.CmsSalesLeads
            .Include(l => l.AssignedTo)
            .Include(l => l.FollowUps.OrderByDescending(f => f.CreatedAt))
            .FirstOrDefaultAsync(l => l.LeadId == leadId);
    }

    public async Task<CmsSalesLead?> GetByMobileAsync(string mobile)
    {
        return await _db.CmsSalesLeads
            .Include(l => l.AssignedTo)
            .Include(l => l.FollowUps.OrderByDescending(f => f.CreatedAt))
            .FirstOrDefaultAsync(l => l.Mobile == mobile);
    }

    public async Task<CmsSalesLead> CreateAsync(CmsSalesLead lead)
    {
        lead.LeadId = Guid.NewGuid();
        lead.CreatedAt = DateTime.UtcNow;
        lead.UpdatedAt = DateTime.UtcNow;
        _db.CmsSalesLeads.Add(lead);
        await _db.SaveChangesAsync();
        return lead;
    }

    public async Task<CmsSalesLead?> UpdateAsync(CmsSalesLead lead)
    {
        lead.UpdatedAt = DateTime.UtcNow;
        _db.CmsSalesLeads.Update(lead);
        await _db.SaveChangesAsync();
        return lead;
    }

    public async Task<bool> DeleteAsync(Guid leadId)
    {
        var lead = await _db.CmsSalesLeads.FindAsync(leadId);
        if (lead == null) return false;
        _db.CmsSalesLeads.Remove(lead);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<CmsSalesLeadFollowUp> AddFollowUpAsync(CmsSalesLeadFollowUp followUp)
    {
        followUp.FollowUpId = Guid.NewGuid();
        followUp.CreatedAt = DateTime.UtcNow;
        _db.CmsSalesLeadFollowUps.Add(followUp);

        // Bump UpdatedAt on the parent lead so it bubbles up in the list
        var lead = await _db.CmsSalesLeads.FindAsync(followUp.LeadId);
        if (lead != null) lead.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return followUp;
    }
}
