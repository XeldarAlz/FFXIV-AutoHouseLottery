using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find.Model;

// The frozen policies Find a home plans its preview with and starts runs from, for the current character. A policy only
// plans; a run starts from it only through the run controller, which checks it again.
internal static class RunPreviewBuilder
{
    public const long PreviewTravelAllowance = 0;

    // The shortlist drawer's plan: the frozen shortlist, its ranking and the price ceiling as the deposit cap.
    public static RunPolicy BestMatchPolicy(in CharacterProfile character, BuyerType buyer, long depositCap, long gilReserve, in ShortlistSnapshot shortlist, in CyclePosition position)
        => new()
        {
            Revision = shortlist.Revision,
            Cycle = position,
            Characters = [new CharacterRunSettings(character.ContentId, buyer, depositCap, gilReserve)],
            TravelAllowance = PreviewTravelAllowance,
            Shortlist = shortlist,
        };

    // One plot for exactly the deposit its details show. The deposit is also the cap, so the run can never pay more.
    public static RunPolicy ExactPolicy(in CharacterProfile character, BuyerType buyer, PlotKey plot, long deposit, long gilReserve, in CyclePosition position, int revision)
        => new()
        {
            Revision = revision,
            Cycle = position,
            Characters = [new CharacterRunSettings(character.ContentId, buyer, deposit, gilReserve)],
            TravelAllowance = PreviewTravelAllowance,
            Exact = new ExactTarget(plot, deposit),
        };

    // The buyer is the player's own choice in the filters. A plot sold to one buyer type only confirms that choice:
    // no choice, or a choice the plot does not take, gives no buyer. A plot whose buyers are not reported takes the
    // choice, and the placard checks it before anything is pressed.
    public static BuyerType BuyerFor(PlotBuyers? plotBuyers, BuyerType chosen)
    {
        if (chosen == BuyerType.Unknown || EligibilityRules.Allows(plotBuyers ?? PlotBuyers.Unknown, chosen) == false)
        {
            return BuyerType.Unknown;
        }

        return chosen;
    }
}
