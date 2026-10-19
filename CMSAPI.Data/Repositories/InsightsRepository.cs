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
            if (from.HasValue) query = query.Where(e => e.OccurredAt >= from.Value.ToDateTime(TimeOnly.MinValue));
            if (to.HasValue) query = query.Where(e => e.OccurredAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
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

        public async Task<SiteVisitStats> GetSiteVisitStatsAsync(DateOnly? from, DateOnly? to)
        {
            var query = _db.WebsiteVisits.AsNoTracking().AsQueryable();
            if (from.HasValue) query = query.Where(v => v.VisitedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
            if (to.HasValue) query = query.Where(v => v.VisitedAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var totalVisits = await query.CountAsync();
            var uniqueVisitors = await query
                .Where(v => v.SessionId != null)
                .Select(v => v.SessionId)
                .Distinct()
                .CountAsync();

            var topRegions = await query
                .Where(v => v.Country != null || v.Region != null || v.City != null)
                .GroupBy(v => new { v.Country, v.Region, v.City })
                .Select(g => new RegionVisitCount { Country = g.Key.Country, Region = g.Key.Region, City = g.Key.City, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(15)
                .ToListAsync();

            var topPages = await query
                .Where(v => v.PagePath != null)
                .GroupBy(v => v.PagePath)
                .Select(g => new PageVisitCount { PagePath = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(15)
                .ToListAsync();

            var dailyRaw = await query
                .GroupBy(v => v.VisitedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(g => g.Date)
                .ToListAsync();

            return new SiteVisitStats
            {
                TotalVisits = totalVisits,
                UniqueVisitors = uniqueVisitors,
                TopRegions = topRegions,
                TopPages = topPages,
                DailyTrend = dailyRaw.Select(d => new DailyVisitCount { Date = d.Date.ToString("yyyy-MM-dd"), Count = d.Count }).ToList(),
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

        public async Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;
            try
            {
                return await GetOnlineAppointmentsSqlAsync(page, limit, from, to, search, sortBy, sortDir, source);
            }
            catch (InvalidOperationException ex)
            {
                LogFallback("online-appointments", ex);
                return await GetOnlineAppointmentsInMemoryAsync(page, limit, from, to, search, sortBy, sortDir, source);
            }
        }

        private IQueryable<Appointment> OnlineAppointmentsQuery(DateOnly? from, DateOnly? to, string? source)
        {
            var query = _db.Appointments.AsNoTracking().Where(a => a.BookingSource == NexeaglePublicSource);
            if (from.HasValue) query = query.Where(a => a.ApptDate >= from.Value);
            if (to.HasValue) query = query.Where(a => a.ApptDate <= to.Value);
            if (string.Equals(source, "Guest", StringComparison.OrdinalIgnoreCase))
                query = query.Where(a => a.BookedByMobile == null);
            else if (string.Equals(source, "LoggedIn", StringComparison.OrdinalIgnoreCase))
                query = query.Where(a => a.BookedByMobile != null);
            return query;
        }

        // One SQL query per page: names come from correlated subqueries, so search, sort, count and
        // paging all run in the database and only `limit` rows come back.
        private async Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsSqlAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source)
        {
            var rows = OnlineAppointmentsQuery(from, to, source).Select(a => new
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
                _ => rows.OrderByDescending(r => r.CreatedAt)
            };

            var totalItems = await rows.CountAsync();
            var pageRows = await ordered.ThenBy(r => r.ApptId)
                .Skip((page - 1) * limit).Take(limit)
                .ToListAsync();

            return new PagedResult<OnlineAppointmentItem>
            {
                Data = pageRows.Select(r => new OnlineAppointmentItem
                {
                    ApptId = r.ApptId,
                    BookedAt = r.CreatedAt,
                    ApptDate = r.ApptDate,
                    Status = r.CurrentStatusCode,
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
        private async Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsInMemoryAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = _db.Appointments.AsNoTracking().Where(a => a.BookingSource == NexeaglePublicSource);
            if (from.HasValue) query = query.Where(a => a.ApptDate >= from.Value);
            if (to.HasValue) query = query.Where(a => a.ApptDate <= to.Value);
            if (string.Equals(source, "Guest", StringComparison.OrdinalIgnoreCase))
                query = query.Where(a => a.BookedByMobile == null);
            else if (string.Equals(source, "LoggedIn", StringComparison.OrdinalIgnoreCase))
                query = query.Where(a => a.BookedByMobile != null);

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

            IEnumerable<OnlineAppointmentItem> projected = matching.Select(a =>
            {
                patientsById.TryGetValue(a.PatientID, out var patient);
                return new OnlineAppointmentItem
                {
                    ApptId = a.ApptId,
                    BookedAt = a.CreatedAt,
                    ApptDate = a.ApptDate,
                    Status = a.CurrentStatusCode,
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
                _db.AnalyticsEvents.AsNoTracking().Where(e =>
                    e.EventType == EventType_LoginInitiated || e.EventType == EventType_OtpSent || e.EventType == EventType_OtpVerified),
                from, to);
            var events = await eventsQuery
                .Select(e => new { e.EventType, e.SessionId, e.Mobile, e.OccurredAt })
                .ToListAsync();

            var visitQuery = _db.WebsiteVisits.AsNoTracking().AsQueryable();
            if (from.HasValue) visitQuery = visitQuery.Where(v => v.VisitedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
            if (to.HasValue) visitQuery = visitQuery.Where(v => v.VisitedAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
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
                _db.AnalyticsEvents.AsNoTracking().Where(e =>
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
                _db.AnalyticsEvents.AsNoTracking().Where(e =>
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
                _db.AnalyticsEvents.AsNoTracking().Where(e =>
                    e.EventType == EventType_SearchPerformed || e.EventType == EventType_DoctorProfileViewed || e.EventType == EventType_BookingStepReached),
                from, to);
            var events = await query
                .Select(e => new { e.EventType, e.SpecialtyId, e.MetadataJson })
                .ToListAsync();

            var searches = events.Where(e => e.EventType == EventType_SearchPerformed).ToList();
            var profileViews = events.Where(e => e.EventType == EventType_DoctorProfileViewed).ToList();
            var bookingSteps = events.Where(e => e.EventType == EventType_BookingStepReached).ToList();

            var stepCounts = bookingSteps
                .Select(e => ParseJson<BookingStepMetadata>(e.MetadataJson)?.Step)
                .Where(step => !string.IsNullOrWhiteSpace(step))
                .GroupBy(step => step)
                .ToDictionary(g => g.Key!, g => g.Count());

            var doneEvents = bookingSteps.Where(e =>
                string.Equals(ParseJson<BookingStepMetadata>(e.MetadataJson)?.Step, "done", StringComparison.OrdinalIgnoreCase)).ToList();

            var specialtyIds = new HashSet<string>(
                searches.Select(e => e.SpecialtyId)
                    .Concat(profileViews.Select(e => e.SpecialtyId))
                    .Concat(doneEvents.Select(e => e.SpecialtyId))
                    .Where(s => !string.IsNullOrWhiteSpace(s))!);

            var specialtyDemand = specialtyIds.Select(sid => new SpecialtyDemandItem
            {
                SpecialtyId = sid,
                SearchCount = searches.Count(e => e.SpecialtyId == sid),
                ProfileViewCount = profileViews.Count(e => e.SpecialtyId == sid),
                CompletedBookingCount = doneEvents.Count(e => e.SpecialtyId == sid),
            })
            .OrderByDescending(s => s.ProfileViewCount)
            .ToList();

            return new BookingFunnelStats
            {
                SearchCount = searches.Count,
                ProfileViewCount = profileViews.Count,
                // Clamped to 100: one search commonly leads to viewing more than one doctor
                // profile from the results, which would otherwise legitimately push this over
                // 100% under completely normal usage, not just a tracking edge case.
                SearchToViewRatePct = searches.Count > 0 ? Math.Min(100, Math.Round(profileViews.Count * 100.0 / searches.Count, 1)) : 0,
                VisitStepCount = stepCounts.TryGetValue("visit", out var v) ? v : 0,
                DetailsStepCount = stepCounts.TryGetValue("details", out var d) ? d : 0,
                DoneStepCount = stepCounts.TryGetValue("done", out var done) ? done : 0,
                SpecialtyDemand = specialtyDemand,
            };
        }

        public async Task<PagedResult<SearchLogItem>> GetSearchLogAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = ApplyDateRange(
                _db.AnalyticsEvents.AsNoTracking().Where(e => e.EventType == EventType_SearchPerformed),
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
