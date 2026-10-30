namespace Generator.Tests;

[TestClass]
public sealed class GeneratorSettingsTests
{
    [TestMethod]
    public void Load_LoopbackSettings_ReturnsConfiguredValues()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "appsettings.json");
        File.WriteAllText(
            path,
            """
            {
              "LMStudio": {
                "Model": "local-model",
                "BaseUrl": "http://127.0.0.1:1235",
                "TimeoutMinutes": 15
              }
            }
            """);

        var settings = GeneratorSettings.Load(path);

        Assert.AreEqual("local-model", settings.Model);
        Assert.AreEqual(
            "http://127.0.0.1:1235/",
            settings.BaseUrl.ToString());
        Assert.AreEqual(15, settings.TimeoutMinutes);
    }

    [TestMethod]
    public void Load_ExternalBaseUrl_ThrowsInvalidOperationException()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "appsettings.json");
        File.WriteAllText(
            path,
            """
            {
              "LMStudio": {
                "Model": "remote-model",
                "BaseUrl": "https://models.test",
                "TimeoutMinutes": 15
              }
            }
            """);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratorSettings.Load(path));

        StringAssert.Contains(exception.Message, "loopback");
    }
}
