using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using GameLotteryPhase = AutoHouseLottery.Core.Game.Model.LotteryPhase;
using LotteryPhase = AutoHouseLottery.Core.Housing.Model.LotteryPhase;

namespace AutoHouseLottery.Core.Integration.Model;

internal readonly record struct SlotEvidence(EntrySlotState State, PlotKey? Plot, DateTime? EnteredUtc, DateTime? ObservedUtc);

internal static class ParticipationRules
{
    private const int OneBased = 1;

    // The journal speaks first: a receipt or an attempt of this plugin for the cycle decides the slot, whatever the
    // reading says. Otherwise the reading decides, and a reading that cannot tell leaves the slot unknown.
    public static CycleParticipation Resolve(CycleKey cycle, in SlotEvidence journal, in SlotEvidence reading)
    {
        var chosen = journal.State is EntrySlotState.Entered or EntrySlotState.Unresolved ? journal : reading;
        return new CycleParticipation(cycle, chosen.State)
        {
            EnteredPlot = chosen.Plot,
            EnteredUtc = chosen.EnteredUtc,
            ObservedUtc = chosen.ObservedUtc,
        };
    }

    public static SlotEvidence FromJournal(in JournalSnapshot snapshot, ulong contentId, uint homeWorldId, CycleKey cycle)
    {
        var key = OperationKey.ForEntry(contentId, homeWorldId, cycle);
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Key != key || !AttemptTransitions.HoldsSlot(attempt.State))
            {
                continue;
            }

            var state = attempt.State == AttemptState.Confirmed ? EntrySlotState.Entered : EntrySlotState.Unresolved;
            return new SlotEvidence(state, attempt.Plot, attempt.State == AttemptState.Confirmed ? attempt.UpdatedUtc : null, attempt.UpdatedUtc);
        }

        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.HoldsSlotOf(contentId, homeWorldId, cycle))
            {
                return new SlotEvidence(EntrySlotState.Entered, receipt.Plot, receipt.EnteredUtc, receipt.EnteredUtc);
            }
        }

        return new SlotEvidence(EntrySlotState.Unknown, null, null, null);
    }

    // Only the validated record can say there is no entry. An entry counts for the cycle only when the phase it shows
    // is the phase the cycle is in; a reading without a phase, or with another one, may belong to another cycle.
    public static SlotEvidence FromReading(in LotterySnapshot reading, in CyclePosition position, GameRegion region, uint homeWorldId)
    {
        switch (reading.State)
        {
            case LotterySnapshotState.LoadedNone:
                return new SlotEvidence(EntrySlotState.Open, null, null, reading.ReadAtUtc);
            case LotterySnapshotState.Active when reading.Entry is { } entry && SamePhase(entry.Phase, position.Phase):
                return new SlotEvidence(EntrySlotState.Entered, PlotOf(entry, region, homeWorldId), null, reading.ReadAtUtc);
            default:
                return new SlotEvidence(EntrySlotState.Unknown, null, null, reading.ReadAtUtc);
        }
    }

    public static bool SamePhase(GameLotteryPhase? shown, LotteryPhase current) => shown switch
    {
        GameLotteryPhase.EntryPeriod => current == LotteryPhase.Entry,
        GameLotteryPhase.ResultsPeriod => current == LotteryPhase.Results,
        _ => false,
    };

    // Entries are only ever on the home world, so the record's zero-based address is placed there.
    public static PlotKey? PlotOf(in LotteryEntry entry, GameRegion region, uint homeWorldId)
    {
        if (entry.TerritoryTypeId is not { } territory || entry.WardIndex is not { } ward || entry.PlotIndex is not { } plot)
        {
            return null;
        }

        return PlotKey.TryCreate(region, homeWorldId, territory, ward + OneBased, plot + OneBased, out var key) ? key : null;
    }
}
