namespace Generator.Tests;

[TestClass]
public sealed class StartupRunPromptTests
{
    [TestMethod]
    [DataRow(
        ConsoleKey.Y,
        true,
        DisplayName = "Yes runs immediately")]
    [DataRow(
        ConsoleKey.N,
        false,
        DisplayName = "No waits for schedule")]
    public void Ask_YesOrNo_ReturnsSelectedChoice(
        ConsoleKey key,
        bool expected)
    {
        var keyReader = new QueueConsoleKeyReader(key);
        using var output = new StringWriter();
        using var shutdown = new CancellationTokenSource();

        var actual = StartupRunPrompt.Ask(
            keyReader,
            output,
            shutdown);

        Assert.AreEqual(expected, actual);
        Assert.IsFalse(shutdown.IsCancellationRequested);
        StringAssert.Contains(output.ToString(), "[Y/N]");
    }

    [TestMethod]
    [DataRow(
        ConsoleKey.Escape,
        DisplayName = "Escape quits")]
    [DataRow(
        ConsoleKey.Q,
        DisplayName = "Q quits")]
    public void Ask_QuitKeyPressed_CancelsShutdown(
        ConsoleKey quitKey)
    {
        var keyReader = new QueueConsoleKeyReader(quitKey);
        using var output = new StringWriter();
        using var shutdown = new CancellationTokenSource();

        var runImmediately = StartupRunPrompt.Ask(
            keyReader,
            output,
            shutdown);

        Assert.IsFalse(runImmediately);
        Assert.IsTrue(shutdown.IsCancellationRequested);
    }

    private sealed class QueueConsoleKeyReader(
        params ConsoleKey[] keys) : IConsoleKeyReader
    {
        private int index;

        public bool CanRead => true;

        public ConsoleKeyInfo ReadKey()
        {
            var key = keys[index++];
            return new ConsoleKeyInfo(
                '\0',
                key,
                false,
                false,
                false);
        }
    }
}
