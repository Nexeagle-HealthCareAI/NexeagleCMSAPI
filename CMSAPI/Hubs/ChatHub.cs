using Microsoft.AspNetCore.SignalR;
using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Hubs;

public class ChatHub : Hub
{
    private readonly AppDbContext _context;

    public ChatHub(AppDbContext context)
    {
        _context = context;
    }

    public async Task JoinSession(string guestId)
    {
        var session = await _context.SupportSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.GuestId == guestId && s.Status == "Active");

        if (session == null)
        {
            session = new SupportSession
            {
                GuestId = guestId,
                Status = "Active",
                StartedAt = DateTime.UtcNow
            };
            _context.SupportSessions.Add(session);
            await _context.SaveChangesAsync();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId.ToString());
        
        // Notify agents about a new/active session
        await Clients.Group("Agents").SendAsync("NewSession", session);
        
        // Send history to guest
        await Clients.Caller.SendAsync("LoadHistory", session.Messages.OrderBy(m => m.SentAt).ToList());
    }

    public async Task JoinAgentGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");
        
        var activeSessions = await _context.SupportSessions
            .Where(s => s.Status == "Active")
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync();
            
        await Clients.Caller.SendAsync("ActiveSessions", activeSessions);
    }

    public async Task SendMessage(Guid sessionId, string message, string senderType, string? senderId)
    {
        var msg = new SupportMessage
        {
            SessionId = sessionId,
            MessageText = message,
            SenderType = senderType,
            SenderId = senderId,
            SentAt = DateTime.UtcNow
        };

        _context.SupportMessages.Add(msg);
        await _context.SaveChangesAsync();

        // Broadcast to the session group (both guest and agents listening to this session)
        await Clients.Group(sessionId.ToString()).SendAsync("ReceiveMessage", msg);
        
        // Also notify all agents in the general "Agents" group for updates (e.g. preview)
        await Clients.Group("Agents").SendAsync("SessionUpdate", sessionId, msg);
    }

    public async Task CloseSession(Guid sessionId)
    {
        var session = await _context.SupportSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = "Closed";
            session.ClosedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
            await Clients.Group(sessionId.ToString()).SendAsync("SessionClosed", sessionId);
            await Clients.Group("Agents").SendAsync("SessionRemoved", sessionId);
        }
    }
}
