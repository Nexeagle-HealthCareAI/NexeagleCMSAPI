using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Daily Work Board, team chat: a channel per board, named groups, and direct messages; tagging
// people, marking a message important, escalating, and each person's notifications.
// Everything needs work.view. Removing someone else's message also needs work.delete.
// The open board asks GET chat/state every few seconds; there is no live socket for chat yet.
// Tables are in CMSDatabase (30_CMSDatabase_WorkChat.sql). Rules live in WorkChatService.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work")]
[HasPermission("work.view")]
public class WorkChatController : ControllerBase
{
    private readonly IWorkChatService _chat;

    public WorkChatController(IWorkChatService chat)
    {
        _chat = chat;
    }

    private bool Has(string permission) =>
        User.Claims.Any(c => c.Type == PermissionConstants.ClaimType && c.Value == permission);

    private WorkActor Actor
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return new WorkActor { UserId = Guid.TryParse(claim, out var id) ? id : (Guid?)null, SeesAllBoards = Has("work.members") };
        }
    }

    private IActionResult Reply<T>(WorkResult<T> result)
    {
        if (result.Ok) return Ok(new { success = true, data = result.Data, message = result.Message });
        return StatusCode(result.Status, new { success = false, message = result.Message });
    }

    // The caller's conversations with unread counts, and how many notifications are unread.
    // With ?channelId=&after= it also returns that conversation's messages newer than "after".
    [HttpGet("chat/state")]
    public async Task<IActionResult> GetState([FromQuery] Guid? channelId, [FromQuery] DateTime? after) =>
        Ok(new { success = true, data = await _chat.GetStateAsync(Actor, channelId, after) });

    [HttpPost("chat/groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateWorkChatGroupRequest request) =>
        Reply(await _chat.CreateGroupAsync(Actor, request));

    // The one conversation between the caller and { userId }, made on first use.
    [HttpPost("chat/direct")]
    public async Task<IActionResult> OpenDirect([FromBody] OpenWorkChatDirectRequest request) =>
        Reply(await _chat.OpenDirectAsync(Actor, request.UserId));

    // The newest 50, oldest first. ?before=<a message's createdAt> loads the 50 before it.
    // ?important=true keeps only important messages and escalations.
    [HttpGet("chat/channels/{channelId:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid channelId, [FromQuery] DateTime? before, [FromQuery] bool important = false) =>
        Reply(await _chat.GetMessagesAsync(Actor, channelId, before, important));

    [HttpPost("chat/channels/{channelId:guid}/messages")]
    public async Task<IActionResult> Send(Guid channelId, [FromBody] SendWorkChatMessageRequest request) =>
        Reply(await _chat.SendAsync(Actor, channelId, request));

    [HttpPost("chat/channels/{channelId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid channelId) =>
        Reply(await _chat.MarkReadAsync(Actor, channelId));

    [HttpPut("chat/messages/{messageId:guid}/important")]
    public async Task<IActionResult> MarkImportant(Guid messageId, [FromBody] MarkWorkChatImportantRequest request) =>
        Reply(await _chat.MarkImportantAsync(Actor, messageId, request.IsImportant));

    [HttpDelete("chat/messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid messageId) =>
        Reply(await _chat.DeleteMessageAsync(Actor, messageId, Has("work.delete")));

    // Escalate a work item: posts in its board's conversation, tags { userIds } and notifies them.
    [HttpPost("items/{workItemId:guid}/escalate")]
    [HasPermission("work.edit")]
    public async Task<IActionResult> EscalateItem(Guid workItemId, [FromBody] EscalateWorkItemRequest request) =>
        Reply(await _chat.EscalateItemAsync(Actor, workItemId, request));

    // The caller's latest 50 notifications.
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications()
    {
        var actor = Actor;
        if (!actor.UserId.HasValue) return Unauthorized(new { success = false, message = "Sign in again." });
        return Ok(new { success = true, data = await _chat.GetNotificationsAsync(actor.UserId.Value) });
    }

    // { notificationIds: [...] }, or an empty list to mark every one read.
    [HttpPost("notifications/read")]
    public async Task<IActionResult> ReadNotifications([FromBody] ReadWorkNotificationsRequest? request)
    {
        var actor = Actor;
        if (!actor.UserId.HasValue) return Unauthorized(new { success = false, message = "Sign in again." });
        await _chat.MarkNotificationsReadAsync(actor.UserId.Value, request?.NotificationIds ?? new List<Guid>());
        return Ok(new { success = true });
    }
}
