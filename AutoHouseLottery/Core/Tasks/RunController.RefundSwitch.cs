using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

// Switch characters to collect refunds (UX section 10): with its explicit option on and switching allowed, once the
// logged-in character has nothing left the scheduler can do and the player has left the game alone, the refund queue
// logs in the next character of the selected roster that has result checks or refunds due, and the scheduler then works
// for that character under its usual rules and gates. Each character is switched to at most once a session; a switch
// that ends anywhere but on the requested character stops the queue for the session.
internal sealed partial class RunController
{
    private readonly HashSet<ulong> refundSwitchVisited = [];
    private readonly List<RefundCandidate> refundCandidates = [];
    private long refundSwitchToken;
    private LeaseOwner? refundSwitchOwner;
    private RosterFacts refundSwitchTarget;
    private bool refundSwitchStopped;

    public bool RefundSwitching => refundSwitchToken != 0;

    private void TryStartRefundSwitch(DateTime nowUtc)
    {
        var configuration = Plugin.Instance.Configuration;
        if (refundSwitchStopped || !configuration.SwitchForRefunds || !SwitchingAllowed || !RefundSwitchRules.SessionFree(scheduler.Facts))
        {
            return;
        }

        var current = Svc.PlayerState.ContentId;
        var currentHasWork = scheduler.Block is not (SchedulerBlock.NothingDue or SchedulerBlock.RefundsOff or SchedulerBlock.BelowReserve or SchedulerBlock.TravelBudgetSpent);
        var position = housing.CurrentPosition(nowUtc);
        refundCandidates.Clear();
        foreach (var (contentId, _) in configuration.Characters)
        {
            var listed = configuration.ChoiceFor(contentId).Selected;
            refundCandidates.Add(new RefundCandidate(contentId, listed, configuration.AutomaticRefunds(contentId), listed && contentId != current ? scheduler.DueCountFor(contentId, position) : 0));
        }

        if (RefundSwitchRules.Choose(true, true, current, currentHasWork, CollectionsMarshal.AsSpan(refundCandidates), refundSwitchVisited) is not { } target
            || housing.Facts(target) is not { } facts
            || WorldNames.Of(facts.HomeWorldId) is not { } worldName
            || !lease.TryAcquire(LeaseWork.CharacterSwitch, out var leaseGrant))
        {
            return;
        }

        refundSwitchVisited.Add(target);
        var start = CharacterSwitch.Begin(target, facts.Name, facts.HomeWorldId, worldName);
        if (start.Result != SwitchStartResult.Started)
        {
            lease.Release(leaseGrant.Owner);
            RunLog.Info($"Refund queue: the switch to {facts.Name} did not start ({start.Result}); it is not tried again this session.");
            return;
        }

        refundSwitchToken = start.Token;
        refundSwitchOwner = leaseGrant.Owner;
        refundSwitchTarget = facts;
        RunLog.Info($"Refund queue: {Svc.PlayerState.CharacterName} has nothing left to collect, so switching to {facts.Name} of {worldName} for its due result checks and refunds (switch {start.Token}).");
    }

    // Once a second, logged in or not.
    private void TickRefundSwitch()
    {
        if (refundSwitchToken == 0)
        {
            return;
        }

        var state = CharacterSwitch.Observe(refundSwitchToken);
        if (state == SwitchState.Switching)
        {
            return;
        }

        refundSwitchToken = 0;
        ReleaseRefundSwitchLease();
        var confirmed = state == SwitchState.Switched
            && Svc.PlayerState.IsLoaded
            && Svc.PlayerState.ContentId == refundSwitchTarget.ContentId
            && Svc.PlayerState.HomeWorld.RowId == refundSwitchTarget.HomeWorldId;
        if (confirmed)
        {
            RunLog.Info($"Refund queue: {refundSwitchTarget.Name} is confirmed by content ID and home world; the scheduler works for it from its own journal and readings.");
            return;
        }

        refundSwitchStopped = true;
        RunLog.Warning($"Refund queue: the switch to {refundSwitchTarget.Name} ended {state}; refund switching stops for this session and each character collects at its own login.");
    }

    private void StopRefundSwitch(string reason)
    {
        if (refundSwitchToken == 0)
        {
            return;
        }

        CharacterSwitch.CancelOwned(refundSwitchToken);
        refundSwitchToken = 0;
        refundSwitchStopped = true;
        ReleaseRefundSwitchLease();
        RunLog.Info($"Refund queue: the switch to {refundSwitchTarget.Name} stopped ({reason}); refund switching stops for this session.");
    }

    private void ReleaseRefundSwitchLease()
    {
        if (refundSwitchOwner is { } holder)
        {
            lease.Release(holder);
            refundSwitchOwner = null;
        }
    }
}
