using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Daily Work Board, tab "Work management": the team's own tracker (epic, feature, story, task, bug, subtask).
// Permissions:
//   work.view     every GET here
//   work.edit     create and change items, add comments, remove your own comment
//   work.manage   add a project; add, rename and reorder the board's columns
//   work.delete   delete an item, remove a board column, remove someone else's comment.
//                 Separate from work.manage on purpose: deleting loses work, so few people hold it.
//   work.members  choose who can open each board; opens every board.
//   work.audit    read the activity log (who did what, visits, what the protected screen stopped).
// Boards: a project is open to everyone with work.view until it is switched to members-only. After
// that only its members (and work.members holders) can open it; for everyone else the project, its
// items, columns and comments answer "not found" (IWorkAccessService).
// Tables are in CMSDatabase (26_CMSDatabase_WorkBoard.sql). Rules live in WorkService.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work")]
public class WorkController : ControllerBase
{
    private readonly IWorkService _service;
    private readonly IWorkAccessService _access;
    private readonly IWorkActivityService _activity;

    public WorkController(IWorkService service, IWorkAccessService access, IWorkActivityService activity)
    {
        _service = service;
        _access = access;
        _activity = activity;
    }

    private WorkActor Actor => new WorkActor { UserId = ActorId, SeesAllBoards = Has("work.members") };

    private IActionResult Hidden(string what) =>
        NotFound(new { success = false, message = what + " not found." });

