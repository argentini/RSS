namespace Generator.Tests;

[TestClass]
public sealed class HydrationPromptTests
{
    [TestMethod]
    public void Create_RequestsObjectAuthors()
    {
        var prompt = HydrationPrompt.Create("<article>Body</article>");

        StringAssert.Contains(prompt, "Represent every author as an object");
        StringAssert.Contains(prompt, "\"name\": \"Mike Scarcella\"");
        Assert.DoesNotContain(
            "\"authors\": [\n        \"Mike Scarcella\"",
            prompt);
    }

    [TestMethod]
    public void Create_OmitsApplicationOwnedUrlFields()
    {
        var prompt = HydrationPrompt.Create("<article>Body</article>");

        Assert.DoesNotContain("\"id\":", prompt);
        Assert.DoesNotContain("\"url\":", prompt);
        Assert.DoesNotContain("\"external_url\":", prompt);
        Assert.DoesNotContain("article URLs should be replaced", prompt);
    }
}
