using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>
/// What people searched for on Doctor Dekho (search_performed events in the HMS tracking tables)
/// and on the WhatsApp bot (its transcripts and language-model readings), sorted into the same
/// kinds: symptom, doctor's name, speciality, hospital, other words.
/// </summary>
public class SearchInsightsService : ISearchInsightsService
{
    private const string EventSearch = "search_performed";
    private const string EventBookingStep = "booking_step_reached";
    private const int TopPerKind = 10;

    private readonly IWhatsAppBotStore _bot;
    private readonly AppDbContext _db;

    public SearchInsightsService(IWhatsAppBotStore bot, AppDbContext db)
    {
        _bot = bot;
        _db = db;
    }

    private sealed class Item
    {
        public string Channel { get; init; } = string.Empty;
        public string Person { get; init; } = string.Empty;
        public DateTime At { get; init; }
        public DateOnly Day { get; init; }
        public FoundSearch Search { get; init; } = new FoundSearch();
        public int? Results { get; init; }
        public bool Booked { get; init; }
        public string? City { get; init; }
        public string? Phone { get; init; }
        public string? SessionId { get; init; }
    }

    private sealed record Gathered(List<Item> Items, bool WhatsAppAvailable, string? WhatsAppProblem);

    private async Task<Gathered> GatherAsync(DateOnly start, DateOnly end, string channel)
    {
        var items = new List<Item>();
        if (channel != SearchKinds.ChannelWhatsApp) items.AddRange(await DoctorDekhoAsync(start, end));

        var available = true;
        string? problem = null;
        if (channel != SearchKinds.ChannelDoctorDekho)
        {
            try
            {
                items.AddRange(await WhatsAppAsync(start, end));
            }
            catch (WhatsAppBotUnavailableException ex)
            {
                available = false;
                problem = ex.Message;
            }
        }
        return new Gathered(items, available, problem);
    }

    private async Task<List<Item>> DoctorDekhoAsync(DateOnly start, DateOnly end)
    {
        var startUtc = TrackingTime.StartUtc(start);
        var endUtc = TrackingTime.EndUtcExclusive(end);
        var events = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => !e.IsAutomated && e.EventType == EventSearch && e.OccurredAt >= startUtc && e.OccurredAt < endUtc)
            .Select(e => new { e.EventId, e.SessionId, e.OccurredAt, e.SpecialtyId, e.City, e.MetadataJson })
            .ToListAsync();
        if (events.Count == 0) return new List<Item>();

        // A search led to a booking when the same visit finished booking ("done") afterwards; a day
        // past the period is read so a late-evening search that booked after midnight still counts.
        // Booking steps are few next to searches, so they are read for the period and matched here.
        var doneUntil = endUtc.AddDays(1);
        var doneRows = (await _db.AnalyticsEvents.AsNoTracking()
                .Where(e => !e.IsAutomated && e.EventType == EventBookingStep && e.OccurredAt >= startUtc && e.OccurredAt < doneUntil && e.SessionId != null)
                .Select(e => new { e.SessionId, e.OccurredAt, e.MetadataJson })
                .ToListAsync())
            .Select(e => (Visit: e.SessionId!, At: e.OccurredAt, Json: e.MetadataJson)).ToList();
        var doneAt = doneRows.Where(r => IsDone(r.Json)).GroupBy(r => r.Visit).ToDictionary(g => g.Key, g => g.Max(r => r.At));

