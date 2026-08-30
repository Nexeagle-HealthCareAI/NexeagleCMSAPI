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

    public Guid? AssignedToUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public CmsUser? AssignedTo { get; set; }
    public ICollection<CmsSalesLeadFollowUp> FollowUps { get; set; } = new List<CmsSalesLeadFollowUp>();
}
