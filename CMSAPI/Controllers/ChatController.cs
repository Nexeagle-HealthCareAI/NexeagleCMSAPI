using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Authorization;
using CMSAPI.Application.Models;
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

    // All sessions (Active + Closed), newest first. from/to are inclusive "yyyy-MM-dd" dates;
    // omit both for all-time, or pass the same date for both for "today" — same convention as
    // InsightsController's date-filtered endpoints.
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null)
    {
        if (page < 1) page = 1;
        if (limit < 1) limit = 10;
        if (limit > 100) limit = 100;

        var query = _context.SupportSessions.AsNoTracking().AsQueryable();

        if (from.HasValue) query = query.Where(s => s.StartedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to.HasValue) query = query.Where(s => s.StartedAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(x =>
                (x.GuestName != null && x.GuestName.Contains(s)) ||
                (x.GuestEmail != null && x.GuestEmail.Contains(s)) ||
                x.GuestId.Contains(s));
        }

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

        var pageSessions = await query
            .OrderByDescending(s => s.StartedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var sessionIds = pageSessions.Select(s => s.SessionId).ToList();

        // Sessions have no persisted assignee column — the only record of "who handled it" is
        // each Agent message's SenderId (email/claim), so derive it fresh per request rather
        // than backfilling a new column.
        var pageMessages = await _context.SupportMessages
            .Where(m => sessionIds.Contains(m.SessionId))
            .OrderBy(m => m.SentAt)
            .Select(m => new { m.SessionId, m.SenderType, m.SenderId })
            .ToListAsync();

        var agentsBySession = pageMessages
            .Where(m => m.SenderType == "Agent")
            .GroupBy(m => m.SessionId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(m => m.SenderId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct()));

        var countBySession = pageMessages
            .GroupBy(m => m.SessionId)
            .ToDictionary(g => g.Key, g => g.Count());

        var items = pageSessions.Select(s => new ChatSessionListItem
        {
            SessionId = s.SessionId,
            GuestId = s.GuestId,
            GuestName = s.GuestName,
            GuestEmail = s.GuestEmail,
            Status = s.Status,
            StartedAt = s.StartedAt,
            ClosedAt = s.ClosedAt,
            AgentNames = agentsBySession.TryGetValue(s.SessionId, out var names) && !string.IsNullOrEmpty(names) ? names : null,
            MessageCount = countBySession.TryGetValue(s.SessionId, out var c) ? c : 0,
        }).ToList();

        return Ok(new PagedResult<ChatSessionListItem>
        {
            Data = items,
            Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
        });
    }
}

public class ChatSessionListItem
{
    public Guid SessionId { get; set; }
    public string GuestId { get; set; } = string.Empty;
    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? AgentNames { get; set; } // comma-joined distinct agent SenderIds, first-reply order; null = never answered
    public int MessageCount { get; set; }
}
