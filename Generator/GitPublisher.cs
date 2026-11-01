using System.Diagnostics;
using System.Globalization;

namespace Generator;

public sealed record CommandResult(
    int ExitCode,
    string Output,
    string Error);

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _timeout;

    public ProcessCommandRunner()
        : this(DefaultTimeout)
    {
    }

    public ProcessCommandRunner(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        _timeout = timeout;
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        for (var index = 0; index < arguments.Count; index++)
            startInfo.ArgumentList.Add(arguments[index]);

        if (environment is not null)
        {
            foreach (var variable in environment)
                startInfo.Environment[variable.Key] = variable.Value;
        }

        using var process = new Process { StartInfo = startInfo };

        if (process.Start() == false)
            throw new InvalidOperationException(
                $"Could not start {fileName}.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);

            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException(
                $"{fileName} exceeded {_timeout.TotalSeconds:g} seconds.");
        }

        return new CommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (process.HasExited == false)
                process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class GitPublisher(
    ICommandRunner commandRunner,
    GitHubCredentials credentials) : IGitPublisher
{
    private const int MaximumAttempts = 3;
    private const string CredentialHelper =
        "!f() { test \"$1\" = get || exit 0; host=; " +
        "while IFS= read -r line; do case \"$line\" in " +
        "host=*) host=\"${line#host=}\" ;; esac; done; " +
        "test \"$host\" = github.com || exit 0; " +
        "printf '%s\\n' " +
        "\"username=$GENERATOR_GITHUB_USERNAME\" " +
        "\"password=$GENERATOR_GITHUB_TOKEN\"; }; f";

    public async Task RestorePendingAsync(
        string projectRoot,
        IReadOnlyList<string> destinationPaths,
        CancellationToken cancellationToken)
    {
        var paths = GetRepositoryPaths(projectRoot, destinationPaths);

        if (paths.Count == 0)
            return;

        var gitDirectory = await GetGitDirectoryAsync(
            projectRoot,
            cancellationToken);
        using var repositoryLock = AcquireRepositoryLock(gitDirectory);
        EnsureNoGitOperation(gitDirectory);
        new PendingFeedStore(projectRoot, gitDirectory, paths).Restore();
    }

    public async Task<bool> PublishAsync(
        string projectRoot,
        IReadOnlyList<string> destinationPaths,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var paths = GetRepositoryPaths(projectRoot, destinationPaths);

        if (paths.Count == 0)
            return false;

        var gitDirectory = await GetGitDirectoryAsync(
            projectRoot,
            cancellationToken);
        using var repositoryLock = AcquireRepositoryLock(gitDirectory);
        EnsureNoGitOperation(gitDirectory);
        var pendingFeeds = new PendingFeedStore(
            projectRoot,
            gitDirectory,
            paths);
        var target = await GetTargetAsync(projectRoot, cancellationToken);
        Exception? lastError = null;
        var snapshotCreated = false;
        var committed = false;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                await FetchAsync(projectRoot, target, cancellationToken);

                if (snapshotCreated == false)
                {
                    var mergeBase = await GetOutputAsync(
                        "git merge-base",
                        ["merge-base", "HEAD", "@{upstream}"],
                        projectRoot,
                        cancellationToken);
                    await SnapshotPendingFilesAsync(
                        projectRoot,
                        pendingFeeds,
                        paths,
                        mergeBase,
                        cancellationToken);
                    snapshotCreated = true;
                }

                await EnsureUntrackedFilesAreSafeAsync(
                    projectRoot,
                    paths,
                    cancellationToken);
                EnsureSuccess(
                    "git reset",
                    await commandRunner.RunAsync(
                        "git",
                        ["reset", "--hard", "@{upstream}"],
                        projectRoot,
                        cancellationToken));
                pendingFeeds.Restore();
                committed |= await CommitAsync(
                    projectRoot,
                    paths,
                    timestamp,
                    cancellationToken);
                var commit = await GetOutputAsync(
                    "git rev-parse",
                    ["rev-parse", "HEAD"],
                    projectRoot,
                    cancellationToken);
                EnsureSuccess(
                    "git push",
                    await commandRunner.RunAsync(
                        "git",
                        ["push", target.Remote, "HEAD:" + target.MergeReference],
                        projectRoot,
                        cancellationToken,
                        CreateGitEnvironment()));
                await FetchAsync(projectRoot, target, cancellationToken);
                var verification = await commandRunner.RunAsync(
                    "git",
                    [
                        "merge-base",
                        "--is-ancestor",
                        commit,
                        "@{upstream}"
                    ],
                    projectRoot,
                    cancellationToken);

                if (verification.ExitCode == 1)
                    throw new InvalidOperationException(
                        "Remote verification did not contain the published commit.");

                EnsureSuccess("git merge-base", verification);
                pendingFeeds.Clear();
                return committed;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;

                if (attempt == MaximumAttempts)
                    break;
            }
        }

        throw new InvalidOperationException(
            $"Git publish failed after {MaximumAttempts} attempts: " +
            lastError?.Message,
            lastError);
    }

    private async Task<bool> CommitAsync(
        string projectRoot,
        IReadOnlyList<string> paths,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var addArguments = new List<string>(paths.Count + 2)
        {
            "add",
            "--"
        };
        addArguments.AddRange(paths);
        EnsureSuccess(
            "git add",
            await commandRunner.RunAsync(
                "git",
                addArguments,
                projectRoot,
                cancellationToken));
        var diffArguments = new List<string>(paths.Count + 5)
        {
            "diff",
            "--cached",
            "--quiet",
            "--exit-code",
            "--"
        };
        diffArguments.AddRange(paths);
        var diff = await commandRunner.RunAsync(
            "git",
            diffArguments,
            projectRoot,
            cancellationToken);

        if (diff.ExitCode == 0)
            return false;

        if (diff.ExitCode != 1)
            EnsureSuccess("git diff", diff);

        var message = "Content generated on " + timestamp
            .ToUniversalTime()
            .ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture);
        var commitArguments = new List<string>(paths.Count + 5)
        {
            "commit",
            "--only",
            "-m",
            message,
            "--"
        };
        commitArguments.AddRange(paths);
        EnsureSuccess(
            "git commit",
            await commandRunner.RunAsync(
                "git",
                commitArguments,
                projectRoot,
                cancellationToken));
        return true;
    }

    private async Task<GitTarget> GetTargetAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var branch = await GetOutputAsync(
            "git symbolic-ref",
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            projectRoot,
            cancellationToken);
        var remote = await GetOutputAsync(
            "git config",
            ["config", "--get", $"branch.{branch}.remote"],
            projectRoot,
            cancellationToken);
        var mergeReference = await GetOutputAsync(
            "git config",
            ["config", "--get", $"branch.{branch}.merge"],
            projectRoot,
            cancellationToken);

        if (mergeReference.StartsWith(
            "refs/heads/",
            StringComparison.Ordinal) == false)
            throw new InvalidOperationException(
                "The current branch upstream is not a branch.");

        return new GitTarget(remote, mergeReference);
    }

    private async Task FetchAsync(
        string projectRoot,
        GitTarget target,
        CancellationToken cancellationToken)
    {
        EnsureSuccess(
            "git fetch",
            await commandRunner.RunAsync(
                "git",
                ["fetch", "--prune", target.Remote],
                projectRoot,
                cancellationToken,
                CreateGitEnvironment()));
    }

    private async Task<string> GetGitDirectoryAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = await GetOutputAsync(
            "git rev-parse",
            ["rev-parse", "--absolute-git-dir"],
            projectRoot,
            cancellationToken);
        return Path.GetFullPath(path);
    }

    private async Task<string> GetOutputAsync(
        string operation,
        IReadOnlyList<string> arguments,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            "git",
            arguments,
            projectRoot,
            cancellationToken);
        EnsureSuccess(operation, result);
        var output = result.Output.Trim();

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException(operation + " returned no output.");

        return output;
    }

    private async Task SnapshotPendingFilesAsync(
        string projectRoot,
        PendingFeedStore pendingFeeds,
        IReadOnlyList<string> paths,
        string mergeBase,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < paths.Count; index++)
        {
            var path = paths[index];
            var diff = await commandRunner.RunAsync(
                "git",
                ["diff", "--quiet", "--exit-code", mergeBase, "--", path],
                projectRoot,
                cancellationToken);

            if (diff.ExitCode != 0 && diff.ExitCode != 1)
                EnsureSuccess("git diff", diff);

            var changed = diff.ExitCode == 1;

            if (changed == false)
            {
                var tracked = await commandRunner.RunAsync(
                    "git",
                    ["ls-files", "--error-unmatch", "--", path],
                    projectRoot,
                    cancellationToken);

                if (tracked.ExitCode != 0 && tracked.ExitCode != 1)
                    EnsureSuccess("git ls-files", tracked);

                changed = tracked.ExitCode == 1 &&
                    File.Exists(Path.Combine(projectRoot, path));
            }

            if (changed)
                pendingFeeds.Snapshot(path);
            else
                pendingFeeds.Clear(path);
        }
    }

    private async Task EnsureUntrackedFilesAreSafeAsync(
        string projectRoot,
        IReadOnlyList<string> feedPaths,
        CancellationToken cancellationToken)
    {
        var untracked = await GetNullSeparatedOutputAsync(
            "git ls-files",
            ["ls-files", "--others", "--exclude-standard", "-z"],
            projectRoot,
            cancellationToken);
        var upstream = await GetNullSeparatedOutputAsync(
            "git ls-tree",
            ["ls-tree", "-r", "--name-only", "-z", "@{upstream}"],
            projectRoot,
            cancellationToken);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        for (var untrackedIndex = 0;
            untrackedIndex < untracked.Count;
            untrackedIndex++)
        {
            var localPath = untracked[untrackedIndex];

            for (var upstreamIndex = 0;
                upstreamIndex < upstream.Count;
                upstreamIndex++)
            {
                var remotePath = upstream[upstreamIndex];

                if (PathsCollide(localPath, remotePath, comparison) == false)
                    continue;

                if (feedPaths.Contains(localPath, StringComparer.Ordinal))
                    continue;

                throw new InvalidOperationException(
                    "Untracked path would be replaced by upstream: " +
                    localPath);
            }
        }
    }

    private async Task<List<string>> GetNullSeparatedOutputAsync(
        string operation,
        IReadOnlyList<string> arguments,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            "git",
            arguments,
            projectRoot,
            cancellationToken);
        EnsureSuccess(operation, result);
        return result.Output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static bool PathsCollide(
        string first,
        string second,
        StringComparison comparison)
    {
        return first.Equals(second, comparison) ||
            first.StartsWith(second + "/", comparison) ||
            second.StartsWith(first + "/", comparison);
    }

    private static FileStream AcquireRepositoryLock(string gitDirectory)
    {
        try
        {
            return new FileStream(
                Path.Combine(gitDirectory, "generator.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Another generator is using this repository.",
                exception);
        }
    }

    private static void EnsureNoGitOperation(string gitDirectory)
    {
        string[] operationFiles =
        [
            "MERGE_HEAD",
            "CHERRY_PICK_HEAD",
            "REVERT_HEAD",
            "BISECT_LOG"
        ];

        for (var index = 0; index < operationFiles.Length; index++)
            if (File.Exists(Path.Combine(gitDirectory, operationFiles[index])))
                throw new InvalidOperationException(
                    "A Git operation is already active.");

        if (Directory.Exists(Path.Combine(gitDirectory, "rebase-apply")) ||
            Directory.Exists(Path.Combine(gitDirectory, "rebase-merge")) ||
            Directory.Exists(Path.Combine(gitDirectory, "sequencer")))
            throw new InvalidOperationException(
                "A Git operation is already active.");
    }

    private IReadOnlyDictionary<string, string> CreateGitEnvironment()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_CONFIG_COUNT"] = "2",
            ["GIT_CONFIG_KEY_0"] = "credential.helper",
            ["GIT_CONFIG_VALUE_0"] = "",
            ["GIT_CONFIG_KEY_1"] = "credential.helper",
            ["GIT_CONFIG_VALUE_1"] = CredentialHelper,
            ["GENERATOR_GITHUB_USERNAME"] = credentials.UserName,
            ["GENERATOR_GITHUB_TOKEN"] = credentials.PersonalAccessToken
        };
    }

    private sealed record GitTarget(
        string Remote,
        string MergeReference);

    private static List<string> GetRepositoryPaths(
        string projectRoot,
        IReadOnlyList<string> destinationPaths)
    {
        var root = Path.GetFullPath(projectRoot);
        var prefix = Path.TrimEndingDirectorySeparator(root) +
            Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var paths = new List<string>(destinationPaths.Count);

        for (var index = 0; index < destinationPaths.Count; index++)
        {
            var fullPath = Path.GetFullPath(destinationPaths[index]);

            if (fullPath.StartsWith(prefix, comparison) == false)
                throw new InvalidOperationException(
                    "Generated feed path escaped the repository root.");

            paths.Add(Path.GetRelativePath(root, fullPath));
        }

        return paths;
    }

    private static void EnsureSuccess(
        string operation,
        CommandResult result)
    {
        if (result.ExitCode == 0)
            return;

        var details = string.IsNullOrWhiteSpace(result.Error)
            ? result.Output
            : result.Error;

        throw new InvalidOperationException(
            $"{operation} failed with exit code {result.ExitCode}: " +
            details.Trim());
    }
}
