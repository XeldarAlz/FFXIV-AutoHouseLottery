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

    public bool Matches(string shown)
    {
        if (!IsUsable)
        {
            return false;
        }

        var text = DisplayText.Normalize(shown);
        var lastIndex = segments.Length - 1;
        var cursor = 0;
        for (var segmentIndex = 0; segmentIndex <= lastIndex; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (segmentIndex == lastIndex && !openEnd)
            {
                var tailStart = text.Length - segment.Length;
                var anchoredBoth = segmentIndex == 0 && !openStart;
                return tailStart >= cursor
                    && text.EndsWith(segment, StringComparison.OrdinalIgnoreCase)
                    && (!anchoredBoth || tailStart == 0);
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

            cursor = found + segment.Length;
        }

        return true;
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
