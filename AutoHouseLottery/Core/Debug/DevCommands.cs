using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Tasks.Model;
using ECommons.DalamudServices;
using System.Globalization;

namespace AutoHouseLottery.Core.Debug;

// /ahl dev: the maintainer's tools for the in-game validation ladder until Find a home starts runs. "run" starts an
// exact-plot run for the logged-in character, which is a dry run unless a grant covers it. "allow-real-entry" grants
// one real entry for one named character, plot and deposit ceiling for this session only; nothing about it is saved.
internal static class DevCommands
{
    private const int RunArgumentCount = 5;
    private const int RunWithBuyerArgumentCount = 6;
    private const int GrantPlaceArgumentCount = 4;
    private const int MinimumNameParts = 2;
    private const string FreeCompanyBuyer = "fc";
    private const long NoReserve = 0;
    private const long NoTravelAllowance = 0;
    private const int FirstRevision = 1;

    public static void Run(string arguments, RunController controller, HousingContext housing)
    {
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = tokens.Length == 0 ? string.Empty : tokens[0].ToLowerInvariant();
        switch (verb)
        {
            case "run" when tokens.Length is RunArgumentCount or RunWithBuyerArgumentCount:
                StartRun(tokens, controller, housing);
                return;
            case "allow-real-entry" when tokens.Length >= 1 + MinimumNameParts + GrantPlaceArgumentCount:
                Grant(tokens, controller);
                return;
            case "revoke-real-entry":
                controller.RevokeRealEntry();
                Print(Loc.T(L.Dev.Revoked));
                return;
            default:
                Print(Loc.T(L.Dev.Usage));
                return;
        }
    }

    private static void StartRun(string[] tokens, RunController controller, HousingContext housing)
    {
        if (!ResidentialDistricts.TryFindByKeyword(tokens[1], out var district)
            || !int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ward)
            || !int.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out var plot)
            || !long.TryParse(tokens[4], NumberStyles.None, CultureInfo.InvariantCulture, out var deposit)
            || !Svc.PlayerState.IsLoaded
            || !PlotKey.TryCreate(HousingContext.Region, Svc.PlayerState.HomeWorld.RowId, district.TerritoryTypeId, ward, plot, out var key))
        {
            Print(Loc.T(L.Dev.Usage));
            return;
        }

        var buyer = tokens.Length == RunWithBuyerArgumentCount && tokens[5].Equals(FreeCompanyBuyer, StringComparison.OrdinalIgnoreCase) ? BuyerType.FreeCompany : BuyerType.Private;
        var policy = new RunPolicy
        {
            Revision = FirstRevision,
            Cycle = housing.CurrentPosition(DateTime.UtcNow),
            Characters = [new CharacterRunSettings(Svc.PlayerState.ContentId, buyer, deposit, NoReserve)],
            TravelAllowance = NoTravelAllowance,
            Exact = new ExactTarget(key, deposit),
        };
        var refusal = controller.StartExact(policy);
        if (refusal != StartRefusal.None)
        {
            Print(Loc.T(L.Dev.RunRefused, RunText.Refusal(refusal)));
            return;
        }

        Print(Loc.T(controller.Status.EntryMode == EntryMode.Real ? L.Dev.RealRunStarted : L.Dev.DryRunStarted, PlotNames.Address(key), Loc.Gil(deposit)));
    }

    private static void Grant(string[] tokens, RunController controller)
    {
        var placeStart = tokens.Length - GrantPlaceArgumentCount;
        var name = string.Join(' ', tokens, 1, placeStart - 1);
        if (!ResidentialDistricts.TryFindByKeyword(tokens[placeStart], out var district)
            || !int.TryParse(tokens[placeStart + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var ward)
            || !int.TryParse(tokens[placeStart + 2], NumberStyles.None, CultureInfo.InvariantCulture, out var plot)
            || !long.TryParse(tokens[placeStart + 3], NumberStyles.None, CultureInfo.InvariantCulture, out var ceiling)
            || controller.GrantRealEntry(name, district.TerritoryTypeId, ward, plot, ceiling) is not { } grant)
        {
            Print(Loc.T(L.Dev.GrantRefused));
            return;
        }

        Svc.Chat.PrintError($"{HouseLotteryConstants.LogPrefix} {Loc.T(L.Dev.GrantWarning, grant.CharacterName, PlotNames.Address(grant.Plot), Loc.Gil(grant.MaxDeposit))}");
    }

    private static void Print(string message) => Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {message}");
}
