using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Windows.Components;
using Dalamud.Interface;

namespace AutoHouseLottery.Windows.Pages;

internal sealed class MyLotteryPage
{
    public void Draw()
    {
        PageHeader.Draw(Loc.T(L.MyLottery.Title), Loc.T(L.MyLottery.Subtitle));
        EmptyState.Draw(FontAwesomeIcon.Ticket, Loc.T(L.MyLottery.Empty));
    }
}
