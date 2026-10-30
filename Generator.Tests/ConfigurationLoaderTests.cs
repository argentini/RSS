namespace Generator.Tests;

[TestClass]
public sealed class ConfigurationLoaderTests
{
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
