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

// 1Rad keeps its own database (1RadDb), so 1Rad payment approvals go over HTTP to
// 1RadAPI's service-key endpoints (subscriptions/admin/payment-requests,
// approve-payment/{id}, reject-payment/{id}) — same key and base URL as
// RadHospitalsService (RadApi:BaseUrl, ServiceAuth:RadServiceKey).
public class RadSubscriptionsService : IRadSubscriptionsService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RadSubscriptionsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RadSubscriptionsService(HttpClient httpClient, IConfiguration configuration, ILogger<RadSubscriptionsService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    private (string? BaseUrl, string? Key) Settings() =>
        (_configuration["RadApi:BaseUrl"]?.TrimEnd('/'), _configuration["ServiceAuth:RadServiceKey"]);

    public async Task<List<RadPaymentRequestItem>?> GetPaymentRequestsAsync()
    {
        var (baseUrl, key) = Settings();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("1Rad payment requests skipped: RadApi:BaseUrl or ServiceAuth:RadServiceKey isn't configured.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/subscriptions/admin/payment-requests");
        request.Headers.Add("X-Service-Key", key);
        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("1RadAPI payment-requests returned {StatusCode}.", response.StatusCode);
                return null;
            }
            var wrapper = await response.Content.ReadFromJsonAsync<Wrapper>(JsonOptions);
            return wrapper?.Data ?? new List<RadPaymentRequestItem>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach 1RadAPI payment-requests.");
            return null;
        }
    }

    public Task<RadReviewResult> ApproveAsync(Guid requestId, string? reviewNote) =>
        PostAsync($"/api/v1/subscriptions/approve-payment/{requestId}", new { reviewNote });

    public Task<RadReviewResult> RejectAsync(Guid requestId, string reason) =>
        PostAsync($"/api/v1/subscriptions/reject-payment/{requestId}", new { reason });

    private async Task<RadReviewResult> PostAsync(string path, object body)
    {
        var (baseUrl, key) = Settings();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
            return new RadReviewResult { Success = false, StatusCode = 503, Error = "1Rad connection isn't configured (RadApi:BaseUrl / ServiceAuth:RadServiceKey)." };

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Service-Key", key);
        try
        {
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return new RadReviewResult { Success = true, StatusCode = (int)response.StatusCode };

            string? error = null;
            try
            {
                var err = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
                error = err?.Error;
            }
            catch { /* non-JSON body */ }

            _logger.LogWarning("1RadAPI {Path} returned {StatusCode}: {Error}", path, response.StatusCode, error);
            return new RadReviewResult
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Error = error ?? (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "1Rad refused the service key. Check ServiceAuth:RadServiceKey matches 1Rad's CMS_SERVICE_KEY."
                    : $"1Rad returned {(int)response.StatusCode}.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach 1RadAPI {Path}.", path);
            return new RadReviewResult { Success = false, StatusCode = 502, Error = "Couldn't reach 1Rad." };
        }
    }

    private class Wrapper
    {
        public bool Success { get; set; }
        public List<RadPaymentRequestItem> Data { get; set; } = new();
    }

    private class ErrorBody
    {
        public string? Error { get; set; }
    }
}
