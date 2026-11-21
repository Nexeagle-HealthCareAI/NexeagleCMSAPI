using System.Globalization;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Services;

/// <summary>
/// Rules of the Daily Work Board: what may be created, how items nest, and what is written to an
/// item's history. Storage is behind IWorkRepository so these rules can be tested without a database.
/// </summary>
public class WorkService : IWorkService
{
    private const int MaxHistoryValueLength = 400;
    private const int MaxCommentLength = 10_000;
    private const int MaxDescriptionLength = 100_000;
    private const double RankStep = 1000;

    private readonly IWorkRepository _repo;

    public WorkService(IWorkRepository repo)
    {
        _repo = repo;
    }

    // ── Projects and people ─────────────────────────────────────────────────────────────────────

    public async Task<List<WorkProjectDto>> GetProjectsAsync()
    {
        var projects = await _repo.GetProjectsAsync();
        var statuses = await _repo.GetAllStatusesAsync();
        var list = new List<WorkProjectDto>();
        foreach (var project in projects)
        {
            var own = statuses.Where(st => st.ProjectId == project.ProjectId).ToList();
            // A project that somehow has no columns gets the usual four, so its board always works.
            if (own.Count == 0) own = await EnsureStatusesAsync(project.ProjectId);
            list.Add(MapProject(project, own));
        }
        return list;
    }

