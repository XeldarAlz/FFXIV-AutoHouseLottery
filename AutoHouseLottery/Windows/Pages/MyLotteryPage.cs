using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Sections;
using AutoHouseLottery.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

// Action needed, Active entries and History (UX section 10), per character, from the journal and the entries imported
// from Timers. Every row has one primary action; the ones that arrive with the results update are shown but disabled.
internal sealed class MyLotteryPage
{
    private const float RowHeight = 64f;
    private const float RowGap = 6f;
    private const float RowPadX = 16f;
    private const float RowPadY = 11f;
    private const float StateGap = 10f;
    private const float SectionGap = 14f;
    private const float RowTint = 0.06f;
    private const string Separator = " · ";
    private const string DateFormat = "g";
    private const int ClaimConfirmWindowMs = 4_000;

    // The logged-in character and the estates it holds, read without building a whole character context each frame.
    private readonly record struct CharacterKey(ulong ContentId, ulong? PersonalEstate, ulong? FreeCompanyEstate);

    // SettledInGame: the row may be marked as settled in game by the player (a receipt, never an attempt in question).
    // Group: for a win, how its ownership group stands, so several wins ask for a choice and a claimed group locks the rest.
    private readonly record struct RowView(
        LotteryRow Row,
        string Id,
        string Title,
        string Facts,
        string Receipt,
        string State,
        string LogInAs,
        string? SettleHint,
        bool SettledInGame,
        ClaimGroupState Group,
        string? GroupHint);

    private ImmutableArray<RowView> rows = [];
    private ImmutableArray<ActionAttempt> cachedAttempts;
    private ImmutableArray<EntryReceipt> cachedReceipts;
    private ImmutableArray<EntrySettlement> cachedSettlements;
    private CyclePosition cachedPosition;
    private System.Globalization.CultureInfo? cachedCulture;
    private int cachedRosterRevision;
    private bool historyOpen;
    private ReconcileReport reconcileFor;
    private LanguageInfo? reconcileLanguage;
    private string? reconcileText;
    private Guid expandedReceipt;
    private string summary = string.Empty;
    private CharacterKey cachedCharacter;
    private int cachedSightings = -1;
    private Guid confirmingSettled;
    private SchedulerBlock statusFor;
    private ulong statusCharacter;
    private LanguageInfo? statusLanguage;
    private string statusText = string.Empty;
    private Guid armedClaim;
    private long armedClaimUntil;

    public void Draw(Plugin plugin)
    {
        PageHeader.Draw(Loc.T(L.MyLottery.Title), Loc.T(L.MyLottery.Subtitle));
        var housing = plugin.HousingData;
        DrawJournalState(housing);
        Refresh(plugin);
        DrawAutomaticRefunds(plugin);
        if (rows.IsEmpty)
        {
            EmptyState.Draw(FontAwesomeIcon.Ticket, Loc.T(L.MyLottery.Empty));
            return;
        }

        var history = 0;
        DrawSection(plugin, LotterySection.ActionNeeded, Loc.T(L.Lottery.ActionNeeded));
        DrawSection(plugin, LotterySection.Active, Loc.T(L.Lottery.Active));
        for (var index = 0; index < rows.Length; index++)
        {
            history += rows[index].Row.Section == LotterySection.History ? 1 : 0;
        }

        if (history == 0)
        {
            return;
        }

        Styling.VSpace(SectionGap);
        if (PillButton.Draw("##ahl_lottery_history", Loc.T(L.Lottery.History, history), Styling.AccentNebula, PillButton.Emphasis.Ghost,
                historyOpen ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight))
        {
            historyOpen = !historyOpen;
        }

        if (historyOpen)
        {
            Styling.VSpace(RowGap);
            DrawRows(plugin, LotterySection.History);
        }
    }

