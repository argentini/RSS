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
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
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

        if (environment is not null)
        {
            foreach (var variable in environment)
                startInfo.Environment[variable.Key] = variable.Value;
        }

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

public sealed class GitPublisher(
    ICommandRunner commandRunner,
    GitHubCredentials credentials) : IGitPublisher
{
    private const string CredentialHelper =
        "!f() { test \"$1\" = get || exit 0; host=; " +
        "while IFS= read -r line; do case \"$line\" in " +
        "host=*) host=\"${line#host=}\" ;; esac; done; " +
        "test \"$host\" = github.com || exit 0; " +
        "printf '%s\\n' " +
        "\"username=$GENERATOR_GITHUB_USERNAME\" " +
        "\"password=$GENERATOR_GITHUB_TOKEN\"; }; f";

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
                cancellationToken,
                CreatePushEnvironment()));

        return true;
    }

    private IReadOnlyDictionary<string, string> CreatePushEnvironment()
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
