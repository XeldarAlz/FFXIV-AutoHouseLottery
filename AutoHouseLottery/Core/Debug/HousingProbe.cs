using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Lottery;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Game.Placard;
using AutoHouseLottery.Core.Game.Ward;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;
using Dalamud.Game;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Debug;

// /ahl probe: logs every housing reading for where the character stands, so M0 can be checked in game, and offers the
// few debug actions that cost nothing (paging wards, a Timers pass, travel, the walk to a placard, the presses that
// open a dialog, No on a result, and closing the entry confirmation). Committing presses (Confirm, Yes) are not
// reachable from here.
internal static unsafe class HousingProbe
{
    private const string Prefix = "Probe:";
    private const int DescribedDialogValues = 32;
    private const int RecentLogMessages = 8;
    private const int VisibleIdDigits = 4;
    private const int GotoArgumentCount = 4;
    private const int ApproachArgumentCount = 3;
    private const int PressArgumentCount = 2;

    private static readonly ClientLanguage[] selfCheckLanguages = [ClientLanguage.English, ClientLanguage.German, ClientLanguage.French, ClientLanguage.Japanese];
    private static readonly HousingText[] dialogLabels = [HousingText.ConfirmLabel, HousingText.YesLabel, HousingText.YesLabelAlternate, HousingText.NoLabel, HousingText.NoLabelAlternate];
    private static readonly HousingText[] checkBoxLabels = [HousingText.ProceedCheckbox];

    public static void Run(string arguments, HousingObservers observers, HousingContext housing)
    {
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = tokens.Length == 0 ? string.Empty : tokens[0].ToLowerInvariant();
        switch (verb)
        {
            case "":
                LogReadings(observers, housing);
                Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {Loc.T(L.Probe.Done)}");
                return;
            case "scan":
                Start(() => new WardScanTask(observers.WardWindow, observers.WardPackets), task => ApplyScan(task, housing));
                return;
            case "lottery":
                Start(() => new LotteryTimersPass(), task => ImportReading(task, housing));
                return;
            case "goto" when tokens.Length == GotoArgumentCount && ResidentialDistricts.TryFindByKeyword(tokens[1], out var district) && TryDestination(district.TerritoryTypeId, tokens[2], tokens[3], out var destination):
                Start(() => new ProbeTravelTask(destination, travel: true, observers));
                return;
            case "approach" when tokens.Length == ApproachArgumentCount && TryDestination(Svc.ClientState.TerritoryType, tokens[1], tokens[2], out var destination):
                Start(() => new ProbeTravelTask(destination, travel: false, observers));
                return;
            case "press" when tokens.Length == PressArgumentCount && UiIsOwned():
                Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {Loc.T(L.Probe.Failed)}");
                return;
            case "press" when tokens.Length == PressArgumentCount && Press(tokens[1].ToLowerInvariant()):
                Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {Loc.T(L.Probe.Done)}");
                return;
            case "stop" when Plugin.Instance.Controller.Busy:
                RunLog.Warning($"{Prefix} stop refused; an entry run or a reconciliation owns the game. Use /ahl stop for the run.");
                Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {Loc.T(L.Probe.Failed)}");
                return;
            case "stop":
                TaskStarter.StopCurrent();
                if (HousingTravel.CurrentToken is { } token)
                {
                    HousingTravel.CancelOwned(token);
                }

                return;
            default:
                Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {Loc.T(L.Probe.Usage)}");
                return;
        }
    }

    private static void Start(Func<AutoCommon> build, Action<AutoCommon>? completed = null)
    {
        var started = !Plugin.Instance.Controller.Busy && TaskStarter.TryStart(build, completed);
        Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {Loc.T(started ? L.Probe.Started : L.Probe.Failed)}");
    }

    private static void ApplyScan(AutoCommon task, HousingContext housing)
    {
        if (task is WardScanTask { Result: { } result })
        {
            housing.ApplyWardScan(result);
        }
    }

    private static void ImportReading(AutoCommon task, HousingContext housing)
    {
        if (task is LotteryTimersPass { Outcome: LotteryPassOutcome.Loaded } pass && Svc.PlayerState.ContentId == pass.Snapshot.ContentId)
        {
            _ = housing.ImportFromReading(pass.Snapshot, pass.Snapshot.ContentId, Svc.PlayerState.HomeWorld.RowId);
        }
    }

    // A press while a run or a task drives the game would fight it for the same window.
    private static bool UiIsOwned()
    {
        if (!Plugin.Instance.Controller.Busy && !TaskStarter.Busy)
        {
            return false;
        }

        RunLog.Warning($"{Prefix} press refused; a run or a task is driving the game.");
        return true;
    }

