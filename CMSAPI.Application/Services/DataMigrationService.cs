using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services
{
    public class DataMigrationService : IDataMigrationService
    {
        private readonly IDataMigrationRepository _repo;
        private readonly IHospitalService _hospitalService;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DataMigrationService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public DataMigrationService(
            IDataMigrationRepository repo, IHospitalService hospitalService, HttpClient httpClient,
            IConfiguration configuration, ILogger<DataMigrationService> logger)
        {
            _repo = repo;
            _hospitalService = hospitalService;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        // Internal shape persisted into MigrationBatches.SummaryJson -- accumulates across the
        // batch's lifecycle (detect-time warnings first, then the full transform summary once
        // /transform has run) rather than needing a second column for detect-only metadata.
        private class BatchSummaryStorage
        {
            public List<string>? DetectWarnings { get; set; }
            public bool DetectGroqUsed { get; set; }
            public MigrationSummaryDto? Transform { get; set; }
        }

        public async Task<BatchDetail?> UploadBatchAsync(Guid hospitalId, string dataType, string fileName, Stream fileStream, Guid createdByUserId)
        {
            var baseUrl = _configuration["DataMigration:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogWarning("Data migration upload requested but DataMigration:BaseUrl isn't configured.");
                return null;
            }

            var maxBytes = long.TryParse(_configuration["DataMigration:MaxUploadBytes"], out var configBytes) ? configBytes : 10 * 1024 * 1024;
            var maxRows = int.TryParse(_configuration["DataMigration:MaxUploadRows"], out var configRows) ? configRows : 15000;

            using var buffered = new MemoryStream();
            await fileStream.CopyToAsync(buffered);
            if (buffered.Length > maxBytes)
            {
                _logger.LogWarning("Data migration upload rejected: {Size} bytes exceeds the {Max} byte limit.", buffered.Length, maxBytes);
                return null;
            }
            buffered.Position = 0;

            PythonDetectResponse? detectResult;
            try
            {
                using var content = new MultipartFormDataContent();
                var fileContent = new StreamContent(buffered);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
                content.Add(fileContent, "file", fileName);
                content.Add(new StringContent(dataType), "dataType");

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/detect") { Content = content };
                AddServiceKey(request);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Data migration /detect failed: {Status} {Body}", response.StatusCode, body);
                    return null;
                }

                detectResult = await response.Content.ReadFromJsonAsync<PythonDetectResponse>(JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Data migration /detect threw while calling {BaseUrl}", baseUrl);
                return null;
            }

            if (detectResult == null) return null;

            if (detectResult.RowCount > maxRows)
            {
                var failedBatch = new MigrationBatch
                {
                    BatchId = Guid.NewGuid(),
                    HospitalId = hospitalId,
                    DataType = dataType,
                    SourceFileName = fileName,
                    SourceRowCount = detectResult.RowCount,
                    Status = "Failed",
                    ErrorMessage = $"File has {detectResult.RowCount} rows, which exceeds the {maxRows}-row limit for this tool.",
                    CreatedByUserId = createdByUserId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                await _repo.CreateBatchAsync(failedBatch);
                return await GetBatchAsync(failedBatch.BatchId);
            }

            var batch = new MigrationBatch
            {
                BatchId = Guid.NewGuid(),
                HospitalId = hospitalId,
                DataType = dataType,
                SourceFileName = fileName,
                SourceRowCount = detectResult.RowCount,
                Status = "Detected",
                ColumnMappingJson = JsonSerializer.Serialize(detectResult.SuggestedMapping),
                SummaryJson = JsonSerializer.Serialize(new BatchSummaryStorage
                {
                    DetectWarnings = detectResult.Warnings,
                    DetectGroqUsed = detectResult.GroqUsed,
                }),
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await _repo.CreateBatchAsync(batch);

            var rows = detectResult.AllRawRows.Select((raw, index) => new MigrationBatchRow
            {
                RowId = Guid.NewGuid(),
                BatchId = batch.BatchId,
                SourceRowNumber = index + 1,
                RawDataJson = JsonSerializer.Serialize(raw),
                RowStatus = "Pending",
                CreatedAt = DateTime.UtcNow,
            });
            await _repo.AddRowsAsync(rows);

            return await GetBatchAsync(batch.BatchId);
        }

        public async Task<PagedResult<BatchListItem>> GetBatchesAsync(Guid? hospitalId, int page, int limit)
        {
            var (items, total) = await _repo.GetBatchesAsync(hospitalId, page, limit);
            return new PagedResult<BatchListItem>
            {
                Data = items.Select(MapToBatchListItem),
                Pagination = new PaginationInfo
                {
                    CurrentPage = page,
                    TotalPages = limit > 0 ? (int)Math.Ceiling(total / (double)limit) : 0,
                    TotalItems = total,
                },
            };
        }

        public async Task<BatchDetail?> GetBatchAsync(Guid batchId)
        {
            var batch = await _repo.GetBatchAsync(batchId);
            if (batch == null) return null;

            var detail = new BatchDetail
            {
                BatchId = batch.BatchId,
                HospitalId = batch.HospitalId,
                DataType = batch.DataType,
                SourceFileName = batch.SourceFileName,
                SourceRowCount = batch.SourceRowCount,
                Status = batch.Status,
                CreatedAt = batch.CreatedAt,
                UpdatedAt = batch.UpdatedAt,
                ErrorMessage = batch.ErrorMessage,
                ColumnMapping = string.IsNullOrWhiteSpace(batch.ColumnMappingJson)
                    ? new List<ColumnMappingEntry>()
                    : JsonSerializer.Deserialize<List<ColumnMappingEntry>>(batch.ColumnMappingJson!, JsonOptions) ?? new(),
            };

            if (!string.IsNullOrWhiteSpace(batch.SummaryJson))
            {
                var storage = JsonSerializer.Deserialize<BatchSummaryStorage>(batch.SummaryJson!, JsonOptions);
                if (storage != null)
                {
                    detail.Warnings = storage.DetectWarnings ?? new List<string>();
                    detail.Summary = storage.Transform;
                }
            }

            // Raw headers + sample rows are derived from the persisted rows rather than stored
            // separately -- the rows are already the durable source of truth for re-transform.
            var firstRows = (await _repo.GetRowsAsync(batchId, 1, 20, null)).Items;
            if (firstRows.Count > 0)
            {
                var firstRaw = JsonSerializer.Deserialize<Dictionary<string, string?>>(firstRows[0].RawDataJson, JsonOptions);
                detail.RawHeaders = firstRaw?.Keys.ToList() ?? new List<string>();
                detail.SampleRawRows = firstRows
                    .Select(r => JsonSerializer.Deserialize<Dictionary<string, string?>>(r.RawDataJson, JsonOptions) ?? new())
                    .ToList();
            }

            return detail;
        }

        public async Task<bool> UpdateColumnMappingAsync(Guid batchId, UpdateColumnMappingRequest request)
        {
            var batch = await _repo.GetBatchAsync(batchId);
            if (batch == null) return false;

            batch.ColumnMappingJson = JsonSerializer.Serialize(request.ColumnMapping);
            await _repo.UpdateBatchAsync(batch);
            return true;
        }

        public async Task<BatchDetail?> TransformAsync(Guid batchId)
        {
            var batch = await _repo.GetBatchAsync(batchId);
            if (batch == null) return null;

            var baseUrl = _configuration["DataMigration:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogWarning("Data migration transform requested but DataMigration:BaseUrl isn't configured.");
                return null;
            }

            batch.Status = "Transforming";
            await _repo.UpdateBatchAsync(batch);

            var allRows = await _repo.GetAllRowsAsync(batchId);
            var columnMapping = string.IsNullOrWhiteSpace(batch.ColumnMappingJson)
                ? new List<ColumnMappingEntry>()
                : JsonSerializer.Deserialize<List<ColumnMappingEntry>>(batch.ColumnMappingJson!, JsonOptions) ?? new();

            var existingPatients = await _hospitalService.GetPatientIdentitySnapshotAsync(batch.HospitalId);

            var doctorMapEntries = await _repo.GetDoctorMapAsync(batchId);
            var doctorMapOverrides = doctorMapEntries
                .Where(m => m.MappedDoctorId.HasValue)
                .Select(m => new DoctorMapEntryDto
                {
                    MapId = m.MapId,
                    SourceDoctorName = m.SourceDoctorName,
                    SourceDepartment = m.SourceDepartment,
                    MappedDoctorId = m.MappedDoctorId,
                    MappedDoctorName = m.MappedDoctorName,
                })
                .ToList();

            var hospital = await _hospitalService.GetHospitalByIdAsync(batch.HospitalId);
            var knownDoctors = (hospital?.Doctors ?? new List<DoctorInfo>())
                .Select(d => new KnownDoctorDto { DoctorId = d.Id, Name = d.Name, Departments = d.Departments })
                .ToList();

            var transformRequest = new PythonTransformRequest
            {
                DataType = batch.DataType,
                ColumnMapping = columnMapping,
                Rows = allRows.Select(r => new PythonTransformRequestRow
                {
                    SourceRowNumber = r.SourceRowNumber,
                    Raw = JsonSerializer.Deserialize<Dictionary<string, string?>>(r.RawDataJson, JsonOptions) ?? new(),
                }).ToList(),
                ExistingPatients = existingPatients,
                DoctorMapOverrides = doctorMapOverrides,
                KnownHospitalDoctors = knownDoctors,
            };

            PythonTransformResponse? transformResult;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transform")
                {
                    Content = JsonContent.Create(transformRequest),
                };
                AddServiceKey(request);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Data migration /transform failed: {Status} {Body}", response.StatusCode, body);
                    batch.Status = "Failed";
                    batch.ErrorMessage = "Transform failed -- please try again.";
                    await _repo.UpdateBatchAsync(batch);
                    return await GetBatchAsync(batchId);
                }

                transformResult = await response.Content.ReadFromJsonAsync<PythonTransformResponse>(JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Data migration /transform threw while calling {BaseUrl}", baseUrl);
                batch.Status = "Failed";
                batch.ErrorMessage = "Transform failed -- please try again.";
                await _repo.UpdateBatchAsync(batch);
                return await GetBatchAsync(batchId);
            }

            if (transformResult == null)
            {
                batch.Status = "Failed";
                batch.ErrorMessage = "Transform returned no result.";
                await _repo.UpdateBatchAsync(batch);
                return await GetBatchAsync(batchId);
            }

            // Replace every row's transformed state -- safe, nothing has been committed yet.
            await _repo.DeleteRowsAsync(batchId);
            var newRows = allRows.Join(
                transformResult.Rows,
                r => r.SourceRowNumber,
                t => t.SourceRowNumber,
                (r, t) => new MigrationBatchRow
                {
                    RowId = Guid.NewGuid(),
                    BatchId = batchId,
                    SourceRowNumber = r.SourceRowNumber,
                    RawDataJson = r.RawDataJson,
                    TransformedDataJson = JsonSerializer.Serialize(t.Transformed),
                    IdentityKey = t.IdentityKey,
                    ResolvedPatientId = t.ResolvedPatientId,
                    IsNewPatient = t.IsNewPatient,
                    FlagsJson = JsonSerializer.Serialize(t.Flags),
                    RowStatus = t.RowStatus,
                    CreatedAt = DateTime.UtcNow,
                });
            await _repo.AddRowsAsync(newRows);

            if (transformResult.DistinctUnmappedDoctors.Count > 0)
            {
                var newMapEntries = transformResult.DistinctUnmappedDoctors.Select(d => new MigrationDoctorMap
                {
                    SourceDoctorName = d.SourceDoctorName,
                    SourceDepartment = d.SourceDepartment,
                });
                await _repo.UpsertDoctorMapAsync(batchId, newMapEntries);
            }

            var existingStorage = string.IsNullOrWhiteSpace(batch.SummaryJson)
                ? new BatchSummaryStorage()
                : JsonSerializer.Deserialize<BatchSummaryStorage>(batch.SummaryJson!, JsonOptions) ?? new BatchSummaryStorage();
            transformResult.Summary.Narrative = transformResult.Narrative;
            existingStorage.Transform = transformResult.Summary;

            batch.SummaryJson = JsonSerializer.Serialize(existingStorage);
            batch.Status = "Ready";
            await _repo.UpdateBatchAsync(batch);

            return await GetBatchAsync(batchId);
        }

        public async Task<PagedResult<MigrationRowDto>> GetRowsAsync(Guid batchId, int page, int limit, string? status)
        {
            var (items, total) = await _repo.GetRowsAsync(batchId, page, limit, status);
            return new PagedResult<MigrationRowDto>
            {
                Data = items.Select(MapToRowDto),
                Pagination = new PaginationInfo
                {
                    CurrentPage = page,
                    TotalPages = limit > 0 ? (int)Math.Ceiling(total / (double)limit) : 0,
                    TotalItems = total,
                },
            };
        }

        public async Task<List<DoctorMapEntryDto>> GetDoctorMapAsync(Guid batchId)
        {
            var entries = await _repo.GetDoctorMapAsync(batchId);
            return entries.Select(m => new DoctorMapEntryDto
            {
                MapId = m.MapId,
                SourceDoctorName = m.SourceDoctorName,
                SourceDepartment = m.SourceDepartment,
                MappedDoctorId = m.MappedDoctorId,
                MappedDoctorName = m.MappedDoctorName,
            }).ToList();
        }

        public async Task<bool> UpdateDoctorMapAsync(Guid batchId, UpdateDoctorMapRequest request)
        {
            var batch = await _repo.GetBatchAsync(batchId);
            if (batch == null) return false;

            var entries = request.Entries.Select(e => new MigrationDoctorMap
            {
                MapId = e.MapId ?? Guid.Empty,
                SourceDoctorName = e.SourceDoctorName,
                SourceDepartment = e.SourceDepartment,
                MappedDoctorId = e.MappedDoctorId,
                MappedDoctorName = e.MappedDoctorName,
            });
            await _repo.UpsertDoctorMapAsync(batchId, entries);
            return true;
        }

        private void AddServiceKey(HttpRequestMessage request)
        {
            var key = _configuration["ServiceAuth:DataMigrationServiceKey"];
            if (!string.IsNullOrWhiteSpace(key))
            {
                request.Headers.Add("X-Service-Key", key);
            }
        }

        private static BatchListItem MapToBatchListItem(MigrationBatch batch) => new()
        {
            BatchId = batch.BatchId,
            HospitalId = batch.HospitalId,
            DataType = batch.DataType,
            SourceFileName = batch.SourceFileName,
            SourceRowCount = batch.SourceRowCount,
            Status = batch.Status,
            CreatedAt = batch.CreatedAt,
            UpdatedAt = batch.UpdatedAt,
        };

        private static MigrationRowDto MapToRowDto(MigrationBatchRow row) => new()
        {
            RowId = row.RowId,
            SourceRowNumber = row.SourceRowNumber,
            Raw = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.RawDataJson, JsonOptions) ?? new(),
            Transformed = string.IsNullOrWhiteSpace(row.TransformedDataJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(row.TransformedDataJson!, JsonOptions),
            ResolvedPatientId = row.ResolvedPatientId,
            IsNewPatient = row.IsNewPatient,
            Flags = string.IsNullOrWhiteSpace(row.FlagsJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(row.FlagsJson!, JsonOptions) ?? new(),
            RowStatus = row.RowStatus,
        };
    }
}
