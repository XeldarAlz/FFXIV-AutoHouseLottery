using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

internal static class ImportedAddress
{
    private const int NumbersInAddress = 2;
    private const int DecimalBase = 10;
    private const int LargestNumber = 1_000;
    private const int CandidateOrders = 2;

    // The plot an entry found in the Timers window names (Addon row 8847): exactly one district's name in the client
    // language and exactly two numbers. Client languages order the ward and the plot differently ("Plot 5, 12 Ward" in
    // English) and which macro of the row holds which is not established yet, so an address resolves only when one order
    // alone makes a valid plot (a plot number above the ward count). Anything less certain names no plot, nothing is
    // guessed, and the entry waits for the player. The visit keeps the plot only once a result row proves it.
    public static PlotKey? Resolve(string addressText, GameRegion region, uint homeWorldId, Func<uint, string?> districtNameOf)
    {
        var text = DisplayText.Normalize(addressText);
        if (!FindDistrict(text, districtNameOf, out var district, out var nameStart, out var nameLength))
        {
            return null;
        }

        Span<int> numbers = stackalloc int[NumbersInAddress];
        var count = CollectNumbers(text.AsSpan(0, nameStart), numbers, 0);
        count = count < 0 ? count : CollectNumbers(text.AsSpan(nameStart + nameLength), numbers, count);
        if (count != NumbersInAddress)
        {
            return null;
        }

        Span<PlotKey> candidates = stackalloc PlotKey[CandidateOrders];
        var found = 0;
        if (PlotKey.TryCreate(region, homeWorldId, district, numbers[0], numbers[1], out var wardFirst))
        {
            candidates[found++] = wardFirst;
        }

        if (numbers[0] != numbers[1] && PlotKey.TryCreate(region, homeWorldId, district, numbers[1], numbers[0], out var plotFirst))
        {
            candidates[found++] = plotFirst;
        }

        return found == 1 ? candidates[0] : null;
    }

    private static bool FindDistrict(string text, Func<uint, string?> districtNameOf, out uint district, out int nameStart, out int nameLength)
    {
        district = 0;
        nameStart = 0;
        nameLength = 0;
        var matches = 0;
        for (var districtIndex = 0; districtIndex < ResidentialDistricts.All.Length; districtIndex++)
        {
            var territory = ResidentialDistricts.All[districtIndex].TerritoryTypeId;
            if (districtNameOf(territory) is not { } shown || DisplayText.Normalize(shown) is not { Length: > 0 } name)
            {
                continue;
            }

            var at = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            matches++;
            district = territory;
            nameStart = at;
            nameLength = name.Length;
        }

        return matches == 1;
    }

    // Appends each run of digits to numbers; -1 when there are more than fit or one is implausibly large.
    private static int CollectNumbers(ReadOnlySpan<char> text, Span<int> numbers, int count)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                index++;
                continue;
            }

            var value = 0;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                value = (value * DecimalBase) + (text[index] - '0');
                index++;
                if (value >= LargestNumber)
                {
                    return -1;
                }
            }

            if (count == numbers.Length)
            {
                return -1;
            }

            numbers[count++] = value;
        }

        return count;
    }
}
