using System.Text.Json.Nodes;

namespace Generator;

public sealed class GeneratorRunner(
    IFeedClient feedClient,
    IPageScraper pageScraper,
    IArticleHydrator articleHydrator,
    IGitPublisher gitPublisher,
    TimeProvider timeProvider,
    IGeneratorConsole console)
{
    public async Task<RunSummary> RunAsync(
        GeneratorContext context,
        IReadOnlyList<ConfigurationFile> configurations,
        CancellationToken cancellationToken)
    {
        var destinations = new List<string>(configurations.Count);

        for (var index = 0; index < configurations.Count; index++)
            destinations.Add(configurations[index].DestinationPath);

        if (destinations.Count > 0)
            await gitPublisher.RestorePendingAsync(
                context.ProjectRoot,
                destinations,
                cancellationToken);

        var feedCount = 0;
        var itemCount = 0;
        var skippedArticles = 0;
        var configurationFailures = 0;

        for (var index = 0; index < configurations.Count; index++)
        {
            var configuration = configurations[index];
            console.BeginFeed(
                configuration.Name,
                index + 1,
                configurations.Count);

            try
            {
                var result = await ProcessConfigurationAsync(
                    context,
                    configuration,
                    cancellationToken);

                skippedArticles += result.Skipped;

                if (result.Success == false)
                {
                    configurationFailures++;
                    continue;
                }

                feedCount++;
                itemCount += result.Items;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                configurationFailures++;
                console.FeedFailed(
                    configuration.Name,
                    exception.Message);
            }
        }

        await console.RunPhaseAsync(
            GenerationPhase.Publish,
            "",
            1,
            async progress =>
            {
                if (destinations.Count == 0)
                {
                    progress.Report(1, "nothing to publish");
                    return false;
                }

                var published = await gitPublisher.PublishAsync(
                    context.ProjectRoot,
                    destinations,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                progress.Report(
                    1,
                    published
                        ? $"published {destinations.Count} feeds"
                        : "feeds unchanged");
                return published;
            });

        return new RunSummary(
            feedCount,
            itemCount,
            skippedArticles,
            configurationFailures);
    }

    private async Task<ConfigurationResult> ProcessConfigurationAsync(
        GeneratorContext context,
        ConfigurationFile configuration,
        CancellationToken cancellationToken)
    {
        var workingDirectory = await console.RunPhaseAsync(
            GenerationPhase.Prepare,
            configuration.Name,
            1,
            progress =>
            {
                var directory = PrepareWorkingDirectory(
                    context.TempRoot,
                    configuration.Name);
                progress.Report(1, "workspace ready");
                return Task.FromResult(directory);
            });

        var feedXml = await console.RunPhaseAsync(
            GenerationPhase.Download,
            configuration.Name,
            1,
            async progress =>
            {
                var xml = await feedClient.DownloadAsync(
                    new Uri(configuration.Settings.GoogleNewsUrl),
                    cancellationToken);
                var feedPath = Path.Combine(
                    workingDirectory,
                    "GoogleNews.xml");
                await File.WriteAllTextAsync(
                    feedPath,
                    xml,
                    cancellationToken);
                progress.Report(1, "feed downloaded");
                return xml;
            });
        var feedLinks = GoogleNewsParser.ParseLatestLinks(
            feedXml,
            configuration.Settings.LatestCount);
        var resolved = await console.RunPhaseAsync(
            GenerationPhase.Resolve,
            configuration.Name,
            feedLinks.Count,
            async progress =>
            {
                var articles = new List<NumberedArticle>(feedLinks.Count);
                var skipped = 0;

                for (var index = 0; index < feedLinks.Count; index++)
                {
                    var number = NumberedFiles.Format(
                        index + 1,
                        configuration.Settings.LatestCount);

                    try
                    {
                        var resolvedUrl = await feedClient.ResolveAsync(
                            feedLinks[index],
                            cancellationToken);
                        articles.Add(new NumberedArticle(number, resolvedUrl));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        skipped++;
                        progress.Warn(
                            $"Article {number} URL skipped: " +
                            exception.Message);
                    }

                    progress.Report(index + 1, $"article {number}");
                }

                var urls = new JsonObject();

                for (var index = 0; index < articles.Count; index++)
                    urls[articles[index].Number] =
                        articles[index].Url.ToString();

                await JsonFeedFiles.WriteAsync(
                    Path.Combine(workingDirectory, "URLs.json"),
                    urls,
                    cancellationToken);
                return new ArticlePhaseResult(articles, skipped);
            });
        var rendered = await console.RunPhaseAsync(
            GenerationPhase.Render,
            configuration.Name,
            resolved.Articles.Count,
            async progress =>
            {
                var articles = new List<NumberedArticle>(
                    resolved.Articles.Count);
                var skipped = 0;

                for (var index = 0; index < resolved.Articles.Count; index++)
                {
                    var article = resolved.Articles[index];

                    try
                    {
                        var html = HtmlPromptContent.Prepare(
                            await pageScraper.GetContentAsync(
                                article.Url,
                                cancellationToken));
                        await File.WriteAllTextAsync(
                            Path.Combine(
                                workingDirectory,
                                article.Number + ".html"),
                            html,
                            cancellationToken);
                        articles.Add(article);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        skipped++;
                        progress.Warn(
                            $"Article {article.Number} render skipped: " +
                            exception.Message);
                    }

                    progress.Report(
                        index + 1,
                        $"article {article.Number}");
                }

                return new ArticlePhaseResult(articles, skipped);
            });
        var hydrated = await console.RunPhaseAsync(
            GenerationPhase.Hydrate,
            configuration.Name,
            rendered.Articles.Count,
            async progress =>
            {
                var items = new List<JsonObject>(rendered.Articles.Count);
                var skipped = 0;

                for (var index = 0; index < rendered.Articles.Count; index++)
                {
                    var article = rendered.Articles[index];
                    var html = await File.ReadAllTextAsync(
                        Path.Combine(
                            workingDirectory,
                            article.Number + ".html"),
                        cancellationToken);

                    try
                    {
                        var item = await HydrateWithRetryAsync(
                            article,
                            html,
                            progress,
                            index,
                            cancellationToken);
                        await JsonFeedFiles.WriteAsync(
                            Path.Combine(
                                workingDirectory,
                                article.Number + ".json"),
                            item,
                            cancellationToken);
                        items.Add(item);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        skipped++;
                        progress.Warn(
                            $"Article {article.Number} generation skipped: " +
                            exception.Message);
                    }

                    progress.Report(
                        index + 1,
                        $"article {article.Number}");
                }

                return new HydrationPhaseResult(items, skipped);
            });
        var skippedArticles = resolved.Skipped +
            rendered.Skipped + hydrated.Skipped;

        var feedResult = await console.RunPhaseAsync(
            GenerationPhase.Write,
            configuration.Name,
            1,
            async progress =>
            {
                if (hydrated.Items.Count == 0)
                {
                    progress.Warn(
                        "No articles succeeded; existing feed preserved.");
                    progress.Report(1, "existing feed preserved");
                    return new FeedBuildResult(
                        null,
                        "existing feed preserved");
                }

                var result = JsonFeedFiles.BuildFeed(
                    configuration.Settings,
                    hydrated.Items,
                    configuration.DestinationPath);

                if (result.Feed is not JsonObject feed)
                {
                    progress.Warn(result.Message);
                    progress.Report(1, "existing feed preserved");
                    return result;
                }

                await JsonFeedFiles.WriteFeedAtomicAsync(
                    configuration.DestinationPath,
                    feed,
                    cancellationToken);
                progress.Report(1, result.Message);
                return result;
            });

        return new ConfigurationResult(
            feedResult.Success,
            feedResult.ItemCount,
            skippedArticles);
    }

    private async Task<JsonObject> HydrateWithRetryAsync(
        NumberedArticle article,
        string html,
        IPhaseProgress progress,
        int completed,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            progress.Report(
                completed,
                $"article {article.Number}, attempt {attempt}/2");

            try
            {
                return await articleHydrator.HydrateAsync(
                    html,
                    article.Url,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        throw lastError
            ?? new InvalidOperationException("Article generation failed.");
    }

    private static string PrepareWorkingDirectory(
        string tempRoot,
        string configurationName)
    {
        var root = Path.GetFullPath(tempRoot);
        var workingDirectory = Path.GetFullPath(
            Path.Combine(root, configurationName));
        var prefix = Path.TrimEndingDirectorySeparator(root) +
            Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (workingDirectory.StartsWith(prefix, comparison) == false)
            throw new InvalidOperationException(
                "Working directory escaped the temporary root.");

        Directory.CreateDirectory(root);

        if (Directory.Exists(workingDirectory))
            Directory.Delete(workingDirectory, true);

        Directory.CreateDirectory(workingDirectory);
        return workingDirectory;
    }

    private sealed record ArticlePhaseResult(
        IReadOnlyList<NumberedArticle> Articles,
        int Skipped);

    private sealed record HydrationPhaseResult(
        IReadOnlyList<JsonObject> Items,
        int Skipped);

    private sealed record ConfigurationResult(
        bool Success,
        int Items,
        int Skipped);
}
