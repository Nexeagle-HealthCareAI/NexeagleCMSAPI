using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services;

// Calls 1RadAPI's service-key-gated /api/v1/hospitals/admin/all -- a separate product with its
// own database (1RadDb), not a shared physical catalog like easyHMSDatabase/CMSDatabase, so this
// goes over HTTP rather than a second DbContext. See RadApi:BaseUrl / ServiceAuth:RadServiceKey.
public class RadHospitalsService : IRadHospitalsService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RadHospitalsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public RadHospitalsService(HttpClient httpClient, IConfiguration configuration, ILogger<RadHospitalsService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<RadHospitalItem>> GetAllAsync()
    {
        var baseUrl = _configuration["RadApi:BaseUrl"]?.TrimEnd('/');
        var serviceKey = _configuration["ServiceAuth:RadServiceKey"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(serviceKey))
        {
            _logger.LogWarning("Rad hospitals lookup skipped: RadApi:BaseUrl or ServiceAuth:RadServiceKey isn't configured.");
            return new List<RadHospitalItem>();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/hospitals/admin/all");
        request.Headers.Add("X-Service-Key", serviceKey);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("1RadAPI admin/all returned {StatusCode}.", response.StatusCode);
                return new List<RadHospitalItem>();
            }

            var wrapper = await response.Content.ReadFromJsonAsync<RadAdminResponse>(JsonOptions);
            return wrapper?.Data ?? new List<RadHospitalItem>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach 1RadAPI admin/all.");
            return new List<RadHospitalItem>();
        }
    }

    private class RadAdminResponse
    {
        public bool Success { get; set; }
        public List<RadHospitalItem> Data { get; set; } = new();
    }
}
