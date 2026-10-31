using Spectre.Console;

namespace Generator.Tests;

[TestClass]
public sealed class GeneratorConsoleTests
{
    [TestMethod]
    public void ShowStartup_ConfiguredSlots_RendersCleanPlainSchedule()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        var console = CreatePlainConsole(output);
        var generatorConsole = new GeneratorConsole(
            console,
            TimingHistory.Load(
                Path.Combine(workspace.TempRoot, "timings.json")),
            TimeProvider.System);
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "ScheduleZone",
            TimeSpan.FromHours(-4),
            "Schedule Zone",
            "Schedule Zone");

        generatorConsole.ShowStartup(
            [
                new ScheduleItem(
                    DayOfWeek.Monday,
                    new TimeOnly(8, 0)),
                new ScheduleItem(
                    DayOfWeek.Monday,
                    new TimeOnly(19, 0)),
                new ScheduleItem(
                    DayOfWeek.Sunday,
                    new TimeOnly(9, 30))
            ],
            timeZone,
            2,
            "local-model");

        var text = output.ToString();
        StringAssert.Contains(text, "RSS Generator");
        StringAssert.Contains(text, "Monday");
        StringAssert.Contains(text, "08:00");
        StringAssert.Contains(text, "19:00");
        StringAssert.Contains(text, "Sunday");
        StringAssert.Contains(text, "09:30");
        StringAssert.Contains(text, "Schedule Zone");
        Assert.DoesNotContain("\u001b", text);
    }

    [TestMethod]
    public void ShowNextRun_QuitKeys_RendersBothBindings()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        var generatorConsole = new GeneratorConsole(
            CreatePlainConsole(output),
            TimingHistory.Load(
                Path.Combine(workspace.TempRoot, "timings.json")),
            TimeProvider.System);

        generatorConsole.ShowNextRun(
            new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(5));

        StringAssert.Contains(
            output.ToString(),
            "Q / Escape quits");
    }

    [TestMethod]
    public async Task RunPhaseAsync_KnownHistory_RendersStepAndRunEtas()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        var history = TimingHistory.Load(
            Path.Combine(workspace.TempRoot, "timings.json"));

        for (var index = 0;
            index < Enum.GetValues<GenerationPhase>().Length;
            index++)
        {
            history.Record(
                (GenerationPhase)index,
                TimeSpan.FromSeconds(2));
        }

        var generatorConsole = new GeneratorConsole(
            CreatePlainConsole(output),
            history,
            TimeProvider.System);
        var configuration = workspace.CreateConfiguration("FeedOne");
        generatorConsole.BeginScheduleItem([configuration]);

        await generatorConsole.RunPhaseAsync(
            GenerationPhase.Prepare,
            configuration.Name,
            1,
            progress =>
            {
                progress.Report(1, "ready");
                return Task.FromResult(true);
            });

        var text = output.ToString();
        StringAssert.Contains(text, "step ETA 2s");
        StringAssert.Contains(text, "run ETA 20s");
        Assert.DoesNotContain("\u001b", text);
    }

    [TestMethod]
    public async Task RunPhaseAsync_MissingHistory_RendersEstimatingEtas()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        var generatorConsole = new GeneratorConsole(
            CreatePlainConsole(output),
            TimingHistory.Load(
                Path.Combine(workspace.TempRoot, "timings.json")),
            TimeProvider.System);
        var configuration = workspace.CreateConfiguration("FeedOne");
        generatorConsole.BeginScheduleItem([configuration]);

        await generatorConsole.RunPhaseAsync(
            GenerationPhase.Prepare,
            configuration.Name,
            1,
            progress =>
            {
                progress.Report(1);
                return Task.FromResult(true);
            });

        StringAssert.Contains(
            output.ToString(),
            "step ETA estimating • run ETA estimating");
    }

    [TestMethod]
    public async Task RunPhaseAsync_InteractiveConsole_RendersSpinnerAndColor()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        var history = TimingHistory.Load(
            Path.Combine(workspace.TempRoot, "timings.json"));
        var generatorConsole = new GeneratorConsole(
            CreateInteractiveConsole(output),
            history,
            TimeProvider.System);
        var configuration = workspace.CreateConfiguration("FeedOne");
        generatorConsole.BeginScheduleItem([configuration]);

        await generatorConsole.RunPhaseAsync(
            GenerationPhase.Resolve,
            configuration.Name,
            2,
            async progress =>
            {
                progress.Report(1, "first article");
                await Task.Delay(200);
                progress.Report(2, "second article");
                return true;
            });

        var text = output.ToString();
        Assert.Contains("\u001b[", text);
        Assert.IsGreaterThan(
            -1,
            text.IndexOfAny("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏".ToCharArray()));
        StringAssert.Contains(text, "step ETA");
        StringAssert.Contains(text, "run ETA");
    }

    [TestMethod]
    [DataRow(0.5, "<1s", DisplayName = "Subsecond")]
    [DataRow(30, "30s", DisplayName = "Seconds")]
    [DataRow(61, "2m", DisplayName = "Minutes rounded upward")]
    [DataRow(3601, "1h 1m", DisplayName = "Hour remainder rounded upward")]
    [DataRow(3660, "1h 1m", DisplayName = "Hours and minutes")]
    public void FormatDuration_ElapsedTime_ReturnsRelativeEta(
        double seconds,
        string expected)
    {
        var actual = GeneratorConsole.FormatDuration(
            TimeSpan.FromSeconds(seconds));

        Assert.AreEqual(expected, actual);
    }

    private static IAnsiConsole CreatePlainConsole(TextWriter output)
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output)
        });
    }

    private static IAnsiConsole CreateInteractiveConsole(TextWriter output)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.Yes,
            Out = new AnsiConsoleOutput(output)
        });
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        return console;
    }
}
