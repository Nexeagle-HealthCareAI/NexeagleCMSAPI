#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// What people searched for on Doctor Dekho and the WhatsApp bot: telling a symptom from a doctor's
// name, a speciality or a hospital, and the figures built on that.
public class SearchInsightsTests
{
    private static readonly SearchLookup Lookup = new(
        new[] { "Dr. Avinash Kumar", "Neha Sharma" },
        new[] { "City Care Hospital", "Max Super Speciality" },
        new[] { "Cardiology", "Dermatology", "Orthopaedics", "General Medicine", "Critical Care" });

    // ── Doctor Dekho: the kind of one search ──

    [Theory]
    [InlineData("dr avinash", false, "doctor")]
    [InlineData("Neha Sharma", false, "doctor")]
    [InlineData("city care", false, "hospital")]
    [InlineData("cardiologist", false, "specialty")]
    [InlineData("ortho", false, "specialty")]
    [InlineData("pet me dard", true, "symptom")]
    [InlineData("knee replacement", false, "other")]
    [InlineData("dr ramesh", false, "doctor")]
    public void DoctorDekhoWordsAreSortedByWhatTheyMatch(string query, bool aiUsed, string kind)
    {
        var meta = new DoctorDekhoSearchMeta { Query = query, AiUsed = aiUsed };
        Assert.Equal(kind, SearchKinds.ClassifyDoctorDekho(meta, null, Lookup));
    }

    [Fact]
    public void PickingASpecialityWithoutTyping_IsASpecialitySearch()
    {
        Assert.Equal(SearchKinds.Specialty, SearchKinds.ClassifyDoctorDekho(new DoctorDekhoSearchMeta(), "cardiology", Lookup));
    }

    [Fact]
    public void TheSitesOwnReading_WinsWhenItIsThere()
    {
        // "neha" alone looks like a doctor's name; the site knew it was answered as a symptom.
        var meta = DoctorDekhoSearchMeta.Parse("""{"query":"neha","aiUsed":false,"searchKind":"symptom","resultsCount":3}""");
        Assert.Equal(SearchKinds.Symptom, SearchKinds.ClassifyDoctorDekho(meta, null, Lookup));
        Assert.Equal(3, meta.ResultsCount);
    }

    // ── WhatsApp: the searches in one conversation ──

    private static string Transcript(params (string Direction, string Type, string? Content, string? Step)[] events) =>
        JsonSerializer.Serialize(events.Select(e => new { direction = e.Direction, type = e.Type, content = e.Content, step = e.Step }));

    private static (string, string, string?, string?) In(string content, string? step, string type = "text") => ("in", type, content, step);
    private static (string, string, string?, string?) Out(string step) => ("out", "step", step, step);

    private static BotSessionRow Session(string id, string phone, DateTime startedUtc, string transcript, Guid? bookingId = null) => new BotSessionRow
    {
        SessionId = id, Phone = phone, StartedAt = startedUtc, LastActivityAt = startedUtc.AddMinutes(5), TranscriptJson = transcript, BookingId = bookingId,
    };

