using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Windows.Components;
using Dalamud.Interface;

namespace AutoHouseLottery.Windows.Pages;

internal sealed class FindHomePage
{
    public void Draw()
    {
        PageHeader.Draw(Loc.T(L.FindHome.Title), Loc.T(L.FindHome.Subtitle));
        EmptyState.Draw(FontAwesomeIcon.MapMarkedAlt, Loc.T(L.FindHome.Empty));
    }
}
