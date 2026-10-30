using System.Net;
using System.Text;
using System.Text.Json;

namespace Generator.Tests;

[TestClass]
public sealed class LMStudioArticleHydratorTests
{
    [TestMethod]
    public async Task HydrateAsync_StreamedResponse_WritesEveryModelToken()
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
        using var output = new StringWriter();
        var hydrator = new LMStudioArticleHydrator(
            client,
            new GeneratorSettings
            {
                BaseUrl = client.BaseAddress,
                Model = "test-model",
                TimeoutMinutes = 1
            },
            output);
        var sourceUrl = new Uri("https://source.test/article");

        var item = await hydrator.HydrateAsync(
            "<html><body>Body</body></html>",
            sourceUrl,
            CancellationToken.None);

        Assert.AreEqual(
            "Streamed article",
            item["title"]?.GetValue<string>());
        Assert.AreEqual(sourceUrl.ToString(), item["url"]?.GetValue<string>());
        Assert.AreEqual(
            "Thinking " + firstContent + secondContent + Environment.NewLine,
            output.ToString());
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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "text/event-stream")
            });
        }
    }
}
