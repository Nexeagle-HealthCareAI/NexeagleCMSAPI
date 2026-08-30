namespace CMSAPI.Domain.Entities;

public class CrmWhatsappTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TemplateName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string HeaderType { get; set; } = "VIDEO";
    public string? HeaderMediaUrl { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public string? FooterText { get; set; }
    public string ButtonsConfig { get; set; } = "[]";
    public string MetaApprovalStatus { get; set; } = "APPROVED";
    public bool IsActive { get; set; } = true;
}
