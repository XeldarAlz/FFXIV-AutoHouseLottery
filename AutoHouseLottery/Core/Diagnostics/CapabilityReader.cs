using System.Globalization;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Diagnostics.Model;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Lottery;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Game.Ward;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Diagnostics;

// Gathers what the plugin can tell about itself for the capability list and the support report. Framework thread only;
// every source it reads is cached or cheap, and the caller reads it once a second at most.
internal static class CapabilityReader
{
    public static CapabilityFacts Read(Plugin plugin)
    {
        var loggedIn = Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded;
        var gameVersion = LotteryReader.GameVersion();
        var observers = plugin.Housing;
        return new CapabilityFacts
        {
            Listings = Listings(plugin),
            LoggedIn = loggedIn,
            HomeRegion = loggedIn ? WorldRegionReader.Of(Svc.PlayerState.HomeWorld.RowId) : WorldRegion.Unknown,
            JournalWritable = plugin.HousingData.JournalWritable,
            Lifestream = LifestreamIpc.Instance.Capabilities(),
            LifestreamVersion = ExternalPlugins.LoadedVersion(ExternalPlugin.Lifestream)?.ToString(),
            NavmeshAvailable = NavmeshIpc.Instance.Available,
            AutoAnswerPlugin = AutoAnswerGuard.LoadedName(),
            LotteryRecordValidated = LotteryRecordAccess.IsValidated(gameVersion),
            GameVersion = gameVersion,
            PlacardSale = observers.PlacardSales.Active ? PlacardSaleFact.Active
                : observers.PlacardSales.SignatureFound ? PlacardSaleFact.FoundNotAttached
                : PlacardSaleFact.NotFound,
            WardList = WardReader.LastLayoutVerdict switch
            {
                WardReadStatus.Read => WardListFact.Read,
                WardReadStatus.LayoutMismatch => WardListFact.LayoutMismatch,
                _ => WardListFact.NotSeen,
            },
            WardPacketsActive = observers.WardPackets.Active,
            EntryConfirmValidated = EntryCapabilities.EntryConfirmValidated,
            RefundValidated = SettlementCapabilities.RefundConfirmValidated,
            ClaimValidated = SettlementCapabilities.ClaimConfirmValidated,
            AbsenceSettlementValidated = SettlementCapabilities.AbsenceSettlementValidated,
            EntryPermitted = plugin.Controller.RealEntryAllowed,
            RefundPermitted = plugin.Controller.RealRefundAllowed,
            ClaimPermitted = plugin.Controller.RealClaimAllowed,
            BatchEntryValidated = BatchCapabilities.BatchEntryValidated,
            CharacterSwitchValidated = SwitchCapabilities.CharacterSwitchValidated,
            BatchSwitchPermitted = plugin.Controller.BatchSwitchGranted,
            RefundSwitchPermitted = plugin.Controller.RefundSwitchGranted,
        };
    }

    public static string DalamudVersion() => typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version?.ToString() ?? "unknown";

    public static string ClientLanguage() => Svc.ClientState.ClientLanguage.ToString();

    public static string HomeRegionName(in CapabilityFacts facts) => facts.LoggedIn ? facts.HomeRegion.ToString() : "not logged in";

    public static string Territory() => Svc.ClientState.TerritoryType.ToString(CultureInfo.InvariantCulture);

    private static ListingsFact Listings(Plugin plugin)
    {
        var discovery = plugin.Discovery;
        var world = discovery.World;
        if (world == 0 || !discovery.WorldSupported)
        {
            return ListingsFact.NoWorld;
        }

        var status = discovery.Feed.Snapshot.StatusOf(world);
        if (status.HasData)
        {
            return status.ShowingSavedCopy ? ListingsFact.SavedCopy : ListingsFact.Live;
        }

        return status.Failure != Providers.Model.FeedFailure.None && !status.Loading ? ListingsFact.Unavailable : ListingsFact.Loading;
    }
}
