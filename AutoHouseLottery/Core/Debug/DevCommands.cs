using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Tasks.Model;
using ECommons.DalamudServices;
using System.Globalization;

namespace AutoHouseLottery.Core.Debug;

// /ahl dev: the maintainer's tools for the in-game validation ladder. "run" starts an exact-plot run for the logged-in
// character; it is a dry run unless the word "real" is given and a grant covers that plot. "allow-real-entry" grants one
// real entry for one named character, plot and deposit ceiling for this session only; nothing about it is saved.
// "settle" visits one entry's plot to read its result and, with the word "real" and an "allow-real-refund" or
// "allow-real-claim" grant for that plot, accepts it once.
internal static class DevCommands
{
    private const int RunArgumentCount = 5;
    private const int RunWithOptionsArgumentCount = 7;
    private const int GrantPlaceArgumentCount = 4;
    private const int MinimumNameParts = 2;
    private const string FreeCompanyBuyer = "fc";
    private const string RealEntryWord = "real";
    private const long NoReserve = 0;
    private const long NoTravelAllowance = 0;
    private const int FirstRevision = 1;
    private const int SettleMinimumArgumentCount = 2;
    private const int SettlementGrantPlaceArgumentCount = 3;
    private const int AddressArgumentCount = 3;
    private const int MinimumIdPrefixLength = 4;
    private const string ClaimWord = "claim";

    public static void Run(string arguments, RunController controller, HousingContext housing)
    {
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = tokens.Length == 0 ? string.Empty : tokens[0].ToLowerInvariant();
        switch (verb)
        {
            case "run" when tokens.Length is >= RunArgumentCount and <= RunWithOptionsArgumentCount:
                StartRun(tokens, controller, housing);
                return;
            case "allow-real-entry" when tokens.Length >= 1 + MinimumNameParts + GrantPlaceArgumentCount:
                Grant(tokens, controller);
                return;
            case "revoke-real-entry":
                controller.RevokeRealEntry();
                Print(Loc.T(L.Dev.Revoked));
                return;
            case "settle" when tokens.Length >= SettleMinimumArgumentCount:
                Settle(tokens, controller, housing);
                return;
            case "allow-real-refund" when tokens.Length >= 1 + MinimumNameParts + SettlementGrantPlaceArgumentCount:
                GrantSettlement(tokens, ActionKind.Refund, controller);
                return;
            case "allow-real-claim" when tokens.Length >= 1 + MinimumNameParts + SettlementGrantPlaceArgumentCount:
                GrantSettlement(tokens, ActionKind.Claim, controller);
                return;
            case "revoke-real-settlement":
                controller.RevokeRealSettlement();
                Print(Loc.T(L.Dev.SettlementRevoked));
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

        var buyer = HasOption(tokens, FreeCompanyBuyer) ? BuyerType.FreeCompany : BuyerType.Private;
        var dryRun = !HasOption(tokens, RealEntryWord);
        var policy = new RunPolicy
        {
            Revision = FirstRevision,
            Cycle = housing.CurrentPosition(DateTime.UtcNow),
            Characters = [new CharacterRunSettings(Svc.PlayerState.ContentId, buyer, deposit, NoReserve)],
            TravelAllowance = NoTravelAllowance,
            Exact = new ExactTarget(key, deposit),
        };
        var refusal = controller.StartExact(policy, dryRun);
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

    // settle <receipt id or its first characters | district ward plot> [claim] [real]: a settlement visit for the
    // logged-in character's entry there. Without the word real it is a dry run whatever is permitted.
    private static void Settle(string[] tokens, RunController controller, HousingContext housing)
    {
        var claim = HasOption(tokens, ClaimWord, 1);
        var real = HasOption(tokens, RealEntryWord, 1);
        if (!Svc.PlayerState.IsLoaded || FindSettlement(tokens, housing, Svc.PlayerState.ContentId) is not { } found)
        {
            Print(Loc.T(L.Dev.SettleNotFound));
            return;
        }

        var mode = claim ? SettlementMode.Claim : SettlementMode.Refund;
        var refusal = controller.StartSettlement(found.Id, mode, SettlementOrigin.Command, dryRun: !real);
        if (refusal != SettlementRefusal.None)
        {
            Print(Loc.T(L.Dev.SettleRefused, SettlementText.Refusal(refusal)));
            return;
        }

        Print(Loc.T(real ? L.Dev.SettleRealStarted : L.Dev.SettleStarted, SettlementText.Title(mode), found.Shown));
    }

    // A receipt, or an earlier cycle's uncertain entry attempt, of the logged-in character: by its id's first characters,
    // or by its district, ward and plot.
    private static (Guid Id, string Shown)? FindSettlement(string[] tokens, HousingContext housing, ulong contentId)
    {
        var snapshot = housing.JournalSnapshot;
        var checks = ResultChecks.Due(snapshot, housing.CurrentPosition(DateTime.UtcNow).Cycle);
        PlotKey? address = tokens.Length > AddressArgumentCount
            && ResidentialDistricts.TryFindByKeyword(tokens[1], out var district)
            && int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ward)
            && int.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out var plot)
            && PlotKey.TryCreate(HousingContext.Region, Svc.PlayerState.HomeWorld.RowId, district.TerritoryTypeId, ward, plot, out var key)
                ? key
                : null;
        var prefix = tokens[1];
        if (address is null && prefix.Length < MinimumIdPrefixLength)
        {
            return null;
        }

        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterContentId == contentId
                && SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is { IsSettled: false }
                && (address is { } wanted ? receipt.Plot == wanted : receipt.ReceiptId.ToString("N").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return (receipt.ReceiptId, receipt.Plot is { } named ? PlotNames.Address(named) : receipt.AddressText ?? receipt.ReceiptId.ToString("N"));
            }
        }

        for (var index = 0; index < checks.Length; index++)
        {
            var check = checks[index];
            if (check.Source == ResultCheckSource.UncertainAttempt
                && check.ContentId == contentId
                && (address is { } wanted ? check.Plot == wanted : check.Id.ToString("N").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return (check.Id, check.Plot is { } named ? PlotNames.Address(named) : check.Id.ToString("N"));
            }
        }

        return null;
    }

    private static void GrantSettlement(string[] tokens, ActionKind kind, RunController controller)
    {
        var placeStart = tokens.Length - SettlementGrantPlaceArgumentCount;
        var name = string.Join(' ', tokens, 1, placeStart - 1);
        if (!ResidentialDistricts.TryFindByKeyword(tokens[placeStart], out var district)
            || !int.TryParse(tokens[placeStart + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var ward)
            || !int.TryParse(tokens[placeStart + 2], NumberStyles.None, CultureInfo.InvariantCulture, out var plot)
            || controller.GrantRealSettlement(kind, name, district.TerritoryTypeId, ward, plot) is not { } grant)
        {
            Print(Loc.T(L.Dev.SettlementGrantRefused));
            return;
        }

        var warning = kind == ActionKind.Refund ? L.Dev.RefundGrantWarning : L.Dev.ClaimGrantWarning;
        Svc.Chat.PrintError($"{HouseLotteryConstants.LogPrefix} {Loc.T(warning, grant.CharacterName, PlotNames.Address(grant.Plot))}");
    }

    private static bool HasOption(string[] tokens, string option, int from = RunArgumentCount)
    {
        for (var index = from; index < tokens.Length; index++)
        {
            if (tokens[index].Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Print(string message) => Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {message}");
}
