#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Daily Work Board, Docs tab: who can see a restricted folder, uploads and their versions, the bin
// (which needs its own permission), pages, and documents tagged on work items.
public class DocsServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();
    private static readonly Guid Chitra = Guid.NewGuid();

    private sealed class FakeStore : IDocFileStore
    {
        public readonly Dictionary<string, byte[]> Saved = new Dictionary<string, byte[]>();
        public bool IsConfigured { get; set; } = true;
        /// <summary>Set to make the store refuse files, as a store with wrong keys would.</summary>
        public DocStorageException? Refuse { get; set; }

        public async Task SaveAsync(string key, Stream content, string contentType)
        {
            if (Refuse != null) throw Refuse;
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            Saved[key] = copy.ToArray();
        }

        public Task<Stream> OpenAsync(string key)
        {
            if (Refuse != null) throw Refuse;
            if (!Saved.TryGetValue(key, out var bytes)) throw new FileNotFoundException("missing", key);
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }
    }

    private sealed class Space
    {
        public CmsDbContext Db = null!;
        public DocsService Service = null!;
        public FakeStore Store = null!;
    }

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
        var store = new FakeStore();
        return new Space { Db = db, Store = store, Service = new DocsService(db, store) };
    }

    private static DocsActor Editor(Guid userId) => new DocsActor { UserId = userId, CanEdit = true };
    private static DocsActor Admin(Guid userId) => new DocsActor { UserId = userId, CanEdit = true, CanDelete = true, CanSensitive = true };
    private static Stream Bytes(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static Task<WorkResult<DocDetailDto>> UploadAsync(Space space, DocsActor actor, Guid? folderId, string name, string text = "hello") =>
        space.Service.UploadAsync(actor, folderId, name, "application/pdf", Encoding.UTF8.GetByteCount(text), Bytes(text));

    // ── Restricted folders ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RestrictedFolder_IsHiddenFromEveryoneNotLetIn()
    {
        var space = await NewSpaceAsync();
        var hr = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "Salaries", IsRestricted = true });
        var inside = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "2026", ParentId = hr.Data!.FolderId });
        var slip = await UploadAsync(space, Editor(Asha), inside.Data!.FolderId, "november.pdf");
        Assert.True(slip.Ok);

        // Bilal was not let in: no folder, no sub-folder, no file, not even in search or by id.
        Assert.Empty(await space.Service.GetFoldersAsync(Editor(Bilal)));
        Assert.Equal(404, (await space.Service.ListAsync(Editor(Bilal), inside.Data.FolderId, null)).Status);
        Assert.Empty((await space.Service.ListAsync(Editor(Bilal), null, "november")).Data!);
        Assert.Equal(404, (await space.Service.GetAsync(Editor(Bilal), slip.Data!.DocumentId)).Status);
        Assert.Equal(404, (await space.Service.OpenFileAsync(Editor(Bilal), slip.Data.DocumentId, null)).Status);

        // The person who made it, and someone who manages sensitive documents, see everything.
        Assert.Equal(2, (await space.Service.GetFoldersAsync(Editor(Asha))).Count);
        Assert.Equal(2, (await space.Service.GetFoldersAsync(Admin(Chitra))).Count);
        Assert.Single((await space.Service.ListAsync(Admin(Chitra), null, "november")).Data!);
    }

    [Fact]
    public async Task RestrictedFolder_OpensForPeopleAddedToItsAccessList()
    {
        var space = await NewSpaceAsync();
        var hr = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "Contracts", IsRestricted = true });

        // Someone who was not let in cannot change the list, or even learn the folder exists.
        Assert.Equal(404, (await space.Service.SetFolderAccessAsync(Editor(Bilal), hr.Data!.FolderId, new SetDocFolderAccessRequest { UserIds = new List<Guid> { Bilal } })).Status);

        var saved = await space.Service.SetFolderAccessAsync(Editor(Asha), hr.Data.FolderId, new SetDocFolderAccessRequest { UserIds = new List<Guid> { Asha, Bilal } });
        Assert.True(saved.Ok);
        Assert.Single(await space.Service.GetFoldersAsync(Editor(Bilal)));
        Assert.Empty(await space.Service.GetFoldersAsync(Editor(Chitra)));

        // Being let in is not the same as managing access.
        Assert.Equal(403, (await space.Service.GetFolderAccessAsync(Editor(Bilal), hr.Data.FolderId)).Status);
    }

    [Fact]
    public async Task OnlyTheMakerOrASensitiveManager_CanLockOrUnlockAFolder()
    {
        var space = await NewSpaceAsync();
        var folder = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "Plans" });
        var locking = new SaveDocFolderRequest { Name = "Plans", IsRestricted = true };

        Assert.Equal(403, (await space.Service.UpdateFolderAsync(Editor(Bilal), folder.Data!.FolderId, locking)).Status);
        Assert.True((await space.Service.UpdateFolderAsync(Admin(Chitra), folder.Data.FolderId, locking)).Ok);
    }

    [Fact]
    public async Task Folder_CannotBeMovedIntoItselfOrSomethingUnderIt()
    {
        var space = await NewSpaceAsync();
        var top = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "Top" });
        var child = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "Child", ParentId = top.Data!.FolderId });

        var moved = await space.Service.UpdateFolderAsync(Editor(Asha), top.Data.FolderId, new SaveDocFolderRequest { Name = "Top", ParentId = child.Data!.FolderId });

        Assert.Equal(400, moved.Status);
    }

    // ── Files ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_SameNameInTheSameFolder_BecomesANewVersion_AndTheOldOneCanStillBeOpened()
    {
        var space = await NewSpaceAsync();
        var first = await UploadAsync(space, Editor(Asha), null, "plan.pdf", "one");
        var second = await UploadAsync(space, Editor(Bilal), null, "plan.pdf", "two!");

        Assert.Equal(first.Data!.DocumentId, second.Data!.DocumentId);
        Assert.Equal(2, second.Data.VersionNumber);
        Assert.Equal("Bilal Khan", second.Data.UpdatedByName);
        var old = Assert.Single(second.Data.Versions);
        Assert.Equal(1, old.VersionNumber);
        Assert.Equal("Asha Rao", old.CreatedByName);

        using var current = new StreamReader((await space.Service.OpenFileAsync(Editor(Asha), first.Data.DocumentId, null)).Data!.Content);
        Assert.Equal("two!", await current.ReadToEndAsync());
        using var earlier = new StreamReader((await space.Service.OpenFileAsync(Editor(Asha), first.Data.DocumentId, old.VersionId)).Data!.Content);
        Assert.Equal("one", await earlier.ReadToEndAsync());
        Assert.Single((await space.Service.ListAsync(Editor(Asha), null, null)).Data!);
    }

    [Fact]
    public async Task Upload_WhenTheStoreRefusesTheFile_SaysWhy_AndKeepsNothing()
    {
        var space = await NewSpaceAsync();
        space.Store.Refuse = new DocStorageException("The file store at s3.example.com did not accept the storage keys.");

        var result = await UploadAsync(space, Editor(Asha), null, "plan.pdf");

        Assert.Equal(503, result.Status);
        Assert.Contains("did not accept the storage keys", result.Message);
        Assert.Empty(space.Store.Saved);
        Assert.Empty((await space.Service.ListAsync(Editor(Asha), null, null)).Data!);
    }

    [Fact]
    public async Task Upload_WithNoStoreSetUp_IsRefused_ButPagesStillWork()
    {
        var space = await NewSpaceAsync();
        space.Store.IsConfigured = false;

        Assert.Equal(503, (await UploadAsync(space, Editor(Asha), null, "plan.pdf")).Status);
        Assert.True((await space.Service.CreatePageAsync(Editor(Asha), new CreateDocPageRequest { Name = "Notes" })).Ok);
    }

    [Fact]
    public async Task Open_WhenTheStoreCannotBeReached_SaysSo()
    {
        var space = await NewSpaceAsync();
        var file = await UploadAsync(space, Editor(Asha), null, "plan.pdf");
        space.Store.Refuse = new DocStorageException("The file store at s3.example.com cannot be reached.");

        var result = await space.Service.OpenFileAsync(Editor(Asha), file.Data!.DocumentId, null);

        Assert.Equal(503, result.Status);
        Assert.Contains("cannot be reached", result.Message);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("run.bat")]
    [InlineData("page.html")]
    [InlineData("noending")]
    public async Task Upload_ProgramsAndUnknownTypes_AreRefused(string fileName)
    {
        var space = await NewSpaceAsync();

        var result = await UploadAsync(space, Editor(Asha), null, fileName);

        Assert.Equal(400, result.Status);
        Assert.Empty(space.Store.Saved);
    }

    [Fact]
    public async Task Upload_TooLarge_OrWithoutStorage_OrWithoutEditPermission_IsRefused()
    {
        var space = await NewSpaceAsync();

        var big = await space.Service.UploadAsync(Editor(Asha), null, "big.pdf", "application/pdf", DocsRules.MaxFileBytes + 1, Bytes("x"));
        Assert.Equal(413, big.Status);

        var viewer = await UploadAsync(space, new DocsActor { UserId = Bilal }, null, "a.pdf");
        Assert.Equal(403, viewer.Status);

        space.Store.IsConfigured = false;
        var noStore = await UploadAsync(space, Editor(Asha), null, "a.pdf");
        Assert.Equal(503, noStore.Status);
        // Pages need no storage.
        Assert.True((await space.Service.CreatePageAsync(Editor(Asha), new CreateDocPageRequest { Name = "Notes" })).Ok);
    }

    [Fact]
    public async Task RenamingAFile_MustKeepItsEnding()
    {
        var space = await NewSpaceAsync();
        var file = await UploadAsync(space, Editor(Asha), null, "plan.pdf");

        var bad = await space.Service.UpdateAsync(Editor(Asha), file.Data!.DocumentId, new UpdateDocRequest { Name = "plan.exe" });
        Assert.Equal(400, bad.Status);

        var good = await space.Service.UpdateAsync(Editor(Asha), file.Data.DocumentId, new UpdateDocRequest { Name = "Launch plan.pdf", Tags = new List<string> { " Launch ", "launch" } });
        Assert.Equal("Launch plan.pdf", good.Data!.Name);
        Assert.Equal(new List<string> { "launch" }, good.Data.Tags);
    }

    // ── Pages ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Page_SavesItsText_IsFoundBySearch_AndKeepsAnEarlierVersionWhenSomeoneElseEdits()
    {
        var space = await NewSpaceAsync();
        var page = await space.Service.CreatePageAsync(Editor(Asha), new CreateDocPageRequest { Name = "Release notes" });

        await space.Service.SavePageAsync(Editor(Asha), page.Data!.DocumentId, new SaveDocPageRequest { Content = "{\"v\":1}", ContentText = "Billing screen is faster" });
        var quick = await space.Service.SavePageAsync(Editor(Asha), page.Data.DocumentId, new SaveDocPageRequest { Content = "{\"v\":2}", ContentText = "Billing screen is much faster" });
        // The same person a moment later: one more autosave, kept as the first version only.
        Assert.Single(quick.Data!.Versions);

        var other = await space.Service.SavePageAsync(Editor(Bilal), page.Data.DocumentId, new SaveDocPageRequest { Content = "{\"v\":3}", ContentText = "Billing and pharmacy" });
        Assert.Equal(2, other.Data!.Versions.Count);
        Assert.Equal("{\"v\":3}", other.Data.Content);

        Assert.Single((await space.Service.ListAsync(Editor(Chitra), null, "pharmacy")).Data!);
        Assert.Empty((await space.Service.ListAsync(Editor(Chitra), null, "radiology")).Data!);
    }

    // ── The bin ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_NeedsItsOwnPermission_AndABinnedDocumentCanBeRestored()
    {
        var space = await NewSpaceAsync();
        var file = await UploadAsync(space, Editor(Asha), null, "plan.pdf");

        // Editing is not enough to delete, or to look in the bin.
        Assert.Equal(403, (await space.Service.DeleteAsync(Editor(Asha), file.Data!.DocumentId)).Status);
        Assert.Equal(403, (await space.Service.GetBinAsync(Editor(Asha))).Status);

        Assert.True((await space.Service.DeleteAsync(Admin(Chitra), file.Data.DocumentId)).Ok);
        Assert.Empty((await space.Service.ListAsync(Editor(Asha), null, null)).Data!);
        Assert.Equal(404, (await space.Service.GetAsync(Editor(Asha), file.Data.DocumentId)).Status);
        var binned = Assert.Single((await space.Service.GetBinAsync(Admin(Chitra))).Data!.Documents);
        Assert.Equal("Chitra Das", binned.DeletedByName);

        Assert.True((await space.Service.RestoreAsync(Admin(Chitra), file.Data.DocumentId)).Ok);
        Assert.Single((await space.Service.ListAsync(Editor(Asha), null, null)).Data!);
        // The files themselves are never removed from storage.
        Assert.Single(space.Store.Saved);
    }

    [Fact]
    public async Task BinningAFolder_HidesWhatIsInside_AndRestoringBringsItBack()
    {
        var space = await NewSpaceAsync();
        var folder = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "Old" });
        var file = await UploadAsync(space, Editor(Asha), folder.Data!.FolderId, "old.pdf");

        Assert.True((await space.Service.DeleteFolderAsync(Admin(Chitra), folder.Data.FolderId)).Ok);
        Assert.Empty(await space.Service.GetFoldersAsync(Editor(Asha)));
        Assert.Equal(404, (await space.Service.GetAsync(Editor(Asha), file.Data!.DocumentId)).Status);
        Assert.Single((await space.Service.GetBinAsync(Admin(Chitra))).Data!.Folders);

        Assert.True((await space.Service.RestoreFolderAsync(Admin(Chitra), folder.Data.FolderId)).Ok);
        Assert.True((await space.Service.GetAsync(Editor(Asha), file.Data.DocumentId)).Ok);
    }

    // ── Documents tagged on work items ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkedDocuments_ShowOnTheItem_ButNotTheOnesLockedToTheReader()
    {
        var space = await NewSpaceAsync();
        var project = new WorkProject { ProjectId = Guid.NewGuid(), Key = "HMS", Name = "1HMS", NextNumber = 2, CreatedAt = DateTime.UtcNow };
        var item = new WorkItem { WorkItemId = Guid.NewGuid(), ProjectId = project.ProjectId, Number = 1, ItemType = "Task", Title = "Ship billing", Status = "ToDo", Priority = "Medium", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        space.Db.WorkProjects.Add(project);
        space.Db.WorkItems.Add(item);
        await space.Db.SaveChangesAsync();

        var open = await UploadAsync(space, Editor(Asha), null, "spec.pdf");
        var locked = await space.Service.CreateFolderAsync(Editor(Asha), new SaveDocFolderRequest { Name = "Costs", IsRestricted = true });
        var secret = await UploadAsync(space, Editor(Asha), locked.Data!.FolderId, "costs.pdf");

        Assert.True((await space.Service.LinkAsync(Editor(Asha), item.WorkItemId, open.Data!.DocumentId)).Ok);
        Assert.True((await space.Service.LinkAsync(Editor(Asha), item.WorkItemId, secret.Data!.DocumentId)).Ok);
        // Bilal cannot tag a document he cannot see.
        Assert.Equal(404, (await space.Service.LinkAsync(Editor(Bilal), item.WorkItemId, secret.Data.DocumentId)).Status);

        Assert.Equal(2, (await space.Service.GetItemDocumentsAsync(Editor(Asha), item.WorkItemId)).Data!.Count);
        var forBilal = Assert.Single((await space.Service.GetItemDocumentsAsync(Editor(Bilal), item.WorkItemId)).Data!);
        Assert.Equal("spec.pdf", forBilal.Name);

        var detail = await space.Service.GetAsync(Editor(Asha), open.Data.DocumentId);
        Assert.Equal("HMS-1", Assert.Single(detail.Data!.LinkedItems).Key);

        Assert.True((await space.Service.UnlinkAsync(Editor(Asha), item.WorkItemId, open.Data.DocumentId)).Ok);
        Assert.Single((await space.Service.GetItemDocumentsAsync(Editor(Asha), item.WorkItemId)).Data!);
    }

    [Fact]
    public async Task TheAccessLog_RecordsOpensAndDownloads_AndOnlySensitiveManagersCanReadIt()
    {
        var space = await NewSpaceAsync();
        var file = await UploadAsync(space, Editor(Asha), null, "plan.pdf");
        await space.Service.GetAsync(Editor(Bilal), file.Data!.DocumentId);
        (await space.Service.OpenFileAsync(Editor(Bilal), file.Data.DocumentId, null)).Data!.Content.Dispose();

        Assert.Equal(403, (await space.Service.GetLogAsync(Editor(Bilal), file.Data.DocumentId)).Status);
        var log = (await space.Service.GetLogAsync(Admin(Chitra), file.Data.DocumentId)).Data!;
        Assert.Contains(log, l => l.Action == "upload" && l.UserName == "Asha Rao");
        Assert.Contains(log, l => l.Action == "open" && l.UserName == "Bilal Khan");
        Assert.Contains(log, l => l.Action == "download" && l.UserName == "Bilal Khan");
    }
}
