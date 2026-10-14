using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Selection.Model;
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
    private const int FallbackCount = 4;
    private const float DrawerPad = 14f;
    private const float ShortlistRowPad = 5f;
    private const float ShortlistShare = 0.42f;
    private const float RemoveSize = 20f;
    private const float ReserveFieldWidth = 120f;
    private const float NameFieldWidth = 220f;
    private const int NameLength = 48;
    private const int ConfirmWindowMs = 3_000;
    private const string DrawerId = "##ahl_find_drawer";
    private const string DetailsId = "##ahl_find_details";
    private const string ShortlistRowsId = "##ahl_find_shortlist_rows";
    private const string SavePopup = "##ahl_find_save";

    private readonly GilField.Buffer reserveBuffer = new();
    private string saveName = string.Empty;
    private long clearArmedUntil;
    private CachedText countsText;
    private CachedText removeUnavailableText;
    private CachedText previewCountsText;
    private CachedText characterText;

    private void DrawDrawer(FindView view, Vector2 size, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        Paint.Glass(ImGui.GetWindowDrawList(), origin, origin + size, Styling.PanelRounding * scale, Styling.AccentNebula, 0.04f);
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(DrawerPad, DrawerPad) * scale))
        using (var child = ImRaii.Child(details is null ? DrawerId : DetailsId, size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (!child)
            {
                return;
            }

            if (details is { } key)
            {
                DrawDetails(view, key, now);
                return;
            }

            DrawShortlist(view);
        }
    }

    private void DrawShortlist(FindView view)
    {
        var shortlist = State.Shortlist;
        Styling.SectionLabel(Loc.T(L.Find.ShortlistTitle));
        var entries = view.Preview?.MaxNewEntries ?? 0;
        var counts = countsText.Get(((long)shortlist.Count << 8) | (uint)entries,
            static key => Loc.T(L.Find.CountsJoin, Loc.Plural(L.Find.PlotCount, (int)(key >> 8)), Loc.Plural(L.Find.EntryCount, (int)(key & 0xFF))));
        TextDraw.Hint(counts);
        Styling.VSpace(4f);
        if (shortlist.Count == 0)
        {
            ImGui.PushTextWrapPos();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            {
                ImGui.TextUnformatted(Loc.T(L.Find.ShortlistEmpty));
            }

            ImGui.PopTextWrapPos();
            return;
        }

        DrawShortlistActions(view);
        Styling.VSpace(6f);
        DrawShortlistRows(view);
        Styling.VSpace(10f);
        DrawBestMatch(view);
        Styling.VSpace(10f);
        DrawPreview(view);
        Styling.VSpace(10f);
        UnavailableAction(FontAwesomeIcon.Ticket, Loc.T(L.Find.EnterBestMatch), Styling.AccentGlow, Loc.T(L.Find.EnteringLater));
    }

    private void DrawShortlistActions(FindView view)
    {
        var unavailable = view.UnavailableCount;
        var removeLabel = removeUnavailableText.Get(unavailable, static count => Loc.T(L.Find.RemoveUnavailable, count));
        if (PillButton.Draw("##ahl_find_remove_unavailable", removeLabel, Styling.AccentAmber, PillButton.Emphasis.Ghost, FontAwesomeIcon.Broom, unavailable > 0))
        {
            State.RemoveUnavailable();
        }

        ImGui.SameLine();
        var armed = Environment.TickCount64 < clearArmedUntil;
        if (PillButton.Draw("##ahl_find_clear", Loc.T(armed ? L.Find.ConfirmClear : L.Find.Clear), Styling.AccentRose, armed ? PillButton.Emphasis.Tinted : PillButton.Emphasis.Ghost, FontAwesomeIcon.Trash))
        {
            if (armed)
            {
                State.ClearShortlist();
                clearArmedUntil = 0;
            }
            else
            {
                clearArmedUntil = Environment.TickCount64 + ConfirmWindowMs;
            }
        }

        ImGui.SameLine();
        var anchor = ImGui.GetCursorScreenPos();
        if (PillButton.Draw("##ahl_find_save_open", Loc.T(L.Find.Save), Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Bookmark))
        {
            saveName = Loc.T(L.Find.DefaultName, Discovery.Names.World(Discovery.World), DateTime.Now.ToString("d", Loc.Culture));
            ImGui.SetNextWindowPos(anchor + new Vector2(0f, ImGui.GetFrameHeight() + 6f * ImGuiHelpers.GlobalScale));
            ImGui.OpenPopup(SavePopup);
        }

        DrawSavePopup();
    }

    private void DrawSavePopup()
    {
        using var popup = ContextMenu.Begin(SavePopup);
        if (!popup.Open)
        {
            return;
        }

        ImGui.SetNextItemWidth(NameFieldWidth * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##ahl_find_save_name", Loc.T(L.Find.NameHint), ref saveName, NameLength);
        var named = saveName.Trim().Length > 0;
        var now = DateTime.UtcNow;
        if (PillButton.Draw("##ahl_find_save_search", Loc.T(L.Find.SaveAsSearch), Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Search, named, tooltip: Loc.T(L.Find.SaveSearchHelp)))
        {
            Discovery.Saved.SaveSearch(saveName, State.Area with { WorldId = Discovery.World }, State.Filter, now);
            notice.Show(Loc.T(L.Find.SavedSearch), Styling.AccentMint);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (PillButton.Draw("##ahl_find_save_list", Loc.T(L.Find.SaveAsList), Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.List, named, tooltip: Loc.T(L.Find.SaveListHelp)))
        {
            var plots = State.Shortlist.Plots;
            var keys = new PlotKey[plots.Count];
            for (var index = 0; index < keys.Length; index++)
            {
                keys[index] = plots[index];
            }

            Discovery.Saved.SaveList(saveName, keys, now);
            notice.Show(Loc.T(L.Find.SavedList), Styling.AccentMint);
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawShortlistRows(FindView view)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rows = view.Shortlist;
        var rowHeight = TextDraw.LineHeight() + ShortlistRowPad * 2f * scale;
        var height = MathF.Min(rows.Length * rowHeight + 2f, MathF.Max(rowHeight * 3f, ImGui.GetContentRegionAvail().Y * ShortlistShare));
        using var child = ImRaii.Child(ShortlistRowsId, new Vector2(ImGui.GetContentRegionAvail().X, height), false, ImGuiWindowFlags.NoBackground);
        if (!child)
        {
            return;
        }

        var scrollY = ImGui.GetScrollY();
        var first = Math.Clamp((int)(scrollY / rowHeight) - 1, 0, rows.Length);
        var last = Math.Min(rows.Length, first + (int)(height / rowHeight) + 3);
        var contentOrigin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        for (var index = first; index < last; index++)
        {
            ImGui.SetCursorScreenPos(contentOrigin + new Vector2(0f, index * rowHeight));
            DrawShortlistRow(rows[index], index, width, rowHeight);
        }

        ImGui.SetCursorScreenPos(contentOrigin);
        ImGui.Dummy(new Vector2(width, rows.Length * rowHeight));
    }

    private void DrawShortlistRow(in ShortlistRow row, int index, float width, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var removeSize = RemoveSize * scale;
        ImGui.PushID(index);
        var hit = Hit.Area("##row", new Vector2(MathF.Max(1f, width - removeSize), height));
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - removeSize, origin.Y + (height - removeSize) * 0.5f));
        var removed = IconButton.Draw(FontAwesomeIcon.Times, "##remove", removeSize, Styling.TextDim, Loc.T(L.Find.Remove));
        ImGui.PopID();
        if (hit.Hovered)
        {
            Paint.Fill(drawList, origin, origin + new Vector2(width, height), Styling.WithAlpha(Styling.Surface2, 0.6f), Styling.FrameRounding * scale);
            Tooltip.Show(FindText.Status(row.Status));
        }

        var midY = origin.Y + height * 0.5f;
        var markRadius = StatusMarkRadius * scale;
        StatusMarks.Draw(drawList, new Vector2(origin.X + markRadius + 2f * scale, midY), markRadius, row.Status);
        var unavailable = ShortlistRules.IsUnavailable(row.Status);
        var address = row.Key.WorldId == Discovery.World ? PlotLabels.Address(Discovery.Names, row.Key) : PlotLabels.AddressOnWorld(Discovery.Names, row.Key);
        var textX = origin.X + markRadius * 2f + 8f * scale;
        TextDraw.At(TextDraw.Truncate(address, width - removeSize - (textX - origin.X) - 4f * scale), new Vector2(textX, midY - TextDraw.LineHeight() * 0.5f), unavailable ? Styling.TextMuted : Styling.TextSecondary);
        if (hit.Clicked)
        {
            details = row.Key;
        }

        if (removed)
        {
            State.Remove(row.Key);
        }
    }

    private void DrawBestMatch(FindView view)
    {
        var order = BestMatchOrder(view);
        if (order.IsDefaultOrEmpty)
        {
            return;
        }

        TextDraw.SmallCaps(Loc.T(L.Find.BestMatch), ImGui.GetCursorScreenPos(), Styling.AccentGlowSoft);
        ImGui.Dummy(new Vector2(1f, TextDraw.CaptionLineHeight()));
        var best = order[0];
        Selectable(best.Key, Styling.TextStrong);
        TextDraw.Hint(string.Concat(Loc.T(L.Find.Deposit, FindText.Gil(best.Deposit)), TextDraw.Separator, Loc.T(L.Find.EntriesValue, FindText.Count(best.ReportedEntries))));
        if (order.Length > 1)
        {
            Styling.VSpace(4f);
            TextDraw.SmallCaps(Loc.T(L.Find.Fallbacks), ImGui.GetCursorScreenPos(), Styling.TextMuted);
            ImGui.Dummy(new Vector2(1f, TextDraw.CaptionLineHeight()));
            for (var index = 1; index < order.Length && index <= FallbackCount; index++)
            {
                Selectable(order[index].Key, Styling.TextSecondary);
            }
        }

        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Find.RankingNote));
        }
    }

    // The top plot and its fallbacks as the planner would try them for this character; before the planner can run, the
    // plain ranking of the shortlist.
    private static System.Collections.Immutable.ImmutableArray<RankedPlot> BestMatchOrder(FindView view)
    {
        if (view.Preview is { PolicyError: RunPolicyError.None, Assignments.Length: > 0 } preview)
        {
            var targets = preview.Assignments[0].TargetOrder;
            var ordered = System.Collections.Immutable.ImmutableArray.CreateBuilder<RankedPlot>(targets.Length);
            for (var index = 0; index < targets.Length; index++)
            {
                for (var rankIndex = 0; rankIndex < preview.Ranking.Length; rankIndex++)
                {
                    if (preview.Ranking[rankIndex].Key == targets[index])
                    {
                        ordered.Add(preview.Ranking[rankIndex]);
                        break;
                    }
                }
            }

            return ordered.ToImmutable();
        }

        return view.Ranking;
    }

    private void Selectable(PlotKey key, Vector4 color)
    {
        var label = key.WorldId == Discovery.World ? PlotLabels.Address(Discovery.Names, key) : PlotLabels.AddressOnWorld(Discovery.Names, key);
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.PushID(key.GetHashCode());
            if (ImGui.Selectable(label))
            {
                details = key;
            }

            ImGui.PopID();
        }
    }

    private void DrawPreview(FindView view)
    {
        TextDraw.SmallCaps(Loc.T(L.Find.PreviewTitle), ImGui.GetCursorScreenPos(), Styling.AccentGlowSoft);
        ImGui.Dummy(new Vector2(1f, TextDraw.CaptionLineHeight()));
        var character = Discovery.Character;
        if (character is not { } profile)
        {
            Reason(Loc.T(L.Find.NeedCharacter));
            return;
        }

        TextDraw.Hint(previewCountsText.Get(State.Shortlist.Count, static count => Loc.T(L.Find.CountsJoin, Loc.Plural(L.Find.PlotCount, (int)count), Loc.Plural(L.Find.CharacterCount, 1))));
        if (!characterText.TryGet((long)profile.ContentId ^ (Discovery.Names.Ready ? 1L : 0L), out var characterLine))
        {
            characterLine = characterText.Set((long)profile.ContentId ^ (Discovery.Names.Ready ? 1L : 0L), string.Concat(profile.Name, TextDraw.Separator, Discovery.Names.World(profile.HomeWorldId)));
        }

        ImGui.TextUnformatted(characterLine);
        if (view.Preview is not { } preview)
        {
            return;
        }

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
            default:
                return;
        }

        for (var index = 0; index < preview.Exclusions.Length; index++)
        {
            Reason(Loc.T(L.Find.Excluded, profile.Name, FindText.Reasons(preview.Exclusions[index].Reasons)));
        }

        if (preview.Assignments.Length > 0)
        {
            var assignment = preview.Assignments[0];
            PreviewLine(Loc.T(L.Find.MaximumDeposit), FindText.Gil(assignment.MaximumDeposit));
            DrawEligibility(assignment.Eligibility);
        }

        DrawReserve();
        DrawBlockers(preview.Blockers);
    }

    private void DrawEligibility(in EligibilityResult eligibility)
    {
        var verdict = eligibility.Verdict;
        var (text, color) = verdict switch
        {
            EligibilityVerdict.Eligible => (Loc.T(L.Find.EligibilityEligible), Styling.AccentMint),
            EligibilityVerdict.Ineligible => (Loc.T(L.Find.EligibilityIneligible), Styling.AccentRose),
            _ => (Loc.T(L.Find.EligibilityUnknown), Styling.AccentAmberSoft),
        };
        PreviewLine(Loc.T(L.Find.Eligibility), text, color);
        if (ImGui.IsItemHovered() && eligibility.Unresolved != EligibilityReason.None)
        {
            Tooltip.Show(Loc.T(L.Find.Unresolved, FindText.Reasons(eligibility.Unresolved)));
        }
    }

    private void DrawReserve()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var label = Loc.T(L.Find.Reserve);
        var origin = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        TextDraw.At(label, new Vector2(origin.X, origin.Y + (height - TextDraw.LineHeight()) * 0.5f), Styling.TextDim);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + ImGui.GetContentRegionAvail().X - ReserveFieldWidth * scale, origin.Y));
        if (GilField.Draw("##ahl_find_reserve", reserveBuffer, State.GilReserve, long.MaxValue, "0", ReserveFieldWidth, out var reserve, Loc.T(L.Find.ReserveTooltip)))
        {
            State.SetGilReserve(reserve);
        }
    }

    private static void DrawBlockers(RunBlocker blockers)
    {
        if ((blockers & RunBlocker.CycleNotVerified) != 0)
        {
            Reason(Loc.T(L.Find.BlockerCycle));
        }

        if ((blockers & RunBlocker.EntryPeriodClosed) != 0)
        {
            Reason(Loc.T(L.Find.BlockerClosed));
        }

        if ((blockers & RunBlocker.CycleExpired) != 0)
        {
            Reason(Loc.T(L.Find.BlockerExpired));
        }

        if ((blockers & RunBlocker.NoAssignment) != 0)
        {
            Reason(Loc.T(L.Find.BlockerNoAssignment));
        }
    }

    private static void PreviewLine(string label, string value, Vector4? color = null)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        TextDraw.At(label, origin, Styling.TextDim);
        var valueWidth = TextDraw.Measure(value).X;
        TextDraw.At(TextDraw.Truncate(value, width * 0.6f), new Vector2(origin.X + width - MathF.Min(valueWidth, width * 0.6f), origin.Y), color ?? Styling.TextStrong);
        ImGui.Dummy(new Vector2(width, TextDraw.LineHeight()));
    }

    private static void Reason(string text)
    {
        ImGui.PushTextWrapPos();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmberSoft))
        {
            ImGui.TextUnformatted(text);
        }

        ImGui.PopTextWrapPos();
    }
}
