using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class PlacardRuleTests
{
    private const uint Mist = 339;
    private const uint Goblet = 341;
    private const string MistName = "Mist";

    private static readonly PlotAddress plot = new(Mist, WardIndex: 11, PlotIndex: 4);
    private static readonly PlacardSaleInfo lotterySale = new(PlacardPurchaseType.Lottery, PlacardTenantType.Private, PlacardAvailability.Available, null, 3, 2, 2, 1);
    private static readonly PlacardSaleInfo staleSale = lotterySale with { EntryCount = 99 };

    [Fact]
    public void ThisWindowsCaptureOfAnUnownedHouseSuppliesTheSale()
    {
        var source = PlacardSaleRule.Choose(Capture(PlacardHousingType.UnownedHouse, lotterySale), agentForSale: true, staleSale, out var sale);
        Assert.Equal(PlacardSaleSource.SaleInfoHook, source);
        Assert.Equal(lotterySale, sale);
    }

    [Fact]
    public void ACaptureOfAnOwnedHouseKeepsTheAgentsStaleBlockOut()
    {
        var source = PlacardSaleRule.Choose(Capture(PlacardHousingType.OwnedHouse, null), agentForSale: true, staleSale, out var sale);
        Assert.Equal(PlacardSaleSource.None, source);
        Assert.Null(sale);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void WithoutACaptureTheAgentsBlockCountsOnlyWhileItSaysForSale(bool agentForSale, bool expectSale)
    {
        var source = PlacardSaleRule.Choose(null, agentForSale, staleSale, out var sale);
        Assert.Equal(expectSale, sale is not null);
        Assert.Equal(expectSale ? PlacardSaleSource.AgentSaleBlock : PlacardSaleSource.None, source);
    }

    [Fact]
    public void AnUnknownForSaleFlagUsesNoBlock()
    {
        Assert.Equal(PlacardSaleSource.None, PlacardSaleRule.Choose(null, agentForSale: null, staleSale, out _));
    }

    [Theory]
    [InlineData("Plot 5, 12th Ward, Mist", true)]
    [InlineData("Mist 12区 5番地", true)]
    [InlineData("Plot 5, 13th Ward, Mist", false)]
    [InlineData("Plot 6, 12th Ward, Mist", false)]
    [InlineData("Plot 5, 12th Ward, The Goblet", false)]
    public void TheAddressTextNamesThePlotByDistrictWardAndPlotNumber(string text, bool expected)
    {
        Assert.Equal(expected, PlacardAddressRule.TextNames(text, plot, MistName));
    }

    [Fact]
    public void SourcesAgreeOnlyWhenEveryComparisonAgrees()
    {
        Assert.Equal(AddressAgreement.Agree, PlacardAddressRule.Evaluate(plot, plot, "Plot 5, 12th Ward, Mist", MistOnly));
        Assert.Equal(AddressAgreement.Agree, PlacardAddressRule.Evaluate(null, plot, "Plot 5, 12th Ward, Mist", MistOnly));
        Assert.Equal(AddressAgreement.Disagree, PlacardAddressRule.Evaluate(plot, plot with { PlotIndex = 5 }, null, MistOnly));
        Assert.Equal(AddressAgreement.Disagree, PlacardAddressRule.Evaluate(plot, null, "Plot 9, 12th Ward, Mist", MistOnly));
    }

    [Fact]
    public void ASingleSourceOrAnUnknownDistrictLeavesTheAddressUnestablished()
    {
        Assert.Equal(AddressAgreement.Incomplete, PlacardAddressRule.Evaluate(plot, null, null, MistOnly));
        Assert.Equal(AddressAgreement.Incomplete, PlacardAddressRule.Evaluate(null, null, "Plot 5, 12th Ward, Mist", MistOnly));
        Assert.Equal(AddressAgreement.Incomplete, PlacardAddressRule.Evaluate(plot with { TerritoryTypeId = Goblet }, null, "Plot 5, 12th Ward, The Goblet", MistOnly));
    }

    [Fact]
    public void ThePlotIsConfirmedOnlyByAnAgreedAddress()
    {
        var destination = new HousingDestination(73, Mist, 11, 4);
        Assert.Equal(PlacardIdentityVerdict.Confirmed, PlacardIdentity.Verify(destination, plot));
        Assert.Equal(PlacardIdentityVerdict.WrongPlot, PlacardIdentity.Verify(destination, plot with { PlotIndex = 5 }));
        Assert.Equal(PlacardIdentityVerdict.Unverified, PlacardIdentity.Verify(destination, null));
        var unagreed = new PlacardReading(plot, plot with { WardIndex = 2 }, null, AddressAgreement.Disagree, null, null, null, PlacardSaleSource.None, null, null, null, null, [], 0, DateTime.UtcNow);
        Assert.Null(unagreed.Address);
    }

    private static string? MistOnly(uint territoryTypeId) => territoryTypeId == Mist ? MistName : null;

    private static PlacardSaleCapture Capture(PlacardHousingType housingType, PlacardSaleInfo? sale)
        => new(housingType, (byte)housingType, plot, 0, sale, Sequence: 10);
}
