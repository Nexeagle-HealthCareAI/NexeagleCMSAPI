using System;
using System.Collections.Generic;
using System.Linq;
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
            var hospitalNameById = await _db.Hospitals
                .Where(h => hospitalIdsUsed.Contains(h.HospitalID))
                .Select(h => new { h.HospitalID, h.Name })
                .ToDictionaryAsync(h => h.HospitalID, h => h.Name);

            var userIds = items.Select(d => d.UserID).Distinct().ToList();
            var nameByUser = await _db.UserProfiles
                .Where(up => userIds.Contains(up.UserID))
                .ToDictionaryAsync(up => up.UserID, up => up.FullName);

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
                return new DoctorListItem
                {
                    DoctorId = d.DoctorID,
                    FullName = nameByUser.TryGetValue(d.UserID, out var n) ? n : null,
                    HospitalId = hasHospital ? hospitalId : Guid.Empty,
                    HospitalName = hasHospital && hospitalNameById.TryGetValue(hospitalId, out var hn) ? hn : null,
                    DepartmentName = d.PrimaryDepartmentID.HasValue && deptNameById.TryGetValue(d.PrimaryDepartmentID.Value, out var dn) ? dn : null,
                    OpdConsultFee = hasHospital && feeLookup.TryGetValue((d.DoctorID, hospitalId), out var fee) ? fee : (decimal?)null,
                    IsPubliclyListed = d.IsPubliclyListed,
                    IsFeatured = d.IsFeatured,
                    IsDelistedByAdmin = d.IsDelistedByAdmin,
                    DiscountPercent = d.DiscountPercent,
                    DiscountStartAt = d.DiscountStartAt,
                    DiscountEndAt = d.DiscountEndAt,
                };
            }).ToList();

            return new PagedResult<DoctorListItem>
            {
                Data = projected,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        public async Task<UpdateDoctorMarketingResult> UpdateDoctorMarketingAsync(Guid doctorId, UpdateDoctorMarketingRequest request)
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

            await _db.SaveChangesAsync();

            return new UpdateDoctorMarketingResult { Success = true, Message = "Doctor marketing settings saved." };
        }
    }
}
