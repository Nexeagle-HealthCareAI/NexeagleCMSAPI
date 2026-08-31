namespace CMSAPI.Domain.Entities;

public class CmsCampaign
{
    public Guid CampaignId { get; set; } = Guid.NewGuid();
    public string CampaignName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? ExternalCampaignId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal BudgetAllocated { get; set; } = 0.00m;
    public decimal ActualSpend { get; set; } = 0.00m;
    public string? TargetSpecialty { get; set; }
    public string TargetGeography { get; set; } = "Bihar";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
