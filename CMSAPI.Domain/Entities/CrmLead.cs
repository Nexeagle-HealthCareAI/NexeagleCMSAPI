namespace CMSAPI.Domain.Entities;

public class CrmLead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LeadNumber { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public string FacilityType { get; set; } = "HOSPITAL"; 
    public int BedCount { get; set; } = 0;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = "Bihar";
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string SourceChannel { get; set; } = "META_ADS";
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmAdId { get; set; }
    public string Status { get; set; } = "NEW"; 
    public int AiIntentScore { get; set; } = 50;
    public string? AiPersonaSummary { get; set; }
    public Guid? AssignedSalesRepId { get; set; }
    public decimal DealValue { get; set; } = 0.00m;
    public string? LostReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<CrmLeadActivity> Activities { get; set; } = new List<CrmLeadActivity>();
}
