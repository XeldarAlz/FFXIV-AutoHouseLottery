using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

// Version 1.1's Characters expander (UX section 7) and the batch preview and Enter for N characters (UX section 8). Each
// ticked character makes at most one entry with its own buyer type, target, deposit cap, reserve and service account; a
// character that is not logged in is planned from its last login, its gil marked as an estimate.
internal sealed partial class FindHomePage
{
    private const float CharacterIndent = 26f;
    private const float CharacterLabelWidth = 92f;
    private const float CharacterFieldGap = 6f;

    private readonly CharactersText charactersText = new();
    private readonly StartCheck batchCheck = new();
    private readonly Dictionary<ulong, GilField.Buffer> capBuffers = [];
    private readonly Dictionary<ulong, GilField.Buffer> reserveBuffers = [];

    private void DrawCharacters(FindView view)
    {
        var roster = State.Roster;
        if (roster.IsEmpty)
        {
            return;
        }

        charactersText.Refresh(view, Discovery, State, plugin.Controller.BatchSwitchingAllowed);
        if (!ImGui.CollapsingHeader(charactersText.HeaderLabel))
        {
            return;
        }

        TextDraw.Hint(Loc.T(L.Characters.Help));
        var rows = charactersText.Rows;
        for (var index = 0; index < rows.Length; index++)
        {
            ImGui.PushID(index);
            DrawCharacterRow(rows[index]);
            ImGui.PopID();
            Styling.VSpace(4f);
        }
    }

