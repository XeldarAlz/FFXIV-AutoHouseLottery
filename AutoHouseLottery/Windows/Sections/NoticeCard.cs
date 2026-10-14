using AutoHouseLottery.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

// One card with what happened and the actions that fit it: Begin measures and paints it, Button lays the actions out
// along its bottom, wrapping onto further rows when given their widths, TrailingButton keeps the right end of the first
// row (Stop) out of their way, and End reserves its space.
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
        public float RowStart;
        public float RowRight;
        public float FirstRowY;
    }

    // LeadingWidths are the widths of the buttons Button will lay out, in order; TrailingWidth is the trailing button's.
    public static Card Begin(FontAwesomeIcon icon, string title, string body, string? facts, Vector4 accent, bool hasButtons, ReadOnlySpan<float> leadingWidths = default, float trailingWidth = 0f)
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
        var gap = ButtonGap * scale;
        var rowRight = origin.X + width - padX - (trailingWidth > 0f ? trailingWidth + gap : 0f);
        var rowStart = origin.X + padX + iconWidth;
        var rows = hasButtons ? ButtonRows(leadingWidths, rowStart, rowRight, gap) : 0;
        var pillHeight = Layout.ActionPillHeight * scale;
        var buttonsHeight = hasButtons ? pillHeight * rows + gap * (rows - 1) + LineGap * 2f * scale : 0f;
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
            RowStart = rowStart,
            RowRight = rowRight,
            FirstRowY = y + LineGap * 2f * scale,
        };
    }

    public static bool Button(ref Card card, string id, string label, Vector4 accent, PillButton.Emphasis emphasis, FontAwesomeIcon icon, bool enabled = true, string? tooltip = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = PillButton.Width(label, icon);
        if (card.ButtonX > card.RowStart && card.ButtonX + width > card.RowRight)
        {
            card.ButtonX = card.RowStart;
            card.ButtonY += (Layout.ActionPillHeight + ButtonGap) * scale;
        }

        ImGui.SetCursorScreenPos(new Vector2(card.ButtonX, card.ButtonY));
        var clicked = PillButton.Draw(id, label, accent, emphasis, icon, enabled, Layout.ActionPillHeight, tooltip);
        card.ButtonX += width + ButtonGap * scale;
        return clicked;
    }

    // Stop sits at the right end of the first row, which the other buttons never reach, so a long translation can never
    // push it off the card or cover it.
    public static bool TrailingButton(in Card card, string id, string label, Vector4 accent, PillButton.Emphasis emphasis, FontAwesomeIcon icon, bool enabled = true, string? tooltip = null)
    {
        var right = card.Origin.X + card.Width - PadX * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorScreenPos(new Vector2(right - PillButton.Width(label, icon), card.FirstRowY));
        return PillButton.Draw(id, label, accent, emphasis, icon, enabled, Layout.ActionPillHeight, tooltip);
    }

    public static float Width(string label, FontAwesomeIcon icon) => PillButton.Width(label, icon);

    // The rows Button will need for these widths, wrapping exactly as it does; at least one.
    private static int ButtonRows(ReadOnlySpan<float> widths, float rowStart, float rowRight, float gap)
    {
        var rows = 1;
        var x = rowStart;
        for (var index = 0; index < widths.Length; index++)
        {
            if (x > rowStart && x + widths[index] > rowRight)
            {
                rows++;
                x = rowStart;
            }

            x += widths[index] + gap;
        }

        return rows;
    }

    public static void End(in Card card)
    {
        ImGui.SetCursorScreenPos(card.Origin);
        ImGui.Dummy(new Vector2(card.Width, card.Height));
    }
}
