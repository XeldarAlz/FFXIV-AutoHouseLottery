using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Core.Travel.Model;

// A plot to travel to, with zero-based ward and plot as the game counts them.
internal readonly record struct HousingDestination(uint WorldId, uint TerritoryTypeId, int WardIndex, int PlotIndex)
{
    public int Division => ResidentialDistricts.DivisionOfPlot(PlotIndex);

    public bool IsValid
        => WorldId != 0
        && ResidentialDistricts.IsResidential(TerritoryTypeId)
        && ResidentialDistricts.IsWardIndex(WardIndex)
        && ResidentialDistricts.IsPlotIndex(PlotIndex);

    public override string ToString() => $"world {WorldId}, territory {TerritoryTypeId}, ward {WardIndex + 1}, plot {PlotIndex + 1}";
}

internal enum ArrivalVerdict : byte
{
    Arrived,
    Unknown,
    WrongWorld,
    WrongDistrict,
    WrongWard,
    WrongDivision,
}

// ZoneReady is false while the character is between areas or not yet in the world; nothing it reports counts then.
internal readonly record struct ArrivalObservation(bool ZoneReady, uint WorldId, HousingLocation Location);

internal static class ArrivalCheck
{
    // Ward-level arrival: world, district, ward and division agree with the game's own housing state. A division the
    // game does not report is not taken as agreement.
    public static ArrivalVerdict Evaluate(in HousingDestination destination, in ArrivalObservation observed)
    {
        if (!observed.ZoneReady)
        {
            return ArrivalVerdict.Unknown;
        }

        if (observed.WorldId != destination.WorldId)
        {
            return ArrivalVerdict.WrongWorld;
        }

        if (observed.Location.TerritoryTypeId != destination.TerritoryTypeId)
        {
            return ArrivalVerdict.WrongDistrict;
        }

        if (observed.Location.WardIndex is not { } wardIndex || observed.Location.Division is not { } division)
        {
            return ArrivalVerdict.Unknown;
        }

        if (wardIndex != destination.WardIndex)
        {
            return ArrivalVerdict.WrongWard;
        }

        return division == destination.Division ? ArrivalVerdict.Arrived : ArrivalVerdict.WrongDivision;
    }
}
