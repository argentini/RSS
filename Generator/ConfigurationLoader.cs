using System.Text.Json;

namespace Generator;

public static class ConfigurationLoader
{
    public static ConfigurationLoadResult LoadAll(
        GeneratorContext context,
        TextWriter error)
    {
        if (Directory.Exists(context.ConfigurationRoot) == false)
            throw new DirectoryNotFoundException(
                $"Configuration folder not found: {context.ConfigurationRoot}");

        var paths = Directory.GetFiles(
            context.ConfigurationRoot,
            "*.json",
            SearchOption.TopDirectoryOnly);

        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

        if (paths.Length == 0)
            throw new InvalidOperationException(
                $"No configuration files found in {context.ConfigurationRoot}.");

        var configurations = new List<ConfigurationFile>(paths.Length);
        var failures = 0;

        for (var index = 0; index < paths.Length; index++)
        {
            try
            {
                configurations.Add(Load(paths[index], context));
            }
            catch (Exception exception)
            {
                failures++;
                error.WriteLine(
                    $"Skipped configuration {Path.GetFileName(paths[index])}: " +
                    exception.Message);
            }
        }

        return new ConfigurationLoadResult(configurations, failures);
    }

    public static ConfigurationFile Load(
        string path,
        GeneratorContext context)
    {
        var settings = JsonSerializer.Deserialize<FeedConfiguration>(
            File.ReadAllText(path))
            ?? throw new InvalidOperationException(
                "Configuration JSON is empty.");

        Validate(settings);

        var destinationPath = Path.GetFullPath(
            Path.IsPathRooted(settings.DestinationRssFilePath)
                ? settings.DestinationRssFilePath
                : Path.Combine(
                    context.GeneratorRoot,
                    settings.DestinationRssFilePath));
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(context.ProjectRoot));

        if (PathEquals(destinationDirectory, root) == false)
            throw new InvalidOperationException(
                "DestinationRssFilePath must target the repository root.");

        return new ConfigurationFile(
            Path.GetFileNameWithoutExtension(path),
            path,
            settings,
            destinationPath);
    }

    private static void Validate(FeedConfiguration settings)
    {
        EnsureAbsoluteHttpUrl(settings.GoogleNewsUrl, "GoogleNewsUrl");

        if (settings.LatestCount <= 0)
            throw new InvalidOperationException(
                "LatestCount must exceed zero.");

        EnsureRequired(
            settings.DestinationRssFilePath,
            "DestinationRssFilePath");
        EnsureRequired(settings.FeedName, "FeedName");
        EnsureAbsoluteHttpUrl(settings.FeedHomePageUrl, "FeedHomePageUrl");
        EnsureAbsoluteHttpUrl(settings.FeedUrl, "FeedUrl");
        EnsureAbsoluteHttpUrl(settings.FeedIconUrl, "FeedIconUrl");
        EnsureAbsoluteHttpUrl(settings.FeedFaviconUrl, "FeedFaviconUrl");
    }

    private static void EnsureRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }

    private static void EnsureAbsoluteHttpUrl(string value, string name)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) == false ||
            uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{name} must be an absolute HTTP URL.");
        }
    }

    private static bool PathEquals(string? left, string right)
    {
        if (left is null)
            return false;

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            right,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
