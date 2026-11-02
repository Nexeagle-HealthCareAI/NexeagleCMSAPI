using System.Text.Json.Nodes;

namespace CMSAPI.Application.Models;

// What the CMS API answers the CMS web with, after talking to the Vita Test Agent control API. StatusCode is what to answer,
// which is not always what Vita said: a refused service key there is a 502 here, never a 401 (the CMS web reads 401 as
// "your session ended"). Same contract as HealthWikiProxyResponse, kept separate so the two proxies can drift independently.
public class VitaProxyResponse
{
    public bool Success { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? Message { get; set; }
    // Vita's JSON answer, passed through unchanged (snake_case keys: run_id, started_at, ...).
    public JsonNode? Data { get; set; }

    public static VitaProxyResponse Fail(int status, string message) =>
        new() { Success = false, StatusCode = status, Message = message };
}

// Body the CMS web sends to promote or retire a case. The actor is never taken from the body: VitaController fills it in
// from the signed-in user's claims.
public class VitaCaseDecisionRequest
{
    public string? Reason { get; set; }
}
