using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

// The CMS side of the Health Wiki. The data and every rule live in 1HMSAPI (its easyHMS database), so this passes the CMS user's
// request on to 1HMSAPI's key-protected /internal routes and hands the answer back. See HealthWikiService.
public interface IHealthWikiService
{
    // path is relative to the 1HMSAPI address, for example "internal/health-articles/diabetes". actor is the signed-in CMS user's name,
    // which 1HMSAPI keeps in the article's history.
    Task<HealthWikiProxyResponse> GetAsync(string path, string? query, string actor, HealthWikiShape shape = HealthWikiShape.Whole);

    // body is passed on exactly as received: for an edit, a field that is absent means "unchanged" and one sent as null means "clear it".
    Task<HealthWikiProxyResponse> SendAsync(HttpMethod method, string path, JsonElement? body, string actor, HealthWikiShape shape);

    Task<HealthWikiProxyResponse> UploadImageAsync(string fileName, string contentType, Stream content, string actor);
}
