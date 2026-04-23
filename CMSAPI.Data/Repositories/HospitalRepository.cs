using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class HospitalRepository : IHospitalRepository
    {
        private readonly AppDbContext _db;

        public HospitalRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<HospitalDetails?> GetHospitalByIdAsync(Guid id)
        {
            var h = await _db.Hospitals
                .Include(h => h.HospitalUsers)
                .Include(h => h.DoctorDepartments)
                .Include(h => h.DoctorDepartments).ThenInclude(dd => dd.Doctor)
                .Include(h => h.DoctorDepartments).ThenInclude(dd => dd.Department)
                .FirstOrDefaultAsync(x => x.HospitalID == id);

            if (h == null) return null;

            // Get user details with joins to Users, UserProfile, UserRoles, Roles, UserAuth, and UserStatus
            var userIds = h.HospitalUsers.Select(hu => hu.UserID).ToList();
            
            var users = await (from hu in _db.HospitalUsers
                               where hu.HospitalID == id
                               join u in _db.Users on hu.UserID equals u.UserID
                               join up in _db.UserProfiles on hu.UserID equals up.UserID
                               join ua in _db.UserAuths on hu.UserID equals ua.UserID
                               join us in _db.UserStatus on ua.UserStatusId equals us.UserStatusId
                               join ur in _db.UserRoles on hu.UserID equals ur.UserID into userRoles
                               from ur in userRoles.DefaultIfEmpty()
                               join r in _db.Roles on ur.RoleID equals r.RoleID into roles
                               from r in roles.DefaultIfEmpty()
                               select new HospitalUserInfo
                               {
                                   Name = up.FullName ?? string.Empty,
                                   Role = r != null ? r.RoleName : (hu.IsPrimary ? "Admin" : "User"),
                                   Contact = u.MobileNumber ?? string.Empty,
                                   Email = u.Email,
                                   Status = us.StatusName ?? "Active",
                                   LastLoginTime = ua.LastLoginTime,
                                   LoginMethod = ua.LoginMethod ?? string.Empty
                               }).ToListAsync();


            // Aggregate doctors from DoctorDepartments
            var doctorIds = h.DoctorDepartments?.Select(d => d.DoctorID).Distinct().ToList() ?? new();

            var doctors = await _db.Doctors
                .Where(d => doctorIds.Contains(d.DoctorID))
                .ToListAsync();

            var doctorInfos = new List<DoctorInfo>();

            foreach (var doc in doctors)
            {
                var deps = await _db.DoctorDepartments
                    .Where(dd => dd.DoctorID == doc.DoctorID && dd.HospitalID == h.HospitalID)
                    .Include(dd => dd.Department)
                    .Select(dd => dd.Department!.Name)
                    .ToListAsync();

                var specs = await _db.DoctorSpecializations
                    .Include(ds => ds.Specialization)
                    .Where(ds => ds.DoctorID == doc.DoctorID && ds.Specialization != null && ds.Specialization.HospitalID == h.HospitalID)
                    .Select(ds => ds.Specialization!.Name)
                    .ToListAsync();

                // Calculate actual stats from appointments
                var doctorAppts = await _db.Appointments
                    .Where(a => a.DoctorID == doc.DoctorID && a.HospitalID == h.HospitalID)
                    .Select(a => new { a.ApptDate, a.PatientID })
                    .ToListAsync();

                var todayDate = DateOnly.FromDateTime(DateTime.UtcNow);
                var oneWeekAgo = todayDate.AddDays(-7);
                var oneMonthAgo = todayDate.AddDays(-30);
                var oneYearAgo = todayDate.AddDays(-365);

                var dailyAppts = doctorAppts.Where(a => a.ApptDate == todayDate).ToList();
                var weeklyAppts = doctorAppts.Where(a => a.ApptDate >= oneWeekAgo).ToList();
                var monthlyAppts = doctorAppts.Where(a => a.ApptDate >= oneMonthAgo).ToList();
                var yearlyAppts = doctorAppts.Where(a => a.ApptDate >= oneYearAgo).ToList();

                var appointmentsCount = new AppointmentCounts
                {
                    Daily = dailyAppts.Count,
                    Weekly = weeklyAppts.Count,
                    Monthly = monthlyAppts.Count,
                    Yearly = yearlyAppts.Count
                };

                var uniquePatientsCount = new AppointmentCounts
                {
                    Daily = dailyAppts.Select(a => a.PatientID).Distinct().Count(),
                    Weekly = weeklyAppts.Select(a => a.PatientID).Distinct().Count(),
                    Monthly = monthlyAppts.Select(a => a.PatientID).Distinct().Count(),
                    Yearly = yearlyAppts.Select(a => a.PatientID).Distinct().Count()
                };

                // Get doctor name from UserProfile
                var userProfile = await _db.UserProfiles.FirstOrDefaultAsync(up => up.UserID == doc.UserID);
                var doctorName = userProfile?.FullName ?? string.Empty;


                doctorInfos.Add(new DoctorInfo
                {
                    Name = doctorName,
                    Departments = deps,
                    Speciality = specs.FirstOrDefault() ?? string.Empty,
                    Degree = doc.Qualification ?? string.Empty,
                    RegistrationNumber = doc.LicenseNumber ?? string.Empty,
                    RegisteredOn = doc.RegistrationYear.HasValue ? new DateTime(doc.RegistrationYear.Value, 1, 1) : DateTime.MinValue,
                    Appointments = appointmentsCount,
                    UniquePatients = uniquePatientsCount
                });
            }

            var hospitalStats = new HospitalStats();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Fetch raw data roughly needed (optimize in real scenario)
            var appointments = await _db.Appointments
                .Where(a => a.HospitalID == id)
                .Select(a => new { a.ApptDate, a.PatientID })
                .ToListAsync();

            // Daily (Last 7 days including today)
            for (int i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                var dayAppts = appointments.Where(a => a.ApptDate == d).ToList();
                var label = d.DayOfWeek.ToString().Substring(0, 3);
                
                hospitalStats.Appointments.Daily.Add(new StatPoint { Label = label, Value = dayAppts.Count });
                hospitalStats.UniquePatients.Daily.Add(new StatPoint { Label = label, Value = dayAppts.Select(x => x.PatientID).Distinct().Count() });
            }

            // Weekly (Last 4 weeks)
            for (int i = 3; i >= 0; i--)
            {
                var start = today.AddDays(-(i * 7) - 6);
                var end = today.AddDays(-(i * 7));
                var weekAppts = appointments.Where(a => a.ApptDate >= start && a.ApptDate <= end).ToList();
                var label = $"Week {4-i}";

                hospitalStats.Appointments.Weekly.Add(new StatPoint { Label = label, Value = weekAppts.Count });
                hospitalStats.UniquePatients.Weekly.Add(new StatPoint { Label = label, Value = weekAppts.Select(x => x.PatientID).Distinct().Count() });
            }

            // Monthly (Last 12 months)
            var currentMonth = today;
            for (int i = 11; i >= 0; i--)
            {
                var monthDate = currentMonth.AddMonths(-i);
                var monthStart = new DateOnly(monthDate.Year, monthDate.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                
                var monthAppts = appointments.Where(a => a.ApptDate >= monthStart && a.ApptDate <= monthEnd).ToList();
                var label = monthDate.ToString("MMM");

                hospitalStats.Appointments.Monthly.Add(new StatPoint { Label = label, Value = monthAppts.Count });
                hospitalStats.UniquePatients.Monthly.Add(new StatPoint { Label = label, Value = monthAppts.Select(x => x.PatientID).Distinct().Count() });
            }

            // Yearly (Last 5 years)
            var currentYear = today.Year;
            for (int i = 4; i >= 0; i--)
            {
                var y = currentYear - i;
                var yearAppts = appointments.Where(a => a.ApptDate.Year == y).ToList();
                
                hospitalStats.Appointments.Yearly.Add(new StatPoint { Label = y.ToString(), Value = yearAppts.Count });
                hospitalStats.UniquePatients.Yearly.Add(new StatPoint { Label = y.ToString(), Value = yearAppts.Select(x => x.PatientID).Distinct().Count() });
            }

            return new HospitalDetails
            {
                Id = h.HospitalID,
                PartnerName = string.Empty,
                Name = h.Name,
                HospitalType = h.Type,
                ContactNumber = h.Contact,
                Email = h.Email,
                Address = h.Location,
                City = h.City,
                State = h.State,
                TotalPatients = await _db.PatientRegistrations.CountAsync(pr => pr.HospitalID == h.HospitalID),
                RegisteredOn = h.CreatedAt,
                Status = h.IsActive ? "Active" : "Inactive",
                SubscriptionMode = string.Empty,
                PaymentMode = string.Empty,
                Users = users,
                Doctors = doctorInfos,
                Stats = hospitalStats
            };
        }

        public async Task<PagedResult<HospitalListItem>> GetHospitalsAsync(int page, int limit, string? search, string? sortBy, string? sortDir)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = _db.Hospitals.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(h =>
                    h.Name.Contains(s) ||
                    h.Contact.Contains(s) ||
                    (h.Email != null && h.Email.Contains(s)));
            }

            var isAsc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            query = sortBy?.ToLowerInvariant() switch
            {
                "name" => isAsc ? query.OrderBy(h => h.Name) : query.OrderByDescending(h => h.Name),
                "registeredon" => isAsc ? query.OrderBy(h => h.CreatedAt) : query.OrderByDescending(h => h.CreatedAt),
                _ => query.OrderByDescending(h => h.CreatedAt)
            };

            var totalItems = await query.LongCountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

            var items = await query
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync();

            var projected = items.Select(h => new HospitalListItem
            {
                Id = h.HospitalID,
                PartnerName = string.Empty,
                Name = h.Name,
                ContactNumber = h.Contact,
                Email = h.Email,
                Address = h.Location,
                City = h.City,
                State = h.State,
                TotalPatients = _db.PatientRegistrations.Count(pr => pr.HospitalID == h.HospitalID),
                RegisteredOn = h.CreatedAt,
                Status = h.IsActive ? "Active" : "Pending"
            }).ToList();

            return new PagedResult<HospitalListItem>
            {
                Data = projected,
                Pagination = new PaginationInfo
                {
                    CurrentPage = page,
                    TotalPages = totalPages,
                    TotalItems = totalItems,
                    ItemsPerPage = limit
                }
            };
        }
    }
}
