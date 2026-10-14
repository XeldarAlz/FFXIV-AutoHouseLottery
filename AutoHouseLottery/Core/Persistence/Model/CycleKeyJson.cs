using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly struct CycleKeyJson
{
    public GameRegion? Region { get; init; }

    public int? Number { get; init; }

    public static CycleKeyJson From(CycleKey cycle) => new() { Region = cycle.Region, Number = cycle.Number };

    public bool TryToKey(out CycleKey cycle)
    {
        cycle = default;
        if (Region is not { } region || region == GameRegion.Unknown || Number is not { } number)
        {
            return false;
        }

        cycle = new CycleKey(region, number);
        return true;
    }
}
