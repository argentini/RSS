using System.Text.Json.Nodes;

namespace Generator.Tests;

[TestClass]
public sealed class Agent360PageScraperTests
{
    [TestMethod]
    public async Task StartAsync_FirstConnectionFails_RetriesOnce()
    {
        var session = CreateSuccessfulSession();
        var factory = new FakeSessionFactory(
            _ => throw new IOException("transport failed"),
            _ => Task.FromResult<IAgent360BrowserSession>(session));
        await using var scraper = new Agent360PageScraper(
            factory,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);

        await scraper.StartAsync(CancellationToken.None);

        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(0, session.DisposeCount);
    }

    [TestMethod]
    public async Task GetContentAsync_FirstNavigationFails_ReconnectsOnce()
    {
        var failedSession = new FakeSession(
            (_, _) => Task.FromResult(new JsonObject
            {
                ["ok"] = false,
                ["error"] = "navigation failed"
            }),
            (_, _) => throw new AssertFailedException("Capture was unexpected."));
        var successfulSession = CreateSuccessfulSession();
        var factory = new FakeSessionFactory(
            _ => Task.FromResult<IAgent360BrowserSession>(failedSession),
            _ => Task.FromResult<IAgent360BrowserSession>(successfulSession));
        await using var scraper = new Agent360PageScraper(
            factory,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);

        var html = await scraper.GetContentAsync(
            new Uri("https://source.test/article"),
            CancellationToken.None);

        Assert.AreEqual("<html>Article</html>", html);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(1, failedSession.CloseCount);
        Assert.AreEqual(1, failedSession.DisposeCount);
        Assert.AreEqual(1, successfulSession.NavigateCount);
        Assert.AreEqual(1, successfulSession.CaptureCount);
    }

    [TestMethod]
    public async Task GetContentAsync_NavigationExceedsDeadline_RetriesOnceThenThrows()
    {
        var first = CreateHangingSession();
        var second = CreateHangingSession();
        var factory = new FakeSessionFactory(
            _ => Task.FromResult<IAgent360BrowserSession>(first),
            _ => Task.FromResult<IAgent360BrowserSession>(second));
        await using var scraper = new Agent360PageScraper(
            factory,
            TimeSpan.FromMilliseconds(30),
            TimeSpan.Zero);

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => scraper.GetContentAsync(
                new Uri("https://source.test/article"),
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "navigation exceeded");
        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.DisposeCount);
    }

    [TestMethod]
    public async Task GetContentAsync_CallerCancels_ClearsSessionWithoutRetry()
    {
        var navigationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession(
            async (_, cancellationToken) =>
            {
                navigationStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new JsonObject();
            },
            (_, _) => throw new AssertFailedException("Capture was unexpected."));
        var factory = new FakeSessionFactory(
            _ => Task.FromResult<IAgent360BrowserSession>(session));
        await using var scraper = new Agent360PageScraper(
            factory,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource();
        var operation = scraper.GetContentAsync(
            new Uri("https://source.test/article"),
            cancellation.Token);
        await navigationStarted.Task;

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.CloseCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task GetContentAsync_FailedCleanupHangs_StillReconnectsOnce()
    {
        var neverClosed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var neverDisposed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failedSession = new FakeSession(
            (_, _) => Task.FromResult(new JsonObject
            {
                ["ok"] = false,
                ["error"] = "navigation failed"
            }),
            (_, _) => throw new AssertFailedException("Capture was unexpected."),
            _ => neverClosed.Task,
            () => neverDisposed.Task);
        var successfulSession = CreateSuccessfulSession();
        var factory = new FakeSessionFactory(
            _ => Task.FromResult<IAgent360BrowserSession>(failedSession),
            _ => Task.FromResult<IAgent360BrowserSession>(successfulSession));
        await using var scraper = new Agent360PageScraper(
            factory,
            TimeSpan.FromMilliseconds(30),
            TimeSpan.Zero);

        var html = await scraper.GetContentAsync(
                new Uri("https://source.test/article"),
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual("<html>Article</html>", html);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(1, failedSession.CloseCount);
        Assert.AreEqual(1, failedSession.DisposeCount);
    }

    private static FakeSession CreateSuccessfulSession()
    {
        return new FakeSession(
            (_, _) => Task.FromResult(new JsonObject
            {
                ["ok"] = true
            }),
            (_, _) => Task.FromResult(new JsonObject
            {
                ["content"] = "<html>Article</html>",
                ["afkortet"] = false
            }));
    }

    private static FakeSession CreateHangingSession()
    {
        var never = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return new FakeSession(
            (_, _) => never.Task,
            (_, _) => throw new AssertFailedException("Capture was unexpected."));
    }

    private sealed class FakeSessionFactory(
        params Func<CancellationToken, Task<IAgent360BrowserSession>>[] factories)
        : IAgent360BrowserSessionFactory
    {
        public int CreateCount { get; private set; }

        public Task<IAgent360BrowserSession> CreateAsync(
            CancellationToken cancellationToken)
        {
            var factory = factories[Math.Min(
                CreateCount,
                factories.Length - 1)];
            CreateCount++;
            return factory(cancellationToken);
        }
    }

    private sealed class FakeSession(
        Func<Uri, CancellationToken, Task<JsonObject>> navigate,
        Func<int, CancellationToken, Task<JsonObject>> capture,
        Func<CancellationToken, Task>? close = null,
        Func<Task>? dispose = null)
        : IAgent360BrowserSession
    {
        public int NavigateCount { get; private set; }
        public int CaptureCount { get; private set; }
        public int CloseCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task<JsonObject> NavigateAsync(
            Uri url,
            CancellationToken cancellationToken)
        {
            NavigateCount++;
            return navigate(url, cancellationToken);
        }

        public Task<JsonObject> GetPageContentAsync(
            int maximumCharacters,
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            return capture(maximumCharacters, cancellationToken);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            return close?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return dispose is null
                ? ValueTask.CompletedTask
                : new ValueTask(dispose());
        }
    }
}
