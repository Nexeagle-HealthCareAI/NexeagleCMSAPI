using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// See IWaTemplateService. Meta keeps the truth about templates: the list here is a copy, read again when
/// someone asks and every 10 minutes while some wait for approval (Meta's approval news goes to the bot's
/// webhook, not ours). Templates made in WhatsApp Manager appear here too.
/// </summary>
public class WaTemplateService : IWaTemplateService
{
    private const int MaxSampleBytes = 16 * 1024 * 1024;
    private readonly CmsDbContext _db;
    private readonly IWaMeta _meta;
    private readonly IDocFileStore _files;
    private readonly ILogger<WaTemplateService> _logger;

    public WaTemplateService(CmsDbContext db, IWaMeta meta, IDocFileStore files, ILogger<WaTemplateService> logger)
    {
        _db = db;
        _meta = meta;
        _files = files;
        _logger = logger;
    }

    public static string SampleKey(Guid templateId, string fileName) => $"whatsapp/templates/{templateId}/{fileName}";

    public async Task<WorkResult<List<WaTemplateDto>>> ListAsync(WaActor actor, bool refresh)
    {
        string? problem = null;
        if (refresh && _meta.IsConfigured)
        {
            try { await SyncAsync(); }
            catch (WaMetaException ex) { problem = ex.Message; }
        }
        var list = await _db.WaTemplates.AsNoTracking().Where(t => t.Status != "DELETED").OrderBy(t => t.Name).ThenBy(t => t.Language).ToListAsync();
        return WorkResult<List<WaTemplateDto>>.Success(list.Select(Dto).ToList(), problem == null ? null : "Could not read the latest from Meta: " + problem);
    }

    public static WaTemplateDto Dto(WaTemplate t)
    {
        var parts = WaTemplateParts.Parse(t.Components, t.ParameterFormat);
        return new WaTemplateDto
        {
            TemplateId = t.TemplateId,
            Name = t.Name,
            Language = t.Language,
            Category = t.Category,
            Status = t.Status,
            RejectedReason = t.RejectedReason,
            QualityScore = t.QualityScore,
            ParameterFormat = parts.Named ? "NAMED" : "POSITIONAL",
            HeaderFormat = parts.HeaderFormat,
            HeaderText = parts.HeaderText,
            Body = parts.Body,
            Footer = parts.Footer,
            Buttons = parts.Buttons,
            HeaderVariables = parts.HeaderVariables,
            BodyVariables = parts.BodyVariables,
            UrlButtonVariable = parts.UrlButtonIndex >= 0,
            HasSampleMedia = t.HeaderMediaKey != null,
            UpdatedAt = t.UpdatedAt,
        };
    }

    public async Task<WorkResult<WaTemplateDto>> CreateAsync(WaActor actor, WaSaveTemplateRequest request, byte[]? sample, string? sampleMime, string? sampleName)
    {
        if (!actor.CanTemplates) return WorkResult<WaTemplateDto>.Fail(403, "You cannot write templates. Ask an administrator for \"Write WhatsApp templates\".");
        request.Name = (request.Name ?? string.Empty).Trim();
        request.Category = (request.Category ?? string.Empty).Trim().ToUpperInvariant();
        request.HeaderFormat = (request.HeaderFormat ?? "NONE").Trim().ToUpperInvariant();
        request.Buttons ??= new();
        foreach (var b in request.Buttons) b.Type = (b.Type ?? string.Empty).Trim().ToUpperInvariant();
        request.BodyExamples ??= new();
        if (WaTemplateParts.Check(request) is { } problem) return WorkResult<WaTemplateDto>.Fail(400, problem);

        var media = request.HeaderFormat is "IMAGE" or "VIDEO" or "DOCUMENT";
        if (media)
        {
            if (sample == null || sample.Length == 0) return WorkResult<WaTemplateDto>.Fail(400, "Add a sample picture, video or document for the header: Meta reviews it.");
            if (sample.Length > MaxSampleBytes) return WorkResult<WaTemplateDto>.Fail(413, "The sample can be at most 16 MB (pictures 5 MB).");
            var fits = request.HeaderFormat switch
            {
                "IMAGE" => sampleMime is "image/jpeg" or "image/png",
                "VIDEO" => sampleMime is "video/mp4" or "video/3gpp",
                _ => sampleMime == "application/pdf",
            };
            if (!fits) return WorkResult<WaTemplateDto>.Fail(415, request.HeaderFormat switch
            {
                "IMAGE" => "The header picture must be a JPG or PNG.",
                "VIDEO" => "The header video must be an MP4.",
                _ => "The header document must be a PDF.",
            });
        }
        if (await _db.WaTemplates.AnyAsync(t => t.Name == request.Name && t.Language == request.Language && t.Status != "DELETED"))
            return WorkResult<WaTemplateDto>.Fail(409, "A template with this name and language already exists. Choose another name.");
        if (!_meta.IsConfigured) return WorkResult<WaTemplateDto>.Fail(503, "The WhatsApp marketing number is not connected on this environment yet.");

        var now = DateTime.UtcNow;
        var template = new WaTemplate
        {
            TemplateId = Guid.NewGuid(), Name = request.Name, Language = request.Language.Trim(), Category = request.Category,
            CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now,
        };
        JsonObject payload;
        try
        {
            string? handle = null;
            if (media)
            {
                var name = string.IsNullOrWhiteSpace(sampleName) ? "sample" : Path.GetFileName(sampleName);
                handle = await _meta.UploadSampleAsync(sample!, sampleMime!, name);
                // Keep the sample so broadcasts can send it as the header without asking for a link.
                if (_files.IsConfigured)
                {
                    try
                    {
                        var key = SampleKey(template.TemplateId, name);
                        using var stream = new MemoryStream(sample!);
                        await _files.SaveAsync(key, stream, sampleMime!);
                        template.HeaderMediaKey = key;
                    }
                    catch (DocStorageException ex) { _logger.LogWarning(ex, "The template sample could not be kept; sending will need a link."); }
                }
            }
            payload = WaTemplateParts.CreatePayload(request, handle);
            var created = await _meta.CreateTemplateAsync(payload);
            template.MetaTemplateId = created.Id;
            template.Status = string.IsNullOrWhiteSpace(created.Status) ? "PENDING" : created.Status.ToUpperInvariant();
            template.Category = string.IsNullOrWhiteSpace(created.Category) ? request.Category : created.Category.ToUpperInvariant();
        }
        catch (WaMetaException ex)
        {
            return WorkResult<WaTemplateDto>.Fail(ex.HttpStatus, "Meta did not accept the template: " + ex.Message);
        }
        template.Components = payload["components"]!.ToJsonString();
        template.ParameterFormat = payload["parameter_format"].Str() ?? "POSITIONAL";
        template.SyncedAt = now;
        _db.WaTemplates.Add(template);
        await _db.SaveChangesAsync();
        _logger.LogInformation("WhatsApp template {Name} sent to Meta ({Status}).", template.Name, template.Status);
        return WorkResult<WaTemplateDto>.Success(Dto(template));
    }

