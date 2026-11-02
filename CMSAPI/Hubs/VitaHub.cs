using CMSAPI.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CMSAPI.Hubs;

/// <summary>
/// Real-time Vita Test Agent run updates for the CMS "Vita Test Agent" tab. Only signed-in users with vita.view may connect
/// (the JWT arrives as the access_token query param, see Program.cs). Server-to-client only: nothing is callable by clients.
/// Vita pushes an update by calling POST /api/v1/vita/events with its X-Service-Key (VitaController.Events), which relays it
/// here through <see cref="BroadcastRunUpdate"/>.
/// </summary>
[HasPermission("vita.view")]
public class VitaHub : Hub
{
    // Event name shared with NexeagleCMSWeb (src/features/vitaAgents/services/vitaApi.ts). Change both together.
    public const string RunUpdated = "vita-run-updated";

    // payload is whatever Vita sent: { repo, run_id, status: queued|running|done, verdict?, trigger?, summary? }.
    public static Task BroadcastRunUpdate(IHubContext<VitaHub> hub, object payload) =>
        hub.Clients.All.SendAsync(RunUpdated, payload);
}
