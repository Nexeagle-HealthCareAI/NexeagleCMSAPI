using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    public class SymptomRouterRepository : ISymptomRouterRepository
    {
        private const string EventType_SearchPerformed = "search_performed";
        private const string EventType_BookingStepReached = "booking_step_reached";

        private readonly AppDbContext _db;

        public SymptomRouterRepository(AppDbContext db)
        {
            _db = db;
        }

        private static TrainingExampleItem ToItem(SymptomTrainingExample e) => new()
        {
            Id = e.Id,
            Text = e.Text,
            Specialist = e.Specialist,
            Type = e.Type,
            Source = e.Source,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            CreatedBy = e.CreatedBy,
        };

        public async Task<PagedResult<TrainingExampleItem>> GetTrainingExamplesAsync(
            int page, int limit, string? specialist, string? search)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = _db.SymptomTrainingExamples.AsNoTracking().Where(e => e.IsActive);
            if (!string.IsNullOrWhiteSpace(specialist))
                query = query.Where(e => e.Specialist == specialist);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(e => e.Text.Contains(s));
            }

            var totalItems = await query.LongCountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

            var items = await query
                .OrderByDescending(e => e.UpdatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync();

            return new PagedResult<TrainingExampleItem>
            {
                Data = items.Select(ToItem),
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }

        public async Task<TrainingExampleItem> AddTrainingExampleAsync(UpsertTrainingExampleRequest request, string? createdBy)
        {
            var now = DateTime.UtcNow;
            var entity = new SymptomTrainingExample
            {
                Id = Guid.NewGuid(),
                Text = request.Text.Trim(),
                Specialist = request.Specialist.Trim(),
                Type = request.Type,
                Source = "ManualCMS",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = createdBy,
            };
            _db.SymptomTrainingExamples.Add(entity);
            await _db.SaveChangesAsync();
            return ToItem(entity);
        }

        public async Task<TrainingExampleItem?> UpdateTrainingExampleAsync(Guid id, UpsertTrainingExampleRequest request)
        {
            var entity = await _db.SymptomTrainingExamples.FirstOrDefaultAsync(e => e.Id == id && e.IsActive);
            if (entity == null) return null;

            entity.Text = request.Text.Trim();
            entity.Specialist = request.Specialist.Trim();
            entity.Type = request.Type;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return ToItem(entity);
        }

        public async Task<bool> DeleteTrainingExampleAsync(Guid id)
        {
            var entity = await _db.SymptomTrainingExamples.FirstOrDefaultAsync(e => e.Id == id && e.IsActive);
            if (entity == null) return false;

            // Soft delete, same convention as IsActive elsewhere — keeps history/audit trail
            // rather than losing the row outright.
            entity.IsActive = false;
            entity.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        private class SearchMetadata
        {
            public string? Query { get; set; }
            public string? Method { get; set; }
            public double? Confidence { get; set; }
            public string? ModelVersion { get; set; }
        }

        private static T? ParseJson<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        // Correlates each NLP-router-driven search_performed event with the same SessionId's
        // completed booking (booking_step_reached, step="done"), if one exists. Mirrors the
        // group-by-SessionId approach InsightsRepository.GetBookingFunnelStatsAsync already
        // uses — same table, same correlation key, just producing model-training-shaped output
        // instead of aggregate stats.
        public async Task<PagedResult<FeedbackLogItem>> GetFeedbackLogAsync(
            int page, int limit, DateOnly? from, DateOnly? to, bool? correctionsOnly)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;

            var query = _db.AnalyticsEvents.AsNoTracking().Where(e =>
                e.EventType == EventType_SearchPerformed || e.EventType == EventType_BookingStepReached);
            if (from.HasValue) query = query.Where(e => e.OccurredAt >= from.Value.ToDateTime(TimeOnly.MinValue));
            if (to.HasValue) query = query.Where(e => e.OccurredAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var events = await query
                .Where(e => e.SessionId != null)
                .Select(e => new { e.EventType, e.SessionId, e.SpecialtyId, e.OccurredAt, e.MetadataJson })
                .ToListAsync();

            var results = new List<FeedbackLogItem>();
            foreach (var grp in events.GroupBy(e => e.SessionId))
            {
                // Only sessions whose search actually went through the NLP router (has a
                // "method" in its metadata) carry the signal this pipeline needs — plain
                // literal-keyword or Anthropic-fallback searches aren't router feedback.
                var searchEvt = grp
                    .Where(e => e.EventType == EventType_SearchPerformed)
                    .Select(e => new { e.OccurredAt, e.SpecialtyId, Meta = ParseJson<SearchMetadata>(e.MetadataJson) })
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Meta?.Method));
                if (searchEvt == null) continue;

                var doneEvt = grp
                    .Where(e => e.EventType == EventType_BookingStepReached)
                    .OrderByDescending(e => e.OccurredAt)
                    .FirstOrDefault();
                // "done" step is nested in metadata (see BookingStepMetadata convention already
                // used by InsightsRepository) — re-check here since we didn't parse it above.
                var hasBooking = doneEvt != null &&
                    string.Equals(ParseJson<Dictionary<string, object>>(doneEvt.MetadataJson)?
                        .GetValueOrDefault("step")?.ToString(), "done", StringComparison.OrdinalIgnoreCase);

                var actualSpecialtyId = hasBooking ? doneEvt!.SpecialtyId : null;
                var predictedSpecialtyId = searchEvt.SpecialtyId;
                var wasCorrection = hasBooking
                    && !string.IsNullOrWhiteSpace(actualSpecialtyId)
                    && !string.Equals(actualSpecialtyId, predictedSpecialtyId, StringComparison.OrdinalIgnoreCase);

                results.Add(new FeedbackLogItem
                {
                    OccurredAt = searchEvt.OccurredAt,
                    SessionId = grp.Key,
                    Query = searchEvt.Meta?.Query ?? string.Empty,
                    PredictedSpecialtyId = predictedSpecialtyId,
                    Method = searchEvt.Meta?.Method,
                    Confidence = searchEvt.Meta?.Confidence,
                    HasBooking = hasBooking,
                    ActualBookedSpecialtyId = actualSpecialtyId,
                    WasCorrection = wasCorrection,
                });
            }

            if (correctionsOnly == true)
                results = results.Where(r => r.WasCorrection).ToList();

            var sorted = results.OrderByDescending(r => r.OccurredAt).ToList();
            var totalItems = sorted.Count;
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);
            var pageItems = sorted.Skip((page - 1) * limit).Take(limit).ToList();

            return new PagedResult<FeedbackLogItem>
            {
                Data = pageItems,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = totalPages, TotalItems = totalItems, ItemsPerPage = limit }
            };
        }
    }
}
