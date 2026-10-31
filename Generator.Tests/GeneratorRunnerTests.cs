using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generator.Tests;

[TestClass]
public sealed class GeneratorRunnerTests
{
    [TestMethod]
    public async Task RunAsync_ArticleFailure_WritesSuccessfulArtifactsAndFeed()
    {
        using var workspace = new TestWorkspace();
        var configuration = workspace.CreateConfiguration("FeedOne");
        WriteExistingFeed(
            configuration,
            CreateItem(new Uri("https://source.test/prior-one")),
            CreateItem(new Uri("https://source.test/prior-two")));
        var oldWorkingDirectory = Path.Combine(
            workspace.TempRoot,
            configuration.Name);
        Directory.CreateDirectory(oldWorkingDirectory);
        var stalePath = Path.Combine(oldWorkingDirectory, "stale.txt");
        File.WriteAllText(stalePath, "stale");
        var publisher = new RecordingGitPublisher();
        var console = new RecordingGeneratorConsole();
        var runner = CreateRunner(
            CreateFeedClient("first", "second"),
            new DelegatePageScraper(url =>
                url.AbsolutePath.EndsWith(
                    "second",
                    StringComparison.Ordinal)
                    ? throw new HttpRequestException("blocked")
                    : "<html><body>First</body></html>"),
            publisher,
            console);

        var summary = await runner.RunAsync(
            workspace.Context,
            [configuration],
            CancellationToken.None);

        Assert.AreEqual(1, summary.FeedCount);
        Assert.AreEqual(2, summary.ItemCount);
        Assert.AreEqual(1, summary.SkippedArticles);
        Assert.AreEqual(0, summary.ConfigurationFailures);
        Assert.IsFalse(File.Exists(stalePath));
        var workingDirectory = Path.Combine(
            workspace.TempRoot,
            configuration.Name);
        Assert.IsTrue(File.Exists(
            Path.Combine(workingDirectory, "GoogleNews.xml")));
        Assert.IsTrue(File.Exists(
            Path.Combine(workingDirectory, "URLs.json")));
        Assert.IsTrue(File.Exists(
            Path.Combine(workingDirectory, "01.html")));
        Assert.IsFalse(File.Exists(
            Path.Combine(workingDirectory, "02.html")));
        Assert.IsTrue(File.Exists(
            Path.Combine(workingDirectory, "01.json")));
        var files = Directory.GetFiles(workingDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "01.html",
                "01.json",
                "GoogleNews.xml",
                "URLs.json"
            },
            files);
        using var urls = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(workingDirectory, "URLs.json")));
        Assert.AreEqual(
            "https://source.test/first",
            urls.RootElement.GetProperty("01").GetString());
        Assert.AreEqual(
            "https://source.test/second",
            urls.RootElement.GetProperty("02").GetString());
        using var feed = JsonDocument.Parse(
            File.ReadAllText(configuration.DestinationPath));
        Assert.AreEqual(
            "https://jsonfeed.org/version/1.1",
            feed.RootElement.GetProperty("version").GetString());
        Assert.AreEqual(
            configuration.Settings.FeedName,
            feed.RootElement.GetProperty("title").GetString());
        Assert.AreEqual(
            configuration.Settings.FeedHomePageUrl,
            feed.RootElement.GetProperty("home_page_url").GetString());
        Assert.AreEqual(
            configuration.Settings.FeedUrl,
            feed.RootElement.GetProperty("feed_url").GetString());
        Assert.AreEqual(
            configuration.Settings.FeedIconUrl,
            feed.RootElement.GetProperty("icon").GetString());
        Assert.AreEqual(
            configuration.Settings.FeedFaviconUrl,
            feed.RootElement.GetProperty("favicon").GetString());
        Assert.AreEqual(
            2,
            feed.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(1, publisher.CallCount);
        Assert.HasCount(1, publisher.Destinations);
        Assert.AreEqual(
            configuration.DestinationPath,
            publisher.Destinations[0]);
        CollectionAssert.AreEqual(
            new[]
            {
                GenerationPhase.Prepare,
                GenerationPhase.Download,
                GenerationPhase.Resolve,
                GenerationPhase.Render,
                GenerationPhase.Hydrate,
                GenerationPhase.Write,
                GenerationPhase.Publish
            },
            console.Phases);
        Assert.HasCount(1, console.Warnings);
        StringAssert.Contains(
            console.Warnings[0],
            "Article 02 render skipped");
    }

    [TestMethod]
    public async Task RunAsync_AllArticlesFail_PreservesExistingFeed()
    {
        using var workspace = new TestWorkspace();
        var configuration = workspace.CreateConfiguration("FeedOne");
        const string existingFeed = "existing feed content";
        File.WriteAllText(configuration.DestinationPath, existingFeed);
        var publisher = new RecordingGitPublisher();
        var runner = CreateRunner(
            CreateFeedClient("first", "second"),
            new DelegatePageScraper(_ =>
                throw new HttpRequestException("blocked")),
            publisher,
            new RecordingGeneratorConsole());

        var summary = await runner.RunAsync(
            workspace.Context,
            [configuration],
            CancellationToken.None);

        Assert.AreEqual(0, summary.FeedCount);
        Assert.AreEqual(0, summary.ItemCount);
        Assert.AreEqual(2, summary.SkippedArticles);
        Assert.AreEqual(1, summary.ConfigurationFailures);
        Assert.AreEqual(
            existingFeed,
            File.ReadAllText(configuration.DestinationPath));
        Assert.AreEqual(0, publisher.CallCount);
    }

    [TestMethod]
    public async Task RunAsync_MultipleConfigurations_PublishesOnceAfterBothFeeds()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.CreateConfiguration(
            "FirstFeed",
            "first.json",
            1);
        var second = workspace.CreateConfiguration(
            "SecondFeed",
            "second.json",
            1);
        var publisher = new RecordingGitPublisher
        {
            BeforePublish = paths =>
            {
                for (var index = 0; index < paths.Count; index++)
                    Assert.IsTrue(File.Exists(paths[index]));
            }
        };
        var runner = CreateRunner(
            CreateFeedClient("article"),
            new DelegatePageScraper(_ => "<html>Article</html>"),
            publisher,
            new RecordingGeneratorConsole());

        var summary = await runner.RunAsync(
            workspace.Context,
            [first, second],
            CancellationToken.None);

        Assert.AreEqual(2, summary.FeedCount);
        Assert.AreEqual(1, publisher.CallCount);
        Assert.HasCount(2, publisher.Destinations);
    }

    [TestMethod]
    public async Task RunAsync_HydrationFailure_SkipsOnlyFailedArticle()
    {
        using var workspace = new TestWorkspace();
        var configuration = workspace.CreateConfiguration("FeedOne");
        WriteExistingFeed(
            configuration,
            CreateItem(new Uri("https://source.test/prior-one")),
            CreateItem(new Uri("https://source.test/prior-two")));
        var publisher = new RecordingGitPublisher();
        var hydrator = new DelegateArticleHydrator((_, sourceUrl) =>
        {
            if (sourceUrl.AbsolutePath.EndsWith(
                "second",
                StringComparison.Ordinal))
                throw new JsonException("invalid model output");

            return CreateItem(sourceUrl);
        });
        var runner = CreateRunner(
            CreateFeedClient("first", "second"),
            new DelegatePageScraper(_ => "<html>Article</html>"),
            publisher,
            new RecordingGeneratorConsole(),
            hydrator);

        var summary = await runner.RunAsync(
            workspace.Context,
            [configuration],
            CancellationToken.None);

        Assert.AreEqual(2, summary.ItemCount);
        Assert.AreEqual(1, summary.SkippedArticles);
        var workingDirectory = Path.Combine(
            workspace.TempRoot,
            configuration.Name);
        Assert.IsTrue(File.Exists(
            Path.Combine(workingDirectory, "02.html")));
        Assert.IsFalse(File.Exists(
            Path.Combine(workingDirectory, "02.json")));
        Assert.AreEqual(1, publisher.CallCount);
    }

    [TestMethod]
    public async Task RunAsync_OneFreshItem_MergesPriorItemsAndPreservesDepth()
    {
        using var workspace = new TestWorkspace();
        var configuration = workspace.CreateConfiguration("FeedOne");
        WriteExistingFeed(
            configuration,
            CreateLegacyItem(new Uri("https://source.test/first")),
            CreateLegacyItem(new Uri("https://source.test/older-one")),
            CreateLegacyItem(new Uri("https://source.test/older-two")));
        var publisher = new RecordingGitPublisher();
        var runner = CreateRunner(
            CreateFeedClient("first", "second"),
            new DelegatePageScraper(url =>
                url.AbsolutePath.EndsWith(
                    "second",
                    StringComparison.Ordinal)
                    ? throw new HttpRequestException("blocked")
                    : "<html>Article</html>"),
            publisher,
            new RecordingGeneratorConsole());

        var summary = await runner.RunAsync(
            workspace.Context,
            [configuration],
            CancellationToken.None);

        using var feed = JsonDocument.Parse(
            File.ReadAllText(configuration.DestinationPath));
        var items = feed.RootElement.GetProperty("items");
        Assert.AreEqual(1, summary.FeedCount);
        Assert.AreEqual(3, summary.ItemCount);
        Assert.AreEqual(3, items.GetArrayLength());
        Assert.AreEqual(
            "https://source.test/first",
            items[0].GetProperty("id").GetString());
        Assert.AreEqual(
            "https://source.test/older-one",
            items[1].GetProperty("id").GetString());
        Assert.AreEqual(
            "https://source.test/older-two",
            items[2].GetProperty("id").GetString());
        Assert.AreEqual(
            JsonValueKind.Object,
            items[1].GetProperty("authors")[0].ValueKind);
        Assert.AreEqual(
            "Writer",
            items[1].GetProperty("authors")[0]
                .GetProperty("name")
                .GetString());
        Assert.AreEqual(1, publisher.CallCount);
    }

    [TestMethod]
    public async Task RunAsync_ShallowCandidate_DoesNotCreateDestination()
    {
        using var workspace = new TestWorkspace();
        var configuration = workspace.CreateConfiguration("FeedOne");
        var publisher = new RecordingGitPublisher();
        var console = new RecordingGeneratorConsole();
        var runner = CreateRunner(
            CreateFeedClient("first"),
            new DelegatePageScraper(_ => "<html>Article</html>"),
            publisher,
            console);

        var summary = await runner.RunAsync(
            workspace.Context,
            [configuration],
            CancellationToken.None);

        Assert.AreEqual(0, summary.FeedCount);
        Assert.AreEqual(0, summary.ItemCount);
        Assert.AreEqual(1, summary.ConfigurationFailures);
        Assert.IsFalse(File.Exists(configuration.DestinationPath));
        Assert.AreEqual(0, publisher.CallCount);
        Assert.HasCount(1, console.Warnings);
        StringAssert.Contains(console.Warnings[0], "2 required");
    }

    [TestMethod]
    public async Task RunAsync_InvalidExistingFeed_PreservesDestination()
    {
        using var workspace = new TestWorkspace();
        var configuration = workspace.CreateConfiguration("FeedOne");
        const string existingFeed = "invalid existing feed";
        File.WriteAllText(configuration.DestinationPath, existingFeed);
        var publisher = new RecordingGitPublisher();
        var runner = CreateRunner(
            CreateFeedClient("first", "second"),
            new DelegatePageScraper(_ => "<html>Article</html>"),
            publisher,
            new RecordingGeneratorConsole());

        var summary = await runner.RunAsync(
            workspace.Context,
            [configuration],
            CancellationToken.None);

        Assert.AreEqual(0, summary.FeedCount);
        Assert.AreEqual(1, summary.ConfigurationFailures);
        Assert.AreEqual(
            existingFeed,
            File.ReadAllText(configuration.DestinationPath));
        Assert.AreEqual(0, publisher.CallCount);
    }

    [TestMethod]
    public async Task RunAsync_RenderedArticle_CleansHtmlBeforeSavingAndHydration()
    {
        using var workspace = new TestWorkspace();
        var configuration = workspace.CreateConfiguration("FeedOne", latestCount: 1);
        string? hydratedHtml = null;
        var hydrator = new DelegateArticleHydrator((html, sourceUrl) =>
        {
            hydratedHtml = html;
            return CreateItem(sourceUrl);
        });
        var runner = CreateRunner(
            CreateFeedClient("article"),
            new DelegatePageScraper(_ => """
                <article>
                    <p>Useful body.</p>
                    <script>script marker</script>
                </article>
                """),
            new RecordingGitPublisher(),
            new RecordingGeneratorConsole(),
            hydrator);

        await runner.RunAsync(
            workspace.Context,
            [configuration],
            CancellationToken.None);

        var savedHtml = File.ReadAllText(Path.Combine(
            workspace.TempRoot,
            configuration.Name,
            "01.html"));
        Assert.AreEqual(savedHtml, hydratedHtml);
        StringAssert.Contains(savedHtml, "Useful body.");
        Assert.IsFalse(savedHtml.Contains("script marker", StringComparison.Ordinal));
        Assert.IsFalse(savedHtml.Contains("<script", StringComparison.OrdinalIgnoreCase));
    }

    private static GeneratorRunner CreateRunner(
        IFeedClient feedClient,
        IPageScraper scraper,
        IGitPublisher publisher,
        IGeneratorConsole console,
        IArticleHydrator? articleHydrator = null)
    {
        return new GeneratorRunner(
            feedClient,
            scraper,
            articleHydrator ?? new DelegateArticleHydrator(
                (_, sourceUrl) => CreateItem(sourceUrl)),
            publisher,
            new FixedTimeProvider(
                new DateTimeOffset(
                    2026,
                    10,
                    30,
                    11,
                    53,
                    47,
                    TimeSpan.Zero)),
            console);
    }

    private static JsonObject CreateItem(Uri sourceUrl)
    {
        return new JsonObject
        {
            ["title"] = "Hydrated article",
            ["date_published"] = "2026-10-30T11:53:47Z",
            ["id"] = sourceUrl.ToString(),
            ["url"] = sourceUrl.ToString(),
            ["external_url"] = sourceUrl.ToString(),
            ["authors"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Writer"
                }
            },
            ["content_html"] = "<p>Hydrated body.</p>"
        };
    }

    private static JsonObject CreateLegacyItem(Uri sourceUrl)
    {
        var item = CreateItem(sourceUrl);
        item["authors"] = new JsonArray("Writer");
        return item;
    }

    private static void WriteExistingFeed(
        ConfigurationFile configuration,
        params JsonObject[] items)
    {
        File.WriteAllText(
            configuration.DestinationPath,
            JsonFeedFiles.CreateFeed(configuration.Settings, items)
                .ToJsonString(JsonFeedFiles.SerializerOptions));
    }

    private static IFeedClient CreateFeedClient(params string[] names)
    {
        var items = new StringWriter();
        items.Write("<rss><channel>");

        for (var index = 0; index < names.Length; index++)
            items.Write(
                $"<item><link>https://news.test/{names[index]}</link></item>");

        items.Write("</channel></rss>");
        return new FakeFeedClient(items.ToString());
    }

    private sealed class FakeFeedClient(string feed) : IFeedClient
    {
        public Task<string> DownloadAsync(
            Uri url,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(feed);
        }

        public Task<Uri> ResolveAsync(
            Uri url,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new Uri(
                "https://source.test" + url.AbsolutePath));
        }
    }

    private sealed class DelegatePageScraper(
        Func<Uri, string> scrape) : IPageScraper
    {
        public Task<string> GetContentAsync(
            Uri url,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(scrape(url));
        }
    }

    private sealed class DelegateArticleHydrator(
        Func<string, Uri, JsonObject> hydrate) : IArticleHydrator
    {
        public Task<JsonObject> HydrateAsync(
            string html,
            Uri sourceUrl,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(hydrate(html, sourceUrl));
        }
    }

    private sealed class RecordingGitPublisher : IGitPublisher
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string> Destinations { get; private set; } = [];
        public Action<IReadOnlyList<string>>? BeforePublish { get; init; }

        public Task<bool> PublishAsync(
            string projectRoot,
            IReadOnlyList<string> destinationPaths,
            DateTimeOffset timestamp,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Destinations = destinationPaths.ToArray();
            BeforePublish?.Invoke(Destinations);
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return timestamp;
        }
    }
}
