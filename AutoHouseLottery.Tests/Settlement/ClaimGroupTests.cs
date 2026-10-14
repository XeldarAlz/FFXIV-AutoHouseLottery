using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Settlement;

// VALIDATION_PLAN section 4, "Ownership group has multiple wins": exactly one selected claim; the other outcomes stay.
public sealed class ClaimGroupTests
{
    private const ulong Second = ContentId + 1;
    private const ulong FreeCompanyId = 900;

    [Fact]
    public void TwoUngroupedPrivateWinsOnOneWorldNeedAChoice()
    {
        var first = Receipt(Plot(12, 5), FirstCycle);
        var second = Receipt(Plot(12, 6), FirstCycle) with { CharacterContentId = Second };
        var journal = Journal([], (first, SettlementState.WonUnclaimed), (second, SettlementState.WonUnclaimed));

        var group = ClaimGroups.Of(journal, first, _ => default);

        Assert.Equal(new ClaimGroupState(2, false), group);
        Assert.True(group.NeedsChoice);
    }

    [Fact]
    public void WinsOnSeparateServiceAccountsAreSeparateGroups()
    {
        var first = Receipt(Plot(12, 5), FirstCycle);
        var second = Receipt(Plot(12, 6), FirstCycle) with { CharacterContentId = Second };
        var journal = Journal([], (first, SettlementState.WonUnclaimed), (second, SettlementState.WonUnclaimed));

        var group = ClaimGroups.Of(journal, first, contentId => new OwnerFacts(contentId == ContentId ? 1 : 2, null));

        Assert.Equal(new ClaimGroupState(1, false), group);
        Assert.False(group.NeedsChoice);
    }

    [Fact]
    public void AClaimedWinLocksTheRestOfItsGroup()
    {
        var first = Receipt(Plot(12, 5), FirstCycle);
        var second = Receipt(Plot(12, 6), FirstCycle) with { CharacterContentId = Second };
        var claimed = Journal([], (first, SettlementState.Claimed), (second, SettlementState.WonUnclaimed));
        var claiming = Journal([], (first, SettlementState.ClaimPending), (second, SettlementState.WonUnclaimed));

        Assert.Equal(new ClaimGroupState(1, true), ClaimGroups.Of(claimed, second, _ => default));
        Assert.True(ClaimGroups.Of(claiming, second, _ => default).Claimed);
        Assert.False(ClaimGroups.Of(claimed, second, _ => default).NeedsChoice);
    }

    [Fact]
    public void FreeCompanyWinsShareTheCompanysGroupAndNotThePrivateOnes()
    {
        var company = Receipt(Plot(12, 5), FirstCycle) with { Buyer = BuyerType.FreeCompany };
        var member = Receipt(Plot(12, 6), FirstCycle) with { CharacterContentId = Second, Buyer = BuyerType.FreeCompany };
        var privateWin = Receipt(Plot(12, 7), FirstCycle) with { CharacterContentId = Second + 1 };
        var journal = Journal([], (company, SettlementState.WonUnclaimed), (member, SettlementState.Claimed), (privateWin, SettlementState.WonUnclaimed));

        Assert.True(ClaimGroups.Of(journal, company, _ => new OwnerFacts(null, FreeCompanyId)).Claimed);
        Assert.Equal(new ClaimGroupState(1, false), ClaimGroups.Of(journal, privateWin, _ => new OwnerFacts(1, FreeCompanyId)));
    }

    [Fact]
    public void AnotherCyclesWinIsNotACompetitor()
    {
        var first = Receipt(Plot(12, 5), FirstCycle);
        var later = Receipt(Plot(12, 6), SecondCycle) with { CharacterContentId = Second };
        var journal = Journal([], (first, SettlementState.Claimed), (later, SettlementState.WonUnclaimed));

        Assert.Equal(new ClaimGroupState(1, false), ClaimGroups.Of(journal, later, _ => default));
    }

    [Fact]
    public void ASecondClaimInAClaimedGroupIsRefused()
    {
        var receipt = Receipt(Plot(12, 6), FirstCycle);
        var facts = new ClaimFacts(ContentId, receipt, SettlementState.WonUnclaimed, false, null, null, false) { GroupAlreadyClaimed = true };

        Assert.Equal(ClaimRefusal.GroupAlreadyClaimed, ClaimRules.Check(facts));
        Assert.Equal(SettlementFailure.GroupAlreadyClaimed, SettlementSteps.FromClaim(ClaimRefusal.GroupAlreadyClaimed));
        Assert.Equal(ClaimRefusal.None, ClaimRules.Check(facts with { GroupAlreadyClaimed = false }));
    }
}