    private static bool TryDestination(uint territoryTypeId, string wardText, string plotText, out HousingDestination destination)
    {
        destination = default;
        if (!int.TryParse(wardText, out var ward) || !int.TryParse(plotText, out var plot))
        {
            return false;
        }

        destination = new HousingDestination(Svc.PlayerState.CurrentWorld.RowId, territoryTypeId, ward - ResidentialDistricts.OneBased, plot - ResidentialDistricts.OneBased);
        return destination.IsValid;
    }

    private static bool Press(string what)
    {
        DialogActionResult result;
        switch (what)
        {
            case "enter":
                using (NativeActionGate.Allow(NativeAction.PressEnterLottery))
                {
                    result = HousingDialogs.PressPlacardButton(HousingText.EnterLotteryButton);
                }

                break;
            case "results":
                using (NativeActionGate.Allow(NativeAction.PressViewResults))
                {
                    result = HousingDialogs.PressPlacardButton(HousingText.ViewResultsButton);
                }

                break;
            case "cancel":
                using (NativeActionGate.Allow(NativeAction.CancelEntry))
                {
                    result = HousingDialogs.CancelEntry();
                }

                break;
            case "no" when HousingDialogs.IdentifyResultDialog() is { Text: { } shown }:
                using (NativeActionGate.Allow(NativeAction.DeclineResult))
                {
                    result = HousingDialogs.DeclineResult(shown);
                }

                break;
            case "no":
                result = DialogActionResult.IdentityMismatch;
                break;
            default:
                return false;
        }

        RunLog.Info($"{Prefix} press {what}: {result}.");
        return true;
    }

    private static void LogReadings(HousingObservers observers, HousingContext housing)
    {
        RunLog.Info($"{Prefix} ---- housing probe, client language {HousingSheetText.Language} ----");
        LogCharacter();
        LogLottery();
        LogWard(observers);
        LogPlacard(observers, housing);
        LogDialogs();
        LogSheetRows();
        LogHousingMessages(observers.HousingLog);
        LogTravel();
        RunLog.Info($"{Prefix} ---- end ----");
    }

    private static void LogCharacter()
    {
        if (CharacterContextReader.Read() is not { } context)
        {
            RunLog.Info($"{Prefix} character: not logged in.");
            return;
        }

        RunLog.Info($"{Prefix} character: content ID …{Tail(context.ContentId)}, name {(context.Name.Length > 0 ? "read" : "empty")}, home world {context.HomeWorldId}, "
                  + $"current world {context.CurrentWorldId}{(context.OnHomeWorld ? " (home)" : string.Empty)}, gil {Show(context.Gil)}, level {context.Level}, "
                  + $"grand company {context.GrandCompanyId} rank {Show(context.GrandCompanyRank)}.");
        RunLog.Info($"{Prefix} free company: ID {(context.FreeCompanyId is { } companyId ? $"…{Tail(companyId)}" : "unknown")}, name {(context.FreeCompanyName is null ? "unknown" : "read")}, "
                  + $"tag {(context.FreeCompanyTag is null ? "unknown" : "read")}, home world {Show(context.FreeCompanyHomeWorldId)}.");
        var location = context.Location;
        RunLog.Info($"{Prefix} location: territory {location.TerritoryTypeId}, ward {ShowOneBased(location.WardIndex)}, plot {ShowOneBased(location.PlotIndex)}, "
                  + $"division {Show(location.Division)}, inside {location.Inside}.");
        RunLog.Info($"{Prefix} owned estates: personal {Describe(context.PersonalEstate)}, free company {Describe(context.FreeCompanyEstate)}, apartment {Describe(context.ApartmentRoom)}.");
    }

    private static void LogLottery()
    {
        var gameVersion = LotteryReader.GameVersion();
        var validated = LotteryRecordAccess.IsValidated(gameVersion);
        RunLog.Info($"{Prefix} game version {gameVersion}, lottery record {(validated ? "validated" : "not validated")} on this build.");
        RunLog.Info($"{Prefix} {LotteryReader.Describe(LotteryReader.Read())} (no Timers pass; /ahl probe lottery runs one).");
        if (!validated)
        {
            RunLog.Info($"{Prefix} for validation only, through checked reads: {LotteryReader.Describe(LotteryReader.ReadRecordForValidation())}.");
        }

        var texts = new List<string>();
        LotteryReader.CollectWindowTexts(LotteryReader.TimersAddon, texts);
        LotteryReader.CollectWindowTexts(LotteryReader.TimersDetailAddon, texts);
        for (var textIndex = 0; textIndex < texts.Count; textIndex++)
        {
            RunLog.Debug($"{Prefix} Timers text {textIndex}: '{texts[textIndex]}'.");
        }
    }

