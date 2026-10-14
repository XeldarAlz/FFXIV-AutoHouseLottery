using System.Collections.Immutable;
using System.Text;

namespace AutoHouseLottery.Core.Game.Model;

// The static segments of a sheet row in order, with whether the row starts or ends in a macro. Displayed text matches
// when every segment appears in that order, the first one at the very start unless a macro precedes it and the last
// one at the very end unless a macro follows it.
internal sealed class SheetTextPattern
{
    public static readonly SheetTextPattern Empty = new([], openStart: true, openEnd: true);

    private readonly ImmutableArray<string> segments;
    private readonly bool openStart;
    private readonly bool openEnd;

    private SheetTextPattern(ImmutableArray<string> segments, bool openStart, bool openEnd)
    {
        this.segments = segments;
        this.openStart = openStart;
        this.openEnd = openEnd;
    }

    public ImmutableArray<string> Segments => segments;

    // A row made only of macros says nothing about which dialog is open, so it never matches.
    public bool IsUsable => segments.Length > 0;

    public static SheetTextPattern FromTokens(ReadOnlySpan<SheetTextToken> tokens)
    {
        var collected = ImmutableArray.CreateBuilder<string>();
        var pending = new StringBuilder();
        var openStart = false;
        var openEnd = false;
        for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            if (token.Kind == SheetTextTokenKind.Literal)
            {
                pending.Append(token.Text);
                continue;
            }

            if (!Flush(pending, collected) && collected.Count == 0)
            {
                openStart = true;
            }

            openEnd = true;
        }

        if (Flush(pending, collected))
        {
            openEnd = false;
        }

        return new SheetTextPattern(collected.ToImmutable(), openStart, openEnd);
    }

    public bool Matches(string shown) => IsUsable && MatchNormalized(DisplayText.Normalize(shown), null);

    // The text each macro rendered, in order: before the first segment when the row opens with a macro, between
    // segments, and after the last when the row ends with one.
    public bool TryCapture(string shown, out ImmutableArray<string> gaps)
    {
        var captured = new List<string>();
        if (!IsUsable || !MatchNormalized(DisplayText.Normalize(shown), captured))
        {
            gaps = [];
            return false;
        }

        gaps = [.. captured];
        return true;
    }

    private bool MatchNormalized(string text, List<string>? gaps)
    {
        var lastIndex = segments.Length - 1;
        var cursor = 0;
        for (var segmentIndex = 0; segmentIndex <= lastIndex; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (segmentIndex == lastIndex && !openEnd)
            {
                var tailStart = text.Length - segment.Length;
                var anchoredBoth = segmentIndex == 0 && !openStart;
                if (tailStart < cursor || !text.EndsWith(segment, StringComparison.OrdinalIgnoreCase) || (anchoredBoth && tailStart != 0))
                {
                    return false;
                }

                AddGap(gaps, text, cursor, tailStart, segmentIndex);
                return true;
            }

            if (segmentIndex == 0 && !openStart)
            {
                if (!text.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                cursor = segment.Length;
                continue;
            }

            var found = text.IndexOf(segment, cursor, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            AddGap(gaps, text, cursor, found, segmentIndex);
            cursor = found + segment.Length;
        }

        gaps?.Add(text[cursor..].Trim());
        return true;
    }

    private void AddGap(List<string>? gaps, string text, int start, int end, int segmentIndex)
    {
        if (gaps is null || (segmentIndex == 0 && !openStart))
        {
            return;
        }

        gaps.Add(text[start..end].Trim());
    }

    private static bool Flush(StringBuilder pending, ImmutableArray<string>.Builder collected)
    {
        if (pending.Length == 0)
        {
            return false;
        }

        var normalized = DisplayText.Normalize(pending.ToString());
        pending.Clear();
        if (normalized.Length == 0)
        {
            return false;
        }

        collected.Add(normalized);
        return true;
    }
}
