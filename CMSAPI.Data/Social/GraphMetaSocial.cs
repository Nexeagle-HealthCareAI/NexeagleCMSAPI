using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using CMSAPI.Data.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Social;

/// <summary>
/// CMS → Social on Meta's Graph API (graph.facebook.com, version Social__GraphVersion, default v23.0).
/// Page and Instagram calls use the Page access token, read from the account's system-user token and kept
/// in memory for 50 minutes; ad and discovery calls use the system-user token itself.
/// </summary>
public class GraphMetaSocial : IMetaSocial
{
    private readonly HttpClient _http;
    private readonly ILogger<GraphMetaSocial> _logger;
    private readonly string _version;
    private readonly string _base;

    private static readonly ConcurrentDictionary<string, (string Token, DateTime At)> PageTokens = new();

    // Insight metrics asked for, per kind of post. Meta keeps changing these (June 2026 removed many Page
    // metrics), so a metric Meta refuses is dropped for that call and the rest are still read.
    private static readonly string[] FacebookMetrics = { "post_media_view", "post_total_media_view_unique", "post_clicks" };
    private static readonly string[] InstagramFeedMetrics = { "views", "reach", "saved", "shares", "total_interactions" };
    private static readonly string[] InstagramReelMetrics = { "views", "reach", "saved", "shares", "total_interactions" };
    private static readonly string[] InstagramStoryMetrics = { "views", "reach", "replies", "shares" };

