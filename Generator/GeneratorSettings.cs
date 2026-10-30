using System.Text.Json;

namespace Generator;

public sealed class GeneratorSettings
{
    public required Uri BaseUrl { get; init; }
    public required string Model { get; init; }
    public required int TimeoutMinutes { get; init; }

    public static GeneratorSettings Load(string settingsPath)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(settingsPath));
        var root = document.RootElement;

        if (root.TryGetProperty("LMStudio", out var section) == false)
            throw new InvalidOperationException(
                "LMStudio configuration is required.");

        var baseUrlValue = GetRequiredString(section, "BaseUrl");

        if (Uri.TryCreate(
            baseUrlValue,
            UriKind.Absolute,
            out var baseUrl) == false ||
            baseUrl.IsLoopback == false)
        {
            throw new InvalidOperationException(
                "LMStudio:BaseUrl must use a local loopback address.");
        }

        var timeoutMinutes = section.TryGetProperty(
            "TimeoutMinutes",
            out var timeoutValue)
            ? timeoutValue.GetInt32()
            : 60;

        if (timeoutMinutes <= 0)
            throw new InvalidOperationException(
                "LMStudio:TimeoutMinutes must exceed zero.");

        return new GeneratorSettings
        {
            BaseUrl = new Uri(baseUrl.ToString().TrimEnd('/') + "/"),
            Model = GetRequiredString(section, "Model"),
            TimeoutMinutes = timeoutMinutes
        };
    }

    private static string GetRequiredString(
        JsonElement section,
        string propertyName)
    {
        if (section.TryGetProperty(
            propertyName,
            out var property) == false ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException(
                $"LMStudio:{propertyName} is required.");
        }

        return property.GetString()
            ?? throw new InvalidOperationException(
                $"LMStudio:{propertyName} is required.");
    }
}
