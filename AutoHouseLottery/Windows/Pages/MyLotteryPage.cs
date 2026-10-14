using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Persistence.Model;
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

    private readonly record struct RowView(LotteryRow Row, string Id, string Title, string Facts, string Receipt, string State);

    private ImmutableArray<RowView> rows = [];
    private ImmutableArray<ActionAttempt> cachedAttempts;
    private ImmutableArray<EntryReceipt> cachedReceipts;
    private ImmutableArray<EntrySettlement> cachedSettlements;
    private CyclePosition cachedPosition;
    private System.Globalization.CultureInfo? cachedCulture;
    private int cachedCharacterCount;
    private bool historyOpen;
    private Guid expandedReceipt;

    public void Draw(Plugin plugin)
    {
        PageHeader.Draw(Loc.T(L.MyLottery.Title), Loc.T(L.MyLottery.Subtitle));
        var housing = plugin.HousingData;
        DrawJournalState(housing);
        Refresh(plugin);
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
        var characters = plugin.Configuration.Characters.Count;
        if (snapshot.Attempts == cachedAttempts && snapshot.Receipts == cachedReceipts && snapshot.Settlements == cachedSettlements
            && position.Cycle == cachedPosition.Cycle && position.Phase == cachedPosition.Phase
            && ReferenceEquals(Loc.Culture, cachedCulture) && characters == cachedCharacterCount)
        {
            return;
        }

        cachedAttempts = snapshot.Attempts;
        cachedReceipts = snapshot.Receipts;
        cachedSettlements = snapshot.Settlements;
        cachedPosition = position;
        cachedCulture = Loc.Culture;
        cachedCharacterCount = characters;
        var built = LotteryRows.Build(snapshot, position, housing.Schedule);
        var views = ImmutableArray.CreateBuilder<RowView>(built.Length);
        for (var index = 0; index < built.Length; index++)
        {
            var row = built[index];
            views.Add(new RowView(row, $"##ahl_lottery_action_{row.Id:N}", string.Concat(CharacterName(plugin, row.ContentId), Separator, Address(row)), Facts(row), Receipt(row), StateLabel(row.State)));
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
        var (actionLabel, actionEnabled, actionTooltip) = ActionOf(plugin, row);
        var actionIcon = ActionIcon(row.Action);
        var actionWidth = PillButton.Width(actionLabel, actionIcon);
        var stateWidth = TextDraw.Measure(view.State).X;
        var textRight = right - actionWidth - stateWidth - StateGap * 2f * scale;

        TextDraw.At(TextDraw.Truncate(view.Title, textRight - x), new Vector2(x, top), Styling.TextStrong);
        TextDraw.At(TextDraw.Truncate(view.Facts, textRight - x), new Vector2(x, top + lineHeight + RowGap * scale * 0.5f), Styling.TextDim);
        var midY = origin.Y + RowHeight * scale * 0.5f;
        TextDraw.At(view.State, new Vector2(right - actionWidth - StateGap * scale - stateWidth, midY - lineHeight * 0.5f), accent);

        ImGui.SetCursorScreenPos(new Vector2(right - actionWidth, midY - Layout.ActionPillHeight * scale * 0.5f));
        if (PillButton.Draw(view.Id, actionLabel, accent, PillButton.Emphasis.Tinted, actionIcon, actionEnabled, Layout.ActionPillHeight, actionTooltip))
        {
            Act(plugin, row);
        }

        if (expanded)
        {
            TextDraw.At(TextDraw.Truncate(view.Receipt, right - x), new Vector2(x, origin.Y + RowHeight * scale), Styling.TextMuted);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height + RowGap * scale));
    }

    private void Act(Plugin plugin, LotteryRow row)
    {
        switch (row.Action)
        {
            case LotteryAction.ViewPlot:
                plugin.ShowPage(AppWindow.Page.FindHome);
                return;
            case LotteryAction.VerifyOutcome:
                plugin.Controller.Reconcile();
                return;
            case LotteryAction.ViewReceipt:
                expandedReceipt = expandedReceipt == row.Id ? Guid.Empty : row.Id;
                return;
        }
    }

    private static (string Label, bool Enabled, string? Tooltip) ActionOf(Plugin plugin, LotteryRow row) => row.Action switch
    {
        LotteryAction.ViewPlot => (Loc.T(L.Lottery.ViewPlot), true, null),
        LotteryAction.CheckResult => (Loc.T(L.Lottery.CheckResult), false, Loc.T(L.Lottery.ArrivesLater)),
        LotteryAction.ClaimHouse => (Loc.T(L.Lottery.ClaimHouse), false, Loc.T(L.Lottery.ArrivesLater)),
        LotteryAction.CollectRefund => (Loc.T(L.Lottery.CollectRefund), false, Loc.T(L.Lottery.ArrivesLater)),
        LotteryAction.VerifyOutcome when row.State == LotteryRowState.SettlementPending => (Loc.T(L.Lottery.VerifyOutcome), false, Loc.T(L.Lottery.ArrivesLater)),
        LotteryAction.VerifyOutcome => (Loc.T(L.Lottery.VerifyOutcome), !plugin.Controller.Busy && row.ContentId == Svc.PlayerState.ContentId,
            row.ContentId != Svc.PlayerState.ContentId ? Loc.T(L.Lottery.LogInAs, CharacterName(plugin, row.ContentId))
            : plugin.Controller.LastReconcile.Outcome == ReconcileOutcome.NotRun ? null
            : RunText.Reconcile(plugin.Controller.LastReconcile)),
        LotteryAction.ViewReceipt => (Loc.T(L.Lottery.ViewReceipt), true, null),
        _ => (StateLabel(row.State), false, null),
    };

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

    private static string Facts(LotteryRow row)
    {
        var number = row.EntryNumber is { } entryNumber ? Loc.T(L.Lottery.Number, entryNumber) : Loc.T(L.Lottery.NumberUnknown);
        var deposit = row.Deposit is { } amount ? Loc.T(L.Lottery.Deposit, Loc.Gil(amount)) : Loc.T(L.Lottery.DepositUnknown);
        var facts = string.Concat(number, Separator, deposit);
        if (row.Section != LotterySection.History)
        {
            facts = string.Concat(facts, Separator, Deadline(row));
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
