using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class InsightsRepository : IInsightsRepository
    {
        private const string NexeaglePublicSource = "NEXEAGLE_PUBLIC";

        private readonly AppDbContext _db;

        public InsightsRepository(AppDbContext db)
        {
            _db = db;
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
                    a.BookedByMobile, a.BookingIpAddress, a.BookingReferrerUrl, a.BookingUtmCampaign
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
                    IpAddress = a.BookingIpAddress,
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
    }
}
