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
}
