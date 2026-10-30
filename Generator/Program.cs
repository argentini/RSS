using System.Net;
using Generator;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: Generator");
    return 2;
}

var generatorRoot = ProjectPaths.FindGeneratorRoot();
var projectRoot = Directory.GetParent(generatorRoot)?.FullName
    ?? throw new InvalidOperationException("RSS project root was not found.");
var context = new GeneratorContext(
    generatorRoot,
    projectRoot,
    Path.Combine(generatorRoot, "Configurations"),
    Path.Combine(projectRoot, ".temp"));
var settings = GeneratorSettings.Load(
    Path.Combine(generatorRoot, "appsettings.json"));
var configurationResult = ConfigurationLoader.LoadAll(
    context,
    Console.Error);

using var webHandler = new SocketsHttpHandler
{
    AllowAutoRedirect = true,
    AutomaticDecompression = DecompressionMethods.All,
    MaxAutomaticRedirections = 10
};
using var webClient = new HttpClient(webHandler)
{
    Timeout = TimeSpan.FromMinutes(2)
};
webClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
    "AppleWebKit/537.36 (KHTML, like Gecko) " +
    "Chrome/156.0.0.0 Safari/537.36");

using var modelClient = new HttpClient
{
    BaseAddress = settings.BaseUrl,
    Timeout = Timeout.InfiniteTimeSpan
};
await using var scraper = new Agent360PageScraper(
    Console.Out,
    Console.Error);
var runner = new GeneratorRunner(
    new WebFeedClient(webClient),
    scraper,
    new LMStudioArticleHydrator(
        modelClient,
        settings,
        Console.Out),
    new GitPublisher(new ProcessCommandRunner()),
    TimeProvider.System,
    Console.Out,
    Console.Error);

try
{
    await scraper.StartAsync(cancellation.Token);
    var summary = await runner.RunAsync(
        context,
        configurationResult.Configurations,
        cancellation.Token);
    var failures = configurationResult.Failures + summary.ConfigurationFailures;

    Console.WriteLine(
        $"Finished {summary.FeedCount} feeds with " +
        $"{summary.ItemCount} items and {summary.SkippedArticles} skipped articles.");

    return failures == 0 ? 0 : 1;
}
catch (OperationCanceledException)
    when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Feed generation cancelled.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Feed generation failed: {exception.Message}");
    return 1;
}
