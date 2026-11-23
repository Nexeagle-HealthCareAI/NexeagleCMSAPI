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

// Insights on the WhatsApp booking bot: reading a conversation (where it came from, how far it got,
// how it ended), the day-by-day figures, the people list with and without full numbers, and growth
// by week next to the two websites.
public class WhatsAppInsightsTests
{
    private static string Transcript(params (string Direction, string Type, string? Content, string? Step)[] events) =>
        JsonSerializer.Serialize(events.Select((e, i) => new { direction = e.Direction, type = e.Type, content = e.Content, step = e.Step, at = 1795400000 + i }));

    private static (string, string, string?, string?) In(string content, string? step = null, string type = "text") => ("in", type, content, step);
    private static (string, string, string?, string?) Out(string step) => ("out", "step", step, step);

    private static BotSessionRow Session(string id, string phone, DateTime startedUtc, string transcript, Guid? bookingId = null) => new BotSessionRow
    {
        SessionId = id, Phone = phone, StartedAt = startedUtc, LastActivityAt = startedUtc.AddMinutes(5), TranscriptJson = transcript, BookingId = bookingId,
    };

    // ── Reading one conversation ──

    [Fact]
    public void AHospitalQrScan_IsRecognised_WithTheHospitalsName()
    {
        var j = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow, Transcript(
            In("Hey, I've scanned the QR code for City Care Hospital! QR ID of this hospital is CCH01"),
            Out("choosing_hospital_action"), Out("choosing_doctor"), Out("choosing_slot"))));

        Assert.Equal(WhatsAppJourneys.SourceHospitalQr, j.Source);
        Assert.Equal("City Care Hospital", j.SourceName);
        Assert.Equal("CCH01", j.SourceCode);
        Assert.Equal(WhatsAppJourneys.OutcomeLeft, j.Outcome);
        Assert.Equal("Choosing a time", j.StageLabel);
    }

    [Theory]
    [InlineData("DRBOOK 3f2a", WhatsAppJourneys.SourceDoctorQr)]
    [InlineData("CHECKIN CCH01", WhatsAppJourneys.SourceCheckinQr)]
    [InlineData("RX abc123", WhatsAppJourneys.SourceDocumentQr)]
    [InlineData("Hi, I need a doctor", WhatsAppJourneys.SourceDirect)]
    public void TheSourceComesFromTheMessageTheQrPrefills(string firstMessage, string source)
    {
        var j = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow, Transcript(In(firstMessage))));
        Assert.Equal(source, j.Source);
    }

    [Fact]
    public void ABookedConversation_IsBooked_WhateverItsLastStep()
    {
        var j = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow,
            Transcript(In("hi"), Out("choosing_language"), Out("confirming")), bookingId: Guid.NewGuid()));

        Assert.Equal(WhatsAppJourneys.OutcomeBooked, j.Outcome);
        Assert.Equal("Booked", j.StageLabel);
    }

    [Fact]
    public void LeavingAfterHello_CountsAsLeftAtTheStart()
    {
        var j = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow, Transcript(In("hi"), Out("choosing_language"))));

        Assert.Equal(WhatsAppJourneys.OutcomeLeft, j.Outcome);
        Assert.Equal(0, j.Stage);
    }

    [Fact]
    public void CheckingAnExistingAppointment_IsNotADropOff()
    {
        var j = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow,
            Transcript(In("hi"), Out("choosing_language"), Out("viewing_appointment_status"))));

        Assert.Equal(WhatsAppJourneys.OutcomeOther, j.Outcome);
    }

    [Fact]
    public void WhatWasTyped_WhenAskedForASymptom_IsKept()
    {
        var j = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow, Transcript(
            In("hi"), Out("awaiting_symptom"), In("pet me dard", "awaiting_symptom"), In("button_1", "choosing_specialty", "button_reply"))));

        Assert.Equal(new[] { "pet me dard" }, j.TypedSearches);
        Assert.Equal("Searching for a doctor", j.StageLabel);
    }

    [Fact]
    public void ABrokenTranscript_StillCountsAsAConversation()
    {
        var j = WhatsAppJourneys.Classify(Session("s", "919800000001", DateTime.UtcNow, "{not json"));
        Assert.Empty(j.Events);
        Assert.Equal(WhatsAppJourneys.OutcomeLeft, j.Outcome);
    }

    [Fact]
    public void NumbersAreShownInFull_OrMaskedToFirstAndLastTwoDigits()
    {
        Assert.Equal("+91 98765 43210", WhatsAppJourneys.FormatPhone("919876543210"));
        Assert.Equal("+91 98XXXXXX10", WhatsAppJourneys.MaskPhone("919876543210"));
    }

    [Fact]
    public void WeeksStartOnMonday()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), WhatsAppJourneys.WeekStart(new DateOnly(2026, 11, 22)));   // Sunday
        Assert.Equal(new DateOnly(2026, 11, 23), WhatsAppJourneys.WeekStart(new DateOnly(2026, 11, 23)));   // Monday
    }

    // ── The service, over a stand-in for the bot's database ──

    private sealed class FakeBot : IWhatsAppBotStore
    {
        public List<BotSessionRow> Sessions = new();
        public List<BotNluRow> Nlu = new();
        public List<BotBookingRow> Bookings = new();
        public bool Down;

        private void Check() { if (Down) throw new WhatsAppBotUnavailableException("cannot connect"); }

        public Task<List<BotSessionRow>> SessionsAsync(DateTime fromUtc, DateTime toUtc)
        { Check(); return Task.FromResult(Sessions.Where(s => s.StartedAt >= fromUtc && s.StartedAt < toUtc).OrderByDescending(s => s.StartedAt).ToList()); }
        public Task<BotSessionRow?> SessionAsync(string sessionId) { Check(); return Task.FromResult(Sessions.FirstOrDefault(s => s.SessionId == sessionId)); }
        public Task<Dictionary<string, DateTime>> FirstSeenAsync(IReadOnlyCollection<string> phones)
        { Check(); return Task.FromResult(Sessions.Where(s => phones.Contains(s.Phone)).GroupBy(s => s.Phone).ToDictionary(g => g.Key, g => g.Min(s => s.StartedAt))); }
        public Task<List<BotNluRow>> NluAsync(IReadOnlyCollection<string> sessionIds) { Check(); return Task.FromResult(Nlu.Where(n => sessionIds.Contains(n.SessionId)).ToList()); }
        public Task<List<BotBookingRow>> BookingsAsync(IReadOnlyCollection<Guid> ids) { Check(); return Task.FromResult(Bookings.Where(b => ids.Contains(b.Id)).ToList()); }
        public Task<List<BotLastSearchRow>> LastSearchesAsync(IReadOnlyCollection<string> phones) { Check(); return Task.FromResult(new List<BotLastSearchRow>()); }
        public Task<List<(string Phone, DateTime StartedAt)>> StartsSinceAsync(DateTime fromUtc)
        { Check(); return Task.FromResult(Sessions.Where(s => s.StartedAt >= fromUtc).Select(s => (s.Phone, s.StartedAt)).ToList()); }
    }

    private static AppDbContext NewDb() =>
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // Noon India time, n days ago.
    private static DateTime DaysAgo(int n) => TrackingTime.StartUtc(TrackingTime.TodayIst().AddDays(-n)).AddHours(12);

    private static (FakeBot Bot, WhatsAppInsightsService Service, AppDbContext Db) NewSpace()
    {
        var bot = new FakeBot();
        var booking = Guid.NewGuid();
        // Asha: wrote 10 days ago (so not new today), then booked today from a hospital QR.
        bot.Sessions.Add(Session("old", "919811111111", DaysAgo(10), Transcript(In("hi"), Out("choosing_language"))));
        bot.Sessions.Add(Session("asha", "919811111111", DaysAgo(0), Transcript(
            In("Hey, I've scanned the QR code for City Care! QR ID of this hospital is CC1"), Out("choosing_doctor"), Out("confirming")), booking));
        bot.Bookings.Add(new BotBookingRow { Id = booking, DoctorName = "Dr. Mehta", PatientName = "Asha Rao", PreferredDate = new DateTime(2026, 11, 25), BookingFor = "self" });
        // Bilal: first time today, searched and left at the time slots.
        bot.Sessions.Add(Session("bilal", "919822222222", DaysAgo(0).AddMinutes(30), Transcript(
            In("hi"), Out("awaiting_symptom"), In("chest pain", "awaiting_symptom"), Out("choosing_doctor"), Out("choosing_slot"))));
        bot.Nlu.Add(new BotNluRow { SessionId = "bilal", Specialty = "Cardiology", Symptom = "chest pain" });
        // Chitra: yesterday, only checked an appointment.
        bot.Sessions.Add(Session("chitra", "919833333333", DaysAgo(1), Transcript(In("hi"), Out("viewing_appointment_status"))));
        var db = NewDb();
        return (bot, new WhatsAppInsightsService(bot, db), db);
    }

    [Fact]
    public async Task TheDayByDayFigures_CountPeopleNewPeopleQrBookingsAndDropOffs()
    {
        var (_, service, _) = NewSpace();

        var stats = await service.DailyAsync(null, null);

        var today = stats.Days[0];
        Assert.Equal(TrackingTime.TodayIst().ToString("yyyy-MM-dd"), today.Date);
        Assert.Equal(2, today.Conversations);
        Assert.Equal(2, today.People);
        Assert.Equal(1, today.NewPeople);
        Assert.Equal(1, today.FromQr);
        Assert.Equal(1, today.Booked);
        Assert.Equal(1, today.Left);
        Assert.Equal(1, stats.Days[1].Other);
        Assert.Equal(7, stats.Days.Count);
        Assert.Equal(3, stats.Totals.Conversations);
        Assert.Contains(stats.TopSearches, s => s.Name == "chest pain");
        Assert.Contains(stats.QrSources, s => s.Name == "Hospital QR · City Care");
        Assert.Equal("Choosing a time", Assert.Single(stats.LeftAt).Name);
    }

    [Fact]
    public async Task ThePeopleList_ShowsNumbersOnlyToThoseAllowed_AndWhatEachSearchedOrBooked()
    {
        var (_, service, _) = NewSpace();

        var masked = await service.PeopleAsync(new WhatsAppPeopleQuery(), showContacts: false);
        var full = await service.PeopleAsync(new WhatsAppPeopleQuery(), showContacts: true);

        Assert.Equal(3, masked.Pagination.TotalItems);
        var bilal = full.Data.Single(r => r.SessionId == "bilal");
        Assert.Equal("+91 98222 22222", bilal.Phone);
        Assert.Equal("919822222222", bilal.PhoneDigits);
        Assert.True(bilal.IsNewPerson);
        Assert.Equal(WhatsAppJourneys.OutcomeLeft, bilal.Outcome);
        Assert.Contains("Cardiology", bilal.Searched);
        Assert.Contains("chest pain", bilal.Searched);
        var hidden = masked.Data.Single(r => r.SessionId == "bilal");
        Assert.Equal("+91 98XXXXXX22", hidden.Phone);
        Assert.Equal(string.Empty, hidden.PhoneDigits);

        var asha = full.Data.Single(r => r.SessionId == "asha");
        Assert.Equal("Dr Mehta", asha.BookedDoctor);
        Assert.Equal("Asha Rao", asha.PatientName);
        Assert.Equal("2026-11-25", asha.AppointmentDate);
        Assert.Equal("Hospital QR · City Care", asha.SourceLabel);
        Assert.False(asha.IsNewPerson);
    }

    [Fact]
    public async Task ThePeopleList_FiltersByOutcomeSourceAndSearch()
    {
        var (_, service, _) = NewSpace();

        var left = await service.PeopleAsync(new WhatsAppPeopleQuery { Outcome = "left" }, true);
        var qr = await service.PeopleAsync(new WhatsAppPeopleQuery { Source = "qr" }, true);
        var byNumber = await service.PeopleAsync(new WhatsAppPeopleQuery { Search = "98222" }, false);
        var byText = await service.PeopleAsync(new WhatsAppPeopleQuery { Search = "cardio" }, true);

        Assert.Equal("bilal", Assert.Single(left.Data).SessionId);
        Assert.Equal("asha", Assert.Single(qr.Data).SessionId);
        Assert.Equal("bilal", Assert.Single(byNumber.Data).SessionId);
        Assert.Equal("bilal", Assert.Single(byText.Data).SessionId);
    }

    [Fact]
    public async Task TheTranscript_ReadsAsAConversation()
    {
        var (_, service, _) = NewSpace();

        var t = await service.TranscriptAsync("bilal");

        Assert.NotNull(t);
        Assert.Equal("+91 98222 22222", t!.Phone);
        Assert.Contains(t.Lines, l => l.From == "person" && l.Text == "chest pain" && l.StepLabel == "Typing a symptom");
        Assert.Contains(t.Lines, l => l.From == "bot" && l.Text == "Moved to: Choosing a time");
        Assert.Null(await service.TranscriptAsync("nobody"));
    }

    [Fact]
    public async Task GrowthByWeek_PutsTheBotNextToBothWebsites()
    {
        var (bot, service, db) = NewSpace();
        var now = DateTime.UtcNow;
        db.WebsiteVisits.AddRange(
            new WebsiteVisit { VisitId = Guid.NewGuid(), VisitedAt = now, SessionId = "a", Site = "nexeagle" },
            new WebsiteVisit { VisitId = Guid.NewGuid(), VisitedAt = now, SessionId = "a", Site = "nexeagle" },
            new WebsiteVisit { VisitId = Guid.NewGuid(), VisitedAt = now, SessionId = "b", Site = "doctordekho" },
            new WebsiteVisit { VisitId = Guid.NewGuid(), VisitedAt = now, SessionId = "c", Site = "doctordekho" },
            new WebsiteVisit { VisitId = Guid.NewGuid(), VisitedAt = now, SessionId = "bot", Site = "doctordekho", IsAutomated = true });
        await db.SaveChangesAsync();

        var growth = await service.WeeklyGrowthAsync(4);

        Assert.True(growth.WhatsAppAvailable);
        Assert.Equal(4, growth.Weeks.Count);
        var thisWeek = growth.Weeks[^1];
        Assert.True(thisWeek.IsPartial);
        Assert.Equal(1, thisWeek.NexeagleVisits);
        Assert.Equal(2, thisWeek.DoctorDekhoVisits);
        // Each number counts once in each week it wrote in.
        var expected = bot.Sessions
            .Select(x => (x.Phone, Week: WhatsAppJourneys.WeekStart(TrackingTime.IstDate(x.StartedAt))))
            .Distinct()
            .Where(x => x.Week >= DateOnly.Parse(growth.Weeks[0].WeekStart))
            .Count();
        Assert.Equal(expected, growth.Weeks.Sum(w => w.WhatsAppPeople));
        Assert.True(thisWeek.WhatsAppPeople >= 2);
    }

    [Fact]
    public async Task GrowthByWeek_StillShowsTheWebsites_WhenTheBotCannotBeRead()
    {
        var (bot, service, _) = NewSpace();
        bot.Down = true;

        var growth = await service.WeeklyGrowthAsync(12);

        Assert.False(growth.WhatsAppAvailable);
        Assert.Contains("cannot connect", growth.WhatsAppProblem);
        Assert.All(growth.Weeks, w => Assert.Null(w.WhatsAppPeople));
        await Assert.ThrowsAsync<WhatsAppBotUnavailableException>(() => service.DailyAsync(null, null));
    }

    [Fact]
    public async Task DoctorDekhoByDay_FollowsVisitsToBookings()
    {
        var (_, service, db) = NewSpace();
        var now = DateTime.UtcNow;
        db.WebsiteVisits.Add(new WebsiteVisit { VisitId = Guid.NewGuid(), VisitedAt = now, SessionId = "v1", Site = "doctordekho" });
        db.WebsiteVisits.Add(new WebsiteVisit { VisitId = Guid.NewGuid(), VisitedAt = now, SessionId = "v2", Site = "doctordekho" });
        void Event(string session, string type, string? step = null) => db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventId = Guid.NewGuid(), EventType = type, OccurredAt = now, SessionId = session,
            MetadataJson = step == null ? null : JsonSerializer.Serialize(new { step }),
        });
        Event("v1", "search_performed"); Event("v2", "search_performed");
        Event("v1", "booking_step_reached", "visit"); Event("v2", "booking_step_reached", "visit");
        Event("v1", "booking_step_reached", "details"); Event("v1", "booking_step_reached", "done");
        await db.SaveChangesAsync();

        var stats = await service.DoctorDekhoDailyAsync(null, null);

        var today = stats.Days[0];
        Assert.Equal(2, today.Visits);
        Assert.Equal(2, today.Searched);
        Assert.Equal(2, today.StartedBooking);
        Assert.Equal(1, today.EnteredDetails);
        Assert.Equal(1, today.Booked);
        Assert.Equal(1, today.Left);
        Assert.Equal(2, stats.Totals.Visits);
    }

    [Fact]
    public void ALongPeriod_IsCutTo92Days()
    {
        var (from, to) = WhatsAppInsightsService.Range(new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 23));
        Assert.Equal(new DateOnly(2026, 11, 23), to);
        Assert.Equal(92, to.DayNumber - from.DayNumber + 1);
    }
}
