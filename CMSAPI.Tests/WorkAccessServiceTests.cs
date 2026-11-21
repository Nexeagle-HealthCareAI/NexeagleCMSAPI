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

// Daily Work Board: who can open a board once it is members-only, and the log of access changes.
public class WorkAccessServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();
    private static readonly Guid Chitra = Guid.NewGuid();

    private sealed class Space
    {
        public CmsDbContext Db = null!;
        public WorkAccessService Service = null!;
        public Guid Hms;
        public Guid Rad;
        public Guid HmsItem;
        public Guid HmsStatus;
        public Guid HmsComment;
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
        db.CmsUsers.Add(new CmsUser { UserId = Chitra, FullName = "Chitra Das", Email = "chitra@example.com", IsActive = false, CreatedAt = DateTime.UtcNow });
        var hms = new WorkProject { ProjectId = Guid.NewGuid(), Key = "HMS", Name = "1HMS", NextNumber = 2, CreatedAt = DateTime.UtcNow };
        var rad = new WorkProject { ProjectId = Guid.NewGuid(), Key = "RAD", Name = "1Rad", NextNumber = 1, CreatedAt = DateTime.UtcNow };
        db.WorkProjects.AddRange(hms, rad);
        var status = new WorkStatus { StatusId = Guid.NewGuid(), ProjectId = hms.ProjectId, Key = "ToDo", Name = "To do", Category = "ToDo", SortOrder = 10 };
        db.WorkStatuses.Add(status);
        var item = new WorkItem { WorkItemId = Guid.NewGuid(), ProjectId = hms.ProjectId, Number = 1, ItemType = "Task", Title = "Audit trail", Status = "ToDo", Priority = "Medium", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.WorkItems.Add(item);
        var comment = new WorkItemComment { CommentId = Guid.NewGuid(), WorkItemId = item.WorkItemId, AuthorUserId = Asha, Body = "Note", CreatedAt = DateTime.UtcNow };
        db.WorkItemComments.Add(comment);
        await db.SaveChangesAsync();
        return new Space
        {
            Db = db, Service = new WorkAccessService(db), Hms = hms.ProjectId, Rad = rad.ProjectId,
            HmsItem = item.WorkItemId, HmsStatus = status.StatusId, HmsComment = comment.CommentId,
        };
    }

    private static SaveWorkProjectAccessRequest Access(bool restricted, params Guid[] members) =>
        new SaveWorkProjectAccessRequest { IsRestricted = restricted, MemberUserIds = members.ToList() };

    [Fact]
    public async Task AnOpenBoard_IsOpenToEveryone()
    {
        var space = await NewSpaceAsync();

        Assert.True(await space.Service.CanOpenProjectAsync(space.Hms, As(Bilal)));
        Assert.Empty(await space.Service.ClosedProjectIdsAsync(As(Bilal)));
    }

    [Fact]
    public async Task AMembersOnlyBoard_IsClosedToEveryoneElse_ItemsColumnsAndCommentsIncluded()
    {
        var space = await NewSpaceAsync();
        await space.Service.SaveAccessAsync(space.Hms, Access(true, Asha), Asha);

        Assert.True(await space.Service.CanOpenProjectAsync(space.Hms, As(Asha)));
        Assert.False(await space.Service.CanOpenProjectAsync(space.Hms, As(Bilal)));
        Assert.False(await space.Service.CanOpenItemAsync(space.HmsItem, As(Bilal)));
        Assert.False(await space.Service.CanOpenStatusAsync(space.HmsStatus, As(Bilal)));
        Assert.False(await space.Service.CanOpenCommentAsync(space.HmsComment, As(Bilal)));
        Assert.Equal(new HashSet<Guid> { space.Hms }, await space.Service.ClosedProjectIdsAsync(As(Bilal)));
        // The other board is untouched.
        Assert.True(await space.Service.CanOpenProjectAsync(space.Rad, As(Bilal)));
    }

    [Fact]
    public async Task SomeoneWhoManagesMembers_OpensEveryBoard()
    {
        var space = await NewSpaceAsync();
        await space.Service.SaveAccessAsync(space.Hms, Access(true), Asha);

        Assert.True(await space.Service.CanOpenItemAsync(space.HmsItem, As(Bilal, seesAll: true)));
        Assert.Empty(await space.Service.ClosedProjectIdsAsync(As(Bilal, seesAll: true)));
    }

    [Fact]
    public async Task RemovingAMember_ClosesTheBoardToThem_AndIsLogged()
    {
        var space = await NewSpaceAsync();
        await space.Service.SaveAccessAsync(space.Hms, Access(true, Asha, Bilal), Asha);

        var saved = await space.Service.SaveAccessAsync(space.Hms, Access(true, Asha), Asha);

        Assert.True(saved.Ok);
        Assert.Equal(new List<Guid> { Asha }, saved.Data!.MemberUserIds);
        Assert.False(await space.Service.CanOpenProjectAsync(space.Hms, As(Bilal)));
        Assert.Contains(saved.Data.Log, l => l.Action == "MemberRemoved" && l.TargetName == "Bilal Khan" && l.ActorName == "Asha Rao");
        Assert.Contains(saved.Data.Log, l => l.Action == "Restricted");
        Assert.Equal(2, saved.Data.Log.Count(l => l.Action == "MemberAdded"));
    }

    [Fact]
    public async Task OpeningTheBoardAgain_LetsEveryoneIn_AndKeepsTheMemberList()
    {
        var space = await NewSpaceAsync();
        await space.Service.SaveAccessAsync(space.Hms, Access(true, Asha), Asha);

        var saved = await space.Service.SaveAccessAsync(space.Hms, Access(false, Asha), Asha);

        Assert.False(saved.Data!.IsRestricted);
        Assert.True(await space.Service.CanOpenProjectAsync(space.Hms, As(Bilal)));
        Assert.Equal(new List<Guid> { Asha }, saved.Data.MemberUserIds);
        Assert.Contains(saved.Data.Log, l => l.Action == "Opened");
    }

    [Fact]
    public async Task SomeoneWhoIsNotAnActiveUser_CannotBeAdded()
    {
        var space = await NewSpaceAsync();

        var inactive = await space.Service.SaveAccessAsync(space.Hms, Access(true, Chitra), Asha);
        var unknown = await space.Service.SaveAccessAsync(space.Hms, Access(true, Guid.NewGuid()), Asha);

        Assert.Equal(400, inactive.Status);
        Assert.Equal(400, unknown.Status);
        Assert.False((await space.Service.GetAccessAsync(space.Hms)).Data!.IsRestricted);
    }

    [Fact]
    public async Task AnIdThatDoesNotExist_IsLeftToTheCallersOwnNotFound()
    {
        var space = await NewSpaceAsync();

        Assert.True(await space.Service.CanOpenItemAsync(Guid.NewGuid(), As(Bilal)));
        Assert.Equal(404, (await space.Service.GetAccessAsync(Guid.NewGuid())).Status);
    }
}
