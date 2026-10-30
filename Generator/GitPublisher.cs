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
        CancellationToken cancellationToken);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
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

        using var process = new Process { StartInfo = startInfo };

        if (process.Start() == false)
            throw new InvalidOperationException(
                $"Could not start {fileName}.");

        var outputTask = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(
            cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}

public sealed class GitPublisher(ICommandRunner commandRunner) : IGitPublisher
{
    public async Task<bool> PublishAsync(
        string projectRoot,
        IReadOnlyList<string> destinationPaths,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var paths = GetRepositoryPaths(projectRoot, destinationPaths);
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
        var commitArguments = new List<string>(paths.Count + 4)
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
        EnsureSuccess(
            "git push",
            await commandRunner.RunAsync(
                "git",
                ["push"],
                projectRoot,
                cancellationToken));

        return true;
    }

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