    private void Refresh(Plugin plugin)
    {
        var housing = plugin.HousingData;
        var snapshot = housing.JournalSnapshot;
        var position = housing.CurrentPosition(DateTime.UtcNow);
        var rosterRevision = plugin.Configuration.RosterRevision;
        var character = Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded
            ? new CharacterKey(Svc.PlayerState.ContentId, CharacterContextReader.ReadPersonalEstateId(), CharacterContextReader.ReadFreeCompanyEstateId())
            : default;
        var sightings = plugin.Configuration.AbsenceSightingsRevision;
        if (snapshot.Attempts == cachedAttempts && snapshot.Receipts == cachedReceipts && snapshot.Settlements == cachedSettlements
            && position.Cycle == cachedPosition.Cycle && position.Phase == cachedPosition.Phase
            && ReferenceEquals(Loc.Culture, cachedCulture) && rosterRevision == cachedRosterRevision && character == cachedCharacter && sightings == cachedSightings)
        {
            return;
        }

        cachedCharacter = character;
        cachedSightings = sightings;
        var context = CharacterContextReader.Read();
        summary = Summary(NetReturn.Of(snapshot));

        cachedAttempts = snapshot.Attempts;
        cachedReceipts = snapshot.Receipts;
        cachedSettlements = snapshot.Settlements;
        cachedPosition = position;
        cachedCulture = Loc.Culture;
        cachedRosterRevision = rosterRevision;
        var built = LotteryRows.Build(snapshot, position, housing.Schedule);
        var views = ImmutableArray.CreateBuilder<RowView>(built.Length);
        for (var index = 0; index < built.Length; index++)
        {
            var row = built[index];
            var name = CharacterName(plugin, row.ContentId);
            var group = row.State == LotteryRowState.Won && SettlementLookups.Receipt(snapshot, row.Id) is { } won ? ClaimGroups.Of(snapshot, won, housing.Owner) : default;
            var settleHint = SettleHint(snapshot, row, context);
            var groupHint = group.Claimed ? Loc.T(L.Settlement.GroupClaimed)
                : group.NeedsChoice ? (settleHint is null ? Loc.T(L.Settlement.GroupChoice, group.Wins) : string.Concat(Loc.T(L.Settlement.GroupChoice, group.Wins), " ", settleHint))
                : null;
            views.Add(new RowView(row, $"##ahl_lottery_action_{row.Id:N}", string.Concat(name, Separator, Address(row)), Facts(row, plugin.Configuration.AbsenceSighting(row.Id)), Receipt(row), StateLabel(row.State), Loc.T(L.Lottery.LogInAs, name),
                settleHint, MaySettleInGame(snapshot, row, position), group, groupHint));
        }

        rows = views.MoveToImmutable();
    }

