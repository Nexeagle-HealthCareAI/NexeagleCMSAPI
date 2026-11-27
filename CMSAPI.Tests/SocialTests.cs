#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Social;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// CMS → Social: webhooks (comments, messages, lead forms), the reply windows, making leads, the missed-lead
// check, composing with approval and publishing to Facebook and Instagram, the queue, ads. Meta is faked.
public class SocialTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Asha = Guid.NewGuid();
    private const string Page = "1001", Ig = "1002", App = "3003";
    private static readonly WaSecretBox Box = new(new byte[32]);

    private sealed class FakeMeta : IMetaSocial
    {
        public List<string> Calls { get; } = new();
        public Dictionary<string, MetaLeadItem> Leads { get; } = new();
        public List<MetaLeadItem> FormLeads { get; } = new();
        public Queue<string> ContainerStatus { get; } = new();
        public int FailFacebookWith { get; set; }
        private int _n;
        private string Id(string p) => $"{p}{++_n}";

        public Task<MetaAssets> DiscoverAsync(string token, CancellationToken ct = default) => Task.FromResult(new MetaAssets
        {
            UserName = "System user", Permissions = new() { "pages_read_engagement", "leads_retrieval" },
            Pages = new() { new MetaPage { Id = Page, Name = "Example Page", InstagramId = Ig, InstagramUsername = "example" } },
            AdAccounts = new() { new MetaAdAccount { Id = "act_9", Name = "Example ads", Currency = "INR" } },
        });
        public Task<List<MetaAppSubscription>> AppSubscriptionsAsync(string appId, string appSecret, CancellationToken ct = default) => Task.FromResult(new List<MetaAppSubscription>());
        public Task SubscribeAppAsync(string appId, string appSecret, string obj, string callbackUrl, string fields, string verifyToken, CancellationToken ct = default) { Calls.Add("subscribe:" + obj); return Task.CompletedTask; }
        public Task SubscribePageAsync(SocialCreds c, string fields, CancellationToken ct = default) { Calls.Add("page:" + fields); return Task.CompletedTask; }
        public Task<List<string>> PageSubscribedFieldsAsync(SocialCreds c, CancellationToken ct = default) => Task.FromResult(new List<string>());
        public Task<List<MetaPostItem>> FacebookPostsAsync(SocialCreds c, DateTime? since, int max, CancellationToken ct = default) => Task.FromResult(new List<MetaPostItem>());
        public Task<List<MetaPostItem>> InstagramMediaAsync(SocialCreds c, DateTime? since, int max, CancellationToken ct = default) => Task.FromResult(new List<MetaPostItem>());
        public Task<Dictionary<string, long>> InsightsAsync(SocialCreds c, string channel, string metaId, string mediaType, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, long> { ["views"] = 120 });
        public Task<(int? Facebook, int? Instagram)> FollowersAsync(SocialCreds c, CancellationToken ct = default) => Task.FromResult<(int?, int?)>((10, 20));
        public Task<List<MetaCommentItem>> CommentsAsync(SocialCreds c, string channel, string postMetaId, CancellationToken ct = default) => Task.FromResult(new List<MetaCommentItem>());
        public Task<string> ReplyToCommentAsync(SocialCreds c, string channel, string commentMetaId, string text, CancellationToken ct = default) { Calls.Add("reply:" + commentMetaId); return Task.FromResult(Id("c")); }
        public Task HideCommentAsync(SocialCreds c, string channel, string commentMetaId, bool hide, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> PrivateReplyAsync(SocialCreds c, string commentMetaId, string text, CancellationToken ct = default) { Calls.Add("private:" + commentMetaId); return Task.FromResult(Id("m")); }
        public Task<string> SendMessageAsync(SocialCreds c, string channel, string recipientId, string text, bool humanAgent, CancellationToken ct = default)
        {
            Calls.Add($"send:{recipientId}:{(humanAgent ? "agent" : "normal")}");
            return Task.FromResult(Id("mid."));
        }
        public Task<string> SendAttachmentAsync(SocialCreds c, string channel, string recipientId, byte[] data, string mime, string fileName, bool humanAgent, CancellationToken ct = default) => Task.FromResult(Id("mid."));
        public Task<MetaPerson> PersonAsync(SocialCreds c, string channel, string externalId, CancellationToken ct = default) => Task.FromResult(new MetaPerson { Name = "Example Person", Username = channel == "instagram" ? "example.person" : null });
        public Task<MetaLeadItem> LeadAsync(SocialCreds c, string leadgenId, CancellationToken ct = default) =>
            Leads.TryGetValue(leadgenId, out var l) ? Task.FromResult(l) : throw new MetaSocialException("No such lead", 100);
        public Task<List<MetaLeadForm>> LeadFormsAsync(SocialCreds c, CancellationToken ct = default) => Task.FromResult(new List<MetaLeadForm> { new() { Id = "f1", Name = "Demo request", Status = "ACTIVE" } });
        public Task<List<MetaLeadItem>> FormLeadsAsync(SocialCreds c, string formId, DateTime since, CancellationToken ct = default) => Task.FromResult(FormLeads);
        public Task<List<MetaAdRow>> AdInsightsAsync(SocialCreds c, string level, DateTime fromDay, DateTime toDay, CancellationToken ct = default) => Task.FromResult(new List<MetaAdRow>());
        public Task<Dictionary<string, string>> AdStatusesAsync(SocialCreds c, string level, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, string>());
        public Task<string> PublishFacebookAsync(SocialCreds c, FacebookPublish post, CancellationToken ct = default)
        {
            if (FailFacebookWith != 0) throw new MetaSocialException("Meta said no", FailFacebookWith, httpStatus: 400);
            Calls.Add("fb:" + post.Kind + ":" + post.Media.Count);
            return Task.FromResult(Page + "_" + Id("p"));
        }
        public Task<string> CreateInstagramContainerAsync(SocialCreds c, InstagramContainer x, CancellationToken ct = default)
        {
            Calls.Add("ig-container:" + x.MediaType + (x.ImageUrl != null ? ":url" : ""));
            return Task.FromResult(Id("ct"));
        }
        public Task<string> InstagramContainerStatusAsync(SocialCreds c, string containerId, CancellationToken ct = default) =>
            Task.FromResult(ContainerStatus.Count > 0 ? ContainerStatus.Dequeue() : "FINISHED");
        public Task<string> PublishInstagramAsync(SocialCreds c, string containerId, CancellationToken ct = default) { Calls.Add("ig-publish:" + containerId); return Task.FromResult(Id("ig")); }
        public Task<(int Used, int Total)> InstagramPublishingLimitAsync(SocialCreds c, CancellationToken ct = default) => Task.FromResult((1, 50));
        public Task<string?> PermalinkAsync(SocialCreds c, string channel, string metaId, CancellationToken ct = default) => Task.FromResult<string?>("https://example.com/" + metaId);
    }

    private sealed class MemoryStore : IDocFileStore
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public bool IsConfigured => true;
        public async Task SaveAsync(string key, System.IO.Stream content, string contentType)
        {
            using var ms = new System.IO.MemoryStream();
            await content.CopyToAsync(ms);
            Files[key] = ms.ToArray();
        }
        public Task<System.IO.Stream> OpenAsync(string key) => Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream(Files[key]));
    }

    private static CmsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SocialAccount Account(CmsDbContext db)
    {
        var a = new SocialAccount
        {
            AccountId = Guid.NewGuid(), Name = "NexEagle", AppId = App, AppSecretEnc = Box.Seal(new string('a', 32)), TokenEnc = Box.Seal("EAAG" + new string('x', 60)),
            VerifyTokenEnc = Box.Seal("verify-me"), PageId = Page, PageName = "Example Page", IgUserId = Ig, IgUsername = "example",
            WebhookUrl = "https://cms-api.example.com/api/v1/meta/webhook", IsActive = true, CreatedAt = Now, UpdatedAt = Now,
        };
        db.SocialAccounts.Add(a);
        db.SaveChanges();
        return a;
    }

    private static SocialKeys Keys(CmsDbContext db) => new(db, Box);
    private static IServiceProvider NoServices => new ServiceCollection().BuildServiceProvider();
    private static SocialLeadsService Leads(CmsDbContext db, FakeMeta meta) => new(db, meta, Keys(db), NoServices, NullLogger<SocialLeadsService>.Instance, () => Now);
    private static SocialWebhookService Webhook(CmsDbContext db, FakeMeta meta) => new(db, meta, Keys(db), Leads(db, meta), NullLogger<SocialWebhookService>.Instance, () => Now);
    private static SocialInboxService Inbox(CmsDbContext db, FakeMeta meta, DateTime? now = null) => new(db, meta, Keys(db), () => now ?? Now);
    private static SocialComposeService Compose(CmsDbContext db, FakeMeta meta, MemoryStore files, DateTime? now = null) =>
        new(db, meta, Keys(db), files, new SocialMediaLinks(new byte[32], "https://cms-api.example.com"), NullLogger<SocialComposeService>.Instance, () => now ?? Now);

    private static SocialActor Writer => new() { UserId = Asha, Name = "Asha", CanReply = true, CanCompose = true };
    private static SocialActor Approver => new() { UserId = Asha, Name = "Asha", CanReply = true, CanCompose = true, CanPublish = true };

    private static JsonElement J(object o) => JsonSerializer.SerializeToElement(o);
    private static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

    // ── webhook: comments ────────────────────────────────────────────────────

    private static JsonElement FbComment(string id, string? parent, string fromId, string verb = "add") => J(new
    {
        @object = "page",
        entry = new[] { new { id = Page, changes = new[] { new { field = "feed", value = new { item = "comment", verb, comment_id = id, post_id = "1001_55", parent_id = parent ?? "1001_55", from = new { id = fromId, name = "Example commenter" }, message = "Price for 30 beds?", created_time = 1795750000 } } } } },
    });

    [Fact]
    public async Task A_comment_is_stored_once_and_our_reply_closes_it()
    {
        using var db = NewDb();
        Account(db);
        var meta = new FakeMeta();
        await Webhook(db, meta).HandleAsync(FbComment("c1", null, "777"));
        await Webhook(db, meta).HandleAsync(FbComment("c1", null, "777"));
        var c = Assert.Single(db.SocialComments);
        Assert.Equal("Open", c.Status);
        Assert.Null(c.ParentMetaId);

        await Webhook(db, meta).HandleAsync(FbComment("c2", "c1", Page));   // the Page answers in Meta Business Suite
        Assert.Equal("Done", db.SocialComments.Single(x => x.MetaId == "c1").Status);
        Assert.True(db.SocialComments.Single(x => x.MetaId == "c2").FromUs);

        await Webhook(db, meta).HandleAsync(FbComment("c1", null, "777", "remove"));
        Assert.True(db.SocialComments.Single(x => x.MetaId == "c1").Deleted);
    }

    [Fact]
    public async Task Replying_privately_is_possible_once_within_seven_days()
    {
        using var db = NewDb();
        Account(db);
        var meta = new FakeMeta();
        await Webhook(db, meta).HandleAsync(FbComment("c1", null, "777"));
        var id = db.SocialComments.Single().CommentId;
        db.SocialComments.Single().CreatedAt = Now.AddDays(-1);
        db.SaveChanges();

        var inbox = Inbox(db, meta);
        Assert.True((await inbox.ReplyToCommentAsync(Writer, id, new SocialCommentReply { Text = "Sent you the price list", Private = true })).Ok);
        Assert.Equal(409, (await inbox.ReplyToCommentAsync(Writer, id, new SocialCommentReply { Text = "Again", Private = true })).Status);
        Assert.Contains("private:c1", meta.Calls);
        Assert.Equal(403, (await inbox.ReplyToCommentAsync(new SocialActor { UserId = Asha, Name = "Viewer" }, id, new SocialCommentReply { Text = "Hi" })).Status);
    }

    // ── webhook: messages ────────────────────────────────────────────────────

    private static JsonElement Dm(string obj, string entryId, string sender, string recipient, string mid, DateTime at, bool echo = false) => J(new
    {
        @object = obj,
        entry = new[] { new { id = entryId, time = Ms(at), messaging = new[] { new { sender = new { id = sender }, recipient = new { id = recipient }, timestamp = Ms(at), message = new { mid, text = "Do you have a Hindi demo?", is_echo = echo } } } } },
    });

    [Fact]
    public async Task A_message_opens_a_conversation_once_and_echoes_are_not_doubled()
    {
        using var db = NewDb();
        Account(db);
        var meta = new FakeMeta();
        await Webhook(db, meta).HandleAsync(Dm("page", Page, "psid1", Page, "m.1", Now));
        await Webhook(db, meta).HandleAsync(Dm("page", Page, "psid1", Page, "m.1", Now));
        var c = Assert.Single(db.SocialContacts);
        Assert.Equal("messenger", c.Channel);
        Assert.Equal("Example Person", c.Name);
        Assert.Equal(1, c.UnreadCount);
        Assert.Equal(Now, c.LastInboundAt);

        // Our reply from the CMS, then Meta's echo of it.
        var sent = await Inbox(db, meta).SendAsync(Writer, c.ContactId, "Yes, on Thursday", null, null, null);
        Assert.True(sent.Ok, sent.Message);
        var mid = db.SocialMessages.Single(m => m.Direction == "Out").MetaId!;
        await Webhook(db, meta).HandleAsync(Dm("page", Page, Page, "psid1", mid, Now.AddSeconds(2), echo: true));
        Assert.Equal(2, db.SocialMessages.Count());

        // A reply written in Meta Business Suite comes in as an echo and is kept.
        await Webhook(db, meta).HandleAsync(Dm("page", Page, Page, "psid1", "m.suite", Now.AddMinutes(1), echo: true));
        Assert.Equal(3, db.SocialMessages.Count());
        Assert.Equal(1, db.SocialContacts.Count());
    }

    [Fact]
    public async Task After_24_hours_only_a_human_agent_may_reply_and_after_7_days_nobody()
    {
        using var db = NewDb();
        var a = Account(db);
        var meta = new FakeMeta();
        var c = new SocialContact { ContactId = Guid.NewGuid(), AccountId = a.AccountId, Channel = "instagram", ExternalId = "igsid1", LastInboundAt = Now.AddHours(-2), LastMessageAt = Now, CreatedAt = Now };
        db.SocialContacts.Add(c);
        db.SaveChanges();

        Assert.True((await Inbox(db, meta).SendAsync(Writer, c.ContactId, "Hello", null, null, null)).Ok);
        Assert.True((await Inbox(db, meta, Now.AddHours(30)).SendAsync(Writer, c.ContactId, "Following up", null, null, null)).Ok);
        Assert.Equal(new[] { "send:igsid1:normal", "send:igsid1:agent" }, meta.Calls);
        Assert.Equal(409, (await Inbox(db, meta, Now.AddDays(8)).SendAsync(Writer, c.ContactId, "Hi?", null, null, null)).Status);
    }

    // ── leads ────────────────────────────────────────────────────────────────

    private static MetaLeadItem Lead(string id, string phone) => new()
    {
        Id = id, CreatedAt = Now.AddHours(-1), FormId = "f1", AdId = "ad1", AdName = "Reel: five questions", CampaignId = "cp1", CampaignName = "1HMS demo", Platform = "ig",
        Answers = new() { new("full_name", "Example Hospital Owner"), new("phone_number", phone), new("city", "Purnia"), new("company_name", "Example Hospital") },
    };

    [Fact]
    public async Task A_lead_form_lead_becomes_one_CRM_lead_with_its_ad()
    {
        using var db = NewDb();
        var a = Account(db);
        var meta = new FakeMeta();
        meta.Leads["L1"] = Lead("L1", "+91 98000 00011");
        var hook = J(new { @object = "page", entry = new[] { new { id = Page, changes = new[] { new { field = "leadgen", value = new { leadgen_id = "L1", form_id = "f1", page_id = Page } } } } } });
        await Webhook(db, meta).HandleAsync(hook);
        await Webhook(db, meta).HandleAsync(hook);

        var crm = Assert.Single(db.CmsSalesLeads);
        Assert.Equal("META_ADS", crm.Source);
        Assert.Equal("Example Hospital Owner", crm.ContactName);
        Assert.Equal("Example Hospital", crm.HospitalName);
        Assert.Equal("ad1", crm.UtmAdId);
        var link = Assert.Single(db.SocialLeads);
        Assert.Equal("Reel: five questions", link.AdName);
        Assert.Equal(crm.LeadId, link.LeadId);
    }

    [Fact]
    public async Task A_lead_the_old_webhook_already_made_is_linked_not_doubled()
    {
        using var db = NewDb();
        var a = Account(db);
        db.CmsSalesLeads.Add(new CmsSalesLead { LeadId = Guid.NewGuid(), HospitalName = "Example Hospital", Mobile = "9800000011", Source = "META_ADS", CreatedAt = Now.AddHours(-1), UpdatedAt = Now });
        db.SaveChanges();
        var meta = new FakeMeta();
        meta.Leads["L1"] = Lead("L1", "+91 98000 00011");
        Assert.True((await Leads(db, meta).IngestFormLeadAsync(a.AccountId, "L1")).Ok);
        Assert.Single(db.CmsSalesLeads);
        Assert.Single(db.SocialLeads);
    }

    [Fact]
    public async Task The_missed_lead_check_finds_and_brings_in_what_the_webhook_missed()
    {
        using var db = NewDb();
        var a = Account(db);
        var meta = new FakeMeta();
        foreach (var (id, phone) in new[] { ("L1", "9800000001"), ("L2", "9800000002"), ("L3", "9800000003") })
        {
            meta.Leads[id] = Lead(id, phone);
            meta.FormLeads.Add(meta.Leads[id]);
        }
        var leads = Leads(db, meta);
        await leads.IngestFormLeadAsync(a.AccountId, "L1");
        await leads.IngestFormLeadAsync(a.AccountId, "L2");

        var check = await leads.CheckFormsAsync(a.AccountId);
        Assert.True(check.Ok, check.Message);
        var form = Assert.Single(check.Data!);
        Assert.Equal(3, form.MetaCount);
        Assert.Equal(1, form.Missing);

        var import = await leads.ImportMissingAsync(Writer, a.AccountId, "f1");
        Assert.True(import.Ok, import.Message);
        Assert.Equal(3, db.SocialLeads.Count());
    }

    [Fact]
    public async Task A_person_who_messages_becomes_a_lead_once()
    {
        using var db = NewDb();
        var a = Account(db);
        var c = new SocialContact { ContactId = Guid.NewGuid(), AccountId = a.AccountId, Channel = "instagram", ExternalId = "igsid1", Username = "example.clinic", CreatedAt = Now };
        db.SocialContacts.Add(c);
        db.SaveChanges();
        var leads = Leads(db, new FakeMeta());
        var made = await leads.FromConversationAsync(Writer, c.ContactId, new SocialMakeLead { Name = "Example Clinic Owner", Phone = "+91 98000 00021", InterestedIn = "1HMS" });
        Assert.True(made.Ok, made.Message);
        Assert.Equal("INSTAGRAM_DM", db.CmsSalesLeads.Single().Source);
        Assert.Contains("Interested in: 1HMS", db.CmsSalesLeads.Single().Notes);
        Assert.Equal(409, (await leads.FromConversationAsync(Writer, c.ContactId, new SocialMakeLead { Name = "Again" })).Status);
    }

    // ── compose and publish ──────────────────────────────────────────────────

    private static SocialMediaRef Jpg(int n) => new() { Key = $"social/media/2026/11/p{n}.jpg", Mime = "image/jpeg", Name = $"p{n}.jpg", Size = 1000 };

    private static SocialDraftSave Post(Guid account, string kind = "Carousel") => new()
    {
        AccountId = account, ToFacebook = true, ToInstagram = true, Kind = kind, Caption = "Five questions to ask before choosing hospital software.",
        Media = kind == "Carousel" ? new() { Jpg(1), Jpg(2), Jpg(3) } : new() { Jpg(1) }, WhenMode = "approved",
        Checklist = new() { ["noTestimonials"] = true, ["factsBackedUp"] = true },
    };

    [Fact]
    public void The_rules_follow_Facebook_and_Instagram()
    {
        var id = Guid.NewGuid();
        Assert.Null(SocialComposeService.Check(Post(id), Now));
        var text = Post(id, "Text"); text.Media.Clear();
        Assert.Contains("Instagram posts need a picture", SocialComposeService.Check(text, Now));
        var png = Post(id, "Photo"); png.Media = new() { new SocialMediaRef { Key = "social/media/x.png", Mime = "image/png", Name = "x.png" } };
        Assert.Contains("JPEG", SocialComposeService.Check(png, Now));
        var big = Post(id); big.Media = Enumerable.Range(1, 11).Select(Jpg).ToList();
        Assert.NotNull(SocialComposeService.Check(big, Now));
        var tags = Post(id); tags.Caption = string.Join(' ', Enumerable.Range(1, 31).Select(i => "#t" + i));
        Assert.Contains("hashtags", SocialComposeService.Check(tags, Now));
        var soon = Post(id); soon.WhenMode = "schedule"; soon.ScheduledAt = Now.AddMinutes(5);
        Assert.Contains("10 minutes", SocialComposeService.Check(soon, Now));
    }

    [Fact]
    public async Task A_post_needs_approval_and_a_change_after_it_needs_approval_again()
    {
        using var db = NewDb();
        var a = Account(db);
        var compose = Compose(db, new FakeMeta(), new MemoryStore());
        var saved = await compose.SaveAsync(Writer, null, Post(a.AccountId));
        Assert.True(saved.Ok, saved.Message);
        var id = saved.Data!.DraftId;
        Assert.True((await compose.SubmitAsync(Writer, id)).Ok);
        Assert.Equal(403, (await compose.ApproveAsync(Writer, id)).Status);
        Assert.True((await compose.ApproveAsync(Approver, id)).Ok);
        Assert.Equal("Scheduled", db.SocialDrafts.Single().Status);

        var changed = Post(a.AccountId);
        changed.Caption = "A different text";
        await compose.SaveAsync(Writer, id, changed);
        Assert.Equal("AwaitingApproval", db.SocialDrafts.Single().Status);
        Assert.Null(db.SocialDrafts.Single().ApprovedHash);
    }

    [Fact]
    public async Task Approval_needs_the_healthcare_checks_ticked()
    {
        using var db = NewDb();
        var a = Account(db);
        var compose = Compose(db, new FakeMeta(), new MemoryStore());
        var post = Post(a.AccountId);
        post.Checklist.Clear();
        var id = (await compose.SaveAsync(Writer, null, post)).Data!.DraftId;
        Assert.Equal(400, (await compose.ApproveAsync(Approver, id)).Status);
    }

    [Fact]
    public async Task An_approved_post_goes_to_Facebook_at_once_and_to_Instagram_when_Meta_has_prepared_it()
    {
        using var db = NewDb();
        var a = Account(db);
        var meta = new FakeMeta();
        meta.ContainerStatus.Enqueue("IN_PROGRESS");
        var files = new MemoryStore();
        foreach (var m in new[] { Jpg(1), Jpg(2), Jpg(3) }) files.Files[m.Key] = new byte[] { 1, 2, 3 };
        var compose = Compose(db, meta, files);
        var id = (await compose.SaveAsync(Writer, null, Post(a.AccountId))).Data!.DraftId;
        await compose.ApproveAsync(Approver, id);

        await compose.PublishDueAsync(CancellationToken.None);
        var d = db.SocialDrafts.Single();
        Assert.Equal("Publishing", d.Status);
        Assert.NotNull(d.FacebookPostId);
        Assert.Contains("fb:Carousel:3", meta.Calls);
        Assert.Equal(3, meta.Calls.Count(c => c == "ig-container:IMAGE:url"));
        Assert.Contains("ig-container:CAROUSEL", meta.Calls);

        await compose.PublishDueAsync(CancellationToken.None);
        d = db.SocialDrafts.Single();
        Assert.Equal("Published", d.Status);
        Assert.NotNull(d.InstagramMediaId);
        Assert.Equal(2, db.SocialPosts.Count(p => p.DraftId == id));
    }

    [Fact]
    public async Task A_refusal_from_Meta_fails_the_post_with_the_reason()
    {
        using var db = NewDb();
        var a = Account(db);
        var meta = new FakeMeta { FailFacebookWith = 100 };
        var files = new MemoryStore();
        files.Files[Jpg(1).Key] = new byte[] { 1 };
        var compose = Compose(db, meta, files);
        var id = (await compose.SaveAsync(Writer, null, Post(a.AccountId, "Photo"))).Data!.DraftId;
        await compose.ApproveAsync(Approver, id);
        await compose.PublishDueAsync(CancellationToken.None);
        var d = db.SocialDrafts.Single();
        Assert.Equal("Failed", d.Status);
        Assert.Equal("Meta said no", d.Error);
    }

    [Fact]
    public async Task The_queue_fills_the_next_free_slot_in_India_time()
    {
        using var db = NewDb();
        var a = Account(db);
        var compose = Compose(db, new FakeMeta(), new MemoryStore());
        // Now is Friday 27 Nov 2026, 11:30 in India. Slots: Friday 11:00 (passed) and Saturday 10:00.
        await compose.SaveSlotsAsync(Approver, a.AccountId, new() { new SocialSlotDto { DayOfWeek = 5, Time = "11:00", ToFacebook = true, ToInstagram = true }, new SocialSlotDto { DayOfWeek = 6, Time = "10:00", ToFacebook = true, ToInstagram = true } });
        var post = Post(a.AccountId);
        post.WhenMode = "queue";
        var first = (await compose.SaveAsync(Writer, null, post)).Data!.DraftId;
        var second = (await compose.SaveAsync(Writer, null, post)).Data!.DraftId;
        await compose.ApproveAsync(Approver, first);
        await compose.ApproveAsync(Approver, second);
        var times = db.SocialDrafts.OrderBy(d => d.ScheduledAt).Select(d => d.ScheduledAt).ToList();
        Assert.Equal(new DateTime(2026, 11, 28, 4, 30, 0, DateTimeKind.Utc), times[0]);   // Sat 10:00 IST
        Assert.Equal(new DateTime(2026, 12, 4, 5, 30, 0, DateTimeKind.Utc), times[1]);   // next Fri 11:00 IST
    }

    // ── ads and links ────────────────────────────────────────────────────────

    [Fact]
    public async Task Cost_per_lead_comes_from_campaign_rows()
    {
        using var db = NewDb();
        var a = Account(db);
        db.SocialAdStats.AddRange(
            new SocialAdStat { AccountId = a.AccountId, Day = Now.Date, Level = "campaign", ObjectId = "c1", Name = "Demo", Spend = 900, Impressions = 1000, Clicks = 30, Leads = 3 },
            new SocialAdStat { AccountId = a.AccountId, Day = Now.Date.AddDays(-1), Level = "campaign", ObjectId = "c1", Name = "Demo", Spend = 300, Impressions = 500, Clicks = 10, Leads = 1 });
        db.SaveChanges();
        var r = await new SocialAdsService(db).SummaryAsync(new SocialAdsQuery { AccountId = a.AccountId, From = Now.Date.AddDays(-6), To = Now.Date });
        Assert.True(r.Ok, r.Message);
        Assert.Equal(1200m, r.Data!.Spend);
        Assert.Equal(300m, r.Data.CostPerLead);
        Assert.Equal(7, r.Data.Days.Count);
        Assert.Equal(300m, Assert.Single(r.Data.Rows).CostPerLead);
    }

    [Fact]
    public void Media_links_for_Instagram_expire_and_cannot_be_forged()
    {
        var links = new SocialMediaLinks(new byte[32], "https://cms-api.example.com");
        var url = links.Link(new SocialAccount(), "social/media/2026/11/p1.jpg", TimeSpan.FromHours(1))!;
        var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.True(links.Valid(q["key"]!, long.Parse(q["exp"]!), q["sig"]!));
        Assert.False(links.Valid("social/media/2026/11/other.jpg", long.Parse(q["exp"]!), q["sig"]!));
        Assert.False(links.Valid(q["key"]!, 1, q["sig"]!));
        Assert.False(links.Valid("../appsettings.json", long.Parse(q["exp"]!), q["sig"]!));
    }
}
