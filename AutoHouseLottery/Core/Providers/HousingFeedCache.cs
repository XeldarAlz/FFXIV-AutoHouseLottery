using System.Globalization;
using System.IO;
using System.Threading;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Providers.Model;

namespace AutoHouseLottery.Core.Providers;

// Ported from Aetherphone's housing cache. Each world's last good response is kept verbatim with the receipt it came
// with, and replayed through the same normalizer at startup, so a cached listing keeps its own source times and the page
// can say how old the saved copy is. A cache that cannot be read is only a missed shortcut: the listings are fetched
// again. Called only from background tasks.
internal sealed class HousingFeedCache
{
    private const uint WorldListId = 0;
    private const string WorldListFile = "worlds.json";

    private readonly string directory;
    private readonly Lock gate = new();

    public HousingFeedCache(string directory)
    {
        this.directory = directory;
    }

    public FeedFetch? ReadWorld(uint worldId, DateTime nowUtc) => Read(WorldFile(worldId), worldId, FeedCacheRules.MaximumAge, nowUtc);

    public void WriteWorld(uint worldId, byte[] body, in FeedReceipt receipt) => Write(WorldFile(worldId), FeedCacheRules.Envelope(worldId, body, receipt));

    public byte[]? ReadWorldList(DateTime nowUtc) => Read(WorldListFile, WorldListId, FeedCacheRules.WorldListMaximumAge, nowUtc)?.Body;

    public void WriteWorldList(byte[] body, in FeedReceipt receipt) => Write(WorldListFile, FeedCacheRules.Envelope(WorldListId, body, receipt));

    private static string WorldFile(uint worldId) => string.Concat("world-", worldId.ToString(CultureInfo.InvariantCulture), ".json");

    private FeedFetch? Read(string fileName, uint worldId, TimeSpan maximumAge, DateTime nowUtc)
    {
        var path = Path.Combine(directory, fileName);
        byte[] bytes;
        try
        {
            lock (gate)
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                bytes = File.ReadAllBytes(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunLog.Warning(exception, $"The saved housing listings in {fileName} could not be read; they will be fetched again");
            return null;
        }

        return FeedCacheRules.TryAccept(bytes, worldId, maximumAge, nowUtc, out var fetch) ? fetch : null;
    }

    private void Write(string fileName, byte[] envelope)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                JournalFile.RewriteAtomically(Path.Combine(directory, fileName), envelope);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunLog.Warning(exception, $"The housing listings could not be saved to {fileName}");
        }
    }
}
