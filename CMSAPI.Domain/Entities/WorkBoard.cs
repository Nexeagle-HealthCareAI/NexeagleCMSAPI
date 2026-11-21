namespace CMSAPI.Domain.Entities;

// Daily Work Board (CMSDatabase, script 26_CMSDatabase_WorkBoard.sql).
// The hierarchy is Epic -> Feature -> Story | Task | Bug -> Subtask; see WorkRules for the rules.

/// <summary>A product the team works on. Its Key prefixes item keys: HMS-142.</summary>
public class WorkProject
{
    public Guid ProjectId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>The number the next new item in this project receives.</summary>
    public int NextNumber { get; set; } = 1;
    public int SprintDays { get; set; } = 11;
    public bool IsArchived { get; set; }
    /// <summary>True: only its members (WorkProjectMember) and holders of work.members can open it.</summary>
    public bool IsRestricted { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Someone who may open a members-only project (28_CMSDatabase_WorkBoardAccess.sql).</summary>
public class WorkProjectMember
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public Guid? AddedByUserId { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>Who changed a project's access. Action: MemberAdded, MemberRemoved, Restricted or Opened.</summary>
public class WorkProjectAccessLog
{
    public Guid LogId { get; set; }
    public Guid ProjectId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? TargetUserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A column of a project's board. Items store Key, which never changes; Name is what people see and
/// can be renamed. Category says what the column means: not started, under way, or finished.
/// </summary>
public class WorkStatus
{
    public Guid StatusId { get; set; }
    public Guid ProjectId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>ToDo, InProgress or Done.</summary>
    public string Category { get; set; } = "ToDo";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A time box. The sprint screens come in the next release; items already carry SprintId.</summary>
public class WorkSprint
{
    public Guid SprintId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string State { get; set; } = "Planned";
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class WorkItem
{
    public Guid WorkItemId { get; set; }
    public Guid ProjectId { get; set; }
    public int Number { get; set; }
    public string ItemType { get; set; } = "Task";
    public string Title { get; set; } = string.Empty;
    /// <summary>Markdown from the editor.</summary>
    public string? Description { get; set; }
    /// <summary>The Key of one of the project's WorkStatus rows.</summary>
    public string Status { get; set; } = "ToDo";
    public string Priority { get; set; } = "Medium";
    public decimal? StoryPoints { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public Guid? ReporterUserId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? SprintId { get; set; }
    /// <summary>Comma-separated, lower case.</summary>
    public string? Labels { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    /// <summary>Order in the backlog; lower comes first.</summary>
    public double Rank { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class WorkItemComment
{
    public Guid CommentId { get; set; }
    public Guid WorkItemId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One changed field on an item.</summary>
public class WorkItemHistory
{
    public Guid HistoryId { get; set; }
    public Guid WorkItemId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>When a person was last on the board (29_CMSDatabase_WorkActivity.sql).</summary>
public class WorkPresence
{
    public Guid UserId { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? LastOpenedAt { get; set; }
    public DateTime? PreviousOpenedAt { get; set; }
    public string? PreviousIp { get; set; }
    public string? LastIp { get; set; }
    public string? LastUserAgent { get; set; }
}

/// <summary>
/// Someone opened the board, or the protected screen stopped something.
/// Kind: Opened, CopyBlocked, PrintBlocked, SaveBlocked or ScreenshotKey.
/// </summary>
public class WorkSessionLog
{
    public Guid LogId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
