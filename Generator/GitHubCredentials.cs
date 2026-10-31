using System.Text.Json;

namespace Generator;

public sealed class GitHubCredentials
{
    public string UserName { get; init; } = "";
    public string PersonalAccessToken { get; init; } = "";
}

public static class GitHubCredentialsLoader
{
    public static GitHubCredentials Load(string path)
    {
        if (File.Exists(path) == false)
            throw new FileNotFoundException(
                "Credentials file was not found.",
                path);

        var document = JsonSerializer.Deserialize<CredentialsDocument>(
            File.ReadAllText(path))
            ?? throw new InvalidOperationException(
                "Credentials JSON is empty.");
        var credentials = document.Github
            ?? throw new InvalidOperationException(
                "Github credentials are required.");

        EnsureRequired(credentials.UserName, "Github.UserName");
        EnsureRequired(
            credentials.PersonalAccessToken,
            "Github.PersonalAccessToken");
        return credentials;
    }

    private static void EnsureRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }

    private sealed class CredentialsDocument
    {
        public GitHubCredentials? Github { get; init; }
    }
}
