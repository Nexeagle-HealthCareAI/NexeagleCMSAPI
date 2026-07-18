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
        private readonly CmsDbContext _cmsDb;

        public HospitalRepository(AppDbContext db, CmsDbContext cmsDb)
        {
            _db = db;
            _cmsDb = cmsDb;
        }

        // Resolves plan names for a batch of subscriptions in two queries total (not per-row),
        // preferring the dedicated EasyHMS catalog and falling back to the legacy shared (1Rad)
        // one for any older rows created before the split — same pattern as
        // SubscriptionApprovalController.
        private async Task<Dictionary<Guid, string>> ResolvePlanNamesAsync(IEnumerable<Guid> planIds)
        {
            var ids = planIds.Distinct().ToList();
            var easyHmsPlans = await _cmsDb.EasyHmsSubscriptionPlans.Where(p => ids.Contains(p.PlanId)).ToDictionaryAsync(p => p.PlanId, p => p.Name);
            var legacyPlans = await _cmsDb.SubscriptionPlans.Where(p => ids.Contains(p.PlanId)).ToDictionaryAsync(p => p.PlanId, p => p.Name);

            var result = new Dictionary<Guid, string>();
            foreach (var id in ids)
            {
                if (easyHmsPlans.TryGetValue(id, out var easyHmsName)) result[id] = easyHmsName;
                else if (legacyPlans.TryGetValue(id, out var legacyName)) result[id] = legacyName;
            }
            return result;
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


            // Aggregate doctors from DoctorDepartments.
            // Load all related data in 4 batch queries instead of 3 queries per doctor (N+1 fix).
            var doctorIds = h.DoctorDepartments?.Select(d => d.DoctorID).Distinct().ToList() ?? new();

            var doctors = await _db.Doctors
                .Where(d => doctorIds.Contains(d.DoctorID))
                .ToListAsync();

            var allDoctorDepts = await _db.DoctorDepartments
                .Where(dd => doctorIds.Contains(dd.DoctorID) && dd.HospitalID == h.HospitalID)
                .Include(dd => dd.Department)
                .ToListAsync();

            var allDoctorSpecs = await _db.DoctorSpecializations
                .Include(ds => ds.Specialization)
                .Where(ds => doctorIds.Contains(ds.DoctorID)
                             && ds.Specialization != null
                             && ds.Specialization.HospitalID == h.HospitalID)
                .ToListAsync();

            var allDoctorAppointments = await _db.Appointments
                .Where(a => doctorIds.Contains(a.DoctorID) && a.HospitalID == h.HospitalID)
                .Select(a => new { a.DoctorID, a.ApptDate, a.PatientID })
                .ToListAsync();

            var doctorUserIds = doctors.Select(d => d.UserID).ToList();
            var allDoctorProfiles = await _db.UserProfiles
                .Where(up => doctorUserIds.Contains(up.UserID))
                .ToDictionaryAsync(up => up.UserID);

            var doctorInfos = new List<DoctorInfo>();
            var todayDate = DateOnly.FromDateTime(DateTime.UtcNow);

            foreach (var doc in doctors)
            {
                var deps = allDoctorDepts
                    .Where(dd => dd.DoctorID == doc.DoctorID)
                    .Select(dd => dd.Department?.Name ?? string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();

                var specs = allDoctorSpecs
                    .Where(ds => ds.DoctorID == doc.DoctorID)
                    .Select(ds => ds.Specialization?.Name ?? string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();

                var doctorAppointments = allDoctorAppointments
                    .Where(a => a.DoctorID == doc.DoctorID)
                    .ToList();

                // Daily: Today's appointments
                var dailyAppts = doctorAppointments.Where(a => a.ApptDate == todayDate).ToList();

                // Weekly: Current calendar week (Monday to Sunday)
                var docWeekStart = todayDate.AddDays(-(int)todayDate.DayOfWeek);
                var docWeekEnd = docWeekStart.AddDays(6);
                var weeklyAppts = doctorAppointments.Where(a => a.ApptDate >= docWeekStart && a.ApptDate <= docWeekEnd).ToList();

                // Monthly: Current calendar month
                var docMonthStart = new DateOnly(todayDate.Year, todayDate.Month, 1);
                var docMonthEnd = docMonthStart.AddMonths(1).AddDays(-1);
                var monthlyAppts = doctorAppointments.Where(a => a.ApptDate >= docMonthStart && a.ApptDate <= docMonthEnd).ToList();

                // Yearly: Current calendar year
                var yearlyAppts = doctorAppointments.Where(a => a.ApptDate.Year == todayDate.Year).ToList();

                // Get doctor name from pre-loaded UserProfile dictionary.
                allDoctorProfiles.TryGetValue(doc.UserID, out var userProfile);
                var doctorName = userProfile?.FullName ?? string.Empty;

                doctorInfos.Add(new DoctorInfo
                {
                    Name = doctorName,
                    Departments = deps,
                    Speciality = specs.FirstOrDefault() ?? string.Empty,
                    Degree = doc.Qualification ?? string.Empty,
                    RegistrationNumber = doc.LicenseNumber ?? string.Empty,
                    RegisteredOn = doc.RegistrationYear.HasValue ? new DateTime(doc.RegistrationYear.Value, 1, 1) : DateTime.MinValue,
                    Appointments = new AppointmentCounts 
                    { 
                        Daily = dailyAppts.Count, 
                        Weekly = weeklyAppts.Count, 
                        Monthly = monthlyAppts.Count, 
                        Yearly = yearlyAppts.Count 
                    },
                    UniquePatients = new AppointmentCounts 
                    { 
                        Daily = dailyAppts.Select(x => x.PatientID).Distinct().Count(),
                        Weekly = weeklyAppts.Select(x => x.PatientID).Distinct().Count(),
                        Monthly = monthlyAppts.Select(x => x.PatientID).Distinct().Count(),
                        Yearly = yearlyAppts.Select(x => x.PatientID).Distinct().Count()
                    }
                });
            }

            var hospitalStats = new HospitalStats();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Fetch raw data from appointments table
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

            // Weekly (Current week + next 3 weeks)
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            for (int i = 0; i < 4; i++)
            {
                var start = weekStart.AddDays(i * 7);
                var end = start.AddDays(6);
                var weekAppts = appointments.Where(a => a.ApptDate >= start && a.ApptDate <= end).ToList();
                var label = $"Week {i+1}";

                hospitalStats.Appointments.Weekly.Add(new StatPoint { Label = label, Value = weekAppts.Count });
                hospitalStats.UniquePatients.Weekly.Add(new StatPoint { Label = label, Value = weekAppts.Select(x => x.PatientID).Distinct().Count() });
            }

            // Monthly (Current month + next 11 months)
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            for (int i = 0; i < 12; i++)
            {
                var currentMonthStart = monthStart.AddMonths(i);
                var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);

                var monthAppts = appointments.Where(a => a.ApptDate >= currentMonthStart && a.ApptDate <= currentMonthEnd).ToList();
                var label = currentMonthStart.ToString("MMM");

                hospitalStats.Appointments.Monthly.Add(new StatPoint { Label = label, Value = monthAppts.Count });
                hospitalStats.UniquePatients.Monthly.Add(new StatPoint { Label = label, Value = monthAppts.Select(x => x.PatientID).Distinct().Count() });
            }

            // Yearly (Current year + next 4 years)
            var currentYear = today.Year;
            for (int i = 0; i < 5; i++)
            {
                var y = currentYear + i;
                var yearAppts = appointments.Where(a => a.ApptDate.Year == y).ToList();

                hospitalStats.Appointments.Yearly.Add(new StatPoint { Label = y.ToString(), Value = yearAppts.Count });
                hospitalStats.UniquePatients.Yearly.Add(new StatPoint { Label = y.ToString(), Value = yearAppts.Select(x => x.PatientID).Distinct().Count() });
            }

            // Subscription summary + full payment ledger for this hospital.
            var sub = await _db.HospitalSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.HospitalId == id);
            string? subPlanName = null;
            string? subStatus = null;
            int? subDaysRemaining = null;
            bool subIsEnterprise = false;
            if (sub != null)
            {
                subStatus = sub.GetEffectiveStatus(DateTime.UtcNow);
                if (sub.PlanId.HasValue)
                {
                    var names = await ResolvePlanNamesAsync(new[] { sub.PlanId.Value });
                    subPlanName = names.TryGetValue(sub.PlanId.Value, out var n) ? n : null;
                    subIsEnterprise = await _cmsDb.EasyHmsSubscriptionPlans.AnyAsync(p => p.PlanId == sub.PlanId.Value && p.IsEnterprise);
                }
                var utcNow = DateTime.UtcNow;
                if (subStatus == "Trial" && sub.TrialEndDate.HasValue)
                    subDaysRemaining = Math.Max(0, (sub.TrialEndDate.Value - utcNow).Days);
                else if (subStatus == "Active" && sub.SubscriptionEndDate.HasValue)
                    subDaysRemaining = Math.Max(0, (sub.SubscriptionEndDate.Value - utcNow).Days);
            }

            var paymentRows = await _db.HospitalSubscriptionPayments
                .AsNoTracking()
                .Where(p => p.HospitalId == id)
                .OrderByDescending(p => p.SubmittedAt)
                .ToListAsync();
            var paymentPlanNames = await ResolvePlanNamesAsync(paymentRows.Where(p => p.PlanId.HasValue).Select(p => p.PlanId!.Value));
            var paymentHistory = paymentRows.Select(p => new HospitalPaymentHistoryItem
            {
                PlanName = p.PlanName ?? (p.PlanId.HasValue && paymentPlanNames.TryGetValue(p.PlanId.Value, out var pn) ? pn : "Unknown"),
                Amount = p.Amount,
                Reference = p.Reference,
                PaymentMode = p.PaymentMode,
                Status = p.Status,
                SubmittedAt = p.SubmittedAt,
                ReviewedAt = p.ReviewedAt,
                RejectionReason = p.RejectionReason
            }).ToList();

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
                SubscriptionPlanName = subPlanName,
                SubscriptionStatus = subStatus,
                SubscriptionDaysRemaining = subDaysRemaining,
                SubscriptionIsEnterprise = subIsEnterprise,
                TrialStartDate = sub?.TrialStartDate,
                TrialEndDate = sub?.TrialEndDate,
                SubscriptionStartDate = sub?.SubscriptionStartDate,
                SubscriptionEndDate = sub?.SubscriptionEndDate,
                PaymentHistory = paymentHistory,
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

            // Batch-fetch subscription rows for this page's hospitals (one query, not N+1) and
            // resolve their plan names the same way the detail endpoint does.
            var hospitalIdsOnPage = items.Select(h => h.HospitalID).ToList();
            var subsByHospital = await _db.HospitalSubscriptions
                .AsNoTracking()
                .Where(s => hospitalIdsOnPage.Contains(s.HospitalId))
                .ToDictionaryAsync(s => s.HospitalId);
            var pagePlanIds = subsByHospital.Values.Where(s => s.PlanId.HasValue).Select(s => s.PlanId!.Value);
            var pagePlanNames = await ResolvePlanNamesAsync(pagePlanIds);
            var enterprisePlanIds = await _cmsDb.EasyHmsSubscriptionPlans.Where(p => p.IsEnterprise).Select(p => p.PlanId).ToListAsync();
            var utcNow = DateTime.UtcNow;

            var projected = items.Select(h =>
            {
                subsByHospital.TryGetValue(h.HospitalID, out var sub);
                string? subStatus = null;
                string? subPlanName = null;
                int? subDaysRemaining = null;
                var subIsEnterprise = false;
                if (sub != null)
                {
                    subStatus = sub.GetEffectiveStatus(utcNow);
                    if (sub.PlanId.HasValue)
                    {
                        pagePlanNames.TryGetValue(sub.PlanId.Value, out subPlanName);
                        subIsEnterprise = enterprisePlanIds.Contains(sub.PlanId.Value);
                    }
                    if (subStatus == "Trial" && sub.TrialEndDate.HasValue)
                        subDaysRemaining = Math.Max(0, (sub.TrialEndDate.Value - utcNow).Days);
                    else if (subStatus == "Active" && sub.SubscriptionEndDate.HasValue)
                        subDaysRemaining = Math.Max(0, (sub.SubscriptionEndDate.Value - utcNow).Days);
                }

                return new HospitalListItem
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
                    Status = h.IsActive ? "Active" : "Pending",
                    SubscriptionPlanName = subPlanName,
                    SubscriptionStatus = subStatus,
                    SubscriptionDaysRemaining = subDaysRemaining,
                    SubscriptionIsEnterprise = subIsEnterprise
                };
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