    public async Task<WorkResult<WorkProjectDto>> CreateProjectAsync(CreateWorkProjectRequest request, Guid? actorUserId)
    {
        var key = (request.Key ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim();

        if (key.Length < 2 || key.Length > 10 || !key.All(c => c >= 'A' && c <= 'Z'))
            return WorkResult<WorkProjectDto>.Fail(400, "The project key must be 2 to 10 letters, for example HMS.");
        if (name.Length == 0 || name.Length > 100)
            return WorkResult<WorkProjectDto>.Fail(400, "Give the project a name of up to 100 characters.");
        if (await _repo.ProjectKeyExistsAsync(key))
            return WorkResult<WorkProjectDto>.Fail(409, $"A project with the key {key} already exists.");

        var project = new WorkProject
        {
            ProjectId = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = Clean(request.Description, 500),
            NextNumber = 1,
            SprintDays = 11,
            IsArchived = false,
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow,
        };
        await _repo.AddProjectAsync(project);
        var statuses = await EnsureStatusesAsync(project.ProjectId);
        return WorkResult<WorkProjectDto>.Success(MapProject(project, statuses), "Project created.");
    }

    public async Task<List<WorkUserDto>> GetUsersAsync()
    {
        var users = await _repo.GetActiveUsersAsync();
        return users
            .Select(u => new WorkUserDto { UserId = u.UserId, FullName = u.FullName, Email = u.Email })
            .ToList();
    }

    // ── Board columns ───────────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<WorkStatusDto>> AddStatusAsync(Guid projectId, SaveWorkStatusRequest request)
    {
        var project = await _repo.GetProjectAsync(projectId);
        if (project == null) return WorkResult<WorkStatusDto>.Fail(404, "Project not found.");

        var name = (request.Name ?? string.Empty).Trim();
        var problem = StatusProblem(name, request.Category);
        if (problem != null) return WorkResult<WorkStatusDto>.Fail(400, problem);

        var statuses = await EnsureStatusesAsync(projectId);
        if (statuses.Count >= WorkRules.MaxStatusesPerProject)
            return WorkResult<WorkStatusDto>.Fail(400, $"A board can have up to {WorkRules.MaxStatusesPerProject} columns.");
        if (statuses.Any(st => string.Equals(st.Name, name, StringComparison.OrdinalIgnoreCase)))
            return WorkResult<WorkStatusDto>.Fail(409, $"There is already a column called {name}.");

        // The key is made from the name once and never changes; add a number if it is taken.
        var baseKey = WorkRules.StatusKeyFrom(name);
        var key = baseKey;
        for (var n = 2; statuses.Any(st => string.Equals(st.Key, key, StringComparison.OrdinalIgnoreCase)); n++)
            key = baseKey + n.ToString(CultureInfo.InvariantCulture);

        var status = new WorkStatus
        {
            StatusId = Guid.NewGuid(),
            ProjectId = projectId,
            Key = key,
            Name = name,
            Category = request.Category,
            SortOrder = statuses.Count == 0 ? 10 : statuses.Max(st => st.SortOrder) + 10,
            CreatedAt = DateTime.UtcNow,
        };
        await _repo.AddStatusesAsync(new List<WorkStatus> { status });
        return WorkResult<WorkStatusDto>.Success(MapStatus(status), "Column added.");
    }

    public async Task<WorkResult<WorkStatusDto>> UpdateStatusAsync(Guid statusId, SaveWorkStatusRequest request)
    {
        var status = await _repo.GetStatusAsync(statusId);
        if (status == null) return WorkResult<WorkStatusDto>.Fail(404, "Column not found.");

        var name = (request.Name ?? string.Empty).Trim();
        var problem = StatusProblem(name, request.Category);
        if (problem != null) return WorkResult<WorkStatusDto>.Fail(400, problem);

        var statuses = await _repo.GetStatusesAsync(status.ProjectId);
        if (statuses.Any(st => st.StatusId != statusId && string.Equals(st.Name, name, StringComparison.OrdinalIgnoreCase)))
            return WorkResult<WorkStatusDto>.Fail(409, $"There is already a column called {name}.");
        if (status.Category == WorkRules.DoneCategory && request.Category != WorkRules.DoneCategory
            && !statuses.Any(st => st.StatusId != statusId && st.Category == WorkRules.DoneCategory))
            return WorkResult<WorkStatusDto>.Fail(400, "A board needs at least one column that means done.");

        status.Name = name;
        status.Category = request.Category;
        await _repo.SaveStatusesAsync(new List<WorkStatus> { status });
        return WorkResult<WorkStatusDto>.Success(MapStatus(status), "Column saved.");
    }

    public async Task<WorkResult<List<WorkStatusDto>>> ReorderStatusesAsync(Guid projectId, ReorderWorkStatusesRequest request)
    {
        var statuses = await _repo.GetStatusesAsync(projectId);
        if (statuses.Count == 0) return WorkResult<List<WorkStatusDto>>.Fail(404, "Project not found.");

        var ids = request.StatusIds ?? new List<Guid>();
        if (ids.Count != statuses.Count || ids.Distinct().Count() != ids.Count || ids.Any(id => statuses.All(st => st.StatusId != id)))
            return WorkResult<List<WorkStatusDto>>.Fail(400, "Send every column of this board once, in the new order.");

        for (var i = 0; i < ids.Count; i++)
            statuses.First(st => st.StatusId == ids[i]).SortOrder = (i + 1) * 10;
        await _repo.SaveStatusesAsync(statuses);
        return WorkResult<List<WorkStatusDto>>.Success(statuses.OrderBy(st => st.SortOrder).Select(MapStatus).ToList(), "Order saved.");
    }

    public async Task<WorkResult<bool>> DeleteStatusAsync(Guid statusId, string? moveToKey)
    {
        var status = await _repo.GetStatusAsync(statusId);
        if (status == null) return WorkResult<bool>.Fail(404, "Column not found.");

        var others = (await _repo.GetStatusesAsync(status.ProjectId)).Where(st => st.StatusId != statusId).ToList();
        if (others.Count == 0) return WorkResult<bool>.Fail(400, "A board needs at least one column.");
        if (status.Category == WorkRules.DoneCategory && !others.Any(st => st.Category == WorkRules.DoneCategory))
            return WorkResult<bool>.Fail(400, "A board needs at least one column that means done.");

        var inUse = await _repo.CountItemsInStatusAsync(status.ProjectId, status.Key);
        var target = others.FirstOrDefault(st => st.Key == moveToKey);
        if (inUse > 0 && target == null)
            return WorkResult<bool>.Fail(409,
                inUse == 1 ? "1 item is in this column. Choose a column to move it to." : $"{inUse} items are in this column. Choose a column to move them to.");

        // With nothing visible in the column, deleted items that still carry its key go to the first other column.
        await _repo.DeleteStatusAsync(status, (target ?? others[0]).Key);
        return WorkResult<bool>.Success(true, "Column removed.");
    }

    private static string? StatusProblem(string name, string category)
    {
        if (name.Length == 0) return "Give the column a name.";
        if (name.Length > WorkRules.MaxStatusNameLength) return $"The name can be up to {WorkRules.MaxStatusNameLength} characters.";
        if (Array.IndexOf(WorkRules.Categories, category) < 0) return "Say what the column means: to do, in progress or done.";
        return null;
    }

    /// <summary>The project's columns, creating the usual four when it has none.</summary>
    private async Task<List<WorkStatus>> EnsureStatusesAsync(Guid projectId)
    {
        var statuses = await _repo.GetStatusesAsync(projectId);
        if (statuses.Count > 0) return statuses;

        var now = DateTime.UtcNow;
        var order = 0;
        foreach (var d in WorkRules.DefaultStatuses)
        {
            order += 10;
            statuses.Add(new WorkStatus
            {
                StatusId = Guid.NewGuid(), ProjectId = projectId, Key = d.Key, Name = d.Name, Category = d.Category, SortOrder = order, CreatedAt = now,
            });
        }
        await _repo.AddStatusesAsync(statuses);
        return statuses;
    }

    // ── Reading items ───────────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<List<WorkItemDto>>> GetItemsAsync(Guid projectId)
    {
        var project = await _repo.GetProjectAsync(projectId);
        if (project == null) return WorkResult<List<WorkItemDto>>.Fail(404, "Project not found.");

        var items = await _repo.GetItemsAsync(projectId);
        var names = await _repo.GetUserNamesAsync(PeopleOf(items));
        var byId = items.ToDictionary(i => i.WorkItemId);
        var statuses = StatusMap(await EnsureStatusesAsync(projectId));

        var list = items
            .OrderBy(i => i.Rank)
            .ThenBy(i => i.Number)
            .Select(i => MapItem(i, project.Key, names, byId, items, statuses))
            .ToList();
        return WorkResult<List<WorkItemDto>>.Success(list);
    }

    public async Task<WorkResult<WorkItemDetailDto>> GetItemAsync(Guid workItemId)
    {
        var item = await _repo.GetItemAsync(workItemId);
        if (item == null) return WorkResult<WorkItemDetailDto>.Fail(404, "Work item not found.");
        return WorkResult<WorkItemDetailDto>.Success(await BuildDetailAsync(item));
    }

    // ── Creating ────────────────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<WorkItemDetailDto>> CreateItemAsync(CreateWorkItemRequest request, Guid? actorUserId)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0) return WorkResult<WorkItemDetailDto>.Fail(400, "Give the item a title.");
        if (title.Length > WorkRules.MaxTitleLength)
            return WorkResult<WorkItemDetailDto>.Fail(400, $"The title can be up to {WorkRules.MaxTitleLength} characters.");
        if (Array.IndexOf(WorkRules.ItemTypes, request.ItemType) < 0)
            return WorkResult<WorkItemDetailDto>.Fail(400, "Choose a type: epic, feature, story, task, bug or subtask.");

        var priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority!;
        if (Array.IndexOf(WorkRules.Priorities, priority) < 0)
            return WorkResult<WorkItemDetailDto>.Fail(400, "Choose a priority from the list.");
        if (request.StoryPoints.HasValue && (request.StoryPoints.Value < 0 || request.StoryPoints.Value > 999))
            return WorkResult<WorkItemDetailDto>.Fail(400, "Story points must be between 0 and 999.");
        if ((request.Description ?? string.Empty).Length > MaxDescriptionLength)
            return WorkResult<WorkItemDetailDto>.Fail(400, "The description is too long.");

        DateTime? startDate = null;
        if (!TryParseDate(request.StartDate, out startDate))
            return WorkResult<WorkItemDetailDto>.Fail(400, "The start date must look like 2026-12-31.");
        DateTime? dueDate = null;
        if (!TryParseDate(request.DueDate, out dueDate))
            return WorkResult<WorkItemDetailDto>.Fail(400, "The due date must look like 2026-12-31.");
        if (startDate.HasValue && dueDate.HasValue && startDate.Value > dueDate.Value)
            return WorkResult<WorkItemDetailDto>.Fail(400, "The start date must be on or before the due date.");

        var project = await _repo.GetProjectAsync(request.ProjectId);
        if (project == null) return WorkResult<WorkItemDetailDto>.Fail(404, "Project not found.");
        if (project.IsArchived) return WorkResult<WorkItemDetailDto>.Fail(400, "This project is archived.");

        WorkItem? parent = null;
        if (request.ParentId.HasValue)
        {
            parent = await _repo.GetItemAsync(request.ParentId.Value);
            if (parent == null || parent.ProjectId != project.ProjectId)
                return WorkResult<WorkItemDetailDto>.Fail(400, "The parent item was not found in this project.");
        }
        var parentProblem = WorkRules.ParentProblem(request.ItemType, parent?.ItemType);
        if (parentProblem != null) return WorkResult<WorkItemDetailDto>.Fail(400, parentProblem);

        if (request.AssigneeUserId.HasValue && !await IsKnownUserAsync(request.AssigneeUserId.Value))
            return WorkResult<WorkItemDetailDto>.Fail(400, "The person chosen as assignee was not found.");

        // New work starts in the first column that means "to do" (the first column if there is none).
        var statuses = await EnsureStatusesAsync(project.ProjectId);
        var first = statuses.FirstOrDefault(st => st.Category == "ToDo") ?? statuses[0];

        var now = DateTime.UtcNow;
        var item = new WorkItem
        {
            WorkItemId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            ItemType = request.ItemType,
            Title = title,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            Status = first.Key,
            Priority = priority,
            StoryPoints = request.StoryPoints,
            AssigneeUserId = request.AssigneeUserId,
            ReporterUserId = actorUserId,
            ParentId = parent?.WorkItemId,
            Labels = JoinLabels(WorkRules.CleanLabels(request.Labels)),
            StartDate = startDate,
            DueDate = dueDate,
            CompletedAt = first.Category == WorkRules.DoneCategory ? now : (DateTime?)null,
            Rank = await _repo.GetMaxRankAsync(project.ProjectId) + RankStep,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var created = NewHistory(item.WorkItemId, actorUserId, "created", null, request.ItemType, now);

        await _repo.AddItemAsync(item, created);
        return WorkResult<WorkItemDetailDto>.Success(await BuildDetailAsync(item), "Created.");
    }

    // ── Changing ────────────────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<WorkItemDetailDto>> UpdateItemAsync(Guid workItemId, UpdateWorkItemRequest request, Guid? actorUserId)
    {
        var item = await _repo.GetItemAsync(workItemId);
        if (item == null) return WorkResult<WorkItemDetailDto>.Fail(404, "Work item not found.");

        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0) return WorkResult<WorkItemDetailDto>.Fail(400, "Give the item a title.");
        if (title.Length > WorkRules.MaxTitleLength)
            return WorkResult<WorkItemDetailDto>.Fail(400, $"The title can be up to {WorkRules.MaxTitleLength} characters.");
        var statuses = await EnsureStatusesAsync(item.ProjectId);
        var newStatus = statuses.FirstOrDefault(st => st.Key == request.Status);
        if (newStatus == null)
            return WorkResult<WorkItemDetailDto>.Fail(400, "Choose a status from the board's columns.");
        if (Array.IndexOf(WorkRules.Priorities, request.Priority) < 0)
            return WorkResult<WorkItemDetailDto>.Fail(400, "Choose a priority from the list.");
        if (request.StoryPoints.HasValue && (request.StoryPoints.Value < 0 || request.StoryPoints.Value > 999))
            return WorkResult<WorkItemDetailDto>.Fail(400, "Story points must be between 0 and 999.");
        if ((request.Description ?? string.Empty).Length > MaxDescriptionLength)
            return WorkResult<WorkItemDetailDto>.Fail(400, "The description is too long.");

        DateTime? startDate = null;
        if (!TryParseDate(request.StartDate, out startDate))
            return WorkResult<WorkItemDetailDto>.Fail(400, "The start date must look like 2026-12-31.");
        DateTime? dueDate = null;
        if (!TryParseDate(request.DueDate, out dueDate))
            return WorkResult<WorkItemDetailDto>.Fail(400, "The due date must look like 2026-12-31.");
        if (startDate.HasValue && dueDate.HasValue && startDate.Value > dueDate.Value)
            return WorkResult<WorkItemDetailDto>.Fail(400, "The start date must be on or before the due date.");

        var blockedReason = request.IsBlocked ? Clean(request.BlockedReason, 300) : null;
        if (request.IsBlocked && blockedReason == null)
            return WorkResult<WorkItemDetailDto>.Fail(400, "Say what is blocking this item.");

        // Parent: must be in the same project and at the right level. Levels only go down
        // (epic, feature, story/task/bug, subtask), so a loop is impossible once the pairing is valid.
        WorkItem? oldParent = item.ParentId.HasValue ? await _repo.GetItemAsync(item.ParentId.Value) : null;
        WorkItem? newParent = oldParent;
        if (request.ParentId != item.ParentId)
        {
            newParent = null;
            if (request.ParentId.HasValue)
            {
                if (request.ParentId.Value == item.WorkItemId)
                    return WorkResult<WorkItemDetailDto>.Fail(400, "An item cannot be its own parent.");
                newParent = await _repo.GetItemAsync(request.ParentId.Value);
                if (newParent == null || newParent.ProjectId != item.ProjectId)
                    return WorkResult<WorkItemDetailDto>.Fail(400, "The parent item was not found in this project.");
            }
            var parentProblem = WorkRules.ParentProblem(item.ItemType, newParent?.ItemType);
            if (parentProblem != null) return WorkResult<WorkItemDetailDto>.Fail(400, parentProblem);
        }

        if (request.AssigneeUserId.HasValue && request.AssigneeUserId != item.AssigneeUserId
            && !await IsKnownUserAsync(request.AssigneeUserId.Value))
            return WorkResult<WorkItemDetailDto>.Fail(400, "The person chosen as assignee was not found.");

        if (request.SprintId.HasValue && request.SprintId != item.SprintId
            && !await _repo.SprintBelongsToProjectAsync(request.SprintId.Value, item.ProjectId))
            return WorkResult<WorkItemDetailDto>.Fail(400, "That sprint was not found in this project.");

        var project = await _repo.GetProjectAsync(item.ProjectId);
        var projectKey = project?.Key ?? string.Empty;
        var now = DateTime.UtcNow;
        var history = new List<WorkItemHistory>();
        void Log(string field, string? before, string? after) =>
            history.Add(NewHistory(item.WorkItemId, actorUserId, field, before, after, now));

        if (item.Title != title)
        {
            Log("title", item.Title, title);
            item.Title = title;
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description;
        if (item.Description != description)
        {
            Log("description", null, null); // the text itself is not copied into history
            item.Description = description;
        }

        if (item.Status != request.Status)
        {
            // History keeps the names people saw, so it still reads well after a column is renamed or removed.
            var oldStatus = statuses.FirstOrDefault(st => st.Key == item.Status);
            Log("status", oldStatus?.Name ?? item.Status, newStatus.Name);
            var wasDone = oldStatus != null && oldStatus.Category == WorkRules.DoneCategory;
            var isDone = newStatus.Category == WorkRules.DoneCategory;
            item.Status = newStatus.Key;
            if (isDone && !wasDone) item.CompletedAt = now;
            if (!isDone) item.CompletedAt = null;
        }

        if (item.Priority != request.Priority)
        {
            Log("priority", item.Priority, request.Priority);
            item.Priority = request.Priority;
        }

        if (item.StoryPoints != request.StoryPoints)
        {
            Log("storyPoints", FormatPoints(item.StoryPoints), FormatPoints(request.StoryPoints));
            item.StoryPoints = request.StoryPoints;
        }

        if (item.AssigneeUserId != request.AssigneeUserId)
        {
            var ids = new List<Guid>();
            if (item.AssigneeUserId.HasValue) ids.Add(item.AssigneeUserId.Value);
            if (request.AssigneeUserId.HasValue) ids.Add(request.AssigneeUserId.Value);
            var names = await _repo.GetUserNamesAsync(ids);
            Log("assignee", NameOf(names, item.AssigneeUserId), NameOf(names, request.AssigneeUserId));
            item.AssigneeUserId = request.AssigneeUserId;
        }

        if (item.ParentId != request.ParentId)
        {
            Log("parent",
                oldParent == null ? null : $"{projectKey}-{oldParent.Number}",
                newParent == null ? null : $"{projectKey}-{newParent.Number}");
            item.ParentId = request.ParentId;
        }

        if (item.SprintId != request.SprintId)
        {
            Log("sprint", item.SprintId.HasValue ? "in a sprint" : null, request.SprintId.HasValue ? "in a sprint" : null);
            item.SprintId = request.SprintId;
        }

        var labels = JoinLabels(WorkRules.CleanLabels(request.Labels));
        if ((item.Labels ?? string.Empty) != (labels ?? string.Empty))
        {
            Log("labels", item.Labels, labels);
            item.Labels = labels;
        }

        if (item.StartDate != startDate)
        {
            Log("startDate", FormatDate(item.StartDate), FormatDate(startDate));
            item.StartDate = startDate;
        }

        if (item.DueDate != dueDate)
        {
            Log("dueDate", FormatDate(item.DueDate), FormatDate(dueDate));
            item.DueDate = dueDate;
        }

        if (item.IsBlocked != request.IsBlocked || (item.BlockedReason ?? string.Empty) != (blockedReason ?? string.Empty))
        {
            Log("blocked", item.IsBlocked ? item.BlockedReason : null, request.IsBlocked ? blockedReason : null);
            item.IsBlocked = request.IsBlocked;
            item.BlockedReason = blockedReason;
        }

        if (history.Count > 0)
        {
            item.UpdatedAt = now;
            await _repo.SaveItemAsync(item, history);
        }
        return WorkResult<WorkItemDetailDto>.Success(await BuildDetailAsync(item), history.Count > 0 ? "Saved." : null);
    }

    public async Task<WorkResult<bool>> DeleteItemAsync(Guid workItemId, Guid? actorUserId)
    {
        var item = await _repo.GetItemAsync(workItemId);
        if (item == null) return WorkResult<bool>.Fail(404, "Work item not found.");

        var children = await _repo.GetChildrenAsync(workItemId);
        if (children.Count > 0)
            return WorkResult<bool>.Fail(409,
                children.Count == 1
                    ? "This item has 1 item under it. Move or delete that first."
                    : $"This item has {children.Count} items under it. Move or delete those first.");

        var now = DateTime.UtcNow;
        item.IsDeleted = true;
        item.UpdatedAt = now;
        await _repo.SaveItemAsync(item, new List<WorkItemHistory>
        {
            NewHistory(item.WorkItemId, actorUserId, "deleted", null, null, now),
        });
        return WorkResult<bool>.Success(true, "Deleted.");
    }

    // ── Comments ────────────────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<WorkCommentDto>> AddCommentAsync(Guid workItemId, AddWorkCommentRequest request, Guid? actorUserId)
    {
        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0) return WorkResult<WorkCommentDto>.Fail(400, "Write a comment first.");
        if (body.Length > MaxCommentLength) return WorkResult<WorkCommentDto>.Fail(400, "The comment is too long.");

        var item = await _repo.GetItemAsync(workItemId);
        if (item == null) return WorkResult<WorkCommentDto>.Fail(404, "Work item not found.");

        var comment = new WorkItemComment
        {
            CommentId = Guid.NewGuid(),
            WorkItemId = workItemId,
            AuthorUserId = actorUserId,
            Body = body,
            CreatedAt = DateTime.UtcNow,
        };
        await _repo.AddCommentAsync(comment);

        var names = await _repo.GetUserNamesAsync(actorUserId.HasValue ? new[] { actorUserId.Value } : Array.Empty<Guid>());
        return WorkResult<WorkCommentDto>.Success(MapComment(comment, names), "Comment added.");
    }

