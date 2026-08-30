using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IDataMigrationService
{
    Task<BatchDetail?> UploadBatchAsync(Guid hospitalId, string dataType, string fileName, Stream fileStream, Guid createdByUserId);
    Task<PagedResult<BatchListItem>> GetBatchesAsync(Guid? hospitalId, int page, int limit);
    Task<BatchDetail?> GetBatchAsync(Guid batchId);
    Task<bool> UpdateColumnMappingAsync(Guid batchId, UpdateColumnMappingRequest request);
    Task<BatchDetail?> TransformAsync(Guid batchId);
    Task<PagedResult<MigrationRowDto>> GetRowsAsync(Guid batchId, int page, int limit, string? status);
    Task<List<DoctorMapEntryDto>> GetDoctorMapAsync(Guid batchId);
    Task<bool> UpdateDoctorMapAsync(Guid batchId, UpdateDoctorMapRequest request);
}
