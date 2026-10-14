using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

internal static class ReadyState
{
    public enum Kind { SetupNeeded, Ready, Blocked, Running, Paused }

    public readonly record struct Info(Kind Kind, Vector4 Accent, Vector4 AccentSoft);

    private static int cachedFrame = -1;
    private static Info cached;

    public static Info Resolve(RunController controller)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame)
        {
            return cached;
        }

        cached = Compute(controller);
        cachedFrame = frame;
        return cached;
    }

    private static Info Compute(RunController controller)
    {
        if (controller.Running)
        {
            return controller.Paused || controller.Status.AwaitsPlayer
                ? new Info(Kind.Paused, Styling.AccentAmber, Styling.AccentAmberSoft)
                : new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft);
        }

        if (controller.Reconciling || controller.Settling)
        {
            return new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft);
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return new Info(Kind.SetupNeeded, Styling.AccentRose, Styling.AccentRoseSoft);
        }

        return Svc.ClientState.IsLoggedIn
            ? new Info(Kind.Ready, Styling.AccentMint, Styling.AccentMintSoft)
            : new Info(Kind.Blocked, Styling.AccentNebula, Styling.AccentGlowSoft);
    }

    public static string ShortLabel(Kind kind) => kind switch
    {
        Kind.Running     => Loc.T(L.Shell.StatusRunning),
        Kind.Paused      => Loc.T(L.Shell.StatusPaused),
        Kind.Ready       => Loc.T(L.Shell.StatusReady),
        Kind.Blocked     => Loc.T(L.Shell.StatusBlocked),
        Kind.SetupNeeded => Loc.T(L.Shell.StatusSetupNeeded),
        _                => Loc.T(L.Shell.StatusIdle),
    };
}