    public async Task<WorkResult<bool>> DeleteCommentAsync(Guid commentId, Guid? actorUserId, bool canManage)
    {
        var comment = await _repo.GetCommentAsync(commentId);
        if (comment == null) return WorkResult<bool>.Fail(404, "Comment not found.");
        if (!canManage && (!actorUserId.HasValue || comment.AuthorUserId != actorUserId))
            return WorkResult<bool>.Fail(403, "You can only remove your own comments.");

        comment.IsDeleted = true;
        await _repo.SaveCommentAsync(comment);
        return WorkResult<bool>.Success(true, "Comment removed.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<WorkItemDetailDto> BuildDetailAsync(WorkItem item)
    {
        var project = await _repo.GetProjectAsync(item.ProjectId);
        var projectKey = project?.Key ?? string.Empty;
        var children = await _repo.GetChildrenAsync(item.WorkItemId);
        var comments = await _repo.GetCommentsAsync(item.WorkItemId);
        var history = await _repo.GetHistoryAsync(item.WorkItemId);
        var parent = item.ParentId.HasValue ? await _repo.GetItemAsync(item.ParentId.Value) : null;

        var family = new List<WorkItem> { item };
        family.AddRange(children);
        if (parent != null) family.Add(parent);

        var people = PeopleOf(family);
        people.AddRange(comments.Where(c => c.AuthorUserId.HasValue).Select(c => c.AuthorUserId!.Value));
        people.AddRange(history.Where(h => h.ActorUserId.HasValue).Select(h => h.ActorUserId!.Value));
        var names = await _repo.GetUserNamesAsync(people.Distinct().ToList());
        var byId = family.ToDictionary(i => i.WorkItemId);
        var statuses = StatusMap(await EnsureStatusesAsync(item.ProjectId));

        var summary = MapItem(item, projectKey, names, byId, family, statuses);
        var detail = new WorkItemDetailDto
        {
            WorkItemId = summary.WorkItemId,
            ProjectId = summary.ProjectId,
            Key = summary.Key,
            Number = summary.Number,
            ItemType = summary.ItemType,
            Title = summary.Title,
            Status = summary.Status,
            StatusName = summary.StatusName,
            StatusCategory = summary.StatusCategory,
            Priority = summary.Priority,
            StoryPoints = summary.StoryPoints,
            AssigneeUserId = summary.AssigneeUserId,
            AssigneeName = summary.AssigneeName,
            ReporterUserId = summary.ReporterUserId,
            ReporterName = summary.ReporterName,
            ParentId = summary.ParentId,
            ParentKey = summary.ParentKey,
            ParentTitle = summary.ParentTitle,
            ParentType = summary.ParentType,
            SprintId = summary.SprintId,
            Labels = summary.Labels,
            StartDate = summary.StartDate,
            DueDate = summary.DueDate,
            IsBlocked = summary.IsBlocked,
            BlockedReason = summary.BlockedReason,
            Rank = summary.Rank,
            ChildCount = summary.ChildCount,
            DoneChildCount = summary.DoneChildCount,
            CreatedAt = summary.CreatedAt,
            UpdatedAt = summary.UpdatedAt,
            CompletedAt = summary.CompletedAt,
            Description = item.Description,
        };
        detail.Children = children
            .OrderBy(c => c.Rank).ThenBy(c => c.Number)
            .Select(c => MapItem(c, projectKey, names, byId, family, statuses))
            .ToList();
        detail.Comments = comments.OrderBy(c => c.CreatedAt).Select(c => MapComment(c, names)).ToList();
        detail.History = history
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new WorkHistoryDto
            {
                HistoryId = h.HistoryId,
                ActorName = NameOf(names, h.ActorUserId) ?? "Someone",
                Field = h.Field,
                OldValue = h.OldValue,
                NewValue = h.NewValue,
                CreatedAt = Utc(h.CreatedAt),
            })
            .ToList();
        return detail;
    }

    private static WorkItemDto MapItem(
        WorkItem i, string projectKey, Dictionary<Guid, string> names,
        Dictionary<Guid, WorkItem> byId, List<WorkItem> all, Dictionary<string, WorkStatus> statuses)
    {
        WorkItem? parent = null;
        if (i.ParentId.HasValue) byId.TryGetValue(i.ParentId.Value, out parent);
        var children = all.Where(c => c.ParentId == i.WorkItemId).ToList();
        WorkStatus? status = null;
        statuses.TryGetValue(i.Status, out status);

        return new WorkItemDto
        {
            WorkItemId = i.WorkItemId,
            ProjectId = i.ProjectId,
            Key = $"{projectKey}-{i.Number}",
            Number = i.Number,
            ItemType = i.ItemType,
            Title = i.Title,
            Status = i.Status,
            StatusName = status?.Name ?? i.Status,
            StatusCategory = status?.Category ?? "ToDo",
            Priority = i.Priority,
            StoryPoints = i.StoryPoints,
            AssigneeUserId = i.AssigneeUserId,
            AssigneeName = NameOf(names, i.AssigneeUserId),
            ReporterUserId = i.ReporterUserId,
            ReporterName = NameOf(names, i.ReporterUserId),
            ParentId = i.ParentId,
            ParentKey = parent == null ? null : $"{projectKey}-{parent.Number}",
            ParentTitle = parent?.Title,
            ParentType = parent?.ItemType,
            SprintId = i.SprintId,
            Labels = WorkRules.SplitLabels(i.Labels),
            StartDate = FormatDate(i.StartDate),
            DueDate = FormatDate(i.DueDate),
            IsBlocked = i.IsBlocked,
            BlockedReason = i.BlockedReason,
            Rank = i.Rank,
            ChildCount = children.Count,
            DoneChildCount = children.Count(c => IsDone(statuses, c.Status)),
            CreatedAt = Utc(i.CreatedAt),
            UpdatedAt = Utc(i.UpdatedAt),
            CompletedAt = i.CompletedAt.HasValue ? Utc(i.CompletedAt.Value) : (DateTime?)null,
        };
    }

    private static WorkCommentDto MapComment(WorkItemComment c, Dictionary<Guid, string> names) => new WorkCommentDto
    {
        CommentId = c.CommentId,
        AuthorUserId = c.AuthorUserId,
        AuthorName = NameOf(names, c.AuthorUserId) ?? "Someone",
        Body = c.Body,
        CreatedAt = Utc(c.CreatedAt),
    };

    private static WorkProjectDto MapProject(WorkProject p, List<WorkStatus> statuses) => new WorkProjectDto
    {
        ProjectId = p.ProjectId,
        Key = p.Key,
        Name = p.Name,
        Description = p.Description,
        SprintDays = p.SprintDays,
        IsArchived = p.IsArchived,
        IsRestricted = p.IsRestricted,
        Statuses = statuses.OrderBy(st => st.SortOrder).ThenBy(st => st.Name).Select(MapStatus).ToList(),
    };

    private static WorkStatusDto MapStatus(WorkStatus st) => new WorkStatusDto
    {
        StatusId = st.StatusId,
        Key = st.Key,
        Name = st.Name,
        Category = st.Category,
        SortOrder = st.SortOrder,
    };

    private static Dictionary<string, WorkStatus> StatusMap(List<WorkStatus> statuses)
    {
        var map = new Dictionary<string, WorkStatus>();
        foreach (var st in statuses) map[st.Key] = st;
        return map;
    }

    private static bool IsDone(Dictionary<string, WorkStatus> statuses, string statusKey) =>
        statuses.TryGetValue(statusKey, out var st) && st.Category == WorkRules.DoneCategory;

    private static WorkItemHistory NewHistory(Guid workItemId, Guid? actor, string field, string? before, string? after, DateTime at) =>
        new WorkItemHistory
        {
            HistoryId = Guid.NewGuid(),
            WorkItemId = workItemId,
            ActorUserId = actor,
            Field = field,
            OldValue = Shorten(before),
            NewValue = Shorten(after),
            CreatedAt = at,
        };

    private async Task<bool> IsKnownUserAsync(Guid userId)
    {
        var names = await _repo.GetUserNamesAsync(new[] { userId });
        return names.ContainsKey(userId);
    }

    private static List<Guid> PeopleOf(IEnumerable<WorkItem> items)
    {
        var ids = new List<Guid>();
        foreach (var i in items)
        {
            if (i.AssigneeUserId.HasValue) ids.Add(i.AssigneeUserId.Value);
            if (i.ReporterUserId.HasValue) ids.Add(i.ReporterUserId.Value);
        }
        return ids.Distinct().ToList();
    }

    private static string? NameOf(Dictionary<Guid, string> names, Guid? userId)
    {
        if (!userId.HasValue) return null;
        return names.TryGetValue(userId.Value, out var name) ? name : null;
    }

    private static string? JoinLabels(List<string> labels) => labels.Count == 0 ? null : string.Join(",", labels);

    private static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length > maxLength ? trimmed.Substring(0, maxLength) : trimmed;
    }

    private static string? Shorten(string? value)
    {
        if (value == null) return null;
        return value.Length > MaxHistoryValueLength ? value.Substring(0, MaxHistoryValueLength) : value;
    }

    /// <summary>Empty text is "no date" (true, null). Anything else must be yyyy-MM-dd.</summary>
    private static bool TryParseDate(string? text, out DateTime? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }
        return false;
    }

    private static string? FormatDate(DateTime? date) =>
        date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static string? FormatPoints(decimal? points) =>
        points.HasValue ? points.Value.ToString("0.#", CultureInfo.InvariantCulture) : null;

    /// <summary>The database stores UTC without saying so; mark it, so the web app shows local time correctly.</summary>
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
