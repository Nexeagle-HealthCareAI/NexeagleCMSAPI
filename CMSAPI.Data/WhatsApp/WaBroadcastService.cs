using System.Text.Json;
using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// See IWaBroadcastService. Only opted-in people receive a broadcast. Before it is created the cost is
/// estimated; when this month's spend, what other broadcasts will still spend and this estimate pass the
/// monthly budget, it waits for someone with whatsapp.approve. While sending, an unapproved broadcast stops
/// at the budget ("Paused: budget reached").
/// </summary>
public class WaBroadcastService : IWaBroadcastService
{
    private static readonly HashSet<int> TooFast = new() { 130429, 131048, 80007, 131056 };
    private readonly CmsDbContext _db;
    private readonly IWaMeta _meta;
    private readonly IDocFileStore _files;
    private readonly ILogger<WaBroadcastService> _logger;
    private readonly Func<DateTime> _now;
    // A header sample uploaded to Meta once per broadcast (Meta keeps media ids for 30 days).
    private static readonly Dictionary<Guid, string> HeaderMedia = new();

    public WaBroadcastService(CmsDbContext db, IWaMeta meta, IDocFileStore files, ILogger<WaBroadcastService> logger)
        : this(db, meta, files, logger, () => DateTime.UtcNow) { }

    internal WaBroadcastService(CmsDbContext db, IWaMeta meta, IDocFileStore files, ILogger<WaBroadcastService> logger, Func<DateTime> now)
    {
        _db = db;
        _meta = meta;
        _files = files;
        _logger = logger;
        _now = now;
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    public async Task<WorkResult<List<WaBroadcastDto>>> ListAsync(WaActor actor)
    {
        var list = await _db.WaBroadcasts.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(200).ToListAsync();
        return WorkResult<List<WaBroadcastDto>>.Success(await DtosAsync(list));
    }

    public async Task<WorkResult<WaBroadcastDto>> GetAsync(WaActor actor, Guid broadcastId)
    {
        var b = await _db.WaBroadcasts.AsNoTracking().FirstOrDefaultAsync(x => x.BroadcastId == broadcastId);
        if (b == null) return WorkResult<WaBroadcastDto>.Fail(404, "This broadcast does not exist.");
        return WorkResult<WaBroadcastDto>.Success((await DtosAsync(new List<WaBroadcast> { b }))[0]);
    }

    private async Task<List<WaBroadcastDto>> DtosAsync(List<WaBroadcast> list)
    {
        var ids = list.Select(b => b.BroadcastId).ToList();
        var templateIds = list.Select(b => b.TemplateId).Distinct().ToList();
        var templates = await _db.WaTemplates.AsNoTracking().Where(t => templateIds.Contains(t.TemplateId)).ToDictionaryAsync(t => t.TemplateId);
        var pending = await _db.WaBroadcastRecipients.AsNoTracking().Where(r => ids.Contains(r.BroadcastId) && r.Status == "Pending")
            .GroupBy(r => r.BroadcastId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var names = await WaShared.NamesAsync(_db, list.Select(b => b.CreatedByUserId).Concat(list.Select(b => b.ApprovedByUserId)));
        return list.Select(b =>
        {
            var t = templates.GetValueOrDefault(b.TemplateId);
            return new WaBroadcastDto
        {
            BroadcastId = b.BroadcastId,
            Name = b.Name,
            TemplateId = b.TemplateId,
            TemplateName = t?.Name ?? "(deleted template)",
            Category = t?.Category ?? string.Empty,
            Status = b.Status,
            PausedReason = b.PausedReason,
            ScheduledAt = b.ScheduledAt,
            StartedAt = b.StartedAt,
            FinishedAt = b.FinishedAt,
            Recipients = b.Recipients,
            Pending = pending.GetValueOrDefault(b.BroadcastId),
            Sent = b.Sent,
            Delivered = b.Delivered,
            Read = b.Read,
            Failed = b.Failed,
            Replied = b.Replied,
            EstimatedCost = b.EstimatedCost,
            ActualCost = b.ActualCost,
            Currency = b.Currency,
            CreatedBy = b.CreatedByUserId is { } c ? names.GetValueOrDefault(c) : null,
            ApprovedBy = b.ApprovedByUserId is { } a ? names.GetValueOrDefault(a) : null,
            CreatedAt = b.CreatedAt,
        };
        }).ToList();
    }

    public async Task<WorkResult<List<WaRecipientDto>>> RecipientsAsync(WaActor actor, Guid broadcastId, string? status)
    {
        if (!await _db.WaBroadcasts.AnyAsync(b => b.BroadcastId == broadcastId)) return WorkResult<List<WaRecipientDto>>.Fail(404, "This broadcast does not exist.");
        var q = from r in _db.WaBroadcastRecipients.AsNoTracking()
                join c in _db.WaContacts.AsNoTracking() on r.ContactId equals c.ContactId
                where r.BroadcastId == broadcastId
                select new { r, c };
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.r.Status == status);
        var rows = await q.OrderBy(x => x.r.Status).ThenBy(x => x.c.Name).Take(2000).ToListAsync();
        return WorkResult<List<WaRecipientDto>>.Success(rows.Select(x => new WaRecipientDto
        {
            ContactId = x.c.ContactId, DisplayPhone = WaRules.Display(x.c.Phone), Name = x.c.Name ?? x.c.ProfileName,
            Status = x.r.Status, Error = x.r.Error, SentAt = x.r.SentAt,
        }).ToList());
    }

    // ── Planning ─────────────────────────────────────────────────────────────

    private sealed record Plan(WaTemplate Template, WaTemplateParts Parts, List<WaContact> Matched, List<WaContact> Sending, decimal Estimate, Dictionary<Guid, decimal> EachCost);

    /// <summary>The template, the audience and what it costs, or why it cannot go.</summary>
    private async Task<(Plan? Plan, string? Problem)> PlanAsync(WaBroadcastRequest r)
    {
        var template = await _db.WaTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateId == r.TemplateId);
        if (template == null) return (null, "Choose a template.");
        if (template.Status != "APPROVED") return (null, "Only templates Meta has approved can be broadcast.");
        var parts = WaTemplateParts.Parse(template.Components, template.ParameterFormat);
        foreach (var key in parts.RequiredValues())
        {
            if (!r.Variables.TryGetValue(key, out var v)) return (null, $"Say how to fill {Label(key)}.");
            if (v.From is not ("name" or "phone" or "attribute" or "text")) return (null, $"{Label(key)} is filled from name, phone, a detail or a fixed text.");
            if (v.From == "attribute" && string.IsNullOrWhiteSpace(v.Attribute)) return (null, $"Choose which detail fills {Label(key)}.");
            if (v.From == "text" && string.IsNullOrWhiteSpace(v.Text)) return (null, $"Write the text for {Label(key)}.");
        }
        if (parts.HasMediaHeader)
        {
            var link = r.HeaderMediaUrl?.Trim();
            if (string.IsNullOrEmpty(link) && template.HeaderMediaKey == null)
                return (null, "This template starts with a picture, video or document: give a https link to it.");
            if (!string.IsNullOrEmpty(link) && !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return (null, "The picture, video or document link must start with https://.");
        }

        var q = _db.WaContacts.AsNoTracking().AsQueryable();
        if (r.Audience.ContactIds.Count > 0)
        {
            var ids = r.Audience.ContactIds;
            q = q.Where(c => ids.Contains(c.ContactId));
        }
        else
        {
            if (r.Audience.Sources.Count > 0) { var sources = r.Audience.Sources; q = q.Where(c => sources.Contains(c.Source)); }
        }
        var matched = await q.ToListAsync();
        if (r.Audience.ContactIds.Count == 0 && r.Audience.Tags.Count > 0)
        {
            var wanted = r.Audience.Tags.Select(t => t.Trim().ToLowerInvariant()).ToHashSet();
            matched = matched.Where(c => WaShared.Tags(c.Tags).Any(wanted.Contains)).ToList();
        }
        // Everyone the business messages first must have agreed; anyone who said stop never gets one.
        var sending = matched.Where(c => c.OptIn == "OptedIn").ToList();

        var rates = await WaShared.RatesAsync(_db);
        var category = WaRules.RateCategory(template.Category);
        var each = sending.ToDictionary(c => c.ContactId, c => WaRules.CostOf(true, c.Phone, category, rates));
        return (new Plan(template, parts, matched, sending, each.Values.Sum(), each), null);
    }

    private static string Label(string key) => key switch
    {
        "url" => "the link's variable",
        "code" => "the code",
        _ when key.StartsWith("h.") => $"the header's {{{{{key[2..]}}}}}",
        _ => $"{{{{{key}}}}}",
    };

    /// <summary>One person's values for the template's variables.</summary>
    private static Dictionary<string, string> ValuesFor(WaContact c, IReadOnlyDictionary<string, WaVariableValue> how)
    {
        var attrs = WaShared.Attributes(c.Attributes);
        var values = new Dictionary<string, string>();
        foreach (var (key, v) in how)
        {
            var value = v.From switch
            {
                "name" => FirstName(c.Name ?? c.ProfileName),
                "phone" => WaRules.Display(c.Phone),
                "attribute" => attrs.GetValueOrDefault((v.Attribute ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_')),
                _ => v.Text,
            };
            values[key] = string.IsNullOrWhiteSpace(value) ? (v.From == "text" ? string.Empty : v.Text ?? string.Empty) : value.Trim();
        }
        return values;
    }

    /// <summary>"Dr Asha Rao" → "Dr Asha"; "Asha Rao" → "Asha".</summary>
    private static string? FirstName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words[0].TrimEnd('.').ToLowerInvariant() is "dr" or "mr" or "mrs" or "ms" && words.Length > 1 ? $"{words[0]} {words[1]}" : words[0];
    }

    private async Task<(decimal Spent, decimal Committed, WaSettings Settings)> MoneyAsync()
    {
        var settings = await WaShared.SettingsAsync(_db);
        var monthStart = WaRules.MonthStart(_now());
        var spent = await _db.WaMessages.AsNoTracking().Where(m => m.Direction == "Out" && m.Cost > 0 && m.StatusAt >= monthStart).SumAsync(m => (decimal?)m.Cost) ?? 0m;
        var committed = await (from rec in _db.WaBroadcastRecipients.AsNoTracking()
                               join b in _db.WaBroadcasts.AsNoTracking() on rec.BroadcastId equals b.BroadcastId
                               where rec.Status == "Pending" && (b.Status == "Scheduled" || b.Status == "Sending" || b.Status == "Paused")
                               select (decimal?)rec.EstimatedCost).SumAsync() ?? 0m;
        return (spent, committed, settings);
    }

    public async Task<WorkResult<WaBroadcastPreview>> PreviewAsync(WaActor actor, WaBroadcastRequest request)
    {
        var (plan, problem) = await PlanAsync(request);
        if (plan == null) return WorkResult<WaBroadcastPreview>.Fail(400, problem!);
        var (spent, committed, settings) = await MoneyAsync();
        var budget = settings.MonthlyBudget is > 0 ? settings.MonthlyBudget : null;
        return WorkResult<WaBroadcastPreview>.Success(new WaBroadcastPreview
        {
            Matched = plan.Matched.Count,
            WillSend = plan.Sending.Count,
            NotOptedIn = plan.Matched.Count(c => c.OptIn == "Unknown"),
            OptedOut = plan.Matched.Count(c => c.OptIn == "OptedOut"),
            EstimatedCost = plan.Estimate,
            Currency = settings.Currency,
            SpentThisMonth = spent,
            Committed = committed,
            MonthlyBudget = budget,
            OverBudget = budget != null && spent + committed + plan.Estimate > budget,
            Samples = plan.Sending.Take(3).Select(c => $"{c.Name ?? c.ProfileName ?? WaRules.Display(c.Phone)}: {plan.Parts.Render(ValuesFor(c, request.Variables))}").ToList(),
        });
    }

    public async Task<WorkResult<WaBroadcastDto>> CreateAsync(WaActor actor, WaBroadcastRequest request)
    {
        if (!actor.CanBroadcast) return WorkResult<WaBroadcastDto>.Fail(403, "You cannot send broadcasts. Ask an administrator for \"Create and send WhatsApp broadcasts\".");
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) return WorkResult<WaBroadcastDto>.Fail(400, "Give the broadcast a name (only your team sees it).");
        if (name.Length > 150) return WorkResult<WaBroadcastDto>.Fail(400, "The name can be at most 150 characters.");
        var (plan, problem) = await PlanAsync(request);
        if (plan == null) return WorkResult<WaBroadcastDto>.Fail(400, problem!);
        if (plan.Sending.Count == 0) return WorkResult<WaBroadcastDto>.Fail(400, "Nobody in this audience has agreed to WhatsApp messages. Record their opt-in in Contacts first.");
        if (request.ScheduledAt is { } at && at > _now().AddDays(60)) return WorkResult<WaBroadcastDto>.Fail(400, "Schedule it within the next 60 days.");

        var (spent, committed, settings) = await MoneyAsync();
        var over = settings.MonthlyBudget is > 0 && spent + committed + plan.Estimate > settings.MonthlyBudget;
        var now = _now();
        var b = new WaBroadcast
        {
            BroadcastId = Guid.NewGuid(),
            Name = name,
            TemplateId = plan.Template.TemplateId,
            Variables = JsonSerializer.Serialize(request.Variables),
            HeaderMediaUrl = string.IsNullOrWhiteSpace(request.HeaderMediaUrl) ? null : request.HeaderMediaUrl.Trim(),
            Audience = JsonSerializer.Serialize(request.Audience),
            // Over the budget: someone who may approve spending sends it by creating it; anyone else waits.
            Status = over && !actor.CanApprove ? "AwaitingApproval" : "Scheduled",
            ScheduledAt = request.ScheduledAt is { } when && when > now ? when : now,
            Recipients = plan.Sending.Count,
            EstimatedCost = plan.Estimate,
            Currency = settings.Currency,
            CreatedByUserId = actor.UserId,
            ApprovedByUserId = over && actor.CanApprove ? actor.UserId : null,
            ApprovedAt = over && actor.CanApprove ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.WaBroadcasts.Add(b);
        foreach (var c in plan.Sending)
            _db.WaBroadcastRecipients.Add(new WaBroadcastRecipient { BroadcastId = b.BroadcastId, ContactId = c.ContactId, Status = "Pending", EstimatedCost = plan.EachCost[c.ContactId] });
        await _db.SaveChangesAsync();
        _logger.LogInformation("WhatsApp broadcast {Name} created for {Count} people ({Status}).", b.Name, b.Recipients, b.Status);
        return await GetAsync(actor, b.BroadcastId);
    }

    // ── Changing ─────────────────────────────────────────────────────────────

    public async Task<WorkResult<WaBroadcastDto>> ApproveAsync(WaActor actor, Guid broadcastId)
    {
        if (!actor.CanApprove) return WorkResult<WaBroadcastDto>.Fail(403, "Only people who may approve WhatsApp spending can do this.");
        var b = await _db.WaBroadcasts.FirstOrDefaultAsync(x => x.BroadcastId == broadcastId);
        if (b == null) return WorkResult<WaBroadcastDto>.Fail(404, "This broadcast does not exist.");
        if (b.Status is not ("AwaitingApproval" or "Paused")) return WorkResult<WaBroadcastDto>.Fail(409, "This broadcast does not need approval.");
        b.ApprovedByUserId = actor.UserId;
        b.ApprovedAt = _now();
        b.Status = b.StartedAt == null ? "Scheduled" : "Sending";
        b.PausedReason = null;
        b.UpdatedAt = _now();
        await _db.SaveChangesAsync();
        return await GetAsync(actor, broadcastId);
    }

    public Task<WorkResult<WaBroadcastDto>> PauseAsync(WaActor actor, Guid broadcastId) =>
        ChangeAsync(actor, broadcastId, b => b.Status is "Scheduled" or "Sending", b => { b.Status = "Paused"; b.PausedReason = $"Paused by {actor.Name}"; }, "Only a scheduled or sending broadcast can be paused.");

    public async Task<WorkResult<WaBroadcastDto>> ResumeAsync(WaActor actor, Guid broadcastId)
    {
        var b = await _db.WaBroadcasts.AsNoTracking().FirstOrDefaultAsync(x => x.BroadcastId == broadcastId);
        if (b?.Status == "Paused" && b.PausedReason?.StartsWith("Budget") == true && b.ApprovedByUserId == null)
            return actor.CanApprove ? await ApproveAsync(actor, broadcastId)
                : WorkResult<WaBroadcastDto>.Fail(403, "It stopped at the monthly budget: someone who may approve WhatsApp spending must continue it.");
        return await ChangeAsync(actor, broadcastId, x => x.Status == "Paused",
            x => { x.Status = x.StartedAt == null ? "Scheduled" : "Sending"; x.PausedReason = null; }, "Only a paused broadcast can continue.");
    }

    public async Task<WorkResult<WaBroadcastDto>> CancelAsync(WaActor actor, Guid broadcastId)
    {
        var result = await ChangeAsync(actor, broadcastId, b => b.Status is "Scheduled" or "Sending" or "Paused" or "AwaitingApproval" or "Draft",
            b => { b.Status = "Cancelled"; b.FinishedAt = _now(); b.PausedReason = null; }, "This broadcast has already finished.");
        if (!result.Ok) return result;
        foreach (var r in await _db.WaBroadcastRecipients.Where(r => r.BroadcastId == broadcastId && r.Status == "Pending").ToListAsync())
        {
            r.Status = "Skipped";
            r.Error = $"Cancelled by {actor.Name}";
        }
        await _db.SaveChangesAsync();
        return await GetAsync(actor, broadcastId);
    }

    private async Task<WorkResult<WaBroadcastDto>> ChangeAsync(WaActor actor, Guid broadcastId, Func<WaBroadcast, bool> allowed, Action<WaBroadcast> change, string notAllowed)
    {
        if (!actor.CanBroadcast) return WorkResult<WaBroadcastDto>.Fail(403, "You cannot change broadcasts.");
        var b = await _db.WaBroadcasts.FirstOrDefaultAsync(x => x.BroadcastId == broadcastId);
        if (b == null) return WorkResult<WaBroadcastDto>.Fail(404, "This broadcast does not exist.");
        if (!allowed(b)) return WorkResult<WaBroadcastDto>.Fail(409, notAllowed);
        change(b);
        b.UpdatedAt = _now();
        await _db.SaveChangesAsync();
        return await GetAsync(actor, broadcastId);
    }

    // ── Sending (the worker) ─────────────────────────────────────────────────

    public async Task<int> SendDueAsync(int max, CancellationToken ct)
    {
        var now = _now();
        foreach (var due in await _db.WaBroadcasts.Where(b => b.Status == "Scheduled" && b.ScheduledAt <= now).ToListAsync(ct))
        {
            due.Status = "Sending";
            due.StartedAt ??= now;
            due.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);

        var sent = 0;
        foreach (var b in await _db.WaBroadcasts.Where(x => x.Status == "Sending").OrderBy(x => x.StartedAt).ToListAsync(ct))
        {
            if (sent >= max || ct.IsCancellationRequested) break;
            sent += await SendBatchAsync(b, max - sent, ct);
        }
        return sent;
    }

    private async Task Pause(WaBroadcast b, string reason, CancellationToken ct)
    {
        b.Status = "Paused";
        b.PausedReason = reason;
        b.UpdatedAt = _now();
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("WhatsApp broadcast {Name} paused: {Reason}", b.Name, reason);
    }

    private async Task<int> SendBatchAsync(WaBroadcast b, int max, CancellationToken ct)
    {
        if (!_meta.IsConfigured) { await Pause(b, "The WhatsApp marketing number is not connected on this environment.", ct); return 0; }
        var template = await _db.WaTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateId == b.TemplateId, ct);
        if (template == null || template.Status != "APPROVED")
        {
            await Pause(b, $"The template is {(template?.Status ?? "deleted").ToLowerInvariant()} at Meta.", ct);
            return 0;
        }
        var pending = await _db.WaBroadcastRecipients.Where(r => r.BroadcastId == b.BroadcastId && r.Status == "Pending").Take(max).ToListAsync(ct);
        if (pending.Count == 0)
        {
            b.Status = "Done";
            b.FinishedAt = _now();
            b.UpdatedAt = _now();
            await _db.SaveChangesAsync(ct);
            return 0;
        }

        // The budget: an approved broadcast may finish; any other stops before it would pass it.
        var settings = await WaShared.SettingsAsync(_db);
        if (b.ApprovedByUserId == null && settings.MonthlyBudget is > 0)
        {
            var monthStart = WaRules.MonthStart(_now());
            var spent = await _db.WaMessages.AsNoTracking().Where(m => m.Direction == "Out" && m.Cost > 0 && m.StatusAt >= monthStart).SumAsync(m => (decimal?)m.Cost, ct) ?? 0m;
            // Sent but not yet charged messages will be: count them too.
            var onTheWay = await (from m in _db.WaMessages.AsNoTracking()
                                  join r in _db.WaBroadcastRecipients.AsNoTracking() on (Guid?)m.MessageId equals r.MessageId
                                  where m.Status == "sent" && m.Billable != false && m.CreatedAt >= monthStart
                                  select (decimal?)r.EstimatedCost).SumAsync(ct) ?? 0m;
            var batch = pending.Sum(r => r.EstimatedCost);
            if (spent + onTheWay + batch > settings.MonthlyBudget)
            {
                await Pause(b, $"Budget reached: {WaRules.Money(spent + onTheWay, settings.Currency)} of {WaRules.Money(settings.MonthlyBudget.Value, settings.Currency)} this month.", ct);
                return 0;
            }
        }

        var parts = WaTemplateParts.Parse(template.Components, template.ParameterFormat);
        var how = JsonSerializer.Deserialize<Dictionary<string, WaVariableValue>>(b.Variables ?? "{}") ?? new();
        string? mediaId = null;
        if (parts.HasMediaHeader && b.HeaderMediaUrl == null)
        {
            mediaId = await HeaderMediaIdAsync(b, template, ct);
            if (mediaId == null) { await Pause(b, "The template's header picture could not be sent to WhatsApp. Edit the broadcast with a link to it.", ct); return 0; }
        }

        var contactIds = pending.Select(p => p.ContactId).ToList();
        var contacts = await _db.WaContacts.Where(c => contactIds.Contains(c.ContactId)).ToDictionaryAsync(c => c.ContactId, ct);
        var count = 0;
        foreach (var r in pending)
        {
            if (ct.IsCancellationRequested) break;
            if (!contacts.TryGetValue(r.ContactId, out var contact) || contact.OptIn != "OptedIn")
            {
                r.Status = "Skipped";
                r.Error = contact?.OptIn == "OptedOut" ? "Opted out before it was sent." : "No longer opted in.";
                continue;
            }
            var values = ValuesFor(contact, how);
            var row = new WaMessage
            {
                MessageId = Guid.NewGuid(), Type = "template", Body = parts.Render(values), TemplateName = template.Name,
                TemplateLanguage = template.Language, BroadcastId = b.BroadcastId, CreatedAt = _now(),
            };
            var payload = new JsonObject
            {
                ["type"] = "template",
                ["template"] = parts.SendPayload(template.Name, template.Language, values, mediaId, b.HeaderMediaUrl, null),
            };
            await WaSender.SendAsync(_db, _meta, contact, row, payload, ct);

            if (row.Status == "failed" && row.ErrorCode is { } code && TooFast.Contains(code))
            {
                // Meta asked us to slow down: forget this try and send it again on the next round.
                _db.WaMessages.Remove(row);
                await _db.SaveChangesAsync(ct);
                break;
            }
            if (row.Status == "failed" && row.ErrorCode is 132015 or 132016 or 132001 or 368)
            {
                r.Status = "Failed";
                r.Error = row.ErrorText;
                r.MessageId = row.MessageId;
                b.Failed += 1;
                await Pause(b, row.ErrorText ?? "Meta stopped this template.", ct);
                return count;
            }
            r.MessageId = row.MessageId;
            r.SentAt = row.CreatedAt;
            r.Status = row.Status == "failed" ? "Failed" : "Sent";
            r.Error = row.Status == "failed" ? row.ErrorText : null;
            if (row.Status == "failed") b.Failed += 1; else b.Sent += 1;
            b.UpdatedAt = _now();
            count++;
        }
        await _db.SaveChangesAsync(ct);
        return count;
    }

    /// <summary>The template's sample (kept when it was made) uploaded to Meta, once per broadcast.</summary>
    private async Task<string?> HeaderMediaIdAsync(WaBroadcast b, WaTemplate template, CancellationToken ct)
    {
        lock (HeaderMedia) { if (HeaderMedia.TryGetValue(b.BroadcastId, out var known)) return known; }
        if (template.HeaderMediaKey == null || !_files.IsConfigured) return null;
        try
        {
            await using var stream = await _files.OpenAsync(template.HeaderMediaKey);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            var parts = WaTemplateParts.Parse(template.Components, template.ParameterFormat);
            var mime = parts.HeaderFormat switch { "IMAGE" => template.HeaderMediaKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg", "VIDEO" => "video/mp4", _ => "application/pdf" };
            var id = await _meta.UploadMediaAsync(memory.ToArray(), mime, Path.GetFileName(template.HeaderMediaKey), ct);
            lock (HeaderMedia) { HeaderMedia[b.BroadcastId] = id; }
            return id;
        }
        catch (Exception ex) when (ex is WaMetaException or DocStorageException or FileNotFoundException)
        {
            _logger.LogWarning(ex, "WhatsApp broadcast {Name}: the header sample could not be uploaded.", b.Name);
            return null;
        }
    }
}
