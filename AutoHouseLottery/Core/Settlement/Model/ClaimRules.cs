using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// Acquire: the claim gives the buyer its first estate. Relocate: the buyer already holds an estate, which the claim moves
// to the won plot. Unknown: the character's estate list cannot say which.
internal enum ClaimEffect : byte { Unknown, Acquire, Relocate }

internal enum ClaimRefusal : byte { None, NotTheEntrant, PlotUnknown, NotWon, BuyerUnknown, EffectUnknown, GroupClaimOpen, AlreadyOwned }

// What the logged-in character and the journal say right before a claim. FreeCompanyKnown: the character's Free Company
// record is loaded, so a missing Free Company estate means there is none.
internal readonly record struct ClaimFacts(
    ulong CharacterContentId,
    EntryReceipt Receipt,
    SettlementState Recorded,
    bool FreeCompanyKnown,
    EstateAddress? PersonalEstate,
    EstateAddress? FreeCompanyEstate,
    bool GroupClaimOpen);

internal static class ClaimRules
{
    // Only the entrant who won, on its own receipt, with a known buyer, a known plot and a known effect; and never while
    // another claim of the same ownership group may still be going through.
    public static ClaimRefusal Check(in ClaimFacts facts)
    {
        var receipt = facts.Receipt;
        if (receipt.CharacterContentId != facts.CharacterContentId)
        {
            return ClaimRefusal.NotTheEntrant;
        }

        if (receipt.Plot is not { } plot)
        {
            return ClaimRefusal.PlotUnknown;
        }

        if (facts.Recorded != SettlementState.WonUnclaimed)
        {
            return ClaimRefusal.NotWon;
        }

        if (receipt.Buyer == BuyerType.Unknown)
        {
            return ClaimRefusal.BuyerUnknown;
        }

        if (Owns(receipt.Buyer, facts.PersonalEstate, facts.FreeCompanyEstate, plot))
        {
            return ClaimRefusal.AlreadyOwned;
        }

        if (EffectOf(receipt.Buyer, facts.FreeCompanyKnown, facts.PersonalEstate, facts.FreeCompanyEstate) == ClaimEffect.Unknown)
        {
            return ClaimRefusal.EffectUnknown;
        }

        return facts.GroupClaimOpen ? ClaimRefusal.GroupClaimOpen : ClaimRefusal.None;
    }

    // An apartment is not land, so it neither blocks a private claim nor moves with it.
    public static ClaimEffect EffectOf(BuyerType buyer, bool freeCompanyKnown, EstateAddress? personalEstate, EstateAddress? freeCompanyEstate) => buyer switch
    {
        BuyerType.Private => personalEstate is { Apartment: false } ? ClaimEffect.Relocate : ClaimEffect.Acquire,
        BuyerType.FreeCompany when !freeCompanyKnown => ClaimEffect.Unknown,
        BuyerType.FreeCompany => freeCompanyEstate is { Apartment: false } ? ClaimEffect.Relocate : ClaimEffect.Acquire,
        _ => ClaimEffect.Unknown,
    };

    // The estate the buyer holds is this plot. A buyer type the receipt does not know is checked against both.
    public static bool Owns(BuyerType buyer, EstateAddress? personalEstate, EstateAddress? freeCompanyEstate, PlotKey plot) => buyer switch
    {
        BuyerType.Private => IsPlot(personalEstate, plot),
        BuyerType.FreeCompany => IsPlot(freeCompanyEstate, plot),
        _ => IsPlot(personalEstate, plot) || IsPlot(freeCompanyEstate, plot),
    };

    // Another claim on the same home world for the same kind of buyer still in question. That is wider than the game's
    // ownership group, which keeps the lock on the safe side when the account grouping is not set.
    public static bool GroupClaimOpen(in JournalSnapshot snapshot, EntryReceipt receipt)
    {
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Kind == ActionKind.Claim
                && attempt.ReceiptId != receipt.ReceiptId
                && attempt.HomeWorldId == receipt.HomeWorldId
                && attempt.Buyer == receipt.Buyer
                && SettlementLookups.IsOpen(attempt.State))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlot(EstateAddress? estate, PlotKey plot)
        => estate is { Apartment: false } owned
        && owned.WorldId == plot.WorldId
        && owned.TerritoryTypeId == plot.DistrictId
        && owned.WardIndex + ResidentialDistricts.OneBased == plot.Ward
        && owned.PlotIndex + ResidentialDistricts.OneBased == plot.Plot;
}
