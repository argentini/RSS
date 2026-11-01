using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generator;

internal sealed record FeedBuildResult(
    JsonObject? Feed,
    string Message)
{
    public bool Success => Feed is not null;

    public int ItemCount => Feed?["items"] is JsonArray items
        ? items.Count
        : 0;
}

public static class JsonFeedFiles
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    internal static FeedBuildResult BuildFeed(
        FeedConfiguration configuration,
        IReadOnlyList<JsonObject> freshItems,
        string destinationPath)
    {
        List<JsonObject> priorItems;
        var priorDepth = 0;

        try
        {
            priorItems = LoadPriorItems(destinationPath, out priorDepth);

            for (var index = 0; index < freshItems.Count; index++)
                JsonFeedValidator.ValidateItem(freshItems[index]);
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or JsonException)
        {
            return new FeedBuildResult(
                null,
                "Candidate feed rejected: " + exception.Message);
        }

        var targetDepth = Math.Max(configuration.LatestCount, priorDepth);
        var mergedItems = new List<JsonObject>(targetDepth);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        AddUniqueItems(freshItems, targetDepth, mergedItems, identifiers);
        AddUniqueItems(priorItems, targetDepth, mergedItems, identifiers);

        if (mergedItems.Count < targetDepth)
            return new FeedBuildResult(
                null,
                $"Candidate feed has {mergedItems.Count} valid items; " +
                $"{targetDepth} required. Existing feed preserved.");

        var feed = CreateFeed(configuration, mergedItems);

        try
        {
            JsonFeedValidator.Validate(feed);
            return new FeedBuildResult(
                feed,
                $"wrote {mergedItems.Count} items");
        }
        catch (JsonException exception)
        {
            return new FeedBuildResult(
                null,
                "Candidate feed rejected: " + exception.Message);
        }
    }

    public static JsonObject CreateFeed(
        FeedConfiguration configuration,
        IReadOnlyList<JsonObject> items)
    {
        var array = new JsonArray();

        for (var index = 0; index < items.Count; index++)
            array.Add(items[index].DeepClone());

        return new JsonObject
        {
            ["version"] = "https://jsonfeed.org/version/1.1",
            ["title"] = configuration.FeedName,
            ["home_page_url"] = configuration.FeedHomePageUrl,
            ["feed_url"] = configuration.FeedUrl,
            ["icon"] = configuration.FeedIconUrl,
            ["favicon"] = configuration.FeedFaviconUrl,
            ["items"] = array
        };
    }

    public static async Task WriteAsync(
        string path,
        JsonNode json,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            path,
            json.ToJsonString(SerializerOptions) + Environment.NewLine,
            cancellationToken);
    }

    public static Task WriteFeedAtomicAsync(
        string path,
        JsonObject feed,
        CancellationToken cancellationToken)
    {
        JsonFeedValidator.Validate(feed);
        return WriteAtomicAsync(path, feed, cancellationToken);
    }

    private static async Task WriteAtomicAsync(
        string path,
        JsonNode json,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await WriteAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static List<JsonObject> LoadPriorItems(
        string destinationPath,
        out int depth)
    {
        depth = 0;

        if (File.Exists(destinationPath) == false)
            return [];

        var feed = JsonNode.Parse(File.ReadAllText(destinationPath)) as JsonObject
            ?? throw new JsonException("Existing feed must be a JSON object.");
        NormalizeLegacyValues(feed);
        JsonFeedValidator.Validate(feed);
        var items = feed["items"]?.AsArray()
            ?? throw new JsonException("Existing feed requires items.");
        depth = items.Count;
        var result = new List<JsonObject>(depth);

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonObject item)
                throw new JsonException(
                    $"Existing feed item {index} must be an object.");

            result.Add((JsonObject)item.DeepClone());
        }

        return result;
    }

    private static void AddUniqueItems(
        IReadOnlyList<JsonObject> source,
        int maximumCount,
        List<JsonObject> destination,
        HashSet<string> identifiers)
    {
        for (var index = 0;
            index < source.Count && destination.Count < maximumCount;
            index++)
        {
            var item = source[index];
            var identifier = item["id"]?.GetValue<string>()
                ?? throw new JsonException("Feed item requires id.");

            if (identifiers.Add(identifier))
                destination.Add((JsonObject)item.DeepClone());
        }
    }

    private static void NormalizeLegacyValues(JsonObject feed)
    {
        NormalizeAuthors(feed);

        if (feed["items"] is not JsonArray items)
            return;

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonObject item)
                continue;

            NormalizeAuthors(item);
            NormalizeImage(item);
        }
    }

    private static void NormalizeAuthors(JsonObject container)
    {
        if (container["authors"] is not JsonArray authors)
            return;

        for (var index = 0; index < authors.Count; index++)
        {
            if (authors[index] is not JsonValue value ||
                value.TryGetValue<string>(out var name) == false ||
                string.IsNullOrWhiteSpace(name))
                continue;

            authors[index] = new JsonObject
            {
                ["name"] = name
            };
        }
    }

    private static void NormalizeImage(JsonObject item)
    {
        if (item["image"] is not JsonObject image ||
            image["url"] is not JsonValue value ||
            value.TryGetValue<string>(out var url) == false ||
            string.IsNullOrWhiteSpace(url))
            return;

        item["image"] = url;
    }
}

