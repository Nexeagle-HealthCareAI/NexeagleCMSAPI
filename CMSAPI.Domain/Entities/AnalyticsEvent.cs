using System;

namespace CMSAPI.Domain.Entities;

// Generic funnel/behavior event log fired by NexEagleWebsite (see easyHMSAPI's /public/track-event)
// — same table, read-only from CMSAPI's side.
public class AnalyticsEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }

    public string? SessionId { get; set; }
    public string? Mobile { get; set; }
    public Guid? DoctorId { get; set; }
    public string? SpecialtyId { get; set; }

    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }

    public string? MetadataJson { get; set; }
}
