using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generator;

public static class JsonFeedFiles
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

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

    public static async Task WriteAtomicAsync(
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
}