    private Guid? ActorId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : (Guid?)null;
        }
    }

    private bool Has(string permission) =>
        User.Claims.Any(c => c.Type == PermissionConstants.ClaimType && c.Value == permission);

    private IActionResult Reply<T>(WorkResult<T> result)
    {
        if (result.Ok) return Ok(new { success = true, data = result.Data, message = result.Message });
        return StatusCode(result.Status, new { success = false, message = result.Message });
    }

    // ── Projects and people ─────────────────────────────────────────────────────────────────────

    [HttpGet("projects")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetProjects()
    {
        var closed = await _access.ClosedProjectIdsAsync(Actor);
        var projects = (await _service.GetProjectsAsync()).Where(p => !closed.Contains(p.ProjectId)).ToList();
        return Ok(new { success = true, data = projects });
    }

    [HttpPost("projects")]
    [HasPermission("work.manage")]
    public async Task<IActionResult> CreateProject([FromBody] CreateWorkProjectRequest request) =>
        Reply(await _service.CreateProjectAsync(request, ActorId));

    // ── Board members ───────────────────────────────────────────────────────────────────────────

    // The members-only switch, the members, and the last 50 access changes.
    [HttpGet("projects/{projectId:guid}/access")]
    [HasPermission("work.members")]
    public async Task<IActionResult> GetAccess(Guid projectId) =>
        Reply(await _access.GetAccessAsync(projectId));

    // { isRestricted, memberUserIds: [...] } is the whole new state; each difference is logged.
    [HttpPut("projects/{projectId:guid}/access")]
    [HasPermission("work.members")]
    public async Task<IActionResult> SaveAccess(Guid projectId, [FromBody] SaveWorkProjectAccessRequest request) =>
        Reply(await _access.SaveAccessAsync(projectId, request, ActorId));

    // ── Board columns (a project's statuses) ────────────────────────────────────────────────────
    // They come back with each project (GET projects). { name, category: ToDo | InProgress | Done }.

    [HttpPost("projects/{projectId:guid}/statuses")]
    [HasPermission("work.manage")]
    public async Task<IActionResult> AddStatus(Guid projectId, [FromBody] SaveWorkStatusRequest request) =>
        await _access.CanOpenProjectAsync(projectId, Actor) ? Reply(await _service.AddStatusAsync(projectId, request)) : Hidden("Project");

    [HttpPut("statuses/{id:guid}")]
    [HasPermission("work.manage")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] SaveWorkStatusRequest request) =>
        await _access.CanOpenStatusAsync(id, Actor) ? Reply(await _service.UpdateStatusAsync(id, request)) : Hidden("Column");

    // { statusIds: [...] } with every column once, in the new order.
    [HttpPut("projects/{projectId:guid}/statuses/order")]
    [HasPermission("work.manage")]
    public async Task<IActionResult> ReorderStatuses(Guid projectId, [FromBody] ReorderWorkStatusesRequest request) =>
        await _access.CanOpenProjectAsync(projectId, Actor) ? Reply(await _service.ReorderStatusesAsync(projectId, request)) : Hidden("Project");

    // ?moveTo=<key of another column> is needed when the column still holds items.
    [HttpDelete("statuses/{id:guid}")]
    [HasPermission("work.delete")]
    public async Task<IActionResult> DeleteStatus(Guid id, [FromQuery] string? moveTo) =>
        await _access.CanOpenStatusAsync(id, Actor) ? Reply(await _service.DeleteStatusAsync(id, moveTo)) : Hidden("Column");

    // People an item can be assigned to: active CMS staff.
    [HttpGet("users")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetUsers() =>
        Ok(new { success = true, data = await _service.GetUsersAsync() });

    // ── Presence and the activity log ───────────────────────────────────────────────────────────

    // The open board calls this about once a minute: { opened: true } on the first call of a visit,
    // { event: "CopyBlocked" } when the protected screen stops something. Answers with the caller's
    // own sign-in details (this visit and the one before).
    [HttpPost("presence")]
    [HasPermission("work.view")]
    public async Task<IActionResult> Touch([FromBody] WorkPresenceRequest? request)
    {
        if (!ActorId.HasValue) return Unauthorized(new { success = false, message = "Sign in again." });
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var agent = Request.Headers.UserAgent.ToString();
        var mine = await _activity.TouchAsync(ActorId.Value, request ?? new WorkPresenceRequest(), ip, agent);
        return Ok(new { success = true, data = mine });
    }

    // When each person was last on the board, and whether they are on it now.
    [HttpGet("presence")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetLastSeen() =>
        Ok(new { success = true, data = await _activity.GetLastSeenAsync() });

    // ?userId= &projectId= &source=items|comments|access|docs|sessions &from= &before= &take=
    [HttpGet("activity")]
    [HasPermission("work.audit")]
    public async Task<IActionResult> GetActivity([FromQuery] WorkActivityQuery query) =>
        Ok(new { success = true, data = await _activity.GetActivityAsync(query, Actor, Has("docs.sensitive")) });

    // ── Items ───────────────────────────────────────────────────────────────────────────────────

    // Every item in one project. The web app filters and groups; a team's project is small enough for that.
    [HttpGet("items")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetItems([FromQuery] Guid projectId) =>
        await _access.CanOpenProjectAsync(projectId, Actor) ? Reply(await _service.GetItemsAsync(projectId)) : Hidden("Project");

    [HttpGet("items/{id:guid}")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetItem(Guid id) =>
        await _access.CanOpenItemAsync(id, Actor) ? Reply(await _service.GetItemAsync(id)) : Hidden("Work item");

    [HttpPost("items")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> CreateItem([FromBody] CreateWorkItemRequest request) =>
        await _access.CanOpenProjectAsync(request.ProjectId, Actor) ? Reply(await _service.CreateItemAsync(request, ActorId)) : Hidden("Project");

    // Send every editable field; a null clears that field. See UpdateWorkItemRequest.
    [HttpPut("items/{id:guid}")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpdateWorkItemRequest request) =>
        await _access.CanOpenItemAsync(id, Actor) ? Reply(await _service.UpdateItemAsync(id, request, ActorId)) : Hidden("Work item");

    [HttpDelete("items/{id:guid}")]
    [HasPermission("work.delete")]
    public async Task<IActionResult> DeleteItem(Guid id) =>
        await _access.CanOpenItemAsync(id, Actor) ? Reply(await _service.DeleteItemAsync(id, ActorId)) : Hidden("Work item");

    // ── Comments ────────────────────────────────────────────────────────────────────────────────

    [HttpPost("items/{id:guid}/comments")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddWorkCommentRequest request) =>
        await _access.CanOpenItemAsync(id, Actor) ? Reply(await _service.AddCommentAsync(id, request, ActorId)) : Hidden("Work item");

    [HttpDelete("comments/{id:guid}")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> DeleteComment(Guid id) =>
        await _access.CanOpenCommentAsync(id, Actor) ? Reply(await _service.DeleteCommentAsync(id, ActorId, Has("work.delete"))) : Hidden("Comment");
}
