using System.Net;
using System.Text;

namespace Generator.Tests;

[TestClass]
public sealed class WebFeedClientTests
{
    [TestMethod]
    public async Task ResolveAsync_GoogleWrapper_DecodesSourceUrl()
    {
        const string articleId = "encoded-article-id";
        const string html = """
            <html><body><div
              data-n-a-id="encoded-article-id"
              data-n-a-ts="1793396105"
              data-n-a-sg="signed-value"></div></body></html>
            """;
        const string batchResponse = """
            )]}'

            [["wrb.fr","Fbv4je","[\"garturlres\",\"https://source.test/article\"]",null,null,null,"generic"]]
            """;
        var handler = new SequencedHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(batchResponse)
            });
        using var httpClient = new HttpClient(handler);
        var client = new WebFeedClient(httpClient);

        var resolved = await client.ResolveAsync(
            new Uri(
                "https://news.google.com/rss/articles/" + articleId),
            CancellationToken.None);

        Assert.AreEqual("https://source.test/article", resolved.ToString());
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        StringAssert.Contains(handler.Requests[1].Body, "Fbv4je");
        StringAssert.Contains(handler.Requests[1].Body, articleId);
    }

    [TestMethod]
    public async Task ResolveAsync_HttpRedirect_ReturnsFinalRequestUrl()
    {
        var finalUrl = new Uri("https://source.test/final-article");
        var handler = new SequencedHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    finalUrl),
                Content = new StringContent("")
            });
        using var httpClient = new HttpClient(handler);
        var client = new WebFeedClient(httpClient);

        var resolved = await client.ResolveAsync(
            new Uri("https://redirect.test/article"),
            CancellationToken.None);

        Assert.AreEqual(finalUrl, resolved);
        Assert.HasCount(1, handler.Requests);
    }

    private sealed class SequencedHandler(
        params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                body));
            var response = responses[_index++];
            response.RequestMessage ??= request;
            return response;
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Url,
        string Body);
}
