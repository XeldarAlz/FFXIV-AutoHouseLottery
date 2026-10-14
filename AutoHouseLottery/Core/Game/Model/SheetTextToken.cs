namespace AutoHouseLottery.Core.Game.Model;

internal enum SheetTextTokenKind : byte
{
    Literal,
    Gap,
}

// How a sheet macro shows up in the text the readers extract (Lumina's ExtractText): a line break as a new line, a
// non-breaking space as that space, a hyphen as '-', a soft hyphen or styling as nothing, anything else as live content.
internal enum SheetMacroKind : byte
{
    LineBreak,
    NonBreakingSpace,
    Hyphen,
    Invisible,
    Dynamic,
}

// A Literal is text the row always shows; a Gap is a macro whose output depends on the moment (a name, an amount, a date).
internal readonly record struct SheetTextToken(SheetTextTokenKind Kind, string Text)
{
    private const string Space = " ";
    private const string HyphenText = "-";

    public static readonly SheetTextToken Gap = new(SheetTextTokenKind.Gap, string.Empty);

    public static SheetTextToken Literal(string text) => new(SheetTextTokenKind.Literal, text);

    // Agrees with the extraction: a line break or a non-breaking space extracts as whitespace, which the matcher
    // collapses to one space on both sides.
    public static SheetTextToken ForMacro(SheetMacroKind kind) => kind switch
    {
        SheetMacroKind.LineBreak or SheetMacroKind.NonBreakingSpace => Literal(Space),
        SheetMacroKind.Hyphen => Literal(HyphenText),
        SheetMacroKind.Invisible => Literal(string.Empty),
        _ => Gap,
    };
}
