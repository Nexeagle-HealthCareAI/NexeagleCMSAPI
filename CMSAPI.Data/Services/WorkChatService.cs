using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>
/// Team chat of the Daily Work Board. See IWorkChatService. Like the other board services that are
/// mostly "which rows may this person see", it uses CmsDbContext directly.
///
/// Who is in a channel
///   Board   everyone who can open that board (IWorkAccessService). The channel is made the first
///           time someone who can open the board asks for their channels.
///   Group   the people listed in WorkChatMembers with IsMember.
///   Direct  the two people it was opened for.
/// </summary>
public class WorkChatService : IWorkChatService
{
    private const int UnreadWindowDays = 30;
    private const int PreviewLength = 90;
    private readonly CmsDbContext _db;
    private readonly IWorkAccessService _access;

    public WorkChatService(CmsDbContext db, IWorkAccessService access)
    {
        _db = db;
        _access = access;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static WorkResult<T> NotFound<T>(string what) => WorkResult<T>.Fail(404, what + " not found.");

    // ── Channels ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The channels this person is in. Makes the missing Board channels on the way.</summary>
    private async Task<List<WorkChatChannel>> VisibleChannelsAsync(WorkActor actor)
    {
        var closed = await _access.ClosedProjectIdsAsync(actor);
        var projectIds = (await _db.WorkProjects.AsNoTracking().Where(p => !p.IsArchived).Select(p => p.ProjectId).ToListAsync())
            .Where(id => !closed.Contains(id))
            .ToList();

        var boards = await _db.WorkChatChannels.Where(c => c.Kind == "Board").ToListAsync();
        var missing = projectIds.Where(id => boards.All(c => c.ProjectId != id)).ToList();
        if (missing.Count > 0)
        {
            var now = DateTime.UtcNow;
            var added = missing.Select(id => new WorkChatChannel { ChannelId = Guid.NewGuid(), Kind = "Board", ProjectId = id, CreatedAt = now }).ToList();
            _db.WorkChatChannels.AddRange(added);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Someone else made the same channel a moment ago: use theirs.
                foreach (var channel in added) _db.Entry(channel).State = EntityState.Detached;
            }
            boards = await _db.WorkChatChannels.Where(c => c.Kind == "Board").ToListAsync();
        }

        var list = boards.Where(c => c.ProjectId.HasValue && projectIds.Contains(c.ProjectId.Value)).ToList();
        if (actor.UserId.HasValue)
        {
            var me = actor.UserId.Value;
            var mine = await _db.WorkChatMembers.AsNoTracking().Where(m => m.UserId == me && m.IsMember).Select(m => m.ChannelId).ToListAsync();
            if (mine.Count > 0)
            {
                list.AddRange(await _db.WorkChatChannels.Where(c => c.Kind != "Board" && mine.Contains(c.ChannelId)).ToListAsync());
            }
        }
        return list;
    }

    private async Task<bool> CanSeeAsync(WorkChatChannel channel, WorkActor actor)
    {
        if (channel.Kind == "Board")
        {
            return channel.ProjectId.HasValue && await _access.CanOpenProjectAsync(channel.ProjectId.Value, actor);
        }
        if (!actor.UserId.HasValue) return false;
        var me = actor.UserId.Value;
        return await _db.WorkChatMembers.AsNoTracking().AnyAsync(m => m.ChannelId == channel.ChannelId && m.UserId == me && m.IsMember);
    }

    private async Task<WorkChatChannel?> FindVisibleAsync(Guid channelId, WorkActor actor)
    {
        var channel = await _db.WorkChatChannels.FirstOrDefaultAsync(c => c.ChannelId == channelId);
        if (channel == null) return null;
        return await CanSeeAsync(channel, actor) ? channel : null;
    }

