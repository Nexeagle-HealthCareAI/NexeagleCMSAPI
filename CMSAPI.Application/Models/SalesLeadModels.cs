using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// ── Query DTOs ──────────────────────────────────────────────────────────────

/// <summary>One row in the leads pipeline list.</summary>
public class SalesLeadSummary
{
    public Guid LeadId { get; set; }
    public string HospitalName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Mobile { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public int FollowUpCount { get; set; }
    public DateTime? LastFollowUpAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string? LeadNumber { get; set; }
    public int AiIntentScore { get; set; }
    public decimal DealValue { get; set; }
}

/// <summary>Full detail for the Lead Detail Drawer.</summary>
public class SalesLeadDetail : SalesLeadSummary
{
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public string? FacilityType { get; set; }
    public int BedCount { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmAdId { get; set; }
    public string? AiPersonaSummary { get; set; }
    public string? LostReason { get; set; }
    public List<SalesLeadFollowUpDto> FollowUps { get; set; } = new();
}

public class SalesLeadFollowUpDto
{
    public Guid FollowUpId { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
    public string Direction { get; set; } = "OUTBOUND";
    public string? TemplateName { get; set; }
    public string? MediaUrl { get; set; }
    public string? WhatsappMessageId { get; set; }
    public string Status { get; set; } = "DELIVERED";
    public DateTime CreatedAt { get; set; }
}

public class SalesLeadListResult
{
    public List<SalesLeadSummary> Data { get; set; } = new();
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; }
}

// ── Command DTOs ────────────────────────────────────────────────────────────

public class CreateSalesLeadRequest
{
    public string HospitalName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string Source { get; set; } = "Manual";
    public string Stage { get; set; } = "New";
    public string Priority { get; set; } = "Medium";
    public string? Notes { get; set; }
    public Guid? AssignedToUserId { get; set; }

    public string? LeadNumber { get; set; }
    public string FacilityType { get; set; } = "HOSPITAL";
    public int BedCount { get; set; } = 0;
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmAdId { get; set; }
    public int AiIntentScore { get; set; } = 50;
    public string? AiPersonaSummary { get; set; }
    public decimal DealValue { get; set; } = 0.00m;
    public string? LostReason { get; set; }
}

public class UpdateSalesLeadRequest
{
    public string? HospitalName { get; set; }
    public string? ContactName { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Source { get; set; }
    public string? Stage { get; set; }
    public string? Priority { get; set; }
    public string? Notes { get; set; }
    public Guid? AssignedToUserId { get; set; }
    
    public string? FacilityType { get; set; }
    public int? BedCount { get; set; }
    public int? AiIntentScore { get; set; }
    public string? AiPersonaSummary { get; set; }
    public decimal? DealValue { get; set; }
    public string? LostReason { get; set; }
}

public class AddFollowUpRequest
{
    public string ActivityType { get; set; } = "Note";
    public string Notes { get; set; } = string.Empty;
    public string Direction { get; set; } = "OUTBOUND";
    public string? TemplateName { get; set; }
    public string? MediaUrl { get; set; }
}

// ── Filter ──────────────────────────────────────────────────────────────────

public class SalesLeadFilter
{
    public string? Stage { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 20;
}

public class SendTemplateRequest
{
    public string TemplateName { get; set; } = string.Empty;
}
