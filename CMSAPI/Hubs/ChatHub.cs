using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CMSAPI.Hubs;

public class ChatHub : Hub
{
    private readonly AppDbContext _context;

    // Per-connection key holding the session a guest has joined, so a guest can only
    // post into its own session.
    private const string JoinedSessionKey = "SessionId";

    public ChatHub(AppDbContext context)
    {
        _context = context;
    }

    // Guest entry point — intentionally anonymous (the public chat widget has no JWT).
    public async Task JoinSession(string guestId, string? name = null, string? email = null)
    {
        // Validate email format before storing PII.
        if (!string.IsNullOrEmpty(email) && !IsValidEmail(email))
            email = null;
        var session = await _context.SupportSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.GuestId == guestId && s.Status == "Active");

        bool isNew = session == null;
        if (session == null)
        {
            session = new SupportSession
            {
                GuestId = guestId,
                GuestName = name,
                GuestEmail = email,
                Status = "Active",
                StartedAt = DateTime.UtcNow
            };
            _context.SupportSessions.Add(session);
            await _context.SaveChangesAsync();
        }
        else if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(email))
        {
            bool updated = false;
            if (!string.IsNullOrEmpty(name) && session.GuestName != name) { session.GuestName = name; updated = true; }
            if (!string.IsNullOrEmpty(email) && session.GuestEmail != email) { session.GuestEmail = email; updated = true; }
            if (updated) await _context.SaveChangesAsync();
        }

        // Remember which session this connection owns so SendMessage can authorize the guest.
        Context.Items[JoinedSessionKey] = session.SessionId;

        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId.ToString());

        // Only announce genuinely new sessions; a reconnecting guest shouldn't re-alert agents.
        if (isNew)
        {
            await Clients.Group("Agents").SendAsync("NewSession", session);
        }

        // Explicitly send the SessionId to the guest (fixes issue where new sessions without history lose the ID)
        await Clients.Caller.SendAsync("SessionJoined", session.SessionId);

        // Send history to guest
        await Clients.Caller.SendAsync("LoadHistory", session.Messages.OrderBy(m => m.SentAt).ToList());
    }

    [Authorize]
    public async Task JoinAgentGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");

        var activeSessions = await _context.SupportSessions
            .Where(s => s.Status == "Active")
            .OrderByDescending(s => s.StartedAt)
            .Take(50) // cap initial payload; agents can paginate via REST if needed
            .ToListAsync();

        await Clients.Caller.SendAsync("ActiveSessions", activeSessions);
    }

    // Shared by guests (anonymous) and agents (authenticated). senderType/senderId are
    // resolved from the connection, never trusted from the client, to prevent impersonation.
    public async Task SendMessage(Guid sessionId, string message, string? senderType = null, string? senderId = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var session = await _context.SupportSessions.FindAsync(sessionId);
        if (session == null || session.Status != "Active") return;

        var isAgent = Context.User?.Identity?.IsAuthenticated == true;

        if (!isAgent)
        {
            // A guest may only post into the session it joined on this connection.
            if (Context.Items.TryGetValue(JoinedSessionKey, out var joined)
                && joined is Guid joinedId && joinedId != sessionId)
            {
                return;
            }
        }

        var resolvedSenderType = isAgent ? "Agent" : "Guest";
        var resolvedSenderId = isAgent
            ? (Context.User?.FindFirst(ClaimTypes.Email)?.Value
               ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? "Agent")
            : session.GuestId;

        var msg = new SupportMessage
        {
            SessionId = sessionId,
            MessageText = message,
            SenderType = resolvedSenderType,
            SenderId = resolvedSenderId,
            SentAt = DateTime.UtcNow
        };

        _context.SupportMessages.Add(msg);
        await _context.SaveChangesAsync();

        // Broadcast to the session group (the guest) and to all agents (console + previews).
        await Clients.Group(sessionId.ToString()).SendAsync("ReceiveMessage", msg);
        await Clients.Group("Agents").SendAsync("SessionUpdate", sessionId, msg);
    }

    [Authorize]
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

    private static bool IsValidEmail(string email)
    {
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch { return false; }
    }
}
