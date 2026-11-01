using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generator.Tests;

[TestClass]
public sealed class LMStudioArticleHydratorTests
{
    [TestMethod]
    [DataRow(true, "xhigh", DisplayName = "Reasoning enabled")]
    [DataRow(false, "none", DisplayName = "Reasoning disabled")]
    public async Task HydrateAsync_Request_MapsUseReasoningToReasoningEffort(
        bool useReasoning,
        string expectedEffort)
    {
        var stream = CreateEvent(
            "content",
            "{\"title\":\"Article\",\"date_published\":" +
            "\"2026-10-30T11:53:47Z\",\"content_html\":\"<p>Body</p>\"}") +
            "data: [DONE]\n\n";
        var handler = new StaticResponseHandler(stream);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:1235/")
        };
        var hydrator = new LMStudioArticleHydrator(
            client,
            new GeneratorSettings
            {
                BaseUrl = client.BaseAddress,
                Model = "test-model",
                UseReasoning = useReasoning,
                TimeoutMinutes = 1,
                Schedule = []
            });

        await hydrator.HydrateAsync(
            "<html><body>Body</body></html>",
            new Uri("https://source.test/article"),
            CancellationToken.None);

        Assert.IsNotNull(handler.RequestContent);
        using var payload = JsonDocument.Parse(handler.RequestContent);
        Assert.AreEqual(
            expectedEffort,
            payload.RootElement
                .GetProperty("reasoning_effort")
                .GetString());
    }

    [TestMethod]
    public async Task HydrateAsync_Request_PrefixesSystemPromptWithCavemanSkill()
    {
        var stream = CreateEvent(
            "content",
            "{\"title\":\"Article\",\"date_published\":" +
            "\"2026-10-30T11:53:47Z\",\"content_html\":\"<p>Body</p>\"}") +
            "data: [DONE]\n\n";
        var handler = new StaticResponseHandler(stream);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:1235/")
        };
        var hydrator = new LMStudioArticleHydrator(
            client,
            new GeneratorSettings
            {
                BaseUrl = client.BaseAddress,
                Model = "test-model",
                TimeoutMinutes = 1,
                Schedule = []
            });

        await hydrator.HydrateAsync(
            "<html><body>Body</body></html>",
            new Uri("https://source.test/article"),
            CancellationToken.None);

        Assert.IsNotNull(handler.RequestContent);
        using var payload = JsonDocument.Parse(handler.RequestContent);
        var systemPrompt = payload.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content")
            .GetString();

        Assert.IsNotNull(systemPrompt);
        Assert.StartsWith("/caveman\n", systemPrompt);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task HydrateAsync_StreamedResponse_BuffersTokensSilently()
    {
        const string firstContent =
            "{\"title\":\"Streamed article\",\"date_published\":" +
            "\"2026-10-30T11:53:47Z\",";
        const string secondContent =
            "\"content_html\":\"<p>Body</p>\"}";
        var stream = CreateEvent("reasoning", "Thinking ") +
            CreateEvent("content", firstContent) +
            CreateEvent("content", secondContent) +
            "data: [DONE]\n\n";
        using var client = new HttpClient(
            new StaticResponseHandler(stream))
        {
            BaseAddress = new Uri("http://127.0.0.1:1235/")
        };
        var originalOutput = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        var hydrator = new LMStudioArticleHydrator(
            client,
            new GeneratorSettings
            {
                BaseUrl = client.BaseAddress,
                Model = "test-model",
                TimeoutMinutes = 1,
                Schedule = []
            });
        var sourceUrl = new Uri("https://source.test/article");
        JsonObject item;

        try
        {
            item = await hydrator.HydrateAsync(
                "<html><body>Body</body></html>",
                sourceUrl,
                CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        Assert.AreEqual(
            "Streamed article",
            item["title"]?.GetValue<string>());
        Assert.AreEqual(sourceUrl.ToString(), item["url"]?.GetValue<string>());
        Assert.AreEqual(
            "<p>Body</p>",
            item["content_html"]?.GetValue<string>());
        Assert.AreEqual("", output.ToString());
    }

    private static string CreateEvent(string property, string value)
    {
        var payload = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    delta = new Dictionary<string, string>
                    {
                        [property] = value
                    }
                }
            }
        });

        return "data: " + payload + "\n\n";
    }

    private sealed class StaticResponseHandler(string content)
        : HttpMessageHandler
    {
        public string? RequestContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestContent = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "text/event-stream")
            };
        }
    }
}