    private void DrawSection(Plugin plugin, LotterySection section, string label)
    {
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].Row.Section != section)
            {
                continue;
            }

            GroupLabel.Draw(label, SectionGap);
            DrawRows(plugin, section);
            return;
        }
    }

    private void DrawRows(Plugin plugin, LotterySection section)
    {
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].Row.Section == section)
            {
                DrawRow(plugin, rows[index]);
            }
        }
    }

    private void DrawRow(Plugin plugin, in RowView view)
    {
        var row = view.Row;
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var expanded = expandedReceipt == row.Id;
        var lineHeight = ImGui.GetTextLineHeight();
        var height = RowHeight * scale + (expanded ? lineHeight + RowGap * scale : 0f);
        var end = origin + new Vector2(width, height);
        var accent = StateColor(row.State);
        Paint.Glass(ImGui.GetWindowDrawList(), origin, end, Styling.CardRounding * scale, accent, RowTint);

        var x = origin.X + RowPadX * scale;
        var right = end.X - RowPadX * scale;
        var top = origin.Y + RowPadY * scale;
        var (actionLabel, actionEnabled, actionTooltip) = ActionOf(plugin, view);
        var actionIcon = ActionIcon(row.Action);
        var actionWidth = PillButton.Width(actionLabel, actionIcon);
        var stateWidth = TextDraw.Measure(view.State).X;
        var confirming = view.SettledInGame && confirmingSettled == row.Id;
        var settledWidth = !view.SettledInGame ? 0f
            : confirming ? PillButton.Width(Loc.T(L.Settlement.SettledInGameConfirm), FontAwesomeIcon.Check) + PillButton.Width(Loc.T(L.Settlement.SettledInGameCancel)) + StateGap * scale
            : PillButton.Width(Loc.T(L.Settlement.SettledInGame), FontAwesomeIcon.HandHoldingUsd);
        var secondaryWidth = settledWidth > 0f ? settledWidth + StateGap * scale : 0f;
        var textRight = right - actionWidth - secondaryWidth - stateWidth - StateGap * 2f * scale;

        TextDraw.At(TextDraw.Truncate(view.Title, textRight - x), new Vector2(x, top), Styling.TextStrong);
        TextDraw.At(TextDraw.Truncate(view.Facts, textRight - x), new Vector2(x, top + lineHeight + RowGap * scale * 0.5f), Styling.TextDim);
        var midY = origin.Y + RowHeight * scale * 0.5f;
        TextDraw.At(view.State, new Vector2(right - actionWidth - secondaryWidth - StateGap * scale - stateWidth, midY - lineHeight * 0.5f), accent);
        if (view.SettledInGame)
        {
            DrawSettledInGame(plugin, row, confirming, right - actionWidth - secondaryWidth, midY - Layout.ActionPillHeight * scale * 0.5f);
        }

        ImGui.SetCursorScreenPos(new Vector2(right - actionWidth, midY - Layout.ActionPillHeight * scale * 0.5f));
        if (PillButton.Draw(view.Id, actionLabel, accent, PillButton.Emphasis.Tinted, actionIcon, actionEnabled, Layout.ActionPillHeight, actionTooltip))
        {
            Act(plugin, view);
        }

        if (expanded)
        {
            TextDraw.At(TextDraw.Truncate(view.Receipt, right - x), new Vector2(x, origin.Y + RowHeight * scale), Styling.TextMuted);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height + RowGap * scale));
    }

    // "Settled in game", then a confirmation in its place: the player's word that the refund or the result was settled by
    // hand. It records the entry as refunded for an amount not seen and never touches the game.
    private void DrawSettledInGame(Plugin plugin, LotteryRow row, bool confirming, float x, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var writable = plugin.HousingData.JournalWritable && !plugin.Controller.Busy;
        if (!confirming)
        {
            if (PillButton.Draw($"##ahl_lottery_settled_{row.Id:N}", Loc.T(L.Settlement.SettledInGame), Styling.AccentNebula, PillButton.Emphasis.Ghost, FontAwesomeIcon.HandHoldingUsd,
                    writable, Layout.ActionPillHeight, Loc.T(L.Settlement.SettledInGameHint)))
            {
                confirmingSettled = row.Id;
            }

            return;
        }

        var confirmLabel = Loc.T(L.Settlement.SettledInGameConfirm);
        if (PillButton.Draw($"##ahl_lottery_settled_yes_{row.Id:N}", confirmLabel, Styling.AccentAmber, PillButton.Emphasis.Tinted, FontAwesomeIcon.Check, writable, Layout.ActionPillHeight,
                Loc.T(L.Settlement.SettledInGameHint)))
        {
            confirmingSettled = Guid.Empty;
            plugin.Controller.MarkSettledInGame(row.Id);
        }

        ImGui.SetCursorScreenPos(new Vector2(x + PillButton.Width(confirmLabel, FontAwesomeIcon.Check) + StateGap * scale, y));
        if (PillButton.Draw($"##ahl_lottery_settled_no_{row.Id:N}", Loc.T(L.Settlement.SettledInGameCancel), Styling.AccentNebula, PillButton.Emphasis.Ghost, null, true, Layout.ActionPillHeight))
        {
            confirmingSettled = Guid.Empty;
        }
    }

    private static bool MaySettleInGame(in JournalSnapshot snapshot, LotteryRow row, in CyclePosition position)
        => SettlementLookups.Receipt(snapshot, row.Id) is { } receipt
        && SettlementLookups.Settlement(snapshot, row.Id) is { } settlement
        && ManualSettlement.CanMarkSettledInGame(settlement.State, SettlementDeadlines.ResultsArrived(receipt.Cycle, position), SettlementLookups.OpenAttempt(snapshot, row.Id) is not null);

    private void Act(Plugin plugin, in RowView view)
    {
        var row = view.Row;
        switch (row.Action)
        {
            case LotteryAction.ViewPlot:
                plugin.ShowPage(AppWindow.Page.FindHome);
                return;
            case LotteryAction.CheckResult:
                Settle(plugin, row, SettlementMode.Inspect);
                return;
            case LotteryAction.CollectRefund:
                Settle(plugin, row, SettlementMode.Refund);
                return;
            case LotteryAction.ClaimHouse:
                Claim(plugin, view);
                return;
            case LotteryAction.VerifyOutcome when row.State == LotteryRowState.SettlementPending:
                Settle(plugin, row, SettlementMode.Inspect);
                return;
            case LotteryAction.VerifyOutcome:
                plugin.Controller.Reconcile();
                return;
            case LotteryAction.ViewReceipt:
                expandedReceipt = expandedReceipt == row.Id ? Guid.Empty : row.Id;
                return;
        }
    }

    private (string Label, bool Enabled, string? Tooltip) ActionOf(Plugin plugin, in RowView view)
    {
        var row = view.Row;
        return row.Action switch
        {
        LotteryAction.ViewPlot => (Loc.T(L.Lottery.ViewPlot), true, null),
        LotteryAction.CheckResult => SettleAction(plugin, view, Loc.T(L.Lottery.CheckResult)),
        LotteryAction.ClaimHouse => ClaimAction(plugin, view),
        LotteryAction.CollectRefund => SettleAction(plugin, view, Loc.T(L.Lottery.CollectRefund)),
        LotteryAction.VerifyOutcome when row.State == LotteryRowState.SettlementPending => SettleAction(plugin, view, Loc.T(L.Lottery.VerifyOutcome)),
        LotteryAction.VerifyOutcome => (Loc.T(L.Lottery.VerifyOutcome), !plugin.Controller.Busy && row.ContentId == Svc.PlayerState.ContentId,
            row.ContentId != Svc.PlayerState.ContentId ? view.LogInAs : ReconcileText(plugin.Controller.LastReconcile)),
        LotteryAction.ViewReceipt => (Loc.T(L.Lottery.ViewReceipt), true, null),
        _ => (view.State, false, null),
        };
    }

    // A settlement action runs only for the logged-in entrant and while nothing else drives the game. Until a refund or
    // claim has been validated in game, the player's own click is a dry run: it reads the result and closes it.
    private static (string Label, bool Enabled, string? Tooltip) SettleAction(Plugin plugin, in RowView view, string label)
    {
        var mine = view.Row.ContentId == Svc.PlayerState.ContentId;
        return (label, mine && !plugin.Controller.Busy && plugin.HousingData.JournalWritable, mine ? view.SettleHint : view.LogInAs);
    }

    // Several wins in one ownership group can be claimed only once: the claim asks for a second press naming this plot as
    // the choice, and once one of them is claimed the others stay locked.
    private (string Label, bool Enabled, string? Tooltip) ClaimAction(Plugin plugin, in RowView view)
    {
        var (label, enabled, tooltip) = SettleAction(plugin, view, Loc.T(L.Lottery.ClaimHouse));
        if (view.Group.Claimed)
        {
            return (label, false, view.GroupHint);
        }

        if (!view.Group.NeedsChoice)
        {
            return (label, enabled, tooltip);
        }

        var armed = armedClaim == view.Row.Id && Environment.TickCount64 < armedClaimUntil;
        return (armed ? Loc.T(L.Settlement.ChooseThisPlot) : label, enabled, view.Row.ContentId == Svc.PlayerState.ContentId ? view.GroupHint : tooltip);
    }

    private void Claim(Plugin plugin, in RowView view)
    {
        if (view.Group.NeedsChoice && (armedClaim != view.Row.Id || Environment.TickCount64 >= armedClaimUntil))
        {
            armedClaim = view.Row.Id;
            armedClaimUntil = Environment.TickCount64 + ClaimConfirmWindowMs;
            return;
        }

        armedClaim = Guid.Empty;
        if (view.Group.NeedsChoice)
        {
            Core.RunLog.Info($"The player chose {Address(view.Row)} to claim out of {view.Group.Wins} wins in one ownership group.");
        }

        Settle(plugin, view.Row, SettlementMode.Claim);
    }

    private static void Settle(Plugin plugin, LotteryRow row, SettlementMode mode)
    {
        var refusal = plugin.Controller.StartSettlement(row.Id, mode, SettlementOrigin.Player, SettlementGate.PlayerDryRun(mode));
        if (refusal != SettlementRefusal.None)
        {
            Svc.Chat.Print($"{Core.HouseLotteryConstants.LogPrefix} {SettlementText.Refusal(refusal)}");
        }
    }

    // What the click will do: a dry run's limits, and for a claim whether it gives a first estate or moves the one held.
    private static string? SettleHint(in JournalSnapshot snapshot, LotteryRow row, CharacterContext? context)
    {
        var mode = row.Action switch
        {
            LotteryAction.ClaimHouse => SettlementMode.Claim,
            LotteryAction.CollectRefund => SettlementMode.Refund,
            _ => SettlementMode.Inspect,
        };
        var dryRun = SettlementGate.PlayerDryRun(mode) && mode != SettlementMode.Inspect ? Loc.T(L.Settlement.DryRunHint) : null;
        if (mode != SettlementMode.Claim || context is null || SettlementLookups.Receipt(snapshot, row.Id) is not { } receipt)
        {
            return dryRun;
        }

        var address = row.Plot is { } plot ? PlotNames.Address(plot) : Loc.T(L.Lottery.PlotNotNamed);
        var effect = ClaimRules.EffectOf(receipt.Buyer, context.FreeCompanyId is not null, context.PersonalEstate, context.FreeCompanyEstate) switch
        {
            ClaimEffect.Acquire => Loc.T(L.Settlement.ClaimAcquires, address),
            ClaimEffect.Relocate => Loc.T(L.Settlement.ClaimRelocates, address),
            _ => Loc.T(L.Settlement.ClaimEffectUnknown, address),
        };
        return dryRun is null ? effect : string.Concat(effect, " ", dryRun);
    }

    // Automatically collect refunds for the logged-in character, what the scheduler waits on, and the net return.
    private void DrawAutomaticRefunds(Plugin plugin)
    {
        var configuration = plugin.Configuration;
        if (Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded)
        {
            var contentId = Svc.PlayerState.ContentId;
            var enabled = configuration.AutomaticRefunds(contentId);
            if (ToggleSwitch.Draw("##ahl_lottery_auto_refunds", ref enabled))
            {
                configuration.SetAutomaticRefunds(contentId, enabled);
                Core.RunLog.Info($"Automatic refunds turned {(enabled ? "on" : "off")} for {Svc.PlayerState.CharacterName}.");
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T(L.Settlement.AutomaticRefunds));
            if (ImGui.IsItemHovered())
            {
                Tooltip.Show(Loc.T(L.Settlement.AutomaticRefundsHelp));
            }

            ImGui.SameLine();
            ImGui.TextColored(Styling.TextDim, SchedulerStatus(plugin.Controller.SchedulerBlock, contentId));
            DrawSwitchForRefunds(plugin);
        }

        if (summary.Length > 0)
        {
            ImGui.TextColored(Styling.TextDim, summary);
        }

        Styling.VSpace(RowGap);
    }

    // UX section 10: a separate, explicit option tied to the roster selected on Find a home. It is shown once the plugin
    // knows more than one character, and says so while switching is not allowed.
    private static void DrawSwitchForRefunds(Plugin plugin)
    {
        var configuration = plugin.Configuration;
        if (configuration.Characters.Count < 2)
        {
            return;
        }

        var enabled = configuration.SwitchForRefunds;
        if (ToggleSwitch.Draw("##ahl_lottery_switch_refunds", ref enabled))
        {
            configuration.SetSwitchForRefunds(enabled);
            Core.RunLog.Info($"Switch characters to collect refunds turned {(enabled ? "on" : "off")}.");
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(Loc.T(L.Settlement.SwitchForRefunds));
        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Settlement.SwitchForRefundsHelp));
        }

        if (enabled && !plugin.Controller.RefundSwitchingAllowed)
        {
            ImGui.SameLine();
            ImGui.TextColored(Styling.TextDim, Loc.T(L.Settlement.SwitchingNotAllowed));
        }
    }

    private string SchedulerStatus(SchedulerBlock block, ulong contentId)
    {
        if (block != statusFor || contentId != statusCharacter || !ReferenceEquals(Loc.Current, statusLanguage))
        {
            statusFor = block;
            statusCharacter = contentId;
            statusLanguage = Loc.Current;
            statusText = SettlementText.Block(block);
        }

        return statusText;
    }

    private static string Summary(in NetReturnSummary net)
    {
        var line = string.Concat(
            Loc.T(L.Settlement.SummaryReceived, Loc.Gil(net.RefundReceived)), Separator,
            Loc.T(L.Settlement.SummaryTravel, Loc.Gil(net.TravelSpent)), Separator,
            Loc.T(L.Settlement.SummaryNet, Loc.Gil(net.NetReturned)), Separator,
            Loc.T(L.Settlement.SummaryPending, Loc.Gil(net.StillPending)));
        return net.AmountsUnknown == 0 ? line : string.Concat(line, Separator, Loc.Plural(L.Settlement.SummaryUnknown, net.AmountsUnknown));
    }

    // The last read-back's result, formatted again only when it changes.
    private string? ReconcileText(in ReconcileReport report)
    {
        if (report != reconcileFor || !ReferenceEquals(Loc.Current, reconcileLanguage))
        {
            reconcileFor = report;
            reconcileLanguage = Loc.Current;
            reconcileText = report.Outcome == ReconcileOutcome.NotRun ? null : RunText.Reconcile(report);
        }

        return reconcileText;
    }

    private static FontAwesomeIcon ActionIcon(LotteryAction action) => action switch
    {
        LotteryAction.ViewPlot => FontAwesomeIcon.MapMarkerAlt,
        LotteryAction.CheckResult => FontAwesomeIcon.Search,
        LotteryAction.ClaimHouse => FontAwesomeIcon.Home,
        LotteryAction.CollectRefund => FontAwesomeIcon.Coins,
        LotteryAction.VerifyOutcome => FontAwesomeIcon.Sync,
        LotteryAction.ViewReceipt => FontAwesomeIcon.Receipt,
        _ => FontAwesomeIcon.Minus,
    };

    private static string CharacterName(Plugin plugin, ulong contentId)
        => plugin.Configuration.Characters.TryGetValue(contentId, out var known) && known.Name.Length > 0 ? known.Name : Loc.T(L.Lottery.UnknownCharacter);

    private static string Address(LotteryRow row)
    {
        if (row.Plot is { } plot)
        {
            return PlotNames.Address(plot);
        }

        return row.AddressText ?? Loc.T(L.Lottery.PlotNotNamed);
    }

    private static string Facts(LotteryRow row, AbsenceSighting? sighting)
    {
        var number = row.EntryNumber is { } entryNumber ? Loc.T(L.Lottery.Number, entryNumber) : Loc.T(L.Lottery.NumberUnknown);
        var deposit = row.Deposit is { } amount ? Loc.T(L.Lottery.Deposit, Loc.Gil(amount)) : Loc.T(L.Lottery.DepositUnknown);
        var facts = string.Concat(number, Separator, deposit);
        if (row.Section != LotterySection.History)
        {
            facts = string.Concat(facts, Separator, Deadline(row));
        }

        if (row.Section != LotterySection.History && sighting is { } seen)
        {
            facts = string.Concat(facts, Separator, Loc.T(L.Settlement.AbsenceMarker, seen.AtUtc.ToLocalTime().ToString(DateFormat, Loc.Culture)));
        }

        return row.Imported ? string.Concat(facts, Separator, Loc.T(L.Lottery.Imported)) : facts;
    }

    private static string Deadline(LotteryRow row)
    {
        if (row.DeadlineUtc is not { } deadline)
        {
            return Loc.T(L.Lottery.DeadlineUnknown);
        }

        var local = deadline.ToLocalTime().ToString(DateFormat, Loc.Culture);
        return Loc.T(row.State == LotteryRowState.Entered ? L.Lottery.EntriesClose : L.Lottery.ResultsUntil, local);
    }

    private static string Receipt(LotteryRow row)
    {
        var evidence = Loc.T(row.Evidence switch
        {
            ReceiptEvidence.ImportedFromGame => L.Lottery.EvidenceImported,
            ReceiptEvidence.Reconciled => L.Lottery.EvidenceReconciled,
            _ => L.Lottery.EvidenceConfirmation,
        });
        return row.EnteredUtc is { } entered ? string.Concat(Loc.T(L.Lottery.EnteredAt, entered.ToLocalTime().ToString(DateFormat, Loc.Culture)), Separator, evidence) : evidence;
    }

    private static string StateLabel(LotteryRowState state) => Loc.T(state switch
    {
        LotteryRowState.Entered => L.Lottery.StateEntered,
        LotteryRowState.ResultsAvailable => L.Lottery.StateResultsAvailable,
        LotteryRowState.Won => L.Lottery.StateWon,
        LotteryRowState.RefundDue => L.Lottery.StateRefundDue,
        LotteryRowState.SettlementPending => L.Lottery.StateSettlementPending,
        LotteryRowState.Uncertain => L.Lottery.StateUncertain,
        LotteryRowState.Settled => L.Lottery.StateSettled,
        _ => L.Lottery.StateRejected,
    });

    private static Vector4 StateColor(LotteryRowState state) => state switch
    {
        LotteryRowState.Entered => Styling.AccentBlue,
        LotteryRowState.Won => Styling.AccentMint,
        LotteryRowState.RefundDue or LotteryRowState.ResultsAvailable => Styling.AccentAmber,
        LotteryRowState.Uncertain or LotteryRowState.SettlementPending => Styling.AccentRose,
        _ => Styling.AccentNebula,
    };

    private static void DrawJournalState(HousingContext housing)
    {
        var message = housing.JournalStatus switch
        {
            JournalOpenStatus.Locked => L.Lottery.JournalLocked,
            JournalOpenStatus.Corrupt => L.Lottery.JournalCorrupt,
            JournalOpenStatus.NewerSchema => L.Lottery.JournalNewerSchema,
            JournalOpenStatus.MigrationFailed => L.Lottery.JournalMigrationFailed,
            JournalOpenStatus.ReadFailed => L.Lottery.JournalReadFailed,
            _ when !housing.JournalWritable => L.Lottery.JournalRecoveryFailed,
            _ => (LocString?)null,
        };
        if (message is not { } text)
        {
            return;
        }

        var card = NoticeCard.Begin(FontAwesomeIcon.ExclamationTriangle, Loc.T(L.Lottery.JournalTitle), Loc.T(text), housing.JournalBackupPath, Styling.AccentRose, hasButtons: false);
        NoticeCard.End(card);
        Styling.VSpace(SectionGap);
    }
}
