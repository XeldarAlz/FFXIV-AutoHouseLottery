using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Game;

// Stamps every setup, refresh and requested update of one window with a GameSequence value, and its close, so a reader
// can tell whether what the window shows was produced after a given request or capture.
internal sealed class AddonUpdateCounter : IDisposable
{
    private static readonly AddonEvent[] updateEvents = [AddonEvent.PostSetup, AddonEvent.PostRefresh, AddonEvent.PostRequestedUpdate];

    private readonly string addonName;

    public AddonUpdateCounter(string addonName)
    {
        this.addonName = addonName;
        for (var eventIndex = 0; eventIndex < updateEvents.Length; eventIndex++)
        {
            Svc.AddonLifecycle.RegisterListener(updateEvents[eventIndex], addonName, OnUpdated);
        }

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, OnClosing);
    }

    public string AddonName => addonName;

    public long LastUpdateSequence { get; private set; }

    public AddonEvent LastUpdateEvent { get; private set; }

    // Refreshes only, without the setup: what the window shows after data arrived while it was already open.
    public long LastRefreshSequence { get; private set; }

    public long LastClosedSequence { get; private set; }

    public void Dispose()
    {
        for (var eventIndex = 0; eventIndex < updateEvents.Length; eventIndex++)
        {
            Svc.AddonLifecycle.UnregisterListener(updateEvents[eventIndex], addonName, OnUpdated);
        }

        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonName, OnClosing);
    }

    private void OnUpdated(AddonEvent type, AddonArgs args)
    {
        LastUpdateSequence = GameSequence.Next();
        LastUpdateEvent = type;
        if (type != AddonEvent.PostSetup)
        {
            LastRefreshSequence = LastUpdateSequence;
        }
    }

    private void OnClosing(AddonEvent type, AddonArgs args)
    {
        LastClosedSequence = GameSequence.Next();
    }
}
