using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Windows.Components;
using Dalamud.Interface;

namespace AutoHouseLottery.Windows.Pages;

internal sealed class SavedPage
{
    public void Draw()
    {
        PageHeader.Draw(Loc.T(L.Saved.Title), Loc.T(L.Saved.Subtitle));
        EmptyState.Draw(FontAwesomeIcon.Bookmark, Loc.T(L.Saved.Empty));
    }
}
