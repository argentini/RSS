namespace Generator.Tests;

[TestClass]
public sealed class TimingHistoryTests
{
    [TestMethod]
    public void Record_RecentSample_UsesWeightedAverageAndPersists()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.TempRoot, "timings.json");
        var history = TimingHistory.Load(path);

        history.Record(
            GenerationPhase.Resolve,
            TimeSpan.FromSeconds(10),
            2);
        history.Record(
            GenerationPhase.Resolve,
            TimeSpan.FromSeconds(20),
            2);
        history.Save();

        var reloaded = TimingHistory.Load(path);
        var estimate = reloaded.Estimate(GenerationPhase.Resolve, 2);

        Assert.AreEqual(
            TimeSpan.FromSeconds(13.5),
            estimate);
    }

    [TestMethod]
    public void Load_CorruptHistory_ReturnsEmptyHistory()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.TempRoot, "timings.json");
        Directory.CreateDirectory(workspace.TempRoot);
        File.WriteAllText(path, "not-json");

        var history = TimingHistory.Load(path);

        Assert.IsNull(history.Estimate(GenerationPhase.Hydrate));
    }
}
