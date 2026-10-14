using System.Collections.Immutable;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Game.Watchers;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Settlement;

// The result and refund scheduler of TECHNICAL_DESIGN section 12, polled once a second by the run controller. It works
// out what is due for the logged-in character from the journal, says why it cannot start, and hands the controller one
// piece of work when the player has been idle long enough and nothing else drives the game. It also records, without a
// visit, a win the estate list shows claimed. Session state only: a reload rebuilds the queue from the journal.
internal sealed class SettlementScheduler(HousingContext housing, Configuration configuration)
{
    private readonly SettlementQueue queue = new();
    private readonly ManualInputWatcher input = new();
    private readonly Dictionary<ulong, long> travelSpent = [];
    private readonly HashSet<ulong> readyAnnounced = [];
    private ImmutableArray<EntrySettlement> estateCheckedFor;
    private Estates estateChecked;
    private long blockedSinceMs;
    private long heldAtMs;
    private bool heldAfterAlert;
    private volatile bool fixing;

    public SchedulerBlock Block { get; private set; } = SchedulerBlock.NotLoggedIn;

    public int DueCount { get; private set; }

    public long TravelSpent(ulong contentId) => travelSpent.TryGetValue(contentId, out var spent) ? spent : 0;

    public long TravelAllowance(ulong contentId, long? gil) => SchedulerRules.TravelAllowance(TravelSpent(contentId), gil, configuration.Find.GilReserve);

    public long IdleMs(long nowMs) => input.IdleMs(nowMs);

    public void SampleGamepad(long nowMs) => input.SampleGamepad(nowMs);

    // After a GM alert no automatic visit starts until the player's own input after this moment.
    public void HoldUntilInput(long nowMs)
    {
        heldAtMs = nowMs;
        heldAfterAlert = true;
        RunLog.Info("Settlement scheduler: held after a GM alert until you act in the game again.");
    }

    public void RecordTravel(ulong contentId, long spent)
    {
        if (spent > 0)
        {
            travelSpent[contentId] = Shared.Gil.SaturatingAdd(TravelSpent(contentId), spent);
        }
    }

    public void Started(SettlementWork work, long nowMs) => queue.Started(work, nowMs);

    // Background work stopped because the player came back keeps its turn for the next idle stretch.
    public void GiveBack(SettlementWork work) => queue.GiveBack(work);

    public void Reset()
    {
        queue.Clear();
        travelSpent.Clear();
        readyAnnounced.Clear();
        estateCheckedFor = default;
        estateChecked = default;
        blockedSinceMs = 0;
        Block = SchedulerBlock.NotLoggedIn;
    }

    // The work to start now, or null with Block saying why not.
    public SettlementWork? Tick(long nowMs, bool ownWorkActive, bool busy, bool ownVisitRunning, Func<Guid, PlotKey, bool> collectable)
    {
        input.Sample(nowMs, ownWorkActive);
        if (heldAfterAlert && SchedulerRules.HoldReleased(heldAtMs, input.IdleMs(nowMs), nowMs))
        {
            heldAfterAlert = false;
            RunLog.Info("Settlement scheduler: you acted in the game after the GM alert; automatic visits may start again.");
        }

        if (CharacterContextReader.Read() is not { } context)
        {
            Block = SchedulerBlock.NotLoggedIn;
            return null;
        }

        if (!busy)
        {
            ReconcileEstates(context);
        }

        var due = SettlementSchedule.Due(housing.JournalSnapshot, context.ContentId, housing.CurrentPosition(DateTime.UtcNow), collectable, Parked);
        DueCount = due.Length;
        var next = queue.Next(due, nowMs);
        var facts = new SchedulerFacts(
            LoggedIn: true,
            AutomaticRefunds: configuration.AutomaticRefunds(context.ContentId),
            WorkDue: next is not null,
            JournalWritable: housing.JournalWritable,
            AutoAnswerLoaded: AutoAnswerGuard.Blocks,
            HeldAfterAlert: heldAfterAlert,
            InDuty: DutyWatcher.InContent(),
            InCombat: Svc.Condition[ConditionFlag.InCombat],
            InCutscene: Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.WatchingCutscene] || Svc.Condition[ConditionFlag.WatchingCutscene78],
            Busy: busy,
            Occupied: GenericHelpers.IsOccupied(),
            Typing: TextInputFocus.Active(),
            IdleMs: input.IdleMs(nowMs),
            TravelAllowance: TravelAllowance(context.ContentId, context.Gil),
            Gil: context.Gil,
            Reserve: configuration.Find.GilReserve);
        var block = SchedulerRules.Check(facts);
        if (block != Block)
        {
            if (block is not (SchedulerBlock.None or SchedulerBlock.NotIdle))
            {
                RunLog.Info($"Settlement scheduler: {DueCount} item(s) due for {context.Name}; waiting ({block}).");
            }

            blockedSinceMs = nowMs;
            Block = block;
        }

        if (block == SchedulerBlock.None)
        {
            return next;
        }

        if (SchedulerRules.ShouldAnnounceReady(block, nowMs - blockedSinceMs, ownVisitRunning) && readyAnnounced.Add(context.ContentId))
        {
            SettlementNoticeAnnouncer.AnnounceReady(DueCount);
            RunLog.Info($"Settlement scheduler: announced that refund collection is ready ({DueCount} item(s), waiting on {block}).");
        }

        return null;
    }

    // While a placard without a result cannot settle anything, revisiting one already found that way in full only costs
    // travel. A weaker sighting (a placard not fully read) is visited again.
    private bool Parked(Guid id) => !SettlementCapabilities.AbsenceSettlementValidated && configuration.AbsenceSighting(id) is { Strong: true };

    // A win the character's estate list now holds was claimed, by this plugin's press or in game; it is recorded as
    // claimed without a visit. Checked again only when the journal's settlements change.
    private void ReconcileEstates(CharacterContext context)
    {
        var settlements = housing.JournalSnapshot.Settlements;
        var estates = new Estates(context.PersonalEstate?.RawId, context.FreeCompanyEstate?.RawId);
        if (fixing || (settlements == estateCheckedFor && estates == estateChecked))
        {
            return;
        }

        estateCheckedFor = settlements;
        estateChecked = estates;
        var fixes = ManualSettlement.FromEstates(housing.JournalSnapshot, context.ContentId, context.PersonalEstate, context.FreeCompanyEstate);
        if (fixes.IsEmpty)
        {
            return;
        }

        fixing = true;
        _ = housing.ApplySettlementFixes(fixes, "the estate list holds the entered plot").ContinueWith(_ => fixing = false);
    }

    private readonly record struct Estates(ulong? Personal, ulong? FreeCompany);
}
