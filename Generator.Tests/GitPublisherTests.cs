using System.Diagnostics;

namespace Generator.Tests;

[TestClass]
public sealed class GitPublisherTests
{
    [TestMethod]
    public async Task PublishAsync_RemoteCodeAndLocalFeedChanges_BothAuthoritiesWin()
    {
        using var workspace = new TestWorkspace();
        var remote = Path.Combine(workspace.Root, "remote.git");
        var seed = Path.Combine(workspace.Root, "seed");
        var local = Path.Combine(workspace.Root, "local");
        var collaborator = Path.Combine(workspace.Root, "collaborator");
        RunGit(workspace.Root, "init", "--bare", remote);
        Directory.CreateDirectory(seed);
        RunGit(seed, "init", "--initial-branch=main");
        ConfigureIdentity(seed);
        File.WriteAllText(Path.Combine(seed, "code.txt"), "base code");
        File.WriteAllText(Path.Combine(seed, "feed.json"), "base feed");
        RunGit(seed, "add", "--", "code.txt", "feed.json");
        RunGit(seed, "commit", "-m", "base");
        RunGit(seed, "remote", "add", "origin", remote);
        RunGit(seed, "push", "--set-upstream", "origin", "main");
        RunGit(remote, "symbolic-ref", "HEAD", "refs/heads/main");
        RunGit(workspace.Root, "clone", remote, local);
        RunGit(workspace.Root, "clone", remote, collaborator);
        ConfigureIdentity(local);
        ConfigureIdentity(collaborator);
        File.WriteAllText(
            Path.Combine(collaborator, "code.txt"),
            "remote code");
        File.WriteAllText(
            Path.Combine(collaborator, "feed.json"),
            "remote feed");
        RunGit(collaborator, "add", "--", "code.txt", "feed.json");
        RunGit(collaborator, "commit", "-m", "remote changes");
        RunGit(collaborator, "push");
        File.WriteAllText(Path.Combine(local, "code.txt"), "local code");
        RunGit(local, "add", "--", "code.txt");
        RunGit(local, "commit", "-m", "local code change");
        File.WriteAllText(Path.Combine(local, "feed.json"), "local feed");
        File.WriteAllText(Path.Combine(local, "notes.txt"), "untracked");
        var publisher = new GitPublisher(
            new ProcessCommandRunner(),
            CreateCredentials());

        var published = await publisher.PublishAsync(
            local,
            [Path.Combine(local, "feed.json")],
            new DateTimeOffset(
                2026,
                10,
                30,
                11,
                53,
                47,
                TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsTrue(published);
        Assert.AreEqual("remote code", File.ReadAllText(
            Path.Combine(local, "code.txt")));
        Assert.AreEqual("local feed", File.ReadAllText(
            Path.Combine(local, "feed.json")));
        Assert.AreEqual("untracked", File.ReadAllText(
            Path.Combine(local, "notes.txt")));
        Assert.AreEqual(
            "remote code",
            RunGit(remote, "show", "refs/heads/main:code.txt"));
        Assert.AreEqual(
            "local feed",
            RunGit(remote, "show", "refs/heads/main:feed.json"));
        Assert.AreEqual("?? notes.txt", RunGit(local, "status", "--short"));
        Assert.IsFalse(File.Exists(Path.Combine(
            local,
            ".git",
            "generator-pending",
            "files",
            "feed.json")));
    }

    [TestMethod]
    public async Task PublishAsync_TwoRejectedPushes_RetriesAndVerifiesRemote()
    {
        using var workspace = new TestWorkspace();
        var gitDirectory = Path.Combine(workspace.Root, ".git");
        Directory.CreateDirectory(gitDirectory);
        var feed = Path.Combine(workspace.Root, "feed.json");
        File.WriteAllText(feed, "local feed");
        var commandRunner = new PublishingCommandRunner(
            gitDirectory,
            localFeedChanged: true,
            rejectedPushes: 2);
        var credentials = CreateCredentials();
        var publisher = new GitPublisher(commandRunner, credentials);

        var published = await publisher.PublishAsync(
            workspace.Root,
            [feed],
            new DateTimeOffset(
                2026,
                10,
                30,
                11,
                53,
                47,
                TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsTrue(published);
        Assert.AreEqual(3, commandRunner.PushCount);
        Assert.AreEqual(1, commandRunner.VerificationCount);
        var commit = commandRunner.Invocations.First(
            invocation => invocation.Arguments[0] == "commit");
        CollectionAssert.Contains(
            commit.Arguments,
            "Content generated on 2026-10-30T11:53:47Z");
        CollectionAssert.Contains(commit.Arguments, "feed.json");
        var networkInvocations = commandRunner.Invocations
            .Where(invocation =>
                invocation.Arguments[0] is "fetch" or "push")
            .ToArray();
        Assert.IsNotEmpty(networkInvocations);

        for (var index = 0; index < networkInvocations.Length; index++)
        {
            var invocation = networkInvocations[index];
            Assert.AreEqual(
                credentials.UserName,
                invocation.Environment["GENERATOR_GITHUB_USERNAME"]);
            Assert.AreEqual(
                credentials.PersonalAccessToken,
                invocation.Environment["GENERATOR_GITHUB_TOKEN"]);
            Assert.DoesNotContain(
                credentials.PersonalAccessToken,
                string.Join('\n', invocation.Arguments));
        }

        Assert.IsFalse(File.Exists(Path.Combine(
            gitDirectory,
            "generator-pending",
            "files",
            "feed.json")));
    }

    [TestMethod]
    public async Task PublishAsync_AllPushesFail_RestoresPendingFeedLater()
    {
        using var workspace = new TestWorkspace();
        var gitDirectory = Path.Combine(workspace.Root, ".git");
        Directory.CreateDirectory(gitDirectory);
        var feed = Path.Combine(workspace.Root, "feed.json");
        File.WriteAllText(feed, "local feed");
        var commandRunner = new PublishingCommandRunner(
            gitDirectory,
            localFeedChanged: true,
            rejectedPushes: 3);
        var publisher = new GitPublisher(
            commandRunner,
            CreateCredentials());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => publisher.PublishAsync(
                workspace.Root,
                [feed],
                DateTimeOffset.UtcNow,
                CancellationToken.None));
        File.WriteAllText(feed, "remote feed");
        await publisher.RestorePendingAsync(
            workspace.Root,
            [feed],
            CancellationToken.None);

        StringAssert.Contains(exception.Message, "after 3 attempts");
        Assert.AreEqual(3, commandRunner.PushCount);
        Assert.AreEqual("local feed", File.ReadAllText(feed));
    }

    [TestMethod]
    public async Task PublishAsync_UnchangedFeed_PushesWithoutCommitting()
    {
        using var workspace = new TestWorkspace();
        var gitDirectory = Path.Combine(workspace.Root, ".git");
        Directory.CreateDirectory(gitDirectory);
        var feed = Path.Combine(workspace.Root, "feed.json");
        File.WriteAllText(feed, "remote feed");
        var commandRunner = new PublishingCommandRunner(
            gitDirectory,
            localFeedChanged: false,
            rejectedPushes: 0);
        var publisher = new GitPublisher(
            commandRunner,
            CreateCredentials());

        var published = await publisher.PublishAsync(
            workspace.Root,
            [feed],
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.IsFalse(published);
        Assert.AreEqual(1, commandRunner.PushCount);
        Assert.IsFalse(commandRunner.Invocations.Exists(
            invocation => invocation.Arguments[0] == "commit"));
    }

    [TestMethod]
    public async Task PublishAsync_UntrackedCollision_StopsBeforeReset()
    {
        using var workspace = new TestWorkspace();
        var gitDirectory = Path.Combine(workspace.Root, ".git");
        Directory.CreateDirectory(gitDirectory);
        var feed = Path.Combine(workspace.Root, "feed.json");
        File.WriteAllText(feed, "local feed");
        var commandRunner = new PublishingCommandRunner(
            gitDirectory,
            localFeedChanged: true,
            rejectedPushes: 0,
            untracked: "code.txt\0",
            upstream: "code.txt\0feed.json\0");
        var publisher = new GitPublisher(
            commandRunner,
            CreateCredentials());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => publisher.PublishAsync(
                workspace.Root,
                [feed],
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "Untracked path");
        Assert.IsFalse(commandRunner.Invocations.Exists(
            invocation => invocation.Arguments[0] == "reset"));
    }

    [TestMethod]
    public async Task PublishAsync_RemoteVerificationFails_KeepsPendingFeed()
    {
        using var workspace = new TestWorkspace();
        var gitDirectory = Path.Combine(workspace.Root, ".git");
        Directory.CreateDirectory(gitDirectory);
        var feed = Path.Combine(workspace.Root, "feed.json");
        File.WriteAllText(feed, "local feed");
        var commandRunner = new PublishingCommandRunner(
            gitDirectory,
            localFeedChanged: true,
            rejectedPushes: 0,
            rejectedVerifications: 3);
        var publisher = new GitPublisher(
            commandRunner,
            CreateCredentials());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => publisher.PublishAsync(
                workspace.Root,
                [feed],
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "Remote verification");
        Assert.AreEqual(3, commandRunner.PushCount);
        Assert.AreEqual(3, commandRunner.VerificationCount);
        Assert.IsTrue(File.Exists(Path.Combine(
            gitDirectory,
            "generator-pending",
            "files",
            "feed.json")));
    }

    [TestMethod]
    public async Task PublishAsync_ActiveGitOperation_StopsBeforeFetch()
    {
        using var workspace = new TestWorkspace();
        var gitDirectory = Path.Combine(workspace.Root, ".git");
        Directory.CreateDirectory(gitDirectory);
        File.WriteAllText(Path.Combine(gitDirectory, "MERGE_HEAD"), "commit");
        var feed = Path.Combine(workspace.Root, "feed.json");
        File.WriteAllText(feed, "local feed");
        var commandRunner = new PublishingCommandRunner(
            gitDirectory,
            localFeedChanged: true,
            rejectedPushes: 0);
        var publisher = new GitPublisher(
            commandRunner,
            CreateCredentials());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => publisher.PublishAsync(
                workspace.Root,
                [feed],
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "already active");
        Assert.HasCount(1, commandRunner.Invocations);
        CollectionAssert.AreEqual(
            new[] { "rev-parse", "--absolute-git-dir" },
            commandRunner.Invocations[0].Arguments);
    }

    [TestMethod]
    public async Task PublishAsync_PathOutsideRoot_ThrowsInvalidOperationException()
    {
        using var workspace = new TestWorkspace();
        var commandRunner = new PublishingCommandRunner(
            Path.Combine(workspace.Root, ".git"),
            localFeedChanged: true,
            rejectedPushes: 0);
        var publisher = new GitPublisher(
            commandRunner,
            CreateCredentials());
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

    private static GitHubCredentials CreateCredentials()
    {
        return new GitHubCredentials
        {
            UserName = "octocat",
            PersonalAccessToken = "test-token"
        };
    }

    private static void ConfigureIdentity(string workingDirectory)
    {
        RunGit(workingDirectory, "config", "user.name", "Generator Tests");
        RunGit(
            workingDirectory,
            "config",
            "user.email",
            "generator@example.test");
    }

    private static string RunGit(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        for (var index = 0; index < arguments.Length; index++)
            startInfo.ArgumentList.Add(arguments[index]);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim());

        return output.Trim();
    }

    private sealed class PublishingCommandRunner(
        string gitDirectory,
        bool localFeedChanged,
        int rejectedPushes,
        string untracked = "",
        string upstream = "feed.json\0",
        int rejectedVerifications = 0) : ICommandRunner
    {
        public List<Invocation> Invocations { get; } = [];
        public int PushCount { get; private set; }
        public int VerificationCount { get; private set; }

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            var invocation = new Invocation(
                arguments.ToArray(),
                environment is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(environment));
            Invocations.Add(invocation);
            var result = GetResult(invocation.Arguments);
            return Task.FromResult(result);
        }

        private CommandResult GetResult(string[] arguments)
        {
            if (arguments.SequenceEqual(["rev-parse", "--absolute-git-dir"]))
                return Success(gitDirectory);

            if (arguments.SequenceEqual(
                ["symbolic-ref", "--quiet", "--short", "HEAD"]))
                return Success("main");

            if (arguments[0] == "config" &&
                arguments[^1].EndsWith(".remote", StringComparison.Ordinal))
                return Success("origin");

            if (arguments[0] == "config" &&
                arguments[^1].EndsWith(".merge", StringComparison.Ordinal))
                return Success("refs/heads/main");

            if (arguments[0] == "fetch")
                return Success();

            if (arguments.SequenceEqual(
                ["merge-base", "HEAD", "@{upstream}"]))
                return Success("base-commit");

            if (arguments[0] == "diff" && arguments.Contains("--cached"))
                return localFeedChanged
                    ? Failure(1)
                    : Success();

            if (arguments[0] == "diff")
                return localFeedChanged
                    ? Failure(1)
                    : Success();

            if (arguments[0] == "ls-files" &&
                arguments.Contains("--error-unmatch"))
                return Success("feed.json");

            if (arguments[0] == "ls-files")
                return Success(untracked);

            if (arguments[0] == "ls-tree")
                return Success(upstream);

            if (arguments[0] is "reset" or "add" or "commit")
                return Success();

            if (arguments.SequenceEqual(["rev-parse", "HEAD"]))
                return Success("published-commit");

            if (arguments[0] == "push")
            {
                PushCount++;
                return PushCount <= rejectedPushes
                    ? Failure(1, "rejected")
                    : Success();
            }

            if (arguments.Count(value => value == "--is-ancestor") == 1)
            {
                VerificationCount++;
                return VerificationCount <= rejectedVerifications
                    ? Failure(1, "missing commit")
                    : Success();
            }

            throw new InvalidOperationException(
                "Unexpected git command: " + string.Join(' ', arguments));
        }

        private static CommandResult Success(string output = "")
        {
            return new CommandResult(0, output, "");
        }

        private static CommandResult Failure(
            int exitCode,
            string error = "failed")
        {
            return new CommandResult(exitCode, "", error);
        }
    }

    private sealed record Invocation(
        string[] Arguments,
        IReadOnlyDictionary<string, string> Environment);
}
