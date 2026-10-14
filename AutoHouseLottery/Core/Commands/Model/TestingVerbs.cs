using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Commands.Model;

// The words after /ahl probe. Every one is listed in docs/testing-commands.md.
internal static class ProbeVerbs
{
    public const string Scan = "scan";
    public const string Lottery = "lottery";
    public const string Goto = "goto";
    public const string Approach = "approach";
    public const string Press = "press";
    public const string Stop = "stop";

    public const string PressEnter = "enter";
    public const string PressResults = "results";
    public const string PressCancel = "cancel";
    public const string PressNo = "no";

    public static readonly ImmutableArray<string> All = [Scan, Lottery, Goto, Approach, Press, Stop];

    public static readonly ImmutableArray<string> PressTargets = [PressEnter, PressResults, PressCancel, PressNo];
}

// The words after /ahl dev, the maintainer's tools.
internal static class DevVerbs
{
    public const string Run = "run";
    public const string Settle = "settle";

    public static readonly ImmutableArray<string> All = [Run, Settle];
}
