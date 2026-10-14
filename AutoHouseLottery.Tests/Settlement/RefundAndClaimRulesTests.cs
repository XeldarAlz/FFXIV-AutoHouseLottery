using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class RefundAndClaimRulesTests
{
    private const long GilBefore = 1_000_000;
    private const ushort HomeWorldShort = (ushort)HomeWorld;

    [Fact]
    public void ALossRefundsTheWholeDepositAndAForfeitHalfOfIt()
    {
        Assert.Equal(RefundOffer.Matches, RefundRules.CheckOffer(RefundKind.Loss, Deposit, Deposit));
        Assert.Equal(RefundOffer.Matches, RefundRules.CheckOffer(RefundKind.Forfeit, Deposit / 2, Deposit));
        Assert.Equal(RefundOffer.Matches, RefundRules.CheckOffer(RefundKind.Forfeit, 1_500_001, 3_000_001));
        Assert.Equal(RefundOffer.Matches, RefundRules.CheckOffer(RefundKind.Forfeit, 1_500_000, 3_000_001));
    }

    [Fact]
    public void APartialRefundIsNeverTakenForAFullLossRefund()
    {
        Assert.Equal(RefundOffer.DiffersFromDeposit, RefundRules.CheckOffer(RefundKind.Loss, Deposit / 2, Deposit));
        Assert.Equal(RefundOffer.DiffersFromDeposit, RefundRules.CheckOffer(RefundKind.Forfeit, Deposit, Deposit));
        Assert.Equal(RefundOffer.DiffersFromDeposit, RefundRules.CheckOffer(RefundKind.None, Deposit, Deposit));
        Assert.Equal(RefundOffer.DepositUnknown, RefundRules.CheckOffer(RefundKind.Loss, Deposit, null));
    }

    [Fact]
    public void ARefundSettlesOnTheExactGilWithTheDialogGoneAndThePlacardNoLongerOffering()
    {
        var settled = RefundRules.Settle(new RefundEvidence(Deposit, GilBefore, GilBefore + Deposit, DialogGone: true, PlacardStillOffers: false));

        Assert.Equal(new RefundSettlement(RefundVerdict.Refunded, Deposit), settled);
    }

    [Theory]
    [InlineData(Deposit / 2)]
    [InlineData(Deposit - 1)]
    [InlineData(Deposit + 1)]
    [InlineData(Deposit * 2)]
    public void AGilRiseOfAnyOtherAmountNeverConfirmsARefund(long rise)
    {
        var first = new RefundEvidence(Deposit, GilBefore, GilBefore + rise, DialogGone: true, PlacardStillOffers: false);

        Assert.Equal(new RefundSettlement(RefundVerdict.Uncertain, null), RefundRules.Settle(first));
        Assert.True(RefundRules.NeedsSecondLook(first));
        Assert.Equal(RefundVerdict.Uncertain, RefundRules.Settle(first with { Looks = 2 }).Verdict);
        Assert.False(RefundRules.NeedsSecondLook(first with { Looks = 2 }));
    }

    [Fact]
    public void ASecondLookThatFindsTheExactAmountConfirmsTheRefund()
    {
        var first = new RefundEvidence(Deposit, GilBefore, GilBefore + Deposit / 2, DialogGone: true, PlacardStillOffers: false);
        var second = first with { GilAfter = GilBefore + Deposit, Looks = 2 };

        Assert.True(RefundRules.NeedsSecondLook(first));
        Assert.Equal(new RefundSettlement(RefundVerdict.Refunded, Deposit), RefundRules.Settle(second));
    }

    [Fact]
    public void AnOwnedPlotsPlacardIsReadableWithoutSaleData()
    {
        Assert.True(RefundRules.PlacardReadable(saleRead: true, housingTypeKnown: false, allLabelsKnown: false));
        Assert.True(RefundRules.PlacardReadable(saleRead: false, housingTypeKnown: true, allLabelsKnown: true));
        Assert.False(RefundRules.PlacardReadable(saleRead: false, housingTypeKnown: false, allLabelsKnown: true));
        Assert.False(RefundRules.PlacardReadable(saleRead: false, housingTypeKnown: true, allLabelsKnown: false));
    }

    [Theory]
    [InlineData(true, true, GilBefore + Deposit)]
    [InlineData(true, null, GilBefore + Deposit)]
    [InlineData(false, false, GilBefore + Deposit)]
    [InlineData(true, false, GilBefore)]
    [InlineData(true, false, GilBefore - 1)]
    public void GilAloneNeverConfirmsARefund(bool dialogGone, bool? placardStillOffers, long gilAfter)
    {
        Assert.Equal(RefundVerdict.Uncertain, RefundRules.Settle(new RefundEvidence(Deposit, GilBefore, gilAfter, dialogGone, placardStillOffers)).Verdict);
        Assert.Equal(RefundVerdict.Uncertain, RefundRules.Settle(new RefundEvidence(Deposit, GilBefore, null, true, false)).Verdict);
    }

    [Fact]
    public void APlacardThatHasNotCaughtUpIsReadOnceMoreBeforeTheRefundIsDecided()
    {
        var first = new RefundEvidence(Deposit, GilBefore, GilBefore + Deposit, DialogGone: true, PlacardStillOffers: true);

        Assert.True(RefundRules.NeedsSecondLook(first));
        Assert.True(RefundRules.NeedsSecondLook(first with { PlacardStillOffers = null }));
        Assert.False(RefundRules.NeedsSecondLook(first with { PlacardStillOffers = false }));
        Assert.False(RefundRules.NeedsSecondLook(first with { GilAfter = GilBefore }));
        Assert.False(RefundRules.NeedsSecondLook(first with { DialogGone = false }));
        var second = first with { PlacardStillOffers = false, Looks = 2 };
        Assert.False(RefundRules.NeedsSecondLook(first with { Looks = 2 }));
        Assert.Equal(RefundVerdict.Refunded, RefundRules.Settle(second).Verdict);
    }

    [Fact]
    public void AClaimAcquiresOrRelocatesFromTheEstateList()
    {
        var house = Estate(14, 3);
        var apartment = Estate(1, 1) with { Apartment = true };

        Assert.Equal(ClaimEffect.Acquire, ClaimRules.EffectOf(BuyerType.Private, false, null, null));
        Assert.Equal(ClaimEffect.Acquire, ClaimRules.EffectOf(BuyerType.Private, false, apartment, null));
        Assert.Equal(ClaimEffect.Relocate, ClaimRules.EffectOf(BuyerType.Private, false, house, null));
        Assert.Equal(ClaimEffect.Unknown, ClaimRules.EffectOf(BuyerType.FreeCompany, false, null, null));
        Assert.Equal(ClaimEffect.Acquire, ClaimRules.EffectOf(BuyerType.FreeCompany, true, house, null));
        Assert.Equal(ClaimEffect.Relocate, ClaimRules.EffectOf(BuyerType.FreeCompany, true, null, house));
        Assert.Equal(ClaimEffect.Unknown, ClaimRules.EffectOf(BuyerType.Unknown, true, null, null));
    }

    [Fact]
    public void OnlyTheWinningEntrantMayClaim()
    {
        var receipt = Receipt(Plot(12, 5), FirstCycle);

        Assert.Equal(ClaimRefusal.None, ClaimRules.Check(Claim(receipt)));
        Assert.Equal(ClaimRefusal.NotTheEntrant, ClaimRules.Check(Claim(receipt) with { CharacterContentId = ContentId + 1 }));
        Assert.Equal(ClaimRefusal.NotWon, ClaimRules.Check(Claim(receipt) with { Recorded = SettlementState.LostRefundDue }));
        Assert.Equal(ClaimRefusal.PlotUnknown, ClaimRules.Check(Claim(receipt with { Plot = null })));
        Assert.Equal(ClaimRefusal.BuyerUnknown, ClaimRules.Check(Claim(receipt with { Buyer = BuyerType.Unknown })));
        Assert.Equal(ClaimRefusal.EffectUnknown, ClaimRules.Check(Claim(receipt with { Buyer = BuyerType.FreeCompany })));
        Assert.Equal(ClaimRefusal.AlreadyOwned, ClaimRules.Check(Claim(receipt) with { PersonalEstate = Estate(12, 5) }));
        Assert.Equal(ClaimRefusal.GroupClaimOpen, ClaimRules.Check(Claim(receipt) with { GroupClaimOpen = true }));
    }

    [Fact]
    public void OwnershipIsTheExactPlotOfTheBuyersOwnEstate()
    {
        var plot = Plot(12, 5);

        Assert.True(ClaimRules.Owns(BuyerType.Private, Estate(12, 5), null, plot));
        Assert.False(ClaimRules.Owns(BuyerType.FreeCompany, Estate(12, 5), null, plot));
        Assert.True(ClaimRules.Owns(BuyerType.FreeCompany, null, Estate(12, 5), plot));
        Assert.False(ClaimRules.Owns(BuyerType.Private, Estate(12, 6), null, plot));
        Assert.False(ClaimRules.Owns(BuyerType.Private, Estate(12, 5) with { WorldId = 79 }, null, plot));
        Assert.False(ClaimRules.Owns(BuyerType.Private, Estate(12, 5) with { Apartment = true }, null, plot));
        Assert.True(ClaimRules.Owns(BuyerType.Unknown, null, Estate(12, 5), plot));
    }

    [Fact]
    public void OneClaimAtATimePerOwnershipGroup()
    {
        var first = Receipt(Plot(12, 5), FirstCycle);
        var second = Receipt(Plot(12, 6), FirstCycle) with { CharacterContentId = ContentId + 1 };
        var openClaim = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Claim, ReceiptId = first.ReceiptId };
        var settledClaim = openClaim with { State = AttemptState.Confirmed };
        var openClaimFreeCompany = openClaim with { Buyer = BuyerType.FreeCompany };

        Assert.True(ClaimRules.GroupClaimOpen(Journal([openClaim], (first, SettlementState.NeedsReconciliation), (second, SettlementState.WonUnclaimed)), second));
        Assert.False(ClaimRules.GroupClaimOpen(Journal([openClaim], (first, SettlementState.NeedsReconciliation), (second, SettlementState.WonUnclaimed)), first));
        Assert.False(ClaimRules.GroupClaimOpen(Journal([settledClaim], (first, SettlementState.Claimed), (second, SettlementState.WonUnclaimed)), second));
        Assert.False(ClaimRules.GroupClaimOpen(Journal([openClaimFreeCompany], (first, SettlementState.NeedsReconciliation), (second, SettlementState.WonUnclaimed)), second));
    }

    private static ClaimFacts Claim(EntryReceipt receipt) => new(ContentId, receipt, SettlementState.WonUnclaimed, false, null, null, false);

    private static EstateAddress Estate(int ward, int plot) => new(HousingDistricts.Mist, HomeWorldShort, ward - 1, plot - 1, false, 1);
}
