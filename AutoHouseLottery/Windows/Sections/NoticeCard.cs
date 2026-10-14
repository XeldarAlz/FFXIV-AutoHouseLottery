using AutoHouseLottery.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

// One card with what happened and the actions that fit it: Begin measures and paints it, Button lays the actions out
// in a row along its bottom, End reserves its space.
internal static class NoticeCard
{
    private const float PadX = 20f;
    private const float PadY = 16f;
    private const float LineGap = 6f;
    private const float ButtonGap = 8f;
    private const float IconGap = 10f;
    private const float CardTint = 0.10f;
    private const float BorderAlpha = 0.45f;
    private const float FillAlpha = 0.6f;

    internal struct Card
    {
        public Vector2 Origin;
        public float Width;
        public float Height;
        public float ButtonX;
        public float ButtonY;
    }

    public static Card Begin(FontAwesomeIcon icon, string title, string body, string? facts, Vector4 accent, bool hasButtons)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var iconWidth = TextDraw.IconSize(icon).X + IconGap * scale;
        var wrapWidth = MathF.Max(1f, width - padX * 2f - iconWidth);
        var titleHeight = ImGui.GetTextLineHeight();
        var bodyHeight = body.Length == 0 ? 0f : TextDraw.MeasureWrapped(body, wrapWidth).Y + LineGap * scale;
        var factsHeight = facts is null ? 0f : TextDraw.MeasureWrapped(facts, wrapWidth).Y + LineGap * scale;
        var buttonsHeight = hasButtons ? Layout.ActionPillHeight * scale + LineGap * 2f * scale : 0f;
        var height = padY * 2f + titleHeight + bodyHeight + factsHeight + buttonsHeight;
        var end = origin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.Tint(Styling.WithAlpha(Styling.Surface1, FillAlpha), accent, CardTint), Styling.WithAlpha(accent, BorderAlpha), topLight: false);
        TextDraw.Icon(icon, new Vector2(origin.X + padX, origin.Y + padY), accent);

        var x = origin.X + padX + iconWidth;
        var y = origin.Y + padY;
        TextDraw.At(TextDraw.Truncate(title, wrapWidth), new Vector2(x, y), Styling.TextStrong);
        y += titleHeight;
        if (body.Length > 0)
        {
            y += LineGap * scale;
            TextDraw.Wrapped(body, new Vector2(x, y), wrapWidth, Styling.TextSecondary);
            y += bodyHeight - LineGap * scale;
        }

        if (facts is not null)
        {
            y += LineGap * scale;
            TextDraw.Wrapped(facts, new Vector2(x, y), wrapWidth, Styling.TextDim);
            y += factsHeight - LineGap * scale;
        }

        return new Card
        {
            Origin = origin,
            Width = width,
            Height = height,
            ButtonX = x,
            ButtonY = y + LineGap * 2f * scale,
        };
    }

    public static bool Button(ref Card card, string id, string label, Vector4 accent, PillButton.Emphasis emphasis, FontAwesomeIcon icon, bool enabled = true, string? tooltip = null)
    {
        ImGui.SetCursorScreenPos(new Vector2(card.ButtonX, card.ButtonY));
        var clicked = PillButton.Draw(id, label, accent, emphasis, icon, enabled, Layout.ActionPillHeight, tooltip);
        card.ButtonX += PillButton.Width(label, icon) + ButtonGap * ImGuiHelpers.GlobalScale;
        return clicked;
    }

    // Stop sits at the card's right edge, so a long translation of the other buttons can never push it off the card.
    public static bool TrailingButton(in Card card, string id, string label, Vector4 accent, PillButton.Emphasis emphasis, FontAwesomeIcon icon, bool enabled = true, string? tooltip = null)
    {
        var right = card.Origin.X + card.Width - PadX * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorScreenPos(new Vector2(right - PillButton.Width(label, icon), card.ButtonY));
        return PillButton.Draw(id, label, accent, emphasis, icon, enabled, Layout.ActionPillHeight, tooltip);
    }

    public static void End(in Card card)
    {
        ImGui.SetCursorScreenPos(card.Origin);
        ImGui.Dummy(new Vector2(card.Width, card.Height));
    }
}
