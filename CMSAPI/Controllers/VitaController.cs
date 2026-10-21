using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using CMSAPI.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.SignalR;

namespace CMSAPI.Controllers;

// CMS "Vita Test Agent" tab: what the autonomous test agent ran (runs, results), the test cases it generated (shadow -> active ->
// retired), the PR gate and the schedules. Vita keeps all of this in its own database, so like HealthWikiController this only
// passes the call on to Vita's control API (vita/api/app.py) after the CMS login and the vita.* permission are checked:
//
//   vita.view     GET  overview, repos, runs, results, cases, graph counts, schedules, coverage, escapes, learning,
//                      settings/repos, repos/{repo}/secrets, audit, controls
//   vita.manage   POST runs (sync), runs/enqueue, schedules, gate pause/resume, generate, escapes, learn, repos (add),
//                      repos/{repo}/validate, repos/{repo}/secrets/{name}/rotated, schedules/{id}/run-now
//                 PATCH schedules/{id}; DELETE schedules/{id}, repos/{repo} (archive)
//   vita.approve  POST cases/{id}/promote | retire, lessons/{id}/apply | revert, escapes/{repo}/{id}/verify
//                 PUT  controls (kill switch, budgets, models, monthly cap; reason required)
//                 (actor = the signed-in user, never the body)
//
// POST events is server-to-server from Vita (X-Service-Key = ServiceAuth:VitaServiceKey) and fans a run update out to the
// CMS web over VitaHub.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vita")]
public class VitaController : ControllerBase
{
    private readonly IVitaProxyService _vita;
    private readonly IHubContext<VitaHub> _hub;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VitaController> _logger;

    public VitaController(IVitaProxyService vita, IHubContext<VitaHub> hub, IConfiguration configuration, ILogger<VitaController> logger)
    {
        _vita = vita;
        _hub = hub;
        _configuration = configuration;
        _logger = logger;
    }

    // The signed-in CMS user, which Vita records in its audit log (case promotions, gate changes, manual runs).
    private string Actor =>
        User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "CMS";

