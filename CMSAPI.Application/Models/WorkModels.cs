namespace CMSAPI.Application.Models;

// Daily Work Board: what the API sends and accepts, and the rules of the hierarchy.

/// <summary>Allowed values and the parent/child rules, in one place for the service and its tests.</summary>
public static class WorkRules
{
    public static readonly string[] ItemTypes = { "Epic", "Feature", "Story", "Task", "Bug", "Subtask" };
    /// <summary>What a board column means, whatever it is called.</summary>
    public static readonly string[] Categories = { "ToDo", "InProgress", "Done" };
    public const string DoneCategory = "Done";
    public const int MaxStatusNameLength = 40;
    public const int MaxStatusesPerProject = 12;

    /// <summary>The columns every new project starts with: key, name, category.</summary>
    public static readonly (string Key, string Name, string Category)[] DefaultStatuses =
    {
        ("ToDo", "To do", "ToDo"),
        ("InProgress", "In progress", "InProgress"),
        ("InReview", "In review", "InProgress"),
        ("Done", "Done", "Done"),
    };

    /// <summary>A key for a new column made from its name: letters and digits only, e.g. "Ready for QA" -> "ReadyForQa".</summary>
    public static string StatusKeyFrom(string name)
    {
        var key = new System.Text.StringBuilder();
        var startOfWord = true;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c)) { startOfWord = true; continue; }
            key.Append(startOfWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            startOfWord = false;
        }
        var result = key.ToString();
        if (result.Length > 30) result = result.Substring(0, 30);
        return result.Length == 0 ? "Column" : result;
    }
    public static readonly string[] Priorities = { "Highest", "High", "Medium", "Low", "Lowest" };

    public const int MaxTitleLength = 300;
    public const int MaxLabels = 10;
    public const int MaxLabelLength = 30;

    /// <summary>Which parent types a child type may sit under. An empty list means "no parent allowed".</summary>
    public static string[] AllowedParentTypes(string childType)
    {
        switch (childType)
        {
            case "Epic": return Array.Empty<string>();
            case "Feature": return new[] { "Epic" };
            case "Story":
            case "Task":
            case "Bug": return new[] { "Feature", "Epic" };
            case "Subtask": return new[] { "Story", "Task", "Bug" };
            default: return Array.Empty<string>();
        }
    }

    /// <summary>A subtask is always part of something; every other type can stand alone.</summary>
    public static bool ParentRequired(string childType) => childType == "Subtask";

    /// <summary>Null when the pairing is fine, otherwise a message for the person.</summary>
    public static string? ParentProblem(string childType, string? parentType)
    {
        if (parentType == null)
            return ParentRequired(childType) ? "A subtask needs a parent story, task or bug." : null;

        var allowed = AllowedParentTypes(childType);
        if (allowed.Length == 0)
            return $"{Article(childType)} {childType.ToLowerInvariant()} cannot have a parent.";
        if (Array.IndexOf(allowed, parentType) < 0)
            return $"{Article(childType)} {childType.ToLowerInvariant()} can only sit under: {string.Join(", ", allowed).ToLowerInvariant()}.";
        return null;
    }

    private static string Article(string word) => word.StartsWith("E", StringComparison.Ordinal) ? "An" : "A";

    /// <summary>Trims, lower-cases, removes blanks and repeats; at most MaxLabels are kept.</summary>
    public static List<string> CleanLabels(IEnumerable<string>? labels)
    {
        var result = new List<string>();
        if (labels == null) return result;
        foreach (var raw in labels)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var label = raw.Trim().ToLowerInvariant().Replace(",", " ");
            if (label.Length > MaxLabelLength) label = label.Substring(0, MaxLabelLength);
            if (!result.Contains(label)) result.Add(label);
            if (result.Count == MaxLabels) break;
        }
        return result;
    }

    public static List<string> SplitLabels(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new List<string>();
        return stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}

/// <summary>Outcome of a change: either Data, or a message with the HTTP status to send.</summary>
public class WorkResult<T>
{
    public bool Ok { get; private set; }
    public int Status { get; private set; } = 200;
    public string? Message { get; private set; }
    public T? Data { get; private set; }

    public static WorkResult<T> Success(T data, string? message = null) =>
        new WorkResult<T> { Ok = true, Data = data, Message = message };

    public static WorkResult<T> Fail(int status, string message) =>
        new WorkResult<T> { Ok = false, Status = status, Message = message };
}

