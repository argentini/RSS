namespace Generator.Tests;

[TestClass]
public sealed class GeneratorSchedulerTests
{
    private static readonly ScheduleItem[] Schedule =
    [
        new(DayOfWeek.Saturday, new TimeOnly(8, 0)),
        new(DayOfWeek.Saturday, new TimeOnly(19, 0)),
        new(DayOfWeek.Sunday, new TimeOnly(8, 0))
    ];

    [TestMethod]
    [DataRow(
        "2026-10-31T07:30:00+00:00",
        "2026-10-31T08:00:00+00:00",
        DisplayName = "Before first daily slot")]
    [DataRow(
        "2026-10-31T08:00:00+00:00",
        "2026-10-31T08:00:00+00:00",
        DisplayName = "Exactly at daily slot")]
    [DataRow(
        "2026-10-31T08:00:01+00:00",
        "2026-10-31T19:00:00+00:00",
        DisplayName = "Between daily slots")]
    [DataRow(
        "2026-11-01T08:00:01+00:00",
        "2026-11-07T08:00:00+00:00",
        DisplayName = "After final weekly slot")]
    public void GetNextOccurrence_CurrentTime_ReturnsExpectedSlot(
        string nowValue,
        string expectedValue)
    {
        var now = DateTimeOffset.Parse(nowValue);
        var expected = DateTimeOffset.Parse(expectedValue);

        var actual = GeneratorScheduler.GetNextOccurrence(
            Schedule,
            now,
            TimeZoneInfo.Utc);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void GetNextOccurrence_LocalTimeZone_UsesLocalWallClock()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "SchedulerTestZone",
            TimeSpan.FromHours(-4),
            "Scheduler test zone",
            "Scheduler test zone");
        var now = DateTimeOffset.Parse("2026-10-31T11:30:00+00:00");
        var expected = DateTimeOffset.Parse("2026-10-31T12:00:00+00:00");

        var actual = GeneratorScheduler.GetNextOccurrence(
            Schedule,
            now,
            timeZone);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task RunAsync_NewSlotDue_CancelsOldRunBeforeStartingNewRun()
    {
        var timeProvider = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-10-31T08:00:00+00:00"));
        var nextDelayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nextSlotDue = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRunStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRunStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var shutdown = new CancellationTokenSource();
        var console = new RecordingGeneratorConsole();
        var runCount = 0;
        var firstRunCancelled = false;
        var cancellationObservedBeforeSecond = false;
        var scheduler = new GeneratorScheduler(
            timeProvider,
            console,
            (_, cancellationToken) =>
            {
                nextDelayStarted.SetResult();
                return nextSlotDue.Task.WaitAsync(cancellationToken);
            });

        var schedulerTask = scheduler.RunAsync(
            Schedule,
            async cancellationToken =>
            {
                runCount++;

                if (runCount == 1)
                {
                    firstRunStarted.SetResult();

                    try
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        firstRunCancelled = true;
                        throw;
                    }
                }
                else
                {
                    cancellationObservedBeforeSecond = firstRunCancelled;
                    secondRunStarted.SetResult();
                    await shutdown.CancelAsync();
                }
            },
            false,
            shutdown.Token);

        await firstRunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await nextDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.SetUtcNow(
            DateTimeOffset.Parse("2026-10-31T19:00:00+00:00"));
        nextSlotDue.SetResult();
        await secondRunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await schedulerTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(2, runCount);
        Assert.IsTrue(firstRunCancelled);
        Assert.IsTrue(cancellationObservedBeforeSecond);
        Assert.AreEqual(1, console.OverlapCancellationCount);
    }

    [TestMethod]
    public async Task RunAsync_RunFails_WaitsForNextSlot()
    {
        var timeProvider = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-10-31T08:00:00+00:00"));
        using var shutdown = new CancellationTokenSource();
        var console = new RecordingGeneratorConsole();
        var runCount = 0;
        var waitedForNextSlot = false;
        var scheduler = new GeneratorScheduler(
            timeProvider,
            console,
            (_, cancellationToken) =>
            {
                waitedForNextSlot = true;
                shutdown.Cancel();
                return Task.FromCanceled(cancellationToken);
            });

        await scheduler.RunAsync(
            Schedule,
            _ =>
            {
                runCount++;
                throw new InvalidOperationException("broken run");
            },
            false,
            shutdown.Token);

        Assert.AreEqual(1, runCount);
        Assert.IsTrue(waitedForNextSlot);
        Assert.AreEqual(1, console.ScheduleFailureCount);
        Assert.Contains("schedule-failed:broken run", console.Events);
    }

    [TestMethod]
    public async Task RunAsync_ImmediateRunRequested_StartsBeforeNextSlot()
    {
        var timeProvider = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-10-31T07:30:00+00:00"));
        using var shutdown = new CancellationTokenSource();
        var runCount = 0;
        var scheduler = new GeneratorScheduler(
            timeProvider,
            new RecordingGeneratorConsole());

        await scheduler.RunAsync(
            Schedule,
            _ =>
            {
                runCount++;
                shutdown.Cancel();
                return Task.CompletedTask;
            },
            true,
            shutdown.Token);

        Assert.AreEqual(1, runCount);
    }

    [TestMethod]
    public async Task RunAsync_ImmediateRunActive_ShowsNextRunAfterCompletion()
    {
        var timeProvider = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-10-31T07:30:00+00:00"));
        var runStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completeRun = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var waitStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var shutdown = new CancellationTokenSource();
        var console = new RecordingGeneratorConsole();
        var scheduler = new GeneratorScheduler(
            timeProvider,
            console,
            (_, cancellationToken) =>
            {
                waitStarted.TrySetResult();
                return Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            });

        var schedulerTask = scheduler.RunAsync(
            Schedule,
            async _ =>
            {
                runStarted.SetResult();
                await completeRun.Task;
            },
            true,
            shutdown.Token);

        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("next-run", console.Events);

        completeRun.SetResult();
        await console.NextRunShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.CancelAsync();
        await schedulerTask.WaitAsync(TimeSpan.FromSeconds(5));

        var finishedIndex = console.Events.IndexOf("schedule-finished");
        var nextRunIndex = console.Events.IndexOf("next-run");
        Assert.IsGreaterThan(finishedIndex, nextRunIndex);
        Assert.AreEqual(1, console.Events.Count(
            eventName => eventName == "next-run"));
    }

    private sealed class MutableTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            utcNow = value;
        }
    }
}
