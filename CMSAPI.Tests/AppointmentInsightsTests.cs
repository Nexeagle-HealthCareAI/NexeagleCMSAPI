#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Every appointment by channel: Doctor Dekho, the WhatsApp bot (told apart by the reason the bot
// writes), and the hospital desk; the chart's periods, the top doctors, and the list.
public class AppointmentInsightsTests
{
    private static readonly Guid Hospital = Guid.NewGuid();
    private static readonly Guid OtherHospital = Guid.NewGuid();
    private static readonly Guid DrMehta = Guid.NewGuid();
    private static readonly Guid DrRao = Guid.NewGuid();

    // Noon India time, n days ago.
    private static DateTime DaysAgo(int n) => TrackingTime.StartUtc(TrackingTime.TodayIst().AddDays(-n)).AddHours(12);

    private static (AppDbContext Db, AppointmentInsightsService Service) NewSpace()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Hospitals.Add(new Hospital { HospitalID = Hospital, Name = "City Care Hospital" });
        db.Hospitals.Add(new Hospital { HospitalID = OtherHospital, Name = "Max Clinic" });
        void Doctor(Guid id, Guid hospital, string name)
        {
            var user = Guid.NewGuid();
            db.UserProfiles.Add(new UserProfile { UserProfileID = Guid.NewGuid(), UserID = user, FullName = name });
            db.Doctors.Add(new Doctor { DoctorID = id, UserID = user, HospitalID = hospital });
        }
        Doctor(DrMehta, Hospital, "Dr. Mehta");
        Doctor(DrRao, OtherHospital, "Dr. Rao");
        db.StatusMaster.Add(new StatusMaster { StatusCode = "FUTURE", StatusName = "Upcoming" });
        db.StatusMaster.Add(new StatusMaster { StatusCode = "CANCELLED", StatusName = "Cancelled" });
        db.PatientRegistrations.Add(new PatientRegistration { RegistrationId = Guid.NewGuid(), HospitalID = Hospital, PatientID = "P1", FullName = "Asha Rao", Mobile = "9811111111" });

