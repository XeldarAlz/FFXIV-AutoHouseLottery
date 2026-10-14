using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AutoHouseLottery.Core.Providers.Model;

// What a saved response must be to be replayed: this schema, the world asked for, a body and both receipt times, and
// young enough. Anything else is ignored and the listings are fetched again.
internal static class FeedCacheRules
{
    public const int MaximumAgeDays = 7;
    public const int WorldListMaximumAgeDays = 30;

    public const string WorldListFileName = "worlds.json";

    private const string WorldFilePrefix = "world-";
    private const string FileExtension = ".json";

    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(MaximumAgeDays);
    public static readonly TimeSpan WorldListMaximumAge = TimeSpan.FromDays(WorldListMaximumAgeDays);

    public static string WorldFileName(uint worldId) => string.Concat(WorldFilePrefix, worldId.ToString(CultureInfo.InvariantCulture), FileExtension);

    // A saved file goes once it is older than its kind may be replayed: a world's listings after 7 days, the list of
    // worlds after 30. A file that is no saved listing is never touched.
    public static bool Expired(string fileName, DateTime writtenUtc, DateTime nowUtc)
    {
        if (string.Equals(fileName, WorldListFileName, StringComparison.Ordinal))
        {
            return nowUtc - writtenUtc > WorldListMaximumAge;
        }

        return IsWorldFile(fileName) && nowUtc - writtenUtc > MaximumAge;
    }

    private static bool IsWorldFile(string fileName)
    {
        if (!fileName.StartsWith(WorldFilePrefix, StringComparison.Ordinal) || !fileName.EndsWith(FileExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var number = fileName.AsSpan(WorldFilePrefix.Length, fileName.Length - WorldFilePrefix.Length - FileExtension.Length);
        return uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

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
