using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What the plugin knows of an entrant's ownership: its service account group and Free Company, each null when unknown.
internal readonly record struct OwnerFacts(int? AccountGroup, ulong? FreeCompanyId);

// Wins: the receipt's own ownership group holds this many unclaimed wins for its cycle, itself included. Claimed: another
// receipt of that group and cycle is claimed or has a claim in progress.
internal readonly record struct ClaimGroupState(int Wins, bool Claimed)
{
    public bool NeedsChoice => Wins > 1 && !Claimed;
}

internal static class ClaimGroups
{
    // The game's own grouping (OwnershipGroups): the Free Company for its plots, the service account and home world for
    // private ones, and every character whose account or company is unknown merged with the others on its world, so an
    // unknown never splits a group that could only claim once.
    public static ClaimGroupState Of(in JournalSnapshot snapshot, EntryReceipt receipt, Func<ulong, OwnerFacts> owners)
    {
        var receipts = snapshot.Receipts;
        var competing = new List<EntryReceipt>();
        for (var index = 0; index < receipts.Length; index++)
        {
            var other = receipts[index];
            if (other.Cycle == receipt.Cycle && other.HomeWorldId == receipt.HomeWorldId)
            {
                competing.Add(other);
            }
        }

        var profiles = new CharacterProfile[competing.Count];
        var buyers = new BuyerType[competing.Count];
        var own = 0;
        for (var index = 0; index < competing.Count; index++)
        {
            var entrant = competing[index];
            var facts = owners(entrant.CharacterContentId);
            profiles[index] = new CharacterProfile
            {
                ContentId = entrant.CharacterContentId,
                Name = string.Empty,
                Region = entrant.Cycle.Region,
                HomeWorldId = entrant.HomeWorldId,
                AccountGroup = facts.AccountGroup,
                FreeCompanyId = facts.FreeCompanyId,
            };
            buyers[index] = entrant.Buyer;
            if (entrant.ReceiptId == receipt.ReceiptId)
            {
                own = index;
            }
        }

        var groups = OwnershipGroups.Assign(profiles, buyers);
        var wins = 0;
        var claimed = false;
        for (var index = 0; index < competing.Count; index++)
        {
            if (groups[index] != groups[own] || SettlementLookups.Settlement(snapshot, competing[index].ReceiptId) is not { } settlement)
            {
                continue;
            }

            if (settlement.State == SettlementState.WonUnclaimed)
            {
                wins++;
            }
            else if (index != own && settlement.State is SettlementState.Claimed or SettlementState.ClaimPending)
            {
                claimed = true;
            }
        }

        return new ClaimGroupState(wins, claimed);
    }
}
