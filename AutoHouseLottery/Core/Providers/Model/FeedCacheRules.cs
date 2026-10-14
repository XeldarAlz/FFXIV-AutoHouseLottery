using System.Text;
using System.Text.Json;

namespace AutoHouseLottery.Core.Providers.Model;

// What a saved response must be to be replayed: this schema, the world asked for, a body and both receipt times, and
// young enough. Anything else is ignored and the listings are fetched again.
internal static class FeedCacheRules
{
    public const int MaximumAgeDays = 7;
    public const int WorldListMaximumAgeDays = 30;

    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(MaximumAgeDays);
    public static readonly TimeSpan WorldListMaximumAge = TimeSpan.FromDays(WorldListMaximumAgeDays);

    public static bool TryAccept(ReadOnlySpan<byte> json, uint worldId, TimeSpan maximumAge, DateTime nowUtc, out FeedFetch fetch)
    {
        fetch = default;
        FeedCacheFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, FeedJsonContext.Default.FeedCacheFile);
        }
        catch (JsonException)
        {
            return false;
        }

        if (file is not { Schema: FeedCacheFile.CurrentSchema, Body: { } body, ReceivedUtc: { } received, UpstreamUtc: { } upstream }
            || file.WorldId != worldId
            || received > nowUtc
            || nowUtc - received > maximumAge)
        {
            return false;
        }

        fetch = new FeedFetch(FetchStatus.Success, Encoding.UTF8.GetBytes(body), 0, null, null, new FeedReceipt(received, upstream));
        return true;
    }

    public static byte[] Envelope(uint worldId, byte[] body, in FeedReceipt receipt)
    {
        var file = new FeedCacheFile
        {
            Schema = FeedCacheFile.CurrentSchema,
            WorldId = worldId,
            ReceivedUtc = receipt.ReceivedUtc,
            UpstreamUtc = receipt.UpstreamUtc,
            Body = Encoding.UTF8.GetString(body),
        };
        return JsonSerializer.SerializeToUtf8Bytes(file, FeedJsonContext.Default.FeedCacheFile);
    }
}
