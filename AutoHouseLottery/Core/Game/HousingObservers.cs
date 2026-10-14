using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Placard;
using AutoHouseLottery.Core.Game.Ward;

namespace AutoHouseLottery.Core.Game;

// The housing listeners and hooks the plugin keeps for its lifetime. Plugin creates and disposes each one in its
// staged load, so this only hands them to the readers.
internal sealed record HousingObservers(
    AddonUpdateCounter WardWindow,
    WardPortalObserver WardPackets,
    AddonUpdateCounter PlacardWindow,
    PlacardSaleHook PlacardSales,
    SignboardAgentLayout SignboardLayout,
    HousingLogWatcher HousingLog);
