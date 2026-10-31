namespace Generator;

public sealed class GeneratorScheduler
{
    private readonly TimeProvider timeProvider;
    private readonly IGeneratorConsole console;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public GeneratorScheduler(
        TimeProvider timeProvider,
        IGeneratorConsole console,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.timeProvider = timeProvider;
        this.console = console;
        this.delay = delay ?? ((duration, cancellationToken) =>
            Task.Delay(duration, timeProvider, cancellationToken));
    }

    public async Task RunAsync(
        IReadOnlyList<ScheduleItem> schedule,
        Func<CancellationToken, Task> run,
        bool runImmediately,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? runCancellation = null;
        var runTask = Task.CompletedTask;
        var cursor = timeProvider.GetUtcNow();

        try
        {
            if (runImmediately)
            {
                runCancellation = CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken);
                runTask = RunSafelyAsync(run, runCancellation.Token);
                cursor = cursor.AddTicks(1);
            }

            while (cancellationToken.IsCancellationRequested == false)
            {
                var scheduledTime = GetNextOccurrence(
                    schedule,
                    cursor,
                    timeProvider.LocalTimeZone);
                var localTime = TimeZoneInfo.ConvertTime(
                    scheduledTime,
                    timeProvider.LocalTimeZone);

                var wait = scheduledTime - timeProvider.GetUtcNow();
                console.ShowNextRun(
                    localTime,
                    wait > TimeSpan.Zero ? wait : TimeSpan.Zero);

                if (wait > TimeSpan.Zero)
                    await delay(wait, cancellationToken);

                if (runCancellation is not null)
                {
                    if (runTask.IsCompleted == false)
                        console.CancellingOverlappingRun();

                    await StopRunAsync(runCancellation, runTask);
                }

                runCancellation = CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken);
                runTask = RunSafelyAsync(run, runCancellation.Token);
                cursor = scheduledTime.AddTicks(1);
                var now = timeProvider.GetUtcNow();

                if (now > cursor)
                    cursor = now;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (runCancellation is not null)
                await StopRunAsync(runCancellation, runTask);
        }
    }

    public static DateTimeOffset GetNextOccurrence(
        IReadOnlyList<ScheduleItem> schedule,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        if (schedule.Count == 0)
            throw new InvalidOperationException(
                "Schedule must contain at least one item.");

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        DateTimeOffset? next = null;

        for (var index = 0; index < schedule.Count; index++)
        {
            var item = schedule[index];
            var daysUntil = ((int)item.DayOfWeek -
                (int)localNow.DayOfWeek + 7) % 7;
            var candidateLocal = DateTime.SpecifyKind(
                localDate
                    .AddDays(daysUntil)
                    .ToDateTime(item.TimeOfDay),
                DateTimeKind.Unspecified);

            while (timeZone.IsInvalidTime(candidateLocal))
                candidateLocal = candidateLocal.AddDays(7);

            var candidate = new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(candidateLocal, timeZone),
                TimeSpan.Zero);

            if (candidate < now)
            {
                candidateLocal = candidateLocal.AddDays(7);
                candidate = new DateTimeOffset(
                    TimeZoneInfo.ConvertTimeToUtc(candidateLocal, timeZone),
                    TimeSpan.Zero);
            }

            if (next is null || candidate < next)
                next = candidate;
        }

        return next
            ?? throw new InvalidOperationException(
                "Schedule contains no valid items.");
    }

    private async Task RunSafelyAsync(
        Func<CancellationToken, Task> run,
        CancellationToken cancellationToken)
    {
        try
        {
            console.ScheduleItemStarting();
            await run(cancellationToken);
            console.ScheduleItemFinished();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            console.ScheduleItemCancelled();
        }
        catch (Exception exception)
        {
            console.ScheduleItemFailed(exception.Message);
        }
    }

    private static async Task StopRunAsync(
        CancellationTokenSource runCancellation,
        Task runTask)
    {
        await runCancellation.CancelAsync();
        await runTask;
        runCancellation.Dispose();
    }
}
