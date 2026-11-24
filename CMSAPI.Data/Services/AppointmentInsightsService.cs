using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>
/// Every appointment in the HMS by channel (Doctor Dekho, WhatsApp bot, hospital desk), for a chart
/// that shows doctors how their appointments come in, and the list behind it. Counts are of
/// appointments booked in the period, whatever happened to them later; cancellations are counted
/// on their own as well.
/// </summary>
public class AppointmentInsightsService : IAppointmentInsightsService
{
    /// <summary>Longest period read at once.</summary>
    public const int MaxDays = 731;
    private const int Top = 10;

    private readonly AppDbContext _db;

    public AppointmentInsightsService(AppDbContext db) => _db = db;

    public static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var end = to ?? TrackingTime.TodayIst();
        var start = from ?? end.AddDays(-29);
        if (start > end) (start, end) = (end, start);
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) start = end.AddDays(-(MaxDays - 1));
        return (start, end);
    }

    private static bool ByAppointmentDate(string? dateBy) => string.Equals(dateBy, "appointment", StringComparison.OrdinalIgnoreCase);

    /// <summary>The appointments of the period, by booking day (India time) or appointment date, with the filters.</summary>
    private IQueryable<Appointment> Filtered(AppointmentQuery q, DateOnly start, DateOnly end, bool placeFilters = true)
    {
        var query = _db.Appointments.AsNoTracking();
        if (ByAppointmentDate(q.DateBy))
        {
            query = query.Where(a => a.ApptDate >= start && a.ApptDate <= end);
        }
        else
        {
            var startUtc = TrackingTime.StartUtc(start);
            var endUtc = TrackingTime.EndUtcExclusive(end);
            query = query.Where(a => a.CreatedAt >= startUtc && a.CreatedAt < endUtc);
        }

        if (placeFilters)
        {
            if (q.HospitalId.HasValue) query = query.Where(a => a.HospitalID == q.HospitalId.Value);
            if (q.DoctorId.HasValue) query = query.Where(a => a.DoctorID == q.DoctorId.Value);
        }

        const string online = AppointmentChannels.NexeaglePublicSource;
        const string wa = AppointmentChannels.WhatsAppReasonPrefix;
        return (q.Channel ?? "all").Trim().ToLowerInvariant() switch
        {
            "online" => query.Where(a => a.BookingSource == online),
            AppointmentChannels.WhatsApp => query.Where(a => a.BookingSource == online && a.Reason != null && a.Reason.StartsWith(wa)),
            AppointmentChannels.DoctorDekho => query.Where(a => a.BookingSource == online && (a.Reason == null || !a.Reason.StartsWith(wa))),
            AppointmentChannels.HospitalDesk => query.Where(a => a.BookingSource != online),
            _ => query,
        };
    }

    private static string GroupOf(string? groupBy, DateOnly start, DateOnly end)
    {
        var g = (groupBy ?? string.Empty).Trim().ToLowerInvariant();
        if (g is "day" or "week" or "month") return g;
        var days = end.DayNumber - start.DayNumber + 1;
        return days <= 45 ? "day" : days <= 200 ? "week" : "month";
    }

    private static DateOnly PeriodOf(DateOnly day, string group) => group switch
    {
        "week" => WhatsAppJourneys.WeekStart(day),
        "month" => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    private static DateOnly NextPeriod(DateOnly p, string group) => group switch
    {
        "week" => p.AddDays(7),
        "month" => p.AddMonths(1),
        _ => p.AddDays(1),
    };

    // ── The chart ───────────────────────────────────────────────────────────────────────────────

    public async Task<AppointmentTrend> TrendAsync(AppointmentQuery q)
    {
        var (start, end) = Range(q.From, q.To);
        var group = GroupOf(q.GroupBy, start, end);
        var byAppointment = ByAppointmentDate(q.DateBy);
        const string online = AppointmentChannels.NexeaglePublicSource;
        const string wa = AppointmentChannels.WhatsAppReasonPrefix;

        var rows = (await Filtered(q, start, end)
                .Select(a => new
                {
                    a.CreatedAt,
                    a.ApptDate,
                    a.HospitalID,
                    a.DoctorID,
                    a.CurrentStatusCode,
                    Online = a.BookingSource == online,
                    WhatsApp = a.BookingSource == online && a.Reason != null && a.Reason.StartsWith(wa),
                })
                .ToListAsync())
            .Select(a => new
            {
                Day = byAppointment ? a.ApptDate : TrackingTime.IstDate(a.CreatedAt),
                a.HospitalID,
                a.DoctorID,
                Cancelled = a.CurrentStatusCode == AppointmentChannels.Cancelled,
                Channel = !a.Online ? AppointmentChannels.HospitalDesk : a.WhatsApp ? AppointmentChannels.WhatsApp : AppointmentChannels.DoctorDekho,
            })
            .ToList();

        var today = TrackingTime.TodayIst();
        var byPeriod = rows.GroupBy(r => PeriodOf(r.Day, group)).ToDictionary(g => g.Key, g => g.ToList());
        var trend = new AppointmentTrend
        {
            From = start.ToString("yyyy-MM-dd"),
            To = end.ToString("yyyy-MM-dd"),
            GroupBy = group,
            DateBy = byAppointment ? "appointment" : "booked",
            Totals = new AppointmentPeriodRow
            {
                Start = "total",
                DoctorDekho = rows.Count(r => r.Channel == AppointmentChannels.DoctorDekho),
                WhatsApp = rows.Count(r => r.Channel == AppointmentChannels.WhatsApp),
                HospitalDesk = rows.Count(r => r.Channel == AppointmentChannels.HospitalDesk),
                Cancelled = rows.Count(r => r.Cancelled),
            },
        };
        for (var p = PeriodOf(start, group); p <= end; p = NextPeriod(p, group))
        {
            var of = byPeriod.TryGetValue(p, out var g) ? g : new();
            var periodEnd = NextPeriod(p, group).AddDays(-1);
            trend.Periods.Add(new AppointmentPeriodRow
            {
                Start = p.ToString("yyyy-MM-dd"),
                DoctorDekho = of.Count(r => r.Channel == AppointmentChannels.DoctorDekho),
                WhatsApp = of.Count(r => r.Channel == AppointmentChannels.WhatsApp),
                HospitalDesk = of.Count(r => r.Channel == AppointmentChannels.HospitalDesk),
                Cancelled = of.Count(r => r.Cancelled),
                // Only part of it is counted: it starts before or ends after the period asked for, or
                // (booking days) it has not finished yet.
                IsPartial = p < start || periodEnd > end || (!byAppointment && periodEnd >= today),
            });
        }

        var hospitalNames = await HospitalNamesAsync(rows.Select(r => r.HospitalID).Distinct().ToList());
        var doctors = await DoctorsAsync(rows.Select(r => r.DoctorID).Distinct().ToList());

        trend.TopHospitals = rows.GroupBy(r => r.HospitalID)
            .Select(g => new AppointmentPlaceRow
            {
                Id = g.Key.ToString(),
                Name = hospitalNames.TryGetValue(g.Key, out var n) ? n : "Unknown hospital",
                DoctorDekho = g.Count(r => r.Channel == AppointmentChannels.DoctorDekho),
                WhatsApp = g.Count(r => r.Channel == AppointmentChannels.WhatsApp),
                HospitalDesk = g.Count(r => r.Channel == AppointmentChannels.HospitalDesk),
            })
            .Where(x => x.Online > 0)
            .OrderByDescending(x => x.Online).ThenByDescending(x => x.Total).ThenBy(x => x.Name)
            .Take(Top).ToList();
        trend.TopDoctors = rows.GroupBy(r => r.DoctorID)
            .Select(g => new AppointmentPlaceRow
            {
                Id = g.Key.ToString(),
                Name = doctors.TryGetValue(g.Key, out var d) ? d.Name : "Unknown doctor",
                Hospital = doctors.TryGetValue(g.Key, out var d2) && hospitalNames.TryGetValue(d2.HospitalId, out var hn) ? hn : null,
                DoctorDekho = g.Count(r => r.Channel == AppointmentChannels.DoctorDekho),
                WhatsApp = g.Count(r => r.Channel == AppointmentChannels.WhatsApp),
                HospitalDesk = g.Count(r => r.Channel == AppointmentChannels.HospitalDesk),
            })
            .Where(x => x.Online > 0)
            .OrderByDescending(x => x.Online).ThenByDescending(x => x.Total).ThenBy(x => x.Name)
            .Take(Top).ToList();

        // The filters list every hospital and doctor with an appointment in the period, whatever is picked now.
        var places = await Filtered(new AppointmentQuery { DateBy = q.DateBy, Channel = q.Channel }, start, end, placeFilters: false)
            .Select(a => new { a.HospitalID, a.DoctorID }).Distinct().ToListAsync();
        var allHospitals = await HospitalNamesAsync(places.Select(p => p.HospitalID).Distinct().ToList());
        var allDoctors = await DoctorsAsync(places.Select(p => p.DoctorID).Distinct().ToList());
        trend.Hospitals = allHospitals.Select(h => new AppointmentOption { Id = h.Key.ToString(), Name = h.Value })
            .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();
        trend.Doctors = allDoctors.Select(d => new AppointmentOption { Id = d.Key.ToString(), Name = d.Value.Name, HospitalId = d.Value.HospitalId.ToString() })
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return trend;
    }

    private async Task<Dictionary<Guid, string>> HospitalNamesAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return (await _db.Hospitals.AsNoTracking().Where(h => ids.Contains(h.HospitalID)).Select(h => new { h.HospitalID, h.Name }).ToListAsync())
            .GroupBy(h => h.HospitalID).ToDictionary(g => g.Key, g => g.First().Name);
    }

    private async Task<Dictionary<Guid, (string Name, Guid HospitalId)>> DoctorsAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return new Dictionary<Guid, (string, Guid)>();
        return (await (from d in _db.Doctors.AsNoTracking()
                       join u in _db.UserProfiles.AsNoTracking() on d.UserID equals u.UserID
                       where ids.Contains(d.DoctorID)
                       select new { d.DoctorID, d.HospitalID, u.FullName }).ToListAsync())
            .GroupBy(d => d.DoctorID).ToDictionary(g => g.Key, g => (g.First().FullName, g.First().HospitalID));
    }

    // ── The list ────────────────────────────────────────────────────────────────────────────────

    public async Task<PagedResult<AppointmentRow>> ListAsync(AppointmentQuery q, bool showContacts)
    {
        var (start, end) = Range(q.From, q.To);
        var limit = Math.Clamp(q.Limit, 1, 1000);
        var page = Math.Max(1, q.Page);
        var query = Filtered(q, start, end);
        if (!q.IncludeCancelled) query = query.Where(a => a.CurrentStatusCode != AppointmentChannels.Cancelled);

        var search = q.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Names live in other tables: find the matching ids first, then the appointments.
            var doctorIds = await (from d in _db.Doctors.AsNoTracking()
                                   join u in _db.UserProfiles.AsNoTracking() on d.UserID equals u.UserID
                                   where u.FullName.Contains(search)
                                   select d.DoctorID).Take(500).ToListAsync();
            var hospitalIds = await _db.Hospitals.AsNoTracking().Where(h => h.Name.Contains(search)).Select(h => h.HospitalID).Take(500).ToListAsync();
            var patientIds = await _db.PatientRegistrations.AsNoTracking().Where(p => p.FullName.Contains(search)).Select(p => p.PatientID).Distinct().Take(1000).ToListAsync();
            query = query.Where(a => doctorIds.Contains(a.DoctorID) || hospitalIds.Contains(a.HospitalID) || patientIds.Contains(a.PatientID));
        }

        var ordered = ByAppointmentDate(q.DateBy)
            ? query.OrderByDescending(a => a.ApptDate).ThenByDescending(a => a.CreatedAt)
            : query.OrderByDescending(a => a.CreatedAt);
        var total = await query.CountAsync();
        var pageRows = await ordered.ThenBy(a => a.ApptId).Skip((page - 1) * limit).Take(limit)
            .Select(a => new { a.ApptId, a.CreatedAt, a.ApptDate, a.CurrentStatusCode, a.BookingSource, a.Reason, a.PatientID, a.HospitalID, a.DoctorID, a.BookedByMobile })
            .ToListAsync();

        var hospitals = await HospitalNamesAsync(pageRows.Select(r => r.HospitalID).Distinct().ToList());
        var doctors = await DoctorsAsync(pageRows.Select(r => r.DoctorID).Distinct().ToList());
        var patientKeys = pageRows.Select(r => r.PatientID).Distinct().ToList();
        var patients = (await _db.PatientRegistrations.AsNoTracking()
                .Where(p => patientKeys.Contains(p.PatientID))
                .Select(p => new { p.PatientID, p.HospitalID, p.FullName, p.Mobile })
                .ToListAsync())
            .GroupBy(p => (p.PatientID, p.HospitalID)).ToDictionary(g => g.Key, g => g.First());
        var statusNames = (await _db.StatusMaster.AsNoTracking().Select(s => new { s.StatusCode, s.StatusName }).ToListAsync())
            .Where(s => !string.IsNullOrWhiteSpace(s.StatusName))
            .GroupBy(s => s.StatusCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().StatusName!, StringComparer.OrdinalIgnoreCase);

        return new PagedResult<AppointmentRow>
        {
            Data = pageRows.Select(r =>
            {
                var channel = AppointmentChannels.Of(r.BookingSource, r.Reason);
                patients.TryGetValue((r.PatientID, r.HospitalID), out var patient);
                var mobile = patient?.Mobile;
                return new AppointmentRow
                {
                    ApptId = r.ApptId,
                    BookedAt = r.CreatedAt,
                    ApptDate = r.ApptDate,
                    Channel = channel,
                    ChannelLabel = AppointmentChannels.Label(channel),
                    Status = statusNames.TryGetValue(r.CurrentStatusCode, out var sn) ? sn : r.CurrentStatusCode,
                    StatusCode = r.CurrentStatusCode,
                    PatientName = patient?.FullName,
                    PatientMobile = string.IsNullOrWhiteSpace(mobile) ? null : showContacts ? mobile : Mask(mobile),
                    DoctorName = doctors.TryGetValue(r.DoctorID, out var d) ? d.Name : null,
                    HospitalName = hospitals.TryGetValue(r.HospitalID, out var h) ? h : null,
                    LoggedIn = channel == AppointmentChannels.DoctorDekho && r.BookedByMobile != null,
                };
            }).ToList(),
            Pagination = new PaginationInfo
            {
                CurrentPage = page,
                ItemsPerPage = limit,
                TotalItems = total,
                TotalPages = (int)Math.Ceiling(total / (double)limit),
            },
        };
    }

    /// <summary>"98XXXXXX10": first two and last two digits.</summary>
    private static string Mask(string mobile)
    {
        var m = mobile.Trim();
        return m.Length <= 4 ? new string('X', m.Length) : m[..2] + new string('X', m.Length - 4) + m[^2..];
    }
}
