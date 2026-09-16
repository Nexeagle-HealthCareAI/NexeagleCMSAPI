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

            // Load doctor appointment data pre-aggregated at the SQL level — count per (doctor, date)
            // and distinct-patient count per (doctor, date) — instead of fetching every raw row into
            // memory and counting in C#. For busy hospitals this avoids loading tens of thousands of
            // rows just to call .Count and .Distinct().Count() on subsets of them.
            var docApptCounts = await _db.Appointments
                .Where(a => doctorIds.Contains(a.DoctorID) && a.HospitalID == h.HospitalID)
                .GroupBy(a => new { a.DoctorID, a.ApptDate })
                .Select(g => new { g.Key.DoctorID, g.Key.ApptDate, Count = g.Count() })
                .ToListAsync();

            var docUniquePatientCounts = await _db.Appointments
                .Where(a => doctorIds.Contains(a.DoctorID) && a.HospitalID == h.HospitalID)
                .Select(a => new { a.DoctorID, a.ApptDate, a.PatientID })
                .Distinct()
                .GroupBy(a => new { a.DoctorID, a.ApptDate })
                .Select(g => new { g.Key.DoctorID, g.Key.ApptDate, Count = g.Count() })
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

                // Aggregate appointment and unique-patient counts for this doctor from the
                // pre-grouped SQL results — no per-doctor DB round-trips needed.
                var docAppts  = docApptCounts.Where(x => x.DoctorID == doc.DoctorID).ToList();
                var docUnique = docUniquePatientCounts.Where(x => x.DoctorID == doc.DoctorID).ToList();

                var docWeekStart  = todayDate.AddDays(-(int)todayDate.DayOfWeek);
                var docWeekEnd    = docWeekStart.AddDays(6);
                var docMonthStart = new DateOnly(todayDate.Year, todayDate.Month, 1);
                var docMonthEnd   = docMonthStart.AddMonths(1).AddDays(-1);

                int dailyApptCount   = docAppts.Where(x => x.ApptDate == todayDate).Sum(x => x.Count);
                int weeklyApptCount  = docAppts.Where(x => x.ApptDate >= docWeekStart && x.ApptDate <= docWeekEnd).Sum(x => x.Count);
                int monthlyApptCount = docAppts.Where(x => x.ApptDate.Year == todayDate.Year && x.ApptDate.Month == todayDate.Month).Sum(x => x.Count);
                int yearlyApptCount  = docAppts.Where(x => x.ApptDate.Year == todayDate.Year).Sum(x => x.Count);

                int dailyUniqueCount   = docUnique.Where(x => x.ApptDate == todayDate).Sum(x => x.Count);
                int weeklyUniqueCount  = docUnique.Where(x => x.ApptDate >= docWeekStart && x.ApptDate <= docWeekEnd).Sum(x => x.Count);
                int monthlyUniqueCount = docUnique.Where(x => x.ApptDate.Year == todayDate.Year && x.ApptDate.Month == todayDate.Month).Sum(x => x.Count);
                int yearlyUniqueCount  = docUnique.Where(x => x.ApptDate.Year == todayDate.Year).Sum(x => x.Count);

                // Get doctor name from pre-loaded UserProfile dictionary.
                allDoctorProfiles.TryGetValue(doc.UserID, out var userProfile);
                var doctorName = userProfile?.FullName ?? string.Empty;

                doctorInfos.Add(new DoctorInfo
                {
                    Id = doc.DoctorID,
                    Name = doctorName,
                    Departments = deps,
                    Speciality = specs.FirstOrDefault() ?? string.Empty,
                    Degree = doc.Qualification ?? string.Empty,
                    RegistrationNumber = doc.LicenseNumber ?? string.Empty,
                    RegisteredOn = doc.RegistrationYear.HasValue ? new DateTime(doc.RegistrationYear.Value, 1, 1) : DateTime.MinValue,
                    Appointments = new AppointmentCounts
                    {
                        Daily   = dailyApptCount,
                        Weekly  = weeklyApptCount,
                        Monthly = monthlyApptCount,
                        Yearly  = yearlyApptCount
                    },
                    UniquePatients = new AppointmentCounts
                    {
                        Daily   = dailyUniqueCount,
                        Weekly  = weeklyUniqueCount,
                        Monthly = monthlyUniqueCount,
                        Yearly  = yearlyUniqueCount
                    }
                });
            }

            var hospitalStats = new HospitalStats();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // SQL-level aggregation: one row per date (not one row per appointment).
            // For a hospital with years of data, this drops the in-process memory from
            // O(appointments) to O(unique_dates), typically orders of magnitude smaller.
            var apptCountByDate = await _db.Appointments
                .Where(a => a.HospitalID == id)
                .GroupBy(a => a.ApptDate)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Date, x => x.Count);

            // Distinct (date, patient) pairs grouped by date — equivalent to COUNT(DISTINCT PatientID) per date.
            var uniquePatientByDate = await _db.Appointments
                .Where(a => a.HospitalID == id)
                .Select(a => new { a.ApptDate, a.PatientID })
                .Distinct()
                .GroupBy(a => a.ApptDate)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Date, x => x.Count);

            // Daily (Last 7 days including today)
            for (int i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                var label = d.DayOfWeek.ToString().Substring(0, 3);
                hospitalStats.Appointments.Daily.Add(new StatPoint   { Label = label, Value = apptCountByDate.GetValueOrDefault(d) });
                hospitalStats.UniquePatients.Daily.Add(new StatPoint  { Label = label, Value = uniquePatientByDate.GetValueOrDefault(d) });
            }

            // Weekly (Current week + next 3 weeks)
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            for (int i = 0; i < 4; i++)
            {
                var start = weekStart.AddDays(i * 7);
                var end   = start.AddDays(6);
                var label = $"Week {i + 1}";
                hospitalStats.Appointments.Weekly.Add(new StatPoint   { Label = label, Value = apptCountByDate.Where(kvp => kvp.Key >= start && kvp.Key <= end).Sum(kvp => kvp.Value) });
                hospitalStats.UniquePatients.Weekly.Add(new StatPoint  { Label = label, Value = uniquePatientByDate.Where(kvp => kvp.Key >= start && kvp.Key <= end).Sum(kvp => kvp.Value) });
            }

            // Monthly (Current month + next 11 months)
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            for (int i = 0; i < 12; i++)
            {
                var currentMonthStart = monthStart.AddMonths(i);
                var currentMonthEnd   = currentMonthStart.AddMonths(1).AddDays(-1);
                var label = currentMonthStart.ToString("MMM");
                hospitalStats.Appointments.Monthly.Add(new StatPoint   { Label = label, Value = apptCountByDate.Where(kvp => kvp.Key >= currentMonthStart && kvp.Key <= currentMonthEnd).Sum(kvp => kvp.Value) });
                hospitalStats.UniquePatients.Monthly.Add(new StatPoint  { Label = label, Value = uniquePatientByDate.Where(kvp => kvp.Key >= currentMonthStart && kvp.Key <= currentMonthEnd).Sum(kvp => kvp.Value) });
            }

            // Yearly (Current year + next 4 years)
            var currentYear = today.Year;
            for (int i = 0; i < 5; i++)
            {
                var y = currentYear + i;
                hospitalStats.Appointments.Yearly.Add(new StatPoint   { Label = y.ToString(), Value = apptCountByDate.Where(kvp => kvp.Key.Year == y).Sum(kvp => kvp.Value) });
                hospitalStats.UniquePatients.Yearly.Add(new StatPoint  { Label = y.ToString(), Value = uniquePatientByDate.Where(kvp => kvp.Key.Year == y).Sum(kvp => kvp.Value) });
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

            // Doctors are also HospitalUsers (they log in too), so "other users than doctor" is
            // everyone in the users list whose UserID isn't one of this hospital's doctor UserIDs —
            // not a role-name string match, which would miss legacy/mislabelled role rows.
            var doctorUserIdSet = doctors.Select(d => d.UserID).ToHashSet();
            var nonDoctorUserCount = userIds.Count(uid => !doctorUserIdSet.Contains(uid));

            return new HospitalDetails
            {
                Id = h.HospitalID,
                // Always empty: Hospital has no PartnerId, and no relationship linking a hospital
                // to a CmsPartner exists anywhere in the schema (see CmsPartner -- it only tracks
                // CreatedByUserId, the CMS staff account that created the partner record, not
                // which hospitals that partner is associated with). Not a bug to fix here -- needs
                // a real product decision (attributed at onboarding? via a referral code? something
                // else?) and a schema change, not a query change.
                PartnerName = string.Empty,
                Name = h.Name,
                HospitalType = h.Type,
                ContactNumber = h.Contact,
                Email = h.Email,
                Address = h.Location,
                City = h.City,
                State = h.State,
                Latitude = h.Latitude,
                Longitude = h.Longitude,
                TotalPatients = await _db.PatientRegistrations.CountAsync(pr => pr.HospitalID == h.HospitalID),
                RegisteredOn = h.CreatedAt,
                // "Pending", not "Inactive" -- must match GetHospitalsAsync's list-view label
                // below (also driven by the same IsActive flag) since OnboardedHospitals.tsx
                // filters the list by "Pending" and a detail page reached from that filtered
                // list needs to show the same status vocabulary, not a differently-worded label
                // for the identical underlying state.
                Status = h.IsActive ? "Active" : "Pending",
                IsArchived = h.IsArchived,
                ArchivedAt = h.ArchivedAt,
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
                Stats = hospitalStats,
                TotalDoctors = doctorInfos.Count,
                TotalNonDoctorUsers = nonDoctorUserCount
            };
        }

        // Online (BookingSource == "NEXEAGLE_PUBLIC") vs hospital-booked (everything else, i.e.
        // staff-created via easyHMSWeb — including legacy rows predating this column, which are
        // never NEXEAGLE_PUBLIC) appointment counts for an arbitrary inclusive date range. Omitting
        // both from/to returns all-time; passing the same date for both is the "today" preset.
        public async Task<HospitalAppointmentSourceStats> GetAppointmentSourceStatsAsync(Guid hospitalId, DateOnly? from, DateOnly? to)
        {
            var query = _db.Appointments.Where(a => a.HospitalID == hospitalId);
            if (from.HasValue) query = query.Where(a => a.ApptDate >= from.Value);
            if (to.HasValue) query = query.Where(a => a.ApptDate <= to.Value);

            var bookingSources = await query.Select(a => a.BookingSource).ToListAsync();

            var online = bookingSources.Count(s => s == "NEXEAGLE_PUBLIC");
            var total = bookingSources.Count;

            return new HospitalAppointmentSourceStats
            {
                OnlineAppointments = online,
                HospitalAppointments = total - online,
                TotalAppointments = total
            };
        }

        public async Task<PagedResult<HospitalListItem>> GetHospitalsAsync(int page, int limit, string? search, string? sortBy, string? sortDir, string? status = null, string? subscriptionStatus = null, bool includeArchived = false)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = _db.Hospitals.AsQueryable();

            if (!includeArchived)
                query = query.Where(h => !h.IsArchived);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(h =>
                    h.Name.Contains(s) ||
                    h.Contact.Contains(s) ||
                    (h.Email != null && h.Email.Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var isActive = string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
                query = query.Where(h => h.IsActive == isActive);
            }

            // ── Fast path: DB-sortable column + no subscriptionStatus filter ────────────
            // Sort and paginate at the SQL level so only one page of hospital rows is
            // loaded instead of the entire table. Computed fields (TotalDoctors,
            // TotalNonDoctorUsers, SubscriptionStatus) can't be sorted/filtered at the
            // SQL level, so those fall through to the slow path below.
            var dbSortColumns = new[] { "name", "registeredon", "city", "contactnumber", "status", null, "" };
            bool useSqlPagination = string.IsNullOrWhiteSpace(subscriptionStatus) &&
                                    dbSortColumns.Contains(sortBy?.ToLowerInvariant());

            if (useSqlPagination)
            {
                var isAscFast = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
                IOrderedQueryable<Hospital> orderedQuery = sortBy?.ToLowerInvariant() switch
                {
                    "name"          => isAscFast ? query.OrderBy(h => h.Name)       : query.OrderByDescending(h => h.Name),
                    "city"          => isAscFast ? query.OrderBy(h => h.City)        : query.OrderByDescending(h => h.City),
                    "contactnumber" => isAscFast ? query.OrderBy(h => h.Contact)     : query.OrderByDescending(h => h.Contact),
                    "status"        => isAscFast ? query.OrderBy(h => h.IsActive)    : query.OrderByDescending(h => h.IsActive),
                    "registeredon"  => isAscFast ? query.OrderBy(h => h.CreatedAt)   : query.OrderByDescending(h => h.CreatedAt),
                    _               => query.OrderByDescending(h => h.CreatedAt)
                };

                var totalItems = await orderedQuery.CountAsync();
                var pageHospitals = await orderedQuery
                    .Skip((page - 1) * limit)
                    .Take(limit)
                    .ToListAsync();

                var pageIds = pageHospitals.Select(h => h.HospitalID).ToList();

                var subsByPage = await _db.HospitalSubscriptions.AsNoTracking()
                    .Where(s => pageIds.Contains(s.HospitalId))
                    .ToDictionaryAsync(s => s.HospitalId);
                var pagePlanIds = subsByPage.Values.Where(s => s.PlanId.HasValue).Select(s => s.PlanId!.Value);
                var pagePlanNames = await ResolvePlanNamesAsync(pagePlanIds);
                var enterprisePlanIdsFast = await _cmsDb.EasyHmsSubscriptionPlans.Where(p => p.IsEnterprise).Select(p => p.PlanId).ToListAsync();
                var utcNowFast = DateTime.UtcNow;

                var patientCountsFast = await _db.PatientRegistrations
                    .Where(pr => pageIds.Contains(pr.HospitalID))
                    .GroupBy(pr => pr.HospitalID)
                    .Select(g => new { HospitalId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.HospitalId, x => x.Count);

                var doctorPairsFast = await _db.DoctorDepartments
                    .Where(dd => pageIds.Contains(dd.HospitalID))
                    .Select(dd => new { dd.HospitalID, dd.DoctorID })
                    .Distinct().ToListAsync();
                var doctorIdsByHospitalFast = doctorPairsFast
                    .GroupBy(p => p.HospitalID)
                    .ToDictionary(g => g.Key, g => g.Select(p => p.DoctorID).ToHashSet());
                var allDoctorIdsFast = doctorPairsFast.Select(p => p.DoctorID).Distinct().ToList();
                var doctorUserIdByDoctorIdFast = await _db.Doctors
                    .Where(d => allDoctorIdsFast.Contains(d.DoctorID))
                    .ToDictionaryAsync(d => d.DoctorID, d => d.UserID);

                var hospitalUserPairsFast = await _db.HospitalUsers
                    .Where(hu => pageIds.Contains(hu.HospitalID))
                    .Select(hu => new { hu.HospitalID, hu.UserID })
                    .ToListAsync();
                var userIdsByHospitalFast = hospitalUserPairsFast
                    .GroupBy(p => p.HospitalID)
                    .ToDictionary(g => g.Key, g => g.Select(p => p.UserID).ToHashSet());

                var pageItems = pageHospitals.Select(h =>
                {
                    subsByPage.TryGetValue(h.HospitalID, out var sub);
                    string? subStatus = null; string? subPlanName = null; int? subDaysRemaining = null; var subIsEnterprise = false;
                    if (sub != null)
                    {
                        subStatus = sub.GetEffectiveStatus(utcNowFast);
                        if (sub.PlanId.HasValue) { pagePlanNames.TryGetValue(sub.PlanId.Value, out subPlanName); subIsEnterprise = enterprisePlanIdsFast.Contains(sub.PlanId.Value); }
                        if (subStatus == "Trial" && sub.TrialEndDate.HasValue) subDaysRemaining = Math.Max(0, (sub.TrialEndDate.Value - utcNowFast).Days);
                        else if (subStatus == "Active" && sub.SubscriptionEndDate.HasValue) subDaysRemaining = Math.Max(0, (sub.SubscriptionEndDate.Value - utcNowFast).Days);
                    }
                    doctorIdsByHospitalFast.TryGetValue(h.HospitalID, out var doctorIdSet); doctorIdSet ??= new HashSet<Guid>();
                    var doctorUserIds = doctorIdSet.Select(did => doctorUserIdByDoctorIdFast.TryGetValue(did, out var uid) ? uid : Guid.Empty).ToHashSet();
                    userIdsByHospitalFast.TryGetValue(h.HospitalID, out var userIdSet); userIdSet ??= new HashSet<Guid>();
                    patientCountsFast.TryGetValue(h.HospitalID, out var patientCount);

                    return new HospitalListItem
                    {
                        Id = h.HospitalID, PartnerName = string.Empty, Name = h.Name, ContactNumber = h.Contact,
                        Email = h.Email, Address = h.Location, City = h.City, State = h.State,
                        TotalPatients = patientCount, TotalDoctors = doctorIdSet.Count,
                        TotalNonDoctorUsers = userIdSet.Count(uid => !doctorUserIds.Contains(uid)),
                        RegisteredOn = h.CreatedAt, Status = h.IsActive ? "Active" : "Pending",
                        IsArchived = h.IsArchived, ArchivedAt = h.ArchivedAt,
                        SubscriptionPlanName = subPlanName, SubscriptionStatus = subStatus,
                        SubscriptionDaysRemaining = subDaysRemaining, SubscriptionIsEnterprise = subIsEnterprise
                    };
                }).ToList();

                return new PagedResult<HospitalListItem>
                {
                    Data = pageItems,
                    Pagination = new PaginationInfo { CurrentPage = page, TotalPages = (int)Math.Ceiling(totalItems / (double)limit), TotalItems = totalItems, ItemsPerPage = limit }
                };
            }

            // ── Slow path: subscriptionStatus filter or computed-column sort ──────────
            // Subscription effective status, doctor counts, and non-doctor user counts are
            // computed values — not plain columns — so filtering/sorting by them can't happen
            // at the SQL level. Materialize every hospital matching the DB-level filters and
            // do computed filtering + sorting in memory.
            var matchingHospitals = await query.ToListAsync();
            var hospitalIds = matchingHospitals.Select(h => h.HospitalID).ToList();

            var subsByHospital = await _db.HospitalSubscriptions
                .AsNoTracking()
                .Where(s => hospitalIds.Contains(s.HospitalId))
                .ToDictionaryAsync(s => s.HospitalId);
            var allPlanIds = subsByHospital.Values.Where(s => s.PlanId.HasValue).Select(s => s.PlanId!.Value);
            var allPlanNames = await ResolvePlanNamesAsync(allPlanIds);
            var enterprisePlanIds = await _cmsDb.EasyHmsSubscriptionPlans.Where(p => p.IsEnterprise).Select(p => p.PlanId).ToListAsync();
            var utcNow = DateTime.UtcNow;

            var patientCounts = await _db.PatientRegistrations
                .Where(pr => hospitalIds.Contains(pr.HospitalID))
                .GroupBy(pr => pr.HospitalID)
                .Select(g => new { HospitalId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.HospitalId, x => x.Count);

            // Doctors: DoctorDepartments gives the doctor-hospital affiliation; Doctors gives the
            // UserID needed to tell doctor-users apart from everyone else in HospitalUsers.
            var doctorPairs = await _db.DoctorDepartments
                .Where(dd => hospitalIds.Contains(dd.HospitalID))
                .Select(dd => new { dd.HospitalID, dd.DoctorID })
                .Distinct()
                .ToListAsync();
            var doctorIdsByHospital = doctorPairs
                .GroupBy(p => p.HospitalID)
                .ToDictionary(g => g.Key, g => g.Select(p => p.DoctorID).ToHashSet());
            var allDoctorIds = doctorPairs.Select(p => p.DoctorID).Distinct().ToList();
            var doctorUserIdByDoctorId = await _db.Doctors
                .Where(d => allDoctorIds.Contains(d.DoctorID))
                .ToDictionaryAsync(d => d.DoctorID, d => d.UserID);

            var hospitalUserPairs = await _db.HospitalUsers
                .Where(hu => hospitalIds.Contains(hu.HospitalID))
                .Select(hu => new { hu.HospitalID, hu.UserID })
                .ToListAsync();
            var userIdsByHospital = hospitalUserPairs
                .GroupBy(p => p.HospitalID)
                .ToDictionary(g => g.Key, g => g.Select(p => p.UserID).ToHashSet());

            var projected = matchingHospitals.Select(h =>
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
                        allPlanNames.TryGetValue(sub.PlanId.Value, out subPlanName);
                        subIsEnterprise = enterprisePlanIds.Contains(sub.PlanId.Value);
                    }
                    if (subStatus == "Trial" && sub.TrialEndDate.HasValue)
                        subDaysRemaining = Math.Max(0, (sub.TrialEndDate.Value - utcNow).Days);
                    else if (subStatus == "Active" && sub.SubscriptionEndDate.HasValue)
                        subDaysRemaining = Math.Max(0, (sub.SubscriptionEndDate.Value - utcNow).Days);
                }

                doctorIdsByHospital.TryGetValue(h.HospitalID, out var doctorIdSet);
                doctorIdSet ??= new HashSet<Guid>();
                var doctorUserIds = doctorIdSet
                    .Select(did => doctorUserIdByDoctorId.TryGetValue(did, out var uid) ? uid : Guid.Empty)
                    .ToHashSet();

                userIdsByHospital.TryGetValue(h.HospitalID, out var userIdSet);
                userIdSet ??= new HashSet<Guid>();
                var nonDoctorUserCount = userIdSet.Count(uid => !doctorUserIds.Contains(uid));

                patientCounts.TryGetValue(h.HospitalID, out var patientCount);

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
                    TotalPatients = patientCount,
                    TotalDoctors = doctorIdSet.Count,
                    TotalNonDoctorUsers = nonDoctorUserCount,
                    RegisteredOn = h.CreatedAt,
                    Status = h.IsActive ? "Active" : "Pending",
                    IsArchived = h.IsArchived,
                    ArchivedAt = h.ArchivedAt,
                    SubscriptionPlanName = subPlanName,
                    SubscriptionStatus = subStatus,
                    SubscriptionDaysRemaining = subDaysRemaining,
                    SubscriptionIsEnterprise = subIsEnterprise
                };
            });

            if (!string.IsNullOrWhiteSpace(subscriptionStatus))
            {
                projected = string.Equals(subscriptionStatus, "None", StringComparison.OrdinalIgnoreCase)
                    ? projected.Where(p => p.SubscriptionStatus == null)
                    : projected.Where(p => string.Equals(p.SubscriptionStatus, subscriptionStatus, StringComparison.OrdinalIgnoreCase));
            }

            var isAsc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            IOrderedEnumerable<HospitalListItem> sorted = sortBy?.ToLowerInvariant() switch
            {
                "name" => isAsc ? projected.OrderBy(p => p.Name) : projected.OrderByDescending(p => p.Name),
                "registeredon" => isAsc ? projected.OrderBy(p => p.RegisteredOn) : projected.OrderByDescending(p => p.RegisteredOn),
                "totalpatients" => isAsc ? projected.OrderBy(p => p.TotalPatients) : projected.OrderByDescending(p => p.TotalPatients),
                "totaldoctors" => isAsc ? projected.OrderBy(p => p.TotalDoctors) : projected.OrderByDescending(p => p.TotalDoctors),
                "totalnondoctorusers" => isAsc ? projected.OrderBy(p => p.TotalNonDoctorUsers) : projected.OrderByDescending(p => p.TotalNonDoctorUsers),
                "status" => isAsc ? projected.OrderBy(p => p.Status) : projected.OrderByDescending(p => p.Status),
                "subscriptionstatus" => isAsc ? projected.OrderBy(p => p.SubscriptionStatus) : projected.OrderByDescending(p => p.SubscriptionStatus),
                "city" => isAsc ? projected.OrderBy(p => p.City) : projected.OrderByDescending(p => p.City),
                "contactnumber" => isAsc ? projected.OrderBy(p => p.ContactNumber) : projected.OrderByDescending(p => p.ContactNumber),
                _ => projected.OrderByDescending(p => p.RegisteredOn)
            };

            var sortedList = sorted.ToList();
            var totalItems = sortedList.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);
            var pageItems = sortedList.Skip((page - 1) * limit).Take(limit).ToList();

            return new PagedResult<HospitalListItem>
            {
                Data = pageItems,
                Pagination = new PaginationInfo
                {
                    CurrentPage = page,
                    TotalPages = totalPages,
                    TotalItems = totalItems,
                    ItemsPerPage = limit
                }
            };
        }

        // Soft-delete only — no cascading data removal. Hiding this hospital everywhere is
        // enforced by easyHMSAPI's HospitalAccessFilter (and the handful of places that bypass
        // it) reading IsArchived live; this just flips the flag. Returns false if the hospital
        // doesn't exist.
        public async Task<bool> ArchiveHospitalAsync(Guid id, Guid archivedByUserId)
        {
            var hospital = await _db.Hospitals.FirstOrDefaultAsync(h => h.HospitalID == id);
            if (hospital == null) return false;

            hospital.IsArchived = true;
            hospital.ArchivedAt = DateTime.UtcNow;
            hospital.ArchivedByUserId = archivedByUserId;
            hospital.LastUpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RestoreHospitalAsync(Guid id)
        {
            var hospital = await _db.Hospitals.FirstOrDefaultAsync(h => h.HospitalID == id);
            if (hospital == null) return false;

            hospital.IsArchived = false;
            hospital.ArchivedAt = null;
            hospital.ArchivedByUserId = null;
            hospital.LastUpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<List<PatientIdentitySnapshot>> GetPatientIdentitySnapshotAsync(Guid hospitalId)
        {
            return await _db.PatientRegistrations
                .AsNoTracking()
                .Where(p => p.HospitalID == hospitalId)
                .Select(p => new PatientIdentitySnapshot
                {
                    PatientId = p.PatientID,
                    FullName = p.FullName,
                    GuardianName = p.GuardianName,
                    GuardianRelation = p.GuardianRelation,
                    Age = p.AgeYears,
                    Sex = p.Sex,
                })
                .ToListAsync();
        }
    }
}
