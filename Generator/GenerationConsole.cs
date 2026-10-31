using Spectre.Console;

namespace Generator;

public interface IPhaseProgress
{
    void Report(int completed, string? detail = null);
    void Warn(string message);
}

public interface IGeneratorConsole
{
    void ShowStartup(
        IReadOnlyList<ScheduleItem> schedule,
        TimeZoneInfo timeZone,
        int feedCount,
        string model);

    Task RunActivityAsync(
        string title,
        Func<Task> action);

    void ShowNextRun(
        DateTimeOffset localTime,
        TimeSpan remaining);

    void ScheduleItemStarting();
    void ScheduleItemFinished();
    void ScheduleItemCancelled();
    void ScheduleItemFailed(string message);
    void CancellingOverlappingRun();
    void ApplicationStopped();
    void FatalError(string message);
    void Warning(string message);
    void BeginScheduleItem(IReadOnlyList<ConfigurationFile> configurations);
    void BeginFeed(string name, int index, int total);

    Task<T> RunPhaseAsync<T>(
        GenerationPhase phase,
        string scope,
        int units,
        Func<IPhaseProgress, Task<T>> action);

    void FeedFailed(string name, string message);

    void CompleteScheduleItem(
        RunSummary summary,
        int failures);
}

public sealed class GeneratorConsole(
    IAnsiConsole console,
    TimingHistory timingHistory,
    TimeProvider timeProvider) : IGeneratorConsole
{
    private const int FeedPhaseCount = (int)GenerationPhase.Write + 1;
    private RunEtaPlan? runPlan;
    private long feedStarted;
    private long runStarted;

    private bool IsInteractive =>
        console.Profile.Capabilities.Interactive;

    public void ShowStartup(
        IReadOnlyList<ScheduleItem> schedule,
        TimeZoneInfo timeZone,
        int feedCount,
        string model)
    {
        console.Write(new Rule("[bold deepskyblue1]RSS Generator[/]")
            .RuleStyle("grey"));
        console.MarkupLine(
            $"[grey]Feeds[/] [white]{feedCount}[/]   " +
            $"[grey]Model[/] [white]{Markup.Escape(model)}[/]");
        console.MarkupLine(
            $"[grey]Local zone[/] [white]" +
            $"{Markup.Escape(timeZone.DisplayName)}[/]");
        console.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[bold]Day[/]")
            .AddColumn("[bold]Slots[/]");
        var days = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };

        for (var dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var times = new List<string>();

            for (var scheduleIndex = 0;
                scheduleIndex < schedule.Count;
                scheduleIndex++)
            {
                if (schedule[scheduleIndex].DayOfWeek == days[dayIndex])
                    times.Add(schedule[scheduleIndex]
                        .TimeOfDay
                        .ToString("HH:mm"));
            }

            if (times.Count != 0)
                table.AddRow(
                    days[dayIndex].ToString(),
                    string.Join("  ", times));
        }

        console.Write(table);
        console.WriteLine();
    }

    public async Task RunActivityAsync(
        string title,
        Func<Task> action)
    {
        var escapedTitle = Markup.Escape(title);

        try
        {
            if (IsInteractive)
            {
                await console.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("deepskyblue1"))
                    .StartAsync(
                        $"[deepskyblue1]{escapedTitle}[/]",
                        _ => action());
            }
            else
            {
                console.MarkupLine($"[blue]→[/] {escapedTitle}");
                await action();
            }

            console.MarkupLine($"[green]✓[/] {escapedTitle}");
        }
        catch
        {
            console.MarkupLine($"[red]✗[/] {escapedTitle}");
            throw;
        }
    }

    public void ShowNextRun(
        DateTimeOffset localTime,
        TimeSpan remaining)
    {
        console.MarkupLine(
            $"[grey]Next run[/] [white]{localTime:ddd, MMM d HH:mm}[/] " +
            $"[grey]({FormatDuration(remaining)} away)[/]  " +
            "[grey]Q / Escape quits[/]");
    }

    public void ScheduleItemStarting()
    {
        console.MarkupLine("[deepskyblue1]▶ Scheduled run started[/]");
    }

    public void ScheduleItemFinished()
    {
        console.MarkupLine("[green]■ Scheduled run finished[/]");
    }

    public void ScheduleItemCancelled()
    {
        console.MarkupLine("[yellow]■ Scheduled run cancelled[/]");
    }

    public void ScheduleItemFailed(string message)
    {
        console.MarkupLine(
            $"[red]■ Scheduled run failed:[/] {Markup.Escape(message)}");
    }

    public void CancellingOverlappingRun()
    {
        console.MarkupLine(
            "[yellow]New slot due; cancelling active run.[/]");
    }

    public void ApplicationStopped()
    {
        console.MarkupLine("[grey]Generator stopped.[/]");
    }

    public void FatalError(string message)
    {
        console.MarkupLine(
            $"[bold red]Generator failed:[/] {Markup.Escape(message)}");
    }

    public void Warning(string message)
    {
        console.MarkupLine(
            $"[yellow]![/] {Markup.Escape(message)}");
    }

    public void BeginScheduleItem(
        IReadOnlyList<ConfigurationFile> configurations)
    {
        runPlan = new RunEtaPlan(configurations, timingHistory);
        runStarted = timeProvider.GetTimestamp();
    }

    public void BeginFeed(string name, int index, int total)
    {
        feedStarted = timeProvider.GetTimestamp();
        console.MarkupLine(
            $"[bold deepskyblue1]{Markup.Escape(name)}[/] " +
            $"[grey]({index}/{total})[/]");
    }

    public async Task<T> RunPhaseAsync<T>(
        GenerationPhase phase,
        string scope,
        int units,
        Func<IPhaseProgress, Task<T>> action)
    {
        runPlan?.SetActualUnits(scope, phase, units);
        var started = timeProvider.GetTimestamp();
        var warnings = new List<string>();
        T result;

        try
        {
            if (IsInteractive)
            {
                result = await console.Progress()
                    .AutoClear(true)
                    .HideCompleted(false)
                    .Columns(
                        new SpinnerColumn(Spinner.Known.Dots),
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn
                        {
                            CompletedStyle = new Style(Color.DeepSkyBlue1),
                            FinishedStyle = new Style(Color.Green),
                            RemainingStyle = new Style(Color.Grey)
                        },
                        new PercentageColumn())
                    .StartAsync(async context =>
                    {
                        var task = context.AddTask(
                            BuildDescription(
                                phase,
                                units,
                                0,
                                null,
                                started),
                            maxValue: Math.Max(units, 1));
                        var progress = new ConsolePhaseProgress(
                            units,
                            warnings,
                            (completed, detail) =>
                            {
                                task.Value = units == 0 ? 1 : completed;
                                task.Description = BuildDescription(
                                    phase,
                                    units,
                                    completed,
                                    detail,
                                    started);
                            });
                        var value = await action(progress);
                        progress.Complete();
                        return value;
                    });
            }
            else
            {
                console.MarkupLine(
                    $"[blue]→[/] {BuildDescription(phase, units, 0, null, started)}");
                var progress = new ConsolePhaseProgress(
                    units,
                    warnings,
                    (_, _) => { });
                result = await action(progress);
                progress.Complete();
            }
        }
        catch
        {
            console.MarkupLine(
                $"[red]✗[/] {GetPhaseLabel(phase)}");
            throw;
        }

        var elapsed = timeProvider.GetElapsedTime(started);
        timingHistory.Record(phase, elapsed, units);
        timingHistory.Save();
        runPlan?.Complete(scope, phase, units);
        console.MarkupLine(
            $"[green]✓[/] {GetPhaseLabel(phase)} " +
            $"[grey]• {FormatDuration(elapsed)}[/]");

        if (phase == GenerationPhase.Write)
            console.MarkupLine(
                $"[green]✓[/] Feed complete [grey]• " +
                $"{FormatDuration(timeProvider.GetElapsedTime(feedStarted))}[/]");

        for (var index = 0; index < warnings.Count; index++)
            console.MarkupLine(
                $"  [yellow]![/] {Markup.Escape(warnings[index])}");

        return result;
    }

    public void FeedFailed(string name, string message)
    {
        runPlan?.SkipScope(name);
        console.MarkupLine(
            $"[red]Feed failed:[/] [white]{Markup.Escape(name)}[/] " +
            $"{Markup.Escape(message)} [grey]• " +
            $"{FormatDuration(timeProvider.GetElapsedTime(feedStarted))}[/]");
    }

    public void CompleteScheduleItem(
        RunSummary summary,
        int failures)
    {
        var elapsed = timeProvider.GetElapsedTime(runStarted);
        var color = failures == 0 ? "green" : "yellow";
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddRow("Feeds", summary.FeedCount.ToString())
            .AddRow("Items", summary.ItemCount.ToString())
            .AddRow("Skipped", summary.SkippedArticles.ToString())
            .AddRow("Failures", failures.ToString())
            .AddRow("Elapsed", FormatDuration(elapsed));
        console.Write(new Panel(table)
            .Header($"[{color}]Run complete[/]")
            .BorderColor(failures == 0 ? Color.Green : Color.Yellow));
        console.WriteLine();
        runPlan = null;
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        if (duration < TimeSpan.FromSeconds(1))
            return "<1s";

        if (duration < TimeSpan.FromMinutes(1))
            return $"{Math.Ceiling(duration.TotalSeconds):0}s";

        if (duration < TimeSpan.FromHours(1))
            return $"{Math.Ceiling(duration.TotalMinutes):0}m";

        var totalMinutes = (int)Math.Ceiling(duration.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return minutes == 0
            ? $"{hours}h"
            : $"{hours}h {minutes}m";
    }

    private string BuildDescription(
        GenerationPhase phase,
        int units,
        int completed,
        string? detail,
        long started)
    {
        var elapsed = timeProvider.GetElapsedTime(started);
        var stepEta = EstimateStep(
            phase,
            units,
            completed,
            elapsed);
        var runEta = runPlan?.EstimateRemaining(
            phase,
            completed,
            elapsed);
        var count = units > 1
            ? $" {completed}/{units}"
            : "";
        var detailText = string.IsNullOrWhiteSpace(detail)
            ? ""
            : $" [grey]• {Markup.Escape(detail)}[/]";

        var marker = phase == GenerationPhase.Publish
            ? ""
            : $"[deepskyblue1]{GetPhaseNumber(phase)}/{FeedPhaseCount}[/] ";

        return marker +
            $"[white]{GetPhaseTitle(phase)}{count}[/]{detailText} " +
            $"[grey]• step ETA {FormatEta(stepEta)} " +
            $"• run ETA {FormatEta(runEta)}[/]";
    }

    private TimeSpan? EstimateStep(
        GenerationPhase phase,
        int units,
        int completed,
        TimeSpan elapsed)
    {
        var remaining = Math.Max(0, units - completed);

        if (remaining == 0)
            return TimeSpan.Zero;

        if (completed > 0 && elapsed > TimeSpan.Zero)
            return TimeSpan.FromTicks(
                elapsed.Ticks / completed * remaining);

        return timingHistory.Estimate(phase, remaining);
    }

    private static string FormatEta(TimeSpan? eta)
    {
        return eta is null
            ? "estimating"
            : FormatDuration(eta.Value);
    }

    private static int GetPhaseNumber(GenerationPhase phase)
    {
        return (int)phase + 1;
    }

    private static string GetPhaseLabel(GenerationPhase phase)
    {
        return phase == GenerationPhase.Publish
            ? GetPhaseTitle(phase)
            : $"{GetPhaseNumber(phase)}/{FeedPhaseCount} " +
                GetPhaseTitle(phase);
    }

    private static string GetPhaseTitle(GenerationPhase phase)
    {
        return phase switch
        {
            GenerationPhase.Prepare => "Prepare workspace",
            GenerationPhase.Download => "Download feed",
            GenerationPhase.Resolve => "Resolve article URLs",
            GenerationPhase.Render => "Render article pages",
            GenerationPhase.Hydrate => "Generate RSS items",
            GenerationPhase.Write => "Write feed",
            GenerationPhase.Publish => "Publish changes",
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }

    private sealed class ConsolePhaseProgress(
        int total,
        List<string> warnings,
        Action<int, string?> update) : IPhaseProgress
    {
        private int completed;

        public void Report(int completed, string? detail = null)
        {
            this.completed = Math.Clamp(completed, 0, total);
            update(this.completed, detail);
        }

        public void Warn(string message)
        {
            warnings.Add(message);
        }

        public void Complete()
        {
            Report(total);
        }
    }

    private sealed class RunEtaPlan
    {
        private readonly TimingHistory history;
        private readonly Dictionary<GenerationPhase, int> totals = [];
        private readonly Dictionary<GenerationPhase, int> completed = [];
        private readonly Dictionary<(string Scope, GenerationPhase Phase), int>
            occurrences = [];
        private readonly HashSet<(string Scope, GenerationPhase Phase)>
            finished = [];

        public RunEtaPlan(
            IReadOnlyList<ConfigurationFile> configurations,
            TimingHistory history)
        {
            this.history = history;

            for (var index = 0; index < configurations.Count; index++)
            {
                var configuration = configurations[index];
                Add(configuration.Name, GenerationPhase.Prepare, 1);
                Add(configuration.Name, GenerationPhase.Download, 1);
                Add(
                    configuration.Name,
                    GenerationPhase.Resolve,
                    configuration.Settings.LatestCount);
                Add(
                    configuration.Name,
                    GenerationPhase.Render,
                    configuration.Settings.LatestCount);
                Add(
                    configuration.Name,
                    GenerationPhase.Hydrate,
                    configuration.Settings.LatestCount);
                Add(configuration.Name, GenerationPhase.Write, 1);
            }

            Add("", GenerationPhase.Publish, 1);
        }

        public void SetActualUnits(
            string scope,
            GenerationPhase phase,
            int units)
        {
            var key = (scope, phase);

            if (occurrences.TryGetValue(key, out var previous))
                totals[phase] += units - previous;
            else
                totals[phase] = totals.GetValueOrDefault(phase) + units;

            occurrences[key] = units;
        }

        public void Complete(
            string scope,
            GenerationPhase phase,
            int units)
        {
            completed[phase] = completed.GetValueOrDefault(phase) + units;
            finished.Add((scope, phase));
        }

        public void SkipScope(string scope)
        {
            foreach (var pair in occurrences)
            {
                if (pair.Key.Scope != scope || finished.Contains(pair.Key))
                    continue;

                totals[pair.Key.Phase] -= pair.Value;
                finished.Add(pair.Key);
            }
        }

        public TimeSpan? EstimateRemaining(
            GenerationPhase? currentPhase = null,
            int currentCompleted = 0,
            TimeSpan currentElapsed = default)
        {
            long ticks = 0;

            foreach (var pair in totals)
            {
                var remaining = pair.Value -
                    completed.GetValueOrDefault(pair.Key);

                if (currentPhase == pair.Key)
                    remaining -= currentCompleted;

                if (remaining <= 0)
                    continue;

                TimeSpan? unitEstimate;

                if (currentPhase == pair.Key &&
                    currentCompleted > 0 &&
                    currentElapsed > TimeSpan.Zero)
                {
                    unitEstimate = TimeSpan.FromTicks(
                        currentElapsed.Ticks / currentCompleted);
                }
                else
                {
                    unitEstimate = history.EstimateUnit(pair.Key);
                }

                if (unitEstimate is null)
                    return null;

                ticks += unitEstimate.Value.Ticks * remaining;
            }

            return TimeSpan.FromTicks(ticks);
        }

        private void Add(
            string scope,
            GenerationPhase phase,
            int units)
        {
            occurrences[(scope, phase)] = units;
            totals[phase] = totals.GetValueOrDefault(phase) + units;
        }
    }
}
