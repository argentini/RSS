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
