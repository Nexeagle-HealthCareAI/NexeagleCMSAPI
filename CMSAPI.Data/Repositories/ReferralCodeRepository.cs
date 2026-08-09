using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class ReferralCodeRepository : IReferralCodeRepository
    {
        private readonly CmsDbContext _db;

        public ReferralCodeRepository(CmsDbContext db)
        {
            _db = db;
        }

        public async Task<IEnumerable<ReferralCodeType>> GetAllTypesAsync()
        {
            return await _db.ReferralCodeTypes
                .AsNoTracking()
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<ReferralCodeType?> GetTypeByIdAsync(Guid referralCodeTypeId)
        {
            return await _db.ReferralCodeTypes.FindAsync(referralCodeTypeId);
        }

        public async Task<ReferralCodeType> CreateTypeAsync(ReferralCodeType type)
        {
            _db.ReferralCodeTypes.Add(type);
            await _db.SaveChangesAsync();
            return type;
        }

        public async Task<ReferralCodeType> UpdateTypeAsync(ReferralCodeType type)
        {
            _db.ReferralCodeTypes.Update(type);
            await _db.SaveChangesAsync();
            return type;
        }

        public async Task<IEnumerable<ReferralCode>> GetAllCodesAsync()
        {
            return await _db.ReferralCodes
                .AsNoTracking()
                .Include(c => c.ReferralCodeType)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<ReferralCode?> GetCodeByIdAsync(Guid referralCodeId)
        {
            return await _db.ReferralCodes.FindAsync(referralCodeId);
        }

        public async Task<ReferralCode?> GetByCodeAsync(string code)
        {
            var normalized = code.Trim().ToUpperInvariant();
            return await _db.ReferralCodes
                .Include(c => c.ReferralCodeType)
                .FirstOrDefaultAsync(c => c.Code == normalized);
        }

        public async Task<bool> ExistsCodeAsync(string code)
        {
            var normalized = code.Trim().ToUpperInvariant();
            return await _db.ReferralCodes.AnyAsync(c => c.Code == normalized);
        }

        public async Task<ReferralCode> CreateCodeAsync(ReferralCode code)
        {
            _db.ReferralCodes.Add(code);
            await _db.SaveChangesAsync();
            return code;
        }

        public async Task<ReferralCode> UpdateCodeAsync(ReferralCode code)
        {
            _db.ReferralCodes.Update(code);
            await _db.SaveChangesAsync();
            return code;
        }
    }
}
