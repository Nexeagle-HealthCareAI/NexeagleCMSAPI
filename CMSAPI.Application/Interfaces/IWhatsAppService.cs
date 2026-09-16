namespace CMSAPI.Application.Interfaces;

// Moved here from CMSAPI.Application.Services.WhatsAppService — interfaces belong in the
// Interfaces namespace, consistent with every other service interface in this project.
public interface IWhatsAppService
{
    Task<bool> SendTemplateMessageAsync(string toPhoneNumber, string templateName, string language = "en", object[]? components = null, CancellationToken ct = default);
    Task<bool> SendInteractiveMessageAsync(string toPhoneNumber, string body, string buttonText, CancellationToken ct = default);
}
