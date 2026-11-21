#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Daily Work Board: "last seen", a person's own sign-in details, and the activity log.
public class WorkActivityServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();

    private sealed class Space
    {
        public CmsDbContext Db = null!;
        public WorkActivityService Service = null!;
        public WorkAccessService Access = null!;
        public Guid Hms;
        public Guid Rad;
    }

    private static WorkActor As(Guid userId, bool seesAll = false) => new WorkActor { UserId = userId, SeesAllBoards = seesAll };

    private static async Task<Space> NewSpaceAsync()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.CmsUsers.Add(new CmsUser { UserId = Asha, FullName = "Asha Rao", Email = "asha@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsUsers.Add(new CmsUser { UserId = Bilal, FullName = "Bilal Khan", Email = "bilal@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        var hms = new WorkProject { ProjectId = Guid.NewGuid(), Key = "HMS", Name = "1HMS", NextNumber = 2, CreatedAt = DateTime.UtcNow };
        var rad = new WorkProject { ProjectId = Guid.NewGuid(), Key = "RAD", Name = "1Rad", NextNumber = 2, CreatedAt = DateTime.UtcNow };
        db.WorkProjects.AddRange(hms, rad);

        var t = DateTime.UtcNow.AddHours(-5);
        var hmsItem = new WorkItem { WorkItemId = Guid.NewGuid(), ProjectId = hms.ProjectId, Number = 1, ItemType = "Task", Title = "Audit trail", Status = "ToDo", Priority = "Medium", CreatedAt = t, UpdatedAt = t };
        var radItem = new WorkItem { WorkItemId = Guid.NewGuid(), ProjectId = rad.ProjectId, Number = 1, ItemType = "Bug", Title = "Viewer crash", Status = "ToDo", Priority = "High", CreatedAt = t, UpdatedAt = t };
        db.WorkItems.AddRange(hmsItem, radItem);
        db.WorkItemHistories.Add(new WorkItemHistory { HistoryId = Guid.NewGuid(), WorkItemId = hmsItem.WorkItemId, ActorUserId = Asha, Field = "created", NewValue = "Task", CreatedAt = t });
        db.WorkItemHistories.Add(new WorkItemHistory { HistoryId = Guid.NewGuid(), WorkItemId = hmsItem.WorkItemId, ActorUserId = Bilal, Field = "status", OldValue = "To do", NewValue = "In progress", CreatedAt = t.AddHours(1) });
        db.WorkItemHistories.Add(new WorkItemHistory { HistoryId = Guid.NewGuid(), WorkItemId = hmsItem.WorkItemId, ActorUserId = Bilal, Field = "description", OldValue = "old secret text", NewValue = "new secret text", CreatedAt = t.AddHours(2) });
        db.WorkItemHistories.Add(new WorkItemHistory { HistoryId = Guid.NewGuid(), WorkItemId = radItem.WorkItemId, ActorUserId = Asha, Field = "created", NewValue = "Bug", CreatedAt = t.AddHours(3) });
        db.WorkItemComments.Add(new WorkItemComment { CommentId = Guid.NewGuid(), WorkItemId = hmsItem.WorkItemId, AuthorUserId = Asha, Body = "Looks right", CreatedAt = t.AddHours(4) });
        db.DocAccessLogs.Add(new DocAccessLog { LogId = Guid.NewGuid(), UserId = Bilal, Action = "download", Detail = "Offer letter.pdf", CreatedAt = t.AddHours(4.5) });
        await db.SaveChangesAsync();

        var access = new WorkAccessService(db);
        return new Space { Db = db, Access = access, Service = new WorkActivityService(db, access), Hms = hms.ProjectId, Rad = rad.ProjectId };
    }

    [Fact]
    public async Task OpeningTheBoard_IsLogged_AndTheNextVisitShowsThePreviousOne()
    {
        var space = await NewSpaceAsync();

        var first = await space.Service.TouchAsync(Asha, new WorkPresenceRequest { Opened = true }, "10.0.0.5", "Chrome");
        await space.Service.TouchAsync(Asha, new WorkPresenceRequest(), "10.0.0.5", "Chrome");
        var second = await space.Service.TouchAsync(Asha, new WorkPresenceRequest { Opened = true }, "10.0.0.9", "Safari");

        Assert.Null(first.PreviousOpenedAt);
        Assert.Equal("10.0.0.5", first.Ip);
        Assert.Equal(first.OpenedAt, second.PreviousOpenedAt);
        Assert.Equal("10.0.0.5", second.PreviousIp);
        Assert.Equal("10.0.0.9", second.Ip);
        // Two visits; the heartbeat in between is not a visit.
        Assert.Equal(2, await space.Db.WorkSessionLogs.CountAsync(l => l.Kind == "Opened"));
    }

    [Fact]
    public async Task WhatTheProtectedScreenStopped_IsLogged_AndUnknownEventsAreNot()
    {
        var space = await NewSpaceAsync();

        await space.Service.TouchAsync(Bilal, new WorkPresenceRequest { Event = "CopyBlocked" }, "10.0.0.7", "Chrome");
        await space.Service.TouchAsync(Bilal, new WorkPresenceRequest { Event = "Anything" }, "10.0.0.7", "Chrome");

        var log = await space.Service.GetActivityAsync(new WorkActivityQuery { Source = "sessions" }, As(Asha), includeDocs: false);
        var only = Assert.Single(log);
        Assert.Equal("CopyBlocked", only.Action);
        Assert.Equal("Bilal Khan", only.ActorName);
        Assert.Contains("10.0.0.7", only.Detail);
    }

    [Fact]
    public async Task LastSeen_SaysWhoIsOnTheBoardNow()
    {
        var space = await NewSpaceAsync();
        await space.Service.TouchAsync(Asha, new WorkPresenceRequest(), null, null);
        space.Db.WorkPresences.Add(new WorkPresence { UserId = Bilal, LastSeenAt = DateTime.UtcNow.AddHours(-3) });
        await space.Db.SaveChangesAsync();

        var seen = await space.Service.GetLastSeenAsync();

        Assert.True(seen.Single(s => s.UserId == Asha).IsOnline);
        Assert.False(seen.Single(s => s.UserId == Bilal).IsOnline);
    }

    [Fact]
    public async Task TheLog_IsNewestFirst_NamesPeopleAndItems_AndNeverRepeatsADescription()
    {
        var space = await NewSpaceAsync();

        var log = await space.Service.GetActivityAsync(new WorkActivityQuery(), As(Asha), includeDocs: true);

        Assert.Equal(log.OrderByDescending(a => a.CreatedAt).Select(a => a.CreatedAt), log.Select(a => a.CreatedAt));
        Assert.Equal("docs", log[0].Source);
        var status = log.Single(a => a.Action == "status");
        Assert.Equal("Bilal Khan", status.ActorName);
        Assert.Equal("HMS-1", status.ItemKey);
        Assert.Equal("In progress", status.NewValue);
        var description = log.Single(a => a.Action == "description");
        Assert.Null(description.OldValue);
        Assert.Null(description.NewValue);
        Assert.Contains(log, a => a.Source == "comments" && a.Action == "commented" && a.ActorName == "Asha Rao");
    }

    [Fact]
    public async Task TheLog_CanBeNarrowedToAPersonABoardAndAKind()
    {
        var space = await NewSpaceAsync();

        var bilal = await space.Service.GetActivityAsync(new WorkActivityQuery { UserId = Bilal }, As(Asha), includeDocs: true);
        var rad = await space.Service.GetActivityAsync(new WorkActivityQuery { ProjectId = space.Rad }, As(Asha), includeDocs: true);
        var comments = await space.Service.GetActivityAsync(new WorkActivityQuery { Source = "comments" }, As(Asha), includeDocs: true);

        Assert.All(bilal, a => Assert.Equal(Bilal, a.ActorUserId));
        Assert.Equal(3, bilal.Count);
        Assert.Equal("RAD-1", Assert.Single(rad).ItemKey);
        Assert.Equal("comments", Assert.Single(comments).Source);
    }

    [Fact]
    public async Task TheLog_LeavesOutDocumentsWithoutThatPermission_AndBoardsTheReaderCannotOpen()
    {
        var space = await NewSpaceAsync();
        await space.Access.SaveAccessAsync(space.Hms, new SaveWorkProjectAccessRequest { IsRestricted = true, MemberUserIds = { Bilal } }, Bilal);

        var asAsha = await space.Service.GetActivityAsync(new WorkActivityQuery(), As(Asha), includeDocs: false);
        var hmsForAsha = await space.Service.GetActivityAsync(new WorkActivityQuery { ProjectId = space.Hms }, As(Asha), includeDocs: false);
        var asManager = await space.Service.GetActivityAsync(new WorkActivityQuery(), As(Asha, seesAll: true), includeDocs: false);

        Assert.DoesNotContain(asAsha, a => a.Source == "docs");
        Assert.DoesNotContain(asAsha, a => a.ProjectKey == "HMS");
        Assert.Contains(asAsha, a => a.ItemKey == "RAD-1");
        Assert.Empty(hmsForAsha);
        Assert.Contains(asManager, a => a.ProjectKey == "HMS" && a.Action == "MemberAdded" && a.Detail == "Bilal Khan");
    }

    [Fact]
    public async Task TheLog_PagesBackwardsWithBefore()
    {
        var space = await NewSpaceAsync();

        var first = await space.Service.GetActivityAsync(new WorkActivityQuery { Take = 2 }, As(Asha), includeDocs: true);
        var next = await space.Service.GetActivityAsync(new WorkActivityQuery { Take = 2, Before = first.Last().CreatedAt }, As(Asha), includeDocs: true);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, next.Count);
        Assert.True(next[0].CreatedAt < first.Last().CreatedAt);
    }
}
