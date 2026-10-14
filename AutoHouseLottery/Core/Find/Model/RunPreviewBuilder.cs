using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find.Model;

// The preview the shortlist drawer shows for the current character. It only plans; nothing here can enter a lottery.
internal static class RunPreviewBuilder
{
    public const long PreviewTravelAllowance = 0;

    public static RunPreview Build(
        in CharacterProfile character,
        BuyerType buyer,
        long depositCap,
        long gilReserve,
        in ShortlistSnapshot shortlist,
        in CyclePosition position,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots)
    {
        var policy = new RunPolicy
        {
            Revision = shortlist.Revision,
            Cycle = position,
            Characters = [new CharacterRunSettings(character.ContentId, buyer, depositCap, gilReserve)],
            TravelAllowance = PreviewTravelAllowance,
            Shortlist = shortlist,
        };
        return AssignmentPlanner.Build(policy, [character], plots);
    }
}
