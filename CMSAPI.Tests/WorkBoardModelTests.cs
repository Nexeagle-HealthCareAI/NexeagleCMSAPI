#nullable enable
using System;
using System.Linq;
using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// The Daily Work Board saves parent and child rows together (a work item with its first history
// line, a chat message with its mentions...). SQL Server refuses that with error 547 if the child is
// inserted first, and EF only gets the order right when the model declares the foreign key. The
// in-memory database used by the other tests does not enforce foreign keys, so this is checked on
// the model itself. In production, creating a work item failed this way (November 2026).
public class WorkBoardModelTests
{
    private static CmsDbContext NewContext() =>
        new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Theory]
    [InlineData(typeof(WorkStatus), typeof(WorkProject), "ProjectId")]
    [InlineData(typeof(WorkItem), typeof(WorkProject), "ProjectId")]
    [InlineData(typeof(WorkItemHistory), typeof(WorkItem), "WorkItemId")]
    [InlineData(typeof(WorkItemComment), typeof(WorkItem), "WorkItemId")]
    [InlineData(typeof(DocFolderAccess), typeof(DocFolder), "FolderId")]
    [InlineData(typeof(WorkDocument), typeof(DocFolder), "FolderId")]
    [InlineData(typeof(DocVersion), typeof(WorkDocument), "DocumentId")]
    [InlineData(typeof(WorkItemDocument), typeof(WorkItem), "WorkItemId")]
    [InlineData(typeof(WorkItemDocument), typeof(WorkDocument), "DocumentId")]
    [InlineData(typeof(WorkProjectMember), typeof(WorkProject), "ProjectId")]
    [InlineData(typeof(WorkProjectAccessLog), typeof(WorkProject), "ProjectId")]
    [InlineData(typeof(WorkChatChannel), typeof(WorkProject), "ProjectId")]
    [InlineData(typeof(WorkChatMember), typeof(WorkChatChannel), "ChannelId")]
    [InlineData(typeof(WorkChatMessage), typeof(WorkChatChannel), "ChannelId")]
    [InlineData(typeof(WorkChatMention), typeof(WorkChatMessage), "MessageId")]
    [InlineData(typeof(WorkNotification), typeof(WorkChatChannel), "ChannelId")]
    [InlineData(typeof(WorkNotification), typeof(WorkChatMessage), "MessageId")]
    [InlineData(typeof(DocComment), typeof(WorkDocument), "DocumentId")]
    [InlineData(typeof(DocComment), typeof(DocComment), "ParentId")]
    [InlineData(typeof(DocCommentMention), typeof(DocComment), "CommentId")]
    [InlineData(typeof(DocNotification), typeof(WorkDocument), "DocumentId")]
    [InlineData(typeof(DocNotification), typeof(DocComment), "CommentId")]
    public void TheModelKnowsWhichRowDependsOnWhich(Type child, Type parent, string foreignKey)
    {
        using var db = NewContext();
        var entity = db.Model.FindEntityType(child);

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == parent
            && fk.Properties.Count == 1
            && fk.Properties[0].Name == foreignKey
            && fk.DeleteBehavior == DeleteBehavior.ClientNoAction);
    }

    [Fact]
    public void NoRelationshipDeletesRowsOnAParentsBehalf()
    {
        using var db = NewContext();
        var boardTypes = new[] { "Work", "Doc" };
        var cascades = db.Model.GetEntityTypes()
            .Where(t => boardTypes.Any(prefix => t.ClrType.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade)
            .Select(fk => fk.DeclaringEntityType.ClrType.Name + " -> " + fk.PrincipalEntityType.ClrType.Name)
            .ToList();

        Assert.Empty(cascades);
    }
}
