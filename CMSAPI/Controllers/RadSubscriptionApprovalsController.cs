using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Review queue for 1Rad payment requests. 1Rad keeps its own database, so this proxies
// 1RadAPI's service-key endpoints (see RadSubscriptionsService). Before Oct 2026 no
// screen anywhere could approve a 1Rad payment.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/rad-subscriptions")]
public class RadSubscriptionApprovalsController : ControllerBase
{
    private readonly IRadSubscriptionsService _service;

    public RadSubscriptionApprovalsController(IRadSubscriptionsService service)
    {
        _service = service;
    }

    private string Actor =>
        User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value ?? "CMS";

    [HasPermission("subscriptions.approve")]
    [HttpGet("payment-requests")]
    public async Task<IActionResult> GetPaymentRequests()
    {
        var result = await _service.GetPaymentRequestsAsync();
        if (result == null)
            return StatusCode(502, new { message = "Couldn't load 1Rad payment requests. Check the 1Rad connection settings." });
        return Ok(result);
    }

    [HasPermission("subscriptions.approve")]
    [HttpPost("payment-requests/{requestId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid requestId, [FromBody] RadApproveBody? body)
    {
        var note = string.IsNullOrWhiteSpace(body?.Note) ? $"Approved in CMS by {Actor}" : $"{body!.Note!.Trim()} (approved in CMS by {Actor})";
        var result = await _service.ApproveAsync(requestId, note.Length > 500 ? note[..500] : note);
        return result.Success
            ? Ok(new { message = "1Rad subscription activated." })
            : StatusCode(result.StatusCode >= 400 ? result.StatusCode : 400, new { message = result.Error });
    }

    [HasPermission("subscriptions.approve")]
    [HttpPost("payment-requests/{requestId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid requestId, [FromBody] RadRejectBody body)
    {
        if (string.IsNullOrWhiteSpace(body?.Reason))
            return BadRequest(new { message = "A reason is required." });
        var result = await _service.RejectAsync(requestId, body.Reason.Trim());
        return result.Success
            ? Ok(new { message = "Payment rejected." })
            : StatusCode(result.StatusCode >= 400 ? result.StatusCode : 400, new { message = result.Error });
    }
}

public record RadApproveBody(string? Note);
public record RadRejectBody(string? Reason);
