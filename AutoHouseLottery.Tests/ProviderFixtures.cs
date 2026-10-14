using System.Globalization;
using System.IO;
using System.Text;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;

namespace AutoHouseLottery.Tests;

internal static class ProviderFixtures
{
    public const uint SampleWorld = 73;
    public const string SampleFile = "global-world-73-2026-10-14T1143Z.json";

    public static readonly DateTime SampleReceived = new(2026, 10, 14, 11, 43, 18, DateTimeKind.Utc);
    public static readonly DateTime SampleUpstream = new(2026, 10, 14, 11, 40, 18, DateTimeKind.Utc);
    public static readonly FeedReceipt SampleReceipt = new(SampleReceived, SampleUpstream);

    public static readonly DateTime EntryEnd = HousingFixtures.CycleStart + LotteryCalendar.EntryPeriod;
    public static readonly FeedReceipt Receipt = new(HousingFixtures.ObservedAt.AddHours(1), HousingFixtures.ObservedAt.AddMinutes(50));

    public static byte[] SampleBody() => File.ReadAllBytes(Path.Combine(RepositoryFiles.Root, "fixtures", "housing-service", SampleFile));

    public static string Row(
        int ward,
        int plot,
        uint world = HousingFixtures.HomeWorld,
        uint district = HousingDistricts.Mist,
        string entries = "3",
        string phase = "1",
        string purchase = "7",
        string price = "3187500",
        string size = "0",
        string? updated = null,
        string? phaseEnd = null)
    {
        updated ??= Seconds(HousingFixtures.ObservedAt);
        phaseEnd ??= Seconds(EntryEnd).Split('.')[0];
        return string.Create(CultureInfo.InvariantCulture,
            $$"""{"world_id":{{world}},"district_id":{{district}},"ward_number":{{ward}},"plot_number":{{plot}},"size":{{size}},"price":{{price}},"last_updated_time":{{updated}},"first_seen_time":{{updated}},"purchase_system":{{purchase}},"lotto_entries":{{entries}},"lotto_phase":{{phase}},"lotto_phase_until":{{phaseEnd}}}""");
    }

    public static byte[] World(uint world, params string[] mistRows) => World(world, mistRows.Length, mistRows);

    public static byte[] World(uint world, int listedCount, string[] mistRows)
    {
        var json = string.Create(CultureInfo.InvariantCulture,
            $$"""{"id":{{world}},"name":"Fixture","num_open_plots":{{listedCount}},"districts":[{"id":339,"name":"Mist","num_open_plots":{{listedCount}},"open_plots":[{{string.Join(',', mistRows)}}]}]}""");
        return Encoding.UTF8.GetBytes(json);
    }

    public static FeedFetch Fetched(byte[] body, FeedReceipt? receipt = null) => new(FetchStatus.Success, body, 200, null, null, receipt ?? Receipt);

    public static string Seconds(DateTime utc) => ((utc - DateTime.UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond).ToString("0.###", CultureInfo.InvariantCulture);
}
