#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Controllers;
using CMSAPI.Data;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// WhatsApp marketing: the rules, the webhook (messages in, delivery and cost), the 24-hour window,
// opt-in and opt-out, broadcasts with the budget, and the webhook's signature check. Meta is faked.
public class WhatsAppMarketingTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private sealed class FakeMeta : IWaMeta
    {
        public bool IsConfigured { get; set; } = true;
        public List<JsonObject> Sent { get; } = new();
        public int? FailWith { get; set; }
        private int _n;

        public Task<string> SendAsync(JsonObject message, CancellationToken ct = default)
        {
            if (FailWith is { } code) throw new WaMetaException("Meta said no", code, 400);
            Sent.Add(message);
            return Task.FromResult($"wamid.out{++_n}");
        }
        public Task<string> UploadMediaAsync(byte[] data, string mime, string fileName, CancellationToken ct = default) => Task.FromResult("media1");
        public Task<WaMediaFile> DownloadMediaAsync(string mediaId, CancellationToken ct = default) => Task.FromResult(new WaMediaFile());
        public Task<string> UploadSampleAsync(byte[] data, string mime, string fileName, CancellationToken ct = default) => Task.FromResult("handle1");
        public Task<WaMetaTemplate> CreateTemplateAsync(JsonObject template, CancellationToken ct = default) =>
            Task.FromResult(new WaMetaTemplate { Id = "t1", Name = template["name"]!.GetValue<string>(), Language = "en", Category = "MARKETING", Status = "PENDING" });
        public Task<List<WaMetaTemplate>> ListTemplatesAsync(CancellationToken ct = default) => Task.FromResult(new List<WaMetaTemplate>());
        public Task DeleteTemplateAsync(string name, string? metaTemplateId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<JsonObject> GetProfileAsync(CancellationToken ct = default) => Task.FromResult(new JsonObject());
        public Task UpdateProfileAsync(JsonObject profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task<JsonObject> GetNumberAsync(CancellationToken ct = default) => Task.FromResult(new JsonObject());
        public Task<List<WaPricePoint>> PricingAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) => Task.FromResult(new List<WaPricePoint>());
        public Task<Dictionary<string, string>> AccountNumbersAsync(CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string> { ["111"] = "+91 90000 00000" });
        public Task<Dictionary<string, string>> SubscribedAppsAsync(CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
        public Task SubscribeAppAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<JsonObject> WebhookConfigurationAsync(CancellationToken ct = default) => Task.FromResult(new JsonObject());
        public Task SetWebhookAsync(string url, string verifyToken, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedConnection : IWaConnectionStore
    {
        public WaConnectionInfo Current { get; set; } = WaConnectionInfo.Empty with { PhoneNumberId = "111", WabaId = "222", AccessToken = "token" };
        public Task<WaConnectionInfo> RefreshAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public Task MarkWebhookAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoStore : IDocFileStore
    {
        public bool IsConfigured => false;
        public Task SaveAsync(string key, Stream content, string contentType) => Task.CompletedTask;
        public Task<Stream> OpenAsync(string key) => Task.FromResult<Stream>(new MemoryStream());
    }

    private static CmsDbContext NewDb()
    {
        var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.WaRates.AddRange(
            new WaRate { Country = "IN", Category = "marketing", Rate = 0.8631m, UpdatedAt = Now },
            new WaRate { Country = "IN", Category = "utility", Rate = 0.115m, UpdatedAt = Now });
        db.SaveChanges();
        return db;
    }

    private static WaWebhookService Webhook(CmsDbContext db) =>
        new(db, new FixedConnection(), NullLogger<WaWebhookService>.Instance);

    private static JsonElement Payload(object value, string phoneId = "111") =>
        JsonSerializer.SerializeToElement(new
        {
            @object = "whatsapp_business_account",
            entry = new[] { new { id = "waba", changes = new[] { new { field = "messages", value = Merge(value, phoneId) } } } },
        });

    private static Dictionary<string, object> Merge(object value, string phoneId)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(value))!;
        d["messaging_product"] = "whatsapp";
        d["metadata"] = new { display_phone_number = "919000000000", phone_number_id = phoneId };
        return d;
    }

    private static JsonElement Inbound(string from, string wamId, string text, DateTime at, string? replyTo = null) => Payload(new
    {
        contacts = new[] { new { wa_id = from, profile = new { name = "Asha Rao" } } },
        messages = new[]
        {
            replyTo == null
                ? (object)new { from, id = wamId, timestamp = new DateTimeOffset(at).ToUnixTimeSeconds().ToString(), type = "text", text = new { body = text } }
                : new { from, id = wamId, timestamp = new DateTimeOffset(at).ToUnixTimeSeconds().ToString(), type = "text", text = new { body = text }, context = new { id = replyTo } },
        },
    });

    private static JsonElement Status(string wamId, string status, DateTime at, bool billable = true, string category = "marketing", int? error = null) => Payload(new
    {
        statuses = new[]
        {
            error == null
                ? (object)new { id = wamId, status, timestamp = new DateTimeOffset(at).ToUnixTimeSeconds().ToString(), recipient_id = "919812345678",
                                pricing = new { billable, pricing_model = "PMP", type = billable ? "regular" : "free_customer_service", category } }
                : new { id = wamId, status, timestamp = new DateTimeOffset(at).ToUnixTimeSeconds().ToString(), recipient_id = "919812345678",
                        errors = new[] { new { code = error.Value, title = "error" } } },
        },
    });

    private static WaActor Team => new() { UserId = Asha, Name = "Asha", CanReply = true, CanContacts = true, CanBroadcast = true, CanTemplates = true };
    private static WaActor Boss => new() { UserId = Asha, Name = "Asha", CanReply = true, CanContacts = true, CanBroadcast = true, CanTemplates = true, CanApprove = true, CanSettings = true };

    private static WaTemplate Approved(CmsDbContext db, string category = "MARKETING")
    {
        var t = new WaTemplate
        {
            TemplateId = Guid.NewGuid(), Name = "camp_offer", Language = "en", Category = category, Status = "APPROVED",
            Components = "[{\"type\":\"BODY\",\"text\":\"Hello {{1}}, our health camp is on {{2}}.\"}]",
            CreatedAt = Now, UpdatedAt = Now,
        };
        db.WaTemplates.Add(t);
        db.SaveChanges();
        return t;
    }

    private static WaContact Contact(CmsDbContext db, string phone, string optIn = "OptedIn", string? name = "Dr Asha Rao")
    {
        var c = new WaContact { ContactId = Guid.NewGuid(), Phone = phone, Name = name, Source = "Import", OptIn = optIn, CreatedAt = Now, UpdatedAt = Now };
        db.WaContacts.Add(c);
        db.SaveChanges();
        return c;
    }

    private static WaBroadcastRequest Request(WaTemplate t) => new()
    {
        Name = "November camp",
        TemplateId = t.TemplateId,
        Variables = new() { ["1"] = new WaVariableValue { From = "name", Text = "there" }, ["2"] = new WaVariableValue { From = "text", Text = "Sunday" } },
        ScheduledAt = Now.AddMinutes(-1),
    };

    // ── Rules ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("98123 45678", "919812345678")]
    [InlineData("+91 98123-45678", "919812345678")]
    [InlineData("0091 9812345678", "919812345678")]
    [InlineData("447700900123", "447700900123")]
    [InlineData("12", null)]
    public void Phone_numbers_are_stored_with_their_country_code(string raw, string? expected) =>
        Assert.Equal(expected, WaRules.NormalisePhone(raw));

    [Fact]
    public void Stop_words_in_English_and_Hindi_opt_out()
    {
        Assert.True(WaRules.IsStopWord("STOP"));
        Assert.True(WaRules.IsStopWord(" unsubscribe "));
        Assert.False(WaRules.IsStopWord("please don't stop the camp"));
    }

    [Fact]
    public void Only_billable_messages_cost_money()
    {
        var rates = new Dictionary<(string, string), decimal> { [("IN", "marketing")] = 0.8631m };
        Assert.Equal(0.8631m, WaRules.CostOf(true, "919812345678", "marketing", rates));
        Assert.Equal(0m, WaRules.CostOf(false, "919812345678", "marketing", rates));
    }

    // ── Webhook ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_incoming_message_opens_a_chat_once_even_when_Meta_sends_it_twice()
    {
        using var db = NewDb();
        var hook = Webhook(db);
        await hook.HandleAsync(Inbound("919812345678", "wamid.in1", "Is the camp free?", Now));
        await hook.HandleAsync(Inbound("919812345678", "wamid.in1", "Is the camp free?", Now));

        var contact = Assert.Single(db.WaContacts);
        Assert.Equal("Asha Rao", contact.ProfileName);
        Assert.Equal(1, contact.UnreadCount);
        Assert.Equal(Now, contact.LastInboundAt);
        Assert.Single(db.WaMessages);
    }

    [Fact]
    public async Task Messages_for_another_number_are_ignored()
    {
        using var db = NewDb();
        await Webhook(db).HandleAsync(JsonSerializer.SerializeToElement(new
        {
            entry = new[] { new { changes = new[] { new { field = "messages", value = Merge(new { messages = new[] { new { from = "919812345678", id = "x", type = "text", text = new { body = "hi" } } } }, "999") } } } },
        }));
        Assert.Empty(db.WaMessages);
    }

    [Fact]
    public async Task Stop_opts_the_person_out()
    {
        using var db = NewDb();
        var c = Contact(db, "919812345678");
        await Webhook(db).HandleAsync(Inbound("919812345678", "wamid.in2", "STOP", Now));
        Assert.Equal("OptedOut", db.WaContacts.Single(x => x.ContactId == c.ContactId).OptIn);
    }

    [Fact]
    public async Task Cost_is_counted_once_on_delivery_and_statuses_never_go_back()
    {
        using var db = NewDb();
        var c = Contact(db, "919812345678");
        db.WaMessages.Add(new WaMessage { MessageId = Guid.NewGuid(), ContactId = c.ContactId, WamId = "wamid.out1", Type = "template", Status = "sent", CreatedAt = Now });
        db.SaveChanges();
        var hook = Webhook(db);

        await hook.HandleAsync(Status("wamid.out1", "delivered", Now.AddSeconds(5)));
        await hook.HandleAsync(Status("wamid.out1", "read", Now.AddSeconds(9)));
        await hook.HandleAsync(Status("wamid.out1", "delivered", Now.AddSeconds(5)));   // late copy

        var m = db.WaMessages.Single();
        Assert.Equal("read", m.Status);
        Assert.Equal(0.8631m, m.Cost);
        Assert.Equal("marketing", m.PricingCategory);
    }

    [Fact]
    public async Task A_free_reply_costs_nothing()
    {
        using var db = NewDb();
        var c = Contact(db, "919812345678");
        db.WaMessages.Add(new WaMessage { MessageId = Guid.NewGuid(), ContactId = c.ContactId, WamId = "wamid.out2", Type = "text", Status = "sent", CreatedAt = Now });
        db.SaveChanges();
        await Webhook(db).HandleAsync(Status("wamid.out2", "delivered", Now, billable: false, category: "service"));
        Assert.Equal(0m, db.WaMessages.Single().Cost);
    }

    [Fact]
    public async Task Error_131050_means_the_person_stopped_marketing_messages()
    {
        using var db = NewDb();
        var c = Contact(db, "919812345678");
        db.WaMessages.Add(new WaMessage { MessageId = Guid.NewGuid(), ContactId = c.ContactId, WamId = "wamid.out3", Type = "template", Status = "sent", CreatedAt = Now });
        db.SaveChanges();
        await Webhook(db).HandleAsync(Status("wamid.out3", "failed", Now, error: 131050));
        Assert.Equal("failed", db.WaMessages.Single().Status);
        Assert.Equal("OptedOut", db.WaContacts.Single().OptIn);
    }

    // ── Inbox ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Free_text_only_inside_the_24_hour_window()
    {
        using var db = NewDb();
        var meta = new FakeMeta();
        var c = Contact(db, "919812345678");
        c.LastInboundAt = Now.AddHours(-25);
        db.SaveChanges();
        var inbox = new WaInboxService(db, meta, new NoStore(), () => Now);

        var closed = await inbox.SendTextAsync(Team, c.ContactId, new WaSendTextRequest { Text = "Hello" });
        Assert.False(closed.Ok);
        Assert.Equal(409, closed.Status);

        c.LastInboundAt = Now.AddHours(-2);
        db.SaveChanges();
        var open = await inbox.SendTextAsync(Team, c.ContactId, new WaSendTextRequest { Text = "Hello" });
        Assert.True(open.Ok, open.Message);
        Assert.Single(meta.Sent);
        Assert.Equal(Asha, db.WaContacts.Single().AssignedUserId);   // the first to reply gets the chat
    }

    [Fact]
    public async Task Reading_needs_no_reply_permission_but_replying_does()
    {
        using var db = NewDb();
        var c = Contact(db, "919812345678");
        c.LastInboundAt = Now;
        db.SaveChanges();
        var inbox = new WaInboxService(db, new FakeMeta(), new NoStore(), () => Now);
        var viewer = new WaActor { UserId = Guid.NewGuid(), Name = "Viewer" };

        Assert.True((await inbox.ThreadAsync(viewer, c.ContactId)).Ok);
        Assert.Equal(403, (await inbox.SendTextAsync(viewer, c.ContactId, new WaSendTextRequest { Text = "Hi" })).Status);
    }

    // ── Contacts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_import_never_opts_back_in_someone_who_opted_out()
    {
        using var db = NewDb();
        Contact(db, "919812345678", optIn: "OptedOut");
        var contacts = new WaContactService(db, null!);
        var result = await contacts.ImportAsync(Team, new WaImportRequest
        {
            AllOptedIn = true,
            OptInSource = "Signed form at the camp",
            Rows = new() { new WaImportRow { Phone = "98123 45678" }, new WaImportRow { Phone = "98765 43210", Name = "Bilal" } },
        });
        Assert.True(result.Ok, result.Message);
        Assert.Equal("OptedOut", db.WaContacts.Single(c => c.Phone == "919812345678").OptIn);
        Assert.Equal("OptedIn", db.WaContacts.Single(c => c.Phone == "919876543210").OptIn);
    }

    [Fact]
    public async Task Opting_people_in_needs_a_reason()
    {
        using var db = NewDb();
        var contacts = new WaContactService(db, null!);
        var result = await contacts.ImportAsync(Team, new WaImportRequest { AllOptedIn = true, Rows = new() { new WaImportRow { Phone = "9812345678" } } });
        Assert.False(result.Ok);
    }

    // ── Broadcasts and budget ────────────────────────────────────────────────

    [Fact]
    public async Task The_estimate_counts_only_opted_in_people()
    {
        using var db = NewDb();
        var t = Approved(db);
        Contact(db, "919812345671");
        Contact(db, "919812345672");
        Contact(db, "919812345673", optIn: "Unknown");
        Contact(db, "919812345674", optIn: "OptedOut");
        var broadcasts = new WaBroadcastService(db, new FakeMeta(), new NoStore(), NullLogger<WaBroadcastService>.Instance, () => Now);

        var preview = await broadcasts.PreviewAsync(Team, Request(t));
        Assert.True(preview.Ok, preview.Message);
        Assert.Equal(4, preview.Data!.Matched);
        Assert.Equal(2, preview.Data.WillSend);
        Assert.Equal(2 * 0.8631m, preview.Data.EstimatedCost);
    }

    [Fact]
    public async Task Over_the_budget_waits_for_approval_then_sends_to_opted_in_people_only()
    {
        using var db = NewDb();
        db.WaSettings.Add(new WaSettings { SettingsId = 1, MonthlyBudget = 1m, UpdatedAt = Now });
        db.SaveChanges();
        var t = Approved(db);
        Contact(db, "919812345671", name: "Dr Asha Rao");
        Contact(db, "919812345672", name: "Bilal Khan");
        Contact(db, "919812345673", optIn: "Unknown");
        var meta = new FakeMeta();
        var broadcasts = new WaBroadcastService(db, meta, new NoStore(), NullLogger<WaBroadcastService>.Instance, () => Now);

        var created = await broadcasts.CreateAsync(Team, Request(t));
        Assert.True(created.Ok, created.Message);
        Assert.Equal("AwaitingApproval", created.Data!.Status);
        Assert.Equal(0, await broadcasts.SendDueAsync(20, CancellationToken.None));

        Assert.Equal(403, (await broadcasts.ApproveAsync(Team, created.Data.BroadcastId)).Status);
        Assert.True((await broadcasts.ApproveAsync(Boss, created.Data.BroadcastId)).Ok);

        Assert.Equal(2, await broadcasts.SendDueAsync(20, CancellationToken.None));
        Assert.Equal(2, meta.Sent.Count);
        var bodies = meta.Sent.Select(m => m.ToJsonString()).ToList();
        Assert.Contains(bodies, b => b.Contains("Dr Asha"));
        Assert.Contains(bodies, b => b.Contains("Bilal") && b.Contains("Sunday"));

        await broadcasts.SendDueAsync(20, CancellationToken.None);
        Assert.Equal("Done", db.WaBroadcasts.Single().Status);
    }

    [Fact]
    public async Task An_unapproved_broadcast_pauses_at_the_budget()
    {
        using var db = NewDb();
        var t = Approved(db);
        Contact(db, "919812345671");
        Contact(db, "919812345672");
        var meta = new FakeMeta();
        var broadcasts = new WaBroadcastService(db, meta, new NoStore(), NullLogger<WaBroadcastService>.Instance, () => Now);
        var created = await broadcasts.CreateAsync(Team, Request(t));
        Assert.Equal("Scheduled", created.Data!.Status);

        // Someone lowers the budget after it was scheduled.
        db.WaSettings.Add(new WaSettings { SettingsId = 1, MonthlyBudget = 1m, UpdatedAt = Now });
        db.SaveChanges();
        Assert.Equal(0, await broadcasts.SendDueAsync(20, CancellationToken.None));
        var b = db.WaBroadcasts.Single();
        Assert.Equal("Paused", b.Status);
        Assert.StartsWith("Budget reached", b.PausedReason);
        Assert.Empty(meta.Sent);
    }

    [Fact]
    public async Task Someone_who_opts_out_after_scheduling_is_skipped()
    {
        using var db = NewDb();
        var t = Approved(db);
        var leaving = Contact(db, "919812345671");
        Contact(db, "919812345672");
        var meta = new FakeMeta();
        var broadcasts = new WaBroadcastService(db, meta, new NoStore(), NullLogger<WaBroadcastService>.Instance, () => Now);
        await broadcasts.CreateAsync(Team, Request(t));

        leaving.OptIn = "OptedOut";
        db.SaveChanges();
        await broadcasts.SendDueAsync(20, CancellationToken.None);
        Assert.Single(meta.Sent);
    }

    [Fact]
    public async Task Only_approved_templates_can_be_broadcast()
    {
        using var db = NewDb();
        var t = Approved(db);
        t.Status = "PENDING";
        db.SaveChanges();
        var broadcasts = new WaBroadcastService(db, new FakeMeta(), new NoStore(), NullLogger<WaBroadcastService>.Instance, () => Now);
        Assert.False((await broadcasts.CreateAsync(Team, Request(t))).Ok);
    }

    // ── Templates ────────────────────────────────────────────────────────────

    [Fact]
    public void Template_limits_are_checked_before_Meta_sees_them()
    {
        var ok = new WaSaveTemplateRequest { Name = "camp_offer", Body = "Hello {{1}}", BodyExamples = new() { ["1"] = "Asha" } };
        Assert.Null(WaTemplateParts.Check(ok));
        Assert.NotNull(WaTemplateParts.Check(new WaSaveTemplateRequest { Name = "Camp Offer!", Body = "Hello" }));
        Assert.NotNull(WaTemplateParts.Check(new WaSaveTemplateRequest { Name = "camp", Body = new string('a', 1025) }));
        Assert.NotNull(WaTemplateParts.Check(new WaSaveTemplateRequest { Name = "camp", Body = "Hello {{1}}" }));   // no example
    }

    // ── Webhook signature ────────────────────────────────────────────────────

    [Fact]
    public void The_webhook_accepts_only_Metas_signature()
    {
        var body = Encoding.UTF8.GetBytes("{\"entry\":[]}");
        var sig = "sha256=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), body)).ToLowerInvariant();
        Assert.True(WhatsAppWebhookController.Signed(body, "secret", sig));
        Assert.False(WhatsAppWebhookController.Signed(body, "other", sig));
        Assert.False(WhatsAppWebhookController.Signed(body, "secret", ""));
        Assert.False(WhatsAppWebhookController.Signed(Encoding.UTF8.GetBytes("{}"), "secret", sig));
    }

    // ── Setup: keys kept encrypted ───────────────────────────────────────────

    [Fact]
    public void Secrets_are_stored_encrypted_and_only_the_same_key_reads_them()
    {
        var box = new WaSecretBox(new byte[32]);
        var sealedText = box.Seal("EAAG-secret-token");
        Assert.StartsWith("v1:", sealedText);
        Assert.DoesNotContain("secret", sealedText);
        Assert.Equal("EAAG-secret-token", box.Open(sealedText));
        Assert.NotEqual(sealedText, box.Seal("EAAG-secret-token"));   // a new nonce every time
        var other = new WaSecretBox(Enumerable.Repeat((byte)7, 32).ToArray());
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => other.Open(sealedText));
    }

    [Fact]
    public void The_key_comes_from_the_sign_in_key_when_none_is_set()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["TokenSettings:Key"] = "a-long-signing-key-for-tests-only-0123456789" }).Build();
        var a = new WaSecretBox(config);
        var b = new WaSecretBox(config);
        Assert.True(a.CanSeal);
        Assert.Equal("x", b.Open(a.Seal("x")));
        Assert.False(new WaSecretBox(new ConfigurationBuilder().Build()).CanSeal);
    }

    [Fact]
    public async Task Only_people_who_may_connect_can_enter_keys_and_ids_are_checked()
    {
        using var db = NewDb();
        var setup = new WaSetupService(db, new FixedConnection(), new WaSecretBox(new byte[32]), new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider());
        Assert.Equal(403, (await setup.SaveAsync(Boss, new WaSetupSaveRequest { PhoneNumberId = "123456789012345" }, "https://x/api/v1/whatsapp/webhook")).Status);

        var admin = new WaActor { UserId = Asha, Name = "Asha", CanConnect = true };
        Assert.Equal(400, (await setup.SaveAsync(admin, new WaSetupSaveRequest { PhoneNumberId = "+91 98765 43210" }, "u")).Status);
        Assert.Equal(400, (await setup.SaveAsync(admin, new WaSetupSaveRequest { PhoneNumberId = "123456789012345", AppSecret = "not-a-secret" }, "u")).Status);

        var token = "EAAG" + new string('x', 60) + "abcd";
        var ok = await setup.SaveAsync(admin, new WaSetupSaveRequest { PhoneNumberId = "123456789012345", WabaId = "998877665544", AccessToken = token, AppSecret = new string('a', 32) }, "u");
        Assert.True(ok.Ok, ok.Message);
        var row = db.WaConnections.Single();
        Assert.DoesNotContain("EAAG", row.AccessTokenEnc);
        Assert.Equal("abcd", row.AccessTokenHint);
        Assert.NotNull(row.VerifyTokenEnc);   // made for the webhook
        Assert.Equal(token, new WaSecretBox(new byte[32]).Open(row.AccessTokenEnc));

        // Saving again without a token keeps the stored one.
        await setup.SaveAsync(admin, new WaSetupSaveRequest { PhoneNumberId = "123456789012345", WabaId = "998877665544" }, "u");
        Assert.Equal("abcd", db.WaConnections.Single().AccessTokenHint);
    }

    [Fact]
    public async Task The_webhook_address_must_be_https()
    {
        using var db = NewDb();
        var setup = new WaSetupService(db, new FixedConnection(), new WaSecretBox(new byte[32]), new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider());
        var admin = new WaActor { UserId = Asha, Name = "Asha", CanConnect = true };
        Assert.Equal(400, (await setup.ConnectWebhookAsync(admin, new WaConnectWebhookRequest { Url = "http://151.185.45.77:5002/api/v1/whatsapp/webhook" }, "u")).Status);
    }
}