    public async Task<WorkResult<bool>> DeleteAsync(WaActor actor, Guid templateId)
    {
        if (!actor.CanTemplates) return WorkResult<bool>.Fail(403, "You cannot delete templates.");
        var template = await _db.WaTemplates.FirstOrDefaultAsync(t => t.TemplateId == templateId);
        if (template == null) return WorkResult<bool>.Fail(404, "This template does not exist.");
        if (await _db.WaBroadcasts.AnyAsync(b => b.TemplateId == templateId && (b.Status == "Scheduled" || b.Status == "Sending" || b.Status == "Paused" || b.Status == "AwaitingApproval")))
            return WorkResult<bool>.Fail(409, "A broadcast still uses this template. Cancel it first.");
        if (template.MetaTemplateId != null && _meta.IsConfigured)
        {
            try { await _meta.DeleteTemplateAsync(template.Name, template.MetaTemplateId); }
            catch (WaMetaException ex) when (ex.HttpStatus != 400 || !ex.Message.Contains("not", StringComparison.OrdinalIgnoreCase))
            {
                return WorkResult<bool>.Fail(ex.HttpStatus, "Meta did not delete the template: " + ex.Message);
            }
        }
        // Broadcasts keep pointing at it, so the row stays, marked deleted.
        template.Status = "DELETED";
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true);
    }

    public async Task<int> SyncAsync()
    {
        if (!_meta.IsConfigured) return 0;
        var remote = await _meta.ListTemplatesAsync();
        var local = await _db.WaTemplates.ToListAsync();
        var now = DateTime.UtcNow;
        var changed = 0;
        foreach (var r in remote)
        {
            var t = local.FirstOrDefault(x => x.MetaTemplateId != null && x.MetaTemplateId == r.Id)
                    ?? local.FirstOrDefault(x => x.Name == r.Name && x.Language == r.Language);
            if (t == null)
            {
                t = new WaTemplate { TemplateId = Guid.NewGuid(), Name = r.Name, Language = r.Language, CreatedAt = now };
                _db.WaTemplates.Add(t);
                local.Add(t);
            }
            var status = r.Status.ToUpperInvariant();
            if (t.Status != status || t.RejectedReason != r.RejectedReason || t.QualityScore != r.QualityScore || t.Category != r.Category.ToUpperInvariant()) changed++;
            t.MetaTemplateId = r.Id ?? t.MetaTemplateId;
            t.Status = status;
            t.Category = r.Category.ToUpperInvariant();
            t.RejectedReason = r.RejectedReason;
            t.QualityScore = r.QualityScore;
            t.Components = r.Components;
            t.ParameterFormat = string.IsNullOrWhiteSpace(r.ParameterFormat) ? "POSITIONAL" : r.ParameterFormat.ToUpperInvariant();
            t.SyncedAt = now;
            t.UpdatedAt = now;
        }
        // Deleted in WhatsApp Manager: no longer on Meta's list.
        foreach (var t in local.Where(t => t.MetaTemplateId != null && t.Status != "DELETED" && !remote.Any(r => r.Id == t.MetaTemplateId || (r.Name == t.Name && r.Language == t.Language))))
        {
            t.Status = "DELETED";
            t.UpdatedAt = now;
            changed++;
        }
        await _db.SaveChangesAsync();
        return changed;
    }
}
