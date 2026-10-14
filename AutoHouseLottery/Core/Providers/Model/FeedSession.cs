namespace AutoHouseLottery.Core.Providers.Model;

// Every request carries a ticket, and a response is used only while its ticket is the newest one for its world. A world
// the player has left keeps its listing up to date: the store holds every world apart and each view filters by world, so
// a late answer for one world never lands in another world's view.
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

    public bool IsCurrent(in FeedTicket ticket) => newest.TryGetValue(ticket.WorldId, out var latest) && latest == ticket.Number;
}
