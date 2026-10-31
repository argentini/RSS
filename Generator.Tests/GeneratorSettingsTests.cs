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
              },
              "Schedule": [
                {
                  "DayOfWeek": "Monday",
                  "TimeOfDay": "08:00"
                },
                {
                  "DayOfWeek": "Monday",
                  "TimeOfDay": "19:00"
                }
              ]
            }
            """);

        var settings = GeneratorSettings.Load(path);

        Assert.AreEqual("local-model", settings.Model);
        Assert.AreEqual(
            "http://127.0.0.1:1235/",
            settings.BaseUrl.ToString());
        Assert.AreEqual(15, settings.TimeoutMinutes);
        Assert.HasCount(2, settings.Schedule);
        Assert.AreEqual(
            new ScheduleItem(DayOfWeek.Monday, new TimeOnly(8, 0)),
            settings.Schedule[0]);
        Assert.AreEqual(
            new ScheduleItem(DayOfWeek.Monday, new TimeOnly(19, 0)),
            settings.Schedule[1]);
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
              },
              "Schedule": [
                {
                  "DayOfWeek": "Monday",
                  "TimeOfDay": "08:00"
                }
              ]
            }
            """);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratorSettings.Load(path));

        StringAssert.Contains(exception.Message, "loopback");
    }

    [TestMethod]
    public void Load_MissingSchedule_ThrowsInvalidOperationException()
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

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratorSettings.Load(path));

        StringAssert.Contains(exception.Message, "Schedule");
    }

    [TestMethod]
    [DataRow(
        "Funday",
        "08:00",
        "DayOfWeek",
        DisplayName = "Invalid weekday")]
    [DataRow(
        "Monday",
        "8am",
        "TimeOfDay",
        DisplayName = "Invalid time")]
    public void Load_InvalidScheduleItem_ThrowsInvalidOperationException(
        string dayOfWeek,
        string timeOfDay,
        string expectedMessage)
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "appsettings.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "LMStudio": {
                "Model": "local-model",
                "BaseUrl": "http://127.0.0.1:1235",
                "TimeoutMinutes": 15
              },
              "Schedule": [
                {
                  "DayOfWeek": "{{dayOfWeek}}",
                  "TimeOfDay": "{{timeOfDay}}"
                }
              ]
            }
            """);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratorSettings.Load(path));

        StringAssert.Contains(exception.Message, expectedMessage);
    }
}
