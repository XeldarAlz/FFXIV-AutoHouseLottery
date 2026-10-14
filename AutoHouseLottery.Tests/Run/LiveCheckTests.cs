using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Run;

public sealed class LiveCheckTests
{
    private const long Gil = 10_000_000;
    private const long Reserve = 1_000_000;
    private static readonly PlacardSaleInfo openLottery = new(PlacardPurchaseType.Lottery, PlacardTenantType.Private, PlacardAvailability.Available, CycleStart + LotteryCalendar.EntryPeriod, 7, 2, 2, 1);
    private static readonly CharacterBudget budget = new(Deposit, Reserve, 0);

    [Fact]
    public void OpenLotteryForTheBuyerPasses()
    {
        Assert.Equal(EntryFailure.None, LivePlotRules.Check(Facts()));
    }

    [Theory]
    [InlineData((byte)PlacardIdentityVerdict.WrongPlot, (byte)EntryFailure.WrongPlacard)]
    [InlineData((byte)PlacardIdentityVerdict.Unverified, (byte)EntryFailure.IdentityUnverified)]
    public void PlacardThatIsNotProvenTheTargetIsNeverRead(byte identity, byte expected)
    {
        Assert.Equal((EntryFailure)expected, LivePlotRules.Check(Facts() with { Identity = (PlacardIdentityVerdict)identity }));
    }

    [Fact]
    public void LiveStateDecidesEachConclusiveFailure()
    {
        Assert.Equal(EntryFailure.Sold, LivePlotRules.Check(Facts() with { HousingType = PlacardHousingType.OwnedHouse, Sale = null }));
        Assert.Equal(EntryFailure.Sold, LivePlotRules.Check(Facts() with { Sale = openLottery with { Availability = PlacardAvailability.Unavailable } }));
        Assert.Equal(EntryFailure.WrongPhase, LivePlotRules.Check(Facts() with { Sale = openLottery with { Availability = PlacardAvailability.InResultsPeriod } }));
        Assert.Equal(EntryFailure.NotLottery, LivePlotRules.Check(Facts() with { Sale = openLottery with { PurchaseType = PlacardPurchaseType.FirstComeFirstServed } }));
        Assert.Equal(EntryFailure.Ineligible, LivePlotRules.Check(Facts() with { IntendedBuyer = BuyerType.FreeCompany }));
    }

    [Fact]
    public void UnknownSaleValuesAreUnreadableNeverOpen()
    {
        Assert.Equal(EntryFailure.SaleUnreadable, LivePlotRules.Check(Facts() with { Sale = openLottery with { Availability = null } }));
        Assert.Equal(EntryFailure.SaleUnreadable, LivePlotRules.Check(Facts() with { Sale = openLottery with { TenantType = null } }));
        Assert.Equal(EntryFailure.SaleUnreadable, LivePlotRules.Check(Facts() with { Sale = null, HousingType = null }));
        Assert.Equal(EntryFailure.SaleUnreadable, LivePlotRules.Check(Facts() with { LiveCycle = null }));
    }

    [Fact]
    public void NewCycleAtThePlacardStopsTheRun()
    {
        Assert.Equal(EntryFailure.CycleChanged, LivePlotRules.Check(Facts() with { LiveCycle = SecondCycle }));
    }

    [Fact]
    public void DisabledEnterButtonIsNotAnEntry()
    {
        Assert.Equal(EntryFailure.EntryButtonUnavailable, LivePlotRules.Check(Facts() with { EnterButtonUsable = false }));
    }

    [Fact]
    public void ExactPlotIsEnteredOnlyForItsOwnAmount()
    {
        Assert.Equal(EntryFailure.None, ConfirmationRules.Check(Confirmation(Deposit, RunMode.Exact, Deposit)));
        Assert.Equal(EntryFailure.ChangedPrice, ConfirmationRules.Check(Confirmation(Deposit - 1, RunMode.Exact, Deposit)));
        Assert.Equal(EntryFailure.ChangedPrice, ConfirmationRules.Check(Confirmation(Deposit + 1, RunMode.Exact, Deposit)));
        Assert.Equal(EntryFailure.ChangedPrice, ConfirmationRules.Check(Confirmation(Deposit, RunMode.Exact, null)));
    }

    [Fact]
    public void BestMatchMayLearnAnUnlistedAmountButNeverExceedItsCap()
    {
        Assert.Equal(EntryFailure.None, ConfirmationRules.Check(Confirmation(Deposit, RunMode.BestMatch, null)));
        Assert.Equal(EntryFailure.OverBudget, ConfirmationRules.Check(Confirmation(Deposit + 1, RunMode.BestMatch, null)));
        Assert.Equal(EntryFailure.ChangedPrice, ConfirmationRules.Check(Confirmation(Deposit - 1, RunMode.BestMatch, Deposit)));
    }

    [Fact]
    public void UnlistedAmountBelowAnyPlotPriceIsUnreadable()
    {
        Assert.Equal(EntryFailure.DepositUnreadable, ConfirmationRules.Check(Confirmation(3, RunMode.BestMatch, null)));
        Assert.Equal(EntryFailure.DepositUnreadable, ConfirmationRules.Check(Confirmation(HousingDistricts.MinimumPlotPrice - 1, RunMode.BestMatch, null)));
        Assert.Equal(EntryFailure.None, ConfirmationRules.Check(Confirmation(HousingDistricts.MinimumPlotPrice, RunMode.BestMatch, null)));
    }

    [Fact]
    public void LiveGilMustCoverTheDepositAndTheReserve()
    {
        Assert.Equal(EntryFailure.None, ConfirmationRules.Check(Confirmation(Deposit, RunMode.Exact, Deposit) with { LiveGil = Deposit + Reserve }));
        Assert.Equal(EntryFailure.OverBudget, ConfirmationRules.Check(Confirmation(Deposit, RunMode.Exact, Deposit) with { LiveGil = Deposit + Reserve - 1 }));
        Assert.Equal(EntryFailure.GilUnknown, ConfirmationRules.Check(Confirmation(Deposit, RunMode.Exact, Deposit) with { LiveGil = null }));
    }

    [Fact]
    public void UnreadDialogOrAmountIsNeverConfirmed()
    {
        Assert.Equal(EntryFailure.ConfirmationNotShown, ConfirmationRules.Check(Confirmation(Deposit, RunMode.Exact, Deposit) with { Identified = false }));
        Assert.Equal(EntryFailure.DepositUnreadable, ConfirmationRules.Check(Confirmation(null, RunMode.Exact, Deposit)));
    }

    private static LivePlotFacts Facts() => new(
        PlacardIdentityVerdict.Confirmed,
        PlacardHousingType.UnownedHouse,
        openLottery,
        BuyerType.Private,
        true,
        FirstCycle,
        FirstCycle);

    private static ConfirmationFacts Confirmation(long? shown, RunMode mode, long? expected)
        => new(true, shown, mode, expected, budget, default, Gil);
}
