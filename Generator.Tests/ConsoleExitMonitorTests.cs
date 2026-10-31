namespace Generator.Tests;

[TestClass]
public sealed class ConsoleExitMonitorTests
{
    [TestMethod]
    [DataRow(
        ConsoleKey.Escape,
        DisplayName = "Escape quits")]
    [DataRow(
        ConsoleKey.Q,
        DisplayName = "Q quits")]
    public void Listen_QuitKeyPressed_CancelsShutdown(
        ConsoleKey quitKey)
    {
        var keyReader = new QueueConsoleKeyReader(
            ConsoleKey.A,
            quitKey);
        var monitor = new ConsoleExitMonitor(keyReader);
        using var shutdown = new CancellationTokenSource();

        monitor.Listen(shutdown);

        Assert.IsTrue(shutdown.IsCancellationRequested);
        Assert.AreEqual(2, keyReader.ReadCount);
    }

    private sealed class QueueConsoleKeyReader(
        params ConsoleKey[] keys) : IConsoleKeyReader
    {
        private int index;

        public bool CanRead => true;
        public int ReadCount => index;

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
