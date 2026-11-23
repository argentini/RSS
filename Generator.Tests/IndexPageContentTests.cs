namespace Generator.Tests;

[TestClass]
public sealed class IndexPageContentTests
{
    [TestMethod]
    public void Create_MultipleCards_PreservesContextAndRemovesExecutableContent()
    {
        var content = IndexPageContent.Create("""
            <html><head><meta http-equiv="Content-Security-Policy" content="script-src 'none'"></head>
            <body><nav><a href="/sections">Navigation</a></nav><main>
                <article><h2><a href="/first" onclick="run()">First report</a></h2></article>
                <section class="podcasts"><h2>Podcasts</h2><a href="/episode">Audio episode</a></section>
                <section class="sponsored"><a href="/promotion">Paid promotion</a></section>
                <article><h2><a href="/second">Second report</a></h2></article>
                <script>script marker</script><style>style marker</style>
                <iframe src="https://ads.test/"></iframe>
                <a hidden href="/hidden">Hidden link</a>
                <a href="/first" data-article-link="999">Duplicate headline</a>
            </main></body></html>
            """, Settings());

        Assert.HasCount(4, content.Links);
        Assert.AreEqual("https://source.test/first", content.Links[0].AbsoluteUri);
        Assert.AreEqual("https://source.test/second", content.Links[3].AbsoluteUri);
        Assert.Contains("First report", content.Html);
        Assert.Contains("Second report", content.Html);
        Assert.Contains("Podcasts", content.Html);
        Assert.Contains("sponsored", content.Html);
        Assert.Contains("data-article-link=\"0\"", content.Html);
        Assert.DoesNotContain("999", content.Html);
        Assert.DoesNotContain("onclick", content.Html);
        Assert.DoesNotContain("script marker", content.Html);
        Assert.DoesNotContain("style marker", content.Html);
        Assert.DoesNotContain("<iframe", content.Html);
        Assert.DoesNotContain("Navigation", content.Html);
        Assert.DoesNotContain("Hidden link", content.Html);
    }

    [TestMethod]
    public void Create_MixedLinks_NormalizesAndRestrictsCandidates()
    {
        var content = IndexPageContent.Create("""
            <body>
                <a href="relative?one=1&amp;two=2#section">Relative</a>
                <a href="relative?one=1&amp;two=2#other">Duplicate fragment</a>
                <a href="//www.source.test/absolute">Same publisher</a>
                <a href="https://news.source.test/report">Publisher subdomain</a>
                <a href="https://forums.source.test/post">Excluded host</a>
                <a href="https://elsewhere.test/article">Other site</a>
                <a href="https://source.test.elsewhere.test/article">Host suffix attack</a>
                <a href="javascript:run()">Script URL</a>
                <a href="mailto:desk@source.test">Email</a>
                <a href="https://user:password@source.test/private">Credentials</a>
                <a href="#content">Fragment</a>
                <a href="/news/">Index itself</a>
            </body>
            """, Settings());

        CollectionAssert.AreEqual(new[]
        {
            "https://source.test/news/relative?one=1&two=2",
            "https://www.source.test/absolute",
            "https://news.source.test/report"
        }, content.Links.Select(url => url.AbsoluteUri).ToArray());
        Assert.DoesNotContain("data-article-link=\"3\"", content.Html);
    }

    [TestMethod]
    public void Create_DocumentBase_ResolvesRelativeLinks()
    {
        var content = IndexPageContent.Create("""
            <head><base href="/reports/"></head>
            <body><a href="story">Report</a></body>
            """, Settings());

        Assert.AreEqual("https://source.test/reports/story", Assert.ContainsSingle(content.Links).AbsoluteUri);
    }

    [TestMethod]
    public void Create_NonHttpDocumentBase_UsesConfiguredPageUrl()
    {
        var content = IndexPageContent.Create("""
            <head><base href="javascript:invalid()"></head>
            <body onload="run()"><a href="story">Report</a></body>
            """, Settings());

        Assert.AreEqual("https://source.test/news/story", Assert.ContainsSingle(content.Links).AbsoluteUri);
        Assert.DoesNotContain("onload", content.Html);
    }

    private static FeedConfiguration Settings() => new()
    {
        BasePageUrl = "https://source.test/news/",
        UseIndexPage = true,
        ExcludedHosts = ["forums.source.test"]
    };
}
