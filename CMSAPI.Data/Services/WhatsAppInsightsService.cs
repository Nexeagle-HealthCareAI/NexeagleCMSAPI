using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>
/// The WhatsApp bot, Doctor Dekho and nexeagle.com, day by day and week by week. WhatsApp figures
/// come from the bot's own database (IWhatsAppBotStore); website figures from the tracking tables
/// in the HMS database (AppDbContext), counted the same way as the Site Visits report: people only,
/// one visit per browser session, India-time days.
/// </summary>
public class WhatsAppInsightsService : IWhatsAppInsightsService
{
    /// <summary>Longest period the day-by-day and people reports read at once.</summary>
    public const int MaxDays = 92;
    public const int MaxWeeks = 52;
    public const int MaxPageSize = 1000;

    private const string SiteDoctorDekho = "doctordekho";
    private const string SiteNexeagle = "nexeagle";
    private const string EventSearch = "search_performed";
    private const string EventProfileView = "doctor_profile_viewed";
    private const string EventBookingStep = "booking_step_reached";

    private readonly IWhatsAppBotStore _bot;
    private readonly AppDbContext _db;

    public WhatsAppInsightsService(IWhatsAppBotStore bot, AppDbContext db)
    {
        _bot = bot;
        _db = db;
    }

    /// <summary>Inclusive India-time days; default the last 7 days; at most MaxDays.</summary>
    public static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to, int defaultDays = 7)
    {
        var end = to ?? TrackingTime.TodayIst();
        var start = from ?? end.AddDays(-(defaultDays - 1));
        if (start > end) (start, end) = (end, start);
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) start = end.AddDays(-(MaxDays - 1));
        return (start, end);
    }

    private sealed record Classified(BotSessionRow Row, BotJourney Journey, DateOnly Day);

    private async Task<List<Classified>> ClassifiedAsync(DateOnly from, DateOnly to)
    {
        var rows = await _bot.SessionsAsync(TrackingTime.StartUtc(from), TrackingTime.EndUtcExclusive(to));
        return rows.Select(r => new Classified(r, WhatsAppJourneys.Classify(r), TrackingTime.IstDate(r.StartedAt))).ToList();
    }

    // ── WhatsApp: day by day ────────────────────────────────────────────────────────────────────

    public async Task<WhatsAppDailyStats> DailyAsync(DateOnly? from, DateOnly? to)
    {
        var (start, end) = Range(from, to);
        var sessions = await ClassifiedAsync(start, end);
        var firstSeen = await _bot.FirstSeenAsync(sessions.Select(s => s.Row.Phone).Distinct().ToList());

        WhatsAppDailyRow Count(string date, IReadOnlyCollection<Classified> group)
        {
            var people = group.Select(s => s.Row.Phone).Distinct().ToList();
            return new WhatsAppDailyRow
            {
                Date = date,
                Conversations = group.Count,
                People = people.Count,
                // New: their first conversation ever falls inside this group.
                NewPeople = people.Count(p => firstSeen.TryGetValue(p, out var first) && group.Any(s => s.Row.Phone == p && s.Row.StartedAt == first)),
                FromQr = group.Count(s => WhatsAppJourneys.IsQr(s.Journey.Source)),
                Booked = group.Count(s => s.Journey.Outcome == WhatsAppJourneys.OutcomeBooked),
                Left = group.Count(s => s.Journey.Outcome == WhatsAppJourneys.OutcomeLeft),
                Other = group.Count(s => s.Journey.Outcome == WhatsAppJourneys.OutcomeOther),
            };
        }

        var byDay = sessions.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => (IReadOnlyCollection<Classified>)g.ToList());
        var days = new List<WhatsAppDailyRow>();
        for (var d = end; d >= start; d = d.AddDays(-1))
        {
            days.Add(Count(d.ToString("yyyy-MM-dd"), byDay.TryGetValue(d, out var g) ? g : Array.Empty<Classified>()));
        }

        var nlu = await _bot.NluAsync(sessions.Select(s => s.Row.SessionId).ToList());
        var searches = nlu.SelectMany(n => new[] { n.Specialty, n.Symptom, n.DoctorName is { } doc ? "Dr " + StripDr(doc) : null })
            .Concat(sessions.SelectMany(s => s.Journey.TypedSearches))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Tidy(x!))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NamedCount { Name = g.First(), Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(15)
            .ToList();

        var doctorNames = await DoctorNamesAsync(sessions.Where(s => s.Journey.Source == WhatsAppJourneys.SourceDoctorQr).Select(s => s.Journey.SourceCode));
        var qr = sessions.Where(s => WhatsAppJourneys.IsQr(s.Journey.Source))
            .GroupBy(s => SourceLabel(s.Journey, doctorNames))
            .Select(g => new NamedCount { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .ToList();

        var leftAt = sessions.Where(s => s.Journey.Outcome == WhatsAppJourneys.OutcomeLeft)
            .GroupBy(s => s.Journey.Stage)
            .OrderBy(g => g.Key)
            .Select(g => new NamedCount { Name = WhatsAppJourneys.LabelOfStage(g.Key), Count = g.Count() })
            .ToList();

        return new WhatsAppDailyStats
        {
            From = start.ToString("yyyy-MM-dd"),
            To = end.ToString("yyyy-MM-dd"),
            Totals = Count("total", sessions),
            Days = days,
            LeftAt = leftAt,
            TopSearches = searches,
            QrSources = qr,
        };
    }

    // ── WhatsApp: the people ────────────────────────────────────────────────────────────────────

    public async Task<PagedResult<WhatsAppPersonRow>> PeopleAsync(WhatsAppPeopleQuery query, bool showContacts)
    {
        var (start, end) = Range(query.From, query.To);
        var limit = Math.Clamp(query.Limit, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);

        var sessions = await ClassifiedAsync(start, end);
        var outcome = (query.Outcome ?? "all").Trim().ToLowerInvariant();
        sessions = outcome switch
        {
            "booked" => sessions.Where(s => s.Journey.Outcome == WhatsAppJourneys.OutcomeBooked).ToList(),
            "left" => sessions.Where(s => s.Journey.Outcome == WhatsAppJourneys.OutcomeLeft).ToList(),
            "other" => sessions.Where(s => s.Journey.Outcome == WhatsAppJourneys.OutcomeOther).ToList(),
            _ => sessions,
        };
        var source = (query.Source ?? "all").Trim().ToLowerInvariant();
        if (source == "qr") sessions = sessions.Where(s => WhatsAppJourneys.IsQr(s.Journey.Source)).ToList();
        else if (source == "direct") sessions = sessions.Where(s => !WhatsAppJourneys.IsQr(s.Journey.Source)).ToList();

        // Details are needed for every row the search could match, so they are read before paging.
        var rows = await DescribeAsync(sessions, showContacts);

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var digits = new string(search.Where(char.IsDigit).ToArray());
            var phoneOf = sessions.ToDictionary(s => s.Row.SessionId, s => s.Row.Phone);
            rows = rows.Where(r =>
                    // A number is matched on its digits; people without insights.contacts can still find
                    // someone whose full number they already have.
                    (digits.Length >= 4 && phoneOf[r.SessionId].Contains(digits))
                    || Has(r.PatientName, search) || Has(r.BookedDoctor, search) || Has(r.BookedHospital, search)
                    || Has(r.SourceLabel, search) || Has(r.City, search) || r.Searched.Any(x => Has(x, search)))
                .ToList();
        }

        var total = rows.Count;
        return new PagedResult<WhatsAppPersonRow>
        {
            Data = rows.Skip((page - 1) * limit).Take(limit).ToList(),
            Pagination = new PaginationInfo
            {
                CurrentPage = page,
                ItemsPerPage = limit,
                TotalItems = total,
                TotalPages = (int)Math.Ceiling(total / (double)limit),
            },
        };
    }

    private static bool Has(string? text, string search) => text != null && text.Contains(search, StringComparison.OrdinalIgnoreCase);

    private async Task<List<WhatsAppPersonRow>> DescribeAsync(List<Classified> sessions, bool showContacts)
    {
        if (sessions.Count == 0) return new List<WhatsAppPersonRow>();
        var phones = sessions.Select(s => s.Row.Phone).Distinct().ToList();
        var firstSeen = await _bot.FirstSeenAsync(phones);
        var nlu = (await _bot.NluAsync(sessions.Select(s => s.Row.SessionId).ToList()))
            .GroupBy(n => n.SessionId).ToDictionary(g => g.Key, g => g.ToList());
        var bookings = (await _bot.BookingsAsync(sessions.Where(s => s.Row.BookingId.HasValue).Select(s => s.Row.BookingId!.Value).ToList()))
            .ToDictionary(b => b.Id);
        var lastSearch = (await _bot.LastSearchesAsync(phones)).GroupBy(x => x.Phone).ToDictionary(g => g.Key, g => g.First());

        var doctorNames = await DoctorNamesAsync(sessions.Where(s => s.Journey.Source == WhatsAppJourneys.SourceDoctorQr).Select(s => s.Journey.SourceCode));
        var hospitalNames = await HospitalNamesAsync(bookings.Values.Select(b => b.HospitalId));

        return sessions
            .OrderByDescending(s => s.Row.StartedAt)
            .Select(s =>
            {
                var j = s.Journey;
                var booking = s.Row.BookingId.HasValue && bookings.TryGetValue(s.Row.BookingId.Value, out var b) ? b : null;
                var found = nlu.TryGetValue(s.Row.SessionId, out var n) ? n : new List<BotNluRow>();
                var searched = found.SelectMany(x => new[] { x.Symptom, x.Specialty, x.DoctorName is { } doc ? "Dr " + StripDr(doc) : null })
                    .Concat(j.TypedSearches)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => Tidy(x!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();
                var place = lastSearch.TryGetValue(s.Row.Phone, out var ls) ? (ls.City ?? ls.LocationText) : null;
                var lastStep = j.Events.LastOrDefault(e => e.Direction == "out")?.Content ?? s.Row.LastStep;

                return new WhatsAppPersonRow
                {
                    SessionId = s.Row.SessionId,
                    StartedAt = s.Row.StartedAt,
                    LastActivityAt = s.Row.LastActivityAt,
                    Phone = showContacts ? WhatsAppJourneys.FormatPhone(s.Row.Phone) : WhatsAppJourneys.MaskPhone(s.Row.Phone),
                    PhoneDigits = showContacts ? new string(s.Row.Phone.Where(char.IsDigit).ToArray()) : string.Empty,
                    IsNewPerson = firstSeen.TryGetValue(s.Row.Phone, out var first) && first == s.Row.StartedAt,
                    Source = j.Source,
                    SourceLabel = SourceLabel(j, doctorNames),
                    Outcome = j.Outcome,
                    Stage = j.Stage,
                    StageLabel = j.StageLabel,
                    LastStepLabel = WhatsAppJourneys.LabelOfStep(lastStep),
                    Searched = searched,
                    City = string.IsNullOrWhiteSpace(place) ? null : place.Trim(),
                    BookedDoctor = booking?.DoctorName is { } dn ? "Dr " + StripDr(dn) : null,
                    BookedHospital = booking?.HospitalId is { } hid && hospitalNames.TryGetValue(hid, out var hn) ? hn : null,
                    BookedFor = booking?.BookingFor,
                    PatientName = booking?.PatientName,
                    AppointmentDate = booking?.PreferredDate?.ToString("yyyy-MM-dd"),
                    Messages = j.Events.Count(e => e.Direction == "in"),
                };
            })
            .ToList();
    }

    private static string StripDr(string name)
    {
        var t = name.Trim();
        return t.StartsWith("dr.", StringComparison.OrdinalIgnoreCase) ? t[3..].Trim()
            : t.StartsWith("dr ", StringComparison.OrdinalIgnoreCase) ? t[3..].Trim()
            : t;
    }

    private static string Tidy(string text)
    {
        var t = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= 60 ? t : t[..60] + "…";
    }

    private static string SourceLabel(BotJourney j, IReadOnlyDictionary<string, string> doctorNames) => j.Source switch
    {
        WhatsAppJourneys.SourceHospitalQr => "Hospital QR · " + (j.SourceName ?? j.SourceCode ?? "unknown"),
        WhatsAppJourneys.SourceDoctorQr => "Doctor QR · " + (j.SourceCode != null && doctorNames.TryGetValue(j.SourceCode, out var name) ? "Dr " + StripDr(name) : j.SourceCode ?? "unknown"),
        WhatsAppJourneys.SourceCheckinQr => "Check-in QR · " + (j.SourceCode ?? "unknown"),
        WhatsAppJourneys.SourceDocumentQr => "Document QR · " + (j.SourceName ?? ""),
        _ => "Messaged directly",
    };

    private async Task<Dictionary<string, string>> DoctorNamesAsync(IEnumerable<string?> ids)
    {
        var guids = ids.Where(x => Guid.TryParse(x, out _)).Select(x => Guid.Parse(x!)).Distinct().ToList();
        if (guids.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = await (from d in _db.Doctors.AsNoTracking()
                          join u in _db.UserProfiles.AsNoTracking() on d.UserID equals u.UserID
                          where guids.Contains(d.DoctorID)
                          select new { d.DoctorID, u.FullName }).ToListAsync();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) map[r.DoctorID.ToString()] = r.FullName;
        return map;
    }

    private async Task<Dictionary<string, string>> HospitalNamesAsync(IEnumerable<string?> ids)
    {
        var guids = ids.Where(x => Guid.TryParse(x, out _)).Select(x => Guid.Parse(x!)).Distinct().ToList();
        if (guids.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = await _db.Hospitals.AsNoTracking().Where(h => guids.Contains(h.HospitalID)).Select(h => new { h.HospitalID, h.Name }).ToListAsync();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) map[r.HospitalID.ToString()] = r.Name;
        return map;
    }

    public async Task<WhatsAppTranscript?> TranscriptAsync(string sessionId)
    {
        var row = await _bot.SessionAsync(sessionId);
        if (row == null) return null;
        var events = WhatsAppJourneys.ParseTranscript(row.TranscriptJson);
        return new WhatsAppTranscript
        {
            SessionId = row.SessionId,
            Phone = WhatsAppJourneys.FormatPhone(row.Phone),
            Lines = events.Select(e => e.Direction == "in"
                    ? new WhatsAppTranscriptLine
                    {
                        From = "person",
                        // Buttons and lists arrive as ids; say what kind of answer it was when there is no text.
                        Text = string.IsNullOrWhiteSpace(e.Content) ? $"({e.Type ?? "message"})" : e.Content!,
                        StepLabel = WhatsAppJourneys.LabelOfStep(e.Step),
                        At = e.At,
                    }
                    : new WhatsAppTranscriptLine
                    {
                        From = "bot",
                        Text = "Moved to: " + WhatsAppJourneys.LabelOfStep(e.Step ?? e.Content),
                        StepLabel = WhatsAppJourneys.LabelOfStep(e.Step ?? e.Content),
                        At = e.At,
                    })
                .ToList(),
        };
    }

    // ── Growth by week ──────────────────────────────────────────────────────────────────────────

    public async Task<WeeklyGrowth> WeeklyGrowthAsync(int weeks)
    {
        weeks = Math.Clamp(weeks, 2, MaxWeeks);
        var thisWeek = WhatsAppJourneys.WeekStart(TrackingTime.TodayIst());
        var firstWeek = thisWeek.AddDays(-7 * (weeks - 1));
        var fromUtc = TrackingTime.StartUtc(firstWeek);

        // Websites: one visit per browser session, counted in the week the visit started.
        var visits = await _db.WebsiteVisits.AsNoTracking()
            .Where(v => !v.IsAutomated && v.VisitedAt >= fromUtc && (v.Site == SiteDoctorDekho || v.Site == SiteNexeagle))
            .GroupBy(v => new { Visit = v.SessionId ?? v.VisitId.ToString(), v.Site })
            .Select(g => new { g.Key.Site, StartedAt = g.Min(v => v.VisitedAt) })
            .ToListAsync();
        var siteWeeks = visits
            .GroupBy(v => (v.Site, Week: WhatsAppJourneys.WeekStart(TrackingTime.IstDate(v.StartedAt))))
            .ToDictionary(g => g.Key, g => g.Count());

        var growth = new WeeklyGrowth();
        Dictionary<DateOnly, int>? botWeeks = null;
        try
        {
            botWeeks = (await _bot.StartsSinceAsync(fromUtc))
                .GroupBy(s => WhatsAppJourneys.WeekStart(TrackingTime.IstDate(s.StartedAt)))
                .ToDictionary(g => g.Key, g => g.Select(s => s.Phone).Distinct().Count());
            growth.WhatsAppAvailable = true;
        }
        catch (WhatsAppBotUnavailableException ex)
        {
            growth.WhatsAppProblem = ex.Message;
        }

        for (var w = firstWeek; w <= thisWeek; w = w.AddDays(7))
        {
            growth.Weeks.Add(new WeeklyGrowthRow
            {
                WeekStart = w.ToString("yyyy-MM-dd"),
                WhatsAppPeople = botWeeks == null ? null : botWeeks.GetValueOrDefault(w),
                NexeagleVisits = siteWeeks.GetValueOrDefault((SiteNexeagle, w)),
                DoctorDekhoVisits = siteWeeks.GetValueOrDefault((SiteDoctorDekho, w)),
                IsPartial = w == thisWeek,
            });
        }
        return growth;
    }

    // ── Doctor Dekho: day by day ────────────────────────────────────────────────────────────────

    public async Task<DoctorDekhoDailyStats> DoctorDekhoDailyAsync(DateOnly? from, DateOnly? to)
    {
        var (start, end) = Range(from, to);
        var startUtc = TrackingTime.StartUtc(start);
        var endUtc = TrackingTime.EndUtcExclusive(end);

        var visits = await _db.WebsiteVisits.AsNoTracking()
            .Where(v => !v.IsAutomated && v.Site == SiteDoctorDekho && v.VisitedAt >= startUtc && v.VisitedAt < endUtc)
            .GroupBy(v => v.SessionId ?? v.VisitId.ToString())
            .Select(g => g.Min(v => v.VisitedAt))
            .ToListAsync();

        // The patient journey (search, profile, booking steps) is tracked on Doctor Dekho. A visit
        // counts once a day for each thing it did; "booked" is the site's own "done" step, so
        // WhatsApp bookings (which also land as online bookings in the HMS) are not counted here.
        var events = (await _db.AnalyticsEvents.AsNoTracking()
                .Where(e => !e.IsAutomated && e.OccurredAt >= startUtc && e.OccurredAt < endUtc
                            && (e.EventType == EventSearch || e.EventType == EventProfileView || e.EventType == EventBookingStep))
                .Select(e => new { e.EventId, e.EventType, e.SessionId, e.OccurredAt, e.MetadataJson })
                .ToListAsync())
            .Select(e => new
            {
                Day = TrackingTime.IstDate(e.OccurredAt),
                e.EventType,
                Visit = e.SessionId ?? e.EventId.ToString(),
                Step = e.EventType == EventBookingStep ? BookingStep(e.MetadataJson) : null,
            })
            .ToList();

        int Visitors(IEnumerable<DateOnly> days, DateOnly? day) => day == null ? days.Count() : days.Count(d => d == day);
        int VisitsThat(DateOnly? day, Func<string, string?, bool> match) => events
            .Where(e => (day == null || e.Day == day) && match(e.EventType, e.Step))
            .Select(e => e.Visit).Distinct().Count();

        DoctorDekhoDailyRow Row(DateOnly? day)
        {
            var started = VisitsThat(day, (t, s) => t == EventBookingStep && s == "visit");
            var booked = VisitsThat(day, (t, s) => t == EventBookingStep && s == "done");
            return new DoctorDekhoDailyRow
            {
                Date = day?.ToString("yyyy-MM-dd") ?? "total",
                Visits = Visitors(visits.Select(TrackingTime.IstDate), day),
                Searched = VisitsThat(day, (t, _) => t == EventSearch),
                ViewedDoctor = VisitsThat(day, (t, _) => t == EventProfileView),
                StartedBooking = started,
                EnteredDetails = VisitsThat(day, (t, s) => t == EventBookingStep && s == "details"),
                Booked = booked,
                Left = Math.Max(0, started - booked),
            };
        }

        var days = new List<DoctorDekhoDailyRow>();
        for (var d = end; d >= start; d = d.AddDays(-1)) days.Add(Row(d));
        return new DoctorDekhoDailyStats
        {
            From = start.ToString("yyyy-MM-dd"),
            To = end.ToString("yyyy-MM-dd"),
            Totals = Row(null),
            Days = days,
        };
    }

    private static string? BookingStep(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(p.Name, "step", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return p.Value.GetString()?.Trim().ToLowerInvariant();
            }
        }
        catch (System.Text.Json.JsonException) { }
        catch (InvalidOperationException) { }
        return null;
    }
}
