using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using Dalamud.Interface;

namespace AutoHouseLottery.Windows.Components;

internal static class PauseButton
{
    public static bool Draw(PauseReason reason, float width = 0f) => reason switch
    {
        PauseReason.None      => HeroButton.Draw(FontAwesomeIcon.Pause, Loc.T(L.Dock.PauseCaps), null, Styling.AccentAmber, true, null, width),
        PauseReason.InContent => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Dock.ResumeCaps), null, Styling.AccentMint, false, Loc.T(L.Dock.InContent), width),
        _                     => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Dock.ResumeCaps), null, Styling.AccentMint, true, null, width),
    };
}
