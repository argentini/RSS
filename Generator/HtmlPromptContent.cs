using System.Text;
using HtmlAgilityPack;

namespace Generator;

public static class HtmlPromptContent
{
    private static readonly HashSet<string> RemovedElements = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "script",
        "style",
        "noscript",
        "template",
        "nav",
        "aside",
        "footer",
        "dialog",
        "form",
        "input",
        "select",
        "textarea",
        "button",
        "fieldset",
        "legend",
        "option",
        "optgroup",
        "datalist",
        "output",
        "iframe",
        "canvas",
        "svg",
        "object",
        "embed",
        "video",
        "audio"
    };

    private static readonly HashSet<string> RemovedRoles = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "navigation",
        "complementary",
        "contentinfo",
        "dialog",
        "alertdialog"
    };

    public static string Prepare(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        Clean(document.DocumentNode);
        var content = FindLargest(document.DocumentNode, "article")
            ?? FindLargest(document.DocumentNode, "main")
            ?? document.DocumentNode;
        var prepared = new StringBuilder(content.OuterHtml.Length);

        if (ReferenceEquals(content, document.DocumentNode) == false)
            AppendMetadata(document.DocumentNode, prepared);

        prepared.Append(content.OuterHtml);
        return prepared.ToString().Trim();
    }

    private static void AppendMetadata(HtmlNode root, StringBuilder content)
    {
        var nodes = root.SelectNodes(".//title | .//meta");

        if (nodes is null)
            return;

        for (var index = 0; index < nodes.Count; index++)
            content.Append(nodes[index].OuterHtml);
    }

    private static void Clean(HtmlNode root)
    {
        var nodes = root.SelectNodes(".//* | .//comment()");

        if (nodes is null)
            return;

        for (var index = nodes.Count - 1; index >= 0; index--)
            if (ShouldRemove(nodes[index]))
                nodes[index].Remove();
    }

    private static bool ShouldRemove(HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Comment)
            return true;

        if (RemovedElements.Contains(node.Name))
            return true;

        if (HasRemovedRole(node))
            return true;

        return HasSidebarMarker(node);
    }

    private static bool HasRemovedRole(HtmlNode node)
    {
        var roles = node
            .GetAttributeValue("role", string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < roles.Length; index++)
            if (RemovedRoles.Contains(roles[index]))
                return true;

        return false;
    }

    private static bool HasSidebarMarker(HtmlNode node)
    {
        return node
                .GetAttributeValue("class", string.Empty)
                .Contains("sidebar", StringComparison.OrdinalIgnoreCase)
            || node
                .GetAttributeValue("id", string.Empty)
                .Contains("sidebar", StringComparison.OrdinalIgnoreCase);
    }

    private static HtmlNode? FindLargest(HtmlNode root, string name)
    {
        var nodes = root.SelectNodes(".//" + name);

        if (nodes is null)
            return null;

        HtmlNode? result = null;
        var largestLength = 0;

        for (var index = 0; index < nodes.Count; index++)
        {
            var length = nodes[index].OuterHtml.Length;

            if (length <= largestLength)
                continue;

            result = nodes[index];
            largestLength = length;
        }

        return result;
    }
}
