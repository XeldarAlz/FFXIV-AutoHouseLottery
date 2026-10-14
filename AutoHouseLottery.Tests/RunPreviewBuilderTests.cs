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

        var preview = RunPreviewBuilder.Build(CharacterFixtures.Character(1), BuyerType.Private, Ceiling, 0, shortlist, VerifiedEntryPhase(), plots);

        Assert.Equal(42, preview.PlotCount);
        Assert.Equal(1, preview.MaxNewEntries);
        Assert.Equal(RunPolicy.MaximumInspections, preview.Assignments[0].TargetOrder.Length);
    }

    [Fact]
    public void ACharacterKnownOnlyByNameAndWorldStaysUnknownRatherThanEligible()
    {
        var (plots, shortlist) = Shortlisted(3);
        var bare = new CharacterProfile { ContentId = 7, Name = "Bare", Region = GameRegion.Global, HomeWorldId = HomeWorld };

        var preview = RunPreviewBuilder.Build(bare, BuyerType.Private, Ceiling, 0, shortlist, VerifiedEntryPhase(), plots);

        Assert.Equal(EligibilityVerdict.Unknown, preview.Assignments[0].Eligibility.Verdict);
    }

    [Fact]
    public void NoBuyerTypeOrNoCeilingPlansNothing()
    {
        var (plots, shortlist) = Shortlisted(3);
        var character = CharacterFixtures.Character(1);

        var noBuyer = RunPreviewBuilder.Build(character, BuyerType.Unknown, Ceiling, 0, shortlist, VerifiedEntryPhase(), plots);
        var noCeiling = RunPreviewBuilder.Build(character, BuyerType.Private, 0, 0, shortlist, VerifiedEntryPhase(), plots);

        Assert.Equal(RunPolicyError.MissingBuyerType, noBuyer.PolicyError);
        Assert.Equal(RunPolicyError.NonPositiveDepositCap, noCeiling.PolicyError);
        Assert.Equal(0, noCeiling.MaxNewEntries);
    }

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
