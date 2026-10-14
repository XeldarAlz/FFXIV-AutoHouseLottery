using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Integration;

internal static class IntegrationFixtures
{
    public const ulong ContentId = 4_000_000_010;
    public const uint LotteryNumber = 412;
    public const string MistName = "Mist";

    public static CharacterContext Context(uint? gil = 5_000_000, EstateAddress? personalEstate = null, ulong? freeCompanyId = null) => new(
        ContentId,
        "Test Character",
        HomeWorld,
        HomeWorld,
        gil,
        90,
        1,
        null,
        freeCompanyId,
        null,
        null,
        null,
        new HousingLocation(HousingDistricts.Mist, 11, null, 1, false),
        personalEstate,
        null,
        null,
        ObservedAt);

    public static LotterySnapshot RecordReading(int ward, int plot, Core.Game.Model.LotteryPhase phase = Core.Game.Model.LotteryPhase.EntryPeriod, ulong contentId = ContentId)
        => new(
            LotterySnapshotState.Active,
            LotterySource.Record,
            contentId,
            new LotteryEntry(HousingDistricts.Mist, ward - 1, plot - 1, LotteryNumber, LotteryBuyerType.Private, phase, 2, (byte)phase, null, null),
            ObservedAt);

    public static LotterySnapshot TextReading(string? addressText, Core.Game.Model.LotteryPhase? phase = Core.Game.Model.LotteryPhase.EntryPeriod)
        => new(
            LotterySnapshotState.Active,
            LotterySource.TimersText,
            ContentId,
            new LotteryEntry(null, null, null, LotteryNumber, null, phase, null, null, addressText, null),
            ObservedAt);

    public static LotterySnapshot Reading(LotterySnapshotState state)
        => new(state, LotterySource.Record, ContentId, null, ObservedAt);

    public static string? MistOnly(uint territoryTypeId) => territoryTypeId == HousingDistricts.Mist ? MistName : null;

    public static ActionAttempt Attempt(PlotKey plot, AttemptState state, CycleKey? cycle = null) => new()
    {
        AttemptId = Guid.NewGuid(),
        Kind = ActionKind.Entry,
        RunId = Guid.NewGuid(),
        CharacterContentId = ContentId,
        HomeWorldId = HomeWorld,
        Cycle = cycle ?? FirstCycle,
        Plot = plot,
        Buyer = BuyerType.Private,
        Amount = Deposit,
        PreparedUtc = ObservedAt,
        State = state,
        UpdatedUtc = ObservedAt,
    };

    public static EntryReceipt Receipt(PlotKey? plot, CycleKey cycle, ReceiptEvidence evidence = ReceiptEvidence.NativeConfirmation) => new()
    {
        ReceiptId = Guid.NewGuid(),
        CharacterContentId = ContentId,
        HomeWorldId = HomeWorld,
        Cycle = cycle,
        Plot = plot,
        Buyer = BuyerType.Private,
        Evidence = evidence,
        EntryNumber = (int)LotteryNumber,
        Deposit = evidence == ReceiptEvidence.ImportedFromGame ? null : Deposit,
        EnteredUtc = ObservedAt,
    };

    public static JournalSnapshot Journal(ActionAttempt[] attempts, params (EntryReceipt Receipt, SettlementState State)[] entries)
    {
        var receipts = new EntryReceipt[entries.Length];
        var settlements = new EntrySettlement[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            receipts[index] = entries[index].Receipt;
            settlements[index] = EntrySettlement.Entered(entries[index].Receipt.ReceiptId, ObservedAt) with { State = entries[index].State };
        }

        return new JournalSnapshot([.. attempts], [.. receipts], [.. settlements]);
    }

    public static bool NothingLeftToTheGame(EntryReceipt receipt, EntrySettlement settlement) => false;

    public static CyclePosition ResultsPhase(CycleKey cycle) => new(
        cycle,
        Core.Housing.Model.LotteryPhase.Results,
        CycleStart + LotteryCalendar.EntryPeriod,
        CycleStart + LotteryCalendar.CycleLength,
        1,
        CycleEvidence.ServerObserved);
}
