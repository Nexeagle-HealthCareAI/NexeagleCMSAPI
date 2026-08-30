namespace CMSAPI.Domain.Entities;

public class CrmLeadActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeadId { get; set; }
    public string ActivityType { get; set; } = "NOTE";
    public string Direction { get; set; } = "OUTBOUND";
    public string MessageBody { get; set; } = string.Empty;
    public string? TemplateName { get; set; }
    public string? MediaUrl { get; set; }
    public string? WhatsappMessageId { get; set; }
    public string Status { get; set; } = "DELIVERED";
    public Guid? PerformedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual CrmLead Lead { get; set; } = null!;
}
