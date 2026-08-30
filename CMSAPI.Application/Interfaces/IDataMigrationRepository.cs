using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces;

public interface IDataMigrationRepository
{
    Task<MigrationBatch> CreateBatchAsync(MigrationBatch batch);
    Task<MigrationBatch?> GetBatchAsync(Guid batchId);
    Task<(List<MigrationBatch> Items, int Total)> GetBatchesAsync(Guid? hospitalId, int page, int limit);
    Task UpdateBatchAsync(MigrationBatch batch);

    Task AddRowsAsync(IEnumerable<MigrationBatchRow> rows);
    Task<(List<MigrationBatchRow> Items, int Total)> GetRowsAsync(Guid batchId, int page, int limit, string? status);
    Task<List<MigrationBatchRow>> GetAllRowsAsync(Guid batchId);
    Task DeleteRowsAsync(Guid batchId);

    Task<List<MigrationDoctorMap>> GetDoctorMapAsync(Guid batchId);
    Task UpsertDoctorMapAsync(Guid batchId, IEnumerable<MigrationDoctorMap> entries);
}
