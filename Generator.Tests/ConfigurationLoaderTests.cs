using System.Text.Json;

namespace Generator.Tests;

[TestClass]
public sealed class ConfigurationLoaderTests
{
    [TestMethod]
    public void Load_IndexMode_RequiresOnlyIndexSource()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteConfiguration("Index", workspace.CreateSettings(
            useIndexPage: true,
            basePageUrl: "https://source.test/news/",
            googleNewsUrl: ""));

        var configuration = ConfigurationLoader.Load(path, workspace.Context);

        Assert.IsTrue(configuration.Settings.UseIndexPage);
        Assert.AreEqual("https://source.test/news/", configuration.Settings.BasePageUrl);
        Assert.AreEqual("", configuration.Settings.GoogleNewsUrl);
    }

    [TestMethod]
    [DataRow("", DisplayName = "Missing index URL")]
    [DataRow("/news", DisplayName = "Relative index URL")]
    [DataRow("file:///index.html", DisplayName = "Non HTTP index URL")]
    public void Load_IndexModeInvalidSource_RejectsConfiguration(string basePageUrl)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteConfiguration("Index", workspace.CreateSettings(
            useIndexPage: true,
            basePageUrl: basePageUrl));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => ConfigurationLoader.Load(path, workspace.Context));

        Assert.Contains("BasePageUrl", exception.Message);
    }

    [TestMethod]
    public void Load_DefaultMode_UsesGoogleAndIgnoresUnusedIndexSource()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteConfiguration("Google", workspace.CreateSettings(
            basePageUrl: "unused invalid URL"));

        var configuration = ConfigurationLoader.Load(path, workspace.Context);

        Assert.IsFalse(configuration.Settings.UseIndexPage);
        Assert.AreEqual("https://news.google.test/rss", configuration.Settings.GoogleNewsUrl);
    }

    [TestMethod]
    public void Load_LegacyConfigurationWithoutNewProperties_DefaultsToGoogle()
    {
        using var workspace = new TestWorkspace();
        var settings = JsonSerializer.SerializeToNode(workspace.CreateSettings());
        Assert.IsNotNull(settings);
        var json = settings.AsObject();
        json.Remove("UseIndexPage");
        json.Remove("BasePageUrl");
        var path = Path.Combine(workspace.ConfigurationRoot, "Legacy.json");
        File.WriteAllText(path, json.ToJsonString());

        var configuration = ConfigurationLoader.Load(path, workspace.Context);

        Assert.IsFalse(configuration.Settings.UseIndexPage);
        Assert.AreEqual("", configuration.Settings.BasePageUrl);
        Assert.AreEqual("https://news.google.test/rss", configuration.Settings.GoogleNewsUrl);
    }

    [TestMethod]
    public void Load_GoogleModeWithoutSource_RejectsConfiguration()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteConfiguration("Google", workspace.CreateSettings(
            googleNewsUrl: ""));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => ConfigurationLoader.Load(path, workspace.Context));

        Assert.Contains("GoogleNewsUrl", exception.Message);
    }

    [TestMethod]
    public void LoadAll_MixedFiles_ReturnsValidFilesInNameOrder()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteConfiguration(
            "Zulu",
            workspace.CreateSettings("zulu.json"));
        workspace.WriteConfiguration(
            "Alpha",
            workspace.CreateSettings("alpha.json"));
        File.WriteAllText(
            Path.Combine(workspace.ConfigurationRoot, "Broken.json"),
            "{}");
        using var error = new StringWriter();

        var result = ConfigurationLoader.LoadAll(
            workspace.Context,
            error);

        Assert.HasCount(2, result.Configurations);
        Assert.AreEqual("Alpha", result.Configurations[0].Name);
        Assert.AreEqual("Zulu", result.Configurations[1].Name);
        Assert.AreEqual(1, result.Failures);
        StringAssert.Contains(error.ToString(), "Broken.json");
    }

    [TestMethod]
    public void Load_DestinationOutsideRoot_ThrowsInvalidOperationException()
    {
        using var workspace = new TestWorkspace();
        var settings = workspace.CreateSettings();
        settings = new FeedConfiguration
        {
            GoogleNewsUrl = settings.GoogleNewsUrl,
            LatestCount = settings.LatestCount,
            DestinationRssFilePath = "../../escaped.json",
            FeedName = settings.FeedName,
            FeedHomePageUrl = settings.FeedHomePageUrl,
            FeedUrl = settings.FeedUrl,
            FeedIconUrl = settings.FeedIconUrl,
            FeedFaviconUrl = settings.FeedFaviconUrl
        };
        var path = workspace.WriteConfiguration("Unsafe", settings);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => ConfigurationLoader.Load(path, workspace.Context));

        StringAssert.Contains(
            exception.Message,
            "repository root");
    }
}
