namespace CMSAPI.Domain.Entities;

public class CmsSocialPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CampaignId { get; set; }
    public string PlatformsTargeted { get; set; } = "[]"; 
    public string PostTitle { get; set; } = string.Empty;
    public string PostCaption { get; set; } = string.Empty;
    public string MediaAssetUrls { get; set; } = "[]";
    public DateTime ScheduledPublishTime { get; set; }
    public string PublishedStatus { get; set; } = "SCHEDULED";
    public string PublishedPostIds { get; set; } = "{}";
    public string? GroqPromptUsed { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual CmsCampaign? Campaign { get; set; }
}
