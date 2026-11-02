using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Repositories
{
    public class InsightsRepository : IInsightsRepository
    {
        private const string NexeaglePublicSource = "NEXEAGLE_PUBLIC";

        private const string EventType_LoginInitiated = "login_initiated";
        private const string EventType_OtpSent = "otp_sent";
        private const string EventType_OtpVerified = "otp_verified";
        private const string EventType_SearchPerformed = "search_performed";
        private const string EventType_DoctorProfileViewed = "doctor_profile_viewed";
        private const string EventType_BookingStepReached = "booking_step_reached";

        private readonly AppDbContext _db;
        private readonly ILogger<InsightsRepository> _logger;

        private readonly TrackingHashSettings _hashSettings;

        public InsightsRepository(AppDbContext db, ILogger<InsightsRepository> logger, TrackingHashSettings hashSettings)
        {
            _db = db;
            _logger = logger;
            _hashSettings = hashSettings;
        }

        // Mobiles in AnalyticsEvents are stored masked + hashed. A full number typed into search is
        // matched by its hash; anything shorter can only match the visible digits of the masked copy.
        private string? SearchHash(string search) =>
            TrackingPrivacy.NormalizeMobile(search) is { Length: 10 } normalized
                ? TrackingPrivacy.HashMobile(normalized, _hashSettings.MobileHashKey)
                : null;

        // The paged reports below filter, sort and page in SQL. If EF Core ever fails to translate one
        // of those queries (InvalidOperationException), the report falls back to the original
        // in-memory path rather than failing the page, and logs it so the query can be fixed.
        private void LogFallback(string report, Exception ex) =>
            _logger.LogWarning(ex, "Insights {Report}: SQL paging could not be translated; used the in-memory fallback", report);

        private static IQueryable<AnalyticsEvent> ApplyDateRange(IQueryable<AnalyticsEvent> query, DateOnly? from, DateOnly? to)
        {
            // India-time days (TrackingTime); rows are stored in UTC.
            if (from.HasValue) { var start = TrackingTime.StartUtc(from.Value); query = query.Where(e => e.OccurredAt >= start); }
            if (to.HasValue) { var end = TrackingTime.EndUtcExclusive(to.Value); query = query.Where(e => e.OccurredAt < end); }
            return query;
        }

        private class SearchMetadata
        {
            public string? Query { get; set; }
            public int? ResultsCount { get; set; }
            public bool AiUsed { get; set; }
        }

        private class BookingStepMetadata
        {
            public string? Step { get; set; }
        }

        // "98XXXXXX10" — keeps the first 2 + last 2 digits, masks the rest. Short numbers (<=4
        // digits, shouldn't happen for a real mobile but guards against a malformed row) are
        // masked entirely rather than risk showing too much of a short string.
        private static string MaskMobile(string? mobile)
        {
            if (string.IsNullOrWhiteSpace(mobile)) return string.Empty;
            if (mobile.Length <= 4) return new string('X', mobile.Length);
            return mobile[..2] + new string('X', mobile.Length - 4) + mobile[^2..];
        }

        private const string LeadSource_DoctorDekho = "DoctorDekho";
        private const string Country_India = "India";
        private const int TopN = 15;

        // People-only tracking rows (EasyHMS flags crawlers / data-centre traffic as IsAutomated).
        private IQueryable<AnalyticsEvent> HumanEvents => _db.AnalyticsEvents.AsNoTracking().Where(e => !e.IsAutomated);

        private static (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to)
        {
            // No bounds = the tracking retention period (365 days), so "all time" stays bounded.
            var end = to ?? TrackingTime.TodayIst();
            var start = from ?? end.AddDays(-364);
            return start <= end ? (start, end) : (end, start);
        }

        private static string? NormalizeSite(string? site) =>
            site?.Trim().ToLowerInvariant() switch { "doctordekho" => "doctordekho", "nexeagle" => "nexeagle", _ => null };

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static List<LocationCount> Top<T>(IEnumerable<T> rows, Func<T, string?> name, Func<T, string?>? parent = null) =>
            rows.Select(r => new { Name = Clean(name(r)), Parent = parent == null ? null : Clean(parent(r)) })
                .Where(r => r.Name != null)
                .GroupBy(r => new { r.Name, r.Parent })
                .Select(g => new LocationCount { Name = g.Key.Name!, Parent = g.Key.Parent, Visits = g.Count() })
                .OrderByDescending(x => x.Visits).ThenBy(x => x.Name)
                .Take(TopN)
                .ToList();

        // One visit (browser session) of a person.
        private sealed record VisitRow(int PageViews, DateTime StartedAt, string? Country, string? Region, string? City, string? Site);

        private static async Task<(List<VisitRow>, List<PageVisitCount>, Dictionary<DateOnly, int>)> SiteVisitAggregatesSqlAsync(IQueryable<WebsiteVisit> human)
        {
            // Location/site per visit: a session stays on one network almost always, so MAX per column
            // is a safe pick. Page views without a session id count as a visit each.
            var visits = (await human
                    .GroupBy(v => v.SessionId ?? v.VisitId.ToString())
                    .Select(g => new
                    {
                        PageViews = g.Count(),
                        StartedAt = g.Min(v => v.VisitedAt),
                        Country = g.Max(v => v.Country),
                        Region = g.Max(v => v.Region),
                        City = g.Max(v => v.City),
                        Site = g.Max(v => v.Site),
                    })
                    .ToListAsync())
                .Select(x => new VisitRow(x.PageViews, x.StartedAt, x.Country, x.Region, x.City, x.Site))
                .ToList();

            var pages = await human
                .Where(v => v.PagePath != null)
                .GroupBy(v => v.PagePath)
                .Select(g => new PageVisitCount
                {
                    PagePath = g.Key,
                    Count = g.Count(),
                    Visits = g.Select(v => v.SessionId ?? v.VisitId.ToString()).Distinct().Count(),
                })
                .OrderByDescending(p => p.Visits).ThenByDescending(p => p.Count)
                .Take(TopN)
                .ToListAsync();

            // India-time day = UTC + 5:30.
            var istMinutes = (int)TrackingTime.IstOffset.TotalMinutes;
            var pageViewsByDay = (await human
                    .GroupBy(v => v.VisitedAt.AddMinutes(istMinutes).Date)
                    .Select(g => new { Day = g.Key, Count = g.Count() })
                    .ToListAsync())
                .ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);

            return (visits, pages, pageViewsByDay);
        }

        private static async Task<(List<VisitRow>, List<PageVisitCount>, Dictionary<DateOnly, int>)> SiteVisitAggregatesInMemoryAsync(IQueryable<WebsiteVisit> human)
        {
            var rows = await human
                .Select(v => new { v.SessionId, v.VisitId, v.VisitedAt, v.Country, v.Region, v.City, v.Site, v.PagePath })
                .ToListAsync();
            string Key(string? sessionId, Guid visitId) => sessionId ?? visitId.ToString();

            var visits = rows
                .GroupBy(v => Key(v.SessionId, v.VisitId))
                .Select(g => new VisitRow(g.Count(), g.Min(v => v.VisitedAt), g.Max(v => v.Country), g.Max(v => v.Region), g.Max(v => v.City), g.Max(v => v.Site)))
                .ToList();
            var pages = rows
                .Where(v => v.PagePath != null)
                .GroupBy(v => v.PagePath)
                .Select(g => new PageVisitCount { PagePath = g.Key, Count = g.Count(), Visits = g.Select(v => Key(v.SessionId, v.VisitId)).Distinct().Count() })
                .OrderByDescending(p => p.Visits).ThenByDescending(p => p.Count)
                .Take(TopN)
                .ToList();
            var pageViewsByDay = rows.GroupBy(v => TrackingTime.IstDate(v.VisitedAt)).ToDictionary(g => g.Key, g => g.Count());
            return (visits, pages, pageViewsByDay);
        }

        public async Task<SiteVisitStats> GetSiteVisitStatsAsync(DateOnly? from, DateOnly? to, string? site)
        {
            var (fromDay, toDay) = ResolveRange(from, to);
            var startUtc = TrackingTime.StartUtc(fromDay);
            var endUtc = TrackingTime.EndUtcExclusive(toDay);
            var siteKey = NormalizeSite(site);

            var inRange = _db.WebsiteVisits.AsNoTracking().Where(v => v.VisitedAt >= startUtc && v.VisitedAt < endUtc);
            if (siteKey != null) inRange = inRange.Where(v => v.Site == siteKey);

            // Automated traffic: counted only to show how much was left out.
            var automatedPageViews = await inRange.CountAsync(v => v.IsAutomated);
            var automatedVisits = await inRange.Where(v => v.IsAutomated)
                .Select(v => v.SessionId ?? v.VisitId.ToString()).Distinct().CountAsync();

            var human = inRange.Where(v => !v.IsAutomated);

            List<VisitRow> visits;
            List<PageVisitCount> pages;
            Dictionary<DateOnly, int> pageViewsByDay;
            try
            {
                (visits, pages, pageViewsByDay) = await SiteVisitAggregatesSqlAsync(human);
            }
            catch (InvalidOperationException ex)
            {
                LogFallback("site-visits", ex);
                (visits, pages, pageViewsByDay) = await SiteVisitAggregatesInMemoryAsync(human);
            }

            var india = visits.Where(v => string.Equals(Clean(v.Country), Country_India, StringComparison.OrdinalIgnoreCase)).ToList();
            var unknown = visits.Count(v => Clean(v.Country) == null);

            var visitsByDay = visits.GroupBy(v => TrackingTime.IstDate(v.StartedAt)).ToDictionary(g => g.Key, g => g.Count());
            var daily = visitsByDay.Keys.Union(pageViewsByDay.Keys).OrderBy(d => d)
                .Select(d => new DailyVisitCount
                {
                    Date = d.ToString("yyyy-MM-dd"),
                    Count = visitsByDay.GetValueOrDefault(d),
                    PageViews = pageViewsByDay.GetValueOrDefault(d),
                })
                .ToList();

            // Care demand by hospital location: exact (the hospital's own address), not IP-based.
            var leadCities = siteKey == "nexeagle"
                ? new List<LocationCount>()
                : Top(
                    (await (from l in _db.HospitalLeads.AsNoTracking()
                            where l.Source == LeadSource_DoctorDekho && !l.IsAutomated
                                  && l.OccurredAt >= startUtc && l.OccurredAt < endUtc
                            join h in _db.Hospitals.AsNoTracking() on l.HospitalId equals h.HospitalID
                            select new { Visit = l.SessionId ?? l.LeadId.ToString(), h.City, h.State })
                        .Distinct()
                        .ToListAsync()),
                    r => r.City, r => r.State);

            var bookingCities = siteKey == "nexeagle"
                ? new List<LocationCount>()
                : Top(
                    await (from a in _db.Appointments.AsNoTracking()
                           where a.BookingSource == NexeaglePublicSource && a.CreatedAt >= startUtc && a.CreatedAt < endUtc
                           join h in _db.Hospitals.AsNoTracking() on a.HospitalID equals h.HospitalID
                           select new { h.City, h.State })
                        .ToListAsync(),
                    r => r.City, r => r.State);

            var pageViews = visits.Sum(v => v.PageViews);
            var indiaCities = Top(india, v => v.City, v => v.Region);

            return new SiteVisitStats
            {
                Site = siteKey,
                From = fromDay.ToString("yyyy-MM-dd"),
                To = toDay.ToString("yyyy-MM-dd"),
                Visits = visits.Count,
                PageViews = pageViews,
                AutomatedVisitsExcluded = automatedVisits,
                AutomatedPageViewsExcluded = automatedPageViews,
                VisitsInIndia = india.Count,
                VisitsOutsideIndia = visits.Count - india.Count - unknown,
                VisitsLocationUnknown = unknown,
                IndiaStates = Top(india, v => v.Region),
                IndiaCities = indiaCities,
                Countries = Top(visits, v => v.Country),
                CareInterestCities = leadCities,
                BookingCities = bookingCities,
                TopPages = pages,
                DailyTrend = daily,
                Sites = visits
                    .GroupBy(v => Clean(v.Site) ?? "unknown")
                    .Select(g => new LocationCount { Name = g.Key, Visits = g.Count() })
                    .OrderByDescending(x => x.Visits)
                    .ToList(),
                // Older CMS Web builds
                TotalVisits = pageViews,
                UniqueVisitors = visits.Count,
                TopRegions = indiaCities.Select(c => new RegionVisitCount { City = c.Name, Region = c.Parent, Country = Country_India, Count = c.Visits }).ToList(),
            };
        }

        public async Task<PagedResult<PatientLoginItem>> GetPatientLoginsAsync(int page, int limit, string? search, string? sortBy, string? sortDir)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            // Only numbers that have actually logged in at least once — a row can exist here purely
            // from an OTP send that was never verified.
            var query = _db.PublicPatientAuths.AsNoTracking().Where(a => a.LastLoginAt != null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(a => a.Mobile.Contains(s));
            }

            var isAsc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            query = sortBy?.ToLowerInvariant() switch
            {
                "logincount" => isAsc ? query.OrderBy(a => a.LoginCount) : query.OrderByDescending(a => a.LoginCount),
                "firstseenat" => isAsc ? query.OrderBy(a => a.CreatedAt) : query.OrderByDescending(a => a.CreatedAt),
                _ => isAsc ? query.OrderBy(a => a.LastLoginAt) : query.OrderByDescending(a => a.LastLoginAt),
            };

            var totalItems = await query.LongCountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

            var items = await query.Skip((page - 1) * limit).Take(limit).ToListAsync();
            var projected = items.Select(a => new PatientLoginItem
            {
                MobileMasked = MaskMobile(a.Mobile),
                LastLoginAt = a.LastLoginAt,
                LoginCount = a.LoginCount,
                FirstSeenAt = a.CreatedAt,
            }).ToList();

            return new PagedResult<PatientLoginItem>
            {
                Data = projected,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        public async Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source, string? dateBy)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;
            try
            {
                return await GetOnlineAppointmentsSqlAsync(page, limit, from, to, search, sortBy, sortDir, source, dateBy);
            }
            catch (InvalidOperationException ex)
            {
                LogFallback("online-appointments", ex);
                return await GetOnlineAppointmentsInMemoryAsync(page, limit, from, to, search, sortBy, sortDir, source, dateBy);
            }
        }

        // StatusMaster code -> display name.
        private async Task<Dictionary<string, string>> StatusNamesAsync() =>
            (await _db.StatusMaster.AsNoTracking().Select(x => new { x.StatusCode, x.StatusName }).ToListAsync())
                .Where(x => !string.IsNullOrWhiteSpace(x.StatusName))
                .GroupBy(x => x.StatusCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().StatusName!, StringComparer.OrdinalIgnoreCase);

        // dateBy = appointment: from/to filter the appointment date. Otherwise (default) they filter the
        // day the booking was made, in India time, so "Today" means "booked today".
        private IQueryable<Appointment> OnlineAppointmentsQuery(DateOnly? from, DateOnly? to, string? source, string? dateBy)
        {
            var query = _db.Appointments.AsNoTracking().Where(a => a.BookingSource == NexeaglePublicSource);
            if (string.Equals(dateBy, "appointment", StringComparison.OrdinalIgnoreCase))
            {
                if (from.HasValue) query = query.Where(a => a.ApptDate >= from.Value);
                if (to.HasValue) query = query.Where(a => a.ApptDate <= to.Value);
            }
            else
            {
                if (from.HasValue) { var start = TrackingTime.StartUtc(from.Value); query = query.Where(a => a.CreatedAt >= start); }
                if (to.HasValue) { var end = TrackingTime.EndUtcExclusive(to.Value); query = query.Where(a => a.CreatedAt < end); }
            }
            if (string.Equals(source, "Guest", StringComparison.OrdinalIgnoreCase))
                query = query.Where(a => a.BookedByMobile == null);
            else if (string.Equals(source, "LoggedIn", StringComparison.OrdinalIgnoreCase))
                query = query.Where(a => a.BookedByMobile != null);
            return query;
        }

        // One SQL query per page: names come from correlated subqueries, so search, sort, count and
        // paging all run in the database and only `limit` rows come back.
        private async Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsSqlAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source, string? dateBy)
        {
            var rows = OnlineAppointmentsQuery(from, to, source, dateBy).Select(a => new
            {
                a.ApptId,
                a.CreatedAt,
                a.ApptDate,
                a.CurrentStatusCode,
                a.BookedByMobile,
                a.BookingReferrerUrl,
                a.BookingUtmCampaign,
                PatientName = _db.PatientRegistrations
                    .Where(p => p.PatientID == a.PatientID && p.HospitalID == a.HospitalID)
                    .Select(p => p.FullName).FirstOrDefault(),
                PatientMobile = _db.PatientRegistrations
                    .Where(p => p.PatientID == a.PatientID && p.HospitalID == a.HospitalID)
                    .Select(p => p.Mobile).FirstOrDefault(),
                DoctorName = (from d in _db.Doctors
                              join u in _db.UserProfiles on d.UserID equals u.UserID
                              where d.DoctorID == a.DoctorID
                              select u.FullName).FirstOrDefault(),
                HospitalName = _db.Hospitals
                    .Where(h => h.HospitalID == a.HospitalID)
                    .Select(h => h.Name).FirstOrDefault(),
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                rows = rows.Where(r =>
                    (r.PatientName != null && r.PatientName.Contains(s))
                    || (r.DoctorName != null && r.DoctorName.Contains(s))
                    || (r.HospitalName != null && r.HospitalName.Contains(s)));
            }

            var isAsc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            var ordered = sortBy?.ToLowerInvariant() switch
            {
                "apptdate" => isAsc ? rows.OrderBy(r => r.ApptDate) : rows.OrderByDescending(r => r.ApptDate),
                "patientname" => isAsc ? rows.OrderBy(r => r.PatientName) : rows.OrderByDescending(r => r.PatientName),
                "doctorname" => isAsc ? rows.OrderBy(r => r.DoctorName) : rows.OrderByDescending(r => r.DoctorName),
                "hospitalname" => isAsc ? rows.OrderBy(r => r.HospitalName) : rows.OrderByDescending(r => r.HospitalName),
                "status" => isAsc ? rows.OrderBy(r => r.CurrentStatusCode) : rows.OrderByDescending(r => r.CurrentStatusCode),
                "bookedat" when isAsc => rows.OrderBy(r => r.CreatedAt),
                _ => rows.OrderByDescending(r => r.CreatedAt)
            };

            var totalItems = await rows.CountAsync();
            var pageRows = await ordered.ThenBy(r => r.ApptId)
                .Skip((page - 1) * limit).Take(limit)
                .ToListAsync();

            var statusNames = await StatusNamesAsync();
            return new PagedResult<OnlineAppointmentItem>
            {
                Data = pageRows.Select(r => new OnlineAppointmentItem
                {
                    ApptId = r.ApptId,
                    BookedAt = r.CreatedAt,
                    ApptDate = r.ApptDate,
                    Status = statusNames.TryGetValue(r.CurrentStatusCode, out var sn) ? sn : r.CurrentStatusCode,
                    StatusCode = r.CurrentStatusCode,
                    PatientName = r.PatientName,
                    PatientMobileMasked = r.PatientMobile != null ? MaskMobile(r.PatientMobile) : null,
                    DoctorName = r.DoctorName,
                    HospitalName = r.HospitalName,
                    IsLoggedIn = r.BookedByMobile != null,
                    BookedByMobileMasked = r.BookedByMobile != null ? MaskMobile(r.BookedByMobile) : null,
                    ReferrerUrl = r.BookingReferrerUrl,
                    UtmCampaign = r.BookingUtmCampaign,
                }).ToList(),
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = (int)Math.Ceiling(totalItems / (double)limit), TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        // Original implementation, kept only as the fallback described at the top of the class.
        private async Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsInMemoryAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source, string? dateBy)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = OnlineAppointmentsQuery(from, to, source, dateBy);

            var matching = await query
                .Select(a => new
                {
                    a.ApptId, a.CreatedAt, a.ApptDate, a.CurrentStatusCode, a.PatientID, a.DoctorID, a.HospitalID,
                    a.BookedByMobile, a.BookingReferrerUrl, a.BookingUtmCampaign
                })
                .ToListAsync();

            var patientIds = matching.Select(a => a.PatientID).Distinct().ToList();
            var doctorIds = matching.Select(a => a.DoctorID).Distinct().ToList();
            var hospitalIds = matching.Select(a => a.HospitalID).Distinct().ToList();

            var patientsById = await _db.PatientRegistrations.AsNoTracking()
                .Where(p => patientIds.Contains(p.PatientID))
                .ToDictionaryAsync(p => p.PatientID);

            var doctors = await _db.Doctors.AsNoTracking()
                .Where(d => doctorIds.Contains(d.DoctorID))
                .Select(d => new { d.DoctorID, d.UserID })
                .ToListAsync();
            var doctorUserIds = doctors.Select(d => d.UserID).Distinct().ToList();
            var doctorNamesByUserId = await _db.UserProfiles.AsNoTracking()
                .Where(up => doctorUserIds.Contains(up.UserID))
                .ToDictionaryAsync(up => up.UserID, up => up.FullName);
            var doctorNameById = doctors.ToDictionary(
                d => d.DoctorID,
                d => doctorNamesByUserId.TryGetValue(d.UserID, out var n) ? n : null);

            var hospitalNameById = await _db.Hospitals.AsNoTracking()
                .Where(h => hospitalIds.Contains(h.HospitalID))
                .ToDictionaryAsync(h => h.HospitalID, h => h.Name);

            var statusNames = await StatusNamesAsync();
            IEnumerable<OnlineAppointmentItem> projected = matching.Select(a =>
            {
                patientsById.TryGetValue(a.PatientID, out var patient);
                return new OnlineAppointmentItem
                {
                    ApptId = a.ApptId,
                    BookedAt = a.CreatedAt,
                    ApptDate = a.ApptDate,
                    Status = statusNames.TryGetValue(a.CurrentStatusCode, out var sn) ? sn : a.CurrentStatusCode,
                    StatusCode = a.CurrentStatusCode,
                    PatientName = patient?.FullName,
                    PatientMobileMasked = patient?.Mobile != null ? MaskMobile(patient.Mobile) : null,
                    DoctorName = doctorNameById.TryGetValue(a.DoctorID, out var dn) ? dn : null,
                    HospitalName = hospitalNameById.TryGetValue(a.HospitalID, out var hn) ? hn : null,
                    IsLoggedIn = a.BookedByMobile != null,
                    BookedByMobileMasked = a.BookedByMobile != null ? MaskMobile(a.BookedByMobile) : null,
                    ReferrerUrl = a.BookingReferrerUrl,
                    UtmCampaign = a.BookingUtmCampaign,
                };
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                projected = projected.Where(p =>
                    (p.PatientName != null && p.PatientName.Contains(s, StringComparison.OrdinalIgnoreCase))
                    || (p.DoctorName != null && p.DoctorName.Contains(s, StringComparison.OrdinalIgnoreCase))
                    || (p.HospitalName != null && p.HospitalName.Contains(s, StringComparison.OrdinalIgnoreCase)));
            }

            var isAsc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            IOrderedEnumerable<OnlineAppointmentItem> sorted = sortBy?.ToLowerInvariant() switch
            {
                "apptdate" => isAsc ? projected.OrderBy(p => p.ApptDate) : projected.OrderByDescending(p => p.ApptDate),
                "patientname" => isAsc ? projected.OrderBy(p => p.PatientName) : projected.OrderByDescending(p => p.PatientName),
                "doctorname" => isAsc ? projected.OrderBy(p => p.DoctorName) : projected.OrderByDescending(p => p.DoctorName),
                "hospitalname" => isAsc ? projected.OrderBy(p => p.HospitalName) : projected.OrderByDescending(p => p.HospitalName),
                "status" => isAsc ? projected.OrderBy(p => p.Status) : projected.OrderByDescending(p => p.Status),
                "bookedat" when isAsc => projected.OrderBy(p => p.BookedAt),
                _ => projected.OrderByDescending(p => p.BookedAt)
            };

            var sortedList = sorted.ToList();
            var totalItems = sortedList.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);
            var pageItems = sortedList.Skip((page - 1) * limit).Take(limit).ToList();

            return new PagedResult<OnlineAppointmentItem>
            {
                Data = pageItems,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        // Grouped by SessionId — one browser session is one login journey (open form -> send OTP
        // -> verify), which is what makes "bounced at verification" and Time-to-Authenticate
        // meaningful (vs. counting raw events, which double-counts a resent OTP).
        public async Task<AuthFunnelStats> GetAuthFunnelStatsAsync(DateOnly? from, DateOnly? to)
        {
            var eventsQuery = ApplyDateRange(
                HumanEvents.Where(e =>
                    e.EventType == EventType_LoginInitiated || e.EventType == EventType_OtpSent || e.EventType == EventType_OtpVerified),
                from, to);
            var events = await eventsQuery
                .Select(e => new { e.EventType, e.SessionId, e.Mobile, e.OccurredAt })
                .ToListAsync();

            // Denominator: people's visits to Doctor Dekho (the only site with a login), India-time days.
            var visitQuery = _db.WebsiteVisits.AsNoTracking()
                .Where(v => !v.IsAutomated && (v.Site == null || v.Site == "doctordekho"));
            if (from.HasValue) { var start = TrackingTime.StartUtc(from.Value); visitQuery = visitQuery.Where(v => v.VisitedAt >= start); }
            if (to.HasValue) { var end = TrackingTime.EndUtcExclusive(to.Value); visitQuery = visitQuery.Where(v => v.VisitedAt < end); }
            var totalVisitorSessions = await visitQuery.Where(v => v.SessionId != null).Select(v => v.SessionId).Distinct().CountAsync();

            int loginInitiated = 0, otpSent = 0, otpVerified = 0;
            var timeToAuthSeconds = new List<double>();

            foreach (var grp in events.Where(e => e.SessionId != null).GroupBy(e => e.SessionId))
            {
                var sentEvt = grp.Where(e => e.EventType == EventType_OtpSent).OrderBy(e => e.OccurredAt).FirstOrDefault();
                var verifiedEvt = grp.Where(e => e.EventType == EventType_OtpVerified).OrderBy(e => e.OccurredAt).FirstOrDefault();

                if (grp.Any(e => e.EventType == EventType_LoginInitiated)) loginInitiated++;
                if (sentEvt != null) otpSent++;
                if (verifiedEvt != null)
                {
                    otpVerified++;
                    if (sentEvt != null && verifiedEvt.OccurredAt >= sentEvt.OccurredAt)
                        timeToAuthSeconds.Add((verifiedEvt.OccurredAt - sentEvt.OccurredAt).TotalSeconds);
                }
            }

            return new AuthFunnelStats
            {
                TotalVisitorSessions = totalVisitorSessions,
                LoginInitiatedSessions = loginInitiated,
                OtpSentSessions = otpSent,
                OtpVerifiedSessions = otpVerified,
                // Clamped to 100: LoginInitiated/OtpSent/OtpVerified come from AnalyticsEvents,
                // totalVisitorSessions from the separate WebsiteVisits table -- both are
                // independent, best-effort "fire and forget, never throws" tracking beacons (see
                // NexEagleWebsite's analytics.ts) that can fail independently (ad blockers
                // commonly target exactly this kind of endpoint), so a session's funnel event can
                // exist with no matching page-view row. Without a clamp that edge case would
                // render as a rate over 100%.
                LoginInitiationRatePct = totalVisitorSessions > 0 ? Math.Min(100, Math.Round(loginInitiated * 100.0 / totalVisitorSessions, 1)) : 0,
                AuthCompletionRatePct = otpSent > 0 ? Math.Min(100, Math.Round(otpVerified * 100.0 / otpSent, 1)) : 0,
                AvgTimeToAuthenticateSeconds = timeToAuthSeconds.Count > 0 ? Math.Round(timeToAuthSeconds.Average(), 1) : (double?)null,
            };
        }

        public async Task<PagedResult<AuthFunnelAttemptItem>> GetAuthFunnelAttemptsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;
            try
            {
                return await GetAuthFunnelAttemptsSqlAsync(page, limit, from, to, search);
            }
            catch (InvalidOperationException ex)
            {
                LogFallback("auth-funnel-attempts", ex);
                return await GetAuthFunnelAttemptsInMemoryAsync(page, limit, from, to, search);
            }
        }

        // Sessions are grouped, counted, sorted and paged in SQL; only the events of the sessions on
        // this page are loaded, then turned into rows by the same rules as before (BuildAttempt).
        private async Task<PagedResult<AuthFunnelAttemptItem>> GetAuthFunnelAttemptsSqlAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search)
        {
            var eventsQuery = ApplyDateRange(
                HumanEvents.Where(e =>
                    (e.EventType == EventType_OtpSent || e.EventType == EventType_OtpVerified) && e.SessionId != null),
                from, to);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                var hash = SearchHash(s);
                var matchingSessions = hash != null
                    ? eventsQuery.Where(e => e.MobileHash == hash).Select(e => e.SessionId)
                    : eventsQuery.Where(e => e.Mobile != null && e.Mobile.Contains(s)).Select(e => e.SessionId);
                eventsQuery = eventsQuery.Where(e => matchingSessions.Contains(e.SessionId));
            }

            var sessions = eventsQuery
                .GroupBy(e => e.SessionId)
                .Select(g => new
                {
                    SessionId = g.Key,
                    AnyMobile = g.Max(e => e.Mobile),
                    SentAt = g.Min(e => e.EventType == EventType_OtpSent ? (DateTime?)e.OccurredAt : null),
                    VerifiedAt = g.Min(e => e.EventType == EventType_OtpVerified ? (DateTime?)e.OccurredAt : null),
                })
                .Where(x => x.AnyMobile != null && x.AnyMobile != "");

            var totalItems = await sessions.CountAsync();
            var pageSessionIds = await sessions
                .OrderByDescending(x => x.VerifiedAt ?? x.SentAt)
                .ThenBy(x => x.SessionId)
                .Skip((page - 1) * limit).Take(limit)
                .Select(x => x.SessionId)
                .ToListAsync();

            var pageEvents = await eventsQuery
                .Where(e => pageSessionIds.Contains(e.SessionId))
                .Select(e => new AuthEventRow(e.EventType, e.SessionId, e.Mobile, e.OccurredAt, e.Country, e.Region, e.City))
                .ToListAsync();
            var bySession = pageEvents.GroupBy(e => e.SessionId).ToDictionary(g => g.Key!, g => g.ToList());

            var items = new List<AuthFunnelAttemptItem>();
            foreach (var sessionId in pageSessionIds)
            {
                if (sessionId != null && bySession.TryGetValue(sessionId, out var evts) && BuildAttempt(evts) is { } attempt)
                    items.Add(attempt.Item);
            }

            return new PagedResult<AuthFunnelAttemptItem>
            {
                Data = items,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = (int)Math.Ceiling(totalItems / (double)limit), TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        private sealed record AuthEventRow(string EventType, string? SessionId, string? Mobile, DateTime OccurredAt, string? Country, string? Region, string? City);

        // One login journey -> one row: first OTP sent, first OTP verified, the mobile and region.
        private static (string Mobile, AuthFunnelAttemptItem Item)? BuildAttempt(IEnumerable<AuthEventRow> sessionEvents)
        {
            var sentEvt = sessionEvents.Where(e => e.EventType == EventType_OtpSent).OrderBy(e => e.OccurredAt).FirstOrDefault();
            var verifiedEvt = sessionEvents.Where(e => e.EventType == EventType_OtpVerified).OrderBy(e => e.OccurredAt).FirstOrDefault();
            var mobile = verifiedEvt?.Mobile ?? sentEvt?.Mobile;
            if (string.IsNullOrWhiteSpace(mobile)) return null;

            var regionSource = verifiedEvt ?? sentEvt!;
            double? timeToAuth = null;
            if (sentEvt != null && verifiedEvt != null && verifiedEvt.OccurredAt >= sentEvt.OccurredAt)
                timeToAuth = Math.Round((verifiedEvt.OccurredAt - sentEvt.OccurredAt).TotalSeconds, 1);

            return (mobile, new AuthFunnelAttemptItem
            {
                MobileMasked = MaskMobile(mobile),
                OtpSentAt = sentEvt?.OccurredAt,
                VerifiedAt = verifiedEvt?.OccurredAt,
                Outcome = verifiedEvt != null ? "Verified" : "Bounced",
                TimeToAuthenticateSeconds = timeToAuth,
                Country = regionSource.Country,
                Region = regionSource.Region,
                City = regionSource.City,
            });
        }

        // Original implementation, kept only as the fallback described at the top of the class.
        private async Task<PagedResult<AuthFunnelAttemptItem>> GetAuthFunnelAttemptsInMemoryAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search)
        {
            var eventsQuery = ApplyDateRange(
                HumanEvents.Where(e =>
                    e.EventType == EventType_OtpSent || e.EventType == EventType_OtpVerified),
                from, to);
            var events = await eventsQuery
                .Select(e => new { e.EventType, e.SessionId, e.Mobile, e.OccurredAt, e.Country, e.Region, e.City })
                .ToListAsync();

            var attempts = new List<(string Mobile, AuthFunnelAttemptItem Item)>();
            foreach (var grp in events.Where(e => e.SessionId != null).GroupBy(e => e.SessionId))
            {
                var sentEvt = grp.Where(e => e.EventType == EventType_OtpSent).OrderBy(e => e.OccurredAt).FirstOrDefault();
                var verifiedEvt = grp.Where(e => e.EventType == EventType_OtpVerified).OrderBy(e => e.OccurredAt).FirstOrDefault();
                var mobile = verifiedEvt?.Mobile ?? sentEvt?.Mobile;
                if (string.IsNullOrWhiteSpace(mobile)) continue;

                var regionSource = verifiedEvt ?? sentEvt!;
                double? timeToAuth = null;
                if (sentEvt != null && verifiedEvt != null && verifiedEvt.OccurredAt >= sentEvt.OccurredAt)
                    timeToAuth = Math.Round((verifiedEvt.OccurredAt - sentEvt.OccurredAt).TotalSeconds, 1);

                attempts.Add((mobile, new AuthFunnelAttemptItem
                {
                    MobileMasked = MaskMobile(mobile),
                    OtpSentAt = sentEvt?.OccurredAt,
                    VerifiedAt = verifiedEvt?.OccurredAt,
                    Outcome = verifiedEvt != null ? "Verified" : "Bounced",
                    TimeToAuthenticateSeconds = timeToAuth,
                    Country = regionSource.Country,
                    Region = regionSource.Region,
                    City = regionSource.City,
                }));
            }

            IEnumerable<(string Mobile, AuthFunnelAttemptItem Item)> filtered = attempts;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                filtered = filtered.Where(a => a.Mobile.Contains(s));
            }

            var sortedList = filtered
                .OrderByDescending(a => a.Item.VerifiedAt ?? a.Item.OtpSentAt ?? DateTime.MinValue)
                .Select(a => a.Item)
                .ToList();
            var totalItems = sortedList.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);
            var pageItems = sortedList.Skip((page - 1) * limit).Take(limit).ToList();

            return new PagedResult<AuthFunnelAttemptItem>
            {
                Data = pageItems,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        public async Task<BookingFunnelStats> GetBookingFunnelStatsAsync(DateOnly? from, DateOnly? to)
        {
            var query = ApplyDateRange(
                HumanEvents.Where(e =>
                    e.EventType == EventType_SearchPerformed || e.EventType == EventType_DoctorProfileViewed || e.EventType == EventType_BookingStepReached),
                from, to);
            var events = (await query
                    .Select(e => new { e.EventId, e.EventType, e.SessionId, e.DoctorId, e.SpecialtyId, e.OccurredAt, e.MetadataJson })
                    .ToListAsync())
                .Select(e => new
                {
                    e.EventType,
                    Visit = e.SessionId ?? e.EventId.ToString(),   // no session id: the event is its own visit
                    e.DoctorId,
                    SpecialtyId = string.IsNullOrWhiteSpace(e.SpecialtyId) ? null : e.SpecialtyId.Trim().ToLowerInvariant(),
                    e.OccurredAt,
                    e.MetadataJson,
                })
                .ToList();

            // Searches: one per visit + query text + specialty (the site used to log the same search again
            // when more results loaded or the symptom router answered).
            var searches = events.Where(e => e.EventType == EventType_SearchPerformed)
                .Select(e => new { e.Visit, e.SpecialtyId, e.OccurredAt, Query = (ParseJson<SearchMetadata>(e.MetadataJson)?.Query ?? "").Trim().ToLowerInvariant() })
                .ToList();
            var distinctSearches = searches.Select(e => (e.Visit, e.Query, e.SpecialtyId)).Distinct().Count();
            var firstSearchAt = searches.GroupBy(e => e.Visit).ToDictionary(g => g.Key, g => g.Min(e => e.OccurredAt));

            var views = events.Where(e => e.EventType == EventType_DoctorProfileViewed).ToList();
            var viewPairs = views.Select(e => (e.Visit, e.DoctorId)).Distinct().ToList();
            var lastViewAt = views.GroupBy(e => e.Visit).ToDictionary(g => g.Key, g => g.Max(e => e.OccurredAt));
            var searchedThenViewed = firstSearchAt.Count(kv => lastViewAt.TryGetValue(kv.Key, out var viewedAt) && viewedAt >= kv.Value);

            var steps = events.Where(e => e.EventType == EventType_BookingStepReached)
                .Select(e => new { e.Visit, e.DoctorId, e.SpecialtyId, Step = ParseJson<BookingStepMetadata>(e.MetadataJson)?.Step?.Trim().ToLowerInvariant() })
                .Where(e => e.Step != null)
                .ToList();
            int StepPairs(string step) => steps.Where(e => e.Step == step).Select(e => (e.Visit, e.DoctorId)).Distinct().Count();
            var donePairs = steps.Where(e => e.Step == "done").Select(e => (e.Visit, e.DoctorId, e.SpecialtyId)).Distinct().ToList();

            var bookings = _db.Appointments.AsNoTracking().Where(a => a.BookingSource == NexeaglePublicSource);
            if (from.HasValue) { var start = TrackingTime.StartUtc(from.Value); bookings = bookings.Where(a => a.CreatedAt >= start); }
            if (to.HasValue) { var end = TrackingTime.EndUtcExclusive(to.Value); bookings = bookings.Where(a => a.CreatedAt < end); }
            var bookingsRecorded = await bookings.CountAsync();

            var specialtyIds = searches.Select(e => e.SpecialtyId)
                .Concat(views.Select(e => e.SpecialtyId))
                .Concat(donePairs.Select(e => e.SpecialtyId))
                .Where(sid => sid != null)
                .Select(sid => sid!)
                .Distinct()
                .ToList();

            var specialtyDemand = specialtyIds.Select(sid => new SpecialtyDemandItem
            {
                SpecialtyId = sid,
                SearchCount = searches.Where(e => e.SpecialtyId == sid).Select(e => e.Visit).Distinct().Count(),
                ProfileViewCount = views.Where(e => e.SpecialtyId == sid).Select(e => (e.Visit, e.DoctorId)).Distinct().Count(),
                CompletedBookingCount = donePairs.Count(e => e.SpecialtyId == sid),
            })
            .OrderByDescending(x => x.SearchCount + x.ProfileViewCount)
            .ThenBy(x => x.SpecialtyId)
            .ToList();

            return new BookingFunnelStats
            {
                SearchCount = distinctSearches,
                SearchSessionCount = firstSearchAt.Count,
                ProfileViewCount = viewPairs.Count,
                SearchToViewRatePct = firstSearchAt.Count > 0 ? Math.Round(searchedThenViewed * 100.0 / firstSearchAt.Count, 1) : 0,
                VisitStepCount = StepPairs("visit"),
                DetailsStepCount = StepPairs("details"),
                DoneStepCount = StepPairs("done"),
                BookingsRecorded = bookingsRecorded,
                SpecialtyDemand = specialtyDemand,
            };
        }

        public async Task<PagedResult<SearchLogItem>> GetSearchLogAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = ApplyDateRange(
                HumanEvents.Where(e => e.EventType == EventType_SearchPerformed),
                from, to);

            // The common case (no text filter, sort by time or specialty) pages in SQL. The query text and
            // result count live inside MetadataJson, so a text search or a result-count sort still
            // needs the JSON parsed in memory, but only over rows SQL has already narrowed down.
            var sortKey = sortBy?.ToLowerInvariant();
            var sqlSortable = sortKey is null or "" or "occurredat" or "specialtyid";
            if (string.IsNullOrWhiteSpace(search) && sqlSortable)
            {
                try
                {
                    var isAscSql = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
                    var ordered = sortKey == "specialtyid"
                        ? (isAscSql ? query.OrderBy(e => e.SpecialtyId) : query.OrderByDescending(e => e.SpecialtyId))
                        : (isAscSql ? query.OrderBy(e => e.OccurredAt) : query.OrderByDescending(e => e.OccurredAt));
                    var total = await query.CountAsync();
                    var pageRows = await ordered.ThenBy(e => e.EventId)
                        .Skip((page - 1) * limit).Take(limit)
                        .Select(e => new { e.OccurredAt, e.SpecialtyId, e.Country, e.Region, e.City, e.MetadataJson })
                        .ToListAsync();
                    return new PagedResult<SearchLogItem>
                    {
                        Data = pageRows.Select(e => ToSearchLogItem(e.OccurredAt, e.SpecialtyId, e.Country, e.Region, e.City, e.MetadataJson)).ToList(),
                        Pagination = new PaginationInfo { CurrentPage = page, TotalPages = (int)Math.Ceiling(total / (double)limit), TotalItems = total, ItemsPerPage = limit }
                    };
                }
                catch (InvalidOperationException ex)
                {
                    LogFallback("search-log", ex);
                }
            }
            else if (!string.IsNullOrWhiteSpace(search))
            {
                // Coarse SQL pre-filter (a JSON key name can also match); the exact check runs below.
                var pre = search.Trim();
                query = query.Where(e =>
                    (e.MetadataJson != null && e.MetadataJson.Contains(pre))
                    || (e.SpecialtyId != null && e.SpecialtyId.Contains(pre)));
            }

            var events = await query
                .Select(e => new { e.OccurredAt, e.SpecialtyId, e.Country, e.Region, e.City, e.MetadataJson })
                .ToListAsync();

            var items = events.Select(e => ToSearchLogItem(e.OccurredAt, e.SpecialtyId, e.Country, e.Region, e.City, e.MetadataJson));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                items = items.Where(i =>
                    (i.Query != null && i.Query.Contains(s, StringComparison.OrdinalIgnoreCase))
                    || (i.SpecialtyId != null && i.SpecialtyId.Contains(s, StringComparison.OrdinalIgnoreCase)));
            }

            var isAsc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            IOrderedEnumerable<SearchLogItem> sorted = sortBy?.ToLowerInvariant() switch
            {
                "resultscount" => isAsc ? items.OrderBy(i => i.ResultsCount) : items.OrderByDescending(i => i.ResultsCount),
                "specialtyid" => isAsc ? items.OrderBy(i => i.SpecialtyId) : items.OrderByDescending(i => i.SpecialtyId),
                _ => isAsc ? items.OrderBy(i => i.OccurredAt) : items.OrderByDescending(i => i.OccurredAt),
            };

            var sortedList = sorted.ToList();
            var totalItems = sortedList.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);
            var pageItems = sortedList.Skip((page - 1) * limit).Take(limit).ToList();

            return new PagedResult<SearchLogItem>
            {
                Data = pageItems,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        private static SearchLogItem ToSearchLogItem(DateTime occurredAt, string? specialtyId, string? country, string? region, string? city, string? metadataJson)
        {
            var meta = ParseJson<SearchMetadata>(metadataJson);
            return new SearchLogItem
            {
                OccurredAt = occurredAt,
                Query = meta?.Query,
                SpecialtyId = specialtyId,
                ResultsCount = meta?.ResultsCount,
                AiUsed = meta?.AiUsed ?? false,
                Country = country,
                Region = region,
                City = city,
            };
        }

        private static T? ParseJson<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }
    }
}
