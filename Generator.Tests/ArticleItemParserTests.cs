using System.Text.Json;

namespace Generator.Tests;

[TestClass]
public sealed class ArticleItemParserTests
{
    [TestMethod]
    public void Parse_ImageResponse_ForcesCanonicalUrlsAndPrependsPreview()
    {
        const string response = """
            ```json
            {
              "title": "Article title",
              "date_published": "2026-10-30T11:53:47Z",
              "id": "https://wrong.test/id",
              "url": "https://wrong.test/url",
              "external_url": "https://wrong.test/external",
              "authors": ["Writer"],
              "content_html": "<p>Full article body.</p>",
              "image": "https://source.test/preview.jpg"
            }
            ```
            """;
        var sourceUrl = new Uri("https://source.test/article");

        var item = ArticleItemParser.Parse(response, sourceUrl);

        Assert.AreEqual(sourceUrl.ToString(), item["id"]?.GetValue<string>());
        Assert.AreEqual(sourceUrl.ToString(), item["url"]?.GetValue<string>());
        Assert.AreEqual(
            sourceUrl.ToString(),
            item["external_url"]?.GetValue<string>());
        Assert.StartsWith(
            "<img src=\"https://source.test/preview.jpg\"",
            item["content_html"]?.GetValue<string>());
    }

    [TestMethod]
    public void Parse_AttachmentImage_UsesAttachmentAsPreview()
    {
        const string response = """
            {
              "title": "Article title",
              "date_published": "2026-10-30T11:53:47Z",
              "content_html": "<p>Full article body.</p>",
              "attachments": [
                {
                  "url": "https://source.test/audio.mp3",
                  "mime_type": "audio/mpeg"
                },
                {
                  "url": "https://source.test/preview.png",
                  "mime_type": "image/png"
                }
              ]
            }
            """;

        var item = ArticleItemParser.Parse(
            response,
            new Uri("https://source.test/article"));

        Assert.AreEqual(
            "https://source.test/preview.png",
            item["image"]?.GetValue<string>());
        Assert.StartsWith(
            "<img src=\"https://source.test/preview.png\"",
            item["content_html"]?.GetValue<string>());
    }

    [TestMethod]
    public void Parse_ImageAfterParagraph_MovesPreviewToContentStart()
    {
        const string response = """
            {
              "title": "Article title",
              "date_published": "2026-10-30T11:53:47Z",
              "content_html": "<p>Introduction</p><img src=\"https://source.test/preview.jpg\">",
              "image": "https://source.test/preview.jpg"
            }
            """;

        var item = ArticleItemParser.Parse(
            response,
            new Uri("https://source.test/article"));

        Assert.StartsWith(
            "<img src=\"https://source.test/preview.jpg\" alt=\"\"><p>",
            item["content_html"]?.GetValue<string>());
    }

    [TestMethod]
    public void Parse_InvalidPublishedDate_ThrowsJsonException()
    {
        const string response = """
            {
              "title": "Article title",
              "date_published": "sometime",
              "content_html": "<p>Body</p>"
            }
            """;

        var exception = Assert.ThrowsExactly<JsonException>(
            () => ArticleItemParser.Parse(
                response,
                new Uri("https://source.test/article")));

        StringAssert.Contains(exception.Message, "date_published");
    }
}
