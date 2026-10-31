using System.Text.Json;
using Generator;

namespace Generator.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "rss-generator-tests",
            Guid.NewGuid().ToString("N"));
        GeneratorRoot = Path.Combine(Root, "Generator");
        ConfigurationRoot = Path.Combine(
            GeneratorRoot,
            "Configurations");
        TempRoot = Path.Combine(Root, ".temp");
        Directory.CreateDirectory(ConfigurationRoot);
    }

    public string Root { get; }
    public string GeneratorRoot { get; }
    public string ConfigurationRoot { get; }
    public string TempRoot { get; }

    public GeneratorContext Context => new(
        GeneratorRoot,
        Root,
        ConfigurationRoot,
        TempRoot);

    public FeedConfiguration CreateSettings(
        string destinationFile = "feed.json",
        int latestCount = 2,
        string feedName = "Test Feed")
    {
        return new FeedConfiguration
        {
            GoogleNewsUrl = "https://news.google.test/rss",
            LatestCount = latestCount,
            DestinationRssFilePath = "../" + destinationFile,
            FeedName = feedName,
            FeedHomePageUrl = "https://source.test/",
            FeedUrl = "https://feeds.test/" + destinationFile,
            FeedIconUrl = "https://source.test/icon.png",
            FeedFaviconUrl = "https://source.test/favicon.ico"
        };
    }

    public string WriteConfiguration(
        string name,
        FeedConfiguration settings)
    {
        var path = Path.Combine(ConfigurationRoot, name + ".json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(settings));
        return path;
    }

    public ConfigurationFile CreateConfiguration(
        string name,
        string destinationFile = "feed.json",
        int latestCount = 2)
    {
        var settings = CreateSettings(
            destinationFile,
            latestCount,
            name);
        var path = WriteConfiguration(name, settings);
        return ConfigurationLoader.Load(path, Context);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
    }
}

internal sealed class RecordingGeneratorConsole : IGeneratorConsole
{
    public List<GenerationPhase> Phases { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Events { get; } = [];
    public TaskCompletionSource NextRunShown { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public int OverlapCancellationCount { get; private set; }
    public int ScheduleFailureCount { get; private set; }

    public void ShowStartup(
        IReadOnlyList<ScheduleItem> schedule,
        TimeZoneInfo timeZone,
        int feedCount,
        string model)
    {
        Events.Add("startup");
    }

    public async Task RunActivityAsync(
        string title,
        Func<Task> action)
    {
        Events.Add("activity:" + title);
        await action();
    }

    public void ShowNextRun(
        DateTimeOffset localTime,
        TimeSpan remaining)
    {
        Events.Add("next-run");
        NextRunShown.TrySetResult();
    }

    public void ScheduleItemStarting()
    {
        Events.Add("schedule-started");
    }

    public void ScheduleItemFinished()
    {
        Events.Add("schedule-finished");
    }

    public void ScheduleItemCancelled()
    {
        Events.Add("schedule-cancelled");
    }

    public void ScheduleItemFailed(string message)
    {
        ScheduleFailureCount++;
        Events.Add("schedule-failed:" + message);
    }

    public void CancellingOverlappingRun()
    {
        OverlapCancellationCount++;
        Events.Add("overlap-cancelled");
    }

    public void ApplicationStopped()
    {
        Events.Add("stopped");
    }

    public void FatalError(string message)
    {
        Events.Add("fatal:" + message);
    }

    public void Warning(string message)
    {
        Warnings.Add(message);
    }

    public void BeginScheduleItem(
        IReadOnlyList<ConfigurationFile> configurations)
    {
        Events.Add("run-started");
    }

    public void BeginFeed(string name, int index, int total)
    {
        Events.Add("feed:" + name);
    }

    public async Task<T> RunPhaseAsync<T>(
        GenerationPhase phase,
        string scope,
        int units,
        Func<IPhaseProgress, Task<T>> action)
    {
        Phases.Add(phase);
        return await action(new RecordingPhaseProgress(Warnings));
    }

    public void FeedFailed(string name, string message)
    {
        Events.Add("feed-failed:" + name + ":" + message);
    }

    public void CompleteScheduleItem(
        RunSummary summary,
        int failures)
    {
        Events.Add("run-finished");
    }

    private sealed class RecordingPhaseProgress(
        List<string> warnings) : IPhaseProgress
    {
        public void Report(int completed, string? detail = null)
        {
        }

        public void Warn(string message)
        {
            warnings.Add(message);
        }
    }
}
