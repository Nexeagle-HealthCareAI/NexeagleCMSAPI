using System.Collections.Concurrent;
using System.Security.Claims;
using CMSAPI.Authorization;
using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Hubs;

/// <summary>
/// Live support chat between website visitors (anonymous "guests", NexEagleWebsite LiveChat.tsx) and
/// CMS agents (NexeagleCMSWeb useSupportStore.ts).
///
/// Who may do what (2026-10-19):
///  * A guest may only read and post in the one session it joined on its current connection.
///  * Only signed-in CMS users holding the live-support.view permission are agents: they join the
///    "Agents" group, see every active session and may reply to or close any of them.
///
/// Server -> guest events:  SessionJoined, LoadHistory, ReceiveMessage, SessionClosed, AgentStatus(bool online), AgentTyping
/// Server -> agent events:  ActiveSessions, NewSession, SessionUpdate, SessionRemoved, GuestPresence(sessionId, online), GuestTyping(sessionId)
///
/// Presence is tracked in memory, which is correct for the single API instance deployed today; a
/// scale-out to several instances would need a SignalR backplane and a shared presence store.
/// </summary>
public class ChatHub : Hub
{
    public const string AgentPermission = "live-support.view";
    public const int MaxMessageLength = 2000;
    private const int HistoryLimit = 200;
    private const string AgentsGroup = "Agents";
    private const string JoinedSessionKey = "SessionId";

    // connectionId -> 0 for agents; sessionId -> live guest connection count.
    private static readonly ConcurrentDictionary<string, byte> AgentConnections = new();
    private static readonly ConcurrentDictionary<Guid, int> GuestConnections = new();

    private readonly AppDbContext _context;

    public ChatHub(AppDbContext context)
    {
        _context = context;
    }

    private bool IsAgent =>
        Context.User?.Identity?.IsAuthenticated == true
        && Context.User.Claims.Any(c => c.Type == PermissionConstants.ClaimType && c.Value == AgentPermission);

    private Guid? JoinedSessionId =>
        Context.Items.TryGetValue(JoinedSessionKey, out var joined) && joined is Guid id ? id : null;

    public static bool AnyAgentOnline => !AgentConnections.IsEmpty;

    // ── Guests ────────────────────────────────────────────────────────────────────────────────────

    // Guest entry point - intentionally anonymous (the public chat widget has no JWT). Joins the
    // guest's active session, or starts a new one when there is none (e.g. after an agent closed it).
    public async Task JoinSession(string guestId, string? name = null, string? email = null)
    {
        guestId = (guestId ?? string.Empty).Trim();
        if (guestId.Length is < 8 or > 100) throw new HubException("Invalid guest id.");
        name = Clip(name, 100);
        email = Clip(email, 150);
        if (!string.IsNullOrEmpty(email) && !IsValidEmail(email)) email = null;   // validate before storing PII

        var session = await _context.SupportSessions
            .FirstOrDefaultAsync(s => s.GuestId == guestId && s.Status == "Active");

        var isNew = session == null;
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
            var updated = false;
            if (!string.IsNullOrEmpty(name) && session.GuestName != name) { session.GuestName = name; updated = true; }
            if (!string.IsNullOrEmpty(email) && session.GuestEmail != email) { session.GuestEmail = email; updated = true; }
            if (updated) await _context.SaveChangesAsync();
        }

