using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoHouseLottery.Core.Diagnostics.Model;

internal readonly record struct KnownIdentity(ulong ContentId, string Name);

// A support report names nobody: every known character's name and content ID becomes the same alias wherever it
// appears (Character A, Character B, ...), the plugin configuration folder and the user's own folder become
// placeholders, and so does any home folder in a path, whether Windows (C:\Users\name), Wine (Z:\home\name), Linux
// (/home/name) or macOS (/Users/name). Longer texts are replaced first, so a name that contains another name keeps its
// own alias.
internal sealed partial class DiagnosticAliases
{
    public const string UserFolderPlaceholder = "%USERPROFILE%";
    public const string ConfigFolderPlaceholder = "%PLUGINCONFIGS%";

    private const string AliasPrefix = "Character ";
    private const string IdSuffix = " ID";
    private const char ProbeEllipsis = '…';
    private const int ProbeIdDigits = 4;
    private const int Letters = 26;
    private const int ReplacementsPerIdentity = 4;
    private const int FolderReplacements = 2;
    private const int MaximumAliasLetters = 8;

    private readonly ImmutableArray<Replacement> replacements;

    public DiagnosticAliases(ReadOnlySpan<KnownIdentity> identities, string? userFolder, string? configFolder)
    {
        var found = new List<Replacement>(identities.Length * ReplacementsPerIdentity + FolderReplacements);
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

        AddFolder(found, configFolder, ConfigFolderPlaceholder);
        AddFolder(found, userFolder, UserFolderPlaceholder);
        found.Sort(static (left, right) => right.Text.Length.CompareTo(left.Text.Length));
        replacements = [.. found];
    }

    // A, B, ..., Z, then AA, AB, ...
    public static string AliasFor(int index)
    {
        Span<char> letters = stackalloc char[MaximumAliasLetters];
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

        return HomeFolder().Replace(text, UserFolderPlaceholder);
    }

    private static void AddFolder(List<Replacement> found, string? folder, string placeholder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            found.Add(new Replacement(folder.TrimEnd('\\', '/'), placeholder));
        }
    }

    private readonly record struct Replacement(string Text, string Alias);

    // A drive letter or nothing, then home or Users, then the user's own folder name.
    [GeneratedRegex(@"(?:[A-Za-z]:)?[\\/](?:home|users)[\\/][^\\/\s'""]+", RegexOptions.IgnoreCase)]
    private static partial Regex HomeFolder();
}
