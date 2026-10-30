using System.Text;
using System.Text.RegularExpressions;

namespace Generator;

public static class HtmlPromptContent
{
    private const RegexOptions ElementOptions =
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Singleline;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex ArticleElement = new(
        @"<article\b[^>]*>.*?</article\s*>",
        ElementOptions,
        MatchTimeout);

    private static readonly Regex MainElement = new(
        @"<main\b[^>]*>.*?</main\s*>",
        ElementOptions,
        MatchTimeout);

    private static readonly Regex Metadata = new(
        @"<title\b[^>]*>.*?</title\s*>|<meta\b[^>]*>",
        ElementOptions,
        MatchTimeout);

    private static readonly Regex Comments = new(
        @"<!--.*?-->",
        ElementOptions,
        MatchTimeout);

    private static readonly Regex TagWhitespace = new(
        @">\s+<",
        ElementOptions,
        MatchTimeout);

    private static readonly Regex[] RemovedElements =
    [
        CreateElementRegex("script"),
        CreateElementRegex("style"),
        CreateElementRegex("noscript"),
        CreateElementRegex("svg"),
        CreateElementRegex("template"),
        CreateElementRegex("iframe"),
        CreateElementRegex("video"),
        CreateElementRegex("audio"),
        CreateElementRegex("canvas"),
        CreateElementRegex("form"),
        CreateElementRegex("button"),
        CreateElementRegex("dialog"),
        CreateElementRegex("nav"),
        CreateElementRegex("aside"),
        CreateElementRegex("footer")
    ];

    public static string Prepare(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        var content = FindLargest(ArticleElement, html);

        if (content.Length == 0)
            content = FindLargest(MainElement, html);

        if (content.Length == 0)
            content = html;

        var prepared = new StringBuilder(content.Length);

        foreach (Match match in Metadata.Matches(html))
            prepared.Append(match.Value);

        prepared.Append(Clean(content));
        return TagWhitespace.Replace(prepared.ToString(), "><").Trim();
    }

    private static string Clean(string html)
    {
        var cleaned = Comments.Replace(html, string.Empty);

        for (var index = 0; index < RemovedElements.Length; index++)
            cleaned = RemovedElements[index].Replace(cleaned, string.Empty);

        return cleaned;
    }

    private static string FindLargest(Regex expression, string html)
    {
        var result = string.Empty;

        foreach (Match match in expression.Matches(html))
        {
            if (match.Length > result.Length)
                result = match.Value;
        }

        return result;
    }

    private static Regex CreateElementRegex(string element)
    {
        return new Regex(
            $@"<{element}\b[^>]*>.*?</{element}\s*>",
            ElementOptions,
            MatchTimeout);
    }
}
