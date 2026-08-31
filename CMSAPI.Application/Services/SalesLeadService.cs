using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Services;

public class SalesLeadService : ISalesLeadService
{
    private readonly ISalesLeadRepository _repo;

    public SalesLeadService(ISalesLeadRepository repo)
    {
        _repo = repo;
    }

    public async Task<SalesLeadListResult> GetLeadsAsync(SalesLeadFilter filter)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.Limit < 1) filter.Limit = 20;

        var (items, total) = await _repo.GetLeadsPagedAsync(filter);

        return new SalesLeadListResult
        {
            Data = items.Select(ToSummary).ToList(),
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)filter.Limit),
            CurrentPage = filter.Page
        };
    }

    public async Task<SalesLeadDetail?> GetLeadDetailAsync(Guid leadId)
    {
        var lead = await _repo.GetByIdAsync(leadId);
        return lead == null ? null : ToDetail(lead);
    }

    public async Task<SalesLeadDetail?> GetLeadByMobileAsync(string mobile)
    {
        var lead = await _repo.GetByMobileAsync(mobile);
        return lead == null ? null : ToDetail(lead);
    }

    public async Task<SalesLeadDetail> CreateLeadAsync(CreateSalesLeadRequest req, Guid currentUserId, string currentUserName)
    {
        var lead = new CmsSalesLead
        {
            HospitalName   = req.HospitalName,
            ContactName    = req.ContactName,
            Mobile         = req.Mobile,
            Email          = req.Email,
            City           = req.City,
            State          = req.State,
            Source         = req.Source,
            Stage          = req.Stage,
            Priority       = req.Priority,
            Notes          = req.Notes,
            LeadNumber     = req.LeadNumber,
            FacilityType   = req.FacilityType ?? "HOSPITAL",
            BedCount       = req.BedCount,
            UtmSource      = req.UtmSource,
            UtmMedium      = req.UtmMedium,
            UtmCampaign    = req.UtmCampaign,
            UtmAdId        = req.UtmAdId,
            AiIntentScore  = req.AiIntentScore,
            AiPersonaSummary = req.AiPersonaSummary,
            DealValue      = req.DealValue,
            LostReason     = req.LostReason,
            AssignedToUserId = req.AssignedToUserId,
            CreatedByUserId  = currentUserId,
        };

        var created = await _repo.CreateAsync(lead);

        // Re-fetch with navigation so we can map assignee name
        var detail = await _repo.GetByIdAsync(created.LeadId);
        return ToDetail(detail!);
    }

    public async Task<SalesLeadDetail?> UpdateLeadAsync(Guid leadId, UpdateSalesLeadRequest req)
    {
        var lead = await _repo.GetByIdAsync(leadId);
        if (lead == null) return null;

        if (req.HospitalName != null) lead.HospitalName = req.HospitalName;
        if (req.ContactName  != null) lead.ContactName  = req.ContactName;
        if (req.Mobile       != null) lead.Mobile       = req.Mobile;
        if (req.Email        != null) lead.Email        = req.Email;
        if (req.City         != null) lead.City         = req.City;
        if (req.State        != null) lead.State        = req.State;
        if (req.Source       != null) lead.Source       = req.Source;
        if (req.Stage        != null) lead.Stage        = req.Stage;
        if (req.Priority     != null) lead.Priority     = req.Priority;
        if (req.Notes        != null) lead.Notes        = req.Notes;
        if (req.FacilityType != null) lead.FacilityType = req.FacilityType;
        if (req.BedCount.HasValue) lead.BedCount = req.BedCount.Value;
        if (req.AiIntentScore.HasValue) lead.AiIntentScore = req.AiIntentScore.Value;
        if (req.AiPersonaSummary != null) lead.AiPersonaSummary = req.AiPersonaSummary;
        if (req.DealValue.HasValue) lead.DealValue = req.DealValue.Value;
        if (req.LostReason != null) lead.LostReason = req.LostReason;
        if (req.AssignedToUserId.HasValue) lead.AssignedToUserId = req.AssignedToUserId;

        await _repo.UpdateAsync(lead);

        var updated = await _repo.GetByIdAsync(leadId);
        return updated == null ? null : ToDetail(updated);
    }

    public Task<bool> DeleteLeadAsync(Guid leadId)
        => _repo.DeleteAsync(leadId);

    public async Task<SalesLeadFollowUpDto?> AddFollowUpAsync(Guid leadId, AddFollowUpRequest req, Guid currentUserId, string currentUserName)
    {
        var lead = await _repo.GetByIdAsync(leadId);
        if (lead == null) return null;

        var followUp = new CmsSalesLeadFollowUp
        {
            LeadId       = leadId,
            AuthorUserId = currentUserId,
            AuthorName   = currentUserName,
            ActivityType = req.ActivityType,
            Notes        = req.Notes,
            Direction    = req.Direction,
            TemplateName = req.TemplateName,
            MediaUrl     = req.MediaUrl,
        };

        var saved = await _repo.AddFollowUpAsync(followUp);
        return ToFollowUpDto(saved);
    }

    // ── Mappers ────────────────────────────────────────────────────────────

    private static SalesLeadSummary ToSummary(CmsSalesLead l) => new()
    {
        LeadId           = l.LeadId,
        HospitalName     = l.HospitalName,
        ContactName      = l.ContactName,
        Mobile           = l.Mobile,
        City             = l.City,
        State            = l.State,
        Source           = l.Source,
        Stage            = l.Stage,
        Priority         = l.Priority,
        AssignedToUserId = l.AssignedToUserId?.ToString(),
        AssignedToName   = l.AssignedTo?.FullName,
        FollowUpCount    = l.FollowUps?.Count ?? 0,
        LastFollowUpAt   = l.FollowUps?.OrderByDescending(f => f.CreatedAt).FirstOrDefault()?.CreatedAt,
        CreatedAt        = l.CreatedAt,
        UpdatedAt        = l.UpdatedAt,
        LeadNumber       = l.LeadNumber,
        AiIntentScore    = l.AiIntentScore,
        DealValue        = l.DealValue,
    };

    private static SalesLeadDetail ToDetail(CmsSalesLead l)
    {
        var summary = ToSummary(l);
        return new SalesLeadDetail
        {
            LeadId           = summary.LeadId,
            HospitalName     = summary.HospitalName,
            ContactName      = summary.ContactName,
            Mobile           = summary.Mobile,
            City             = summary.City,
            State            = summary.State,
            Source           = summary.Source,
            Stage            = summary.Stage,
            Priority         = summary.Priority,
            AssignedToUserId = summary.AssignedToUserId,
            AssignedToName   = summary.AssignedToName,
            FollowUpCount    = summary.FollowUpCount,
            LastFollowUpAt   = summary.LastFollowUpAt,
            CreatedAt        = summary.CreatedAt,
            UpdatedAt        = summary.UpdatedAt,
            Email            = l.Email,
            Notes            = l.Notes,
            CreatedByUserId  = l.CreatedByUserId?.ToString(),
            FacilityType     = l.FacilityType,
            BedCount         = l.BedCount,
            UtmSource        = l.UtmSource,
            UtmMedium        = l.UtmMedium,
            UtmCampaign      = l.UtmCampaign,
            UtmAdId          = l.UtmAdId,
            AiPersonaSummary = l.AiPersonaSummary,
            LostReason       = l.LostReason,
            FollowUps        = l.FollowUps?.Select(ToFollowUpDto).ToList() ?? new(),
        };
    }

    private static SalesLeadFollowUpDto ToFollowUpDto(CmsSalesLeadFollowUp f) => new()
    {
        FollowUpId   = f.FollowUpId,
        ActivityType = f.ActivityType,
        Notes        = f.Notes,
        AuthorName   = f.AuthorName,
        Direction    = f.Direction,
        TemplateName = f.TemplateName,
        MediaUrl     = f.MediaUrl,
        WhatsappMessageId = f.WhatsappMessageId,
        Status       = f.Status,
        CreatedAt    = f.CreatedAt,
    };
}
