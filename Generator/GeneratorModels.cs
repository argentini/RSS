using System.Text.Json.Nodes;

namespace Generator;

public sealed record GeneratorContext(
    string GeneratorRoot,
    string ProjectRoot,
    string ConfigurationRoot,
    string TempRoot);

public sealed record ConfigurationFile(
    string Name,
    string Path,
    FeedConfiguration Settings,
    string DestinationPath);

public sealed class FeedConfiguration
{
    public string GoogleNewsUrl { get; init; } = "";
    public int LatestCount { get; init; }
    public string DestinationRssFilePath { get; init; } = "";
    public string FeedName { get; init; } = "";
    public string FeedHomePageUrl { get; init; } = "";
    public string FeedUrl { get; init; } = "";
    public string FeedIconUrl { get; init; } = "";
    public string FeedFaviconUrl { get; init; } = "";
}

public sealed record ConfigurationLoadResult(
    IReadOnlyList<ConfigurationFile> Configurations,
    int Failures);

public sealed record NumberedArticle(
    string Number,
    Uri Url);

public sealed record RunSummary(
    int FeedCount,
    int ItemCount,
    int SkippedArticles,
    int ConfigurationFailures);

public interface IFeedClient
{
    Task<string> DownloadAsync(
        Uri url,
        CancellationToken cancellationToken);

    Task<Uri> ResolveAsync(
        Uri url,
        CancellationToken cancellationToken);
}

public interface IPageScraper
{
    Task<string> GetContentAsync(
        Uri url,
        CancellationToken cancellationToken);
}

public interface IArticleHydrator
{
    Task<JsonObject> HydrateAsync(
        string html,
        Uri sourceUrl,
        CancellationToken cancellationToken);
}

public interface IGitPublisher
{
    Task<bool> PublishAsync(
        string projectRoot,
        IReadOnlyList<string> destinationPaths,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken);
}
