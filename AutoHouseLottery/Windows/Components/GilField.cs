using System.Globalization;

namespace AutoHouseLottery.Windows.Components;

internal static class GilField
{
    public const int Digits = 9;

    public sealed class Buffer
    {
        public long Observed = -1;
        public string Text = string.Empty;
    }

    public static bool Draw(string id, Buffer buffer, long current, long maximum, string hint, float width, out long value, string? tooltip = null)
    {
        value = current;
        if (buffer.Observed != current)
        {
            buffer.Observed = current;
            buffer.Text = Format(current);
        }

        if (!NumberField.Draw(id, ref buffer.Text, hint, width, Digits, tooltip))
        {
            return false;
        }

        long parsed = 0;
        if (buffer.Text.Length > 0 && (!long.TryParse(buffer.Text, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed > maximum))
        {
            buffer.Text = Format(current);
            return false;
        }

        if (parsed == current)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static string Format(long value) => value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
}
