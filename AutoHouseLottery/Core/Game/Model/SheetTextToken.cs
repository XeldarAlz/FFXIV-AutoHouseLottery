namespace AutoHouseLottery.Core.Game.Model;

internal enum SheetTextTokenKind : byte
{
    Literal,
    Gap,
}

// A Literal is text the row always shows; a Gap is a macro whose output depends on the moment (a name, an amount, a date).
internal readonly record struct SheetTextToken(SheetTextTokenKind Kind, string Text)
{
    public static readonly SheetTextToken Gap = new(SheetTextTokenKind.Gap, string.Empty);

    public static SheetTextToken Literal(string text) => new(SheetTextTokenKind.Literal, text);
}