internal static class JsonFeedValidator
{
    private const string Version = "https://jsonfeed.org/version/1.1";

    public static void Validate(JsonObject feed)
    {
        if (GetRequiredString(feed, "version") != Version)
            throw new JsonException("Feed version must be JSON Feed 1.1.");

        GetRequiredString(feed, "title");
        ValidateOptionalUri(feed, "home_page_url");
        ValidateOptionalUri(feed, "feed_url");
        ValidateOptionalString(feed, "description");
        ValidateOptionalString(feed, "user_comment");
        ValidateOptionalUri(feed, "next_url");
        ValidateOptionalUri(feed, "icon");
        ValidateOptionalUri(feed, "favicon");
        ValidateOptionalString(feed, "language");
        ValidateOptionalBoolean(feed, "expired");
        ValidateAuthors(feed);
        ValidateHubs(feed);

        if (feed["items"] is not JsonArray items)
            throw new JsonException("Feed items must be an array.");

        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonObject item)
                throw new JsonException($"Feed item {index} must be an object.");

            ValidateItem(item);
            var identifier = GetRequiredString(item, "id");

            if (identifiers.Add(identifier) == false)
                throw new JsonException(
                    $"Feed item id is duplicated: {identifier}");
        }
    }

    public static void ValidateItem(JsonObject item)
    {
        GetRequiredString(item, "id");
        ValidateOptionalUri(item, "url");
        ValidateOptionalUri(item, "external_url");
        ValidateOptionalString(item, "title");
        ValidateOptionalString(item, "summary");
        ValidateOptionalUri(item, "image");
        ValidateOptionalUri(item, "banner_image");
        ValidateOptionalDate(item, "date_published");
        ValidateOptionalDate(item, "date_modified");
        ValidateOptionalString(item, "language");
        ValidateAuthors(item);
        ValidateTags(item);
        ValidateAttachments(item);

        if (IsString(item["content_html"]) == false &&
            IsString(item["content_text"]) == false)
            throw new JsonException(
                "Feed item requires content_html or content_text.");
    }

    private static void ValidateAuthors(JsonObject container)
    {
        if (container.ContainsKey("authors") == false)
            return;

        if (container["authors"] is not JsonArray authors || authors.Count == 0)
            throw new JsonException("Authors must be a nonempty array.");

        for (var index = 0; index < authors.Count; index++)
        {
            if (authors[index] is not JsonObject author)
                throw new JsonException($"Author {index} must be an object.");

            ValidateOptionalString(author, "name");
            ValidateOptionalUri(author, "url");
            ValidateOptionalUri(author, "avatar");

            if (HasNonemptyString(author, "name") == false &&
                HasNonemptyString(author, "url") == false &&
                HasNonemptyString(author, "avatar") == false)
                throw new JsonException(
                    $"Author {index} requires name, url, or avatar.");
        }
    }

    private static void ValidateHubs(JsonObject feed)
    {
        if (feed.ContainsKey("hubs") == false)
            return;

        if (feed["hubs"] is not JsonArray hubs)
            throw new JsonException("Feed hubs must be an array.");

        for (var index = 0; index < hubs.Count; index++)
        {
            if (hubs[index] is not JsonObject hub)
                throw new JsonException($"Hub {index} must be an object.");

            GetRequiredString(hub, "type");
            GetRequiredUri(hub, "url");
        }
    }

    private static void ValidateTags(JsonObject item)
    {
        if (item.ContainsKey("tags") == false)
            return;

        if (item["tags"] is not JsonArray tags)
            throw new JsonException("Item tags must be an array.");

        for (var index = 0; index < tags.Count; index++)
            if (IsString(tags[index]) == false)
                throw new JsonException($"Item tag {index} must be a string.");
    }

    private static void ValidateAttachments(JsonObject item)
    {
        if (item.ContainsKey("attachments") == false)
            return;

        if (item["attachments"] is not JsonArray attachments)
            throw new JsonException("Item attachments must be an array.");

        for (var index = 0; index < attachments.Count; index++)
        {
            if (attachments[index] is not JsonObject attachment)
                throw new JsonException(
                    $"Attachment {index} must be an object.");

            GetRequiredUri(attachment, "url");
            GetRequiredString(attachment, "mime_type");
            ValidateOptionalString(attachment, "title");
            ValidateOptionalNumber(attachment, "size_in_bytes");
            ValidateOptionalNumber(attachment, "duration_in_seconds");
        }
    }

    private static string GetRequiredString(
        JsonObject container,
        string propertyName)
    {
        if (TryGetString(container[propertyName], out var value) == false ||
            string.IsNullOrWhiteSpace(value))
            throw new JsonException($"{propertyName} requires a string value.");

        return value;
    }

    private static void GetRequiredUri(
        JsonObject container,
        string propertyName)
    {
        var value = GetRequiredString(container, propertyName);

        if (Uri.TryCreate(value, UriKind.Absolute, out _) == false)
            throw new JsonException($"{propertyName} requires an absolute URI.");
    }

    private static void ValidateOptionalString(
        JsonObject container,
        string propertyName)
    {
        if (container.ContainsKey(propertyName) &&
            IsString(container[propertyName]) == false)
            throw new JsonException($"{propertyName} must be a string.");
    }

    private static void ValidateOptionalUri(
        JsonObject container,
        string propertyName)
    {
        if (container.ContainsKey(propertyName) == false)
            return;

        GetRequiredUri(container, propertyName);
    }

    private static void ValidateOptionalDate(
        JsonObject container,
        string propertyName)
    {
        if (container.ContainsKey(propertyName) == false)
            return;

        var value = GetRequiredString(container, propertyName);

        if (DateTimeOffset.TryParseExact(
            value,
            [
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _) == false &&
            DateTimeOffset.TryParseExact(
                value,
                [
                    "yyyy-MM-dd'T'HH:mm:sszzz",
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
                ],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _) == false)
            throw new JsonException($"{propertyName} must be an RFC 3339 date.");
    }

    private static void ValidateOptionalBoolean(
        JsonObject container,
        string propertyName)
    {
        if (container.ContainsKey(propertyName) &&
            container[propertyName]?.GetValueKind() != JsonValueKind.True &&
            container[propertyName]?.GetValueKind() != JsonValueKind.False)
            throw new JsonException($"{propertyName} must be a boolean.");
    }

    private static void ValidateOptionalNumber(
        JsonObject container,
        string propertyName)
    {
        if (container.ContainsKey(propertyName) &&
            container[propertyName]?.GetValueKind() != JsonValueKind.Number)
            throw new JsonException($"{propertyName} must be a number.");
    }

    private static bool HasNonemptyString(
        JsonObject container,
        string propertyName)
    {
        return TryGetString(container[propertyName], out var value) &&
            string.IsNullOrWhiteSpace(value) == false;
    }

    private static bool IsString(JsonNode? node)
    {
        return TryGetString(node, out _);
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        value = "";

        if (node is not JsonValue jsonValue ||
            jsonValue.TryGetValue<string>(out var text) == false ||
            text is null)
            return false;

        value = text;
        return true;
    }
}
