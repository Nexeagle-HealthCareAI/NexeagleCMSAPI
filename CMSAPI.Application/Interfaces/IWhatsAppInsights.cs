using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// Read-only access to the WhatsApp booking bot's own database (WhatsAppBookingProd/Dev). Throws
/// WhatsAppBotUnavailableException when that database cannot be reached.
/// </summary>
public interface IWhatsAppBotStore
{
    /// <summary>Conversations that started in [fromUtc, toUtc).</summary>
    Task<List<BotSessionRow>> SessionsAsync(DateTime fromUtc, DateTime toUtc);
    Task<BotSessionRow?> SessionAsync(string sessionId);
    /// <summary>When each of these numbers first wrote to the bot, ever.</summary>
    Task<Dictionary<string, DateTime>> FirstSeenAsync(IReadOnlyCollection<string> phones);
    Task<List<BotNluRow>> NluAsync(IReadOnlyCollection<string> sessionIds);
    Task<List<BotBookingRow>> BookingsAsync(IReadOnlyCollection<Guid> bookingIds);
    Task<List<BotLastSearchRow>> LastSearchesAsync(IReadOnlyCollection<string> phones);
    /// <summary>(phone, started) of every conversation since fromUtc, for counting people per week.</summary>
    Task<List<(string Phone, DateTime StartedAt)>> StartsSinceAsync(DateTime fromUtc);
}

public class WhatsAppBotUnavailableException : Exception
{
    public WhatsAppBotUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Who wrote to the WhatsApp bot, what they looked for and how it ended; growth by week.</summary>
public interface IWhatsAppInsightsService
{
    Task<WhatsAppDailyStats> DailyAsync(DateOnly? from, DateOnly? to);
    /// <summary>showContacts: the reader holds insights.contacts (full numbers).</summary>
    Task<PagedResult<WhatsAppPersonRow>> PeopleAsync(WhatsAppPeopleQuery query, bool showContacts);
    Task<WhatsAppTranscript?> TranscriptAsync(string sessionId);
    Task<WeeklyGrowth> WeeklyGrowthAsync(int weeks);
    Task<DoctorDekhoDailyStats> DoctorDekhoDailyAsync(DateOnly? from, DateOnly? to);
}
