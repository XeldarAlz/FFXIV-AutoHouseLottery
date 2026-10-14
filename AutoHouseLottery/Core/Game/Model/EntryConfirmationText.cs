using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

internal static class EntryConfirmationText
{
    // Addon row 7046 names the deposit through its only numeric macro; the buyer type and the plot's restriction are
    // its other macros and carry no digits, in English, German, French and Japanese alike. Anything but exactly one
    // well-formed amount reads as unknown.
    public static long? Deposit(ImmutableArray<string> gaps)
        => DisplayNumbers.Single(gaps) is { } amount and > 0 ? amount : null;
}
