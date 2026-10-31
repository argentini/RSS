namespace Generator.Tests;

[TestClass]
public sealed class GitHubCredentialsTests
{
    [TestMethod]
    public void Load_ValidFile_ReturnsGitHubCredentials()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "Credentials.json");
        File.WriteAllText(
            path,
            """
            {
              "Github": {
                "UserName": "octocat",
                "PersonalAccessToken": "test-token"
              }
            }
            """);

        var credentials = GitHubCredentialsLoader.Load(path);

        Assert.AreEqual("octocat", credentials.UserName);
        Assert.AreEqual(
            "test-token",
            credentials.PersonalAccessToken);
    }

    [TestMethod]
    [DataRow(
        "{\"Github\":{\"UserName\":\"\",\"PersonalAccessToken\":\"test-token\"}}",
        "Github.UserName",
        DisplayName = "Missing username")]
    [DataRow(
        "{\"Github\":{\"UserName\":\"octocat\",\"PersonalAccessToken\":\"\"}}",
        "Github.PersonalAccessToken",
        DisplayName = "Missing token")]
    public void Load_RequiredValueMissing_ThrowsInvalidOperationException(
        string json,
        string expectedName)
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "Credentials.json");
        File.WriteAllText(path, json);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => GitHubCredentialsLoader.Load(path));

        StringAssert.Contains(exception.Message, expectedName);
    }

    [TestMethod]
    public void Load_FileMissing_ThrowsFileNotFoundException()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "Credentials.json");

        var exception = Assert.ThrowsExactly<FileNotFoundException>(
            () => GitHubCredentialsLoader.Load(path));

        Assert.AreEqual(path, exception.FileName);
    }
}
