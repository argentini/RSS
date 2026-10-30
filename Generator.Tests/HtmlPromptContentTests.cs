namespace Generator.Tests;

[TestClass]
public sealed class HtmlPromptContentTests
{
    [TestMethod]
    public void Prepare_ArticlesAndPageNoise_KeepsLargestArticleAndMetadata()
    {
        const string html = """
            <html>
            <head>
                <title>Useful story</title>
                <meta property="og:image" content="https://source.test/image.jpg">
                <script>largePageCode()</script>
            </head>
            <body>
                <article><p>Short card.</p></article>
                <article>
                    <h1>Useful story</h1>
                    <img src="https://source.test/image.jpg">
                    <p>Complete meaningful article body.</p>
                    <aside>Unrelated story.</aside>
                    <script>largeArticleCode()</script>
                </article>
            </body>
            </html>
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "<title>Useful story</title>");
        StringAssert.Contains(prepared, "property=\"og:image\"");
        StringAssert.Contains(prepared, "Complete meaningful article body.");
        Assert.IsFalse(prepared.Contains("Short card.", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("Unrelated story.", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("largePageCode", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("largeArticleCode", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Prepare_PageWithoutArticle_KeepsLargestMainElement()
    {
        const string html = """
            <html><head><title>Main story</title></head><body>
            <main><h1>Main story</h1><p>Full body.</p></main>
            <script>pageCode()</script>
            </body></html>
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "<main>");
        StringAssert.Contains(prepared, "Full body.");
        Assert.IsFalse(prepared.Contains("pageCode", StringComparison.Ordinal));
    }
}
