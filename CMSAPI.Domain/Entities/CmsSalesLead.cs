using System;

namespace CMSAPI.Domain.Entities;

/// <summary>
/// A prospective B2B hospital client tracked in the CMS marketing team's sales pipeline.
/// Completely separate from HospitalLead (patient leads inside already-onboarded hospitals).
/// Stage lifecycle: New → Contacted → Demo Scheduled → Demo Done → Negotiation → Closed Won / Closed Lost.
/// </summary>
public class CmsSalesLead
{
    public Guid LeadId { get; set; }

    public string HospitalName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }

    /// <summary>Cold Call | WhatsApp | Website | Referral | Event | Partner | Other</summary>
    public string Source { get; set; } = "Manual";

    /// <summary>New | Contacted | Demo Scheduled | Demo Done | Negotiation | Closed Won | Closed Lost</summary>
    public string Stage { get; set; } = "New";

    /// <summary>High | Medium | Low</summary>
    public string Priority { get; set; } = "Medium";

    public string? Notes { get; set; }
    public string? LeadNumber { get; set; }
    public string FacilityType { get; set; } = "HOSPITAL";
    public int BedCount { get; set; } = 0;
    
    // UTM / Attribution
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmAdId { get; set; }
    
    // AI Metrics
    public int AiIntentScore { get; set; } = 50;
    public string? AiPersonaSummary { get; set; }
    
    // Sales Data
    public decimal DealValue { get; set; } = 0.00m;
    public string? LostReason { get; set; }

    public Guid? AssignedToUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public CmsUser? AssignedTo { get; set; }
    public ICollection<CmsSalesLeadFollowUp> FollowUps { get; set; } = new List<CmsSalesLeadFollowUp>();
}