    [Fact]
    public void WhatsAppSearches_FollowTheStepThePersonWasOn()
    {
        var journey = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow, Transcript(
            In("I need a heart doctor", null), Out("choosing_language"),
            In("symptom", "choosing_search_mode", "button_reply"), Out("awaiting_symptom"),
            In("chest pain", "awaiting_symptom"), Out("choosing_sort"),
            In("name", "choosing_search_mode", "button_reply"), Out("awaiting_doctor_name"),
            In("Avinash", "awaiting_doctor_name"),                      // bot did not move on: not found
            In("Dermatologist", "choosing_specialty", "list_reply"), Out("choosing_sort"))));
        var nlu = new List<BotNluRow> { new() { SessionId = "s", Utterance = "I need a heart doctor", Specialty = "Cardiology" } };

        var found = SearchKinds.FromWhatsApp(journey, nlu);

        Assert.Collection(found,
            s => { Assert.Equal(SearchKinds.Specialty, s.Kind); Assert.Equal("Cardiology", s.Specialty); },
            s => { Assert.Equal(SearchKinds.Symptom, s.Kind); Assert.Equal("chest pain", s.Text); Assert.True(s.Found); },
            s => { Assert.Equal(SearchKinds.DoctorName, s.Kind); Assert.Equal("Avinash", s.Text); Assert.False(s.Found); },
            s => { Assert.Equal(SearchKinds.Specialty, s.Kind); Assert.Equal("Dermatologist", s.Specialty); Assert.True(s.Found); });
    }

    [Fact]
    public void ButtonPressesAndPlaces_AreNotSearches()
    {
        var journey = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow, Transcript(
            In("hi", null), Out("choosing_language"),
            In("en", "choosing_language", "button_reply"), Out("choosing_location"),
            In("Purnia", "choosing_location"), Out("choosing_search_mode"),
            In("browse", "choosing_search_mode", "button_reply"), Out("choosing_specialty_group"))));

        Assert.Empty(SearchKinds.FromWhatsApp(journey, new List<BotNluRow>()));
    }

    [Theory]
    [InlineData("Cardiology", "Cardiologist (Heart)")]
    [InlineData("dermatology", "Dermatologist")]
    [InlineData("Orthopaedics", "orthopedics")]
    [InlineData("Urology", "Urologist")]
    public void OneSpecialityWrittenTwoWays_IsCountedOnce(string a, string b)
    {
        Assert.Equal(SearchInsightsService.SpecialtyKey(a), SearchInsightsService.SpecialtyKey(b));
    }

    [Fact]
    public void GeneralMedicineAndGeneralSurgery_StayApart()
    {
        Assert.NotEqual(SearchInsightsService.SpecialtyKey("General Physician"), SearchInsightsService.SpecialtyKey("General Surgery"));
    }

    // ── Both channels together ──

    private sealed class FakeBot : IWhatsAppBotStore
    {
        public List<BotSessionRow> Sessions = new();
        public List<BotNluRow> Nlu = new();
        public bool Down;
        private void Check() { if (Down) throw new WhatsAppBotUnavailableException("cannot connect"); }
        public Task<List<BotSessionRow>> SessionsAsync(DateTime fromUtc, DateTime toUtc)
        { Check(); return Task.FromResult(Sessions.Where(s => s.StartedAt >= fromUtc && s.StartedAt < toUtc).ToList()); }
        public Task<BotSessionRow?> SessionAsync(string sessionId) { Check(); return Task.FromResult(Sessions.FirstOrDefault(s => s.SessionId == sessionId)); }
        public Task<Dictionary<string, DateTime>> FirstSeenAsync(IReadOnlyCollection<string> phones) { Check(); return Task.FromResult(new Dictionary<string, DateTime>()); }
        public Task<List<BotNluRow>> NluAsync(IReadOnlyCollection<string> sessionIds) { Check(); return Task.FromResult(Nlu.Where(n => sessionIds.Contains(n.SessionId)).ToList()); }
        public Task<List<BotBookingRow>> BookingsAsync(IReadOnlyCollection<Guid> ids) { Check(); return Task.FromResult(new List<BotBookingRow>()); }
        public Task<List<BotLastSearchRow>> LastSearchesAsync(IReadOnlyCollection<string> phones)
        { Check(); return Task.FromResult(phones.Select(p => new BotLastSearchRow { Phone = p, City = "Purnia" }).ToList()); }
        public Task<List<(string Phone, DateTime StartedAt)>> StartsSinceAsync(DateTime fromUtc) { Check(); return Task.FromResult(new List<(string, DateTime)>()); }
    }

    private static DateTime Today(int hour) => TrackingTime.StartUtc(TrackingTime.TodayIst()).AddHours(hour);

    private static (FakeBot Bot, SearchInsightsService Service) NewSpace()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = Guid.NewGuid();
        db.UserProfiles.Add(new UserProfile { UserProfileID = Guid.NewGuid(), UserID = user, FullName = "Dr. Avinash Kumar" });
        db.Doctors.Add(new Doctor { DoctorID = Guid.NewGuid(), UserID = user, HospitalID = Guid.NewGuid() });
        db.Hospitals.Add(new Hospital { HospitalID = Guid.NewGuid(), Name = "City Care Hospital" });
        db.Specializations.Add(new Specialization { SpecializationID = Guid.NewGuid(), Name = "Cardiology" });

        void Event(string visit, string type, DateTime at, object meta, string? specialty = null, bool bot = false) => db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventId = Guid.NewGuid(), EventType = type, OccurredAt = at, SessionId = visit, SpecialtyId = specialty,
            MetadataJson = JsonSerializer.Serialize(meta), City = "Purnia", IsAutomated = bot,
        });
        // Visit 1 typed a doctor's name and booked; visit 2 typed a symptom; visit 3 a word nothing matched;
        // visit 4 picked a speciality; a search engine's search is left out.
        Event("v1", "search_performed", Today(1), new { query = "dr avinash", resultsCount = 1, aiUsed = false });
        Event("v1", "booking_step_reached", Today(2), new { step = "done" });
        Event("v2", "search_performed", Today(3), new { query = "sir dard", resultsCount = 4, aiUsed = true }, "neurology");
        Event("v3", "search_performed", Today(4), new { query = "xyzabc", resultsCount = 0, aiUsed = false });
        Event("v4", "search_performed", Today(5), new { resultsCount = 6, aiUsed = false }, "cardiology");
        Event("crawler", "search_performed", Today(5), new { query = "dr avinash", resultsCount = 1 }, bot: true);
        db.SaveChanges();

        var bot = new FakeBot();
        bot.Sessions.Add(Session("w1", "919822222222", Today(6), Transcript(
            In("symptom", "choosing_search_mode", "button_reply"), Out("awaiting_symptom"),
            In("chest pain", "awaiting_symptom"), Out("choosing_sort")), bookingId: Guid.NewGuid()));
        bot.Sessions.Add(Session("w2", "919833333333", Today(7), Transcript(
            In("name", "choosing_search_mode", "button_reply"), Out("awaiting_doctor_name"),
            In("Dr Ramesh", "awaiting_doctor_name"))));
        return (bot, new SearchInsightsService(bot, db));
    }

    [Fact]
    public async Task TheSummary_CountsEachKindOnEachChannel()
    {
        var (_, service) = NewSpace();
        var today = TrackingTime.TodayIst();

        var s = await service.SummaryAsync(today, today, null);

        Assert.Equal(6, s.Searches);
        Assert.Equal(6, s.People);
        Assert.Equal(2, s.LedToBooking);               // v1 on Doctor Dekho, w1 on WhatsApp
        Assert.Equal(2, s.FoundNothing);               // xyzabc, Dr Ramesh
        var kind = s.Kinds.ToDictionary(k => k.Kind);
        Assert.Equal((1, 1), (kind["doctor"].DoctorDekho, kind["doctor"].WhatsApp));
        Assert.Equal((1, 1), (kind["symptom"].DoctorDekho, kind["symptom"].WhatsApp));
        Assert.Equal(1, kind["specialty"].DoctorDekho);
        Assert.Equal(1, kind["other"].DoctorDekho);
        Assert.Equal(1, kind["symptom"].LedToBooking);
        Assert.Contains(s.TopTerms, t => t.Term == "chest pain" && t.WhatsApp == 1);
        Assert.Contains(s.Specialties, t => t.Term == "Cardiology" && t.DoctorDekho == 1);
        Assert.Contains(s.NothingFound, t => t.Term == "Dr Ramesh");
        Assert.Equal(4, s.Days.Single().DoctorDekho);
        Assert.True(s.WhatsAppAvailable);
    }

    [Fact]
    public async Task WhenTheBotCannotBeRead_DoctorDekhoIsStillAnswered()
    {
        var (bot, service) = NewSpace();
        bot.Down = true;

        var s = await service.SummaryAsync(null, null, "all");

        Assert.False(s.WhatsAppAvailable);
        Assert.Equal(4, s.Searches);
    }

    [Fact]
    public async Task TheList_FiltersAndMasksNumbers()
    {
        var (_, service) = NewSpace();

        var nothing = await service.ListAsync(new SearchListQuery { Kind = "nothing" }, showContacts: false);
        Assert.Equal(new[] { "Dr Ramesh", "xyzabc" }, nothing.Data.Select(r => r.Text));
        Assert.Equal("+91 98XXXXXX33", nothing.Data[0].Phone);
        Assert.Null(nothing.Data[1].Phone);

        var whatsapp = await service.ListAsync(new SearchListQuery { Channel = "whatsapp", Search = "chest" }, showContacts: true);
        var row = Assert.Single(whatsapp.Data);
        Assert.Equal("+91 98222 22222", row.Phone);
        Assert.True(row.Booked);
        Assert.Equal("Purnia", row.City);
    }
}