    private async Task<List<WorkChatChannelDto>> BuildChannelsAsync(List<WorkChatChannel> channels, WorkActor actor)
    {
        var me = actor.UserId ?? Guid.Empty;
        var ids = channels.Select(c => c.ChannelId).ToList();
        var projects = await _db.WorkProjects.AsNoTracking().ToDictionaryAsync(p => p.ProjectId, p => p);
        var members = await _db.WorkChatMembers.AsNoTracking().Where(m => ids.Contains(m.ChannelId)).ToListAsync();
        var readAt = members.Where(m => m.UserId == me).ToDictionary(m => m.ChannelId, m => m.LastReadAt);

        // Unread: other people's messages newer than where this person has read to.
        var since = DateTime.UtcNow.AddDays(-UnreadWindowDays);
        var recent = await _db.WorkChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ChannelId) && !m.IsDeleted && m.AuthorUserId != me && m.CreatedAt > since)
            .Select(m => new { m.ChannelId, m.CreatedAt })
            .ToListAsync();

        // The newest message of each channel, for the preview line.
        var lastTimes = channels.Where(c => c.LastMessageAt.HasValue).Select(c => c.LastMessageAt!.Value).Distinct().ToList();
        var lastRows = lastTimes.Count == 0
            ? new List<WorkChatMessage>()
            : await _db.WorkChatMessages.AsNoTracking().Where(m => ids.Contains(m.ChannelId) && lastTimes.Contains(m.CreatedAt)).ToListAsync();

        var people = members.Where(m => m.IsMember).Select(m => m.UserId).ToList();
        people.AddRange(lastRows.Where(m => m.AuthorUserId.HasValue).Select(m => m.AuthorUserId!.Value));
        var names = await NamesAsync(people);

        var list = new List<WorkChatChannelDto>();
        foreach (var c in channels)
        {
            var inIt = members.Where(m => m.ChannelId == c.ChannelId && m.IsMember).Select(m => m.UserId).ToList();
            var dto = new WorkChatChannelDto { ChannelId = c.ChannelId, Kind = c.Kind, ProjectId = c.ProjectId, MemberUserIds = inIt };
            if (c.Kind == "Board")
            {
                var project = c.ProjectId.HasValue && projects.TryGetValue(c.ProjectId.Value, out var p) ? p : null;
                dto.Name = project?.Name ?? "Board";
                dto.ProjectKey = project?.Key;
            }
            else if (c.Kind == "Direct")
            {
                var other = inIt.FirstOrDefault(u => u != me);
                dto.OtherUserId = other == Guid.Empty ? (Guid?)null : other;
                dto.Name = names.TryGetValue(other, out var otherName) ? otherName : "Direct message";
            }
            else
            {
                dto.Name = c.Name ?? "Group";
            }

            var read = readAt.TryGetValue(c.ChannelId, out var at) ? at : null;
            dto.Unread = recent.Count(m => m.ChannelId == c.ChannelId && (!read.HasValue || m.CreatedAt > read.Value));

            var last = lastRows.Where(m => m.ChannelId == c.ChannelId && m.CreatedAt == c.LastMessageAt).FirstOrDefault();
            if (last != null)
            {
                dto.LastMessageAt = Utc(last.CreatedAt);
                dto.LastMessageBy = last.AuthorUserId.HasValue && names.TryGetValue(last.AuthorUserId.Value, out var by) ? by : null;
                dto.LastMessageText = last.IsDeleted ? "Message removed" : Preview(last.Body);
            }
            list.Add(dto);
        }

        // Boards first by key, then groups by name, then direct messages by name.
        return list
            .OrderBy(c => c.Kind == "Board" ? 0 : c.Kind == "Group" ? 1 : 2)
            .ThenBy(c => c.Kind == "Board" ? c.ProjectKey : c.Name)
            .ToList();
    }

    private static string Preview(string body)
    {
        var flat = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= PreviewLength ? flat : flat.Substring(0, PreviewLength) + "…";
    }

    public async Task<WorkChatStateDto> GetStateAsync(WorkActor actor, Guid? channelId, DateTime? after)
    {
        var channels = await VisibleChannelsAsync(actor);
        var state = new WorkChatStateDto { Channels = await BuildChannelsAsync(channels, actor) };

        if (channelId.HasValue && after.HasValue && channels.Any(c => c.ChannelId == channelId.Value))
        {
            var id = channelId.Value;
            var since = after.Value.ToUniversalTime();
            var rows = await _db.WorkChatMessages.AsNoTracking()
                .Where(m => m.ChannelId == id && m.CreatedAt > since)
                .OrderBy(m => m.CreatedAt)
                .Take(WorkChatRules.PageSize * 2)
                .ToListAsync();
            state.NewMessages = await BuildMessagesAsync(rows);
        }

        if (actor.UserId.HasValue)
        {
            var me = actor.UserId.Value;
            state.UnreadNotifications = await _db.WorkNotifications.AsNoTracking().CountAsync(n => n.UserId == me && !n.IsRead);
        }
        return state;
    }

    public async Task<WorkResult<WorkChatChannelDto>> CreateGroupAsync(WorkActor actor, CreateWorkChatGroupRequest request)
    {
        if (!actor.UserId.HasValue) return WorkResult<WorkChatChannelDto>.Fail(401, "Sign in again.");
        var me = actor.UserId.Value;
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) return WorkResult<WorkChatChannelDto>.Fail(400, "Give the group a name.");
        if (name.Length > WorkChatRules.MaxGroupName) return WorkResult<WorkChatChannelDto>.Fail(400, $"The name can be up to {WorkChatRules.MaxGroupName} characters.");

        var wanted = (request.MemberUserIds ?? new List<Guid>()).Where(id => id != me).Distinct().ToList();
        if (wanted.Count == 0) return WorkResult<WorkChatChannelDto>.Fail(400, "Choose at least one other person.");
        var known = await _db.CmsUsers.AsNoTracking().CountAsync(u => wanted.Contains(u.UserId) && u.IsActive);
        if (known != wanted.Count) return WorkResult<WorkChatChannelDto>.Fail(400, "One of the people chosen is not an active CMS user.");

        var now = DateTime.UtcNow;
        var channel = new WorkChatChannel { ChannelId = Guid.NewGuid(), Kind = "Group", Name = name, CreatedByUserId = me, CreatedAt = now };
        _db.WorkChatChannels.Add(channel);
        foreach (var userId in wanted.Append(me))
        {
            _db.WorkChatMembers.Add(new WorkChatMember { ChannelId = channel.ChannelId, UserId = userId, IsMember = true, JoinedAt = now });
        }
        await _db.SaveChangesAsync();
        return WorkResult<WorkChatChannelDto>.Success((await BuildChannelsAsync(new List<WorkChatChannel> { channel }, actor))[0], "Group created.");
    }

    public async Task<WorkResult<WorkChatChannelDto>> OpenDirectAsync(WorkActor actor, Guid otherUserId)
    {
        if (!actor.UserId.HasValue) return WorkResult<WorkChatChannelDto>.Fail(401, "Sign in again.");
        var me = actor.UserId.Value;
        if (otherUserId == me) return WorkResult<WorkChatChannelDto>.Fail(400, "Choose someone else to message.");
        if (!await _db.CmsUsers.AsNoTracking().AnyAsync(u => u.UserId == otherUserId && u.IsActive))
        {
            return WorkResult<WorkChatChannelDto>.Fail(400, "That person is not an active CMS user.");
        }

        var key = WorkChatRules.DirectKey(me, otherUserId);
        var channel = await _db.WorkChatChannels.FirstOrDefaultAsync(c => c.DirectKey == key);
        if (channel == null)
        {
            var now = DateTime.UtcNow;
            channel = new WorkChatChannel { ChannelId = Guid.NewGuid(), Kind = "Direct", DirectKey = key, CreatedByUserId = me, CreatedAt = now };
            _db.WorkChatChannels.Add(channel);
            _db.WorkChatMembers.Add(new WorkChatMember { ChannelId = channel.ChannelId, UserId = me, IsMember = true, JoinedAt = now });
            _db.WorkChatMembers.Add(new WorkChatMember { ChannelId = channel.ChannelId, UserId = otherUserId, IsMember = true, JoinedAt = now });
            await _db.SaveChangesAsync();
        }
        return WorkResult<WorkChatChannelDto>.Success((await BuildChannelsAsync(new List<WorkChatChannel> { channel }, actor))[0]);
    }

    // ── Messages ────────────────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<List<WorkChatMessageDto>>> GetMessagesAsync(WorkActor actor, Guid channelId, DateTime? before, bool importantOnly)
    {
        var channel = await FindVisibleAsync(channelId, actor);
        if (channel == null) return NotFound<List<WorkChatMessageDto>>("Conversation");

        var q = _db.WorkChatMessages.AsNoTracking().Where(m => m.ChannelId == channelId);
        if (importantOnly) q = q.Where(m => (m.IsImportant || m.IsEscalation) && !m.IsDeleted);
        if (before.HasValue)
        {
            var until = before.Value.ToUniversalTime();
            q = q.Where(m => m.CreatedAt < until);
        }
        var rows = await q.OrderByDescending(m => m.CreatedAt).Take(WorkChatRules.PageSize).ToListAsync();
        rows.Reverse();
        return WorkResult<List<WorkChatMessageDto>>.Success(await BuildMessagesAsync(rows));
    }

    public async Task<WorkResult<WorkChatMessageDto>> SendAsync(WorkActor actor, Guid channelId, SendWorkChatMessageRequest request)
    {
        if (!actor.UserId.HasValue) return WorkResult<WorkChatMessageDto>.Fail(401, "Sign in again.");
        var channel = await FindVisibleAsync(channelId, actor);
        if (channel == null) return NotFound<WorkChatMessageDto>("Conversation");
        return await SendToAsync(actor, channel, request);
    }

    private async Task<WorkResult<WorkChatMessageDto>> SendToAsync(WorkActor actor, WorkChatChannel channel, SendWorkChatMessageRequest request)
    {
        var me = actor.UserId!.Value;
        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0) return WorkResult<WorkChatMessageDto>.Fail(400, "Write a message first.");
        if (body.Length > WorkChatRules.MaxBody) return WorkResult<WorkChatMessageDto>.Fail(400, $"A message can be up to {WorkChatRules.MaxBody} characters.");

        var mentions = (request.MentionUserIds ?? new List<Guid>()).Where(id => id != me).Distinct().ToList();
        if (request.IsEscalation && mentions.Count == 0)
        {
            return WorkResult<WorkChatMessageDto>.Fail(400, "Tag at least one person to escalate to.");
        }
        var allowed = await PeopleInAsync(channel);
        if (mentions.Any(id => !allowed.Contains(id)))
        {
            return WorkResult<WorkChatMessageDto>.Fail(400, "Someone you tagged is not in this conversation.");
        }

        WorkItem? item = null;
        if (request.WorkItemId.HasValue)
        {
            var itemId = request.WorkItemId.Value;
            item = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(i => i.WorkItemId == itemId && !i.IsDeleted);
            if (item == null || !await _access.CanOpenProjectAsync(item.ProjectId, actor))
            {
                return WorkResult<WorkChatMessageDto>.Fail(400, "That work item was not found.");
            }
        }

        var now = DateTime.UtcNow;
        var message = new WorkChatMessage
        {
            MessageId = Guid.NewGuid(), ChannelId = channel.ChannelId, AuthorUserId = me, Body = body,
            IsImportant = request.IsImportant || request.IsEscalation, IsEscalation = request.IsEscalation,
            WorkItemId = item?.WorkItemId, CreatedAt = now,
        };
        _db.WorkChatMessages.Add(message);
        channel.LastMessageAt = now;

        var told = new HashSet<Guid>();
        foreach (var userId in mentions)
        {
            _db.WorkChatMentions.Add(new WorkChatMention { MessageId = message.MessageId, UserId = userId });
            Notify(userId, request.IsEscalation ? "Escalation" : "Mention", message, now);
            told.Add(userId);
        }
        if (channel.Kind == "Direct")
        {
            foreach (var userId in allowed.Where(id => id != me && !told.Contains(id))) Notify(userId, "Direct", message, now);
        }

        // The sender has read up to their own message.
        var marker = await _db.WorkChatMembers.FirstOrDefaultAsync(m => m.ChannelId == channel.ChannelId && m.UserId == me);
        if (marker == null)
        {
            _db.WorkChatMembers.Add(new WorkChatMember { ChannelId = channel.ChannelId, UserId = me, IsMember = channel.Kind != "Board", LastReadAt = now, JoinedAt = now });
        }
        else
        {
            marker.LastReadAt = now;
        }

        // An escalated work item says so in its own history, which also puts it in the activity log.
        if (request.IsEscalation && item != null)
        {
            var names = await NamesAsync(mentions);
            var to = string.Join(", ", mentions.Select(id => names.TryGetValue(id, out var n) ? n : "someone"));
            _db.WorkItemHistories.Add(new WorkItemHistory
            {
                HistoryId = Guid.NewGuid(), WorkItemId = item.WorkItemId, ActorUserId = me, Field = "escalated",
                NewValue = to.Length <= 400 ? to : to.Substring(0, 400), CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync();
        return WorkResult<WorkChatMessageDto>.Success((await BuildMessagesAsync(new List<WorkChatMessage> { message }))[0]);
    }

    private void Notify(Guid userId, string kind, WorkChatMessage message, DateTime at) =>
        _db.WorkNotifications.Add(new WorkNotification
        {
            NotificationId = Guid.NewGuid(), UserId = userId, Kind = kind, ChannelId = message.ChannelId,
            MessageId = message.MessageId, ActorUserId = message.AuthorUserId, CreatedAt = at,
        });

    /// <summary>Who can be tagged in a channel.</summary>
    private async Task<HashSet<Guid>> PeopleInAsync(WorkChatChannel channel)
    {
        if (channel.Kind != "Board")
        {
            return (await _db.WorkChatMembers.AsNoTracking().Where(m => m.ChannelId == channel.ChannelId && m.IsMember).Select(m => m.UserId).ToListAsync()).ToHashSet();
        }
        var projectId = channel.ProjectId ?? Guid.Empty;
        var restricted = await _db.WorkProjects.AsNoTracking().AnyAsync(p => p.ProjectId == projectId && p.IsRestricted);
        // An open board: every active person. A members-only board: its members.
        var ids = restricted
            ? await _db.WorkProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId).Select(m => m.UserId).ToListAsync()
            : await _db.CmsUsers.AsNoTracking().Where(u => u.IsActive).Select(u => u.UserId).ToListAsync();
        return ids.ToHashSet();
    }

    public async Task<WorkResult<WorkChatMessageDto>> EscalateItemAsync(WorkActor actor, Guid workItemId, EscalateWorkItemRequest request)
    {
        if (!actor.UserId.HasValue) return WorkResult<WorkChatMessageDto>.Fail(401, "Sign in again.");
        var item = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(i => i.WorkItemId == workItemId && !i.IsDeleted);
        if (item == null || !await _access.CanOpenProjectAsync(item.ProjectId, actor)) return NotFound<WorkChatMessageDto>("Work item");

        // Makes the board's channel if this is the first time it is used.
        var channel = (await VisibleChannelsAsync(actor)).FirstOrDefault(c => c.Kind == "Board" && c.ProjectId == item.ProjectId);
        if (channel == null) return NotFound<WorkChatMessageDto>("Conversation");

        var note = (request.Note ?? string.Empty).Trim();
        return await SendToAsync(actor, channel, new SendWorkChatMessageRequest
        {
            Body = note.Length > 0 ? note : "This needs attention.",
            IsEscalation = true,
            IsImportant = true,
            MentionUserIds = request.UserIds ?? new List<Guid>(),
            WorkItemId = item.WorkItemId,
        });
    }

    public async Task<WorkResult<WorkChatMessageDto>> MarkImportantAsync(WorkActor actor, Guid messageId, bool isImportant)
    {
        var message = await _db.WorkChatMessages.FirstOrDefaultAsync(m => m.MessageId == messageId && !m.IsDeleted);
        if (message == null || await FindVisibleAsync(message.ChannelId, actor) == null) return NotFound<WorkChatMessageDto>("Message");
        if (message.IsEscalation && !isImportant) return WorkResult<WorkChatMessageDto>.Fail(400, "An escalation always stays marked important.");
        message.IsImportant = isImportant;
        await _db.SaveChangesAsync();
        return WorkResult<WorkChatMessageDto>.Success((await BuildMessagesAsync(new List<WorkChatMessage> { message }))[0]);
    }

    public async Task<WorkResult<bool>> DeleteMessageAsync(WorkActor actor, Guid messageId, bool canRemoveAny)
    {
        var message = await _db.WorkChatMessages.FirstOrDefaultAsync(m => m.MessageId == messageId && !m.IsDeleted);
        if (message == null || await FindVisibleAsync(message.ChannelId, actor) == null) return NotFound<bool>("Message");
        if (message.AuthorUserId != actor.UserId && !canRemoveAny) return WorkResult<bool>.Fail(403, "You can only remove your own messages.");
        message.IsDeleted = true;
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true, "Message removed.");
    }

    public async Task<WorkResult<bool>> MarkReadAsync(WorkActor actor, Guid channelId)
    {
        if (!actor.UserId.HasValue) return WorkResult<bool>.Fail(401, "Sign in again.");
        var channel = await FindVisibleAsync(channelId, actor);
        if (channel == null) return NotFound<bool>("Conversation");

        var me = actor.UserId.Value;
        var now = DateTime.UtcNow;
        var marker = await _db.WorkChatMembers.FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == me);
        if (marker == null)
        {
            _db.WorkChatMembers.Add(new WorkChatMember { ChannelId = channelId, UserId = me, IsMember = false, LastReadAt = now, JoinedAt = now });
        }
        else
        {
            marker.LastReadAt = now;
        }
        foreach (var n in await _db.WorkNotifications.Where(n => n.UserId == me && n.ChannelId == channelId && !n.IsRead).ToListAsync())
        {
            n.IsRead = true;
        }
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true);
    }

    private async Task<List<WorkChatMessageDto>> BuildMessagesAsync(List<WorkChatMessage> rows)
    {
        var ids = rows.Select(m => m.MessageId).ToList();
        var mentions = await _db.WorkChatMentions.AsNoTracking().Where(x => ids.Contains(x.MessageId)).ToListAsync();
        var names = await NamesAsync(rows.Where(m => m.AuthorUserId.HasValue).Select(m => m.AuthorUserId!.Value));

        var itemIds = rows.Where(m => m.WorkItemId.HasValue && !m.IsDeleted).Select(m => m.WorkItemId!.Value).Distinct().ToList();
        var items = itemIds.Count == 0
            ? new Dictionary<Guid, WorkItem>()
            : await _db.WorkItems.AsNoTracking().Where(i => itemIds.Contains(i.WorkItemId)).ToDictionaryAsync(i => i.WorkItemId, i => i);
        var projectIds = items.Values.Select(i => i.ProjectId).Distinct().ToList();
        var keys = projectIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.WorkProjects.AsNoTracking().Where(p => projectIds.Contains(p.ProjectId)).ToDictionaryAsync(p => p.ProjectId, p => p.Key);

        return rows.Select(m =>
        {
            var dto = new WorkChatMessageDto
            {
                MessageId = m.MessageId,
                ChannelId = m.ChannelId,
                AuthorUserId = m.AuthorUserId,
                AuthorName = m.AuthorUserId.HasValue && names.TryGetValue(m.AuthorUserId.Value, out var name) ? name : "Someone",
                IsDeleted = m.IsDeleted,
                CreatedAt = Utc(m.CreatedAt),
            };
            // A removed message keeps its place and its author, nothing else.
            if (m.IsDeleted) return dto;

            dto.Body = m.Body;
            dto.IsImportant = m.IsImportant;
            dto.IsEscalation = m.IsEscalation;
            dto.MentionUserIds = mentions.Where(x => x.MessageId == m.MessageId).Select(x => x.UserId).ToList();
            if (m.WorkItemId.HasValue && items.TryGetValue(m.WorkItemId.Value, out var item) && !item.IsDeleted)
            {
                dto.WorkItemId = item.WorkItemId;
                dto.WorkItemKey = (keys.TryGetValue(item.ProjectId, out var key) ? key : "?") + "-" + item.Number;
                dto.WorkItemTitle = item.Title;
            }
            return dto;
        }).ToList();
    }

    // ── Notifications ───────────────────────────────────────────────────────────────────────────

    public async Task<List<WorkNotificationDto>> GetNotificationsAsync(Guid userId)
    {
        var rows = await _db.WorkNotifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();
        if (rows.Count == 0) return new List<WorkNotificationDto>();

        var messageIds = rows.Select(n => n.MessageId).Distinct().ToList();
        var messages = await _db.WorkChatMessages.AsNoTracking().Where(m => messageIds.Contains(m.MessageId)).ToDictionaryAsync(m => m.MessageId, m => m);
        var channelIds = rows.Select(n => n.ChannelId).Distinct().ToList();
        var channels = await _db.WorkChatChannels.AsNoTracking().Where(c => channelIds.Contains(c.ChannelId)).ToListAsync();
        var projects = await _db.WorkProjects.AsNoTracking().ToDictionaryAsync(p => p.ProjectId, p => p.Name);
        var names = await NamesAsync(rows.Where(n => n.ActorUserId.HasValue).Select(n => n.ActorUserId!.Value));

        string ChannelName(Guid channelId, string actorName)
        {
            var c = channels.FirstOrDefault(x => x.ChannelId == channelId);
            if (c == null) return "Chat";
            if (c.Kind == "Board") return c.ProjectId.HasValue && projects.TryGetValue(c.ProjectId.Value, out var p) ? p : "Board";
            return c.Kind == "Direct" ? actorName : c.Name ?? "Group";
        }

        return rows.Select(n =>
        {
            var actor = n.ActorUserId.HasValue && names.TryGetValue(n.ActorUserId.Value, out var name) ? name : "Someone";
            var message = messages.TryGetValue(n.MessageId, out var m) ? m : null;
            return new WorkNotificationDto
            {
                NotificationId = n.NotificationId,
                Kind = n.Kind,
                ChannelId = n.ChannelId,
                ChannelName = ChannelName(n.ChannelId, actor),
                MessageId = n.MessageId,
                ActorName = actor,
                Text = message == null || message.IsDeleted ? "Message removed" : Preview(message.Body),
                IsRead = n.IsRead,
                CreatedAt = Utc(n.CreatedAt),
            };
        }).ToList();
    }

    public async Task MarkNotificationsReadAsync(Guid userId, List<Guid> notificationIds)
    {
        var q = _db.WorkNotifications.Where(n => n.UserId == userId && !n.IsRead);
        if (notificationIds != null && notificationIds.Count > 0) q = q.Where(n => notificationIds.Contains(n.NotificationId));
        foreach (var n in await q.ToListAsync()) n.IsRead = true;
        await _db.SaveChangesAsync();
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _db.CmsUsers.AsNoTracking().Where(u => ids.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName);
    }
}
