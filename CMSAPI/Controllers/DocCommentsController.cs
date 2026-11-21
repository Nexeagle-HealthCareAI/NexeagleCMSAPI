using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Daily Work Board, comments on documents: threads with replies, tagging people, quoting the text a
// comment is about, resolving and reopening. Everything needs docs.view and the right to open the
// document itself (sensitive folders hide a document's comments too). People change and remove
// their own comments; removing someone else's needs docs.delete.
// Tables are in CMSDatabase (31_CMSDatabase_DocComments.sql). Rules live in DocCommentsService.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/docs")]
[HasPermission("docs.view")]
public class DocCommentsController : ControllerBase
{
    private readonly IDocCommentsService _comments;

    public DocCommentsController(IDocCommentsService comments)
    {
        _comments = comments;
    }

    private bool Has(string permission) =>
        User.Claims.Any(c => c.Type == PermissionConstants.ClaimType && c.Value == permission);

    private DocsActor Actor
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return new DocsActor
            {
                UserId = Guid.TryParse(claim, out var id) ? id : (Guid?)null,
                CanEdit = Has("docs.edit"),
                CanDelete = Has("docs.delete"),
                CanSensitive = Has("docs.sensitive"),
            };
        }
    }

    private IActionResult Reply<T>(WorkResult<T> result)
    {
        if (result.Ok) return Ok(new { success = true, data = result.Data, message = result.Message });
        return StatusCode(result.Status, new { success = false, message = result.Message });
    }

    // Every comment of the document, oldest first; a reply carries its thread's parentId.
    [HttpGet("{documentId:guid}/comments")]
    public async Task<IActionResult> Get(Guid documentId) =>
        Reply(await _comments.GetAsync(Actor, documentId));

    // { body, quote?, parentId?, mentionUserIds? }
    [HttpPost("{documentId:guid}/comments")]
    public async Task<IActionResult> Add(Guid documentId, [FromBody] SaveDocCommentRequest request) =>
        Reply(await _comments.AddAsync(Actor, documentId, request));

    [HttpPut("comments/{commentId:guid}")]
    public async Task<IActionResult> Edit(Guid commentId, [FromBody] SaveDocCommentRequest request) =>
        Reply(await _comments.EditAsync(Actor, commentId, request));

    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid commentId) =>
        Reply(await _comments.DeleteAsync(Actor, commentId));

    // { isResolved } on the first comment of a thread.
    [HttpPut("comments/{commentId:guid}/resolved")]
    public async Task<IActionResult> Resolve(Guid commentId, [FromBody] ResolveDocCommentRequest request) =>
        Reply(await _comments.ResolveAsync(Actor, commentId, request.IsResolved));

    // The caller's latest 50 comment notifications.
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications() =>
        Ok(new { success = true, data = await _comments.GetNotificationsAsync(Actor) });

    // { notificationIds: [...] }, or an empty list to mark every one read.
    [HttpPost("notifications/read")]
    public async Task<IActionResult> ReadNotifications([FromBody] ReadDocNotificationsRequest? request)
    {
        var actor = Actor;
        if (!actor.UserId.HasValue) return Unauthorized(new { success = false, message = "Sign in again." });
        await _comments.MarkNotificationsReadAsync(actor.UserId.Value, request?.NotificationIds ?? new List<Guid>());
        return Ok(new { success = true });
    }
}
