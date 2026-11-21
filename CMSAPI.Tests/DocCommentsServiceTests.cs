#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Daily Work Board, comments on documents: threads, tagging and its notifications, resolving,
// changing and removing, and sensitive folders hiding a document's comments.
public class DocCommentsServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();
    private static readonly Guid Chitra = Guid.NewGuid();

    private sealed class NoStore : IDocFileStore
    {
        public bool IsConfigured => false;
        public Task SaveAsync(string key, Stream content, string contentType) => Task.CompletedTask;
        public Task<Stream> OpenAsync(string key) => Task.FromResult<Stream>(new MemoryStream());
    }

    private sealed class Space
    {
        public CmsDbContext Db = null!;
        public DocsService Docs = null!;
        public DocCommentsService Comments = null!;
        public Guid Page;
    }

    private static DocsActor Admin(Guid userId) => new DocsActor { UserId = userId, CanEdit = true, CanDelete = true, CanSensitive = true };
    private static DocsActor Reader(Guid userId) => new DocsActor { UserId = userId };
    private static SaveDocCommentRequest Say(string body, params Guid[] tag) => new SaveDocCommentRequest { Body = body, MentionUserIds = tag.ToList() };

    private static async Task<Space> NewSpaceAsync()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.CmsUsers.Add(new CmsUser { UserId = Asha, FullName = "Asha Rao", Email = "asha@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsUsers.Add(new CmsUser { UserId = Bilal, FullName = "Bilal Khan", Email = "bilal@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsUsers.Add(new CmsUser { UserId = Chitra, FullName = "Chitra Das", Email = "chitra@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var docs = new DocsService(db, new NoStore());
        var page = await docs.CreatePageAsync(Admin(Asha), new CreateDocPageRequest { Name = "Release plan" });
        return new Space { Db = db, Docs = docs, Comments = new DocCommentsService(db, docs), Page = page.Data!.DocumentId };
    }

    [Fact]
    public async Task AnyoneWhoCanOpenTheDocument_CanComment_AndTheAuthorOfTheDocumentIsTold()
    {
        var space = await NewSpaceAsync();

        var added = await space.Comments.AddAsync(Reader(Bilal), space.Page, new SaveDocCommentRequest { Body = " The date looks tight ", Quote = "Go live on 1 December" });

        Assert.True(added.Ok);
        Assert.Equal("The date looks tight", added.Data!.Body);
        Assert.Equal("Go live on 1 December", added.Data.Quote);
        Assert.Equal("Bilal Khan", added.Data.AuthorName);
        var note = Assert.Single(await space.Comments.GetNotificationsAsync(Reader(Asha)));
        Assert.Equal("Comment", note.Kind);
        Assert.Equal("Release plan", note.DocumentName);
        Assert.Equal("The date looks tight", note.Text);
    }

    [Fact]
    public async Task AReply_JoinsTheThread_TellsWhoStartedIt_AndReopensAResolvedThread()
    {
        var space = await NewSpaceAsync();
        var first = await space.Comments.AddAsync(Reader(Bilal), space.Page, Say("Is the budget approved?"));
        await space.Comments.ResolveAsync(Reader(Asha), first.Data!.CommentId, true);
        var reply = await space.Comments.AddAsync(Reader(Asha), space.Page, new SaveDocCommentRequest { Body = "Yes, on Monday", ParentId = first.Data.CommentId });

        // An answer to the reply stays in the same thread.
        var nested = await space.Comments.AddAsync(Reader(Chitra), space.Page, new SaveDocCommentRequest { Body = "Thanks", ParentId = reply.Data!.CommentId });
        var all = await space.Comments.GetAsync(Reader(Chitra), space.Page);

        Assert.Equal(first.Data.CommentId, reply.Data.ParentId);
        Assert.Equal(first.Data.CommentId, nested.Data!.ParentId);
        Assert.False(all.Data!.Single(c => c.CommentId == first.Data.CommentId).IsResolved);
        Assert.Equal(new[] { "Is the budget approved?", "Yes, on Monday", "Thanks" }, all.Data.Select(c => c.Body));
        Assert.Contains(await space.Comments.GetNotificationsAsync(Reader(Bilal)), n => n.Kind == "Reply" && n.ActorName == "Asha Rao");
    }

    [Fact]
    public async Task TaggingSomeone_NotifiesThemOnce_AndNotTheAuthor()
    {
        var space = await NewSpaceAsync();

        var added = await space.Comments.AddAsync(Reader(Bilal), space.Page, Say("@Asha Rao @Chitra Das please check", Asha, Chitra, Bilal));

        Assert.Equal(new[] { Asha, Chitra }.OrderBy(x => x), added.Data!.MentionUserIds.OrderBy(x => x));
        // Asha both wrote the document and was tagged: one notification, as a mention.
        Assert.Equal("Mention", Assert.Single(await space.Comments.GetNotificationsAsync(Reader(Asha))).Kind);
        Assert.Single(await space.Comments.GetNotificationsAsync(Reader(Chitra)));
        Assert.Empty(await space.Comments.GetNotificationsAsync(Reader(Bilal)));
    }

    [Fact]
    public async Task ResolvingAThread_SaysWhoDidIt_AndOnlyTheFirstCommentCanBeResolved()
    {
        var space = await NewSpaceAsync();
        var first = await space.Comments.AddAsync(Reader(Bilal), space.Page, Say("Typo in the title"));
        var reply = await space.Comments.AddAsync(Reader(Asha), space.Page, new SaveDocCommentRequest { Body = "Fixed", ParentId = first.Data!.CommentId });

        var resolved = await space.Comments.ResolveAsync(Reader(Asha), first.Data.CommentId, true);
        var onReply = await space.Comments.ResolveAsync(Reader(Asha), reply.Data!.CommentId, true);
        var reopened = await space.Comments.ResolveAsync(Reader(Bilal), first.Data.CommentId, false);

        Assert.True(resolved.Data!.IsResolved);
        Assert.Equal("Asha Rao", resolved.Data.ResolvedByName);
        Assert.Equal(400, onReply.Status);
        Assert.False(reopened.Data!.IsResolved);
        Assert.Null(reopened.Data.ResolvedByName);
    }

    [Fact]
    public async Task PeopleChangeAndRemoveOnlyTheirOwnComments_UnlessTheyMayDelete()
    {
        var space = await NewSpaceAsync();
        var mine = await space.Comments.AddAsync(Reader(Bilal), space.Page, Say("First draft"));
        var id = mine.Data!.CommentId;

        var editedByOther = await space.Comments.EditAsync(Reader(Chitra), id, Say("Hijacked"));
        var edited = await space.Comments.EditAsync(Reader(Bilal), id, Say("Second draft @Chitra Das", Chitra));
        var removedByOther = await space.Comments.DeleteAsync(Reader(Chitra), id);
        var removedByAdmin = await space.Comments.DeleteAsync(Admin(Asha), id);
        var listed = await space.Comments.GetAsync(Reader(Bilal), space.Page);

        Assert.Equal(403, editedByOther.Status);
        Assert.True(edited.Data!.IsEdited);
        Assert.Equal("Second draft @Chitra Das", edited.Data.Body);
        Assert.Contains(await space.Db.DocNotifications.ToListAsync(), n => n.UserId == Chitra && n.Kind == "Mention");
        Assert.Equal(403, removedByOther.Status);
        Assert.True(removedByAdmin.Ok);
        var gone = Assert.Single(listed.Data!);
        Assert.True(gone.IsDeleted);
        Assert.Equal(string.Empty, gone.Body);
    }

    [Fact]
    public async Task ASensitiveFolder_HidesItsDocumentsComments_AndNobodyOutsideIsNotified()
    {
        var space = await NewSpaceAsync();
        var folder = await space.Docs.CreateFolderAsync(Admin(Asha), new SaveDocFolderRequest { Name = "Board papers", IsRestricted = true });
        var page = await space.Docs.CreatePageAsync(Admin(Asha), new CreateDocPageRequest { Name = "Salaries", FolderId = folder.Data!.FolderId });
        var secret = page.Data!.DocumentId;

        var read = await space.Comments.GetAsync(Reader(Bilal), secret);
        var write = await space.Comments.AddAsync(Reader(Bilal), secret, Say("Let me in"));
        var tagged = await space.Comments.AddAsync(Admin(Asha), secret, Say("@Bilal Khan see this", Bilal));

        Assert.Equal(404, read.Status);
        Assert.Equal(404, write.Status);
        Assert.True(tagged.Ok);
        Assert.Empty(tagged.Data!.MentionUserIds);
        Assert.Empty(await space.Comments.GetNotificationsAsync(Reader(Bilal)));
    }

    [Fact]
    public async Task AnEmptyOrOverlongComment_IsRefused_AndALongQuoteIsCut()
    {
        var space = await NewSpaceAsync();

        var quoted = await space.Comments.AddAsync(Reader(Bilal), space.Page, new SaveDocCommentRequest { Body = "About this", Quote = new string('q', 900) });

        Assert.Equal(400, (await space.Comments.AddAsync(Reader(Bilal), space.Page, Say("  "))).Status);
        Assert.Equal(400, (await space.Comments.AddAsync(Reader(Bilal), space.Page, Say(new string('x', 4001)))).Status);
        Assert.Equal(500, quoted.Data!.Quote!.Length);
    }

    [Fact]
    public async Task ReadingNotifications_ClearsThem()
    {
        var space = await NewSpaceAsync();
        await space.Comments.AddAsync(Reader(Bilal), space.Page, Say("One"));
        await space.Comments.AddAsync(Reader(Chitra), space.Page, Say("Two"));

        await space.Comments.MarkNotificationsReadAsync(Asha, new List<Guid>());

        Assert.All(await space.Comments.GetNotificationsAsync(Reader(Asha)), n => Assert.True(n.IsRead));
    }
}
