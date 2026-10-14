using System.Threading;

namespace AutoHouseLottery.Core.Game;

// One counter for requests, window updates and captured packets, so the readers can tell which came after which.
internal static class GameSequence
{
    private static long last;

    public static long Next() => Interlocked.Increment(ref last);
}
