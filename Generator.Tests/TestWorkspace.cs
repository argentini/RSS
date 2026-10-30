using System.Text.Json;

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
