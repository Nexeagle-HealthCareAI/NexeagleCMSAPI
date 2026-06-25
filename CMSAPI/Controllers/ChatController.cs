using Microsoft.AspNetCore.Mvc;
using CMSAPI.Authorization;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers;

[ApiController]
[HasPermission("live-support.view")] // Transcripts + guest PII are agent-only; the guest gets history over the hub.
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _context;

    public ChatController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("history/{sessionId}")]
    public async Task<IActionResult> GetHistory(Guid sessionId)
    {
        var messages = await _context.SupportMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
            
        return Ok(messages);
    }

    [HttpGet("active-sessions")]
    public async Task<IActionResult> GetActiveSessions([FromQuery] int limit = 50)
    {
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        var sessions = await _context.SupportSessions
            .Where(s => s.Status == "Active")
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .ToListAsync();
            
        return Ok(sessions);
    }
}
