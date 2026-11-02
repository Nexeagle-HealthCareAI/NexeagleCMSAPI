using System;

namespace CMSAPI.Domain.Entities;

// Page-view beacons fired by NexEagleWebsite (see easyHMSAPI's /public/track-visit) for the CMS
// "Site Visits" report — same table, read-only from CMSAPI's side.
public class WebsiteVisit
{
    public Guid VisitId { get; set; }
    public DateTime VisitedAt { get; set; }

    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }

    public string? PagePath { get; set; }
    public string? ReferrerUrl { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }

    public string? UserAgent { get; set; }
    public string? SessionId { get; set; }

    // Bot / crawler / data-centre page view, flagged by EasyHMS at write time; excluded from reports.
    public bool IsAutomated { get; set; }
    // Which website sent it: "doctordekho" | "nexeagle" (null = before 2026-10 and not inferable).
    public string? Site { get; set; }
}
