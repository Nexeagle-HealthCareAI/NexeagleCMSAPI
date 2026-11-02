using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Repositories
{
    // Storage behind the CMS "AI Agents" tab: decisions streamed in by 1HMS-NLP-Router, the human review
    // queue the model learns from, and the history of model rebuilds. Days are India time (TrackingTime).
    public class AgentMonitorRepository : IAgentMonitorRepository
    {
        public const string Pending = "pending", Approved = "approved", Rejected = "rejected";
        public const string Src_BookingCorrection = "booking_correction", Src_NoMatch = "no_match",
            Src_LowConfidence = "low_confidence", Src_Clarify = "clarify", Src_Emergency = "emergency_check",
            Src_UserFeedback = "user_feedback", Src_NovelPhrase = "novel_phrase";

        private const int MaxText = 500;
        private readonly AppDbContext _db;
        private readonly ILogger<AgentMonitorRepository> _logger;

        public AgentMonitorRepository(AppDbContext db, ILogger<AgentMonitorRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        /// <summary>CMS-side key for "the same phrase": lower-case, single spaces, SHA-256.</summary>
        public static string TextHash(string text)
        {
            var norm = Spaces.Replace(text.Trim().ToLowerInvariant(), " ");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm))).ToLowerInvariant();
        }

        private static string? Clip(string? s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

        private static (DateTime Start, DateTime End, DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
        {
            var end = to ?? TrackingTime.TodayIst();
            var start = from ?? end.AddDays(-29);
            if (start > end) (start, end) = (end, start);
            return (TrackingTime.StartUtc(start), TrackingTime.EndUtcExclusive(end), start, end);
        }

        private IQueryable<AgentDecision> Decisions(DateTime start, DateTime end, string? agent, string? caller)
        {
            var q = _db.AgentDecisions.AsNoTracking().Where(d => d.OccurredAt >= start && d.OccurredAt < end);
            if (!string.IsNullOrWhiteSpace(agent)) q = q.Where(d => d.Agent == agent);
            if (!string.IsNullOrWhiteSpace(caller)) q = q.Where(d => d.Caller == caller);
            return q;
        }

        private static string SourceFor(DecisionIngestItem d) =>
            d.IsEmergency ? Src_Emergency
            : d.NoMatch || d.Decision == "abstain" ? Src_NoMatch
            : d.Decision is "clarify" or "follow_up" ? Src_Clarify
            : Src_LowConfidence;

        // Adds a sighting of a phrase to the queue: same phrase + same source = one item with a counter.
        // Items already reviewed keep their status (a rejected phrase is not re-opened).
        private async Task<int> UpsertItemsAsync(IEnumerable<AgentLearningItem> sightings)
        {
            var list = sightings.Where(s => !string.IsNullOrWhiteSpace(s.QueryText)).ToList();
            if (list.Count == 0) return 0;
            var hashes = list.Select(s => s.QueryHash).Distinct().ToList();
            var existing = await _db.AgentLearningItems.Where(i => hashes.Contains(i.QueryHash)).ToListAsync();
            var byKey = existing.GroupBy(i => (i.QueryHash, i.Source)).ToDictionary(g => g.Key, g => g.First());
            var created = 0;
            foreach (var s in list)
            {
                if (byKey.TryGetValue((s.QueryHash, s.Source), out var item))
                {
                    item.Occurrences += Math.Max(1, s.Occurrences);
                    if (s.LastSeenAt > item.LastSeenAt) item.LastSeenAt = s.LastSeenAt;
                    if (item.Status == Pending)
                    {
                        item.PredictedSpecialtyId = s.PredictedSpecialtyId ?? item.PredictedSpecialtyId;
                        item.ActualSpecialtyId = s.ActualSpecialtyId ?? item.ActualSpecialtyId;
                        item.Confidence = s.Confidence ?? item.Confidence;
                        item.ModelVersion = s.ModelVersion ?? item.ModelVersion;
                    }
                }
                else
                {
                    s.ItemId = Guid.NewGuid();
                    s.Status = Pending;
                    s.Occurrences = Math.Max(1, s.Occurrences);
                    _db.AgentLearningItems.Add(s);
                    byKey[(s.QueryHash, s.Source)] = s;
                    created++;
                }
            }
            await _db.SaveChangesAsync();
            return created;
        }

        // ── ingest ────────────────────────────────────────────────────────────────────────────────
        public async Task<IngestResult> IngestDecisionsAsync(List<DecisionIngestItem> items)
        {
            var valid = items.Where(i => i.DecisionId != Guid.Empty && !string.IsNullOrWhiteSpace(i.Agent)
                                         && !string.IsNullOrWhiteSpace(i.Decision))
                             .GroupBy(i => i.DecisionId).Select(g => g.First()).ToList();
            var ids = valid.Select(i => i.DecisionId).ToList();
            var known = (await _db.AgentDecisions.AsNoTracking().Where(d => ids.Contains(d.DecisionId))
                .Select(d => d.DecisionId).ToListAsync()).ToHashSet();
            var now = DateTime.UtcNow;
            var fresh = valid.Where(i => !known.Contains(i.DecisionId)).ToList();   // re-sent batch: idempotent

            foreach (var i in fresh)
            {
                _db.AgentDecisions.Add(new AgentDecision
                {
                    DecisionId = i.DecisionId,
                    OccurredAt = i.OccurredAt == default ? now : i.OccurredAt.ToUniversalTime(),
                    Agent = Clip(i.Agent, 20)!,
                    Caller = Clip(i.Caller, 50),
                    RequestId = Clip(i.RequestId, 64),
                    Decision = Clip(i.Decision, 20)!,
                    RouteDecision = Clip(i.RouteDecision, 20),
                    TopSpecialtyId = Clip(i.TopSpecialtyId, 60),
                    TopSpecialist = Clip(i.TopSpecialist, 100),
                    CandidateIds = Clip(i.CandidateIds is { Count: > 0 } c ? string.Join(",", c) : null, 400),
                    Confidence = i.Confidence,
                    MatchScore = i.MatchScore,
                    IsEmergency = i.IsEmergency,
                    EmergencySource = Clip(i.EmergencySource, 20),
                    ClarifyKind = Clip(i.ClarifyKind, 30),
                    NoMatch = i.NoMatch,
                    Method = Clip(i.Method, 40),
                    UsedReasoner = i.UsedReasoner,
                    LatencyMs = i.LatencyMs,
                    ModelVersion = Clip(i.ModelVersion, 60),
                    QueryLength = i.QueryLength,
                    QueryHash = Clip(i.QueryHash, 64),
                    SessionRef = Clip(i.SessionRef, 64),
                    Turn = i.Turn,
                    Degraded = Clip(i.Degraded, 200),
                    ReviewText = Clip(i.ReviewText, 300),
                    Notes = Clip(i.Notes, 400),
                    ReceivedAt = now,
                });
            }
            await _db.SaveChangesAsync();

            var sightings = fresh.Where(i => !string.IsNullOrWhiteSpace(i.ReviewText)).Select(i => new AgentLearningItem
            {
                CreatedAt = now,
                LastSeenAt = i.OccurredAt == default ? now : i.OccurredAt.ToUniversalTime(),
                Occurrences = 1,
                Source = SourceFor(i),
                Agent = Clip(i.Agent, 20)!,
                QueryText = Clip(i.ReviewText!.Trim(), MaxText)!,
                QueryHash = TextHash(i.ReviewText!),
                PredictedSpecialtyId = i.TopSpecialtyId ?? SymptomRouterConstants.LabelToSpecialtyId(i.TopSpecialist) ?? Clip(i.TopSpecialist, 60),
                Confidence = i.Confidence,
                ModelVersion = Clip(i.ModelVersion, 60),
            });
            var created = await UpsertItemsAsync(sightings);
            return new IngestResult { Received = items.Count, Stored = fresh.Count, LearningItems = created };
        }

        public async Task<IngestResult> IngestFeedbackAsync(List<FeedbackIngestItem> items)
        {
            var now = DateTime.UtcNow;
            var sightings = items
                .Where(f => f.Kind is "wrong_specialist" or "new_symptom_phrase" && !string.IsNullOrWhiteSpace(f.Text))
                .Select(f => new AgentLearningItem
                {
                    CreatedAt = now,
                    LastSeenAt = f.OccurredAt == default ? now : f.OccurredAt.ToUniversalTime(),
                    Occurrences = 1,
                    Source = f.Kind == "wrong_specialist" ? Src_UserFeedback : Src_NovelPhrase,
                    Agent = Clip(f.Agent ?? "chat", 20)!,
                    QueryText = Clip(f.Text!.Trim(), MaxText)!,
                    QueryHash = TextHash(f.Text!),
                    PredictedSpecialtyId = SymptomRouterConstants.LabelToSpecialtyId(f.ShownSpecialist) ?? Clip(f.ShownSpecialist, 60),
                    ActualSpecialtyId = SymptomRouterConstants.LabelToSpecialtyId(f.SuggestedSpecialist) ?? Clip(f.SuggestedSpecialist, 60),
                });
            var created = await UpsertItemsAsync(sightings);
            return new IngestResult { Received = items.Count, Stored = items.Count, LearningItems = created };
        }

        public async Task<int> IngestModelVersionAsync(ModelVersionIngestItem m)
        {
            var builtAt = (m.BuiltAt ?? DateTime.UtcNow).ToUniversalTime();
            var learned = 0;
            if (m.Promoted && !string.IsNullOrWhiteSpace(m.ModelVersion) && m.CmsExampleIds is { Count: > 0 })
            {
                var exampleIds = m.CmsExampleIds.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty).ToList();
                // Chunked: SQL Server allows ~2100 parameters per statement.
                foreach (var chunk in exampleIds.Chunk(1000))
                {
                    var chunkList = chunk.ToList();
                    var items = await _db.AgentLearningItems
                        .Where(i => i.Status == Approved && i.ModelVersionLearned == null
                                    && i.TrainingExampleId != null && chunkList.Contains(i.TrainingExampleId.Value))
                        .ToListAsync();
                    foreach (var i in items)
                    {
                        i.ModelVersionLearned = Clip(m.ModelVersion, 60);
                        i.LearnedAt = builtAt;
                    }
                    learned += items.Count;
                }
            }

            var v = m.Validation;
            var p = m.PreviousValidation;
            _db.AgentModelVersions.Add(new AgentModelVersion
            {
                Id = Guid.NewGuid(),
                ModelVersion = Clip(m.ModelVersion, 60),
                PreviousVersion = Clip(m.PreviousVersion, 60),
                BuiltAt = builtAt,
                TriggerReason = Clip(m.Trigger, 30),
                Promoted = m.Promoted,
                Verdict = Clip(m.Verdict, 1000),
                TrainingRows = m.TrainingRows,
                CmsRows = m.CmsRows,
                LearnedItems = learned,
                ValidationRows = v?.Rows,
                Top1 = v?.Top1,
                Top3 = v?.Top3,
                Answered = v?.Answered,
                AnsweredAccuracy = v?.AnsweredAccuracy,
                ConfidentlyWrong = v?.ConfidentlyWrong,
                Abstain = v?.Abstain,
                Clarify = v?.Clarify,
                EmergencyFlagRate = v?.EmergencyFlagRate,
                PrevTop1 = p?.Top1,
                PrevConfidentlyWrong = p?.ConfidentlyWrong,
                DetailsJson = JsonSerializer.Serialize(new { m.Sources, m.Seconds, previousValidation = p }),
            });
            await _db.SaveChangesAsync();
            return learned;
        }

        public async Task<int> UpsertBookingCorrectionsAsync(IEnumerable<FeedbackLogItem> corrections)
        {
            var now = DateTime.UtcNow;
            var rows = corrections.Where(c => c.WasCorrection && !string.IsNullOrWhiteSpace(c.Query)).ToList();
            if (rows.Count == 0) return 0;

            // A rescan sees the same bookings again: only bookings newer than what an item has already
            // counted add to it.
            var hashes = rows.Select(r => TextHash(r.Query)).Distinct().ToList();
            var lastSeen = (await _db.AgentLearningItems.AsNoTracking()
                    .Where(i => i.Source == Src_BookingCorrection && hashes.Contains(i.QueryHash))
                    .Select(i => new { i.QueryHash, i.LastSeenAt }).ToListAsync())
                .GroupBy(x => x.QueryHash).ToDictionary(g => g.Key, g => g.Max(x => x.LastSeenAt));

            var sightings = rows
                .Select(r => new { Row = r, Hash = TextHash(r.Query) })
                .Where(x => !lastSeen.TryGetValue(x.Hash, out var seen) || x.Row.OccurredAt > seen)
                .GroupBy(x => x.Hash)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(x => x.Row.OccurredAt).First().Row;
                    return new AgentLearningItem
                    {
                        CreatedAt = now,
                        LastSeenAt = latest.OccurredAt,
                        Occurrences = g.Count(),
                        Source = Src_BookingCorrection,
                        Agent = "route",
                        QueryText = Clip(latest.Query.Trim(), MaxText)!,
                        QueryHash = g.Key,
                        PredictedSpecialtyId = Clip(latest.PredictedSpecialtyId, 60),
                        ActualSpecialtyId = Clip(latest.ActualBookedSpecialtyId, 60),
                        Confidence = latest.Confidence,
                    };
                })
                .ToList();
            return await UpsertItemsAsync(sightings);
        }

        public async Task PruneDecisionsAsync(DateTime cutoffUtc)
        {
            // Batched so the delete never holds a long lock on a busy table.
            for (var i = 0; i < 50; i++)
            {
                var n = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE TOP (5000) FROM dbo.AgentDecisions WHERE OccurredAt < {cutoffUtc}");
                if (n < 5000) break;
            }
            // Unreviewed phrases also stop being kept after the retention period.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.AgentLearningItems WHERE Status <> 'approved' AND LastSeenAt < {cutoffUtc}");
        }

        // ── dashboard ─────────────────────────────────────────────────────────────────────────────
        public async Task<AgentOverview> GetOverviewAsync(DateOnly? from, DateOnly? to, string? agent, string? caller)
        {
            var (start, end, fromDay, toDay) = Range(from, to);
            var q = Decisions(start, end, agent, caller);

            var byDecision = await q.GroupBy(d => d.Decision).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            int Of(string k) => byDecision.Where(x => x.Key == k).Sum(x => x.Count);
            var total = byDecision.Sum(x => x.Count);

            var latencyCount = await q.CountAsync(d => d.LatencyMs != null);
            double? p95 = null;
            if (latencyCount > 0)
            {
                var skip = (int)Math.Floor(0.95 * (latencyCount - 1));
                p95 = await q.Where(d => d.LatencyMs != null).OrderBy(d => d.LatencyMs).Skip(skip)
                    .Select(d => d.LatencyMs).FirstOrDefaultAsync();
            }

            var buckets = await q.Where(d => d.Confidence != null)
                .GroupBy(d => (int)Math.Floor(d.Confidence!.Value * 10))
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var bucketList = Enumerable.Range(0, 10).Select(b => new NameCount
            {
                Name = $"{b / 10.0:0.0}–{(b + 1) / 10.0:0.0}",
                Count = buckets.Where(x => Math.Clamp(x.Key, 0, 9) == b).Sum(x => x.Count),
            }).ToList();

            return new AgentOverview
            {
                From = fromDay.ToString("yyyy-MM-dd"),
                To = toDay.ToString("yyyy-MM-dd"),
                Agent = agent,
                Caller = caller,
                Total = total,
                Answered = Of("answer"),
                Clarified = Of("clarify"),
                Abstained = Of("abstain"),
                Emergencies = await q.CountAsync(d => d.IsEmergency),
                FollowUps = Of("follow_up"),
                Recommendations = Of("recommendation"),
                ReasonerUsed = await q.CountAsync(d => d.UsedReasoner),
                Degraded = await q.CountAsync(d => d.Degraded != null),
                AvgConfidence = await q.AverageAsync(d => d.Confidence),
                AvgLatencyMs = await q.AverageAsync(d => d.LatencyMs),
                P95LatencyMs = p95,
                LastDecisionAt = await _db.AgentDecisions.AsNoTracking().MaxAsync(d => (DateTime?)d.OccurredAt),
                ByAgent = await CountBy(q, d => d.Agent),
                ByCaller = await CountBy(q, d => d.Caller ?? "unknown"),
                TopSpecialties = await CountBy(q.Where(d => d.Decision == "answer" && d.TopSpecialtyId != null), d => d.TopSpecialtyId!, 15),
                ConfidenceBuckets = bucketList,
                ModelVersionsInUse = await CountBy(q.Where(d => d.ModelVersion != null), d => d.ModelVersion!, 10),
                Daily = await DailyAsync(q),
            };
        }

        private static async Task<List<NameCount>> CountBy(IQueryable<AgentDecision> q,
            System.Linq.Expressions.Expression<Func<AgentDecision, string>> key, int take = 20) =>
            await q.GroupBy(key).Select(g => new NameCount { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(take).ToListAsync();

        private async Task<List<AgentDailyRow>> DailyAsync(IQueryable<AgentDecision> q)
        {
            var ist = (int)TrackingTime.IstOffset.TotalMinutes;
            List<(DateTime Day, string Decision, bool Emergency, int Count, double? Conf, double? Lat)> rows;
            try
            {
                rows = (await q.GroupBy(d => new { Day = d.OccurredAt.AddMinutes(ist).Date, d.Decision, d.IsEmergency })
                        .Select(g => new { g.Key.Day, g.Key.Decision, g.Key.IsEmergency, Count = g.Count(),
                                           Conf = g.Average(d => d.Confidence), Lat = g.Average(d => d.LatencyMs) })
                        .ToListAsync())
                    .Select(x => (x.Day, x.Decision, x.IsEmergency, x.Count, x.Conf, x.Lat)).ToList();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Agent daily trend: SQL grouping not translated; computing in memory");
                rows = (await q.Select(d => new { d.OccurredAt, d.Decision, d.IsEmergency, d.Confidence, d.LatencyMs }).ToListAsync())
                    .GroupBy(d => new { Day = (d.OccurredAt + TrackingTime.IstOffset).Date, d.Decision, d.IsEmergency })
                    .Select(g => (g.Key.Day, g.Key.Decision, g.Key.IsEmergency, g.Count(),
                                  g.Average(d => d.Confidence), g.Average(d => d.LatencyMs)))
                    .ToList();
            }

            return rows.GroupBy(r => r.Day).OrderBy(g => g.Key).Select(g =>
            {
                int Of(string k) => g.Where(r => r.Decision == k).Sum(r => r.Count);
                var total = g.Sum(r => r.Count);
                double? Weighted(Func<(DateTime Day, string Decision, bool Emergency, int Count, double? Conf, double? Lat), double?> f)
                {
                    var parts = g.Where(r => f(r) != null).ToList();
                    var n = parts.Sum(r => r.Count);
                    return n > 0 ? parts.Sum(r => f(r)!.Value * r.Count) / n : null;
                }
                return new AgentDailyRow
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    Total = total,
                    Answer = Of("answer"),
                    Clarify = Of("clarify"),
                    Abstain = Of("abstain"),
                    Emergency = g.Where(r => r.Emergency).Sum(r => r.Count),
                    FollowUp = Of("follow_up"),
                    Recommendation = Of("recommendation"),
                    AvgConfidence = Weighted(r => r.Conf),
                    AvgLatencyMs = Weighted(r => r.Lat),
                };
            }).ToList();
        }

        public async Task<PagedResult<AgentDecisionItem>> GetDecisionsAsync(int page, int limit, DateOnly? from, DateOnly? to,
            string? agent, string? caller, string? decision)
        {
            if (page < 1) page = 1;
            limit = Math.Clamp(limit, 1, 200);
            var (start, end, _, _) = Range(from, to);
            var q = Decisions(start, end, agent, caller);
            if (!string.IsNullOrWhiteSpace(decision))
                q = decision == "emergency" ? q.Where(d => d.IsEmergency) : q.Where(d => d.Decision == decision);
            var total = await q.CountAsync();
            var rows = await q.OrderByDescending(d => d.OccurredAt).ThenBy(d => d.DecisionId)
                .Skip((page - 1) * limit).Take(limit)
                .Select(d => new AgentDecisionItem
                {
                    DecisionId = d.DecisionId, OccurredAt = d.OccurredAt, Agent = d.Agent, Caller = d.Caller,
                    Decision = d.Decision, TopSpecialtyId = d.TopSpecialtyId, TopSpecialist = d.TopSpecialist,
                    CandidateIds = d.CandidateIds, Confidence = d.Confidence, MatchScore = d.MatchScore,
                    IsEmergency = d.IsEmergency, UsedReasoner = d.UsedReasoner, LatencyMs = d.LatencyMs,
                    ModelVersion = d.ModelVersion, ReviewText = d.ReviewText, Notes = d.Notes,
                })
                .ToListAsync();
            return new PagedResult<AgentDecisionItem>
            {
                Data = rows,
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = (int)Math.Ceiling(total / (double)limit), TotalItems = total, ItemsPerPage = limit },
            };
        }

        public Task<DateTime?> LastDecisionReceivedAtAsync() =>
            _db.AgentDecisions.AsNoTracking().MaxAsync(d => (DateTime?)d.ReceivedAt);

        // ── learning loop ─────────────────────────────────────────────────────────────────────────
        public async Task<LearningSummary> GetLearningSummaryAsync()
        {
            var counts = await _db.AgentLearningItems.AsNoTracking()
                .GroupBy(i => new { i.Status, Learned = i.ModelVersionLearned != null })
                .Select(g => new { g.Key.Status, g.Key.Learned, Count = g.Count() }).ToListAsync();
            var pendingBySource = await _db.AgentLearningItems.AsNoTracking().Where(i => i.Status == Pending)
                .GroupBy(i => i.Source).Select(g => new NameCount { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ToListAsync();

            // Last 12 weeks (Monday-based, India time): found / reviewed / learned.
            var weeks = 12;
            var todayIst = TrackingTime.TodayIst();
            var monday = todayIst.AddDays(-(((int)todayIst.DayOfWeek + 6) % 7)).AddDays(-7 * (weeks - 1));
            var since = TrackingTime.StartUtc(monday);
            var recent = await _db.AgentLearningItems.AsNoTracking()
                .Where(i => i.CreatedAt >= since || i.ReviewedAt >= since || i.LearnedAt >= since)
                .Select(i => new { i.CreatedAt, i.ReviewedAt, i.LearnedAt }).ToListAsync();
            int WeekIndex(DateTime utc) => (TrackingTime.IstDate(utc).DayNumber - monday.DayNumber) / 7;
            var weekly = Enumerable.Range(0, weeks).Select(w => new WeekCount
            {
                WeekStart = monday.AddDays(7 * w).ToString("yyyy-MM-dd"),
                Found = recent.Count(r => r.CreatedAt >= since && WeekIndex(r.CreatedAt) == w),
                Reviewed = recent.Count(r => r.ReviewedAt is { } t && t >= since && WeekIndex(t) == w),
                Learned = recent.Count(r => r.LearnedAt is { } t && t >= since && WeekIndex(t) == w),
            }).ToList();

            int Sum(string status, bool? learned = null) =>
                counts.Where(c => c.Status == status && (learned == null || c.Learned == learned)).Sum(c => c.Count);
            return new LearningSummary
            {
                Pending = Sum(Pending),
                Approved = Sum(Approved),
                Rejected = Sum(Rejected),
                AwaitingRebuild = Sum(Approved, false),
                Learned = Sum(Approved, true),
                PendingBySource = pendingBySource,
                Weekly = weekly,
            };
        }

        public async Task<PagedResult<LearningItemDto>> GetLearningItemsAsync(int page, int limit, string? status, string? source, string? search)
        {
            if (page < 1) page = 1;
            limit = Math.Clamp(limit, 1, 100);
            var q = _db.AgentLearningItems.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                q = status == "learned" ? q.Where(i => i.ModelVersionLearned != null)
                  : status == "awaiting" ? q.Where(i => i.Status == Approved && i.ModelVersionLearned == null)
                  : q.Where(i => i.Status == status);
            if (!string.IsNullOrWhiteSpace(source)) q = q.Where(i => i.Source == source);
            if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(i => i.QueryText.Contains(s)); }

            var total = await q.CountAsync();
            // Most-repeated first while pending (biggest impact), newest review first otherwise.
            var ordered = status == Pending || string.IsNullOrWhiteSpace(status)
                ? q.OrderByDescending(i => i.Occurrences).ThenByDescending(i => i.LastSeenAt)
                : q.OrderByDescending(i => i.ReviewedAt ?? i.LastSeenAt);
            var rows = await ordered.ThenBy(i => i.ItemId).Skip((page - 1) * limit).Take(limit).ToListAsync();
            return new PagedResult<LearningItemDto>
            {
                Data = rows.Select(ToDto).ToList(),
                Pagination = new PaginationInfo { CurrentPage = page, TotalPages = (int)Math.Ceiling(total / (double)limit), TotalItems = total, ItemsPerPage = limit },
            };
        }

        private static LearningItemDto ToDto(AgentLearningItem i) => new()
        {
            ItemId = i.ItemId, CreatedAt = i.CreatedAt, LastSeenAt = i.LastSeenAt, Occurrences = i.Occurrences,
            Source = i.Source, Agent = i.Agent, QueryText = i.QueryText, PredictedSpecialtyId = i.PredictedSpecialtyId,
            ActualSpecialtyId = i.ActualSpecialtyId, Confidence = i.Confidence, ModelVersion = i.ModelVersion,
            Status = i.Status, ApprovedSpecialtyId = i.ApprovedSpecialtyId, ReviewNote = i.ReviewNote,
            ReviewedBy = i.ReviewedBy, ReviewedAt = i.ReviewedAt, ModelVersionLearned = i.ModelVersionLearned, LearnedAt = i.LearnedAt,
        };

        public async Task<LearningItemDto?> ReviewAsync(Guid id, ReviewLearningItemRequest request, string? reviewer)
        {
            var item = await _db.AgentLearningItems.FirstOrDefaultAsync(i => i.ItemId == id);
            if (item == null) return null;
            if (item.Status != Pending)
                throw new InvalidOperationException("This item was already reviewed.");

            var now = DateTime.UtcNow;
            var action = (request.Action ?? "").Trim().ToLowerInvariant();
            if (action == "approve")
            {
                var sid = (request.SpecialtyId ?? "").Trim().ToLowerInvariant();
                if (!SymptomRouterConstants.SpecialtyIdToLabel.TryGetValue(sid, out var label))
                    throw new ArgumentException("Choose one of the router's specialties.");
                var example = new SymptomTrainingExample
                {
                    Id = Guid.NewGuid(),
                    Text = item.QueryText.Length <= 500 ? item.QueryText : item.QueryText[..500],
                    Specialist = label,
                    Type = $"Review queue: {item.Source}",
                    Source = "ReviewQueue",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = reviewer,
                };
                _db.SymptomTrainingExamples.Add(example);
                item.Status = Approved;
                item.ApprovedSpecialtyId = sid;
                item.TrainingExampleId = example.Id;
            }
            else if (action == "reject")
            {
                item.Status = Rejected;
            }
            else
            {
                throw new ArgumentException("Action must be approve or reject.");
            }
            item.ReviewNote = Clip(request.Note?.Trim(), 400);
            item.ReviewedBy = Clip(reviewer, 200);
            item.ReviewedAt = now;
            await _db.SaveChangesAsync();
            return ToDto(item);
        }

        public async Task<List<ModelVersionDto>> GetModelVersionsAsync(int limit)
        {
            limit = Math.Clamp(limit, 1, 200);
            return await _db.AgentModelVersions.AsNoTracking().OrderByDescending(v => v.BuiltAt).Take(limit)
                .Select(v => new ModelVersionDto
                {
                    Id = v.Id, ModelVersion = v.ModelVersion, PreviousVersion = v.PreviousVersion, BuiltAt = v.BuiltAt,
                    TriggerReason = v.TriggerReason, Promoted = v.Promoted, Verdict = v.Verdict, TrainingRows = v.TrainingRows,
                    CmsRows = v.CmsRows, LearnedItems = v.LearnedItems, ValidationRows = v.ValidationRows, Top1 = v.Top1,
                    Top3 = v.Top3, Answered = v.Answered, AnsweredAccuracy = v.AnsweredAccuracy, ConfidentlyWrong = v.ConfidentlyWrong,
                    Abstain = v.Abstain, Clarify = v.Clarify, EmergencyFlagRate = v.EmergencyFlagRate,
                    PrevTop1 = v.PrevTop1, PrevConfidentlyWrong = v.PrevConfidentlyWrong,
                })
                .ToListAsync();
        }
    }
}