    private static void LogWard(HousingObservers observers)
    {
        var window = observers.WardWindow;
        var header = WardReader.ReadHeader(window.LastUpdateSequence);
        var status = WardReader.ReadDisplayed(window.LastUpdateSequence, out var reading);
        RunLog.Info($"{Prefix} ward window: {status}, shown ward {ShowOneBased(header.ShownWardIndex >= 0 ? header.ShownWardIndex : null)}, last update {window.LastUpdateEvent} at sequence {window.LastUpdateSequence}, "
                  + $"portal territory {Show(WardReader.PortalTerritory())}.");
        if (reading is not null)
        {
            var acknowledged = WardPager.AcceptDisplayed(reading.WardIndex, window, observers.WardPackets);
            RunLog.Info($"{Prefix} shown ward {(acknowledged is null ? "not tied to its packet yet" : $"acknowledged ({acknowledged.Correlation})")}.");
            RunLog.Info($"{Prefix} ward {reading.WardIndex + ResidentialDistricts.OneBased} '{reading.WardName}', ward count {Show(reading.WardCount)}, buyer flags 0x{reading.BuyerFlags:X8} "
                      + $"(FC bit {WardLayout.FreeCompanyFlag(reading.BuyerFlags)}, private bit {WardLayout.PrivateFlag(reading.BuyerFlags)}), {reading.Rows.Length} rows.");
            for (var rowIndex = 0; rowIndex < reading.Rows.Length; rowIndex++)
            {
                var row = reading.Rows[rowIndex];
                RunLog.Debug($"{Prefix} ward row {row.PlotIndex + ResidentialDistricts.OneBased}: status {row.Status}, size '{row.SizeText}', name or price '{row.NameOrPriceText}', "
                           + $"value 3 {row.UnidentifiedValue}, tags {row.Tag1}/{row.Tag2}/{row.Tag3}.");
            }
        }

        var packets = observers.WardPackets;
        if (packets.Latest is not { } packet)
        {
            RunLog.Info($"{Prefix} ward packet: none seen (observer {(packets.Active ? "active" : "off")}).");
            return;
        }

        var owned = 0;
        for (var entryIndex = 0; entryIndex < packet.Entries.Length; entryIndex++)
        {
            var entry = packet.Entries[entryIndex];
            if (entry.PlotOwned)
            {
                owned++;
                continue;
            }

            RunLog.Debug($"{Prefix} ward packet plot {entry.PlotIndex + ResidentialDistricts.OneBased}: unowned, price {entry.Price}, flags 0x{entry.Flags:X2}.");
        }

        RunLog.Info($"{Prefix} ward packet: territory {packet.TerritoryTypeId}, ward {packet.WardIndex + ResidentialDistricts.OneBased}, world {packet.WorldId}, {owned} of {packet.Entries.Length} owned, "
                  + $"purchase type {packet.PurchaseType}, tenant type {packet.TenantType}, sequence {packet.Sequence}.");
    }

    private static void LogPlacard(HousingObservers observers, HousingContext housing)
    {
        RunLog.Info($"{Prefix} placard sources: sale hook {(observers.PlacardSales.Active ? "active" : observers.PlacardSales.SignatureFound ? "signature found, not hooked" : "signature missing")}, "
                  + $"agent layout {observers.SignboardLayout.Describe()}.");
        if (observers.PlacardSales.Latest is { } capture)
        {
            var current = PlacardCaptureRule.BelongsToOpenWindow(capture.Sequence, observers.PlacardWindow.LastClosedSequence, AddonReads.TryGetReady(PlacardReader.AddonName, out _));
            RunLog.Info($"{Prefix} sale hook capture{(current ? " (this placard)" : " (an earlier placard)")}: housing type {capture.HousingType?.ToString() ?? $"unknown ({capture.RawHousingType})"}, "
                      + $"{Describe(capture.Address)}, apartment {capture.ApartmentNumber}, sale {Describe(capture.Sale)}.");
        }

        if (PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout) is not { } reading)
        {
            RunLog.Info($"{Prefix} placard: window not open.");
            return;
        }

