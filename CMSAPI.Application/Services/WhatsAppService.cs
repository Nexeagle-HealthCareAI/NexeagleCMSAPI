using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Services;

public interface IWhatsAppService
{
    Task<bool> SendTemplateMessageAsync(string toPhoneNumber, string templateName, string language = "en", CancellationToken ct = default);
    Task<bool> SendInteractiveMessageAsync(string toPhoneNumber, string body, string buttonText, CancellationToken ct = default);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly string _phoneNumberId;
    private readonly string _accessToken;

    public WhatsAppService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _phoneNumberId = config["Meta:WhatsApp:PhoneNumberId"] ?? throw new InvalidOperationException("WhatsApp PhoneNumberId missing");
        _accessToken = config["Meta:WhatsApp:AccessToken"] ?? throw new InvalidOperationException("WhatsApp AccessToken missing");
        
        _httpClient.BaseAddress = new Uri($"https://graph.facebook.com/v19.0/{_phoneNumberId}/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public async Task<bool> SendTemplateMessageAsync(string toPhoneNumber, string templateName, string language = "en", CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = language }
            }
        };

        return await SendWhatsAppRequestAsync(payload, ct);
    }

    public async Task<bool> SendInteractiveMessageAsync(string toPhoneNumber, string body, string buttonText, CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = body },
                action = new
                {
                    buttons = new[]
                    {
                        new
                        {
                            type = "reply",
                            reply = new { id = "btn_1", title = buttonText }
                        }
                    }
                }
            }
        };

        return await SendWhatsAppRequestAsync(payload, ct);
    }

    private async Task<bool> SendWhatsAppRequestAsync(object payload, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("messages", content, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"WhatsApp API Error: {error}");
            return false;
        }

        return true;
    }
}
