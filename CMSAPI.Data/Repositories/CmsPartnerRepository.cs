using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class CmsPartnerRepository : ICmsPartnerRepository
    {
        private readonly CmsDbContext _db;

        public CmsPartnerRepository(CmsDbContext db)
        {
            _db = db;
        }

        public async Task<IEnumerable<CmsPartner>> GetAllPartnersAsync()
        {
            return await _db.CmsPartners
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<CmsPartner?> GetPartnerByIdAsync(Guid partnerId)
        {
            return await _db.CmsPartners
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PartnerId == partnerId);
        }

        public async Task<CmsPartner?> GetPartnerByTokenAsync(string token)
        {
            return await _db.CmsPartners
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.DashboardToken == token);
        }

        public async Task<CmsPartner> CreatePartnerAsync(CmsPartner partner)
        {
            _db.CmsPartners.Add(partner);
            await _db.SaveChangesAsync();
            return partner;
        }
    }
}
