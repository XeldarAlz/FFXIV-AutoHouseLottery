using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Tasks;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Game.Lottery;

// Loads the personal lottery entry the way a player does, by opening the Timers window on its lottery tab, and closes
// the window again if it was not open before, also when the pass is stopped. A reading counts only when it is fresh:
// it changed from what was there before the tab was selected, or the window refreshed after that. It writes nothing to
// game memory; HouseHunter's timer-state reset is deliberately not used.
internal sealed class LotteryTimersPass : AutoCommon
{
    private const string TimersCommand = "/timers";
    private const int PlayerFreeWaitMs = 10_000;
    private const int PreCommandSettleMs = 500;
    private const int OpenWaitMs = 5_000;
    private const int TabWaitMs = 5_000;
    private const int CloseWaitMs = 3_000;
    // HouseHunter allows the record 30 seconds to arrive.
    private const int RecordWaitMs = 30_000;

    public LotterySnapshot Snapshot { get; private set; }

    public LotteryPassOutcome Outcome { get; private set; } = LotteryPassOutcome.Cancelled;

    protected override async Task Execute()
    {
        Status = "Reading the lottery entry";
        Snapshot = LotteryReader.Read();
        if (Snapshot.State == LotterySnapshotState.NoCharacter)
        {
            Finish(LotteryPassOutcome.NoCharacter);
            return;
        }

        if (Svc.PlayerState.HomeWorld.RowId != Svc.PlayerState.CurrentWorld.RowId)
        {
            Finish(LotteryPassOutcome.NotHomeWorld);
            return;
        }

        if (!await WaitUntilTimed(PlayerFree, PlayerFreeWaitMs, "lottery-player-free"))
        {
            Finish(LotteryPassOutcome.PlayerBusy);
            return;
        }

        var contentId = Snapshot.ContentId;
        var leftOpen = LotteryTimersWindow.Ready();
        var outcome = LotteryPassOutcome.Cancelled;
        using var window = new AddonUpdateCounter(LotteryReader.TimersAddon);
        try
        {
            outcome = await LoadFresh(contentId, leftOpen, window);
        }
        finally
        {
            if (!leftOpen)
            {
                LotteryTimersWindow.Close();
            }

            Finish(outcome);
        }
    }

    private async Task<LotteryPassOutcome> LoadFresh(ulong contentId, bool leftOpen, AddonUpdateCounter window)
    {
        if (leftOpen)
        {
            Diag("Lottery pass: the Timers window is already open; closing it so it fetches the lottery afresh.");
            await WaitUntilTimed(LotteryTimersWindow.ClosedAfterNudge, CloseWaitMs, "lottery-reopen");
        }

        var before = LotteryReader.Read();
        await DelayMs(PreCommandSettleMs);
        Diag("Lottery pass: opening the Timers window.");
        Chat.ExecuteCommand(TimersCommand);
        if (!await WaitUntilTimed(LotteryTimersWindow.Ready, OpenWaitMs, "lottery-timers-open")
            || !await WaitUntilTimed(LotteryTimersWindow.LotteryTabSelected, TabWaitMs, "lottery-tab"))
        {
            return LotteryPassOutcome.NotLoaded;
        }

        var tabSelectedAt = GameSequence.Next();
        var fresh = await WaitUntilTimed(() => SameCharacter(contentId) && Fresh(before, tabSelectedAt, window), RecordWaitMs, "lottery-record");
        if (!SameCharacter(contentId))
        {
            Snapshot = default;
            return LotteryPassOutcome.CharacterChanged;
        }

        Snapshot = LotteryReader.Read();
        return fresh && Snapshot.Loaded ? LotteryPassOutcome.Loaded : LotteryPassOutcome.NotLoaded;
    }

    // A record left over from before the tab was selected, or from another character before a relog, does not count.
    private static bool Fresh(in LotterySnapshot before, long tabSelectedAt, AddonUpdateCounter window)
    {
        var now = LotteryReader.Read();
        return now.Loaded && (now.State != before.State || now.Entry != before.Entry || window.LastRefreshSequence > tabSelectedAt);
    }

    private static bool SameCharacter(ulong contentId) => Svc.ClientState.IsLoggedIn && Svc.PlayerState.ContentId == contentId;

    private void Finish(LotteryPassOutcome outcome)
    {
        Outcome = outcome;
        Diag($"Lottery pass: finished as {outcome}; {LotteryReader.Describe(Snapshot)}.");
    }

    private static bool PlayerFree()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BoundByDuty]
        && !Svc.Condition[ConditionFlag.BoundByDuty56]
        && !Svc.Condition[ConditionFlag.BoundByDuty95]
        && !GenericHelpers.IsOccupied();
}
