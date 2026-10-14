using ECommons.Automation.UIInput;
using ECommons.Throttlers;

namespace AutoHouseLottery.Core.Game.Lottery;

internal static unsafe class LotteryTimersWindow
{
    // HouseHunter's Timers reader: value 1 is true while the window is still loading, value 3 is true once the tab that
    // requests the housing lottery is selected, and radio button node 3 selects it.
    private const int LoadingValue = 1;
    private const int LotteryTabSelectedValue = 3;
    private const uint LotteryTabRadioNodeId = 3;
    private const bool FireCloseCallback = true;
    private const int ClickThrottleMs = 500;
    private const string ClickThrottleKey = "AutoHouseLottery.Lottery.Timers";

    public static bool Ready() => AddonReads.TryGetReady(LotteryReader.TimersAddon, out _);

    // True once the window has loaded with the lottery tab selected; selects the tab when the window says it is not.
    public static bool LotteryTabSelected()
    {
        if (!AddonReads.TryGetReady(LotteryReader.TimersAddon, out var addon) || AddonReads.Bool(addon, LoadingValue) != false)
        {
            return false;
        }

        var selected = AddonReads.Bool(addon, LotteryTabSelectedValue);
        if (selected == true)
        {
            return true;
        }

        var node = addon->GetNodeById(LotteryTabRadioNodeId);
        var radio = node == null ? null : node->GetAsAtkComponentRadioButton();
        if (selected == false && radio != null && EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
        {
            RunLog.Debug("Lottery pass: selecting the Timers tab that requests the housing lottery.");
            radio->ClickRadioButton(addon);
        }

        return false;
    }

    // HouseHunter closes the window the same way; it needs no callback value.
    public static void Close()
    {
        if (AddonReads.TryGetReady(LotteryReader.TimersAddon, out var addon))
        {
            addon->Close(FireCloseCallback);
        }
    }

    public static bool ClosedAfterNudge()
    {
        if (!Ready())
        {
            return true;
        }

        if (EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
        {
            Close();
        }

        return false;
    }
}
