using System.Xml;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generator;

public sealed class WebFeedClient(HttpClient httpClient) : IFeedClient
{
    public async Task<string> DownloadAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<Uri> ResolveAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            "CONSENT=PENDING+987");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var resolvedUrl = response.RequestMessage?.RequestUri
            ?? throw new HttpRequestException(
                $"No resolved URL was returned for {url}.");

        if (IsGoogleNewsUrl(resolvedUrl) == false)
            return resolvedUrl;

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return await DecodeGoogleNewsUrlAsync(
            html,
            cancellationToken);
    }

    private async Task<Uri> DecodeGoogleNewsUrlAsync(
        string html,
        CancellationToken cancellationToken)
    {
        var articleId = ExtractAttribute(html, "data-n-a-id");
        var signature = ExtractAttribute(html, "data-n-a-sg");
        var timestampText = ExtractAttribute(html, "data-n-a-ts");

        if (long.TryParse(
            timestampText,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var timestamp) == false)
        {
            throw new HttpRequestException(
                "Google News returned an invalid decoding timestamp.");
        }

        var requestData = new object?[]
        {
            "garturlreq",
            new object?[]
            {
                new object?[]
                {
                    "X",
                    "X",
                    new object?[] { "X", "X" },
                    null,
                    null,
                    1,
                    1,
                    "US:en",
                    null,
                    1,
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    1
                },
                "X",
                "X",
                1,
                new object?[] { 1, 1, 1 },
                1,
                1,
                null,
                0,
                0,
                null,
                0
            },
            articleId,
            timestamp,
            signature
        };
        var innerJson = JsonSerializer.Serialize(requestData);
        var outerJson = JsonSerializer.Serialize(
            new object?[]
            {
                new object?[]
                {
                    new object?[] { "Fbv4je", innerJson }
                }
            });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://news.google.com/_/DotsSplashUi/data/batchexecute")
        {
            Content = new FormUrlEncodedContent(
                [new KeyValuePair<string, string>("f.req", outerJson)])
        };
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.Referrer = new Uri("https://news.google.com/");
        request.Headers.TryAddWithoutValidation(
            "Origin",
            "https://news.google.com");
        request.Headers.TryAddWithoutValidation("X-Same-Domain", "1");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync(
            cancellationToken);
        var decodedUrl = ParseDecodedUrl(responseText);

        if (Uri.TryCreate(
            decodedUrl,
            UriKind.Absolute,
            out var decoded) == false ||
            decoded.Scheme != Uri.UriSchemeHttp &&
            decoded.Scheme != Uri.UriSchemeHttps ||
            IsGoogleNewsUrl(decoded))
        {
            throw new HttpRequestException(
                "Google News returned an invalid source URL.");
        }

        return decoded;
    }

    private static string ExtractAttribute(
        string html,
        string attributeName)
    {
        var marker = attributeName + "=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0)
            throw new HttpRequestException(
                $"Google News omitted {attributeName}.");

        start += marker.Length;
        var end = html.IndexOf('"', start);

        if (end < 0)
            throw new HttpRequestException(
                $"Google News returned malformed {attributeName}.");

        return WebUtility.HtmlDecode(html[start..end]);
    }

    private static string ParseDecodedUrl(string response)
    {
        var lines = response.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.StartsWith("[", StringComparison.Ordinal) == false)
                continue;

            try
            {
                var root = JsonNode.Parse(line);
                var decodedUrl = FindDecodedUrl(root);

                if (decodedUrl is not null)
                    return decodedUrl;
            }
            catch (JsonException)
            {
            }
        }

        throw new HttpRequestException(
            "Google News source URL response was malformed.");
    }

    private static string? FindDecodedUrl(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            if (array.Count >= 3 &&
                TryGetString(array[0], out var frameType) &&
                frameType == "wrb.fr" &&
                TryGetString(array[1], out var rpcId) &&
                rpcId == "Fbv4je" &&
                array[2] is JsonValue innerValue &&
                innerValue.TryGetValue<string>(out var innerJson))
            {
                var inner = JsonNode.Parse(innerJson) as JsonArray;

                if (inner?.Count >= 2 &&
                    TryGetString(inner[0], out var responseType) &&
                    responseType == "garturlres" &&
                    inner[1] is JsonValue urlValue &&
                    urlValue.TryGetValue<string>(out var decodedUrl))
                    return decodedUrl;
            }

            for (var index = 0; index < array.Count; index++)
            {
                var decodedUrl = FindDecodedUrl(array[index]);

                if (decodedUrl is not null)
                    return decodedUrl;
            }
        }
        else if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject)
            {
                var decodedUrl = FindDecodedUrl(property.Value);

                if (decodedUrl is not null)
                    return decodedUrl;
            }
        }

        return null;
    }

    private static bool TryGetString(
        JsonNode? node,
        out string value)
    {
        value = "";

        if (node is not JsonValue jsonValue ||
            jsonValue.TryGetValue<string>(out var text) == false ||
            text is null)
            return false;

        value = text;
        return true;
    }

    private static bool IsGoogleNewsUrl(Uri url)
    {
        return url.Host.Equals(
            "news.google.com",
            StringComparison.OrdinalIgnoreCase) &&
            (url.AbsolutePath.Contains(
                "/articles/",
                StringComparison.Ordinal) ||
            url.AbsolutePath.Contains(
                "/read/",
                StringComparison.Ordinal));
    }
}

public static class GoogleNewsParser
{
    public static IReadOnlyList<Uri> ParseLatestLinks(
        string xml,
        int latestCount)
    {
        if (latestCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(latestCount));

        var links = new List<Uri>(latestCount);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            XmlResolver = null
        };
        using var textReader = new StringReader(xml);
        using var reader = XmlReader.Create(textReader, settings);

        while (reader.Read() && links.Count < latestCount)
        {
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "item")
                continue;

            using var itemReader = reader.ReadSubtree();

            while (itemReader.Read())
            {
                if (itemReader.NodeType != XmlNodeType.Element ||
                    itemReader.LocalName != "link")
                    continue;

                var value = itemReader.ReadElementContentAsString().Trim();

                if (Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out var link) &&
                    (link.Scheme == Uri.UriSchemeHttp ||
                    link.Scheme == Uri.UriSchemeHttps))
                {
                    links.Add(link);
                }

                break;
            }
        }

        return links;
    }
}

public static class NumberedFiles
{
    public static string Format(int index, int total)
    {
        if (index <= 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var width = Math.Max(2, total.ToString().Length);
        return index.ToString($"D{width}");
    }
}
