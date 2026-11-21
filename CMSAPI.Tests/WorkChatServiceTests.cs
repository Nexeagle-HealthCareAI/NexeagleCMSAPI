#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Daily Work Board, team chat: who sees which conversation, unread counts, tagging and its
// notifications, important messages, escalation, and removing a message.
public class WorkChatServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();
    private static readonly Guid Chitra = Guid.NewGuid();

    private sealed class Space
    {
        public CmsDbContext Db = null!;
        public WorkChatService Chat = null!;
        public WorkAccessService Access = null!;
        public Guid Hms;
        public Guid Rad;
        public Guid HmsItem;
    }

    private static WorkActor As(Guid userId, bool seesAll = false) => new WorkActor { UserId = userId, SeesAllBoards = seesAll };
    private static SendWorkChatMessageRequest Say(string body, params Guid[] tag) => new SendWorkChatMessageRequest { Body = body, MentionUserIds = tag.ToList() };

    private static async Task<Space> NewSpaceAsync()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.CmsUsers.Add(new CmsUser { UserId = Asha, FullName = "Asha Rao", Email = "asha@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsUsers.Add(new CmsUser { UserId = Bilal, FullName = "Bilal Khan", Email = "bilal@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsUsers.Add(new CmsUser { UserId = Chitra, FullName = "Chitra Das", Email = "chitra@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        var hms = new WorkProject { ProjectId = Guid.NewGuid(), Key = "HMS", Name = "1HMS", NextNumber = 2, CreatedAt = DateTime.UtcNow };
        var rad = new WorkProject { ProjectId = Guid.NewGuid(), Key = "RAD", Name = "1Rad", NextNumber = 1, CreatedAt = DateTime.UtcNow };
        db.WorkProjects.AddRange(hms, rad);
        var item = new WorkItem { WorkItemId = Guid.NewGuid(), ProjectId = hms.ProjectId, Number = 1, ItemType = "Bug", Title = "Bill total is wrong", Status = "ToDo", Priority = "High", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync();
        var access = new WorkAccessService(db);
        return new Space { Db = db, Access = access, Chat = new WorkChatService(db, access), Hms = hms.ProjectId, Rad = rad.ProjectId, HmsItem = item.WorkItemId };
    }

    private static async Task<Guid> BoardChannelAsync(Space space, Guid user, Guid projectId) =>
        (await space.Chat.GetStateAsync(As(user), null, null)).Channels.Single(c => c.ProjectId == projectId).ChannelId;

    [Fact]
    public async Task EveryBoardHasAConversation_MadeOnce()
    {
        var space = await NewSpaceAsync();

        var first = await space.Chat.GetStateAsync(As(Asha), null, null);
        var second = await space.Chat.GetStateAsync(As(Bilal), null, null);

        Assert.Equal(new[] { "HMS", "RAD" }, first.Channels.Select(c => c.ProjectKey));
        Assert.Equal(first.Channels.Select(c => c.ChannelId), second.Channels.Select(c => c.ChannelId));
        Assert.Equal(2, await space.Db.WorkChatChannels.CountAsync());
    }

    [Fact]
    public async Task AMembersOnlyBoardsConversation_IsHiddenFromEveryoneElse()
    {
        var space = await NewSpaceAsync();
        var channel = await BoardChannelAsync(space, Asha, space.Hms);
        await space.Access.SaveAccessAsync(space.Hms, new SaveWorkProjectAccessRequest { IsRestricted = true, MemberUserIds = { Asha } }, Asha);

        var bilal = await space.Chat.GetStateAsync(As(Bilal), null, null);
        var read = await space.Chat.GetMessagesAsync(As(Bilal), channel, null, false);
        var write = await space.Chat.SendAsync(As(Bilal), channel, Say("Hello"));

        Assert.DoesNotContain(bilal.Channels, c => c.ProjectId == space.Hms);
        Assert.Equal(404, read.Status);
        Assert.Equal(404, write.Status);
    }

    [Fact]
    public async Task Unread_CountsOtherPeoplesMessagesUntilTheConversationIsRead()
    {
        var space = await NewSpaceAsync();
        var channel = await BoardChannelAsync(space, Asha, space.Hms);
        await space.Chat.SendAsync(As(Asha), channel, Say("Stand-up in five"));
        await space.Chat.SendAsync(As(Asha), channel, Say("Bring the billing numbers"));

        var before = await space.Chat.GetStateAsync(As(Bilal), null, null);
        var mine = await space.Chat.GetStateAsync(As(Asha), null, null);
        await space.Chat.MarkReadAsync(As(Bilal), channel);
        var after = await space.Chat.GetStateAsync(As(Bilal), null, null);

        var row = before.Channels.Single(c => c.ChannelId == channel);
        Assert.Equal(2, row.Unread);
        Assert.Equal("Bring the billing numbers", row.LastMessageText);
        Assert.Equal("Asha Rao", row.LastMessageBy);
        Assert.Equal(0, mine.Channels.Single(c => c.ChannelId == channel).Unread);
        Assert.Equal(0, after.Channels.Single(c => c.ChannelId == channel).Unread);
    }

    [Fact]
    public async Task TheOpenConversation_GetsOnlyMessagesNewerThanTheLastOneSeen()
    {
        var space = await NewSpaceAsync();
        var channel = await BoardChannelAsync(space, Asha, space.Hms);
        var first = await space.Chat.SendAsync(As(Asha), channel, Say("One"));
        await Task.Delay(15);
        await space.Chat.SendAsync(As(Bilal), channel, Say("Two"));

        var state = await space.Chat.GetStateAsync(As(Asha), channel, first.Data!.CreatedAt);

        Assert.Equal("Two", Assert.Single(state.NewMessages).Body);
    }

    [Fact]
    public async Task TaggingSomeone_NotifiesThem_AndReadingTheConversationClearsIt()
    {
        var space = await NewSpaceAsync();
        var channel = await BoardChannelAsync(space, Asha, space.Hms);

        var sent = await space.Chat.SendAsync(As(Asha), channel, Say("@Bilal Khan can you check the tax line?", Bilal));

        Assert.True(sent.Ok);
        Assert.Equal(new List<Guid> { Bilal }, sent.Data!.MentionUserIds);
        var note = Assert.Single(await space.Chat.GetNotificationsAsync(Bilal));
        Assert.Equal("Mention", note.Kind);
        Assert.Equal("Asha Rao", note.ActorName);
        Assert.Equal("1HMS", note.ChannelName);
        Assert.Empty(await space.Chat.GetNotificationsAsync(Chitra));
        Assert.Equal(1, (await space.Chat.GetStateAsync(As(Bilal), null, null)).UnreadNotifications);

        await space.Chat.MarkReadAsync(As(Bilal), channel);
        Assert.Equal(0, (await space.Chat.GetStateAsync(As(Bilal), null, null)).UnreadNotifications);
    }

    [Fact]
    public async Task ADirectMessage_IsOneConversationPerPair_AndNotifiesTheOtherPerson()
    {
        var space = await NewSpaceAsync();

        var fromAsha = await space.Chat.OpenDirectAsync(As(Asha), Bilal);
        var fromBilal = await space.Chat.OpenDirectAsync(As(Bilal), Asha);
        await space.Chat.SendAsync(As(Asha), fromAsha.Data!.ChannelId, Say("Got a minute?"));

        Assert.Equal(fromAsha.Data.ChannelId, fromBilal.Data!.ChannelId);
        Assert.Equal("Bilal Khan", fromAsha.Data.Name);
        Assert.Equal("Asha Rao", fromBilal.Data.Name);
        Assert.Equal("Direct", Assert.Single(await space.Chat.GetNotificationsAsync(Bilal)).Kind);
        // Nobody else can see or read it.
        Assert.DoesNotContain((await space.Chat.GetStateAsync(As(Chitra), null, null)).Channels, c => c.Kind == "Direct");
        Assert.Equal(404, (await space.Chat.GetMessagesAsync(As(Chitra), fromAsha.Data.ChannelId, null, false)).Status);
        Assert.Equal(400, (await space.Chat.OpenDirectAsync(As(Asha), Asha)).Status);
    }

    [Fact]
    public async Task AGroup_IsSeenOnlyByItsPeople_AndOnlyTheyCanBeTagged()
    {
        var space = await NewSpaceAsync();

        var group = await space.Chat.CreateGroupAsync(As(Asha), new CreateWorkChatGroupRequest { Name = " Release crew ", MemberUserIds = { Bilal } });
        var outsiderTag = await space.Chat.SendAsync(As(Asha), group.Data!.ChannelId, Say("@Chitra Das look", Chitra));

        Assert.Equal("Release crew", group.Data.Name);
        Assert.Contains((await space.Chat.GetStateAsync(As(Bilal), null, null)).Channels, c => c.ChannelId == group.Data.ChannelId);
        Assert.DoesNotContain((await space.Chat.GetStateAsync(As(Chitra), null, null)).Channels, c => c.ChannelId == group.Data.ChannelId);
        Assert.Equal(400, outsiderTag.Status);
        Assert.Equal(400, (await space.Chat.CreateGroupAsync(As(Asha), new CreateWorkChatGroupRequest { Name = "Solo" })).Status);
    }

    [Fact]
    public async Task ImportantMessages_CanBeMarkedAndListedOnTheirOwn()
    {
        var space = await NewSpaceAsync();
        var channel = await BoardChannelAsync(space, Asha, space.Hms);
        await space.Chat.SendAsync(As(Asha), channel, Say("Lunch?"));
        var release = await space.Chat.SendAsync(As(Asha), channel, Say("Release is on Friday"));

        await space.Chat.MarkImportantAsync(As(Bilal), release.Data!.MessageId, true);
        var important = await space.Chat.GetMessagesAsync(As(Bilal), channel, null, importantOnly: true);

        Assert.Equal("Release is on Friday", Assert.Single(important.Data!).Body);
    }

    [Fact]
    public async Task EscalatingAWorkItem_PostsInItsBoard_NotifiesThePeople_AndIsWrittenInTheItemsHistory()
    {
        var space = await NewSpaceAsync();

        var nobody = await space.Chat.EscalateItemAsync(As(Asha), space.HmsItem, new EscalateWorkItemRequest());
        var sent = await space.Chat.EscalateItemAsync(As(Asha), space.HmsItem, new EscalateWorkItemRequest { UserIds = { Bilal }, Note = "Customer is waiting" });

        Assert.Equal(400, nobody.Status);
        Assert.True(sent.Ok);
        Assert.True(sent.Data!.IsEscalation);
        Assert.True(sent.Data.IsImportant);
        Assert.Equal("HMS-1", sent.Data.WorkItemKey);
        Assert.Equal(await BoardChannelAsync(space, Asha, space.Hms), sent.Data.ChannelId);
        Assert.Equal("Escalation", Assert.Single(await space.Chat.GetNotificationsAsync(Bilal)).Kind);
        var history = await space.Db.WorkItemHistories.SingleAsync(h => h.WorkItemId == space.HmsItem && h.Field == "escalated");
        Assert.Equal("Bilal Khan", history.NewValue);
        // An escalation cannot be quietly un-marked.
        Assert.Equal(400, (await space.Chat.MarkImportantAsync(As(Bilal), sent.Data.MessageId, false)).Status);
    }

    [Fact]
    public async Task RemovingAMessage_IsForItsAuthorOrSomeoneWithTheDeletePermission_AndLeavesAnEmptyPlace()
    {
        var space = await NewSpaceAsync();
        var channel = await BoardChannelAsync(space, Asha, space.Hms);
        var sent = await space.Chat.SendAsync(As(Asha), channel, Say("Wrong chat, sorry"));

        var byOther = await space.Chat.DeleteMessageAsync(As(Bilal), sent.Data!.MessageId, canRemoveAny: false);
        var byAuthor = await space.Chat.DeleteMessageAsync(As(Asha), sent.Data.MessageId, canRemoveAny: false);
        var listed = await space.Chat.GetMessagesAsync(As(Bilal), channel, null, false);

        Assert.Equal(403, byOther.Status);
        Assert.True(byAuthor.Ok);
        var gone = Assert.Single(listed.Data!);
        Assert.True(gone.IsDeleted);
        Assert.Equal(string.Empty, gone.Body);
    }

    [Fact]
    public async Task AnEmptyOrOverlongMessage_IsRefused()
    {
        var space = await NewSpaceAsync();
        var channel = await BoardChannelAsync(space, Asha, space.Hms);

        Assert.Equal(400, (await space.Chat.SendAsync(As(Asha), channel, Say("   "))).Status);
        Assert.Equal(400, (await space.Chat.SendAsync(As(Asha), channel, Say(new string('x', 4001)))).Status);
    }
}
