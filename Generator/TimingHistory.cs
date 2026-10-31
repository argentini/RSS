using System.Text.Json;

namespace Generator;

public enum GenerationPhase
{
    Prepare,
    Download,
    Resolve,
    Render,
    Hydrate,
    Write,
    Publish
}

public sealed class TimingHistory
{
    private const double NewSampleWeight = 0.35;
    private readonly string path;
    private readonly Dictionary<GenerationPhase, TimingSample> samples;

    private TimingHistory(
        string path,
        Dictionary<GenerationPhase, TimingSample>? samples = null)
    {
        this.path = path;
        this.samples = samples ?? [];
    }

    public static TimingHistory Load(string path)
    {
        if (File.Exists(path) == false)
            return new TimingHistory(path);

        try
        {
            var state = JsonSerializer.Deserialize<TimingState>(
                File.ReadAllText(path));
            var samples = new Dictionary<GenerationPhase, TimingSample>();

            if (state?.Phases is not null)
            {
                foreach (var pair in state.Phases)
                {
                    if (Enum.TryParse<GenerationPhase>(
                        pair.Key,
                        out var phase) == false ||
                        double.IsFinite(pair.Value.SecondsPerUnit) == false ||
                        pair.Value.SecondsPerUnit <= 0 ||
                        pair.Value.Samples <= 0)
                        continue;

                    samples[phase] = pair.Value;
                }
            }

            return new TimingHistory(path, samples);
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return new TimingHistory(path);
        }
    }

    public TimeSpan? Estimate(
        GenerationPhase phase,
        int units = 1)
    {
        if (units <= 0 || samples.TryGetValue(
            phase,
            out var sample) == false)
            return null;

        return TimeSpan.FromSeconds(sample.SecondsPerUnit * units);
    }

    public TimeSpan? EstimateUnit(GenerationPhase phase)
    {
        return Estimate(phase);
    }

    public void Record(
        GenerationPhase phase,
        TimeSpan elapsed,
        int units = 1)
    {
        if (elapsed <= TimeSpan.Zero || units <= 0)
            return;

        var secondsPerUnit = elapsed.TotalSeconds / units;

        if (samples.TryGetValue(phase, out var existing))
        {
            secondsPerUnit = existing.SecondsPerUnit *
                (1 - NewSampleWeight) +
                secondsPerUnit * NewSampleWeight;
            samples[phase] = new TimingSample(
                secondsPerUnit,
                existing.Samples + 1);
        }
        else
        {
            samples[phase] = new TimingSample(secondsPerUnit, 1);
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(directory) == false)
                Directory.CreateDirectory(directory);

            var phases = new Dictionary<string, TimingSample>(samples.Count);

            foreach (var pair in samples)
                phases[pair.Key.ToString()] = pair.Value;

            var temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new TimingState { Phases = phases },
                    new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class TimingState
    {
        public Dictionary<string, TimingSample> Phases { get; init; } = [];
    }

    private sealed record TimingSample(
        double SecondsPerUnit,
        int Samples);
}
