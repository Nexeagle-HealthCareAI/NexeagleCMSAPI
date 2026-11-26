using System.Text.Json.Nodes;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// Meta's WhatsApp Cloud API for the CMS's marketing number (settings: WhatsAppMarketing__*). Every call
/// throws WaMetaException with Meta's error when Meta refuses, and WaNotConnectedException when the
/// number is not set up on this environment.
/// </summary>
public interface IWaMeta
{
    /// <summary>False until the phone number id, account id and token are set.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends a message (the body of POST /{phone-number-id}/messages, without messaging_product). Returns Meta's message id.</summary>
    Task<string> SendAsync(JsonObject message, CancellationToken ct = default);
    /// <summary>Uploads a file to send in a chat. Returns Meta's media id.</summary>
    Task<string> UploadMediaAsync(byte[] data, string mime, string fileName, CancellationToken ct = default);
    /// <summary>A file someone sent (or we sent), by Meta's media id.</summary>
    Task<WaMediaFile> DownloadMediaAsync(string mediaId, CancellationToken ct = default);
    /// <summary>Uploads a template's sample picture/video/document (Resumable Upload API). Returns the header handle.</summary>
    Task<string> UploadSampleAsync(byte[] data, string mime, string fileName, CancellationToken ct = default);

    Task<WaMetaTemplate> CreateTemplateAsync(JsonObject template, CancellationToken ct = default);
    Task<List<WaMetaTemplate>> ListTemplatesAsync(CancellationToken ct = default);
    Task DeleteTemplateAsync(string name, string? metaTemplateId, CancellationToken ct = default);

    Task<JsonObject> GetProfileAsync(CancellationToken ct = default);
    Task UpdateProfileAsync(JsonObject profile, CancellationToken ct = default);
    /// <summary>The number's name, quality and limits.</summary>
    Task<JsonObject> GetNumberAsync(CancellationToken ct = default);
    /// <summary>Meta's own daily cost and volume by country and category (pricing_analytics).</summary>
    Task<List<WaPricePoint>> PricingAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

public class WaMetaException : Exception
{
    /// <summary>Meta's error code (131047 window closed, 131049 marketing limit, 131050 stopped marketing, 132xxx template…).</summary>
    public int? Code { get; }
    public int HttpStatus { get; }

    public WaMetaException(string message, int? code = null, int httpStatus = 502, Exception? inner = null) : base(message, inner)
    {
        Code = code;
        HttpStatus = httpStatus;
    }
}

public class WaNotConnectedException : Exception
{
    public WaNotConnectedException() : base("The WhatsApp marketing number is not connected on this environment yet. Set WhatsAppMarketing__PhoneNumberId, __WabaId and __AccessToken.") { }
}

public class WaMediaFile
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string Mime { get; set; } = "application/octet-stream";
}

public class WaMetaTemplate
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RejectedReason { get; set; }
    public string? QualityScore { get; set; }
    public string? ParameterFormat { get; set; }
    /// <summary>Meta's components array as JSON.</summary>
    public string Components { get; set; } = "[]";
}

public class WaPricePoint
{
    public DateTime Start { get; set; }
    public string? Country { get; set; }
    public string? PricingType { get; set; }
    public string? PricingCategory { get; set; }
    public long Volume { get; set; }
    public decimal Cost { get; set; }
}
