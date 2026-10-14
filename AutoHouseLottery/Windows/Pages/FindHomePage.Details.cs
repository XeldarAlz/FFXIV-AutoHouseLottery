using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

internal sealed partial class FindHomePage
{
    private const float FieldGap = 4f;
    private const string ListPopup = "##ahl_find_save_to_list";

    private readonly DetailsText detailsText = new();
    private readonly StartCheck exactCheck = new();
    private readonly StartCheck bestMatchCheck = new();

    private void DrawDetails(FindView view, PlotKey key, DateTime now)
    {
        if (PillButton.Draw("##ahl_find_back", Loc.T(L.Find.Back), Styling.AccentGlow, PillButton.Emphasis.Ghost, FontAwesomeIcon.ChevronLeft))
        {
            details = null;
            return;
        }

        detailsText.Refresh(key, view, State, Discovery, now);
        Styling.VSpace(6f);
        using (Fonts.PushHeadline())
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(detailsText.Address);
            ImGui.PopTextWrapPos();
        }

        TextDraw.Hint(detailsText.World);
        DrawStatusLine();
        Styling.VSpace(8f);
        if (!detailsText.Listed)
        {
            TextDraw.Hint(Loc.T(L.Find.NoListing));
        }

        for (var index = 0; index < detailsText.Fields.Length; index++)
        {
            Field(detailsText.Fields[index]);
        }

        if (detailsText.Conflicted)
        {
            Reason(Loc.T(L.Find.ConflictNote));
        }

