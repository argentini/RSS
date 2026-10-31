namespace Generator;

public interface IConsoleKeyReader
{
    bool CanRead { get; }
    ConsoleKeyInfo ReadKey();
}

public sealed class SystemConsoleKeyReader : IConsoleKeyReader
{
    public bool CanRead => Console.IsInputRedirected == false;

    public ConsoleKeyInfo ReadKey()
    {
        return Console.ReadKey(true);
    }
}

internal static class ConsoleKeyCommands
{
    public static bool IsQuit(ConsoleKey key) =>
        key is ConsoleKey.Escape or ConsoleKey.Q;
}

public sealed class ConsoleExitMonitor(IConsoleKeyReader keyReader)
{
    public void Start(CancellationTokenSource shutdown)
    {
        if (keyReader.CanRead == false)
            return;

        _ = Task.Run(() => Listen(shutdown));
    }

    public void Listen(CancellationTokenSource shutdown)
    {
        try
        {
            while (shutdown.IsCancellationRequested == false)
            {
                var key = keyReader.ReadKey().Key;
                if (ConsoleKeyCommands.IsQuit(key) == false)
                    continue;

                shutdown.Cancel();
                return;
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                InvalidOperationException or
                ObjectDisposedException)
        {
        }
    }
}
