using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Google;

/// <summary>
/// Tells Google Ads which leads turned into business: when a Google lead (lead form, or website form with a click ID)
/// reaches a chosen CRM stage, it is sent once as an offline click conversion through the Data Manager API (the Google
/// Ads API takes no new users for this since June 2026). Only the click ID, the time and a value are sent; never a
/// name, phone number or email. Clicks older than 90 days are not accepted by Google and are skipped.
/// </summary>
public class GoogleOutcomesService : IGoogleOutcomesService
{
    public const int MaxAttempts = 5;
    private static readonly TimeSpan ClickLife = TimeSpan.FromDays(90);

    private readonly CmsDbContext _db;
    private readonly IGoogleApi _google;
    private readonly GoogleKeys _keys;
    private readonly ILogger<GoogleOutcomesService> _logger;
    private readonly Func<DateTime> _now;

    public GoogleOutcomesService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, ILogger<GoogleOutcomesService> logger)
        : this(db, google, keys, logger, () => DateTime.UtcNow) { }

    internal GoogleOutcomesService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, ILogger<GoogleOutcomesService> logger, Func<DateTime> now)
    {
        _db = db;
        _google = google;
        _keys = keys;
        _logger = logger;
        _now = now;
    }

    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var sent = 0;
        var accounts = await _db.GoogleAccounts.Where(a => a.IsActive && a.OutcomesJson != null && a.AdsCustomerId != null && a.RefreshTokenEnc != null).ToListAsync(ct);
        foreach (var a in accounts)
        {
            var outcomes = GoogleShared.Read<List<GoogleOutcome>>(a.OutcomesJson) ?? new();
            var client = _keys.Client(a);
            if (outcomes.Count == 0 || client == null) continue;
            try { sent += await SendAccountAsync(a, client, outcomes, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Google results for {Account} could not be sent.", a.Name);
                a.LastOutcomeError = GoogleShared.Trim(ex.Message, 1000);
                await _db.SaveChangesAsync(ct);
            }
        }
        return sent;
    }

    private async Task<int> SendAccountAsync(GoogleAccount a, GoogleClient client, List<GoogleOutcome> outcomes, CancellationToken ct)
    {
        var now = _now();
        var oldest = now - ClickLife;
        var leads = await _db.GoogleLeads.Where(l => l.AccountId == a.AccountId && l.ReceivedAt >= oldest && (l.Gclid != null || l.Gbraid != null || l.Wbraid != null))
            .ToListAsync(ct);
        if (leads.Count == 0) return 0;
        var leadIds = leads.Select(l => l.LeadId).ToList();
        var crm = (await _db.CmsSalesLeads.AsNoTracking().Where(x => leadIds.Contains(x.LeadId) && !x.IsDeleted)
            .Select(x => new { x.LeadId, x.Stage, x.DealValue, x.UpdatedAt }).ToListAsync(ct)).ToDictionary(x => x.LeadId);
        var googleIds = leads.Select(l => l.GoogleLeadId).ToList();
        var done = await _db.GoogleConversionUploads.Where(u => googleIds.Contains(u.GoogleLeadId)).ToListAsync(ct);

        var sent = 0;
        var tried = 0;
        string? lastError = null;
        foreach (var o in outcomes)
        {
            var due = new List<(GoogleLead Lead, GoogleConversionUpload? Earlier, decimal? Value, DateTime At)>();
            foreach (var l in leads)
            {
                if (!crm.TryGetValue(l.LeadId, out var c) || !GoogleShared.Reached(c.Stage, o.Stage)) continue;
                var earlier = done.FirstOrDefault(u => u.GoogleLeadId == l.GoogleLeadId && u.Stage == o.Stage);
                if (earlier is { Status: "sent" } || earlier is { Attempts: >= MaxAttempts }) continue;
                var value = o.Stage == "Closed Won" && c.DealValue > 0 ? c.DealValue : o.Value;
                // The conversion happened after the click: never before the lead arrived, never in the future.
                var at = c.UpdatedAt < l.ReceivedAt ? l.ReceivedAt : c.UpdatedAt > now ? now : c.UpdatedAt;
                due.Add((l, earlier, value, at));
            }
            foreach (var chunk in due.Chunk(500))
            {
                var batch = new ConversionBatch
                {
                    CustomerId = a.AdsCustomerId!, LoginCustomerId = a.AdsLoginCustomerId, ConversionActionId = o.ConversionActionId,
                    Events = chunk.Select(d => new ConversionEvent
                    {
                        Gclid = d.Lead.Gclid, Gbraid = d.Lead.Gbraid, Wbraid = d.Lead.Wbraid, EventTimeUtc = d.At,
                        TransactionId = $"{d.Lead.GoogleLeadId:N}-{o.Stage.Replace(' ', '-').ToLowerInvariant()}", Value = d.Value, Currency = a.Currency ?? "INR",
                    }).ToList(),
                };
                string? requestId = null;
                string? error = null;
                tried += chunk.Length;
                try { requestId = await _google.IngestConversionsAsync(client, batch, ct); }
                catch (GoogleApiException ex) { error = ex.Message; lastError = $"{o.Stage}: {ex.Message}"; }
                foreach (var d in chunk)
                {
                    var row = d.Earlier;
                    if (row == null)
                    {
                        row = new GoogleConversionUpload { UploadId = Guid.NewGuid(), AccountId = a.AccountId, GoogleLeadId = d.Lead.GoogleLeadId, Stage = o.Stage, Attempts = 0 };
                        _db.GoogleConversionUploads.Add(row);
                        done.Add(row);
                    }
                    row.ConversionActionId = o.ConversionActionId;
                    row.Attempts += 1;
                    row.SentAt = now;
                    row.Status = error == null ? "sent" : "failed";
                    row.RequestId = requestId;
                    row.Error = GoogleShared.Trim(error, 1000);
                }
                if (error == null) sent += chunk.Length;
                await _db.SaveChangesAsync(ct);
            }
        }
        if (sent > 0) a.LastOutcomeAt = now;
        // A run with nothing to send keeps the last refusal, so Setup still shows why results stopped.
        if (tried > 0) a.LastOutcomeError = GoogleShared.Trim(lastError, 1000);
        await _db.SaveChangesAsync(ct);
        return sent;
    }
}
