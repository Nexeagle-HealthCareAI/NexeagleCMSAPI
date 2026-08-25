using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class DataMigrationRepository : IDataMigrationRepository
    {
        private readonly CmsDbContext _db;

        public DataMigrationRepository(CmsDbContext db)
        {
            _db = db;
        }

        public async Task<MigrationBatch> CreateBatchAsync(MigrationBatch batch)
        {
            _db.MigrationBatches.Add(batch);
            await _db.SaveChangesAsync();
            return batch;
        }

        public Task<MigrationBatch?> GetBatchAsync(Guid batchId)
            => _db.MigrationBatches.FirstOrDefaultAsync(b => b.BatchId == batchId);

        public async Task<(List<MigrationBatch> Items, int Total)> GetBatchesAsync(Guid? hospitalId, int page, int limit)
        {
            var query = _db.MigrationBatches.AsNoTracking().AsQueryable();
            if (hospitalId.HasValue)
            {
                query = query.Where(b => b.HospitalId == hospitalId.Value);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(b => b.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync();

            return (items, total);
        }

        public async Task UpdateBatchAsync(MigrationBatch batch)
        {
            batch.UpdatedAt = DateTime.UtcNow;
            _db.MigrationBatches.Update(batch);
            await _db.SaveChangesAsync();
        }

        public async Task AddRowsAsync(IEnumerable<MigrationBatchRow> rows)
        {
            _db.MigrationBatchRows.AddRange(rows);
            await _db.SaveChangesAsync();
        }

        public async Task<(List<MigrationBatchRow> Items, int Total)> GetRowsAsync(Guid batchId, int page, int limit, string? status)
        {
            var query = _db.MigrationBatchRows.AsNoTracking().Where(r => r.BatchId == batchId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.RowStatus == status);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(r => r.SourceRowNumber)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync();

            return (items, total);
        }

        public Task<List<MigrationBatchRow>> GetAllRowsAsync(Guid batchId)
            => _db.MigrationBatchRows.AsNoTracking()
                .Where(r => r.BatchId == batchId)
                .OrderBy(r => r.SourceRowNumber)
                .ToListAsync();

        public async Task DeleteRowsAsync(Guid batchId)
        {
            // Transformed/flag state only -- raw rows get re-inserted right after by the caller,
            // so this is safe to call as the first half of a "replace all rows" re-transform.
            var rows = await _db.MigrationBatchRows.Where(r => r.BatchId == batchId).ToListAsync();
            _db.MigrationBatchRows.RemoveRange(rows);
            await _db.SaveChangesAsync();
        }

        public Task<List<MigrationDoctorMap>> GetDoctorMapAsync(Guid batchId)
            => _db.MigrationDoctorMaps.AsNoTracking().Where(m => m.BatchId == batchId).ToListAsync();

        public async Task UpsertDoctorMapAsync(Guid batchId, IEnumerable<MigrationDoctorMap> entries)
        {
            var existing = await _db.MigrationDoctorMaps.Where(m => m.BatchId == batchId).ToListAsync();
            var existingByKey = existing.ToDictionary(m => (m.SourceDoctorName, m.SourceDepartment));

            foreach (var entry in entries)
            {
                var key = (entry.SourceDoctorName, entry.SourceDepartment);
                if (existingByKey.TryGetValue(key, out var current))
                {
                    current.MappedDoctorId = entry.MappedDoctorId;
                    current.MappedDoctorName = entry.MappedDoctorName;
                    current.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    entry.MapId = entry.MapId == Guid.Empty ? Guid.NewGuid() : entry.MapId;
                    entry.BatchId = batchId;
                    entry.CreatedAt = DateTime.UtcNow;
                    entry.UpdatedAt = DateTime.UtcNow;
                    _db.MigrationDoctorMaps.Add(entry);
                }
            }

            await _db.SaveChangesAsync();
        }
    }
}
