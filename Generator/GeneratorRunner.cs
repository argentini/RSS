using System.Text.Json.Nodes;

namespace Generator;

public sealed class GeneratorRunner(
    IFeedClient feedClient,
    IPageScraper pageScraper,
    IArticleHydrator articleHydrator,
    IGitPublisher gitPublisher,
    TimeProvider timeProvider,
    TextWriter output,
    TextWriter error)
{
    public async Task<RunSummary> RunAsync(
        GeneratorContext context,
        IReadOnlyList<ConfigurationFile> configurations,
        CancellationToken cancellationToken)
    {
        var destinations = new List<string>(configurations.Count);
        var feedCount = 0;
        var itemCount = 0;
        var skippedArticles = 0;
        var configurationFailures = 0;

        for (var index = 0; index < configurations.Count; index++)
        {
            var configuration = configurations[index];
            output.WriteLine();
            output.WriteLine(
                $"=== {configuration.Name} " +
                $"({index + 1}/{configurations.Count}) ===");

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
                destinations.Add(configuration.DestinationPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                configurationFailures++;
                error.WriteLine(
                    $"Configuration {configuration.Name} failed: " +
                    exception.Message);
            }
        }

        output.WriteLine();

        if (destinations.Count == 0)
        {
            output.WriteLine("[Final] No generated feeds require publishing.");
        }
        else
        {
            output.WriteLine(
                $"[Final] Publishing {destinations.Count} generated feed files.");
            var published = await gitPublisher.PublishAsync(
                context.ProjectRoot,
                destinations,
                timeProvider.GetUtcNow(),
                cancellationToken);
            output.WriteLine(
                published
                    ? "[Final] Git commit created and pushed."
                    : "[Final] Feed files are unchanged; Git skipped.");
        }

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
        output.WriteLine("[Step 1/6] Preparing clean working directory.");
        var workingDirectory = PrepareWorkingDirectory(
            context.TempRoot,
            configuration.Name);
        output.WriteLine($"[Step 1/6] Ready: {workingDirectory}");

        output.WriteLine("[Step 2/6] Downloading Google News feed.");
        var feedXml = await feedClient.DownloadAsync(
            new Uri(configuration.Settings.GoogleNewsUrl),
            cancellationToken);
        var feedPath = Path.Combine(workingDirectory, "GoogleNews.xml");
        await File.WriteAllTextAsync(
            feedPath,
            feedXml,
            cancellationToken);
        output.WriteLine($"[Step 2/6] Saved: {feedPath}");

        output.WriteLine("[Step 3/6] Resolving source article URLs.");
        var feedLinks = GoogleNewsParser.ParseLatestLinks(
            feedXml,
            configuration.Settings.LatestCount);
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
                output.WriteLine(
                    $"[Step 3/6] {number}: {resolvedUrl}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                skipped++;
                error.WriteLine(
                    $"[Step 3/6] Skipped {number}: {exception.Message}");
            }
        }

        var urls = new JsonObject();

        for (var index = 0; index < articles.Count; index++)
            urls[articles[index].Number] = articles[index].Url.ToString();

        var urlsPath = Path.Combine(workingDirectory, "URLs.json");
        await JsonFeedFiles.WriteAsync(
            urlsPath,
            urls,
            cancellationToken);
        output.WriteLine($"[Step 3/6] Saved: {urlsPath}");

        output.WriteLine("[Step 4/6] Rendering article DOM content.");
        var scrapedArticles = new List<NumberedArticle>(articles.Count);

        for (var index = 0; index < articles.Count; index++)
        {
            var article = articles[index];

            try
            {
                output.WriteLine(
                    $"[Step 4/6] {article.Number}: Rendering {article.Url}");
                var html = await pageScraper.GetContentAsync(
                    article.Url,
                    cancellationToken);
                var htmlPath = Path.Combine(
                    workingDirectory,
                    article.Number + ".html");

                await File.WriteAllTextAsync(
                    htmlPath,
                    html,
                    cancellationToken);
                scrapedArticles.Add(article);
                output.WriteLine(
                    $"[Step 4/6] {article.Number}: Saved {htmlPath}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                skipped++;
                error.WriteLine(
                    $"[Step 4/6] Skipped {article.Number}: " +
                    exception.Message);
            }
        }

        output.WriteLine("[Step 5/6] Hydrating RSS item JSON.");
        var items = new List<JsonObject>(scrapedArticles.Count);

        for (var index = 0; index < scrapedArticles.Count; index++)
        {
            var article = scrapedArticles[index];
            var htmlPath = Path.Combine(
                workingDirectory,
                article.Number + ".html");
            var html = await File.ReadAllTextAsync(
                htmlPath,
                cancellationToken);

            try
            {
                var item = await HydrateWithRetryAsync(
                    article,
                    html,
                    cancellationToken);
                var itemPath = Path.Combine(
                    workingDirectory,
                    article.Number + ".json");

                await JsonFeedFiles.WriteAsync(
                    itemPath,
                    item,
                    cancellationToken);
                items.Add(item);
                output.WriteLine(
                    $"[Step 5/6] {article.Number}: Saved {itemPath}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                skipped++;
                error.WriteLine(
                    $"[Step 5/6] Skipped {article.Number}: " +
                    exception.Message);
            }
        }

        if (items.Count == 0)
        {
            error.WriteLine(
                "[Step 6/6] No articles succeeded; existing feed preserved.");
            return new ConfigurationResult(false, 0, skipped);
        }

        output.WriteLine("[Step 6/6] Building final JSON feed.");
        var feed = JsonFeedFiles.CreateFeed(
            configuration.Settings,
            items);
        await JsonFeedFiles.WriteAtomicAsync(
            configuration.DestinationPath,
            feed,
            cancellationToken);
        output.WriteLine(
            $"[Step 6/6] Saved {items.Count} items: " +
            configuration.DestinationPath);

        return new ConfigurationResult(true, items.Count, skipped);
    }

    private async Task<JsonObject> HydrateWithRetryAsync(
        NumberedArticle article,
        string html,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            output.WriteLine(
                $"[Step 5/6] {article.Number}: " +
                $"Streaming model attempt {attempt}/2.");

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

                if (attempt < 2)
                    error.WriteLine(
                        $"[Step 5/6] {article.Number}: " +
                        $"Retrying after {exception.Message}");
            }
        }

        throw lastError
            ?? new InvalidOperationException("Article hydration failed.");
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

    private sealed record ConfigurationResult(
        bool Success,
        int Items,
        int Skipped);
}