        var lookup = await LookupAsync();
        return events.Select(e =>
        {
            var meta = DoctorDekhoSearchMeta.Parse(e.MetadataJson);
            var kind = SearchKinds.ClassifyDoctorDekho(meta, e.SpecialtyId, lookup);
            var query = meta.Query?.Trim();
            var visit = e.SessionId ?? e.EventId.ToString();
            return new Item
            {
                Channel = SearchKinds.ChannelDoctorDekho,
                Person = "dd:" + visit,
                At = e.OccurredAt,
                Day = TrackingTime.IstDate(e.OccurredAt),
                Search = new FoundSearch
                {
                    Kind = kind,
                    Text = string.IsNullOrEmpty(query) ? (e.SpecialtyId == null ? null : SearchKinds.SpecialtyName(e.SpecialtyId)) : query,
                    Specialty = e.SpecialtyId,
                    At = e.OccurredAt,
                    Found = meta.ResultsCount.HasValue ? meta.ResultsCount > 0 : null,
                },
                Results = meta.ResultsCount,
                Booked = e.SessionId != null && doneAt.TryGetValue(e.SessionId, out var done) && done >= e.OccurredAt,
                City = e.City,
            };
        }).ToList();
    }

    private static bool IsDone(string? json) =>
        DoctorDekhoStep(json) == "done";

    private static string? DoctorDekhoStep(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(p.Name, "step", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return p.Value.GetString()?.Trim().ToLowerInvariant();
            }
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    private async Task<SearchLookup> LookupAsync()
    {
        var doctors = await (from d in _db.Doctors.AsNoTracking()
                             join u in _db.UserProfiles.AsNoTracking() on d.UserID equals u.UserID
                             select u.FullName).ToListAsync();
        var hospitals = await _db.Hospitals.AsNoTracking().Select(h => h.Name).ToListAsync();
        var specialties = await _db.Specializations.AsNoTracking().Where(s => s.IsActive).Select(s => s.Name).Distinct().ToListAsync();
        return new SearchLookup(doctors, hospitals, specialties);
    }

    private async Task<List<Item>> WhatsAppAsync(DateOnly start, DateOnly end)
    {
        var sessions = await _bot.SessionsAsync(TrackingTime.StartUtc(start), TrackingTime.EndUtcExclusive(end));
        if (sessions.Count == 0) return new List<Item>();
        var nlu = (await _bot.NluAsync(sessions.Select(s => s.SessionId).ToList()))
            .GroupBy(n => n.SessionId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<BotNluRow>)g.ToList());
        var places = (await _bot.LastSearchesAsync(sessions.Select(s => s.Phone).Distinct().ToList()))
            .GroupBy(p => p.Phone).ToDictionary(g => g.Key, g => g.First());

        var items = new List<Item>();
        foreach (var s in sessions)
        {
            var journey = WhatsAppJourneys.Classify(s);
            var found = SearchKinds.FromWhatsApp(journey, nlu.TryGetValue(s.SessionId, out var n) ? n : Array.Empty<BotNluRow>());
            foreach (var f in found)
            {
                var at = f.At ?? s.StartedAt;
                items.Add(new Item
                {
                    Channel = SearchKinds.ChannelWhatsApp,
                    Person = "wa:" + s.Phone,
                    At = at,
                    Day = TrackingTime.IstDate(at),
                    Search = f,
                    Booked = s.BookingId.HasValue,
                    City = places.TryGetValue(s.Phone, out var p) ? (p.City ?? p.LocationText)?.Trim() : null,
                    Phone = s.Phone,
                    SessionId = s.SessionId,
                });
            }
        }
        return items;
    }

    // ── The summary ─────────────────────────────────────────────────────────────────────────────

    public async Task<SearchInsights> SummaryAsync(DateOnly? from, DateOnly? to, string? channel)
    {
        var (start, end) = WhatsAppInsightsService.Range(from, to);
        var gathered = await GatherAsync(start, end, Channel(channel));
        var items = gathered.Items;

        var result = new SearchInsights
        {
            From = start.ToString("yyyy-MM-dd"),
            To = end.ToString("yyyy-MM-dd"),
            Searches = items.Count,
            People = items.Select(i => i.Person).Distinct().Count(),
            LedToBooking = items.Count(i => i.Booked),
            FoundNothing = items.Count(i => i.Search.Found == false),
            WhatsAppAvailable = gathered.WhatsAppAvailable,
            WhatsAppProblem = gathered.WhatsAppProblem,
        };

        result.Kinds = SearchKinds.All.Select(k =>
        {
            var of = items.Where(i => i.Search.Kind == k).ToList();
            return new SearchKindRow
            {
                Kind = k,
                Label = SearchKinds.Label(k),
                DoctorDekho = of.Count(i => i.Channel == SearchKinds.ChannelDoctorDekho),
                WhatsApp = of.Count(i => i.Channel == SearchKinds.ChannelWhatsApp),
                LedToBooking = of.Count(i => i.Booked),
                FoundNothing = of.Count(i => i.Search.Found == false),
            };
        }).ToList();

        var byDay = items.GroupBy(i => i.Day).ToDictionary(g => g.Key, g => g.ToList());
        for (var d = end; d >= start; d = d.AddDays(-1))
        {
            var day = byDay.TryGetValue(d, out var g) ? g : new List<Item>();
            result.Days.Add(new SearchDayRow
            {
                Date = d.ToString("yyyy-MM-dd"),
                DoctorDekho = day.Count(i => i.Channel == SearchKinds.ChannelDoctorDekho),
                WhatsApp = day.Count(i => i.Channel == SearchKinds.ChannelWhatsApp),
                Symptom = day.Count(i => i.Search.Kind == SearchKinds.Symptom),
                DoctorName = day.Count(i => i.Search.Kind == SearchKinds.DoctorName),
                Specialty = day.Count(i => i.Search.Kind == SearchKinds.Specialty),
                Hospital = day.Count(i => i.Search.Kind == SearchKinds.Hospital),
                Other = day.Count(i => i.Search.Kind == SearchKinds.Other),
            });
        }

        result.TopTerms = SearchKinds.All
            .SelectMany(k => Terms(items.Where(i => i.Search.Kind == k), i => TermOf(i.Search), k).Take(TopPerKind))
            .ToList();
        result.Specialties = Terms(items.Where(i => !string.IsNullOrWhiteSpace(i.Search.Specialty)), i => SearchKinds.SpecialtyName(i.Search.Specialty!), SearchKinds.Specialty,
                key: i => SpecialtyKey(i.Search.Specialty!))
            .Take(20).ToList();
        result.NothingFound = Terms(items.Where(i => i.Search.Found == false), i => TermOf(i.Search), null).Take(20).ToList();
        return result;
    }

    private static string TermOf(FoundSearch s)
    {
        if (s.Kind == SearchKinds.Specialty && !string.IsNullOrWhiteSpace(s.Specialty) && string.IsNullOrWhiteSpace(s.Text))
            return SearchKinds.SpecialtyName(s.Specialty);
        var text = SearchKinds.Tidy(s.Text ?? s.Specialty ?? "(nothing typed)");
        return s.Kind == SearchKinds.Specialty ? SearchKinds.SpecialtyName(text) : text;
    }

    /// <summary>One key for a speciality however it is written: "cardiology", "Cardiologist", "Cardiology (Heart)".</summary>
    public static string SpecialtyKey(string raw)
    {
        var name = SearchKinds.SpecialtyName(raw).ToLowerInvariant().Replace("ae", "e");
        var letters = new string(name.Where(char.IsLetter).ToArray()).Replace("ologists", "ology").Replace("ologist", "ology");
        return letters.Length <= 8 ? letters : letters[..8];
    }

    private static IEnumerable<SearchTermRow> Terms(IEnumerable<Item> items, Func<Item, string> name, string? kind, Func<Item, string>? key = null)
    {
        key ??= i => name(i).ToLowerInvariant();
        return items
            .GroupBy(key)
            .Select(g => new SearchTermRow
            {
                // The way most people wrote it.
                Term = g.GroupBy(name).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key,
                Kind = kind ?? g.GroupBy(i => i.Search.Kind).OrderByDescending(x => x.Count()).First().Key,
                DoctorDekho = g.Count(i => i.Channel == SearchKinds.ChannelDoctorDekho),
                WhatsApp = g.Count(i => i.Channel == SearchKinds.ChannelWhatsApp),
                LedToBooking = g.Count(i => i.Booked),
                FoundNothing = g.Count(i => i.Search.Found == false),
            })
            .OrderByDescending(t => t.Total).ThenBy(t => t.Term, StringComparer.OrdinalIgnoreCase);
    }

    private static string Channel(string? channel) => (channel ?? "all").Trim().ToLowerInvariant() switch
    {
        SearchKinds.ChannelDoctorDekho => SearchKinds.ChannelDoctorDekho,
        SearchKinds.ChannelWhatsApp => SearchKinds.ChannelWhatsApp,
        _ => "all",
    };

    // ── Every search, newest first ──────────────────────────────────────────────────────────────

    public async Task<PagedResult<SearchListRow>> ListAsync(SearchListQuery query, bool showContacts)
    {
        var (start, end) = WhatsAppInsightsService.Range(query.From, query.To);
        var limit = Math.Clamp(query.Limit, 1, WhatsAppInsightsService.MaxPageSize);
        var page = Math.Max(1, query.Page);
        IEnumerable<Item> items = (await GatherAsync(start, end, Channel(query.Channel))).Items;

        var kind = (query.Kind ?? "all").Trim().ToLowerInvariant();
        if (kind == "nothing") items = items.Where(i => i.Search.Found == false);
        else if (kind == "booked") items = items.Where(i => i.Booked);
        else if (SearchKinds.All.Contains(kind)) items = items.Where(i => i.Search.Kind == kind);

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var digits = new string(search.Where(char.IsDigit).ToArray());
            items = items.Where(i =>
                (i.Search.Text?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (i.Search.Specialty?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (i.City?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (digits.Length >= 4 && i.Phone != null && i.Phone.Contains(digits)));
        }

        var all = items.OrderByDescending(i => i.At).ToList();
        return new PagedResult<SearchListRow>
        {
            Data = all.Skip((page - 1) * limit).Take(limit).Select(i => new SearchListRow
            {
                At = i.At,
                Channel = i.Channel,
                Kind = i.Search.Kind,
                KindLabel = SearchKinds.Label(i.Search.Kind),
                Text = i.Search.Text,
                Specialty = i.Search.Specialty == null ? null : SearchKinds.SpecialtyName(i.Search.Specialty),
                Results = i.Results,
                Found = i.Search.Found,
                Booked = i.Booked,
                City = string.IsNullOrWhiteSpace(i.City) ? null : i.City,
                Phone = i.Phone == null ? null : showContacts ? WhatsAppJourneys.FormatPhone(i.Phone) : WhatsAppJourneys.MaskPhone(i.Phone),
                SessionId = i.SessionId,
            }).ToList(),
            Pagination = new PaginationInfo
            {
                CurrentPage = page,
                ItemsPerPage = limit,
                TotalItems = all.Count,
                TotalPages = (int)Math.Ceiling(all.Count / (double)limit),
            },
        };
    }
}
