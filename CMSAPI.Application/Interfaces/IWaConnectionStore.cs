namespace CMSAPI.Application.Interfaces;

/// <summary>
/// The Meta connection of the WhatsApp marketing number, as entered in CMS → WhatsApp → Setup (table
/// WaConnection, secrets encrypted). A value left empty there falls back to the server setting of the same
/// name (WhatsAppMarketing__PhoneNumberId and so on), so a deploy that sets them keeps working.
/// </summary>
public interface IWaConnectionStore
{
    /// <summary>What is in use now (read once from the database, then kept up to date by RefreshAsync).</summary>
    WaConnectionInfo Current { get; }
    Task<WaConnectionInfo> RefreshAsync(CancellationToken ct = default);
    /// <summary>Notes that Meta reached the webhook (at most once a minute is written).</summary>
    Task MarkWebhookAsync(CancellationToken ct = default);
}

public record WaConnectionInfo(
    string? PhoneNumberId,
    string? WabaId,
    string? AccessToken,
    string? AppId,
    string? AppSecret,
    string? VerifyToken,
    string GraphVersion,
    /// <summary>"cms", "server" or "none": where the token comes from.</summary>
    string TokenSource,
    /// <summary>The stored secrets could not be read (the encryption key changed): enter them again.</summary>
    bool Unreadable)
{
    public bool IsConfigured => PhoneNumberId != null && WabaId != null && AccessToken != null;
    public static WaConnectionInfo Empty { get; } = new(null, null, null, null, null, null, "v23.0", "none", false);
}
