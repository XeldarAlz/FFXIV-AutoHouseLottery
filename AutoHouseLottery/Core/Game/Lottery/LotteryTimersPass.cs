using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Tasks;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Game.Lottery;

// Loads the personal lottery record the way a player does, by opening the Timers window, and closes the window again
// if it was not open before. It writes nothing to game memory; HouseHunter's timer-state reset is deliberately not used.
internal sealed class LotteryTimersPass(bool force) : AutoCommon
{
    private const string TimersCommand = "/timers";
    private const int PlayerFreeWaitMs = 10_000;
    private const int PreCommandSettleMs = 500;
    private const int OpenWaitMs = 5_000;
    private const int TabWaitMs = 5_000;
    // HouseHunter allows the record 30 seconds to arrive.
    private const int RecordWaitMs = 30_000;
    private const int CloseWaitMs = 3_000;

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

        if (!force && Snapshot.Loaded)
        {
            Finish(LotteryPassOutcome.Loaded);
            return;
        }

        if (Svc.PlayerState.HomeWorld.RowId != Svc.PlayerState.CurrentWorld.RowId)
        {
            Finish(LotteryPassOutcome.NotHomeWorld);
            return;
        }

        if (!await WaitUntilTimed(PlayerFree, PlayerFreeWaitMs, "lottery-player-free"))
        {
            Finish(CancelToken.IsCancellationRequested ? LotteryPassOutcome.Cancelled : LotteryPassOutcome.PlayerBusy);
            return;
        }

        var leftOpen = LotteryTimersWindow.Ready();
        if (leftOpen)
        {
            Diag("Lottery pass: the Timers window is already open; closing it so it fetches the lottery afresh.");
            await WaitUntilTimed(LotteryTimersWindow.ClosedAfterNudge, CloseWaitMs, "lottery-reopen");
        }

        try
        {
            if (await OpenTimers() && await SelectLotteryTab())
            {
                await WaitUntilTimed(RecordLoaded, RecordWaitMs, "lottery-record");
            }
        }
        finally
        {
            if (!leftOpen)
            {
                await CloseTimers();
            }
        }

        Snapshot = LotteryReader.Read();
        Finish(CancelToken.IsCancellationRequested ? LotteryPassOutcome.Cancelled : Snapshot.Loaded ? LotteryPassOutcome.Loaded : LotteryPassOutcome.NotLoaded);
    }

    private async Task<bool> OpenTimers()
    {
        await DelayMs(PreCommandSettleMs);
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        Diag("Lottery pass: opening the Timers window.");
        Chat.ExecuteCommand(TimersCommand);
        return await WaitUntilTimed(LotteryTimersWindow.Ready, OpenWaitMs, "lottery-timers-open");
    }

    private Task<bool> SelectLotteryTab() => WaitUntilTimed(LotteryTimersWindow.LotteryTabSelected, TabWaitMs, "lottery-tab");

    private async Task CloseTimers()
    {
        if (!await WaitUntilTimed(LotteryTimersWindow.ClosedAfterNudge, CloseWaitMs, "lottery-timers-close") && !CancelToken.IsCancellationRequested)
        {
            Warn("Lottery pass: the Timers window did not close; it is left open.");
        }
    }

    private void Finish(LotteryPassOutcome outcome)
    {
        Outcome = outcome;
        Diag($"Lottery pass: finished as {outcome}; {LotteryReader.Describe(Snapshot)}.");
    }

    private static bool RecordLoaded() => LotteryReader.Read().Loaded;

    private static bool PlayerFree()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BoundByDuty]
        && !Svc.Condition[ConditionFlag.BoundByDuty56]
        && !Svc.Condition[ConditionFlag.BoundByDuty95]
        && !GenericHelpers.IsOccupied();
}
