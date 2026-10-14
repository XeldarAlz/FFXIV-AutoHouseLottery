using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Providers.Model;

// Only the world the player is browsing and the worlds their saved searches, lists and shortlist point at are fetched;
// the service is never polled for every world it knows.
internal static class FetchScope
{
    public static ImmutableArray<uint> Worlds(uint selectedWorld, ReadOnlySpan<uint> watchedWorlds)
    {
        var worlds = ImmutableArray.CreateBuilder<uint>(watchedWorlds.Length + 1);
        if (selectedWorld != 0)
        {
            worlds.Add(selectedWorld);
        }

        for (var index = 0; index < watchedWorlds.Length; index++)
        {
            var world = watchedWorlds[index];
            if (world != 0 && !worlds.Contains(world))
            {
                worlds.Add(world);
            }
        }

        return worlds.ToImmutable();
    }
}