    private void DrawCharacterRow(CharacterRowText row)
    {
        var entry = row.Entry;
        var contentId = entry.ContentId;
        var selected = entry.Selection.Selected;
        if (ImGui.Checkbox("##selected", ref selected))
        {
            State.SetSelected(contentId, selected);
        }

        ImGui.SameLine();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        TextDraw.At(TextDraw.Truncate(row.Title, width), origin, selected ? Styling.TextStrong : Styling.TextSecondary);
        ImGui.Dummy(new Vector2(width, TextDraw.LineHeight()));
        ImGui.SetCursorScreenPos(new Vector2(origin.X, ImGui.GetCursorScreenPos().Y));
        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(row.Status, width), ImGui.GetCursorScreenPos(), row.StatusColor);
            ImGui.Dummy(new Vector2(width, TextDraw.LineHeight()));
        }

        if (!selected)
        {
            return;
        }

        using var indent = ImRaii.PushIndent(CharacterIndent * ImGuiHelpers.GlobalScale, false);
        DrawCharacterBuyer(row, entry.Selection.Buyer == BuyerType.Unknown ? State.Filter.Buyer : entry.Selection.Buyer);
        DrawCharacterTarget(row);
        DrawCharacterLimits(row, entry.Selection.DepositCap, entry.Selection.Reserve);
        DrawCharacterAccount(row, entry.Facts.AccountGroup);
        if (row.TargetIndex < 0)
        {
            Reason(Loc.T(L.Characters.TargetGone));
        }

        if (row.Exclusion is { } exclusion)
        {
            Reason(exclusion);
        }
    }

    private void DrawCharacterBuyer(CharacterRowText row, BuyerType buyer)
    {
        CharacterLabel(Loc.T(L.Characters.Buyer));
        var height = ImGui.GetFrameHeight();
        if (FilterChip.Draw(row.Ids.Private, Loc.T(L.Find.BuyerPrivate), null, Styling.AccentNebula, buyer == BuyerType.Private, height))
        {
            State.SetBuyer(row.Entry.ContentId, BuyerType.Private);
        }

        ImGui.SameLine(0f, CharacterFieldGap * ImGuiHelpers.GlobalScale);
        if (FilterChip.Draw(row.Ids.FreeCompany, Loc.T(L.Find.BuyerFreeCompany), null, Styling.AccentNebula, buyer == BuyerType.FreeCompany, height))
        {
            State.SetBuyer(row.Entry.ContentId, BuyerType.FreeCompany);
        }
    }

    private void DrawCharacterTarget(CharacterRowText row)
    {
        CharacterLabel(Loc.T(L.Characters.Target));
        var selected = Math.Max(0, row.TargetIndex);
        if (Dropdown.Draw(row.Ids.Target, row.TargetLabels, ref selected, FieldWidth()))
        {
            State.SetTarget(row.Entry.ContentId, row.TargetKeys[selected]);
        }
    }

    private void DrawCharacterLimits(CharacterRowText row, long cap, long? reserve)
    {
        var contentId = row.Entry.ContentId;
        CharacterLabel(Loc.T(L.Characters.Cap));
        if (GilField.Draw(row.Ids.Cap, BufferFor(capBuffers, contentId), cap, long.MaxValue, Loc.T(L.Characters.CapHint), FieldWidth(), out var newCap))
        {
            State.SetDepositCap(contentId, newCap);
        }

        CharacterLabel(Loc.T(L.Find.Reserve));
        if (GilField.Draw(row.Ids.Reserve, BufferFor(reserveBuffers, contentId), reserve ?? 0, long.MaxValue, Loc.T(L.Characters.ReserveHint), FieldWidth(), out var newReserve, Loc.T(L.Find.ReserveTooltip)))
        {
            State.SetReserve(contentId, newReserve > 0 ? newReserve : null);
        }
    }

    private static float FieldWidth() => MathF.Max(1f, ImGui.GetContentRegionAvail().X / ImGuiHelpers.GlobalScale);

    private void DrawCharacterAccount(CharacterRowText row, int? accountGroup)
    {
        CharacterLabel(Loc.T(L.Characters.Account));
        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Characters.AccountHelp));
        }

        var selected = accountGroup is { } group and > 0 and <= CharactersText.AccountGroups ? group : 0;
        if (Dropdown.Draw(row.Ids.Account, charactersText.AccountLabels, ref selected, FieldWidth()))
        {
            State.SetAccountGroup(row.Entry.ContentId, selected == 0 ? null : selected);
        }
    }

    private static void CharacterLabel(string label)
    {
        var origin = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        TextDraw.At(TextDraw.Truncate(label, CharacterLabelWidth * ImGuiHelpers.GlobalScale), new Vector2(origin.X, origin.Y + (height - TextDraw.LineHeight()) * 0.5f), Styling.TextDim);
        ImGui.Dummy(new Vector2(CharacterLabelWidth * ImGuiHelpers.GlobalScale, height));
        ImGui.SameLine(0f, CharacterFieldGap * ImGuiHelpers.GlobalScale);
    }

    private static GilField.Buffer BufferFor(Dictionary<ulong, GilField.Buffer> buffers, ulong contentId)
    {
        if (!buffers.TryGetValue(contentId, out var buffer))
        {
            buffer = new GilField.Buffer();
            buffers[contentId] = buffer;
        }

        return buffer;
    }

    // UX section 8 for several characters: placement and its odds, each character's destination and funds, what is left
    // out and why, the total of the deposits, and whether the run switches characters.
    private void DrawBatchPreview(FindView view, BatchView batch)
    {
        TextDraw.SmallCaps(Loc.T(L.Find.PreviewTitle), ImGui.GetCursorScreenPos(), Styling.AccentGlowSoft);
        ImGui.Dummy(new Vector2(1f, TextDraw.CaptionLineHeight()));
        TextDraw.Hint(charactersText.Counts);
        var preview = batch.Preview;
        switch (preview.PolicyError)
        {
            case RunPolicyError.None:
                break;
            case RunPolicyError.MissingBuyerType:
                Reason(Loc.T(L.Find.NeedBuyer));
                return;
            case RunPolicyError.NonPositiveDepositCap:
                Reason(Loc.T(L.Find.NeedCeiling));
                return;
            case RunPolicyError.InvalidExactTarget:
                Reason(Loc.T(L.Batch.TargetNotOnShortlist));
                return;
            default:
                return;
        }

        DrawPlacement(preview);
        Styling.VSpace(6f);
        for (var index = 0; index < charactersText.Lines.Length; index++)
        {
            var line = charactersText.Lines[index];
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(line.Title);
            ImGui.PopTextWrapPos();
            using (Fonts.PushCaption())
            {
                TextDraw.Hint(line.Detail);
                if (line.Note is { } note)
                {
                    TextDraw.Hint(note);
                }
            }
        }

        for (var index = 0; index < charactersText.Exclusions.Length; index++)
        {
            Reason(charactersText.Exclusions[index]);
        }

        if (preview.Assignments.Length > 0)
        {
            PreviewLine(Loc.T(L.Batch.TotalDeposits), charactersText.Total);
            TextDraw.Hint(Loc.T(L.Batch.NotPooled));
        }

        DrawReserve();
        TextDraw.Hint(charactersText.Switching);
        DrawBlockers(preview.Blockers);
    }

    private void DrawPlacement(RunPreview preview)
    {
        var label = Loc.T(L.Batch.Placement);
        TextDraw.At(label, ImGui.GetCursorScreenPos(), Styling.TextDim);
        ImGui.Dummy(new Vector2(TextDraw.Measure(label).X, TextDraw.LineHeight()));
        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Batch.PlacementHelp));
        }

        var items = (ReadOnlySpan<Segmented.Item>)
        [
            new(null, Loc.T(L.Batch.PlacementAutomatic)),
            new(null, Loc.T(L.Batch.PlacementSamePlot)),
            new(null, Loc.T(L.Batch.PlacementSpread)),
        ];
        var placement = (int)State.Placement;
        if (Segmented.Draw("##ahl_find_placement", items, ref placement))
        {
            State.SetPlacement((PlacementChoice)placement);
        }

        if (charactersText.OddsAny is { } any)
        {
            using (Fonts.PushCaption())
            {
                TextDraw.Hint(Loc.T(L.Batch.OddsTitle));
                TextDraw.Hint(any);
                if (charactersText.OddsTwoOrMore is { } twoOrMore)
                {
                    TextDraw.Hint(twoOrMore);
                }
            }
        }

        if ((preview.Blockers & RunBlocker.MultipleWinsNeedConfirmation) == 0 && !(State.MultipleWinRiskAccepted && !preview.GroupOdds.IsEmpty))
        {
            return;
        }

        Reason(Loc.T(L.Batch.MultipleWinsWarning));
        var accepted = State.MultipleWinRiskAccepted;
        if (ImGui.Checkbox(Loc.T(L.Batch.MultipleWinsConfirm), ref accepted))
        {
            State.AcceptMultipleWins(accepted);
        }
    }

    // Enter for N characters starts a real entry for each character. While the controller would refuse it, the button is
    // locked and its reason is shown.
    private void DrawBatchEnter(FindView view, BatchView batch)
    {
        var controller = plugin.Controller;
        var current = view.QueryRevision == State.QueryRevision
            && view.ShortlistRevision == State.Shortlist.Revision
            && batch.RosterRevision == State.RosterRevision
            && batch.Policy is { } fresh
            && fresh.MultipleWinRiskAccepted == State.MultipleWinRiskAccepted
            && fresh.Placement == State.Placement;
        var policy = current ? batch.Policy : null;
        var reason = policy is null ? Loc.T(L.Find.BlockerNoAssignment) : batchCheck.BatchReason(controller, policy);
        var enabled = policy is not null && reason.Length == 0;
        if (HeroButton.Draw(FontAwesomeIcon.Users, charactersText.EnterTitle, null, Styling.AccentGlow, enabled) && policy is not null && enabled)
        {
            var refusal = controller.StartBatch(policy);
            batchCheck.Invalidate();
            if (refusal != StartRefusal.None)
            {
                notice.Show(Core.Tasks.RunText.Refusal(refusal), Styling.AccentRose);
            }
        }

        Styling.VSpace(4f);
        ImGui.PushTextWrapPos();
        using (ImRaii.PushColor(ImGuiCol.Text, enabled ? Styling.TextDim : Styling.TextMuted))
        {
            ImGui.TextUnformatted(enabled ? Loc.T(L.Run.RealEntryHint) : reason);
        }

        ImGui.PopTextWrapPos();
    }
}
