using System;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class MarketingRepository : IMarketingRepository
    {
        private const string Source_1HMSDemo = "1HMSDemo";

        private readonly AppDbContext _db;

        public MarketingRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<PagedResult<DemoLoginLeadItem>> GetDemoLoginLeadsAsync(int page, int limit)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 20;

            var query = _db.HospitalLeads.AsNoTracking()
                .Where(l => l.Source == Source_1HMSDemo)
                .OrderByDescending(l => l.OccurredAt);

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

            var pageItems = await query
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(l => new DemoLoginLeadItem
                {
                    LeadId = l.LeadId,
                    OccurredAt = l.OccurredAt,
                    PatientName = l.PatientName,
                    Mobile = l.Mobile,
                    Country = l.Country,
                    Region = l.Region,
                    City = l.City,
                })
                .ToListAsync();

            return new PagedResult<DemoLoginLeadItem>
            {
                Data = pageItems,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        public async Task<DemoLoginStats> GetDemoLoginStatsAsync()
        {
            var query = _db.HospitalLeads.AsNoTracking().Where(l => l.Source == Source_1HMSDemo);

            var totalLogins = await query.CountAsync();
            var uniqueVisitors = await query
                .Where(l => l.SessionId != null)
                .Select(l => l.SessionId)
                .Distinct()
                .CountAsync();

            var topLocations = await query
                .Where(l => l.Country != null || l.Region != null || l.City != null)
                .GroupBy(l => new { l.Country, l.Region, l.City })
                .Select(g => new DemoLocationCount { Country = g.Key.Country, Region = g.Key.Region, City = g.Key.City, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToListAsync();

            return new DemoLoginStats
            {
                TotalLogins = totalLogins,
                UniqueVisitors = uniqueVisitors,
                TopLocations = topLocations,
            };
        }
    }
}
