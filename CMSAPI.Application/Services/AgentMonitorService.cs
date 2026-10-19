using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services;

// AI Agents tab: storage via IAgentMonitorRepository, plus the calls to 1HMS-NLP-Router itself
// (GET /model-info, POST/GET /admin/rebuild) with the router API key NlpRouter:ApiKey (scopes route|admin).
public class AgentMonitorService : IAgentMonitorService
{
    private static readonly TimeSpan ScanEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(6);
    private const int RetentionDays = 365;

    private readonly IAgentMonitorRepository _repo;
    private readonly ISymptomRouterRepository _routerRepo;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AgentMonitorService> _logger;

    public AgentMonitorService(IAgentMonitorRepository repo, ISymptomRouterRepository routerRepo, HttpClient http,
        IConfiguration config, IMemoryCache cache, ILogger<AgentMonitorService> logger)
    {
        _repo = repo;
        _routerRepo = routerRepo;
        _http = http;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    // ── ingest ──────────────────────────────────────────────────────────────────────────────────────
    public async Task<IngestResult> IngestDecisionsAsync(List<DecisionIngestItem> items)
    {
        var result = await _repo.IngestDecisionsAsync(items);
        await PruneIfDueAsync();
        return result;
    }

    public Task<IngestResult> IngestFeedbackAsync(List<FeedbackIngestItem> items) => _repo.IngestFeedbackAsync(items);

    public async Task<IngestResult> IngestModelVersionsAsync(List<ModelVersionIngestItem> items)
    {
        var learned = 0;
        foreach (var m in items) learned += await _repo.IngestModelVersionAsync(m);
        return new IngestResult { Received = items.Count, Stored = items.Count, LearningItems = learned };
    }

    private async Task PruneIfDueAsync()
    {
        if (_cache.TryGetValue("agent-monitor:pruned", out _)) return;
        _cache.Set("agent-monitor:pruned", true, PruneEvery);
        try
        {
            await _repo.PruneDecisionsAsync(DateTime.UtcNow.AddDays(-RetentionDays));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent decision retention cleanup failed");
        }
    }

    // ── dashboard ───────────────────────────────────────────────────────────────────────────────────
    public Task<AgentOverview> GetOverviewAsync(DateOnly? from, DateOnly? to, string? agent, string? caller)
        => _repo.GetOverviewAsync(from, to, agent, caller);

    public Task<PagedResult<AgentDecisionItem>> GetDecisionsAsync(int page, int limit, DateOnly? from, DateOnly? to,
        string? agent, string? caller, string? decision)
        => _repo.GetDecisionsAsync(page, limit, from, to, agent, caller, decision);

    // ── learning loop ───────────────────────────────────────────────────────────────────────────────
    public async Task<LearningSummary> GetLearningSummaryAsync()
    {
        await ScanBookingCorrectionsAsync(force: false);
        var summary = await _repo.GetLearningSummaryAsync();
        if (_cache.TryGetValue("agent-monitor:last-scan", out (DateTime At, int Found) last))
        {
            summary.LastScanAt = last.At;
            summary.BookingCorrectionsFoundLastScan = last.Found;
        }
        return summary;
    }

    // Doctor Dekho bookings that landed on a specialty the router did not suggest (Feedback Log
    // corrections, last 30 days) become review items. Runs at most every 30 minutes unless forced.
    public async Task<int> ScanBookingCorrectionsAsync(bool force)
    {
        if (!force && _cache.TryGetValue("agent-monitor:last-scan", out (DateTime At, int Found) _)) return 0;
        var found = 0;
        try
        {
            var today = TrackingTime.TodayIst();
            var corrections = new List<FeedbackLogItem>();
            for (var page = 1; page <= 20; page++)
            {
                var result = await _routerRepo.GetFeedbackLogAsync(page, 500, today.AddDays(-30), today, true);
                corrections.AddRange(result.Data);
                if (page >= result.Pagination.TotalPages) break;
            }
            found = await _repo.UpsertBookingCorrectionsAsync(corrections);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Booking-correction scan failed");
        }
        _cache.Set("agent-monitor:last-scan", (DateTime.UtcNow, found), ScanEvery);
        return found;
    }

    public Task<PagedResult<LearningItemDto>> GetLearningItemsAsync(int page, int limit, string? status, string? source, string? search)
        => _repo.GetLearningItemsAsync(page, limit, status, source, search);

    public Task<LearningItemDto?> ReviewAsync(Guid id, ReviewLearningItemRequest request, string? reviewer)
        => _repo.ReviewAsync(id, request, reviewer);

    // ── models / router ─────────────────────────────────────────────────────────────────────────────
    public Task<List<ModelVersionDto>> GetModelVersionsAsync(int limit) => _repo.GetModelVersionsAsync(limit);

    private HttpRequestMessage RouterRequest(HttpMethod method, string path)
    {
        var baseUrl = (_config["NlpRouter:BaseUrl"] ?? "").TrimEnd('/');
        var req = new HttpRequestMessage(method, $"{baseUrl}{path}");
        var key = _config["NlpRouter:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("<")) req.Headers.Add("X-Api-Key", key);   // "<set-via-env...>" placeholder = unset
        return req;
    }

    public async Task<AgentServiceStatus> GetServiceStatusAsync()
    {
        var status = new AgentServiceStatus { LastDecisionReceivedAt = await _repo.LastDecisionReceivedAtAsync() };
        if (string.IsNullOrWhiteSpace(_config["NlpRouter:BaseUrl"]))
        {
            status.Error = "NlpRouter:BaseUrl is not configured.";
            return status;
        }
        try
        {
            using var resp = await _http.SendAsync(RouterRequest(HttpMethod.Get, "/model-info"));
            if (!resp.IsSuccessStatusCode)
            {
                status.Error = $"Router answered {(int)resp.StatusCode}" + ((int)resp.StatusCode == 401 ? " (set NlpRouter:ApiKey)" : "");
                return status;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            status.Reachable = true;
            status.ModelVersion = root.TryGetProperty("modelVersion", out var mv) ? mv.GetString() : null;
            if (root.TryGetProperty("trainedAt", out var ta) && ta.TryGetDouble(out var secs))
                status.ModelBuiltAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(secs * 1000)).UtcDateTime;
            status.TrainingRows = root.TryGetProperty("trainingRows", out var tr) && tr.TryGetInt32(out var n) ? n : null;
            status.Reasoner = root.TryGetProperty("reasoner", out var rs) ? rs.GetString() : null;
            if (root.TryGetProperty("telemetry", out var tel)) status.Telemetry = tel.Clone();
            if (root.TryGetProperty("rebuild", out var rb) && rb.ValueKind != JsonValueKind.Null) status.Rebuild = rb.Clone();
            if (root.TryGetProperty("validation", out var val)) status.Validation = val.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NLP router status check failed");
            status.Error = "Could not reach the NLP router.";
        }
        return status;
    }

    public async Task<(bool Started, int Status, string Body)> TriggerRebuildAsync()
    {
        if (string.IsNullOrWhiteSpace(_config["NlpRouter:BaseUrl"]))
            return (false, 503, "NlpRouter:BaseUrl is not configured.");
        try
        {
            using var resp = await _http.SendAsync(RouterRequest(HttpMethod.Post, "/admin/rebuild"));
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.StatusCode == System.Net.HttpStatusCode.Accepted, (int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Router rebuild trigger failed");
            return (false, 502, "Could not reach the NLP router.");
        }
    }
}
