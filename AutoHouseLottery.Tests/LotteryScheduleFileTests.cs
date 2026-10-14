using System.IO;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class LotteryScheduleFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ahl-schedule-" + Guid.NewGuid().ToString("N"));
    private readonly string path;

    public LotteryScheduleFileTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "schedules.json");
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void CorrectedScheduleSurvivesARestart()
    {
        var schedules = LotterySchedules.Create([]);
        schedules.Observe(GameRegion.Global, LotteryPhase.Entry, CycleStart.AddDays(14).AddHours(20), out _);
        schedules.Observe(GameRegion.China, LotteryPhase.Results, CycleStart.AddHours(-7), out _);

        Assert.True(LotteryScheduleFile.TrySave(path, schedules.All.AsSpan()));
        Assert.True(LotteryScheduleFile.TryLoad(path, out var loaded));
        var restored = LotterySchedules.Create(loaded.AsSpan());

        Assert.True(restored.TryGet(GameRegion.Global, out var global));
        Assert.True(schedules.TryGet(GameRegion.Global, out var expected));
        Assert.Equal(expected, global);
        Assert.Equal(DateTimeKind.Utc, global.AnchorEntryStartUtc.Kind);
        Assert.True(restored.TryGet(GameRegion.China, out _));
    }

    [Fact]
    public void MissingFileLoadsNothingAndMalformedFileFails()
    {
        Assert.True(LotteryScheduleFile.TryLoad(path, out var nothing));
        Assert.Empty(nothing);

        File.WriteAllText(path, "{\"schema\":1,\"schedules\":[{\"region\":\"Unknown\"}]}");
        Assert.False(LotteryScheduleFile.TryLoad(path, out _));
    }
}
