using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

// The CMS side of Vita's emergency takeover (vision Phase 3). The opt-ins, CMS-added places and the alert log live in 1HMSAPI (easyHMS
// database); this passes the CMS user's request to its key-protected /internal/emergency routes. See EmergencyTakeoverService.
public interface IEmergencyTakeoverService
{
    Task<HealthWikiProxyResponse> GetAsync(string path, string? query, string actor);
    Task<HealthWikiProxyResponse> SendAsync(HttpMethod method, string path, JsonElement? body, string actor);
}
