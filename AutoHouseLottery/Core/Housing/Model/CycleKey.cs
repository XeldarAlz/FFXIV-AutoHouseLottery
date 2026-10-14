using System.Globalization;

namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct CycleKey(GameRegion Region, int Number)
{
    public bool IsValid => Region != GameRegion.Unknown;

    public bool IsLaterThan(CycleKey other)
    {
        if (Region != other.Region)
        {
            throw new ArgumentException("Cycles from different regions cannot be ordered.", nameof(other));
        }

        return Number > other.Number;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Region}:{Number}");
}
