namespace Generator;

public static class StartupRunPrompt
{
    public static bool Ask(
        IConsoleKeyReader keyReader,
        TextWriter output,
        CancellationTokenSource shutdown)
    {
        if (keyReader.CanRead == false)
        {
            output.WriteLine(
                "Input redirected; waiting for the next scheduled run.");
            return false;
        }

        while (shutdown.IsCancellationRequested == false)
        {
            output.Write("Run immediately? [Y/N] ");
            var key = keyReader.ReadKey().Key;
            output.WriteLine();

            if (key == ConsoleKey.Y)
                return true;

            if (key == ConsoleKey.N)
                return false;

            if (ConsoleKeyCommands.IsQuit(key))
            {
                shutdown.Cancel();
                return false;
            }
        }

        return false;
    }
}
