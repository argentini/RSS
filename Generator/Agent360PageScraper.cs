using System.Text.Json.Nodes;
using CliWrap;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Generator;

public sealed class Agent360PageScraper : IPageScraper, IAsyncDisposable
{
    private const int MaximumHtmlCharacters = 10_000_000;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private McpClient? _client;
    private McpClientTool? _navigate;
    private McpClientTool? _getPageContent;
    private McpClientTool? _closeTab;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            return;

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
        var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);
        var tools = await client.ListToolsAsync(
            cancellationToken: cancellationToken);

        _navigate = GetRequiredTool(tools, "browser_navigate");
        _getPageContent = GetRequiredTool(tools, "browser_get_page_content");
        _closeTab = GetRequiredTool(tools, "browser_close_tab");
        _client = client;
    }

    public async Task<string> GetContentAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await StartAsync(cancellationToken);
            var navigate = _navigate
                ?? throw new InvalidOperationException(
                    "Agent360 navigation tool is unavailable.");
            var getPageContent = _getPageContent
                ?? throw new InvalidOperationException(
                    "Agent360 page tool is unavailable.");
            var navigation = await CallAsync(
                navigate,
                new Dictionary<string, object?>
                {
                    ["url"] = url.ToString(),
                    ["new_tab"] = false
                },
                "navigation",
                cancellationToken);
            ValidateNavigation(navigation, url);
            await Task.Delay(1_000, cancellationToken);

            var page = await CallAsync(
                getPageContent,
                new Dictionary<string, object?>
                {
                    ["format"] = "html",
                    ["max_chars"] = MaximumHtmlCharacters
                },
                "page capture",
                cancellationToken);

            if (page["afkortet"]?.GetValue<bool>() == true)
                throw new InvalidOperationException(
                    $"Chrome DOM exceeded {MaximumHtmlCharacters} characters.");

            var html = page["content"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(html))
                throw new InvalidOperationException("Chrome returned an empty DOM.");

            return html;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_closeTab is not null)
        {
            try
            {
                await _closeTab.CallAsync();
            }
            catch
            {
            }
        }

        if (_client is not null)
            await _client.DisposeAsync();

        _lock.Dispose();
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
