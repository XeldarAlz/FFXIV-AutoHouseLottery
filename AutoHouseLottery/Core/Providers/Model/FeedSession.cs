namespace AutoHouseLottery.Core.Providers.Model;

// Every request carries a ticket. A response is used only while its ticket is the newest one for its world and that
// world is still in the fetched scope, so a slow answer for a world the player has left never lands in the view.
internal sealed class FeedSession
{
    private readonly Dictionary<uint, long> newest = [];
    private long issued;

    public FeedTicket Issue(uint worldId)
    {
        issued++;
        newest[worldId] = issued;
        return new FeedTicket(worldId, issued);
    }

    public bool IsCurrent(in FeedTicket ticket, ReadOnlySpan<uint> scope)
    {
        return newest.TryGetValue(ticket.WorldId, out var latest) && latest == ticket.Number && scope.Contains(ticket.WorldId);
    }
}