    public GraphMetaSocial(HttpClient http, IConfiguration config, ILogger<GraphMetaSocial> logger)
    {
        _http = http;
        _logger = logger;
        _version = string.IsNullOrWhiteSpace(config["Social:GraphVersion"]) ? "v23.0" : config["Social:GraphVersion"]!.Trim();
        _base = $"https://graph.facebook.com/{_version}/";
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private HttpRequestMessage Request(HttpMethod method, string path, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path.StartsWith("https://") ? path : _base + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
    private static FormUrlEncodedContent Form(params (string, string)[] fields) => new(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));
    private static string Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    private static string E(string s) => Uri.EscapeDataString(s);

    private async Task<JsonNode> CallAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, ct); }
        catch (HttpRequestException) { throw new MetaSocialException("Meta cannot be reached right now. Try again in a minute.", httpStatus: 503); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new MetaSocialException("Meta did not answer in time. Try again in a minute.", httpStatus: 504); }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? body = null;
            try { body = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text); } catch { }
            if (response.IsSuccessStatusCode && body != null && body["error"] == null) return body;

            var error = body?["error"];
            var code = error?["code"].Int();
            var sub = error?["error_subcode"].Int();
            var message = error?["error_user_msg"].Str() ?? error?["message"].Str() ?? $"Meta answered {(int)response.StatusCode}.";
            if (code == 190) message = "The Meta token is not valid any more. An administrator needs to enter a new one in Social → Setup.";
            else if (code is 4 or 17 or 32 or 613) message = "Meta's limit on calls was reached. The CMS tries again later.";
            else if (code is 10 or 200 && !message.Contains("permission", StringComparison.OrdinalIgnoreCase)) message = "Meta refused: the token lacks a permission for this. " + message;
            throw new MetaSocialException(message, code, sub, response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? 401 : code == 190 ? 401 : 502);
        }
    }

    /// <summary>Follows Meta's "next" links until `max` items or `stop` says enough.</summary>
    private async Task<List<JsonNode>> PagesAsync(string path, string token, int max, Func<JsonNode, bool>? stop, CancellationToken ct)
    {
        var items = new List<JsonNode>();
        string? next = path;
        while (next != null && items.Count < max)
        {
            var page = await CallAsync(Request(HttpMethod.Get, next, token), ct);
            foreach (var item in page["data"]?.AsArray() ?? new JsonArray())
            {
                if (item == null) continue;
                if (stop != null && stop(item)) return items;
                items.Add(item);
                if (items.Count >= max) break;
            }
            next = page["paging"]?["next"].Str();
        }
        return items;
    }

    private static void Need(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new MetaSocialException($"Choose the {what} in Social → Setup first.", httpStatus: 400);
    }

    /// <summary>The Page access token for this Page, from the system-user token.</summary>
    private async Task<string> PageTokenAsync(SocialCreds c, CancellationToken ct)
    {
        Need(c.PageId, "Facebook Page");
        var key = c.PageId + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.Token)))[..16];
        if (PageTokens.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(50)) return hit.Token;
        var answer = await CallAsync(Request(HttpMethod.Get, $"{c.PageId}?fields=access_token", c.Token), ct);
        var token = answer["access_token"].Str()
            ?? throw new MetaSocialException("The token cannot act for this Facebook Page. In Business settings, assign the Page to the system user with full control.", httpStatus: 400);
        PageTokens[key] = (token, DateTime.UtcNow);
        return token;
    }

    private static DateTime When(JsonNode? node) =>
        DateTime.TryParse(node.Str(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.UtcNow;

    // ── setup ────────────────────────────────────────────────────────────────

    public async Task<MetaAssets> DiscoverAsync(string token, CancellationToken ct = default)
    {
        var assets = new MetaAssets();
        var me = await CallAsync(Request(HttpMethod.Get, "me?fields=id,name", token), ct);
        assets.UserName = me["name"].Str();
        foreach (var p in await PagesAsync("me/accounts?fields=id,name,instagram_business_account{id,username}&limit=100", token, 200, null, ct))
            assets.Pages.Add(new MetaPage
            {
                Id = p["id"].Str() ?? "", Name = p["name"].Str() ?? "",
                InstagramId = p["instagram_business_account"]?["id"].Str(), InstagramUsername = p["instagram_business_account"]?["username"].Str(),
            });
        try
        {
            foreach (var a in await PagesAsync("me/adaccounts?fields=id,name,currency,account_status&limit=100", token, 200, null, ct))
                assets.AdAccounts.Add(new MetaAdAccount { Id = a["id"].Str() ?? "", Name = a["name"].Str() ?? "", Currency = a["currency"].Str(), Status = a["account_status"].Int() });
        }
        catch (MetaSocialException ex) { _logger.LogInformation("Social: ad accounts not readable with this token: {Message}", ex.Message); }
        try
        {
            foreach (var p in await PagesAsync("me/permissions?limit=200", token, 300, null, ct))
                if (p["status"].Str() == "granted" && p["permission"].Str() is { } name) assets.Permissions.Add(name);
        }
        catch (MetaSocialException) { }
        return assets;
    }

    private static string AppToken(string appId, string appSecret) => appId + "|" + appSecret;

    public async Task<List<MetaAppSubscription>> AppSubscriptionsAsync(string appId, string appSecret, CancellationToken ct = default)
    {
        var answer = await CallAsync(Request(HttpMethod.Get, $"{appId}/subscriptions", AppToken(appId, appSecret)), ct);
        return (answer["data"]?.AsArray() ?? new JsonArray()).Where(x => x != null).Select(x => new MetaAppSubscription
        {
            Object = x!["object"].Str() ?? "",
            CallbackUrl = x["callback_url"].Str(),
            Active = x["active"].Bool() ?? false,
            Fields = (x["fields"]?.AsArray() ?? new JsonArray()).Select(f => f?["name"].Str() ?? f.Str() ?? "").Where(f => f != "").ToList(),
        }).ToList();
    }

    public async Task SubscribeAppAsync(string appId, string appSecret, string obj, string callbackUrl, string fields, string verifyToken, CancellationToken ct = default)
    {
        // Meta calls the callback once with the verify token before it answers.
        await CallAsync(Request(HttpMethod.Post, $"{appId}/subscriptions", AppToken(appId, appSecret),
            Form(("object", obj), ("callback_url", callbackUrl), ("fields", fields), ("verify_token", verifyToken), ("include_values", "true"))), ct);
    }

    public async Task SubscribePageAsync(SocialCreds c, string fields, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/subscribed_apps", token, Form(("subscribed_fields", fields))), ct);
    }

    public async Task<List<string>> PageSubscribedFieldsAsync(SocialCreds c, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Get, $"{c.PageId}/subscribed_apps", token), ct);
        var fields = new List<string>();
        foreach (var app in answer["data"]?.AsArray() ?? new JsonArray())
        {
            if (c.AppId != null && app?["id"].Str() != c.AppId) continue;
            foreach (var f in app?["subscribed_fields"]?.AsArray() ?? new JsonArray())
                if (f.Str() is { } name) fields.Add(name);
        }
        return fields;
    }

    // ── posts ────────────────────────────────────────────────────────────────

    public async Task<List<MetaPostItem>> FacebookPostsAsync(SocialCreds c, DateTime? since, int max, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var path = $"{c.PageId}/published_posts?fields=id,message,created_time,permalink_url,full_picture,status_type,attachments{{media_type,type}},shares,reactions.summary(total_count).limit(0),comments.summary(total_count).limit(0)&limit=50"
                   + (since is { } s ? "&since=" + Unix(s) : "");
        var list = new List<MetaPostItem>();
        foreach (var p in await PagesAsync(path, token, max, null, ct))
        {
            var media = p["attachments"]?["data"]?[0]?["media_type"].Str();
            list.Add(new MetaPostItem
            {
                Id = p["id"].Str() ?? "",
                MediaType = "POST",
                Type = media switch { "photo" => "Photo", "video" => "Video", "album" => "Carousel", "link" or "share" => "Link", null => "Text", _ => "Photo" },
                Caption = p["message"].Str(),
                ThumbnailUrl = p["full_picture"].Str(),
                Permalink = p["permalink_url"].Str(),
                PublishedAt = When(p["created_time"]),
                Likes = p["reactions"]?["summary"]?["total_count"].Int(),
                Comments = p["comments"]?["summary"]?["total_count"].Int(),
                Shares = p["shares"]?["count"].Int() ?? 0,
            });
        }
        return list;
    }

    public async Task<List<MetaPostItem>> InstagramMediaAsync(SocialCreds c, DateTime? since, int max, CancellationToken ct = default)
    {
        Need(c.IgUserId, "Instagram account");
        var token = await PageTokenAsync(c, ct);
        const string fields = "id,caption,media_type,media_product_type,media_url,thumbnail_url,permalink,timestamp,like_count,comments_count";
        MetaPostItem Map(JsonNode m)
        {
            var product = m["media_product_type"].Str() ?? "FEED";
            var type = m["media_type"].Str();
            return new MetaPostItem
            {
                Id = m["id"].Str() ?? "",
                MediaType = product,
                Type = product == "REELS" ? "Reel" : product == "STORY" ? "Story" : type == "CAROUSEL_ALBUM" ? "Carousel" : type == "VIDEO" ? "Video" : "Photo",
                Caption = m["caption"].Str(),
                ThumbnailUrl = m["thumbnail_url"].Str() ?? m["media_url"].Str(),
                Permalink = m["permalink"].Str(),
                PublishedAt = When(m["timestamp"]),
                Likes = m["like_count"].Int(),
                Comments = m["comments_count"].Int(),
            };
        }
        var list = (await PagesAsync($"{c.IgUserId}/media?fields={fields}&limit=50", token, max, m => since != null && When(m["timestamp"]) < since, ct)).Select(Map).ToList();
        // Stories live 24 hours and are not in the media list.
        try { list.AddRange((await PagesAsync($"{c.IgUserId}/stories?fields={fields}", token, 100, null, ct)).Select(Map)); }
        catch (MetaSocialException ex) { _logger.LogInformation("Social: Instagram stories not readable: {Message}", ex.Message); }
        return list;
    }

    public async Task<Dictionary<string, long>> InsightsAsync(SocialCreds c, string channel, string metaId, string mediaType, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var metrics = (channel == "facebook" ? FacebookMetrics : mediaType == "STORY" ? InstagramStoryMetrics : mediaType == "REELS" ? InstagramReelMetrics : InstagramFeedMetrics).ToList();
        var result = new Dictionary<string, long>();
        for (var attempt = 0; attempt < 6 && metrics.Count > 0; attempt++)
        {
            try
            {
                var answer = await CallAsync(Request(HttpMethod.Get, $"{metaId}/insights?metric={string.Join(',', metrics)}", token), ct);
                foreach (var m in answer["data"]?.AsArray() ?? new JsonArray())
                {
                    var name = m?["name"].Str();
                    var value = m?["values"]?[0]?["value"] ?? m?["total_value"]?["value"];
                    if (name != null && value.Long() is { } v) result[name] = v;
                }
                return result;
            }
            catch (MetaSocialException ex) when (ex.Code == 100)
            {
                // Drop the metric Meta named, or, if it named none, give up on the oldest-looking one.
                var bad = metrics.FirstOrDefault(m => ex.Message.Contains(m, StringComparison.Ordinal)) ?? metrics[^1];
                metrics.Remove(bad);
            }
        }
        return result;
    }

    public async Task<(int? Facebook, int? Instagram)> FollowersAsync(SocialCreds c, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        int? fb = null, ig = null;
        try { fb = (await CallAsync(Request(HttpMethod.Get, $"{c.PageId}?fields=followers_count", token), ct))["followers_count"].Int(); } catch (MetaSocialException) { }
        if (!string.IsNullOrEmpty(c.IgUserId))
            try { ig = (await CallAsync(Request(HttpMethod.Get, $"{c.IgUserId}?fields=followers_count", token), ct))["followers_count"].Int(); } catch (MetaSocialException) { }
        return (fb, ig);
    }

    public async Task<string?> PermalinkAsync(SocialCreds c, string channel, string metaId, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Get, $"{metaId}?fields={(channel == "facebook" ? "permalink_url" : "permalink")}", token), ct);
        return answer["permalink_url"].Str() ?? answer["permalink"].Str();
    }

    // ── comments ─────────────────────────────────────────────────────────────

    public async Task<List<MetaCommentItem>> CommentsAsync(SocialCreds c, string channel, string postMetaId, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var list = new List<MetaCommentItem>();
        if (channel == "facebook")
        {
            foreach (var x in await PagesAsync($"{postMetaId}/comments?fields=id,from{{id,name}},message,created_time,is_hidden,parent{{id}}&filter=stream&limit=100", token, 500, null, ct))
                list.Add(new MetaCommentItem
                {
                    Id = x["id"].Str() ?? "", ParentId = x["parent"]?["id"].Str(), AuthorId = x["from"]?["id"].Str(), AuthorName = x["from"]?["name"].Str(),
                    Text = x["message"].Str(), Hidden = x["is_hidden"].Bool() ?? false, CreatedAt = When(x["created_time"]),
                });
        }
        else
        {
            foreach (var x in await PagesAsync($"{postMetaId}/comments?fields=id,text,username,timestamp,hidden,from{{id,username}},replies{{id,text,username,timestamp,hidden,from{{id,username}}}}&limit=50", token, 500, null, ct))
            {
                MetaCommentItem Map(JsonNode n, string? parent) => new()
                {
                    Id = n["id"].Str() ?? "", ParentId = parent, AuthorId = n["from"]?["id"].Str(), AuthorName = n["username"].Str() ?? n["from"]?["username"].Str(),
                    Text = n["text"].Str(), Hidden = n["hidden"].Bool() ?? false, CreatedAt = When(n["timestamp"]),
                };
                list.Add(Map(x, null));
                foreach (var r in x["replies"]?["data"]?.AsArray() ?? new JsonArray())
                    if (r != null) list.Add(Map(r, x["id"].Str()));
            }
        }
        return list;
    }

    public async Task<string> ReplyToCommentAsync(SocialCreds c, string channel, string commentMetaId, string text, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Post, channel == "facebook" ? $"{commentMetaId}/comments" : $"{commentMetaId}/replies", token, Form(("message", text))), ct);
        return answer["id"].Str() ?? throw new MetaSocialException("Meta did not say the reply went out.");
    }

    public async Task HideCommentAsync(SocialCreds c, string channel, string commentMetaId, bool hide, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        await CallAsync(Request(HttpMethod.Post, commentMetaId, token, Form(channel == "facebook" ? ("is_hidden", hide ? "true" : "false") : ("hide", hide ? "true" : "false"))), ct);
    }

    public async Task<string> PrivateReplyAsync(SocialCreds c, string commentMetaId, string text, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var body = new JsonObject { ["recipient"] = new JsonObject { ["comment_id"] = commentMetaId }, ["message"] = new JsonObject { ["text"] = text } };
        var answer = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/messages", token, Json(body)), ct);
        return answer["message_id"].Str() ?? "";
    }

    // ── direct messages ──────────────────────────────────────────────────────

    private static JsonObject Envelope(string recipientId, JsonObject message, bool humanAgent)
    {
        var body = new JsonObject { ["recipient"] = new JsonObject { ["id"] = recipientId }, ["message"] = message };
        if (humanAgent) { body["messaging_type"] = "MESSAGE_TAG"; body["tag"] = "HUMAN_AGENT"; }
        else body["messaging_type"] = "RESPONSE";
        return body;
    }

    public async Task<string> SendMessageAsync(SocialCreds c, string channel, string recipientId, string text, bool humanAgent, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/messages", token, Json(Envelope(recipientId, new JsonObject { ["text"] = text }, humanAgent))), ct);
        return answer["message_id"].Str() ?? throw new MetaSocialException("Meta did not say the message went out.");
    }

    public async Task<string> SendAttachmentAsync(SocialCreds c, string channel, string recipientId, byte[] data, string mime, string fileName, bool humanAgent, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var type = mime.StartsWith("image/") ? "image" : mime.StartsWith("video/") ? "video" : mime.StartsWith("audio/") ? "audio" : "file";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(new JsonObject { ["attachment"] = new JsonObject { ["type"] = type, ["payload"] = new JsonObject { ["is_reusable"] = false } } }.ToJsonString()), "message");
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "filedata", fileName);
        if (channel == "instagram") form.Add(new StringContent("instagram"), "platform");
        var uploaded = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/message_attachments", token, form), ct);
        var attachmentId = uploaded["attachment_id"].Str() ?? throw new MetaSocialException("Meta did not take the file.");
        var message = new JsonObject { ["attachment"] = new JsonObject { ["type"] = type, ["payload"] = new JsonObject { ["attachment_id"] = attachmentId } } };
        var answer = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/messages", token, Json(Envelope(recipientId, message, humanAgent))), ct);
        return answer["message_id"].Str() ?? "";
    }

    public async Task<MetaPerson> PersonAsync(SocialCreds c, string channel, string externalId, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Get, channel == "instagram" ? $"{externalId}?fields=name,username" : $"{externalId}?fields=first_name,last_name,name", token), ct);
        var name = answer["name"].Str() ?? string.Join(' ', new[] { answer["first_name"].Str(), answer["last_name"].Str() }.Where(x => !string.IsNullOrEmpty(x)));
        return new MetaPerson { Name = string.IsNullOrWhiteSpace(name) ? null : name, Username = answer["username"].Str() };
    }

    // ── leads ────────────────────────────────────────────────────────────────

    private const string LeadFields = "id,created_time,field_data,ad_id,ad_name,campaign_id,campaign_name,form_id,platform,is_organic";

    private static MetaLeadItem Lead(JsonNode x)
    {
        var lead = new MetaLeadItem
        {
            Id = x["id"].Str() ?? "", CreatedAt = When(x["created_time"]), FormId = x["form_id"].Str(), AdId = x["ad_id"].Str(), AdName = x["ad_name"].Str(),
            CampaignId = x["campaign_id"].Str(), CampaignName = x["campaign_name"].Str(), Platform = x["platform"].Str(), IsOrganic = x["is_organic"].Bool(),
        };
        foreach (var f in x["field_data"]?.AsArray() ?? new JsonArray())
        {
            var name = f?["name"].Str();
            var values = (f?["values"]?.AsArray() ?? new JsonArray()).Select(v => v.Str()).Where(v => !string.IsNullOrEmpty(v));
            if (name != null) lead.Answers.Add(new(name, string.Join(", ", values)));
        }
        return lead;
    }

    public async Task<MetaLeadItem> LeadAsync(SocialCreds c, string leadgenId, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        return Lead(await CallAsync(Request(HttpMethod.Get, $"{leadgenId}?fields={LeadFields}", token), ct));
    }

    public async Task<List<MetaLeadForm>> LeadFormsAsync(SocialCreds c, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        return (await PagesAsync($"{c.PageId}/leadgen_forms?fields=id,name,status&limit=100", token, 500, null, ct))
            .Select(f => new MetaLeadForm { Id = f["id"].Str() ?? "", Name = f["name"].Str() ?? "", Status = f["status"].Str() }).ToList();
    }

    public async Task<List<MetaLeadItem>> FormLeadsAsync(SocialCreds c, string formId, DateTime since, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var filter = E($"[{{\"field\":\"time_created\",\"operator\":\"GREATER_THAN\",\"value\":{Unix(since)}}}]");
        return (await PagesAsync($"{formId}/leads?fields={LeadFields}&filtering={filter}&limit=500", token, 5000, null, ct)).Select(Lead).ToList();
    }

    // ── ads ──────────────────────────────────────────────────────────────────

    public async Task<List<MetaAdRow>> AdInsightsAsync(SocialCreds c, string level, DateTime fromDay, DateTime toDay, CancellationToken ct = default)
    {
        Need(c.AdAccountId, "ad account");
        var fields = level switch
        {
            "adset" => "adset_id,adset_name,campaign_name",
            "ad" => "ad_id,ad_name,adset_name,campaign_name",
            _ => "campaign_id,campaign_name",
        } + ",spend,impressions,clicks,actions";
        var range = E($"{{\"since\":\"{fromDay:yyyy-MM-dd}\",\"until\":\"{toDay:yyyy-MM-dd}\"}}");
        var rows = new List<MetaAdRow>();
        foreach (var r in await PagesAsync($"{c.AdAccountId}/insights?level={level}&fields={fields}&time_increment=1&time_range={range}&limit=500", c.Token, 20000, null, ct))
        {
            int Action(params string[] types)
            {
                foreach (var t in types)
                    foreach (var a in r["actions"]?.AsArray() ?? new JsonArray())
                        if (a?["action_type"].Str() == t && decimal.TryParse(a["value"].Str(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return (int)v;
                return 0;
            }
            rows.Add(new MetaAdRow
            {
                Day = DateTime.ParseExact(r["date_start"].Str() ?? $"{fromDay:yyyy-MM-dd}", "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Id = r[$"{level}_id"].Str() ?? "",
                Name = r[$"{level}_name"].Str() ?? "",
                ParentName = level switch { "adset" => r["campaign_name"].Str(), "ad" => $"{r["campaign_name"].Str()} · {r["adset_name"].Str()}", _ => null },
                Spend = decimal.TryParse(r["spend"].Str(), NumberStyles.Number, CultureInfo.InvariantCulture, out var spend) ? spend : 0,
                Impressions = r["impressions"].Int() ?? 0,
                Clicks = r["clicks"].Int() ?? 0,
                // "lead" counts every lead once; the grouped kind is used only when Meta gives no "lead".
                Leads = Action("lead") is var l and > 0 ? l : Action("onsite_conversion.lead_grouped"),
                Conversations = Action("onsite_conversion.messaging_conversation_started_7d"),
            });
        }
        return rows;
    }

    public async Task<Dictionary<string, string>> AdStatusesAsync(SocialCreds c, string level, CancellationToken ct = default)
    {
        Need(c.AdAccountId, "ad account");
        var edge = level switch { "adset" => "adsets", "ad" => "ads", _ => "campaigns" };
        return (await PagesAsync($"{c.AdAccountId}/{edge}?fields=id,effective_status&limit=500", c.Token, 5000, null, ct))
            .Where(x => x["id"].Str() != null).GroupBy(x => x["id"].Str()!).ToDictionary(g => g.Key, g => g.First()["effective_status"].Str() ?? "");
    }

    // ── publishing ───────────────────────────────────────────────────────────

    private static MultipartFormDataContent Upload(MediaFile file, params (string, string)[] fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        var content = new ByteArrayContent(file.Data);
        content.Headers.ContentType = new MediaTypeHeaderValue(file.Mime);
        form.Add(content, "source", file.Name);
        return form;
    }

    public async Task<string> PublishFacebookAsync(SocialCreds c, FacebookPublish post, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var message = post.Message ?? string.Empty;
        switch (post.Kind)
        {
            case "Text":
            case "Link":
            {
                var fields = new List<(string, string)> { ("message", message) };
                if (!string.IsNullOrWhiteSpace(post.Link)) fields.Add(("link", post.Link!));
                var answer = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/feed", token, Form(fields.ToArray())), ct);
                return answer["id"].Str() ?? throw new MetaSocialException("Meta did not return the post.");
            }
            case "Video":
            case "Reel":
            {
                var video = post.Media.FirstOrDefault() ?? throw new MetaSocialException("Add the video.", httpStatus: 400);
                using var form = Upload(video, ("description", message));
                var answer = await CallAsync(Request(HttpMethod.Post, $"https://graph-video.facebook.com/{_version}/{c.PageId}/videos", token, form), ct);
                var id = answer["id"].Str() ?? throw new MetaSocialException("Meta did not return the video.");
                return $"{c.PageId}_{id}";
            }
            case "Photo" when post.Media.Count == 1:
            {
                using var form = Upload(post.Media[0], ("caption", message), ("published", "true"));
                var answer = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/photos", token, form), ct);
                return answer["post_id"].Str() ?? answer["id"].Str() ?? throw new MetaSocialException("Meta did not return the post.");
            }
            default:
            {
                // Several pictures: upload each unpublished, then one post with them all.
                var ids = new JsonArray();
                foreach (var file in post.Media)
                {
                    using var form = Upload(file, ("published", "false"));
                    var answer = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/photos", token, form), ct);
                    ids.Add(new JsonObject { ["media_fbid"] = answer["id"].Str() });
                }
                var body = new JsonObject { ["message"] = message, ["attached_media"] = ids };
                var post2 = await CallAsync(Request(HttpMethod.Post, $"{c.PageId}/feed", token, Json(body)), ct);
                return post2["id"].Str() ?? throw new MetaSocialException("Meta did not return the post.");
            }
        }
    }

    public async Task<string> CreateInstagramContainerAsync(SocialCreds c, InstagramContainer x, CancellationToken ct = default)
    {
        Need(c.IgUserId, "Instagram account");
        var token = await PageTokenAsync(c, ct);
        var fields = new List<(string, string)>();
        if (x.MediaType == "CAROUSEL")
        {
            fields.Add(("media_type", "CAROUSEL"));
            fields.Add(("children", string.Join(',', x.Children)));
        }
        else
        {
            if (x.MediaType is "REELS" or "STORIES" or "VIDEO") fields.Add(("media_type", x.MediaType));
            if (x.ImageUrl != null) fields.Add(("image_url", x.ImageUrl));
            if (x.VideoUrl != null) fields.Add(("video_url", x.VideoUrl));
            if (x.IsCarouselItem) fields.Add(("is_carousel_item", "true"));
        }
        if (!x.IsCarouselItem && x.MediaType != "STORIES" && !string.IsNullOrEmpty(x.Caption)) fields.Add(("caption", x.Caption!));
        var answer = await CallAsync(Request(HttpMethod.Post, $"{c.IgUserId}/media", token, Form(fields.ToArray())), ct);
        return answer["id"].Str() ?? throw new MetaSocialException("Meta did not return the Instagram container.");
    }

    public async Task<string> InstagramContainerStatusAsync(SocialCreds c, string containerId, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Get, $"{containerId}?fields=status_code", token), ct);
        return answer["status_code"].Str() ?? "IN_PROGRESS";
    }

    public async Task<string> PublishInstagramAsync(SocialCreds c, string containerId, CancellationToken ct = default)
    {
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Post, $"{c.IgUserId}/media_publish", token, Form(("creation_id", containerId))), ct);
        return answer["id"].Str() ?? throw new MetaSocialException("Meta did not return the Instagram post.");
    }

    public async Task<(int Used, int Total)> InstagramPublishingLimitAsync(SocialCreds c, CancellationToken ct = default)
    {
        Need(c.IgUserId, "Instagram account");
        var token = await PageTokenAsync(c, ct);
        var answer = await CallAsync(Request(HttpMethod.Get, $"{c.IgUserId}/content_publishing_limit?fields=quota_usage,config", token), ct);
        var data = answer["data"]?[0];
        return (data?["quota_usage"].Int() ?? 0, data?["config"]?["quota_total"].Int() ?? 50);
    }
}
