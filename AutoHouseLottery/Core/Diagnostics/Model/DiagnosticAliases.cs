using System.Collections.Immutable;
using System.Globalization;

namespace AutoHouseLottery.Core.Diagnostics.Model;

internal readonly record struct KnownIdentity(ulong ContentId, string Name);

// A support report names nobody: every known character's name and content ID becomes the same alias wherever it
// appears (Character A, Character B, ...), and the Windows user folder in a file path becomes a placeholder. Longer
// texts are replaced first, so a name that contains another name keeps its own alias.
internal sealed class DiagnosticAliases
{
    public const string UserFolderPlaceholder = "%USERPROFILE%";

    private const string AliasPrefix = "Character ";
    private const string IdSuffix = " ID";
    private const char ProbeEllipsis = '…';
    private const int ProbeIdDigits = 4;
    private const int Letters = 26;

    private readonly ImmutableArray<Replacement> replacements;

    public DiagnosticAliases(ReadOnlySpan<KnownIdentity> identities, string? userFolder)
    {
        var found = new List<Replacement>(identities.Length * 4 + 1);
        for (var index = 0; index < identities.Length; index++)
        {
            var identity = identities[index];
            var alias = AliasFor(index);
            if (!string.IsNullOrWhiteSpace(identity.Name))
            {
                found.Add(new Replacement(identity.Name.Trim(), alias));
            }

            if (identity.ContentId == 0)
            {
                continue;
            }

            var idAlias = string.Concat(alias, IdSuffix);
            var hex = identity.ContentId.ToString("X", CultureInfo.InvariantCulture);
            found.Add(new Replacement(identity.ContentId.ToString(CultureInfo.InvariantCulture), idAlias));
            found.Add(new Replacement(hex, idAlias));
            if (hex.Length > ProbeIdDigits)
            {
                found.Add(new Replacement(string.Concat(ProbeEllipsis.ToString(), hex[^ProbeIdDigits..]), idAlias));
            }
        }

        if (!string.IsNullOrWhiteSpace(userFolder))
        {
            found.Add(new Replacement(userFolder.TrimEnd('\\', '/'), UserFolderPlaceholder));
        }

        found.Sort(static (left, right) => right.Text.Length.CompareTo(left.Text.Length));
        replacements = [.. found];
    }

    // A, B, ..., Z, then AA, AB, ...
    public static string AliasFor(int index)
    {
        Span<char> letters = stackalloc char[8];
        var position = letters.Length;
        var remaining = index + 1;
        while (remaining > 0)
        {
            remaining--;
            letters[--position] = (char)('A' + remaining % Letters);
            remaining /= Letters;
        }

        return string.Concat(AliasPrefix, letters[position..].ToString());
    }

    public string Apply(string text)
    {
        for (var index = 0; index < replacements.Length; index++)
        {
            var replacement = replacements[index];
            text = text.Replace(replacement.Text, replacement.Alias, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private readonly record struct Replacement(string Text, string Alias);
}
