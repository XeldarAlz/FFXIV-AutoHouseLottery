using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using static AutoHouseLottery.Tests.FindFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class RunPreviewBuilderTests
{
    private const long Ceiling = 4_000_000;

    [Fact]
    public void FortyTwoShortlistedPlotsPlanOneEntryForOneCharacter()
    {
        var (plots, shortlist) = Shortlisted(42);

        var preview = Preview(CharacterFixtures.Character(1), BuyerType.Private, Ceiling, 0, shortlist, VerifiedEntryPhase(), plots, ObservedAt);

        Assert.Equal(42, preview.PlotCount);
        Assert.Equal(1, preview.MaxNewEntries);
        Assert.Equal(RunPolicy.MaximumInspections, preview.Assignments[0].TargetOrder.Length);
    }

    [Fact]
    public void ACharacterKnownOnlyByNameAndWorldStaysUnknownRatherThanEligible()
    {
        var (plots, shortlist) = Shortlisted(3);
        var bare = new CharacterProfile { ContentId = 7, Name = "Bare", Region = GameRegion.Global, HomeWorldId = HomeWorld };

        var preview = Preview(bare, BuyerType.Private, Ceiling, 0, shortlist, VerifiedEntryPhase(), plots, ObservedAt);

        Assert.Equal(EligibilityVerdict.Unknown, preview.Assignments[0].Eligibility.Verdict);
    }

    [Fact]
    public void NoBuyerTypeOrNoCeilingPlansNothing()
    {
        var (plots, shortlist) = Shortlisted(3);
        var character = CharacterFixtures.Character(1);

        var noBuyer = Preview(character, BuyerType.Unknown, Ceiling, 0, shortlist, VerifiedEntryPhase(), plots, ObservedAt);
        var noCeiling = Preview(character, BuyerType.Private, 0, 0, shortlist, VerifiedEntryPhase(), plots, ObservedAt);

        Assert.Equal(RunPolicyError.MissingBuyerType, noBuyer.PolicyError);
        Assert.Equal(RunPolicyError.NonPositiveDepositCap, noCeiling.PolicyError);
        Assert.Equal(0, noCeiling.MaxNewEntries);
    }

    [Fact]
    public void ExactPolicyIsOnePlotCappedAtItsShownDeposit()
    {
        var (plots, _) = Shortlisted(1);
        var plot = Plot(1, 1);

        var policy = RunPreviewBuilder.ExactPolicy(CharacterFixtures.Character(1), BuyerType.Private, plot, Deposit, 0, VerifiedEntryPhase(), 3);
        var preview = AssignmentPlanner.Build(policy, [CharacterFixtures.Character(1)], plots, ObservedAt);

        Assert.Equal(RunPolicyError.None, RunPolicyRules.Validate(policy));
        Assert.Null(policy.Shortlist);
        Assert.Equal(Deposit, policy.Characters[0].DepositCap);
        var assignment = Assert.Single(preview.Assignments);
        Assert.Equal(plot, Assert.Single(assignment.TargetOrder));
        Assert.Equal(Deposit, assignment.MaximumDeposit);
    }

    [Fact]
    public void BestMatchPolicyKeepsTheShortlistAsItWasWhenItWasPlanned()
    {
        var plots = new Dictionary<PlotKey, PlotSnapshot>();
        var live = new Shortlist(Guid.NewGuid(), "Shortlist");
        live.Add(Plot(1, 1));
        live.Add(Plot(1, 2));
        var policy = RunPreviewBuilder.BestMatchPolicy(CharacterFixtures.Character(1), BuyerType.Private, Ceiling, 0, live.Freeze(), VerifiedEntryPhase());

        live.Remove(Plot(1, 1));
        live.Add(Plot(2, 7));

        var frozen = policy.Shortlist!.Value;
        Assert.Equal(2, frozen.Plots.Length);
        Assert.Contains(Plot(1, 1), frozen.Plots);
        Assert.DoesNotContain(Plot(2, 7), frozen.Plots);
        Assert.NotEqual(live.Revision, frozen.Revision);
        Assert.Equal(frozen.Revision, policy.Revision);
    }

    [Fact]
    public void BuyerIsThePlayersChoiceThatThePlotOnlyConfirms()
    {
        Assert.Equal(BuyerType.Private, RunPreviewBuilder.BuyerFor(PlotBuyers.Private, BuyerType.Private));
        Assert.Equal(BuyerType.FreeCompany, RunPreviewBuilder.BuyerFor(PlotBuyers.Both, BuyerType.FreeCompany));
        Assert.Equal(BuyerType.FreeCompany, RunPreviewBuilder.BuyerFor(null, BuyerType.FreeCompany));
    }

    [Fact]
    public void NoChoiceOrAChoiceThePlotRefusesGivesNoBuyer()
    {
        Assert.Equal(BuyerType.Unknown, RunPreviewBuilder.BuyerFor(PlotBuyers.Private, BuyerType.FreeCompany));
        Assert.Equal(BuyerType.Unknown, RunPreviewBuilder.BuyerFor(PlotBuyers.FreeCompany, BuyerType.Private));
        Assert.Equal(BuyerType.Unknown, RunPreviewBuilder.BuyerFor(PlotBuyers.Private, BuyerType.Unknown));
        Assert.Equal(BuyerType.Unknown, RunPreviewBuilder.BuyerFor(null, BuyerType.Unknown));
    }

    private static RunPreview Preview(CharacterProfile character, BuyerType buyer, long cap, long reserve, ShortlistSnapshot shortlist, CyclePosition position, Dictionary<PlotKey, PlotSnapshot> plots, DateTime nowUtc)
        => AssignmentPlanner.Build(RunPreviewBuilder.BestMatchPolicy(character, buyer, cap, reserve, shortlist, position), [character], plots, nowUtc);

    private static (Dictionary<PlotKey, PlotSnapshot> Plots, ShortlistSnapshot Shortlist) Shortlisted(int count)
    {
        var plots = new Dictionary<PlotKey, PlotSnapshot>();
        var shortlist = new Shortlist(Guid.NewGuid(), "Shortlist");
        for (var index = 0; index < count; index++)
        {
            var key = Plot(index / HousingDistricts.PlotsPerDivision + 1, index % HousingDistricts.PlotsPerDivision + 1);
            plots[key] = Listing(key, entries: index);
            shortlist.Add(key);
        }

        return (plots, shortlist.Freeze());
    }
}
