using System.Xml;

namespace Generator.Tests;

[TestClass]
public sealed class GoogleNewsParserTests
{
    [TestMethod]
    public void ParseLatestLinks_ExtraItems_ReturnsLimitedFeedOrder()
    {
        const string xml = """
            <rss><channel>
              <item><link>https://news.test/first</link></item>
              <item><link>https://news.test/second</link></item>
              <item><link>https://news.test/third</link></item>
            </channel></rss>
            """;

        var links = GoogleNewsParser.ParseLatestLinks(xml, 2);

        Assert.HasCount(2, links);
        Assert.AreEqual("https://news.test/first", links[0].ToString());
        Assert.AreEqual("https://news.test/second", links[1].ToString());
    }

    [TestMethod]
    public void ParseLatestLinks_UnorderedDates_ReturnsNewestLimitedLinks()
    {
        const string xml = """
            <rss><channel>
              <item>
                <link>https://news.test/oldest</link>
                <pubDate>Fri, 30 Oct 2026 10:00:00 GMT</pubDate>
              </item>
              <item>
                <link>https://news.test/newest</link>
                <pubDate>Sun, 01 Nov 2026 12:00:00 GMT</pubDate>
              </item>
              <item>
                <link>https://news.test/middle</link>
                <pubDate>Sat, 31 Oct 2026 11:00:00 GMT</pubDate>
              </item>
            </channel></rss>
            """;

        var links = GoogleNewsParser.ParseLatestLinks(xml, 2);

        Assert.HasCount(2, links);
        Assert.AreEqual("https://news.test/newest", links[0].ToString());
        Assert.AreEqual("https://news.test/middle", links[1].ToString());
    }

    [TestMethod]
    public void ParseLatestLinks_EqualMissingInvalidDates_ReturnsStableOrder()
    {
        const string xml = """
            <rss><channel>
              <item>
                <link>https://news.test/invalid-first</link>
                <pubDate>not-a-date</pubDate>
              </item>
              <item>
                <link>https://news.test/tied-first</link>
                <pubDate>Sun, 01 Nov 2026 12:00:00 GMT</pubDate>
              </item>
              <item><link>https://news.test/missing</link></item>
              <item>
                <link>https://news.test/tied-second</link>
                <pubDate>Sun, 01 Nov 2026 12:00:00 GMT</pubDate>
              </item>
              <item>
                <link>https://news.test/invalid-second</link>
                <pubDate>still-not-a-date</pubDate>
              </item>
            </channel></rss>
            """;

        var links = GoogleNewsParser.ParseLatestLinks(xml, 5);

        Assert.HasCount(5, links);
        Assert.AreEqual("https://news.test/tied-first", links[0].ToString());
        Assert.AreEqual("https://news.test/tied-second", links[1].ToString());
        Assert.AreEqual("https://news.test/invalid-first", links[2].ToString());
        Assert.AreEqual("https://news.test/missing", links[3].ToString());
        Assert.AreEqual("https://news.test/invalid-second", links[4].ToString());
    }

    [TestMethod]
    public void ParseLatestLinks_DtdPayload_ThrowsXmlException()
    {
        const string xml = """
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <rss><channel><item><link>&xxe;</link></item></channel></rss>
            """;

        Assert.ThrowsExactly<XmlException>(
            () => GoogleNewsParser.ParseLatestLinks(xml, 1));
    }

    [TestMethod]
    [DataRow(1, 30, "01", DisplayName = "Two digit minimum")]
    [DataRow(30, 30, "30", DisplayName = "Two digit upper item")]
    [DataRow(100, 100, "100", DisplayName = "Three digit count")]
    public void Format_NumberAndTotal_ReturnsStableNumber(
        int index,
        int total,
        string expected)
    {
        Assert.AreEqual(expected, NumberedFiles.Format(index, total));
    }
}
