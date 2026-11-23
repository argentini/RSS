using System.Net;
using System.Text.Json;

namespace Generator.Tests;

[TestClass]
public sealed class LMStudioIndexPageParserTests
{
    private const string IndexHtml = """
        <body><main>
            <article><a href="/first">First written report</a></article>
            <section class="podcasts"><a href="/episode">Podcast episode</a></section>
            <section class="sponsored"><a href="/promotion">Paid advertisement</a></section>
            <article><a href="/second">Second written report</a></article>
            <article><a href="/third">Third written report</a></article>
        </main></body>
        """;

    [TestMethod]
    [DataRow(1, DisplayName = "Below article count")]
    [DataRow(3, DisplayName = "Exact article count")]
    [DataRow(8, DisplayName = "Fewer articles than requested")]
    public async Task ParseAsync_SelectedArticles_ReturnsUniquePageOrderWithinLimit(int latestCount)
    {
        var handler = new ModelResponseHandler("{\"article_link_ids\":[4,0,3,0]}");
        using var client = CreateClient(handler);
        var parser = CreateParser(client);

        var links = await parser.ParseAsync(IndexHtml, Configuration(latestCount), CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "https://source.test/first", "https://source.test/second", "https://source.test/third" }
                .Take(latestCount).ToArray(),
            links.Select(url => url.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public async Task ParseAsync_Request_SuppliesSemanticContextAndConstrainedIds()
    {
        var handler = new ModelResponseHandler("{\"article_link_ids\":[0,3]}");
        using var client = CreateClient(handler);
        var parser = CreateParser(client);

        await parser.ParseAsync(IndexHtml, Configuration(), CancellationToken.None);

        Assert.AreEqual("/v1/chat/completions", handler.RequestPath);
        Assert.IsNotNull(handler.RequestContent);
        using var payload = JsonDocument.Parse(handler.RequestContent);
        var root = payload.RootElement;
        Assert.AreEqual("test-model", root.GetProperty("model").GetString());
        Assert.AreEqual("medium", root.GetProperty("reasoning_effort").GetString());
        Assert.IsTrue(root.GetProperty("stream").GetBoolean());
        var messages = root.GetProperty("messages");
        var systemPrompt = messages[0].GetProperty("content").GetString();
        Assert.IsNotNull(systemPrompt);
        Assert.Contains("untrusted data", systemPrompt);
        var prompt = messages[1].GetProperty("content").GetString();
        Assert.IsNotNull(prompt);
        Assert.Contains("written editorial news articles", prompt);
        Assert.Contains("advertisements", prompt);
        Assert.Contains("social posts", prompt);
        Assert.Contains("podcasts", prompt);
        Assert.Contains("surrounding card", prompt);
        Assert.Contains("Skip uncertain", prompt);
        Assert.Contains("First written report", prompt);
        Assert.Contains("Third written report", prompt);
        var format = root.GetProperty("response_format");
        Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
        var schema = format.GetProperty("json_schema");
        Assert.IsTrue(schema.GetProperty("strict").GetBoolean());
        var items = schema.GetProperty("schema").GetProperty("properties")
            .GetProperty("article_link_ids").GetProperty("items");
        Assert.AreEqual("integer", items.GetProperty("type").GetString());
        Assert.AreEqual(0, items.GetProperty("minimum").GetInt32());
        Assert.AreEqual(4, items.GetProperty("maximum").GetInt32());
    }

    [TestMethod]
    public async Task ParseAsync_NoSelectedArticles_ReturnsEmpty()
    {
        var handler = new ModelResponseHandler("{\"article_link_ids\":[]}");
        using var client = CreateClient(handler);

        var links = await CreateParser(client).ParseAsync(IndexHtml, Configuration(), CancellationToken.None);

        Assert.IsEmpty(links);
        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task ParseAsync_NoEligibleLinks_SkipsModel()
    {
        var handler = new ModelResponseHandler("unused");
        using var client = CreateClient(handler);

        var links = await CreateParser(client).ParseAsync(
            "<body><a href=\"https://elsewhere.test/post\">Social post</a></body>",
            Configuration(), CancellationToken.None);

        Assert.IsEmpty(links);
        Assert.AreEqual(0, handler.CallCount);
    }

    [TestMethod]
    [DataRow("{\"article_link_ids\":[99]}", DisplayName = "Invented link ID")]
    [DataRow("{\"article_link_ids\":[5]}", DisplayName = "First missing link ID")]
    [DataRow("{\"article_link_ids\":[-1]}", DisplayName = "Negative link ID")]
    [DataRow("{\"article_link_ids\":[0.5]}", DisplayName = "Fractional link ID")]
    [DataRow("{\"article_link_ids\":[\"0\"]}", DisplayName = "Non integer link ID")]
    [DataRow("{\"article_link_ids\":null}", DisplayName = "Null selection")]
    [DataRow("{}", DisplayName = "Missing selection")]
    [DataRow("[]", DisplayName = "Wrong root type")]
    [DataRow("invalid JSON", DisplayName = "Malformed model JSON")]
    public async Task ParseAsync_InvalidModelOutput_RejectsResponse(string response)
    {
        using var client = CreateClient(new ModelResponseHandler(response));

        await Assert.ThrowsAsync<JsonException>(async () =>
            await CreateParser(client).ParseAsync(IndexHtml, Configuration(), CancellationToken.None));
    }

    [TestMethod]
    public async Task ParseAsync_ModelHttpFailure_ReportsFailure()
    {
        using var client = CreateClient(new ModelResponseHandler("unavailable", HttpStatusCode.ServiceUnavailable));

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await CreateParser(client).ParseAsync(IndexHtml, Configuration(), CancellationToken.None));

        Assert.Contains("503", exception.Message);
    }

    [TestMethod]
    public async Task ParseAsync_CancellationDuringRequest_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new WaitingHandler();
        using var client = CreateClient(handler);
        var parse = CreateParser(client).ParseAsync(IndexHtml, Configuration(), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await parse);
    }

    private static FeedConfiguration Configuration(int latestCount = 3) => new()
    {
        BasePageUrl = "https://source.test/",
        UseIndexPage = true,
        LatestCount = latestCount
    };

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://127.0.0.1:1235/")
    };

    private static LMStudioIndexPageParser CreateParser(HttpClient client) => new(client, new GeneratorSettings
    {
        BaseUrl = client.BaseAddress ?? throw new AssertFailedException("Missing test base URL."),
        Model = "test-model",
        ReasoningEffort = "medium",
        TimeoutMinutes = 1,
        Schedule = []
    });

    private sealed class ModelResponseHandler(
        string response,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? RequestPath { get; private set; }
        public string? RequestContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestPath = request.RequestUri?.AbsolutePath;
            RequestContent = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var chunk = JsonSerializer.Serialize(new
            {
                choices = new[] { new { delta = new { content = response } } }
            });
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("data: " + chunk + "\n\ndata: [DONE]\n\n")
            };
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertFailedException("Request should be cancelled.");
        }
    }
}
