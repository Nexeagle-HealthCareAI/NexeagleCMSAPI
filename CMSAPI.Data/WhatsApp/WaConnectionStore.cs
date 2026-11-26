using System.Security.Cryptography;
using CMSAPI.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// Keeps the marketing number's Meta connection in memory (one per API process). Read from WaConnection on
/// first use and after every change made on the Setup page; the send worker also refreshes it every minute,
/// so a change made on another API instance is picked up too.
/// </summary>
public class WaConnectionStore : IWaConnectionStore
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly WaSecretBox _box;
    private readonly ILogger<WaConnectionStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile WaConnectionInfo? _current;
    private DateTime _lastMark = DateTime.MinValue;

    public WaConnectionStore(IServiceScopeFactory scopes, IConfiguration config, WaSecretBox box, ILogger<WaConnectionStore> logger)
    {
        _scopes = scopes;
        _config = config;
        _box = box;
        _logger = logger;
    }

    public WaConnectionInfo Current
    {
        get
        {
            var c = _current;
            if (c != null) return c;
            // First use in this process: read it once. Runs on the thread pool, so waiting here is safe.
            try { return RefreshAsync().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp marketing connection could not be read; using the server settings only.");
                return _current = FromSettings();
            }
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private WaConnectionInfo FromSettings()
    {
        var token = Clean(_config["WhatsAppMarketing:AccessToken"]);
        return new WaConnectionInfo(
            Clean(_config["WhatsAppMarketing:PhoneNumberId"]), Clean(_config["WhatsAppMarketing:WabaId"]), token,
            Clean(_config["WhatsAppMarketing:AppId"]), Clean(_config["WhatsAppMarketing:AppSecret"]), Clean(_config["WhatsAppMarketing:VerifyToken"]),
            Clean(_config["WhatsAppMarketing:GraphVersion"]) ?? "v23.0", token != null ? "server" : "none", false);
    }

    public async Task<WaConnectionInfo> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var server = FromSettings();
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var row = await db.WaConnections.AsNoTracking().FirstOrDefaultAsync(x => x.ConnectionId == 1, ct);
            if (row == null) return _current = server;

            var unreadable = false;
            string? Open(string? sealedText)
            {
                try { return _box.Open(sealedText); }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    unreadable = true;
                    return null;
                }
            }
            var token = Open(row.AccessTokenEnc);
            var info = new WaConnectionInfo(
                Clean(row.PhoneNumberId) ?? server.PhoneNumberId,
                Clean(row.WabaId) ?? server.WabaId,
                token ?? server.AccessToken,
                Clean(row.AppId) ?? server.AppId,
                Open(row.AppSecretEnc) ?? server.AppSecret,
                Open(row.VerifyTokenEnc) ?? server.VerifyToken,
                server.GraphVersion,
                token != null ? "cms" : server.TokenSource,
                unreadable);
            if (unreadable) _logger.LogWarning("WhatsApp marketing: stored secrets could not be decrypted (the key changed?). Enter them again in WhatsApp → Setup.");
            return _current = info;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkWebhookAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (now - _lastMark < TimeSpan.FromMinutes(1)) return;
        _lastMark = now;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var row = await db.WaConnections.FirstOrDefaultAsync(x => x.ConnectionId == 1, ct);
            if (row == null) return;
            row.LastWebhookAt = now;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp marketing: could not note the webhook's last call.");
        }
    }
}
