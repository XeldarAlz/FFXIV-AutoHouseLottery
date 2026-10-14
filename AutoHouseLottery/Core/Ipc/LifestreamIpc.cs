using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Travel.Model;
using Dalamud.Plugin.Ipc;
using ECommons;
using ECommons.DalamudServices;
using System.Numerics;
using HousingAddress = (string Name, int World, int City, int Ward, int PropertyType, int Plot, int Apartment, bool ApartmentSubdivision, bool AliasEnabled, string Alias);

namespace AutoHouseLottery.Core.Ipc;

// Typed wrappers over the Lifestream gates in TECHNICAL_DESIGN section 14, with signatures checked against Lifestream's
// IPC/IPCProvider.cs (NightmareXIV/Lifestream ef759e9). A void travel call is only a request; arrival is proved elsewhere.
internal sealed class LifestreamIpc
{
    // Lifestream's AddressBookEntryTuple: City is the district's ResidentialAetheryteKind (its city aetheryte ID),
    // PropertyType 0 is a house, and ward and plot count from 1.
    private const int HouseProperty = 0;
    private const int UnusedApartment = 1;

    private const string IsBusyFailed = "Lifestream IsBusy failed";
    private const string GoToFailed = "Lifestream GoToHousingAddress failed";
    private const string AbortFailed = "Lifestream Abort failed";
    private const string IsHereFailed = "Lifestream IsHere failed";
    private const string QuickTravelFailed = "Lifestream IsQuickTravelAvailable failed";
    private const string PlotEntranceFailed = "Lifestream GetPlotEntrance failed";
    private const string CurrentPlotFailed = "Lifestream GetCurrentPlotInfo failed";
    private const string ChangeCharacterFailed = "Lifestream ChangeCharacter failed";

    private static LifestreamIpc? instance;

    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<object> abort;
    private readonly ICallGateSubscriber<HousingAddress, object> goToHousingAddress;
    private readonly ICallGateSubscriber<HousingAddress, bool> isHere;
    private readonly ICallGateSubscriber<HousingAddress, bool> isQuickTravelAvailable;
    private readonly ICallGateSubscriber<uint, int, Vector3?> getPlotEntrance;
    // Lifestream returns (ResidentialAetheryteKind Kind, int Ward, int Plot)?; the enum crosses the gate as its int value.
    private readonly ICallGateSubscriber<(int Kind, int Ward, int Plot)?> getCurrentPlotInfo;
    // Lifestream's own ECommons ErrorCode crosses the gate into this plugin's copy of the same enum by its value.
    private readonly ICallGateSubscriber<string, string, ErrorCode> changeCharacter;

    private readonly Func<bool?> isBusyCall;

    private LifestreamIpc()
    {
        var pluginInterface = Svc.PluginInterface;
        isBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        abort = pluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");
        goToHousingAddress = pluginInterface.GetIpcSubscriber<HousingAddress, object>("Lifestream.GoToHousingAddress");
        isHere = pluginInterface.GetIpcSubscriber<HousingAddress, bool>("Lifestream.IsHere");
        isQuickTravelAvailable = pluginInterface.GetIpcSubscriber<HousingAddress, bool>("Lifestream.IsQuickTravelAvailable");
        getPlotEntrance = pluginInterface.GetIpcSubscriber<uint, int, Vector3?>("Lifestream.GetPlotEntrance");
        getCurrentPlotInfo = pluginInterface.GetIpcSubscriber<(int Kind, int Ward, int Plot)?>("Lifestream.GetCurrentPlotInfo");
        changeCharacter = pluginInterface.GetIpcSubscriber<string, string, ErrorCode>("Lifestream.ChangeCharacter");

        isBusyCall = () => isBusy.InvokeFunc();
    }

    public static LifestreamIpc Instance => instance ??= new LifestreamIpc();

    public LifestreamCapabilities Capabilities()
        => new(
            ExternalPlugins.IsInstalled(ExternalPlugin.Lifestream),
            goToHousingAddress.HasAction,
            isBusy.HasFunction,
            abort.HasAction,
            isHere.HasFunction,
            isQuickTravelAvailable.HasFunction,
            getPlotEntrance.HasFunction,
            getCurrentPlotInfo.HasFunction)
        {
            ChangeCharacter = changeCharacter.HasFunction,
        };

    // Null when the gate is missing or failed, which callers treat as "cannot tell", never as idle.
    public bool? IsBusy() => IpcGate.Invoke(isBusy.HasFunction, isBusyCall, null, IsBusyFailed);

    public bool GoToHousingAddress(in HousingDestination destination, uint aetheryteId)
    {
        var address = AddressOf(destination, aetheryteId);
        var sent = false;
        IpcGate.Run(goToHousingAddress.HasAction, () =>
        {
            goToHousingAddress.InvokeAction(address);
            sent = true;
        }, GoToFailed);
        return sent;
    }

    public bool? IsHere(in HousingDestination destination, uint aetheryteId)
    {
        var address = AddressOf(destination, aetheryteId);
        return IpcGate.Invoke<bool?>(isHere.HasFunction, () => isHere.InvokeFunc(address), null, IsHereFailed);
    }

    public bool? IsQuickTravelAvailable(in HousingDestination destination, uint aetheryteId)
    {
        var address = AddressOf(destination, aetheryteId);
        return IpcGate.Invoke<bool?>(isQuickTravelAvailable.HasFunction, () => isQuickTravelAvailable.InvokeFunc(address), null, QuickTravelFailed);
    }

    // Lifestream's own entrance point for a plot (zero-based), from its housing data; null when it has none.
    public Vector3? PlotEntrance(uint territoryTypeId, int plotIndex)
        => IpcGate.Invoke(getPlotEntrance.HasFunction, () => getPlotEntrance.InvokeFunc(territoryTypeId, plotIndex), null, PlotEntranceFailed);

    // Zero-based ward and plot, as Lifestream reads them from HousingManager.
    public (int Kind, int Ward, int Plot)? CurrentPlotInfo()
        => IpcGate.Invoke(getCurrentPlotInfo.HasFunction, () => getCurrentPlotInfo.InvokeFunc(), null, CurrentPlotFailed);

    // Logs the current character out and the named one in, by its name and home world's name; Lifestream finds the service
    // account and data center itself and stores nothing of ours. Success means the request was queued, never that the
    // character is in: the switch is proved by the game's own content ID and home world. Null when the gate is missing or
    // failed.
    public ErrorCode? ChangeCharacter(string name, string homeWorldName)
        => IpcGate.Invoke<ErrorCode?>(changeCharacter.HasFunction, () => changeCharacter.InvokeFunc(name, homeWorldName), null, ChangeCharacterFailed);

    // Lifestream's Abort stops all of its work, so only HousingTravel calls it, and only for work it can attribute.
    public bool Abort()
    {
        var sent = false;
        IpcGate.Run(abort.HasAction, () =>
        {
            abort.InvokeAction();
            sent = true;
        }, AbortFailed);
        return sent;
    }

    private static HousingAddress AddressOf(in HousingDestination destination, uint aetheryteId)
        => (string.Empty, (int)destination.WorldId, (int)aetheryteId, destination.WardIndex + ResidentialDistricts.OneBased, HouseProperty, destination.PlotIndex + ResidentialDistricts.OneBased, UnusedApartment, false, false, string.Empty);
}