        void Appt(int daysAgo, Guid doctor, Guid hospital, string? source, string? reason, string status = "FUTURE", string patient = "P1") =>
            db.Appointments.Add(new Appointment
            {
                ApptId = Guid.NewGuid(), HospitalID = hospital, DoctorID = doctor, PatientID = patient, ApptDate = TrackingTime.TodayIst().AddDays(-daysAgo + 2),
                CreatedAt = DaysAgo(daysAgo), BookingSource = source, Reason = reason, CurrentStatusCode = status,
            });
        // Today: one Doctor Dekho, two WhatsApp (one cancelled), one at the desk.
        Appt(0, DrMehta, Hospital, "NEXEAGLE_PUBLIC", "Fever");
        Appt(0, DrMehta, Hospital, "NEXEAGLE_PUBLIC", "WhatsApp booking — preferred Morning");
        Appt(0, DrRao, OtherHospital, "NEXEAGLE_PUBLIC", "WhatsApp booking — preferred Evening", status: "CANCELLED");
        Appt(0, DrMehta, Hospital, null, "Follow-up");
        // Three days ago: one Doctor Dekho with no reason, one desk booking marked INTERNAL.
        Appt(3, DrRao, OtherHospital, "NEXEAGLE_PUBLIC", null);
        Appt(3, DrRao, OtherHospital, "INTERNAL", null);
        // Outside the period.
        Appt(60, DrMehta, Hospital, "NEXEAGLE_PUBLIC", null);
        db.SaveChanges();
        return (db, new AppointmentInsightsService(db));
    }

    [Theory]
    [InlineData("NEXEAGLE_PUBLIC", "WhatsApp booking — preferred Morning", AppointmentChannels.WhatsApp)]
    [InlineData("NEXEAGLE_PUBLIC", "Fever", AppointmentChannels.DoctorDekho)]
    [InlineData("NEXEAGLE_PUBLIC", null, AppointmentChannels.DoctorDekho)]
    [InlineData(null, "WhatsApp booking — preferred Morning", AppointmentChannels.HospitalDesk)]
    [InlineData("INTERNAL", null, AppointmentChannels.HospitalDesk)]
    public void TheChannelComesFromTheSourceAndTheBotsReason(string? source, string? reason, string channel)
    {
        Assert.Equal(channel, AppointmentChannels.Of(source, reason));
    }

    [Fact]
    public async Task TheChart_CountsEachChannelPerDay()
    {
        var (_, service) = NewSpace();
        var today = TrackingTime.TodayIst();

        var trend = await service.TrendAsync(new AppointmentQuery { From = today.AddDays(-6), To = today, GroupBy = "day" });

        Assert.Equal(7, trend.Periods.Count);
        Assert.Equal((2, 2, 2, 1), (trend.Totals.DoctorDekho, trend.Totals.WhatsApp, trend.Totals.HospitalDesk, trend.Totals.Cancelled));
        var todayRow = trend.Periods.Single(p => p.Start == today.ToString("yyyy-MM-dd"));
        Assert.Equal((1, 2, 1), (todayRow.DoctorDekho, todayRow.WhatsApp, todayRow.HospitalDesk));
        Assert.True(todayRow.IsPartial);
        Assert.False(trend.Periods.First().IsPartial);
        var top = trend.TopDoctors.First();
        Assert.Equal("Dr. Mehta", top.Name);
        Assert.Equal("City Care Hospital", top.Hospital);
        Assert.Equal(2, top.Online);
        Assert.Equal(2, trend.Hospitals.Count);
        Assert.Equal(2, trend.Doctors.Count);
    }

    [Fact]
    public async Task OneDoctorsChart_ShowsOnlyTheirAppointments_ButTheFiltersStillListEveryone()
    {
        var (_, service) = NewSpace();
        var today = TrackingTime.TodayIst();

        var trend = await service.TrendAsync(new AppointmentQuery { From = today.AddDays(-6), To = today, DoctorId = DrRao, Channel = "online" });

        Assert.Equal((1, 1, 0), (trend.Totals.DoctorDekho, trend.Totals.WhatsApp, trend.Totals.HospitalDesk));
        Assert.Equal(2, trend.Doctors.Count);
    }

    [Fact]
    public async Task ALongPeriod_IsShownByMonth()
    {
        var (_, service) = NewSpace();
        var today = TrackingTime.TodayIst();

        var trend = await service.TrendAsync(new AppointmentQuery { From = today.AddDays(-300), To = today });

        Assert.Equal("month", trend.GroupBy);
        Assert.Equal(7, trend.Totals.Total);
        Assert.All(trend.Periods, p => Assert.EndsWith("-01", p.Start));
    }

    [Fact]
    public async Task TheList_LabelsEachAppointment_AndMasksNumbersWithoutPermission()
    {
        var (_, service) = NewSpace();
        var today = TrackingTime.TodayIst();

        var whatsapp = await service.ListAsync(new AppointmentQuery { From = today, To = today, Channel = "whatsapp" }, showContacts: false);
        Assert.Equal(2, whatsapp.Pagination.TotalItems);
        Assert.All(whatsapp.Data, r => Assert.Equal("WhatsApp bot", r.ChannelLabel));
        var mehta = whatsapp.Data.Single(r => r.DoctorName == "Dr. Mehta");
        Assert.Equal("98XXXXXX11", mehta.PatientMobile);
        Assert.Equal("Asha Rao", mehta.PatientName);
        Assert.Equal("City Care Hospital", mehta.HospitalName);
        Assert.Equal("Upcoming", mehta.Status);

        var withNumbers = await service.ListAsync(new AppointmentQuery { From = today, To = today, Search = "Asha", IncludeCancelled = false }, showContacts: true);
        Assert.Equal(3, withNumbers.Pagination.TotalItems);
        Assert.All(withNumbers.Data, r => Assert.Equal("9811111111", r.PatientMobile));
    }
}
