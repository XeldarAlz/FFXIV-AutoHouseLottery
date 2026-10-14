using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Providers.Model;

namespace AutoHouseLottery.Core.Providers;

// Ported from Aetherphone's housing cache. Each world's last good response is kept verbatim with the receipt it came
// with, and replayed through the same normalizer at startup, so a cached listing keeps its own source times and the page
// can say how old the saved copy is. Called only from background tasks.
internal sealed class HousingFeedCache
{
    public const int MaximumAgeDays = 7;
    public const int WorldListMaximumAgeDays = 30;

    private const uint WorldListId = 0;
    private const string WorldListFile = "worlds.json";

    private static readonly TimeSpan maximumAge = TimeSpan.FromDays(MaximumAgeDays);
    private static readonly TimeSpan worldListMaximumAge = TimeSpan.FromDays(WorldListMaximumAgeDays);

    private readonly string directory;
    private readonly Lock gate = new();

    public HousingFeedCache(string directory)
    {
        this.directory = directory;
    }

    public FeedFetch? ReadWorld(uint worldId, DateTime nowUtc)
    {
        if (Read(WorldFile(worldId), worldId, maximumAge, nowUtc) is not { } cached)
        {
            return null;
        }

        return new FeedFetch(FetchStatus.Success, cached.Body, 0, null, null, cached.Receipt);
    }

    public void WriteWorld(uint worldId, in FeedFetch fetch)
    {
        if (fetch.Body is { } body)
        {
            Write(WorldFile(worldId), worldId, body, fetch.Receipt);
        }
    }

    public byte[]? ReadWorldList(DateTime nowUtc) => Read(WorldListFile, WorldListId, worldListMaximumAge, nowUtc)?.Body;

    public void WriteWorldList(in FeedFetch fetch)
    {
        if (fetch.Body is { } body)
        {
            Write(WorldListFile, WorldListId, body, fetch.Receipt);
        }
    }

    private static string WorldFile(uint worldId) => string.Concat("world-", worldId.ToString(CultureInfo.InvariantCulture), ".json");

    private (byte[] Body, FeedReceipt Receipt)? Read(string fileName, uint worldId, TimeSpan maximum, DateTime nowUtc)
    {
        var path = Path.Combine(directory, fileName);
        try
        {
            FeedCacheFile? file;
            lock (gate)
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                file = JsonSerializer.Deserialize(File.ReadAllBytes(path), FeedJsonContext.Default.FeedCacheFile);
            }

            if (file is not { Schema: FeedCacheFile.CurrentSchema, Body: { } body, ReceivedUtc: { } received, UpstreamUtc: { } upstream }
                || file.WorldId != worldId
                || nowUtc - received > maximum)
            {
                return null;
            }

            return (Encoding.UTF8.GetBytes(body), new FeedReceipt(received, upstream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            RunLog.Warning(exception, $"The saved housing listings in {fileName} could not be read; they will be fetched again");
            return null;
        }
    }

    private void Write(string fileName, uint worldId, byte[] body, in FeedReceipt receipt)
    {
        var file = new FeedCacheFile
        {
            Schema = FeedCacheFile.CurrentSchema,
            WorldId = worldId,
            ReceivedUtc = receipt.ReceivedUtc,
            UpstreamUtc = receipt.UpstreamUtc,
            Body = Encoding.UTF8.GetString(body),
        };
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                JournalFile.RewriteAtomically(Path.Combine(directory, fileName), JsonSerializer.SerializeToUtf8Bytes(file, FeedJsonContext.Default.FeedCacheFile));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunLog.Warning(exception, $"The housing listings could not be saved to {fileName}");
        }
    }
}
