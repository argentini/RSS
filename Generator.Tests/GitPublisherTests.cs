namespace Generator.Tests;

[TestClass]
public sealed class GitPublisherTests
{
    [TestMethod]
    public async Task PublishAsync_ChangedFeed_CommitsExactPathsAndPushes()
    {
        using var workspace = new TestWorkspace();
        var commandRunner = new RecordingCommandRunner(
            new CommandResult(0, "", ""),
            new CommandResult(1, "", ""),
            new CommandResult(0, "committed", ""),
            new CommandResult(0, "pushed", ""));
        var publisher = new GitPublisher(commandRunner);
        var timestamp = new DateTimeOffset(
            2026,
            10,
            30,
            11,
            53,
            47,
            TimeSpan.Zero);

        var published = await publisher.PublishAsync(
            workspace.Root,
            [
                Path.Combine(workspace.Root, "ReutersUS.json"),
                Path.Combine(workspace.Root, "AppleInsider.json")
            ],
            timestamp,
            CancellationToken.None);

        Assert.IsTrue(published);
        Assert.HasCount(4, commandRunner.Invocations);
        CollectionAssert.AreEqual(
            new[] { "push" },
            commandRunner.Invocations[3].Arguments);
        CollectionAssert.Contains(
            commandRunner.Invocations[2].Arguments,
            "Content generated on 2026-10-30T11:53:47Z");
        CollectionAssert.Contains(
            commandRunner.Invocations[2].Arguments,
            "ReutersUS.json");
        CollectionAssert.Contains(
            commandRunner.Invocations[2].Arguments,
            "AppleInsider.json");
    }

    [TestMethod]
    public async Task PublishAsync_UnchangedFeed_SkipsCommitAndPush()
    {
        using var workspace = new TestWorkspace();
        var commandRunner = new RecordingCommandRunner(
            new CommandResult(0, "", ""),
            new CommandResult(0, "", ""));
        var publisher = new GitPublisher(commandRunner);

        var published = await publisher.PublishAsync(
            workspace.Root,
            [Path.Combine(workspace.Root, "feed.json")],
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.IsFalse(published);
        Assert.HasCount(2, commandRunner.Invocations);
    }

    [TestMethod]
    public async Task PublishAsync_PathOutsideRoot_ThrowsInvalidOperationException()
    {
        using var workspace = new TestWorkspace();
        var commandRunner = new RecordingCommandRunner();
        var publisher = new GitPublisher(commandRunner);
        var outsidePath = Path.Combine(
            Path.GetDirectoryName(workspace.Root) ?? Path.GetTempPath(),
            "outside.json");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => publisher.PublishAsync(
                workspace.Root,
                [outsidePath],
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "repository root");
        Assert.IsEmpty(commandRunner.Invocations);
    }

    private sealed class RecordingCommandRunner(
        params CommandResult[] results) : ICommandRunner
    {
        private int _index;

        public List<Invocation> Invocations { get; } = [];

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Invocations.Add(new Invocation(
                fileName,
                arguments.ToArray(),
                workingDirectory));
            return Task.FromResult(results[_index++]);
        }
    }

    private sealed record Invocation(
        string FileName,
        string[] Arguments,
        string WorkingDirectory);
}
