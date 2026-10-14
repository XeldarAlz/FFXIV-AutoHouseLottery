using AutoHouseLottery.Core;
using AutoHouseLottery.Core.Diagnostics;
using AutoHouseLottery.Core.Diagnostics.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

// The Plugins page's compatibility list: every capability with its state and a plain reason, and Copy diagnostics for a
// bug report. The facts are read once a second; the words are rebuilt only when a fact or the language changes.
internal sealed class CapabilitySection
{
    private const long RefreshMs = TimeUnits.MillisecondsPerSecond;
    private const long CopiedNoticeMs = 1_800;
    private const float RowPadX = 14f;
    private const float RowPadY = 9f;
    private const float RowGap = 4f;
    private const float NameGap = 3f;
    private const float DotRadius = 4f;
    private const float DotGap = 10f;
    private const float BadgeGap = 12f;
    private const float GroupGap = 8f;
    private const float HeaderGap = 10f;

    private static readonly (LocString Label, CapabilityKind First, CapabilityKind Last)[] groups =
    [
        (L.Diagnostics.GroupBasics, CapabilityKind.Listings, CapabilityKind.Journal),
        (L.Diagnostics.GroupHelpers, CapabilityKind.Lifestream, CapabilityKind.AutoAnswer),
        (L.Diagnostics.GroupReadings, CapabilityKind.LotteryRecord, CapabilityKind.WardPackets),
        (L.Diagnostics.GroupActions, CapabilityKind.EntryConfirm, CapabilityKind.AbsenceSettlement),
    ];

    private readonly Capability[] rows = new Capability[CapabilityRules.Count];
    private readonly string[] names = new string[CapabilityRules.Count];
    private readonly string[] states = new string[CapabilityRules.Count];
    private readonly string[] reasons = new string[CapabilityRules.Count];
    private CapabilityFacts facts;
    private CapabilityFacts wordsFacts;
    private LanguageInfo? wordsLanguage;
    private long refreshedAtMs = long.MinValue;
    private long copiedAtMs = long.MinValue;
    private CachedText copyTooltip;

    public void Draw(Plugin plugin)
    {
        Refresh(plugin);
        DrawHeader(plugin);
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var (label, first, last) = groups[groupIndex];
            GroupLabel.Draw(Loc.T(label), groupIndex == 0 ? 0f : GroupGap, RowGap);
            for (var index = (int)first; index <= (int)last; index++)
            {
                DrawRow(index);
                Styling.VSpace(RowGap);
            }
        }
    }

    private void Refresh(Plugin plugin)
    {
        var now = Environment.TickCount64;
        if (now - refreshedAtMs >= RefreshMs)
        {
            refreshedAtMs = now;
            facts = CapabilityReader.Read(plugin);
            CapabilityRules.Evaluate(facts, rows);
        }

        if (wordsFacts == facts && ReferenceEquals(wordsLanguage, Loc.Current) && names[0] is not null)
        {
            return;
        }

        wordsFacts = facts;
        wordsLanguage = Loc.Current;
        for (var index = 0; index < rows.Length; index++)
        {
            names[index] = CapabilityText.Name(rows[index].Kind, english: false);
            states[index] = CapabilityText.State(rows[index].State, english: false);
            reasons[index] = CapabilityText.Reason(rows[index].Reason, facts, english: false);
        }
    }

    private void DrawHeader(Plugin plugin)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var copied = Environment.TickCount64 - copiedAtMs < CopiedNoticeMs;
        var label = Loc.T(copied ? L.Diagnostics.Copied : L.Diagnostics.Copy);
        var buttonWidth = PillButton.Width(label, FontAwesomeIcon.Copy);
        var title = Loc.T(L.Diagnostics.Title);
        float titleHeight;
        using (Fonts.PushHeadline())
        {
            titleHeight = TextDraw.Measure(title).Y;
            TextDraw.At(TextDraw.Truncate(title, MathF.Max(1f, width - buttonWidth - HeaderGap * scale)), origin, Styling.TextStrong);
        }

        var buttonHeight = Layout.ActionPillHeight * scale;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, origin.Y + (titleHeight - buttonHeight) * 0.5f));
        var tooltip = copyTooltip.Get(DiagnosticsReport.LogLines, static lines => Loc.T(L.Diagnostics.CopyTooltip, lines));
        if (PillButton.Draw("##ahl_copy_diagnostics", label, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Copy, height: Layout.ActionPillHeight, tooltip: tooltip))
        {
            ImGui.SetClipboardText(DiagnosticsReport.Build(plugin.Configuration, facts, rows));
            copiedAtMs = Environment.TickCount64;
            RunLog.Info("Diagnostics copied to the clipboard.");
        }

        var subtitle = Loc.T(L.Diagnostics.Subtitle);
        var subtitleY = origin.Y + MathF.Max(titleHeight, buttonHeight) + NameGap * scale;
        float subtitleHeight;
        using (Fonts.PushCaption())
        {
            subtitleHeight = TextDraw.MeasureWrapped(subtitle, width).Y;
            TextDraw.Wrapped(subtitle, new Vector2(origin.X, subtitleY), width, Styling.TextDim);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, subtitleY - origin.Y + subtitleHeight + HeaderGap * scale));
    }

    private void DrawRow(int index)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = RowPadX * scale;
        var padY = RowPadY * scale;
        var accent = Accent(rows[index].State);
        var textX = origin.X + padX + (DotRadius * 2f + DotGap) * scale;
        var textWidth = MathF.Max(1f, origin.X + width - padX - textX);

        float badgeWidth;
        using (Fonts.PushCaption())
        {
            badgeWidth = TextDraw.Measure(states[index]).X + 14f * scale;
        }

        float nameHeight;
        using (Fonts.PushHeadline())
        {
            nameHeight = TextDraw.Measure(names[index]).Y;
        }

        float reasonHeight;
        using (Fonts.PushCaption())
        {
            reasonHeight = TextDraw.MeasureWrapped(reasons[index], textWidth).Y;
        }

        var height = padY * 2f + nameHeight + NameGap * scale + reasonHeight;
        var end = origin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, 0.05f);

        var nameY = origin.Y + padY;
        Paint.Dot(drawList, new Vector2(origin.X + padX + DotRadius * scale, nameY + nameHeight * 0.5f), DotRadius * scale, accent);
        var badgeRight = end.X - padX;
        Badge.Draw(drawList, states[index], accent, badgeRight, nameY + nameHeight * 0.5f);
        using (Fonts.PushHeadline())
        {
            TextDraw.At(TextDraw.Truncate(names[index], MathF.Max(1f, badgeRight - badgeWidth - BadgeGap * scale - textX)), new Vector2(textX, nameY), Styling.TextStrong);
        }

        using (Fonts.PushCaption())
        {
            TextDraw.Wrapped(reasons[index], new Vector2(textX, nameY + nameHeight + NameGap * scale), textWidth, Styling.TextDim);
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static Vector4 Accent(CapabilityState state) => state switch
    {
        CapabilityState.Ready => Styling.AccentMint,
        CapabilityState.Fallback or CapabilityState.DryRunOnly => Styling.AccentAmber,
        CapabilityState.PermittedOnce => Styling.AccentRose,
        CapabilityState.NotChecked => Styling.AccentBlue,
        _ => Styling.AccentRose,
    };
}
