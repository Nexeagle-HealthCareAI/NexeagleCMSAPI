using Microsoft.AspNetCore.Mvc;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers;

[ApiController]
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
    public async Task<IActionResult> GetActiveSessions()
    {
        var sessions = await _context.SupportSessions
            .Where(s => s.Status == "Active")
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync();
            
        return Ok(sessions);
    }
}
