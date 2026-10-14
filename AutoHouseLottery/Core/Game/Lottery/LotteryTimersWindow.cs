using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.Throttlers;

namespace AutoHouseLottery.Core.Game.Lottery;

internal static unsafe class LotteryTimersWindow
{
    private const string AddonName = "ContentsInfo";
    private const int CloseCallback = -1;
    // HouseHunter's Timers reader: value 1 is true while the window is still loading, value 3 is false until the tab
    // that requests the housing lottery is selected, and radio button node 3 selects it.
    private const int LoadingValue = 1;
    private const int LotteryTabSelectedValue = 3;
    private const uint LotteryTabRadioNodeId = 3;
    private const int ClickThrottleMs = 500;
    private const string ClickThrottleKey = "AutoHouseLottery.Lottery.Timers";

    public static bool Ready() => AddonReads.TryGetReady(AddonName, out _);

    public static bool LotteryTabSelected()
    {
        if (!AddonReads.TryGetReady(AddonName, out var addon) || AddonReads.Bool(addon, LoadingValue) != false)
        {
            return false;
        }

        if (AddonReads.Bool(addon, LotteryTabSelectedValue) != false)
        {
            return true;
        }

        var node = addon->GetNodeById(LotteryTabRadioNodeId);
        var radio = node == null ? null : node->GetAsAtkComponentRadioButton();
        if (radio != null && EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
        {
            RunLog.Debug("Lottery pass: selecting the Timers tab that requests the housing lottery.");
            radio->ClickRadioButton(addon);
        }

        return false;
    }

    public static bool ClosedAfterNudge()
    {
        if (!AddonReads.TryGetReady(AddonName, out var addon))
        {
            return true;
        }

        if (EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
        {
            Callback.Fire(addon, true, CloseCallback);
        }

        return false;
    }
}
