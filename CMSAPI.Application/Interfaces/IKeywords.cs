using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

// CMS → Marketing → Keywords. Collection: KeywordCollector (daily, KeywordWorker). Screens: KeywordsService.

public interface IKeywordCollector
{
    /// <summary>Collects from every source (or only <paramref name="only"/>: site, gsc, ads, planner). Returns rows read per source.</summary>
    Task<Dictionary<string, int>> RunAsync(string? only = null, CancellationToken ct = default);
}

public interface IKeywordsService
{
    Task<KeywordOverviewDto> OverviewAsync(string? site, CancellationToken ct = default);
    Task<KeywordPage> ListAsync(KeywordQuery query, CancellationToken ct = default);
    Task<WorkResult<KeywordDetailDto>> DetailAsync(Guid keywordId, CancellationToken ct = default);
    Task<WorkResult<KeywordDetailDto>> UpdateAsync(KeywordActor actor, Guid keywordId, KeywordUpdate update, CancellationToken ct = default);

    Task<List<KeywordTopicRowDto>> TopicsAsync(string? site, string? kind, CancellationToken ct = default);
    Task<WorkResult<KeywordTopicDetailDto>> TopicAsync(Guid topicId, CancellationToken ct = default);
    Task<WorkResult<KeywordTopicDetailDto>> CreateTopicAsync(KeywordActor actor, KeywordTopicSave save, CancellationToken ct = default);
    Task<WorkResult<KeywordTopicDetailDto>> UpdateTopicAsync(KeywordActor actor, Guid topicId, KeywordTopicSave save, CancellationToken ct = default);
    Task<WorkResult<KeywordTopicDetailDto>> MergeTopicAsync(KeywordActor actor, Guid topicId, Guid intoTopicId, CancellationToken ct = default);

    Task<List<KeywordContentDto>> ContentAsync(string? site, string? kind, CancellationToken ct = default);
    Task<WorkResult<KeywordContentDto>> CreateContentAsync(KeywordActor actor, KeywordContentSave save, CancellationToken ct = default);
    Task<WorkResult<KeywordContentDto>> UpdateContentAsync(KeywordActor actor, Guid contentId, KeywordContentSave save, CancellationToken ct = default);
    Task<WorkResult<bool>> DeleteContentAsync(KeywordActor actor, Guid contentId, CancellationToken ct = default);

    Task<KeywordSetupDto> SetupAsync(KeywordActor actor, CancellationToken ct = default);
    Task<WorkResult<KeywordSetupDto>> SaveSetupAsync(KeywordActor actor, KeywordSetupSave save, CancellationToken ct = default);
}
