using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// "Last seen", each person's own sign-in details, and the activity log of the Daily Work Board.
/// The log is read from what the board already records (item history, comments, access changes,
/// the documents log) together with visits and protected-screen events.
/// </summary>
public interface IWorkActivityService
{
    /// <summary>Called by the open board about once a minute. Returns the caller's own sign-in details.</summary>
    Task<WorkMyPresenceDto> TouchAsync(Guid userId, WorkPresenceRequest request, string? ip, string? userAgent);
    Task<List<WorkLastSeenDto>> GetLastSeenAsync();
    /// <summary>
    /// Newest first. Boards the reader may not open are left out; document events are included only
    /// when includeDocs is true (the reader holds docs.sensitive, as for the documents' own log).
    /// </summary>
    Task<List<WorkActivityDto>> GetActivityAsync(WorkActivityQuery query, WorkActor reader, bool includeDocs);
}
