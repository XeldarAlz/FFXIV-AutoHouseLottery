using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.CharacterFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class EligibilityTests
{
    private static readonly CharacterBudget budget = new(Deposit, 100_000, 10_000);

    [Fact]
    public void UnknownFactsNeverBecomeEligible()
    {
        var unknown = new CharacterProfile { ContentId = 1, Name = "Unknown", Region = GameRegion.Global, HomeWorldId = HomeWorld };

        var result = EligibilityRules.EvaluateCharacter(unknown, BuyerType.Private, FirstCycle, collectRefundFirst: true);

        Assert.Equal(EligibilityVerdict.Unknown, result.Verdict);
        Assert.Equal(EligibilityReason.None, result.Blockers);
        Assert.Equal(
            EligibilityReason.AlreadyEnteredThisCycle
            | EligibilityReason.OutstandingRefundOrProperty
            | EligibilityReason.BuyerRequirementsNotMet
            | EligibilityReason.AccountOwnsLandOnWorld,
            result.Unresolved);
    }

    [Fact]
    public void AnUnevaluatedResultIsUnknownEvenWhenCombined()
    {
        var eligible = new EligibilityResult(EligibilityReason.None, EligibilityReason.None, ObservedAt);

        Assert.Equal(EligibilityVerdict.Unknown, default(EligibilityResult).Verdict);
        Assert.Equal(EligibilityVerdict.Eligible, eligible.Verdict);
        Assert.Equal(EligibilityVerdict.Unknown, eligible.Combine(default).Verdict);
    }

    [Fact]
    public void KnownGoodCharacterAndPlotAreEligible()
    {
        var character = Character(1);
        var result = EligibilityRules.EvaluateCharacter(character, BuyerType.Private, FirstCycle, collectRefundFirst: true)
            .Combine(EligibilityRules.EvaluatePlot(character, BuyerType.Private, Candidate(Plot(4, 4)), budget, default));

        Assert.Equal(EligibilityVerdict.Eligible, result.Verdict);
    }

    [Fact]
    public void AnExistingEntryBlocksEveryBuyerType()
    {
        var entered = Character(1) with { Participation = new CycleParticipation(FirstCycle, EntrySlotState.Entered) };
        var unresolved = Character(2) with { Participation = new CycleParticipation(FirstCycle, EntrySlotState.Unresolved) };

        Assert.Equal(EligibilityReason.AlreadyEnteredThisCycle, EligibilityRules.EvaluateCharacter(entered, BuyerType.FreeCompany, FirstCycle, true).Blockers);
        Assert.Equal(EligibilityReason.AlreadyEnteredThisCycle, EligibilityRules.EvaluateCharacter(unresolved, BuyerType.Private, FirstCycle, true).Blockers);
    }

    [Fact]
    public void ParticipationFromAnotherCycleIsUnknown()
    {
        var stale = Character(1) with { Participation = new CycleParticipation(new CycleKey(GameRegion.Global, 0), EntrySlotState.Open) };

        var result = EligibilityRules.EvaluateCharacter(stale, BuyerType.Private, FirstCycle, true);

        Assert.Equal(EligibilityVerdict.Unknown, result.Verdict);
        Assert.Equal(EligibilityReason.AlreadyEnteredThisCycle, result.Unresolved);
    }

    [Fact]
    public void LandOwnedOnTheAccountBlocksPrivateEntriesOnly()
    {
        var owner = Character(1) with { AccountOwnsLandOnHomeWorld = true };

        Assert.Equal(EligibilityReason.AccountOwnsLandOnWorld, EligibilityRules.EvaluateCharacter(owner, BuyerType.Private, FirstCycle, true).Blockers);
        Assert.Equal(EligibilityVerdict.Eligible, EligibilityRules.EvaluateCharacter(owner, BuyerType.FreeCompany, FirstCycle, true).Verdict);
    }

    [Fact]
    public void OutstandingRefundBlocksUnlessItIsCollectedFirst()
    {
        var refundDue = Character(1) with { Outstanding = OutstandingSettlement.RefundDue };
        var unclaimedWin = Character(2) with { Outstanding = OutstandingSettlement.UnclaimedWin };

        Assert.Equal(EligibilityReason.OutstandingRefundOrProperty, EligibilityRules.EvaluateCharacter(refundDue, BuyerType.Private, FirstCycle, false).Blockers);
        Assert.Equal(EligibilityVerdict.Eligible, EligibilityRules.EvaluateCharacter(refundDue, BuyerType.Private, FirstCycle, true).Verdict);
        Assert.Equal(EligibilityReason.OutstandingRefundOrProperty, EligibilityRules.EvaluateCharacter(unclaimedWin, BuyerType.Private, FirstCycle, true).Blockers);
    }

    [Fact]
    public void FreeCompanyEntryNeedsAKnownFreeCompany()
    {
        var withoutCompany = Character(1) with { FreeCompanyId = CharacterProfile.NoFreeCompany };
        var unknownCompany = Character(2) with { FreeCompanyId = null };

        Assert.Equal(EligibilityReason.BuyerRequirementsNotMet, EligibilityRules.EvaluateCharacter(withoutCompany, BuyerType.FreeCompany, FirstCycle, true).Blockers);
        Assert.Equal(EligibilityReason.BuyerRequirementsNotMet, EligibilityRules.EvaluateCharacter(unknownCompany, BuyerType.FreeCompany, FirstCycle, true).Unresolved);
    }

    [Fact]
    public void PlotOnAnotherWorldOrForAnotherBuyerIsBlocked()
    {
        var character = Character(1);

        var otherWorld = EligibilityRules.EvaluatePlot(character, BuyerType.Private, Candidate(Plot(4, 4, world: OtherWorld)), budget, default);
        var companyOnly = EligibilityRules.EvaluatePlot(character, BuyerType.Private, Candidate(Plot(4, 4)) with { Buyers = PlotBuyers.FreeCompany }, budget, default);
        var unknownBuyers = EligibilityRules.EvaluatePlot(character, BuyerType.Private, Candidate(Plot(4, 4)) with { Buyers = PlotBuyers.Unknown }, budget, default);

        Assert.Equal(EligibilityReason.WrongHomeWorld, otherWorld.Blockers);
        Assert.Equal(EligibilityReason.BuyerTypeMismatch, companyOnly.Blockers);
        Assert.Equal(EligibilityReason.BuyerTypeMismatch, unknownBuyers.Unresolved);
        Assert.Equal(EligibilityVerdict.Unknown, unknownBuyers.Verdict);
    }

    [Fact]
    public void SoldPlotIsBlockedAndUnreportedPlotIsUnresolved()
    {
        var character = Character(1);

        var sold = EligibilityRules.EvaluatePlot(character, BuyerType.Private, Candidate(Plot(4, 4)) with { Availability = PlotAvailability.Occupied }, budget, default);
        var unreported = EligibilityRules.EvaluatePlot(character, BuyerType.Private, Candidate(Plot(4, 4)) with { Availability = PlotAvailability.NotReported }, budget, default);

        Assert.Equal(EligibilityReason.PlotUnavailable, sold.Blockers);
        Assert.Equal(EligibilityReason.PlotUnavailable, unreported.Unresolved);
    }

    [Fact]
    public void MissingBalanceOrPriceStaysUnresolved()
    {
        var noBalance = EligibilityRules.EvaluatePlot(Character(1, gil: null), BuyerType.Private, Candidate(Plot(4, 4)), budget, default);
        var noPrice = EligibilityRules.EvaluatePlot(Character(1), BuyerType.Private, Candidate(Plot(4, 4)) with { Deposit = null }, budget, default);

        Assert.Equal(EligibilityReason.InsufficientGil, noBalance.Unresolved);
        Assert.Equal(EligibilityReason.DepositOverCap, noPrice.Unresolved);
        Assert.Equal(EligibilityVerdict.Unknown, noPrice.Verdict);
    }

    private static EntryCandidate Candidate(PlotKey plot) => new(plot, PlotBuyers.Both, PlotAvailability.LotteryEntry, Deposit);
}