        // A connection belongs to one session; leaving an old one (new chat after a close) first.
        if (JoinedSessionId is { } previous && previous != session.SessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, previous.ToString());
            await MarkGuestOfflineAsync(previous);
        }

        var firstJoinOnThisConnection = JoinedSessionId != session.SessionId;
        Context.Items[JoinedSessionKey] = session.SessionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId.ToString());

        if (firstJoinOnThisConnection && GuestConnections.AddOrUpdate(session.SessionId, 1, (_, n) => n + 1) == 1 && !isNew)
            await Clients.Group(AgentsGroup).SendAsync("GuestPresence", session.SessionId, true);

        // Only announce genuinely new sessions; a reconnecting guest shouldn't re-alert agents.
        if (isNew)
            await Clients.Group(AgentsGroup).SendAsync("NewSession", ToSummary(session, null, guestOnline: true));

        var history = await _context.SupportMessages.AsNoTracking()
            .Where(m => m.SessionId == session.SessionId)
            .OrderByDescending(m => m.SentAt).Take(HistoryLimit)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        await Clients.Caller.SendAsync("SessionJoined", session.SessionId);
        await Clients.Caller.SendAsync("LoadHistory", history);
        await Clients.Caller.SendAsync("AgentStatus", AnyAgentOnline);
    }

    // ── Agents ────────────────────────────────────────────────────────────────────────────────────

    public async Task JoinAgentGroup()
    {
        if (!IsAgent) throw new HubException("Live support permission required.");

        await Groups.AddToGroupAsync(Context.ConnectionId, AgentsGroup);
        var wasEmpty = AgentConnections.IsEmpty;
        AgentConnections[Context.ConnectionId] = 0;
        if (wasEmpty) await Clients.All.SendAsync("AgentStatus", true);

        var sessions = await _context.SupportSessions.AsNoTracking()
            .Where(s => s.Status == "Active")
            .OrderByDescending(s => s.StartedAt)
            .Take(50) // cap initial payload; agents can paginate via REST if needed
            .Select(s => new
            {
                Session = s,
                Last = s.Messages.OrderByDescending(m => m.SentAt)
                    .Select(m => new { m.MessageText, m.SentAt, m.SenderType })
                    .FirstOrDefault()
            })
            .ToListAsync();

        await Clients.Caller.SendAsync("ActiveSessions", sessions.Select(x => ToSummary(
            x.Session,
            x.Last == null ? ((string, DateTime, string)?)null : (x.Last.MessageText, x.Last.SentAt, x.Last.SenderType),
            GuestConnections.TryGetValue(x.Session.SessionId, out var n) && n > 0)).ToList());
    }

    public async Task CloseSession(Guid sessionId)
    {
        if (!IsAgent) throw new HubException("Live support permission required.");

        var session = await _context.SupportSessions.FindAsync(sessionId);
        if (session == null || session.Status != "Active") return;

        session.Status = "Closed";
        session.ClosedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await Clients.Group(sessionId.ToString()).SendAsync("SessionClosed", sessionId);
        await Clients.Group(AgentsGroup).SendAsync("SessionRemoved", sessionId);
    }

    // ── Both ──────────────────────────────────────────────────────────────────────────────────────

    // senderType/senderId are resolved from the connection, never trusted from the client. The two
    // trailing parameters are ignored and kept only so older clients' calls still bind.
    public async Task SendMessage(Guid sessionId, string message, string? senderType = null, string? senderId = null)
    {
        message = (message ?? string.Empty).Trim();
        if (message.Length == 0) return;
        if (message.Length > MaxMessageLength) throw new HubException($"Messages are limited to {MaxMessageLength} characters.");

        var isAgent = IsAgent;
        if (!isAgent && JoinedSessionId != sessionId) return;   // guests: only the session joined on this connection

        var session = await _context.SupportSessions.FindAsync(sessionId);
        if (session == null || session.Status != "Active") throw new HubException("This chat has ended.");

        var msg = new SupportMessage
        {
            SessionId = sessionId,
            MessageText = message,
            SenderType = isAgent ? "Agent" : "Guest",
            SenderId = isAgent
                ? (Context.User?.FindFirst(ClaimTypes.Email)?.Value
                   ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? "Agent")
                : session.GuestId,
            SentAt = DateTime.UtcNow
        };

        _context.SupportMessages.Add(msg);
        await _context.SaveChangesAsync();

        // To the session group (the guest) and to all agents (console + previews).
        await Clients.Group(sessionId.ToString()).SendAsync("ReceiveMessage", msg);
        await Clients.Group(AgentsGroup).SendAsync("SessionUpdate", sessionId, msg);
    }

    // "Is typing" signal. Clients throttle it (about one call every 2-3 seconds while typing).
    public async Task Typing(Guid sessionId)
    {
        if (IsAgent)
            await Clients.Group(sessionId.ToString()).SendAsync("AgentTyping");
        else if (JoinedSessionId == sessionId)
            await Clients.Group(AgentsGroup).SendAsync("GuestTyping", sessionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (AgentConnections.TryRemove(Context.ConnectionId, out _) && AgentConnections.IsEmpty)
            await Clients.All.SendAsync("AgentStatus", false);

        if (JoinedSessionId is { } sessionId)
            await MarkGuestOfflineAsync(sessionId);

        await base.OnDisconnectedAsync(exception);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private async Task MarkGuestOfflineAsync(Guid sessionId)
    {
        var remaining = GuestConnections.AddOrUpdate(sessionId, 0, (_, n) => Math.Max(0, n - 1));
        if (remaining == 0)
        {
            GuestConnections.TryRemove(new KeyValuePair<Guid, int>(sessionId, 0));
            await Clients.Group(AgentsGroup).SendAsync("GuestPresence", sessionId, false);
        }
    }

    // What agents see in their session list.
    private static object ToSummary(SupportSession s, (string Text, DateTime At, string SenderType)? last, bool guestOnline) => new
    {
        s.SessionId,
        s.GuestId,
        s.GuestName,
        s.GuestEmail,
        s.Status,
        s.StartedAt,
        LastMessageText = last?.Text is { Length: > 140 } t ? t[..140] + "…" : last?.Text,
        LastMessageAt = last?.At,
        LastSenderType = last?.SenderType,
        GuestOnline = guestOnline,
    };

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }

    private static bool IsValidEmail(string email)
    {
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch { return false; }
    }
}
