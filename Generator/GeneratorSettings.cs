using System.Globalization;
using System.Text.Json;

namespace Generator;

public sealed class GeneratorSettings
{
    public required Uri BaseUrl { get; init; }
    public required string Model { get; init; }
    public required int TimeoutMinutes { get; init; }
    public required IReadOnlyList<ScheduleItem> Schedule { get; init; }

    public static GeneratorSettings Load(string settingsPath)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(settingsPath));
        var root = document.RootElement;

        if (root.TryGetProperty("LMStudio", out var section) == false)
            throw new InvalidOperationException(
                "LMStudio configuration is required.");

        var baseUrlValue = GetRequiredString(
            section,
            "BaseUrl",
            "LMStudio");

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
            Model = GetRequiredString(section, "Model", "LMStudio"),
            TimeoutMinutes = timeoutMinutes,
            Schedule = LoadSchedule(root)
        };
    }

    private static IReadOnlyList<ScheduleItem> LoadSchedule(
        JsonElement root)
    {
        if (root.TryGetProperty("Schedule", out var section) == false ||
            section.ValueKind != JsonValueKind.Array ||
            section.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Schedule must contain at least one item.");
        }

        var schedule = new List<ScheduleItem>(section.GetArrayLength());
        var uniqueItems = new HashSet<ScheduleItem>();
        var index = 0;

        foreach (var item in section.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"Schedule[{index}] must be an object.");

            var dayValue = GetRequiredString(
                item,
                "DayOfWeek",
                $"Schedule[{index}]");
            var timeValue = GetRequiredString(
                item,
                "TimeOfDay",
                $"Schedule[{index}]");

            if (Enum.TryParse<DayOfWeek>(
                dayValue,
                true,
                out var dayOfWeek) == false ||
                Enum.IsDefined(dayOfWeek) == false)
            {
                throw new InvalidOperationException(
                    $"Schedule[{index}]:DayOfWeek is invalid.");
            }

            if (TimeOnly.TryParseExact(
                timeValue,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var timeOfDay) == false)
            {
                throw new InvalidOperationException(
                    $"Schedule[{index}]:TimeOfDay must use HH:mm.");
            }

            var scheduleItem = new ScheduleItem(dayOfWeek, timeOfDay);

            if (uniqueItems.Add(scheduleItem) == false)
                throw new InvalidOperationException(
                    $"Schedule[{index}] duplicates another item.");

            schedule.Add(scheduleItem);
            index++;
        }

        schedule.Sort(static (left, right) =>
        {
            var dayComparison = left.DayOfWeek.CompareTo(right.DayOfWeek);
            return dayComparison != 0
                ? dayComparison
                : left.TimeOfDay.CompareTo(right.TimeOfDay);
        });
        return schedule;
    }

    private static string GetRequiredString(
        JsonElement section,
        string propertyName,
        string sectionName)
    {
        if (section.TryGetProperty(
            propertyName,
            out var property) == false ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException(
                $"{sectionName}:{propertyName} is required.");
        }

        return property.GetString()
            ?? throw new InvalidOperationException(
                $"{sectionName}:{propertyName} is required.");
    }
}

public readonly record struct ScheduleItem(
    DayOfWeek DayOfWeek,
    TimeOnly TimeOfDay);
