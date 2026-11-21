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
//   work.manage   add a project, delete an item, remove anyone's comment
// Tables are in CMSDatabase (26_CMSDatabase_WorkBoard.sql). Rules live in WorkService.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work")]
public class WorkController : ControllerBase
{
    private readonly IWorkService _service;

    public WorkController(IWorkService service)
    {
        _service = service;
    }

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
    public async Task<IActionResult> GetProjects() =>
        Ok(new { success = true, data = await _service.GetProjectsAsync() });

    [HttpPost("projects")]
    [HasPermission("work.manage")]
    public async Task<IActionResult> CreateProject([FromBody] CreateWorkProjectRequest request) =>
        Reply(await _service.CreateProjectAsync(request, ActorId));

    // People an item can be assigned to: active CMS staff.
    [HttpGet("users")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetUsers() =>
        Ok(new { success = true, data = await _service.GetUsersAsync() });

    // ── Items ───────────────────────────────────────────────────────────────────────────────────

    // Every item in one project. The web app filters and groups; a team's project is small enough for that.
    [HttpGet("items")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetItems([FromQuery] Guid projectId) =>
        Reply(await _service.GetItemsAsync(projectId));

    [HttpGet("items/{id:guid}")]
    [HasPermission("work.view")]
    public async Task<IActionResult> GetItem(Guid id) =>
        Reply(await _service.GetItemAsync(id));

    [HttpPost("items")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> CreateItem([FromBody] CreateWorkItemRequest request) =>
        Reply(await _service.CreateItemAsync(request, ActorId));

    // Send every editable field; a null clears that field. See UpdateWorkItemRequest.
    [HttpPut("items/{id:guid}")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpdateWorkItemRequest request) =>
        Reply(await _service.UpdateItemAsync(id, request, ActorId));

    [HttpDelete("items/{id:guid}")]
    [HasPermission("work.manage")]
    public async Task<IActionResult> DeleteItem(Guid id) =>
        Reply(await _service.DeleteItemAsync(id, ActorId));

    // ── Comments ────────────────────────────────────────────────────────────────────────────────

    [HttpPost("items/{id:guid}/comments")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddWorkCommentRequest request) =>
        Reply(await _service.AddCommentAsync(id, request, ActorId));

    [HttpDelete("comments/{id:guid}")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> DeleteComment(Guid id) =>
        Reply(await _service.DeleteCommentAsync(id, ActorId, Has("work.manage")));
}
