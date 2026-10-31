using System.Net;
using Generator;
using Spectre.Console;

using var shutdown = new CancellationTokenSource();
var exitCode = 0;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Interlocked.Exchange(ref exitCode, 130);
    shutdown.Cancel();
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
var gitHubCredentials = GitHubCredentialsLoader.Load(
    Path.Combine(projectRoot, "Credentials.json"));
using var configurationErrors = new StringWriter();
var configurationResult = ConfigurationLoader.LoadAll(
    context,
    configurationErrors);
var appConsole = new GeneratorConsole(
    AnsiConsole.Console,
    TimingHistory.Load(Path.Combine(context.TempRoot, "timings.json")),
    TimeProvider.System);
appConsole.ShowStartup(
    settings.Schedule,
    TimeProvider.System.LocalTimeZone,
    configurationResult.Configurations.Count,
    settings.Model);

if (string.IsNullOrWhiteSpace(configurationErrors.ToString()) == false)
    appConsole.Warning(configurationErrors.ToString().Trim());

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
await using var scraper = new Agent360PageScraper();
var runner = new GeneratorRunner(
    new WebFeedClient(webClient),
    scraper,
    new LMStudioArticleHydrator(
        modelClient,
        settings),
    new GitPublisher(
        new ProcessCommandRunner(),
        gitHubCredentials),
    TimeProvider.System,
    appConsole);

try
{
    var keyReader = new SystemConsoleKeyReader();
    var now = TimeProvider.System.GetUtcNow();
    var nextRun = GeneratorScheduler.GetNextOccurrence(
        settings.Schedule,
        now,
        TimeProvider.System.LocalTimeZone);
    var nextRunLocal = TimeZoneInfo.ConvertTime(
        nextRun,
        TimeProvider.System.LocalTimeZone);
    var runImmediately = StartupRunPrompt.Ask(
        keyReader,
        Console.Out,
        () => appConsole.ShowNextRun(
            nextRunLocal,
            nextRun > now ? nextRun - now : TimeSpan.Zero),
        shutdown);

    if (shutdown.IsCancellationRequested)
    {
        appConsole.ApplicationStopped();
        return Volatile.Read(ref exitCode);
    }

    new ConsoleExitMonitor(keyReader).Start(shutdown);
    await appConsole.RunActivityAsync(
        "Connect browser automation",
        () => scraper.StartAsync(shutdown.Token));
    var scheduler = new GeneratorScheduler(
        TimeProvider.System,
        appConsole);

    await scheduler.RunAsync(
        settings.Schedule,
        async cancellationToken =>
        {
            appConsole.BeginScheduleItem(
                configurationResult.Configurations);
            var summary = await runner.RunAsync(
                context,
                configurationResult.Configurations,
                cancellationToken);
            var failures = configurationResult.Failures +
                summary.ConfigurationFailures;
            appConsole.CompleteScheduleItem(summary, failures);
        },
        runImmediately,
        shutdown.Token);

    appConsole.ApplicationStopped();
    return Volatile.Read(ref exitCode);
}
catch (OperationCanceledException)
    when (shutdown.IsCancellationRequested)
{
    appConsole.ApplicationStopped();
    return Volatile.Read(ref exitCode);
}
catch (Exception exception)
{
    appConsole.FatalError(exception.Message);
    return 1;
}
