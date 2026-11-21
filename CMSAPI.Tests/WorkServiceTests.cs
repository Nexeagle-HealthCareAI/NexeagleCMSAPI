#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using CMSAPI.Data.Repositories;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Daily Work Board: the hierarchy rules (epic -> feature -> story/task/bug -> subtask), item numbering,
// what is written to an item's history, and who may remove a comment.
public class WorkServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();

    private sealed class Board
    {
        public CmsDbContext Db = null!;
        public WorkService Service = null!;
        public Guid ProjectId;
    }

    private static async Task<Board> NewBoardAsync()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.CmsUsers.Add(new CmsUser { UserId = Asha, FullName = "Asha Rao", Email = "asha@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsUsers.Add(new CmsUser { UserId = Bilal, FullName = "Bilal Khan", Email = "bilal@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        var project = new WorkProject { ProjectId = Guid.NewGuid(), Key = "HMS", Name = "1HMS", NextNumber = 1, CreatedAt = DateTime.UtcNow };
        db.WorkProjects.Add(project);
        await db.SaveChangesAsync();

        return new Board { Db = db, Service = new WorkService(new WorkRepository(db)), ProjectId = project.ProjectId };
    }

    private static CreateWorkItemRequest New(Board board, string type, string title, Guid? parentId = null) =>
        new CreateWorkItemRequest { ProjectId = board.ProjectId, ItemType = type, Title = title, ParentId = parentId };

    private static UpdateWorkItemRequest Same(WorkItemDetailDto item) => new UpdateWorkItemRequest
    {
        Title = item.Title,
        Description = item.Description,
        Status = item.Status,
        Priority = item.Priority,
        StoryPoints = item.StoryPoints,
        AssigneeUserId = item.AssigneeUserId,
        ParentId = item.ParentId,
        SprintId = item.SprintId,
        Labels = item.Labels,
        DueDate = item.DueDate,
        IsBlocked = item.IsBlocked,
        BlockedReason = item.BlockedReason,
    };

    // ── Hierarchy rules ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Epic", null, true)]
    [InlineData("Epic", "Epic", false)]
    [InlineData("Feature", "Epic", true)]
    [InlineData("Feature", "Story", false)]
    [InlineData("Feature", null, true)]
    [InlineData("Story", "Feature", true)]
    [InlineData("Story", "Epic", true)]
    [InlineData("Task", "Story", false)]
    [InlineData("Bug", null, true)]
    [InlineData("Subtask", "Task", true)]
    [InlineData("Subtask", "Bug", true)]
    [InlineData("Subtask", "Epic", false)]
    [InlineData("Subtask", null, false)]
    public void ParentProblem_FollowsTheHierarchy(string child, string? parent, bool allowed)
    {
        Assert.Equal(allowed, WorkRules.ParentProblem(child, parent) == null);
    }

    [Fact]
    public void CleanLabels_TrimsLowersAndRemovesRepeats()
    {
        var labels = WorkRules.CleanLabels(new[] { " Billing ", "billing", "", "ABDM" });
        Assert.Equal(new List<string> { "billing", "abdm" }, labels);
    }

    // ── Creating ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateItem_NumbersItemsInOrderWithTheProjectKey()
    {
        var board = await NewBoardAsync();

        var first = await board.Service.CreateItemAsync(New(board, "Epic", "Billing"), Asha);
        var second = await board.Service.CreateItemAsync(New(board, "Task", "Fix a typo"), Asha);

        Assert.True(first.Ok);
        Assert.Equal("HMS-1", first.Data!.Key);
        Assert.Equal("HMS-2", second.Data!.Key);
        Assert.Equal("ToDo", first.Data.Status);
        Assert.Equal("Asha Rao", first.Data.ReporterName);
        Assert.Contains(first.Data.History, h => h.Field == "created");
    }

    [Fact]
    public async Task CreateItem_SubtaskWithoutParent_IsRefused()
    {
        var board = await NewBoardAsync();

        var result = await board.Service.CreateItemAsync(New(board, "Subtask", "Write the test"), Asha);

        Assert.False(result.Ok);
        Assert.Equal(400, result.Status);
    }

    [Fact]
    public async Task CreateItem_StoryUnderTask_IsRefused()
    {
        var board = await NewBoardAsync();
        var task = await board.Service.CreateItemAsync(New(board, "Task", "A task"), Asha);

        var result = await board.Service.CreateItemAsync(New(board, "Story", "A story", task.Data!.WorkItemId), Asha);

        Assert.False(result.Ok);
        Assert.Equal(400, result.Status);
    }

    [Fact]
    public async Task CreateItem_ParentFromAnotherProject_IsRefused()
    {
        var board = await NewBoardAsync();
        var other = new WorkProject { ProjectId = Guid.NewGuid(), Key = "RAD", Name = "1Rad", NextNumber = 1, CreatedAt = DateTime.UtcNow };
        board.Db.WorkProjects.Add(other);
        await board.Db.SaveChangesAsync();
        var epic = await board.Service.CreateItemAsync(
            new CreateWorkItemRequest { ProjectId = other.ProjectId, ItemType = "Epic", Title = "Viewer" }, Asha);

        var result = await board.Service.CreateItemAsync(New(board, "Feature", "Zoom", epic.Data!.WorkItemId), Asha);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task GetItems_CountsChildrenAndDoneChildren()
    {
        var board = await NewBoardAsync();
        var story = await board.Service.CreateItemAsync(New(board, "Story", "Book an appointment"), Asha);
        var one = await board.Service.CreateItemAsync(New(board, "Subtask", "Screen", story.Data!.WorkItemId), Asha);
        await board.Service.CreateItemAsync(New(board, "Subtask", "API", story.Data.WorkItemId), Asha);
        var done = Same(one.Data!);
        done.Status = "Done";
        await board.Service.UpdateItemAsync(one.Data!.WorkItemId, done, Asha);

        var list = await board.Service.GetItemsAsync(board.ProjectId);

        var row = list.Data!.Single(i => i.WorkItemId == story.Data.WorkItemId);
        Assert.Equal(2, row.ChildCount);
        Assert.Equal(1, row.DoneChildCount);
        Assert.Equal("HMS-1", list.Data!.Single(i => i.Title == "API").ParentKey);
    }

    // ── Changing ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateItem_WritesOneHistoryRowPerChangedField()
    {
        var board = await NewBoardAsync();
        var created = await board.Service.CreateItemAsync(New(board, "Bug", "Bill total is wrong"), Asha);
        var change = Same(created.Data!);
        change.Status = "InProgress";
        change.AssigneeUserId = Bilal;

        var result = await board.Service.UpdateItemAsync(created.Data!.WorkItemId, change, Asha);

        Assert.True(result.Ok);
        Assert.Equal("InProgress", result.Data!.Status);
        Assert.Equal("Bilal Khan", result.Data.AssigneeName);
        var status = result.Data.History.Single(h => h.Field == "status");
        Assert.Equal("ToDo", status.OldValue);
        Assert.Equal("InProgress", status.NewValue);
        Assert.Equal("Bilal Khan", result.Data.History.Single(h => h.Field == "assignee").NewValue);
        Assert.Equal(3, result.Data.History.Count); // created + status + assignee
    }

    [Fact]
    public async Task UpdateItem_NothingChanged_WritesNoHistory()
    {
        var board = await NewBoardAsync();
        var created = await board.Service.CreateItemAsync(New(board, "Task", "Tidy up"), Asha);

        var result = await board.Service.UpdateItemAsync(created.Data!.WorkItemId, Same(created.Data), Bilal);

        Assert.True(result.Ok);
        Assert.Single(result.Data!.History);
    }

    [Fact]
    public async Task UpdateItem_DoneSetsCompletedAt_AndReopeningClearsIt()
    {
        var board = await NewBoardAsync();
        var created = await board.Service.CreateItemAsync(New(board, "Task", "Ship it"), Asha);
        var done = Same(created.Data!);
        done.Status = "Done";

        var finished = await board.Service.UpdateItemAsync(created.Data!.WorkItemId, done, Asha);
        Assert.NotNull(finished.Data!.CompletedAt);

        var reopen = Same(finished.Data);
        reopen.Status = "InProgress";
        var reopened = await board.Service.UpdateItemAsync(created.Data.WorkItemId, reopen, Asha);
        Assert.Null(reopened.Data!.CompletedAt);
    }

    [Fact]
    public async Task UpdateItem_BlockedWithoutAReason_IsRefused()
    {
        var board = await NewBoardAsync();
        var created = await board.Service.CreateItemAsync(New(board, "Task", "Deploy"), Asha);
        var change = Same(created.Data!);
        change.IsBlocked = true;
        change.BlockedReason = "  ";

        var result = await board.Service.UpdateItemAsync(created.Data!.WorkItemId, change, Asha);

        Assert.False(result.Ok);
        Assert.Equal(400, result.Status);
    }

    [Fact]
    public async Task UpdateItem_MovingAStoryUnderASubtask_IsRefused()
    {
        var board = await NewBoardAsync();
        var task = await board.Service.CreateItemAsync(New(board, "Task", "A task"), Asha);
        var sub = await board.Service.CreateItemAsync(New(board, "Subtask", "A step", task.Data!.WorkItemId), Asha);
        var story = await board.Service.CreateItemAsync(New(board, "Story", "A story"), Asha);
        var change = Same(story.Data!);
        change.ParentId = sub.Data!.WorkItemId;

        var result = await board.Service.UpdateItemAsync(story.Data!.WorkItemId, change, Asha);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task UpdateItem_BadDueDate_IsRefused()
    {
        var board = await NewBoardAsync();
        var created = await board.Service.CreateItemAsync(New(board, "Task", "Plan"), Asha);
        var change = Same(created.Data!);
        change.DueDate = "31/12/2026";

        var result = await board.Service.UpdateItemAsync(created.Data!.WorkItemId, change, Asha);

        Assert.False(result.Ok);
    }

    // ── Deleting ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteItem_WithChildren_IsRefused_ThenWorksOnceTheyAreGone()
    {
        var board = await NewBoardAsync();
        var task = await board.Service.CreateItemAsync(New(board, "Task", "A task"), Asha);
        var sub = await board.Service.CreateItemAsync(New(board, "Subtask", "A step", task.Data!.WorkItemId), Asha);

        var refused = await board.Service.DeleteItemAsync(task.Data.WorkItemId, Asha);
        Assert.False(refused.Ok);
        Assert.Equal(409, refused.Status);

        Assert.True((await board.Service.DeleteItemAsync(sub.Data!.WorkItemId, Asha)).Ok);
        Assert.True((await board.Service.DeleteItemAsync(task.Data.WorkItemId, Asha)).Ok);
        Assert.Empty((await board.Service.GetItemsAsync(board.ProjectId)).Data!);
        Assert.Equal(404, (await board.Service.GetItemAsync(task.Data.WorkItemId)).Status);
    }

    // ── Comments ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Comments_OnlyTheAuthorOrAManagerCanRemoveOne()
    {
        var board = await NewBoardAsync();
        var task = await board.Service.CreateItemAsync(New(board, "Task", "Review"), Asha);
        var comment = await board.Service.AddCommentAsync(task.Data!.WorkItemId, new AddWorkCommentRequest { Body = "Looks good" }, Asha);
        Assert.Equal("Asha Rao", comment.Data!.AuthorName);

        var byOther = await board.Service.DeleteCommentAsync(comment.Data.CommentId, Bilal, canManage: false);
        Assert.Equal(403, byOther.Status);

        var byManager = await board.Service.DeleteCommentAsync(comment.Data.CommentId, Bilal, canManage: true);
        Assert.True(byManager.Ok);
        Assert.Empty((await board.Service.GetItemAsync(task.Data.WorkItemId)).Data!.Comments);
    }

    [Fact]
    public async Task AddComment_Empty_IsRefused()
    {
        var board = await NewBoardAsync();
        var task = await board.Service.CreateItemAsync(New(board, "Task", "Review"), Asha);

        var result = await board.Service.AddCommentAsync(task.Data!.WorkItemId, new AddWorkCommentRequest { Body = "   " }, Asha);

        Assert.False(result.Ok);
    }

    // ── Projects ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateProject_UpperCasesTheKey_AndRefusesARepeat()
    {
        var board = await NewBoardAsync();

        var created = await board.Service.CreateProjectAsync(new CreateWorkProjectRequest { Key = "lab", Name = "1Lab" }, Asha);
        Assert.True(created.Ok);
        Assert.Equal("LAB", created.Data!.Key);

        var repeat = await board.Service.CreateProjectAsync(new CreateWorkProjectRequest { Key = "HMS", Name = "Again" }, Asha);
        Assert.Equal(409, repeat.Status);

        var bad = await board.Service.CreateProjectAsync(new CreateWorkProjectRequest { Key = "H1", Name = "Bad key" }, Asha);
        Assert.Equal(400, bad.Status);
    }
}
