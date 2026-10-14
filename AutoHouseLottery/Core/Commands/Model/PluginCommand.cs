namespace AutoHouseLottery.Core.Commands.Model;

internal enum PluginCommand : byte
{
    ToggleWindow,
    Settings,
    MyLottery,
    Saved,
    Plugins,
    Console,
    Changelog,
    About,
    Stop,
    Pause,
    Resume,
    Target,
    Probe,
    Dev,
}

// Player commands are the ones the command help and the README's Commands table list. Testing commands are the probe
// and the maintainer's tools, listed only in docs/testing-commands.md.
internal enum CommandAudience : byte
{
    Player,
    Testing,
}

internal readonly record struct CommandVerb(PluginCommand Command, string Verb, CommandAudience Audience, string? Alias = null)
{
    public bool TakesArguments => Audience == CommandAudience.Testing;

    public bool Answers(ReadOnlySpan<char> word)
        => word.Equals(Verb, StringComparison.OrdinalIgnoreCase) || (Alias is not null && word.Equals(Alias, StringComparison.OrdinalIgnoreCase));
}
