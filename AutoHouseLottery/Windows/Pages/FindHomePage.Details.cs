using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
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

    private void DrawDetails(FindView view, PlotKey key, DateTime now)
    {
        if (PillButton.Draw("##ahl_find_back", Loc.T(L.Find.Back), Styling.AccentGlow, PillButton.Emphasis.Ghost, FontAwesomeIcon.ChevronLeft))
        {
            details = null;
            return;
        }

        Styling.VSpace(6f);
        using (Fonts.PushHeadline())
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(PlotLabels.Address(Discovery.Names, key));
            ImGui.PopTextWrapPos();
        }

        TextDraw.Hint(Discovery.Names.World(key.WorldId));
        var plot = view.Plot(key);
        var status = plot is null ? (PlotStatus?)null : PlotStatuses.Classify(plot, view.Position);
        DrawStatusLine(plot, status, now);
        Styling.VSpace(8f);
        if (plot is null)
        {
            TextDraw.Hint(Loc.T(L.Find.NoListing));
        }
        else
        {
            DrawFields(plot, view.Position, now);
        }

        Styling.VSpace(8f);
        DrawDetailActions(key, plot, status);
        Styling.VSpace(10f);
        DrawPlotEligibility(view, key);
        if (plot is not null)
        {
            Styling.VSpace(8f);
            DrawSources(plot, now);
        }
    }

    private static void DrawStatusLine(PlotSnapshot? plot, PlotStatus? status, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var radius = StatusMarkRadius * scale;
        var lineHeight = TextDraw.LineHeight();
        StatusMarks.Draw(ImGui.GetWindowDrawList(), origin + new Vector2(radius, lineHeight * 0.5f), radius, status);
        var label = FindText.Status(status);
        if (plot?.Sale?.Value.PhaseEndUtc is { } end && status is PlotStatus.EntryOpen or PlotStatus.Results or PlotStatus.NextCycle)
        {
            label = string.Concat(label, TextDraw.Separator, Loc.T(L.Find.PhaseEnds, FindText.LocalTime(end), FindText.Countdown(end - now)));
        }

        TextDraw.At(TextDraw.Truncate(label, ImGui.GetContentRegionAvail().X - radius * 3f), origin + new Vector2(radius * 3f, 0f), StatusMarks.Color(status));
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, lineHeight));
        if (status is { } known && ImGui.IsItemHovered())
        {
            Tooltip.Show(FindText.StatusHelp(known));
        }
    }

    private static void DrawFields(PlotSnapshot plot, in CyclePosition position, DateTime now)
    {
        Field(Loc.T(L.Find.FieldSize), FindText.Size(plot.Size?.Value), plot.Size?.Provenance, now);
        Field(Loc.T(L.Find.FieldDeposit), FindText.Gil(plot.Price?.Value), plot.Price?.Provenance, now);
        Field(Loc.T(L.Find.FieldBuyers), FindText.Buyers(plot.Buyers?.Value), plot.Buyers?.Provenance, now);
        Field(Loc.T(L.Find.FieldPhase), FindText.Status(PlotStatuses.Classify(plot, position)), plot.Sale?.Provenance, now);
        var entries = plot.EntryCountIn(position.Cycle);
        Field(Loc.T(L.Find.FieldEntries), FindText.Count(entries), entries is null ? null : plot.EntryCount?.Provenance, now);
        Field(Loc.T(L.Find.FieldMap), Loc.T(plot.Key.IsSubdivision ? L.Find.SubdivisionLong : L.Find.MainDivisionLong), null, now);
        if (plot.NeedsVerification)
        {
            Reason(Loc.T(L.Find.ConflictNote));
        }
    }

    private static void Field(string label, string value, Provenance? provenance, DateTime now)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        TextDraw.At(label, origin, Styling.TextDim);
        var valueWidth = MathF.Min(TextDraw.Measure(value).X, width * 0.6f);
        TextDraw.At(TextDraw.Truncate(value, width * 0.6f), new Vector2(origin.X + width - valueWidth, origin.Y), Styling.TextStrong);
        var height = TextDraw.LineHeight();
        if (provenance is { } source)
        {
            using (Fonts.PushCaption())
            {
                var age = FindText.Observed(source.SourceTimeUtc, now);
                var ageWidth = TextDraw.Measure(age).X;
                TextDraw.At(age, new Vector2(origin.X + width - ageWidth, origin.Y + height), Styling.TextMuted);
                height += TextDraw.LineHeight();
            }
        }

        ImGui.Dummy(new Vector2(width, height + FieldGap * ImGuiHelpers.GlobalScale));
    }

    private void DrawDetailActions(PlotKey key, PlotSnapshot? plot, PlotStatus? status)
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
        var actionable = status == PlotStatus.EntryOpen && plot?.Price?.Value is not null;
        if (actionable)
        {
            UnavailableAction(FontAwesomeIcon.Ticket, Loc.T(L.Find.EnterFor, FindText.Gil(plot!.Price!.Value.Value)), Styling.AccentGlow, Loc.T(L.Find.EnteringLater));
            return;
        }

        UnavailableAction(FontAwesomeIcon.Search, Loc.T(L.Find.InspectPlot), Styling.AccentBlue, Loc.T(L.Find.InspectLater));
    }

    // Entering and visiting arrive with later updates; their buttons are shown, locked, with the reason under them.
    private static void UnavailableAction(FontAwesomeIcon icon, string title, Vector4 accent, string reason)
    {
        HeroButton.Draw(icon, title, null, accent, false);
        Styling.VSpace(4f);
        ImGui.PushTextWrapPos();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextUnformatted(reason);
        }

        ImGui.PopTextWrapPos();
    }

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

    private void DrawPlotEligibility(FindView view, PlotKey key)
    {
        if (Discovery.Character is not { } character)
        {
            return;
        }

        TextDraw.SmallCaps(Loc.T(L.Find.ForCharacter, character.Name), ImGui.GetCursorScreenPos(), Styling.AccentGlowSoft);
        ImGui.Dummy(new Vector2(1f, TextDraw.CaptionLineHeight()));
        var filter = State.Filter;
        if (filter.Buyer == BuyerType.Unknown)
        {
            Reason(Loc.T(L.Find.NeedBuyer));
            return;
        }

        var plot = view.Plot(key);
        var candidate = new EntryCandidate(
            key,
            plot?.Buyers?.Value ?? PlotBuyers.Unknown,
            plot?.AvailabilityAt(view.Position) ?? PlotAvailability.Unknown,
            plot?.Price?.Value)
        {
            NeedsVerification = plot?.NeedsVerification ?? false,
        };
        // Without a price ceiling the cap would refuse every plot; this view only shows the other checks, the preview keeps the real cap.
        var budget = new CharacterBudget(filter.PriceCeiling > 0 ? filter.PriceCeiling : long.MaxValue, State.GilReserve, Core.Find.Model.RunPreviewBuilder.PreviewTravelAllowance);
        var result = EligibilityRules.EvaluatePlot(character, filter.Buyer, candidate, budget, default)
            .Combine(EligibilityRules.EvaluateCharacter(character, filter.Buyer, view.Position.Cycle, true));
        DrawEligibility(result);
        if (result.Blockers != EligibilityReason.None)
        {
            Reason(FindText.Reasons(result.Blockers));
        }
    }

    private static void DrawSources(PlotSnapshot plot, DateTime now)
    {
        if (!ImGui.CollapsingHeader(Loc.T(L.Find.SourceDetails)))
        {
            return;
        }

        using var caption = Fonts.PushCaption();
        ImGui.PushTextWrapPos();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
        {
            Source(Loc.T(L.Find.FieldSize), plot.Size?.Provenance, now);
            Source(Loc.T(L.Find.FieldDeposit), plot.Price?.Provenance, now);
            Source(Loc.T(L.Find.FieldBuyers), plot.Buyers?.Provenance, now);
            Source(Loc.T(L.Find.FieldPhase), plot.Sale?.Provenance, now);
            Source(Loc.T(L.Find.FieldEntries), plot.EntryCount?.Provenance, now);
        }

        ImGui.PopTextWrapPos();
    }

    private static void Source(string field, Provenance? provenance, DateTime now)
    {
        if (provenance is not { } source)
        {
            return;
        }

        var origin = FindText.Source(source.Source);
        if (source.Cycle is { } cycle)
        {
            origin = string.Concat(origin, TextDraw.Separator, Loc.T(L.Find.SourceCycle, cycle.Number));
        }

        ImGui.TextUnformatted(Loc.T(L.Find.SourceLine, field, origin, FindText.Age(source.SourceTimeUtc, now), FindText.Age(source.ReceivedUtc, now)));
    }
}
