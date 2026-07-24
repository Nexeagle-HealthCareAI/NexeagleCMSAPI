using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services
{
    public class SymptomRouterService : ISymptomRouterService
    {
        private readonly ISymptomRouterRepository _repo;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SymptomRouterService> _logger;

        public SymptomRouterService(
            ISymptomRouterRepository repo, HttpClient httpClient,
            IConfiguration configuration, ILogger<SymptomRouterService> logger)
        {
            _repo = repo;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public Task<PagedResult<TrainingExampleItem>> GetTrainingExamplesAsync(int page, int limit, string? specialist, string? search)
            => _repo.GetTrainingExamplesAsync(page, limit, specialist, search);

        public Task<TrainingExampleItem?> AddTrainingExampleAsync(UpsertTrainingExampleRequest request, string? createdBy)
        {
            if (!IsValid(request)) return Task.FromResult<TrainingExampleItem?>(null);
            return _repo.AddTrainingExampleAsync(request, createdBy)!;
        }

        public Task<TrainingExampleItem?> UpdateTrainingExampleAsync(Guid id, UpsertTrainingExampleRequest request)
        {
            if (!IsValid(request)) return Task.FromResult<TrainingExampleItem?>(null);
            return _repo.UpdateTrainingExampleAsync(id, request);
        }

        public Task<bool> DeleteTrainingExampleAsync(Guid id) => _repo.DeleteTrainingExampleAsync(id);

        public Task<PagedResult<FeedbackLogItem>> GetFeedbackLogAsync(int page, int limit, DateOnly? from, DateOnly? to, bool? correctionsOnly)
            => _repo.GetFeedbackLogAsync(page, limit, from, to, correctionsOnly);

        private static bool IsValid(UpsertTrainingExampleRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.Text)
                && SymptomRouterConstants.ValidSpecialists.Contains(request.Specialist);
        }

        public async Task<ModelInfoDto?> GetModelInfoAsync()
        {
            var baseUrl = _configuration["NlpRouter:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;

            try
            {
                var response = await _httpClient.GetFromJsonAsync<ModelInfoDto>($"{baseUrl}/model-info");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch model-info from NLP router at {BaseUrl}", baseUrl);
                return null;
            }
        }

        // Fires a workflow_dispatch on 1HMS-NLP-Router's retrain.yml via GitHub's REST API —
        // same pipeline the nightly cron uses, just triggered on demand. Requires a PAT with
        // `repo`/`workflow` scope (GitHub:RetrainWorkflowToken) and the repo owner/name
        // (GitHub:RetrainWorkflowRepo, e.g. "Nexeagle-HealthCareAI/1HMS-NLP-Router").
        public async Task<bool> TriggerRetrainAsync()
        {
            var token = _configuration["GitHub:RetrainWorkflowToken"];
            var repo = _configuration["GitHub:RetrainWorkflowRepo"];
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(repo))
            {
                _logger.LogWarning("Retrain trigger requested but GitHub:RetrainWorkflowToken/Repo isn't configured.");
                return false;
            }

            var url = $"https://api.github.com/repos/{repo}/actions/workflows/retrain.yml/dispatches";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { @ref = "develop" }),
                    Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("User-Agent", "CMSAPI-SymptomRouter");
            request.Headers.Add("Accept", "application/vnd.github+json");

            try
            {
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Retrain trigger failed: {Status} {Body}", response.StatusCode, body);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retrain trigger threw while calling GitHub's API.");
                return false;
            }
        }
    }
}
