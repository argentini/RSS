using System.Diagnostics;

namespace Generator.Tests;

[TestClass]
public sealed class ProcessCommandRunnerTests
{
    [TestMethod]
    public async Task RunAsync_CancelledCommand_KillsProcessTree()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix process-tree test.");
            return;
        }

        using var workspace = new TestWorkspace();
        var childPath = Path.Combine(workspace.Root, "child.pid");
        using var cancellation = new CancellationTokenSource();
        var runner = new ProcessCommandRunner(TimeSpan.FromSeconds(10));
        var command = runner.RunAsync(
            "/bin/sh",
            [
                "-c",
                "sleep 30 & child=$!; echo $child > \"$1\"; wait",
                "generator-test",
                childPath
            ],
            workspace.Root,
            cancellation.Token);
        await WaitForFileAsync(childPath);
        var childIdentifier = int.Parse(
            await File.ReadAllTextAsync(childPath),
            System.Globalization.CultureInfo.InvariantCulture);

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => command);
        await WaitForExitAsync(childIdentifier);
        Assert.IsFalse(IsRunning(childIdentifier));
    }

    [TestMethod]
    public async Task RunAsync_CommandExceedsDeadline_ThrowsTimeoutException()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix command deadline test.");
            return;
        }

        using var workspace = new TestWorkspace();
        var runner = new ProcessCommandRunner(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => runner.RunAsync(
                "/bin/sh",
                ["-c", "sleep 30"],
                workspace.Root,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "exceeded");
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (File.Exists(path) == false)
            await Task.Delay(10, deadline.Token);
    }

    private static async Task WaitForExitAsync(int processIdentifier)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (IsRunning(processIdentifier))
            await Task.Delay(10, deadline.Token);
    }

    private static bool IsRunning(int processIdentifier)
    {
        try
        {
            using var process = Process.GetProcessById(processIdentifier);
            return process.HasExited == false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
