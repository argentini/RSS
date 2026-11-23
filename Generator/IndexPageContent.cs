using System.Globalization;
using HtmlAgilityPack;

namespace Generator;

internal sealed record IndexPageContent(string Html, IReadOnlyList<Uri> Links)
{
    private static readonly HashSet<string> RemovedElements = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "iframe",
        "object", "embed", "canvas", "nav", "aside", "footer", "base"
    };
    private static readonly HashSet<string> KeptAttributes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "href", "title", "aria-label", "role", "class", "id", "datetime",
        "rel", "itemprop", "itemtype", "alt", "data-testid"
    };

    public static IndexPageContent Create(
        string html,
        FeedConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        var pageUrl = new Uri(configuration.BasePageUrl);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var baseHref = document.DocumentNode.SelectSingleNode("//base[@href]")
            ?.GetAttributeValue("href", "");
        var linkBase = Uri.TryCreate(
            pageUrl,
            HtmlEntity.DeEntitize(baseHref ?? ""),
            out var documentBase) &&
            (documentBase.Scheme == Uri.UriSchemeHttp ||
                documentBase.Scheme == Uri.UriSchemeHttps)
            ? documentBase
            : pageUrl;
        var root = document.DocumentNode.SelectSingleNode("//body")
            ?? document.DocumentNode;
        var nodes = root.SelectNodes(". | .//* | .//comment()");

        if (nodes is not null)
            for (var index = nodes.Count - 1; index >= 0; index--)
            {
                var node = nodes[index];

                if (node.NodeType == HtmlNodeType.Comment ||
                    RemovedElements.Contains(node.Name) ||
                    node.Attributes["hidden"] is not null ||
                    node.GetAttributeValue("aria-hidden", "") == "true")
                {
                    node.Remove();
                    continue;
                }

                for (var attribute = node.Attributes.Count - 1; attribute >= 0; attribute--)
                    if (KeptAttributes.Contains(node.Attributes[attribute].Name) == false)
                        node.Attributes.RemoveAt(attribute);
            }

        var links = new List<Uri>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var anchors = root.SelectNodes(".//a[@href]");

        if (anchors is not null)
            for (var index = 0; index < anchors.Count; index++)
            {
                var anchor = anchors[index];
                var href = HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", "")).Trim();

                if (href.Length == 0 || href[0] == '#' ||
                    Uri.TryCreate(linkBase, href, out var url) == false ||
                    url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps ||
                    url.UserInfo.Length > 0 ||
                    IsSameSite(pageUrl, url) == false ||
                    configuration.ExcludesHost(url))
                {
                    anchor.Attributes.Remove("href");
                    continue;
                }

                url = new UriBuilder(url) { Fragment = "" }.Uri;

                if (url == pageUrl)
                {
                    anchor.Attributes.Remove("href");
                    continue;
                }

                if (ids.TryGetValue(url.AbsoluteUri, out var id) == false)
                {
                    id = links.Count;
                    ids.Add(url.AbsoluteUri, id);
                    links.Add(url);
                }

                anchor.SetAttributeValue("href", url.AbsoluteUri);
                anchor.SetAttributeValue("data-article-link", id.ToString(CultureInfo.InvariantCulture));
            }

        // Preserve every card and its surrounding labels, rather than choosing one article.
        return new IndexPageContent(root.OuterHtml, links);
    }

    private static bool IsSameSite(Uri pageUrl, Uri url)
    {
        var pageHost = NormalizeHost(pageUrl);
        var host = NormalizeHost(url);
        return host.Equals(pageHost, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + pageHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHost(Uri url)
    {
        var host = url.IdnHost.TrimEnd('.');
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..]
            : host;
    }
}
