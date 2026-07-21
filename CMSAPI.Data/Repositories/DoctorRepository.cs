using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class DoctorRepository : IDoctorRepository
    {
        private const string OpdConsultFeeType = "OPD_CONSULT";

        private readonly AppDbContext _db;

        public DoctorRepository(AppDbContext db)
        {
            _db = db;
        }

        // Same convention easyHMSWeb's opdDocuments.ts/TokenPrintModal.tsx use for a display-ready
        // hospital address: join whichever of Location/City/State/Pincode are non-empty.
        private static string? FormatAddress(string? location, string? city, string? state, string? pincode)
        {
            var parts = new[] { location, city, state, pincode }.Where(p => !string.IsNullOrWhiteSpace(p));
            var joined = string.Join(", ", parts);
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }

        public async Task<PagedResult<DoctorListItem>> GetDoctorsAsync(int page, int limit, string? search, string? sortBy, string? sortDir)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = _db.Doctors.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                var matchingUserIds = await _db.UserProfiles
                    .Where(up => up.FullName.Contains(s))
                    .Select(up => up.UserID)
                    .ToListAsync();
                query = query.Where(d => matchingUserIds.Contains(d.UserID) || d.LicenseNumber.Contains(s));
            }

            var isAsc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            query = sortBy?.ToLowerInvariant() switch
            {
                "createdat" => isAsc ? query.OrderBy(d => d.CreatedAt) : query.OrderByDescending(d => d.CreatedAt),
                _ => query.OrderByDescending(d => d.CreatedAt)
            };

            var totalItems = await query.LongCountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

            var items = await query
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(d => new
                {
                    d.DoctorID, d.UserID, d.PrimaryDepartmentID,
                    d.IsPubliclyListed, d.IsFeatured, d.IsDelistedByAdmin,
                    d.IsRegistrationVerified, d.RegistrationVerifiedAt,
                    d.DiscountPercent, d.DiscountStartAt, d.DiscountEndAt
                })
                .ToListAsync();

            if (items.Count == 0)
            {
                return new PagedResult<DoctorListItem>
                {
                    Data = Enumerable.Empty<DoctorListItem>(),
                    Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
                };
            }

            var doctorIds = items.Select(d => d.DoctorID).ToList();

            // Doctor -> hospital affiliation resolved via DoctorDepartments, not the stale/possibly-
            // NULL Doctor.HospitalID field — a doctor is a global identity that can belong to more
            // than one hospital; pick one deterministically (lowest HospitalId), same convention
            // easyHMSAPI's GetPublicDoctorsHandler uses.
            var doctorHospitalPairs = await _db.DoctorDepartments
                .Where(dd => doctorIds.Contains(dd.DoctorID))
                .Select(dd => new { dd.DoctorID, dd.HospitalID })
                .Distinct()
                .ToListAsync();
            var doctorHospital = doctorHospitalPairs
                .GroupBy(p => p.DoctorID)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.HospitalID).First().HospitalID);

            var hospitalIdsUsed = doctorHospital.Values.Distinct().ToList();
            var hospitalById = await _db.Hospitals
                .Where(h => hospitalIdsUsed.Contains(h.HospitalID))
                .Select(h => new { h.HospitalID, h.Name, h.Location, h.City, h.State, h.Pincode })
                .ToDictionaryAsync(h => h.HospitalID);

            var userIds = items.Select(d => d.UserID).Distinct().ToList();
            var nameByUser = await _db.UserProfiles
                .Where(up => userIds.Contains(up.UserID))
                .ToDictionaryAsync(up => up.UserID, up => up.FullName);
            var lastLoginByUser = await _db.UserAuths
                .Where(ua => userIds.Contains(ua.UserID))
                .ToDictionaryAsync(ua => ua.UserID, ua => ua.LastLoginTime);

            var deptIds = items.Where(d => d.PrimaryDepartmentID.HasValue).Select(d => d.PrimaryDepartmentID!.Value).Distinct().ToList();
            var deptNameById = await _db.Departments
                .Where(dept => deptIds.Contains(dept.DepartmentID))
                .ToDictionaryAsync(dept => dept.DepartmentID, dept => dept.Name);

            var feeLookup = (await _db.DoctorFees
                .Where(f => f.FeeType == OpdConsultFeeType && f.IsActive
                         && doctorIds.Contains(f.DoctorId) && hospitalIdsUsed.Contains(f.HospitalId))
                .Select(f => new { f.DoctorId, f.HospitalId, f.Amount })
                .ToListAsync())
                .ToDictionary(f => (f.DoctorId, f.HospitalId), f => f.Amount);

            var projected = items.Select(d =>
            {
                var hasHospital = doctorHospital.TryGetValue(d.DoctorID, out var hospitalId);
                var hospital = hasHospital && hospitalById.TryGetValue(hospitalId, out var h) ? h : null;
                return new DoctorListItem
                {
                    DoctorId = d.DoctorID,
                    FullName = nameByUser.TryGetValue(d.UserID, out var n) ? n : null,
                    HospitalId = hasHospital ? hospitalId : Guid.Empty,
                    HospitalName = hospital?.Name,
                    HospitalAddress = hospital != null ? FormatAddress(hospital.Location, hospital.City, hospital.State, hospital.Pincode) : null,
                    DepartmentName = d.PrimaryDepartmentID.HasValue && deptNameById.TryGetValue(d.PrimaryDepartmentID.Value, out var dn) ? dn : null,
                    OpdConsultFee = hasHospital && feeLookup.TryGetValue((d.DoctorID, hospitalId), out var fee) ? fee : (decimal?)null,
                    IsPubliclyListed = d.IsPubliclyListed,
                    IsFeatured = d.IsFeatured,
                    IsDelistedByAdmin = d.IsDelistedByAdmin,
                    IsRegistrationVerified = d.IsRegistrationVerified,
                    RegistrationVerifiedAt = d.RegistrationVerifiedAt,
                    DiscountPercent = d.DiscountPercent,
                    DiscountStartAt = d.DiscountStartAt,
                    DiscountEndAt = d.DiscountEndAt,
                    LastLoginTime = lastLoginByUser.TryGetValue(d.UserID, out var lastLogin) ? lastLogin : null,
                };
            }).ToList();

            return new PagedResult<DoctorListItem>
            {
                Data = projected,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        // Full "A to Z" profile for the CMS admin detail view — every hospital this doctor is
        // affiliated with (not the single deterministic pick GetDoctorsAsync's list row uses),
        // each with its own department + OPD/IPD/Emergency fees.
        public async Task<DoctorDetail?> GetDoctorDetailAsync(Guid doctorId)
        {
            var doctor = await _db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.DoctorID == doctorId);
            if (doctor == null) return null;

            var userProfile = await _db.UserProfiles.AsNoTracking()
                .Where(up => up.UserID == doctor.UserID)
                .Select(up => new { up.FullName })
                .FirstOrDefaultAsync();
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.UserID == doctor.UserID)
                .Select(u => new { u.MobileNumber, u.Email })
                .FirstOrDefaultAsync();
            var lastLoginTime = await _db.UserAuths.AsNoTracking()
                .Where(ua => ua.UserID == doctor.UserID)
                .Select(ua => ua.LastLoginTime)
                .FirstOrDefaultAsync();

            var affiliations = await _db.DoctorDepartments.AsNoTracking()
                .Where(dd => dd.DoctorID == doctorId)
                .Select(dd => new { dd.HospitalID, dd.DepartmentID })
                .Distinct()
                .ToListAsync();
            var hospitalIds = affiliations.Select(a => a.HospitalID).Distinct().ToList();
            var departmentIds = affiliations.Select(a => a.DepartmentID).Distinct().ToList();

            var hospitalById = await _db.Hospitals.AsNoTracking()
                .Where(h => hospitalIds.Contains(h.HospitalID))
                .Select(h => new { h.HospitalID, h.Name, h.Location, h.City, h.State, h.Pincode })
                .ToDictionaryAsync(h => h.HospitalID);
            var departmentNameById = await _db.Departments.AsNoTracking()
                .Where(dept => departmentIds.Contains(dept.DepartmentID))
                .ToDictionaryAsync(dept => dept.DepartmentID, dept => dept.Name);

            var specializations = await _db.DoctorSpecializations.AsNoTracking()
                .Where(ds => ds.DoctorID == doctorId && ds.Specialization != null && ds.Specialization.IsActive)
                .Select(ds => ds.Specialization!.Name)
                .ToListAsync();

            var fees = await _db.DoctorFees.AsNoTracking()
                .Where(f => f.DoctorId == doctorId && hospitalIds.Contains(f.HospitalId) && f.IsActive
                    && (f.FeeType == "OPD_CONSULT" || f.FeeType == "IPD_VISIT" || f.FeeType == "EMERGENCY"))
                .ToListAsync();
            var feesByHospital = fees.GroupBy(f => f.HospitalId).ToDictionary(g => g.Key, g => g.ToList());

            var languages = string.IsNullOrWhiteSpace(doctor.LanguagesJson)
                ? new List<string>()
                : (JsonSerializer.Deserialize<List<string>>(doctor.LanguagesJson) ?? new List<string>());

            // Group by hospital rather than iterate affiliations directly — a doctor can have more
            // than one department row per hospital; one affiliation entry per hospital is enough here.
            var hospitalGroups = affiliations.GroupBy(a => a.HospitalID);
            var hospitalAffiliations = hospitalGroups.Select(g =>
            {
                var first = g.First();
                feesByHospital.TryGetValue(g.Key, out var hospitalFees);
                hospitalById.TryGetValue(g.Key, out var hospital);
                return new DoctorHospitalAffiliation
                {
                    HospitalId = g.Key,
                    HospitalName = hospital?.Name,
                    HospitalAddress = hospital != null ? FormatAddress(hospital.Location, hospital.City, hospital.State, hospital.Pincode) : null,
                    DepartmentName = departmentNameById.TryGetValue(first.DepartmentID, out var dn) ? dn : null,
                    OpdConsultFee = hospitalFees?.FirstOrDefault(f => f.FeeType == "OPD_CONSULT")?.Amount,
                    IpdVisitFee = hospitalFees?.FirstOrDefault(f => f.FeeType == "IPD_VISIT")?.Amount,
                    EmergencyFee = hospitalFees?.FirstOrDefault(f => f.FeeType == "EMERGENCY")?.Amount,
                };
            }).OrderBy(h => h.HospitalName).ToList();

            return new DoctorDetail
            {
                DoctorId = doctor.DoctorID,
                UserId = doctor.UserID,
                FullName = userProfile?.FullName,
                MobileNumber = user?.MobileNumber,
                Email = user?.Email,
                PhotoUrl = doctor.ObjectURL,
                LicenseNumber = doctor.LicenseNumber,
                MedicalCouncil = doctor.MedicalCouncil,
                RegistrationYear = doctor.RegistrationYear,
                Qualification = doctor.Qualification,
                ExperienceYears = doctor.ExperienceYears,
                Bio = doctor.Bio,
                ProfileCompletionPercent = doctor.ProfileCompletionPercent,
                Specializations = specializations
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Languages = languages,
                PublicContactEmail = doctor.PublicContactEmail,
                PublicContactPhone = doctor.PublicContactPhone,
                IsPubliclyListed = doctor.IsPubliclyListed,
                IsFeatured = doctor.IsFeatured,
                IsDelistedByAdmin = doctor.IsDelistedByAdmin,
                IsRegistrationVerified = doctor.IsRegistrationVerified,
                RegistrationVerifiedAt = doctor.RegistrationVerifiedAt,
                DiscountPercent = doctor.DiscountPercent,
                DiscountStartAt = doctor.DiscountStartAt,
                DiscountEndAt = doctor.DiscountEndAt,
                CreatedAt = doctor.CreatedAt,
                LastLoginTime = lastLoginTime,
                Hospitals = hospitalAffiliations,
            };
        }

        public async Task<UpdateDoctorMarketingResult> UpdateDoctorMarketingAsync(Guid doctorId, UpdateDoctorMarketingRequest request, Guid? actingUserId)
        {
            if (request.DiscountPercent.HasValue && (request.DiscountPercent.Value < 0 || request.DiscountPercent.Value > 100))
                return new UpdateDoctorMarketingResult { Success = false, Message = "Discount percent must be between 0 and 100." };

            if (request.DiscountStartAt.HasValue && request.DiscountEndAt.HasValue && request.DiscountEndAt.Value < request.DiscountStartAt.Value)
                return new UpdateDoctorMarketingResult { Success = false, Message = "Discount end date cannot be before the start date." };

            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.DoctorID == doctorId);
            if (doctor == null)
                return new UpdateDoctorMarketingResult { Success = false, Message = "Doctor not found." };

            doctor.IsFeatured = request.IsFeatured;
            doctor.IsDelistedByAdmin = request.IsDelistedByAdmin;
            doctor.DiscountPercent = request.DiscountPercent;
            doctor.DiscountStartAt = request.DiscountStartAt;
            doctor.DiscountEndAt = request.DiscountEndAt;

            // Only touch the verified-at/by-whom audit trail on an actual transition — otherwise
            // resaving this endpoint for an unrelated field (e.g. just toggling Featured) would
            // keep resetting RegistrationVerifiedAt to "now" every time.
            if (request.IsRegistrationVerified != doctor.IsRegistrationVerified)
            {
                doctor.IsRegistrationVerified = request.IsRegistrationVerified;
                doctor.RegistrationVerifiedAt = request.IsRegistrationVerified ? DateTime.UtcNow : (DateTime?)null;
                doctor.RegistrationVerifiedByUserId = request.IsRegistrationVerified ? actingUserId : (Guid?)null;
            }

            await _db.SaveChangesAsync();

            return new UpdateDoctorMarketingResult { Success = true, Message = "Doctor marketing settings saved." };
        }

        public async Task<BulkUpdateDoctorMarketingResult> BulkUpdateDoctorMarketingAsync(BulkUpdateDoctorMarketingRequest request)
        {
            var doctorIds = request.DoctorIds.Distinct().ToList();
            if (doctorIds.Count == 0)
                return new BulkUpdateDoctorMarketingResult { Success = false, Message = "No doctors selected." };

            if (request.UpdateDiscount)
            {
                if (request.DiscountPercent.HasValue && (request.DiscountPercent.Value < 0 || request.DiscountPercent.Value > 100))
                    return new BulkUpdateDoctorMarketingResult { Success = false, Message = "Discount percent must be between 0 and 100." };

                if (request.DiscountStartAt.HasValue && request.DiscountEndAt.HasValue && request.DiscountEndAt.Value < request.DiscountStartAt.Value)
                    return new BulkUpdateDoctorMarketingResult { Success = false, Message = "Discount end date cannot be before the start date." };
            }

            var doctors = await _db.Doctors.Where(d => doctorIds.Contains(d.DoctorID)).ToListAsync();
            var foundIds = doctors.Select(d => d.DoctorID).ToHashSet();
            var notFoundIds = doctorIds.Where(id => !foundIds.Contains(id)).ToList();

            foreach (var doctor in doctors)
            {
                if (request.IsFeatured.HasValue) doctor.IsFeatured = request.IsFeatured.Value;
                if (request.IsDelistedByAdmin.HasValue) doctor.IsDelistedByAdmin = request.IsDelistedByAdmin.Value;
                if (request.UpdateDiscount)
                {
                    doctor.DiscountPercent = request.DiscountPercent;
                    doctor.DiscountStartAt = request.DiscountStartAt;
                    doctor.DiscountEndAt = request.DiscountEndAt;
                }
            }

            await _db.SaveChangesAsync();

            return new BulkUpdateDoctorMarketingResult
            {
                Success = true,
                Message = $"Updated {doctors.Count} doctor(s).",
                UpdatedCount = doctors.Count,
                NotFoundDoctorIds = notFoundIds,
            };
        }
    }
}