    private static string Query(params (string Name, string? Value)[] parts) =>
        string.Join("&", parts.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));

    // { success, data } like the rest of this API, or { success: false, message }; the status code is Vita's (see VitaProxyService.Failure).
    private IActionResult Reply(VitaProxyResponse r) =>
        r.Success
            ? StatusCode(r.StatusCode, new { success = true, data = r.Data })
            : StatusCode(r.StatusCode, new { success = false, message = r.Message });

    // ---- read (vita.view) ------------------------------------------------------------------------------------------

    // Per-repo summary: last run verdict per trigger, open failures, flaky count, cases by status.
    [HasPermission("vita.view")]
    [HttpGet("overview")]
    public async Task<IActionResult> Overview() => Reply(await _vita.GetAsync("overview", null, Actor));

    // Onboarded repos (those with a vita.yaml).
    [HasPermission("vita.view")]
    [HttpGet("repos")]
    public async Task<IActionResult> Repos() => Reply(await _vita.GetAsync("repos", null, Actor));

    [HasPermission("vita.view")]
    [HttpGet("runs/{repo}")]
    public async Task<IActionResult> Runs(string repo, [FromQuery] int? limit) =>
        Reply(await _vita.GetAsync($"runs/{Uri.EscapeDataString(repo)}", Query(("limit", limit?.ToString())), Actor));

    // { status: queued|running|done, verdict, summary, report_md } -- the CMS web polls this while a run is queued or running.
    [HasPermission("vita.view")]
    [HttpGet("runs/{repo}/{runId}")]
    public async Task<IActionResult> Run(string repo, string runId) =>
        Reply(await _vita.GetAsync($"runs/{Uri.EscapeDataString(repo)}/{Uri.EscapeDataString(runId)}", null, Actor));

    [HasPermission("vita.view")]
    [HttpGet("runs/{repo}/{runId}/results")]
    public async Task<IActionResult> RunResults(string repo, string runId) =>
        Reply(await _vita.GetAsync($"runs/{Uri.EscapeDataString(repo)}/{Uri.EscapeDataString(runId)}/results", null, Actor));

    // status: shadow | active | retired. skill: a skill pack name such as api-contract.
    [HasPermission("vita.view")]
    [HttpGet("cases/{repo}")]
    public async Task<IActionResult> Cases(string repo, [FromQuery] string? status, [FromQuery] string? skill) =>
        Reply(await _vita.GetAsync($"cases/{Uri.EscapeDataString(repo)}", Query(("status", status), ("skill", skill)), Actor));

    [HasPermission("vita.view")]
    [HttpGet("graph/{repo}/counts")]
    public async Task<IActionResult> GraphCounts(string repo) =>
        Reply(await _vita.GetAsync($"graph/{Uri.EscapeDataString(repo)}/counts", null, Actor));

    [HasPermission("vita.view")]
    [HttpGet("schedules")]
    public async Task<IActionResult> Schedules() => Reply(await _vita.GetAsync("schedules", null, Actor));

    // Gap report: per node type covered/uncovered, uncovered endpoints and tables, untested permission keys, cases per pack.
    [HasPermission("vita.view")]
    [HttpGet("coverage/{repo}")]
    public async Task<IActionResult> Coverage(string repo) =>
        Reply(await _vita.GetAsync($"coverage/{Uri.EscapeDataString(repo)}", null, Actor));

    // Escaped bugs logged for a repo, newest first (status: open | linked | closed).
    [HasPermission("vita.view")]
    [HttpGet("escapes/{repo}")]
    public async Task<IActionResult> Escapes(string repo, [FromQuery] string? status) =>
        Reply(await _vita.GetAsync($"escapes/{Uri.EscapeDataString(repo)}", Query(("status", status)), Actor));

    // Learner: pending / applied / reverted lessons, the risk map (top 20) and the latest agent-eval metrics.
    [HasPermission("vita.view")]
    [HttpGet("learning/{repo}")]
    public async Task<IActionResult> Learning(string repo) =>
        Reply(await _vita.GetAsync($"learning/{Uri.EscapeDataString(repo)}", null, Actor));

    // Settings tab: repos with status (active | paused | archived), the manifest path they were added with, skills, targets,
    // declared secret names and schedule count.
    [HasPermission("vita.view")]
    [HttpGet("settings/repos")]
    public async Task<IActionResult> SettingsRepos() => Reply(await _vita.GetAsync("settings/repos", null, Actor));

    // Secret NAMES the manifest declares (plus any recorded by hand) with last_rotated_at, rotated_by, expires_at, present and
    // flags (rotation_overdue > 90 days, expiring_soon < 30 days, expired, never_recorded). Values are never stored or returned.
    [HasPermission("vita.view")]
    [HttpGet("repos/{repo}/secrets")]
    public async Task<IActionResult> RepoSecrets(string repo) =>
        Reply(await _vita.GetAsync($"repos/{Uri.EscapeDataString(repo)}/secrets", null, Actor));

    // Vita's audit log, newest first: { id, at, actor, action, subject, reason, detail }. repo narrows it to that repo's
    // rows (repo, its schedules, cases and secrets); limit defaults to 100 (max 500).
    [HasPermission("vita.view")]
    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] string? repo, [FromQuery] int? limit) =>
        Reply(await _vita.GetAsync("audit", Query(("repo", repo), ("limit", limit?.ToString())), Actor));

    // Kill switch, monthly INR cap, per-repo budgets and the model router:
    // { kill_switch: { on, actor, reason }, budgets: { repo: { pr_tokens_inr, pr_minutes } }, models: { plan, judge, classify }, monthly_inr_cap }.
    [HasPermission("vita.view")]
    [HttpGet("controls")]
    public async Task<IActionResult> Controls() => Reply(await _vita.GetAsync("controls", null, Actor));

    // ---- operate (vita.manage) -------------------------------------------------------------------------------------

    // "Add repo": { manifest_path, source_root? } -> { repo, kind, skills, targets, graph: { endpoint: n, ... } } or 400 with
    // the manifest problems (message is the problems joined with "; "). Adding an archived repo again makes it active.
    [HasPermission("vita.manage")]
    [HttpPost("repos")]
    public async Task<IActionResult> AddRepo([FromBody] JsonElement body) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, "repos", WithActor(body), Actor));

    // Re-validate the repo's stored manifest path: { ok, problems, changed_since_onboarding }.
    [HasPermission("vita.manage")]
    [HttpPost("repos/{repo}/validate")]
    public async Task<IActionResult> ValidateRepo(string repo) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"repos/{Uri.EscapeDataString(repo)}/validate", null, Actor));

    // Archive: status only (runs, cases, verdicts and audit rows are kept) and its schedules are disabled. Body { reason? }.
    [HasPermission("vita.manage")]
    [HttpDelete("repos/{repo}")]
    public async Task<IActionResult> ArchiveRepo(string repo, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VitaCaseDecisionRequest? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Delete, $"repos/{Uri.EscapeDataString(repo)}", new { actor = Actor, reason = body?.Reason ?? "" }, Actor));

    // Records that a secret was rotated where it lives (GitHub secret / VM env): { expires_at?, note? }. No value is sent.
    [HasPermission("vita.manage")]
    [HttpPost("repos/{repo}/secrets/{name}/rotated")]
    public async Task<IActionResult> SecretRotated(string repo, string name, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"repos/{Uri.EscapeDataString(repo)}/secrets/{Uri.EscapeDataString(name)}/rotated", WithActor(body), Actor));

    // { enabled?, cron? } -> { changed, next_run_at }. Vita validates the cron (5 fields, IST) and recomputes next_run_at.
    [HasPermission("vita.manage")]
    [HttpPatch("schedules/{id}")]
    public async Task<IActionResult> UpdateSchedule(string id, [FromBody] JsonElement body) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Patch, $"schedules/{Uri.EscapeDataString(id)}", WithActor(body), Actor));

    // Queue the schedule's run now -> { run_id, status: "queued" }.
    [HasPermission("vita.manage")]
    [HttpPost("schedules/{id}/run-now")]
    public async Task<IActionResult> RunScheduleNow(string id) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"schedules/{Uri.EscapeDataString(id)}/run-now", new { actor = Actor }, Actor));

    // Vita takes the actor as a query parameter on this route (DELETE /schedules/{sid}?actor=).
    [HasPermission("vita.manage")]
    [HttpDelete("schedules/{id}")]
    public async Task<IActionResult> DeleteSchedule(string id) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Delete, $"schedules/{Uri.EscapeDataString(id)}?{Query(("actor", Actor))}", null, Actor));

    // Asynchronous: { manifest_path | repo, trigger, target, ... } -> { run_id, status: "queued" }. The CMS web then polls GET runs/{repo}/{run_id}
    // and listens on /hubs/vita.
    [HasPermission("vita.manage")]
    [HttpPost("runs/enqueue")]
    public async Task<IActionResult> EnqueueRun([FromBody] JsonElement body) =>
        Reply(await _vita.SendAsync(HttpMethod.Post, "runs/enqueue", body, Actor));

    // Synchronous (Phase 0 surface): blocks until the run finishes, so it can hit the 20 s client timeout on a real repo. Kept for
    // parity with Vita's API; the CMS web uses runs/enqueue.
    [HasPermission("vita.manage")]
    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun([FromBody] JsonElement body) =>
        Reply(await _vita.SendAsync(HttpMethod.Post, "runs", body, Actor));

    [HasPermission("vita.manage")]
    [HttpPost("schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] JsonElement body) =>
        Reply(await _vita.SendAsync(HttpMethod.Post, "schedules", body, Actor));

    // Pausing the gate makes `vita / gate` answer neutral on every PR of that repo until it is resumed. The optional body is { reason }.
    [HasPermission("vita.manage")]
    [HttpPost("gate/{repo}/pause")]
    public async Task<IActionResult> PauseGate(string repo, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"gate/{Uri.EscapeDataString(repo)}/pause", WithActor(body), Actor));

    [HasPermission("vita.manage")]
    [HttpPost("gate/{repo}/resume")]
    public async Task<IActionResult> ResumeGate(string repo, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"gate/{Uri.EscapeDataString(repo)}/resume", WithActor(body), Actor));

    // Case Generator: { targets?, max_cases? } -> { generated, stored, source, dropped: [{ title, reasons }], cases }. Generated cases are
    // always shadow; promoting them is still vita.approve. The actor is the signed-in user, never the body.
    [HasPermission("vita.manage")]
    [HttpPost("generate/{repo}")]
    public async Task<IActionResult> Generate(string repo, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"generate/{Uri.EscapeDataString(repo)}", WithActor(body), Actor));

    // Log a bug that reached users: { title, description, sha? } -> { escape, case_ids, ... }. Vita saves it and asks the Case Generator
    // for shadow cases that would have caught it.
    [HasPermission("vita.manage")]
    [HttpPost("escapes/{repo}")]
    public async Task<IActionResult> LogEscape(string repo, [FromBody] JsonElement body) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"escapes/{Uri.EscapeDataString(repo)}", WithActor(body), Actor));

    // Run the Learner now: { apply_auto? } -> { report, applied, stale }. Lessons it may apply on its own (shadow-only case changes,
    // risk map, relabelled verdicts) are applied when apply_auto is true; anything touching an ACTIVE case stays pending for vita.approve.
    [HasPermission("vita.manage")]
    [HttpPost("learn/{repo}")]
    public async Task<IActionResult> Learn(string repo, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"learn/{Uri.EscapeDataString(repo)}", WithActor(body), Actor));

    // ---- approve (vita.approve) ------------------------------------------------------------------------------------

    // Kill switch, budgets, monthly cap and model router. Four-eyes: vita.approve AND a non-empty reason, which Vita records
    // with the signed-in user. The body is passed through (any of kill_switch / budgets / models / monthly_inr_cap) with
    // actor set from the claims, also inside kill_switch, whose reason defaults to the top-level one.
    [HasPermission("vita.approve")]
    [HttpPut("controls")]
    public async Task<IActionResult> UpdateControls([FromBody] JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { success = false, message = "The controls must be a JSON object." });
        var reason = body.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(reason))
            return BadRequest(new { success = false, message = "A reason is required to change the Vita controls." });

        var dict = (Dictionary<string, object?>)WithActor(body);
        dict["reason"] = reason;
        if (body.TryGetProperty("kill_switch", out var ks) && ks.ValueKind == JsonValueKind.Object)
        {
            var kill = new Dictionary<string, object?>();
            foreach (var p in ks.EnumerateObject())
                if (p.Name != "actor") kill[p.Name] = p.Value.Clone();
            kill["actor"] = Actor;
            if (!kill.TryGetValue("reason", out var kr) || kr is not JsonElement { ValueKind: JsonValueKind.String } ke || string.IsNullOrWhiteSpace(ke.GetString()))
                kill["reason"] = reason;
            dict["kill_switch"] = kill;
        }
        _logger.LogInformation("Vita controls changed by {Actor}: {Keys} ({Reason})", Actor, string.Join(",", dict.Keys), reason);
        return Reply(await _vita.SendObjectAsync(HttpMethod.Put, "controls", dict, Actor));
    }

    // Approve a pending lesson (e.g. quarantine an ACTIVE flaky case). 409 when the case changed since the lesson was mined.
    [HasPermission("vita.approve")]
    [HttpPost("lessons/{id}/apply")]
    public async Task<IActionResult> ApplyLesson(string id) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"lessons/{Uri.EscapeDataString(id)}/apply", new { actor = Actor }, Actor));

    // Revert an applied lesson (restores the previous case status / risk map) or dismiss a pending one.
    [HasPermission("vita.approve")]
    [HttpPost("lessons/{id}/revert")]
    public async Task<IActionResult> RevertLesson(string id) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"lessons/{Uri.EscapeDataString(id)}/revert", new { actor = Actor }, Actor));

    // Escape -> verified case: { broken_sha, fixed_sha }. Cases that FAIL on broken and PASS on fixed become ACTIVE (hence vita.approve)
    // and the escape is closed; the rest are retired. Slow (a PR stack per build): raise Vita:TimeoutSeconds where it is used.
    [HasPermission("vita.approve")]
    [HttpPost("escapes/{repo}/{id}/verify")]
    public async Task<IActionResult> VerifyEscape(string repo, string id, [FromBody] JsonElement body) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"escapes/{Uri.EscapeDataString(repo)}/{Uri.EscapeDataString(id)}/verify", WithActor(body), Actor));

    // shadow -> active: the case starts counting towards the PR gate. Vita wants { actor, reason }; the actor is always the signed-in user.
    [HasPermission("vita.approve")]
    [HttpPost("cases/{id}/promote")]
    public async Task<IActionResult> Promote(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VitaCaseDecisionRequest? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"cases/{Uri.EscapeDataString(id)}/promote", new { actor = Actor, reason = body?.Reason ?? "" }, Actor));

    [HasPermission("vita.approve")]
    [HttpPost("cases/{id}/retire")]
    public async Task<IActionResult> Retire(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VitaCaseDecisionRequest? body = null) =>
        Reply(await _vita.SendObjectAsync(HttpMethod.Post, $"cases/{Uri.EscapeDataString(id)}/retire", new { actor = Actor, reason = body?.Reason ?? "" }, Actor));

    // ---- events (Vita -> CMS, service key) -------------------------------------------------------------------------

    // Vita calls this when a run changes state ({ repo, run_id, status, verdict?, trigger?, summary? }); the payload goes to every
    // connected CMS web tab as "vita-run-updated". No JWT: the shared key is the authentication, as on the agent-monitor ingest routes.
    [AllowAnonymous]
    [HttpPost("events")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Events([FromHeader(Name = "X-Service-Key")] string? serviceKey, [FromBody] JsonElement payload)
    {
        if (!IsValidServiceKey(serviceKey)) return Unauthorized();
        if (payload.ValueKind != JsonValueKind.Object) return BadRequest(new { success = false, message = "The event must be a JSON object." });

        await VitaHub.BroadcastRunUpdate(_hub, payload);
        _logger.LogInformation("Vita run update relayed: {Payload}", payload.GetRawText());
        return Ok(new { success = true });
    }

    private bool IsValidServiceKey(string? key)
    {
        var expected = _configuration["ServiceAuth:VitaServiceKey"];
        // The committed placeholder ("<set-via-env-...>") must never act as a real key.
        if (string.IsNullOrWhiteSpace(expected) || expected.StartsWith('<') || key == null) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(expected));
    }

    // Adds the signed-in user to a pass-through body (gate pause/resume) so Vita can record who did it; an absent body becomes { actor }.
    private object WithActor(JsonElement? body)
    {
        var dict = new Dictionary<string, object?> { ["actor"] = Actor };
        if (body.HasValue && body.Value.ValueKind == JsonValueKind.Object)
            foreach (var p in body.Value.EnumerateObject())
                if (p.Name != "actor") dict[p.Name] = p.Value.Clone();
        return dict;
    }
}
