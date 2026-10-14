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

    private readonly string directory;
    private readonly Lock gate = new();

    public HousingFeedCache(string directory)
    {
        this.directory = directory;
    }

    public FeedFetch? ReadWorld(uint worldId, DateTime nowUtc) => Read(FeedCacheRules.WorldFileName(worldId), worldId, FeedCacheRules.MaximumAge, nowUtc);

    public void WriteWorld(uint worldId, byte[] body, in FeedReceipt receipt) => Write(FeedCacheRules.WorldFileName(worldId), FeedCacheRules.Envelope(worldId, body, receipt));

    public byte[]? ReadWorldList(DateTime nowUtc) => Read(FeedCacheRules.WorldListFileName, WorldListId, FeedCacheRules.WorldListMaximumAge, nowUtc)?.Body;

    public void WriteWorldList(byte[] body, in FeedReceipt receipt) => Write(FeedCacheRules.WorldListFileName, FeedCacheRules.Envelope(WorldListId, body, receipt));

    // Saved listings past the age at which they could still be replayed are deleted, so the folder never grows with
    // every world ever browsed. Run once at load, off the framework thread.
    public void Prune(DateTime nowUtc)
    {
        var removed = 0;
        try
        {
            lock (gate)
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                var files = Directory.GetFiles(directory);
                for (var index = 0; index < files.Length; index++)
                {
                    if (FeedCacheRules.Expired(Path.GetFileName(files[index]), File.GetLastWriteTimeUtc(files[index]), nowUtc))
                    {
                        File.Delete(files[index]);
                        removed++;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunLog.Warning(exception, "Old saved housing listings could not all be removed; they are tried again at the next load");
        }

        if (removed > 0)
        {
            RunLog.Info($"Removed {removed} saved housing listing file(s) past their age ({FeedCacheRules.MaximumAgeDays} days for a world, {FeedCacheRules.WorldListMaximumAgeDays} for the list of worlds).");
        }
    }

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
