using System.Text.Json.Nodes;
using CliWrap;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Generator;

internal interface IAgent360BrowserSessionFactory
{
    Task<IAgent360BrowserSession> CreateAsync(
        CancellationToken cancellationToken);
}

internal interface IAgent360BrowserSession : IAsyncDisposable
{
    Task<JsonObject> NavigateAsync(
        Uri url,
        CancellationToken cancellationToken);

    Task<JsonObject> GetPageContentAsync(
        int maximumCharacters,
        CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}

public sealed class Agent360PageScraper : IPageScraper, IAsyncDisposable
{
    private const int MaximumAttempts = 2;
    private const int MaximumCaptureAttempts = 10;
    private const int MaximumHtmlCharacters = 10_000_000;
    private static readonly TimeSpan DefaultOperationTimeout =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumCleanupTimeout =
        TimeSpan.FromSeconds(10);
    private readonly IAgent360BrowserSessionFactory _sessionFactory;
    private readonly TimeSpan _operationTimeout;
    private readonly TimeSpan _cleanupTimeout;
    private readonly TimeSpan _pageSettleDelay;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IAgent360BrowserSession? _session;

    public Agent360PageScraper()
        : this(
            new Agent360BrowserSessionFactory(),
            DefaultOperationTimeout,
            TimeSpan.FromSeconds(1))
    {
    }

    internal Agent360PageScraper(
        IAgent360BrowserSessionFactory sessionFactory,
        TimeSpan operationTimeout,
        TimeSpan pageSettleDelay)
    {
        if (operationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));

        if (pageSettleDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pageSettleDelay));

        _sessionFactory = sessionFactory;
        _operationTimeout = operationTimeout;
        _pageSettleDelay = pageSettleDelay;
        _cleanupTimeout = operationTimeout < MaximumCleanupTimeout
            ? operationTimeout
            : MaximumCleanupTimeout;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await ExecuteWithReconnectAsync(
                static (_, _) => Task.FromResult(true),
                cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> GetContentAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            return await ExecuteWithReconnectAsync(
                async (session, operationToken) =>
                {
                    var navigation = await RunWithDeadlineAsync(
                        token => session.NavigateAsync(url, token),
                        "navigation",
                        operationToken);
                    ValidateNavigation(navigation, url);

                    for (var attempt = 1;
                        attempt <= MaximumCaptureAttempts;
                        attempt++)
                    {
                        await Task.Delay(_pageSettleDelay, operationToken);
                        var page = await RunWithDeadlineAsync(
                            token => session.GetPageContentAsync(
                                MaximumHtmlCharacters,
                                token),
                            "page capture",
                            operationToken);

                        if (page["afkortet"]?.GetValue<bool>() == true)
                            throw new InvalidOperationException(
                                $"Chrome DOM exceeded {MaximumHtmlCharacters} " +
                                "characters.");

                        var html = page["content"]?.GetValue<string>();

                        if (html is not null &&
                            HtmlPromptContent.HasVisibleContent(html))
                            return html;
                    }

                    throw new InvalidOperationException(
                        "Chrome returned no visible DOM content.");
                },
                cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();

        try
        {
            await ResetSessionAsync();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }

    private async Task<T> ExecuteWithReconnectAsync<T>(
        Func<IAgent360BrowserSession, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var session = await GetSessionAsync(cancellationToken);
                return await operation(session, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await ResetSessionAsync();
                throw;
            }
            catch
            {
                await ResetSessionAsync();

                if (attempt == MaximumAttempts)
                    throw;
            }
        }

        throw new InvalidOperationException("Agent360 retry limit was invalid.");
    }

    private async Task<IAgent360BrowserSession> GetSessionAsync(
        CancellationToken cancellationToken)
    {
        if (_session is not null)
            return _session;

        _session = await RunWithDeadlineAsync(
            _sessionFactory.CreateAsync,
            "connection",
            cancellationToken);
        return _session;
    }

    private async Task<T> RunWithDeadlineAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_operationTimeout);