public class WorkStatusDto
{
    public Guid StatusId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class WorkProjectDto
{
    public Guid ProjectId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SprintDays { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>True when only its members can open it.</summary>
    public bool IsRestricted { get; set; }
    /// <summary>The board's columns, in order.</summary>
    public List<WorkStatusDto> Statuses { get; set; } = new List<WorkStatusDto>();
}

public class WorkUserDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class WorkItemDto
{
    public Guid WorkItemId { get; set; }
    public Guid ProjectId { get; set; }
    /// <summary>Project key and number: HMS-142.</summary>
    public string Key { get; set; } = string.Empty;
    public int Number { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>The column's key. StatusName and StatusCategory describe it.</summary>
    public string Status { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StatusCategory { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public decimal? StoryPoints { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public Guid? ReporterUserId { get; set; }
    public string? ReporterName { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentKey { get; set; }
    public string? ParentTitle { get; set; }
    public string? ParentType { get; set; }
    public Guid? SprintId { get; set; }
    public List<string> Labels { get; set; } = new List<string>();
    /// <summary>yyyy-MM-dd, or null.</summary>
    public string? StartDate { get; set; }
    /// <summary>yyyy-MM-dd, or null.</summary>
    public string? DueDate { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public double Rank { get; set; }
    public int ChildCount { get; set; }
    public int DoneChildCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class WorkCommentDto
{
    public Guid CommentId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class WorkHistoryDto
{
    public Guid HistoryId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Everything the item page shows.</summary>
public class WorkItemDetailDto : WorkItemDto
{
    public string? Description { get; set; }
    public List<WorkItemDto> Children { get; set; } = new List<WorkItemDto>();
    public List<WorkCommentDto> Comments { get; set; } = new List<WorkCommentDto>();
    public List<WorkHistoryDto> History { get; set; } = new List<WorkHistoryDto>();
}

public class CreateWorkProjectRequest
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateWorkItemRequest
{
    public Guid ProjectId { get; set; }
    public string ItemType { get; set; } = "Task";
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Priority { get; set; }
    public decimal? StoryPoints { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public Guid? ParentId { get; set; }
    public List<string>? Labels { get; set; }
    /// <summary>yyyy-MM-dd, or null.</summary>
    public string? StartDate { get; set; }
    /// <summary>yyyy-MM-dd, or null.</summary>
    public string? DueDate { get; set; }
}

/// <summary>
/// The full editable state of an item. The web app sends every field each time, so a null here means
/// "clear it" (no assignee, no parent, no due date), never "leave as it was".
/// </summary>
public class UpdateWorkItemRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "ToDo";
    public string Priority { get; set; } = "Medium";
    public decimal? StoryPoints { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? SprintId { get; set; }
    public List<string>? Labels { get; set; }
    public string? StartDate { get; set; }
    public string? DueDate { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
}

public class AddWorkCommentRequest
{
    public string Body { get; set; } = string.Empty;
}

/// <summary>A board column to add or change.</summary>
public class SaveWorkStatusRequest
{
    public string Name { get; set; } = string.Empty;
    /// <summary>ToDo, InProgress or Done.</summary>
    public string Category { get; set; } = "InProgress";
}

/// <summary>Every column of the project, in the order they should appear.</summary>
public class ReorderWorkStatusesRequest
{
    public List<Guid> StatusIds { get; set; } = new List<Guid>();
}

// ── Who can open a board ────────────────────────────────────────────────────────────────────────

/// <summary>The person asking, for the "may they open this board" check.</summary>
public class WorkActor
{
    public Guid? UserId { get; set; }
    /// <summary>work.members: opens every board and chooses its members.</summary>
    public bool SeesAllBoards { get; set; }
}

public class WorkAccessLogDto
{
    /// <summary>MemberAdded, MemberRemoved, Restricted or Opened.</summary>
    public string Action { get; set; } = string.Empty;
    public string? TargetName { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class WorkProjectAccessDto
{
    public Guid ProjectId { get; set; }
    public bool IsRestricted { get; set; }
    public List<Guid> MemberUserIds { get; set; } = new List<Guid>();
    /// <summary>Newest first, the last 50.</summary>
    public List<WorkAccessLogDto> Log { get; set; } = new List<WorkAccessLogDto>();
}

/// <summary>The whole access state of one board: the switch and every member.</summary>
public class SaveWorkProjectAccessRequest
{
    public bool IsRestricted { get; set; }
    public List<Guid> MemberUserIds { get; set; } = new List<Guid>();
}
