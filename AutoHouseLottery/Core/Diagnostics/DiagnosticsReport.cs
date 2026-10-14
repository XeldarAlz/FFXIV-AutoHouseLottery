using System.Globalization;
using System.Text;
using AutoHouseLottery.Core.Diagnostics.Model;
using AutoHouseLottery.Core.Localization;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Diagnostics;

// The support report behind Copy diagnostics: versions, the client language, every capability with its reason, and the
// latest Console lines, in English, with every known character's name and content ID replaced by an alias. Built only
// when a copy button is pressed.
internal static class DiagnosticsReport
{
    public const int LogLines = 200;

    private const string Separator = " | ";

    public static string Build(Configuration configuration, in CapabilityFacts facts, ReadOnlySpan<Capability> capabilities)
    {
        var builder = new StringBuilder(4_096 + LogLines * 128);
        var manifest = Svc.PluginInterface.Manifest;
        builder.Append(manifest.Name).Append(' ').Append(manifest.AssemblyVersion).Append(" diagnostics").AppendLine()
               .Append("Dalamud ").Append(CapabilityReader.DalamudVersion())
               .Append(Separator).Append("Game ").Append(facts.GameVersion)
               .Append(Separator).Append("Client language ").Append(CapabilityReader.ClientLanguage())
               .Append(Separator).Append("Plugin language ").Append(Loc.Current.Code).AppendLine()
               .Append("Home world region ").Append(CapabilityReader.HomeRegionName(facts))
               .Append(Separator).Append("Territory ").Append(CapabilityReader.Territory())
               .Append(Separator).Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss 'UTC'zzz", CultureInfo.InvariantCulture)).AppendLine()
               .AppendLine()
               .AppendLine("Capabilities");
        for (var index = 0; index < capabilities.Length; index++)
        {
            var capability = capabilities[index];
            builder.Append("- ").Append(CapabilityText.Name(capability.Kind, english: true))
                   .Append(": ").Append(CapabilityText.State(capability.State, english: true))
                   .Append(". ").Append(CapabilityText.Reason(capability.Reason, facts, english: true)).AppendLine();
        }

        var lines = LatestLines();
        builder.AppendLine()
               .Append("Last ").Append(lines.Length).Append(' ').Append(HouseLotteryConstants.LogPrefix)
               .AppendLine(" lines (character names and IDs replaced by aliases)")
               .Append(RunLogReport.Plain(lines));
        return Anonymize(configuration, builder.ToString());
    }

    // Console lines copied for a bug report get the same aliases as the report itself.
    public static string Anonymize(Configuration configuration, string text) => Aliases(configuration).Apply(text);

    private static ReadOnlySpan<RunLogLine> LatestLines()
    {
        var all = new RunLogLine[RunLog.Capacity];
        Span<int> levelTotals = stackalloc int[RunLog.LevelCount];
        var count = RunLog.Snapshot(RunLogFilter.Everything, all, levelTotals);
        var start = Math.Max(0, count - LogLines);
        return all.AsSpan(start, count - start);
    }

    // Every character this install has seen, and the logged-in one in case it has not been remembered yet.
    private static DiagnosticAliases Aliases(Configuration configuration)
    {
        var identities = new List<KnownIdentity>(configuration.Characters.Count + 1);
        foreach (var (contentId, known) in configuration.Characters)
        {
            identities.Add(new KnownIdentity(contentId, known.Name));
        }

        if (Svc.PlayerState.IsLoaded && !configuration.Characters.ContainsKey(Svc.PlayerState.ContentId))
        {
            identities.Add(new KnownIdentity(Svc.PlayerState.ContentId, Svc.PlayerState.CharacterName));
        }

        return new DiagnosticAliases(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(identities), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
}