        var agent = AgentHousingSignboard.Instance();
        if (agent != null)
        {
            RunLog.Info($"{Prefix} placard agent sale block as stored: {Describe(observers.SignboardLayout.ReadSaleBlock(agent))}.");
        }

        RunLog.Info($"{Prefix} placard address: hook {Describe(reading.HookAddress)}, agent {Describe(reading.AgentAddress)}, text '{reading.AddressText ?? "missing"}', "
                  + $"agreement {reading.Agreement}, address {Describe(reading.Address)}.");
        RunLog.Info($"{Prefix} placard: housing type {Show(reading.HousingType)}, for sale {Show(reading.ForSale)}, size {Show(reading.Size)}, "
                  + $"owner FC {(reading.OwnerFreeCompanyId is { } owner ? (owner == 0 ? "none" : "set") : "unknown")}, sale {Describe(reading.Sale)} (from {reading.SaleSource}), "
                  + $"details '{reading.DetailsText ?? "missing"}'.");
        LogButtons("placard", reading.Buttons);
        var proved = housing.ObservePlacard(reading, Svc.PlayerState.CurrentWorld.RowId);
        RunLog.Info($"{Prefix} placard cycle {(proved is { } position ? $"{position.Cycle}, {position.Phase} until {position.PhaseEndUtc:u}" : "not proved")}.");
    }

    private static void LogButtons(string window, ImmutableArray<WindowButton> buttons)
    {
        for (var buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
        {
            var button = buttons[buttonIndex];
            RunLog.Info($"{Prefix} {window} button node {button.NodeId}: '{button.Text}', enabled {button.Enabled}, visible {button.Visible}, event param {button.EventParam} "
                      + $"(LlamaLibrary placard candidate {HousingDialogs.PlacardButtonCallbackCandidate}), matches {Show(button.MatchedText)}.");
        }
    }

    private static void LogCheckBoxes(string window, ImmutableArray<WindowCheckBox> checkBoxes)
    {
        if (checkBoxes.IsEmpty)
        {
            RunLog.Info($"{Prefix} {window}: no checkbox component found.");
        }

        for (var checkBoxIndex = 0; checkBoxIndex < checkBoxes.Length; checkBoxIndex++)
        {
            var checkBox = checkBoxes[checkBoxIndex];
            RunLog.Info($"{Prefix} {window} checkbox node {checkBox.NodeId}: '{checkBox.Text}', enabled {checkBox.Enabled}, visible {checkBox.Visible}, checked {checkBox.Checked}, matches {Show(checkBox.MatchedText)}.");
        }
    }

    private static void LogDialogs()
    {
        if (HousingDialogs.IdentifyEntryConfirmation() is { } confirmation)
        {
            RunLog.Info($"{Prefix} {confirmation.AddonName}: matches {Show(confirmation.Text)}, shows '{confirmation.ShownText}'.");
            if (AddonReads.TryGetReady(HousingDialogs.EntryConfirmationAddon, out var addon))
            {
                RunLog.Info($"{Prefix} {HousingDialogs.EntryConfirmationAddon} values: {AddonReads.Describe(addon, DescribedDialogValues)}.");
                LogButtons(HousingDialogs.EntryConfirmationAddon, LabelledButtons.Collect(addon, dialogLabels));
                LogCheckBoxes(HousingDialogs.EntryConfirmationAddon, LabelledButtons.CollectCheckBoxes(addon, checkBoxLabels));
                if (HousingDialogs.ReadEntryConfirmation() is { } shown)
                {
                    RunLog.Info($"{Prefix} entry confirmation reads as row 7046 {shown.Identified}, deposit {Show(shown.Deposit)}, checkbox {shown.Proceed}, Confirm {(shown.ConfirmFound ? shown.ConfirmEnabled ? "enabled" : "disabled" : "missing")}.");
                }
            }
        }

        if (HousingDialogs.IdentifyResultDialog() is { } result)
        {
            RunLog.Info($"{Prefix} {result.AddonName}: matches {Show(result.Text)}, shows '{result.ShownText}'.");
            if (AddonReads.TryGetReady(HousingDialogs.ResultAddon, out var addon))
            {
                LogButtons(HousingDialogs.ResultAddon, LabelledButtons.Collect(addon, dialogLabels));
            }
        }
    }

    private static void LogSheetRows()
    {
        for (var rowIndex = 0; rowIndex < HousingTextRows.All.Length; rowIndex++)
        {
            var row = HousingTextRows.All[rowIndex];
            var pattern = HousingSheetText.Pattern(row.Text);
            RunLog.Debug($"{Prefix} sheet {row.Sheet} {row.RowId} ({row.Text}): {(pattern.IsUsable ? $"{pattern.Segments.Length} segment(s), first '{pattern.Segments[0]}'" : "not usable")}.");
        }

        for (var languageIndex = 0; languageIndex < selfCheckLanguages.Length; languageIndex++)
        {
            SelfCheckResultRows(selfCheckLanguages[languageIndex]);
        }
    }

    // Each result row is rendered by the game's evaluator and extracted as a dialog is; it has to match its own row
    // and no other result row, in every client language the game data holds.
    private static void SelfCheckResultRows(ClientLanguage language)
    {
        var rows = HousingTextRows.ResultDialogs;
        var failures = 0;
        try
        {
            for (var renderedIndex = 0; renderedIndex < rows.Length; renderedIndex++)
            {
                var sample = HousingSheetText.RenderedSample(rows[renderedIndex], language);
                for (var patternIndex = 0; patternIndex < rows.Length; patternIndex++)
                {
                    var matches = HousingSheetText.PatternIn(rows[patternIndex], language).Matches(sample);
                    if (matches != (patternIndex == renderedIndex))
                    {
                        failures++;
                        RunLog.Warning($"{Prefix} self-check {language}: {rows[patternIndex]} {(matches ? "matches" : "does not match")} the rendered {rows[renderedIndex]} '{sample}'.");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"{Prefix} self-check {language}: the rows could not be rendered");
            return;
        }

        RunLog.Info($"{Prefix} self-check {language}: {rows.Length} result rows, {failures} mismatch(es).");
    }

    private static void LogHousingMessages(HousingLogWatcher watcher)
    {
        Span<HousingLogObservation> recent = stackalloc HousingLogObservation[RecentLogMessages];
        var count = watcher.CopyRecent(recent);
        if (count == 0)
        {
            RunLog.Info($"{Prefix} housing log messages: none seen this session.");
            return;
        }

        for (var observationIndex = 0; observationIndex < count; observationIndex++)
        {
            var observation = recent[observationIndex];
            RunLog.Info($"{Prefix} housing log message LogMessage {observation.RowId} ({observation.Text}) at {RunLog.Time(observation.AtUtc)}, first number {Show(observation.FirstNumber)}.");
        }
    }

    private static void LogTravel()
    {
        var lifestream = LifestreamIpc.Instance;
        var capabilities = lifestream.Capabilities();
        RunLog.Info($"{Prefix} Lifestream: {capabilities}, busy {Show(lifestream.IsBusy())}, current plot {(lifestream.CurrentPlotInfo() is { } info ? $"kind {info.Kind}, ward {info.Ward + ResidentialDistricts.OneBased}, plot {info.Plot + ResidentialDistricts.OneBased}" : "none")}, "
                  + $"travel token {Show(HousingTravel.CurrentToken)}.");
        RunLog.Info($"{Prefix} vnavmesh: {(NavmeshIpc.Instance.Available ? "available" : "not available")}.");
    }

    private static string Describe(PlacardSaleInfo? sale)
        => sale is not { } info
            ? "none"
            : $"purchase {info.PurchaseType?.ToString() ?? $"unknown ({info.RawPurchaseType})"}, tenant {info.TenantType?.ToString() ?? $"unknown ({info.RawTenantType})"}, "
            + $"availability {info.Availability?.ToString() ?? $"unknown ({info.RawAvailability})"}, phase ends {info.PhaseEndsAtUtc?.ToString("u") ?? "unknown"}, entries {info.EntryCount}";

    private static string Describe(PlotAddress? address)
        => address is not { } plot ? "none" : $"territory {plot.TerritoryTypeId}, ward {plot.WardIndex + ResidentialDistricts.OneBased}, plot {plot.PlotIndex + ResidentialDistricts.OneBased}";

    private static string Describe(EstateAddress? estate)
        => estate is not { } address ? "none" : $"territory {address.TerritoryTypeId}, ward {address.WardIndex + ResidentialDistricts.OneBased}, plot {address.PlotIndex + ResidentialDistricts.OneBased}{(address.Apartment ? " (apartment)" : string.Empty)}";

    private static string Tail(ulong value)
    {
        var hex = value.ToString("X");
        return hex.Length <= VisibleIdDigits ? hex : hex[^VisibleIdDigits..];
    }

    private static string Show<T>(T? value) where T : struct => value?.ToString() ?? "unknown";

    private static string ShowOneBased(int? index) => index is { } value ? (value + ResidentialDistricts.OneBased).ToString() : "none";
}
