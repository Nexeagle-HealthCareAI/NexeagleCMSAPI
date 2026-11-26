using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// Sends one message: the row is saved first (queued), then Meta is asked, then the row gets Meta's id
/// ("sent") or the reason it failed. A message is never sent without a row to receive its statuses.
/// </summary>
internal static class WaSender
{
    public static async Task SendAsync(CmsDbContext db, IWaMeta meta, WaContact contact, WaMessage row, JsonObject payload, CancellationToken ct = default)
    {
        row.ContactId = contact.ContactId;
        row.Direction = "Out";
        row.Status = "queued";
        row.CreatedAt = row.CreatedAt == default ? DateTime.UtcNow : row.CreatedAt;
        db.WaMessages.Add(row);
        contact.LastMessageAt = row.CreatedAt;
        contact.LastMessagePreview = WaRules.Preview(row.Type, row.Body);
        contact.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var message = (JsonObject)payload.DeepClone();
        message["to"] = contact.Phone;
        try
        {
            row.WamId = await meta.SendAsync(message, ct);
            row.Status = "sent";
            row.StatusAt = DateTime.UtcNow;
        }
        catch (WaMetaException ex)
        {
            row.Status = "failed";
            row.StatusAt = DateTime.UtcNow;
            row.ErrorCode = ex.Code;
            row.ErrorText = WaShared.FriendlyError(ex.Code, ex.Message);
            if (ex.Code == 131050 && contact.OptIn != "OptedOut")
            {
                contact.OptIn = "OptedOut";
                contact.OptOutAt = DateTime.UtcNow;
                contact.OptOutReason = "Stopped marketing messages in WhatsApp";
            }
        }
        catch (WaNotConnectedException)
        {
            row.Status = "failed";
            row.StatusAt = DateTime.UtcNow;
            row.ErrorText = "The WhatsApp marketing number is not connected on this environment.";
        }
        await db.SaveChangesAsync(ct);
    }
}
