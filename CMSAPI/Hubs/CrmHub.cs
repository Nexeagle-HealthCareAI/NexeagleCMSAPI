using CMSAPI.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CMSAPI.Hubs;

/// <summary>
/// Real-time CRM alerts for CMS admins. Lead payloads carry phone numbers, so only signed-in users
/// with marketing.view may connect (the JWT arrives as the access_token query param, see Program.cs).
/// Server-to-client only: CrmWebhookController pushes the events below.
/// </summary>
[HasPermission("marketing.view")]
public class CrmHub : Hub
{
    // Event names shared with NexeagleCMSWeb (src/components/Layout.tsx). Change both together.
    public const string HotLeadReceived = "crm-hot-lead-received";
    public const string InboundWhatsApp = "crm-inbound-whatsapp";
}
