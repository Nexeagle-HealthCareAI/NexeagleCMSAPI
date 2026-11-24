#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Daily Work Board: tagging people (@Name) in a work item's comments, description and blocked
// reason, who can be tagged, and the notifications that follow.
public class WorkMentionServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();
    private static readonly Guid Chitra = Guid.NewGuid();
    private static readonly Guid Gone = Guid.NewGuid();

    private sealed class Space
    {
        public CmsDbContext Db = null!;
        public WorkMentionService Mentions = null!;
        public Guid Item;
        public Guid Project;
        public Guid Comment;
    }

    private static WorkActor As(Guid userId) => new WorkActor { UserId = userId };
    private static List<Guid> Ids(params Guid[] ids) => ids.ToList();

    private static async Task<Space> NewSpaceAsync(bool membersOnly = false)
    {
        var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        void User(Guid id, string name, bool active = true) =>
            db.CmsUsers.Add(new CmsUser { UserId = id, FullName = name, Email = name.Replace(" ", ".") + "@example.com", IsActive = active, CreatedAt = DateTime.UtcNow });
        User(Asha, "Asha Rao");
        User(Bilal, "Bilal Khan");
        User(Chitra, "Chitra Das");
        User(Gone, "Gita Former", active: false);
        var project = new WorkProject { ProjectId = Guid.NewGuid(), Key = "HMS", Name = "1HMS", NextNumber = 8, IsRestricted = membersOnly, CreatedAt = DateTime.UtcNow };
        db.WorkProjects.Add(project);
        if (membersOnly)
        {
            db.WorkProjectMembers.Add(new WorkProjectMember { ProjectId = project.ProjectId, UserId = Asha, AddedAt = DateTime.UtcNow });
            db.WorkProjectMembers.Add(new WorkProjectMember { ProjectId = project.ProjectId, UserId = Bilal, AddedAt = DateTime.UtcNow });
        }
        var item = new WorkItem { WorkItemId = Guid.NewGuid(), ProjectId = project.ProjectId, Number = 7, ItemType = "Story", Title = "Bill total is wrong", Status = "ToDo", Priority = "High", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.WorkItems.Add(item);
        var comment = new WorkItemComment { CommentId = Guid.NewGuid(), WorkItemId = item.WorkItemId, AuthorUserId = Asha, Body = "@Bilal Khan can you check the GST line?", CreatedAt = DateTime.UtcNow };
        db.WorkItemComments.Add(comment);
        await db.SaveChangesAsync();
        return new Space { Db = db, Mentions = new WorkMentionService(db, new WorkAccessService(db)), Item = item.WorkItemId, Project = project.ProjectId, Comment = comment.CommentId };
    }

    [Fact]
    public async Task OnAnOpenBoard_EveryActivePersonButMe_CanBeTagged()
    {
        var space = await NewSpaceAsync();
        var who = await space.Mentions.TaggableAsync(space.Item, Asha);
        Assert.Equal(new[] { Bilal, Chitra }.OrderBy(x => x), who.OrderBy(x => x));
    }

    [Fact]
    public async Task OnAMembersOnlyBoard_OnlyItsMembers()
    {
        var space = await NewSpaceAsync(membersOnly: true);
        Assert.Equal(new[] { Bilal }, await space.Mentions.TaggableAsync(space.Item, Asha));
    }

    [Fact]
    public async Task TaggingInAComment_TellsThePerson_WithTheWordsAroundIt()
    {
        var space = await NewSpaceAsync();

        var tagged = await space.Mentions.TagCommentAsync(space.Item, space.Comment, "@Bilal Khan can you check the GST line?", Ids(Bilal, Asha), Asha);

        Assert.Equal(new[] { Bilal }, tagged);   // never yourself
        var notes = await space.Mentions.GetNotificationsAsync(As(Bilal));
        var note = Assert.Single(notes);
        Assert.Equal(("comment", "HMS-7", "Asha Rao"), (note.Place, note.ItemKey, note.ActorName));
        Assert.Equal("@Bilal Khan can you check the GST line?", note.Text);
        Assert.Empty(await space.Mentions.GetNotificationsAsync(As(Chitra)));
    }

    [Fact]
    public async Task ATagWhoseNameIsNotInTheText_OrWhoCannotOpenTheBoard_IsLeftOut()
    {
        var space = await NewSpaceAsync(membersOnly: true);

        var tagged = await space.Mentions.TagCommentAsync(space.Item, space.Comment, "@Bilal Khan and @Chitra Das please look", Ids(Bilal, Chitra), Asha);
        Assert.Equal(new[] { Bilal }, tagged);   // Chitra is not a member

        var notInText = await space.Mentions.TagCommentAsync(space.Item, space.Comment, "please look", Ids(Bilal), Asha);
        Assert.Empty(notInText);
        Assert.Empty(await space.Mentions.TagCommentAsync(space.Item, space.Comment, "@Gita Former", Ids(Gone), Asha));
    }

    [Fact]
    public async Task EditingTheDescription_TellsOnlyThoseNewlyTagged_AndForgetsTheRemoved()
    {
        var space = await NewSpaceAsync();

        await space.Mentions.TagTextAsync(space.Item, WorkMentionPlaces.Description, "## Steps\n**@Bilal Khan** to fix the total", Ids(Bilal), Asha);
        await space.Mentions.TagTextAsync(space.Item, WorkMentionPlaces.Description, "@Bilal Khan to fix, @Chitra Das to test", Ids(Bilal, Chitra), Asha);
        await space.Mentions.TagTextAsync(space.Item, WorkMentionPlaces.Description, "@Chitra Das to test", Ids(Chitra), Asha);

        Assert.Single(await space.Mentions.GetNotificationsAsync(As(Bilal)));
        Assert.Equal("Steps @Bilal Khan to fix the total", (await space.Mentions.GetNotificationsAsync(As(Bilal)))[0].Text);
        Assert.Single(await space.Mentions.GetNotificationsAsync(As(Chitra)));
        var now = await space.Db.WorkItemMentions.Where(m => m.Place == "description").Select(m => m.UserId).ToListAsync();
        Assert.Equal(new[] { Chitra }, now);
    }

    [Fact]
    public async Task LeavingTheTagsOut_ChangesNothing()
    {
        var space = await NewSpaceAsync();
        await space.Mentions.TagTextAsync(space.Item, WorkMentionPlaces.Blocked, "Waiting on @Bilal Khan", Ids(Bilal), Asha);

        await space.Mentions.TagTextAsync(space.Item, WorkMentionPlaces.Blocked, "Waiting on @Bilal Khan", null, Asha);

        Assert.Equal(1, await space.Db.WorkItemMentions.CountAsync(m => m.Place == "blocked"));
        Assert.Equal("blocked", (await space.Mentions.GetNotificationsAsync(As(Bilal))).Single().Place);
    }

    [Fact]
    public async Task ARemovedComment_SaysSo_AndIsNoLongerUnread()
    {
        var space = await NewSpaceAsync();
        await space.Mentions.TagCommentAsync(space.Item, space.Comment, "@Bilal Khan can you check the GST line?", Ids(Bilal), Asha);
        var comment = await space.Db.WorkItemComments.SingleAsync();
        comment.IsDeleted = true;
        await space.Db.SaveChangesAsync();

        await space.Mentions.CommentRemovedAsync(space.Comment);

        var note = (await space.Mentions.GetNotificationsAsync(As(Bilal))).Single();
        Assert.Equal("Comment removed", note.Text);
        Assert.True(note.IsRead);
    }

    [Fact]
    public async Task TheItemShowsWhoIsTaggedInEachComment_AndNotificationsCanBeRead()
    {
        var space = await NewSpaceAsync();
        await space.Mentions.TagCommentAsync(space.Item, space.Comment, "@Bilal Khan can you check the GST line?", Ids(Bilal), Asha);
        var detail = new WorkItemDetailDto { WorkItemId = space.Item, Comments = new List<WorkCommentDto> { new WorkCommentDto { CommentId = space.Comment } } };

        await space.Mentions.FillAsync(detail);
        Assert.Equal(new[] { Bilal }, detail.Comments[0].MentionUserIds);

        await space.Mentions.MarkNotificationsReadAsync(Bilal, new List<Guid>());
        Assert.True((await space.Mentions.GetNotificationsAsync(As(Bilal))).Single().IsRead);
    }

    [Theory]
    [InlineData("short @Bilal Khan note", "short @Bilal Khan note")]
    [InlineData("**bold** and `code` @Bilal Khan", "bold and code @Bilal Khan")]
    public void TheExcerpt_DropsMarkdownMarks(string text, string excerpt)
    {
        Assert.Equal(excerpt, WorkMentionService.Excerpt(text, "Bilal Khan"));
    }

    [Fact]
    public void ALongTextIsCutAroundTheTag()
    {
        var text = new string('a', 300) + " please @Bilal Khan look at this " + new string('b', 300);
        var excerpt = WorkMentionService.Excerpt(text, "Bilal Khan");
        Assert.StartsWith("…", excerpt);
        Assert.EndsWith("…", excerpt);
        Assert.Contains("@Bilal Khan look at this", excerpt);
        Assert.True(excerpt.Length <= 162);
    }
}
