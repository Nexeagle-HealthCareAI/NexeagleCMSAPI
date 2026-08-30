using System;

namespace CMSAPI.Domain.Entities;

/// <summary>
/// A single follow-up interaction logged against a CmsSalesLead.
/// ActivityType: Call | WhatsApp | Email | Meeting | Note
/// </summary>
public class CmsSalesLeadFollowUp
{
    public Guid FollowUpId { get; set; }
    public Guid LeadId { get; set; }

    public Guid? AuthorUserId { get; set; }

    /// <summary>Snapshot of the author's name at the time of writing, so it survives user renames.</summary>
    public string? AuthorName { get; set; }

    /// <summary>Call | WhatsApp | Email | Meeting | Note</summary>
    public string ActivityType { get; set; } = "Note";

    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public CmsSalesLead? Lead { get; set; }
}
