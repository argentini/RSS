namespace Generator.Tests;

[TestClass]
public sealed class HtmlPromptContentTests
{
    [TestMethod]
    public void Prepare_ScriptsAndStyles_RemovesNodesAndContent()
    {
        const string html = """
            <article>
                <p>Useful body.</p>
                <script>script marker</script>
                <style>.style-marker { color: red; }</style>
            </article>
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "Useful body.");
        Assert.IsFalse(prepared.Contains("script marker", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("style-marker", StringComparison.Ordinal));
        AssertElementsRemoved(prepared, "script", "style");
    }

    [TestMethod]
    public void Prepare_CommentsNoscriptAndTemplates_RemovesNodesAndContent()
    {
        const string html = """
            <article>
                <p>Useful body.</p>
                <!-- comment marker -->
                <noscript>noscript marker</noscript>
                <template><p>template marker</p></template>
            </article>
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "Useful body.");
        Assert.IsFalse(prepared.Contains("comment marker", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("noscript marker", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("template marker", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("<!--", StringComparison.Ordinal));
        AssertElementsRemoved(prepared, "noscript", "template");
    }

    [TestMethod]
    public void Prepare_LayoutNoise_RemovesSemanticRoleAndSidebarNodes()
    {
        const string html = """
            <article>
                <p>Useful body.</p>
                <nav>nav marker</nav>
                <aside>aside marker</aside>
                <footer>footer marker</footer>
                <dialog>dialog marker</dialog>
                <section role="navigation">role navigation marker</section>
                <section role="complementary">role sidebar marker</section>
                <section role="contentinfo">role footer marker</section>
                <section role="alertdialog">role dialog marker</section>
                <section class="right-sidebar">class sidebar marker</section>
                <section id="article-sidebar">id sidebar marker</section>
            </article>
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "Useful body.");
        Assert.IsFalse(prepared.Contains("marker", StringComparison.Ordinal));
        AssertElementsRemoved(prepared, "nav", "aside", "footer", "dialog", "section");
    }

    [TestMethod]
    public void Prepare_FormsAndControls_RemovesInteractiveNodes()
    {
        const string html = """
            <article>
                <p>Useful body.</p>
                <form><label>form marker<input value="form value"></label></form>
                <input type="hidden" value="hidden marker">
                <input value="input marker">
                <select><option>select marker</option></select>
                <textarea>textarea marker</textarea>
                <button>button marker</button>
            </article>
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "Useful body.");
        Assert.IsFalse(prepared.Contains("marker", StringComparison.Ordinal));
        Assert.IsFalse(prepared.Contains("form value", StringComparison.Ordinal));
        AssertElementsRemoved(
            prepared,
            "form",
            "input",
            "select",
            "textarea",
            "button");
    }

    [TestMethod]
    public void Prepare_EmbeddedNodes_RemovesNodesAndContent()
    {
        const string html = """
            <article>
                <p>Useful body.</p>
                <iframe>iframe marker</iframe>
                <canvas>canvas marker</canvas>
                <svg><text>svg marker</text></svg>
                <object><p>object marker</p></object>
                <embed src="embed-marker">
            </article>
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "Useful body.");
        Assert.IsFalse(prepared.Contains("marker", StringComparison.Ordinal));
        AssertElementsRemoved(
            prepared,
            "iframe",
            "canvas",
            "svg",
            "object",
            "embed");
    }

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

    [TestMethod]
    public void Prepare_MalformedArticle_KeepsUsefulContent()
    {
        const string html = """
            <article><h1>Useful title<p>Useful body<script>script marker
            """;

        var prepared = HtmlPromptContent.Prepare(html);

        StringAssert.Contains(prepared, "Useful title");
        StringAssert.Contains(prepared, "Useful body");
        Assert.IsFalse(prepared.Contains("script marker", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Prepare_EmptyBody_ThrowsInvalidOperationException()
    {
        const string html = """
            <html>
            <head><title>Source</title></head>
            <body><script>pageCode()</script></body>
            </html>
            """;

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => HtmlPromptContent.Prepare(html));

        StringAssert.Contains(exception.Message, "no visible content");
    }

    private static void AssertElementsRemoved(string html, params string[] names)
    {
        for (var index = 0; index < names.Length; index++)
            Assert.IsFalse(
                html.Contains(
                    "<" + names[index],
                    StringComparison.OrdinalIgnoreCase),
                names[index] + " node remains.");
    }
}
