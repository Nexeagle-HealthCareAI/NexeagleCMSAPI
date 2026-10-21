using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

// The CMS side of the Vita Test Agent. Runs, cases, verdicts and the PR gate live in Vita's own database (vita/api/app.py),
// so this passes the CMS user's request on to Vita's X-Service-Key-protected routes and hands the answer back.
// See VitaProxyService (CMSAPI/Services) and Vita:BaseUrl / ServiceAuth:VitaServiceKey.
public interface IVitaProxyService
{
    // path is relative to the Vita address, for example "runs/1HMSAPI". query is already URL-encoded ("status=shadow&skill=api-contract").
    // actor is the signed-in CMS user's email, sent as X-Actor so Vita's audit log names the person.
    Task<VitaProxyResponse> GetAsync(string path, string? query, string actor);

    // body is passed on exactly as received (JSON), or omitted when null.
    Task<VitaProxyResponse> SendAsync(HttpMethod method, string path, JsonElement? body, string actor);

    // Same as SendAsync, for a body this API built itself (e.g. { actor, reason } on promote/retire); serialised with snake_case keys.
    Task<VitaProxyResponse> SendObjectAsync(HttpMethod method, string path, object? body, string actor);
}