        try
        {
            return await operation(deadline.Token).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested == false)
        {
            throw new TimeoutException(
                $"Agent360 {operationName} exceeded " +
                $"{_operationTimeout.TotalSeconds:g} seconds.");
        }
    }

    private async Task ResetSessionAsync()
    {
        var session = _session;
        _session = null;

        if (session is null)
            return;

        using var cleanup = new CancellationTokenSource(_cleanupTimeout);

        try
        {
            await session.CloseAsync(cleanup.Token).WaitAsync(_cleanupTimeout);
        }
        catch
        {
        }

        try
        {
            await session.DisposeAsync().AsTask().WaitAsync(_cleanupTimeout);
        }
        catch
        {
        }
    }

    private static void ValidateNavigation(JsonObject navigation, Uri url)
    {
        if (navigation["captcha_detected"] is JsonNode captcha)
            throw new HttpRequestException(
                $"Chrome detected CAPTCHA: {captcha.GetValue<string>()}.");

        if (navigation["ok"]?.GetValue<bool>() == false)
        {
            var error = navigation["error"]?.GetValue<string>()
                ?? "unknown navigation error";
            throw new HttpRequestException(
                $"Chrome could not load {url}: {error}.");
        }
    }
}

internal sealed class Agent360BrowserSessionFactory
    : IAgent360BrowserSessionFactory
{
    public async Task<IAgent360BrowserSession> CreateAsync(
        CancellationToken cancellationToken)
    {
        await OpenChromeAsync(cancellationToken);
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "Agent360 Browser MCP",
                Command = "npx",
                Arguments = ["--yes", "@agent360/browser-mcp@1.31.0"],
                InheritEnvironmentVariables = false,
                EnvironmentVariables =
                    StdioClientTransportOptions.GetDefaultEnvironmentVariables(),
                ShutdownTimeout = TimeSpan.FromSeconds(10),
                StandardErrorLines = _ => { }
            });
        McpClient? client = null;

        try
        {
            client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(
                cancellationToken: cancellationToken);
            return new Agent360BrowserSession(
                client,
                GetRequiredTool(tools, "browser_navigate"),
                GetRequiredTool(tools, "browser_get_page_content"),
                GetRequiredTool(tools, "browser_close_tab"));
        }
        catch
        {
            if (client is not null)
            {
                try
                {
                    await client.DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch
                {
                }
            }

            throw;
        }
    }

    private static async Task OpenChromeAsync(
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS() == false)
            throw new PlatformNotSupportedException(
                "Automatic Chrome startup currently requires macOS.");

        var result = await Cli.Wrap("/usr/bin/open")
            .WithArguments(arguments => arguments
                .Add("-a")
                .Add("Google Chrome"))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Chrome launch exited with {result.ExitCode}.");
    }

    private static McpClientTool GetRequiredTool(
        IList<McpClientTool> tools,
        string name)
    {
        for (var index = 0; index < tools.Count; index++)
            if (string.Equals(
                tools[index].Name,
                name,
                StringComparison.Ordinal))
                return tools[index];

        throw new InvalidOperationException(
            $"Agent360 does not provide {name}.");
    }
}

internal sealed class Agent360BrowserSession(
    McpClient client,
    McpClientTool navigate,
    McpClientTool getPageContent,
    McpClientTool closeTab) : IAgent360BrowserSession
{
    public Task<JsonObject> NavigateAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        return CallAsync(
            navigate,
            new Dictionary<string, object?>
            {
                ["url"] = url.ToString(),
                ["new_tab"] = false
            },
            "navigation",
            cancellationToken);
    }

    public Task<JsonObject> GetPageContentAsync(
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        return CallAsync(
            getPageContent,
            new Dictionary<string, object?>
            {
                ["format"] = "html",
                ["max_chars"] = maximumCharacters
            },
            "page capture",
            cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        await closeTab.CallAsync(cancellationToken: cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return client.DisposeAsync();
    }

    private static async Task<JsonObject> CallAsync(
        McpClientTool tool,
        IReadOnlyDictionary<string, object?>? arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await tool.CallAsync(
            arguments,
            cancellationToken: cancellationToken);
        var text = GetText(result);

        if (result.IsError == true)
            throw new HttpRequestException(
                $"Agent360 {operation} failed: {text}");

        try
        {
            return JsonNode.Parse(text)?.AsObject()
                ?? throw new InvalidOperationException(
                    $"Agent360 {operation} returned no JSON.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidOperationException(
                $"Agent360 {operation} returned invalid JSON.",
                exception);
        }
    }

    private static string GetText(CallToolResult result)
    {
        for (var index = 0; index < result.Content.Count; index++)
            if (result.Content[index] is TextContentBlock text)
                return text.Text;

        throw new InvalidOperationException(
            "Agent360 returned no text response.");
    }
}