        Styling.VSpace(8f);
        DrawDetailActions(key);
        Styling.VSpace(10f);
        DrawPlotEligibility();
        if (detailsText.Sources.Length > 0)
        {
            Styling.VSpace(8f);
            DrawSources();
        }
    }

    private void DrawStatusLine()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var radius = StatusMarkRadius * scale;
        var lineHeight = TextDraw.LineHeight();
        StatusMarks.Draw(ImGui.GetWindowDrawList(), origin + new Vector2(radius, lineHeight * 0.5f), radius, detailsText.Status);
        TextDraw.At(TextDraw.Truncate(detailsText.StatusLine, ImGui.GetContentRegionAvail().X - radius * 3f), origin + new Vector2(radius * 3f, 0f), detailsText.StatusColor);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, lineHeight));
        if (detailsText.Status is { } known && ImGui.IsItemHovered())
        {
            Tooltip.Show(FindText.StatusHelp(known));
        }
    }

    private static void Field(in DetailField field)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        TextDraw.At(field.Label, origin, Styling.TextDim);
        var valueWidth = MathF.Min(TextDraw.Measure(field.Value).X, width * 0.6f);
        TextDraw.At(TextDraw.Truncate(field.Value, width * 0.6f), new Vector2(origin.X + width - valueWidth, origin.Y), Styling.TextStrong);
        var height = TextDraw.LineHeight();
        if (field.Age is { } age)
        {
            using (Fonts.PushCaption())
            {
                TextDraw.Right(age, origin.X + width, origin.Y + height, Styling.TextMuted);
                height += TextDraw.LineHeight();
            }
        }

        ImGui.Dummy(new Vector2(width, height + FieldGap * ImGuiHelpers.GlobalScale));
    }

    private void DrawDetailActions(PlotKey key)
    {
        var shortlisted = State.Shortlist.Contains(key);
        var label = Loc.T(shortlisted ? L.Find.RemoveFromShortlist : L.Find.AddToShortlist);
        if (PillButton.Draw("##ahl_find_detail_shortlist", label, Styling.AccentGlow, shortlisted ? PillButton.Emphasis.Ghost : PillButton.Emphasis.Filled, shortlisted ? FontAwesomeIcon.Minus : FontAwesomeIcon.Plus))
        {
            State.Toggle(key);
        }

        var anchor = ImGui.GetCursorScreenPos();
        if (PillButton.Draw("##ahl_find_detail_save", Loc.T(L.Find.SaveToList), Styling.AccentAmber, PillButton.Emphasis.Tinted, FontAwesomeIcon.Star))
        {
            ImGui.SetNextWindowPos(anchor + new Vector2(0f, ImGui.GetFrameHeight() + 6f * ImGuiHelpers.GlobalScale));
            ImGui.OpenPopup(ListPopup);
        }

        DrawSaveToListPopup(key);
        ImGui.SameLine();
        if (IconButton.Draw(FontAwesomeIcon.Copy, "##ahl_find_detail_copy", ImGui.GetFrameHeight(), Styling.TextSecondary, Loc.T(L.Find.CopyAddress)))
        {
            ImGui.SetClipboardText(PlotLabels.AddressOnWorld(Discovery.Names, key));
            notice.Show(Loc.T(L.Find.Copied), Styling.AccentMint);
        }

        ImGui.SameLine();
        var district = key.DistrictId;
        var preferred = State.IsPreferred(district);
        if (IconButton.Draw(FontAwesomeIcon.Heart, "##ahl_find_detail_prefer", ImGui.GetFrameHeight(), preferred ? Styling.AccentRose : Styling.TextMuted, Loc.T(L.Find.PreferDistrict)))
        {
            State.TogglePreferred(district);
        }

        Styling.VSpace(6f);
        if (detailsText.EnterTitle is { } enterTitle)
        {
            DrawEnterAction(enterTitle, RunMode.Exact, detailsText.EnterPolicy, detailsText.EnterMissing, exactCheck);
            return;
        }

        ImGui.PushTextWrapPos();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextUnformatted(Loc.T(L.Find.EnterWhenOpen));
        }

        ImGui.PopTextWrapPos();
    }

    // Enter asks for a dry run until entries are validated in game, and a requested dry run no grant can turn real; it
    // starts from the frozen policy. While the controller would refuse it, the button is locked and its reason is shown.
    private void DrawEnterAction(string title, RunMode mode, RunPolicy? policy, string? missing, StartCheck check)
    {
        var controller = plugin.Controller;
        var reason = policy is null ? missing ?? string.Empty : check.Reason(controller, mode, policy);
        var enabled = policy is not null && reason.Length == 0;
        if (HeroButton.Draw(FontAwesomeIcon.Ticket, title, BuyerLabel(State.Filter.Buyer), Styling.AccentGlow, enabled) && policy is not null && enabled)
        {
            var dryRun = EntryCapabilities.PlayerDryRun;
            var refusal = mode == RunMode.Exact ? controller.StartExact(policy, dryRun) : controller.StartBestMatch(policy, dryRun);
            check.Invalidate();
            if (refusal != StartRefusal.None)
            {
                notice.Show(RunText.Refusal(refusal), Styling.AccentRose);
            }
        }

        Styling.VSpace(4f);
        ImGui.PushTextWrapPos();
        using (ImRaii.PushColor(ImGuiCol.Text, enabled ? Styling.TextDim : Styling.TextMuted))
        {
            ImGui.TextUnformatted(enabled ? Loc.T(EntryCapabilities.PlayerDryRun ? L.Run.DryRunHint : L.Run.RealEntryHint) : reason);
        }

        ImGui.PopTextWrapPos();
    }

    private static string? BuyerLabel(BuyerType buyer) => buyer switch
    {
        BuyerType.Private => Loc.T(L.Find.BuyerPrivate),
        BuyerType.FreeCompany => Loc.T(L.Find.BuyerFreeCompany),
        _ => null,
    };

    private void DrawSaveToListPopup(PlotKey key)
    {
        using var popup = ContextMenu.Begin(ListPopup);
        if (!popup.Open)
        {
            return;
        }

        var saved = Discovery.Saved;
        var lists = saved.Library.Lists;
        for (var index = 0; index < lists.Count; index++)
        {
            var list = lists[index];
            var contains = list.Contains(key);
            ImGui.PushID(index);
            if (ImGui.Selectable(list.Name, contains))
            {
                if (contains)
                {
                    saved.RemoveFromList(list.Id, key);
                }
                else
                {
                    saved.AddToList(list.Id, key);
                }
            }

            ImGui.PopID();
        }

        if (ImGui.Selectable(Loc.T(L.Find.NewList)))
        {
            saved.SaveList(PlotLabels.Address(Discovery.Names, key), [key], DateTime.UtcNow);
        }
    }

    private void DrawPlotEligibility()
    {
        if (Discovery.Character is null)
        {
            return;
        }

        TextDraw.SmallCaps(detailsText.ForCharacter, ImGui.GetCursorScreenPos(), Styling.AccentGlowSoft);
        ImGui.Dummy(new Vector2(1f, TextDraw.CaptionLineHeight()));
        if (detailsText.Eligibility is not { } eligibility)
        {
            Reason(Loc.T(L.Find.NeedBuyer));
            return;
        }

        DrawEligibility(eligibility);
        if (detailsText.Blockers is { } blockers)
        {
            Reason(blockers);
        }
    }

    private void DrawSources()
    {
        if (!ImGui.CollapsingHeader(Loc.T(L.Find.SourceDetails)))
        {
            return;
        }

        using var caption = Fonts.PushCaption();
        ImGui.PushTextWrapPos();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
        {
            for (var index = 0; index < detailsText.Sources.Length; index++)
            {
                ImGui.TextUnformatted(detailsText.Sources[index]);
            }
        }

        ImGui.PopTextWrapPos();
    }
}
