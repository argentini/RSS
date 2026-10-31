using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generator.Tests;

[TestClass]
public sealed class JsonFeedFilesTests
{
    [TestMethod]
    public async Task WriteFeedAtomicAsync_InvalidFeed_PreservesDestination()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "feed.json");
        const string existing = "existing destination bytes";
        File.WriteAllText(path, existing);
        var invalidItem = CreateItem("one");
        invalidItem["authors"] = new JsonArray("Writer");
        var feed = JsonFeedFiles.CreateFeed(
            workspace.CreateSettings(),
            [invalidItem]);

        var exception = await Assert.ThrowsExactlyAsync<JsonException>(
            () => JsonFeedFiles.WriteFeedAtomicAsync(
                path,
                feed,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "Author 0 must be an object");
        Assert.AreEqual(existing, File.ReadAllText(path));
        Assert.IsEmpty(Directory.GetFiles(workspace.Root, "*.tmp"));
    }

    [TestMethod]
    public async Task WriteFeedAtomicAsync_MissingTitle_PreservesDestination()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "feed.json");
        const string existing = "existing destination bytes";
        File.WriteAllText(path, existing);
        var feed = JsonFeedFiles.CreateFeed(
            workspace.CreateSettings(),
            [CreateItem("one")]);
        feed.Remove("title");

        var exception = await Assert.ThrowsExactlyAsync<JsonException>(
            () => JsonFeedFiles.WriteFeedAtomicAsync(
                path,
                feed,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "title requires");
        Assert.AreEqual(existing, File.ReadAllText(path));
        Assert.IsEmpty(Directory.GetFiles(workspace.Root, "*.tmp"));
    }

    [TestMethod]
    public void Validate_DuplicateItemIdentifiers_ThrowsJsonException()
    {
        using var workspace = new TestWorkspace();
        var feed = JsonFeedFiles.CreateFeed(
            workspace.CreateSettings(),
            [CreateItem("same"), CreateItem("same")]);

        var exception = Assert.ThrowsExactly<JsonException>(
            () => JsonFeedValidator.Validate(feed));

        StringAssert.Contains(exception.Message, "duplicated");
    }

    [TestMethod]
    public void ValidateItem_MissingContent_ThrowsJsonException()
    {
        var item = CreateItem("one");
        item.Remove("content_html");

        var exception = Assert.ThrowsExactly<JsonException>(
            () => JsonFeedValidator.ValidateItem(item));

        StringAssert.Contains(exception.Message, "content_html or content_text");
    }

    [TestMethod]
    public void ValidateItem_MissingIdentifier_ThrowsJsonException()
    {
        var item = CreateItem("one");
        item.Remove("id");

        var exception = Assert.ThrowsExactly<JsonException>(
            () => JsonFeedValidator.ValidateItem(item));

        StringAssert.Contains(exception.Message, "id requires");
    }

    [TestMethod]
    public void ValidateItem_EmptyAuthorObject_ThrowsJsonException()
    {
        var item = CreateItem("one");
        item["authors"] = new JsonArray(new JsonObject());

        var exception = Assert.ThrowsExactly<JsonException>(
            () => JsonFeedValidator.ValidateItem(item));

        StringAssert.Contains(exception.Message, "requires name, url, or avatar");
    }

    [TestMethod]
    public void ValidateItem_AttachmentMissingMimeType_ThrowsJsonException()
    {
        var item = CreateItem("one");
        item["attachments"] = new JsonArray
        {
            new JsonObject
            {
                ["url"] = "https://source.test/image.jpg"
            }
        };

        var exception = Assert.ThrowsExactly<JsonException>(
            () => JsonFeedValidator.ValidateItem(item));

        StringAssert.Contains(exception.Message, "mime_type requires");
    }

    [TestMethod]
    public void ValidateItem_NonRfc3339Date_ThrowsJsonException()
    {
        var item = CreateItem("one");
        item["date_published"] = "October 30, 2026";

        var exception = Assert.ThrowsExactly<JsonException>(
            () => JsonFeedValidator.ValidateItem(item));

        StringAssert.Contains(exception.Message, "RFC 3339");
    }

    [TestMethod]
    public void ValidateItem_ObjectAuthor_AcceptsItem()
    {
        var item = CreateItem("one");

        JsonFeedValidator.ValidateItem(item);

        Assert.AreEqual(
            "Writer",
            item["authors"]?[0]?["name"]?.GetValue<string>());
    }

    private static JsonObject CreateItem(string identifier)
    {
        var url = "https://source.test/" + identifier;
        return new JsonObject
        {
            ["id"] = url,
            ["url"] = url,
            ["content_html"] = "<p>Body</p>",
            ["authors"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Writer"
                }
            }
        };
    }
}
